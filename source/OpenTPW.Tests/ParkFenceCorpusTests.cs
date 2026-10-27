using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The park fence and the net worth's cell points against the original saves (docs/reverse/ORACLE.md, F30; PATH-007, ECON-026): the Jungle Easymode save and the corpus of
/// <c>OPENTPW_TPWS_SAVES</c> (the four <c>restart.INTS</c> fresh parks and the <c>.TPWS</c> saves). Inconclusive without OPENTPW_GAME_PATH; nothing of the game data is written.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkFenceCorpusTests
{
	/// <summary>The cell points tp.exe's 0x4C7DB0 gives the saves' own cells (types, flags, hoardings), in the x86 oracle (tools/ppc-analysis/oracle_x86 net worth over the exported cells); no objects.</summary>
	private static readonly Dictionary<string, int> OracleCellPoints = new( StringComparer.OrdinalIgnoreCase )
	{
		["jungle/easymode"] = 19527,
		["fantasy/Wonder Land.TPWS"] = 32282,
		["fantasy/autosave.TPWS"] = 32282,
		["fantasy/restart.INTS"] = 14138,
		["hallow/Halloween World.TPWS"] = 32250,
		["hallow/autosave.TPWS"] = 32250,
		["hallow/restart.INTS"] = 14418,
		["jungle/Lost Kingdom.TPWS"] = 29864,
		["jungle/autosave.TPWS"] = 29864,
		["jungle/restart.INTS"] = 14438,
		["space/Space Zone.TPWS"] = 32038,
		["space/restart.INTS"] = 14422,
	};

	private BaseFileSystem? previous;

	[TestInitialize]
	public void Initialize()
	{
		previous = FileSystem;
		if ( ObjectCatalogCorpusTests.UseOriginalData( out _ ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game." );
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previous!;

	private static IEnumerable<(string Name, string Theme, OriginalParkImport Park)> Saves()
	{
		var easy = OriginalPark.Load( "jungle" );
		yield return ("jungle/easymode", "jungle", easy.Save!);
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPWS_SAVES" );
		if ( string.IsNullOrWhiteSpace( folder ) || !Directory.Exists( folder ) )
			yield break;
		var maps = new Dictionary<string, MapFile>();
		foreach ( var file in Directory.EnumerateFiles( folder, "*", SearchOption.AllDirectories ).OrderBy( path => path, StringComparer.Ordinal ) )
		{
			if ( Path.GetExtension( file ).ToUpperInvariant() is not (".TPWS" or ".INTS") )
				continue;
			var theme = Path.GetFileName( Path.GetDirectoryName( file ) )!.ToLowerInvariant();
			using var reader = new SaveReader( file );
			reader.Inspect();
			var payload = reader.ReadFile();
			if ( !maps.TryGetValue( theme, out var map ) )
				maps[theme] = map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( $"levels/{theme}/terrain.wad", "base.map" ) ) );
			yield return ($"{theme}/{Path.GetFileName( file )}", theme, OriginalParkImport.Import( payload, map ));
		}
	}

	private static ParkLandSetup Setup( string theme )
	{
		var settings = GuestSettings.Load( theme );
		return ParkLandSetup.FromLevel( settings.EntranceA, settings.EntranceB, FileSystem.ReadAllText( $"/levels/{theme}/Hoardings.sam" ) )!;
	}

	/// <summary>A cell map holding a save's own cell types, flags and fence records.</summary>
	private static ParkCellMap SavedCells( string theme, OriginalParkImport save )
	{
		var cells = new ParkCellMap( 128, 128 );
		for ( var y = 0; y < 128; y++ )
		{
			for ( var x = 0; x < 128; x++ )
			{
				cells.SetType( x, y, (ParkCellType)save.Cells[x, y].MapType );
				cells.SetFlags( x, y, (ParkCellFlags)save.Cells[x, y].MapFlags & ParkCellFlags.Unowned );
			}
		}
		cells.ImportLand( Setup( theme ), ( x, y ) => (save.Cells[x, y].MapFlags & (ushort)ParkCellFlags.Unowned) != 0, ParkCellMap.FenceOfSave( save.Cells, 128, 128 ) );
		return cells;
	}

	/// <summary>
	/// The cell points of the saved cells (their types, the unowned flag and the hoardings with their four-cell footprints) are what tp.exe's net worth gives them: the x86 oracle's values, with the
	/// cells of a save that carries a track structure left out (the corpus holds none).
	/// </summary>
	[TestMethod]
	public void TheCellPointsOfEverySaveAreTheOraclesNetWorth()
	{
		var seen = 0;
		foreach ( var (name, theme, save) in Saves() )
		{
			if ( !OracleCellPoints.TryGetValue( name, out var expected ) )
				continue;
			var park = EconomyTestData.Park();
			park.Litter.Cells = SavedCells( theme, save );
			Assert.AreEqual( expected, (int)park.ParkValue, name );
			seen++;
		}
		Assert.IsTrue( seen >= 1 );
	}

	/// <summary>
	/// The fence the setup lays on a theme's fresh terrain is the one of the original's fresh park (the <c>restart.INTS</c> saves): every cell's unowned flag and every fence record, cell for cell.
	/// </summary>
	[TestMethod]
	public void TheFillOfEveryFreshParkIsTheOriginalsCellForCell()
	{
		var seen = 0;
		foreach ( var (name, theme, save) in Saves().Where( entry => entry.Name.EndsWith( "restart.INTS", StringComparison.OrdinalIgnoreCase ) ) )
		{
			var park = OriginalPark.Load( theme, readShippedSave: false );
			var cells = ParkCellMap.FromOriginal( park.Map, null, Setup( theme ), park.Heightfield );
			Assert.IsNotNull( cells.Land, name );
			var layer = cells.Land!.LayerFor( 128, 128 );
			var expected = ParkCellMap.FenceOfSave( save.Cells, 128, 128 );
			var differences = new List<string>();
			for ( var y = 0; y < 128; y++ )
			{
				for ( var x = 0; x < 128; x++ )
				{
					var saved = (save.Cells[x, y].MapFlags & (ushort)ParkCellFlags.Unowned) != 0;
					if ( saved != cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ) )
						differences.Add( $"flag ({x},{y})" );
					if ( layer.TypeAt( x, y ) != expected.TypeAt( x, y ) || layer.LinksAt( x, y ) != expected.LinksAt( x, y ) || layer.OwnerAt( x, y ) != expected.OwnerAt( x, y ) || layer.DirectionAt( x, y ) != expected.DirectionAt( x, y ) )
						differences.Add( $"record ({x},{y}) {layer.TypeAt( x, y )}/{expected.TypeAt( x, y )}" );
				}
			}
			Assert.AreEqual( 0, differences.Count, $"{name}: {string.Join( "; ", differences.Take( 10 ) )}" );
			seen++;
		}
		if ( seen == 0 )
			Assert.Inconclusive( "Set OPENTPW_TPWS_SAVES to the corpus for the fresh parks." );
	}
}
