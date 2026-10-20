using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The real pause-menu builder and callbacks with synthetic park state, strings and load entries (no game assets).</summary>
[TestClass, DoNotParallelize]
public class LostParkPauseMenuTests
{
	private static readonly string[] LostEntries = { "Load", "RestartPark", "ResumeGame", "ExitToLobby", "QuitGame" };
	private static readonly string[] VisitEntries = { "ResumeGame", "ExitToLobby", "QuitGame" };
	private Logger previousLog = null!;
	private GameEdition previousEdition;
	private UiContext context = null!;

	[TestInitialize]
	public void Initialize()
	{
		previousLog = Log;
		Log = new();
		previousEdition = GameEditions.Current;
		GameEditions.Select( GameEdition.ThemeParkWorld );
		context = OriginalUiTests.FakeContext();
	}

	[TestCleanup]
	public void Cleanup()
	{
		Log = previousLog;
		GameEditions.Select( previousEdition );
	}

	private static void Set( object target, string field, object? value ) =>
		target.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private static void Lost( ParkEconomy economy, bool lost ) =>
		typeof( ParkEconomy ).GetProperty( nameof( ParkEconomy.IsBankrupt ) )!.SetValue( economy, lost );

	private static HudHost AllHooks( bool online = true ) => new()
	{
		CreateLoad = _ => new UiScreen( "load" ),
		CreateOptions = (_, _) => new UiScreen( "options" ),
		RestartPark = () => { },
		PublishPark = _ => { },
		GoOnline = online ? _ => { } : null
	};

	// The same CPU-only HUD seam as AutomaticCheckpointIngressTests: OpenPauseMenu, the actual UiMenuItems,
	// screen input dispatch and child dialogs are production code; only unrelated renderer/asset setup is omitted.
	private static (Level Level, ParkHud Hud) World( HudHost? host = null, bool lost = true, bool visit = false, bool economy = true )
	{
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		if ( economy )
		{
			var park = EconomyTestData.Park( initialCash: -123 );
			park.Speed = GameSpeed.Fast;
			Lost( park, lost );
			Set( level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( park, ParkStartKind.FullSimulation ) );
		}
		if ( visit )
			Set( level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		var hud = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		Set( hud, "level", level );
		Set( hud, "strings", OriginalUiTests.FakeStrings() );
		Set( hud, "host", host ?? AllHooks() );
		Set( hud, "<Status>k__BackingField", EconomyParkStatus.ForLevel( level ) );
		Set( hud, "<Stack>k__BackingField", new UiScreenStack() );
		var screen = new UiScreen( "hud" ) { Modal = false };
		Set( hud, "hud", screen );
		hud.Stack.Push( screen );
		return (level, hud);
	}

	private void Click( ParkHud hud, string id ) =>
		hud.Stack.Update( context, UiInput.Click( hud.Stack.Top!.Find( id )!.ScreenRect( context.Canvas ).Center ) );

	private static string[] Entries( ParkHud hud ) => hud.Stack.Top!.Elements.OfType<UiMenuItem>().Select( item => item.Id ).ToArray();

	private static void StillLost( Level level, GameSpeed speed = GameSpeed.Fast )
	{
		Assert.IsTrue( level.Park!.Economy.IsBankrupt );
		Assert.AreEqual( -123L, level.Park.Economy.Balance );
		Assert.AreEqual( speed, level.Park.Economy.Speed );
	}

	[DataTestMethod, DataRow( false ), DataRow( true )]
	public void BankruptParkBuildsExactlyTheFiveOrderedRowsWithExistingLabels( bool online )
	{
		var (level, hud) = World( AllHooks( online ) );
		hud.OpenPauseMenu();
		CollectionAssert.AreEqual( LostEntries, Entries( hud ) );
		foreach ( var excluded in new[] { "Save", "PublishPark", "Options", "GoOnline" } )
			Assert.IsNull( hud.Stack.Top!.Find( excluded ), excluded + " must be absent, not disabled" );
		var strings = OriginalUiTests.FakeStrings();
		foreach ( var item in hud.Stack.Top!.Elements.OfType<UiMenuItem>() )
		{
			Assert.AreEqual( strings[Enum.Parse<UIStrings>( item.Id )], item.Text() );
			Assert.IsTrue( item.Enabled );
		}
		Assert.AreEqual( "ResumeGame", hud.Stack.Top.Focused!.Id );
		Assert.IsNull( hud.Stack.Top.Find( "title" ) );
		StillLost( level );
	}

	[DataTestMethod, DataRow( false ), DataRow( true )]
	public void HealthyParkKeepsItsOrdinaryEntriesAndOptionalOnlineRow( bool online )
	{
		var (_, hud) = World( AllHooks( online ), lost: false );
		hud.OpenPauseMenu();
		var ordinary = new[] { "Load", "Save", "RestartPark", "PublishPark", "Options", "GoOnline", "ResumeGame", "ExitToLobby", "QuitGame" };
		CollectionAssert.AreEqual( ordinary.Where( id => online || id != "GoOnline" ).ToArray(), Entries( hud ) );
	}

	[DataTestMethod, DataRow( false ), DataRow( true )]
	public void VisitMenuTakesPrecedenceEvenWithASyntheticLostEconomy( bool lost )
	{
		var (_, hud) = World( lost: lost, visit: true );
		hud.OpenPauseMenu();
		CollectionAssert.AreEqual( VisitEntries, Entries( hud ) );
	}

	[DataTestMethod, DataRow( false, false ), DataRow( false, true ), DataRow( true, false ), DataRow( true, true )]
	public void MissingLoadAndRestartHooksDisableTheirLossRowsWithoutRemovingThem( bool canLoad, bool canRestart )
	{
		var loads = 0;
		var restarts = 0;
		var (_, hud) = World( new HudHost
		{
			CreateLoad = canLoad ? _ => { loads++; return new UiScreen( "load" ); } : null,
			RestartPark = canRestart ? () => restarts++ : null
		} );
		hud.OpenPauseMenu();
		var menu = hud.Stack.Top!;
		CollectionAssert.AreEqual( LostEntries, Entries( hud ) );
		Assert.AreEqual( canLoad, menu.Find( "Load" )!.Enabled );
		Assert.AreEqual( canRestart, menu.Find( "RestartPark" )!.Enabled );
		if ( !canLoad ) Click( hud, "Load" );
		if ( !canRestart ) Click( hud, "RestartPark" );
		Assert.AreSame( menu, hud.Stack.Top );
		Assert.AreEqual( 2, hud.Stack.Screens.Count );
		Assert.AreEqual( 0, loads );
		Assert.AreEqual( 0, restarts );
	}

	[DataTestMethod, DataRow( false ), DataRow( true )]
	public void MissingEconomyKeepsTheOrdinaryMenu( bool online )
	{
		var (_, hud) = World( AllHooks( online ), economy: false );
		hud.OpenPauseMenu();
		var ordinary = new[] { "Load", "Save", "RestartPark", "PublishPark", "Options", "GoOnline", "ResumeGame", "ExitToLobby", "QuitGame" };
		CollectionAssert.AreEqual( ordinary.Where( id => online || id != "GoOnline" ).ToArray(), Entries( hud ) );
	}

	[TestMethod]
	public void ReopeningReadsTheLiveLossStateAndAReplacedEconomy()
	{
		var (level, hud) = World( lost: false );
		hud.OpenPauseMenu();
		Assert.IsNotNull( hud.Stack.Top!.Find( "Save" ) );
		hud.ClosePauseMenu();
		Lost( level.Park!.Economy, true );
		hud.OpenPauseMenu();
		CollectionAssert.AreEqual( LostEntries, Entries( hud ) );
		hud.ClosePauseMenu();
		Set( level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation ) );
		hud.OpenPauseMenu();
		Assert.IsNotNull( hud.Stack.Top!.Find( "Save" ) );
		Assert.IsNotNull( hud.Stack.Top.Find( "Options" ) );
	}

