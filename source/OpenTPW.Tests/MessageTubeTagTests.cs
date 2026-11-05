using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The message tube as the original's tag windows behave: the tag table decides a row's lifetime and replacement, a read row stays, the expiry and warning timers
/// run on the UI clock, the warning starts the retained particle and the first show a burst (UI-025, ECON-058, PTCL-908).
/// </summary>
[TestClass]
[DoNotParallelize]
public class MessageTubeTagTests
{
	private sealed class Clock
	{
		public long Now;
		public ParkHud Hud { get; } = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		public Pool Particles { get; } = new();
		public Clock()
		{
			Hud.ChallengeMessageClock = () => Now;
			Hud.UiParticlePool = Particles;
		}

		public void Pump( long now )
		{
			Now = now;
			Hud.QueueChallengeTerminalTimers();
			Hud.DispatchChallengeTerminalTimers();
			Hud.ReleaseChallengeTerminalRows();
		}
	}

	/// <summary>Records what the tube asks of the particle pool; a handle is its running number.</summary>
	private sealed class Pool : IAdvisorParticlePool
	{
		public readonly List<(int Effect, (int X, int Y, int Z) At, int Handle)> Created = new();
		public readonly List<int> Killed = new();
		public readonly List<(int Handle, bool Hidden)> Hidden = new();
		public readonly List<(int Handle, (int X, int Y, int Z) At)> Moved = new();
		public readonly HashSet<int> Live = new();
		private int next = 1;

		public object? Create( int effectId, (int X, int Y, int Z) native )
		{
			var handle = next++;
			Created.Add( (effectId, native, handle) );
			Live.Add( handle );
			return handle;
		}

		public void Move( object handle, (int X, int Y, int Z) native ) => Moved.Add( ((int)handle, native) );
		public void Kill( object handle ) { Killed.Add( (int)handle ); Live.Remove( (int)handle ); }
		public bool IsAlive( object handle ) => Live.Contains( (int)handle );
		public void SetHidden( object handle, bool hidden ) => Hidden.Add( ((int)handle, hidden) );
	}

	[TestMethod]
	public void TheTagTableHoldsEveryTagOnceWithTheOriginalsLifetimes()
	{
		var table = MessageTagTable.Table;
		Assert.IsTrue( table.Count > 300, "the shipped table is the whole tag table" );
		// tag 64 is the advisor's "no rides" caption: its text entry 67, the buy-window action 9 and the 30 s lifetime
		var tag = MessageTagTable.For( 64 )!.Value;
		Assert.AreEqual( (2, 67, 9, 0, 30000), (tag.Icon, tag.Text, tag.FirstAction, tag.SecondAction, tag.Lifetime) );
		// the offers: accept 0x1A, decline 0x1B, 90 s; the progress rows live until they are replaced; the terminal tags are 30 s
		var offer = MessageTagTable.For( 247 )!.Value;
		Assert.IsTrue( offer.IsOffer );
		Assert.AreEqual( (0x1A, 0x1B, 90000), (offer.FirstAction, offer.SecondAction, offer.Lifetime) );
		Assert.AreEqual( 0, MessageTagTable.For( 280 )!.Value.Lifetime );
		Assert.IsTrue( ChallengeMessageRecords.TryGet( 1, ChallengePresentationKind.Success, out var success ) );
		Assert.AreEqual( 30000, MessageTagTable.For( success.Tag )!.Value.Lifetime );
		// the lookup refuses the tag of no text and 0x17E, as 0x50AF40 does
		Assert.IsNull( MessageTagTable.For( MessageTag.NoTag ) );
		Assert.IsNull( MessageTagTable.For( 0x17E ) );
		// every tag whose lifetime is not the default is one of the three kinds the original names
		Assert.IsTrue( table.Values.All( record => record.Lifetime is 0 or 30000 or 90000 ) );
	}

