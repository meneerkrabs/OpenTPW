namespace OpenTPW;

/// <summary>One live particle as the renderer reads it: original-unit position (Y up) after the draw-time control scale.</summary>
public struct ParticleSprite
{
	public int Emitter;
	/// <summary>Position in 1/64 original unit (the native particle unit), already scaled around the emitter by the control.</summary>
	public int X, Y, Z;
	/// <summary>Native size; the half-size is size / 1024 original units times the sprite scale.</summary>
	public int Size;
	/// <summary>0xAARRGGBB.</summary>
	public uint Colour;
	public int SpriteCode;
	/// <summary>0 .. FrameCount - 1; FrameCount 0 is untextured.</summary>
	public int Frame;
	public int FrameCount;
	/// <summary>Rotation in 1/65536 turn.</summary>
	public int Spin;
	public int RenderFlags;
}

/// <summary>One live emitter slot's draw bounds (1/64 original unit).</summary>
public readonly record struct ParticleEmitterBounds( int Slot, int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ );

/// <summary>
/// The original particle pool (docs/PARTICLES.md): 120 emitter slots, a fixed particle pool, a private LCG seeded 1 at
/// every reset, advanced in 31 ms ticks. GPU free and integer-valued like the native fields, so the same inputs replay
/// the same particles. Presentation only: it is not part of the canonical simulation, the world hash or any save.
/// </summary>
// [BIN:STP-PPC:0x1009EE70 particle pool init] density, ground callback and particle count (clamped 100..32767); 120 emitter slots of 0x140 bytes, 20 effector slots
// [BIN:STP-PPC:0x1009EF70 particle pool reset] the private generator restarts from 1
// [BIN:STP-PPC:0x1009F748 particle tick] once per 31 ms substep; emitters newest first, then effectors, then particles; the very first call does nothing
// [APPROX:PTCL-902] the template fields' exact arithmetic is read from the template bytes and the traced summary, not from the native instructions: velocity and position units, the drag and gravity signs, random acceleration and jitter ranges, the spawn volume shapes, random colour and spin are modelled, the native integer rounding is not reproduced
public sealed class ParticleSystem
{
	public const int EmitterSlots = 120;
	public const int EffectorSlots = 20;
	public const int DefaultParticleCount = 2048;
	public const int MinimumParticleCount = 100;
	public const int MaximumParticleCount = 32767;
	public const int TickMilliseconds = 31;
	/// <summary>The most wall-clock time one frame advances, so a stalled frame does not replay seconds of particles.</summary>
	public const float MaximumFrameSeconds = 0.25f;

	private sealed class Emitter
	{
		public bool InUse;
		public int Generation;
		public int EffectId;
		public ParticleTemplate Template = null!;
		public int Control;
		public int X, Y, Z;
		public int VelocityX, VelocityY, VelocityZ;
		public int ParticleVelocityX, ParticleVelocityY, ParticleVelocityZ;
		public int Life;
		public int Age;
		public int Live;
		public int First = -1;
		public int Burst, Cap;
		public int[] Rates = new int[4];
		public int Child = -1;
		public int ChildGeneration;
		public bool ChildFollows;
		public bool HasBounds;
		public int MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
		public long Sequence;
	}

	private struct Particle
	{
		public int Next;
		public int X, Y, Z;
		public int VelocityX, VelocityY, VelocityZ;
		public int Life, MaxLife;
		public int ColourSeed;
		public int Spin;
		public bool Fresh;
	}

	private readonly IReadOnlyList<ParticleTemplate> templates;
	private readonly Emitter[] emitters = new Emitter[EmitterSlots];
	private readonly List<int> order = new();
	private readonly Particle[] particles;
	private int freeParticle;
	private readonly Func<int, int, int>? ground;
	private long sequence;
	private uint random = 1;
	private bool skippedFirstTick;
	private float accumulator;

