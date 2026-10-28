namespace OpenTPW;

/// <summary>
/// The rides slice's approximation register (docs/OBJECTS.md, "Approximation register"). Each entry is
/// tagged <c>[APPROX:RIDES-NNN]</c> (texture-frame application: <c>MD2-9NN</c>; MD2-900 is retired) at its code site;
/// <see cref="LogOnce"/> lists them at the first catalog load.
/// </summary>
public static class RidesApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("RIDES-011", "add-on double-layer flags and rotated endpoint mode4 validation are implemented; coaster type21 markers now integrate single-cell preview, live stack payload and teardown. Residual: native sticky tool globals are not modeled and defensive host owner/generation leases avoid unconditional native foreign-cell commits; see docs/COASTER-CELL-MARKERS.md"),
		("RIDES-018", "terrain, NoModify paths, overwrite-priority overlap (objects, paths and queues), owned-land and access-cell rules of tp.exe's validator 0x535670 (the access cells are laid by the placement, ObjectAccessCells); the second-layer link tests 0x53ae00/0x53ae90 and the types whose record +0x288 is 1 (their Bumper.WhichTrackType turns the access-cell option 0x81b0cc off, 0x46d605) are not modelled; guests on a path cell a placement clears are set down on the nearest path cell, where tp.exe's handling of a guest inside a new footprint is not traced; Level enforces economy purchases"),
		("RIDES-021", "the cursor's cell is the cell of the ray's nearest hit (the hit divided by the cell size, 0x45d560; the click command 0x46c5d0 hands it to 0x524790): the terrain hit (TerrainRayPick, the original's heightfield triangle planes of 0x45bf90) or, when nearer, the first object model the ray meets in 0x45bf90's walk of the cells from the camera (ObjectRayPick and ObjectPickModel: the walk, the edge clamp and the height window of 0x45cdc0 with the owner and height of the cell lookup 0x452ae0, and the model test 0x45d090, as read); not ported: a cell whose second-layer piece instance answers 0x452ae0 (a kart or water ride's track tile) is not tested, where the original tests the piece's model; an instance with the group flag 0x80000 tests its group's members instead (0x447790, 0x4477d0), not modelled; the node state bits 0x20 (prune the children) and 0x8000 are not modelled (0x200 of 0x8210 marks every dummy, which holds no faces); the refresh 0x473c30, which recomputes the face normals of vertex-passed nodes (0x473a40) when the flags word +0x30 of the instance's model has 0x40000 (set after a vertex pass, 0x469a66, 0x471c10) and 0x20000 (set at load for load flag 0x800, 0x46220e, from instance flag 0x10000 of 0x4629d0), is not ported: the port uses the stored normals; the hit instance that 0x45bf90 hands to 0x469340 (0x45ccd5) and the hover handler to 0x469180 (its normals copied, node flag 0x1000000 and model flag 0x400000 set: a hover highlight, not read further) is not used; the original keeps its previous hit when the ray finds nothing, where the port has no cell; the generic sandbox has no heightfield and takes the Z = 0 plane for the terrain hit ([EXT:build-cursor-ground-plane]); the anchor taken from the clicked cell is the original's (0x528b44), so the click is local (W/2, H/2) when each side is odd or at most 5 cells (the only shipped exception is space 3104 Orbiter, 6 x 3: local (3, 1) at 0 degrees, (2, 2) at 90)"),
		("RIDES-031", "a Theme Park Inc world keeps the cells its layout was given and types none of its footprints, so the road signs' '@' cells (\"*@*\", \"*@@*\") are plain occupied cells; tp.exe's commit stamps '@' as a type-1 NoModify path cell linked to its path neighbours (0x529173..0x529208) and 'Q' as a queue cell (0x529119), which ParkCellMap.WriteFootprint does for Theme Park World, whose shapes have neither; Theme Park Inc's own placement is not traced"),
		("RIDES-032", "this simulation updates and poses every instance in its tick, seen or not (the saved node states, picks, point lights and the hash must not depend on the camera); the original's list pass 0x44e410 updates an instance the renderer did not pose in the previous frame without sampling (0x473c70(inst, 0, 8)) and the render callback 0x463030, which the mesh prepare 0x57a750 calls as call eax (so no direct call through +0x140 exists), poses it. What the handshake shows is ported (flags 0x200000/0x800000, the stored pose restored on return 0x44e380 -> 0x4726d0, the key table 0x44ab30); the callback's visibility test is each drawn part's, where the original's is the whole mesh object's"),
		("RIDES-040", "Theme Park Inc fixed scene records saved at (0, 0) use their local model origin; the thing records of the six that have an archive carry the fixed-item default position (128, 128) and angle 0, so the save leaves no transform field unread; how the original places a local-coordinate fixed model at its anchor is not read"),
		("RIDES-045", "a Theme Park Inc object that a mod brings into a Theme Park World park runs its original script, but only the script variables Theme Park World's engine writes are driven: the variables the scripts of the three Theme Park Inc worlds declare beyond the standard ride set (VAR_FPBYPASS in 69 scripts, VAR_SPEED in 19, VAR_SPECIAL in 15, the sideshows' VAR_WIN*/VAR_IN*/VAR_TIMEOUT/VAR_WINLANE, the litter bin's VAR_BOMB, VAR_PRANKED, VAR_ONFIRE, VAR_DIRTY, VAR_EMPTY, VAR_FIRERISK, VAR_CONTENTS and VAR_TIMER, the staff rooms' VAR_CLOSED) keep their initial 0, so the branches they guard never run (the litter bin's WAITABS, opcode 45, sits behind VAR_BOMB); what Theme Park Inc's executable writes into them, and when, is not read"),
				("MD2-901", "texture-frame keys naming a frame outside the slot's frame-name list, or a slot the model does not have, are ignored (the one corpus case, Pmegacostm slot 12, addresses a slot its base model lacks); the PC writer 0x470c90 does not validate the slot index and the traced frame consumer 0x575f90 reads its pointer array unchecked; unsafe out-of-range memory behaviour is deliberately not reproduced"),
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
