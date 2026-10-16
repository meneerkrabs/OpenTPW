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
		("RIDES-011", "uses of shape cell type 11 ('+', track-upgrade cells) and of the track ends '<' '>' (coaster station ends) are inferred; the symbol types and direction bits are the executable's table"),
		("RIDES-012", "access cells open towards the compass bit of their symbol (exit: its bit, entrance: the opposite); the mapping of the bits to the local axes is medium confidence (no reader of def+0x49c/+0x4a8 traced), every corpus shape agrees"),
		("RIDES-013", "180 degree and non-square rotations derived"),
		("RIDES-014", "base height = mean footprint ground height"),
		("RIDES-015", "imported and built objects start open"),
		("RIDES-018", "terrain/overlap/owned-land rules without slope/path; Level enforces economy purchases"),
		("RIDES-019", "End is not created: the original creates it through 0x516b00 from the message handler 0x503d40 (message type 0x11, from 0x50b550), not at setup; a Theme Park Inc world without a prebuilt save keeps a Bus from setup where the TPW arrival vehicles are placed on first dispatch"),
		("RIDES-021", "build centred on clicked cell, Z = 0 cursor plane"),
		("RIDES-023", "completed cycle = VAR_RUNNING 1 -> 0"),
		("RIDES-025", "bonus name from the selected language file, then English, then the .sam name; the original reads only the selected language file (0x41f350) and the OBJECT_NAMES id table (OpenTPW extension for the fallbacks)"),
		("RIDES-028", "non-walkable object access cells use the nearest walkable path"),
		("RIDES-031", "shape cell types of '@' (1), 'Q' (3) and '#' (16) differ from the occupied type 4, but their placement meaning is untraced; they count as occupied here (Theme Park Inc may differ)"),
		("RIDES-032", "every placed instance takes the callback update route; the PC list pass 0x44e410 (updates without posing via 0x473c70(inst, 0, 8)) is not modelled, as the renderer's call site of the callback at model record +0x140 is not found"),
		("RIDES-033", "ride-settings speed changes (UI command 0x4af440 -> 0x4dd6e0) are not modelled: OpenTPW has no ride speed control"),
		("RIDES-035", "MD2 texture-frame tracks apply to rendering; the PC writer is 0x4721f0 -> 0x470c90 (slot record at model+0x50+slot*8), the draw-path reader of a slot's current frame is not traced, so the binding follows this port's material-slot mapping (see MD2-900..902)"),
		("RIDES-040", "Theme Park Inc fixed scene records saved at (0, 0) use their local model origin; other transform fields are not decoded"),
		("RIDES-042", "node-state bit 0x10 (clip node lists, toggles) hides the node's mesh"),
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
