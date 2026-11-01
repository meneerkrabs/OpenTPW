using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The lobby's key panel as tp.exe's refresh 0x4B9340 and selection step 0x4B90F0 keep it: the key frame EA and its cost digit EB (gold while the
/// player's keys meet the selected island's cost, grey below it), effect 0x61 while they do, and the counter EC. Synthetic hosts, GPU free.
/// </summary>
[TestClass, DoNotParallelize]
public class LobbyKeyPanelTests
{
	private Logger previousLog = null!;
	[TestInitialize] public void Initialize() { previousLog = Log; Log = new(); }
	[TestCleanup] public void Cleanup() => Log = previousLog;

	private sealed class RecordingPool : IAdvisorParticlePool
	{
		public readonly List<(int Effect, (int X, int Y, int Z) Position)> Created = new();
		public readonly List<object> Killed = new();
		public object? Create( int effectId, (int X, int Y, int Z) native ) { Created.Add( (effectId, native) ); return Created.Count; }
		public void Move( object handle, (int X, int Y, int Z) native ) { }
		public void Kill( object handle ) => Killed.Add( handle );
	}

	// Jungle and Halloween cost one key, Fantasy three, Space five (the shipped global.sam files)
	private static readonly Dictionary<string, int> Costs = new() { ["jungle"] = 1, ["hallow"] = 1, ["fantasy"] = 3, ["space"] = 5 };

	private static LobbyIslandInfo Island( int index, string level ) =>
		new( index, level, "", "", "", level, 0, 0, (0, 0, 0), Array.Empty<string>(), false, 0 );

	private static FrontEndMenu Menu( int? keys, RecordingPool? pool, Func<string, int?>? cost = null ) => new( OriginalUiTests.FakeStrings(),
		Costs.Keys.Select( ( level, index ) => Island( index, level ) ).ToArray(),
		new FrontEndActions { PlayerKeySnapshot = keys, ThemeEntryCost = cost ?? ( level => Costs.TryGetValue( level, out var value ) ? value : null ), KeyPanelEffects = () => pool } );

	private static UiModelImage Frame( FrontEndMenu menu ) => (UiModelImage)menu.Main.Find( "lobbyKeyPanel" )!;
	private static UiModelImage Digit( FrontEndMenu menu ) => (UiModelImage)menu.Main.Find( "lobbyKeyCost" )!;
	private static UiModelImage Icon( FrontEndMenu menu ) => (UiModelImage)menu.Main.Find( "lobbyGoldenKey" )!;
	private static UiLabel Count( FrontEndMenu menu ) => (UiLabel)menu.Main.Find( "lobbyGoldenKeys" )!;

	[TestMethod]
	public void ThePanelUsesTheLobbyTablesRectanglesAndModels()
	{
		// [BIN:TPW-EXE:0x757F60 key panel layout] EA s_key, its child EB s_X1 and the counter's ED gkey
		var menu = Menu( 1, null );
		var table = OriginalLayoutTables.Lobby;
		Assert.AreEqual( "s_key", Frame( menu ).Model ); Assert.AreEqual( table.Control( 0x1E0EA ).Rect, Frame( menu ).Bounds );
		Assert.AreEqual( "s_X1", Digit( menu ).Model ); Assert.AreEqual( table.Control( 0x1E0EB ).Rect, Digit( menu ).Bounds );
		Assert.AreEqual( 554f, Frame( menu ).Bounds.X ); Assert.AreEqual( 170f, Frame( menu ).Bounds.Y );
		Assert.AreEqual( UiAnchor.Top, Frame( menu ).Anchor ); Assert.AreEqual( UiAnchor.Top, Digit( menu ).Anchor );
	}

	[TestMethod]
	public void WithoutAKnownKeyCountNoPanelAndNoEffectShow()
	{
		var pool = new RecordingPool(); var menu = Menu( null, pool );
		Assert.IsFalse( Frame( menu ).Visible ); Assert.IsFalse( Digit( menu ).Visible ); Assert.IsFalse( Icon( menu ).Visible ); Assert.IsFalse( Count( menu ).Visible );
		Assert.AreEqual( 0, pool.Created.Count );
	}

	[DataTestMethod]
	[DataRow( "jungle", 1, 0, 0 )]
	[DataRow( "hallow", 1, 0, 0 )]
	[DataRow( "fantasy", 3, 0, 2 )]
	[DataRow( "space", 5, 0, 4 )]
	public void KeysAtTheCostShowTheGoldFrameTheCostDigitAndStartEffect0x61Once( string level, int keys, int frame, int digit )
	{
		// [BIN:TPW-EXE:0x4B958F affordable] EA frame 0, EB frame cost - 1, 0x521E60(0x61, 50000, 0, z of EA's centre row)
		var pool = new RecordingPool(); var menu = Menu( keys, pool );
		menu.SelectLevel( level );
		Assert.IsTrue( Frame( menu ).Visible ); Assert.IsTrue( Digit( menu ).Visible );
		Assert.AreEqual( frame, Frame( menu ).Frame() ); Assert.AreEqual( digit, Digit( menu ).Frame() );
		CollectionAssert.AreEqual( new[] { (0x61, (0xC350, 0, 14794)) }, pool.Created, "one effect, the key panel's centre row at the screen's centre column" );
		Assert.AreEqual( 0, pool.Killed.Count );
	}

	[DataTestMethod]
	[DataRow( "jungle", 0, 5 )]
	[DataRow( "hallow", 0, 5 )]
	[DataRow( "fantasy", 2, 7 )]
	[DataRow( "space", 4, 9 )]
	public void KeysBelowTheCostShowTheGreyFrameAndDigitAndNoEffect( string level, int keys, int digit )
	{
		// [BIN:TPW-EXE:0x4B963B unaffordable] EA frame 1, EB frame cost + 4, the effect ended
		var pool = new RecordingPool(); var menu = Menu( keys, pool );
		menu.SelectLevel( level );
		Assert.AreEqual( 1, Frame( menu ).Frame() ); Assert.AreEqual( digit, Digit( menu ).Frame() );
		// the first island (Jungle, one key) is affordable from 1 key on: the effect started with the creation and ended with this island's selection
		Assert.AreEqual( keys >= 1 ? 1 : 0, pool.Created.Count ); Assert.AreEqual( pool.Created.Count, pool.Killed.Count, "no effect runs for an island the keys do not meet" );
		Assert.IsTrue( Frame( menu ).Visible, "the frame stays up with no keys: only the counter is hidden" );
		Assert.AreEqual( keys > 0, Icon( menu ).Visible );
	}

	[TestMethod]
	public void TheSelectionStepComparesTheStoredCountAndStartsOrEndsTheEffectPerIsland()
	{
		// [BIN:TPW-EXE:0x4B90F0 key panel selection] 3 keys: Jungle (1) and Fantasy (3) are affordable, Space (5) is not
		var pool = new RecordingPool(); var menu = Menu( 3, pool );
		Assert.AreEqual( 1, pool.Created.Count, "Jungle is selected first: the effect runs from the creation" );
		menu.SelectLevel( "hallow" );
		Assert.AreEqual( 1, pool.Created.Count, "a running effect is not started again (the handle 0x7CC4BC is nonzero)" );
		menu.SelectLevel( "space" );
		Assert.AreEqual( 1, Frame( menu ).Frame() ); Assert.AreEqual( 9, Digit( menu ).Frame() );
		CollectionAssert.AreEqual( new object[] { 1 }, pool.Killed, "an island the keys do not meet ends the effect" );
		menu.SelectLevel( "fantasy" );
		Assert.AreEqual( 0, Frame( menu ).Frame() ); Assert.AreEqual( 2, Digit( menu ).Frame() );
		Assert.AreEqual( 2, pool.Created.Count, "and a later affordable island starts it again" );
		Assert.AreEqual( 0x61, pool.Created[1].Effect );
		Assert.AreEqual( "3 x", Count( menu ).Text(), "the counter does not change with the selection" );
	}

	[TestMethod]
	public void TheRefreshReadsTheNewCountAndTheSelectionStepKeepsUsingTheStoredOne()
	{
		// [BIN:TPW-EXE:0x4B9340 key panel refresh] it stores the count it read (0x7CC4B8); 0x4B90F0 compares with that, never with the ledger
		var pool = new RecordingPool(); var menu = Menu( 1, pool );
		menu.SelectLevel( "fantasy" );
		Assert.AreEqual( 1, Frame( menu ).Frame(), "1 key do not meet Fantasy's 3" );
		menu.RefreshKeyPanel( 3 );
		Assert.AreEqual( 0, Frame( menu ).Frame() ); Assert.AreEqual( 2, Digit( menu ).Frame() ); Assert.AreEqual( "3 x", Count( menu ).Text() );
		Assert.AreEqual( 2, pool.Created.Count, "Jungle's effect ended with the selection of Fantasy, and the refresh starts it for the 3 keys" );
		menu.SelectLevel( "space" ); menu.SelectLevel( "fantasy" );
		Assert.AreEqual( 0, Frame( menu ).Frame(), "the stored 3 still meets Fantasy" );
		menu.RefreshKeyPanel( 0 );
		Assert.AreEqual( 1, Frame( menu ).Frame() ); Assert.AreEqual( 7, Digit( menu ).Frame() );
		Assert.IsFalse( Icon( menu ).Visible, "no keys hide the counter" ); Assert.IsFalse( Count( menu ).Visible );
		// kills: Jungle's effect (Fantasy selected with 1 key), the one the refresh started (Space selected with the stored 3), the one the return to Fantasy started (the count dropped to 0)
		Assert.AreEqual( 3, pool.Created.Count ); Assert.AreEqual( 3, pool.Killed.Count, "a count below the cost ends the effect" );
	}

	[TestMethod]
	public void AnIslandWithoutARecordLeavesTheFramesAndStillShowsTheCounter()
	{
		// [BIN:TPW-EXE:0x4B940E no record] 0x5B09C0 finds nothing usable: the affordability part is skipped, the counter phase still runs (0x4B9681)
		var pool = new RecordingPool(); var menu = Menu( 4, pool, level => null );
		Assert.AreEqual( 1, Frame( menu ).Frame(), "the creation frames (EA 1, EB 5) stay" ); Assert.AreEqual( 5, Digit( menu ).Frame() );
		Assert.IsTrue( Icon( menu ).Visible ); Assert.AreEqual( "4 x", Count( menu ).Text() );
		Assert.AreEqual( 0, pool.Created.Count );
	}

	[TestMethod]
	public void RemovingTheLobbyEndsTheEffectAndTheRefreshIsIgnoredAfterwards()
	{
		// [BIN:TPW-EXE:0x4B8B70 key panel window] message 0x14 clears the window (0x7CC4B4) and ends the effect; 0x4B9340 returns at once without the window
		var pool = new RecordingPool(); var menu = Menu( 5, pool );
		Assert.AreEqual( 1, pool.Created.Count );
		menu.Stack.Clear();
		CollectionAssert.AreEqual( new object[] { 1 }, pool.Killed );
		menu.RefreshKeyPanel( 0 ); menu.SelectLevel( "space" );
		Assert.AreEqual( 1, pool.Created.Count ); Assert.AreEqual( 1, pool.Killed.Count );
		Assert.AreEqual( 0, Frame( menu ).Frame(), "the frames are no longer touched" );
	}

	[TestMethod]
	public void APoolThatIsNotThereIsAskedAgainAtTheNextAffordableRefresh()
	{
		RecordingPool? pool = null; var asked = 0;
		var menu = new FrontEndMenu( OriginalUiTests.FakeStrings(), new[] { Island( 0, "jungle" ) },
			new FrontEndActions { PlayerKeySnapshot = 1, ThemeEntryCost = _ => 1, KeyPanelEffects = () => { asked++; return pool; } } );
		Assert.AreEqual( 1, asked ); Assert.AreEqual( 0, Frame( menu ).Frame(), "the art does not depend on the effect" );
		pool = new RecordingPool();
		menu.RefreshKeyPanel( 1 );
		Assert.AreEqual( 2, asked ); Assert.AreEqual( 1, pool.Created.Count );
	}

	[TestMethod]
	public void TheEffectSitsWhereTheEnterParkEffectDoes()
	{
		Assert.AreEqual( LobbyEnterEffect.Position(), LobbyKeyPanelEffect.Position() );
		Assert.AreEqual( 0x61, LobbyKeyPanelEffect.EffectId );
	}

	[TestMethod]
	public void TheShippedModelsHaveTheFramesTheArtUses()
	{
		var data = StringTableTests.OriginalDataDirectory();
		if ( !File.Exists( Path.Combine( data, "ui.wad" ) ) ) Assert.Inconclusive( "Original ui.wad is not installed." );
		var original = FileSystem;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			var models = new UiModels();
			var key = models.Assets.Single( model => model.Name == "s_key" );
			var digit = models.Assets.Single( model => model.Name == "s_X1" );
			Assert.AreEqual( 2, key.Frames.Count, "EA: gold, grey" );
			Assert.AreEqual( 10, digit.Frames.Count, "EB: five gold digits, then five grey ones (cost - 1 and cost + 4)" );
			Assert.IsTrue( key.Frames[0].Parts.Any( part => part.TextureName == "gold_grad" ) ); Assert.IsTrue( key.Frames[1].Parts.Any( part => part.TextureName == "grey_grad" ) );
			Assert.IsTrue( Enumerable.Range( 0, 5 ).All( frame => digit.Frames[frame].Parts.Any( part => part.TextureName == "gold_grad" ) ) );
			Assert.IsTrue( Enumerable.Range( 5, 5 ).All( frame => digit.Frames[frame].Parts.Any( part => part.TextureName == "grey_grad" ) ) );
			Assert.AreEqual( 4, models.Assets.Single( model => model.Name == "gkey" ).Frames.Count, "ED frame 3 exists" );
			foreach ( var cost in Costs.Values ) Assert.IsTrue( cost + 4 < digit.Frames.Count && cost - 1 >= 0 );
		}
		finally { FileSystem = original; }
	}

	[TestMethod]
	public void TheShippedGlobalSamFilesGiveTheCosts()
	{
		var data = StringTableTests.OriginalDataDirectory();
		if ( !Directory.Exists( Path.Combine( data, "levels", "jungle" ) ) && !File.Exists( Path.Combine( data, "levels.wad" ) ) ) Assert.Inconclusive( "Original levels are not installed." );
		var original = FileSystem;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			foreach ( var (level, cost) in Costs ) Assert.AreEqual( cost, GameFlow.ReadLobbyThemeEntryCost( level ), level );
			Assert.IsNull( GameFlow.ReadLobbyThemeEntryCost( "nowhere" ) );
		}
		finally { FileSystem = original; }
	}

	[TestMethod]
	public void TheShippedTemplate0x61IsAScreenSpaceEffect()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) ) Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the shipped Tp2.plb." );
		var data = StringTableTests.OriginalDataDirectory();
		var plb = Directory.EnumerateFiles( data, "*", SearchOption.AllDirectories ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "Tp2.plb", StringComparison.OrdinalIgnoreCase ) );
		if ( plb == null ) Assert.Inconclusive( "The particle templates are not installed." );
		using var stream = File.OpenRead( plb! );
		var file = new ParticleTemplateFile( stream );
		Assert.IsTrue( file.Templates[LobbyKeyPanelEffect.EffectId].ScreenSpace, "template 0x61 has byte +5 set" );
		var system = new ParticleSystem( file.Templates, 1024, effectors: file.EffectorTemplates );
		var pool = new ParticleSystemPool( system );
		var handle = pool.Create( LobbyKeyPanelEffect.EffectId, LobbyKeyPanelEffect.Position() );
		Assert.IsNotNull( handle ); Assert.AreEqual( 1, system.LiveEmitters );
		pool.Kill( handle! );
		for ( var tick = 0; tick < 5000 && system.LiveEmitters + system.LiveEffectors + system.LiveParticles > 0; tick++ ) system.Tick();
		Assert.AreEqual( 0, system.LiveEmitters + system.LiveEffectors + system.LiveParticles, "the killed effect's particles run out" );
	}
}
