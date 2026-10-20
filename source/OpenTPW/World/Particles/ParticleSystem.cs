namespace OpenTPW;

/// <summary>One live particle as the renderer reads it: original-unit position (Y up) after the draw-time control scale.</summary>
public struct ParticleSprite
{
	public int Emitter;
	/// <summary>Position in 1/64 original unit (the native particle unit), already scaled around the emitter by the control.</summary>
	public int X, Y, Z;
	/// <summary>Native size; the half-height is size / 1024 original units.</summary>
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
	/// <summary>The emitter's template is screen-space (+5): <see cref="X"/> and <see cref="Z"/> place it on the screen (see <see cref="ParticleScreenSpace"/>).</summary>
	public bool ScreenSpace;
}

/// <summary>One live emitter slot's draw bounds (1/64 original unit).</summary>
public readonly record struct ParticleEmitterBounds( int Slot, int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ );

/// <summary>
/// The original particle pool (docs/PARTICLES.md), ported from the Windows executable's instructions: 120 emitter slots on a LIFO free
/// list and a newest-first active list, 20 effector slots, a fixed particle pool, a private LCG seeded 1 at every reset, advanced in
/// 31 ms ticks. GPU free, integer-valued like the native fields and allocation free per tick, so the same inputs replay the same
/// particles. Presentation only: it is not part of the canonical simulation or the world hash; a park-resume save holds its emitters
/// and effectors but no particles, as the original's PART save does (<see cref="Capture"/>).
/// </summary>
// [BIN:TPW-EXE:0x51FAA0 pool init] ground callback (null = the stub 0x5D60C0 that returns 0), density (0 becomes 1), count (clamped 100..0x7FFF), RNG seed 1, sine table 0x51F320; the game passes (0, PARTICLEDENSITY, 0x800) at 0x54E5F9 and 0x54EAB9
// [BIN:TPW-EXE:0x51FB70 pool reset] RNG 1, generation counters 1, the emitter and effector free lists ascending from slot 0, the particle free list ascending from index 0
// [BIN:TPW-EXE:0x520130 tick] first call does nothing; emitters newest first, then effectors 0x520F10, then particles 0x5214A0
// [BIN:STP-PPC:0x1009EE70 particle pool init] the Mac twin (density, ground callback and particle count; 120 emitter slots of 0x140 bytes, 20 effector slots)
public sealed partial class ParticleSystem
{
	public const int EmitterSlots = 120;
	public const int EffectorSlots = 20;
	public const int DefaultParticleCount = 0x800;
	public const int MinimumParticleCount = 100;
	public const int MaximumParticleCount = 0x7FFF;
	public const int TickMilliseconds = 31;
	/// <summary>The most wall-clock time one frame advances, so a stalled frame does not replay seconds of particles.</summary>
	public const float MaximumFrameSeconds = 0.25f;
	/// <summary>The PARTICLEDENSITY of the Low detail file (<c>low.sam</c>), used when no detail options are loaded; 1024 would run the templates as authored.</summary>
	// [BIN:TPW-EXE:0x54E5F9 pool init call] density = [0x7858C8], GameOptions.PARTICLEDENSITY of the detail object (its fields start at 0x785874, schema 0x7442C0), which only the detail-file load 0x44DC60 fills
	// [DATA:low.sam:GameOptions.PARTICLEDENSITY] 500 (med.sam 1000, high.sam 1500)
	public const int NativeDefaultDensity = 500;

	/// <summary>
	/// The density a park's pool starts with: the PARTICLEDENSITY of the player's detail options (the preset's original file, Enhanced or
	/// Custom), or <see cref="NativeDefaultDensity"/> when no options are loaded or the preset's file is missing.
	/// </summary>
	// [BIN:TPW-EXE:0x0044DE20 detail options] the player's saved detail level [0x78D8E0] (0, 1, 2) selects low.sam, med.sam or high.sam (high only with [0x78D8D8] set, else med) for the load 0x44DC60 -> 0x401650 into the detail object, whose PARTICLEDENSITY the pool init reads; OpenTPW's graphics settings keep the same choice (and its Enhanced/Custom extensions)
	public static int OptionsDensity( IGraphicsSettings? graphics ) =>
		graphics != null && graphics.GetPresetValues( graphics.Current.Preset ) != null ? graphics.Detail.ParticleDensity : NativeDefaultDensity;
	/// <summary>The highest effect id plus one (tp.exe 0x521E60 refuses ids at 0x69).</summary>
	public const int EffectIdLimit = 0x69;

	private sealed class Emitter
	{
		public bool InUse;
		public int Next = -1, Prev = -1;
		public int First = -1;
		public int Live;
		public int Generation;
		public int EffectId;
		public ParticleTemplate Template = null!;
		public int Hold;
		public int Life, InitialLife;
		public int X, Y, Z;
		public int VelocityX, VelocityY, VelocityZ;
		public int BaseX, BaseY, BaseZ;
		public int SpiralAngle;
		public int RateCounter;
		public int Control;
		public int Child = -1;
		public bool FollowedByParent;
		public int DeathEffect;
		public bool RandomAcceleration;
		public bool Hidden;
		public int Burst, Cap;
		public readonly int[] Rates = new int[4];
		public int MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
		/// <summary>The volume extent the spawns use (the template's, or the caller's after <see cref="SetVolumeExtent"/>).</summary>
		public int ExtentX, ExtentY, ExtentZ;
	}

	private sealed class Effector
	{
		public bool InUse;
		public int Next = -1, Prev = -1;
		public ParticleEffectorTemplate Template = null!;
		public int EffectorId;
		public int Group;
		public int Generation;
		public bool Followed;
		public int X, Y, Z;
		public int VelocityX, VelocityY, VelocityZ;
		public int Radius;
		public int DeathEffect;
		public bool DeathEffectIsEffector;
		public int Life;
	}

	private struct Particle
	{
		public int X, Y, Z;
		public int VelocityX, VelocityY, VelocityZ;
		public uint Colour;
		public int MaxLife, Life;
		public short State;
		public short Emitter;
		public short Size;
		public short Group;
		public short Frame;
		public ushort Rotation;
		public int Next, Prev;
	}

	// [BIN:TPW-EXE:0x51F320 sine table] 512 entries of trunc(sin(2 i * 0.00613591796875) * 256), the angle unit is 1/512 turn (the 12-bit angles are shifted right by 3)
	private static readonly int[] Sine = BuildSine();

