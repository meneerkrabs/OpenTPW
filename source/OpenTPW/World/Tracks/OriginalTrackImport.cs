namespace OpenTPW;

/// <summary>
/// The kart and water tracks of an original save (the structure layer's track cells, the grid's <c>0x4D0B30</c> base and <c>0x50C200</c> segment, and the TrackRides block's chunks, loader <c>0x543560</c>), mapped onto OpenTPW's
/// track layer and controllers. The pure part: the cell records, the owner of each tile and the comparison of the saved section list with a walked circuit; <see cref="Level.ImportOriginalTracks"/> applies them.
/// </summary>
internal static class OriginalTrackImport
{
	/// <summary>Map type values under a track tile that make it a bridge: a path (1) or a queue (3) in any of its four cells (TRACK-010).</summary>
	private static bool IsPathUnder( uint mapType ) => mapType is 1 or 3;

	/// <summary>
	/// The track cell a saved structure cell holds: the type, connection mask, flow link, flags (bits 0 to 3 the bridge state, 0x10 an add-on), the origin's cell word turned into the layer's cell index
	/// (−1 in an origin cell), the chosen family, piece and rotation, the overlap counter, the alternate mask and the circuit segment. The ride (<see cref="TrackCell.Owner"/>) and the type a station tile stands on
	/// (<see cref="TrackCell.BaseType"/>) are the import's to decide.
	/// </summary>
	// [BIN:TPW-EXE:0x4D0B30 cell base serializer] mDirection (+0xD), mFlags (+0xE, 2 bytes), mMeshInstance (+4), mNeighbours (+0xC), mOverlapCounter (+0x20), mParentID (+0x10), mTileData (+0x14, 12 bytes), mType (+8) and +0x22; the track cell 0x50C200 adds the 2-byte segment
	public static TrackCell ToCell( SaveTrackCell saved, int width, int height )
	{
		var parent = -1;
		if ( saved.Type == TrackCellTypes.Extension )
		{
			var word = saved.Parent - 1;
			parent = saved.Parent == 0 ? -1 : word;
			if ( parent >= 0 && (parent % 128 >= width || parent / 128 >= height) )
				parent = -1;
			else if ( parent >= 0 )
				parent = parent / 128 * width + parent % 128;
		}
		return new TrackCell
		{
			Type = saved.Type,
			Mask = saved.Mask,
			Link = saved.Link,
			Flags = (byte)(saved.Flags & 0xFF),
			Parent = parent,
			Family = saved.Family,
			Piece = saved.Piece,
			Rotation = saved.Rotation,
			Overlap = saved.Overlap,
			AltMask = saved.Alternate,
			Segment = saved.Segment
		};
	}

	/// <summary>Whether a saved flag word is something a track cell of OpenTPW holds: the bridge state and add-on bits only.</summary>
	public static bool HasUnknownFlags( SaveTrackCell saved ) => (saved.Flags & ~0x1Fu) != 0;

	/// <summary>
	/// The tiles reached from <paramref name="start"/> through the saved connection masks: a tile's arm toward a neighbour tile (two cells along the arm) whose own arm points back. The ride a tile belongs to is the
	/// one whose station reaches it; the saved cells carry no owner.
	/// </summary>
	public static IReadOnlyList<(int X, int Y)> Reach( IEnumerable<(int X, int Y)> start, IReadOnlyDictionary<(int X, int Y), SaveTrackCell> origins )
	{
		var seen = new HashSet<(int X, int Y)>();
		var order = new List<(int X, int Y)>();
		var queue = new Queue<(int X, int Y)>();
		foreach ( var tile in start )
		{
			if ( origins.ContainsKey( tile ) && seen.Add( tile ) )
			{
				order.Add( tile );
				queue.Enqueue( tile );
			}
		}
		while ( queue.Count > 0 )
		{
			var tile = queue.Dequeue();
			var mask = origins[tile].Mask;
			foreach ( var heading in TrackHeading.All )
			{
				if ( (mask & heading) == 0 )
					continue;
				var next = (X: tile.X + TrackStation.TileCells * TrackHeading.Dx( heading ), Y: tile.Y + TrackStation.TileCells * TrackHeading.Dy( heading ));
				if ( !origins.TryGetValue( next, out var neighbour ) || (neighbour.Mask & TrackHeading.Opposite( heading )) == 0 || !seen.Add( next ) )
					continue;
				order.Add( next );
				queue.Enqueue( next );
			}
		}
		return order;
	}

	/// <summary>
	/// The ride's saved sections (chunk 4: the section's kind word with the no-buoy bit, and its tile corner in section units) set against the sections a walk of the written track built.
	/// Returns the differences, empty when the lists are the same in the same order.
	/// </summary>
	// [BIN:TPW-EXE:0x543FF4 section chunk] the loader reads the handle, the type word and the two corner words and hands them to the section builder 0x54B2F0, which rebuilds the buoy and waypoint chain: the saved list is the sections in the order the builder appended them
	public static IReadOnlyList<string> CompareSections( IReadOnlyList<SaveTrackTile> saved, IReadOnlyList<TrackSection> walked )
	{
		var differences = new List<string>();
		var common = Math.Min( saved.Count, walked.Count );
		for ( var index = 0; index < common; index++ )
		{
			var (kind, x, y) = (unchecked( (int)saved[index].Type ), saved[index].Coordinate0, saved[index].Coordinate1);
			if ( walked[index].KindWord != kind || walked[index].X != x || walked[index].Y != y )
				differences.Add( $"section {index} saved (kind 0x{kind:X}, {x}, {y}), walked (kind 0x{walked[index].KindWord:X}, {walked[index].X}, {walked[index].Y})" );
		}
		if ( saved.Count != walked.Count )
			differences.Add( $"{saved.Count} saved sections, {walked.Count} walked" );
		return differences;
	}

	/// <summary>
	/// What the loader makes of chunk 3's ten words for one ride: the station reference (<c>+0xB4</c>, <c>+0xB8</c>) and orientation (<c>+0xB0</c>) the constructor 0x545890 stores, the performance (+0x1C), the laps or duration
	/// (+4) and the phase (+0x50). The words +0x10, +0x14 and +0x18 are the template words the controller copies and OpenTPW's controller does not hold.
	/// </summary>
	// [BIN:TPW-EXE:0x543638 ride chunk] the handle, the station cell (+0xB4, +0xB8), the orientation word (+0xB0), the words +0x10, +0x1C, +0x14 and +0x18, the duration (+4, logged "Set Ride Duration to %d") and the phase (+0x50); the controller is made by 0x545890 (copying the kind's template of 0xD0 bytes) and the handle it gets must be the saved one
	public readonly record struct RideWords( int StationX, int StationY, int Orientation, int Performance, int Laps, int Phase );

	public static RideWords WordsOf( SaveTrackRideHeader ride ) =>
		new( ride.Coordinate0, ride.Coordinate1, unchecked( (int)ride.FieldB0 ), unchecked( (int)ride.Field1C ), unchecked( (int)ride.Duration ), unchecked( (int)ride.Field50 ) );
}
