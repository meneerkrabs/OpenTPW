using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>
/// One 320-byte particle effect template of <c>Data/Particle/Tp2.plb</c>. The accessors name the fields the particle
/// system reads (docs/PARTICLES.md); the remaining bytes stay available through <see cref="Raw"/>.
/// </summary>
// [BIN:STP-PPC:0x1009BA44 gei_Particle template load] the template block is copied out of the file unchanged; offsets below are the native template offsets
// [BIN:TPW-EXE:0x521E60 create type 1] the emitter slot is a full copy of this record, so the template offsets are the emitter offsets; each accessor reads the width the native code reads (movsx word, byte or dword)
public sealed class ParticleTemplate
{
	public const int Size = 320;

	private readonly byte[] data;

	public ParticleTemplate( ReadOnlySpan<byte> bytes )
	{
		if ( bytes.Length != Size )
			throw new ArgumentException( $"A particle template is {Size} bytes, not {bytes.Length}.", nameof( bytes ) );
		data = bytes.ToArray();
	}

	public ReadOnlySpan<byte> Raw => data;

	private int Int( int offset ) => BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( offset ) );
	private int Short( int offset ) => BinaryPrimitives.ReadInt16LittleEndian( data.AsSpan( offset ) );
	private (int X, int Y, int Z) Vector( int offset ) => (Int( offset ), Int( offset + 4 ), Int( offset + 8 ));

	/// <summary>The effect's name (+0x118), e.g. "Sparks".</summary>
	public string Name
	{
		get
		{
			var span = data.AsSpan( 0x118, 16 );
			var end = span.IndexOf( (byte)0 );
			return Encoding.ASCII.GetString( end < 0 ? span : span[..end] );
		}
	}

	/// <summary>Group the effectors match against (+4, a signed byte).</summary>
	public int Group => (sbyte)data[4];
	public bool ScreenSpace => data[5] != 0;
	/// <summary>The emitter does not tick while this word is not zero (+2).</summary>
	public int Hold => Short( 2 );
	/// <summary>Ring-emitter mode (+0xE); 1 selects tp.exe 0x5224F0, which no shipped template uses.</summary>
	public int RingMode => Short( 0xE );

	/// <summary>Effect started when the emitter's life reaches -1 (+0x10, a word), or a negative value.</summary>
	public int DeathEffect => Short( 0x10 );
	/// <summary>The death effect is an effector (+0xBB).</summary>
	public bool DeathEffectIsEffector => data[0xBB] != 0;
	/// <summary>The emitter velocity is jittered by +-(n - 1) per axis at creation (+0x12, a word); a value of -1 adds nothing but still draws.</summary>
	public int EmitterVelocityJitter => Short( 0x12 );
	/// <summary>Emitter life in ticks (+0x20).</summary>
	public int EmitterLife => Int( 0x20 );
	public int EmitterDrag => Short( 0x24 );
	/// <summary>The emitter's life never counts down (+0x26, a word).</summary>
	public bool Immortal => Short( 0x26 ) != 0;
	/// <summary>Emitter gravity (+0x28, a word; the word after it is the screen-space hide flag).</summary>
	public int EmitterGravity => Short( 0x28 );
	public (int X, int Y, int Z) EmitterVelocity => Vector( 0x2C );
	/// <summary>Ground bounce flag of the emitter (+0x76, a word); no shipped template sets it.</summary>
	public int GroundBounce => Short( 0x76 );
	/// <summary>Base particle velocity (+0x38..+0x40); an EVENT 2 effect replaces it with its direction.</summary>
	public (int X, int Y, int Z) ParticleVelocity => Vector( 0x38 );
	/// <summary>Spawn volume extents (+0x44).</summary>
	public (int X, int Y, int Z) VolumeExtent => Vector( 0x44 );
	/// <summary>Not zero: the box volume; zero: the horizontal disc or ring (+0xC2, a byte).</summary>
	public int VolumeShape => data[0xC2];
	/// <summary>The disc is a ring (+0xA8, a word).</summary>
	public bool VolumeRing => Short( 0xA8 ) != 0;
	/// <summary>The volume's Y offset is drawn unsigned (+0xA0, a word).</summary>
	public bool VolumeUnsignedY => Short( 0xA0 ) != 0;
	/// <summary>The volume is rotated about Y by this angle, 4096 per turn (+0xAC).</summary>
	public int VolumeRotation => Int( 0xAC );
	/// <summary>Spiral offset radius (+0xC4, a word) and the angle step per tick (+0xC8).</summary>
	public int SpiralRadius => Short( 0xC4 );
	public int SpiralRate => Int( 0xC8 );
	/// <summary>Initial spiral angle (+0x54) and the countdown of a negative rate (+0x66, a word); both are zero in the shipped file.</summary>
	public int SpiralPhase => Int( 0x54 );
	public int RateCounter => Short( 0x66 );
	/// <summary>The speed a directed (type 2) effect gives its direction (+0xB0).</summary>
	public int DirectedSpeed => Int( 0xB0 );
	/// <summary>Velocity jitter +-(n - 1) per axis (+0x50, a word).</summary>
	public int VelocityJitter => Short( 0x50 );
	/// <summary>Not zero: random colours; an odd value flickers every tick (+0x58).</summary>
	public int ColourMode => Int( 0x58 );
	public bool RandomColour => Int( 0x58 ) != 0;
	public int RadialSpeed => Int( 0x5C );
	public int Burst => Int( 0x60 );
	/// <summary>Live-particle cap of one emitter (+0x64, a word).</summary>
	public int Cap => Short( 0x64 );
	/// <summary>Add the emitter velocity to the particle velocity (+0x6C).</summary>
	public bool AddsEmitterVelocity => data[0x6C] != 0;
	/// <summary>Particles start with a random rotation (+0x6D).</summary>
	public bool RandomRotation => data[0x6D] != 0;
	/// <summary>The rate byte +0x68 + index (a signed byte): positive = particles per tick, negative = one particle every |n| ticks. The emitter picks +0x6B in the last quarter of its remaining life, +0x6A, +0x69, and +0x68 for the first quarter and for immortal emitters.</summary>
	public int Rate( int index ) => (sbyte)data[0x68 + index];
	public int RenderFlags => Int( 0x70 );
	/// <summary>Size at birth and at death (+0x74 and +0xA6, words).</summary>
	public int SizeAtBirth => Short( 0x74 );
	public int SizeAtDeath => Short( 0xA6 );
	public int ParticleLife => Int( 0x78 );
	public int ParticleDrag => Int( 0x7C );
	public int ParticleGravity => Int( 0x80 );
	public (int X, int Y, int Z) RandomAcceleration => Vector( 0x84 );
	/// <summary>Sprite code (+0x94): bank * 16 + animation of the particles sprite set.</summary>
	public int SpriteCode => Short( 0x94 );
	/// <summary>Frames of the sprite animation (+0x96); 0 is an untextured particle.</summary>
	public int FrameCount => Short( 0x96 );
	/// <summary>Effect started when one particle dies (+0x98, a dword), or a negative value.</summary>
	public int ParticleDeathEffect => Int( 0x98 );
	/// <summary>Effectors that target every group skip this template's particles when this is not zero (+0x9C).</summary>
	public bool IgnoredByAllGroupEffectors => Int( 0x9C ) != 0;
	/// <summary>Draw-time spatial scale in permille around the emitter origin (+0xA2, 1000 = unscaled).</summary>
	public int Control => Short( 0xA2 );
	/// <summary>Particles die with their emitter (+0xAA).</summary>
	public bool ParticlesDieWithEmitter => data[0xAA] != 0;
	/// <summary>Spin per tick (+0xBE) and the upper bound of the random spin step (+0xC6), words in 1/65536 turn.</summary>
	public int Spin => Short( 0xBE );
	public int SpinMaximum => Short( 0xC6 );
	/// <summary>Chained effect started at creation (+0xB4, a dword), or a negative value.</summary>
	public int ChildEffect => Int( 0xB4 );
	/// <summary>The chained effect follows its parent (+0xB8, a word).</summary>
	public bool ChildFollows => Short( 0xB8 ) != 0;
	/// <summary>The chained effect is an effector (+0xBA).</summary>
	public bool ChildIsEffector => data[0xBA] != 0;
	/// <summary>When the emitter dies its child's life is set to -1 (+0xBC, a word).</summary>
	public bool KillsChildWithEmitter => Short( 0xBC ) != 0;
	/// <summary>Not scaled by the density setting (+0xC0).</summary>
	public bool IgnoresDensity => data[0xC0] != 0;
	/// <summary>Refused while the low-detail flag is on (+0xC1).</summary>
	public bool RefusedInLowDetail => data[0xC1] != 0;
	/// <summary>The 16 colours 0xAARRGGBB (+0xD8), index 15 at birth and 0 at death.</summary>
	public uint Colour( int index ) => BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 0xD8 + index * 4 ) );
}

