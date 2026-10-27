using System;
using System.Linq;
using System.Runtime.CompilerServices;
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

	private static MessageTubeSaveData RoundTrip( MessageTubeSaveData data ) => ParkResumeJson.Deserialize<MessageTubeSaveData>( ParkResumeJson.Serialize( data ) );

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
		Assert.IsNotNull( ( row with { Kind = 9 } ).Problem(), "unknown kind" );
		Assert.IsNotNull( ( row with { Text = " " } ).Problem(), "blank text" );
		Assert.IsNotNull( ( row with { Tag = 5 } ).Problem(), "plain row with an identity" );
		Assert.IsNotNull( ( row with { Kind = MessageTubeRowSave.Terminal, Tag = 5, Lifetime = 0 } ).Problem(), "terminal without a lifetime" );
		Assert.IsNotNull( ( row with { Kind = MessageTubeRowSave.Terminal, Tag = 5, Lifetime = 30000, ElapsedMs = -1 } ).Problem(), "negative elapsed time" );
		Assert.IsNotNull( ( row with { Kind = MessageTubeRowSave.Progress, Tag = 0 } ).Problem(), "progress without a tag" );
		Assert.IsNotNull( ( row with { Active = true } ).Problem(), "timer state on a plain row" );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => new Clock().Hud.RestoreMessageTube( new MessageTubeSaveData { Rows = new[] { row with { Kind = 9 } } } ) );
	}
}
