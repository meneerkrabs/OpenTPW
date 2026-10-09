namespace OpenTPW;

/// <summary>A member of a PS2 <see cref="FknlArchive"/>.</summary>
public sealed class FknlArchiveFile : ArchiveFile
{
	internal int Offset { get; init; }
	internal int StoredSize { get; init; }
	public int Size { get; init; }

	/// <summary>True for the few entries that record a size but store no bytes (three <c>.dup</c> files on the PAL disc).</summary>
	public bool IsEmptyPlaceholder => StoredSize == 0 && Size > 0;

	public override byte[] GetData()
	{
		if ( IsEmptyPlaceholder )
			return Array.Empty<byte>();
		var data = Archive!.GetData( Offset, StoredSize );
		if ( StoredSize == Size )
			return data;
		if ( data.Length < 2 || data[0] != 0x10 || data[1] != 0xFB )
			throw new InvalidDataException( $"FKNL member {Name} is smaller than its size ({StoredSize} < {Size}) but has no RefPack header." );
		var result = new Refpack( data ).Decompress().ToArray();
		if ( result.Length != Size )
			throw new InvalidDataException( $"FKNL member {Name} decompressed to {result.Length} bytes, expected {Size}." );
		return result;
	}
}

/// <summary>
/// The PS2 version's archive (<c>DATA/*.WAD</c> on the PAL disc SLES-50032), magic <c>FKNL</c>; unrelated to
/// the PC/Mac DWFB <see cref="WadArchive"/> despite the extension. Layout (little-endian, docs/PS2.md):
/// header <c>FKNL</c>, 0, data start, unknown word, name-pool start, root record offset. A directory
/// record is (file entries offset, subdirectory entries offset, file count, subdirectory count); a
/// file entry is (name offset, data offset, stored size, size) and is RefPack-compressed when the
/// stored size is smaller; a subdirectory entry is (name offset, record offset). Names are
/// NUL-terminated in the pool. Lookups ignore case and accept <c>/</c> and <c>\</c>.
/// </summary>
public sealed class FknlArchive : IArchive
{
	public const int MaximumDepth = 64;
	public const int MaximumEntries = 1_000_000;

	private byte[] data = Array.Empty<byte>();
	private readonly ArchiveDirectory root = new( "" );

	public FknlArchive( string path )
	{
		using var stream = File.OpenRead( path );
		ReadFromStream( stream );
	}

	public FknlArchive( Stream stream ) => ReadFromStream( stream );

	public static bool IsFknl( ReadOnlySpan<byte> header ) =>
		header.Length >= 4 && header[0] == (byte)'F' && header[1] == (byte)'K' && header[2] == (byte)'N' && header[3] == (byte)'L';

	public int FileCount { get; private set; }

	public void ReadFromStream( Stream stream )
	{
		using var buffer = new MemoryStream();
		stream.CopyTo( buffer );
		data = buffer.ToArray();
		if ( data.Length < 24 || !IsFknl( data ) )
			throw new InvalidDataException( "Not an FKNL archive." );
		var dataStart = Word( 8 );
		var nameStart = Word( 16 );
		var rootRecord = Word( 20 );
		if ( nameStart > data.Length || dataStart > data.Length || nameStart > dataStart )
			throw new InvalidDataException( "FKNL header offsets are outside the file." );
		var visited = new HashSet<int>();
		var entries = 0;

		void Read( int record, ArchiveDirectory directory, int depth )
		{
			if ( depth > MaximumDepth )
				throw new InvalidDataException( "FKNL directory tree is too deep." );
			if ( !visited.Add( record ) )
				throw new InvalidDataException( "FKNL directory tree has a cycle." );
			var filesAt = Word( record );
			var directoriesAt = Word( record + 4 );
			var fileCount = Word( record + 8 );
			var directoryCount = Word( record + 12 );
			entries += fileCount + directoryCount;
			if ( entries > MaximumEntries )
				throw new InvalidDataException( "FKNL archive has too many entries." );
			for ( var i = 0; i < fileCount; i++ )
			{
				var entry = Checked( filesAt, i, 16 );
				var offset = Word( entry + 4 );
				var stored = Word( entry + 8 );
				var size = Word( entry + 12 );
				if ( (long)offset + stored > data.Length || stored > size )
					throw new InvalidDataException( $"FKNL file entry {i} at {entry} points outside the file." );
				directory.Children.Add( new FknlArchiveFile { Name = Name( Word( entry ), nameStart, dataStart ), Offset = offset, StoredSize = stored, Size = size, Archive = this } );
				FileCount++;
			}
			for ( var i = 0; i < directoryCount; i++ )
			{
				var entry = Checked( directoriesAt, i, 8 );
				var child = new ArchiveDirectory( Name( Word( entry ), nameStart, dataStart ) );
				directory.Children.Add( child );
				Read( Word( entry + 4 ), child, depth + 1 );
			}
		}

		Read( rootRecord, root, 0 );
	}

