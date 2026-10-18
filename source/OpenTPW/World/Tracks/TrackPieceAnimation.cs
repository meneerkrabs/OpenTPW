namespace OpenTPW;

/// <summary>
/// The walks the original runs to trigger the piece animations of a track (docs/TRACKS.md, "Piece animation"): from one side of the station along the
/// flow, through every tile, turning at corners and going straight through junctions, until a tile has no arm ahead or the next tile is the station.
/// </summary>
public static class TrackPieceWalk
{
	/// <summary>
	/// The tiles from <paramref name="start"/>, entered with <paramref name="heading"/>, that hold a piece instance, in walk order. The walk ends at a cell
	/// that is not a track tile, at a tile with no arm ahead (a corner turns first), or before a station tile whose arm points back at the walk; it is cut off
	/// after <paramref name="limit"/> tiles (OpenTPW's guard: the original loops for as long as the track leads on).
	/// </summary>
	// [BIN:TPW-EXE:0x0052A490 forward piece walk] from the attraction cell plus the station's exit offset (rotation 0: (x+4, y+1), 90: (x+1, y−5), 180: (x−5, y−2), 270: (x−2, y+4)) with the rotation's heading;
	// each cell of type 11/13/16/18/25 (0x53AD30) turns at a two-arm corner (0x53AE00: the mask ^ the reversed heading when the mask holds it, else ^ the previous heading),
	// then a cell with a mesh instance (+4) gets 0x4732A0(instance, flag ? 5 : 12, 0, 7, 1.0, 0); the loop stops when a cell is no track tile, when a non-corner's mask lacks
	// the heading, or when the cell two ahead (0x50C3E0) is a station cell (0x53ADB0: type 13/18 or the extension of one) with the same +0x10 whose mask holds the reversed heading
	// [BIN:TPW-EXE:0x0052A700 backward piece walk] the same loop from the station's other side (rotation 0: (x−2, y+1), 90: (x+1, y+1), 180: (x+1, y−2), 270: (x−2, y−2)) with the reversed heading
	public static IEnumerable<(int X, int Y)> Walk( TrackCellLayer layer, (int X, int Y) start, byte heading, int limit )
	{
		ArgumentNullException.ThrowIfNull( layer );
		var (x, y) = start;
		var along = heading;
		var entered = heading;
		for ( var step = 0; step < limit; step++ )
		{
			var cell = layer.Get( x, y );
			var corner = IsCorner( cell.Mask );
			if ( !TrackCellTypes.IsWalkable( cell.Type ) )
			{
				along = 0;
				entered = 0;
			}
			else
			{
				if ( corner )
				{
					var back = Reverse( along );
					along = (byte)(cell.Mask ^ ((back & cell.Mask) != 0 ? back : entered));
					entered = along;
				}
				if ( cell.IsOrigin && cell.Piece != 0 )
					yield return (x, y);
			}
			if ( !corner && (entered & cell.Mask) == 0 )
				yield break;
			if ( TrackHeading.Dx( along ) == 0 && TrackHeading.Dy( along ) == 0 )
				continue;
			var nextX = x + TrackStation.TileCells * TrackHeading.Dx( along );
			var nextY = y + TrackStation.TileCells * TrackHeading.Dy( along );
			var next = layer.Get( nextX, nextY );
			if ( IsStationCell( layer, next ) && next.Parent == cell.Parent && (next.Mask & Reverse( along )) != 0 )
				yield break;
			(x, y) = (nextX, nextY);
		}
	}

	/// <summary>The walk from the tile after the station along the flow (<see cref="TrackStation.Exit"/>), the original's 0x52A490.</summary>
	public static IEnumerable<(int X, int Y)> Forward( TrackCellLayer layer, TrackStation station ) =>
		Walk( layer, station.Exit, station.Heading, Limit( layer ) );

	/// <summary>The walk from the tile before the station against the flow (<see cref="TrackStation.Entry"/>), the original's 0x52A700.</summary>
	public static IEnumerable<(int X, int Y)> Backward( TrackCellLayer layer, TrackStation station ) =>
		Walk( layer, station.Entry, TrackHeading.Opposite( station.Heading ), Limit( layer ) );

	private static int Limit( TrackCellLayer layer ) => layer.Count + 16;

