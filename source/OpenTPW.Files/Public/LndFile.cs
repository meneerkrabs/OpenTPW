using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>One ramp of the blend parameters: the table entry i is 0 below <c>Centre - (Width + 1) / 2</c>, 255 above <c>Centre + (Width + 1) / 2</c> and linear in between (docs/LND.md).</summary>
public readonly record struct LndRamp( byte Centre, byte Width );

/// <summary>One image of a <see cref="LndImageSet"/>: <see cref="BytesPerPixel"/> 4 is <c>[0, R, G, B]</c> per pixel, 1 is one byte (a detail or mask map). Rows run top to bottom.</summary>
public readonly record struct LndImage( int Width, int Height, int BytesPerPixel, ReadOnlyMemory<byte> Pixels );

/// <summary>One resolution set of a <see cref="LndFile"/>: the texture size the set serves (64, 32 or 16) and its 13 images (six colour images, then seven byte maps).</summary>
public sealed class LndImageSet
{
	public LndImageSet( int textureSize, int index, IReadOnlyList<LndImage> images )
	{
		TextureSize = textureSize;
		Index = index;
		Images = images;
	}

	/// <summary>The cell texture size N this set is for; also the file's header dword of the set.</summary>
	public int TextureSize { get; }

	/// <summary>0, 1 or 2 for the sizes 64, 32 and 16.</summary>
	public int Index { get; }

	/// <summary>Always <see cref="LndFile.ImagesPerSet"/> images.</summary>
	public IReadOnlyList<LndImage> Images { get; }
}

/// <summary>
/// Bounded strict reader for a theme's <c>base.lnd</c> (<c>levels/&lt;theme&gt;/terrain.wad</c>), the input of the original's procedural
/// terrain texturer (docs/LND.md). The file holds the blend parameters, a per-vertex layer weight grid at four samples per cell and three
/// resolution sets of 13 source images. The parse must end exactly at the end of the file.
/// </summary>
// [BIN:TPW-EXE:0x00560FF0 base.lnd loader] version byte 3, the ten parameter bytes, the weight grid and the three sets; the set reader is 0x0055F470
public sealed class LndFile : BaseFormat
{
	public const int MaximumFileBytes = 16 * 1024 * 1024;
	public const byte ExpectedVersion = 3;
	public const int ParameterBytes = 10;
	public const int SetCount = 3;
	public const int ImagesPerSet = 13;
	public const int ColourImages = 6;
	/// <summary>Weight samples per cell along each axis.</summary>
	public const int SamplesPerCell = 4;
	/// <summary>Bytes per grid sample: valid flag (0xFF), then the weights 1, 2 and 3.</summary>
	public const int GridBytesPerSample = 4;
	public const int MaximumGridSide = 4096;
	public const int MaximumImageSide = 4096;

	/// <summary>The three set sizes in file order.</summary>
	public static ReadOnlySpan<int> SetSizes => [64, 32, 16];

	public int Version { get; private set; }

	/// <summary>The ten raw parameter bytes, five (centre, width) pairs.</summary>
	public ReadOnlyMemory<byte> Parameters { get; private set; }

	/// <summary>Layer A ramp (dirt in the jungle), object <c>+2</c>/<c>+3</c>.</summary>
	public LndRamp LayerA => Ramp( 0 );
	/// <summary>Layer B ramp (grass in the jungle), object <c>+4</c>/<c>+5</c>.</summary>
	public LndRamp LayerB => Ramp( 2 );
	/// <summary>Mask 2 ramp (B over A), object <c>+6</c>/<c>+7</c>.</summary>
	public LndRamp Mask2 => Ramp( 4 );
	/// <summary>Layer C ramp (foliage in the jungle), object <c>+8</c>/<c>+9</c>.</summary>
	public LndRamp LayerC => Ramp( 6 );
	/// <summary>Mask 1 ramp (C over the rest), object <c>+10</c>/<c>+11</c>.</summary>
	public LndRamp Mask1 => Ramp( 8 );

	/// <summary>Weight grid width in samples (384 in every shipped file).</summary>
	public int GridWidth { get; private set; }
	/// <summary>Weight grid height in samples (344 in every shipped file).</summary>
	public int GridHeight { get; private set; }
	/// <summary>The grid, <see cref="GridWidth"/> × <see cref="GridHeight"/> samples of <see cref="GridBytesPerSample"/> bytes, row-major.</summary>
	public ReadOnlyMemory<byte> Grid { get; private set; }

	/// <summary>Whole cells across, <c>(GridWidth - 1) / 4</c> (95).</summary>
	public int CellsX => (GridWidth - 1) / SamplesPerCell;
	/// <summary>Whole cells down, <c>(GridHeight - 1) / 4</c> (85).</summary>
	public int CellsY => (GridHeight - 1) / SamplesPerCell;

	/// <summary>The sets for the sizes 64, 32 and 16, in file order.</summary>
	public IReadOnlyList<LndImageSet> Sets { get; private set; } = Array.Empty<LndImageSet>();