	/// <param name="density">The density setting (PARTICLEDENSITY 500..2000, 1000 = the templates as authored).</param>
	/// <param name="ground">Terrain height (1/64 unit) under a native x, z (1/64 unit), or null for flat ground at no bounce.</param>
	/// <param name="lowDetail">Refuses templates with the low-detail flag (+0xC1).</param>
	public ParticleSystem( IReadOnlyList<ParticleTemplate> templates, int density = 1000, int particleCount = DefaultParticleCount,
		Func<int, int, int>? ground = null, bool lowDetail = false )
	{
		this.templates = templates ?? throw new ArgumentNullException( nameof( templates ) );
		Density = Math.Clamp( density, 500, 2000 );
		particles = new Particle[Math.Clamp( particleCount, MinimumParticleCount, MaximumParticleCount )];
		this.ground = ground;
		LowDetail = lowDetail;
		for ( var index = 0; index < emitters.Length; index++ )
			emitters[index] = new Emitter();
		Clear();
	}

	public int Density { get; }
	public bool LowDetail { get; }
	public int Capacity => particles.Length;
	public int LiveEmitters => order.Count;
	public int LiveParticles { get; private set; }
	public int TicksRun { get; private set; }

	/// <summary>Releases every emitter and particle and restarts the generator from 1 (the native reset).</summary>
	public void Clear()
	{
		foreach ( var emitter in emitters )
		{
			emitter.InUse = false;
			emitter.Generation++;
			emitter.First = -1;
			emitter.Live = 0;
		}
		order.Clear();
		for ( var index = 0; index < particles.Length; index++ )
			particles[index].Next = index + 1;
		particles[^1].Next = -1;
		freeParticle = 0;
		LiveParticles = 0;
		random = 1;
		skippedFirstTick = false;
		accumulator = 0;
		TicksRun = 0;
	}

	// [BIN:STP-PPC:0x1009EE70 private generator] Microsoft LCG: state * 214013 + 2531011, value = state >> 16 (15 bits)
	private int Next( int bound )
	{
		random = unchecked( random * 214013u + 2531011u );
		var value = (int)((random >> 16) & 0x7FFF);
		return bound <= 1 ? 0 : value % bound;
	}

	/// <summary>Uniform in -magnitude..magnitude.</summary>
	private int Spread( int magnitude ) => magnitude <= 0 ? 0 : Next( 2 * magnitude + 1 ) - magnitude;

	/// <summary>Advances by wall-clock seconds in whole 31 ms ticks (the remainder carries over).</summary>
	public int Advance( float seconds )
	{
		if ( !float.IsFinite( seconds ) || seconds <= 0 )
			return 0;
		accumulator += Math.Min( seconds, MaximumFrameSeconds );
		var ticks = 0;
		while ( accumulator >= TickMilliseconds / 1000f )
		{
			accumulator -= TickMilliseconds / 1000f;
			Tick();
			ticks++;
		}
		return ticks;
	}

	/// <summary>Type-1 effect: an emitter at a native position (1/1024 original unit, Y up). Returns slot | generation &lt;&lt; 16, or -1 when refused.</summary>
	// [BIN:STP-PPC:0x1009FD70 type 1 constructor] one emitter, not one particle; ids outside 0..104 and low-detail templates are refused
	public int Create( int effectId, (int X, int Y, int Z) nativePosition, int? control = null ) =>
		Construct( effectId, nativePosition, null, control );

	/// <summary>Type-2 effect: as <see cref="Create"/>, with a direction (unit vector * 1024) that sets the base particle velocity.</summary>
	// [BIN:STP-PPC:0x1009F968 type 2 constructor] velocity = direction * template[+0xB0] / 1024 replaces the base particle velocity before the burst (0x1009FB54)
	public int CreateDirected( int effectId, (int X, int Y, int Z) nativePosition, (int X, int Y, int Z) direction, int? control = null ) =>
		Construct( effectId, nativePosition, direction, control );