	private static int[] BuildSine()
	{
		var table = new int[512];
		for ( var index = 0; index < table.Length; index++ )
			table[index] = (int)(Math.Sin( 2.0 * index * 0.00613591796875 ) * 256.0);
		return table;
	}

	private readonly IReadOnlyList<ParticleTemplate> templates;
	private readonly IReadOnlyList<ParticleEffectorTemplate> effectorTemplates;
	private readonly Emitter[] emitters = new Emitter[EmitterSlots];
	private readonly Effector[] effectors = new Effector[EffectorSlots];
	private Particle[] particles;
	private int activeEmitter, freeEmitter, activeEffector, freeEffector, freeParticle;
	private int liveEmitters;
	private int generationCounter, effectorGenerationCounter;
	private readonly Func<int, int, int>? ground;
	private uint random = 1;
	private int tickCounter;
	private float accumulator;

	/// <param name="density">PARTICLEDENSITY: each rate byte, cap and burst becomes (value * density) &gt;&gt; 10, so 1024 runs the templates as authored; 0 becomes 1.</param>
	/// <param name="ground">Terrain height (native 1/1024 unit) under a native x, z (1/1024 unit), or null for the original's stub, which returns 0.</param>
	/// <param name="lowDetail">Refuses templates with the low-detail flag (+0xC1).</param>
	/// <param name="effectors">The effector templates of the same file (tp.exe 0x80C6B0); without them effector effects are refused.</param>
	public ParticleSystem( IReadOnlyList<ParticleTemplate> templates, int density = NativeDefaultDensity, int particleCount = DefaultParticleCount,
		Func<int, int, int>? ground = null, bool lowDetail = false, IReadOnlyList<ParticleEffectorTemplate>? effectors = null )
	{
		this.templates = templates ?? throw new ArgumentNullException( nameof( templates ) );
		effectorTemplates = effectors ?? Array.Empty<ParticleEffectorTemplate>();
		Density = density == 0 ? 1 : density;
		particles = new Particle[Math.Clamp( particleCount, MinimumParticleCount, MaximumParticleCount )];
		this.ground = ground;
		LowDetail = lowDetail;
		for ( var index = 0; index < emitters.Length; index++ )
			emitters[index] = new Emitter();
		for ( var index = 0; index < this.effectors.Length; index++ )
			this.effectors[index] = new Effector();
		Clear();
	}

	public int Density { get; private set; }
	public bool LowDetail { get; private set; }
	public int Capacity => particles.Length;
	public int LiveEmitters => liveEmitters;
	public int LiveEffectors { get; private set; }
	public int LiveParticles { get; private set; }
	public int TicksRun { get; private set; }
	/// <summary>The private generator's state (diagnostics, tests and saves).</summary>
	public uint RandomState => random;

	/// <summary>Releases every emitter, effector and particle and restarts the generator from 1 (the native reset).</summary>
	public void Clear()
	{
		for ( var slot = 0; slot < emitters.Length; slot++ )
		{
			var emitter = emitters[slot];
			emitter.InUse = false;
			emitter.Generation = 0;
			emitter.First = -1;
			emitter.Live = 0;
			emitter.Child = -1;
			emitter.Next = slot + 1 < EmitterSlots ? slot + 1 : -1;
			emitter.Prev = slot - 1;
		}
		for ( var slot = 0; slot < effectors.Length; slot++ )
		{
			var effector = effectors[slot];
			effector.InUse = false;
			effector.Generation = 0;
			effector.Next = slot + 1 < EffectorSlots ? slot + 1 : -1;
			effector.Prev = slot - 1;
		}
		activeEmitter = activeEffector = -1;
		freeEmitter = freeEffector = 0;
		liveEmitters = 0;
		LiveEffectors = 0;
		ResetParticlePool();
		generationCounter = effectorGenerationCounter = 1;
		random = 1;
		tickCounter = 0;
		accumulator = 0;
		TicksRun = 0;
	}

	private void ResetParticlePool()
	{
		for ( var index = 0; index < particles.Length; index++ )
		{
			particles[index].Next = index + 1;
			particles[index].Prev = index - 1;
			particles[index].State = 0;
		}
		particles[^1].Next = -1;
		freeParticle = 0;
		LiveParticles = 0;
	}

	// [BIN:TPW-EXE:0x5205D1 private generator] state = state * 214013 + 2531011 (the lea chain), the value is the state shifted right arithmetically by 16
	private int Step()
	{
		random = unchecked( random * 214013u + 2531011u );
		return unchecked( (int)random ) >> 16;
	}

	/// <summary>The signed form <c>sar(state, 16) % n</c> (tp.exe 0x5205F2: sar, cdq, idiv): -(|n| - 1) .. |n| - 1, zero twice as likely; a draw is consumed only when n is not zero.</summary>
	private int Signed( int n ) => n == 0 ? 0 : Step() % n;

	/// <summary>The unsigned form <c>(state &gt;&gt; 16 &amp; 0x7FFF) % n</c> (tp.exe 0x521850): 0 .. |n| - 1; a draw is consumed only when n is not zero.</summary>
	private int Unsigned( int n ) => n == 0 ? 0 : (Step() & 0x7FFF) % n;

	/// <summary>Truncating division by 1024 the way the native code does it (add 0x3FF to negative values, then sar 10).</summary>
	private static int Trunc1024( int value ) => (value + ((value >> 31) & 0x3FF)) >> 10;

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

	// ---- handles ----

	private Emitter? Resolve( int handle )
	{
		// [BIN:TPW-EXE:0x5222E0 handle check] non-zero, slot below 120, generation equal to the slot's; a freed slot's generation is 0
		var slot = handle & 0xFFFF;
		if ( handle == 0 || slot >= EmitterSlots )
			return null;
		var emitter = emitters[slot];
		return emitter.InUse && emitter.Generation == (int)((uint)handle >> 16) ? emitter : null;
	}

	private Effector? ResolveEffector( int handle )
	{
		// [BIN:TPW-EXE:0x522320 effector handle check] slot below 20 and the generation equal to the slot's (freed effector slots keep theirs)
		var slot = handle & 0xFFFF;
		if ( slot >= EffectorSlots )
			return null;
		var effector = effectors[slot];
		return effector.Generation == (int)((uint)handle >> 16) ? effector : null;
	}

