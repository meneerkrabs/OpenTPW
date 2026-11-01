using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using Veldrid;

namespace OpenTPW.Tests;

/// <summary>The map window's cell classes, layers and outlines (tp.exe 0x5F2050 writes the classes, 0x5F2380 draws them; UI-086).</summary>
[TestClass]
public class MapClassTests
{
	private sealed class Cells : IParkMapCells
	{
		public int Width { get; init; } = 6;
		public int Height { get; init; } = 4;
		public Dictionary<(int, int), byte> Types { get; } = new();
		public HashSet<(int, int)> UnownedCells { get; } = new();
		public HashSet<(int, int)> Posts { get; } = new();
		public HashSet<(int, int)> Tiles { get; } = new();
		public Dictionary<(int, int), ParkMapThing[]> TileOwners { get; } = new();
		public Dictionary<(int, int), ParkMapThing[]> Owners { get; } = new();
		public byte Type( int x, int y ) => Types.GetValueOrDefault( (x, y) );
		public bool Unowned( int x, int y ) => UnownedCells.Contains( (x, y) );
		public bool FencePost( int x, int y ) => Posts.Contains( (x, y) );
		public bool TrackStructure( int x, int y ) => Tiles.Contains( (x, y) );
		public IEnumerable<ParkMapThing> TrackOwners( int x, int y ) => TileOwners.GetValueOrDefault( (x, y) ) ?? [];
		public IEnumerable<ParkMapThing> ThingsOfOwnerWord( int x, int y ) => Owners.GetValueOrDefault( (x, y) ) ?? [];
	}

	private static ParkMapThing Thing( int id, int category, bool relief = false, bool litter = false, bool security = false, bool room = false ) =>
		new() { Id = id, Category = category, Relief = relief, Litter = litter, Security = security, StaffRoom = room };

	private static (int[] Classes, ParkMapThing?[] Owners) Write( Cells cells )
	{
		var classes = new int[cells.Width * cells.Height];
		var owners = new ParkMapThing?[classes.Length];
		ParkMapClasses.Write( cells, classes, owners );
		return (classes, owners);
	}

	[TestMethod]
	public void ObjectFreeCellsTakeThePathQueueAndUnownedClasses()
	{
		var cells = new Cells();
		cells.Types[(0, 0)] = (byte)ParkCellType.Path;
		cells.Types[(1, 0)] = (byte)ParkCellType.Queue;
		cells.UnownedCells.Add( (2, 0) );
		cells.Types[(3, 0)] = (byte)ParkCellType.Path;
		cells.UnownedCells.Add( (3, 0) );
		cells.Types[(4, 0)] = ParkCellTypes.Water;
		var (classes, owners) = Write( cells );
		Assert.AreEqual( ParkMapClasses.Path, classes[0] );
		Assert.AreEqual( ParkMapClasses.Queue, classes[1] );
		Assert.AreEqual( ParkMapClasses.Unowned, classes[2] );
		Assert.AreEqual( ParkMapClasses.Path, classes[3], "a path wins over the unowned flag: the flag test is the last" );
		Assert.AreEqual( ParkMapClasses.None, classes[4], "water has no class" );
		Assert.IsTrue( owners.All( owner => owner == null ) );
	}

