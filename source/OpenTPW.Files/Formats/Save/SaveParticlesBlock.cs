using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>
/// One emitter slot of the saved particle pool: the 320-byte record tp.exe keeps at <c>0x80CEFC + 320 × slot</c>. The slot is a full copy of the effect template
/// (<c>0x521E60</c>, so the template offsets of docs/PARTICLES.md are the slot offsets) overwritten at creation by the runtime fields named here. Every byte is in
/// <see cref="Raw"/>; a field with no traced reader is reachable by offset through <see cref="Word(int)"/>.
/// </summary>
public sealed record SaveParticleEmitter
{
	public const int Bytes = 320;
	public const int NameOffset = 0x118, NameBytes = 16;

	public required int Slot { get; init; }
	public required byte[] Raw { get; init; }

	public byte Byte( int offset ) => Raw[offset];
	public ushort U16( int offset ) => BinaryPrimitives.ReadUInt16LittleEndian( Raw.AsSpan( offset ) );
	public short I16( int offset ) => BinaryPrimitives.ReadInt16LittleEndian( Raw.AsSpan( offset ) );
	public int Word( int offset ) => BinaryPrimitives.ReadInt32LittleEndian( Raw.AsSpan( offset ) );

	/// <summary>+0x00: 1 once <c>0x521E60</c> has created the emitter; 0 in a free slot.</summary>
	public bool InUse => U16( 0 ) != 0;
	/// <summary>+0x02: the emitter does not tick while this is not zero (the tick tests it first).</summary>
	public int Hold => U16( 2 );
	/// <summary>+0x04 (signed byte): the group the effectors match.</summary>
	public int Group => (sbyte)Raw[4];
	/// <summary>+0x05: a screen-space emitter (the post-load pass <c>0x5200B0</c> kills it).</summary>
	public bool ScreenSpace => Raw[5] != 0;
	/// <summary>+0x06: the first live particle of the emitter (the creator writes -1); the particle pool is not saved, so a loaded save has no particle behind it.</summary>
	public int FirstParticle => I16( 6 );
	/// <summary>+0x08: the live particle count, compared with the cap at +0x64.</summary>
	public int LiveParticles => U16( 8 );
	/// <summary>+0x0A: the handle generation (<c>0x80CEE8</c> at creation).</summary>
	public int Generation => U16( 0x0A );
	/// <summary>+0x0C: the effect template index (0 to 0x68).</summary>
	public int EffectId => U16( 0x0C );
	/// <summary>+0x0E: ring-emitter mode (0x5224F0).</summary>
	public int RingMode => U16( 0x0E );
	/// <summary>+0x10 (signed): the effect started when the life reaches -1, cleared to -1 after it starts.</summary>
	public int DeathEffect => I16( 0x10 );
	public (int X, int Y, int Z) Position => (Word( 0x14 ), Word( 0x18 ), Word( 0x1C ));
	/// <summary>+0x20: ticks left; negative once dead (-2 is "killed").</summary>
	public int Life => Word( 0x20 );
	public bool Immortal => U16( 0x26 ) != 0;
	/// <summary>+0x2A: set by the post-load pass for screen-space emitters.</summary>
	public int HiddenWord => U16( 0x2A );
	public (int X, int Y, int Z) Velocity => (Word( 0x2C ), Word( 0x30 ), Word( 0x34 ));
	public (int X, int Y, int Z) BaseVelocity => (Word( 0x38 ), Word( 0x3C ), Word( 0x40 ));
	public (int X, int Y, int Z) VolumeExtent => (Word( 0x44 ), Word( 0x48 ), Word( 0x4C ));
	public int SpiralAngle => Word( 0x54 );
	public int Burst => Word( 0x60 );
	public int Cap => I16( 0x64 );
	/// <summary>+0x66: the counter of a negative rate (decremented per tick).</summary>
	public int RateCounter => I16( 0x66 );
	/// <summary>+0x68 to +0x6B (signed bytes): the rates of the four quarters of the life.</summary>
	public IReadOnlyList<int> Rates => new[] { (int)(sbyte)Raw[0x68], (sbyte)Raw[0x69], (sbyte)Raw[0x6A], (sbyte)Raw[0x6B] };
	/// <summary>+0xA2: the draw-time spatial scale in permille.</summary>
	public int Control => I16( 0xA2 );
	/// <summary>+0xAB: set when the parent drives this child (it then skips its own motion).</summary>
	public bool FollowedByParent => Raw[0xAB] != 0;
	/// <summary>+0xB4: the child emitter or effector handle (-1 none); the template holds the child's effect index here.</summary>
	public int Child => Word( 0xB4 );
	public int NextSlot => I16( 0xD0 );
	public int PreviousSlot => I16( 0xD2 );
	/// <summary>+0xD4: the life at creation.</summary>
	public int InitialLife => Word( 0xD4 );
	/// <summary>+0x118: the effect's name (16 bytes, NUL padded), for example <c>WaterFall</c> or <c>Button</c>.</summary>
	public string Name
	{
		get
		{
			var span = Raw.AsSpan( NameOffset, NameBytes );
			var end = span.IndexOf( (byte)0 );
			return Encoding.ASCII.GetString( end < 0 ? span : span[..end] );
		}
	}
	/// <summary>+0x128 to +0x13F: the particle bounds (min xyz, max xyz); an extreme sentinel until the first particle.</summary>
	public int[] Bounds => Enumerable.Range( 0, 6 ).Select( index => Word( 0x128 + index * 4 ) ).ToArray();
}