	/// <summary>A two-arm corner mask (0x05, 0x41, 0x50, 0x14).</summary>
	// [BIN:TPW-EXE:0x0053AE00 corner test] two arms (0x522810) of which one is ±Y and one ±X: the mask, else 0
	public static bool IsCorner( byte mask ) => TrackHeading.Count( mask ) == 2 && (mask & 0x11) != 0 && (mask & 0x44) != 0;

	// [BIN:TPW-EXE:0x004D8C00 reverse heading] a nonzero heading below 0x10 shifts left by 4, otherwise right by 4
	private static byte Reverse( byte heading ) => heading == 0 ? (byte)0 : heading < 0x10 ? (byte)(heading << 4) : (byte)(heading >> 4);

	// [BIN:TPW-EXE:0x0053ADB0 station cell] type 13 or 18, or an extension (12, 17) whose parent has type 13 or 18
	private static bool IsStationCell( TrackCellLayer layer, TrackCell cell )
	{
		if ( TrackCellTypes.IsStation( cell.Type ) )
			return true;
		if ( !cell.IsExtension || cell.Parent < 0 )
			return false;
		var (px, py) = layer.CellOf( cell.Parent );
		return TrackCellTypes.IsStation( layer.Get( px, py ).Type );
	}
}

/// <summary>
/// The animation state of the track pieces (presentation only, never saved or hashed): which tiles play their piece's animation 5 (the <c>m</c> clip: the
/// water's texture-coordinate scroll) since when, and when a stopped one froze. A tile counts only while it still shows the piece and rotation it had when it
/// was triggered: a re-chosen piece is a new instance, which starts with no clip (animation 12).
/// </summary>
// [BIN:TPW-EXE:0x0053AFA0 piece instance] a re-chosen piece deletes the cell's instance (0x522A90) and creates a new one (0x522B90 kart, 0x522D50 water), which starts animation 12 with flags 7 (0x522E1F)
public sealed class TrackPieceAnimations
{
	/// <summary>One tile's clip: the piece and rotation it was triggered on, the start, and the time it froze (null while it plays).</summary>
	public readonly record struct Play( int Piece, int Rotation, double Start, double? Stop );

	private readonly Dictionary<(int X, int Y), Play> plays = new();

	/// <summary>The presentation clock in seconds the starts and stops are measured on.</summary>
	public double Clock { get; private set; }
	/// <summary>Bumped whenever a tile starts or stops.</summary>
	public int Version { get; private set; }
	/// <summary>Tiles with a clip (playing or frozen).</summary>
	public int Count => plays.Count;

	public void Advance( double seconds )
	{
		if ( seconds > 0 && double.IsFinite( seconds ) )
			Clock += seconds;
	}

	/// <summary>
	/// The original's trigger on one tile: <paramref name="play"/> restarts the piece's animation 5 now (flags 7: immediate and looping); otherwise animation
	/// 12 stops a playing tile where it is (flag 4 keeps the last pose; a tile that does not play keeps its stored one).
	/// </summary>
	// [BIN:TPW-EXE:0x004732A0 animation start] flags bit 1 starts at once instead of after the running clip; 0x00472F60: bit 0 loops, bit 2 sets channel flag 0x20, and bits 2–3 skip the old clip's release 0x00472310 when the new animation is 12
	public void Trigger( TrackCellLayer layer, int x, int y, bool play )
	{
		ArgumentNullException.ThrowIfNull( layer );
		var cell = layer.Get( x, y );
		if ( play )
		{
			plays[(x, y)] = new Play( cell.Piece, cell.Rotation, Clock, null );
			Version++;
			return;
		}
		if ( !plays.TryGetValue( (x, y), out var current ) )
			return;
		if ( current.Piece != cell.Piece || current.Rotation != cell.Rotation )
			plays.Remove( (x, y) );
		else if ( current.Stop == null )
			plays[(x, y)] = current with { Stop = Clock };
		else
			return;
		Version++;
	}

	/// <summary>
	/// Triggers the pieces of a station's track as the original's piece walks do: the walk from the station's exit along the flow and, with
	/// <paramref name="backward"/>, the walk from its entry against the flow; <paramref name="play"/> starts the water clip, otherwise it stops.
	/// </summary>
	public void TriggerTrack( TrackCellLayer layer, TrackStation station, bool play, bool backward )
	{
		ArgumentNullException.ThrowIfNull( layer );
		foreach ( var (x, y) in TrackPieceWalk.Forward( layer, station ).ToList() )
			Trigger( layer, x, y, play );
		if ( !backward )
			return;
		foreach ( var (x, y) in TrackPieceWalk.Backward( layer, station ).ToList() )
			Trigger( layer, x, y, play );
	}

