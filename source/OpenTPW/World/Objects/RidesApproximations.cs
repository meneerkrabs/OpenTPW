namespace OpenTPW;

/// <summary>
/// The rides slice's approximation register (docs/OBJECTS.md, "Approximation register"). Each entry is
/// tagged <c>[APPROX:RIDES-NNN]</c> at its code site; <see cref="LogOnce"/> lists them at the first catalog load.
/// </summary>
public static class RidesApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("RIDES-001", "Mac channel rate 30 is proved; Windows rate and native scaled/unscaled clock selection remain unverified"),
		("RIDES-003", "finished clips hold their last pose (AnimID 14 path traced; object flags 0x8/0x10 and the AnimID-14 copy-back slot untraced)"),
		("RIDES-008", "ANIM_* to member letter/variant mapping derived from names and scripts"),
		("RIDES-009", "position of shared (non-Info.Id) .sam files in the object layer order"),
		("RIDES-010", "OBJECT_NAMES bound by English name equality"),
		("RIDES-011", "shape symbols N/E/</>/+/W inferred"),
		("RIDES-012", "access cells open across first/last row before columns"),
		("RIDES-013", "180 degree and non-square rotations derived"),
		("RIDES-014", "base height = mean footprint ground height"),
		("RIDES-015", "imported and built objects start open"),
		("RIDES-016", "VAR_DURATION = raw Upgrades[0].InitDuration"),
		("RIDES-017", "buildable = WhichUIType 0-3, not fixed/tool/upgrade"),
		("RIDES-018", "terrain/overlap rules without slope/path/land; Level enforces economy purchases"),
		("RIDES-019", "levels without a save get Gates, Lights and Bus"),
		("RIDES-021", "build centred on clicked cell, Z = 0 cursor plane"),
		("RIDES-022", "texture search order archive/gtexture/sharetex"),
		("RIDES-023", "completed cycle = VAR_RUNNING 1 -> 0"),
		("RIDES-024", "bonus archives merge, Info.Id collisions skipped"),
		("RIDES-025", "bonus name language fallback"),
		("RIDES-026", "engine scale 0.2 per MD2 unit (presentation)"),
		("RIDES-028", "non-walkable object access cells use the nearest walkable path"),
		("RIDES-030", "non-finite object texture coordinates drawn as (0, 0)"),
		("RIDES-031", "Theme Park Inc shape symbol @ (road sign span) treated as occupied"),
		("RIDES-032", "every placed instance takes the callback update route; the object-list route (no posing, no-reset replay) is not modelled"),
		("RIDES-033", "script speed fixed at 1.0 (initial bias 50); native writers of the bias not modelled"),
		("RIDES-034", "no idle restart of AnimID 5 on objects with +4 & 0x8004 clear"),
		("RIDES-035", "MD2 texture-frame tracks not applied"),
		("RIDES-036", "UV pass always runs: global option bit 0x10 and node bit 0x4000 taken as clear (setters untraced)"),
		("RIDES-037", "TRIGWAITANIM without an object treats a literal out operand like a variable (never resumes)"),
		("RIDES-038", "TRIGANIMSPEED speed below 0 faults; negative channel playback untraced"),
		("RIDES-040", "Theme Park Inc fixed scene records saved at (0, 0) use their local model origin; other transform fields are not decoded"),
		("RIDES-041", "12-byte vertex block (record flag 0x4000) shown as the stored mesh; no reader of its floats traced"),
		("RIDES-042", "node-state bit 0x10 (clip node lists, toggles) hides the node's mesh"),
		("RIDES-043", "relative-animation models play vertex and UV tracks in set mode; add mode behind ride-loader argument 0x20000 not modelled"),
		("RIDES-044", "scheduler clock (GETTIME, WAITANIM(_CH) deadlines) starts at 0 with the park's script world; original start value untraced")
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