/// <summary>One effector slot of the saved pool: the 104-byte record at <c>0x8164FC + 104 × slot</c> (a copy of the effector template made by <c>0x522360</c>).</summary>
public sealed record SaveParticleEffector
{
	public const int Bytes = 104;

	public required int Slot { get; init; }
	public required byte[] Raw { get; init; }

	public ushort U16( int offset ) => BinaryPrimitives.ReadUInt16LittleEndian( Raw.AsSpan( offset ) );
	public short I16( int offset ) => BinaryPrimitives.ReadInt16LittleEndian( Raw.AsSpan( offset ) );
	public int Word( int offset ) => BinaryPrimitives.ReadInt32LittleEndian( Raw.AsSpan( offset ) );

	/// <summary>+0x00: 1 once created.</summary>
	public bool InUse => Word( 0 ) != 0;
	public int Group => Word( 4 );
	/// <summary>+0x08: the handle generation (<c>0x80CEEA</c> at creation).</summary>
	public int Generation => U16( 8 );
	/// <summary>+0x0A: set when a parent emitter drives the effector.</summary>
	public bool Followed => Raw[0x0A] != 0;
	public (int X, int Y, int Z) Position => (Word( 0x0C ), Word( 0x10 ), Word( 0x14 ));
	public (int X, int Y, int Z) Velocity => (Word( 0x18 ), Word( 0x1C ), Word( 0x20 ));
	public int Drag => Word( 0x24 );
	public int Gravity => Word( 0x28 );
	public int Radius => Word( 0x2C );
	/// <summary>+0x30: 2 kills particles inside the radius.</summary>
	public int Mode => Word( 0x30 );
	public int Strength => Word( 0x40 );
	public bool KilledOnLoad => Raw[0x45] != 0;
	public int DeathEffect => I16( 0x46 );
	public bool DeathEffectIsEffector => Raw[0x49] != 0;
	public bool Immortal => U16( 0x4A ) != 0;
	public int Life => Word( 0x4C );
	public int NextSlot => I16( 0x60 );
	public int PreviousSlot => I16( 0x62 );
}

/// <summary>
/// The particle pool's globals (the first 0x2C bytes of the <c>0x9E68</c>-byte table at <c>0x80CED0</c>) and list heads (its last 0x1C bytes, <c>0x816D1C</c> to <c>0x816D38</c>).
/// Words the loader keeps from the running process (<see cref="SineTable"/>, <see cref="GroundFunction"/>) or replaces (<see cref="PoolPointer"/>, <see cref="LiveParticles"/>,
/// <see cref="FreeParticleHead"/>) are process values and carry no park state.
/// </summary>
public sealed record SaveParticleGlobals
{
	public const int HeaderBytes = 0x2C;
	public const int TailOffset = 0x9E4C;
	public const int TailBytes = 0x1C;