	/// <summary>
	/// What the original's re-walks do to the pieces: an add-on's placement (and the ride's own) starts them on a valid circuit and stops them from both sides on an invalid one; a
	/// laid track line stops them from the exit side, and from both sides on an invalid circuit; the edit sub-modes' cut starts them on a valid circuit and stops them from
	/// both sides on an invalid one, as an add-on's placement does; a restore leaves them alone (null).
	/// </summary>
	// [BIN:TPW-EXE:0x00524D0A add-on placement walk] the walk after an add-on's placement: a valid circuit calls 0x0052A490(ride, 1), an invalid one 0x0052A490(ride, 0) and 0x0052A700(ride, 0); the ride's own placement (0x00528A70 → 0x00529E10) lays its station and walks nothing, and OpenTPW's walk of the fresh station ([EXT:track-placement-walk], Level.OnTrackStationPlaced) follows the same rule
	// [BIN:TPW-EXE:0x00526F02 track line walk] when a line of the kart or water tool ends, 0x0052A490(ride, 0) runs whatever the verdict (0x00526F58, edi cleared at 0x00526EFF), and an invalid circuit also gets 0x0052A700(ride, 0)
	// [BIN:TPW-EXE:0x00526BD0 edit walk] after a cut the walk 0x52A970(0) is followed on a valid circuit by 0x0052A490(ride, 1) (0x526BEF) and on an invalid one by 0x0052A490(ride, 0) and 0x0052A700(ride, 0) (0x526BFD, 0x526C0D)
	public static (bool Play, bool Backward)? ForWalk( TrackWalkCause cause, bool valid ) => cause switch
	{
		TrackWalkCause.Placement or TrackWalkCause.AddOn or TrackWalkCause.Edit => (valid, !valid),
		TrackWalkCause.Line => (false, !valid),
		_ => null
	};

	/// <summary>The clip of the tile at (<paramref name="x"/>, <paramref name="y"/>), or null when it shows its stored mesh.</summary>
	public Play? At( TrackCellLayer layer, int x, int y )
	{
		ArgumentNullException.ThrowIfNull( layer );
		if ( !plays.TryGetValue( (x, y), out var play ) )
			return null;
		var cell = layer.Get( x, y );
		return cell.IsOrigin && cell.Piece == play.Piece && cell.Rotation == play.Rotation ? play : null;
	}

	/// <summary>Seconds of the clip shown at the clock: the time since the start, or up to the freeze.</summary>
	public double Elapsed( Play play ) => Math.Max( 0, (play.Stop ?? Clock) - play.Start );

	/// <summary>
	/// Forgets the clips of tiles that no longer show the piece and rotation they were triggered on (a demolished or re-laid tile is a new instance without a
	/// clip), so laying the same piece on the cell again does not bring an old clip back.
	/// </summary>
	public void Prune( TrackCellLayer layer )
	{
		ArgumentNullException.ThrowIfNull( layer );
		var stale = plays.Where( pair =>
		{
			var cell = layer.Get( pair.Key.X, pair.Key.Y );
			return !cell.IsOrigin || cell.Piece != pair.Value.Piece || cell.Rotation != pair.Value.Rotation;
		} ).Select( pair => pair.Key ).ToList();
		foreach ( var key in stale )
			plays.Remove( key );
		if ( stale.Count > 0 )
			Version++;
	}

	public void Clear()
	{
		if ( plays.Count == 0 )
			return;
		plays.Clear();
		Version++;
	}
}

/// <summary>Which of the original's circuit walks a re-walk stands for (the pieces' water clip follows it, <see cref="TrackPieceAnimations.ForWalk"/>).</summary>
public enum TrackWalkCause
{
	/// <summary>A restore or migration: the pieces are not triggered (the original's load creates every piece without a clip).</summary>
	Restore,
	/// <summary>The ride was placed.</summary>
	Placement,
	/// <summary>A track line was laid.</summary>
	Line,
	/// <summary>A track add-on was placed or sold.</summary>
	AddOn,
	/// <summary>The track edit tool cut the circuit (<see cref="TrackEditor"/>).</summary>
	Edit
}
