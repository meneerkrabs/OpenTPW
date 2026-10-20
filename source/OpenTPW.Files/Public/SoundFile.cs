namespace OpenTPW;

// BIG SHOUT TO Toksisitee - https://github.com/Toksisitee/PopSoundEditor

public class SoundFile : BaseFormat
{
	private ExpandedMemoryStream memoryStream;
	public byte[] buffer;

	public SoundFile( string path )
	{
		ReadFromFile( path );
	}

	/// <summary>Reads the sound and takes ownership of <paramref name="stream"/>, disposing it when reading finishes.</summary>
	public SoundFile( Stream stream )
	{
		ReadFromStream( stream );
	}

	/// <summary>Wraps <paramref name="bytes"/> without copying them; the caller keeps ownership of the array.</summary>
	public SoundFile( byte[] bytes )
	{
		buffer = bytes;
		memoryStream = new ExpandedMemoryStream( buffer );
	}

	public void Dispose()
	{
		memoryStream.Dispose();
	}

	/// <summary>
	/// Reads the remaining bytes of <paramref name="stream"/> from its current position and disposes it.
	/// The stream is owned by this read path; callers must not use it afterwards.
	/// </summary>
	protected override void ReadFromStream( Stream stream )
	{
		using ( stream )
		using ( var copy = new MemoryStream() )
		{
			stream.CopyTo( copy );
			buffer = copy.ToArray();
		}

		memoryStream = new ExpandedMemoryStream( buffer );
	}

	public MP2File GetFile( MemoryStream stream, int offset = 0, bool dataOnly = false )
	{
		/*
		 * Entry header (40 bytes in all 5,674 entries of the installed corpus; offsets from the entry start)
		 * +0  4 bytes: Header size (always 40)
		 * +4  4 bytes: Data size (MPEG frame bytes plus one trailing zero byte in every entry)
		 * +8  16 bytes: File name, NUL padded; stored as-is and never extended (names such as "x.mp" occur)
		 * +24 2 bytes: Sample rate (UInt16); equals the first MPEG frame header's rate in every entry
		 * +26 1 byte: BitsPerSample (always 16 in the corpus; tp.exe 0x6C6F40 plays 16-bit when it is exactly 16, else 8-bit)
		 * +27 1 byte: Sound type bits (0x20 MPEG, 0x10 another codec, 0x04 playback length present, 0x01 stereo; 36 = mono MPEG, 37 = stereo)
		 * +28 4 bytes: Unknown (always 0)
		 * +32 4 bytes: Playback length in 16-bit PCM bytes when type bit 0x04 is set (see SdtEntryHeader.PlaybackBytes)
		 * +36 4 bytes: Unknown (always 0)
		 * +40 n bytes: MPEG frame data (Data size bytes)
		*/

		memoryStream.Seek( offset, SeekOrigin.Begin );

		var headerSize = memoryStream.ReadInt32();
		var soundDataSize = memoryStream.ReadInt32();
		var fileName = memoryStream.ReadString( 16 ).Split( '\0' )[0];

		if ( headerSize < 40 )
			throw new InvalidDataException( $"Entry header of {headerSize} bytes is shorter than the 40-byte layout." );

		var sampleRate = BitConverter.ToUInt16( memoryStream.ReadBytes( 2 ), 0 );
		var soundTypeAndBits = BitConverter.ToUInt16( memoryStream.ReadBytes( 2 ), 0 );
		var bitsPerSample = soundTypeAndBits & 0xFF;
		var soundType = soundTypeAndBits >> 8;

		// Unknown
		_ = memoryStream.ReadInt32();

		var rawSampleField = memoryStream.ReadInt32();

		// Unknown
		_ = memoryStream.ReadInt32();

		// Frame data starts after the header, not at a fixed offset
		memoryStream.Seek( offset + headerSize, SeekOrigin.Begin );
		var soundData = memoryStream.ReadBytes( soundDataSize );

		// Gather full byte data of file
		var dataSize = headerSize + soundDataSize;

		// We seek back to offset to capture full data set
		memoryStream.Seek ( offset, SeekOrigin.Begin );
		byte[] data = memoryStream.ReadBytes( dataSize );

		return new MP2File( headerSize, fileName, soundData, sampleRate, bitsPerSample, soundType, rawSampleField, data );
	}
}
