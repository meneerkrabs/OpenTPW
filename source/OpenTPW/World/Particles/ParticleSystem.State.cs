using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>One live emitter slot's saved state (tp.exe's 320-byte slot, without the particles).</summary>
public sealed record ParticleEmitterState
{
	public int Slot { get; init; }
	public int EffectId { get; init; }
	public int Generation { get; init; }
	public int Life { get; init; }
	public int InitialLife { get; init; }
	public int X { get; init; }
	public int Y { get; init; }
	public int Z { get; init; }
	public int VelocityX { get; init; }
	public int VelocityY { get; init; }
	public int VelocityZ { get; init; }
	public int BaseX { get; init; }
	public int BaseY { get; init; }
	public int BaseZ { get; init; }
	public int SpiralAngle { get; init; }
	public int RateCounter { get; init; }
	public int Control { get; init; }
	public int Child { get; init; }
	public bool FollowedByParent { get; init; }
	public int DeathEffect { get; init; }
	public IReadOnlyList<int> Rates { get; init; } = Array.Empty<int>();
	public int Burst { get; init; }
	public int Cap { get; init; }
	/// <summary>The spawn volume extent when <c>SetVolumeExtent</c> changed it (X, Y, Z); null keeps the template's.</summary>
	public IReadOnlyList<int>? VolumeExtent { get; init; }
}

/// <summary>One live effector slot's saved state.</summary>
public sealed record ParticleEffectorState
{
	public int Slot { get; init; }
	public int EffectorId { get; init; }
	public int Generation { get; init; }
	public bool Followed { get; init; }
	public int X { get; init; }
	public int Y { get; init; }
	public int Z { get; init; }
	public int VelocityX { get; init; }
	public int VelocityY { get; init; }
	public int VelocityZ { get; init; }
	public int Radius { get; init; }
	public int DeathEffect { get; init; }
	public bool DeathEffectIsEffector { get; init; }
	public int Life { get; init; }
}

/// <summary>The saved pool settings, independent of the loading host's graphics preset.</summary>
public sealed record ParticlePoolConfiguration
{
	/// <summary>The signed density word used by the native wrapped multiply; load does not normalize zero.</summary>
	public required int Density { get; init; }
	/// <summary>The supported initialized-pool domain, 100..32767; invalid saved counts are refused, not clamped.</summary>
	public required int PoolCapacity { get; init; }
	/// <summary>The native low-detail word's zero/nonzero meaning.</summary>
	public required bool LowDetail { get; init; }
}

/// <summary>
/// The particle pool as the original saves it (tp.exe 0x51F680 'PTCL', 0x9E68 bytes of pool state): the private generator, the
/// generation counters, the live emitters (active list, newest first) and effectors with the free lists' order, and no particles.
/// </summary>
public sealed record ParticleSystemState
{
	/// <summary>Absent in older own-format saves: keep the loading pool's current settings.</summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public ParticlePoolConfiguration? Configuration { get; init; }
	public uint Random { get; init; }
	public int TickCounter { get; init; }
	public int GenerationCounter { get; init; }
	public int EffectorGenerationCounter { get; init; }
	public IReadOnlyList<ParticleEmitterState> Emitters { get; init; } = Array.Empty<ParticleEmitterState>();
	public IReadOnlyList<int> FreeEmitters { get; init; } = Array.Empty<int>();
	public IReadOnlyList<ParticleEffectorState> Effectors { get; init; } = Array.Empty<ParticleEffectorState>();
	public IReadOnlyList<int> FreeEffectors { get; init; } = Array.Empty<int>();
}

