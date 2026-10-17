using System.Buffers.Binary;
using System.Text;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace OpenTPW;

/// <summary>
/// The fields of a save container's header (docs/SAVE-CONTAINER.md). <see cref="Stamp"/> is the big-endian u32 the writer stores as <c>htonl(0x01221985)</c>
/// (file bytes 01 22 19 85). <see cref="Language"/> is the u8 after the magic (0 to 6; after the Theme Park Inc version block when there is one).
/// <see cref="VersionBlockBytes"/> is 0 for a Theme Park World container, 1 for a Theme Park Inc container that saved no version string and 5 plus the string length
/// otherwise; <see cref="VersionText"/> is that string without its terminating NUL (retail: <c>"Version: Beta_21\r\n"</c>).
/// </summary>
public readonly record struct SaveContainerInfo( uint Magic, byte Language, uint Stamp, int DecodedLength, int CompressedChunkLength,
	int VersionBlockBytes = 0, string? VersionText = null )
{
	/// <summary>Whether the container carries the Theme Park Inc version block after the magic (tp.exe writes none).</summary>
	public bool IsThemeParkInc => VersionBlockBytes != 0;
}

/// <summary>The banner and the required-content bitmap of a container header.</summary>
/// <param name="Banner">The 0x500-byte UTF-16 banner up to its first NUL.</param>
/// <param name="RequiredContent">The first 32 bytes of the 0x100-byte header: the 256-bit bitmap of content the save needs (Theme Park Inc checks it at 0x6505f0, error 8).</param>
/// <param name="OnlineFlag">The u32 after the stamp; the reader supports only 0.</param>
public sealed record SaveContainerHeader( string Banner, byte[] RequiredContent, uint OnlineFlag );

