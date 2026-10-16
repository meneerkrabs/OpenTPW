namespace OpenTPW;

/// <summary>
/// Where a kart or water ride's station sits on the track grid (docs/TRACKS.md). The 4 × 4 footprint holds two station tiles
/// along the ride's heading, on the rows next to the entrance; the circuit starts at the first one. The tile before it and the
/// tile after the second one are where the player's track joins.
/// </summary>
/// <param name="Rotation">The ride's rotation in degrees.</param>
/// <param name="Heading">The heading bit the circuit's flow runs along at the station.</param>
/// <param name="First">Origin cell of the first station tile (the circuit's start cell).</param>
/// <param name="Second">Origin cell of the second station tile.</param>
public readonly record struct TrackStation( int Rotation, byte Heading, (int X, int Y) First, (int X, int Y) Second )
{
	/// <summary>Cells per tile edge.</summary>
	public const int TileCells = 2;
	/// <summary>Section units per cell (docs/TRACKS.md: 3072, 10 world units × 307.2).</summary>
	public const int CellUnits = 0xC00;

	/// <summary>The tile cell before the first station tile, where the circuit's last tile joins.</summary>
	public (int X, int Y) Entry => (First.X - TileCells * TrackHeading.Dx( Heading ), First.Y - TileCells * TrackHeading.Dy( Heading ));
	/// <summary>The tile cell after the second station tile, where the player's first line starts.</summary>
	public (int X, int Y) Exit => (Second.X + TileCells * TrackHeading.Dx( Heading ), Second.Y + TileCells * TrackHeading.Dy( Heading ));

	/// <summary>The origin cells of both station tiles in circuit order.</summary>
	public IEnumerable<(int X, int Y)> Tiles
	{
		get
		{
			yield return First;
			yield return Second;
		}
	}

	/// <summary>
	/// The station of a ride whose footprint anchor is (<paramref name="anchorX"/>, <paramref name="anchorY"/>): the two tile columns of the
	/// rows next to the entrance, in the ride's rotation.
	/// </summary>
	// [APPROX:TRACK-005] the station tile layout is OpenTPW's: two tiles on the footprint's first two tile columns along the heading, in the rows beside the entrance, with the circuit starting at the first; the original's station is the line of 0x52A1A0 ([BIN:TPW-EXE:0x0052A1A0]: two tiles of type 13 (kart) or 18 (water) from the attraction cell (x, y): rot 0 (x, y+1)..(x+2, y+1), walk start (x, y+1), exit (x+4, y+1); rot 90 (x+1, y)..(x+1, y−3), exit (x+1, y−5); rot 180 (x−1, y−2)..(x−3, y−2), exit (x−5, y−2); rot 270 (x−2, y)..(x−2, y+2), exit (x−2, y+4)); the placement's cursor cell is OpenTPW's anchor (0x818C3C, written at 0x528E12 from the build click's anchor 0x528B44), but the attraction 0x52A1A0 reads (the object 0x528A70 returns) is constructed at that cell plus the per-rotation offset 0x4D9CC0 returns from type +0x4B0 (0x529F86–0x529FD5) — evidence needed: that offset (0x4D9CC0) and whether the attraction's +8/+0xC is the constructed cell, and the line modes 0x80, 0x82 and 0x83 of 0x53AAC0
	public static TrackStation FromPlacement( int anchorX, int anchorY, int rotation )
	{
		var heading = TrackHeading.FromRotation( rotation );
		return new TrackStation( rotation, heading, OriginOf( anchorX, anchorY, rotation, 0 ), OriginOf( anchorX, anchorY, rotation, 1 ) );
	}

	private static (int X, int Y) OriginOf( int anchorX, int anchorY, int rotation, int column )
	{
		var minX = int.MaxValue;
		var minY = int.MaxValue;
		for ( var u = column * 2; u < column * 2 + 2; u++ )
		{
			for ( var v = 0; v < 2; v++ )
			{
				var (x, y) = ObjectFootprint.ToGrid( anchorX, anchorY, rotation, u, v );
				minX = Math.Min( minX, x );
				minY = Math.Min( minY, y );
			}
		}
		return (minX, minY);
	}

	/// <summary>The four cells of the tile whose origin is (<paramref name="x"/>, <paramref name="y"/>): the origin first.</summary>
	public static IEnumerable<(int X, int Y)> TileCellsOf( int x, int y )
	{
		yield return (x, y);
		yield return (x + 1, y);
		yield return (x, y + 1);
		yield return (x + 1, y + 1);
	}

	/// <summary>The centre of a tile in section units.</summary>
	public static (int X, int Y) Centre( (int X, int Y) origin ) => (origin.X * CellUnits + CellUnits, origin.Y * CellUnits + CellUnits);
}