	public LndFile( string path ) => ReadFromFile( path );
	public LndFile( Stream stream ) => ReadFromStream( stream );

	private LndRamp Ramp( int offset ) => new( Parameters.Span[offset], Parameters.Span[offset + 1] );

	/// <summary>The set for a texture size of 64, 32 or 16.</summary>
	public LndImageSet SetFor( int textureSize )
	{
		foreach ( var set in Sets )
			if ( set.TextureSize == textureSize )
				return set;
		throw new ArgumentOutOfRangeException( nameof( textureSize ), "A procedural texture size is 16, 32 or 64." );
	}

	/// <summary>The grid byte <paramref name="channel"/> (0 valid, 1..3 weights) at sample (<paramref name="x"/>, <paramref name="y"/>).</summary>
	public byte GridSample( int x, int y, int channel )
	{
		if ( (uint)x >= (uint)GridWidth || (uint)y >= (uint)GridHeight || (uint)channel >= GridBytesPerSample )
			throw new ArgumentOutOfRangeException( nameof( x ) );
		return Grid.Span[(y * GridWidth + x) * GridBytesPerSample + channel];
	}

	protected override void ReadFromStream( Stream stream )
	{
		ArgumentNullException.ThrowIfNull( stream );
		var data = ReadBounded( stream );
		var position = 0;
		if ( data.Length < 1 + ParameterBytes + 12 )
			throw new InvalidDataException( "LND header is truncated." );
		Version = data[position++];
		if ( Version != ExpectedVersion )
			throw new InvalidDataException( "LND version is not recognised." );
		Parameters = data.AsMemory( position, ParameterBytes );
		position += ParameterBytes;
		var gridWidth = ReadInt( data, ref position );
		var gridHeight = ReadInt( data, ref position );
		var bytesPerSample = ReadInt( data, ref position );
		if ( gridWidth is < SamplesPerCell + 1 or > MaximumGridSide || gridHeight is < SamplesPerCell + 1 or > MaximumGridSide )
			throw new InvalidDataException( "LND weight grid size is out of range." );
		if ( bytesPerSample != GridBytesPerSample )
			throw new InvalidDataException( "LND weight grid does not have four bytes per sample." );
		var gridLength = (long)gridWidth * gridHeight * GridBytesPerSample;
		if ( gridLength > data.Length - position )
			throw new InvalidDataException( "LND weight grid is truncated." );
		GridWidth = gridWidth;
		GridHeight = gridHeight;
		Grid = data.AsMemory( position, (int)gridLength );
		position += (int)gridLength;

		var sets = new LndImageSet[SetCount];
		for ( var index = 0; index < SetCount; index++ )
		{
			if ( data.Length - position < 5 )
				throw new InvalidDataException( "LND image set header is truncated." );
			var size = ReadInt( data, ref position );
			var setIndex = data[position++];
			if ( size != SetSizes[index] || setIndex != index )
				throw new InvalidDataException( "LND image sets are not in the order 64, 32, 16." );
			var images = new LndImage[ImagesPerSet];
			for ( var image = 0; image < ImagesPerSet; image++ )
			{
				if ( data.Length - position < 12 )
					throw new InvalidDataException( "LND image header is truncated." );
				var width = ReadInt( data, ref position );
				var height = ReadInt( data, ref position );
				var bytes = ReadInt( data, ref position );
				var expectedBytes = image < ColourImages ? 4 : 1;
				if ( bytes != expectedBytes )
					throw new InvalidDataException( "LND image pixel size does not match its role." );
				// The texture window of a cell wraps with the period (width - N, height - N), so the image must be larger than N.
				if ( width <= size || height <= size || width > MaximumImageSide || height > MaximumImageSide )
					throw new InvalidDataException( "LND image size is out of range." );
				var length = (long)width * height * bytes;
				if ( length > data.Length - position )
					throw new InvalidDataException( "LND image data is truncated." );
				images[image] = new LndImage( width, height, bytes, data.AsMemory( position, (int)length ) );
				position += (int)length;
			}
			sets[index] = new LndImageSet( size, setIndex, images );
		}
		if ( position != data.Length )
			throw new InvalidDataException( "LND file has bytes after its last image." );
		Sets = sets;
	}

	private static int ReadInt( byte[] data, ref int position )
	{
		var value = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( position ) );
		position += 4;
		if ( value > int.MaxValue )
			throw new InvalidDataException( "LND size field is out of range." );
		return (int)value;
	}

	private static byte[] ReadBounded( Stream stream )
	{
		using var input = new MemoryStream();
		var readBuffer = new byte[65536];
		while ( true )
		{
			var count = stream.Read( readBuffer, 0, (int)Math.Min( readBuffer.Length, MaximumFileBytes - input.Length + 1 ) );
			if ( count == 0 )
				break;
			if ( count > MaximumFileBytes - input.Length )
				throw new InvalidDataException( "LND exceeds the input byte limit." );
			input.Write( readBuffer, 0, count );
		}
		return input.ToArray();
	}
}