	/// <summary>+0x00: the live particle count; the loader sets it to 0 (<c>0x51F949</c>) when the saved pool is empty.</summary>
	public uint LiveParticles { get; init; }
	/// <summary>+0x04: the tick counter; the first tick after a reset only counts.</summary>
	public uint TickCounter { get; init; }
	/// <summary>+0x08: the low-detail flag (a template with +0xC1 set is refused while it is not zero).</summary>
	public uint LowDetail { get; init; }
	/// <summary>+0x0C: the particle pool capacity (the init clamps it to 100..0x7FFF; the game uses 0x800).</summary>
	public uint PoolCapacity { get; init; }
	/// <summary>+0x10: the pool's private generator (<c>state × 214013 + 2531011</c>).</summary>
	public uint Random { get; init; }
	/// <summary>+0x14: set to 1 by the reset and read by nothing.</summary>
	public uint ParticleCounter { get; init; }
	/// <summary>+0x18 (u16): the next emitter handle generation.</summary>
	public ushort EmitterGeneration { get; init; }
	/// <summary>+0x1A (u16): the next effector handle generation.</summary>
	public ushort EffectorGeneration { get; init; }
	/// <summary>+0x1C: the sine table pointer of the process; the loader keeps the running process's value.</summary>
	public uint SineTable { get; init; }
	/// <summary>+0x20: the density the pool was initialised with (the player's detail level: 500, 1000 or 1500); the loader takes the saved value.</summary>
	public uint Density { get; init; }
	/// <summary>+0x24: the ground function pointer (the stub 0x5D60C0 in the game); the loader keeps the running process's value.</summary>
	public uint GroundFunction { get; init; }
	/// <summary>+0x28: the pool pointer; the loader allocates a new pool.</summary>
	public uint PoolPointer { get; init; }

	// the list heads (signed slot numbers, -1 for none)
	public short ActiveEmitter { get; init; }
	public short ActiveEffector { get; init; }
	public short FreeEmitter { get; init; }
	public short FreeEffector { get; init; }
	/// <summary>0x816D24 (u16): the free list head of the particle pool; the loader resets it to 0.</summary>
	public ushort FreeParticleHead { get; init; }
	/// <summary>0x816D26 (u16) and 0x816D28 to 0x816D37: not read by any traced code.</summary>
	public byte[] TailRaw { get; init; } = Array.Empty<byte>();
}

/// <summary>
/// The Particles block (writer <c>0x51F680</c> (called at 0x41661E), loader <c>0x51F7A0</c>, 76,252 bytes in a save the game writes): <c>u32 'PCLT'</c> (the file holds <c>LCTP</c>), <c>u32 on</c> and, when
/// on, <c>u32 0x9E68</c> + the pool table (globals, 120 emitter slots of 320 bytes, 20 effector slots of 104 bytes, list heads), <c>u32 0x8B60</c> + the template table (105 emitter templates of
/// 320 bytes and 20 effector templates of 104 bytes, the game's in-memory copy of <c>Data/Particle/Tp2.plb</c> plus whatever the running game changed), and <c>u32 c</c> + <c>c</c>
/// bytes of the particle pool (52 bytes a particle). The game's save call (<c>0x41661B</c>) always writes c = 0. With c = 0 the loader rebuilds an empty pool; with c not zero it reads the pool
/// into the 52 × capacity bytes (32-bit wrapped) it allocates and skips the rebuild (<c>0x51F8FB</c>), so a larger c is rejected here (the original overruns its heap), as is a capacity of 0 (the rebuild
/// dereferences the NULL pool at <c>0x51F954</c>). A wrong magic or <c>on</c> = 0 is not a failure of the original load: the loader returns 0 (<c>0x51FA0C</c>, success to the caller at <c>0x41543F</c>) after
/// 4 or 8 bytes (<see cref="SaveBlockWidths"/>), which <see cref="MagicMatched"/> and <see cref="On"/> report.
/// </summary>
public sealed class SaveParticlesBlock
{
	public const int EmitterSlots = 120, EffectorSlots = 20, EmitterTemplates = 105, EffectorTemplates = 20, ParticleBytes = 52;
	public const int EmittersOffset = 0x2C;
	public const int EffectorsOffset = EmittersOffset + EmitterSlots * SaveParticleEmitter.Bytes;

