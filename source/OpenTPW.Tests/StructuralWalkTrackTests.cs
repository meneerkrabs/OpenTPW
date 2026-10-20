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
		if ( overPath ) map.SetType( 4, 4, ParkCellType.Path );
		var builder = new TrackBuilder( map, family, () => null, new FlatGrid() );
		Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( 4, 4 ) );
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
		foreach ( var parent in new[] { -1, 0, 256, int.MaxValue } )
		{
			var map = new ParkCellMap( 16, 16 );
			map.Tracks.Set( 0, 0, new TrackCell { Type = 11, Parent = -1 } );
			map.SetType( 4, 4, TrackCellTypes.Extension );
			map.Tracks.Set( 4, 4, new TrackCell { Type = 12, Parent = parent } );
			Assert.IsFalse( WalkEdges.Blocked( map, 3, 4, 1, 1 ), $"parent {parent}" );
		}
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
				tile.Flags &= unchecked((byte)~TrackBuilder.FlagOverPath);
				map.Tracks.Set( 4, 4, tile );
				builder.Refresh( 4, 4 );
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
}
