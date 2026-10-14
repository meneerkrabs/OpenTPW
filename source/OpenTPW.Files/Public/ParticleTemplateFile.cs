using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>
/// One 320-byte particle effect template of <c>Data/Particle/Tp2.plb</c>. The accessors name the fields the particle
/// system reads (docs/PARTICLES.md); the remaining bytes stay available through <see cref="Raw"/>.
/// </summary>
// [BIN:STP-PPC:0x1009BA44 gei_Particle template load] the template block is copied out of the file unchanged; offsets below are the native template offsets
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

	public int Group => data[4];
	public bool ScreenSpace => data[5] != 0;

	/// <summary>Effect started when the emitter's life runs out (+0x10), or a negative value.</summary>
	public int DeathEffect => Short( 0x10 );
	/// <summary>The emitter velocity is jittered by this much per axis at creation (+0x12).</summary>
	public int EmitterVelocityJitter => Short( 0x12 );
	/// <summary>Emitter life in ticks (+0x20).</summary>
	public int EmitterLife => Int( 0x20 );
	public int EmitterDrag => Short( 0x24 );
	/// <summary>The emitter's life never counts down (+0x26).</summary>
	public bool Immortal => data[0x26] != 0;
	public int EmitterGravity => Int( 0x28 );
	public (int X, int Y, int Z) EmitterVelocity => Vector( 0x2C );
	/// <summary>Ground bounce of the emitter (+0x76).</summary>
	public int GroundBounce => Short( 0x76 );
	/// <summary>Base particle velocity (+0x38..+0x40); an EVENT 2 effect replaces it with its direction.</summary>
	public (int X, int Y, int Z) ParticleVelocity => Vector( 0x38 );
	/// <summary>Spawn volume extents (+0x44).</summary>
	public (int X, int Y, int Z) VolumeExtent => Vector( 0x44 );
	public int VolumeShape => data[0xC2];
	public int VelocityJitter => Int( 0x50 );
	public bool RandomColour => Int( 0x58 ) != 0;
	public int RadialSpeed => Int( 0x5C );
	public int Burst => Int( 0x60 );
	/// <summary>Live-particle cap of one emitter (+0x64).</summary>
	public int Cap => Int( 0x64 );
	/// <summary>Per quarter of the emitter's life: positive = particles per tick, negative = one particle every |n| ticks (+0x68..+0x6B).</summary>
	public int Rate( int quarter ) => (sbyte)data[0x68 + quarter];
	public int RenderFlags => Int( 0x70 );
	public int SizeAtBirth => Int( 0x74 );
	public int SizeAtDeath => Short( 0xA6 );
	public int ParticleLife => Int( 0x78 );
	public int ParticleDrag => Int( 0x7C );
	public int ParticleGravity => Int( 0x80 );
	public (int X, int Y, int Z) RandomAcceleration => Vector( 0x84 );
	/// <summary>Sprite code (+0x94): bank * 16 + animation of the particles sprite set.</summary>
	public int SpriteCode => Short( 0x94 );
	/// <summary>Frames of the sprite animation (+0x96); 0 is an untextured particle.</summary>
	public int FrameCount => Short( 0x96 );
	/// <summary>Effect started when one particle dies (+0x98), or a negative value.</summary>
	public int ParticleDeathEffect => Short( 0x98 );
	/// <summary>Draw-time spatial scale in permille around the emitter origin (+0xA2, 1000 = unscaled).</summary>
	public int Control => Short( 0xA2 );
	public int Spin => Short( 0xBE );
	/// <summary>Chained effect started at creation (+0xB4), or a negative value.</summary>
	public int ChildEffect => Short( 0xB4 );
	/// <summary>The chained effect follows its parent (+0xB8, low byte).</summary>
	public bool ChildFollows => data[0xB8] != 0;
	/// <summary>Not scaled by the density setting (+0xC0).</summary>
	public bool IgnoresDensity => data[0xC0] != 0;
	/// <summary>Refused while the low-detail flag is on (+0xC1).</summary>
	public bool RefusedInLowDetail => data[0xC1] != 0;
	/// <summary>The 16 colours 0xAARRGGBB (+0xD8), index 15 at birth and 0 at death.</summary>
	public uint Colour( int index ) => BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 0xD8 + index * 4 ) );
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
	/// <summary>The raw effector records; their layout is not decoded and they are not simulated.</summary>
	public IReadOnlyList<byte[]> Effectors { get; private set; } = Array.Empty<byte[]>();

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
		for ( var index = 0; index < effectorCount; index++ )
		{
			effectors[index] = data.AsSpan( position, effectorSize ).ToArray();
			position += effectorSize;
		}
		var rest = data.Length - position;
		if ( rest != 0 && rest != TrailerBytes )
			throw new InvalidDataException( $"Particle template file has {rest} bytes after its effectors, expected none or {TrailerBytes}." );
		Templates = templates;
		Effectors = effectors;
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
