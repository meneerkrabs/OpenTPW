using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>The message tube is saved with the park (tp.exe SaveTags 0x48EE50) and holds ten rows without eviction (AddATag 0x48EB20, cap 0x750610).</summary>
[TestClass]
[DoNotParallelize]
public class MessageTubeSaveTests
{
	private sealed class Clock
	{
		public long Now;
		public ParkHud Hud { get; } = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		public Clock() => Hud.ChallengeMessageClock = () => Now;
		public void Pump( long now )
		{
			Now = now;
			Hud.QueueChallengeTerminalTimers();
			Hud.DispatchChallengeTerminalTimers();
			Hud.ReleaseChallengeTerminalRows();
		}
	}

	private static int QueuedTimerCount( ParkHud hud )
	{
		var queue = typeof( ParkHud ).GetField( "challengeTimerEvents", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud );
		return queue == null ? 0 : (int)queue.GetType().GetProperty( "Count" )!.GetValue( queue )!;
	}

	private static MessageTubeSaveData RoundTrip( MessageTubeSaveData data ) => ParkResumeJson.Deserialize<MessageTubeSaveData>( ParkResumeJson.Serialize( data ) );

	private static MessageTubeSaveData WorldRoundTrip( MessageTubeSaveData data ) => ParkResumeSaveFile.Deserialize(
		ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() with { MessageTube = data } ) ).MessageTube!;

	private static void PostTerminal( Clock clock )
	{
		Assert.IsTrue( ChallengeMessageRecords.TryGet( 1, ChallengePresentationKind.Success, out var record ) );
		Assert.IsTrue( clock.Hud.PostChallengeTerminal( record, "terminal" ) );
	}

	[TestMethod]
	public void AReadTerminalStillAllowsAWorldSaveHoursLaterAndNeverRestartsItsTimers()
	{
		var old = new Clock();
		PostTerminal( old );
		Assert.IsTrue( old.Hud.OpenPendingMessage( 0 ) );
		old.Hud.CloseMessagePanel();
		old.Now = 7_200_000;
		var saved = WorldRoundTrip( old.Hud.CaptureMessageTube()! );
		Assert.AreEqual( 0L, saved.Rows[0].ElapsedMs, "a stopped timer carries no wall-clock age" );
		Assert.IsFalse( saved.Rows[0].Active );
		Assert.IsFalse( saved.Rows[0].PreExpiryEnabled );
		var loaded = new Clock { Now = 500_000 };
		loaded.Hud.RestoreMessageTube( saved );
		loaded.Pump( 9_000_000 );
		Assert.IsTrue( loaded.Hud.IsPendingMessageVisible( 0 ), "reading stopped expiry permanently, including across a save" );
		Assert.IsNull( loaded.Hud.CaptureMessageTube()!.Problem() );
	}

	[TestMethod]
	public void ASuspendedTerminalSavesTheFullLifetimeThatResumeWouldRestart()
	{
		var old = new Clock();
		PostTerminal( old );
		old.Now = 26_000;
		old.Hud.SetChallengeMessageTimersSuspended( true );
		old.Now = 7_200_000;
		var saved = WorldRoundTrip( old.Hud.CaptureMessageTube()! );
		Assert.AreEqual( 0L, saved.Rows[0].ElapsedMs );
		Assert.IsTrue( saved.Rows[0].Active, "suspension must not turn expiry off" );
		var loaded = new Clock { Now = 500_000 };
		loaded.Hud.RestoreMessageTube( saved );
		loaded.Pump( 529_999 );
		Assert.IsTrue( loaded.Hud.IsPendingMessageVisible( 0 ) );
		loaded.Pump( 530_000 );
		Assert.IsFalse( loaded.Hud.IsPendingMessageVisible( 0 ), "the full restarted lifetime expires, not an immortal row" );
	}

	[TestMethod]
	public void ASuspendedQueuedWarningStaysPendingUntilDispatchWithoutShorteningTheRestartedLifetime()
	{
		var old = new Clock();
		PostTerminal( old );
		old.Now = 25_000; // warning queues at the inclusive boundary
		old.Hud.QueueChallengeTerminalTimers();
		old.Hud.SetChallengeMessageTimersSuspended( true );
		old.Now = 7_200_000;
		var saved = WorldRoundTrip( old.Hud.CaptureMessageTube()! );
		Assert.AreEqual( 0L, saved.Rows[0].ElapsedMs );
		Assert.IsTrue( saved.Rows[0].PendingPreExpiry );
		Assert.IsTrue( saved.Rows[0].PreExpiryEnabled );
		Assert.IsFalse( saved.Rows[0].PreExpiryReached, "pending delivery must not be projected as already delivered" );
		Assert.IsFalse( old.Hud.ChallengeTerminalState( 0 )!.Value.PreExpiryReached, "capture must not dispatch or mutate the live HUD" );
		var loaded = new Clock { Now = 500_000 };
		loaded.Hud.RestoreMessageTube( saved );
		Assert.IsTrue( loaded.Hud.IsPendingMessageVisible( 0 ) );
		Assert.IsFalse( loaded.Hud.ChallengeTerminalState( 0 )!.Value.PreExpiryReached, "restoration is not dispatch" );
		loaded.Hud.SetChallengeMessageTimersSuspended( true );
		loaded.Hud.DispatchChallengeTerminalTimers();
		Assert.IsTrue( loaded.Hud.ChallengeTerminalState( 0 )!.Value.PreExpiryReached, "queued delivery still occurs while suspended" );
		Assert.IsFalse( loaded.Hud.CaptureMessageTube()!.Rows[0].PendingPreExpiry, "the sticky queued latch is not another pending event" );
		loaded.Hud.SetChallengeMessageTimersSuspended( false );
		loaded.Pump( 529_999 );
		Assert.IsTrue( loaded.Hud.IsPendingMessageVisible( 0 ) );
		loaded.Pump( 530_000 );
		Assert.IsFalse( loaded.Hud.IsPendingMessageVisible( 0 ) );
	}

	[TestMethod]
	public void ASuspendedQueuedExpiryRetainsItsVisibleRowUntilPostLoadDispatch()
	{
		var old = new Clock();
		PostTerminal( old );
		Assert.IsTrue( old.Hud.PostGameMessage( "plain" ) );
		old.Now = 30_000; // expiry queues at the inclusive boundary
		old.Hud.QueueChallengeTerminalTimers();
		old.Hud.SetChallengeMessageTimersSuspended( true );
		var saved = WorldRoundTrip( old.Hud.CaptureMessageTube()! );
		Assert.AreEqual( 2, saved.Rows.Count, "the still-visible terminal must remain available to post-load input" );
		Assert.IsTrue( saved.Rows[0].PendingExpiry );
		Assert.IsTrue( old.Hud.IsPendingMessageVisible( 0 ), "capture does not change the current frame" );
		var loaded = new Clock();
		loaded.Hud.RestoreMessageTube( saved );
		CollectionAssert.AreEqual( new[] { "terminal", "plain" }, loaded.Hud.PendingMessages.ToArray() );
		Assert.IsTrue( loaded.Hud.IsPendingMessageVisible( 0 ) );
		loaded.Hud.SetChallengeMessageTimersSuspended( true );
		loaded.Hud.DispatchChallengeTerminalTimers();
		Assert.IsFalse( loaded.Hud.IsPendingMessageVisible( 0 ), "without a read, queued expiry still delivers while suspended" );
		Assert.IsTrue( loaded.Hud.IsPendingMessageVisible( 1 ) );
		var afterDispatch = loaded.Hud.CaptureMessageTube()!;
		Assert.AreEqual( 1, afterDispatch.Rows.Count );
		Assert.AreEqual( "plain", afterDispatch.Rows[0].Text );
	}

	[TestMethod]
	public void RestoredPendingEventsAreNotDuplicatedAndCanBeReadBeforeDispatch()
	{
		foreach ( var age in new[] { 25_000, 30_000 } )
		{
			var old = new Clock();
			PostTerminal( old );
			old.Now = age;
			old.Hud.QueueChallengeTerminalTimers();
			var saved = WorldRoundTrip( old.Hud.CaptureMessageTube()! );
			var loaded = new Clock { Now = 500_000 };
			loaded.Hud.RestoreMessageTube( saved );
			Assert.IsTrue( loaded.Hud.IsPendingMessageVisible( 0 ) );
			Assert.IsFalse( loaded.Hud.ChallengeTerminalState( 0 )!.Value.PreExpiryReached );
			var expectedEvents = age == 25_000 ? 1 : 2;
			Assert.AreEqual( expectedEvents, QueuedTimerCount( loaded.Hud ) );
			loaded.Hud.QueueChallengeTerminalTimers(); // the ordinary next frame queues before input
			Assert.AreEqual( expectedEvents, QueuedTimerCount( loaded.Hud ), "rehydrated events must not be queued twice" );
			Assert.IsTrue( loaded.Hud.OpenPendingMessage( 0 ), "input still gets the row before dispatch" );
			loaded.Hud.CloseMessagePanel();
			loaded.Hud.DispatchChallengeTerminalTimers();
			Assert.AreEqual( 0, QueuedTimerCount( loaded.Hud ) );
			Assert.IsTrue( loaded.Hud.IsPendingMessageVisible( 0 ), "post-load read cancels queued expiry" );
			Assert.IsFalse( loaded.Hud.ChallengeTerminalState( 0 )!.Value.PreExpiryReached, "post-load read cancels queued warning" );
		}
	}

	[TestMethod]
	public void ReadingBeforeCaptureCancelsQueuedWarningAndExpiryAcrossTheSave()
	{
		var old = new Clock();
		PostTerminal( old );
		old.Now = 30_000;
		old.Hud.QueueChallengeTerminalTimers();
		Assert.IsTrue( old.Hud.OpenPendingMessage( 0 ) );
		old.Hud.CloseMessagePanel();
		old.Hud.SetChallengeMessageTimersSuspended( true );
		old.Now = 7_200_000;
		var saved = WorldRoundTrip( old.Hud.CaptureMessageTube()! );
		Assert.AreEqual( 1, saved.Rows.Count );
		Assert.IsFalse( saved.Rows[0].Active );
		Assert.IsFalse( saved.Rows[0].PendingPreExpiry );
		Assert.IsFalse( saved.Rows[0].PendingExpiry );
		Assert.IsFalse( saved.Rows[0].PreExpiryReached, "a read row cancels, rather than delivers, the queued warning" );
		var loaded = new Clock();
		loaded.Hud.RestoreMessageTube( saved );
		loaded.Pump( 9_000_000 );
		Assert.IsTrue( loaded.Hud.IsPendingMessageVisible( 0 ) );
	}

	[TestMethod]
	public void AnOverdueActiveTerminalRemainsDueWithoutSavingUnboundedClockAge()
	{
		var old = new Clock();
		PostTerminal( old );
		old.Now = 7_200_000; // capture before the next frame's timer dispatch
		var saved = WorldRoundTrip( old.Hud.CaptureMessageTube()! );
		Assert.AreEqual( 30_000L, saved.Rows[0].ElapsedMs );
		var loaded = new Clock { Now = 500_000 };
		loaded.Hud.RestoreMessageTube( saved );
		loaded.Pump( loaded.Now );
		Assert.IsFalse( loaded.Hud.IsPendingMessageVisible( 0 ), "an overdue row must not acquire a fresh lifetime" );
	}

	[TestMethod]
	public void ANullTubeRowIsRejectedByWorldSaveValidationBeforeHudRestoration()
	{
		var json = JsonNode.Parse( ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() ) )!;
		json["MessageTube"] = JsonNode.Parse( "{\"Rows\":[null]}" );
		Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ) );
		var malformed = ParkResumeJson.Deserialize<MessageTubeSaveData>( Encoding.UTF8.GetBytes( "{\"Rows\":[null]}" ) );
		var loaded = new Clock();
		Assert.ThrowsException<InvalidDataException>( () => loaded.Hud.RestoreMessageTube( malformed ) );
		Assert.AreEqual( 0, loaded.Hud.PendingMessages.Count, "failure comes before mutating the HUD" );
	}

	[TestMethod]
	public void TheTubeHoldsTenRowsAndRefusesTheEleventhWithoutEviction()
	{
		var clock = new Clock();
		for ( var index = 0; index < ParkHud.MessageTubeCapacity; index++ )
			Assert.IsTrue( clock.Hud.PostGameMessage( $"message {index}" ) );
		Assert.IsFalse( clock.Hud.PostGameMessage( "one too many" ) );
		Assert.AreEqual( 10, clock.Hud.PendingMessages.Count );
		Assert.AreEqual( "message 0", clock.Hud.PendingMessages[0], "the oldest row is not evicted" );
		Assert.AreEqual( MessageTubeSaveData.MaximumRows, ParkHud.MessageTubeCapacity );
	}

	[TestMethod]
	public void AnEmptyTubeSavesNothing()
	{
		Assert.IsNull( new Clock().Hud.CaptureMessageTube() );
	}

	[TestMethod]
	public void RowsAndTerminalTimersComeBackFromTheSave()
	{
		var old = new Clock { Now = 1000 };
		old.Hud.PostGameMessage( "plain" );
		Assert.IsTrue( old.Hud.PostChallengeProgress( 77, "progress", 3, 9 ) );
		Assert.IsTrue( ChallengeMessageRecords.TryGet( 1, ChallengePresentationKind.Success, out var record ) );
		Assert.IsTrue( old.Hud.PostChallengeTerminal( record, "terminal" ) );
		old.Now = 1000 + 26000;
		var saved = RoundTrip( old.Hud.CaptureMessageTube()! );
		Assert.AreEqual( 3, saved.Rows.Count );
		Assert.IsNull( saved.Problem() );

		var loaded = new Clock { Now = 500_000 };
		loaded.Hud.RestoreMessageTube( saved );
		CollectionAssert.AreEqual( new[] { "plain", "progress", "terminal" }, loaded.Hud.PendingMessages.ToArray() );
		// the progress row keeps its identity: a later update of the same row refreshes it instead of adding one
		Assert.IsTrue( loaded.Hud.PostChallengeProgress( 77, "progress 2", 3, 9 ) );
		Assert.AreEqual( 3, loaded.Hud.PendingMessages.Count );
		Assert.AreEqual( "progress 2", loaded.Hud.PendingMessages[1] );
		// 26 s of the 30 s lifetime had passed: it expires 4 s after the load, and its warning had already come due
		loaded.Pump( 500_000 + 3999 );
		Assert.IsTrue( loaded.Hud.IsPendingMessageVisible( 2 ) );
		loaded.Pump( 500_000 + 4000 );
		Assert.IsFalse( loaded.Hud.IsPendingMessageVisible( 2 ) );
	}

	[TestMethod]
	public void AnOfferKeepsItsAcceptAndDeclineActions()
	{
		var old = new Clock();
		old.Hud.PostGameMessage( "offer", old.Hud.ChallengeActions( () => null ) );
		var saved = RoundTrip( old.Hud.CaptureMessageTube()! );
		Assert.AreEqual( MessageTubeRowSave.Offer, saved.Rows[0].Kind );
		var loaded = new Clock();
		loaded.Hud.RestoreMessageTube( saved );
		Assert.IsTrue( loaded.Hud.OpenPendingMessage( 0 ) );
		CollectionAssert.AreEqual( new[] { GameMessageKind.Accept, GameMessageKind.Decline }, loaded.Hud.OpenedActions.Select( action => action.Kind ).ToArray() );
	}

	[TestMethod]
	public void ARowThatWaitsForItsReleaseIsNotSaved()
	{
		var clock = new Clock { Now = 0 };
		Assert.IsTrue( ChallengeMessageRecords.TryGet( 1, ChallengePresentationKind.Success, out var record ) );
		Assert.IsTrue( clock.Hud.PostChallengeTerminal( record, "terminal" ) );
		clock.Hud.PostGameMessage( "plain" );
		clock.Hud.DeletePendingMessage( 0 );
		var saved = clock.Hud.CaptureMessageTube()!;
		Assert.AreEqual( 1, saved.Rows.Count );
		Assert.AreEqual( "plain", saved.Rows[0].Text );
	}

	[TestMethod]
	public void InvalidTubesAreRefused()
	{
		var row = new MessageTubeRowSave( MessageTubeRowSave.Message, "text", 0, 0, 0, 0, 0, false, false, false );
		Assert.IsNull( new MessageTubeSaveData { Rows = new[] { row } }.Problem() );
		Assert.IsNotNull( new MessageTubeSaveData { Rows = Enumerable.Repeat( row, 11 ).ToArray() }.Problem(), "eleven rows" );
		Assert.IsNotNull( new MessageTubeSaveData { Rows = new MessageTubeRowSave[] { null! } }.Problem(), "null row" );
		Assert.IsNotNull( new MessageTubeSaveData { Rows = null! }.Problem(), "null collection" );
		Assert.IsNotNull( ( row with { Kind = 9 } ).Problem(), "unknown kind" );
		Assert.IsNotNull( ( row with { Text = " " } ).Problem(), "blank text" );
		Assert.IsNotNull( ( row with { Tag = 5 } ).Problem(), "plain row with an identity" );
		Assert.IsNotNull( ( row with { Kind = MessageTubeRowSave.Terminal, Tag = 5, Lifetime = 0 } ).Problem(), "terminal without a lifetime" );
		Assert.IsNotNull( ( row with { Kind = MessageTubeRowSave.Terminal, Tag = 5, Lifetime = 30000, ElapsedMs = -1 } ).Problem(), "negative elapsed time" );
		Assert.IsNotNull( ( row with { Kind = MessageTubeRowSave.Terminal, Tag = 5, Lifetime = 600001 } ).Problem(), "excessive lifetime remains invalid" );
		Assert.IsNotNull( ( row with { Kind = MessageTubeRowSave.Terminal, Tag = 5, Lifetime = 30000, ElapsedMs = 630001 } ).Problem(), "unbounded saved age remains invalid" );
		Assert.IsNotNull( ( row with { Kind = MessageTubeRowSave.Progress, Tag = 0 } ).Problem(), "progress without a tag" );
		Assert.IsNotNull( ( row with { Active = true } ).Problem(), "timer state on a plain row" );
		Assert.IsNotNull( ( row with { PendingPreExpiry = true } ).Problem(), "pending warning on a plain row" );
		Assert.IsNotNull( ( row with { PendingExpiry = true } ).Problem(), "pending expiry on a plain row" );
		var terminal = row with { Kind = MessageTubeRowSave.Terminal, Tag = 5, Lifetime = 30000, Active = true, PreExpiryEnabled = true };
		Assert.IsNull( ( terminal with { PendingPreExpiry = true, PendingExpiry = true } ).Problem() );
		Assert.IsNotNull( ( terminal with { Active = false, PendingExpiry = true } ).Problem(), "stopped timer with pending expiry" );
		Assert.IsNotNull( ( terminal with { Active = false, PendingPreExpiry = true } ).Problem(), "stopped timer with pending warning" );
		Assert.IsNotNull( ( terminal with { PreExpiryEnabled = false, PendingPreExpiry = true } ).Problem(), "disabled warning cannot be pending" );
		Assert.IsNotNull( ( terminal with { PreExpiryReached = true, PendingPreExpiry = true } ).Problem(), "delivered warning cannot still be pending" );
		Assert.IsNull( ( terminal with { Active = false } ).Problem(), "legacy no-pending flag combinations retain their previous acceptance" );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => new Clock().Hud.RestoreMessageTube( new MessageTubeSaveData { Rows = new[] { row with { Kind = 9 } } } ) );
		Assert.ThrowsException<InvalidDataException>( () => new Clock().Hud.RestoreMessageTube( new MessageTubeSaveData { Rows = null! } ) );
	}
}
