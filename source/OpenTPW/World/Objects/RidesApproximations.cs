namespace OpenTPW;

/// <summary>
/// The rides slice's approximation register (docs/OBJECTS.md, "Approximation register"). Each entry is
/// tagged <c>[APPROX:RIDES-NNN]</c> at its code site; <see cref="LogOnce"/> lists them at the first catalog load.
/// </summary>
public static class RidesApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("RIDES-001", "animation clips play at 30 ticks/s"),
		("RIDES-002", "most recently started channel wins a node"),
		("RIDES-003", "finished clips hold their last pose"),
		("RIDES-004", "re-issued LOOPANIM continues the running loop"),
		("RIDES-005", "plain animation opcodes use a channel separate from _CH channels"),
		("RIDES-006", "GETANIM_CH returns 1 while playing"),
		("RIDES-007", "TRIGANIMSPEED ignores its speed operand"),
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
		("RIDES-020", "sandbox Totem cell blocking by model box"),
		("RIDES-021", "build centred on clicked cell, Z = 0 cursor plane"),
		("RIDES-022", "texture search order archive/gtexture/sharetex"),
		("RIDES-023", "completed cycle = VAR_RUNNING 1 -> 0"),
		("RIDES-024", "bonus archives merge, Info.Id collisions skipped"),
		("RIDES-025", "bonus name language fallback"),
		("RIDES-026", "engine scale 0.2 per MD2 unit (presentation)"),
		("RIDES-027", "sandbox Totem 5-unit bounds radius"),
		("RIDES-028", "non-walkable object access cells use the nearest walkable path"),
		("RIDES-029", "developer Totem registers uncharged when the purchase is refused")
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
