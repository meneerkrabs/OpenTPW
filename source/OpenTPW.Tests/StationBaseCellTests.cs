using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class StationBaseCellTests
{
	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 16;
		public int Height => 16;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private static ulong Hash( ParkCellMap cells ) => WorldStateHash.Compute( new WorldStateSources
	{
		Guests = new GuestSimulation( new GuestPathGrid( cells ), new GuestSettings(), 42 )
	} );

	[TestMethod]
	public void FootprintThenStationAddsOnlyTheEightStructureCellsInEveryRotation()
	{
		foreach ( var family in new[] { TrackFamily.Kart, TrackFamily.Water } )
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var cells = new ParkCellMap( 16, 16 );
			var park = EconomyTestData.Park();
			park.Litter.Cells = cells;
			var shape = new ObjectShape( new[] { "****", "****", "****" } );
			var anchor = Level.GetCentredAnchor( shape, 8, 8, rotation );
			cells.WriteFootprint( ObjectFootprint.GetCells( shape, anchor.X, anchor.Y, rotation ), ParkCellMap.CellWord( anchor.X, anchor.Y ) );
			var station = TrackStation.FromPlacement( anchor.X, anchor.Y, rotation );
			var positions = station.Tiles.SelectMany( tile => TrackStation.TileCellsOf( tile.X, tile.Y ) ).ToArray();
			Assert.AreEqual( 8, positions.Length );
			Assert.IsTrue( positions.All( p => cells.RawTypeAt( p.X, p.Y ) == ParkCellTypes.Object ) );
			var before = park.ParkValue;
			var covered = park.CoveredCells();
			var builder = new TrackBuilder( cells, family, () => null, new FlatGrid() );
			builder.PlaceStation( station );
			Assert.AreEqual( before + 400, park.ParkValue, $"{family}, {rotation}: the occupied base cells do not add another 16" );
			Assert.AreEqual( covered, park.CoveredCells(), "the footprint was already covered" );
			Assert.IsTrue( positions.All( p => cells.Tracks.Get( p.X, p.Y ).BaseType == ParkCellTypes.Object ) );
			var hash = Hash( cells );
			builder.PlaceStation( station );
			Assert.AreEqual( hash, Hash( cells ), "rebuilding must not capture station markers as base types" );
			Assert.AreEqual( before + 400, park.ParkValue );
			builder.RemoveStation( station );
			Assert.IsTrue( positions.All( p => cells.Tracks.Get( p.X, p.Y ).BaseType == 0 && cells.RawTypeAt( p.X, p.Y ) == 0 ) );
			builder.PlaceStation( station );
			Assert.IsTrue( positions.All( p => cells.Tracks.Get( p.X, p.Y ).BaseType == 0 ), "replacement on empty cells inherits no removed footprint" );
			Assert.AreEqual( before + 416, park.ParkValue );
		}
	}

	[TestMethod]
	public void EachExtensionKeepsItsOwnBaseTypeAndSecurityExclusions()
	{
		var cells = new ParkCellMap( 4, 2 );
		var station = new TrackStation( 0, TrackHeading.PosX, (0, 0), (2, 0) );
		var types = new byte[] { 2, 7, 30, 4, 9, 10, 3, 0 };
		for ( var i = 0; i < types.Length; i++ )
			cells.SetType( i % 4, i / 4, types[i] );
		var park = EconomyTestData.Park();
		park.Litter.Cells = cells;
		park.Litter.Environment.Effects[RegionEffect.Camera] = new RegionEffect( 0, 0, 0, 0, 10, 0 );
		park.Litter.Environment.Apply( RegionEffect.Camera, 3, 0, add: true );
		Assert.AreEqual( 20, park.SecurityCoveragePercent() );
		var before = park.ParkValue;
		new TrackBuilder( cells, TrackFamily.Kart, () => null, new FlatGrid() ).PlaceStation( station );
		Assert.AreEqual( before + 400, park.ParkValue );
		Assert.AreEqual( 20, park.SecurityCoveragePercent(), "three excluded base cells remain outside the denominator" );
		Assert.AreEqual( 8L, park.CoveredCells(), "each extension resolves its station's counted structure" );
		for ( var i = 0; i < types.Length; i++ )
			Assert.AreEqual( types[i], cells.Tracks.Get( i % 4, i / 4 ).BaseType );
	}

	[TestMethod]
	public void SavedStationBaseTypesSurviveRestoreAndChangeTheCanonicalHash()
	{
		var cells = new ParkCellMap( 16, 16 );
		var station = TrackStation.FromPlacement( 4, 4, 0 );
		foreach ( var p in station.Tiles.SelectMany( tile => TrackStation.TileCellsOf( tile.X, tile.Y ) ) )
			cells.SetType( p.X, p.Y, ParkCellTypes.Object );
		new TrackBuilder( cells, TrackFamily.Kart, () => null, new FlatGrid() ).PlaceStation( station );
		TrackBuilder.RefreshAll( cells.Tracks );
		var saved = TrackSave.Capture( cells, new BumpWorld(), Array.Empty<(int, BumpController)>() )!;
		var json = ParkResumeJson.Serialize( saved );
		StringAssert.Contains( System.Text.Encoding.UTF8.GetString( json ), "\"BaseType\":4" );
		var restored = new ParkCellMap( 16, 16 );
		foreach ( var item in saved.Cells )
			restored.SetType( item.X, item.Y, item.Type );
		// The layout snapshot restores these separately from TrackSave: the original cells were written twice.
		restored.RestoreBlockStamps( cells.StampCounter, cells.CaptureBlockStamps() );
		TrackSave.RestoreCells( restored, ParkResumeJson.Deserialize<TrackWorldSaveData>( json ) );
		Assert.AreEqual( Hash( cells ), Hash( restored ) );
		var legacy = saved with { Cells = saved.Cells.Select( cell => cell with { BaseType = 0 } ).ToList() };
		var oldJson = System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( legacy ) );
		Assert.IsFalse( oldJson.Contains( "BaseType" ), "default metadata is absent" );
		TrackSave.RestoreCells( restored, ParkResumeJson.Deserialize<TrackWorldSaveData>( System.Text.Encoding.UTF8.GetBytes( oldJson ) ) );
		Assert.AreNotEqual( Hash( cells ), Hash( restored ), "base type affects canonical gameplay state" );
		Assert.IsTrue( restored.Tracks.Cells().All( item => item.Cell.BaseType == 0 ) );
	}
	[TestMethod]
	public void DefaultBaseTypeKeepsThePreviousPackedHashAndJson()
	{
		var cells = new ParkCellMap( 16, 16 );
		var builder = new TrackBuilder( cells, TrackFamily.Kart, () => null, new FlatGrid() );
		cells.SetType( 4, 5, ParkCellType.Path );
		builder.PlaceStation( TrackStation.FromPlacement( 4, 4, 0 ) );
		Assert.IsTrue( cells.Tracks.Cells().All( item => item.Cell.BaseType == 0 ), "the path is cleared before base capture" );
		var expected = new StateHasher();
		expected.Add( cells.Tracks.Count );
		foreach ( var (x, y, cell) in cells.Tracks.Cells() )
		{
			expected.Add( y * cells.Width + x );
			expected.Add( cell.Type | cell.Mask << 8 | cell.Link << 16 | cell.Flags << 24 | (long)(byte)cell.Family << 32 | (long)(byte)cell.Piece << 40 );
			expected.Add( cell.Parent );
			expected.Add( cell.Rotation );
			expected.Add( cell.Overlap | cell.AltMask << 16 );
			expected.Add( cell.Segment );
			expected.Add( cell.Owner );
		}
		var actual = new StateHasher();
		cells.Tracks.AddCanonicalState( actual );
		Assert.AreEqual( expected.Value, actual.Value, "zero metadata preserves the previous canonical byte stream" );
		const string oldJson = "{\"X\":4,\"Y\":2,\"Type\":18,\"Mask\":68,\"Link\":0,\"Flags\":0,\"Parent\":-1,\"Family\":0,\"Piece\":0,\"Rotation\":0,\"Overlap\":0,\"AltMask\":0,\"Segment\":1,\"Owner\":0}";
		var old = ParkResumeJson.Deserialize<TrackCellSaveData>( System.Text.Encoding.UTF8.GetBytes( oldJson ) );
		Assert.AreEqual( (byte)0, old.BaseType );
		Assert.AreEqual( oldJson, System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( old ) ) );
	}

	[TestMethod]
	public void OwnershipFilteringAndOrdinaryTrackPlacementKeepTheirPreviousRules()
	{
		var cells = new ParkCellMap( 16, 16 );
		var park = EconomyTestData.Park();
		park.Litter.Cells = cells;
		var station = TrackStation.FromPlacement( 4, 4, 0 );
		foreach ( var p in station.Tiles.SelectMany( tile => TrackStation.TileCellsOf( tile.X, tile.Y ) ) )
			cells.SetType( p.X, p.Y, ParkCellTypes.Object );
		cells.SetFlags( station.First.X, station.First.Y, ParkCellFlags.Unowned );
		var before = park.ParkValue;
		var builder = new TrackBuilder( cells, TrackFamily.Kart, () => null, new FlatGrid() );
		builder.PlaceStation( station );
		Assert.AreEqual( before + 350, park.ParkValue, "the unowned station origin still contributes nothing" );
		Assert.AreEqual( 8L, park.CoveredCells(), "coverage includes unowned structures" );
		before = park.ParkValue;
		Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( 0, 0 ) );
		Assert.AreEqual( before + 200, park.ParkValue );
		Assert.IsTrue( TrackStation.TileCellsOf( 0, 0 ).All( p => cells.Tracks.Get( p.X, p.Y ).BaseType == 0 ) );
	}

	[TestMethod]
	public void HighFlagBitsDoNotHideThePreservedBaseTypeInTheHash()
	{
		foreach ( var flags in new byte[] { 128, 255 } )
		{
			var cells = new ParkCellMap( 1, 1 );
			cells.SetType( 0, 0, TrackCellTypes.KartStart );
			var cell = new TrackCell { Type = TrackCellTypes.KartStart, Flags = flags, Parent = -1 };
			cells.Tracks.Set( 0, 0, cell );
			var expected = new StateHasher();
			expected.Add( 1 );
			expected.Add( 0 );
			expected.Add( cell.Type | cell.Mask << 8 | cell.Link << 16 | cell.Flags << 24 | (long)(byte)cell.Family << 32 | (long)(byte)cell.Piece << 40 );
			expected.Add( cell.Parent );
			expected.Add( cell.Rotation );
			expected.Add( cell.Overlap | cell.AltMask << 16 );
			expected.Add( cell.Segment );
			expected.Add( cell.Owner );
			var actual = new StateHasher();
			cells.Tracks.AddCanonicalState( actual );
			Assert.AreEqual( expected.Value, actual.Value, $"flags {flags}: the legacy sign-extended stream is preserved" );
			var oldHash = Hash( cells );
			cell.BaseType = 4;
			cells.Tracks.Set( 0, 0, cell );
			Assert.AreNotEqual( oldHash, Hash( cells ), $"flags {flags}: base metadata must remain visible" );
		}
	}

}