	private int Construct( int effectId, (int X, int Y, int Z) nativePosition, (int X, int Y, int Z)? direction, int? control )
	{
		if ( effectId < 0 || effectId > 104 || effectId >= templates.Count )
			return -1;
		var template = templates[effectId];
		if ( LowDetail && template.RefusedInLowDetail )
			return -1;
		var slot = Array.FindIndex( emitters, e => !e.InUse );
		if ( slot < 0 )
			return -1;
		var emitter = emitters[slot];
		emitter.InUse = true;
		emitter.Template = template;
		emitter.EffectId = effectId;
		emitter.Control = control ?? template.Control;
		// [BIN:STP-PPC:0x1009F968 position] the dispatcher passes position * 1024; the constructor stores it >> 4, 1/64 original unit
		emitter.X = nativePosition.X >> 4;
		emitter.Y = nativePosition.Y >> 4;
		emitter.Z = nativePosition.Z >> 4;
		var velocity = template.EmitterVelocity;
		var jitter = template.EmitterVelocityJitter;
		emitter.VelocityX = velocity.X + Spread( jitter );
		emitter.VelocityY = velocity.Y + Spread( jitter );
		emitter.VelocityZ = velocity.Z + Spread( jitter );
		var baseVelocity = template.ParticleVelocity;
		emitter.ParticleVelocityX = baseVelocity.X;
		emitter.ParticleVelocityY = baseVelocity.Y;
		emitter.ParticleVelocityZ = baseVelocity.Z;
		if ( direction is { } unit )
		{
			var speed = ReadSpeed( template );
			emitter.ParticleVelocityX = unit.X * speed / 1024;
			emitter.ParticleVelocityY = unit.Y * speed / 1024;
			emitter.ParticleVelocityZ = unit.Z * speed / 1024;
		}
		emitter.Life = template.EmitterLife;
		emitter.Age = 0;
		emitter.Live = 0;
		emitter.First = -1;
		emitter.HasBounds = false;
		emitter.Child = -1;
		emitter.ChildFollows = false;
		emitter.Sequence = ++sequence;
		// [BIN:STP-PPC:0x1009F968 density] burst, cap and rates scale with the density setting unless template byte +0xC0 is set
		var scale = template.IgnoresDensity ? 1000 : Density;
		emitter.Burst = (int)Math.Clamp( (long)template.Burst * scale / 1000, 0, int.MaxValue );
		emitter.Cap = (int)Math.Clamp( (long)template.Cap * scale / 1000, 1, int.MaxValue );
		for ( var quarter = 0; quarter < 4; quarter++ )
		{
			var rate = template.Rate( quarter );
			emitter.Rates[quarter] = rate > 0 ? (rate * scale + 999) / 1000 : rate < 0 ? -Math.Max( 1, -rate * 1000 / scale ) : 0;
		}
		order.Add( slot );
		// stops at the first refusal (cap or pool full), so a huge burst value cannot spin
		for ( var index = 0; index < emitter.Burst; index++ )
			if ( !Spawn( emitter ) )
				break;
		// [BIN:STP-PPC:0x1009FD70 chained effect] template +0xB4 starts a second emitter at the same place; +0xB8 makes it follow
		if ( template.ChildEffect >= 0 )
		{
			var child = Construct( template.ChildEffect, nativePosition, null, null );
			if ( child >= 0 )
			{
				emitter.Child = child & 0xFFFF;
				emitter.ChildGeneration = child >> 16;
				emitter.ChildFollows = template.ChildFollows;
			}
		}
		return slot | (emitter.Generation << 16);
	}

	/// <summary>Template +0xB0: the speed of a directed (type 2) effect.</summary>
	private static int ReadSpeed( ParticleTemplate template ) => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian( template.Raw[0xB0..] );

	/// <summary>RSE KILL: ends an emitter without its death effect (life -2); its particles run out.</summary>
	// [BIN:STP-PPC:0x100AE718 RSE KILL] sets life -2 on script-owned particle objects
	public bool Kill( int handle )
	{
		if ( Resolve( handle ) is not { } emitter )
			return false;
		emitter.Life = -2;
		return true;
	}

	private Emitter? Resolve( int handle )
	{
		var slot = handle & 0xFFFF;
		if ( handle < 0 || slot >= EmitterSlots )
			return null;
		var emitter = emitters[slot];
		return emitter.InUse && emitter.Generation == handle >> 16 ? emitter : null;
	}

