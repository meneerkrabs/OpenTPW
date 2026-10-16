namespace OpenTPW;

/// <summary>
/// The rides slice's approximation register (docs/OBJECTS.md, "Approximation register"). Each entry is
/// tagged <c>[APPROX:RIDES-NNN]</c> (texture-frame application: <c>MD2-9NN</c>) at its code site;
/// <see cref="LogOnce"/> lists them at the first catalog load.
/// </summary>
public static class RidesApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("RIDES-011", "the cells of '+' (11) and '#' (16) are stamped as tp.exe's placement 0x528a70 stamps them (object type 4, the owner word, flag word 0x10 on the cell and its track-layer cell) and validated as its add-on mode does (a cell of a straight, unflagged tile of the selected ride, 0x535953..0x5359b3), and a track end '<' '>' 'D' needs the cell beyond it on the map (0x5295a8); the type 0x15 the commit writes on that cell with the ride's owner word (ops 0x87, 0x15, 0x83, 0x84, 0x81 at 0x5295f7..0x52963f) is not written: it is the coaster module's cell marker (0x452d30, called by the coaster tool 0x434412, 0x443057, 0x444ee7, 0x445b50), which OpenTPW's coaster builder neither writes nor reads"),
		("RIDES-014", "base height = (lowest + highest terrain vertex) / 2 under the footprint's shape cells as the original's 0x466D70; the ground under the ride is not blended flat as the original does (0x467030), and the instance words +0xF0/+0xF4 are taken as the shape size"),
		("RIDES-018", "terrain, NoModify paths, overwrite-priority overlap (objects, paths and queues), owned-land and access-cell rules of tp.exe's validator 0x535670 (the access cells are laid by the placement, ObjectAccessCells); the second-layer link tests 0x53ae00/0x53ae90 and the types whose record +0x288 is 1 (their Bumper.WhichTrackType turns the access-cell option 0x81b0cc off, 0x46d605) are not modelled; guests on a path cell a placement clears are set down on the nearest path cell, where tp.exe's handling of a guest inside a new footprint is not traced; Level enforces economy purchases"),
		("RIDES-019", "End is not created: the original creates it through 0x516b00 from the lab's message handler 0x503d40 (message 0x11) when the last research item completes, by the GameType word DAT_00fb3b7c (2: Instant Action at once; 0: Full Simulation once every online park record is done, 0x5b0c40; 1: never), after posting the advisor message 0xa7"),
		("RIDES-021", "the build cursor's cell is the cell of the ray's nearest terrain hit (TerrainRayPick: the original's heightfield triangle planes, 0x45bf90; the cell is the hit divided by the cell size, 0x45d560, and the click command 0x46c5d0 hands it to 0x524790); the original's nearest hit also tests the model triangles of the objects in the cells the ray crosses (0x45cdc0, 0x45d090), so the cell moves onto an object that covers the terrain hit, and it keeps the previous hit when the ray finds nothing; the port picks the terrain only (the generic sandbox has no heightfield and picks on the Z = 0 plane, [EXT:build-cursor-ground-plane]); the anchor taken from the clicked cell is the original's (0x528b44), so the click is local (W/2, H/2) when each side is odd or at most 5 cells (the only shipped exception is space 3104 Orbiter, 6 x 3: local (3, 1) at 0 degrees, (2, 2) at 90)"),
		("RIDES-031", "a Theme Park Inc world keeps the cells its layout was given and types none of its footprints, so the road signs' '@' cells (\"*@*\", \"*@@*\") are plain occupied cells; tp.exe's commit stamps '@' as a type-1 NoModify path cell linked to its path neighbours (0x529173..0x529208) and 'Q' as a queue cell (0x529119), which ParkCellMap.WriteFootprint does for Theme Park World, whose shapes have neither; Theme Park Inc's own placement is not traced"),
		("RIDES-032", "every placed instance takes the callback update route; the PC list pass 0x44e410 (updates without posing via 0x473c70(inst, 0, 8), posing only with instance flag 0x400000) is not modelled, so an instance the renderer does not pose is posed here; the callback at model record +0x140 has no call site in the executable (no call or jump through that offset), so which path invokes it is not found"),
		("RIDES-040", "Theme Park Inc fixed scene records saved at (0, 0) use their local model origin; the thing records of the six that have an archive carry the fixed-item default position (128, 128) and angle 0, so the save leaves no transform field unread; how the original places a local-coordinate fixed model at its anchor is not read"),
		("MD2-900", "the slot's frame selection is the PC writer 0x470c90 (the last key at or before the clip time wins, none before the first key keeps the previous frame, later tracks of a slot overwrite earlier ones, hyenasm1 holds two tracks on slot 15); untraced: the draw-path reader of a slot's current frame, whether a slot no bound clip addresses returns to frame 0 (OpenTPW does; the writer never resets), and the fixed-item pose hold is not extended to frames"),
		("MD2-901", "texture-frame keys naming a frame outside the slot's frame-name list, or a slot the model does not have, are ignored (the one corpus case, Pmegacostm slot 12, addresses a slot its base model lacks); the PC writer 0x470c90 does not check either index, so the original's behaviour depends on the unread consumer of the slot record"),
	};

	private static bool logged;

	public static void LogOnce()
	{
		if ( logged )
			return;
		logged = true;
		foreach ( var (id, rule) in Entries )
			Log?.Warning( $"[APPROX:{id}] {rule}" );
	}
}