public sealed partial class ParticleSystem
{
	/// <summary>
	/// The initialized pool, including its settings and generator even when no emitter or effector is live. The original saves the whole block with a particle
	/// array of length 0 (the game save passes 0, tp.exe 0x41661E), so the particles themselves are never captured.
	/// </summary>
	// [BIN:TPW-EXE:0x51F680 save] 'PTCL', the init flag, 0x9E68 bytes from 0x80CED0 (pool globals, RNG, 120 emitters, 20 effectors, list heads), the templates (0x8B60 bytes, file data here), the particle array length and data
	public ParticleSystemState Capture()
	{
		var savedEmitters = new List<ParticleEmitterState>( liveEmitters );
		for ( var slot = activeEmitter; slot >= 0; slot = emitters[slot].Next )
		{
			var emitter = emitters[slot];
			savedEmitters.Add( new ParticleEmitterState
			{
				Slot = slot, EffectId = emitter.EffectId, Generation = emitter.Generation, Life = emitter.Life, InitialLife = emitter.InitialLife,
				X = emitter.X, Y = emitter.Y, Z = emitter.Z,
				VelocityX = emitter.VelocityX, VelocityY = emitter.VelocityY, VelocityZ = emitter.VelocityZ,
				BaseX = emitter.BaseX, BaseY = emitter.BaseY, BaseZ = emitter.BaseZ,
				SpiralAngle = emitter.SpiralAngle, RateCounter = emitter.RateCounter, Control = emitter.Control, Child = emitter.Child,
				FollowedByParent = emitter.FollowedByParent, DeathEffect = emitter.DeathEffect, Rates = emitter.Rates.ToArray(),
				Burst = emitter.Burst, Cap = emitter.Cap,
				VolumeExtent = (emitter.ExtentX, emitter.ExtentY, emitter.ExtentZ) == emitter.Template.VolumeExtent ? null : new[] { emitter.ExtentX, emitter.ExtentY, emitter.ExtentZ }
			} );
		}
		var freeEmitters = new List<int>();
		for ( var slot = freeEmitter; slot >= 0; slot = emitters[slot].Next )
			freeEmitters.Add( slot );
		var savedEffectors = new List<ParticleEffectorState>( LiveEffectors );
		for ( var slot = activeEffector; slot >= 0; slot = effectors[slot].Next )
		{
			var effector = effectors[slot];
			savedEffectors.Add( new ParticleEffectorState
			{
				Slot = slot, EffectorId = effector.EffectorId, Generation = effector.Generation, Followed = effector.Followed,
				X = effector.X, Y = effector.Y, Z = effector.Z,
				VelocityX = effector.VelocityX, VelocityY = effector.VelocityY, VelocityZ = effector.VelocityZ,
				Radius = effector.Radius, DeathEffect = effector.DeathEffect, DeathEffectIsEffector = effector.DeathEffectIsEffector, Life = effector.Life
			} );
		}
		var freeEffectors = new List<int>();
		for ( var slot = freeEffector; slot >= 0; slot = effectors[slot].Next )
			freeEffectors.Add( slot );
		return new ParticleSystemState
		{
			Configuration = new ParticlePoolConfiguration { Density = Density, PoolCapacity = Capacity, LowDetail = LowDetail },
			Random = random, TickCounter = tickCounter, GenerationCounter = generationCounter, EffectorGenerationCounter = effectorGenerationCounter,
			Emitters = savedEmitters, FreeEmitters = freeEmitters, Effectors = savedEffectors, FreeEffectors = freeEffectors
		};
	}

	// The native loader trusts the saved count. Bound allocation to the initialized-pool domain instead of reproducing unsafe heap/index accesses.
	internal static string? ValidateConfiguration( ParticlePoolConfiguration? configuration ) =>
		configuration is { PoolCapacity: < MinimumParticleCount or > MaximumParticleCount }
			? $"the particle state's pool capacity {configuration.PoolCapacity} is outside the supported {MinimumParticleCount}..{MaximumParticleCount} range"
			: null;