	/// <summary>Moves a live emitter to a native position (1/1024 original unit, Y up), and its following child with it; false for a dead handle.</summary>
	// [BIN:TPW-EXE:0x0051FE30 emitter move] the bump module moves a vehicle's smoke emitter to its sparks node every tick; the position is stored like the constructor's (>> 4)
	public bool Move( int handle, (int X, int Y, int Z) nativePosition )
	{
		if ( Resolve( handle ) is not { } emitter )
			return false;
		emitter.X = nativePosition.X >> 4;
		emitter.Y = nativePosition.Y >> 4;
		emitter.Z = nativePosition.Z >> 4;
		if ( emitter.Child >= 0 && emitter.ChildFollows )
			Move( emitter.Child | (emitter.ChildGeneration << 16), nativePosition );
		return true;
	}

	public bool IsAlive( int handle ) => Resolve( handle ) != null;

	/// <summary>The template id of every live emitter, in slot order (diagnostics and tests).</summary>
	public IEnumerable<int> LiveEffects() => emitters.Where( e => e.InUse ).Select( e => e.EffectId );

	// [BIN:STP-PPC:0x1009D39C particle spawn] position = emitter + spawn volume, velocity = base + jitter + radial burst, life jittered by +-25%
	private bool Spawn( Emitter emitter )
	{
		if ( emitter.Live >= emitter.Cap || freeParticle < 0 )
			return false;
		var template = emitter.Template;
		var index = freeParticle;
		ref var particle = ref particles[index];
		freeParticle = particle.Next;

		var extent = template.VolumeExtent;
		var x = emitter.X;
		var y = emitter.Y;
		var z = emitter.Z;
		if ( template.VolumeShape == 1 && (extent.X | extent.Y | extent.Z) != 0 )
		{
			int dx, dy, dz;
			do
			{
				dx = Spread( 1000 ); dy = Spread( 1000 ); dz = Spread( 1000 );
			} while ( (long)dx * dx + (long)dy * dy + (long)dz * dz > 1000L * 1000L );
			x += extent.X * dx / 1000; y += extent.Y * dy / 1000; z += extent.Z * dz / 1000;
		}
		else
		{
			x += Spread( extent.X ); y += Spread( extent.Y ); z += Spread( extent.Z );
		}

		var jitter = template.VelocityJitter;
		var vx = emitter.ParticleVelocityX + Spread( jitter );
		var vy = emitter.ParticleVelocityY + Spread( jitter );
		var vz = emitter.ParticleVelocityZ + Spread( jitter );
		if ( template.RadialSpeed != 0 )
		{
			int dx, dy, dz;
			double length;
			do
			{
				dx = Spread( 1000 ); dy = Spread( 1000 ); dz = Spread( 1000 );
				length = Math.Sqrt( (double)dx * dx + (double)dy * dy + (double)dz * dz );
			} while ( length < 1 );
			vx += (int)(dx * template.RadialSpeed / length);
			vy += (int)(dy * template.RadialSpeed / length);
			vz += (int)(dz * template.RadialSpeed / length);
		}

		var life = Math.Max( 2, template.ParticleLife + template.ParticleLife * (Next( 51 ) - 25) / 100 );
		particle = new Particle
		{
			X = x, Y = y, Z = z, VelocityX = vx, VelocityY = vy, VelocityZ = vz,
			Life = life, MaxLife = life,
			ColourSeed = template.RandomColour ? Next( 16 ) : -1,
			Fresh = true,
			Next = emitter.First
		};
		emitter.First = index;
		emitter.Live++;
		LiveParticles++;
		Grow( emitter, x, y, z );
		return true;
	}

	private static void Grow( Emitter emitter, int x, int y, int z )
	{
		if ( !emitter.HasBounds )
		{
			emitter.HasBounds = true;
			emitter.MinX = emitter.MaxX = x; emitter.MinY = emitter.MaxY = y; emitter.MinZ = emitter.MaxZ = z;
			return;
		}
		emitter.MinX = Math.Min( emitter.MinX, x ); emitter.MaxX = Math.Max( emitter.MaxX, x );
		emitter.MinY = Math.Min( emitter.MinY, y ); emitter.MaxY = Math.Max( emitter.MaxY, y );
		emitter.MinZ = Math.Min( emitter.MinZ, z ); emitter.MaxZ = Math.Max( emitter.MaxZ, z );
	}