public class SaveReader : BaseFormat, IDisposable
{
	public const int MaximumContainerBytes = 64 * 1024 * 1024;
	public const int MaximumDecodedBytes = 64 * 1024 * 1024;
	// [BIN:TPW-EXE:0x415f50 header writer] u32 magic, u8 language, 0x500 bytes UTF-16 banner, 0x100 bytes header, u32 stamp (htonl 0x01221985), u32 online flag, then the BILZ chunk
	// [BIN:TPI-EXE:0x440a20 header writer] the same fields; the version block (writer 0x43ea60, reader 0x43eba0) sits between the magic and the language byte: u8 hasVersion, then u32 length and the bytes of the first line of Data\Ver.txt including its NUL
	private const int LanguageOffset = 4;
	private const int BannerBytes = 0x500;
	private const int HeaderBytes = 0x100;
	private const int RequiredContentBytes = 32;
	private const int StampLength = 4;
	private const uint Stamp = 0x01221985;
	private const int MaximumLanguage = 6;
	private const int MaximumVersionBytes = 0x100;
	private const int ChunkHeaderBytes = 28;
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
		if ( container.Length < LanguageOffset )
			throw new InvalidDataException( "Save container is truncated before its zlib payload." );
		var magic = BinaryPrimitives.ReadUInt32LittleEndian( container );
		if ( magic != 500 && magic != 400 )
			throw new InvalidDataException( $"Unsupported save container magic 0x{magic:X8}." );
		var offsets = FindOffsets( magic );
		if ( container.Length < offsets.Payload + 6 )
			throw new InvalidDataException( "Save container is truncated before its zlib payload." );
		var language = container[offsets.Language];
		if ( language > MaximumLanguage )
			throw new InvalidDataException( $"Unsupported save container language {language}; the game writes 0 to {MaximumLanguage}." );
		var stamp = BinaryPrimitives.ReadUInt32BigEndian( container.AsSpan( offsets.StampField ) );
		if ( stamp != Stamp )
			throw new InvalidDataException( $"Unsupported save container stamp 0x{stamp:X8}; expected 0x{Stamp:X8} (version byte 133)." );
		if ( BinaryPrimitives.ReadUInt32LittleEndian( container.AsSpan( offsets.Online ) ) != 0 )
			throw new NotSupportedException( "Online save containers are not supported." );
		if ( !container.AsSpan( offsets.Chunk, 4 ).SequenceEqual( "BILZ"u8 ) )
			throw new InvalidDataException( $"Missing BILZ chunk at offset 0x{offsets.Chunk:X}." );
		var decodedLength = BinaryPrimitives.ReadUInt32LittleEndian( container.AsSpan( offsets.Chunk + 4 ) );
		var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian( container.AsSpan( offsets.Chunk + 8 ) );
		if ( decodedLength > maximumDecodedBytes )
			throw new InvalidDataException( $"Declared save payload exceeds the {maximumDecodedBytes}-byte decoded limit." );
		if ( chunkLength != container.Length - offsets.Chunk )
			throw new InvalidDataException( "BILZ chunk length does not match the remaining container bytes." );
		return new SaveContainerInfo( magic, language, stamp, (int)decodedLength, (int)chunkLength, offsets.VersionBlockBytes, offsets.VersionText );
	}

	/// <summary>The banner, required-content bitmap and online flag of the header (validated like <see cref="Inspect"/>).</summary>
	public SaveContainerHeader InspectHeader()
	{
		Inspect();
		var offsets = FindOffsets( BinaryPrimitives.ReadUInt32LittleEndian( container ) );
		var characters = new char[BannerBytes / 2];
		for ( var index = 0; index < characters.Length; index++ )
			characters[index] = (char)BinaryPrimitives.ReadUInt16LittleEndian( container.AsSpan( offsets.Banner + index * 2 ) );
		var end = Array.IndexOf( characters, '\0' );
		return new SaveContainerHeader( new string( characters, 0, end < 0 ? characters.Length : end ),
			container.AsSpan( offsets.Header, RequiredContentBytes ).ToArray(), BinaryPrimitives.ReadUInt32LittleEndian( container.AsSpan( offsets.Online ) ) );
	}

	/// <summary>Where the container's fields sit: the version block (if any) shifts everything from the language byte on.</summary>
	private readonly record struct ContainerOffsets( int VersionBlockBytes, string? VersionText )
	{
		public int Language => LanguageOffset + VersionBlockBytes;
		public int Banner => Language + 1;
		public int Header => Banner + BannerBytes;
		public int StampField => Header + HeaderBytes;
		public int Online => StampField + StampLength;
		public int Chunk => Online + 4;
		public int Payload => Chunk + ChunkHeaderBytes;
	}

	/// <summary>
	/// Tells the two writers' layouts apart. A Theme Park Inc container has the version block after the magic: a 1 flag, a u32 length of at most
	/// <see cref="MaximumVersionBytes"/> and that many printable ASCII bytes ending in NUL. The language byte of a Theme Park World container is
	/// also 1 for Spanish, so the byte alone proves nothing; the banner that follows it is UTF-16 text, whose second bytes are NUL, so it never reads as the string.
	/// A Theme Park Inc container that saved no version string (flag 0, one byte) has its BILZ chunk one byte later than a Theme Park World one.
	/// </summary>
	private ContainerOffsets FindOffsets( uint magic )
	{
		var plain = new ContainerOffsets( 0, null );
		if ( magic != 500 || container.Length <= LanguageOffset )
			return plain;
		var flag = container[LanguageOffset];
		if ( flag == 0 )
		{
			var tpw = plain.Chunk;
			var shifted = new ContainerOffsets( 1, null ).Chunk;
			return container.Length >= shifted + 4 && !container.AsSpan( tpw, 4 ).SequenceEqual( "BILZ"u8 ) && container.AsSpan( shifted, 4 ).SequenceEqual( "BILZ"u8 )
				? new ContainerOffsets( 1, null ) : plain;
		}
		if ( flag != 1 || container.Length < LanguageOffset + 5 )
			return plain;
		var length = BinaryPrimitives.ReadUInt32LittleEndian( container.AsSpan( LanguageOffset + 1 ) );
		if ( length < 2 || length > MaximumVersionBytes || container.Length < LanguageOffset + 5 + length )
			return plain;
		var text = container.AsSpan( LanguageOffset + 5, (int)length );
		if ( text[^1] != 0 )
			return plain;
		foreach ( var value in text[..^1] )
			if ( value is not (>= 0x20 and <= 0x7E or (byte)'\r' or (byte)'\n') )
				return plain;
		return new ContainerOffsets( 5 + (int)length, Encoding.ASCII.GetString( text[..^1] ) );
	}

	public byte[] ReadFile()
	{
		var header = Inspect();
		var payloadOffset = FindOffsets( header.Magic ).Payload;
		var inflater = new Inflater();
		inflater.SetInput( container, payloadOffset, container.Length - payloadOffset );
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
