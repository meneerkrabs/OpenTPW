namespace OpenTPW;

/// <summary>
/// The kart and water track slice's approximation register (docs/TRACKS.md). Each entry is tagged <c>[APPROX:TRACK-NNN]</c> at its code site; the
/// words before the colon name the topic. Everything else of the slice is traced from the Mac and Windows binaries (BIN labels at the code sites).
/// </summary>
public static class TrackApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("TRACK-001", "TRACK-HEIGHT: a vehicle's height is the four-probe mean of the original (0x546280) over the pieces' water meshes (boats) or their .hmp height profiles (karts; the profile matches the mesh's top surface sample for sample); a tile stands on the mean ground of its four cells, the station tiles use one water height or the straight piece's floor; the original's surface-face flag (bit 0x8000 of the face's first u16) and surface object lookup are not decoded"),
		("TRACK-002", "TRACK-HMP: the .hmp height profiles are parsed and bounded but nothing reads them; their consumers are not traced (RIDES-014)"),
		("TRACK-003", "TRACK-OFFSET: each 20-unit tile turns about its own centre, where the original instances at the origin cell plus a one-cell offset by rotation; Bumper.NorthXAdjust and the other adjusts are read but not applied"),
		("TRACK-004", "TRACK-X2LINK: the multi-bit flow links of water crossings (0x14, 0x41, 0x50) choose the second crossing family as traced, but OpenTPW writes them by OR-ing the headings of a crossing walked twice; the original's writer is not traced"),
		("TRACK-005", "TRACK-STATION: the station is two tiles on the footprint's first two tile columns along the heading in the rows beside the entrance, the circuit starts at the first, and the station and finish buoys sit behind the station reference; the original's station-derived start cell and cell writes are not traced"),
		("TRACK-006", "TRACK-MSG: the advisor and event messages after a circuit walk (ids 0xCC, 0x151, 0xB8, 0xD6) are not shown; OpenTPW posts its own status line"),
		("TRACK-007", "TRACK-DEMOLISH: removing a water ride clears the tiles laid for it and pays nothing; the original's one Spend (2 × the cell cost × a percentage) has an untraced sign"),
		("TRACK-008", "TRACK-ADDON: jumps and tunnels (map flag 0x10, section kinds 12 and 13, their run counters and values) are deferred"),
		("TRACK-009", "TRACK-SNAP: a track line from the start rounds an odd cursor distance down to whole two-cell tile steps; the original's rounding is not traced"),
		("TRACK-010", "TRACK-BRIDGE: a straight tile is a bridge when terrain water lies under any of its cells or it replaced path cells; the original tests map types 2, 7 and 30"),
		("TRACK-011", "TRACK-TICK: the controller runs 30 ticks per second on every second fixed step; the module tick's caller and rate are not traced"),
		("TRACK-012", "TRACK-HEADING: a launched boat or kart starts heading (and a kart drawing along) the station's flow; the original leaves a new vehicle at heading 0"),
		("TRACK-013", "TRACK-BREAK: BUMP 8 sets the controller state to broken (2) or fixed (0) and BUMP 9 to worn (1) or fixed, as the traced routines do; a worn kart puffs (sparks or exhaust) but what the worn state changes for water boats is not traced, and nothing but BUMP 9 sets it in the shipped data"),
		("TRACK-014", "TRACK-WALK: the circuit walk's junction rule beyond 'the cell ahead has type 0', its cut-off and arrival from a side arm are not traced"),
		("TRACK-015", "TRACK-BUMPER: the bumper-car controller types (-1, -6, -11, -14), the chase mode and the add-on handler 0x00548E80 are not ported; the go-kart and water types are complete apart from their own TRACK entries"),
		("TRACK-016", "TRACK-SPRAY: the water boats' spray cadence keeps a counter only and makes no particle (the particle 0x60 of 0x00547F50), no boat sound, and no boat model variants; the kart effects are TRACK-025"),
		("TRACK-017", "TRACK-XKEEP: the four keep-out circles of a crossing are clipped to the tile's quadrants like a corner's; the trace gives their centres and radius only"),
		("TRACK-018", "TRACK-ANIM: the pieces are static; the water texture animation (BUMP 17) is stored but not played and the tee pieces show all four arms"),
		("TRACK-019", "TRACK-SCOPE: a controller's collision objects, buoys and boats are its own, where the original's pools are module-wide"),
		("TRACK-020", "TRACK-POOL: a circuit that overflows the section, collision-object or buoy pool is invalid and builds no geometry; the original's overflow path is not traced"),
		("TRACK-021", "TRACK-OWNER: every track tile records the ride it was laid for (the station's first-tile cell index plus one); tiles of different rides never connect and a demolition removes the ride's own tiles; the original's cell record has no owner field and its walk and demolition follow the masks"),
		("TRACK-023", "TRACK-KARTLOOK: a kart's drawn heading (+0x54) starts equal to its steering heading and follows the velocity as traced; the break smoke's cadence is OpenTPW's where the executable gives none"),
		("TRACK-024", "TRACK-BANK: the bank of the hovering karts leans into the turn and is clamped to 0.6 radians; the original's term (yaw rate / 13 x pi/2) has an untraced sign and no clamp"),
		("TRACK-025", "TRACK-KARTFX: the kart sounds and particles are submitted as traced (global cat_rides 12 and 24, EventMap slots 0, 1 and 3, particles 1, 2, 10 and 15) but the cat_rides ids are not mapped to named sounds, the engine voice gets no pitch parameter (speed / 3 is raised, not applied), sounds play at the vehicle without the original's emitter updates, and the break smoke on the sparks node has an assumed 32-tick cadence"),
		("TRACK-026", "TRACK-HEADS: a kart's riders are drawn as head sprites at the head dummies (class 0x100000B1, numbers 1 and 2) through the ride-head renderer; the original's draw internals (0x0044B410, 0x00475A10) and the seat order of riders are not traced, and a boarding guest is not drawn between BUMP 1 and the car's creation")
	};
}
