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
		("RIDES-011", "the cells of '+' (11) and '#' (16) are stamped as tp.exe's placement 0x528a70 stamps them (object type 4, the owner word, flag word 0x10 on the cell and its track-layer cell) but are not validated as the original does (cell ops 0x10b/0x110, 0x20b/0x210 -> the validator 0x535670 in mode 0xb/0x10, which in add-on mode 0x8187f8 accepts them only on a straight, unflagged, non-queue track tile of the selected ride, 0x535953..0x5359b3); the track ends '<' '>' 'D' (23) are plain object cells here, where the original needs the cell beyond an end (refused when off the map, 0x5295a8) and writes type 0x15 on it with the ride's owner word (ops 0x87, 0x15, 0x83, 0x84, 0x81 at 0x5295f7..0x52963f), whose consumer (the validator's modes 0x15 and 0x16, 0x535b53) no OpenTPW tool uses"),
		("RIDES-014", "base height = mean footprint ground height"),
		("RIDES-015", "imported and built objects start open; the original's constructor 0x4db090 opens a built object (open word +0x68 = 1, 0x4db3b9) but closes it again when its type has Info.HasQueue (def+0x40 sets flag 8 at 0x4db420; 'Object %d Closing', open word 0 and VAR_RIDECLOSED = 1 at 0x4db712..0x4db779), so built rides with a queue start closed; the saved ride state of imported objects is not decoded"),
		("RIDES-018", "terrain, overwrite-priority overlap (objects stand on path cells without NoModify, the entrance's and exit's outside cells are validated by their own rules) and owned-land rules; paths and queues never replace scenery, where tp.exe lets them replace objects of a lower overwrite priority (0x535d66 with 0x818c10 = 2 and 3); Level enforces economy purchases"),
		("RIDES-019", "End is not created: the original creates it through 0x516b00 from the message handler 0x503d40 (message type 0x11, from 0x50b550), not at setup; a Theme Park Inc world without a prebuilt save keeps a Bus from setup where the TPW arrival vehicles are placed on first dispatch"),
		("RIDES-021", "the build cursor's ray picks the clicked cell on the Z = 0 plane; the original's mouse-to-cell pick (the cell the build tool 0x524960 hands to 0x528a70) is not traced; the anchor taken from the clicked cell is the original's (0x528b44), so the click is local (W/2, H/2) when each side is odd or at most 5 cells (the only shipped exception is space 3104 Orbiter, 6 x 3: local (3, 1) at 0 degrees, (2, 2) at 90)"),
		("RIDES-023", "completed cycle = VAR_RUNNING 1 -> 0"),
		("RIDES-025", "bonus name from the selected language file, then English, then the .sam name; the original reads only the selected language file (0x41f350) and the OBJECT_NAMES id table (OpenTPW extension for the fallbacks)"),
		("RIDES-031", "a Theme Park Inc world keeps the cells its layout was given and types none of its footprints, so the road signs' '@' cells (\"*@*\", \"*@@*\") are plain occupied cells; tp.exe's commit stamps '@' as a type-1 NoModify path cell linked to its path neighbours (0x529173..0x529208) and 'Q' as a queue cell (0x529119), which ParkCellMap.WriteFootprint does for Theme Park World, whose shapes have neither; Theme Park Inc's own placement is not traced"),
		("RIDES-032", "every placed instance takes the callback update route; the PC list pass 0x44e410 (updates without posing via 0x473c70(inst, 0, 8)) is not modelled, as the renderer's call site of the callback at model record +0x140 is not found"),
		("RIDES-035", "MD2 texture-frame tracks apply to rendering; the PC writer is 0x4721f0 -> 0x470c90 (slot record at model+0x50+slot*8), the draw-path reader of a slot's current frame is not traced, so the binding follows this port's material-slot mapping (see MD2-900..902)"),
		("RIDES-040", "Theme Park Inc fixed scene records saved at (0, 0) use their local model origin; other transform fields are not decoded"),
		("RIDES-044", "scheduler clock (GETTIME, WAITANIM(_CH) deadlines) starts at 0 per park script world; the PC clock is the app-lifetime scaled timer 0x785970 never reset for a new park (differs only in absolute GETTIME values)"),
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
