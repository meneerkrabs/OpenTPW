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
		("RIDES-003", "finished clips hold their last pose (AnimID 14 path traced; object flags 0x8/0x10 and the AnimID-14 copy-back slot untraced)"),
		("RIDES-011", "'+' (11) is an overlay cell over the parent ride's track and the track ends '<' '>' are station connections here; the original placement 0x528a70 stamps '+', '#' (16) and the ends (23) as occupied type 4 (0x5290c7..0x5290ee), checks '+' with cell op 0x10b (0x20b when validating) and '#' with 0x110/0x210 and on commit overwrites the flags word +0xe of the cell and of its second-layer cell with 0x10 (0x52950d..0x52951c); an end with a direction ('>' 4, '<' 0x40) needs the cell beyond it in its rotated direction (refused when missing, 0x5295a8) and on commit turns that cell into type 0x15 (ops 0x87, 0x15, 0x83, 0x84, 0x81 at 0x5295f7..0x52963f); what ops 0x10b/0x110 and type 0x15 require is not decoded"),
		("RIDES-014", "base height = mean footprint ground height"),
		("RIDES-015", "imported and built objects start open; the original's constructor 0x4db090 opens a built object (open word +0x68 = 1, 0x4db3b9) but closes it again when its type has Info.HasQueue (def+0x40 sets flag 8 at 0x4db420; 'Object %d Closing', open word 0 and VAR_RIDECLOSED = 1 at 0x4db712..0x4db779), so built rides with a queue start closed; the saved ride state of imported objects is not decoded"),
		("RIDES-018", "terrain, NoModify paths, overwrite-priority overlap (objects, paths and queues), owned-land and access-cell rules of tp.exe's validator 0x535670; the second-layer link tests 0x53ae00/0x53ae90, the pending cost of the access cells the commit lays and the types whose record +0x288 is 1 (their Bumper.WhichTrackType turns the access-cell option 0x81b0cc off, 0x46d605) are not modelled; guests on a path cell a placement clears are set down on the nearest path cell, where tp.exe's handling of a guest inside a new footprint is not traced; Level enforces economy purchases"),
		("RIDES-019", "End is not created: the original creates it through 0x516b00 from the message handler 0x503d40 (message type 0x11, from 0x50b550), not at setup; a Theme Park Inc world without a prebuilt save keeps a Bus from setup where the TPW arrival vehicles are placed on first dispatch"),
		("RIDES-021", "the build cursor's ray picks the clicked cell on the Z = 0 plane; the original's mouse-to-cell pick (the cell the build tool 0x524960 hands to 0x528a70) is not traced; the anchor taken from the clicked cell is the original's (0x528b44), so the click is local (W/2, H/2) when each side is odd or at most 5 cells (the only shipped exception is space 3104 Orbiter, 6 x 3: local (3, 1) at 0 degrees, (2, 2) at 90)"),
		("RIDES-023", "completed cycle = VAR_RUNNING 1 -> 0"),
		("RIDES-025", "bonus name from the selected language file, then English, then the .sam name; the original reads only the selected language file (0x41f350) and the OBJECT_NAMES id table (OpenTPW extension for the fallbacks)"),
		("RIDES-028", "non-walkable object access cells use the nearest walkable path; the original placement 0x528a70 lays them itself: on commit with 0x52fab0 true (option 0x81b0cc, the type's Info.HasQueue from the build tool's enter 0x46d644, and 0x8187f8 clear) the entrance's outside cell becomes a linked queue cell (0x5297bd..0x529890) and the exit's a path cell linked to its path neighbours (0x529a58..0x529b1a), with it false the entrance's outside cell becomes a path cell and the exit gets none; OpenTPW checks these cells as the original does (ParkObjects, RIDES-018) but lays neither, keeping a path already there, and the link ops 0x81/0x85/0x86 are not decoded"),
		("RIDES-031", "'@' (1), 'Q' (3) and '#' (16) count as occupied cells here; the original placement 0x528a70 stamps '@' as a path cell inside the footprint (type 1; on commit flags word 0x20, links by 0x5348d0 and to its path neighbours, 0x529173..0x529208), 'Q' as a queue cell (type 3, the generic stamp at 0x529119) and '#' as RIDES-011 describes"),
		("RIDES-032", "every placed instance takes the callback update route; the PC list pass 0x44e410 (updates without posing via 0x473c70(inst, 0, 8)) is not modelled, as the renderer's call site of the callback at model record +0x140 is not found"),
		("RIDES-035", "MD2 texture-frame tracks apply to rendering; the PC writer is 0x4721f0 -> 0x470c90 (slot record at model+0x50+slot*8), the draw-path reader of a slot's current frame is not traced, so the binding follows this port's material-slot mapping (see MD2-900..902)"),
		("RIDES-040", "Theme Park Inc fixed scene records saved at (0, 0) use their local model origin; other transform fields are not decoded"),
		("RIDES-044", "scheduler clock (GETTIME, WAITANIM(_CH) deadlines) starts at 0 per park script world; the PC clock is the app-lifetime scaled timer 0x785970 never reset for a new park (differs only in absolute GETTIME values)"),
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
