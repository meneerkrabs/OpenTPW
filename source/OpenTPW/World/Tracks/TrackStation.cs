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
/// <param name="Legacy">The layout of a park saved before the station followed the original's line (<see cref="FromPlacementLegacy"/>).</param>
public readonly record struct TrackStation( int Rotation, byte Heading, (int X, int Y) First, (int X, int Y) Second, bool Legacy = false )
{
	/// <summary>Cells per tile edge.</summary>
	public const int TileCells = 2;
	/// <summary>Section units per cell (docs/TRACKS.md: 3072, 10 world units × 307.2).</summary>
	public const int CellUnits = 0xC00;

	/// <summary>The tile cell before the first station tile, where the circuit's last tile joins.</summary>
	public (int X, int Y) Entry => (First.X - TileCells * TrackHeading.Dx( Heading ), First.Y - TileCells * TrackHeading.Dy( Heading ));
	/// <summary>The tile cell after the second station tile, where the player's first line starts.</summary>
	public (int X, int Y) Exit => (Second.X + TileCells * TrackHeading.Dx( Heading ), Second.Y + TileCells * TrackHeading.Dy( Heading ));

	/// <summary>
	/// The controller's station reference in section units: the point between the two tiles on their centre line, one cell along the heading from the first tile's centre
	/// (the original's <c>0x529E10</c> reference, <see cref="Level.TrackStationReference"/>). A legacy station's reference is the first tile's centre.
	/// </summary>
	public (int X, int Y) Reference
	{
		get
		{
			var centre = Centre( First );
			return Legacy ? centre : (centre.X + CellUnits * TrackHeading.Dx( Heading ), centre.Y + CellUnits * TrackHeading.Dy( Heading ));
		}
	}

	/// <summary>
	/// The ride's anchor cell (the original's attraction cell <c>0x7639A4/A8</c>), the cell <see cref="FromPlacement"/> was given: the edit tool's hover keeps it in the exit globals
	/// (<see cref="TrackEditor.ToolExit"/>). A legacy station is searched for the anchor that gives its layout.
	/// </summary>
	public (int X, int Y) Anchor
	{
		get
		{
			var (x, y) = Rotation switch
			{
				0 => (First.X, First.Y - 1),
				90 => (First.X - 1, First.Y + 1),
				180 => (First.X + 1, First.Y + 2),
				270 => (First.X + 2, First.Y),
				_ => First
			};
			if ( !Legacy )
				return (x, y);
			for ( var dy = -4; dy <= 4; dy++ )
				for ( var dx = -4; dx <= 4; dx++ )
					if ( FromPlacementLegacy( x + dx, y + dy, Rotation ).First == First )
						return (x + dx, y + dy);
			return (x, y);
		}
	}

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
	/// The station of a ride whose placement anchor is (<paramref name="anchorX"/>, <paramref name="anchorY"/>) (the original's attraction cell, <c>0x528B44</c>): the line of
	/// <c>0x52A1A0</c>, two tiles one row beside the entrance row, in the ride's rotation.
	/// </summary>
	// [BIN:TPW-EXE:0x0052A1A0 station line] from the attraction cell (x, y) the line runs, per rotation, 0°: (x, y+1) → (x+2, y+1), exit (x+4, y+1); 90°: (x+1, y−1) → (x+1, y−3), exit (x+1, y−5); 180°: (x−1, y−2) → (x−3, y−2), exit (x−5, y−2); 270°: (x−2, y) → (x−2, y+2), exit (x−2, y+4) (0x52A1E1..0x52A28C: the start cell is the walk's start 0x7639E0/E4); the tiles are the footprint's rows 1 and 2 across the heading, the entrance row 0 beside them
	// [BIN:TPW-EXE:0x00529E10 ride placement] the controller's reference is the cell between the two tiles on their centre line (anchor + the type's Bumper.*Adjust + (4, 1), (1, −5), (−5, −2) or (−2, 4) by rotation, 0x529EDF..0x529F2B), see Level.TrackStationReference
	// [BIN:TPW-EXE:0x005254C5 station ops] the placement lays the exit and entry tiles with the op list 0x87, the type op, 0x80, 0x82, 0x83, 0x85, 0x86, 0x81 on each (TrackBuilder.PlaceInitialStation): the station tiles written here carry the arms of the station line (the first tile toward the second, the second back), and the other arms follow the exit's and entry's ops whatever their type ops answered (0x5255D8..0x52572C)
	public static TrackStation FromPlacement( int anchorX, int anchorY, int rotation )
	{
		var heading = TrackHeading.FromRotation( rotation );
		var (first, second) = rotation switch
		{
			0 => ((anchorX, anchorY + 1), (anchorX + 2, anchorY + 1)),
			90 => ((anchorX + 1, anchorY - 1), (anchorX + 1, anchorY - 3)),
			180 => ((anchorX - 1, anchorY - 2), (anchorX - 3, anchorY - 2)),
			270 => ((anchorX - 2, anchorY), (anchorX - 2, anchorY + 2)),
			_ => throw new ArgumentOutOfRangeException( nameof( rotation ), "Rotations are multiples of 90 degrees." )
		};
		return new TrackStation( rotation, heading, first, second );
	}

	/// <summary>
	/// The station layout of a park saved before the station followed the original's line: the two tile columns of the footprint's rows 0 and 1 across the heading (one row
	/// nearer the entrance than the original's), with the station reference at the centre of the first tile. Only a restored ride of such a save keeps it.
	/// </summary>
	// [EXT:legacy-station-layout] a ride of a park saved before the station followed the original's line keeps the tiles and the reference it was saved with (its saved tiles lie there)
	public static TrackStation FromPlacementLegacy( int anchorX, int anchorY, int rotation )
	{
		var heading = TrackHeading.FromRotation( rotation );
		return new TrackStation( rotation, heading, OriginOf( anchorX, anchorY, rotation, 0 ), OriginOf( anchorX, anchorY, rotation, 1 ), Legacy: true );
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