	/// <summary>Moves a live emitter to a native position (1/1024 original unit, Y up), and its following child with it; false for a dead handle.</summary>
	// [BIN:TPW-EXE:0x51FE30 emitter move] the bump module moves a vehicle's smoke emitter to its sparks node every tick; the position is stored like the constructor's (>> 4)
	public bool Move( int handle, (int X, int Y, int Z) nativePosition )
	{
		if ( Resolve( handle ) is not { } emitter )
			return false;
		emitter.X = nativePosition.X >> 4;
		emitter.Y = nativePosition.Y >> 4;
		emitter.Z = nativePosition.Z >> 4;
		if ( emitter.Child >= 0 && emitter.Template.ChildFollows )
			Move( emitter.Child, nativePosition );
		return true;
	}

	/// <summary>
	/// Replaces the spawn volume of a live emitter (the box or disc extent the template carries), from the half-sizes the caller computed; the burst a create ran
	/// before this call used the template's. False for a dead handle.
	/// </summary>
	// [BIN:TPW-EXE:0x520030 set volume extent] the handle is checked (non-zero, slot below 0x78, generation), then the slot's +0x44/+0x48/+0x4C = the three arguments >> 4; 0x558e7c passes the footprint's width * 512, height * 256 and depth * 512
	public bool SetVolumeExtent( int handle, int x, int y, int z )
	{
		if ( Resolve( handle ) is not { } emitter )
			return false;
		emitter.ExtentX = x >> 4;
		emitter.ExtentY = y >> 4;
		emitter.ExtentZ = z >> 4;
		return true;
	}

	/// <summary>Turns a live emitter's particle velocity to a native direction (1024 = unit), times the template's directed speed; false for a dead handle.</summary>
	// [BIN:TPW-EXE:0x51FD90 emitter direction] the handle is checked, then E+0x38/+0x3C/+0x40 = (d * E+0xB0 + (sign & 0x3FF)) >> 10, as the type-2 create does
	public bool SetDirection( int handle, (int X, int Y, int Z) direction )
	{
		if ( Resolve( handle ) is not { } emitter )
			return false;
		var speed = emitter.Template.DirectedSpeed;
		emitter.BaseX = Trunc1024( unchecked( direction.X * speed ) );
		emitter.BaseY = Trunc1024( unchecked( direction.Y * speed ) );
		emitter.BaseZ = Trunc1024( unchecked( direction.Z * speed ) );
		return true;
	}

	public bool IsAlive( int handle ) => Resolve( handle ) != null;

	/// <summary>The template id of every live emitter, in slot order (diagnostics and tests).</summary>
	public IEnumerable<int> LiveEffects()
	{
		var result = new List<int>();
		foreach ( var emitter in emitters )
			if ( emitter.InUse )
				result.Add( emitter.EffectId );
		return result;
	}

	/// <summary>RSE KILL / KILLOBJ: ends an emitter without its death effect (life -2); its particles run out.</summary>
	// [BIN:TPW-EXE:0x51FF70 set life] the handle is checked, then E+0x20 = -2; KILLOBJ 0x5522C5 calls it for types 1 and 2
	public bool Kill( int handle )
	{
		if ( Resolve( handle ) is not { } emitter )
			return false;
		emitter.Life = -2;
		return true;
	}

	/// <summary>Sets the draw-time control of a live emitter (permille; tp.exe 0x51FFD0 writes E+0xA2; the dispatcher calls it when the control is not 1000).</summary>
	public bool SetControl( int handle, int control )
	{
		if ( Resolve( handle ) is not { } emitter )
			return false;
		emitter.Control = (short)control;
		return true;
	}

	// ---- create ----

	/// <summary>Type-1 effect: an emitter at a native position (1/1024 original unit, Y up). Returns slot | generation &lt;&lt; 16, or -1 when refused.</summary>
	// [BIN:TPW-EXE:0x521E60 create type 1] ids outside 0..0x68 and low-detail templates are refused; the slot comes from the free list head, the generation from the global counter
	public int Create( int effectId, (int X, int Y, int Z) nativePosition, int? control = null )
	{
		var handle = CreateEmitter( effectId, nativePosition.X, nativePosition.Y, nativePosition.Z, null );
		if ( control is { } value && value != 1000 )
			SetControl( handle, value );
		return handle;
	}

	/// <summary>Type-2 effect: as <see cref="Create"/>, with a direction (unit vector * 1024) that sets the base particle velocity and no emitter-velocity draws.</summary>
	// [BIN:TPW-EXE:0x521930 create type 2] base velocity = direction * template[+0xB0] / 1024 truncated toward zero; there is no emitter velocity jitter
	public int CreateDirected( int effectId, (int X, int Y, int Z) nativePosition, (int X, int Y, int Z) direction, int? control = null )
	{
		var handle = CreateEmitter( effectId, nativePosition.X, nativePosition.Y, nativePosition.Z, direction );
		if ( control is { } value && value != 1000 )
			SetControl( handle, value );
		return handle;
	}

