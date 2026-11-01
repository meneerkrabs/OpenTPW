namespace OpenTPW;

/// <summary>
/// Which cells the build tools mark and with which marker texture (docs/reverse/BLUEPRINT.md): the path or queue line preview and
/// the footprint of the object being placed. Pure functions, shared by both HUDs and the tests.
/// </summary>
public static class BlueprintGhosts
{
	/// <summary>
	/// The preview of a path or queue line: every cell of the line is marked. The cells before the first refusal are blue; the
	/// refused cell and every later cell are red, because the validator keeps refusing once its flag is set.
	/// </summary>
	// [BIN:TPW-EXE:0x00536100 LayLine ghost] the line preview calls the ghost SetCell 0x532FC0 for every cell of the line and it answers 1 for allowed (0x53391D) and refused (0x534009) cells alike, so the loop never stops early; the validator 0x535670 refuses every later cell once the flag 0x816D50 is set (0x5358A0; reset at 0x527C52 and 0x5247AA), and a refused ghost cell takes the red texture (the second AddFace argument pair 1 at 0x533FEC against the chooser's result at 0x5338F3)
	// [APPROX:BLUEPRINT-003] the validator's answer for a cell that already holds the line's type is not read (0x535670 after 0x5358B5): such cells before the refusal stay blue
	public static List<(int X, int Y, BlueprintMarker Marker)> LineCells( IReadOnlyList<(int X, int Y)> line, (int X, int Y)? stoppedAt,
		IReadOnlyList<((int X, int Y) Origin, BlueprintMarker Symbol)>? symbols = null )
	{
		var cells = new List<(int X, int Y, BlueprintMarker Marker)>( line.Count );
		var refused = false;
		// a symbol tile is one face over its four cells: the origin carries it and the other three cells give way
		var covered = symbols is { Count: > 0 } ? symbols.SelectMany( symbol => new[] { (symbol.Origin.X + 1, symbol.Origin.Y), (symbol.Origin.X, symbol.Origin.Y + 1), (symbol.Origin.X + 1, symbol.Origin.Y + 1) } ).ToHashSet() : null;
		var origins = symbols?.ToDictionary( symbol => symbol.Origin, symbol => symbol.Symbol );
		foreach ( var cell in line )
		{
			refused |= stoppedAt is { } stop && cell == stop;
			if ( covered?.Contains( cell ) == true )
				continue;
			cells.Add( (cell.X, cell.Y, origins != null && origins.TryGetValue( cell, out var symbol ) ? symbol : refused ? BlueprintMarker.Refused : BlueprintMarker.Allowed) );
		}
		return cells;
	}

	/// <summary>The direction byte of a build rotation: 1, 0x40, 0x10 and 4 for 0, 90, 180 and 270 degrees.</summary>
	// [BIN:TPW-EXE:0x00528F62 build direction byte] the footprint loop sets [esp+0x14] to 1 (default), 0x40 (90), 0x10 (180) or 4 (270) (0x528F8B, 0x52900F, 0x528FE7, 0x528FB7)
	public static byte BuildDirection( int rotation ) => rotation switch
	{
		90 => 0x40,
		180 => 0x10,
		270 => 4,
		_ => 1,
	};

	/// <summary>A symbol's direction byte turned by the build direction byte: the original's <c>0x4D8C20</c>.</summary>
	// [BIN:TPW-EXE:0x004D8C20 rotate direction] shifts the byte left by log2 of the build direction byte; a result above 0x80 wraps by an arithmetic shift of 8
	public static byte Rotate( byte buildDirection, byte symbolDirection )
	{
		var shifts = 0;
		for ( var value = buildDirection; value > 1; value >>= 1 )
			shifts++;
		var shifted = (symbolDirection & 0xFF) << shifts;
		return shifted > 0x80 ? (byte)(shifted >> 8) : (byte)shifted;
	}

	/// <summary>The opposite direction byte: the original's <c>0x4D8C00</c> (a byte below 0x10 shifts left by 4, otherwise right by 4).</summary>
	// [BIN:TPW-EXE:0x004D8C00 opposite direction] 0 stays 0, a byte below 0x10 goes <<4, any other >>4
	public static byte Opposite( byte direction ) => direction == 0 ? (byte)0 : direction < 0x10 ? (byte)(direction << 4) : (byte)(direction >> 4);