	[TestMethod]
	public void OpeningAgainDoesNotDuplicateTheMenuOrItsChild()
	{
		var (level, hud) = World();
		hud.OpenPauseMenu();
		var menu = hud.Stack.Top;
		hud.OpenPauseMenu();
		Assert.AreSame( menu, hud.Stack.Top );
		Assert.AreEqual( 2, hud.Stack.Screens.Count );
		Click( hud, "RestartPark" );
		var child = hud.Stack.Top;
		hud.OpenPauseMenu();
		Assert.AreSame( child, hud.Stack.Top );
		Assert.AreEqual( 3, hud.Stack.Screens.Count );
		Assert.AreEqual( 1, hud.Stack.Screens.Count( screen => screen.Name == ParkHud.PauseMenuScreen ) );
		Assert.AreEqual( 0f, hud.SimulationTimeScale );
		StillLost( level );
	}

	[DataTestMethod]
	[DataRow( false, GameSpeed.Paused ), DataRow( false, GameSpeed.Normal ), DataRow( false, GameSpeed.Fast ), DataRow( false, GameSpeed.Fastest )]
	[DataRow( true, GameSpeed.Paused ), DataRow( true, GameSpeed.Normal ), DataRow( true, GameSpeed.Fast ), DataRow( true, GameSpeed.Fastest )]
	public void ResumeAndBackCloseTheMenuWithoutRevivingTheParkOrChangingSpeed( bool back, GameSpeed speed )
	{
		var (level, hud) = World();
		level.Park!.Economy.Speed = speed;
		hud.OpenPauseMenu();
		Assert.AreEqual( 0f, hud.SimulationTimeScale );
		if ( back ) hud.Stack.Update( context, UiInput.Key( UiKeys.Back ) );
		else Click( hud, "ResumeGame" );
		Assert.AreSame( hud.Screen, hud.Stack.Top );
		Assert.AreEqual( 1, hud.Stack.Screens.Count );
		Assert.IsFalse( hud.Paused );
		StillLost( level, speed );
		Assert.AreEqual( GameSpeedScale.Of( speed ), hud.SimulationTimeScale );
		hud.OpenPauseMenu();
		CollectionAssert.AreEqual( LostEntries, Entries( hud ) );
		Assert.AreEqual( 0f, hud.SimulationTimeScale );
	}