	private int CreateEmitter( int effectId, int x, int y, int z, (int X, int Y, int Z)? direction )
	{
		if ( effectId < 0 || effectId >= EffectIdLimit || effectId >= templates.Count )
			return -1;
		var template = templates[effectId];
		// the native code returns 0 here, which is no valid handle either
		if ( LowDetail && template.RefusedInLowDetail )
			return -1;
		if ( freeEmitter < 0 )
			return -1;

		var slot = freeEmitter;
		var emitter = emitters[slot];
		freeEmitter = emitter.Next;
		if ( freeEmitter >= 0 )
			emitters[freeEmitter].Prev = -1;
		emitter.Next = activeEmitter;
		emitter.Prev = -1;
		if ( activeEmitter >= 0 )
			emitters[activeEmitter].Prev = slot;
		activeEmitter = slot;
		liveEmitters++;

		// the slot is a full copy of the template: the fields the tick changes are copied here
		emitter.InUse = true;
		emitter.Template = template;
		emitter.EffectId = effectId;
		emitter.Hold = template.Hold;
		emitter.Hidden = false;
		ScaleDensity( emitter, template );
		emitter.Life = template.EmitterLife;
		emitter.InitialLife = emitter.Life;
		// the dispatcher passes position * 1024; the slot keeps it >> 4, 1/64 original unit
		emitter.X = x >> 4;
		emitter.Y = y >> 4;
		emitter.Z = z >> 4;
		ResetBounds( emitter );
		emitter.Live = 0;
		emitter.First = -1;
		var velocity = template.EmitterVelocity;
		emitter.VelocityX = velocity.X;
		emitter.VelocityY = velocity.Y;
		emitter.VelocityZ = velocity.Z;
		var baseVelocity = template.ParticleVelocity;
		emitter.BaseX = baseVelocity.X;
		emitter.BaseY = baseVelocity.Y;
		emitter.BaseZ = baseVelocity.Z;
		emitter.SpiralAngle = template.SpiralPhase;
		emitter.RateCounter = template.RateCounter;
		emitter.Control = template.Control;
		// the slot is a full copy of the template, volume extent included (+0x44): SetVolumeExtent may overwrite it after the burst
		(emitter.ExtentX, emitter.ExtentY, emitter.ExtentZ) = template.VolumeExtent;
		emitter.DeathEffect = template.DeathEffect;
		emitter.Child = -1;
		emitter.FollowedByParent = false;
		if ( direction is { } unit )
		{
			// [BIN:TPW-EXE:0x521B3C type 2 velocity] (direction * speed + (sign & 0x3FF)) >> 10
			var speed = template.DirectedSpeed;
			emitter.BaseX = Trunc1024( unchecked( unit.X * speed ) );
			emitter.BaseY = Trunc1024( unchecked( unit.Y * speed ) );
			emitter.BaseZ = Trunc1024( unchecked( unit.Z * speed ) );
		}
		else
		{
			// [BIN:TPW-EXE:0x522034 type 1 emitter velocity] + sar(state) % j per axis with j = word +0x12: 92 shipped templates have j = -1, which adds 0 but draws
			var jitter = template.EmitterVelocityJitter;
			emitter.VelocityX += Signed( jitter );
			emitter.VelocityY += Signed( jitter );
			emitter.VelocityZ += Signed( jitter );
		}
		emitter.Generation = generationCounter;
		generationCounter = (generationCounter + 1) & 0xFFFF;
		var acceleration = template.RandomAcceleration;
		emitter.RandomAcceleration = (acceleration.X | acceleration.Y | acceleration.Z) != 0;

		// [BIN:TPW-EXE:0x522144 burst] spawn is called directly: there is no cap check, only pool exhaustion stops it (a refused spawn draws nothing, so stopping early is the same)
		// the ring emitter (+0xE) is tp.exe 0x5224F0; no shipped template uses it, so a template that asks for it starts no burst
		if ( template.RingMode == 0 )
			for ( var index = 0; index < emitter.Burst; index++ )
				if ( !Spawn( slot, emitter ) )
					break;

		// [BIN:TPW-EXE:0x52216E child] +0xB4 (int); an effector child (+0xBA) or an emitter child that is not the template's own id; +0xB8 starts it at parent + its +0x2C / +0x18 vector * 16 and makes it follow
		var childId = template.ChildEffect;
		var handle = (emitter.Generation << 16) | slot;
		if ( childId >= 0 )
		{
			if ( template.ChildIsEffector )
			{
				if ( template.ChildFollows && childId < effectorTemplates.Count )
				{
					var offset = effectorTemplates[childId].Velocity;
					x += offset.X << 4;
					y += offset.Y << 4;
					z += offset.Z << 4;
				}
				emitter.Child = CreateEffector( childId, x, y, z );
				// the native code indexes the effector table with the whole handle here (an unmasked generation); the intent is the slot
				if ( emitter.Child >= 0 && template.ChildFollows && ResolveEffector( emitter.Child ) is { } childEffector )
					childEffector.Followed = true;
				return handle;
			}
			if ( template.ChildFollows && childId < templates.Count )
			{
				var offset = templates[childId].EmitterVelocity;
				x += offset.X << 4;
				y += offset.Y << 4;
				z += offset.Z << 4;
			}
			emitter.Child = childId == effectId ? -1 : CreateEmitter( childId, x, y, z, null );
			if ( emitter.Child >= 0 && template.ChildFollows && Resolve( emitter.Child ) is { } child )
				child.FollowedByParent = true;
		}
		return handle;
	}

	// [BIN:TPW-EXE:0x521D60 density scale] every non-zero rate byte -> (density * rate) >> 10 clamped to at most 127 and at least 1 (negative rates scaled the same way); cap (word) and burst only when > 0, at least 1
	private void ScaleDensity( Emitter emitter, ParticleTemplate template )
	{
		var scale = !template.IgnoresDensity;
		for ( var index = 0; index < 4; index++ )
		{
			var rate = template.Rate( index );
			if ( scale && rate != 0 )
			{
				var value = unchecked( (Density * rate) >> 10 );
				if ( value > 0x7F )
					value = 0x7F;
				if ( value == 0 )
					value = 1;
				rate = (sbyte)value;
			}
			emitter.Rates[index] = rate;
		}
		var cap = template.Cap;
		if ( scale && cap > 0 )
		{
			cap = (short)unchecked( (Density * cap) >> 10 );
			if ( cap == 0 )
				cap = 1;
		}
		emitter.Cap = cap;
		var burst = template.Burst;
		if ( scale && burst > 0 )
		{
			burst = unchecked( (Density * burst) >> 10 );
			if ( burst == 0 )
				burst = 1;
		}
		emitter.Burst = burst;
	}

	private static void ResetBounds( Emitter emitter )
	{
		emitter.MinX = emitter.MinY = emitter.MinZ = int.MaxValue;
		emitter.MaxX = emitter.MaxY = emitter.MaxZ = int.MinValue;
	}

	/// <summary>Starts an effector (a group-targeted attractor with an optional kill radius) at a native position (1/1024 unit); returns slot | generation &lt;&lt; 16, or -1 when refused. The engine's own callers start them through templates' child and death effects.</summary>
	public int CreateEffector( int effectorId, (int X, int Y, int Z) nativePosition ) => CreateEffector( effectorId, nativePosition.X, nativePosition.Y, nativePosition.Z );

