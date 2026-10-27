using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// ECON-030: the lost park's window and overlay (tp.exe 0x5168F0 -> 0x481B50 -> 0x48AC40, handler 0x48A740, layout 0x74FB20, banner table 0x74FC38, hide-HUD overlay 0x4A29D0, load restore 0x4815E0 -> 0x48ADB0).
/// Synthetic fixed models and a looping duck script exercise the real objects without original assets or a GPU.
/// </summary>
[TestClass, DoNotParallelize]
public class ParkLossWindowTests
{
	private string root = null!;
	private System.Collections.Generic.List<Entity> entities = null!;
	private Logger log = null!;
	private CameraMode camera = null!;
	private GameEdition edition;
	private BaseFileSystem files = null!;
	private Level currentLevel = null!;
	private bool text;
	private System.Collections.Generic.List<InputButton> down = null!, last = null!;
	private Key[] frameKeys = null!;
	private static System.Collections.Generic.HashSet<Key> FrameKeys => (System.Collections.Generic.HashSet<Key>)typeof( Input ).GetField( "frameKeys", BindingFlags.Static | BindingFlags.NonPublic )!.GetValue( null )!;
	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object owner, string field, object value ) => owner.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( owner, value );
	private static void Invoke( object owner, string method, params object[] arguments ) => owner.GetType().GetMethod( method, BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( owner, arguments );
	private sealed class EmptyCatalog : IBuildCatalog { public System.Collections.Generic.IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => Array.Empty<BuildItem>(); }

	[TestInitialize]
	public void Initialize()
	{
		entities = Entity.All; Entity.All = new(); log = Log; Log = new(); files = FileSystem;
		currentLevel = Level.Current; Level.Current = null!;
		camera = Camera.Mode; Camera.Mode = new ParkCameraMode(); edition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkWorld );
		text = Input.TextEntryActive; Input.TextEntryActive = false; down = Input.KeysDown; last = Input.LastKeysDown; Input.KeysDown = new(); Input.LastKeysDown = new();
		frameKeys = FrameKeys.ToArray(); FrameKeys.Clear();
		root = Directory.CreateTempSubdirectory( "opentpw-loss-" ).FullName;
		var model = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x42 ), 1 ); BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x44 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x268 ), 0 ); BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x78 ), 0x264 );
		foreach ( var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( offset ), 0 );
		File.WriteAllBytes( Path.Combine( root, "model.MD2" ), model );
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( Enum.GetNames<RideVariables>() ) );
		FileSystem = new BaseFileSystem( root );
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var entity in Entity.All.ToArray() ) entity.Delete();
		Entity.All = entities; Level.Current = currentLevel; Log = log; FileSystem = files; Camera.Mode = camera; GameEditions.Select( edition );
		Input.TextEntryActive = text; Input.KeysDown = down; Input.LastKeysDown = last;
		FrameKeys.Clear(); FrameKeys.UnionWith( frameKeys );
		Directory.Delete( root, true );
	}

	private ObjectCatalogEntry Gate() => new( "jungle", ObjectCategory.Feature, "/synthetic",
		new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1601\nInfo.Name Gates\nInfo.DontApplyOffset 1\n", "synthetic.sam" ) } ),
		new ObjectShape( new[] { "." } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), FileSystem, null );

	private Level LevelFor( bool withGate = true, ParkEconomy? economy = null )
	{
		var entries = withGate ? new[] { Gate() } : Array.Empty<ObjectCatalogEntry>();
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { "jungle", entries }, null )!;
		var objects = new ParkObjects( catalog, new SandboxParkGrid(), new WorldSeed( 123 ) );
		if ( withGate )
			objects.Add( entries[0], 0, 0, 0 );
		var level = Empty<Level>();
		var guests = new GuestSimulation( new GuestPathGrid( 32, 32 ), GuestTests.CreateSettings(), 1 ) { ArrivalsEnabled = false };
		Set( level, "<Guests>k__BackingField", guests );
		Set( level, "<Paths>k__BackingField", new ParkPathBuilder( guests.Grid.Cells, guests.Grid, (ParkEconomy?)null, new SandboxParkGrid() ) );
		Set( level, "<Objects>k__BackingField", objects );
		Set( level, "<CellTool>k__BackingField", new CellBuildTool() );
		Set( level, "<LastActionMessage>k__BackingField", "" );
		Set( level, "economyInstances", new System.Collections.Generic.Dictionary<OriginalObject, int>() );
		Set( level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( economy ?? EconomyTestData.Park(), ParkStartKind.FullSimulation ) );
		Invoke( level, "SetupParkGate" );
		return level;
	}

	private static UiContext Context( float time = 0 )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
		{ Canvas = new UiCanvas( 2048, 1536 ), Time = time, ResolveTexture = _ => null };
	}

	private static ParkHud Hud( Level level ) => new( level, OriginalUiTests.FakeStrings(), new EconomyParkStatus( () => level.Park!.Economy ), new EmptyCatalog(), new HudHost() );

	private static readonly N.Vector2 Nowhere = new( -1 );
	private static bool Frame( ParkHud hud, UiContext context, UiInput? input = null ) => hud.Update( context, input ?? UiInput.Idle( Nowhere ), false, false, false );
	private static void Lose( Level level ) => Invoke( level.Park!.Economy, "HandleBankruptcyMessage" );

	[TestMethod]
	public void TheBannerTableIsTheOriginalsTwentyFourSteps()
	{
		var table = UiWindowAnimation.LossBanner;
		Assert.AreEqual( 24, table.Count );
		Assert.AreEqual( 35, UiWindowAnimation.LossBannerInterval );
		for ( var index = 0; index < 9; index++ )
			Assert.AreEqual( index % 2 == 0 ? UiWindowAnimation.Step.Edges( -1, -1, 1, 1 ) : UiWindowAnimation.Step.Edges( 0, -1, 0, 1 ), table[index], $"step {index}" );
		for ( var index = 9; index < 18; index++ )
			Assert.AreEqual( UiWindowAnimation.Step.Edges( 0, 1, 0, -1 ), table[index], $"step {index}" );
		for ( var index = 18; index < 23; index++ )
			Assert.AreEqual( UiWindowAnimation.Step.Edges( 1, 0, -1, 0 ), table[index], $"step {index}" );
		Assert.AreEqual( UiWindowAnimation.Step.JumpTo( 0 ), table[23] );
		Assert.AreEqual( (0, 0, 0, 0), (table.Where( step => step.First != UiWindowAnimation.Jump ).Sum( step => (int)step.First ), table.Where( step => step.First != UiWindowAnimation.Jump ).Sum( step => (int)step.Second ),
			table.Where( step => step.First != UiWindowAnimation.Jump ).Sum( step => (int)step.Third ), table.Where( step => step.First != UiWindowAnimation.Jump ).Sum( step => (int)step.Fourth ) ), "the loop returns the banner to its rectangle" );
		var animation = new UiWindowAnimation( table, UiWindowAnimation.LossBannerInterval, 1000 );
		for ( var step = 0; step < 24; step++ )
			Assert.IsTrue( animation.Update( 1036 + 36 * step ), $"step {step} runs strictly after the interval" );
		Assert.AreEqual( 0, animation.Index, "the jump took its own step and went back to the start" );
		Assert.AreEqual( (0, 0, 0, 0), animation.Offset );
	}

	[TestMethod]
	public void TheLayoutIsTheOneBannerWindowOfTheTable()
	{
		var banner = OriginalLayoutTables.LossScreen.Root;
		Assert.AreEqual( new UiRect( 174, 500, 1700, 500 ), banner.Rect );
		Assert.AreEqual( 0x5Au, banner.Id );
		Assert.AreEqual( "s_bkrupt", banner.Model );
		Assert.AreEqual( 0x3u, banner.Attributes, "the disabled bit 2 is set: the banner takes no click" );
		Assert.AreEqual( 0, banner.Children.Count );
	}

	[TestMethod]
	public void TheBanksMessageOpensTheLossWindowOnTheNextUpdateAndTheClockKeepsRunning()
	{
		var level = LevelFor(); var hud = Hud( level ); var context = Context( 10 );
		Assert.IsFalse( Frame( hud, context ) );
		Assert.IsFalse( hud.LossWindowOpen );
		Lose( level );
		Assert.IsTrue( level.Park!.Economy.IsBankrupt );
		Assert.IsTrue( Frame( hud, context ), "the window owns the frame" );
		Assert.IsTrue( hud.LossWindowOpen ); Assert.IsFalse( hud.LossOverlayOpen );
		var screen = hud.Stack.Top!; Assert.AreEqual( ParkHud.LossWindowScreen, screen.Name );
		var banner = (UiAnimatedModel)screen.Elements.Single();
		Assert.AreEqual( "s_bkrupt", banner.Model );
		Assert.AreEqual( new UiRect( 174, 500, 1700, 500 ), banner.Bounds );
		Assert.AreEqual( new N.Vector2( 937, 500 ), banner.Pivot, "half the right and bottom edges (0x65F345)" );
		Assert.IsNotNull( banner.Animation ); Assert.AreEqual( 0, banner.Animation!.Index );
		Assert.IsTrue( ((ParkCameraMode)Camera.Mode).ModalOpen );
		Assert.IsNull( level.RideView, "the synthetic gate has no camera seat, so the camera stays (0x42A6EE)" );
		Assert.IsFalse( hud.Paused ); Assert.AreEqual( 1f, hud.SimulationTimeScale, "the window pauses nothing: the park's calendar and bank go on" );
		Assert.IsFalse( level.LossOverlayActive, "the window covers the HUD; the overlay is what hides it afterwards" );
	}

	[TestMethod]
	public void OnlyAnUnmodifiedEscapeAndALateRightClickLeaveTheWindow()
	{
		var level = LevelFor(); var hud = Hud( level ); var context = Context( 10 );
		Lose( level ); Frame( hud, context );
		var speed = hud.Status.Speed;
		Assert.IsTrue( Frame( hud, context, UiInput.Key( UiKeys.Pause ) ) );
		Assert.IsTrue( hud.LossWindowOpen ); Assert.AreEqual( speed, hud.Status.Speed, "Pause is not passed on" );
		foreach ( var modifier in new[] { Key.ControlLeft, Key.ShiftRight, Key.AltLeft } )
		{
			FrameKeys.Add( modifier ); Frame( hud, context, UiInput.Key( UiKeys.Back ) );
			Assert.IsTrue( hud.LossWindowOpen, $"{modifier}+Escape is not the handler's exit (0x48A8B2)" ); FrameKeys.Clear();
		}
		Assert.IsTrue( Frame( hud, context, UiInput.Click( new N.Vector2( 20, 20 ) ) ) );
		Assert.IsTrue( hud.LossWindowOpen, "a click does not leave it: the handler has no tick-button action" );
		var previous = GameOptions.Current.RmbCancel;
		try
		{
			GameOptions.Current.RmbCancel = true;
			Frame( hud, Context( 11.9f ), UiInput.Idle( Nowhere ) with { RightPressed = true } );
			Assert.IsTrue( hud.LossWindowOpen, "within 2000 ms of the window's creation the right click does nothing" );
			Frame( hud, Context( 12.5f ), UiInput.Idle( Nowhere ) with { RightPressed = true } );
			Assert.IsTrue( hud.LossOverlayOpen, "more than 2000 ms later it leaves" );
		}
		finally { GameOptions.Current.RmbCancel = previous; }
	}

	[TestMethod]
	public void ADeactivationOverTheLossWindowGivesTheFocusToThePWindow()
	{
		var level = LevelFor(); var hud = Hud( level ); var context = Context( 10 );
		Lose( level ); Frame( hud, context );
		var speed = hud.Status.Speed;
		hud.ObserveApplicationFocus( true, false ); hud.ObserveApplicationFocus( false, false );
		Assert.AreEqual( 0f, hud.SimulationTimeScale, "the deactivation pauses the park under the loss window" );
		var previous = GameOptions.Current.RmbCancel;
		try
		{
			GameOptions.Current.RmbCancel = true;
			Frame( hud, Context( 13 ), UiInput.Key( UiKeys.Back ) );
			Assert.IsTrue( hud.LossWindowOpen, "Escape reaches only the P window's procedure (0x4A2706), which ignores it" );
			Frame( hud, Context( 13 ), UiInput.Idle( Nowhere ) with { RightPressed = true } );
			Assert.IsTrue( hud.LossWindowOpen, "the backdrop owns the pointer" );
			Frame( hud, Context( 13 ), UiInput.Key( UiKeys.Pause ) );
			Assert.IsTrue( hud.LossWindowOpen ); Assert.AreEqual( speed, hud.Status.Speed ); Assert.AreEqual( hud.Status.TimeScale, hud.SimulationTimeScale, "P resumes and destroys the backdrop" );
			Frame( hud, Context( 13 ), UiInput.Key( UiKeys.Back ) );
			Assert.IsTrue( hud.LossOverlayOpen, "the window's own exits work again" );
		}
		finally { GameOptions.Current.RmbCancel = previous; }
	}

	[TestMethod]
	public void TheLossSceneIsNotThemeParkIncs()
	{
		var economy = new ParkEconomy( OriginalSaveImportTests.Settings(), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1, EconomyRules.ThemeParkIncMoney );
		var level = LevelFor( economy: economy ); var hud = Hud( level );
		Lose( level ); Frame( hud, Context( 10 ) );
		Assert.IsFalse( hud.LossWindowOpen || hud.LossOverlayOpen, "Theme Park Inc has no bankrupt banner" );
	}

	[TestMethod]
	public void TheRightClickLeavesOnlyWithRmbCancelOn()
	{
		var level = LevelFor(); var hud = Hud( level );
		Lose( level ); Frame( hud, Context( 10 ) );
		var previous = GameOptions.Current.RmbCancel;
		try
		{
			GameOptions.Current.RmbCancel = false;
			Frame( hud, Context( 20 ), UiInput.Idle( Nowhere ) with { RightPressed = true } );
			Assert.IsTrue( hud.LossWindowOpen, "RMBCancelOn clear (0x78D911)" );
		}
		finally { GameOptions.Current.RmbCancel = previous; }
	}

	[TestMethod]
	public void EscapeMovesTheBannerIntoTheHiddenHudOverlayAndTheCameraIsThePlayersAgain()
	{
		var level = LevelFor(); var hud = Hud( level ); var context = Context( 10 );
		Lose( level ); Frame( hud, context );
		Frame( hud, Context( 10.5f ), UiInput.Key( UiKeys.Back ) );
		Assert.IsTrue( hud.LossOverlayOpen ); Assert.IsFalse( hud.LossWindowOpen );
		Assert.IsTrue( level.LossOverlayActive ); Assert.IsTrue( level.HidesInterface, "the HUD is hidden" );
		var screen = hud.Stack.Top!; Assert.AreEqual( ParkHud.LossOverlayScreen, screen.Name );
		Assert.IsTrue( screen.HidesBelow, "the HUD below is not drawn" ); Assert.IsFalse( screen.Modal );
		var banner = (UiAnimatedModel)screen.Elements.Single();
		Assert.AreEqual( "s_bkrupt", banner.Model ); Assert.AreEqual( 0, banner.Animation!.Index, "the animation starts again (0x65E72D)" );
		Assert.IsFalse( hud.Paused ); Assert.AreEqual( 1f, hud.SimulationTimeScale );
		// the camera input of the overlay (0x4A287D): the pointer is not captured, the camera takes keys and mouse, and no click reaches the park or the HUD
		var park = (ParkCameraMode)Camera.Mode;
		Assert.IsFalse( Frame( hud, Context( 11 ), UiInput.Click( new N.Vector2( 500, 500 ) ) ), "the overlay leaves the pointer to the camera" );
		Assert.IsFalse( park.ModalOpen ); Assert.IsFalse( park.UiCapturesKeyboard );
		Assert.IsTrue( level.WorldPointerRouted, "the level's own click handling is switched off" );
	}

	[TestMethod]
	public void TheOverlaysBannerHasNoExitOfItsOwn()
	{
		var level = LevelFor(); var hud = Hud( level );
		Lose( level ); Frame( hud, Context( 10 ) ); Frame( hud, Context( 10.1f ), UiInput.Key( UiKeys.Back ) );
		Assert.IsTrue( hud.LossOverlayOpen );
		var previous = GameOptions.Current.RmbCancel;
		try
		{
			GameOptions.Current.RmbCancel = true;
			Frame( hud, Context( 60 ), UiInput.Idle( Nowhere ) with { RightPressed = true } );
			Assert.IsTrue( hud.LossOverlayOpen, "the pointer [0x7C24CC] was cleared by the destroyed window's message 0x14: nothing leaves the overlay" );
		}
		finally { GameOptions.Current.RmbCancel = previous; }
		Frame( hud, Context( 61 ), UiInput.Click( new N.Vector2( 20, 20 ) ) );
		Assert.IsTrue( hud.LossOverlayOpen );
	}

	[TestMethod]
	public void EscapeInTheOverlayOpensTheLostParksPauseMenuAndResumeReturnsToTheOverlay()
	{
		var level = LevelFor(); var hud = Hud( level );
		Lose( level ); Frame( hud, Context( 10 ) ); Frame( hud, Context( 10.1f ), UiInput.Key( UiKeys.Back ) );
		FrameKeys.Add( Key.ControlLeft ); Frame( hud, Context( 11 ), UiInput.Key( UiKeys.Back ) ); FrameKeys.Clear();
		Assert.AreEqual( ParkHud.LossOverlayScreen, hud.Stack.Top!.Name, "Ctrl+Escape is not the overlay's key (0x4A2938)" );
		Frame( hud, Context( 12 ), UiInput.Key( UiKeys.Back ) );
		Assert.AreEqual( ParkHud.PauseMenuScreen, hud.Stack.Top!.Name );
		Assert.IsTrue( hud.Paused ); Assert.AreEqual( 0f, hud.SimulationTimeScale, "the pause menu pauses the lost park as any" );
		Assert.IsTrue( level.Park!.Economy.IsBankrupt );
		Frame( hud, Context( 13 ) );
		Assert.AreEqual( ParkHud.PauseMenuScreen, hud.Stack.Top!.Name, "the menu stays" );
		hud.Stack.Pop();
		Assert.AreEqual( ParkHud.LossOverlayScreen, hud.Stack.Top!.Name ); Assert.IsTrue( hud.LossOverlayOpen );
		Assert.IsTrue( level.Park!.Economy.IsBankrupt, "Resume does not clear the lost state" );
	}

	[TestMethod]
	public void ALostParkFoundOnTheFirstFrameTakesTheOverlayWithoutTheWindowOrTheCamera()
	{
		var level = LevelFor(); Lose( level );
		var hud = Hud( level ); var context = Context( 10 );
		Frame( hud, context );
		Assert.IsTrue( hud.LossOverlayOpen ); Assert.IsFalse( hud.LossWindowOpen );
		Assert.IsNull( level.RideView ); Assert.IsInstanceOfType( Camera.Mode, typeof( ParkCameraMode ) );
		Assert.AreEqual( ParkHud.LossOverlayScreen, hud.Stack.Top!.Name ); Assert.IsTrue( level.HidesInterface );
		Assert.IsFalse( ((ParkCameraMode)Camera.Mode).ModalOpen );
	}

	[TestMethod]
	public void ASavedLostParkRestoresTheOverlayAndTheSaveHoldsNoNewField()
	{
		var economy = EconomyTestData.Park(); var level = LevelFor( economy: economy );
		Lose( level );
		var json = ParkSaveFile.Serialize( economy );
		Assert.IsTrue( json.Contains( "\"Bankrupt\": true" ), "the save carries the lost state in its existing field; the overlay is rebuilt from it, as the original rebuilds it from the world state" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ), economy.Settings, EconomyTestData.Catalog() );
		Assert.IsTrue( restored.IsBankrupt );
		var loaded = LevelFor( economy: restored ); var hud = Hud( loaded );
		Frame( hud, Context( 10 ) );
		Assert.IsTrue( hud.LossOverlayOpen ); Assert.IsFalse( hud.LossWindowOpen );
		var lived = EconomyTestData.Park();
		var older = ParkSaveFile.Serialize( lived );
		Assert.IsFalse( ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( older ) ), lived.Settings, EconomyTestData.Catalog() ).IsBankrupt, "a save of a park that is not lost loads as before" );
	}

	[TestMethod]
	public void AnotherParkLoadedIntoTheLevelClosesTheScreens()
	{
		var economy = EconomyTestData.Park(); var level = LevelFor( economy: economy );
		var path = Path.Combine( root, "alive.otpw" ); level.Park!.Save( path );
		var hud = Hud( level );
		Lose( level ); Frame( hud, Context( 10 ) ); Frame( hud, Context( 10.1f ), UiInput.Key( UiKeys.Back ) );
		Assert.IsTrue( hud.LossOverlayOpen );
		level.Park!.Load( path );
		Assert.IsFalse( level.Park.Economy.IsBankrupt );
		Frame( hud, Context( 11 ) );
		Assert.IsFalse( hud.LossOverlayOpen ); Assert.IsFalse( level.LossOverlayActive ); Assert.AreEqual( "hud", hud.Stack.Top!.Name );
	}

	[TestMethod]
	public void AVisitAndASharedParkKeepTheirHud()
	{
		var visit = LevelFor(); Set( visit, "<Visit>k__BackingField", Empty<ParkVisitInfo>() ); Lose( visit );
		var hud = Hud( visit ); Frame( hud, Context( 10 ) );
		Assert.IsFalse( hud.LossWindowOpen || hud.LossOverlayOpen, "a read-only visit runs no park" );
		Assert.AreEqual( "hud", hud.Stack.Top!.Name );
	}

	[TestMethod]
	public void TheWindowWinsNothingOverTheWinningScreen()
	{
		// the bank handler does not lose a park while a winning sequence shows (0x4D02D0): no Bankrupt event, so no window
		var level = LevelFor(); var economy = level.Park!.Economy;
		economy.WinningSequenceActive = () => true;
		Lose( level );
		Assert.IsFalse( economy.IsBankrupt );
		var hud = Hud( level ); Frame( hud, Context( 10 ) );
		Assert.IsFalse( hud.LossWindowOpen || hud.LossOverlayOpen );
	}

	[TestMethod]
	public void TheGateIsFoundByItsFixedItemNameAndWithoutASeatTheCameraStays()
	{
		var level = LevelFor();
		var gate = level.Objects.Objects.Single();
		Assert.IsTrue( Level.IsGateObject( gate ) );
		var before = Camera.Mode;
		Assert.IsFalse( level.EnterLossView(), "the synthetic gate model has no camera seat (type 0x1031, value 1)" );
		Assert.AreSame( before, Camera.Mode ); Assert.IsNull( level.RideView );
		var empty = LevelFor( withGate: false );
		Assert.IsFalse( empty.EnterLossView(), "a park without its gate leaves the camera alone" );
	}
}
