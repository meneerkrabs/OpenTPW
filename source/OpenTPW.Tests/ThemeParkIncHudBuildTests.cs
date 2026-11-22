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

/// <summary>The build list's facilities tab: the single-instance refusal of the text HUD, and Theme Park World's buy window keeping its four tabs.</summary>
[TestClass]
public class ThemeParkIncHudBuildTests
{
	private static void Set( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private static ObjectCatalogEntry Entry( int id, string usage ) => new( "science", ObjectCategory.Facility, "/synthetic",
		new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id {id}\nInfo.Name Room {id}\nInfo.WhichUIType 5\n{usage}", "synthetic.sam" ) } ), new ObjectShape( new[] { "*" } ),
		"/unused.MD2", null, null, Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), null!, null );

	/// <summary>An object of <paramref name="entry"/> standing in the park; no model or script is loaded.</summary>
	private static OriginalObject Placed( ObjectCatalogEntry entry )
	{
		var runtime = (OriginalObjectRuntime)RuntimeHelpers.GetUninitializedObject( typeof( OriginalObjectRuntime ) );
		Set( runtime, "Entry", entry );
		var item = (OriginalObject)RuntimeHelpers.GetUninitializedObject( typeof( OriginalObject ) );
		Set( item, "Runtime", runtime );
		return item;
	}

	private GameEdition previousEdition;

	[TestInitialize]
	public void Initialize() { previousEdition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkInc ); }

	[TestCleanup]
	public void Cleanup() => GameEditions.Select( previousEdition );

	[TestMethod]
	public void ASecondResearchRoomIsRefusedAtTheList()
	{
		// [BIN:TPI-EXE:0x57CF60 single instance] UsageInfo.SingleInstance 1 and a built copy refuse the row; another type, or the first copy, is armed
		var room = Entry( 1404, "UsageInfo.SingleInstance 1\nUsageInfo.ThinkingSpace 1\n" );
		var toilet = Entry( 1439, "UsageInfo.ProvidesRelief 1\n" );
		Assert.IsFalse( ThemeParkIncHud.RefusesSingleInstance( room, Array.Empty<OriginalObject>() ), "the first research room is armed" );
		Assert.IsTrue( ThemeParkIncHud.RefusesSingleInstance( room, new[] { Placed( room ) } ), "a second one is refused" );
		Assert.IsFalse( ThemeParkIncHud.RefusesSingleInstance( toilet, new[] { Placed( toilet ), Placed( toilet ) } ), "a toilet may stand many times" );
		Assert.IsFalse( ThemeParkIncHud.RefusesSingleInstance( room, new[] { Placed( toilet ) } ), "only a copy of the same type counts" );

		// through the HUD: the row's activation refuses with a note, arms nothing and leaves the window as it was
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { "science", new[] { room } }, null )!;
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		var objects = new ParkObjects( catalog, new SandboxParkGrid(), new WorldSeed( 1 ) );
		((List<OriginalObject>)objects.Objects).Add( Placed( room ) );
		Set( level, "Objects", objects );
		Set( level, "CellTool", new CellBuildTool() );
		Set( level, "LastActionMessage", "" );
		var hud = new ThemeParkIncHud( level, new UiStringTable( "English", new string[1], new[] { "" }, new string[1], new string[1] ), new ThemeParkIncHudHost() );
		typeof( ThemeParkIncHud ).GetMethod( "ArmBuild", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( hud, new object[] { room } );
		Assert.IsNull( level.BuildEntry, "nothing is armed" );
		Assert.AreEqual( string.Format( hud.Strings.Extra( OpenTpwText.TpiSingleInstance ), room.DisplayName ), hud.Messages.Last() );
	}

	[TestMethod]
	public void TheBuyWindowWalksTheFourThemeParkWorldTabs()
	{
		// the facilities tab (5) is Theme Park Inc's text HUD's; Theme Park World's buy window has four tabs, and what walks them (the perf build scenario, the debug captures, --debug-hud
		// buytab) walks ParkHud.BuyTabs, so its title never looks for a fifth
		CollectionAssert.AreEqual( new[] { BuildCategory.Rides, BuildCategory.Shops, BuildCategory.Sideshows, BuildCategory.Features }, ParkHud.BuyTabs.ToArray() );
		Assert.IsTrue( Enum.IsDefined( BuildCategory.Facilities ) && !ParkHud.BuyTabs.Contains( BuildCategory.Facilities ) );
		var root = new DirectoryInfo( AppContext.BaseDirectory );
		while ( root != null && !File.Exists( Path.Combine( root.FullName, "source", "OpenTPW.sln" ) ) )
			root = root.Parent;
		Assert.IsNotNull( root, "the repository root" );
		foreach ( var file in new[] { "Client/PerfBuildScenario.cs", "Hud/ParkHud.DebugCapture.cs", "Hud/ParkHud.cs" } )
		{
			var path = Path.Combine( root!.FullName, "source", "OpenTPW", file );
			if ( File.Exists( path ) )
				Assert.IsFalse( File.ReadAllText( path ).Contains( "Enum.GetValues<BuildCategory>()", StringComparison.Ordinal ), $"{file} walks every BuildCategory" );
		}
		StringAssert.Contains( File.ReadAllText( Path.Combine( root!.FullName, "source", "OpenTPW", "Client", "Game.cs" ) ), "ParkHud.BuyTabs.Contains( buyTab )", "--debug-hud buytab refuses a tab the buy window does not have" );
	}
}
