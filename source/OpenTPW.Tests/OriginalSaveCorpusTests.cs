using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers.Binary;
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
		var files = CorpusFiles();
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
			Assert.AreEqual( (500u, 0x01221985u, false), (container.Magic, container.Stamp, container.IsThemeParkInc), name );
			var payload = reader.ReadFile();
			var layout = SavePayloadLayout.Parse( payload );
			Assert.AreEqual( 19, layout.Blocks.Count, name );

			if ( !maps.TryGetValue( theme, out var map ) )
				maps[theme] = map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( $"levels/{theme}/terrain.wad", "base.map" ) ) );
			// The RideSystem block on its own, before any import: it must have exactly one complete parse (Parse throws on none
			// or on several), and for every record whose object archive is known the control count the frame fixes must be the one tp.exe's
			// type entry holds, the object's max(1, UsageInfo.NumSimultAnims) (0x4629d0 -> 0x461f10 -> type entry +0xe -> instance +0xe).
			var standalone = SaveRideSystemBlock.Parse( payload, layout );
			Assert.AreEqual( standalone.Used, (uint)standalone.Records.Length, name );
			Assert.AreEqual( 0, standalone.TableControlCounts, name );
			var catalog = ObjectCatalog.Load( theme );
			Func<int, int?> table = infoId => catalog.Find( infoId )?.NumSimultaneousAnimations;
			var resolved = 0;
			foreach ( var record in standalone.Records )
			{
				if ( table( record.InfoId ) is { } count )
				{
					Assert.AreEqual( count, record.Controls.Length, $"{name}: slot {record.Slot} (Info.Id {record.InfoId}) control count" );
					resolved++;
				}
				else
				{
					// Types the object archives do not declare (hoardings and the like) are registered with a literal count of 1
					// (tp.exe 0x41437e and 0x43015b pass 1 as the 0x4629d0 count); no corpus record of that kind has more.
					Assert.AreEqual( 1, record.Controls.Length, $"{name}: slot {record.Slot} (Info.Id {record.InfoId}) has no object archive" );
				}
			}
			Assert.IsTrue( resolved > 0, name );
			var pinned = SaveRideSystemBlock.Parse( payload, layout, table );
			Assert.AreEqual( resolved, pinned.TableControlCounts, name );
			CollectionAssert.AreEqual( standalone.Records.Select( record => (record.Slot, record.InfoId, record.Controls.Length) ).ToArray(),
				pinned.Records.Select( record => (record.Slot, record.InfoId, record.Controls.Length) ).ToArray(), name );

			// The production import (OriginalPark) supplies the object catalogue's control counts itself.
			var production = OriginalPark.ImportOriginalSave( theme, payload, map );
			Assert.AreEqual( resolved, production.RideSystem!.TableControlCounts, $"{name}: production control-count table" );
			CollectionAssert.AreEqual( standalone.Records.Select( record => (record.Slot, record.InfoId, record.Controls.Length) ).ToArray(),
				production.RideSystem.Records.Select( record => (record.Slot, record.InfoId, record.Controls.Length) ).ToArray(), name );
			var park = OriginalParkImport.Import( payload, map );
			Assert.AreEqual( 0, park.UnresolvedObjects.Count, name );
			// The framed RideSystem walk runs on every save: each block has one complete parse, with the routed records
			// matching the placed/fixed counts.
			Assert.IsNotNull( park.RideSystem, name );
			Assert.AreEqual( park.RideSystem!.Used, (uint)park.RideSystem.Records.Length, name );
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
		var files = CorpusFiles();
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

	/// <summary>
	/// Each corpus attraction's time stamp, read in the loader's order (tp.exe 0x4DC8A1 rebuilds it without tv[3]), holds that date's day of the week in its fourth word, and the
	/// newest one is the park analyser's mLastObjectBuilt to the 100 ns: no corpus park demolished anything after building its newest object. The economy import dates that
	/// object at the same park turn.
	/// </summary>
	[TestMethod]
	public void OriginalSavesStampTheirNewestAttractionAtTheAnalysersLastConstruction()
	{
		var files = CorpusFiles();
		UseOriginalData();
		foreach ( var (path, theme) in files )
		{
			var name = $"{theme}/{Path.GetFileName( path )}";
			using var reader = new SaveReader( path );
			var payload = reader.ReadFile();
			var park = OriginalParkImport.Import( payload, new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( $"levels/{theme}/terrain.wad", "base.map" ) ) ) );
			foreach ( var attraction in park.Attractions )
				Assert.AreEqual( (uint)attraction.Timestamp.ToDateTime().DayOfWeek, attraction.Timestamp.DayOfWeek, $"{name}: thing {attraction.Id} {attraction.Timestamp}" );
			// [BIN:TPW-EXE:0x4C5D70 analyser serializer] the body ends with six 8-byte stamps (mLastStrike, mLastPuke, mLastPrank, mParkLastOpened, mLastObjectBuilt +0x21C30, mLastStaffHired) and three words
			var analyser = park.World.Find( park.World.Variables.ParkAnalyser )!.Value;
			var lastObjectBuilt = BinaryPrimitives.ReadInt64LittleEndian( payload.AsSpan( analyser.BodyOffset + SaveParkStatistics.BodyBytes - 3 * 4 - 2 * 8, 8 ) );
			var newest = park.Attractions.Max( attraction => attraction.Timestamp.ToDateTime() );
			Assert.AreEqual( lastObjectBuilt, newest.ToFileTimeUtc(), $"{name}: newest attraction {newest:yyyy-MM-dd HH:mm:ss}, mLastObjectBuilt {DateTime.FromFileTimeUtc( lastObjectBuilt ):yyyy-MM-dd HH:mm:ss}" );

			var economy = new ParkEconomy( BalanceSettings.Load( theme, easy: false ), EconomyObjectCatalog.Load( theme, easy: false ), ParkGameMode.FullSimulation, 1 );
			OriginalEconomyImport.Apply( economy, park );
			var seconds = (lastObjectBuilt - SaveCalendar.InitialFunnyTimeStart) / TimeSpan.TicksPerSecond;
			Assert.AreEqual( ParkCalendar.TickAtSeconds( seconds ), economy.Objects.Max( item => item.BuiltTick ), $"{name}: the newest object is dated at the last construction, {seconds} park seconds after 2000-01-01" );
		}
	}

	/// <summary>The corpus saves in path order with their theme folders; inconclusive without <c>OPENTPW_TPWS_SAVES</c> or saves in it.</summary>
	private static List<(string Path, string Theme)> CorpusFiles()
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
		return files;
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
