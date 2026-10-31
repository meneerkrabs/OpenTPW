using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class ApplicationPauseTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private ParkHud hud = null!;
	private Level level = null!;
	private UiContext context = null!;
	private Action restore = null!;
	private sealed class EmptyCatalog : IBuildCatalog
	{
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => Array.Empty<BuildItem>();
	}
	private static void Set( object target, string field, object? value ) => target.GetType().GetField( field, Private )!.SetValue( target, value );
	[TestInitialize]
	public void Initialize()
	{
		var oldCamera = Camera.Mode; var oldEdition = GameEditions.Current;
		var keyboard = Input.Keyboard; var down = Input.KeysDown; var last = Input.LastKeysDown; var text = Input.TextEntryActive;
		restore = () => { Camera.Mode = oldCamera; GameEditions.Select( oldEdition ); Input.Keyboard = keyboard; Input.UpdateButtons(); Input.KeysDown = down; Input.LastKeysDown = last; Input.TextEntryActive = text; };
		Input.Keyboard = new(); Input.KeysDown = new(); Input.LastKeysDown = new(); Input.TextEntryActive = false; Input.UpdateButtons();
		GameEditions.Select( GameEdition.ThemeParkWorld ); Camera.Mode = new ParkCameraMode();
		var fixture = VisitorWorldSelectionTests.Fixture( out _, out _ );
		level = (Level)typeof( ParkHud ).GetField( "level", Private )!.GetValue( fixture )!;
		hud = CreateHud();
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		context = new( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
			{ Canvas = new UiCanvas( 1280, 960 ), PopupHelp = false, ResolveTexture = _ => null };
	}
	[TestCleanup]
	public void Cleanup() => restore();
	private ParkHud CreateHud() => new( level, OriginalUiTests.FakeStrings(), new NoEconomyStatus { Speed = GameSpeed.Fast }, new EmptyCatalog(), new HudHost() );
	private void Observe( bool focused, bool offscreen = false ) => hud.ObserveApplicationFocus( focused, offscreen );
	private void LoseFocus() { Observe( true ); Observe( false ); }
	private void Key( UiKeys key ) => hud.Update( context, UiInput.Key( key ), false, false, false );
	private void Click( string id )
	{
		// Text menus measure their rows on update; use the laid-out bounds, not the initial fallback boxes.
		hud.Stack.Update( context, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
		var element = hud.Stack.Top!.Find( id ) ?? throw new AssertFailedException( "Missing control " + id );
		hud.Stack.Update( context, UiInput.Click( element.ScreenRect( context.Canvas ).Center ) );
	}
	private bool Selected( GameSpeed speed ) => ((UiButton)hud.Stack.Screens[0].Find( "speed" + speed )!).Selected!();

	[TestMethod]
	public void RefocusKeepsPauseAndActualPauseKeyRestoresStoredSpeed()
	{
		LoseFocus(); Assert.AreEqual( 0f, hud.SimulationTimeScale ); Assert.AreEqual( GameSpeed.Fast, hud.Status.Speed );
		Assert.IsTrue( Selected( GameSpeed.Paused ) ); Assert.IsFalse( Selected( GameSpeed.Fast ) );
		Observe( true ); Assert.AreEqual( 0f, hud.SimulationTimeScale );
		Key( UiKeys.Pause ); Assert.AreEqual( GameSpeed.Fast, hud.Status.Speed ); Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
		Assert.IsTrue( Selected( GameSpeed.Fast ) );
		Observe( true ); Observe( true ); Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
	}

	[TestMethod]
	public void TheBackdropHoldsTheFocusSoNoEscapeOrShortcutWorksUnderP()
	{
		Key( UiKeys.Pause ); Assert.AreEqual( GameSpeed.Paused, hud.Status.Speed );
		Key( UiKeys.Back );
		Assert.AreEqual( "hud", hud.Stack.Top!.Name, "Escape reaches only the P window's procedure (0x4A2680), which uses P alone" );
		Key( UiKeys.Pause ); Assert.AreEqual( GameSpeed.Normal, hud.Status.Speed, "P resumes" );
		Key( UiKeys.Back ); Assert.AreEqual( ParkHud.PauseMenuScreen, hud.Stack.Top!.Name, "without the backdrop Escape opens the menu" );
	}

	[TestMethod]
	public void ABoxThatPausedResumesTheGameWhenItClosesAndClearsAPPause()
	{
		Key( UiKeys.Pause ); Assert.AreEqual( GameSpeed.Paused, hud.Status.Speed );
		var box = new UiScreen( "confirmDelete" ); hud.Stack.Push( box ); Key( UiKeys.None );
		hud.Stack.Pop(); Key( UiKeys.None );
		Assert.AreEqual( GameSpeed.Normal, hud.Status.Speed, "0x47EED0 resumes (0x409300) when the box that paused is destroyed" );
		Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
		// a box under the Escape menu held no pause of its own: its close leaves the P pause alone
		Key( UiKeys.Pause ); hud.OpenPauseMenu(); var nested = new UiScreen( "confirmDelete" ); hud.Stack.Push( nested ); Key( UiKeys.None );
		hud.Stack.Pop(); Key( UiKeys.None );
		Assert.AreEqual( GameSpeed.Paused, hud.Status.Speed );
	}

	[TestMethod]
	public void TheCameraPanelsPostcardRefusalIsTheAlertQueuesPausingBox()
	{
		Assert.IsTrue( ParkHud.IsPausingMessageBox( ParkHud.AlertBoxScreen ) );
		Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 1f, new[] { "hud", ParkHud.AlertBoxScreen } ) );
	}

	[TestMethod]
	public void SpeedButtonExplicitlyResumesAndAppliesItsRequestedSpeed()
	{
		LoseFocus(); Observe( true ); Click( "speedNormal" );
		Assert.AreEqual( GameSpeed.Normal, hud.Status.Speed ); Assert.AreEqual( 1f, hud.SimulationTimeScale ); Assert.IsTrue( Selected( GameSpeed.Normal ) );
	}

	[TestMethod]
	public void ResumeMenuActionClearsFocusPauseButProgrammaticCloseDoesNot()
	{
		LoseFocus(); Observe( true ); hud.OpenPauseMenu(); hud.ClosePauseMenu(); Assert.AreEqual( 0f, hud.SimulationTimeScale );
		hud.OpenPauseMenu(); Click( UIStrings.ResumeGame.ToString() );
		Assert.AreEqual( GameSpeed.Fast, hud.Status.Speed ); Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
		LoseFocus(); Observe( true ); hud.OpenPauseMenu(); hud.Stack.Update( context, UiInput.Key( UiKeys.Back ) );
		Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
	}

	[TestMethod]
	public void UserPausedBeforeDeactivationStaysPausedAfterResumeMenu()
	{
		hud.SetUserGameSpeed( GameSpeed.Paused ); LoseFocus(); Observe( true ); hud.OpenPauseMenu(); Click( UIStrings.ResumeGame.ToString() );
		Assert.AreEqual( GameSpeed.Paused, hud.Status.Speed ); Assert.AreEqual( 0f, hud.SimulationTimeScale );
		Key( UiKeys.Pause ); Assert.AreEqual( GameSpeed.Normal, hud.Status.Speed );
	}

	[TestMethod]
	public void InitialUnfocusedAndOffscreenDoNotManufactureDeactivation()
	{
		Observe( false ); Observe( false ); Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
		Observe( true, offscreen: true ); Observe( false, offscreen: true ); Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
		Observe( false ); Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
		LoseFocus(); Observe( false ); Observe( false ); Assert.AreEqual( 0f, hud.SimulationTimeScale );
	}

	[TestMethod]
	public void ModalLossAddsNoLatchAndExistingModalStillOwnsPauseAfterSpeedChange()
	{
		Observe( true ); hud.Stack.Push( new UiScreen( "map" ) ); Observe( false ); Observe( true ); hud.Stack.Pop();
		Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
		LoseFocus(); hud.Stack.Push( new UiScreen( "map" ) ); hud.SetUserGameSpeed( GameSpeed.Fastest );
		Assert.AreEqual( 0f, hud.SimulationTimeScale ); hud.Stack.Pop(); Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
	}

	[TestMethod]
	public void EnteringAReadOnlyVisitClearsOnlyFocusLatch()
	{
		// a shared park is gated the same way (level.CoopRole != None, [EXT:online-coop-lockstep]); a session cannot be built asset-free here
		void Enter() => Set( level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		LoseFocus();
		Enter();
		Observe( false ); Assert.AreEqual( GameSpeed.Fast, hud.Status.Speed ); Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
		Observe( true ); Observe( false ); Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
		Set( level, "<Visit>k__BackingField", null ); Observe( false ); Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
		hud.SetUserGameSpeed( GameSpeed.Paused ); LoseFocus(); Enter(); Observe( true ); Assert.AreEqual( 0f, hud.SimulationTimeScale );
	}

	[TestMethod]
	public void ALoggedInPlayersOwnParkPausesOnDeactivationToo()
	{
		// 0x46B74C tests [0xF82884], which is 0 in every park: no session state is read here
		LoseFocus(); Assert.AreEqual( 0f, hud.SimulationTimeScale );
	}

	[TestMethod]
	public void ReplacementHudDoesNotInheritPreviousParksFocusLatch()
	{
		LoseFocus(); hud = CreateHud(); Observe( false ); Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
	}

	[TestMethod]
	public void FocusLossDoesNotBecomeExplicitUserChallengeTimerSuspension()
	{
		LoseFocus(); Assert.IsFalse( (bool)typeof( ParkHud ).GetField( "challengeUserPaused", Private )!.GetValue( hud )! );
		Assert.IsFalse( (bool)typeof( ParkHud ).GetField( "challengeTimersSuspended", Private )!.GetValue( hud )! );
	}

	// UI-096: the sound side of the pause follows the owners of the original's game pause (ParkHud.ObservePauseSound, GamePauseSound)
	private void WithMixer( Action<AudioMixer> test )
	{
		var old = AudioMixer.Current;
		using var mixer = new AudioMixer( null );
		AudioMixer.Current = mixer;
		GamePauseSound.Reset();
		try { test( mixer ); }
		finally { AudioMixer.Current = old; GamePauseSound.Reset(); }
	}

	[TestMethod]
	public void ThePKeyIsAGamePauseForTheSoundAndTheSpeedButtonsAreNot()
	{
		WithMixer( mixer =>
		{
			hud.ObservePauseSound();
			Assert.IsFalse( GamePauseSound.ListenerRaised );
			hud.SetUserGameSpeed( GameSpeed.Paused ); hud.ObservePauseSound();
			Assert.IsFalse( GamePauseSound.ListenerRaised, "the speed button's pause never calls 0x4092A0 or 0x409350" );
			hud.SetUserGameSpeed( GameSpeed.Normal ); hud.ObservePauseSound();
			Key( UiKeys.Pause ); hud.ObservePauseSound();
			Assert.IsTrue( GamePauseSound.ListenerRaised, "P is the pause object's toggle (0x409350 from 0x40BF77)" );
			Assert.IsFalse( mixer.Paused );
			Key( UiKeys.Pause ); hud.ObservePauseSound();
			Assert.IsFalse( GamePauseSound.ListenerRaised, "P again resumes" );
		} );
	}

	[TestMethod]
	public void ThePauseMenuPausesTheSoundOnceAndItsCloseResumesIt()
	{
		WithMixer( mixer =>
		{
			Key( UiKeys.Back ); hud.ObservePauseSound();
			Assert.AreEqual( ParkHud.PauseMenuScreen, hud.Stack.Top!.Name );
			Assert.IsTrue( GamePauseSound.ListenerRaised );
			hud.ObservePauseSound();
			Assert.IsTrue( GamePauseSound.ListenerRaised );
			Key( UiKeys.Back ); hud.ObservePauseSound();
			Assert.IsFalse( GamePauseSound.ListenerRaised );
		} );
	}

	[TestMethod]
	public void TheDeactivationPausesTheMixerAndTheReturnToTheGameResumesIt()
	{
		WithMixer( mixer =>
		{
			LoseFocus(); hud.ObservePauseSound();
			Assert.IsTrue( mixer.Paused, "0x46B74C: the pause 0x4092A0(1, 1) pauses the whole mixer" );
			Assert.IsFalse( GamePauseSound.ListenerRaised );
			Observe( true ); hud.ObservePauseSound();
			Assert.IsTrue( mixer.Paused, "the focus return is not a game resume" );
			Key( UiKeys.Pause ); hud.ObservePauseSound();
			Assert.IsFalse( mixer.Paused, "the resume answers the last pause: the mixer" );
			Assert.IsFalse( GamePauseSound.ListenerRaised );
		} );
	}

	[TestMethod]
	public void TheDeactivationFindsTheGateShutWhileAMenuHoldsTheGame()
	{
		WithMixer( mixer =>
		{
			Key( UiKeys.Back ); hud.ObservePauseSound();
			LoseFocus(); hud.ObservePauseSound();
			Assert.IsFalse( mixer.Paused, "the pause menu cleared the gate 0x3C: 0x4092A0 returns at once" );
			Assert.IsTrue( GamePauseSound.ListenerRaised );
		} );
	}

	[TestMethod]
	public void OnePKeyEndsAFocusLossPauseEvenOverAUserPause()
	{
		// [BIN:TPW-EXE:0x409350] the pause object has one flag: with it set, P resumes in one press (a focus loss after P is the same flag again)
		Key( UiKeys.Pause ); Assert.AreEqual( GameSpeed.Paused, hud.Status.Speed );
		LoseFocus(); Assert.IsTrue( hud.ClockPaused );
		Key( UiKeys.Pause );
		Assert.IsFalse( hud.ClockPaused, "one press resumes" );
		Assert.AreNotEqual( GameSpeed.Paused, hud.Status.Speed );
	}

	[TestMethod]
	public void AParkLeavesNoFrontEndFocusLatchBehind()
	{
		WithMixer( mixer =>
		{
			GamePauseSound.ObserveFrontEndFocus( true, false );
			GameAudio.LeavePark();
			GamePauseSound.ObserveFrontEndFocus( false, false );
			Assert.IsFalse( mixer.Paused, "the lobby's first sample after a park is not a loss" );
			GamePauseSound.ObserveFrontEndFocus( true, false );
			GamePauseSound.ObserveFrontEndFocus( false, false );
			Assert.IsTrue( mixer.Paused, "a real loss after a sampled return still pauses" );
		} );
	}

	[TestMethod]
	public void ResetSavedSoundPutsThePauseObjectBack()
	{
		WithMixer( mixer =>
		{
			GamePauseSound.Pause( false );
			Assert.IsTrue( GamePauseSound.ListenerRaised );
			GameAudio.ResetSavedSound();
			Assert.IsFalse( GamePauseSound.ListenerRaised );
			Assert.IsFalse( GamePauseSound.AdvisorSpeechPaused );
		} );
	}
}
