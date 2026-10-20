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
	private void Observe( bool focused, bool offscreen = false, bool loggedIn = false ) => hud.ObserveApplicationFocus( focused, offscreen, loggedIn );
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

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void EnteringExcludedOnlineOrReadOnlyStateClearsOnlyFocusLatch( bool loggedIn )
	{
		LoseFocus();
		if ( !loggedIn ) Set( level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		Observe( false, loggedIn: loggedIn ); Assert.AreEqual( GameSpeed.Fast, hud.Status.Speed ); Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
		Observe( true, loggedIn: loggedIn ); Observe( false, loggedIn: loggedIn ); Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
		Set( level, "<Visit>k__BackingField", null ); Observe( false ); Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale );
		hud.SetUserGameSpeed( GameSpeed.Paused ); LoseFocus(); Observe( true, loggedIn: true ); Assert.AreEqual( 0f, hud.SimulationTimeScale );
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
}
