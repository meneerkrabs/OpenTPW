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

	protected void ReadFromStream( Stream stream )
	{
		// Set up read buffer
		var tempStreamReader = new StreamReader( stream );
		var fileLength = (int)tempStreamReader.BaseStream.Length;

		buffer = new byte[fileLength];
		tempStreamReader.BaseStream.Read( buffer, 0, fileLength );
		tempStreamReader.Close();

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
		 * +26 1 byte: BitsPerSample (always 16 in the corpus; the meaning is not independently verified)
		 * +27 1 byte: Sound type (36 = mono, 37 = stereo); matches the MPEG channel mode in every entry
		 * +28 4 bytes: Unknown (always 0)
		 * +32 4 bytes: Raw value, not proven to be a sample count (see MP2File.RawSampleField)
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