	private int Word( int offset )
	{
		if ( offset < 0 || offset + 4 > data.Length )
			throw new InvalidDataException( $"FKNL read at {offset} is outside the file." );
		var value = BitConverter.ToUInt32( data, offset );
		if ( value > int.MaxValue )
			throw new InvalidDataException( $"FKNL value at {offset} is out of range." );
		return (int)value;
	}

	private int Checked( int table, int index, int size )
	{
		var offset = (long)table + (long)index * size;
		if ( offset + size > data.Length )
			throw new InvalidDataException( "FKNL table runs past the end of the file." );
		return (int)offset;
	}

	private string Name( int offset, int nameStart, int dataStart )
	{
		if ( offset < nameStart || offset >= dataStart )
			throw new InvalidDataException( $"FKNL name offset {offset} is outside the name pool." );
		var end = Array.IndexOf( data, (byte)0, offset, dataStart - offset );
		if ( end < 0 )
			throw new InvalidDataException( $"FKNL name at {offset} is not terminated." );
		return System.Text.Encoding.Latin1.GetString( data, offset, end - offset );
	}

	private ArchiveItem? Find( string internalPath )
	{
		ArchiveItem current = root;
		foreach ( var segment in internalPath.Split( new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries ) )
		{
			if ( current is not ArchiveDirectory directory )
				return null;
			var next = directory.Children.FirstOrDefault( child => string.Equals( child.Name, segment, StringComparison.OrdinalIgnoreCase ) );
			if ( next == null )
				return null;
			current = next;
		}
		return current;
	}

	public string[] GetFiles( string internalPath ) =>
		Find( internalPath ) is ArchiveDirectory directory ? directory.Children.OfType<ArchiveFile>().Select( file => file.Name! ).ToArray() : Array.Empty<string>();

	public string[] GetDirectories( string internalPath ) =>
		Find( internalPath ) is ArchiveDirectory directory ? directory.Children.OfType<ArchiveDirectory>().Select( child => child.Name! ).ToArray() : Array.Empty<string>();

	public ArchiveFile GetFile( string internalPath ) =>
		Find( internalPath ) as ArchiveFile ?? throw new FileNotFoundException( $"'{internalPath}' is not in this FKNL archive.", internalPath );

	/// <summary>Every file path in the archive, depth first, with <c>/</c> separators.</summary>
	public IEnumerable<string> EnumerateFiles()
	{
		var stack = new Stack<(ArchiveDirectory Directory, string Prefix)>();
		stack.Push( (root, "") );
		while ( stack.Count > 0 )
		{
			var (directory, prefix) = stack.Pop();
			foreach ( var child in directory.Children )
				if ( child is ArchiveDirectory sub )
					stack.Push( (sub, $"{prefix}{sub.Name}/") );
				else
					yield return prefix + child.Name;
		}
	}

	public byte[] GetData( int offset, int length ) => data.AsSpan( offset, length ).ToArray();

	public Stream OpenFile( string path ) => new MemoryStream( GetFile( path ).GetData() );

	public long GetFileSize( string path ) => ((FknlArchiveFile)GetFile( path )).Size;

	public DateTime GetModifiedTime() => DateTime.UnixEpoch;

	public void Dispose() => data = Array.Empty<byte>();
}
