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

	/// <summary>
	/// The original's translucent pass for world geometry: entities that are translucent <see cref="ModelEntity"/> parts (<see cref="ModelEntity.Translucent"/>) leave their place in the draw sequence and are drawn
	/// after every other entity, the farthest first by the mean depth of their vertices (<see cref="ModelEntity.TranslucentDepth"/>, ties in entity order). Particle and weather renderers, which draw without
	/// writing depth, then follow in their own order, so a particle in front of water or glass is still drawn over it and the translucent geometry no longer overpaints it. Every other entity keeps its relative
	/// position. With no translucent entity the snapshot is returned as it is. The caller supplies its usual per-pass snapshot (<see cref="WeatherAfterParticles"/> applied first) and the depth of a part
	/// (<paramref name="depthOf"/>: larger is farther).
	/// </summary>
	// [BIN:TPW-EXE:0x582170 frame flush] the scene pass 0x576A00 ends with 0x582170: the batcher 0x581DB0 draws every model face whose state has bit 0x1000 or 0x2000 (opaque 0x1256, alpha-test-0xF0 0x2756) at once and defers the rest (0x556: alpha test 0x10),
	// then 0x582AD0 draws the deferred records far to near by mean vertex depth (0x5829E0); see WorldTranslucency.
	// PTCL-905 (the particle state words) is untouched: the original also defers the particle batches whose state has neither bit (0x556, 0x200552) into the same far-to-near list; here the particles stay a pass of their own after the translucent world
	internal static Entity[] WorldPassOrder( Entity[] snapshot, Func<ModelEntity, float> depthOf )
	{
		var translucent = new List<(ModelEntity Entity, float Depth, int Order)>();
		for ( var index = 0; index < snapshot.Length; index++ )
			if ( snapshot[index] is ModelEntity { Translucent: true } part )
				translucent.Add( (part, depthOf( part ), index) );
		if ( translucent.Count == 0 )
			return snapshot;
		translucent.Sort( ( left, right ) =>
		{
			var byDepth = right.Depth.CompareTo( left.Depth );
			return byDepth != 0 ? byDepth : left.Order.CompareTo( right.Order );
		} );
		var ordered = new Entity[snapshot.Length];
		var next = 0;
		foreach ( var entity in snapshot )
			if ( entity is not ModelEntity { Translucent: true } && entity is not (ParticleRenderer or WeatherRenderer) )
				ordered[next++] = entity;
		foreach ( var (entity, _, _) in translucent )
			ordered[next++] = entity;
		foreach ( var entity in snapshot )
			if ( entity is ParticleRenderer or WeatherRenderer )
				ordered[next++] = entity;
		return ordered;
	}
}