	// [BIN:TPW-EXE:0x522360 effector create] id 0..19, low-detail templates refused; slot from the free list, generation from its own counter, position >> 4
	private int CreateEffector( int effectorId, int x, int y, int z )
	{
		if ( effectorId < 0 || effectorId >= EffectorSlots || effectorId >= effectorTemplates.Count )
			return -1;
		var template = effectorTemplates[effectorId];
		if ( LowDetail && template.RefusedInLowDetail )
			return -1;
		if ( freeEffector < 0 )
			return -1;
		var slot = freeEffector;
		var effector = effectors[slot];
		freeEffector = effector.Next;
		if ( freeEffector >= 0 )
			effectors[freeEffector].Prev = -1;
		effector.Next = activeEffector;
		effector.Prev = -1;
		if ( activeEffector >= 0 )
			effectors[activeEffector].Prev = slot;
		activeEffector = slot;
		LiveEffectors++;

		effector.InUse = true;
		effector.Template = template;
		effector.EffectorId = effectorId;
		effector.Group = template.Group;
		var velocity = template.Velocity;
		effector.VelocityX = velocity.X;
		effector.VelocityY = velocity.Y;
		effector.VelocityZ = velocity.Z;
		effector.Radius = template.Radius;
		effector.DeathEffect = template.DeathEffect;
		effector.DeathEffectIsEffector = template.DeathEffectIsEffector;
		effector.Life = template.Life;
		effector.X = x >> 4;
		effector.Y = y >> 4;
		effector.Z = z >> 4;
		effector.Generation = effectorGenerationCounter;
		effectorGenerationCounter = (effectorGenerationCounter + 1) & 0xFFFF;
		effector.Followed = false;
		return (effector.Generation << 16) | slot;
	}

	// ---- free ----

	// [BIN:TPW-EXE:0x5204C0 free emitter] unlinks from the active list, clears the in-use word and the generation, pushes on the free list head (LIFO)
	private void FreeEmitter( int slot )
	{
		var emitter = emitters[slot];
		if ( activeEmitter == slot )
			activeEmitter = emitter.Next;
		if ( emitter.Prev >= 0 )
			emitters[emitter.Prev].Next = emitter.Next;
		if ( emitter.Next >= 0 )
			emitters[emitter.Next].Prev = emitter.Prev;
		emitter.InUse = false;
		emitter.Next = freeEmitter;
		emitter.Prev = -1;
		emitter.Generation = 0;
		freeEmitter = slot;
		liveEmitters--;
	}

	private void FreeEffector( int slot )
	{
		var effector = effectors[slot];
		if ( activeEffector == slot )
			activeEffector = effector.Next;
		if ( effector.Prev >= 0 )
			effectors[effector.Prev].Next = effector.Next;
		if ( effector.Next >= 0 )
			effectors[effector.Next].Prev = effector.Prev;
		effector.InUse = false;
		effector.Next = freeEffector;
		effector.Prev = -1;
		freeEffector = slot;
		LiveEffectors--;
	}

	// [BIN:TPW-EXE:0x520D60 particle alloc] pops the free list head and pushes it on the emitter's list head (newest first)
	private int AllocateParticle( Emitter emitter )
	{
		if ( freeParticle < 0 )
			return -1;
		var index = freeParticle;
		freeParticle = particles[index].Next;
		if ( freeParticle >= 0 )
			particles[freeParticle].Prev = -1;
		var head = emitter.First;
		emitter.First = index;
		particles[index].Next = head;
		particles[index].Prev = -1;
		if ( head >= 0 )
			particles[head].Prev = index;
		return index;
	}

	// [BIN:TPW-EXE:0x521890 particle free] unlinks from the emitter's list, pushes on the free list head (LIFO)
	private void FreeParticle( Emitter emitter, int index )
	{
		ref var particle = ref particles[index];
		if ( emitter.First == index )
			emitter.First = particle.Next;
		if ( particle.Prev >= 0 )
			particles[particle.Prev].Next = particle.Next;
		if ( particle.Next >= 0 )
			particles[particle.Next].Prev = particle.Prev;
		particle.State = 0;
		particle.Next = freeParticle;
		particle.Prev = -1;
		freeParticle = index;
		LiveParticles--;
	}

	// ---- spawn ----

	/// <summary>One particle of an emitter (tp.exe 0x520560). Returns false when the pool is exhausted, before anything is drawn.</summary>
	private bool Spawn( int slot, Emitter emitter )
	{
		var index = AllocateParticle( emitter );
		if ( index < 0 )
			return false;
		var template = emitter.Template;
		emitter.Live++;
		LiveParticles++;
		ref var particle = ref particles[index];
		particle.State = 2;
		particle.Emitter = (short)slot;
		particle.Size = (short)template.SizeAtBirth;
		particle.Group = (short)template.Group;

		// life = L + sar(state) % (L >> 2): about +-L/4 exclusive, no minimum; a life of 0 keeps life 0 but a maximum of 1
		var life = template.ParticleLife;
		life += Signed( life >> 2 );
		particle.Frame = 0;
		particle.Life = life;
		particle.MaxLife = life == 0 ? 1 : life;
		particle.Rotation = template.RandomRotation ? (ushort)(Step() & 0x7FFF) : (ushort)0;
		particle.Colour = template.ColourMode != 0 ? template.Colour( Step() & 0x7FFF & 0xF ) : template.Colour( 15 );

		int px, py, pz;
		var extent = (X: emitter.ExtentX, Y: emitter.ExtentY, Z: emitter.ExtentZ);
		if ( (extent.X | extent.Y | extent.Z) == 0 )
		{
			px = emitter.X;
			py = emitter.Y;
			pz = emitter.Z;
		}
		else
		{
			int vx, vy, vz;
			if ( template.VolumeShape != 0 )
			{
				// box: x and z signed draws, y signed or (word +0xA0) unsigned
				vx = Signed( extent.X );
				vy = template.VolumeUnsignedY ? Unsigned( extent.Y ) : Signed( extent.Y );
				vz = Signed( extent.Z );
			}
			else
			{
				// horizontal disc: a 12-bit angle (the table index is angle >> 3); the ring (word +0xA8) uses the extents as they are
				var angle = (Step() & 0xFFF) >> 3;
				var sin = Sine[angle & 0x1FF];
				var cos = Sine[(angle + 0x80) & 0x1FF];
				if ( template.VolumeRing )
				{
					vx = unchecked( (cos * extent.X) << 2 ) >> 10;
					vy = template.VolumeUnsignedY ? Unsigned( extent.Y ) : Signed( extent.Y );
					vz = -(unchecked( (sin * extent.Z) << 2 ) >> 10);
				}
				else
				{
					var radius = Unsigned( extent.X );
					vx = unchecked( (cos * radius) << 2 ) >> 10;
					vy = template.VolumeUnsignedY ? Unsigned( extent.Y ) : Signed( extent.Y );
					var depth = Signed( extent.Z );
					vz = -(unchecked( (sin * depth) << 2 ) >> 10);
				}
			}
			var rotation = template.VolumeRotation;
			if ( rotation != 0 )
			{
				// rotated about Y by rotation / 4096 turn
				var turn = (rotation + ((rotation >> 31) & 7)) >> 3;
				var s = Sine[turn & 0x1FF] << 2;
				var c = Sine[(turn + 0x80) & 0x1FF] << 2;
				var rotatedX = unchecked( c * vx - s * vz ) >> 10;
				var rotatedZ = unchecked( c * vz + s * vx ) >> 10;
				vx = rotatedX;
				vz = rotatedZ;
			}
			px = vx + emitter.X;
			py = vy + emitter.Y;
			pz = vz + emitter.Z;
		}
		if ( template.SpiralRadius != 0 )
		{
			var turn = (emitter.SpiralAngle + ((emitter.SpiralAngle >> 31) & 7)) >> 3;
			var radius = template.SpiralRadius;
			px += unchecked( (Sine[turn & 0x1FF] * radius) << 2 ) >> 10;
			pz += unchecked( (Sine[(turn + 0x80) & 0x1FF] * radius) << 2 ) >> 10;
		}
		particle.X = px;
		particle.Y = py;
		particle.Z = pz;

		var radial = template.RadialSpeed;
		if ( radial != 0 )
		{
			// horizontal burst replacing the base velocity: x = cos * (u % R), z = sin * (u % R), y = sar % R
			var angle = (Step() & 0xFFF) >> 3;
			var spread = Unsigned( radial );
			particle.VelocityX = unchecked( (Sine[(angle + 0x80) & 0x1FF] * spread) << 2 ) >> 10;
			particle.VelocityY = Signed( radial );
			particle.VelocityZ = unchecked( (Sine[angle & 0x1FF] * spread) << 2 ) >> 10;
		}
		else
		{
			var jitter = template.VelocityJitter;
			particle.VelocityX = emitter.BaseX + Signed( jitter );
			particle.VelocityY = emitter.BaseY + Signed( jitter );
			particle.VelocityZ = emitter.BaseZ + Signed( jitter );
		}
		if ( template.AddsEmitterVelocity )
		{
			particle.VelocityX += emitter.VelocityX;
			particle.VelocityY += emitter.VelocityY;
			particle.VelocityZ += emitter.VelocityZ;
		}
		return true;
	}