	private SaveParticlesBlock() { }

	public int Offset { get; private init; }
	public int Length { get; private init; }
	/// <summary>The 'on' word: the particle system was initialised. A block with 0 ends after its 8 bytes.</summary>
	public bool On { get; private init; }
	/// <summary>Whether the first word is 'PCLT'. When it is not, the loader stops after that word and succeeds (4-byte block, <see cref="On"/> false).</summary>
	public bool MagicMatched { get; private init; } = true;
	/// <summary>Findings about the slot lists. The original loader checks none of them (no counterpart is proven for every writer), so they are reported, not refused; <see cref="SaveParticleState.ToSystemState"/> refuses a block that has any.</summary>
	public IReadOnlyList<string> ListProblems { get; private init; } = Array.Empty<string>();
	public SaveParticleGlobals Globals { get; private init; } = new();
	public IReadOnlyList<SaveParticleEmitter> Emitters { get; private init; } = Array.Empty<SaveParticleEmitter>();
	public IReadOnlyList<SaveParticleEffector> Effectors { get; private init; } = Array.Empty<SaveParticleEffector>();
	/// <summary>The 105 emitter templates as saved (320 bytes each).</summary>
	public IReadOnlyList<byte[]> EmitterTemplateBytes { get; private init; } = Array.Empty<byte[]>();
	/// <summary>The 20 effector templates as saved (104 bytes each).</summary>
	public IReadOnlyList<byte[]> EffectorTemplateBytes { get; private init; } = Array.Empty<byte[]>();
	/// <summary>The saved particle pool bytes (empty in a save the game writes).</summary>
	public byte[] ParticlePool { get; private init; } = Array.Empty<byte>();

	/// <summary>The slots of the active emitter list from its head, newest first (a list that loops or leaves the slots stops there and is reported in <see cref="ListProblems"/>).</summary>
	public IReadOnlyList<int> ActiveEmitterOrder { get; private init; } = Array.Empty<int>();
	public IReadOnlyList<int> FreeEmitterOrder { get; private init; } = Array.Empty<int>();
	public IReadOnlyList<int> ActiveEffectorOrder { get; private init; } = Array.Empty<int>();
	public IReadOnlyList<int> FreeEffectorOrder { get; private init; } = Array.Empty<int>();

	private static List<int> Walk( string list, int head, int[] next, List<string> problems )
	{
		var order = new List<int>();
		var seen = new bool[next.Length];
		for ( var slot = head; slot >= 0; slot = next[slot] )
		{
			if ( slot >= next.Length || seen[slot] )
			{
				problems.Add( $"The {list} list visits slot {slot} twice or outside 0..{next.Length - 1}." );
				break;
			}
			seen[slot] = true;
			order.Add( slot );
		}
		return order;
	}

	/// <summary>Reads the Particles block of <paramref name="layout"/>; it must end exactly where its loader stops.</summary>
	public static SaveParticlesBlock Parse( ReadOnlySpan<byte> payload, SavePayloadLayout layout )
	{
		ArgumentNullException.ThrowIfNull( layout );
		if ( layout.PayloadLength != payload.Length )
			throw new ArgumentException( "Layout does not describe this payload.", nameof( layout ) );
		var block = layout[SaveBlock.Particles];
		var parsed = Parse( payload, block.Offset );
		if ( parsed.Length != block.Length )
			throw new InvalidDataException( $"The Particles block reads {parsed.Length} bytes but the layout holds {block.Length}." );
		return parsed;
	}

