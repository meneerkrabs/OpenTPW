namespace OpenTPW;

public partial class Level
{
	/// <summary>
	/// Defers weather that was registered before the last particle renderer until just after it.
	/// Every non-weather entity keeps its relative position in the draw sequence; the global entity
	/// list and its update order are untouched. The caller supplies its usual per-pass snapshot.
	/// </summary>
	// [BIN:TPW-EXE:0x576a00 scene pass] the particle pass (0x57C620) precedes weather (0x57FF60); see ParticleRenderer's PTCL-909 annotation.
	// [BIN:TPW-EXE:0x582170 advisor model] the advisor's model (flag 0x200) is queued by the scene's model list loop (0x584B10, 0x584BE0) after the particle pass and drawn by the frame flush 0x582170 after the weather, so it lies over the particles and the weather in the original too
	// [BIN:TPW-EXE:0x576DBE HUD order] the HUD windows reach the screen through the scene's sprite flush (0x58A660, after the particle pass and before the weather) and its model flush (0x582170, with the advisor's model), so the particles lie under all of them and the weather under the models (PTCL-909 retired; ParticleRenderer's annotation).
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
	/// Splits a draw sequence into the entities drawn before the translucent pass (<see cref="WorldPass"/>) and the ones drawn after it: the weather renderer, the pools' water and the spray of their jets and the swimmers. Each group keeps the
	/// order of the snapshot (the caller's lists are cleared and refilled, so a frame allocates nothing here). The particle renderer is in the first group: its opaque quads are drawn there and its blended quads join the pass's list, far to near with
	/// the translucent world geometry (<see cref="Model.Companions"/>, <see cref="WorldPass.SubmitParticle"/>), as the original's deferred list holds both. Its screen-space quads are not in that list: they are drawn last (<see cref="ParticleRenderer.RenderScreen"/>), over the late group too.
	/// </summary>
	// [BIN:TPW-EXE:0x582170 frame flush] the scene pass 0x576A00 ends with 0x582170: the batcher 0x581DB0 draws every model face whose state has bit 0x1000 or 0x2000 (opaque 0x1256, alpha-test-0xF0 0x2756) at once and
	// defers the rest (0x556: alpha test 0x10), then 0x582AD0 draws the deferred records far to near by mean vertex depth (0x5829E0); see WorldTranslucency.
	// The original defers the particle batches whose state has neither bit (0x556, 0x200552, 0x200522) into that same far-to-near list (0x565590): the particle renderer hands its blended quads to the pass (ParticleRenderer, PTCL-905). The weather's streaks and flakes (0x200D52, 0x556; the streaks' and bolts' key is 0) are still drawn after the whole list, as are the pools' water and spray and the swimmers.
	internal static bool SplitLate( Entity[] snapshot, List<Entity> before, List<Entity> after )
	{
		before.Clear();
		after.Clear();
		foreach ( var entity in snapshot )
			( IsLate( entity ) ? after : before ).Add( entity );
		return after.Count > 0;
	}

	/// <summary>The effects drawn after the translucent pass (see <see cref="SplitLate"/>). The swimmers' sprites come after the pool water, which writes no depth, so the water's blend does not tint them (docs/POOLS.md, Rendering).</summary>
	private static bool IsLate( Entity entity ) => entity is WeatherRenderer or global::OpenTPW.PoolSprayRenderer or PoolPartEntity { IsWater: true } or GuestRenderer { DrawsAfterWater: true };

	// a scene built without its constructor (the park-build tests make one) has no lists yet
	private List<Entity>? lateBefore, lateAfter;
}