/// <summary>
/// One 104-byte effector template of <c>Data/Particle/Tp2.plb</c> (a group-targeted attractor with an optional kill radius). The record is
/// copied into an effector slot at creation, so these offsets are the slot's offsets.
/// </summary>
// [BIN:TPW-EXE:0x522360 effector create] 0x1A dwords copied from the template table at 0x80C6B0
public sealed class ParticleEffectorTemplate
{
	public const int Size = 104;
	private readonly byte[] data;

	public ParticleEffectorTemplate( ReadOnlySpan<byte> bytes )
	{
		if ( bytes.Length != Size )
			throw new ArgumentException( $"An effector template is {Size} bytes, not {bytes.Length}.", nameof( bytes ) );
		data = bytes.ToArray();
	}

	public ReadOnlySpan<byte> Raw => data;

	private int Int( int offset ) => BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( offset ) );
	private int Short( int offset ) => BinaryPrimitives.ReadInt16LittleEndian( data.AsSpan( offset ) );

	/// <summary>Target group (+4): -1 = every group, else the emitter's and the particle's group.</summary>
	public int Group => Int( 4 );
	/// <summary>The effector bounces on the ground function (+0xB).</summary>
	public bool GroundBounce => data[0xB] != 0;
	public (int X, int Y, int Z) Velocity => (Int( 0x18 ), Int( 0x1C ), Int( 0x20 ));
	public int Drag => Int( 0x24 );
	public int Gravity => Int( 0x28 );
	/// <summary>Kill radius (+0x2C) and mode (+0x30; 2 = kill the particles inside the radius).</summary>
	public int Radius => Int( 0x2C );
	public int Mode => Int( 0x30 );
	/// <summary>Per-axis jitter of the attraction target (+0x34, +0x38, +0x3C) and the strength (+0x40).</summary>
	public (int X, int Y, int Z) TargetJitter => (Int( 0x34 ), Int( 0x38 ), Int( 0x3C ));
	public int Strength => Int( 0x40 );
	/// <summary>Refused under low detail (+0x44).</summary>
	public bool RefusedInLowDetail => data[0x44] != 0;
	/// <summary>Killed when a save is loaded (+0x45).</summary>
	public bool KilledOnLoad => data[0x45] != 0;
	/// <summary>Death effect (+0x46, a word) and whether it is an effector (+0x49).</summary>
	public int DeathEffect => Short( 0x46 );
	public bool DeathEffectIsEffector => data[0x49] != 0;
	public bool Immortal => Short( 0x4A ) != 0;
	public int Life => Int( 0x4C );
}