	[TestMethod]
	public void ACellOfAnObjectTakesTheClassOfItsOwnersKind()
	{
		var cells = new Cells();
		ParkMapThing[] things = { Thing( 1, 0 ), Thing( 2, 1 ), Thing( 3, 2 ), Thing( 4, 3 ), Thing( 5, 3, relief: true ), Thing( 6, 3, litter: true ),
			Thing( 7, 3, security: true ), Thing( 8, 3, room: true ), Thing( 9, 3, relief: true, security: true, room: true ) };
		for ( var index = 0; index < 6; index++ )
		{
			cells.Types[(index, 0)] = ParkCellTypes.Object;
			cells.Owners[(index, 0)] = new[] { things[index] };
		}
		for ( var index = 0; index < 3; index++ )
		{
			cells.Types[(index, 1)] = ParkCellTypes.Object;
			cells.Owners[(index, 1)] = new[] { things[6 + index] };
		}
		var (classes, owners) = Write( cells );
		CollectionAssert.AreEqual( new[] { 1, 2, 3, 4, 5, 5 }, classes.Take( 6 ).ToArray() );
		CollectionAssert.AreEqual( new[] { 6, 7, 5 }, classes.Skip( 6 ).Take( 3 ).ToArray(), "flag 0x41 is tested first, then 0x10, then 2" );
		Assert.AreSame( things[0], owners[0] );
		Assert.AreSame( things[8], owners[8] );
	}

	[TestMethod]
	public void EntranceAndExitCellsBelongToTheirObjectAndTheMarkerAndTileCellsTakeClassEight()
	{
		var cells = new Cells();
		var ride = Thing( 1, 0 );
		cells.Types[(0, 0)] = ParkCellTypes.Entrance;
		cells.Types[(1, 0)] = ParkCellTypes.Exit;
		cells.Types[(2, 0)] = ParkCellTypes.TrackConnection;
		cells.Types[(3, 0)] = ParkCellTypes.Object;
		cells.Tiles.Add( (4, 0) );
		cells.TileOwners[(4, 0)] = new[] { ride };
		for ( var x = 0; x < 4; x++ )
			cells.Owners[(x, 0)] = new[] { ride };
		var (classes, owners) = Write( cells );
		Assert.AreEqual( 1, classes[0] );
		Assert.AreEqual( 1, classes[1] );
		Assert.AreEqual( 8, classes[2], "a type 0x15 cell of a ride is class 8 (0x5363e0)" );
		Assert.AreEqual( 1, classes[3], "a type 4 cell is class 1" );
		Assert.AreEqual( 8, classes[4], "a kart or water tile of a ride is class 8 (0x5363f0)" );
		Assert.AreSame( ride, owners[4] );
	}

	[TestMethod]
	public void ACellWithoutAnOwnerOfAKnownKindHasNoClassAndAFenceRecordWinsOverEverything()
	{
		var cells = new Cells();
		cells.Types[(0, 0)] = ParkCellTypes.Object;
		cells.Types[(1, 0)] = ParkCellTypes.Object;
		cells.Owners[(1, 0)] = new[] { Thing( 1, 4 ) };
		cells.Types[(2, 0)] = ParkCellTypes.Object;
		cells.Owners[(2, 0)] = new[] { Thing( 2, 4 ), Thing( 3, 1 ) };
		cells.Types[(3, 0)] = (byte)ParkCellType.Path;
		cells.Posts.Add( (3, 0) );
		cells.Types[(4, 0)] = ParkCellTypes.Object;
		cells.Owners[(4, 0)] = new[] { Thing( 4, 2 ) };
		cells.Posts.Add( (4, 0) );
		var (classes, _) = Write( cells );
		Assert.AreEqual( ParkMapClasses.None, classes[0], "no thing on the owner's cell" );
		Assert.AreEqual( ParkMapClasses.None, classes[1], "a fixed item (kind 4) has no class" );
		Assert.AreEqual( 2, classes[2], "the next thing on the list takes the cell when the first has no class" );
		Assert.AreEqual( ParkMapClasses.Fence, classes[3] );
		Assert.AreEqual( ParkMapClasses.Fence, classes[4] );
	}

