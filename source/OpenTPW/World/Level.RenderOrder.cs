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
	/// Splits a draw sequence into the entities drawn before the translucent pass (<see cref="WorldPass"/>) and the ones drawn after it: the blended effects that write no depth, the particle and weather renderers, the
	/// pools' water and the spray of their jets. Each group keeps the order of the snapshot (the caller's lists are cleared and refilled, so a frame allocates nothing here). The translucent world geometry (<see cref="Model.Companions"/>) is far to near before these, so a particle, a swimmer's water
	/// or a jet's spray in front of water or glass is still drawn over it, and the geometry no longer overpaints them. 
	/// </summary>
	// [BIN:TPW-EXE:0x582170 frame flush] the scene pass 0x576A00 ends with 0x582170: the batcher 0x581DB0 draws every model face whose state has bit 0x1000 or 0x2000 (opaque 0x1256, alpha-test-0xF0 0x2756) at once and
	// defers the rest (0x556: alpha test 0x10), then 0x582AD0 draws the deferred records far to near by mean vertex depth (0x5829E0); see WorldTranslucency.
	// The original also defers the particle and weather batches whose state has neither bit (0x556, 0x200552) into that same far-to-near list; here they are drawn after the whole list, which hides a particle that lies behind water or glass (see PTCL-905).
	internal static bool SplitLate( Entity[] snapshot, List<Entity> before, List<Entity> after )
	{
		before.Clear();
		after.Clear();
		foreach ( var entity in snapshot )
			( IsLate( entity ) ? after : before ).Add( entity );
		return after.Count > 0;
	}

	/// <summary>The effects drawn after the translucent pass (see <see cref="SplitLate"/>). The swimmers' sprites come after the pool water, which writes no depth, so the water's blend does not tint them (docs/POOLS.md, Rendering).</summary>
	private static bool IsLate( Entity entity ) => entity is ParticleRenderer or WeatherRenderer or global::OpenTPW.PoolSprayRenderer or PoolPartEntity { IsWater: true } or GuestRenderer { DrawsAfterWater: true };

	// a scene built without its constructor (the park-build tests make one) has no lists yet
	private List<Entity>? lateBefore, lateAfter;
}
