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
		("RIDES-011", "'+' (11) is an overlay cell over the parent ride's track and the track ends '<' '>' are station connections here; the original placement 0x528a70 stamps '+', '#' (16) and the ends (23) as occupied type 4 (0x5290c7..0x5290ee), checks '+' with cell op 0x10b (0x20b when validating) and '#' with 0x110/0x210 and on commit overwrites the flags word +0xe of the cell and of its second-layer cell with 0x10 (0x52950d..0x52951c); an end with a direction ('>' 4, '<' 0x40) needs the cell beyond it in its rotated direction (refused when missing, 0x5295a8) and on commit turns that cell into type 0x15 (ops 0x87, 0x15, 0x83, 0x84, 0x81 at 0x5295f7..0x52963f); what ops 0x10b/0x110 and type 0x15 require is not decoded"),
		("RIDES-014", "base height = (lowest + highest terrain vertex) / 2 under the footprint's shape cells as the original's 0x466D70; the ground under the ride is not blended flat as the original does (0x467030), and the instance words +0xF0/+0xF4 are taken as the shape size"),
		("RIDES-018", "terrain, overlap (objects, paths, queues) and owned-land rules without a slope rule; the original cell validator 0x535670 accepts an object cell over a path cell where the port refuses it (the placement's own pre-checks are unread); Level enforces economy purchases"),
		("RIDES-019", "End is not created: the original creates it through 0x516b00 from the lab's message handler 0x503d40 (message 0x11) when the last research item completes, by the GameType word DAT_00fb3b7c (2: Instant Action at once; 0: Full Simulation once every online park record is done, 0x5b0c40; 1: never), after posting the advisor message 0xa7; Theme Park Inc worlds without a prebuilt save keep a Bus from setup"),
		("RIDES-021", "the original's mouse-to-cell pick is not found: the build tool 0x524960 takes its cell from the replayed command 0x4045B0 (commands 0x14 and 0x2D), whose producer is not found; the port picks on the Z = 0 plane ([EXT:build-cursor-ground-plane])"),
		("RIDES-023", "completed cycle = VAR_RUNNING 1 -> 0"),
		("RIDES-028", "non-walkable object access cells use the nearest walkable path; the original placement 0x528a70 lays them itself: on commit with 0x52fab0 true (option 0x81b0cc, default 1 at 0x52f139, and 0x8187f8 clear) the entrance's outside cell becomes a linked queue cell (0x5297bd..0x529890) and the exit's a path cell linked to its path neighbours (0x529a58..0x529b1a), with it false the entrance's outside cell becomes a path cell and the exit gets none; an outside cell off the map refuses the placement (0x52974e, 0x5299ec) and validation runs ops 0x204/0x201 on it; OpenTPW lays neither and the link ops 0x81/0x85/0x86 are not decoded"),
		("RIDES-031", "'@' (1), 'Q' (3) and '#' (16) count as occupied cells here; the original placement 0x528a70 stamps '@' as a path cell inside the footprint (type 1; on commit flags word 0x20, links by 0x5348d0 and to its path neighbours, 0x529173..0x529208), 'Q' as a queue cell (type 3, the generic stamp at 0x529119) and '#' as RIDES-011 describes"),
		("RIDES-032", "every placed instance takes the callback update route; the PC list pass 0x44e410 (updates without posing via 0x473c70(inst, 0, 8), posing only with instance flag 0x400000) is not modelled, so an instance the renderer does not pose is posed here; the callback at model record +0x140 has no call site in the executable (no call or jump through that offset), so which path invokes it is not found"),
		("RIDES-040", "Theme Park Inc fixed scene records saved at (0, 0) use their local model origin; the thing records of the six that have an archive carry the fixed-item default position (128, 128) and angle 0, so the save leaves no transform field unread; how the original places a local-coordinate fixed model at its anchor is not read"),
		("MD2-900", "texture-frame slots mix like node components: ascending channels and tracks (hyenasm1 holds two tracks on slot 15; its constant 0 loses to the face cycle), the last non-null frame wins, null before the first key keeps the previous frame (initially 0), and a slot no bound clip addresses returns to frame 0; the fixed-item pose hold is not extended to frames"),
		("MD2-901", "texture-frame keys naming a frame outside the slot's frame-name list, or a slot the model does not have, are ignored (the one corpus case, Pmegacostm slot 12, addresses a slot its base model lacks)"),
		("MD2-902", "a texture frame whose art no searched directory has (including Theme Park Inc tvsim slot 39 frame 3 lt1.tga) retains the effective selected frame; decoded-art availability is registered with the animator so the visible frame survives save/restore")
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