/// <summary>
/// <c>Data/Particle/Tp2.plb</c>: u32 template count, u32 template size (320), the templates, u32 effector count,
/// u32 effector size (104), the effector records and an 8-byte trailer (33, 1024) the Mac build does not use.
/// </summary>
// [BIN:STP-PPC:0x1004CDB0 gei_Particle] little-endian layout; the counts are checked against the file length
// [DATA:Data/Particle/Tp2.plb] 105 templates and 20 effectors in the shipped file (35704 bytes)
public sealed class ParticleTemplateFile : BaseFormat
{
	public const int TemplateSize = ParticleTemplate.Size;
	public const int EffectorSize = 104;
	public const int MaximumTemplates = 105;
	public const int MaximumEffectors = 20;
	public const int TrailerBytes = 8;

	public IReadOnlyList<ParticleTemplate> Templates { get; private set; } = Array.Empty<ParticleTemplate>();
	/// <summary>The raw effector records (104 bytes each).</summary>
	public IReadOnlyList<byte[]> Effectors { get; private set; } = Array.Empty<byte[]>();
	/// <summary>The effector records with their fields named (docs/PARTICLES.md).</summary>
	public IReadOnlyList<ParticleEffectorTemplate> EffectorTemplates { get; private set; } = Array.Empty<ParticleEffectorTemplate>();

