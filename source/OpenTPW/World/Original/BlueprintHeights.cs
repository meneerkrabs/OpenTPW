namespace OpenTPW;

/// <summary>
/// The raise the original's ghost <c>SetCell</c> gives a cell's marker (docs/reverse/BLUEPRINT.md, BLUEPRINT-001): 10 x ((int)H + 9) / 10 of the cell's ground H for a cell that holds an object, a track
/// piece, a post or no terrain, and 0 for every other cell.
/// </summary>
internal static class BlueprintHeights
{
	// [BIN:TPW-EXE:0x5330C6–0x533189] truncate H, add 9, signed divide by 10, multiply by 10.
	// 0x67A830 temporarily sets x87 RC to truncate; this is not Math.Ceiling, particularly for negative H.
	internal static float? Quantize( float height )
	{
		if ( !float.IsFinite( height ) || height < int.MinValue || height >= 2147483520f ) return null;
		return ((long)height + 9) / 10 * 10f;
	}

	/// <summary>
	/// Whether the second-layer record of a cell raises the marker: the record types 0xB and 0x10 (a kart or water track piece), their extensions 0xC and 0x11 and 0x19 (a hoarding post).
	/// </summary>
	// [BIN:TPW-EXE:0x005330AD second-layer types] SetCell reads the type of the cell's record (0x4D0AF0 + 8) and, for 0xB, 0xC, 0x10, 0x11 or 0x19, sets the raise from the ground 0x452AE0 (0x5330C6..0x5330F5); a station record (0xD, 0x12) is none of them
	internal static bool RaisesSecondLayer( byte recordType ) => recordType is 0xB or 0xC or 0x10 or 0x11 or 0x19;

	/// <summary>Whether the map cell's own type raises the marker: 4, 9 and 10 (an object), 7 (no terrain) and 30 (a fixed walkway); they overwrite what the second layer set.</summary>
	// [BIN:TPW-EXE:0x00533102 map cell types] the cell's type (+8) of 4, 9, 0xA, 7 or 0x1E replaces the raise with the same ground (0x533106..0x533189)
	internal static bool RaisesMapType( byte type ) => type is 4 or 9 or 10 or 7 or 30;

	/// <summary>
	/// The type of the cell's second-layer record as <c>0x4D0AF0</c> hands it to the ghost: the kart and water tiles' own cells (an extension reads 0xC or 0x11, both raise), and the fence layer's posts
	/// (0x19) and the cells beside them (0xC); 0 for none.
	/// </summary>
	internal static byte RecordType( ParkCellMap cells, int x, int y )
	{
		var track = cells.Tracks.Get( x, y );
		if ( !track.IsEmpty )
			return track.IsExtension ? (byte)0xC : track.Type;
		return cells.Land?.LayerFor( cells.Width, cells.Height ).TypeAt( x, y ) ?? 0;
	}

	/// <summary>
	/// The raise of one cell's marker, or null when the cell is outside the map or its ground is no number. <paramref name="ground"/> is the cell lookup <c>0x452AE0</c> of the cell (x, y): the second-layer piece
	/// instance's height map, else the owner object's, else the terrain at the cell corner.
	/// </summary>
	// [BIN:TPW-EXE:0x00533102 ghost raise] the raise starts as 0, takes 10 x ((int)H + 9) / 10 for a second-layer type of 0xB, 0xC, 0x10, 0x11 or 0x19 and again for a map type of 4, 7, 9, 10 or 0x1E, both with H = 0x452AE0(x, y, 0); every other cell stays at 0 (a plain cell: the face builder 0x53DDD0 adds the terrain at each vertex)
	internal static float? Sample( ParkCellMap cells, int x, int y, Func<int, int, float> ground )
	{
		if ( !cells.InBounds( x, y ) )
			return null;
		var raise = 0f;
		if ( RaisesSecondLayer( RecordType( cells, x, y ) ) )
		{
			if ( Quantize( ground( x, y ) ) is not { } second )
				return null;
			raise = second;
		}
		if ( RaisesMapType( cells.PrimaryTypeAt( x, y ) ) )
		{
			if ( Quantize( ground( x, y ) ) is not { } map )
				return null;
			raise = map;
		}
		return raise;
	}
}
