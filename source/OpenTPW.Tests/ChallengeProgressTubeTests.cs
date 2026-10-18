using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class ChallengeProgressTubeTests
{
	private static ParkHud Hud() => (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
	private static ChallengePresentation Message( int type = 1, ChallengePresentationKind kind = ChallengePresentationKind.Progress ) =>
		new( kind, type, 100, 10, 20, 3, 4, 1180, 1100, 0, "synthetic offer" );

	private static StringFile Templates()
	{
		var table = new StringFile( new MemoryStream( StringTableTests.CreateStringTable( Enumerable.Range( 0, 332 ).Select( _ => Array.Empty<byte>() ).ToArray() ) ),
			new BFMUReader( new MemoryStream( StringTableTests.CreateCharacterTable( "x" ) ) ) );
		for ( var type = 1; type <= 33; type++ )
		{
			Assert.IsTrue( ChallengeMessageRecords.TryGet( type, ChallengePresentationKind.Progress, out var record ) );
			table.Templates[record.Entry] = new StringTemplate( new[] { new StringTemplatePart( "progress-", -1 ), new StringTemplatePart( "", 3 ) } );
		}
		return table;
	}

	[TestMethod]
	public void PendingOpenAndClosedProgressRefreshInPlaceAndDeleteReinsertsOnce()
	{
		using var probe = new AudioTriggerProbe();
		var hud = Hud();
		hud.PostGameMessage( "ordinary" );
		Assert.IsTrue( hud.PostChallengeProgress( 280, "first" ) );
		Assert.IsTrue( hud.PostChallengeProgress( 280, "second" ) );
		CollectionAssert.AreEqual( new[] { "ordinary", "second" }, hud.PendingMessages.ToArray() );
		Assert.IsTrue( hud.OpenPendingMessage( 1 ) );
		Assert.AreEqual( 2, hud.PendingMessages.Count );
		Assert.AreEqual( 0, hud.OpenedActions.Count );
		hud.PostChallengeProgress( 280, "third" );
		Assert.AreEqual( "third", hud.OpenedMessage );
		Assert.AreEqual( 0, hud.OpenedActions.Count );
		Assert.AreEqual( 2, probe.Events( "cat_ui" ).Count( id => id == ParkHud.MessagePostedEvent ) );
		hud.CloseMessagePanel();
		Assert.IsNull( hud.OpenedMessage );
		Assert.AreEqual( 2, hud.PendingMessages.Count, "close retains the row" );
		hud.PostChallengeProgress( 280, "closed refresh" );
		hud.OpenPendingMessage( 1 );
		Assert.AreEqual( "closed refresh", hud.OpenedMessage );
		hud.OpenPendingMessage( 1 );
		Assert.IsNull( hud.OpenedMessage, "clicking the selected icon closes the panel" );
		hud.OpenPendingMessage( 1 );
		hud.DeleteOpenedMessage();
		Assert.IsNull( hud.OpenedMessage );
		CollectionAssert.AreEqual( new[] { "ordinary" }, hud.PendingMessages.ToArray() );
		hud.PostChallengeProgress( 280, "reinserted" );
		hud.PostChallengeProgress( 280, "same row" );
		Assert.AreEqual( 3, probe.Events( "cat_ui" ).Count( id => id == ParkHud.MessagePostedEvent ) );
		Assert.IsTrue( hud.DeletePendingMessage( 1 ) );
		Assert.AreEqual( 1, hud.PendingMessages.Count );
	}

	[TestMethod]
	public void FullTubeRefreshSucceedsButNewProgressDoesNotEvictOrPlaySound()
	{
		using var probe = new AudioTriggerProbe();
		var hud = Hud();
		hud.PostChallengeProgress( 280, "progress" );
		for ( var i = 1; i < ParkHud.MessageTubeCapacity; i++ )
			hud.PostGameMessage( "ordinary-" + i );
		hud.OpenPendingMessage( 0 );
		Assert.IsTrue( hud.PostChallengeProgress( 280, "updated" ) );
		Assert.AreEqual( "updated", hud.OpenedMessage );
		Assert.IsFalse( hud.PostChallengeProgress( 281, "new type" ) );
		Assert.AreEqual( ParkHud.MessageTubeCapacity, hud.PendingMessages.Count );
		Assert.AreEqual( "updated", hud.PendingMessages[0] );
		Assert.AreEqual( ParkHud.MessageTubeCapacity, probe.Events( "cat_ui" ).Count( id => id == ParkHud.MessagePostedEvent ) );
		// Generic messages preserve their existing oldest-eviction policy, including selected-row cleanup.
		hud.PostGameMessage( "new ordinary" );
		Assert.IsNull( hud.OpenedMessage );
		Assert.AreEqual( "ordinary-1", hud.PendingMessages[0] );
	}

	[TestMethod]
	public void TagAndBothContextFieldsOwnTheRowAndSelectedDeletionIsDistinctFromClose()
	{
		var hud = Hud();
		hud.PostChallengeProgress( 280, "default" );
		hud.PostChallengeProgress( 280, "short", contextShort: 1 );
		hud.PostChallengeProgress( 280, "value", contextInt: 2 );
		hud.PostChallengeProgress( 281, "other tag" );
		hud.PostChallengeProgress( 280, "changed" );
		CollectionAssert.AreEqual( new[] { "changed", "short", "value", "other tag" }, hud.PendingMessages.ToArray() );
		hud.OpenPendingMessage( 0 );
		hud.RemoveChallengeProgress( 280 );
		Assert.IsNull( hud.OpenedMessage );
		CollectionAssert.AreEqual( new[] { "short", "value", "other tag" }, hud.PendingMessages.ToArray() );
	}

	[TestMethod]
	public void All33ProgressTypesRefreshTheirTaggedIdentityUsingLocalizedTemplates()
	{
		var templates = Templates();
		for ( var type = 1; type <= 33; type++ )
		{
			var hud = Hud();
			Assert.IsTrue( hud.ApplyChallengePresentation( Message( type ), templates, _ => "object", _ => "staff" ) );
			hud.OpenPendingMessage( 0 );
			Assert.IsTrue( hud.ApplyChallengePresentation( Message( type ) with { Prize = 101 }, templates, _ => "object", _ => "staff" ) );
			Assert.AreEqual( "progress-101", hud.OpenedMessage, $"type {type}" );
			Assert.AreEqual( 1, hud.PendingMessages.Count );
		}
	}

	[TestMethod]
	public void ExactlyFourIncomingOffersRemoveTheirSpecifiedProgressTag()
	{
		foreach ( var (type, previousTag) in new[] { (19, 306), (20, 307), (31, 309), (22, 312) } )
		{
			var hud = Hud();
			hud.PostChallengeProgress( previousTag, "replaced" );
			hud.PostChallengeProgress( 280, "untouched" );
			hud.OpenPendingMessage( 0 );
			hud.ApplyChallengePresentation( Message( type, ChallengePresentationKind.Offer ), null, _ => null, _ => null );
			Assert.IsNull( hud.OpenedMessage );
			CollectionAssert.AreEqual( new[] { "untouched", "synthetic offer" }, hud.PendingMessages.ToArray() );
			hud.OpenPendingMessage( 1 );
			CollectionAssert.AreEqual( new[] { GameMessageKind.Accept, GameMessageKind.Decline }, hud.OpenedActions.Select( action => action.Kind ).ToArray() );
		}
		var normal = Hud();
		normal.PostChallengeProgress( 280, "same-type old progress" );
		normal.ApplyChallengePresentation( Message( 1, ChallengePresentationKind.Offer ), null, _ => null, _ => null );
		Assert.AreEqual( "same-type old progress", normal.PendingMessages[0], "ordinary offers have no replacement" );
	}

	[TestMethod]
	public void OfferRepostDoesNotRepeatIncomingReplacementEffects()
	{
		var settings = EconomyTestData.Settings();
		settings.AdoptChallenges( new[] { new ChallengeDefinition( 0, 19, 0, 10, 10, 1180, 0, 0, 100, false, true ) } );
		var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		park.Objectives.Update( 540 * ParkCalendar.SecondsPerDay, 1, _ => 0 ).ToList();
		var hud = Hud();
		hud.ApplyChallengePresentation( Message( 19, ChallengePresentationKind.Offer ), null, _ => null, _ => null );
		hud.DeletePendingMessage( 0 );
		hud.PostChallengeProgress( 306, "retained during repost" );
		Assert.IsTrue( hud.KeepChallengeOfferPosted( park ) );
		Assert.AreEqual( "retained during repost", hud.PendingMessages[0] );
		Assert.IsFalse( hud.KeepChallengeOfferPosted( park ) );
	}

	[TestMethod]
	public void ActualSuccessAndFailureRemoveOnlyTheirReplacementIdentityEvenWithoutText()
	{
		foreach ( var kind in new[] { ChallengePresentationKind.Success, ChallengePresentationKind.Failure } )
		{
			var hud = Hud();
			hud.PostChallengeProgress( 306, "old part" );
			hud.PostChallengeProgress( 308, "follow-up" );
			hud.OpenPendingMessage( 1 );
			Assert.IsTrue( hud.ApplyChallengePresentation( Message( 20, kind ), null, _ => null, _ => null ) );
			CollectionAssert.AreEqual( new[] { "old part" }, hud.PendingMessages.ToArray(), "success type20 must not delete type18 by completion-event inference" );
			Assert.IsNull( hud.OpenedMessage );
		}
	}

	[TestMethod]
	public void MissingTemplatesDoNotInventCaptionsAndRowsAreBounded()
	{
		var hud = Hud();
		Assert.IsFalse( hud.ApplyChallengePresentation( Message(), null, _ => null, _ => null ) );
		Assert.AreEqual( 0, hud.PendingMessages.Count );
		hud.PostChallengeProgress( 280, new string( 'x', 300 ) );
		Assert.AreEqual( 255, hud.PendingMessages.Single().Length );
		hud.PostChallengeProgress( 280, new string( 'y', 400 ) );
		Assert.AreEqual( new string( 'y', 255 ), hud.PendingMessages.Single() );
	}
}
