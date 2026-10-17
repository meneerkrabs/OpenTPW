using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// <c>OPENTPW_TPWS_SAVES</c>: a folder of original Theme Park World park saves (<c>.TPWS</c>, <c>.INTS</c>) in one subfolder per
/// theme (<c>jungle</c>, <c>hallow</c>, <c>fantasy</c>, <c>space</c>), kept private. Needs <c>OPENTPW_GAME_PATH</c> for the maps
/// and settings. Every save must pass each reader that the single Easymode fixture established (docs/TPWS-PAYLOAD.md).
/// </summary>
[TestClass]
[DoNotParallelize]
public class OriginalSaveCorpusTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "fantasy", "space" };
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestMethod]
	public void OriginalSavesPassEveryProvenReader()
	{
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPWS_SAVES" );
		if ( string.IsNullOrWhiteSpace( folder ) || !Directory.Exists( folder ) )
			Assert.Inconclusive( "Set OPENTPW_TPWS_SAVES to a folder with original .TPWS/.INTS saves in per-theme subfolders." );
		var files = Directory.EnumerateFiles( folder!, "*", SearchOption.AllDirectories )
			.Where( file => Path.GetExtension( file ).ToUpperInvariant() is ".TPWS" or ".INTS" )
			.Select( file => (Path: file, Theme: Path.GetFileName( Path.GetDirectoryName( file ) )!.ToLowerInvariant()) )
			.Where( file => Themes.Contains( file.Theme ) ).OrderBy( file => file.Path, StringComparer.Ordinal ).ToList();
		if ( files.Count == 0 )
			Assert.Inconclusive( "The OPENTPW_TPWS_SAVES folder has no .TPWS/.INTS files in a theme subfolder." );
		foreach ( var theme in Themes )
			foreach ( var extension in new[] { ".TPWS", ".INTS" } )
				Assert.IsTrue( files.Any( file => file.Theme == theme && Path.GetExtension( file.Path ).Equals( extension, StringComparison.OrdinalIgnoreCase ) ),
					$"The corpus needs a {extension} save for {theme}." );
		UseOriginalData();
		var maps = new Dictionary<string, MapFile>();
		foreach ( var (path, theme) in files )
		{
			var name = $"{theme}/{Path.GetFileName( path )}";
			using var reader = new SaveReader( path );
			var container = reader.Inspect();
			Assert.AreEqual( (500u, (byte)133), (container.Magic, container.Version), name );
			var payload = reader.ReadFile();
			var layout = SavePayloadLayout.Parse( payload );
			Assert.AreEqual( 19, layout.Blocks.Count, name );

			if ( !maps.TryGetValue( theme, out var map ) )
				maps[theme] = map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( $"levels/{theme}/terrain.wad", "base.map" ) ) );
			var park = OriginalParkImport.Import( payload, map );
			Assert.AreEqual( 0, park.UnresolvedObjects.Count, name );
			// The framed RideSystem walk (SaveRideSystemBlock) runs on every save: each block walks exactly and its
			// derived control counts are strictly minimal, with the routed records matching the placed/fixed counts.
			Assert.IsNotNull( park.RideSystem, name );
			Assert.IsTrue( park.RideSystem!.UniquelyWalked, $"{name}: every derived control count is strictly minimal" );
			Assert.AreEqual( park.RideSystem.Used, (uint)park.RideSystem.Records.Length, name );
			Assert.AreEqual( park.RideSystem.Records.Count( record => record.IsPlacedObject ), park.PlacedObjects.Count, name );
			Assert.AreEqual( park.RideSystem.Records.Count( record => record.IsFixedItem ), park.FixedItems.Count, name );
			for ( var x = 0; x < map.CellCountX; x++ )
				for ( var y = 0; y < map.CellCountY; y++ )
					if ( map.GetFlagsAt( x, y ).HasFlag( MapCellFlags.InitialPath ) )
						Assert.IsTrue( park.Cells[x, y].IsPath, $"{name}: initial path ({x}, {y})" );

			var attractions = park.Attractions;
			// The Coasters block reader runs on every save through Import; a listed coaster must parse to a coaster.
			Assert.IsNotNull( park.CoasterBlock, name );
			Assert.AreEqual( park.CoasterBlock.Count, (uint)park.CoasterBlock.Coasters.Count, name );
			foreach ( var coaster in park.CoasterBlock.Coasters )
				foreach ( var section in coaster.Sections )
					Assert.IsTrue( section.CellX < map.CellCountX && section.CellY < map.CellCountY, $"{name}: coaster section inside the grid" );
			Assert.AreEqual( park.World.Block.End, park.World.Things[^1].End, $"{name}: the thing list closes at the World delimiter" );
			// A park save keeps one attraction record per saved object (as in Easymode); the initial INTS saves keep none.
			var expected = Path.GetExtension( path ).Equals( ".INTS", StringComparison.OrdinalIgnoreCase ) ? Array.Empty<int>()
				: park.PlacedObjects.Select( item => item.Record.InfoId ).Concat( park.FixedItems.Select( item => item.InfoId ) ).ToArray();
			Assert.IsTrue( park.FixedItems.Count > 0, name );
			CollectionAssert.AreEquivalent( expected, attractions.Select( ride => ride.InfoId ).ToArray(), name );

			var economy = new ParkEconomy( BalanceSettings.Load( theme, easy: false ), EconomyObjectCatalog.Load( theme, easy: false ), ParkGameMode.FullSimulation, 1 );
			var evidence = new List<string>();
			OriginalEconomyImport.Verify( economy.Settings, park.Economy, evidence );
			Assert.AreEqual( economy.Settings.ChallengesInThisLevel.Count, park.Economy.Challenges.Count, name );
		}
	}

	private void UseOriginalData()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}
}
