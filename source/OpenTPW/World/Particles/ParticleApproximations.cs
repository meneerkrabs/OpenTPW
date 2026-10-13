namespace OpenTPW;

public static class ParticleApproximations
{
	public static readonly (string Id, string Description, string EvidenceNeeded)[] All =
	{
		("PTCL-900", "Current animator hierarchy matrices supply particle key frames; native lookup-table refresh gates and instance callback timing are unqualified", "Object-header bit 1 setters and the runtime key-table walk in placed objects"),
	};
}