	[DataTestMethod]
	[DataRow( 1, 1u, 0, true )]
	[DataRow( 1, 2u, 0, false )]
	[DataRow( 2, 2u, 0, true )]
	[DataRow( 3, 4u, 0, true )]
	[DataRow( 4, 8u, 0, true )]
	[DataRow( 4, 7u, 0, false )]
	[DataRow( 5, 0u, 1, true )]
	[DataRow( 5, 0u, 3, false )]
	[DataRow( 6, 0u, 3, true )]
	[DataRow( 6, 0u, 1, false )]
	[DataRow( 7, 0u, 4, true )]
	[DataRow( 7, 0u, 0, false )]
	[DataRow( 8, 0x10u, 0, true )]
	[DataRow( 8, 0x2Fu, 0, false )]
	[DataRow( 9, 0u, 0, true )]
	[DataRow( 10, 0u, 0, true )]
	[DataRow( 11, 0u, 0, true )]
	[DataRow( 12, 0u, 0, true )]
	[DataRow( 0, 0xFFu, 4, false )]
	public void EachClassIsShownByItsLayerBitOrStaffRole( int cls, uint layers, int staff, bool shown ) =>
		Assert.AreEqual( shown, ParkMapClasses.Shown( cls, layers, staff ) );

	private static ParkMapCanvas Canvas( Cells cells, bool original = true )
	{
		var canvas = new ParkMapCanvas( new MapSurface( cells.Width, cells.Height, original ) );
		canvas.WriteClasses( cells );
		return canvas;
	}

	private static Cells TwoRides( out ParkMapThing first, out ParkMapThing second )
	{
		first = Thing( 1, 0 );
		second = Thing( 2, 0 );
		var cells = new Cells { Width = 8, Height = 8 };
		foreach ( var (x, y, thing) in new[] { (1, 1, first), (2, 1, first), (3, 1, second) } )
		{
			cells.Types[(x, y)] = ParkCellTypes.Object;
			cells.Owners[(x, y)] = new[] { thing };
		}
		return cells;
	}

	[TestMethod]
	public void ValueModesColourClassesOneTwoThreeAndEightWhateverTheLayersSayAndTheFeaturesStayOnTheirLayer()
	{
		var shop = Thing( 1, 1 );
		shop.Excitement = 100;
		shop.Satisfaction = 0;
		shop.Repair = new RgbaByte( 1, 2, 3, 255 );
		var feature = Thing( 2, 3 );
		feature.Excitement = 100;
		feature.Repair = new RgbaByte( 4, 5, 6, 255 );
		var cells = new Cells();
		cells.Types[(0, 0)] = ParkCellTypes.Object;
		cells.Owners[(0, 0)] = new[] { shop };
		cells.Types[(1, 0)] = ParkCellTypes.Object;
		cells.Owners[(1, 0)] = new[] { feature };
		var canvas = Canvas( cells );
		Assert.IsNull( ParkMapCanvas.FillOf( 2, shop, layers: 0, staff: 0, valueMode: 1 ), "happiness is no cell mode and the shop layer is off" );
		Assert.AreEqual( new RgbaByte( 1, 2, 3, 255 ), ParkMapCanvas.FillOf( 2, shop, layers: 2, staff: 0, valueMode: 1 ) );
		Assert.AreEqual( ParkMapPalette.RepairRamp( 100 ), ParkMapCanvas.FillOf( 2, shop, layers: 0, staff: 0, valueMode: 2 ), "excitement ignores the layers" );
		Assert.AreEqual( ParkMapPalette.RepairRamp( 0 ), ParkMapCanvas.FillOf( 2, shop, layers: 0, staff: 0, valueMode: 3 ), "satisfaction too" );
		Assert.IsNull( ParkMapCanvas.FillOf( 4, feature, layers: 0, staff: 0, valueMode: 2 ), "a feature takes no value mode: its layer decides" );
		Assert.AreEqual( new RgbaByte( 4, 5, 6, 255 ), ParkMapCanvas.FillOf( 4, feature, layers: 8, staff: 0, valueMode: 2 ) );
		shop.Satisfaction = null;
		Assert.AreEqual( ParkMapPalette.UnavailableObject, ParkMapCanvas.FillOf( 2, shop, layers: 0, staff: 0, valueMode: 3 ), "a value the economy cannot give keeps the host fallback" );
		canvas.RenderCells( 0, 0, 2 );
		Assert.AreEqual( ParkMapPalette.RepairRamp( 100 ), canvas.Prims[0].Color );
	}

