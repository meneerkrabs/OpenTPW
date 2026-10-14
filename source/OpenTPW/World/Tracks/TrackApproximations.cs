namespace OpenTPW;

/// <summary>
/// The kart and water track slice's approximation register (docs/TRACKS.md). Each entry is tagged <c>[APPROX:TRACK-NNN]</c> at its code site; the
/// words before the colon name the topic. Everything else of the slice is traced from the Mac and Windows binaries (BIN labels at the code sites).
/// </summary>
public static class TrackApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("TRACK-001", "TRACK-HEIGHT: a boat's height is the four-probe mean of the original (0x546280) over the pieces' water meshes (a tile stands on the mean ground of its four cells, the station tiles use one water height); the original's surface-face flag (bit 0x8000 of the face's first u16) and surface object lookup are not decoded"),
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
		("TRACK-012", "TRACK-HEADING: a launched boat starts heading along the station's flow; the original leaves a new water boat at heading 0"),
		("TRACK-013", "TRACK-BREAK: BUMP 8 sets the controller state to broken (2) or fixed (0) and BUMP 9 to worn (1) or fixed, as the traced routines do; what the worn state changes for boats is not traced"),
		("TRACK-014", "TRACK-WALK: the circuit walk's junction rule beyond 'the cell ahead has type 0', its cut-off and arrival from a side arm are not traced"),
		("TRACK-015", "TRACK-KART: kart rides share the piece tables and templates but have no controller: the grid start, race laps, followers, kart sounds and the bumper-car modes are not implemented"),
		("TRACK-016", "TRACK-COSMETIC: the spray, smoke, horn and crash cues and the boat model variants and the unload's BeamUpCar particle and EventMap cue make no particle, sound or mesh; a cosmetic generator and counter keep their cadence"),
		("TRACK-017", "TRACK-XKEEP: the four keep-out circles of a crossing are clipped to the tile's quadrants like a corner's; the trace gives their centres and radius only"),
		("TRACK-018", "TRACK-ANIM: the pieces are static; the water texture animation (BUMP 17) is stored but not played and the tee pieces show all four arms"),
		("TRACK-019", "TRACK-SCOPE: a controller's collision objects, buoys and boats are its own, where the original's pools are module-wide"),
		("TRACK-020", "TRACK-POOL: a circuit that overflows the section, collision-object or buoy pool is invalid and builds no geometry; the original's overflow path is not traced"),
		("TRACK-021", "TRACK-OWNER: every track tile records the ride it was laid for (the station's first-tile cell index plus one); tiles of different rides never connect and a demolition removes the ride's own tiles; the original's cell record has no owner field and its walk and demolition follow the masks")
	};
}
