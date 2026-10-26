using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// Bounded strict reader for the <c>.hmp</c> height profile that ships beside a ride's track-piece meshes (docs/TRACKS.md).
/// A 48-byte header (version 3, magic 0xAB1E, samples per cell 5, 100, cells wide, cells high, body offset 48, the offsets of
/// the per-cell maxima and the per-cell flags, then six bounding-box floats) is followed by the profile
/// (5 × width) × (5 × height) bytes, the width × height per-cell maxima and the width × height per-cell flags.
/// The profile steps are about 0.4 world units; the file's consumers are the object height map lookup (<c>ObjectHeightMaps</c>, 0x00452AE0) and the track piece profile (TRACK-HMP); it does not deform the terrain.
/// </summary>
public sealed class HmpFile : BaseFormat
{
	public const int MaximumFileBytes = 1024 * 1024;
	public const int HeaderBytes = 48;
	public const ushort ExpectedVersion = 3;
	public const ushort ExpectedMagic = 0xAB1E;
	public const int MaximumCells = 256;

	public int Version { get; private set; }
	/// <summary>Profile samples per cell along each axis (5 in every shipped file).</summary>
	public int SamplesPerCell { get; private set; }
	/// <summary>Header word 3 (100 in every shipped file; its meaning is not traced).</summary>
	public int Scale { get; private set; }
	/// <summary>Cells wide.</summary>
	public int Width { get; private set; }
	/// <summary>Cells high.</summary>
	public int Height { get; private set; }
	/// <summary>The bounding box floats (minimum x, y, z then maximum x, y, z).</summary>
	public IReadOnlyList<float> BoundingBox { get; private set; } = Array.Empty<float>();
	/// <summary>The height profile, (5 × <see cref="Width"/>) × (5 × <see cref="Height"/>) bytes, row-major.</summary>
	public ReadOnlyMemory<byte> Profile { get; private set; }
	/// <summary>The per-cell maxima, <see cref="Width"/> × <see cref="Height"/> bytes.</summary>
	public ReadOnlyMemory<byte> CellMaxima { get; private set; }
	/// <summary>The per-cell flags, <see cref="Width"/> × <see cref="Height"/> bytes (zero in every shipped file).</summary>
	public ReadOnlyMemory<byte> CellFlags { get; private set; }

	public int ProfileWidth => Width * SamplesPerCell;
	public int ProfileHeight => Height * SamplesPerCell;

	public HmpFile( string path ) => ReadFromFile( path );
	public HmpFile( Stream stream ) => ReadFromStream( stream );

	/// <summary>The profile byte at sample (<paramref name="x"/>, <paramref name="y"/>).</summary>
	public byte Sample( int x, int y )
	{
		if ( (uint)x >= (uint)ProfileWidth || (uint)y >= (uint)ProfileHeight )
			throw new ArgumentOutOfRangeException( nameof( x ) );
		return Profile.Span[y * ProfileWidth + x];
	}

	/// <summary>The largest profile byte of the cell (<paramref name="cellX"/>, <paramref name="cellY"/>) as the file's per-cell table states it.</summary>
	public byte CellMaximum( int cellX, int cellY )
	{
		if ( (uint)cellX >= (uint)Width || (uint)cellY >= (uint)Height )
			throw new ArgumentOutOfRangeException( nameof( cellX ) );
		return CellMaxima.Span[cellY * Width + cellX];
	}

	protected override void ReadFromStream( Stream stream )
	{
		ArgumentNullException.ThrowIfNull( stream );
		var data = ReadBounded( stream );
		if ( data.Length < HeaderBytes )
			throw new InvalidDataException( "HMP header is truncated." );
		Version = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 0 ) );
		if ( Version != ExpectedVersion || BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 2 ) ) != ExpectedMagic )
			throw new InvalidDataException( "HMP version or magic is not recognised." );
		SamplesPerCell = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 4 ) );
		Scale = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 6 ) );
		Width = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 8 ) );
		Height = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 10 ) );
		if ( SamplesPerCell is < 1 or > 16 )
			throw new InvalidDataException( "HMP samples per cell is out of range." );
		if ( Width is < 1 or > MaximumCells || Height is < 1 or > MaximumCells )
			throw new InvalidDataException( "HMP size is out of range." );
		var profileLength = (long)Width * SamplesPerCell * Height * SamplesPerCell;
		var cellCount = (long)Width * Height;
		var bodyOffset = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 12 ) );
		var maximaOffset = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 16 ) );
		var flagsOffset = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 20 ) );
		// The three sections follow each other, so a file that points elsewhere (or is cut short or padded) is not this layout.
		if ( bodyOffset != HeaderBytes || maximaOffset != bodyOffset + profileLength || flagsOffset != maximaOffset + cellCount )
			throw new InvalidDataException( "HMP section offsets do not match its size." );
		if ( data.Length != flagsOffset + cellCount )
			throw new InvalidDataException( "HMP body length does not match its header." );
		var box = new float[6];
		for ( var index = 0; index < box.Length; index++ )
		{
			box[index] = BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( 24 + index * 4 ) );
			if ( !float.IsFinite( box[index] ) )
				throw new InvalidDataException( "HMP bounding box is not finite." );
		}
		BoundingBox = box;
		Profile = data.AsMemory( (int)bodyOffset, (int)profileLength );
		CellMaxima = data.AsMemory( (int)maximaOffset, (int)cellCount );
		CellFlags = data.AsMemory( (int)flagsOffset, (int)cellCount );
	}

	private static byte[] ReadBounded( Stream stream )
	{
		using var input = new MemoryStream();
		var readBuffer = new byte[8192];
		while ( true )
		{
			var count = stream.Read( readBuffer, 0, (int)Math.Min( readBuffer.Length, MaximumFileBytes - input.Length + 1 ) );
			if ( count == 0 )
				break;
			if ( count > MaximumFileBytes - input.Length )
				throw new InvalidDataException( "HMP exceeds the input byte limit." );
			input.Write( readBuffer, 0, count );
		}
		return input.ToArray();
	}
}
