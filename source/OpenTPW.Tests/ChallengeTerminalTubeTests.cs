using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class ChallengeTerminalTubeTests
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

	private static ChallengePresentation Message( int type = 1, ChallengePresentationKind kind = ChallengePresentationKind.Success ) =>
		new( kind, type, 137, 10, 20, 83, 4, 1180, 1100, 0, "synthetic offer" );

	private static StringFile Templates()
	{
		var table = new StringFile( new MemoryStream( StringTableTests.CreateStringTable( Enumerable.Range( 0, 332 ).Select( _ => Array.Empty<byte>() ).ToArray() ) ),
			new BFMUReader( new MemoryStream( StringTableTests.CreateCharacterTable( "x" ) ) ) );
		foreach ( var kind in new[] { ChallengePresentationKind.Success, ChallengePresentationKind.Failure } )
			for ( var type = 1; type <= 33; type++ )
			{
				Assert.IsTrue( ChallengeMessageRecords.TryGet( type, kind, out var record ) );
				table.Templates[record.Entry] = new StringTemplate( kind == ChallengePresentationKind.Success
					? new[] { new StringTemplatePart( "synthetic success ", -1 ), new StringTemplatePart( "", 3 ) }
					: new[] { new StringTemplatePart( "synthetic failure ", -1 ), new StringTemplatePart( "", 4 ), new StringTemplatePart( "/", -1 ), new StringTemplatePart( "", 1 ) } );
			}
		return table;
	}

	private static void Post( Clock clock, int type = 1, ChallengePresentationKind kind = ChallengePresentationKind.Success ) =>
		Assert.IsTrue( clock.Hud.ApplyChallengePresentation( Message( type, kind ), Templates(), _ => throw new AssertFailedException( "Terminal selectors need no live object names." ), _ => null ) );

	private static (bool Active, bool PreExpiryEnabled, bool PreExpiryReached, long? ReleaseAt) State( Clock clock, int index = 0 ) =>
		clock.Hud.ChallengeTerminalState( index ) ?? throw new AssertFailedException( "No terminal instance." );

	[TestMethod]
	public void All66LocalizedTerminalsUseEmittedPrizeOrRawValueAndNoActions()
	{
		using var audio = new AudioTriggerProbe();
		foreach ( var kind in new[] { ChallengePresentationKind.Success, ChallengePresentationKind.Failure } )
			for ( var type = 1; type <= 33; type++ )
			{
				var clock = new Clock();
				Post( clock, type, kind );
				Assert.AreEqual( kind == ChallengePresentationKind.Success ? "synthetic success 137" : "synthetic failure 83/20", clock.Hud.PendingMessages.Single(), $"{kind}/{type}" );
				Assert.IsTrue( clock.Hud.OpenPendingMessage( 0 ) );
				Assert.AreEqual( 0, clock.Hud.OpenedActions.Count );
				Assert.AreEqual( 1, clock.Hud.PendingMessages.Count );
			}
		Assert.AreEqual( 66, audio.Events( "cat_ui" ).Count( id => id == ParkHud.MessagePostedEvent ) );
	}

	[TestMethod]
	public void TerminalThresholdsHideAtThirtySecondsAndReleaseOnlyAfterAnotherFiveHundredMilliseconds()
	{
		using var audio = new AudioTriggerProbe();
		var clock = new Clock();
		Post( clock );
		clock.Pump( 24999 );
		Assert.IsFalse( State( clock ).PreExpiryReached );
		clock.Pump( 25000 );
		Assert.IsTrue( State( clock ).PreExpiryReached );
		Assert.IsFalse( State( clock ).PreExpiryEnabled );
		clock.Pump( 29999 );
		Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ) );
		clock.Pump( 30000 );
		Assert.AreEqual( 30500L, State( clock ).ReleaseAt );
		Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 0 ) );
		Assert.IsFalse( clock.Hud.OpenPendingMessage( 0 ) );
		Assert.IsFalse( clock.Hud.DeletePendingMessage( 0 ) );
		Assert.AreEqual( 1, clock.Hud.PendingMessages.Count, "hidden still occupies its slot" );
		clock.Pump( 30499 );
		Assert.AreEqual( 1, clock.Hud.PendingMessages.Count );
		clock.Pump( 30500 );
		Assert.AreEqual( 0, clock.Hud.PendingMessages.Count );
		CollectionAssert.AreEqual( new[] { ParkHud.MessagePostedEvent }, audio.Events( "cat_ui" ), "particle 91 must never become a warning sound" );
	}

	[TestMethod]
	public void ReadingBeforeOrAfterPreExpiryPermanentlyDisablesTimersAcrossCloseAndResume()
	{
		foreach ( var readAt in new[] { 0L, 24999, 25000, 29999 } )
		{
			var clock = new Clock();
			Post( clock );
			clock.Pump( readAt );
			clock.Hud.OpenPendingMessage( 0 );
			Assert.IsFalse( State( clock ).Active );
			Assert.IsFalse( State( clock ).PreExpiryEnabled );
			clock.Hud.CloseMessagePanel();
			clock.Hud.SetChallengeMessageTimersSuspended( true );
			clock.Now = 100000;
			clock.Hud.SetChallengeMessageTimersSuspended( false );
			clock.Pump( 1000000 );
			Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ), $"read at {readAt}" );
			Assert.IsNull( clock.Hud.OpenedMessage );
			clock.Hud.OpenPendingMessage( 0 );
			clock.Hud.OpenPendingMessage( 0 );
			Assert.IsNull( clock.Hud.OpenedMessage, "selected icon is close, never deletion" );
			Assert.IsNull( State( clock ).ReleaseAt );
		}
	}

	[TestMethod]
	public void QueuedExpiryThenReadUsesTheActiveGuardRatherThanAssumingStopClearsTheQueue()
	{
		var clock = new Clock();
		Post( clock );
		clock.Now = 30000;
		clock.Hud.QueueChallengeTerminalTimers();
		Assert.IsTrue( clock.Hud.OpenPendingMessage( 0 ) );
		clock.Hud.DispatchChallengeTerminalTimers();
		clock.Pump( 100000 );
		Assert.AreEqual( "synthetic success 137", clock.Hud.OpenedMessage );
		Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ) );
		Assert.IsFalse( State( clock ).PreExpiryReached, "the bounded managed queue suppresses both events once read" );
	}

	[TestMethod]
	public void SuspensionRestartsTheFullLifetimeRatherThanPreservingRemainingTime()
	{
		var clock = new Clock();
		Post( clock );
		clock.Pump( 20000 );
		clock.Hud.SetChallengeMessageTimersSuspended( true );
		clock.Pump( 200000 );
		Assert.IsFalse( State( clock ).PreExpiryReached );
		clock.Hud.SetChallengeMessageTimersSuspended( false );
		clock.Pump( 224999 );
		Assert.IsFalse( State( clock ).PreExpiryReached );
		clock.Pump( 225000 );
		Assert.IsTrue( State( clock ).PreExpiryReached );
		clock.Pump( 229999 );
		Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ) );
		clock.Pump( 230000 );
		Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 0 ) );
	}

	[TestMethod]
	public void ResumeAfterPreExpiryLeavesItsDisabledTimerDisabledAndRestartsExpiryOnly()
	{
		var clock = new Clock();
		Post( clock );
		clock.Pump( 25000 );
		clock.Hud.SetChallengeMessageTimersSuspended( true );
		clock.Now = 100000;
		clock.Hud.SetChallengeMessageTimersSuspended( false );
		Assert.IsFalse( State( clock ).PreExpiryEnabled );
		clock.Pump( 125000 );
		Assert.IsFalse( State( clock ).PreExpiryEnabled );
		Assert.IsTrue( State( clock ).Active );
		clock.Pump( 130000 );
		Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 0 ) );
	}

	[TestMethod]
	public void SuspendIsOneFlagAndRedundantResumeDoesNotKeepPostponingTheOrigin()
	{
		var clock = new Clock();
		Post( clock );
		clock.Now = 10000;
		clock.Hud.SetChallengeMessageTimersSuspended( true );
		clock.Now = 20000;
		clock.Hud.SetChallengeMessageTimersSuspended( true );
		clock.Now = 30000;
		clock.Hud.SetChallengeMessageTimersSuspended( false );
		clock.Now = 40000;
		clock.Hud.SetChallengeMessageTimersSuspended( false );
		clock.Pump( 55000 );
		Assert.IsTrue( State( clock ).PreExpiryReached );
		clock.Pump( 60000 );
		Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 0 ) );
	}

	[TestMethod]
	public void AlreadyQueuedExpiryAndDelayedFinalReleaseAreIndependentOfSuspension()
	{
		var clock = new Clock();
		Post( clock );
		clock.Now = 30000;
		clock.Hud.QueueChallengeTerminalTimers();
		clock.Hud.SetChallengeMessageTimersSuspended( true );
		clock.Hud.DispatchChallengeTerminalTimers();
		clock.Now = 30499;
		clock.Hud.ReleaseChallengeTerminalRows();
		Assert.AreEqual( 1, clock.Hud.PendingMessages.Count );
		clock.Now = 30500;
		clock.Hud.ReleaseChallengeTerminalRows();
		Assert.AreEqual( 0, clock.Hud.PendingMessages.Count );
	}

	[TestMethod]
	public void ReleaseDelayStartsAtDeliveredExpiryNotAtTheOldNominalDeadline()
	{
		var clock = new Clock();
		Post( clock );
		clock.Now = 30000;
		clock.Hud.QueueChallengeTerminalTimers();
		clock.Now = 45000;
		clock.Hud.DispatchChallengeTerminalTimers();
		Assert.AreEqual( 45500L, State( clock ).ReleaseAt );
		clock.Pump( 45499 );
		Assert.AreEqual( 1, clock.Hud.PendingMessages.Count );
		clock.Pump( 45500 );
		Assert.AreEqual( 0, clock.Hud.PendingMessages.Count );
	}

	[TestMethod]
	public void DuplicateTagsKeepIndependentSelectionDeletionAndExpiry()
	{
		var clock = new Clock();
		Post( clock );
		clock.Now = 1000;
		Post( clock );
		Assert.AreEqual( 2, clock.Hud.PendingMessages.Count );
		clock.Hud.OpenPendingMessage( 1 );
		Assert.IsTrue( State( clock, 0 ).Active );
		Assert.IsFalse( State( clock, 1 ).Active );
		clock.Hud.DeleteOpenedMessage();
		Assert.IsNull( clock.Hud.OpenedMessage );
		Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ) );
		Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 1 ) );
		clock.Pump( 1500 );
		Assert.AreEqual( 1, clock.Hud.PendingMessages.Count );
		clock.Pump( 30000 );
		Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 0 ) );
		clock.Pump( 30500 );
		Assert.AreEqual( 0, clock.Hud.PendingMessages.Count );
	}

	[TestMethod]
	public void ExpiringOneDuplicateDoesNotCloseOrRemoveTheReadDuplicate()
	{
		var clock = new Clock();
		Post( clock );
		Post( clock );
		clock.Hud.OpenPendingMessage( 1 );
		clock.Pump( 30000 );
		Assert.AreEqual( 2, clock.Hud.PendingMessages.Count );
		Assert.AreEqual( "synthetic success 137", clock.Hud.OpenedMessage );
		clock.Pump( 30500 );
		Assert.AreEqual( 1, clock.Hud.PendingMessages.Count );
		Assert.AreEqual( "synthetic success 137", clock.Hud.OpenedMessage );
		Assert.IsTrue( clock.Hud.OpenPendingMessage( 0 ) );
		Assert.IsNull( clock.Hud.OpenedMessage, "selection follows its instance through compaction" );
	}

	[TestMethod]
	public void RightClickSelectedTerminalClosesNowButRetainsOccupancyUntilRelease()
	{
		var clock = new Clock();
		Post( clock );
		clock.Hud.OpenPendingMessage( 0 );
		clock.Now = 100;
		Assert.IsTrue( clock.Hud.DeletePendingMessage( 0 ) );
		Assert.IsNull( clock.Hud.OpenedMessage );
		Assert.AreEqual( 1, clock.Hud.PendingMessages.Count );
		Assert.AreEqual( 600L, State( clock ).ReleaseAt );
		clock.Pump( 599 );
		Assert.AreEqual( 1, clock.Hud.PendingMessages.Count );
		clock.Pump( 600 );
		Assert.AreEqual( 0, clock.Hud.PendingMessages.Count );
	}

	[TestMethod]
	public void FullTubeRefusesNewTerminalsWithoutEvictionTimerOrSound()
	{
		using var audio = new AudioTriggerProbe();
		var clock = new Clock();
		for ( var i = 0; i < ParkHud.MessageTubeCapacity; i++ ) clock.Hud.PostGameMessage( "ordinary-" + i );
		Assert.IsTrue( ChallengeMessageRecords.TryGet( 1, ChallengePresentationKind.Success, out var record ) );
		Assert.IsFalse( clock.Hud.PostChallengeTerminal( record, "refused terminal" ) );
		Post( clock ); // Recognized transition remains true even when admission fails.
		Assert.AreEqual( "ordinary-0", clock.Hud.PendingMessages[0] );
		Assert.AreEqual( ParkHud.MessageTubeCapacity, audio.Events( "cat_ui" ).Count( id => id == ParkHud.MessagePostedEvent ) );
		clock.Pump( 100000 );
		Assert.AreEqual( ParkHud.MessageTubeCapacity, clock.Hud.PendingMessages.Count );
		Assert.IsNull( clock.Hud.ChallengeTerminalState( 0 ) );
		Assert.IsNull( clock.Hud.ChallengeTerminalState( 9 ), "refusal must not attach terminal metadata to the final ordinary row" );
	}

	[TestMethod]
	public void CrossTagReplacementReleasesSynchronouslyBeforeFullTubeInsertion()
	{
		using var audio = new AudioTriggerProbe();
		var clock = new Clock();
		clock.Hud.PostChallengeProgress( 280, "old progress" );
		for ( var i = 1; i < ParkHud.MessageTubeCapacity; i++ ) clock.Hud.PostGameMessage( "ordinary-" + i );
		clock.Hud.OpenPendingMessage( 0 );
		Post( clock );
		Assert.IsNull( clock.Hud.OpenedMessage );
		Assert.AreEqual( ParkHud.MessageTubeCapacity, clock.Hud.PendingMessages.Count );
		Assert.AreEqual( "ordinary-1", clock.Hud.PendingMessages[0] );
		Assert.AreEqual( "synthetic success 137", clock.Hud.PendingMessages[^1] );
		Assert.AreEqual( ParkHud.MessageTubeCapacity + 1, audio.Events( "cat_ui" ).Count( id => id == ParkHud.MessagePostedEvent ) );
		Assert.IsTrue( State( clock, ParkHud.MessageTubeCapacity - 1 ).Active );
	}

	[TestMethod]
	public void HiddenTerminalOccupancyRefusesReinsertionAndReplacementCannotFreeAnInvisibleMatch()
	{
		var clock = new Clock();
		Post( clock );
		for ( var i = 1; i < ParkHud.MessageTubeCapacity; i++ ) clock.Hud.PostGameMessage( "ordinary-" + i );
		clock.Hud.DeletePendingMessage( 0 );
		clock.Hud.RemoveChallengeProgress( 313 );
		Assert.AreEqual( ParkHud.MessageTubeCapacity, clock.Hud.PendingMessages.Count, "replacement has a visibility guard" );
		Assert.IsFalse( clock.Hud.PostChallengeProgress( 280, "new progress" ) );
		Post( clock );
		Assert.AreEqual( ParkHud.MessageTubeCapacity, clock.Hud.PendingMessages.Count );
		clock.Pump( 500 );
		Assert.IsTrue( clock.Hud.PostChallengeProgress( 280, "new progress" ) );
	}

	[TestMethod]
	public void MissingOrUnsupportedLocalizationStillAppliesOnlyTheExactReplacementAndNeverInventsText()
	{
		using var audio = new AudioTriggerProbe();
		foreach ( var kind in new[] { ChallengePresentationKind.Success, ChallengePresentationKind.Failure } )
		{
			var clock = new Clock();
			clock.Hud.PostChallengeProgress( 280, "remove" );
			clock.Hud.PostChallengeProgress( 281, "keep" );
			Assert.IsTrue( clock.Hud.ApplyChallengePresentation( Message( kind: kind ), null, _ => null, _ => null ) );
			CollectionAssert.AreEqual( new[] { "keep" }, clock.Hud.PendingMessages.ToArray() );
			var table = Templates();
			Assert.IsTrue( ChallengeMessageRecords.TryGet( 1, kind, out var record ) );
			table.Templates[record.Entry] = new StringTemplate( new[] { new StringTemplatePart( "", 7 ) } );
			Assert.IsTrue( clock.Hud.ApplyChallengePresentation( Message( kind: kind ), table, _ => "not supplied", _ => null ) );
			Assert.AreEqual( 1, clock.Hud.PendingMessages.Count );
			clock.Pump( 100000 );
			Assert.AreEqual( 1, clock.Hud.PendingMessages.Count );
		}
		Assert.AreEqual( 4, audio.Events( "cat_ui" ).Count( id => id == ParkHud.MessagePostedEvent ) );
	}

	[TestMethod]
	public void UnknownTypesAndKindsCannotRemoveOrInsertAnything()
	{
		var clock = new Clock();
		clock.Hud.PostChallengeProgress( 280, "keep" );
		foreach ( var type in new[] { 0, -1, 34, 383 } )
			Assert.IsFalse( clock.Hud.ApplyChallengePresentation( Message( type ), Templates(), _ => null, _ => null ) );
		Assert.IsFalse( clock.Hud.ApplyChallengePresentation( Message() with { Kind = (ChallengePresentationKind)4 }, Templates(), _ => null, _ => null ) );
		CollectionAssert.AreEqual( new[] { "keep" }, clock.Hud.PendingMessages.ToArray() );
	}

	[TestMethod]
	public void TerminalFormattingIsBoundedAndSupportsParameterFreeLocalizedTemplates()
	{
		var clock = new Clock();
		var table = Templates();
		ChallengeMessageRecords.TryGet( 1, ChallengePresentationKind.Success, out var record );
		table.Templates[record.Entry] = new StringTemplate( new[] { new StringTemplatePart( new string( 'x', 254 ) + "\U0001f642end", -1 ) } );
		clock.Hud.ApplyChallengePresentation( Message(), table, _ => null, _ => null );
		Assert.AreEqual( new string( 'x', 254 ), clock.Hud.PendingMessages.Single(), "no split UTF-16 surrogate" );
	}

	[TestMethod]
	public void ProgressAndExistingOffersNeverAcquireTerminalTimersOrAutoDecline()
	{
		var clock = new Clock();
		var park = EconomyTestData.Park(); park.OpenPark(); park.AdvanceDays( 541 );
		clock.Hud.PostChallengeProgress( 280, "progress" );
		Assert.IsTrue( clock.Hud.KeepChallengeOfferPosted( park ) );
		Post( clock, type: 2 );
		clock.Pump( 100000 );
		clock.Pump( 100500 );
		Assert.AreEqual( 2, clock.Hud.PendingMessages.Count );
		Assert.IsNotNull( park.Objectives.Current );
		Assert.IsFalse( park.Objectives.Current!.Accepted );
		clock.Hud.OpenPendingMessage( 1 );
		CollectionAssert.AreEqual( new[] { GameMessageKind.Accept, GameMessageKind.Decline }, clock.Hud.OpenedActions.Select( action => action.Kind ).ToArray() );
		clock.Hud.CloseMessagePanel();
		Assert.IsTrue( clock.Hud.KeepChallengeOfferPosted( park ), "the existing repost extension remains intact" );
	}

	[TestMethod]
	public void TwoQueuedExpiryPumpsDoNotDuplicateRemovalOrAffectANewRowAfterRelease()
	{
		var clock = new Clock();
		Post( clock );
		clock.Now = 30000;
		clock.Hud.QueueChallengeTerminalTimers();
		clock.Hud.QueueChallengeTerminalTimers();
		clock.Hud.DispatchChallengeTerminalTimers();
		clock.Pump( 30500 );
		Post( clock );
		clock.Hud.DispatchChallengeTerminalTimers();
		Assert.IsTrue( State( clock ).Active );
		Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ) );
		clock.Pump( 60499 );
		Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ) );
		clock.Pump( 60500 );
		Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 0 ) );
	}

	[TestMethod]
	public void NewHudOnLoadHasNoOldRowsQueuedEventsOrReadSuspensionState()
	{
		var old = new Clock();
		Post( old );
		old.Now = 30000;
		old.Hud.QueueChallengeTerminalTimers();
		old.Hud.SetChallengeMessageTimersSuspended( true );
		var loaded = new Clock { Now = 30000 };
		Assert.AreEqual( 0, loaded.Hud.PendingMessages.Count, "tube state is not persisted (UI-025)" );
		Post( loaded );
		old.Hud.DispatchChallengeTerminalTimers();
		old.Pump( 30500 );
		loaded.Pump( 59999 );
		Assert.IsTrue( loaded.Hud.IsPendingMessageVisible( 0 ) );
		loaded.Pump( 60000 );
		Assert.IsFalse( loaded.Hud.IsPendingMessageVisible( 0 ) );
	}

	[TestMethod]
	public void NamedSuspensionMappingExcludesPostcardDirectLoadSaveAndUnprovenDialogs()
	{
		foreach ( var screen in new[] { ParkHud.PauseMenuScreen, ParkHud.MapScreenName, "options", "confirmDelete", "confirmDeleteAttraction", "confirmDismissStaff" } )
			Assert.IsTrue( ParkHud.ChallengeMessageSuspensionFor( false, new[] { screen } ), screen );
		foreach ( var screen in new[] { ParkHud.PostcardScreen, "load", "save", "loans", "confirmQuit", "texturePackDownload", "openTpwOptions", "buy", "staff", "ride" } )
			Assert.IsFalse( ParkHud.ChallengeMessageSuspensionFor( false, new[] { screen } ), screen );
		Assert.IsTrue( ParkHud.ChallengeMessageSuspensionFor( false, new[] { "pause", "load" } ), "load inherits Escape" );
		Assert.IsTrue( ParkHud.ChallengeMessageSuspensionFor( true, Array.Empty<string>() ), "explicit user pause" );
		Assert.IsFalse( ParkHud.ChallengeMessageSuspensionFor( false, Array.Empty<string>() ) );
	}

	[TestMethod]
	public void ExplicitUserSpeedControlsSuspendAndResumeButRunningSpeedChangesDoNotResetTime()
	{
		var clock = new Clock();
		var status = new StubParkStatus( 0 );
		typeof( ParkHud ).GetField( "<Status>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( clock.Hud, status );
		Post( clock );
		clock.Now = 10000;
		clock.Hud.SetUserGameSpeed( GameSpeed.Fast );
		clock.Now = 20000;
		clock.Hud.SetUserGameSpeed( GameSpeed.Normal );
		clock.Pump( 25000 );
		Assert.IsTrue( State( clock ).PreExpiryReached );
		clock.Hud.SetUserGameSpeed( GameSpeed.Paused );
		clock.Pump( 100000 );
		Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ) );
		clock.Hud.SetUserGameSpeed( GameSpeed.Normal );
		clock.Pump( 130000 );
		Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 0 ) );
	}

	[TestMethod]
	public void FollowupOfferPrecedesItsOwnSuccessAndSuppressedSuccessLeavesOldProgressAlone()
	{
		foreach ( var nextFollowup in new[] { 0, 13 } )
		{
			var settings = EconomyTestData.Settings();
			settings.AdoptChallenges( new[]
			{
				new ChallengeDefinition( 0, 11, 12, 10, 1000, 0, 1100, 0, 111, false, true ),
				new ChallengeDefinition( 0, 12, nextFollowup, 10, 1000, 0, 1100, 0, 222, false, true )
			} );
			var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
			var start = 540 * ParkCalendar.SecondsPerDay;
			park.Objectives.Update( start, 1, _ => 0 ).ToList();
			park.Objectives.AcceptAt( start, 0 );
			var clock = new Clock();
			clock.Hud.PostChallengeProgress( 293, "old part progress" );
			clock.Hud.PostChallengeProgress( 297, "follow-up progress" );
			var emitted = new List<ChallengePresentation>();
			var templates = Templates();
			park.Objectives.PresentationPosted += message =>
			{
				emitted.Add( message );
				clock.Hud.ApplyChallengePresentation( message, templates, _ => null, _ => null );
			};
			var gameplay = park.Objectives.Update( start + ParkCalendar.SecondsPerDay, 3, _ => 1000 ).ToArray();
			CollectionAssert.AreEqual( new[] { ParkEventKind.ChallengeCompleted, ParkEventKind.ChallengeOffered }, gameplay.Select( item => item.Kind ).ToArray() );
			Assert.AreEqual( "old part progress", clock.Hud.PendingMessages[0] );
			Assert.AreEqual( ChallengePresentationKind.Offer, emitted[0].Kind );
			if ( nextFollowup == 0 )
			{
				CollectionAssert.AreEqual( new[] { "old part progress", emitted[0].OfferText, "synthetic success 222" }, clock.Hud.PendingMessages.ToArray() );
				Assert.AreEqual( 12, emitted[1].Type );
				Assert.IsTrue( State( clock, 2 ).Active );
			}
			else
			{
				Assert.AreEqual( 1, emitted.Count );
				Assert.AreEqual( "follow-up progress", clock.Hud.PendingMessages[1] );
				Assert.AreEqual( 3, clock.Hud.PendingMessages.Count );
			}
		}
	}

	[TestMethod]
	public void ReadOnlyVisitStillRejectsOfferAnswersAndTerminalRemovalCannotMutateGameplay()
	{
		var clock = new Clock();
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		typeof( Level ).GetField( "<Visit>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( clock.Hud, level );
		typeof( ParkHud ).GetField( "messages", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( clock.Hud, new List<(string, float)>() );
		var park = EconomyTestData.Park(); park.OpenPark(); park.AdvanceDays( 541 );
		var current = park.Objectives.Current;
		var balance = park.Balance;
		Assert.IsFalse( clock.Hud.AnswerChallenge( park, true ) );
		Assert.IsFalse( clock.Hud.AnswerChallenge( park, false ) );
		Post( clock );
		clock.Hud.OpenPendingMessage( 0 );
		clock.Hud.DeleteOpenedMessage();
		clock.Pump( 500 );
		Assert.AreEqual( current, park.Objectives.Current );
		Assert.AreEqual( balance, park.Balance );
		Assert.AreEqual( 0, clock.Hud.PendingMessages.Count );
	}

	[TestMethod]
	public void HiddenSlotsCannotBeHitAndFollowingRowsCompactOnlyOnFinalRelease()
	{
		var clock = new Clock();
		Post( clock );
		Post( clock, type: 2 );
		var listType = typeof( ParkHud ).GetNestedType( "MessageTubeList", BindingFlags.NonPublic )!;
		var list = (IUiRowList)Activator.CreateInstance( listType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { clock.Hud }, null )!;
		var canvas = new UiCanvas( 2048, 1536 );
		var first = canvas.Map( ParkHud.TubeIcon( 0 ), UiAnchor.BottomLeft ).Center;
		var second = canvas.Map( ParkHud.TubeIcon( 1 ), UiAnchor.BottomLeft ).Center;
		Assert.AreEqual( 0, list.RowAt( canvas, first ) );
		clock.Hud.DeletePendingMessage( 0 );
		Assert.AreEqual( -1, list.RowAt( canvas, first ) );
		Assert.AreEqual( 1, list.RowAt( canvas, second ) );
		Assert.IsFalse( list.RowClicked( 0, true ) );
		clock.Pump( 500 );
		Assert.AreEqual( 0, list.RowAt( canvas, first ) );
		Assert.AreEqual( -1, list.RowAt( canvas, second ) );
	}

	[TestMethod]
	public void GenericRefusalKeepsHiddenTerminalOccupiedUntilReleaseThenAppendsReuse()
	{
		using var audio = new AudioTriggerProbe();
		var clock = new Clock();
		Post( clock );
		for ( var i = 1; i < 10; i++ ) Assert.IsTrue( clock.Hud.PostGameMessage( "ordinary-" + i ) );
		Assert.IsTrue( clock.Hud.DeletePendingMessage( 0 ) );
		var occupied = clock.Hud.PendingMessages.ToArray();
		Assert.IsFalse( clock.Hud.PostGameMessage( "latest ordinary" ) );
		clock.Pump( 499 );
		Assert.IsFalse( clock.Hud.PostGameMessage( "still full" ) );
		CollectionAssert.AreEqual( occupied, clock.Hud.PendingMessages.ToArray() );
		Assert.AreEqual( 10, audio.Events( "cat_ui" ).Count( id => id == ParkHud.MessagePostedEvent ) );
		clock.Pump( 500 );
		Assert.AreEqual( 9, clock.Hud.PendingMessages.Count );
		Assert.IsTrue( clock.Hud.PostGameMessage( "latest ordinary" ) );
		CollectionAssert.AreEqual( Enumerable.Range( 1, 9 ).Select( i => "ordinary-" + i ).Concat( new[] { "latest ordinary" } ).ToArray(), clock.Hud.PendingMessages.ToArray() );
		clock.Pump( 1000 );
		Assert.AreEqual( 10, clock.Hud.PendingMessages.Count, "an old release never removes a reused slot" );
		Assert.AreEqual( 11, audio.Events( "cat_ui" ).Count( id => id == ParkHud.MessagePostedEvent ) );
	}

	[TestMethod]
	public void LongMonotonicClockHasNoClaimOfOriginalThirtyTwoBitWrapBehavior()
	{
		var clock = new Clock { Now = uint.MaxValue - 10000L };
		Post( clock );
		clock.Pump( uint.MaxValue + 19999L );
		Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ) );
		clock.Pump( uint.MaxValue + 20000L );
		Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 0 ) );
		clock.Pump( uint.MaxValue + 20500L );
		Assert.AreEqual( 0, clock.Hud.PendingMessages.Count );
	}
}
