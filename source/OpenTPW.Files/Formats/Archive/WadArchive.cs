namespace OpenTPW;

/*
 * Special thanks to Fatbag: http://wiki.niotso.org/RefPack, 
 * WATTO: http://wiki.xentax.com/index.php/WAD_DWFB, 
 * and Rhys: https://github.com/riperiperi/FreeSO/blob/master/TSOClient/tso.files/FAR3/Decompresser.cs.
 */

public sealed class WadArchiveFile : ArchiveFile
{
	internal bool Compressed { get; set; }
	internal int SizeInArchive { get; set; }
	/// <summary>
	/// The directory entry's declared decompressed size (+0x18). [BIN:TPW-EXE:0x00637690 WAD member size] the original takes it as a
	/// compressed member's size and, opening it (0x61A800 → 0x638B20), allocates exactly that many bytes for the Refpack output
	/// (decoder 0x641F10, which takes no capacity and checks nothing); a raw member's size is its data length. In all 6,804
	/// shipped Refpack members it equals the decoded length, and raw members declare 0.
	/// </summary>
	internal uint DeclaredSize { get; set; }

	/// <summary>The member's size as the original reports it, without decompressing it.</summary>
	public long Size => Compressed ? DeclaredSize : SizeInArchive;
	internal int OffsetInArchive { get; set; } 

	internal byte[]? CachedData { get; set; }

	public override byte[] GetData()
	{
		if ( CachedData != null )
			return CachedData;

		var data = Archive.GetData( OffsetInArchive, SizeInArchive );

		// Decompress file if necessary
		if ( Compressed )
		{
			using var profile = LoadProfile.Measure( "wad-decompress" );
			var refpack = new Refpack( data );
			data = refpack.Decompress();
		}

		// Save off data so we can use it again quickly if we need to
		CachedData = data;
		Interlocked.Add( ref WadArchive.cachedMemberBytes, data.Length );

		return data;
	}

	public void Free()
	{
		if ( CachedData != null )
			Interlocked.Add( ref WadArchive.cachedMemberBytes, -CachedData.Length );
		CachedData = null;
	}
}

public sealed class WadArchive : IArchive
{
	internal static long loadedBytes;
	internal static long cachedMemberBytes;

	/// <summary>
	/// Bytes of the archive files read into memory so far (archives are never closed, so this is also what they hold) and of the members
	/// kept decompressed now, for the memory accounting of the browser harness.
	/// </summary>
	public static (long Archives, long Members) MemoryInUse => (Interlocked.Read( ref loadedBytes ), Interlocked.Read( ref cachedMemberBytes ));

	private ExpandedMemoryStream? memoryStream;
	public byte[] Buffer { get; internal set; }

	/// <summary>
	/// The root directory ("/") for this archive.
	/// This may contain a collection of <see cref="ArchiveItem"/>s as children.
	/// </summary>
	public ArchiveDirectory Root { get; internal set; }

	/// <summary>
	/// The file an archive opened from a path reads its members from, one read per member, instead of holding the whole file:
	/// the archives of a park are tens of megabytes, and the browser already holds the files (docs/BROWSER-PERFORMANCE.md).
	/// </summary>
	private readonly string? path;

	public WadArchive( string path )
	{
		this.path = path;
		using var profile = LoadProfile.Measure( "wad-index" );
		using var fileStream = File.OpenRead( path );
		LoadProfile.Count( "file opens" );
		LoadProfile.Count( "wad archives indexed" );
		LoadProfile.Count( "bytes read", fileStream.Length );
		ReadFromStream( fileStream, keepInMemory: false );
	}

	public WadArchive( Stream stream )
	{
		ReadFromStream( stream );
	}

	public void Dispose()
	{
		lock ( readLock )
		{
			disposed = true;
			memoryStream?.Dispose();
		}
	}

	private bool disposed;