	[TestMethod]
	public void TheChallengeRecordsAgreeWithTheTagTable()
	{
		for ( var type = 1; type <= 33; type++ )
			foreach ( var kind in new[] { ChallengePresentationKind.Offer, ChallengePresentationKind.Progress, ChallengePresentationKind.Success, ChallengePresentationKind.Failure } )
			{
				Assert.IsTrue( ChallengeMessageRecords.TryGet( type, kind, out var record ) );
				var tag = MessageTagTable.For( record.Tag )!.Value;
				Assert.AreEqual( (record.ReplacementTag, record.Icon, record.Entry, record.FirstAction, record.SecondAction, record.Lifetime, record.Selector),
					(tag.Replaces, tag.Icon, tag.Text, tag.FirstAction, tag.SecondAction, tag.Lifetime, tag.Kind), $"type {type} {kind} tag {record.Tag}" );
			}
	}

	[TestMethod]
	public void TheTableReadsCrLfAndRefusesMalformedRows()
	{
		const string row = "  { tag = 3, replaces = 4, icon = 2, text = 7, a1 = 24, a2 = 0, life = 30000, kind = 0 },";
		var table = MessageTagTable.Parse( $"# c\r\n\r\ntags = [\r\n{row}\r\n]\r\n" );
		Assert.AreEqual( 1, table.Count );
		Assert.AreEqual( 24, table[3].FirstAction );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => MessageTagTable.Parse( $"tags = [\n{row}\n{row}\n]\n" ), "a tag twice" );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => MessageTagTable.Parse( $"tags = [\n{row}\n" ), "no closing bracket" );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => MessageTagTable.Parse( "tags = [\n  { tag = 3 },\n]\n" ), "a short row" );
	}

	[TestMethod]
	public void AMessageOfATagExpiresUnreadAfterItsLifetimeAndLeavesHalfASecondLater()
	{
		var clock = new Clock();
		Assert.IsTrue( clock.Hud.PostTagMessage( 64, "no rides" ) );
		clock.Pump( 29999 );
		Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ) );
		clock.Pump( 30000 );
		Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 0 ), "the expiry hides the tag" );
		Assert.AreEqual( 1, clock.Hud.PendingMessages.Count, "the slot is occupied until the removal" );
		clock.Pump( 30499 );
		Assert.AreEqual( 1, clock.Hud.PendingMessages.Count );
		clock.Pump( 30500 );
		Assert.AreEqual( 0, clock.Hud.PendingMessages.Count );
	}

	[TestMethod]
	public void AReadMessageStaysUntilItIsDeletedAndANoTagMessageNeverExpires()
	{
		var clock = new Clock();
		clock.Hud.PostTagMessage( 64, "tagged" );
		clock.Hud.PostGameMessage( "plain" );
		clock.Hud.PostTagMessage( 280, "progress" );
		Assert.IsTrue( clock.Hud.OpenPendingMessage( 0 ) );
		Assert.AreEqual( 3, clock.Hud.PendingMessages.Count, "reading removes nothing" );
		clock.Pump( 1_000_000 );
		CollectionAssert.AreEqual( new[] { "tagged", "plain", "progress" }, clock.Hud.PendingMessages.ToArray(), "a read tag and a tag without a lifetime are never expired" );
		Assert.IsTrue( clock.Hud.OpenPendingMessage( 1 ) );
		Assert.AreEqual( "plain", clock.Hud.OpenedMessage );
		clock.Hud.DeleteOpenedMessage();
		Assert.IsNull( clock.Hud.OpenedMessage );
		clock.Pump( 1_000_500 );
		CollectionAssert.AreEqual( new[] { "tagged", "progress" }, clock.Hud.PendingMessages.ToArray() );
	}

	[TestMethod]
	public void AnUnreadTagKeepsItsTimersWhileAnotherIsRead()
	{
		var clock = new Clock();
		clock.Hud.PostTagMessage( 64, "first" );
		clock.Hud.PostTagMessage( 62, "second" );
		clock.Hud.OpenPendingMessage( 1 );
		Assert.IsTrue( clock.Hud.ChallengeTerminalState( 0 )!.Value.Active );
		Assert.IsFalse( clock.Hud.ChallengeTerminalState( 1 )!.Value.Active, "the read tag's timers are stopped" );
		clock.Pump( 30000 );
		CollectionAssert.AreEqual( new[] { "first", "second" }, clock.Hud.PendingMessages.ToArray() );
		Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 0 ) );
		Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 1 ) );
	}

	[TestMethod]
	public void ALifetimeOfTheMessageOverridesTheTagsAndNegativeKeepsIt()
	{
		var clock = new Clock();
		clock.Hud.PostTagMessage( 64, "short", lifetime: 6000 );
		clock.Hud.PostTagMessage( 62, "tag's own", lifetime: -1 );
		clock.Hud.PostTagMessage( 63, "none", lifetime: 0 );
		clock.Pump( 6000 );
		Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 0 ) );
		Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 1 ) );
		clock.Pump( 1_000_000 );
		Assert.IsTrue( clock.Hud.IsPendingMessageVisible( clock.Hud.PendingMessages.ToList().IndexOf( "none" ) ), "a lifetime of 0 keeps the row" );
	}

	[TestMethod]
	public void ATagReplacesTheRowsOfItsReplacedTagWithTheSameContextAtOnceAndASameTagRefreshes()
	{
		var clock = new Clock();
		// tags 3 and 4 replace each other (the entry fee too low and too high)
		Assert.AreEqual( 4, MessageTagTable.For( 3 )!.Value.Replaces );
		Assert.AreEqual( 3, MessageTagTable.For( 4 )!.Value.Replaces );
		clock.Hud.PostTagMessage( 3, "too low", 1, 2 );
		clock.Hud.PostTagMessage( 4, "other context", 1, 3 );
		Assert.AreEqual( 2, clock.Hud.PendingMessages.Count, "another context is another row" );
		clock.Hud.PostTagMessage( 4, "too high", 1, 2 );
		CollectionAssert.AreEqual( new[] { "other context", "too high" }, clock.Hud.PendingMessages.ToArray(), "the replaced row is removed at once, not after 500 ms" );
		// a tag that replaces itself refreshes the open row in place
		clock.Hud.PostTagMessage( 280, "progress", 0, 0 );
		clock.Hud.OpenPendingMessage( 2 );
		Assert.IsTrue( clock.Hud.PostChallengeProgress( 280, "progress 2" ) );
		Assert.AreEqual( "progress 2", clock.Hud.OpenedMessage );
		Assert.AreEqual( 3, clock.Hud.PendingMessages.Count );
	}

	[TestMethod]
	public void AnAnsweredRowStaysDrawnButCannotBeClickedUntilItsRemoval()
	{
		var clock = new Clock();
		clock.Hud.PostTagMessage( 64, "message" );
		clock.Hud.OpenPendingMessage( 0 );
		clock.Hud.MessagePanelSlidOut();
		clock.Now = 10;
		Assert.IsTrue( clock.Hud.DeletePendingMessage( 0 ), "a right click hides the row" );
		Assert.IsFalse( clock.Hud.IsPendingMessageDrawn( 0 ) );
		Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 0 ) );
		Assert.IsFalse( clock.Hud.DeletePendingMessage( 0 ), "a removal already started is not started again" );
		clock.Pump( 510 );
		Assert.AreEqual( 0, clock.Hud.PendingMessages.Count );
	}

	[TestMethod]
	public void ARightClickOnTheTubeBodyRemovesEveryRowOneHundredMillisecondsApart()
	{
		var clock = new Clock();
		for ( var index = 0; index < 4; index++ )
			clock.Hud.PostTagMessage( 280 + index, $"row {index}" );
		clock.Hud.OpenPendingMessage( 1 );
		clock.Now = 1000;
		clock.Hud.DeleteAllPendingMessages();
		clock.Pump( 1099 );
		Assert.AreEqual( 4, clock.Hud.PendingMessages.Count, "the first goes at 100 ms" );
		clock.Pump( 1100 );
		CollectionAssert.AreEqual( new[] { "row 1", "row 2", "row 3" }, clock.Hud.PendingMessages.ToArray() );
		clock.Pump( 1200 );
		Assert.IsNull( clock.Hud.OpenedMessage, "the open row's removal closes its panel" );
		clock.Pump( 1400 );
		Assert.AreEqual( 0, clock.Hud.PendingMessages.Count );
	}

	[TestMethod]
	public void TheOpenRowIsProtectedFromARightClickUntilThePanelHasSlidOut()
	{
		var clock = new Clock();
		clock.Hud.PostTagMessage( 64, "first" );
		clock.Hud.PostTagMessage( 62, "second" );
		clock.Hud.OpenPendingMessage( 0 );
		Assert.IsFalse( clock.Hud.DeletePendingMessage( 0 ) );
		Assert.IsTrue( clock.Hud.DeletePendingMessage( 1 ), "any other row can be deleted" );
		clock.Hud.MessagePanelSlidOut();
		Assert.IsTrue( clock.Hud.DeletePendingMessage( 0 ) );
		Assert.IsNull( clock.Hud.OpenedMessage, "the right click closes the panel of the row it deletes" );
	}

	[TestMethod]
	public void TheFirstShowStartsABurstAtTheIconsCentreOnce()
	{
		var clock = new Clock();
		clock.Hud.PostTagMessage( 64, "first" );
		clock.Hud.PostTagMessage( 62, "second" );
		CollectionAssert.AreEqual( new[] { ParkHud.TagShownEffect, ParkHud.TagShownEffect }, clock.Particles.Created.Select( made => made.Effect ).ToArray() );
		Assert.AreEqual( ParkHud.UiParticleNative( ParkHud.TubeIcon( 0 ) ), clock.Particles.Created[0].At );
		Assert.AreEqual( ParkHud.UiParticleNative( ParkHud.TubeIcon( 1 ) ), clock.Particles.Created[1].At );
		clock.Hud.PostTagMessage( 280, "progress" );
		clock.Hud.PostTagMessage( 280, "refreshed" );
		Assert.AreEqual( 3, clock.Particles.Created.Count, "a refresh shows nothing new" );
	}

	[TestMethod]
	public void TheWarningTimerStartsTheRetainedParticleFiveSecondsBeforeTheExpiryAndAReadEndsIt()
	{
		var clock = new Clock();
		clock.Hud.PostTagMessage( 64, "first" );
		clock.Hud.PostTagMessage( 62, "second" );
		clock.Pump( 24999 );
		Assert.IsFalse( clock.Particles.Created.Any( made => made.Effect == ParkHud.TagWarningEffect ) );
		clock.Pump( 25000 );
		var warnings = clock.Particles.Created.Where( made => made.Effect == ParkHud.TagWarningEffect ).ToArray();
		Assert.AreEqual( 2, warnings.Length );
		Assert.AreEqual( ParkHud.UiParticleNative( ParkHud.TubeIcon( 0 ) ), warnings[0].At );
		clock.Pump( 26000 );
		Assert.AreEqual( 2, clock.Particles.Created.Count( made => made.Effect == ParkHud.TagWarningEffect ), "the warning timer disabled itself" );
		Assert.IsTrue( clock.Hud.ChallengeTerminalState( 0 )!.Value.PreExpiryReached );
		// a read of the second row, whose timers are stopped, ends its particle; the first keeps its own
		clock.Hud.OpenPendingMessage( 1 );
		CollectionAssert.AreEqual( new[] { warnings[1].Handle }, clock.Particles.Killed );
		// the expiry hides the first row and its particle; the removal ends it
		clock.Pump( 30000 );
		CollectionAssert.Contains( clock.Particles.Hidden, (warnings[0].Handle, true) );
		clock.Pump( 30500 );
		CollectionAssert.Contains( clock.Particles.Killed, warnings[0].Handle );
	}

	[TestMethod]
	public void ARemovedRowBelowMovesTheRetainedParticleOfTheRowAbove()
	{
		var clock = new Clock();
		clock.Hud.PostTagMessage( 64, "first" );
		clock.Hud.PostTagMessage( 62, "second" );
		clock.Hud.PostTagMessage( 63, "third" );
		clock.Pump( 25000 );
		var third = clock.Particles.Created.Last( made => made.Effect == ParkHud.TagWarningEffect );
		clock.Hud.OpenPendingMessage( 0 );
		clock.Hud.MessagePanelSlidOut();
		Assert.IsTrue( clock.Hud.DeletePendingMessage( 1 ) );
		clock.Now += 500;
		clock.Hud.ReleaseChallengeTerminalRows();
		clock.Hud.MovesParticlesForTests();
		CollectionAssert.AreEqual( new[] { (third.Handle, ParkHud.UiParticleNative( ParkHud.TubeIcon( 1 ) )) }, clock.Particles.Moved );
	}

	[TestMethod]
	public void TheParticlePositionIsTheCentreInTheNativeScreenUnits()
	{
		// the original's own arithmetic: x = centre * 100000 / 2048 and z = centre row * 75000 / 1536, truncating
		var native = ParkHud.UiParticleNative( new UiRect( 982, 600, 100, 100 ) );
		Assert.AreEqual( (1032 * 100000 / 2048, 0, 650 * 75000 / 1536), native );
		Assert.AreEqual( (0, 0, 0), ParkHud.UiParticleNative( new UiRect( -1, -1, 2, 2 ) ), "an odd negative centre truncates toward zero" );
		Assert.AreEqual( (50000, 0, 37500), ParkHud.UiParticleNative( new UiRect( 1024 - 5, 768 - 5, 10, 10 ) ), "the canvas centre is the screen centre" );
	}

	[TestMethod]
	public void ATimerReportsOnceAPassedIntervalKeepsTheRemainderAndAdvancesWhenDisabled()
	{
		var timer = new UiTimer();
		timer.SetInterval( 1000 );
		timer.Enable( 100 );
		Assert.AreEqual( 0, timer.Tick( 1099 ) );
		Assert.AreEqual( 1, timer.Tick( 1100 ) );
		Assert.AreEqual( 1100, timer.Origin );
		Assert.AreEqual( 1, timer.Tick( 3550 ), "two intervals passed in one tick: at most one report" );
		Assert.AreEqual( 3100, timer.Origin, "the remainder of 450 ms is kept" );
		timer.Disable();
		Assert.AreEqual( 0, timer.Tick( 9999 ), "a disabled timer posts nothing" );
		Assert.AreNotEqual( 3100, timer.Origin, "but its origin advances" );
	}

	[TestMethod]
	public void ATimerCountsAcrossTheWrapOfTheThirtyTwoBitClock()
	{
		var timer = new UiTimer();
		timer.SetInterval( 1000 );
		timer.Enable( int.MaxValue - 100 );
		Assert.AreEqual( 0, timer.Tick( int.MinValue + 800 ), "900 ms after the origin" );
		Assert.AreEqual( 1, timer.Tick( int.MinValue + 900 ), "1000 ms after the origin, past the wrap" );
	}

	[TestMethod]
	public void SuspendingTheTimersAndResumingRestartsEveryTimerFromTheResumeClock()
	{
		var clock = new Clock();
		clock.Hud.PostTagMessage( 64, "message" );
		clock.Pump( 20000 );
		clock.Hud.SetChallengeMessageTimersSuspended( true );
		clock.Pump( 90000 );
		Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ), "no tick while suspended" );
		clock.Hud.SetChallengeMessageTimersSuspended( false );
		clock.Pump( 119999 );
		Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ) );
		clock.Pump( 120000 );
		Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 0 ) );
	}

	[TestMethod]
	public void TheFocusLossPauseSuspendsTheTubeTimersLikeTheOtherPauseOwners()
	{
		// [BIN:TPW-EXE:0x48A6E0] the pause 0x4092A0(1) of the deactivation suspends the UI timers before it builds the P window
		Assert.IsTrue( ParkHud.ChallengeMessageSuspensionFor( false, Array.Empty<string>(), focusPaused: true ) );
		Assert.IsFalse( ParkHud.ChallengeMessageSuspensionFor( false, Array.Empty<string>() ) );
	}

	[TestMethod]
	public void ATagsActionsBecomeThePanelsButtonsWithTheOriginalsArtAndHelp()
	{
		var clock = new Clock();
		clock.Hud.PostTagMessage( 64, "buy" );      // action 9: the rides tab of the buy window
		clock.Hud.PostTagMessage( 10, "hire" );     // action 13: the hire window of handymen
		clock.Hud.PostTagMessage( 129, "nothing" ); // action 25 has no button
		clock.Hud.PostTagMessage( 60, "locate" );   // action 3: locate a thing, panel and tag stay
		clock.Hud.OpenPendingMessage( 0 );
		Assert.AreEqual( (GameMessageKind.Buy, 0x209), (clock.Hud.OpenedActions[0].Kind, clock.Hud.OpenedActions[0].HelpId) );
		clock.Hud.OpenPendingMessage( 1 );
		Assert.AreEqual( (GameMessageKind.Buy, 0x20D), (clock.Hud.OpenedActions[0].Kind, clock.Hud.OpenedActions[0].HelpId) );
		clock.Hud.OpenPendingMessage( 2 );
		Assert.AreEqual( 0, clock.Hud.OpenedActions.Count );
		clock.Hud.OpenPendingMessage( 3 );
		Assert.AreEqual( (GameMessageKind.Locate, 0x204), (clock.Hud.OpenedActions[0].Kind, clock.Hud.OpenedActions[0].HelpId) );
		CollectionAssert.AreEqual( new[] { "mb_buy", "b_loan", "b_pkinfo", "b_staffcost", "b_entry" }, new[] { GameMessageKind.Buy, GameMessageKind.Loans, GameMessageKind.ParkInfo, GameMessageKind.StaffCost, GameMessageKind.Entry }.Select( kind => kind.Model() ).ToArray() );
	}

	[TestMethod]
	public void ARecordOfTagZeroImportsAndABadRecordIsDroppedNotThrown()
	{
		static byte[] Record( int tag, string text, int lifetime )
		{
			var bytes = new byte[SaveTubeRecord.Bytes];
			BitConverter.GetBytes( tag ).CopyTo( bytes, 0 );
			Encoding.Unicode.GetBytes( text + "\0" ).CopyTo( bytes, 4 );
			BitConverter.GetBytes( lifetime ).CopyTo( bytes, 0x204 );
			return bytes;
		}
		var records = SaveTubeRecord.Read( Record( 0, "tag zero", 30000 ).Concat( Record( 1, "tag one", 0 ) ).Concat( Record( 64, "bad lifetime", 700000 ) ).ToArray(), 3 );
		var problems = new List<string>();
		var tube = OriginalTubeImport.Rows( records, problems )!;
		Assert.AreEqual( 2, tube.Rows.Count );
		Assert.AreEqual( 1, problems.Count );
		Assert.IsNull( tube.Problem() );
		var clock = new Clock();
		clock.Hud.RestoreMessageTube( tube );
		CollectionAssert.AreEqual( new[] { "tag zero", "tag one" }, clock.Hud.PendingMessages.ToArray() );
	}

	[TestMethod]
	public void ATagShownDuringAPauseSetsItsFlagButStartsNoBurst()
	{
		var clock = new Clock();
		clock.Hud.UiPausedOverride = true;
		clock.Hud.PostTagMessage( 64, "paused" );
		Assert.AreEqual( 0, clock.Particles.Created.Count );
		clock.Hud.UiPausedOverride = false;
		clock.Hud.PostTagMessage( 62, "running" );
		Assert.AreEqual( 1, clock.Particles.Created.Count, "only the later tag bursts: the first one's flag was set while paused" );
	}

	[TestMethod]
	public void AnOfferTagReplacesItsProgressTagRowAtOnce()
	{
		Assert.AreEqual( 306, MessageTagTable.For( 274 )!.Value.Replaces );
		var clock = new Clock();
		clock.Hud.PostTagMessage( 306, "progress" );
		clock.Hud.PostTagMessage( 274, "offer", actions: Array.Empty<GameMessageAction>() );
		CollectionAssert.AreEqual( new[] { "offer" }, clock.Hud.PendingMessages.ToArray(), "the offer tag's record replaces tag 306 on the same context (0x48ebd5)" );
	}

	[TestMethod]
	public void ASecondRightClickOnTheTubeDoesNotDelayTheFirstClearsRemovals()
	{
		var clock = new Clock();
		clock.Hud.PostTagMessage( 280, "a" );
		clock.Hud.PostTagMessage( 281, "b" );
		clock.Now = 1000;
		clock.Hud.DeleteAllPendingMessages();
		clock.Now = 1050;
		clock.Hud.DeleteAllPendingMessages();
		clock.Pump( 1100 );
		CollectionAssert.AreEqual( new[] { "b" }, clock.Hud.PendingMessages.ToArray() );
		clock.Pump( 1200 );
		Assert.AreEqual( 0, clock.Hud.PendingMessages.Count );
	}

	[TestMethod]
	public void TheLocateActionsReadTheirOperandsFromTheWordAndTheDwordOfThePost()
	{
		var clock = new Clock();
		var seen = new List<(int X, int Y)>();
		clock.Hud.CellJumpForTests = ( x, y ) => seen.Add( (x, y) );
		clock.Hud.ThingCellForTests = id => id == 77 ? (12, 34) : null;
		// tag 60 (action 3): the thing id is the context word, the dword is something else
		clock.Hud.PostTagMessage( 60, "thing", 77, 5 );
		// tag 45 (action 4): the cell is the dword's low word, z * 128 + x + 1
		clock.Hud.PostTagMessage( 45, "cell", 9, 3 * 128 + 7 + 1 );
		clock.Hud.OpenPendingMessage( 0 );
		clock.Hud.OpenedActions[0].Clicked();
		Assert.AreEqual( 1, clock.Hud.PendingMessages.Count( message => message == "thing" ), "locate leaves the tag" );
		Assert.IsNotNull( clock.Hud.OpenedMessage, "and the panel" );
		clock.Hud.OpenPendingMessage( 1 );
		clock.Hud.OpenedActions[0].Clicked();
		CollectionAssert.AreEqual( new[] { (12, 34), (7, 3) }, seen );
	}

	[TestMethod]
	public void AnAdvisorsRecordWordsDriveTheLocateButtonsOfItsCaptions()
	{
		// the post of an advisor caption (0x59B7D4) takes the record's id word as the context word (a short) and its cell dword as the context dword
		var clock = new Clock();
		var things = new List<int>();
		var cells = new List<(int X, int Y)>();
		clock.Hud.ThingCellForTests = id => { things.Add( id ); return (id & 0x7F, id >> 7); };
		clock.Hud.CellJumpForTests = ( x, y ) => cells.Add( (x, y) );
		// the dirty toilets' tag 88 locates a thing; a pool slot above 32767 survives the short
		ushort thing = 40000;
		clock.Hud.PostTagMessage( 88, "dirty toilet", unchecked( (short)thing ), 0 );
		// the big busload's tag 162 locates the cell of the second bus stop, zero-extended from its word
		uint cell = (uint)(12 * 128 + 34 + 1);
		clock.Hud.PostTagMessage( 162, "big bus", 0, unchecked( (int)cell ) );
		clock.Hud.OpenPendingMessage( 0 );
		clock.Hud.OpenedActions[0].Clicked();
		clock.Hud.OpenPendingMessage( 1 );
		clock.Hud.OpenedActions[0].Clicked();
		CollectionAssert.AreEqual( new[] { 40000 }, things );
		CollectionAssert.AreEqual( new[] { (40000 & 0x7F, 40000 >> 7), (34, 12) }, cells );
	}

	[TestMethod]
	public void AnOriginalSavesTubeRecordsReadAndBecomeRows()
	{
		static byte[] Record( int tag, string text, int lifetime, short icon, int first, int second, short word, int dword )
		{
			var bytes = new byte[SaveTubeRecord.Bytes];
			BitConverter.GetBytes( tag ).CopyTo( bytes, 0 );
			Encoding.Unicode.GetBytes( text + "\0" ).CopyTo( bytes, 4 );
			// the buffer after the terminator holds stack leftovers
			bytes[4 + (text.Length + 1) * 2] = 0x37;
			BitConverter.GetBytes( lifetime ).CopyTo( bytes, 0x204 );
			BitConverter.GetBytes( icon ).CopyTo( bytes, 0x208 );
			BitConverter.GetBytes( first ).CopyTo( bytes, 0x20C );
			BitConverter.GetBytes( second ).CopyTo( bytes, 0x210 );
			BitConverter.GetBytes( word ).CopyTo( bytes, 0x214 );
			BitConverter.GetBytes( dword ).CopyTo( bytes, 0x218 );
			return bytes;
		}
		var block = Record( 64, "Your park has no rides in it.", 30000, 2, 9, 0, 0, 0 ).Concat( Record( 280, "progress", 0, 4, 0, 0, 3, 77 ) ).Concat( Record( 247, "an offer", 90000, 4, 26, 27, 0, 0 ) )
			.Concat( Record( 9999, "unknown tag", 30000, 2, 0, 0, 0, 0 ) ).ToArray();
		var records = SaveTubeRecord.Read( block, 4 );
		Assert.AreEqual( "Your park has no rides in it.", records[0].Text );
		Assert.AreEqual( (64, 30000, (short)2, 9, 0), (records[0].Tag, records[0].Lifetime, records[0].Icon, records[0].FirstAction, records[0].SecondAction) );
		Assert.AreEqual( ((short)3, 77), (records[1].ContextShort, records[1].ContextInt) );
		var problems = new List<string>();
		var tube = OriginalTubeImport.Rows( records, problems )!;
		CollectionAssert.AreEqual( new[] { MessageTubeRowSave.Terminal, MessageTubeRowSave.Progress, MessageTubeRowSave.Offer }, tube.Rows.Select( row => row.Kind ).ToArray() );
		Assert.AreEqual( 30000, tube.Rows[0].Lifetime );
		Assert.AreEqual( 1, problems.Count, "the unknown tag is named" );
		Assert.IsNull( tube.Problem() );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => SaveTubeRecord.Read( block, 3 ), "a count that is not the block's" );
		// the park HUD restores them: every row's timers start from the full lifetime and the first show bursts
		var clock = new Clock { Now = 1000 };
		clock.Hud.RestoreMessageTube( tube );
		CollectionAssert.AreEqual( new[] { "Your park has no rides in it.", "progress", "an offer" }, clock.Hud.PendingMessages.ToArray() );
		clock.Pump( 1000 + 29999 );
		Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ) );
		clock.Pump( 1000 + 30000 );
		Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 0 ) );
		Assert.AreEqual( 3, clock.Particles.Created.Count( made => made.Effect == ParkHud.TagShownEffect ) );
	}

	[TestMethod]
	public void ASavedTubeKeepsTheTagsAndTheLifetimesOfTheGenericRowsToo()
	{
		var clock = new Clock();
		clock.Hud.PostTagMessage( 64, "terminal", 2, 5 );
		clock.Hud.PostTagMessage( 280, "progress" );
		clock.Hud.PostGameMessage( "plain" );
		var saved = clock.Hud.CaptureMessageTube()!;
		CollectionAssert.AreEqual( new[] { MessageTubeRowSave.Terminal, MessageTubeRowSave.Progress, MessageTubeRowSave.Message }, saved.Rows.Select( row => row.Kind ).ToArray() );
		Assert.AreEqual( (64, (short)2, 5, 30000), (saved.Rows[0].Tag, saved.Rows[0].ContextShort, saved.Rows[0].ContextInt, saved.Rows[0].Lifetime) );
		Assert.IsNull( saved.Problem() );
		var loaded = new Clock { Now = 5000 };
		loaded.Hud.RestoreMessageTube( saved );
		loaded.Hud.PostTagMessage( 64, "new" , 2, 5 );
		Assert.AreEqual( 4, loaded.Hud.PendingMessages.Count, "tag 64 replaces nothing, so the new post adds a row beside the restored one" );
	}
}
