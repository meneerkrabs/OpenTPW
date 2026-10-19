using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The map/options silent stop and paused queue gate; no GPU, audio device or original assets.</summary>
[TestClass, DoNotParallelize]
public class AdvisorModalStopTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static void WithPark( AutomaticAdvisor advisor ) =>
		typeof( AutomaticAdvisor ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!
			.SetValue( advisor, RuntimeHelpers.GetUninitializedObject( typeof( Level ) ) );

	private static AdvisorController HelpController()
	{
		var balance = AdvisorSnapshotTests.Balance();
		return new AdvisorController( new AdvisorBalance
		{
			MinimumScore = balance.MinimumScore, MinimumTimeAnyMessage = balance.MinimumTimeAnyMessage,
			MinimumTimeSameMessage = balance.MinimumTimeSameMessage, Groups = balance.Groups,
			Scores = new Dictionary<string, int>( balance.Scores ) { [AdvisorTables.HelpScoreKey] = 1000 }
		} );
	}

	[DataTestMethod]
	[DataRow( "map" )]
	[DataRow( "options" )]
	public void StoppingAnOrdinaryResponsePreservesItsHistoryAndReservation( string window )
	{
		var controller = HelpController();
		controller.HandleGameEvent( AdvisorGameEvent.ParkNowClosed, 0, 0, true );
		controller.Update( () => 1000u, () => 0u, _ => 4000u );
		var before = controller.CaptureSnapshot().Queue;
		Assert.IsNotNull( before.CurrentMessageId );
		var speaking = true;
		var advisor = new AutomaticAdvisor( controller, () => 1000L, speechActive: () => speaking, endSpeech: () => speaking = false );
		WithPark( advisor );
		advisor.OnWindowOpened( window );
		var after = controller.CaptureSnapshot().Queue;
		Assert.AreEqual( before.CurrentMessageId, after.CurrentMessageId );
		Assert.AreEqual( before.LastActionStarted, after.LastActionStarted );
		Assert.AreEqual( before.LastActionDuration, after.LastActionDuration );
		Assert.AreEqual( before.History[before.CurrentMessageId.Value], after.History[before.CurrentMessageId.Value] );
	}

	[TestMethod]
	public void TheMapStopsBeforeWithdrawingToolHelpAndStillPostsItsOwnHelp()
	{
		var previous = GameOptions.Current.Tutorial;
		try
		{
			GameOptions.Current.Tutorial = true;
			var controller = HelpController();
			var tool = AdvisorUiHelp.ModeHelpMessages[0];
			Assert.IsTrue( controller.Advise( tool, 0, 0, true )!.Value.Stored );
			controller.Update( () => 1000u, () => 0u, _ => 4000u );
			Assert.AreEqual( tool, controller.Queue.CurrentMessageId );
			var speaking = true; var draws = 0; var slaps = 0;
			var advisor = new AutomaticAdvisor( controller, () => 1000L, speechActive: () => speaking,
				endSpeech: () => speaking = false, slapDraw: () => { draws++; return 0; }, playSlap: _ => slaps++ );
			WithPark( advisor );
			advisor.OnWindowOpened( "map" );
			var after = controller.CaptureSnapshot().Queue;
			Assert.IsFalse( speaking );
			Assert.AreEqual( 0, draws ); Assert.AreEqual( 0, slaps );
			Assert.AreEqual( 1u, after.History[tool].SlapCount, "the existing help-withdrawal history still applies" );
			Assert.AreEqual( 0u, after.LastActionDuration );
			Assert.IsTrue( after.Slots.Any( slot => slot?.MessageId == 304 ), "the map still posts its own help" );
		}
		finally { GameOptions.Current.Tutorial = previous; }
	}

	[DataTestMethod]
	[DataRow( "map", true )]
	[DataRow( "options", true )]
	[DataRow( "map", false )]
	[DataRow( "options", false )]
	public void OpeningAModalStopsEvenASilentAnimationWithoutASlap( string window, bool speaking )
	{
		var ended = 0; var draws = 0; var slaps = new List<uint>();
		var advisor = new AutomaticAdvisor( new AdvisorController( AdvisorSnapshotTests.Balance() ), () => 0L,
			speechActive: () => speaking, endSpeech: () => { ended++; speaking = false; },
			slapDraw: () => { draws++; return 0; }, playSlap: slaps.Add );
		WithPark( advisor );
		advisor.OnWindowOpened( window );
		Assert.AreEqual( 1, ended, "kill also clears the delayed start/model tail when the sound is inactive" );
		Assert.IsFalse( advisor.IsSpeechActive );
		Assert.AreEqual( 0, draws );
		Assert.AreEqual( 0, slaps.Count );
	}

	[DataTestMethod]
	[DataRow( "buy" )]
	[DataRow( "pause" )]
	[DataRow( "load" )]
	[DataRow( "save" )]
	public void OtherWindowsDoNotTakeTheModalStopPath( string window )
	{
		var ended = 0;
		var advisor = new AutomaticAdvisor( new AdvisorController( AdvisorSnapshotTests.Balance() ), () => 0L,
			speechActive: () => true, endSpeech: () => ended++ );
		WithPark( advisor );
		advisor.OnWindowOpened( window );
		Assert.AreEqual( 0, ended );
	}

	[DataTestMethod]
	[DataRow( "finance" )]
	[DataRow( "loans" )]
	[DataRow( "research" )]
	[DataRow( "parkInfo" )]
	public void AnOrdinaryWindowClockPauseDoesNotSuppressQueuedAdvice( string window )
	{
		Assert.AreEqual( 1f, Hud.ParkHud.TimeScaleFor( 1f, new[] { window } ) );
		var previous = GameOptions.Current.Advisor;
		try
		{
			// Silent playback exercises real queue consumption without opening an audio device.
			GameOptions.Current.Advisor = false;
			var controller = new AdvisorController( AdvisorSnapshotTests.Balance() );
			controller.HandleGameEvent( AdvisorGameEvent.ParkNowClosed, 0, 0, true );
			Assert.IsTrue( controller.CaptureSnapshot().Queue.Slots.Any( slot => slot != null ) );
			var advisor = new AutomaticAdvisor( controller, () => 0L );
			WithPark( advisor );
			// Hud.Paused is true for these windows, but their park simulation still advances.
			advisor.Update( gamePaused: true, parkPaused: false );
			Assert.IsFalse( controller.CaptureSnapshot().Queue.Slots.Any( slot => slot != null ) );
		}
		finally { GameOptions.Current.Advisor = previous; }
	}

	[TestMethod]
	public void ADetachedAdvisorDoesNotReceiveParkWindowStops()
	{
		var ended = 0;
		var advisor = new AutomaticAdvisor( new AdvisorController( AdvisorSnapshotTests.Balance() ), () => 0L, endSpeech: () => ended++ );
		advisor.OnWindowOpened( "map" );
		advisor.OnWindowOpened( "options" );
		Assert.AreEqual( 0, ended );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void APausedModalCannotConsumeQueuedAdvice( bool enabled )
	{
		var previous = GameOptions.Current.Advisor;
		try
		{
			GameOptions.Current.Advisor = enabled;
			var controller = new AdvisorController( AdvisorSnapshotTests.Balance() );
			controller.HandleGameEvent( AdvisorGameEvent.ParkNowClosed, 0, 0, true );
			var advisor = new AutomaticAdvisor( controller, () => 0L );
			WithPark( advisor );
			Assert.IsTrue( controller.CaptureSnapshot().Queue.Slots.Any( slot => slot != null ), "the queue must contain a response for this regression" );
			var before = ParkResumeJson.Serialize( controller.CaptureSnapshot() );
			advisor.Update( gamePaused: true, parkPaused: true );
			CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( controller.CaptureSnapshot() ), "no response/history/reservation change while the park turn is paused" );
		}
		finally { GameOptions.Current.Advisor = previous; }
	}
}
