namespace OpenTPW;

/// <summary>One tile of a circuit walk: where it is, how the flow passes it, and the segment number the walk gave it.</summary>
/// <param name="Entry">The heading the flow entered the tile with.</param>
/// <param name="Exit">The heading the flow leaves with (the new heading in a corner).</param>
/// <param name="Mask">The tile's connection mask.</param>
/// <param name="Segment">The 1-based position of the tile in the walk (+0x24).</param>
/// <param name="AddOn">The tile carries an add-on (flag 0x10).</param>
public readonly record struct TrackStep( int X, int Y, byte Entry, byte Exit, byte Mask, int Segment, bool AddOn )
{
	/// <summary>The section kind the tile builds (<see cref="TrackSections"/>): 5–8 corners, 9/10 straights (12/13 add-on), 11 crossings.</summary>
	public int Kind
	{
		get
		{
			var plus = AddOn ? 3 : 0;
			return Mask switch
			{
				0x05 => 5,
				0x41 => 6,
				0x50 => 7,
				0x14 => 8,
				0x44 => 9 + plus,
				0x11 => 10 + plus,
				_ => 11
			};
		}
	}
}

/// <summary>The result of one circuit walk: whether it returned to the start and the ordered tiles it passed.</summary>
public sealed record TrackCircuit( bool IsValid, IReadOnlyList<TrackStep> Steps, string? Failure )
{
	public static TrackCircuit Invalid( string reason, IReadOnlyList<TrackStep>? steps = null ) => new( false, steps ?? Array.Empty<TrackStep>(), reason );
	/// <summary>The ordered section list of a valid circuit.</summary>
	public IReadOnlyList<TrackStep> Sections => IsValid ? Steps : Array.Empty<TrackStep>();
}

/// <summary>
/// Q3 of the track trace: the circuit validity walk. From the station's start tile the walk follows the connection masks two cells
/// at a time, turning corners by the entry arm, and the circuit is valid when it arrives back at the start tile. Every visited tile
/// gets its flow link (map +0xD) and segment number (+0x24) and refreshes its piece; the result is the attraction's validity flag
/// (<c>mIsTrackRideValid</c>, +0x2C on the PC) and the ordered section list the collision geometry is built from.
/// </summary>
public static class TrackCircuitWalker
{
	/// <summary>Walks the circuit that starts at <paramref name="station"/>'s first tile. With <paramref name="commit"/> the visited cells are written, otherwise only the verdict is computed.</summary>
	// [BIN:TPW-EXE:0x0052A970 circuit walk] start = the station cell, each step needs a cell of type 11/13/16/18/25 with at least two arms, a corner sets the new heading from the mask and the entry arm and rewrites +0xD, a 3-arm junction fails when the cell ahead has type 0, the segment number goes to +0x24 and the walk moves 2 cells; success is arriving back at the start cell exactly, with no minimum length
	// [APPROX:TRACK-014] the junction rule beyond "the cell ahead has type 0" is not traced (docs/TRACKS.md): a tee or crossing continues straight ahead when that arm and tile exist, the walk is cut off after as many steps as the layer has tiles plus the station, and a walk that reaches the start from a side arm still counts as arriving — evidence needed: the walk's untraced junction conditions after 0x0052A970's call to 0x00535DB0
	// [APPROX:TRACK-008] the add-on flag (map +0xE bit 0x10: jumps and tunnels, upgrade objects 1500–1502, 2500–2502, 3500–3501, 4500–4501) is read into the section kind but never set: add-on straights, their run counters and values (+8/+0xC/+0x10 of a section) are deferred (the hook is the end of 0x547F50 ([BIN:TPW-EXE:0x00547F50]): a vehicle with flag 0x100 in a section with +0x10 == 0xDAC within ±0x4B0 calls 0x548E80) — evidence needed: the add-on placement (0x10088DEC)
	public static TrackCircuit Walk( TrackBuilder builder, TrackStation station, bool commit )
	{
		ArgumentNullException.ThrowIfNull( builder );
		var tracks = builder.Tracks;
		var heading = station.Heading;
		var position = station.First;
		var steps = new List<TrackStep>();
		var visited = new HashSet<(int X, int Y)>();
		var limit = tracks.Count / 4 * 4 + 16;
		while ( true )
		{
			var tile = tracks.Get( position.X, position.Y );
			if ( !tile.IsOrigin )
				return TrackCircuit.Invalid( $"no track tile at ({position.X}, {position.Y})", steps );
			var mask = tile.Mask;
			var arms = TrackHeading.Count( mask );
			if ( arms < 2 )
				return TrackCircuit.Invalid( $"the tile at ({position.X}, {position.Y}) has fewer than two arms", steps );
			var back = TrackHeading.Opposite( heading );
			if ( (mask & back) == 0 )
				return TrackCircuit.Invalid( $"the tile at ({position.X}, {position.Y}) has no arm toward the flow", steps );
			byte exit;
			if ( arms == 2 )
			{
				// a straight keeps the heading; a corner leaves by the arm that is not the entry arm
				exit = (mask & heading) != 0 ? heading : (byte)(mask & ~back);
			}
			else
			{
				var ahead = tracks.Get( position.X + TrackStation.TileCells * TrackHeading.Dx( heading ), position.Y + TrackStation.TileCells * TrackHeading.Dy( heading ) );
				if ( (mask & heading) == 0 || ahead.Type == 0 )
					return TrackCircuit.Invalid( $"the junction at ({position.X}, {position.Y}) has nothing ahead", steps );
				exit = heading;
			}
			var segment = steps.Count + 1;
			var first = visited.Add( position );
			steps.Add( new TrackStep( position.X, position.Y, heading, exit, mask, segment, (tile.Flags & TrackCell.FlagAddOn) != 0 ) );
			if ( commit )
			{
				tile.Link = first ? exit : (byte)(tile.Link | exit);
				tile.Segment = segment;
				var piece = TrackPieceSelector.Select( tile.Type, tile.Mask, tile.Link, tile.Flags );
				tile.Family = piece.Family;
				tile.Piece = piece.Piece;
				tile.Rotation = piece.Rotation;
				tracks.Set( position.X, position.Y, tile );
			}
			heading = exit;
			position = (position.X + TrackStation.TileCells * TrackHeading.Dx( heading ), position.Y + TrackStation.TileCells * TrackHeading.Dy( heading ));
			if ( position == station.First )
				return new TrackCircuit( true, steps, null );
			if ( steps.Count > limit )
				return TrackCircuit.Invalid( "the walk did not return to the start", steps );
		}
	}

	/// <summary>
	/// Walks the circuit and refreshes the bridge pass of its tiles: the flow links the walk wrote decide which neighbour is ahead of a bridge
	/// tile, so the variants are chosen again afterwards.
	/// </summary>
	public static TrackCircuit WalkAndRefresh( TrackBuilder builder, TrackStation station )
	{
		var circuit = Walk( builder, station, commit: true );
		foreach ( var step in circuit.Steps )
			builder.Refresh( step.X, step.Y );
		return circuit;
	}
}
