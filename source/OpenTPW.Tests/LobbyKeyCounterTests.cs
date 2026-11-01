using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class LobbyKeyCounterTests
{
	private string directory = null!;
	private Logger previousLog = null!;
	private string Profile => Path.Combine( directory, PlayerTicketFile.FileName );
	[TestInitialize]
	public void Initialize() { directory = Directory.CreateTempSubdirectory( "opentpw-lobby-keys-" ).FullName; previousLog = Log; Log = new(); }
	[TestCleanup]
	public void Cleanup() { Log = previousLog; Directory.Delete( directory, true ); }
	private void Save( int keys, ParkGameMode mode = ParkGameMode.FullSimulation ) => PlayerTicketFile.Save( Profile,
		new PlayerTicketSnapshot( mode, keys, 0, Array.Empty<EarnedTicketIdentity>() ) );
	private FrontEndMenu Menu() => new( OriginalUiTests.FakeStrings(), new[] {
		new LobbyIslandInfo( 0, "jungle", "", "", "", "jungle", 0, 0, (0, 0, 0), Array.Empty<string>(), false, 0 ) },
		new FrontEndActions { PlayerKeySnapshot = GameFlow.ReadLobbyPlayerKeys( Profile ) } );
	private static UiLabel Count( FrontEndMenu menu ) => (UiLabel)menu.Main.Find( "lobbyGoldenKeys" )!;

	[DataTestMethod]
	[DataRow( 0, false )]
	[DataRow( 1, true )]
	[DataRow( 23, true )]
	public void SavedFullSimulationCountUsesNativeCounterPredicateAndSlots( int keys, bool visible )
	{
		Save( keys ); var before = File.ReadAllBytes( Profile ); var menu = Menu();
		var count = Count( menu ); var icon = (UiModelImage)menu.Main.Find( "lobbyGoldenKey" )!;
		Assert.AreEqual( visible, count.Visible ); Assert.AreEqual( visible, icon.Visible );
		// [BIN:TPW-EXE:0x4B96BE counter phase] 0x4B97C6 formats the count only on the branch that shows EC: no keys leave EE unwritten
		if ( visible ) Assert.AreEqual( keys + " x", count.Text() );
		Assert.AreEqual( "gkey", icon.Model ); Assert.AreEqual( 3, icon.Frame(), "0x4B8D65 sets ED to frame 3" );
		Assert.AreEqual( OriginalLayoutTables.Lobby.Control( 0x1E0EE ).Rect, count.Bounds );
		Assert.AreEqual( OriginalLayoutTables.Lobby.Control( 0x1E0ED ).Rect, icon.Bounds );
		Assert.AreEqual( UiAnchor.TopRight, count.Anchor ); Assert.AreEqual( UiAnchor.TopRight, icon.Anchor );
		foreach ( var id in new[] { "islandName", "previousIsland", "nextIsland", "enterPark" } ) Assert.IsTrue( menu.Main.Find( id )!.Visible );
		CollectionAssert.AreEqual( before, File.ReadAllBytes( Profile ) );
	}

	[TestMethod]
	public void MissingHistoryStaysAbsentAndDoesNotInventStarterKeys()
	{
		Assert.IsNull( GameFlow.ReadLobbyPlayerKeys( Profile ) ); Assert.IsFalse( Count( Menu() ).Visible ); Assert.IsFalse( File.Exists( Profile ) );
	}

	[TestMethod]
	public void InvalidHistoryIsNotRepairedOrDisplayed()
	{
		File.WriteAllText( Profile, "not a progress file" ); var before = File.ReadAllBytes( Profile );
		Assert.IsNull( GameFlow.ReadLobbyPlayerKeys( Profile ) ); Assert.IsFalse( Count( Menu() ).Visible );
		CollectionAssert.AreEqual( before, File.ReadAllBytes( Profile ) );
	}

	[TestMethod]
	public void InstantActionHistoryDoesNotDisplayFullSimulationKeys()
	{
		Save( 7, ParkGameMode.InstantAction ); Assert.IsNull( GameFlow.ReadLobbyPlayerKeys( Profile ) ); Assert.IsFalse( Count( Menu() ).Visible );
	}

	[TestMethod]
	public void RebuiltLobbyReadsLatestSavedStateWithoutChangingExistingSnapshot()
	{
		Save( 7 ); var oldMenu = Menu(); Save( 1 ); var rebuilt = Menu();
		Assert.AreEqual( "7 x", Count( oldMenu ).Text() ); Assert.AreEqual( "1 x", Count( rebuilt ).Text() );
		Save( 0 ); Assert.IsFalse( Count( Menu() ).Visible );
		File.Delete( Profile ); Assert.IsFalse( Count( Menu() ).Visible );
	}
}
