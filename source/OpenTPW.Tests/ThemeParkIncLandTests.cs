using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's owned land (docs/reverse/TPI-BOUNDARY.md): the land loader 0x678B00 on synthetic files, the new world's land against the prebuilt Science park's saved flags,
/// the owned counts each zone reaches through its extents, the cell validator's refusal of unowned land and the catalogue's mission gate. The data tests need OPENTPW_TPI_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncLandTests
{
	private BaseFileSystem? previous;

	[TestInitialize]
	public void Initialize() => previous = FileSystem;

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previous!;
		GameEditions.Select( GameEdition.ThemeParkWorld );
	}

	private static void UseTpiData()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		ObjectCatalog.BonusDataRoot = null;
	}

	/// <summary>A map with every cell unowned, as the world init leaves the squares without NoModify.</summary>
	private static ParkCellMap Unowned()
	{
		var cells = new ParkCellMap( 128, 128 );
		for ( var y = 0; y < 128; y++ )
			for ( var x = 0; x < 128; x++ )
				cells.SetFlags( x, y, ParkCellFlags.Unowned );
		return cells;
	}

	private static byte[] AllOnes() => Enumerable.Repeat( (byte)1, ThemeParkIncLand.MapFileBytes ).ToArray();

	[TestMethod]
	public void TheLoaderReadsXMajorAndClearsOnlyZeroBytesOffTheBorder()
	{
		var cells = Unowned();
		var bytes = AllOnes();
		bytes[5 * 128 + 9] = 0;       // byte k is (k / 128, k % 128): cell (5, 9)
		bytes[0 * 128 + 9] = 0;       // x = 0: border
		bytes[127 * 128 + 9] = 0;     // x = 127: border
		bytes[5 * 128 + 0] = 0;       // y = 0: border
		bytes[5 * 128 + 127] = 0;     // y = 127: border
		bytes[6 * 128 + 7] = 2;       // a value other than 0 does nothing
		Assert.IsTrue( ThemeParkIncLand.ApplyLandFile( cells, bytes ) );
		Assert.AreEqual( 1, ThemeParkIncLand.OwnedCount( cells ) );
		Assert.IsFalse( cells.FlagsAt( 5, 9 ).HasFlag( ParkCellFlags.Unowned ) );
		Assert.IsTrue( cells.FlagsAt( 9, 5 ).HasFlag( ParkCellFlags.Unowned ), "the file is transposed against the map's row order" );
	}

	[TestMethod]
	public void TheLoaderNeverTakesLandAwayAndKeepsTheCellsReadBeforeAShortEnd()
	{
		var cells = Unowned();
		cells.SetFlags( 40, 40, ParkCellFlags.None );
		Assert.IsTrue( ThemeParkIncLand.ApplyLandFile( cells, AllOnes() ) );
		Assert.IsFalse( cells.FlagsAt( 40, 40 ).HasFlag( ParkCellFlags.Unowned ), "a 1 byte does not set 0x40" );
		var shortFile = new byte[3 * 128 + 10];
		Assert.IsFalse( ThemeParkIncLand.ApplyLandFile( cells, shortFile ), "a short file fails" );
		// the cells read before the end are applied: x = 1 and 2 (y 1..126) and x = 3, y = 1..9
		Assert.AreEqual( 1 + 2 * 126 + 9, ThemeParkIncLand.OwnedCount( cells ) );
	}

	[TestMethod]
	public void TheNewWorldMarksEverySquareButNoModifyAndOpensTheGateStrip()
	{
		var cells = new ParkCellMap( 128, 128 );
		cells.SetFlags( 20, 20, ParkCellFlags.NoModify );
		Assert.IsTrue( ThemeParkIncLand.SetUpNewWorld( cells, (47, 17), (48, 17), AllOnes() ) );
		// post B = (43, 17), post A = (51, 17): x from 45 to 50 on rows 17 and 18, and the NoModify square
		Assert.AreEqual( 6 * 2 + 1, ThemeParkIncLand.OwnedCount( cells ) );
		foreach ( var x in Enumerable.Range( 45, 6 ) )
			Assert.IsFalse( cells.FlagsAt( x, 17 ).HasFlag( ParkCellFlags.Unowned ) || cells.FlagsAt( x, 18 ).HasFlag( ParkCellFlags.Unowned ) );
		Assert.IsTrue( cells.FlagsAt( 44, 17 ).HasFlag( ParkCellFlags.Unowned ) && cells.FlagsAt( 51, 17 ).HasFlag( ParkCellFlags.Unowned ) );
	}

	private static ParkCellMap ZoneStart( string level, bool readShippedSave ) =>
		Level.OriginalCellMap( OriginalPark.Load( level, readShippedSave ), GuestSettings.Load( level ), null );

	private static bool[] UnownedMask( ParkCellMap cells ) =>
		Enumerable.Range( 0, 128 * 128 ).Select( index => cells.FlagsAt( index % 128, index / 128 ).HasFlag( ParkCellFlags.Unowned ) ).ToArray();

	[TestMethod]
	public void AFreshScienceWorldOwnsExactlyThePrebuiltParksLand()
	{
		UseTpiData();
		var prebuilt = ZoneStart( "science", readShippedSave: true );
		var fresh = ZoneStart( "science", readShippedSave: false );
		Assert.AreEqual( 1259, ThemeParkIncLand.OwnedCount( prebuilt ) );
		CollectionAssert.AreEqual( UnownedMask( prebuilt ), UnownedMask( fresh ), "the init, gate strip and hoardings.map rebuild the prebuilt park's saved flags 0x40" );
	}

	/// <summary>
	/// The owned squares of each zone's new world and after each extent of its EndingTheme missions (the missions with Completion.LoadNewHoardings 1). The campaign plan's
	/// counts (science 1259, 3066, 4503, 6472, 7640) are every 0 byte of the files; the loader skips the map border (0x678B80), so the extents' border zeros stay unowned.
	/// </summary>
	[TestMethod]
	public void EachZoneGrowsThroughItsExtents()
	{
		UseTpiData();
		var zones = new (string Level, int[] Extents, int[] Owned, int[] PlanZeros)[]
		{
			("science", new[] { 0, 1, 8, 9 }, new[] { 1259, 3030, 4391, 6360, 7500 }, new[] { 1259, 3066, 4503, 6472, 7640 }),
			("water", new[] { 3, 11, 12, 13 }, new[] { 1330, 2456, 4042, 6225, 7500 }, new[] { 1343, 2523, 4154, 6337, 7640 }),
			("arabian", new[] { 5, 6, 15, 16 }, new[] { 2323, 3476, 4723, 6325, 7500 }, new[] { 2323, 3517, 4835, 6465, 7640 }),
		};
		foreach ( var (level, extents, owned, planZeros) in zones )
		{
			var world = ZoneStart( level, readShippedSave: false );
			var zeros = ZeroBytes( FileSystem.ReadAllBytes( ThemeParkIncLand.HoardingsMapPath( level ) ) );
			Assert.AreEqual( owned[0], ThemeParkIncLand.OwnedCount( world ), level );
			Assert.AreEqual( planZeros[0], zeros.Count, level );
			for ( var step = 0; step < extents.Length; step++ )
			{
				Assert.IsTrue( ThemeParkIncLand.ApplyExtent( world, extents[step] ), $"{level} extent {extents[step]}" );
				zeros.UnionWith( ZeroBytes( FileSystem.ReadAllBytes( ThemeParkIncLand.ExtentPath( extents[step] ) ) ) );
				Assert.AreEqual( owned[step + 1], ThemeParkIncLand.OwnedCount( world ), $"{level} after extent {extents[step]}" );
				Assert.AreEqual( planZeros[step + 1], zeros.Count, $"{level} plan count after extent {extents[step]}" );
			}
		}
		Assert.IsFalse( ThemeParkIncLand.ApplyExtent( Unowned(), 2 ), "mission 2 loads no hoardings and has no extent file" );
	}

	private static System.Collections.Generic.HashSet<int> ZeroBytes( byte[] bytes ) =>
		Enumerable.Range( 0, bytes.Length ).Where( index => bytes[index] == 0 ).ToHashSet();

	[TestMethod]
	public void ThePathToolRefusesUnownedLandOfATpiWorld()
	{
		UseTpiData();
		var cells = ZoneStart( "water", readShippedSave: false );
		var grid = new GuestPathGrid( cells );
		var paths = new ParkPathBuilder( cells, grid, () => null, new SandboxParkGrid() );
		(int X, int Y) Find( bool unowned ) => Enumerable.Range( 0, 128 * 128 ).Select( index => (X: index % 128, Y: index / 128) )
			.First( cell => cell.X is > 0 and < 127 && cell.Y is > 0 and < 127 && cells.RawTypeAt( cell.X, cell.Y ) == 0
				&& cells.FlagsAt( cell.X, cell.Y ).HasFlag( ParkCellFlags.Unowned ) == unowned );
		var outside = Find( true );
		var inside = Find( false );
		Assert.AreEqual( CellBuildResult.NotOwned, paths.CheckCell( outside.X, outside.Y ) );
		Assert.AreEqual( CellBuildResult.Ok, paths.CheckCell( inside.X, inside.Y ) );
		Assert.IsTrue( ThemeParkIncLand.ApplyExtent( cells, 3 ) );
		var grown = Enumerable.Range( 0, 128 * 128 ).Select( index => (X: index % 128, Y: index / 128) ).Count( cell => !cells.FlagsAt( cell.X, cell.Y ).HasFlag( ParkCellFlags.Unowned ) );
		Assert.AreEqual( 2456, grown );
	}

	[TestMethod]
	public void TheCatalogueListsThinkingSpacesFromMissionOneAndTrainingRoomsFromMissionTwo()
	{
		// (isSpecial, upgradesId, upgradesAnyRide, thinkingSpace, trainingRoom, mission, researchDone)
		Assert.IsTrue( ThemeParkIncCatalogue.IsListed( 0, 0, 0, 0, 0, 0, false ) );
		Assert.IsFalse( ThemeParkIncCatalogue.IsListed( 1, 0, 0, 0, 0, 5, true ), "a special object is never listed" );
		Assert.IsFalse( ThemeParkIncCatalogue.IsListed( 0, 1150, 0, 0, 0, 5, true ), "an add-on is never listed" );
		Assert.IsFalse( ThemeParkIncCatalogue.IsListed( 0, 0, 1, 0, 0, 5, true ), "an any-ride add-on is never listed" );
		Assert.IsFalse( ThemeParkIncCatalogue.IsListed( 0, 0, 0, 1, 0, 0, true ), "a thinking space waits for mission 1" );
		Assert.IsTrue( ThemeParkIncCatalogue.IsListed( 0, 0, 0, 1, 0, 1, false ) );
		Assert.IsTrue( ThemeParkIncCatalogue.IsListed( 0, 0, 0, 2, 0, 0, false ), "only ThinkingSpace 1 is gated" );
		Assert.IsFalse( ThemeParkIncCatalogue.IsListed( 0, 0, 0, 0, 1, 0, true ), "never a training room in mission 0" );
		Assert.IsFalse( ThemeParkIncCatalogue.IsListed( 0, 0, 0, 0, 1, 1, false ), "mission 1 waits for the obstacle research" );
		Assert.IsTrue( ThemeParkIncCatalogue.IsListed( 0, 0, 0, 0, 1, 1, true ) );
		Assert.IsTrue( ThemeParkIncCatalogue.IsListed( 0, 0, 0, 0, 1, 2, false ) );
	}

	[TestMethod]
	public void ScienceListsNoSpecialRideInMissionZero()
	{
		UseTpiData();
		var catalog = ObjectCatalog.Load( "science" );
		var gated = new OriginalBuildCatalog( catalog, entry => ThemeParkIncCatalogue.IsListed( entry, 0, false ) );
		var all = new OriginalBuildCatalog( catalog );
		var listed = Enum.GetValues<BuildCategory>().SelectMany( gated.GetItems ).Select( item => item.InfoId ).ToHashSet();
		var hidden = Enum.GetValues<BuildCategory>().SelectMany( all.GetItems ).Select( item => item.InfoId ).Where( id => !listed.Contains( id ) ).OrderBy( id => id ).ToArray();
		CollectionAssert.AreEqual( new[] { 1110, 1160, 1170, 1180 }, hidden, "Info.IsSpecial: the Victorian house, the water ride, the tour ride and the log mill" );
		Assert.IsTrue( listed.Count > 0 );
		// the research lab (1404, UsageInfo.ThinkingSpace 1) and the training room (1440, UsageInfo.TrainingRoom 1) are Facilities (Info.WhichUIType 5), which the slice's catalogue does not load
		Assert.IsFalse( catalog.Entries.Any( entry => entry.InfoId is 1404 or 1440 ) );
	}

	[TestMethod]
	public void AFencePostRefusesTheWholeFileBeforeAnyCellChanges()
	{
		var cells = Unowned();
		cells.Tracks.Set( 60, 60, new TrackCell { Type = ParkCellTypes.Hoarding } );
		var bytes = AllOnes();
		bytes[5 * 128 + 9] = 0;
		bytes[60 * 128 + 60] = 0;
		Assert.ThrowsException<InvalidOperationException>( () => ThemeParkIncLand.ApplyLandFile( cells, bytes ) );
		Assert.AreEqual( 0, ThemeParkIncLand.OwnedCount( cells ), "the cell read before the post stays unowned" );
	}

	[TestMethod]
	public void TheLayoutKeepsTpiOwnershipAsOneBitMapAndRefusesSavesWithoutIt()
	{
		var cells = Unowned();
		Assert.IsTrue( ThemeParkIncLand.ApplyLandFile( cells, Enumerable.Range( 0, ThemeParkIncLand.MapFileBytes ).Select( index => (byte)(index % 3 == 0 ? 0 : 1) ).ToArray() ) );
		cells.SetFlags( 10, 10, cells.FlagsAt( 10, 10 ) | ParkCellFlags.NoModify );
		var objects = new ParkObjects( null!, new SandboxParkGrid() );
		Assert.IsNull( ParkLayoutSnapshot.CaptureSnapshot( cells, objects ).Cells.TpiUnowned, "a map without Theme Park Inc's land keeps the cell records' bits, whatever the edition" );
		cells.HasThemeParkIncLand = true;
		var snapshot = ParkLayoutSnapshot.CaptureSnapshot( cells, objects );
		Assert.IsNotNull( snapshot.Cells.TpiUnowned );
		Assert.IsNull( snapshot.Cells.Land );
		Assert.AreEqual( 1, snapshot.Cells.Cells.Count, "only the NoModify cell is recorded; the unowned bit lives in the bit map" );
		var restored = ParkLayoutSnapshot.RestoreCellMap( snapshot );
		CollectionAssert.AreEqual( UnownedMask( cells ), UnownedMask( restored ) );
		Assert.AreEqual( cells.FlagsAt( 10, 10 ), restored.FlagsAt( 10, 10 ) );
		ParkResumeSaveFile.RequireThemeParkIncLand( snapshot );
		var old = snapshot with { Cells = snapshot.Cells with { TpiUnowned = null } };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.RequireThemeParkIncLand( old ) ).Message, ParkResumeSaveFile.ThemeParkIncLandMissing );
		Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( snapshot with { Cells = snapshot.Cells with { TpiUnowned = "AAAA" } } ), "a bit map of the wrong size is refused" );
		Assert.IsTrue( restored.HasThemeParkIncLand, "a restored map keeps its land's encoding" );
	}
}