	/// <summary>Reads a Particles block that starts at <paramref name="offset"/> (the delimiter is not checked here).</summary>
	public static SaveParticlesBlock Parse( ReadOnlySpan<byte> payload, int offset )
	{
		if ( offset < 0 || offset > payload.Length )
			throw new InvalidDataException( "The Particles block starts outside the payload." );
		var cursor = new SaveBlockCursor( payload, offset, "Particles" );
		// [BIN:TPW-EXE:0x51F7E9 Particles loader] a wrong magic jumps to 0x51FA0C and 0 (success) is returned after 4 bytes; on = 0 (0x51F80B) does the same after 8
		var magic = cursor.U32();
		if ( magic != SaveBlockWidths.ParticlesMagic )
			return new SaveParticlesBlock { Offset = offset, Length = cursor.Position - offset, MagicMatched = false };
		if ( cursor.U32() == 0 )
			return new SaveParticlesBlock { Offset = offset, Length = cursor.Position - offset };
		var tableSize = cursor.U32();
		if ( tableSize != SaveBlockWidths.ParticleHeaderBytes )
			throw new InvalidDataException( $"The Particles block stores a {tableSize}-byte pool table; the original loader requires {SaveBlockWidths.ParticleHeaderBytes}." );
		var table = payload.Slice( cursor.Take( tableSize ), (int)tableSize );
		var templateSize = cursor.U32();
		if ( templateSize != SaveBlockWidths.ParticleFieldBytes )
			throw new InvalidDataException( $"The Particles block stores a {templateSize}-byte template table; the original loader requires {SaveBlockWidths.ParticleFieldBytes}." );
		var templates = payload.Slice( cursor.Take( templateSize ), (int)templateSize );
		var globals = ReadGlobals( table );
		var poolLength = cursor.U32();
		var poolAt = cursor.Take( poolLength );
		var pool = payload.Slice( poolAt, (int)poolLength );
		// [BIN:TPW-EXE:0x51F8B3 pool length] the loader allocates 13 × capacity × 4 bytes (32-bit) and reads c bytes into them; a capacity of 0 reaches the NULL pool at 0x51F954
		var allocated = unchecked( globals.PoolCapacity * (uint)ParticleBytes );
		if ( globals.PoolCapacity == 0 )
			throw new InvalidDataException( "The Particles block has a pool capacity of 0; the original dereferences a NULL pool." );
		if ( poolLength > allocated )
			throw new InvalidDataException( $"The Particles block holds {poolLength} particle bytes, more than the {allocated} of its {globals.PoolCapacity}-particle pool; the original overruns its heap." );

		var emitters = new SaveParticleEmitter[EmitterSlots];
		for ( var slot = 0; slot < emitters.Length; slot++ )
			emitters[slot] = new SaveParticleEmitter { Slot = slot, Raw = table.Slice( EmittersOffset + slot * SaveParticleEmitter.Bytes, SaveParticleEmitter.Bytes ).ToArray() };
		var effectors = new SaveParticleEffector[EffectorSlots];
		for ( var slot = 0; slot < effectors.Length; slot++ )
			effectors[slot] = new SaveParticleEffector { Slot = slot, Raw = table.Slice( EffectorsOffset + slot * SaveParticleEffector.Bytes, SaveParticleEffector.Bytes ).ToArray() };
		var emitterTemplates = new byte[EmitterTemplates][];
		for ( var index = 0; index < emitterTemplates.Length; index++ )
			emitterTemplates[index] = templates.Slice( index * SaveParticleEmitter.Bytes, SaveParticleEmitter.Bytes ).ToArray();
		var effectorTemplates = new byte[EffectorTemplates][];
		for ( var index = 0; index < effectorTemplates.Length; index++ )
			effectorTemplates[index] = templates.Slice( EmitterTemplates * SaveParticleEmitter.Bytes + index * SaveParticleEffector.Bytes, SaveParticleEffector.Bytes ).ToArray();

		var problems = new List<string>();
		var activeEmitters = Walk( "active emitter", globals.ActiveEmitter, emitters.Select( slot => (int)slot.NextSlot ).ToArray(), problems );
		var freeEmitters = Walk( "free emitter", globals.FreeEmitter, emitters.Select( slot => (int)slot.NextSlot ).ToArray(), problems );
		var activeEffectors = Walk( "active effector", globals.ActiveEffector, effectors.Select( slot => (int)slot.NextSlot ).ToArray(), problems );
		var freeEffectors = Walk( "free effector", globals.FreeEffector, effectors.Select( slot => (int)slot.NextSlot ).ToArray(), problems );
		CheckPartition( "emitter", EmitterSlots, activeEmitters, freeEmitters, problems );
		CheckPartition( "effector", EffectorSlots, activeEffectors, freeEffectors, problems );
		foreach ( var slot in activeEmitters )
			if ( !emitters[slot].InUse )
				problems.Add( $"Emitter slot {slot} is on the active list but not in use." );
		foreach ( var slot in freeEmitters )
			if ( emitters[slot].InUse )
				problems.Add( $"Emitter slot {slot} is on the free list but in use." );
		return new SaveParticlesBlock
		{
			Offset = offset, Length = cursor.Position - offset, On = true, Globals = globals, Emitters = emitters, Effectors = effectors,
			EmitterTemplateBytes = emitterTemplates, EffectorTemplateBytes = effectorTemplates, ParticlePool = pool.ToArray(),
			ActiveEmitterOrder = activeEmitters, FreeEmitterOrder = freeEmitters, ActiveEffectorOrder = activeEffectors, FreeEffectorOrder = freeEffectors, ListProblems = problems
		};
	}

