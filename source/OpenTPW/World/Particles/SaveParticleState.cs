namespace OpenTPW;

/// <summary>
/// Maps a decoded Particles block of an original park save (<see cref="SaveParticlesBlock"/>, tp.exe <c>0x51F7A0</c>) onto the runtime pool's save shape
/// (<see cref="ParticleSystemState"/>, <see cref="ParticleSystem.Restore"/>). Both describe the same native structure and the fields <see cref="ParticleSystemState"/> has are copied directly. The conversion is partial, and what it leaves out is listed here and asserted by the corpus test:
/// <list type="bullet">
/// <item>Not carried (the runtime state has no field): the emitter's hold word, group, screen-space flag, ring mode, immortal word, word +0x2A, live particle count, first particle and bounds, the effector's group, mode and strength, and the saved template tables. <see cref="ParticleSystem.Restore"/> refills hold, group, screen-space and killed-on-load from the runtime's own templates, so a save made with modified templates
/// (Jungle's scratch templates 101 to 104) restores with the file's; the corpus emitters use effects 0 to 100 only and their saved fields equal the file's.</item>
/// <item>Inferred: an effector slot stores no template id, so it is matched by template bytes (group, mode, jitter, strength) that the tick does not write directly (checked for direct-displacement writes only); no match or several is refused.</item>
/// <item>Conditional: <c>VolumeExtent</c> is null when the slot equals the runtime template's extent. <c>TickCounter</c> is capped at <see cref="int.MaxValue"/>.</item>
/// </list>
/// The conversion is pure; a level that starts from an original save restores the result into its particle system (Level.ImportOriginalParticles, docs/TPWS-PAYLOAD.md and SAVE-013).
/// </summary>
public static class SaveParticleState
{
	// [APPROX:SAVE-013] the sprite scripts of the Scripts block (the saved guests' and litter's animations) are decoded and not applied, and the effector slot's template id is recovered from template bytes — evidence needed: whether the saved animation phase of a guest or litter sprite shows anything OpenTPW's state-derived frames lack, and a save with a live effector to confirm the template match

	// [BIN:TPW-EXE:0x52244B effector slot] the slot is a copy of the effector template without an id word, so the id is found by the template bytes the tick does not write by direct displacement (writes through a base register were not checked) (group, mode, jitter, strength: +4, +0x30 to +0x43)
	private const int EffectorMatchFrom = 0x30, EffectorMatchTo = 0x44;

	/// <summary>
	/// The runtime state for <paramref name="block"/>, or null when the game's particle system was not initialised. <paramref name="templates"/> are the runtime's emitter templates
	/// (used to tell a changed spawn volume from the template's own). Throws <see cref="InvalidDataException"/> for an effector slot that matches no or several saved effector templates.
	/// </summary>
	public static ParticleSystemState? ToSystemState( this SaveParticlesBlock block, IReadOnlyList<ParticleTemplate> templates )
	{
		ArgumentNullException.ThrowIfNull( block );
		ArgumentNullException.ThrowIfNull( templates );
		if ( !block.On )
			return null;
		if ( block.ListProblems.Count > 0 )
			throw new InvalidDataException( $"The Particles block's slot lists are not well formed: {block.ListProblems[0]}" );
		var configuration = new ParticlePoolConfiguration
		{
			Density = unchecked( (int)block.Globals.Density ), PoolCapacity = unchecked( (int)block.Globals.PoolCapacity ), LowDetail = block.Globals.LowDetail != 0
		};
		if ( ParticleSystem.ValidateConfiguration( configuration ) is { } configurationReason )
			throw new InvalidDataException( configurationReason );
		var emitters = new List<ParticleEmitterState>();
		foreach ( var slot in block.ActiveEmitterOrder )
		{
			var emitter = block.Emitters[slot];
			var extent = emitter.VolumeExtent;
			var changed = emitter.EffectId < templates.Count && templates[emitter.EffectId].VolumeExtent != extent;
			emitters.Add( new ParticleEmitterState
			{
				Slot = slot, EffectId = emitter.EffectId, Generation = emitter.Generation, Life = emitter.Life, InitialLife = emitter.InitialLife,
				X = emitter.Position.X, Y = emitter.Position.Y, Z = emitter.Position.Z,
				VelocityX = emitter.Velocity.X, VelocityY = emitter.Velocity.Y, VelocityZ = emitter.Velocity.Z,
				BaseX = emitter.BaseVelocity.X, BaseY = emitter.BaseVelocity.Y, BaseZ = emitter.BaseVelocity.Z,
				SpiralAngle = emitter.SpiralAngle, RateCounter = emitter.RateCounter, Control = emitter.Control, Child = emitter.Child,
				FollowedByParent = emitter.FollowedByParent, DeathEffect = emitter.DeathEffect, Rates = emitter.Rates.ToArray(),
				Burst = emitter.Burst, Cap = emitter.Cap, VolumeExtent = changed ? new[] { extent.X, extent.Y, extent.Z } : null
			} );
		}
		var effectors = new List<ParticleEffectorState>();
		foreach ( var slot in block.ActiveEffectorOrder )
		{
			var effector = block.Effectors[slot];
			effectors.Add( new ParticleEffectorState
			{
				Slot = slot, EffectorId = EffectorId( block, effector ), Generation = effector.Generation, Followed = effector.Followed,
				X = effector.Position.X, Y = effector.Position.Y, Z = effector.Position.Z,
				VelocityX = effector.Velocity.X, VelocityY = effector.Velocity.Y, VelocityZ = effector.Velocity.Z,
				Radius = effector.Radius, DeathEffect = effector.DeathEffect, DeathEffectIsEffector = effector.DeathEffectIsEffector, Life = effector.Life
			} );
		}
		return new ParticleSystemState
		{
			Configuration = configuration,
			Random = block.Globals.Random, TickCounter = (int)Math.Min( block.Globals.TickCounter, int.MaxValue ),
			GenerationCounter = block.Globals.EmitterGeneration, EffectorGenerationCounter = block.Globals.EffectorGeneration,
			Emitters = emitters, FreeEmitters = block.FreeEmitterOrder, Effectors = effectors, FreeEffectors = block.FreeEffectorOrder
		};
	}

	private static int EffectorId( SaveParticlesBlock block, SaveParticleEffector effector )
	{
		var matches = new List<int>();
		for ( var id = 0; id < block.EffectorTemplateBytes.Count; id++ )
		{
			var template = block.EffectorTemplateBytes[id];
			if ( template.AsSpan( EffectorMatchFrom, EffectorMatchTo - EffectorMatchFrom ).SequenceEqual( effector.Raw.AsSpan( EffectorMatchFrom, EffectorMatchTo - EffectorMatchFrom ) )
				&& template.AsSpan( 4, 4 ).SequenceEqual( effector.Raw.AsSpan( 4, 4 ) ) )
				matches.Add( id );
		}
		if ( matches.Count != 1 )
			throw new InvalidDataException( $"Effector slot {effector.Slot} matches {matches.Count} of the saved effector templates; its id cannot be told." );
		return matches[0];
	}
}
