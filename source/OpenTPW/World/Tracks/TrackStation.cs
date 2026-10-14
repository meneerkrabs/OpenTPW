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
	// [APPROX:TRACK-005] the station tile layout is OpenTPW's: two tiles on the footprint's first two tile columns along the heading, in the rows beside the entrance, with the circuit starting at the first. The original walk starts at a station-derived cell shifted 4 cells by the rotation (0x0052A970), but which cell that is, which cells the station writes (types 13 and 18 are presumed) and where the station and finish buoys lie in the footprint are not traced — evidence needed: the writer of the walk's start cell (DAT 0x007639E0/E4) and the station's cell writes at placement
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
