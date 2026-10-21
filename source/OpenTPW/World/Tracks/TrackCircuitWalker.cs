namespace OpenTPW;

/// <summary>One tile of a circuit walk: where it is, how the flow passes it, and the segment number the walk gave it.</summary>
/// <param name="Entry">The heading the flow entered the tile with.</param>
/// <param name="Exit">The heading the flow leaves with (the new heading in a corner).</param>
/// <param name="Mask">The tile's connection mask.</param>
/// <param name="Segment">The 1-based position of the tile in the walk (+0x24).</param>
/// <param name="AddOn">The tile carries an add-on (flag 0x10).</param>
/// <param name="AddOnRun">The tile's place in its run of add-on tiles: 0 for the first, 1 for the next, …; 0 for every other tile (section +8).</param>
public readonly record struct TrackStep( int X, int Y, byte Entry, byte Exit, byte Mask, int Segment, bool AddOn, int AddOnRun = 0 )
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
	/// <summary>
	/// Walks the circuit that starts at <paramref name="station"/>'s first tile. The cells are written as the walk goes, as the original's walk does in both of its modes:
	/// a corner takes its new heading as the flow link and every visited tile its 0-based step number.
	/// </summary>
	// [BIN:TPW-EXE:0x0052A970 circuit walk] start = the station cell, each step needs a cell of type 11/13/16/18/25 (0x53AD30), a corner (0x53AE00: two perpendicular arms) takes the new heading mask ^ opposite(heading) (the heading itself when the opposite arm is missing) and rewrites +0xD, a non-corner keeps the heading, the step number goes to +0x24 (0x53AEF0, 0-based) and the walk moves 2 cells by a single-bit heading; success is arriving back at the start cell exactly, from any side, with no minimum length
	// [BIN:TPW-EXE:0x0052B20C walk failures] the walk fails (the flag [esp+0x28] clears) on a non-track cell, a heading of 0 or a cell with fewer than two arms (0x52B20C..0x52B220), and on a three-arm cell (0x522790 = 3) when the cell two ahead has type 0 (0x52B26F..0x52B280) or is a track cell without arms while the heading has bit 0x01 (a literal bit test, 0x52B238..0x52B267); a four-arm cell has no rule, no arm toward the flow is tested, the walk arriving at the start with the flag clear still fails
	// [EXT:track-walk-step-limit] the walk is cut off after as many steps as the layer has tiles plus the station; the original 0x52A970 has no step limit and would hang on a flow that never returns to the start
	// [BIN:TPW-EXE:0x0053AEE6 step number readers] the step number the walk writes to +0x24 (0-based) has one reader, the getter 0x53AEC0 (an extension cell reads its origin's), whose callers are the track tool's edit sub-modes 0xE and 0x13 (0x524960 orders its two picks by step, 0x527960 hovers, 0x52B400 finds the cut's end tiles by equal steps): TrackEditor reads TrackCell.Segment the same way; an arrival at the start from any side ends the walk as in the original
	// [BIN:TPW-EXE:0x00528A70 add-on placement] an object whose footprint has code 0xB or 0x10 (the track add-ons) must stand on a straight, unflagged tile of its ride's kart or water track (validator passes 0x10B/0x110, 0x20B/0x210 in add-on mode, 0x535953..0x5359B3) and its placement writes cell flags 0x10 (0x53A510 at 0x529511 and 0x52951C); a flagged tile gets no piece instance (0x0053AFA0 skips +0xE & 0x10)
	// [BIN:TPW-EXE:0x0052AC68 add-on sections] a straight with flag 0x10 builds kind 12 (0x44) or 13 (0x11) with +8 = its place in the run of add-on tiles (0 for the first, reset by any other tile), +0xC = the add-on's instance (thing +0x20) and +0x10 = its type id (0x4DD4E0 +0x10; Level.LinkAddOns links them after the walk, TrackAddOns.OpenEndsOf the open ends of 0x0052ADE3); the vehicle hook is the Space Racers Teleport only (type 0xDAC, 3500): [BIN:TPW-EXE:0x00548CA5] within ±0x4B0 of the first tile's centre a vehicle is hidden (0x548E80: flag 0x200, its instance, shadow and riders' heads) and the teleport plays animation 5, past 0x4B0 on the far side of the second tile it shows again
	public static TrackCircuit Walk( TrackBuilder builder, TrackStation station )
	{
		ArgumentNullException.ThrowIfNull( builder );
		var tracks = builder.Tracks;
		var heading = station.Heading;
		var position = station.First;
		var steps = new List<TrackStep>();
		var limit = tracks.Count / 4 * 4 + 16;
		// the run counter of add-on tiles ([esp+0x20] of 0x0052A970): a straight with flag 0x10 takes it and raises it, any other tile resets it
		var run = 0;
		string? failure = null;
		while ( true )
		{
			var tile = tracks.Get( position.X, position.Y );
			var entry = heading;
			var mask = tile.Mask;
			if ( !tile.IsOrigin )
			{
				failure ??= $"no track tile at ({position.X}, {position.Y})";
				heading = 0;
			}
			else
			{
				if ( IsCorner( mask ) )
				{
					var back = OppositeBits( heading );
					if ( (mask & back) == 0 )
						back = heading;
					heading = (byte)(mask ^ back);
					if ( tile.Link != heading )
						tile.Link = heading;
				}
				var arms = TrackHeading.Count( mask );
				if ( heading == 0 || arms < 2 )
					failure ??= $"the tile at ({position.X}, {position.Y}) has fewer than two arms";
				else if ( arms == 3 )
				{
					var ahead = tracks.Get( position.X + TrackStation.TileCells * TrackHeading.Dx( heading ), position.Y + TrackStation.TileCells * TrackHeading.Dy( heading ) );
					if ( (ahead.IsOrigin && ahead.Mask == 0 && (heading & 0x01) != 0) || ahead.Type == 0 )
						failure ??= $"the junction at ({position.X}, {position.Y}) has nothing ahead";
				}
				tile.Segment = steps.Count;
				var piece = TrackPieceSelector.Select( tile.Type, tile.Mask, tile.Link, tile.Flags );
				tile.Family = piece.Family;
				tile.Piece = piece.Piece;
				tile.Rotation = piece.Rotation;
				tracks.Set( position.X, position.Y, tile );
				var addOn = (tile.Flags & TrackCell.FlagAddOn) != 0;
				var place = 0;
				if ( addOn && mask is 0x44 or 0x11 )
					place = run++;
				else
					run = 0;
				steps.Add( new TrackStep( position.X, position.Y, entry, heading, mask, tile.Segment, addOn, place ) );
			}
			position = (position.X + TrackStation.TileCells * TrackHeading.Dx( heading ), position.Y + TrackStation.TileCells * TrackHeading.Dy( heading ));
			if ( position == station.First )
				return failure == null ? new TrackCircuit( true, steps, null ) : TrackCircuit.Invalid( failure, steps );
			if ( failure != null )
				return TrackCircuit.Invalid( failure, steps );
			if ( steps.Count > limit )
				return TrackCircuit.Invalid( "the walk did not return to the start", steps );
		}
	}

	/// <summary>A cell with exactly two perpendicular arms (the original's corner test 0x53AE00).</summary>
	private static bool IsCorner( byte mask ) => TrackHeading.Count( mask ) == 2 && (mask & 0x11) != 0 && (mask & 0x44) != 0;

	/// <summary>The original's opposite heading (0x4D8C00): bit 0x01 or 0x04 shifted up by four, 0x10 or 0x40 down, 0 kept, a multi-bit value shifted as a whole.</summary>
	private static byte OppositeBits( byte heading ) => heading == 0 ? (byte)0 : heading < 0x10 ? (byte)(heading << 4) : (byte)(heading >> 4);

	/// <summary>
	/// Walks the circuit and refreshes its tiles, then corrects bridge direction from the route for supported simple closed loops.
	/// Unsupported circuits retain the context-free refresh behavior.
	/// </summary>
	public static TrackCircuit WalkAndRefresh( TrackBuilder builder, TrackStation station )
	{
		var circuit = Walk( builder, station );
		foreach ( var step in circuit.Steps )
			builder.Refresh( step.X, step.Y );
		builder.RefreshClosedCircuitBridges( station, circuit );
		return circuit;
	}
}