	/// <summary>One 31 ms tick: every live emitter newest first, then every particle.</summary>
	public void Tick()
	{
		if ( !skippedFirstTick )
		{
			skippedFirstTick = true;
			return;
		}
		TicksRun++;
		for ( var position = order.Count - 1; position >= 0; position-- )
		{
			if ( position >= order.Count )
				continue;
			var slot = order[position];
			var emitter = emitters[slot];
			if ( emitter.InUse )
				TickEmitter( emitter );
		}
		// [APPROX:PTCL-903] the 20 effector slots (attract/repel with a kill radius) are read from the file but not simulated: no OpenTPW effect uses one
		for ( var position = order.Count - 1; position >= 0; position-- )
		{
			var emitter = emitters[order[position]];
			TickParticles( emitter );
		}
		for ( var position = order.Count - 1; position >= 0; position-- )
		{
			var slot = order[position];
			var emitter = emitters[slot];
			if ( emitter.Life < 0 && emitter.Live == 0 )
			{
				// A finished emitter frees its slot; the generation change retires its handle.
				emitter.InUse = false;
				emitter.Generation++;
				emitter.HasBounds = false;
				order.RemoveAt( position );
			}
		}
	}

	private void TickEmitter( Emitter emitter )
	{
		var template = emitter.Template;
		if ( !template.Immortal && emitter.Life >= 0 )
			emitter.Life--;
		// [BIN:STP-PPC:0x1009DF1C emitter life] at -1 the death effect starts; -2 (RSE KILL) ends silently
		if ( emitter.Life == -1 && template.DeathEffect >= 0 )
			Construct( template.DeathEffect, (emitter.X << 4, emitter.Y << 4, emitter.Z << 4), null, null );
		if ( emitter.Life == -1 )
			emitter.Life = -3;
		if ( emitter.ChildFollows && emitter.Child >= 0 && emitters[emitter.Child] is { InUse: true } child && child.Generation == emitter.ChildGeneration )
		{
			child.X = emitter.X; child.Y = emitter.Y; child.Z = emitter.Z;
		}
		// emitter motion: drag, gravity, then position, with the ground bounce
		emitter.VelocityX -= emitter.VelocityX * template.EmitterDrag / 1024;
		emitter.VelocityY -= emitter.VelocityY * template.EmitterDrag / 1024;
		emitter.VelocityZ -= emitter.VelocityZ * template.EmitterDrag / 1024;
		emitter.VelocityY -= template.EmitterGravity;
		emitter.X += emitter.VelocityX; emitter.Y += emitter.VelocityY; emitter.Z += emitter.VelocityZ;
		if ( ground != null )
		{
			var floor = ground( emitter.X, emitter.Z );
			if ( emitter.Y < floor )
			{
				emitter.Y = floor;
				emitter.VelocityY = -emitter.VelocityY * template.GroundBounce / 100;
			}
		}
		if ( emitter.Life < 0 )
			return;
		var quarter = template.EmitterLife > 0 ? Math.Min( 3, emitter.Age * 4 / template.EmitterLife ) : 0;
		var rate = emitter.Rates[quarter];
		var count = rate > 0 ? rate : rate < 0 && emitter.Age % -rate == 0 ? 1 : 0;
		for ( var index = 0; index < count; index++ )
			if ( !Spawn( emitter ) )
				break;
		emitter.Age++;
	}

