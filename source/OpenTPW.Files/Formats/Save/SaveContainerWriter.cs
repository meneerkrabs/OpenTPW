using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// What tp.exe's container writer puts in front of the compressed payload (docs/SAVE-CONTAINER.md): the language byte, the copyright banner and the 256-bit
/// bitmap of the content the park needs. <see cref="Magic"/> is 500 for every file tp.exe writes (the static at 0x6FD928); the shipped <c>Easymode.TPWI</c> carries 400.
/// </summary>
/// <param name="Language">0 English, 1 Spanish, 2 Italian, 3 Swedish, 4 German, 5 French, 6 Japanese (the chain of name compares at 0x415F88..0x416048; any other language name writes 0).</param>
/// <param name="Banner">The text copied into the 0x500-byte UTF-16 field (the original copies its per-language notice from the table at 0x78A464); at most 0x27F characters.</param>
/// <param name="RequiredContent">The first 32 bytes of the 0x100-byte header block (a copy of the 8 words at 0x802080); the rest of the block is zero.</param>
public sealed record SaveContainerFields( uint Magic, byte Language, string Banner, byte[] RequiredContent )
{
	public const uint WriterMagic = 500;
	public const int BannerCharacters = 0x280;
	public const int RequiredContentBytes = 32;

	/// <summary>The fields of an existing container (its banner, language, bitmap and magic), for writing an equivalent one.</summary>
	public static SaveContainerFields Of( SaveReader reader )
	{
		ArgumentNullException.ThrowIfNull( reader );
		var info = reader.Inspect();
		if ( info.IsThemeParkInc )
			throw new NotSupportedException( "Theme Park Inc containers (version block) are not written." );
		var header = reader.InspectHeader();
		return new SaveContainerFields( info.Magic, info.Language, header.Banner, header.RequiredContent );
	}
}

/// <summary>
/// Writes a Theme Park World save container: the byte-exact twin of tp.exe's save function <c>0x414920</c>, which writes the magic from the static 500, calls the header
/// writer <c>0x415F50</c> (language byte, 0x500-byte banner, 0x100-byte header block, <c>htonl(0x01221985)</c> stamp, online flag) and then the compressed payload as
/// one BILZ chunk (<c>0x619280</c>: <c>'BILZ'</c>, the payload length, the chunk length including its 0x1C header bytes, the words 15 (window bits), 9 (memory level), 0, 0 and a
/// zlib 1.1.3 stream, <see cref="ClassicZlibDeflater"/>).
/// </summary>
public static class SaveContainerWriter
{
	// [BIN:TPW-EXE:0x415F50 header writer] a zeroed 0x500-byte banner buffer and a zeroed 0x100-byte header buffer are filled from the language table (0x78A464) and the 8-word bitmap 0x802080 and written in full; then the stamp (htonl of 0x749870 = 0x01221985) and the online flag word
	// [BIN:TPW-EXE:0x619280 BILZ writer] deflateInit2(-1, 8, 15, 9, 0) and deflate(Z_FINISH); header words 'BILZ', source length, compressed length + 0x1C, 15, 9, 0, 0
	public const int ChunkHeaderBytes = 28;
	private const uint Stamp = 0x01221985;
	private const int BannerBytes = 0x500;
	private const int HeaderBytes = 0x100;

	public static byte[] Write( SaveContainerFields fields, ReadOnlySpan<byte> payload )
	{
		ArgumentNullException.ThrowIfNull( fields );
		ArgumentNullException.ThrowIfNull( fields.Banner );
		ArgumentNullException.ThrowIfNull( fields.RequiredContent );
		if ( fields.Language > 6 )
			throw new ArgumentOutOfRangeException( nameof( fields ), "The language byte is 0 to 6." );
		if ( fields.Banner.Length >= SaveContainerFields.BannerCharacters )
			throw new ArgumentException( $"The banner holds at most {SaveContainerFields.BannerCharacters - 1} characters.", nameof( fields ) );
		if ( fields.RequiredContent.Length != SaveContainerFields.RequiredContentBytes )
			throw new ArgumentException( $"The required-content bitmap is {SaveContainerFields.RequiredContentBytes} bytes.", nameof( fields ) );
		if ( payload.Length > SaveReader.MaximumDecodedBytes )
			throw new ArgumentException( "The payload exceeds the reader's decoded limit.", nameof( payload ) );
		var compressed = ClassicZlibDeflater.Compress( payload );
		var result = new byte[4 + 1 + BannerBytes + HeaderBytes + 4 + 4 + ChunkHeaderBytes + compressed.Length];
		var span = result.AsSpan();
		BinaryPrimitives.WriteUInt32LittleEndian( span, fields.Magic );
		span[4] = fields.Language;
		for ( var index = 0; index < fields.Banner.Length; index++ )
			BinaryPrimitives.WriteUInt16LittleEndian( span[(5 + index * 2)..], fields.Banner[index] );
		fields.RequiredContent.CopyTo( span[(5 + BannerBytes)..] );
		var cursor = 5 + BannerBytes + HeaderBytes;
		BinaryPrimitives.WriteUInt32BigEndian( span[cursor..], Stamp );
		// the online flag word (0: offline)
		cursor += 8;
		"BILZ"u8.CopyTo( span[cursor..] );
		BinaryPrimitives.WriteUInt32LittleEndian( span[(cursor + 4)..], (uint)payload.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( span[(cursor + 8)..], (uint)(ChunkHeaderBytes + compressed.Length) );
		BinaryPrimitives.WriteUInt32LittleEndian( span[(cursor + 12)..], 15 );
		BinaryPrimitives.WriteUInt32LittleEndian( span[(cursor + 16)..], 9 );
		compressed.CopyTo( span[(cursor + ChunkHeaderBytes)..] );
		return result;
	}

	/// <summary>Writes a container to <paramref name="path"/>; refuses to replace an existing file.</summary>
	public static void WriteFile( string path, SaveContainerFields fields, ReadOnlySpan<byte> payload )
	{
		var bytes = Write( fields, payload );
		using var stream = new FileStream( path, FileMode.CreateNew, FileAccess.Write );
		stream.Write( bytes );
	}
}
