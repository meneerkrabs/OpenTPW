namespace OpenTPW;

/// <summary>
/// The kart and water track slice's approximation register (docs/TRACKS.md). Each entry is tagged <c>[APPROX:TRACK-NNN]</c> at its code site (a retired one leaves its BIN labels, a reclassified one an <c>[EXT:name]</c> decision, docs/TRACKS.md); the
/// words before the colon name the topic. Everything else of the slice is traced from the Mac and Windows binaries (BIN labels at the code sites).
/// </summary>
public static class TrackApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("TRACK-001", "TRACK-HEIGHT: a vehicle's height is the four-probe mean of the original (0x546280) over the pieces' water meshes (boats) or their .hmp height profiles (karts; the profile matches the mesh's top surface sample for sample); a tile stands on the mean ground of its four cells, the station tiles use one water height or the straight piece's floor; the original's pose routine calls the cell object, select object and sample routines 0x450AC0, 0x450EA0 and 0x4511A0, whose sampler mode (face flag 0x8000 over the mesh parts, the 0x4527F0 profile sampler or the mesh at +0x2C) the track pieces take is not settled"),
		("TRACK-002", "TRACK-HMP: the .hmp height profile is the original's per-object height map (loader 0x451640 from the mesh loader 0x4629D0, rebuilt from the mesh by 0x451880 when missing or mis-sized); OpenTPW parses it and uses it only as the kart surface (TRACK-001); the sampler mode that reads it (0x4527F0) is not traced"),
		("TRACK-003", "TRACK-OFFSET: each 20-unit tile turns about its own centre, where the original instances at the origin cell plus a one-cell offset by rotation (the Bumper.*Adjust keys are the station-reference offsets of TRACK-005 and TRACK-030, not a mesh pivot)"),
		("TRACK-004", "TRACK-X2LINK: the multi-bit flow links of water crossings (0x14, 0x41, 0x50) choose the second crossing family as traced, but OpenTPW writes them by OR-ing the headings of a crossing walked twice; the original's writer is not traced"),
		("TRACK-005", "TRACK-STATION: the station is two tiles on the footprint's first two tile columns along the heading in the rows beside the entrance, the circuit starts at the first, and the station and finish buoys sit behind the station reference; the original's station is the line of 0x52A1A0 (two tiles of type 13 or 18 from the attraction cell, with the walk start and exit cell per rotation), whose relation to OpenTPW's placement anchor and whose line modes 0x80, 0x82 and 0x83 are not traced"),
		("TRACK-006", "TRACK-MSG: the advisor and event messages after a circuit walk (ids 0xCC, 0x151, 0xB8, 0xD6) are not shown; OpenTPW posts its own status line"),
		("TRACK-007", "TRACK-DEMOLISH: removing a water ride clears the tiles laid for it and pays nothing; the original's one Spend (2 × the cell cost × a percentage) has an untraced sign"),
		("TRACK-008", "TRACK-ADDON: jumps and tunnels (map flag 0x10, section kinds 12 and 13, their run counters and values) are deferred"),
		("TRACK-010", "TRACK-BRIDGE: a straight tile is a bridge when terrain water lies under any of its cells or it replaced path cells; the original tests map types 2, 7 and 30"),
		("TRACK-014", "TRACK-WALK: the circuit walk's junction rule beyond 'the cell ahead has type 0', its cut-off and arrival from a side arm are not traced"),
		("TRACK-018", "TRACK-ANIM: the pieces are static; the water texture animation (BUMP 17) is stored but not played and the tee pieces show all four arms"),
		("TRACK-019", "TRACK-SCOPE: the original's vehicle pass, object searches and pool limits are module-wide and its 32-tick gates use one module frame counter (0x546C80); OpenTPW runs them per controller, so vehicles of different rides never collide (reachable for the karts of adjacent circuits), the gates' phase depends on the controller's creation and the pool limits count per ride"),
		("TRACK-025", "TRACK-KARTFX: the kart sounds and particles are submitted as traced (global cat_rides 12 and 24, EventMap slots 0, 1 and 3, particles 1, 2, 10 and 15) but the engine voice gets no parameter (the original pushes EventMap variable 10 = speed / 3 into the voice every tick, 0x51BC40) and voices do not follow their vehicle (the original moves them every tick, 0x51C270); the smoke emitter sits at the car's reference point plus its height, not on its sparks node"),
		("TRACK-026", "TRACK-HEADS: a kart's riders are drawn as head sprites at the head dummies (class 0x100000B1, numbers 1 and 2) through the ride-head renderer in the original's seat order (rider list order, 0x549C60); the draw internals (0x0044B410, 0x00475A10) and the boarding guest between BUMP 1 and the car's creation are not traced"),
		("TRACK-030", "TRACK-ARENA: the bumper arena is centred on the middle of the ride's footprint, where the original puts it at (cursor cell + Adjust[rotation] + fixed[rotation]) x 0xC00 + 0x600 (0x529E10); the relation of OpenTPW's placement anchor to the original cursor cell is not mapped"),
		("TRACK-031", "TRACK-BUMPERLOOK: bumper cars are drawn like karts on flat ground: the four probes read the terrain, not the ride's own platform mesh, and the jungle cars' wake (mesh 0) is drawn as a shadow-style instance; the traced effects (crash code 5, sparks, engine voice, smoke emitter, BeamUpCar) are submitted but the engine voice gets no parameter (TRACK-025)")
	};
}