	private static SaveParticleGlobals ReadGlobals( ReadOnlySpan<byte> table )
	{
		var tail = SaveParticleGlobals.TailOffset;
		return new SaveParticleGlobals
		{
			LiveParticles = BinaryPrimitives.ReadUInt32LittleEndian( table[0..] ), TickCounter = BinaryPrimitives.ReadUInt32LittleEndian( table[4..] ),
			LowDetail = BinaryPrimitives.ReadUInt32LittleEndian( table[8..] ), PoolCapacity = BinaryPrimitives.ReadUInt32LittleEndian( table[0xC..] ),
			Random = BinaryPrimitives.ReadUInt32LittleEndian( table[0x10..] ), ParticleCounter = BinaryPrimitives.ReadUInt32LittleEndian( table[0x14..] ),
			EmitterGeneration = BinaryPrimitives.ReadUInt16LittleEndian( table[0x18..] ), EffectorGeneration = BinaryPrimitives.ReadUInt16LittleEndian( table[0x1A..] ),
			SineTable = BinaryPrimitives.ReadUInt32LittleEndian( table[0x1C..] ), Density = BinaryPrimitives.ReadUInt32LittleEndian( table[0x20..] ),
			GroundFunction = BinaryPrimitives.ReadUInt32LittleEndian( table[0x24..] ), PoolPointer = BinaryPrimitives.ReadUInt32LittleEndian( table[0x28..] ),
			ActiveEmitter = BinaryPrimitives.ReadInt16LittleEndian( table[tail..] ), ActiveEffector = BinaryPrimitives.ReadInt16LittleEndian( table[(tail + 2)..] ),
			FreeEmitter = BinaryPrimitives.ReadInt16LittleEndian( table[(tail + 4)..] ), FreeEffector = BinaryPrimitives.ReadInt16LittleEndian( table[(tail + 6)..] ),
			FreeParticleHead = BinaryPrimitives.ReadUInt16LittleEndian( table[(tail + 8)..] ), TailRaw = table.Slice( tail + 10, SaveParticleGlobals.TailBytes - 10 ).ToArray()
		};
	}

	private static void CheckPartition( string kind, int slots, List<int> active, List<int> free, List<string> problems )
	{
		if ( active.Count + free.Count != slots || active.Intersect( free ).Any() )
			problems.Add( $"The {kind} lists hold {active.Count} active and {free.Count} free slots, not a partition of {slots}." );
	}
}