	public ParticleTemplateFile( string path ) => ReadFromFile( path );
	public ParticleTemplateFile( Stream stream ) => ReadFromStream( stream );

	protected override void ReadFromStream( Stream stream )
	{
		// bounded read: the header, the templates and effectors of the largest supported file, the trailer and some margin
		const int Limit = 8 + MaximumTemplates * TemplateSize + 8 + MaximumEffectors * EffectorSize + TrailerBytes + 1024;
		using var memory = new MemoryStream();
		var buffer = new byte[8192];
		int read;
		while ( (read = stream.Read( buffer, 0, buffer.Length )) > 0 )
		{
			if ( memory.Length + read > Limit )
				throw new InvalidDataException( "Particle template file is larger than any supported layout." );
			memory.Write( buffer, 0, read );
		}
		var data = memory.ToArray();
		var position = 0;
		var count = ReadInt( data, ref position );
		var size = ReadInt( data, ref position );
		if ( count < 0 || count > MaximumTemplates || size != TemplateSize )
			throw new InvalidDataException( $"Particle template file declares {count} templates of {size} bytes." );
		if ( (long)count * size > data.Length - position )
			throw new InvalidDataException( "Particle template file ends inside its templates." );
		var templates = new ParticleTemplate[count];
		for ( var index = 0; index < count; index++ )
		{
			templates[index] = new ParticleTemplate( data.AsSpan( position, size ) );
			position += size;
		}
		var effectorCount = ReadInt( data, ref position );
		var effectorSize = ReadInt( data, ref position );
		if ( effectorCount < 0 || effectorCount > MaximumEffectors || effectorSize != EffectorSize )
			throw new InvalidDataException( $"Particle template file declares {effectorCount} effectors of {effectorSize} bytes." );
		if ( (long)effectorCount * effectorSize > data.Length - position )
			throw new InvalidDataException( "Particle template file ends inside its effectors." );
		var effectors = new byte[effectorCount][];
		var effectorTemplates = new ParticleEffectorTemplate[effectorCount];
		for ( var index = 0; index < effectorCount; index++ )
		{
			effectors[index] = data.AsSpan( position, effectorSize ).ToArray();
			effectorTemplates[index] = new ParticleEffectorTemplate( effectors[index] );
			position += effectorSize;
		}
		var rest = data.Length - position;
		if ( rest != 0 && rest != TrailerBytes )
			throw new InvalidDataException( $"Particle template file has {rest} bytes after its effectors, expected none or {TrailerBytes}." );
		Templates = templates;
		Effectors = effectors;
		EffectorTemplates = effectorTemplates;
	}

	private static int ReadInt( byte[] data, ref int position )
	{
		if ( data.Length - position < 4 )
			throw new InvalidDataException( "Particle template file is truncated." );
		var value = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( position ) );
		position += 4;
		return value;
	}
}