	/// <summary>
	/// The marked cells of an object footprint, and the direction bytes of its arrow cells. Every occupied cell is blue, or red when the
	/// placement is refused; on a refusal-free placement the cells of the shape's first row carry <c>m_front</c> turned to the build
	/// direction, and the outside cell of the entrance <c>m_enter</c> and of the exit <c>m_exit</c> carry their arrows.
	/// </summary>
	// [BIN:TPW-EXE:0x0052967E footprint front row] the placement 0x528A70 in ghost mode stamps each cell of the stamping loop with SetCell(0x104, 0) (0x52969C) and the cells of row 0 (outer loop counter 0, shape cell row v = 0) with SetCell(0x104, 6 = m_front, build direction byte) (0x529688); the entrance and exit cells take the same path (the exit's ghost branch 0x529313 jumps to 0x529688)
	// [BIN:TPW-EXE:0x0052989A entrance outside cell] with the queue option (0x52FAB0 true, default) the entrance's outside cell gets SetCell(0x104, 3 = m_enter, opposite of the rotated entrance direction) and the exit's outside cell SetCell(0x118, 4 = m_exit, opposite of the rotated exit direction) (0x529B1C); the entrance-side bounds check and stamp precede the exit-side work (0x529744, 0x5299E2); either access-presence flag enters this sequence (0x529711..0x529722), so absent/multiple access points need separate treatment
	// [BIN:TPW-EXE:0x0052390E object hover preflight] the hover calls placement 0x528A70 with commit=0/check=1, sets force-red 0x81B604 when it returns 0 (0x52391A), then calls it with commit=0/check=0 (0x5239EE); SetCell reads that flag before its per-cell validator (0x53319A), so a refused ordinary footprint is all red, including a NoModify refusal that does not set the validator's sticky flag
	// [APPROX:BLUEPRINT-003] this context-free fallback omits refused outside access markers; the grid-aware overload below supplies the proven ordinary paired-access case only. Other access selection/bounds cases and special footprint symbols '@', 'Q', '+', '#' and track ends keep simplified markers (docs/reverse/BLUEPRINT.md).
	public static (List<(int X, int Y, BlueprintMarker Marker)> Cells, Dictionary<(int X, int Y), byte> Directions) Footprint(
		ObjectShape shape, int anchorX, int anchorY, int rotation, bool allowed, bool original = false )
	{
		var cells = new List<(int X, int Y, BlueprintMarker Marker)>();
		var directions = new Dictionary<(int X, int Y), byte>();
		var build = BuildDirection( rotation );
		foreach ( var (x, y, cell) in ObjectFootprint.GetCells( shape, anchorX, anchorY, rotation ) )
		{
			var type = cell.CellType;
			// [BIN:TPW-EXE:0x00529526 track end] a type-23 cell stamps its own cell (blue, direction byte 0x10 turned by the build direction) only when its shape direction byte is not 0, and the cell beside it in the turned direction gets m_direct (texture 5, the same byte) (0x5295D9); a cell that is none (direction 0) or off the map gets no face
			if ( original && type == ObjectShapeSymbols.TypeTrackEnd )
			{
				if ( cell.Direction != 0 )
				{
					cells.Add( (x, y, allowed ? BlueprintMarker.Allowed : BlueprintMarker.Refused) );
					directions[(x, y)] = Rotate( build, 0x10 );
					var beside = Rotate( build, cell.Direction );
					var (dx, dy) = beside switch { 1 => (0, -1), 4 => (1, 0), 0x10 => (0, 1), 0x40 => (-1, 0), _ => (0, 0) };
					if ( (dx, dy) != (0, 0) )
					{
						cells.Add( (x + dx, y + dy, allowed ? BlueprintMarker.Direct : BlueprintMarker.Refused) );
						directions[(x + dx, y + dy)] = Rotate( build, 0x10 );
					}
				}
				continue;
			}
			// [BIN:TPW-EXE:0x00529173 own-branch kinds] '@' (1), '+' (11) and '#' (16) stamp SetCell(0x104, 0, 0), 0x10B and 0x110: blue or red, and no m_front on the first row, which only the default branch (4, 3) and the entrance and exit give
			var plain = original && type is ObjectShapeSymbols.TypeAt or ObjectShapeSymbols.TypeUpgrade or ObjectShapeSymbols.TypeHash;
			var front = allowed && !plain && cell.V == 0 && cell.Kind is ObjectCellKind.Occupied or ObjectCellKind.Entrance or ObjectCellKind.Exit;
			cells.Add( (x, y, !allowed ? BlueprintMarker.Refused : front ? BlueprintMarker.Front : BlueprintMarker.Allowed) );
			if ( front )
				directions[(x, y)] = build;
		}
		if ( !allowed )
			return (cells, directions);
		var access = shape.Cells.Where( cell => cell.Kind is ObjectCellKind.Entrance or ObjectCellKind.Exit ).ToArray();
		var points = ObjectFootprint.GetAccessPoints( shape, anchorX, anchorY, rotation ).ToArray();
		var entrance = Array.FindIndex( points, point => point.Kind == ObjectCellKind.Entrance );
		if ( entrance < 0 )
			return (cells, directions);
		cells.Add( (points[entrance].OutsideX, points[entrance].OutsideY, BlueprintMarker.Enter) );
		directions[(points[entrance].OutsideX, points[entrance].OutsideY)] = Opposite( Rotate( build, access[entrance].Direction ) );
		var exit = Array.FindIndex( points, point => point.Kind == ObjectCellKind.Exit );
		if ( exit >= 0 )
		{
			cells.Add( (points[exit].OutsideX, points[exit].OutsideY, BlueprintMarker.Exit) );
			directions[(points[exit].OutsideX, points[exit].OutsideY)] = Opposite( Rotate( build, access[exit].Direction ) );
		}
		return (cells, directions);
	}