	private void ReadArchive()
	{
		if ( memoryStream!.Length < 88 )
			throw new InvalidDataException( "WAD header is truncated." );
		memoryStream.Seek( 0, SeekOrigin.Begin );

		/* 
		 * Header:
		 * 4 - magic number ('DWFB')
		 * 4 - version
		 * 64 - padding??
		 * 4 - file count
		 * 4 - file list offset
		 * 4 - file list length
		 * 4 - null
		 */

		var magicNumber = memoryStream.ReadString( 4 );

		// Magic number
		if ( magicNumber != "DWFB" )
			throw new InvalidDataException( $"WAD magic number did not match: {magicNumber}" );

		// Version
		_ = memoryStream.ReadInt32();

		// Padding
		memoryStream.Seek( 64, SeekOrigin.Current ); // Skip padding

		// File count
		var fileCount = memoryStream.ReadInt32();
		if ( fileCount < 0 || (long)fileCount * 40 > memoryStream.Length - 88 )
			throw new InvalidDataException( "WAD directory count exceeds its available records." );

		// File list offset
		_ = memoryStream.ReadInt32();

		// File list length
		_ = memoryStream.ReadInt32();

		// Unused / unknown
		_ = memoryStream.ReadInt32();

		//
		// Whenever a filename contains a directory name (e.g. "textures/hi.wct"),
		// every file that comes after it will omit that directory name - but should
		// be considered part of the subdirectory.
		//
		var currentSubdirectory = "";

		// Details directory
		for ( var i = 0; i < fileCount; ++i )
		{
			/*
			 * File
			 * 4 - unused
			 * 4 - filename offset
			 * 4 - filename length
			 * 4 - data offset
			 * 4 - data length
			 * 4 - compression type ('4' for refpack)
			 * 4 - decompressed size
			 * 12 - null
			 */

			// Save the current position so that we can go back to it later
			var initialPos = memoryStream.Position;

			// Unused / unknown
			memoryStream.Seek( 4, SeekOrigin.Current );

			var newFile = new WadArchiveFile
			{
				Archive = this
			};

			// Filename offset
			var filenameOffset = memoryStream.ReadUInt32();

			// Filename length
			var filenameLength = memoryStream.ReadUInt32();

			// Data offset
			var dataOffset = memoryStream.ReadUInt32();

			// Data length
			var dataLength = memoryStream.ReadUInt32();

			// Compression type
			newFile.Compressed = memoryStream.ReadUInt32() == 4;

			// Decompressed size (the size the original reports for a compressed member)
			newFile.DeclaredSize = memoryStream.ReadUInt32();

			if ( filenameLength == 0 )
				throw new InvalidDataException( "WAD filename length does not include its terminator." );
			RequireRange( filenameOffset, filenameLength, "filename" );
			RequireRange( dataOffset, dataLength, "member" );
			newFile.OffsetInArchive = (int)dataOffset;
			newFile.SizeInArchive = (int)dataLength;

			// Set file's name name
			memoryStream.Seek( filenameOffset, SeekOrigin.Begin );
			newFile.Name = memoryStream.ReadString( (int)filenameLength - 1 ); // String is null terminated

			//
			// Retrieve the target subdirectory within the archive's
			// file tree (and add to the tree if necessary)
			//
			ArchiveDirectory? subDirectory = Root;

			if ( newFile.Name.Contains( '\\' ) )
			{
				currentSubdirectory = newFile.Name[..(newFile.Name.LastIndexOf( "\\" ))];
				newFile.Name = newFile.Name[(newFile.Name.LastIndexOf( "\\" ) + 1)..];
			}

			// Are we currently in the root directory?
			if ( !string.IsNullOrEmpty( currentSubdirectory ) )
			{
				// Find a subdirectory object..
				var splitPath = currentSubdirectory.Split( "\\" );

				foreach ( string dir in splitPath )
				{
					ArchiveDirectory newSubDir;

					newSubDir = subDirectory.Children.OfType<ArchiveDirectory>().FirstOrDefault( x => x.Name == dir );

					if ( newSubDir == null )
					{
						// ..or create one if it doesn't exist
						newSubDir = new ArchiveDirectory( dir );
						subDirectory.Children.Add( newSubDir );
						subDirectory = newSubDir;
					}

					subDirectory = newSubDir;
				}
			}

			// Add to selected subdirectory
			subDirectory.Children.Add( newFile );

			// Return to initial position, skip to the next file's data
			memoryStream.Seek( initialPos + 40, SeekOrigin.Begin );
		}

		void RequireRange( uint offset, uint length, string section )
		{
			if ( (ulong)offset + length > (ulong)memoryStream!.Length )
				throw new InvalidDataException( $"WAD {section} range exceeds the archive." );
		}
	}

	public void ReadFromStream( Stream stream ) => ReadFromStream( stream, keepInMemory: true );

	/// <summary>Reads the directory; with <paramref name="keepInMemory"/> false (an archive with a path) the file's bytes are dropped afterwards.</summary>
	private void ReadFromStream( Stream stream, bool keepInMemory )
	{
		// Set up read buffer
		using var tempStreamReader = new StreamReader( stream );
		var remaining = tempStreamReader.BaseStream.Length - tempStreamReader.BaseStream.Position;
		if ( remaining < 0 || remaining > Array.MaxLength )
			throw new InvalidDataException( "WAD input exceeds the supported managed buffer range." );
		var fileLength = (int)remaining;

		var bytes = new byte[fileLength];
		try
		{
			tempStreamReader.BaseStream.ReadExactly( bytes );
		}
		catch ( EndOfStreamException exception )
		{
			throw new InvalidDataException( "WAD input stream is truncated.", exception );
		}
		tempStreamReader.Close();

		memoryStream = new ExpandedMemoryStream( bytes );
		Root = new ArchiveDirectory( "/" );

		ReadArchive();

		if ( keepInMemory )
		{
			Buffer = bytes;
			Interlocked.Add( ref loadedBytes, fileLength );
		}
		else
		{
			memoryStream.Dispose();
			memoryStream = null;
			Buffer = Array.Empty<byte>();
			fileSize = fileLength;
		}
	}