	// ---- tick ----

	/// <summary>One 31 ms tick: every live emitter newest first, then the effectors, then every particle. The very first call does nothing.</summary>
	public void Tick()
	{
		tickCounter++;
		if ( tickCounter <= 1 )
			return;
		TicksRun++;
		for ( var slot = activeEmitter; slot >= 0; )
		{
			var emitter = emitters[slot];
			var next = emitter.Next;
			if ( emitter.Hold == 0 )
				TickEmitter( slot, emitter );
			slot = next;
		}
		TickEffectors();
		TickParticles();
	}

	private void TickEmitter( int slot, Emitter emitter )
	{
		var template = emitter.Template;
		if ( !template.Immortal )
			emitter.Life--;
		ResetBounds( emitter );
		if ( emitter.Life < 0 )
		{
			// dead: the death effect starts at life exactly -1 and is then cleared; the slot is freed once the last particle is gone; no follow, motion or emission
			if ( emitter.DeathEffect >= 0 && emitter.Life == -1 )
			{
				if ( template.DeathEffectIsEffector )
					CreateEffector( emitter.DeathEffect, emitter.X << 4, emitter.Y << 4, emitter.Z << 4 );
				else
					CreateEmitter( emitter.DeathEffect, emitter.X << 4, emitter.Y << 4, emitter.Z << 4, null );
				emitter.DeathEffect = -1;
			}
			// [BIN:TPW-EXE:0x520237 +0xBC] the child's life becomes -1 (every tick while the parent is dead); a child that already ticked this tick goes to -2 on its next decrement
			if ( template.KillsChildWithEmitter && emitter.Child >= 0 )
			{
				if ( template.ChildIsEffector )
				{
					if ( ResolveEffector( emitter.Child ) is { } childEffector )
						childEffector.Life = -1;
				}
				else if ( Resolve( emitter.Child ) is { } child )
					child.Life = -1;
			}
			if ( emitter.Live == 0 )
				FreeEmitter( slot );
			return;
		}

		if ( template.ChildFollows && emitter.Child >= 0 )
		{
			if ( template.ChildIsEffector )
			{
				if ( ResolveEffector( emitter.Child ) is { } childEffector )
				{
					childEffector.X = emitter.X;
					childEffector.Y = emitter.Y;
					childEffector.Z = emitter.Z;
				}
			}
			else if ( Resolve( emitter.Child ) is { } child )
			{
				child.X = emitter.X;
				child.Y = emitter.Y;
				child.Z = emitter.Z;
			}
		}
		if ( template.SpiralRate != 0 )
			emitter.SpiralAngle = (emitter.SpiralAngle + template.SpiralRate) & 0xFFF;
		if ( !emitter.FollowedByParent && ((emitter.VelocityX | emitter.VelocityY | emitter.VelocityZ) != 0 || template.EmitterGravity != 0) )
			MoveEmitter( emitter );
		if ( template.RingMode != 0 )
			return;

		// the rate quarter comes from the remaining life (immortal emitters stay on +0x68); the cap is checked once per tick
		var rate = SelectRate( emitter );
		if ( emitter.Cap > emitter.Live )
		{
			if ( rate >= 0 )
			{
				for ( var index = 0; index < rate; index++ )
					if ( !Spawn( slot, emitter ) )
						break;
			}
			else
			{
				emitter.RateCounter = (short)(emitter.RateCounter - 1);
				if ( emitter.RateCounter <= 0 )
				{
					Spawn( slot, emitter );
					emitter.RateCounter = (short)-rate;
				}
			}
		}
	}

	// [BIN:TPW-EXE:0x520470 rate select] (life << 2) / initial life: 0 -> +0x6B, 1 -> +0x6A, 2 -> +0x69, otherwise +0x68
	private static int SelectRate( Emitter emitter )
	{
		if ( emitter.InitialLife == 0 )
			return emitter.Rates[0];
		var quarter = unchecked( emitter.Life << 2 ) / emitter.InitialLife;
		return quarter switch
		{
			0 => emitter.Rates[3],
			1 => emitter.Rates[2],
			2 => emitter.Rates[1],
			_ => emitter.Rates[0]
		};
	}

	/// <summary>The ground function the tick uses: native 1/1024 unit in and out; the original passes the stub 0x5D60C0 that returns 0.</summary>
	private int GroundHeight( int x, int z ) => ground?.Invoke( x, z ) ?? 0;

