using System.Buffers.Binary;
using System.Text;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace OpenTPW;

public readonly record struct SaveContainerInfo( uint Magic, uint FileType, byte Version, int DecodedLength, int CompressedChunkLength );

public class SaveReader : BaseFormat, IDisposable
{
	public const int MaximumContainerBytes = 64 * 1024 * 1024;
	public const int MaximumDecodedBytes = 64 * 1024 * 1024;
	private const int ChunkOffset = 0x60d;
	private const int PayloadOffset = ChunkOffset + 28;
	private byte[] container = Array.Empty<byte>();
	private readonly int maximumContainerBytes;
	private readonly int maximumDecodedBytes;
	private bool disposed;

	public byte[] buffer => (byte[])container.Clone();

	public SaveReader( string path, int maximumContainerBytes = MaximumContainerBytes, int maximumDecodedBytes = MaximumDecodedBytes )
	{
		ValidateLimits( maximumContainerBytes, maximumDecodedBytes );
		this.maximumContainerBytes = maximumContainerBytes;
		this.maximumDecodedBytes = maximumDecodedBytes;
		using var fileStream = File.OpenRead( path );
		ReadFromStream( fileStream );
	}

	public SaveReader( Stream stream, int maximumContainerBytes = MaximumContainerBytes, int maximumDecodedBytes = MaximumDecodedBytes )
	{
		ValidateLimits( maximumContainerBytes, maximumDecodedBytes );
		this.maximumContainerBytes = maximumContainerBytes;
		this.maximumDecodedBytes = maximumDecodedBytes;
		ReadFromStream( stream );
	}

	private static void ValidateLimits( int maximumContainerBytes, int maximumDecodedBytes )
	{
		if ( maximumContainerBytes < 1 || maximumContainerBytes > MaximumContainerBytes )
			throw new ArgumentOutOfRangeException( nameof( maximumContainerBytes ) );
		if ( maximumDecodedBytes < 1 || maximumDecodedBytes > MaximumDecodedBytes )
			throw new ArgumentOutOfRangeException( nameof( maximumDecodedBytes ) );
	}

	protected override void ReadFromStream( Stream stream )
	{
		ArgumentNullException.ThrowIfNull( stream );
		if ( !stream.CanRead )
			throw new ArgumentException( "Save container stream must be readable.", nameof( stream ) );
		using var input = new MemoryStream();
		var readBuffer = new byte[8192];
		while ( true )
		{
			var readCount = stream.Read( readBuffer, 0, (int)Math.Min( readBuffer.Length, maximumContainerBytes - input.Length + 1 ) );
			if ( readCount == 0 )
				break;
			if ( readCount > maximumContainerBytes - input.Length )
				throw new InvalidDataException( $"Save container exceeds the {maximumContainerBytes}-byte input limit." );
			input.Write( readBuffer, 0, readCount );
		}
		container = input.ToArray();
	}

	public SaveContainerInfo Inspect()
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		if ( container.Length < PayloadOffset + 6 )
			throw new InvalidDataException( "Save container is truncated before its zlib payload." );
		var magic = BinaryPrimitives.ReadUInt32LittleEndian( container );
		if ( magic != 500 && magic != 400 )
			throw new InvalidDataException( $"Unsupported save container magic 0x{magic:X8}." );
		var version = container[0x608];
		if ( version != 133 )
			throw new InvalidDataException( $"Unsupported save container version {version}; expected 133." );
		if ( container[0x609] != 0 )
			throw new NotSupportedException( "Online save containers are not supported." );
		if ( !container.AsSpan( ChunkOffset, 4 ).SequenceEqual( "BILZ"u8 ) )
			throw new InvalidDataException( $"Missing BILZ chunk at offset 0x{ChunkOffset:X}." );
		var decodedLength = BinaryPrimitives.ReadUInt32LittleEndian( container.AsSpan( ChunkOffset + 4 ) );
		var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian( container.AsSpan( ChunkOffset + 8 ) );
		if ( decodedLength > maximumDecodedBytes )
			throw new InvalidDataException( $"Declared save payload exceeds the {maximumDecodedBytes}-byte decoded limit." );
		if ( chunkLength != container.Length - ChunkOffset )
			throw new InvalidDataException( "BILZ chunk length does not match the remaining container bytes." );
		return new SaveContainerInfo( magic, BinaryPrimitives.ReadUInt32LittleEndian( container.AsSpan( 0x604 ) ), version, (int)decodedLength, (int)chunkLength );
	}

	public byte[] ReadFile()
	{
		var header = Inspect();
		var inflater = new Inflater();
		inflater.SetInput( container, PayloadOffset, container.Length - PayloadOffset );
		using var output = new MemoryStream();
		var readBuffer = new byte[8192];
		try
		{
			while ( !inflater.IsFinished )
			{
				var readCount = inflater.Inflate( readBuffer );
				if ( readCount > header.DecodedLength - output.Length )
					throw new InvalidDataException( "Inflated save payload exceeds its declared decoded length." );
				output.Write( readBuffer, 0, readCount );
				if ( readCount == 0 && !inflater.IsFinished )
					throw new InvalidDataException( inflater.IsNeedingDictionary
						? "Save zlib payload requires an unsupported dictionary."
						: "Save zlib payload is truncated or cannot make progress." );
			}
		}
		catch ( Exception exception ) when ( exception is SharpZipBaseException or FormatException )
		{
			throw new InvalidDataException( "Save zlib payload or checksum is invalid.", exception );
		}
		if ( inflater.RemainingInput != 0 )
			throw new InvalidDataException( "Unexpected trailing bytes after the save zlib stream." );
		if ( output.Length != header.DecodedLength )
			throw new InvalidDataException( "Inflated save payload does not match its declared decoded length." );
		return output.ToArray();
	}

	public string FileToString() => Encoding.ASCII.GetString( ReadFile() ).Replace( "\0", string.Empty );

	public void Dispose()
	{
		disposed = true;
		container = Array.Empty<byte>();
	}
}