	private long fileSize;

	private T GetItem<T>( string internalPath ) where T : ArchiveItem
	{
		var internalDirectory = Root;

		if ( internalPath == "" )
			throw new Exception( $"Path was empty" );

		var splitPath = internalPath.Split( Path.DirectorySeparatorChar );

		for ( int i = 0; i < splitPath.Length; i++ )
		{
			string dir = splitPath[i];

			if ( i == splitPath.Length - 1 )
			{
				return internalDirectory.Children.OfType<T>().FirstOrDefault( x => x.Name.Equals( dir, StringComparison.CurrentCultureIgnoreCase ) );
			}

			internalDirectory = internalDirectory.Children.OfType<ArchiveDirectory>().First( x => x.Name.Equals( dir, StringComparison.CurrentCultureIgnoreCase ) );
		}

		throw new FileNotFoundException( $"File not found: {internalPath}" );
	}

	private List<T> EnumerateItems<T>( string internalPath ) where T : ArchiveItem
	{
		var internalDirectory = Root;

		if ( internalPath != "" )
		{
			var splitPath = internalPath.Split( Path.DirectorySeparatorChar );

			foreach ( string dir in splitPath )
			{
				internalDirectory = internalDirectory.Children.OfType<ArchiveDirectory>().FirstOrDefault( x => x.Name.Equals( dir, StringComparison.CurrentCultureIgnoreCase ) );
			}
		}

		return internalDirectory?.Children.OfType<T>().ToList() ?? null;
	}

	public ArchiveFile GetFile( string internalPath )
	{
		return GetItem<ArchiveFile>( internalPath );
	}

	public ArchiveDirectory GetDirectory( string internalPath )
	{
		return GetItem<ArchiveDirectory>( internalPath );
	}

	public string[] GetFiles( string internalPath )
	{
		var files = EnumerateItems<ArchiveFile>( internalPath );
		return (files?.Select( x => x.Name ).ToArray() ?? Array.Empty<string>())!;
	}

	public string[] GetDirectories( string internalPath )
	{
		var files = EnumerateItems<ArchiveDirectory>( internalPath );
		return (files?.Select( x => x.Name ).ToArray() ?? Array.Empty<string>())!;
	}

	private readonly object readLock = new();

	/// <summary>Seek and read are one step: a texture pack switch reads members on a worker thread while the game thread may read the same archive.</summary>
	public byte[] GetData( int offset, int length )
	{
		using var profile = LoadProfile.Measure( "wad-read" );
		ArgumentOutOfRangeException.ThrowIfNegative( offset );
		ArgumentOutOfRangeException.ThrowIfNegative( length );
		lock ( readLock )
		{
			ObjectDisposedException.ThrowIf( disposed, this );
			if ( memoryStream != null )
			{
				memoryStream.Seek( offset, SeekOrigin.Begin );
				return memoryStream.ReadBytes( length );
			}
			// From the file, the same bytes the in-memory copy gave: a range past its end comes back padded with zeros.
			if ( offset >= fileSize || length == 0 )
				return new byte[length];
			// Opened per read and closed again, so the file can still be replaced or deleted (a bonus import on Windows) while the
			// archive is open; a file of another length is not the one whose directory was read.
			using var file = new FileStream( path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1 );
			LoadProfile.Count( "file opens" );
			LoadProfile.Count( "wad member reads" );
			LoadProfile.Count( "bytes read", length );
			if ( file.Length != fileSize )
				throw new IOException( $"{path} changed on disk after its directory was read." );
			var data = new byte[length];
			file.Seek( offset, SeekOrigin.Begin );
			var filled = 0;
			while ( filled < length )
			{
				var read = file.Read( data, filled, length - filled );
				if ( read == 0 )
					break;
				filled += read;
			}
			return data;
		}
	}

	public Stream OpenFile( string path )
	{
		var file = GetFile( path );

		if ( file == null )
			return null;

		return new MemoryStream( file.GetData() );
	}

	public long GetFileSize( string path )
	{
		var file = GetFile( path );
		return file is WadArchiveFile member ? member.Size : file.GetData().Length;
	}

	public DateTime GetModifiedTime()
	{
		return DateTime.UnixEpoch;
	}
}