	// [BIN:TPW-EXE:0x520E00 emitter motion] v -= trunc(v * drag / 1024), vy -= word +0x28, pos += v; the ground branch runs only for word +0x76 != 0
	private void MoveEmitter( Emitter emitter )
	{
		var template = emitter.Template;
		var drag = template.EmitterDrag;
		var vx = emitter.VelocityX;
		var vy = emitter.VelocityY;
		var vz = emitter.VelocityZ;
		vx -= Trunc1024( unchecked( vx * drag ) );
		vy -= Trunc1024( unchecked( vy * drag ) );
		vy -= template.EmitterGravity;
		vz -= Trunc1024( unchecked( vz * drag ) );
		emitter.VelocityX = vx;
		emitter.VelocityY = vy;
		emitter.VelocityZ = vz;
		emitter.X += vx;
		emitter.Y += vy;
		emitter.Z += vz;
		if ( template.GroundBounce == 0 )
			return;
		var floor = GroundHeight( emitter.X << 4, emitter.Z << 4 ) >> 4;
		if ( emitter.Y >= floor )
			return;
		if ( emitter.Y - floor < emitter.VelocityY )
		{
			// went through the floor faster than it fell: undo the step, damp every axis by half
			emitter.X -= emitter.VelocityX;
			emitter.Y -= emitter.VelocityY;
			emitter.VelocityX = -(emitter.VelocityX / 2);
			emitter.VelocityY /= 2;
			emitter.VelocityZ = -(emitter.VelocityZ / 2);
		}
		else
		{
			emitter.Y = floor;
			emitter.VelocityY = -(emitter.VelocityY / 2);
			emitter.VelocityX /= 2;
			emitter.VelocityZ /= 2;
		}
	}

	// [BIN:TPW-EXE:0x520F10 effector tick] life, motion (drag, gravity, bounce), then death (the death effect starts and the slot is freed) or attraction
	private void TickEffectors()
	{
		for ( var slot = activeEffector; slot >= 0; )
		{
			var effector = effectors[slot];
			var next = effector.Next;
			var template = effector.Template;
			if ( !template.Immortal )
				effector.Life--;
			if ( effector.Radius == 0 )
				effector.Radius = 1;
			if ( !effector.Followed )
				MoveEffector( effector );
			if ( effector.Life < 0 )
			{
				if ( effector.DeathEffectIsEffector )
					CreateEffector( effector.DeathEffect, effector.X << 4, effector.Y << 4, effector.Z << 4 );
				else
					CreateEmitter( effector.DeathEffect, effector.X << 4, effector.Y << 4, effector.Z << 4, null );
				FreeEffector( slot );
			}
			else
				Attract( effector );
			slot = next;
		}
	}

	private void MoveEffector( Effector effector )
	{
		var template = effector.Template;
		var gravity = template.Gravity;
		if ( (effector.VelocityX | effector.VelocityY | effector.VelocityZ) == 0 && gravity == 0 )
			return;
		var drag = template.Drag;
		var vx = effector.VelocityX;
		var vy = effector.VelocityY;
		var vz = effector.VelocityZ;
		vx -= Trunc1024( unchecked( vx * drag ) );
		vy -= Trunc1024( unchecked( vy * drag ) );
		vy -= gravity;
		vz -= Trunc1024( unchecked( vz * drag ) );
		effector.VelocityX = vx;
		effector.VelocityY = vy;
		effector.VelocityZ = vz;
		effector.X += vx;
		effector.Y += vy;
		effector.Z += vz;
		if ( !template.GroundBounce )
			return;
		var floor = GroundHeight( effector.X << 4, effector.Z << 4 ) >> 4;
		if ( effector.Y >= floor )
			return;
		if ( effector.Y - floor < effector.VelocityY )
		{
			// the side branch damps by a quarter
			effector.X -= effector.VelocityX;
			effector.Y -= effector.VelocityY;
			effector.VelocityX = -(effector.VelocityX / 4);
			effector.VelocityY /= 4;
			effector.VelocityZ = -(effector.VelocityZ / 4);
		}
		else
		{
			effector.Y = floor;
			effector.VelocityY = -(effector.VelocityY / 2);
			effector.VelocityX /= 2;
			effector.VelocityZ /= 2;
		}
	}

	// [BIN:TPW-EXE:0x52124E effector attraction] target = position + sar(state) % jitter per axis (three draws at most, once per tick); for every particle of a matching group:
	// d = (target - p) / 4, q = (d . d >> 8) + 10, v += strength * d / q; mode 2 sets the life of a particle with q < radius^2 >> 8 to 0
	private void Attract( Effector effector )
	{
		var template = effector.Template;
		var jitter = template.TargetJitter;
		var targetX = effector.X + Signed( jitter.X );
		var targetY = effector.Y + Signed( jitter.Y );
		var targetZ = effector.Z + Signed( jitter.Z );
		var strength = template.Strength;
		var group = effector.Group;
		var killLimit = unchecked( effector.Radius * effector.Radius ) >> 8;
		for ( var slot = activeEmitter; slot >= 0; slot = emitters[slot].Next )
		{
			var emitter = emitters[slot];
			if ( emitter.Hold != 0 || (group != -1 && emitter.Template.Group != group) )
				continue;
			for ( var index = emitter.First; index >= 0; index = particles[index].Next )
			{
				ref var particle = ref particles[index];
				if ( group >= 0 ? particle.Group != group : emitter.Template.IgnoredByAllGroupEffectors )
					continue;
				var dx = (targetX - particle.X) / 4;
				var dy = (targetY - particle.Y) / 4;
				var dz = (targetZ - particle.Z) / 4;
				var q = (unchecked( dx * dx + dy * dy + dz * dz ) >> 8) + 10;
				if ( q == 0 )
					continue;
				particle.VelocityX += unchecked( strength * dx ) / q;
				particle.VelocityY += unchecked( strength * dy ) / q;
				particle.VelocityZ += unchecked( strength * dz ) / q;
				if ( template.Mode == 2 && q < killLimit )
					particle.Life = 0;
			}
		}
	}

