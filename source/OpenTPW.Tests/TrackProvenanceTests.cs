using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-claims] A kart or water ride's protected track (C5-3, <see cref="TrackProvenance"/>): its tagged tiles, the tiles a flood over every arm reaches from its seeds through tiles
/// of any owner word, junctions and side branches included, and the tiles its own walks visit. A tile is each ride's whose protected track holds it: an orphan is none's, an ambiguous tile two
/// rides'. Reading it leaves the park untouched.
/// </summary>
[TestClass]
public class TrackProvenanceTests
{
	private sealed class Grid : IParkGrid
	{
		public int Width => 40;
		public int Height => 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => x < 0 || y < 0 || x >= Width || y >= Height ? OriginalPlacementResult.OutsideTerrain : OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private const byte NegY = TrackHeading.NegY, PosX = TrackHeading.PosX, PosY = TrackHeading.PosY, NegX = TrackHeading.NegX;

	/// <summary>A kart tile's origin at <paramref name="at"/> with its arms, of <paramref name="owner"/> (0: laid without a ride, as an older save leaves it).</summary>
	private static void Tile( TrackCellLayer tracks, (int X, int Y) at, byte mask, int owner = 0 ) =>
		tracks.Set( at.X, at.Y, new TrackCell { Type = TrackCellTypes.KartOrigin, Mask = mask, Parent = -1, Owner = owner } );

	[TestMethod]
	public void ARidesProtectedTrackHoldsItsTaggedTilesItsFloodAndItsWalksTilesAndNoOrphan()
	{
		var cells = new ParkCellMap( 40, 40 );
		var builder = new TrackBuilder( cells, TrackFamily.Kart, () => null, new Grid() );
		// A's station at (10, 10) and (12, 10), its exit (14, 10); B's at (10, 22) and (12, 22), its exit (14, 22)
		var a = TrackStation.FromPlacement( 10, 9, 0 );
		var b = TrackStation.FromPlacement( 10, 21, 0 );
		builder.PlaceStation( a );
		builder.PlaceStation( b );
		var tracks = cells.Tracks;
		// B's track of an older save, owner word 0: from its exit through a junction on (16, 22), on to (18, 22), and a side branch down (16, 24) to (16, 26)
		Tile( tracks, (14, 22), NegX | PosX );
		Tile( tracks, (16, 22), NegX | PosX | PosY );
		Tile( tracks, (18, 22), NegX | PosX );
		Tile( tracks, (16, 24), NegY | PosY );
		Tile( tracks, (16, 26), NegY );
		// B's last tile points at (20, 22), whose own arm does not point back: it leads up to A's track, which reaches it too
		Tile( tracks, (20, 22), NegY );
		// A's track of the same save: from its exit along the row, then down column 20 to (20, 20), which points at (20, 22)
		Tile( tracks, (14, 10), NegX | PosX );
		Tile( tracks, (16, 10), NegX | PosX );
		Tile( tracks, (18, 10), NegX | PosX );
		Tile( tracks, (20, 10), NegX | PosY );
		for ( var y = 12; y <= 20; y += 2 )
			Tile( tracks, (20, y), NegY | PosY );
		// a tile tagged B that no arm reaches, and an orphan
		Tile( tracks, (30, 22), 0, builder.OwnerOf( b ) );
		Tile( tracks, (30, 30), NegX );
		var live = cells.CopyForPrediction();
		var ofB = TrackProvenance.ProtectedTiles( cells.CopyForPrediction(), TrackFamily.Kart, new Grid(), b );
		var ofA = TrackProvenance.ProtectedTiles( cells.CopyForPrediction(), TrackFamily.Kart, new Grid(), a );
		Assert.IsTrue( ofB.IsSupersetOf( new[] { b.First, b.Second } ), "B's station" );
		Assert.IsTrue( ofB.Contains( (30, 22) ), "a tile its owner word gives B" );
		Assert.IsFalse( ofA.Contains( (30, 22) ) );
		Assert.IsTrue( ofB.IsSupersetOf( new[] { (14, 22), (16, 22), (18, 22), (16, 24), (16, 26) } ), "the flood across the junction and down the side branch, owner word 0 all through" );
		Assert.IsTrue( ofA.IsSupersetOf( new[] { (14, 10), (16, 10), (18, 10), (20, 10), (20, 20) } ) );
		Assert.IsTrue( ofA.Contains( (20, 22) ) && ofB.Contains( (20, 22) ), "a tile both reach is each one's: ambiguous" );
		// an arm leads to its tile whether or not that tile points back, and that tile's own arms lead on: (20, 22) points up into A's track, so B's flood goes on there; no arm of A's
		// track points at B's
		Assert.IsTrue( ofB.IsSupersetOf( new[] { (20, 20), (20, 10), (14, 10), a.Second } ), "B's flood follows (20, 22)'s own arm into A's track" );
		Assert.IsFalse( ofA.Overlaps( new[] { (14, 22), (16, 22), (18, 22), (16, 24), (16, 26), b.First, b.Second } ), "no arm leads from A's track to B's" );
		Assert.IsFalse( ofA.Contains( (30, 30) ) || ofB.Contains( (30, 30) ), "an orphan is none's" );
		// the walks wrote their copies alone
		foreach ( var (x, y, cell) in live.Tracks.Cells() )
			Assert.IsTrue( cell.SameAs( tracks.Get( x, y ) ), $"({x}, {y}) untouched" );
	}

	[TestMethod]
	public void TheCircuitWalkGoingStraightOnThroughATileWhoseMaskLacksItsHeadingAndTheCollectorsExitSeedProtectTheirTiles()
	{
		var cells = new ParkCellMap( 40, 40 );
		var builder = new TrackBuilder( cells, TrackFamily.Kart, () => null, new Grid() );
		var b = TrackStation.FromPlacement( 10, 21, 0 );
		builder.PlaceStation( b );
		var tracks = cells.Tracks;
		// the station's second tile has no arm toward its laid exit tile, so no arm leads from the station to it: the laid exit is a seed all the same, as the open-end collector starts there
		tracks.Set( b.Second.X, b.Second.Y, tracks.Get( b.Second.X, b.Second.Y ) with { Mask = NegX } );
		Tile( tracks, (14, 22), PosX );
		Tile( tracks, (16, 22), NegY | PosY );
		var ofB = TrackProvenance.ProtectedTiles( cells.CopyForPrediction(), TrackFamily.Kart, new Grid(), b );
		Assert.IsTrue( ofB.Contains( (14, 22) ), "the laid exit tile, a seed" );
		Assert.IsTrue( ofB.Contains( (16, 22) ), "the exit's arm reaches beyond it" );
		// a station whose tiles both carry the axis: the circuit walk goes straight on through a tile whose arms are across its heading, onto tiles no arm of the station's track points at
		var axis = TrackStation.FromPlacement( 10, 29, 0 );
		builder.PlaceStation( axis );
		Tile( tracks, (14, 30), NegX | PosX );
		Tile( tracks, (16, 30), NegY | PosY );
		Tile( tracks, (18, 30), NegX | PosX );
		Tile( tracks, (20, 30), NegX );
		var walked = TrackProvenance.ProtectedTiles( cells.CopyForPrediction(), TrackFamily.Kart, new Grid(), axis );
		Assert.IsTrue( walked.Contains( (18, 30) ) && walked.Contains( (20, 30) ), "the walk went straight on through (16, 30), whose arms are across its heading" );
	}
}
