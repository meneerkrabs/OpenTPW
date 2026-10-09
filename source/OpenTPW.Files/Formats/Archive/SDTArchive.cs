namespace OpenTPW;

public class SdtArchive : IArchive
{
	private ExpandedMemoryStream memoryStream;
	private SoundFile mp2Reader;

	public byte[] buffer;
	public List<MP2File> soundFiles;

	/// <summary>Maximum entry count accepted from the header (the largest shipped bank holds far fewer).</summary>
	public const int MaximumEntryCount = 65536;
	/// <summary>Smallest entry header: the two sizes and the 16-byte name (shipped headers are 40 bytes).</summary>
	public const int MinimumEntryHeaderBytes = 24;

	/// <summary>
	/// Entries that were skipped because their offset, header or data size points outside the bank;
	/// the remaining entries stay playable (one damaged entry no longer silences the whole bank).
	/// </summary>
	public List<string> SkippedEntries { get; } = new();

	/// <summary>Receives one line per skipped entry (wire to the game log).</summary>
	public static Action<string>? Diagnostic { get; set; }

	public SdtArchive( string path )
	{
		soundFiles = new List<MP2File>();
		using var fileStream = File.OpenRead( path );
		ReadFromStream( fileStream );
	}

	public SdtArchive( Stream stream )
	{
		soundFiles = new List<MP2File>();
		ReadFromStream( stream );
	}

	public void Dispose()
	{
		memoryStream.Dispose();
	}

	public void ReadFromStream( Stream stream )
	{
		// Set up read buffer
		var tempStreamReader = new StreamReader( stream );
		var fileLength = (int)tempStreamReader.BaseStream.Length;

		buffer = new byte[fileLength];
		tempStreamReader.BaseStream.Read( buffer, 0, fileLength );
		tempStreamReader.Close();
		memoryStream = new ExpandedMemoryStream( buffer );
		mp2Reader = new SoundFile( new MemoryStream(buffer) );

		ReadArchive();
	}

	public void ReadArchive(bool dataOnly = false)
	{
		memoryStream.Seek( 0, SeekOrigin.Begin );

		/*
			#File header
				4 bytes: File count
			#For each file
				4 bytes: File offset
			#For each file (at offset)
				4 bytes: Header size
				4 bytes: Data size
				16 bytes: File name (usually null terminated)
				4 bytes: Sample rate
				4 bytes: Resolution
				4 bytes: Sound type
					*See enum above (also: https://github.com/ufdada/dk2-tools/blob/6b4e49b607bbb7e0aa843856e584f6dd1365e7fc/Formats/Sound/sdt_struct.bt)
				4 bytes: Unknown
				4 bytes: Samples
				4 bytes: Unknown
				n bytes: File data
		*/

		if ( buffer.Length < 4 )
		{
			Skip( "the bank is shorter than its entry count" );
			return;
		}
		var fileCount = BitConverter.ToInt32( buffer, 0 );
		if ( fileCount < 0 || fileCount > MaximumEntryCount || 4L + fileCount * 4L > buffer.Length )
		{
			Skip( $"entry count {fileCount} does not fit the {buffer.Length}-byte bank" );
			return;
		}

		for ( int i = 0; i < fileCount; i++ )
		{
			var offset = BitConverter.ToInt32( buffer, 4 + i * 4 );
			if ( offset < 4 + fileCount * 4 || offset > buffer.Length - MinimumEntryHeaderBytes )
			{
				Skip( $"entry {i}: offset {offset} is outside the bank" );
				continue;
			}
			var headerSize = BitConverter.ToInt32( buffer, offset );
			var dataSize = BitConverter.ToInt32( buffer, offset + 4 );
			if ( headerSize < MinimumEntryHeaderBytes || dataSize < 0 || (long)offset + headerSize + dataSize > buffer.Length )
			{
				Skip( $"entry {i}: header {headerSize} + data {dataSize} bytes at {offset} exceed the {buffer.Length}-byte bank" );
				continue;
			}
			try
			{
				soundFiles.Add( mp2Reader.GetFile( memoryStream, offset ) );
			}
			catch ( Exception exception ) when ( exception is ArgumentException or IOException or InvalidDataException )
			{
				Skip( $"entry {i}: {exception.Message}" );
			}
		}
	}

	public string[] GetFiles( string internalPath )
	{
		return soundFiles.Select( x => x.Name ).ToArray();
	}

	public string[] GetDirectories( string internalPath )
	{
		// No directories in SDT files
		return Array.Empty<string>();
	}

	private void Skip( string reason )
	{
		SkippedEntries.Add( reason );
		Diagnostic?.Invoke( $"SDT bank: skipped {reason}." );
	}

	public ArchiveFile GetFile( string name )
	{
		int index = soundFiles.FindIndex( x => x.Name.StartsWith( name, StringComparison.OrdinalIgnoreCase ) );
		if ( index < 0 )
			throw new FileNotFoundException( $"'{name}' is not in this SDT bank{(SkippedEntries.Count > 0 ? $" ({SkippedEntries.Count} damaged entries were skipped)" : "")}.", name );
		return soundFiles[index];
	}

	public byte[] GetData( int offset, int length )
	{
		memoryStream.Seek( offset, SeekOrigin.Begin );
		return memoryStream.ReadBytes( length );
	}

	public Stream OpenFile( string path )
	{
		return new MemoryStream( GetFile( path ).GetData() );
	}

	public long GetFileSize( string path )
	{
		return GetFile( path ).GetData().Length;
	}

	public DateTime GetModifiedTime()
	{
		return DateTime.UnixEpoch;
	}
}
