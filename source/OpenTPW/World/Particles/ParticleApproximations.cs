namespace OpenTPW;

public static class ParticleApproximations
{
	public static readonly (string Id, string Description, string EvidenceNeeded)[] All =
	{
		("PTCL-900", "Current animator hierarchy matrices supply particle key frames; native lookup-table refresh gates and instance callback timing are unqualified", "Object-header bit 1 setters and the runtime key-table walk in placed objects"),
		("PTCL-901", "Latest owner-local managed EVENT1/2 preparation is polling diagnostics only; attempts replace older data and Stop clears it, with no particle pool, delivery, rendering, physics or persistence", "Native constructors, template contents, scoped lookup, lifetime/rendering and update timing"),
	};
}