	[TestMethod]
	public void AnOutlineIsDrawnOnTheSidesWhereTheNeighbourIsAnotherClassOrObjectInTheOrderUpDownLeftRight()
	{
		var cells = TwoRides( out var first, out var second );
		first.Repair = new RgbaByte( 9, 9, 9, 255 );
		second.Repair = new RgbaByte( 8, 8, 8, 255 );
		second.Closed = true;
		var canvas = Canvas( cells );
		canvas.RenderCells( 1, 0, 0 );
		var cw = canvas.Surface.CellWidth;
		var prims = canvas.Prims.ToArray();
		// 8 x 8 cells of (int)(480 / 8) = 60 by (int)(425 / 8) = 53
		Assert.AreEqual( 60f, cw );
		Assert.AreEqual( 53f, canvas.Surface.CellHeight );
		var green = ParkMapPalette.OutlineOpen;
		var black = ParkMapPalette.OutlineClosed;
		// cell (1, 1): fill, then up, down, left (right has the same object next to it)
		var x0 = 15f + 60;
		var y0 = 42f + 53;
		Assert.AreEqual( new MapPrim( x0, y0, 60, 53, first.Repair ), prims[0] );
		Assert.AreEqual( new MapPrim( x0, y0, 60, 1, green ), prims[1] );
		Assert.AreEqual( new MapPrim( x0, y0 + 59, 60, 1, green ), prims[2], "the lower edge lies at y + the cell WIDTH - 1 whatever the height is" );
		Assert.AreEqual( new MapPrim( x0, y0, 1, 60, green ), prims[3], "the vertical edges are as long as the cell is wide" );
		// cell (2, 1): fill, up, down, right
		Assert.AreEqual( new MapPrim( x0 + 60, y0, 60, 53, first.Repair ), prims[4] );
		Assert.AreEqual( new MapPrim( x0 + 60, y0, 60, 1, green ), prims[5] );
		Assert.AreEqual( new MapPrim( x0 + 60, y0 + 59, 60, 1, green ), prims[6] );
		Assert.AreEqual( new MapPrim( x0 + 120 - 1, y0, 1, 60, green ), prims[7] );
		// the second object's cell (3, 1): its fill and all four edges (the left neighbour is the other object), in the closed style
		Assert.AreEqual( 13, prims.Length );
		Assert.AreEqual( second.Repair, prims[8].Color );
		Assert.IsTrue( prims.Skip( 9 ).All( prim => prim.Color == black ) );
	}

	[TestMethod]
	public void ObjectsOfEveryClassAreDrawnInRasterOrderAndOnlyTheLayersShowTheirFills()
	{
		var cells = TwoRides( out var first, out _ );
		cells.Types[(0, 0)] = (byte)ParkCellType.Path;
		cells.Types[(5, 0)] = (byte)ParkCellType.Queue;
		cells.Types[(6, 5)] = (byte)ParkCellType.Path;
		var canvas = Canvas( cells );
		canvas.RenderCells( 0, 0, 0 );
		var fills = canvas.Prims.Where( prim => prim.Height > 1 && prim.Width > 1 ).Select( prim => prim.Color ).ToArray();
		CollectionAssert.AreEqual( new[] { ParkMapPalette.Path, ParkMapPalette.Queue, ParkMapPalette.Path }, fills, "with every layer off only the object-free classes are filled, row by row" );
		Assert.AreEqual( 3 + 10, canvas.Prims.Count, "the outlines of the objects (ten edges) are drawn even when their fills are not" );
		Assert.AreSame( first, canvas.Owners[1 * 8 + 1] );
	}
}