	/// <summary>Whether a saved state is well formed for the fixed slot counts (the envelope's shape check; templates are not consulted).</summary>
	public static string? ValidateState( ParticleSystemState? state )
	{
		if ( state == null )
			return "the particle state is null";
		if ( ValidateConfiguration( state.Configuration ) is { } configurationReason )
			return configurationReason;
		if ( state.Emitters == null || state.FreeEmitters == null || state.Effectors == null || state.FreeEffectors == null )
			return "the particle state has a missing list";
		if ( state.Emitters.Count + state.FreeEmitters.Count != EmitterSlots )
			return $"the particle state holds {state.Emitters.Count} live and {state.FreeEmitters.Count} free emitter slots, not {EmitterSlots}";
		if ( state.Effectors.Count + state.FreeEffectors.Count != EffectorSlots )
			return $"the particle state holds {state.Effectors.Count} live and {state.FreeEffectors.Count} free effector slots, not {EffectorSlots}";
		var seen = new bool[EmitterSlots];
		foreach ( var slot in state.FreeEmitters.Concat( state.Emitters.Select( emitter => emitter?.Slot ?? -1 ) ) )
		{
			if ( slot < 0 || slot >= EmitterSlots || seen[slot] )
				return $"the particle state names emitter slot {slot} twice or outside 0..{EmitterSlots - 1}";
			seen[slot] = true;
		}
		var seenEffectors = new bool[EffectorSlots];
		foreach ( var slot in state.FreeEffectors.Concat( state.Effectors.Select( effector => effector?.Slot ?? -1 ) ) )
		{
			if ( slot < 0 || slot >= EffectorSlots || seenEffectors[slot] )
				return $"the particle state names effector slot {slot} twice or outside 0..{EffectorSlots - 1}";
			seenEffectors[slot] = true;
		}
		foreach ( var emitter in state.Emitters )
		{
			if ( emitter.EffectId < 0 || emitter.EffectId >= EffectIdLimit )
				return $"the particle state's emitter {emitter.Slot} runs effect {emitter.EffectId}, outside 0..{EffectIdLimit - 1}";
			if ( emitter.Generation is < 0 or > 0xFFFF )
				return $"the particle state's emitter {emitter.Slot} has generation {emitter.Generation}";
			if ( emitter.Rates == null || emitter.Rates.Count != 4 )
				return $"the particle state's emitter {emitter.Slot} does not hold four rates";
		}
		foreach ( var effector in state.Effectors )
		{
			if ( effector.EffectorId < 0 || effector.EffectorId >= EffectorSlots )
				return $"the particle state's effector {effector.Slot} is template {effector.EffectorId}, outside 0..{EffectorSlots - 1}";
			if ( effector.Generation is < 0 or > 0xFFFF )
				return $"the particle state's effector {effector.Slot} has generation {effector.Generation}";
		}
		const int Bound = 1 << 24;
		foreach ( var emitter in state.Emitters )
			if ( Math.Abs( (long)emitter.Life ) > Bound || Math.Abs( (long)emitter.InitialLife ) > Bound || emitter.Rates.Any( rate => rate is < -128 or > 127 )
				|| emitter.Control is < short.MinValue or > short.MaxValue || emitter.Cap is < short.MinValue or > short.MaxValue || Math.Abs( (long)emitter.Burst ) > Bound
				|| emitter.RateCounter is < short.MinValue or > short.MaxValue || emitter.SpiralAngle is < 0 or > 0xFFF
				|| (emitter.VolumeExtent is { } extent && (extent.Count != 3 || extent.Any( value => Math.Abs( (long)value ) > Bound ))) )
				return $"the particle state's emitter {emitter.Slot} has a life, rate, control, cap or counter outside the native ranges";
		foreach ( var effector in state.Effectors )
			if ( Math.Abs( (long)effector.Life ) > Bound || Math.Abs( (long)effector.Radius ) > Bound )
				return $"the particle state's effector {effector.Slot} has a life or radius outside the native ranges";
		if ( state.GenerationCounter is < 0 or > 0xFFFF || state.EffectorGenerationCounter is < 0 or > 0xFFFF || state.TickCounter < 0 )
			return "the particle state's counters are out of range";
		return null;
	}

