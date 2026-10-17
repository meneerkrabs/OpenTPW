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
			// A park save keeps one attraction record per saved object (as in Easymode); the initial INTS saves keep the two fixed items (gates, lights) and nothing placed.
			var initial = Path.GetExtension( path ).Equals( ".INTS", StringComparison.OrdinalIgnoreCase );
			var expected = park.PlacedObjects.Select( item => item.Record.InfoId ).Concat( park.FixedItems.Select( item => item.InfoId ) ).ToArray();
			Assert.IsTrue( park.FixedItems.Count > 0, name );
			CollectionAssert.AreEquivalent( expected, attractions.Select( ride => ride.InfoId ).ToArray(), name );
			if ( initial )
			{
				// restart.INTS is the park before its first update: the constructor's calendar and empty history rings (tp.exe 0x4F7E80, 0x51ADE0)
				Assert.AreEqual( 0, park.PlacedObjects.Count, name );
				Assert.IsTrue( park.World.Calendar.IsInitial, name );
				Assert.AreEqual( 0u, park.World.Variables.GameTick, name );
				Assert.IsTrue( attractions.SelectMany( ride => ride.Histories ).All( ring => ring.CurrentEntry == SaveAttraction.EmptyRingHead && !ring.WrappedAround ), name );
			}
			else
				Assert.IsFalse( park.World.Calendar.IsInitial, name );

			var economy = new ParkEconomy( BalanceSettings.Load( theme, easy: false ), EconomyObjectCatalog.Load( theme, easy: false ), ParkGameMode.FullSimulation, 1 );
			var evidence = new List<string>();
			OriginalEconomyImport.Verify( economy.Settings, park.Economy, evidence );
			Assert.AreEqual( economy.Settings.ChallengesInThisLevel.Count, park.Economy.Challenges.Count, name );
			// The economy import runs on every save, the restart saves included.
			var import = OriginalEconomyImport.Apply( economy, park );
			Assert.AreEqual( (park.PlacedObjects.Count, park.FixedItems.Count), (import.ImportedObjects, import.ImportedFixedItems), name );
			Assert.IsTrue( import.Evidence.Any( line => line.StartsWith( $"Park turn {park.World.Variables.GameTick} imported", StringComparison.Ordinal ) && line.EndsWith( "agrees with that date.", StringComparison.Ordinal ) ), $"{name}: the calendar agrees with the saved turn" );
		}
	}

	/// <summary>
	/// The RSSE block (loader 0x5597A0) of every corpus save is consumed to its delimiter, each record matches its ride's .RSE member and every person's navigation state
	/// (reader 0x50D150) sits at the thing's position. The Scripts and Particles strictness (their loaders fail the whole load on a wrong magic or table size) must also hold, which the layout split checks.
	/// A save the park import cannot read for another reason is reported, not failed, so the block checks still run on it.
	/// </summary>
	[TestMethod]
	public void OriginalSavesReadTheirRsseBlocksAndNavigationStates()
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
		UseOriginalData();
		var failures = new List<string>();
		foreach ( var (path, theme) in files )
		{
			var name = $"{theme}/{Path.GetFileName( path )}";
			try
			{
				using var reader = new SaveReader( path );
				var payload = reader.ReadFile();
				var layout = SavePayloadLayout.Parse( payload );
				var rsse = SaveRsseBlock.Read( payload, layout );
				Assert.AreEqual( layout[SaveBlock.Rsse].Length, rsse.Length, name );
				SaveRsseTests.CheckRecordsAgainstTheArchives( rsse, name );
				var import = "imported";
				try
				{
					var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( $"levels/{theme}/terrain.wad", "base.map" ) ) );
					var park = OriginalParkImport.Import( payload, map );
					SaveRsseTests.CheckThingsAgainstTheAttractions( park, name );
					foreach ( var guest in park.Guests )
						SaveNavigationTests.CheckPerson( guest.Person, $"{name} guest {guest.Id}" );
					foreach ( var member in park.Staff )
						SaveNavigationTests.CheckPerson( member.Person, $"{name} staff {member.Id}" );
					import = $"{park.Guests.Count} guests and {park.Staff.Count} staff checked";
				}
				catch ( InvalidDataException exception )
				{
					import = $"park import refused ({exception.Message})";
				}
				Console.WriteLine( $"RSSE-CORPUS {name}: {rsse.Length} of {layout[SaveBlock.Rsse].Length} bytes consumed, {rsse.Records.Count} records; {import}" );
			}
			catch ( Exception exception ) when ( exception is not AssertInconclusiveException )
			{
				failures.Add( $"{name}: {exception.GetType().Name}: {exception.Message}" );
			}
		}
		Assert.AreEqual( 0, failures.Count, string.Join( Environment.NewLine, failures ) );
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
