namespace OpenTPW;

public partial class Level
{
	/// <summary>
	/// Defers weather that was registered before the last particle renderer until just after it.
	/// Every non-weather entity keeps its relative position in the draw sequence; the global entity
	/// list and its update order are untouched. The caller supplies its usual per-pass snapshot.
	/// </summary>
	// [BIN:TPW-EXE:0x576a00 scene pass] the particle pass (0x57C620) precedes weather (0x57FF60); see ParticleRenderer's PTCL-909 annotation.
	// [APPROX:PTCL-909] this establishes only park particles-before-weather, not the unresolved native HUD/advisor layering.
	internal static Entity[] WeatherAfterParticles( Entity[] snapshot )
	{
		var lastParticle = Array.FindLastIndex( snapshot, entity => entity is ParticleRenderer );
		if ( lastParticle <= 0 )
			return snapshot;
		var deferred = new List<Entity>();
		for ( var index = 0; index < lastParticle; index++ )
			if ( snapshot[index] is WeatherRenderer )
				deferred.Add( snapshot[index] );
		if ( deferred.Count == 0 )
			return snapshot;

		var ordered = new Entity[snapshot.Length];
		var next = 0;
		for ( var index = 0; index <= lastParticle; index++ )
			if ( snapshot[index] is not WeatherRenderer )
				ordered[next++] = snapshot[index];
		foreach ( var weather in deferred )
			ordered[next++] = weather;
		Array.Copy( snapshot, lastParticle + 1, ordered, next, snapshot.Length - lastParticle - 1 );
		return ordered;
	}
}
