using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

// Host regression coverage, not preservation of the original primary path layer.
[TestClass]
public class TrackBridgePathMemoryTests
{
	private sealed class Grid : IParkGrid
	{
		public int Width => 32;
		public int Height => 32;
		public System.Numerics.Vector2 Origin => default;
		public float GetCellHeight( int x, int y ) => 0;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
	}

	private static TrackBuilder Place( TrackFamily family, int pathSubset )
	{
		var cells = new ParkCellMap( 32, 32 );
		var footprint = TrackStation.TileCellsOf( 12, 12 ).ToArray();
		for ( var i = 0; i < footprint.Length; i++ )
			if ( (pathSubset & (1 << i)) != 0 )
				cells.SetType( footprint[i].X, footprint[i].Y, ParkCellType.Path );
		var builder = new TrackBuilder( cells, family, () => null, new Grid() );
		Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( 12, 12, owner: 19 ) );
		return builder;
	}

	[TestMethod]
	public void EveryFootprintSubsetRetainsMemoryAcrossAllCardinalShapeTransitions()
	{
		foreach ( var family in new[] { TrackFamily.Kart, TrackFamily.Water } )
		for ( var subset = 0; subset < 16; subset++ )
		{
			var builder = Place( family, subset );
			var memory = subset == 0 ? 0 : TrackBuilder.FlagOverPath;
			for ( var shape = 0; shape < 16; shape++ )
			{
				var mask = (byte)((shape & 1) | ((shape & 2) << 1) | ((shape & 4) << 2) | ((shape & 8) << 3));
				var tile = builder.Tracks.Get( 12, 12 );
				tile.Mask = mask;
				builder.Tracks.Set( 12, 12, tile );
				builder.Refresh( 12, 12 );
				var actual = builder.Tracks.Get( 12, 12 );
				var bridgeShape = mask is 0 or 1 or 4 or 16 or 64 or 0x11 or 0x44;
				var bridge = subset != 0 && bridgeShape ? TrackCell.FlagBridge : 0;
				Assert.AreEqual( memory | bridge, (int)actual.Flags, $"{family}/{subset}/{mask:X2}" );
				Assert.AreEqual( 19, actual.Owner );
				Assert.AreEqual( mask, actual.Mask );
				var selected = TrackPieceSelector.Select( actual.Type, actual.Mask, actual.Link, actual.Flags );
				Assert.AreEqual( selected, new TrackPiece( actual.Family, actual.Piece, actual.Rotation ) );
				builder.Refresh( 12, 12 );
				Assert.AreEqual( actual, builder.Tracks.Get( 12, 12 ), "refresh is idempotent" );
			}
			// A crossing suppresses the bridge nibble, not the memory used when it becomes straight again.
			var straight = builder.Tracks.Get( 12, 12 );
			straight.Mask = 0x44;
			builder.Tracks.Set( 12, 12, straight );
			builder.Refresh( 12, 12 );
			Assert.AreEqual( memory | (subset == 0 ? 0 : TrackCell.FlagBridge), (int)builder.Tracks.Get( 12, 12 ).Flags );
		}
	}

	[TestMethod]
	public void FootprintMemoryRoundTripsTrackPayloadAndStillDrivesLaterShapeRefresh()
	{
		foreach ( var family in new[] { TrackFamily.Kart, TrackFamily.Water } )
		for ( var subset = 0; subset < 16; subset++ )
		{
			var builder = Place( family, subset );
			var corner = builder.Tracks.Get( 12, 12 );
			corner.Mask = 0x14;
			builder.Tracks.Set( 12, 12, corner );
			builder.Refresh( 12, 12 );
			var saved = TrackSave.Capture( builder.Cells, new BumpWorld(), Array.Empty<(int, BumpController)>() )!;
			var decoded = ParkResumeJson.Deserialize<TrackWorldSaveData>( ParkResumeJson.Serialize( saved ) );
			var restored = new ParkCellMap( 32, 32 );
			TrackSave.RestoreCells( restored, decoded );
			foreach ( var item in builder.Tracks.Cells() )
				Assert.AreEqual( item.Cell, restored.Tracks.Get( item.X, item.Y ) );
			var head = restored.Tracks.Get( 12, 12 );
			head.Mask = 0x44;
			restored.Tracks.Set( 12, 12, head );
			TrackBuilder.RefreshAll( restored.Tracks );
			Assert.AreEqual( subset == 0 ? 0 : TrackBuilder.FlagOverPath | TrackCell.FlagBridge, (int)restored.Tracks.Get( 12, 12 ).Flags );
		}
	}
}
