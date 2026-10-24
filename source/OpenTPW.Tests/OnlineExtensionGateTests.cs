using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.Mods;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The <c>opentpw.online</c> official extension switched off (docs/MODS.md): no online entries, sessions or tools.</summary>
[TestClass, DoNotParallelize]
public class OnlineExtensionGateTests
{
	private GameEdition previousEdition;

	[TestInitialize]
	public void Initialize()
	{
		previousEdition = GameEditions.Current;
		GameEditions.Select( GameEdition.ThemeParkWorld );
	}

	[TestCleanup]
	public void Cleanup() => GameEditions.Select( previousEdition );

	internal static ModSet WithOnline( bool enabled ) => ModSet.Resolve( Array.Empty<DiscoveredMod>(),
		new ModSettings { Enabled = new Dictionary<string, bool> { [OfficialExtensions.Online.Id] = enabled } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );

	private static void Set( object target, string name, object? value ) => target.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private static GameFlow Flow()
	{
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		Set( flow, "<Strings>k__BackingField", OriginalUiTests.FakeStrings() );
		return flow;
	}

	private static ParkHud BareHud( Level level, HudHost host )
	{
		var hud = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		Set( hud, "level", level ); Set( hud, "strings", OriginalUiTests.FakeStrings() ); Set( hud, "host", host ); Set( hud, "<Stack>k__BackingField", new UiScreenStack() );
		var screen = new UiScreen( "hud" ) { Modal = false };
		Set( hud, "hud", screen );
		hud.Stack.Push( screen );
		return hud;
	}

	private static LobbyIslandInfo Island() => new( 0, "jungle", "", "", "", "jungle", 0, 0, (0, 0, 0), Array.Empty<string>(), false, 0 );

	[TestMethod]
	public void TheOnlineExtensionIsOnByDefault()
	{
		Assert.IsTrue( OfficialExtensions.Online.DefaultEnabled );
		Assert.IsFalse( OfficialExtensions.Online.AffectsSimulation );
		Assert.IsTrue( ModSet.Defaults( OfficialExtensions.All ).IsEnabled( "opentpw.online" ) );
		using var scope = OpenTpwExtensions.Use( ModSet.Defaults( OfficialExtensions.All ) );
		Assert.IsTrue( FeatureGates.Online );
		Assert.IsTrue( FeatureGates.Sharing );
		Assert.IsNotNull( GameFlow.WhenOnline( _ => { } ) );
	}

	[TestMethod]
	public void SharingNeedsBothTheEditionAndTheExtension()
	{
		using var scope = OpenTpwExtensions.Use( WithOnline( false ) );
		Assert.IsTrue( GameEditions.Capabilities.Sharing );
		Assert.IsFalse( FeatureGates.Online );
		Assert.IsFalse( FeatureGates.Sharing );
	}

	[TestMethod]
	public void TheParkHudHasNoOnlineEntriesWhenDisabled()
	{
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		using ( OpenTpwExtensions.Use( WithOnline( true ) ) )
		{
			var host = Flow().CreateHudHost( level, () => null! );
			Assert.IsNotNull( host.GoOnline );
			Assert.IsNotNull( host.PublishPark );
			Assert.IsNotNull( host.OpenPostcard );
		}
		using ( OpenTpwExtensions.Use( WithOnline( false ) ) )
		{
			var host = Flow().CreateHudHost( level, () => null! );
			Assert.IsNull( host.GoOnline );
			Assert.IsNull( host.PublishPark );
			Assert.IsNull( host.OpenPostcard );
			var hud = BareHud( level, host );
			hud.OpenPauseMenu();
			var menu = hud.Stack.Top!;
			Assert.IsNull( menu.Find( nameof( UIStrings.GoOnline ) ), "no Go Online line" );
			// Publish Park is an original pause-menu line: it stays in place, disabled, as when publishing is unavailable
			Assert.IsFalse( ((UiMenuItem)menu.Find( nameof( UIStrings.PublishPark ) )!).Enabled );
		}
	}

	[TestMethod]
	public void TheLobbyHasNoGlobeButtonOrGoOnlineLineWhenDisabled()
	{
		using var scope = OpenTpwExtensions.Use( WithOnline( false ) );
		var flow = Flow();
		FrontEndActions Actions() => new() { GoOnline = GameFlow.WhenOnline( flow.ShowOnline ), BuildTogether = GameFlow.WhenOnline( _ => { } ) };
		var menu = new FrontEndMenu( OriginalUiTests.FakeStrings(), new[] { Island() }, Actions() );
		Assert.IsNull( menu.Main.Find( "worldView" ) );
		menu.ShowMenu();
		Assert.AreEqual( "lobbyMenu", menu.Stack.Top!.Name );
		Assert.IsNull( menu.Stack.Top.Find( "goOnline" ) );
		Assert.IsNull( menu.Stack.Top.Find( "buildTogether" ), "[EXT:online-coop-session] no Build Together line without the online extension" );
		Assert.IsNotNull( menu.Stack.Top.Find( "load" ) );

		using var enabled = OpenTpwExtensions.Use( WithOnline( true ) );
		var online = new FrontEndMenu( OriginalUiTests.FakeStrings(), new[] { Island() }, Actions() );
		Assert.IsNotNull( online.Main.Find( "worldView" ) );
		online.ShowMenu();
		Assert.IsNotNull( online.Stack.Top!.Find( "buildTogether" ), "[EXT:online-coop-session] the lobby's Build Together line follows Go Online" );
	}

	[TestMethod]
	public void NoOnlineSessionOrPostcardCanBeCreatedWhenDisabled()
	{
		using var scope = OpenTpwExtensions.Use( WithOnline( false ) );
		var flow = Flow();
		var exception = Assert.ThrowsException<InvalidOperationException>( () => flow.Online );
		StringAssert.Contains( exception.Message, FeatureGates.OnlineDisabledMessage );
		Assert.IsNull( typeof( GameFlow ).GetField( "onlineSession", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( flow ) );
		StringAssert.Contains( Assert.ThrowsException<NotSupportedException>( () => flow.ShowPostcard( new UiScreenStack() ) ).Message, FeatureGates.OnlineDisabledMessage );
	}

	[DataTestMethod]
	[DataRow( "--export-park", "out.tpwpark" )]
	[DataRow( "--import-park", "in.tpwpark" )]
	[DataRow( "--visit-park", "in.tpwpark" )]
	[DataRow( "--online-dir", "/tmp/online" )]
	public void OnlineCommandLineOptionsRefuseWhenDisabled( string option, string value )
	{
		var args = new[] { "--game-path", "/games/tpw", option, value };
		using ( OpenTpwExtensions.Use( WithOnline( true ) ) )
			FeatureGates.RefuseOnlineCommandLine( args );
		using ( OpenTpwExtensions.Use( WithOnline( false ) ) )
		{
			var exception = Assert.ThrowsException<ArgumentException>( () => FeatureGates.RefuseOnlineCommandLine( args ) );
			StringAssert.Contains( exception.Message, "OpenTPW online extension is disabled" );
			StringAssert.Contains( exception.Message, option );
			FeatureGates.RefuseOnlineCommandLine( new[] { "--smoke-test" } );
		}
	}

	[TestMethod]
	public void DisableExtensionTurnsTheOnlineExtensionOffForTheRun()
	{
		// no OPENTPW_MOD_DIRS and no program folder: independent of the developer's own mods
		using var scope = OpenTpwExtensions.Use( OpenTpwExtensions.Active );
		// startup installs the content roots of the active set (the default set's pools root among them); leave the catalog as it was
		var previousRoots = ObjectCatalog.ContentRoots;
		using var restoreRoots = new Restore( () => ObjectCatalog.SetContentRoots( previousRoots ) );
		var traces = new List<string>();
		var set = ModStartup.Initialize( new[] { "--smoke-test", "--disable-extension", "opentpw.online" }, traces.Add, message => Assert.Fail( message ), null, null );
		Assert.IsFalse( set.IsEnabled( "opentpw.online" ) );
		Assert.AreSame( set, OpenTpwExtensions.Active );
		Assert.IsFalse( FeatureGates.Online );
		StringAssert.Contains( traces.Single(), "opentpw.online" );
		Assert.ThrowsException<ArgumentException>( () => ModStartup.Initialize( new[] { "--smoke-test", "--disable-extension", "opentpw.offline" }, _ => { }, _ => { }, null, null ) );
	}

	private sealed class Restore( Action action ) : IDisposable
	{
		public void Dispose() => action();
	}
}
