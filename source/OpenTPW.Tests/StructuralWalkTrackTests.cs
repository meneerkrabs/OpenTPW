using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class StructuralWalkTrackTests
{
	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 16;
		public int Height => 16;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private static (ParkCellMap Map, TrackBuilder Builder) Tile( TrackFamily family, bool overPath )
	{
		var map = new ParkCellMap( 16, 16 );
		var builder = new TrackBuilder( map, family, () => null, new FlatGrid() );
		Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( 4, 4 ) );
		// a path laid over the tile bridges it (the path tool's op 0x85, 0x534115)
		if ( overPath ) map.SetType( 5, 5, ParkCellType.Path );
		Assert.AreEqual( overPath, (map.Tracks.Get( 4, 4 ).Flags & TrackCell.BridgeMask) != 0 );
		return (map, builder);
	}

	[DataTestMethod]
	[DataRow( TrackFamily.Kart )]
	[DataRow( TrackFamily.Water )]
	public void ActualGroundTilesBlockOriginAndExtensionsBeforeOffPathFallthrough( TrackFamily family )
	{
		var (map, _) = Tile( family, false );
		foreach ( var mask in new[] { 0, 1, 2 } )
		{
			Assert.IsTrue( WalkEdges.Blocked( map, 3, 4, 1, mask ), "Off-path source must not cross the ground origin." );
			Assert.IsTrue( WalkEdges.Blocked( map, 3, 5, 1, mask ), "An extension reads its origin's flags." );
			Assert.IsFalse( WalkEdges.Commit( map, (3, 4), (4, 4), mask ) );
		}
		map.SetType( 3, 4, ParkCellType.Path );
		Assert.IsTrue( WalkEdges.Blocked( map, 3, 4, 1, 1 ), "Mask 1 must not bypass the structural check." );
		Assert.IsFalse( WalkEdges.Blocked( map, 4, 4, 3, 0 ), "The earlier non-queue-to-path branch remains open." );
	}

	[DataTestMethod]
	[DataRow( TrackFamily.Kart )]
	[DataRow( TrackFamily.Water )]
	public void ActualOverPathBridgeAllowsExistingOffPathAndMaskOneRules( TrackFamily family )
	{
		var (map, _) = Tile( family, true );
		Assert.IsFalse( WalkEdges.Blocked( map, 3, 4, 1, 0 ) );
		Assert.IsFalse( WalkEdges.Blocked( map, 3, 5, 1, 0 ) );
		map.SetType( 3, 4, ParkCellType.Path );
		Assert.IsFalse( WalkEdges.Blocked( map, 3, 4, 1, 1 ) );
		Assert.IsTrue( WalkEdges.Blocked( map, 3, 4, 1, 0 ), "Nonzero bridge flags do not bypass later mask rules." );
	}

	[TestMethod]
	public void AllSupportedOriginsUseOnlyTheLowNibble()
	{
		foreach ( var type in new byte[] { 11, 13, 16, 18, 25 } )
			foreach ( var flags in new byte[] { 0, 1, 2, 4, 8, 15, 16, 32, 128, 255 } )
			{
				var map = new ParkCellMap( 16, 16 );
				map.SetType( 4, 4, type );
				map.Tracks.Set( 4, 4, new TrackCell { Type = type, Flags = flags, Parent = -1 } );
				Assert.AreEqual( (flags & 15) == 0, WalkEdges.Blocked( map, 3, 4, 1, 1 ), $"type {type}, flags {flags}" );
				map.Tracks.Set( 4, 5, new TrackCell { Type = 12, Flags = 255, Parent = map.Tracks.IndexOf( 4, 4 ) } );
				Assert.AreEqual( (flags & 15) == 0, WalkEdges.Blocked( map, 3, 5, 1, 1 ), "Extensions read parent flags, not their own flags." );
			}
	}

	[TestMethod]
	public void UnsupportedAndInvalidParentsKeepTheExistingFallthrough()
	{
		foreach ( var parent in new[] { -1, 256, int.MaxValue } )
		{
			var map = new ParkCellMap( 16, 16 );
			map.Tracks.Set( 0, 0, new TrackCell { Type = 11, Parent = -1 } );
			map.SetType( 4, 4, TrackCellTypes.Extension );
			map.Tracks.Set( 4, 4, new TrackCell { Type = 12, Parent = parent } );
			Assert.IsFalse( WalkEdges.Blocked( map, 3, 4, 1, 1 ), $"parent {parent}" );
		}
		// the parent word is the cell index plus one, so cell (0, 0) is a real parent (0x4D89E4) and only a missing word (-1 here) is no track
		var first = new ParkCellMap( 16, 16 );
		first.Tracks.Set( 0, 0, new TrackCell { Type = 11, Parent = -1 } );
		first.SetType( 4, 4, TrackCellTypes.Extension );
		first.Tracks.Set( 4, 4, new TrackCell { Type = 12, Parent = 0 } );
		Assert.IsTrue( WalkEdges.Blocked( first, 3, 4, 1, 1 ), "parent index 0 is cell (0, 0)" );
		foreach ( var parentType in new byte[] { 0, 12, 17, 99 } )
		{
			var map = new ParkCellMap( 16, 16 );
			map.Tracks.Set( 6, 6, new TrackCell { Type = parentType, Parent = map.Tracks.IndexOf( 4, 4 ) } );
			map.Tracks.Set( 4, 4, new TrackCell { Type = 12, Parent = map.Tracks.IndexOf( 6, 6 ) } );
			Assert.IsFalse( WalkEdges.Blocked( map, 3, 4, 1, 1 ), "An unsupported parent is not recursively interpreted." );
		}
		var last = new ParkCellMap( 16, 16 );
		last.Tracks.Set( 15, 15, new TrackCell { Type = 11, Parent = -1 } );
		last.Tracks.Set( 4, 4, new TrackCell { Type = 12, Parent = 255 } );
		Assert.IsTrue( WalkEdges.Blocked( last, 3, 4, 1, 1 ), "The final valid parent index is accepted." );
		var unsupported = new ParkCellMap( 16, 16 );
		unsupported.Tracks.Set( 4, 4, new TrackCell { Type = 17, Parent = 1 } );
		Assert.IsFalse( WalkEdges.Blocked( unsupported, 3, 4, 1, 1 ) );
		Assert.IsTrue( WalkEdges.Blocked( unsupported, 0, 0, 3, 1 ) );
		Assert.IsTrue( WalkEdges.Blocked( unsupported, 15, 4, 1, 1 ) );
	}

	[TestMethod]
	public void EarlierPathQueueAndPoolBranchesRetainTheirPriority()
	{
		var (map, _) = Tile( TrackFamily.Kart, false );
		map.SetType( 4, 4, ParkCellType.Path );
		Assert.IsFalse( WalkEdges.Blocked( map, 3, 4, 1, 0 ) );
		map.SetType( 3, 4, ParkCellType.Path );
		map.SetType( 4, 4, ParkCellType.Queue );
		map.SetLinks( 4, 4, 64 );
		Assert.IsFalse( WalkEdges.Blocked( map, 3, 4, 1, 0 ) );
		map.SetLinks( 4, 4, 0 );
		Assert.IsTrue( WalkEdges.Blocked( map, 3, 4, 1, 0 ) );
		map.SetType( 4, 4, ParkCellType.PoolWater );
		Assert.IsTrue( WalkEdges.Blocked( map, 3, 4, 1, 1 ) );
	}

	[TestMethod]
	public void ExistingWalkReadsChangedBridgeFlagsWithoutAMapVersionChange()
	{
		foreach ( var close in new[] { false, true } )
		{
			var (map, builder) = Tile( TrackFamily.Kart, true );
			var walker = new GuestNavigation { X = (3 << 16) + 0xE000, Y = (4 << 16) + 0x8000, Mask = 1 };
			Assert.IsTrue( walker.Navigate( map, (4 << 16) + 0x8000, (4 << 16) + 0x8000 ) );
			var version = map.Version;
			var trackVersion = map.Tracks.Version;
			if ( close )
			{
				var tile = map.Tracks.Get( 4, 4 );
				tile.Flags = 0;
				map.Tracks.Set( 4, 4, tile );
				Assert.AreEqual( 0, map.Tracks.Get( 4, 4 ).Flags & TrackCell.BridgeMask );
				Assert.IsTrue( map.Tracks.Version > trackVersion );
			}
			Assert.AreEqual( version, map.Version );
			var steps = 0;
			for ( ; steps < 80 && !walker.Arrived && !walker.Stuck; steps++ )
			{
				walker.Step( 7733, 15466, map, ( _, _ ) => true );
				if ( close ) Assert.IsFalse( walker.Cell.X >= 4 && walker.Cell.X <= 5 && walker.Cell.Y >= 4 && walker.Cell.Y <= 5 );
			}
			Assert.IsTrue( steps > 0, "The live-step assertion must actually step the walker." );
			if ( !close )
			{
				Assert.IsTrue( walker.Arrived && !walker.Stuck, "The same live walker reaches the unchanged bridge." );
				Assert.AreEqual( (4, 4), walker.Cell );
			}
		}
	}

	[TestMethod]
	public void AFencePostAndTheCellsBesideItBlockAStepOntoThemUnlessTheirFlagNibbleIsSet()
	{
		// 0x4D89C3: a fence post is a record of kind 0x19 and the three cells beside it are its extensions (0xC): a step onto any of them is blocked, whatever the walker stands on, unless the
		// post's flag nibble (+0xE & 0xF, 1 for a post at the water) is set
		var map = new ParkCellMap( 16, 16 );
		map.SetLand( new ParkLand( (2, 2), (0, 2), new (int X, int Y)[] { (2, 2), (4, 2), (4, 4), (2, 4), (0, 4), (0, 2) } ) );
		var layer = map.Land!.LayerFor( 16, 16 );
		Assert.AreEqual( FenceLayer.PostType, layer.TypeAt( 4, 2 ) );
		Assert.AreEqual( FenceLayer.ExtensionType, layer.TypeAt( 5, 3 ) );
		foreach ( var mask in new[] { 0, 1 } )
		{
			Assert.IsTrue( WalkEdges.Blocked( map, 6, 3, 3, mask ), $"onto the cell beside the post, mask {mask}" );
			Assert.IsTrue( WalkEdges.Blocked( map, 3, 2, 1, mask ), $"onto the post, mask {mask}" );
			Assert.IsFalse( WalkEdges.Blocked( map, 8, 8, 1, mask ), "control: open ground away from the fence" );
		}
		Assert.IsFalse( WalkEdges.Commit( map, (6, 3), (5, 3), 1 ) );
		map.SetType( 5, 3, ParkCellType.Path );
		Assert.IsFalse( WalkEdges.Blocked( map, 6, 3, 3, 0 ), "a path target from a non-queue cell is decided before the record" );
		layer.Flags[2 * 16 + 4] = 1;
		Assert.IsFalse( WalkEdges.Blocked( map, 3, 2, 1, 1 ), "the post's flag nibble opens it" );
	}

	[TestMethod]
	public void ATrackTileReadsTheMapCellsOwnTypeNotTheTilesType()
	{
		// the map cell under a tile keeps its own type (0x538FC0 writes the record's): a walker leaving an exit (type 10) toward a tile over empty ground still reads that cell as type 0 (the queue-link rule)
		var map = new ParkCellMap( 16, 16 );
		map.Tracks.Set( 5, 4, new TrackCell { Type = 11, Parent = -1, Flags = 1 } );
		map.SetType( 5, 4, 11 );
		Assert.AreEqual( (byte)0, map.PrimaryTypeAt( 5, 4 ) );
		map.SetType( 4, 4, ParkCellTypes.Exit );
		map.SetQueueLink( 4, 4, 0x40 );
		Assert.IsFalse( WalkEdges.Blocked( map, 4, 4, 1, 0 ), "an exit's queue link toward the open tile (bridge flag set) is followed as toward an empty cell" );
	}
}