	[DataTestMethod, DataRow( false ), DataRow( true )]
	public void LossLoadUsesTheRealChooserAndCancellationReturnsToTheLostMenu( bool cancel )
	{
		var creates = 0;
		var loads = 0;
		var entry = new ParkLoadEntry( "synthetic", -1, false, "/synthetic/park.json", "Synthetic park" );
		var (level, hud) = World( new HudHost
		{
			CreateLoad = stack =>
			{
				creates++;
				return FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), new[] { entry }, selected => { Assert.AreSame( entry, selected ); loads++; } );
			}
		} );
		hud.OpenPauseMenu();
		var menu = hud.Stack.Top;
		Click( hud, "Load" );
		Assert.AreEqual( 1, creates );
		Assert.AreEqual( 3, hud.Stack.Screens.Count );
		Assert.IsNotNull( hud.Stack.Top!.Find( "parks" ) );
		Assert.AreEqual( 0f, hud.SimulationTimeScale );
		if ( cancel )
		{
			hud.Stack.Update( context, UiInput.Key( UiKeys.Back | UiKeys.Accept ) );
			Assert.AreEqual( 0, loads );
			Assert.AreSame( menu, hud.Stack.Top );
			CollectionAssert.AreEqual( LostEntries, Entries( hud ) );
			Assert.AreEqual( 2, hud.Stack.Screens.Count );
		}
		else
		{
			var point = hud.Stack.Top!.Find( "loadSelected" )!.ScreenRect( context.Canvas ).Center;
			hud.Stack.Update( context, UiInput.Click( point ) with { Pressed = UiKeys.Accept } );
			Assert.AreEqual( 1, loads, "combined click and Accept consume one chooser activation" );
		}
		Assert.AreEqual( 0f, hud.SimulationTimeScale );
		StillLost( level );
	}

	[DataTestMethod, DataRow( false ), DataRow( true )]
	public void RestartUsesTheExistingConfirmationAndOnlyTheHostCanReplaceTheLostPark( bool confirm )
	{
		var restarts = 0;
		var (level, hud) = World( new HudHost { RestartPark = () => restarts++ } );
		hud.OpenPauseMenu();
		var menu = hud.Stack.Top;
		Click( hud, "RestartPark" );
		Assert.AreEqual( "confirmRestart", hud.Stack.Top!.Name );
		Assert.AreEqual( OriginalUiTests.FakeStrings()[UIStrings.ConfirmRestartPark], ((UiLabel)hud.Stack.Top.Find( "message" )!).Text() );
		Assert.AreEqual( 0, restarts );
		Assert.AreEqual( 0f, hud.SimulationTimeScale );
		Click( hud, confirm ? "choice0" : "choice1" );
		Assert.AreEqual( confirm ? 1 : 0, restarts );
		Assert.AreSame( menu, hud.Stack.Top, "the host owns any world/menu replacement" );
		Assert.AreEqual( 2, hud.Stack.Screens.Count );
		Assert.AreEqual( 0f, hud.SimulationTimeScale );
		StillLost( level );
	}

	[DataTestMethod, DataRow( false ), DataRow( true )]
	public void QuitUsesTheExistingConfirmationAndCancellationDoesNotQuit( bool confirm )
	{
		var quits = 0;
		var (level, hud) = World( new HudHost { Quit = () => quits++ } );
		hud.OpenPauseMenu();
		var menu = hud.Stack.Top;
		Click( hud, "QuitGame" );
		Assert.AreEqual( "confirmQuit", hud.Stack.Top!.Name );
		Assert.AreEqual( OriginalUiTests.FakeStrings()[UIStrings.ConfirmQuit], ((UiLabel)hud.Stack.Top.Find( "message" )!).Text() );
		Assert.AreEqual( 0, quits );
		Assert.AreEqual( 0f, hud.SimulationTimeScale );
		Click( hud, confirm ? "choice0" : "choice1" );
		Assert.AreEqual( confirm ? 1 : 0, quits );
		if ( !confirm ) Assert.AreSame( menu, hud.Stack.Top );
		Assert.AreEqual( 0f, hud.SimulationTimeScale );
		StillLost( level );
	}

	[TestMethod]
	public void ExitToLobbyKeepsItsCurrentMenuOwnershipGuard()
	{
		var exits = 0;
		var (level, hud) = World( new HudHost { ExitToLobby = () => exits++ } );
		hud.OpenPauseMenu();
		var retired = (UiMenuItem)hud.Stack.Top!.Find( "ExitToLobby" )!;
		hud.ClosePauseMenu();
		hud.OpenPauseMenu();
		retired.Clicked();
		Assert.AreEqual( 0, exits );
		var current = (UiMenuItem)hud.Stack.Top!.Find( "ExitToLobby" )!;
		Click( hud, "QuitGame" );
		current.Clicked();
		Assert.AreEqual( 0, exits, "a child owns input until it closes" );
		hud.Stack.Update( context, UiInput.Key( UiKeys.Back ) );
		Click( hud, "ExitToLobby" );
		Assert.AreEqual( 1, exits );
		StillLost( level );
	}
}