	/// <summary>
	/// The ordinary queued object's preview with the host grid and placement result available. A known placement refusal also
	/// marks the outside access cells red for a supported, in-bounds footprint with exactly one entrance and one exit.
	/// The caller's colour decision stays separate: a money-only or other global refusal keeps the legacy footprint-only fallback.
	/// </summary>
	// [BIN:TPW-EXE:0x00529744 refused access bounds] after the footprint ghost, a failed entrance-side bounds check returns before both outside stamps; a failed exit-side check (0x5299E2) returns after the entrance stamp. With the queue option on, the ghost stamps 0x104/3 at 0x52989A and 0x118/4 at 0x529B1C, both forced red by 0x81B604 after a failed preflight. The host grid's dimensions replace the original 128-cell bound (0x4D8300).
	// [APPROX:BLUEPRINT-003] only ordinary type-4/9/10 footprints with one entrance and one exit use this refused-access branch; special symbols, missing/multiple access points, partially off-grid footprints, no-queue mode and unproven refusal reasons retain the existing fallback rather than guess their stamping rules
	public static (List<(int X, int Y, BlueprintMarker Marker)> Cells, Dictionary<(int X, int Y), byte> Directions) Footprint(
		ObjectShape shape, int anchorX, int anchorY, int rotation, bool allowed, IParkGrid grid, bool hasQueue,
		OriginalPlacementResult placementResult, bool original = false )
	{
		var preview = Footprint( shape, anchorX, anchorY, rotation, allowed, original );
		if ( allowed || !hasQueue || placementResult is not (OriginalPlacementResult.Path or OriginalPlacementResult.Occupied
			or OriginalPlacementResult.NotOwned or OriginalPlacementResult.AccessBlocked or OriginalPlacementResult.OutsideTerrain) )
			return preview;
		if ( shape.Cells.Any( cell => cell.CellType is not (ObjectShapeSymbols.TypeFree or ObjectShapeSymbols.TypeOccupied
			or ObjectShapeSymbols.TypeEntrance or ObjectShapeSymbols.TypeExit) ) )
			return preview;
		var points = ObjectFootprint.GetAccessPoints( shape, anchorX, anchorY, rotation ).ToArray();
		if ( points.Count( point => point.Kind == ObjectCellKind.Entrance ) != 1 || points.Count( point => point.Kind == ObjectCellKind.Exit ) != 1 )
			return preview;
		bool InBounds( int x, int y ) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height;
		var bounds = ObjectFootprint.GetBounds( shape, anchorX, anchorY, rotation );
		if ( !InBounds( bounds.MinX, bounds.MinY ) || !InBounds( bounds.MaxX, bounds.MaxY ) )
			return preview;
		var entrance = points.Single( point => point.Kind == ObjectCellKind.Entrance );
		if ( !InBounds( entrance.OutsideX, entrance.OutsideY ) )
			return preview;
		preview.Cells.Add( (entrance.OutsideX, entrance.OutsideY, BlueprintMarker.Refused) );
		var exit = points.Single( point => point.Kind == ObjectCellKind.Exit );
		if ( InBounds( exit.OutsideX, exit.OutsideY ) )
			preview.Cells.Add( (exit.OutsideX, exit.OutsideY, BlueprintMarker.Refused) );
		return preview;
	}
}