	// [BIN:TPW-EXE:0x5214A0 particle update] per emitter newest first, particles in list order
	private void TickParticles()
	{
		for ( var slot = activeEmitter; slot >= 0; slot = emitters[slot].Next )
		{
			var emitter = emitters[slot];
			if ( emitter.Hold != 0 )
				continue;
			var template = emitter.Template;
			for ( var index = emitter.First; index >= 0; )
			{
				ref var particle = ref particles[index];
				var next = particle.Next;
				particle.Life--;
				if ( particle.Life < 0 )
				{
					// the particle's death effect starts where it died, before the particle is freed
					emitter.Live--;
					if ( template.ParticleDeathEffect >= 0 )
						CreateEmitter( template.ParticleDeathEffect, particle.X << 4, particle.Y << 4, particle.Z << 4, null );
					FreeParticle( emitter, index );
					index = next;
					continue;
				}
				UpdateParticle( emitter, template, ref particle );
				index = next;
			}
		}
	}

	private void UpdateParticle( Emitter emitter, ParticleTemplate template, ref Particle particle )
	{
		// a dead emitter kills its particles when the template asks for it
		if ( emitter.Life < 0 && template.ParticlesDieWithEmitter )
			particle.Life = -1;

		// colour: palette[life * 15 / max] when not random; an odd random mode flickers every tick, an even one keeps the spawn colour
		var mode = template.ColourMode;
		if ( mode != 0 )
		{
			if ( (mode & 1) != 0 )
				particle.Colour = template.Colour( Unsigned( 16 ) );
		}
		else
		{
			var colourIndex = template.ParticleLife > 0 ? unchecked( particle.Life * 15 ) / particle.MaxLife : 0;
			particle.Colour = template.Colour( Math.Clamp( colourIndex, 0, 15 ) );
		}

		// frame = (n - 1) - (n - 1) * life / max, kept to a word; out of range falls back to 0
		var frames = template.FrameCount;
		if ( frames != 0 )
		{
			var frame = frames;
			if ( particle.Life >= 0 )
				frame -= unchecked( (frames - 1) * particle.Life ) / particle.MaxLife;
			frame--;
			var word = (ushort)frame;
			particle.Frame = word >= frames ? (short)0 : (short)word;
		}
		else
			particle.Frame = 0;

		// size = (birth - death) * life / max + death
		var birth = template.SizeAtBirth;
		particle.Size = (short)(template.ParticleLife > 0
			? unchecked( (birth - template.SizeAtDeath) * particle.Life ) / particle.MaxLife + template.SizeAtDeath
			: birth);

		// spin step = BE, or BE + u % (C6 - BE) when they differ
		var spin = template.Spin;
		var spinMaximum = template.SpinMaximum;
		var step = spin == spinMaximum ? spin : Unsigned( spinMaximum - spin ) + spin;
		particle.Rotation = (ushort)(particle.Rotation + step);

		if ( emitter.RandomAcceleration )
		{
			var acceleration = template.RandomAcceleration;
			particle.VelocityX += Signed( acceleration.X );
			particle.VelocityY += Signed( acceleration.Y );
			particle.VelocityZ += Signed( acceleration.Z );
		}
		var drag = template.ParticleDrag;
		if ( drag != 0 )
		{
			particle.VelocityX -= Trunc1024( unchecked( drag * particle.VelocityX ) );
			particle.VelocityY -= Trunc1024( unchecked( drag * particle.VelocityY ) );
			particle.VelocityZ -= Trunc1024( unchecked( drag * particle.VelocityZ ) );
		}
		particle.VelocityY -= template.ParticleGravity;
		// the spawn tick (state 2) ages, colours and accelerates the particle like any other, only the move is skipped
		if ( particle.State == 2 )
			particle.State = 1;
		else
		{
			particle.Y += particle.VelocityY;
			particle.X += particle.VelocityX;
			particle.Z += particle.VelocityZ;
		}

		// the bounds grow by the particle's size >> 10 (native: not for screen-space emitters, whose bounds stay empty; OpenTPW keeps them for culling)
		var pad = Trunc1024( particle.Size );
		emitter.MinX = Math.Min( emitter.MinX, particle.X - pad );
		emitter.MinY = Math.Min( emitter.MinY, particle.Y - pad );
		emitter.MinZ = Math.Min( emitter.MinZ, particle.Z - pad );
		emitter.MaxX = Math.Max( emitter.MaxX, particle.X + pad );
		emitter.MaxY = Math.Max( emitter.MaxY, particle.Y + pad );
		emitter.MaxZ = Math.Max( emitter.MaxZ, particle.Z + pad );
	}

	/// <summary>The live emitters' bounds (1/64 original unit), for culling; a slot with no particles is omitted.</summary>
	public int CollectBounds( List<ParticleEmitterBounds> destination )
	{
		destination.Clear();
		for ( var slot = 0; slot < EmitterSlots; slot++ )
		{
			var emitter = emitters[slot];
			if ( emitter.InUse && emitter.Live > 0 && emitter.MinX <= emitter.MaxX )
				destination.Add( new ParticleEmitterBounds( slot, emitter.MinX, emitter.MinY, emitter.MinZ, emitter.MaxX, emitter.MaxY, emitter.MaxZ ) );
		}
		return destination.Count;
	}

	/// <summary>Writes every live particle, emitter slots 0..119 in ascending order and each slot's particles newest first (the native draw order; depth sorting is untraced), and returns the count.</summary>
	// [BIN:TPW-EXE:0x57C620 draw driver] slots in ascending order; [BIN:TPW-EXE:0x51EF30 draw list] position scaled by the control: drawn = emitter + (particle - emitter) * control / 1000
	public int Snapshot( List<ParticleSprite> destination )
	{
		destination.Clear();
		for ( var slot = 0; slot < EmitterSlots; slot++ )
		{
			var emitter = emitters[slot];
			if ( !emitter.InUse || emitter.Hidden )
				continue;
			var template = emitter.Template;
			var frames = Math.Max( 0, template.FrameCount );
			for ( var index = emitter.First; index >= 0; index = particles[index].Next )
			{
				ref readonly var particle = ref particles[index];
				destination.Add( new ParticleSprite
				{
					Emitter = slot,
					X = emitter.X + (int)((long)(particle.X - emitter.X) * emitter.Control / 1000),
					Y = emitter.Y + (int)((long)(particle.Y - emitter.Y) * emitter.Control / 1000),
					Z = emitter.Z + (int)((long)(particle.Z - emitter.Z) * emitter.Control / 1000),
					Size = particle.Size,
					Colour = particle.Colour,
					SpriteCode = template.SpriteCode,
					FrameCount = frames,
					Frame = particle.Frame,
					Spin = particle.Rotation,
					RenderFlags = template.RenderFlags,
					ScreenSpace = template.ScreenSpace
				} );
			}
		}
		return destination.Count;
	}
}
