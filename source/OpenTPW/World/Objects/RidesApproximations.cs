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
		("RIDES-009", "position of shared (non-Info.Id) .sam files in the object layer order"),
		("RIDES-010", "OBJECT_NAMES bound by English name equality"),
		("RIDES-011", "shape symbols N/E/</>/+/W inferred"),
		("RIDES-012", "access cells open across first/last row before columns"),
		("RIDES-013", "180 degree and non-square rotations derived"),
		("RIDES-014", "base height = mean footprint ground height"),
		("RIDES-015", "imported and built objects start open"),
		("RIDES-017", "buildable = WhichUIType 0-3, not fixed/tool/upgrade"),
		("RIDES-018", "terrain/overlap rules without slope/path/land; Level enforces economy purchases"),
		("RIDES-019", "levels without a save get Gates, Lights and Bus"),
		("RIDES-021", "build centred on clicked cell, Z = 0 cursor plane"),
		("RIDES-022", "texture search archive/gtexture/sharetex follows the PC lookup 0x46ead0; the stexture/ssharete low-detail choice (globals 0x78d968, 0x785874, 0x79d86c) is untraced"),
		("RIDES-023", "completed cycle = VAR_RUNNING 1 -> 0"),
		("RIDES-024", "bonus archives merge, Info.Id collisions skipped"),
		("RIDES-025", "bonus name language fallback"),
		("RIDES-026", "engine scale 0.2 per MD2 unit (presentation)"),
		("RIDES-028", "non-walkable object access cells use the nearest walkable path"),
		("RIDES-030", "non-finite object texture coordinates drawn as (0, 0)"),
		("RIDES-031", "Theme Park Inc shape symbol @ (road sign span) treated as occupied"),
		("RIDES-032", "every placed instance takes the callback update route; the PC list pass 0x44e410 (updates without posing via 0x473c70(inst, 0, 8)) is not modelled, as the renderer's call site of the callback at model record +0x140 is not found"),
		("RIDES-033", "script speed = 0.5 + bias / 100 with the bias from ride init InitSpeed and parent copies; the PC writers at ride-settings apply (0x4af440 → 0x4dd6e0) and live upgrade completion (0x4df8f0) are not modelled"),
		("RIDES-035", "MD2 texture-frame tracks apply to rendering; the original's draw-path reader of a slot's current frame is not traced, so the binding follows this port's material-slot mapping (see MD2-900..902)"),
		("RIDES-036", "UV pass always runs: PC option bit 0x10 is always clear (single write 0x4740d0 with 0); node bit 0x4000 is taken as clear (writers untraced)"),
		("RIDES-038", "TRIGANIMSPEED speed of 0 or below faults; PC 0 is a divide fault too, a negative speed decreases the frame and never ends the clip (0x4735d0), pose sampling at negative frames untraced"),
		("RIDES-040", "Theme Park Inc fixed scene records saved at (0, 0) use their local model origin; other transform fields are not decoded"),
		("RIDES-041", "12-byte vertex block (record flag 0x4000) shown as the stored mesh; no reader of its floats traced"),
		("RIDES-042", "node-state bit 0x10 (clip node lists, toggles) hides the node's mesh"),
		("RIDES-043", "relative-animation models play vertex and UV tracks in set mode as the PC object loader does; Add mode (inner argument 0x20000, TrackMaster coaster loader 0x42fad0 only) is not modelled"),
		("RIDES-044", "scheduler clock (GETTIME, WAITANIM(_CH) deadlines) starts at 0 with the park's script world; a new park's PC timer start value (0x785970) needs a capture and GETTIME's PC handler is not located"),
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