	/// <summary>
	/// Replaces the pool with a saved state: emitters and effectors come back with the generator, the particle pool is empty and every live count is
	/// zero (the original's load with a particle array of length 0), then the post-load pass kills screen-space emitters (life -2, hidden) and
	/// effectors flagged +0x45 (life -2).
	/// </summary>
	// [BIN:TPW-EXE:0x51F7A0 load] restores the block, keeps the current ground function and sine table, rebuilds an empty particle pool
	// [BIN:TPW-EXE:0x5200B0 post-load] emitters with byte +5: life = -2, +0x2A = 1; effectors with byte +0x45: life = -2
	public void Restore( ParticleSystemState state )
	{
		if ( ValidateState( state ) is { } reason )
			throw new InvalidDataException( reason );
		foreach ( var saved in state.Emitters )
			if ( saved.EffectId >= templates.Count )
				throw new InvalidDataException( $"The saved particle emitter {saved.Slot} runs effect {saved.EffectId}, which the template file lacks." );
		foreach ( var saved in state.Effectors )
			if ( saved.EffectorId >= effectorTemplates.Count )
				throw new InvalidDataException( $"The saved particle effector {saved.Slot} is template {saved.EffectorId}, which the template file lacks." );

		// [BIN:TPW-EXE:0x51F851 pool load] the saved globals replace the host's; only the sine/ground pointers are retained at 0x51F9FC.
		// All validation and any allocation happen before mutation, so a rejected state leaves the old pool and settings intact.
		if ( state.Configuration is { } configuration )
		{
			var restoredParticles = configuration.PoolCapacity == Capacity ? particles : new Particle[configuration.PoolCapacity];
			particles = restoredParticles;
			Density = configuration.Density;
			LowDetail = configuration.LowDetail;
		}
		Clear();
		random = state.Random;
		tickCounter = state.TickCounter;
		generationCounter = state.GenerationCounter;
		effectorGenerationCounter = state.EffectorGenerationCounter;

		var previous = -1;
		activeEmitter = state.Emitters.Count > 0 ? state.Emitters[0].Slot : -1;
		foreach ( var saved in state.Emitters )
		{
			var emitter = emitters[saved.Slot];
			var template = templates[saved.EffectId];
			emitter.InUse = true;
			emitter.Template = template;
			emitter.EffectId = saved.EffectId;
			emitter.Hold = template.Hold;
			emitter.Hidden = false;
			emitter.Generation = saved.Generation;
			emitter.Life = saved.Life;
			emitter.InitialLife = saved.InitialLife;
			emitter.X = saved.X; emitter.Y = saved.Y; emitter.Z = saved.Z;
			emitter.VelocityX = saved.VelocityX; emitter.VelocityY = saved.VelocityY; emitter.VelocityZ = saved.VelocityZ;
			emitter.BaseX = saved.BaseX; emitter.BaseY = saved.BaseY; emitter.BaseZ = saved.BaseZ;
			emitter.SpiralAngle = saved.SpiralAngle;
			emitter.RateCounter = saved.RateCounter;
			emitter.Control = saved.Control;
			emitter.Child = saved.Child;
			emitter.FollowedByParent = saved.FollowedByParent;
			emitter.DeathEffect = saved.DeathEffect;
			for ( var index = 0; index < 4; index++ )
				emitter.Rates[index] = saved.Rates[index];
			emitter.Burst = saved.Burst;
			emitter.Cap = saved.Cap;
			(emitter.ExtentX, emitter.ExtentY, emitter.ExtentZ) = saved.VolumeExtent is { Count: 3 } extent ? (extent[0], extent[1], extent[2]) : template.VolumeExtent;
			var acceleration = template.RandomAcceleration;
			emitter.RandomAcceleration = (acceleration.X | acceleration.Y | acceleration.Z) != 0;
			emitter.Live = 0;
			emitter.First = -1;
			ResetBounds( emitter );
			emitter.Prev = previous;
			if ( previous >= 0 )
				emitters[previous].Next = saved.Slot;
			emitter.Next = -1;
			previous = saved.Slot;
			// the post-load pass
			if ( template.ScreenSpace )
			{
				emitter.Life = -2;
				emitter.Hidden = true;
			}
		}
		liveEmitters = state.Emitters.Count;
		LinkFreeList( state.FreeEmitters, slot => emitters[slot], out freeEmitter );

		previous = -1;
		activeEffector = state.Effectors.Count > 0 ? state.Effectors[0].Slot : -1;
		foreach ( var saved in state.Effectors )
		{
			var effector = effectors[saved.Slot];
			var template = effectorTemplates[saved.EffectorId];
			effector.InUse = true;
			effector.Template = template;
			effector.EffectorId = saved.EffectorId;
			effector.Group = template.Group;
			effector.Generation = saved.Generation;
			effector.Followed = saved.Followed;
			effector.X = saved.X; effector.Y = saved.Y; effector.Z = saved.Z;
			effector.VelocityX = saved.VelocityX; effector.VelocityY = saved.VelocityY; effector.VelocityZ = saved.VelocityZ;
			effector.Radius = saved.Radius;
			effector.DeathEffect = saved.DeathEffect;
			effector.DeathEffectIsEffector = saved.DeathEffectIsEffector;
			effector.Life = template.KilledOnLoad ? -2 : saved.Life;
			effector.Prev = previous;
			if ( previous >= 0 )
				effectors[previous].Next = saved.Slot;
			effector.Next = -1;
			previous = saved.Slot;
		}
		LiveEffectors = state.Effectors.Count;
		LinkFreeEffectors( state.FreeEffectors );
	}

	private void LinkFreeList( IReadOnlyList<int> order, Func<int, Emitter> slotOf, out int head )
	{
		head = order.Count > 0 ? order[0] : -1;
		var previous = -1;
		foreach ( var slot in order )
		{
			var emitter = slotOf( slot );
			emitter.Prev = previous;
			emitter.Next = -1;
			if ( previous >= 0 )
				slotOf( previous ).Next = slot;
			previous = slot;
		}
	}

	private void LinkFreeEffectors( IReadOnlyList<int> order )
	{
		freeEffector = order.Count > 0 ? order[0] : -1;
		var previous = -1;
		foreach ( var slot in order )
		{
			var effector = effectors[slot];
			effector.Prev = previous;
			effector.Next = -1;
			if ( previous >= 0 )
				effectors[previous].Next = slot;
			previous = slot;
		}
	}
}