	private void TickParticles( Emitter emitter )
	{
		var template = emitter.Template;
		var acceleration = template.RandomAcceleration;
		var previous = -1;
		var index = emitter.First;
		while ( index >= 0 )
		{
			ref var particle = ref particles[index];
			var next = particle.Next;
			if ( particle.Fresh )
			{
				// a particle does not age or move on its spawn tick
				particle.Fresh = false;
				previous = index;
				index = next;
				continue;
			}
			particle.Life--;
			if ( particle.Life <= 0 )
			{
				if ( template.ParticleDeathEffect >= 0 )
					Construct( template.ParticleDeathEffect, (particle.X << 4, particle.Y << 4, particle.Z << 4), null, null );
				if ( previous < 0 )
					emitter.First = next;
				else
					particles[previous].Next = next;
				particle.Next = freeParticle;
				freeParticle = index;
				emitter.Live--;
				LiveParticles--;
				index = next;
				continue;
			}
			particle.Spin += template.Spin;
			if ( (acceleration.X | acceleration.Y | acceleration.Z) != 0 )
			{
				particle.VelocityX += Spread( acceleration.X );
				particle.VelocityY += Spread( acceleration.Y );
				particle.VelocityZ += Spread( acceleration.Z );
			}
			particle.VelocityX -= particle.VelocityX * template.ParticleDrag / 1024;
			particle.VelocityY -= particle.VelocityY * template.ParticleDrag / 1024;
			particle.VelocityZ -= particle.VelocityZ * template.ParticleDrag / 1024;
			particle.VelocityY -= template.ParticleGravity;
			particle.X += particle.VelocityX; particle.Y += particle.VelocityY; particle.Z += particle.VelocityZ;
			Grow( emitter, particle.X, particle.Y, particle.Z );
			previous = index;
			index = next;
		}
	}

	/// <summary>The live emitters' bounds (1/64 original unit), for culling; a slot with no particles is omitted.</summary>
	public int CollectBounds( List<ParticleEmitterBounds> destination )
	{
		destination.Clear();
		foreach ( var slot in order )
		{
			var emitter = emitters[slot];
			if ( emitter.HasBounds && emitter.Live > 0 )
				destination.Add( new ParticleEmitterBounds( slot, emitter.MinX, emitter.MinY, emitter.MinZ, emitter.MaxX, emitter.MaxY, emitter.MaxZ ) );
		}
		return destination.Count;
	}

	/// <summary>Writes every live particle, emitters in slot order (the native draw order; depth sorting is untraced), and returns the count.</summary>
	// [BIN:STP-PPC:engine_shared 0x1ACC8 draw] control scale: drawn = emitter + (particle - emitter) * control / 1000
	public int Snapshot( List<ParticleSprite> destination )
	{
		destination.Clear();
		for ( var slot = 0; slot < EmitterSlots; slot++ )
		{
			var emitter = emitters[slot];
			if ( !emitter.InUse )
				continue;
			var template = emitter.Template;
			var frames = Math.Max( 0, template.FrameCount );
			for ( var index = emitter.First; index >= 0; index = particles[index].Next )
			{
				ref readonly var particle = ref particles[index];
				var age = particle.MaxLife - particle.Life;
				var colourIndex = Math.Clamp( particle.Life * 15 / particle.MaxLife, 0, 15 );
				var colour = template.Colour( colourIndex );
				if ( particle.ColourSeed >= 0 )
					colour = (colour & 0xFF000000u) | (template.Colour( particle.ColourSeed ) & 0x00FFFFFFu);
				destination.Add( new ParticleSprite
				{
					Emitter = slot,
					X = emitter.X + (int)((long)(particle.X - emitter.X) * emitter.Control / 1000),
					Y = emitter.Y + (int)((long)(particle.Y - emitter.Y) * emitter.Control / 1000),
					Z = emitter.Z + (int)((long)(particle.Z - emitter.Z) * emitter.Control / 1000),
					Size = template.SizeAtBirth + (template.SizeAtDeath - template.SizeAtBirth) * age / particle.MaxLife,
					Colour = colour,
					SpriteCode = template.SpriteCode,
					FrameCount = frames,
					Frame = frames == 0 ? 0 : Math.Min( frames - 1, age * frames / particle.MaxLife ),
					Spin = particle.Spin,
					RenderFlags = template.RenderFlags
				} );
			}
		}
		return destination.Count;
	}
}
