using System.Text;

namespace OpenTPW;

public class BaseFileSystem
{
	private readonly string basePath;
	private readonly Dictionary<string, Type> archiveHandlers = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, IArchive> archiveCache = new( OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal );

	public BaseFileSystem( string relativePath )
	{
		basePath = Path.GetFullPath( NormalizeSeparators( relativePath ) );
		Directory.CreateDirectory( basePath );
	}

	/// <summary>
	/// Read-only fallback for files missing from this root (e.g. <c>--cd-data</c> movies and music):
	/// maps a relative path to another file system and its on-disk (case-corrected) relative path, or
	/// null. Only <see cref="OpenRead"/>, <see cref="FileExists"/> and <see cref="GetSize"/> consult it;
	/// directory listings stay those of this root.
	/// </summary>
	public Func<string, (BaseFileSystem FileSystem, string RelativePath)?>? FallbackResolver { get; set; }

	private bool ExistsHere( string absolutePath ) =>
		File.Exists( absolutePath ) || Directory.Exists( absolutePath ) || !string.IsNullOrEmpty( FindArchivePath( absolutePath ).ArchivePath );

	private (BaseFileSystem FileSystem, string RelativePath)? Fallback( string relativePath, string absolutePath ) =>
		FallbackResolver != null && !ExistsHere( absolutePath ) ? FallbackResolver( GetRelativePath( absolutePath ) ) : null;

	public void RegisterArchiveHandler<T>( string extension ) where T : IArchive
	{
		archiveHandlers[extension] = typeof( T );
	}

	public string ReadAllText( string relativePath )
	{
		using var stream = OpenRead( relativePath );
		using var reader = new StreamReader( stream, Encoding.ASCII );
		return reader.ReadToEnd();
	}

	public byte[] ReadAllBytes( string relativePath )
	{
		using var stream = OpenRead( relativePath );
		using var ms = new MemoryStream();
		stream.CopyTo( ms );
		return ms.ToArray();
	}

	public bool FileExists( string relativePath )
	{
		var absolutePath = GetAbsolutePath( relativePath );
		if ( Fallback( relativePath, absolutePath ) is { } fallback )
			return fallback.FileSystem.FileExists( fallback.RelativePath );
		return File.Exists( absolutePath );
	}

	public bool DirectoryExists( string relativePath )
	{
		var absolutePath = GetAbsolutePath( relativePath );
		return Directory.Exists( absolutePath );
	}

	public Stream OpenWrite( string relativePath )
	{
		var absolutePath = GetAbsolutePath( relativePath );
		var (archivePath, _) = FindArchivePath( absolutePath );

		if ( !string.IsNullOrEmpty( archivePath ) )
		{
			throw new NotImplementedException( "Can't write to archives" );
		}

		string? directoryName = Path.GetDirectoryName( absolutePath );
		if ( !Directory.Exists( directoryName ) )
			Directory.CreateDirectory( directoryName );

		return File.OpenWrite( absolutePath );
	}

	public Stream OpenRead( string relativePath )
	{
		var absolutePath = GetAbsolutePath( relativePath );
		if ( Fallback( relativePath, absolutePath ) is { } fallback )
			return fallback.FileSystem.OpenRead( fallback.RelativePath );
		var (archivePath, internalPath) = FindArchivePath( absolutePath );

		if ( !string.IsNullOrEmpty( archivePath ) )
		{
			var archive = GetArchive( archivePath );
			return archive?.OpenFile( internalPath );
		}

		return File.Open( absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read );
	}

	public long GetSize( string relativePath )
	{
		var absolutePath = GetAbsolutePath( relativePath );
		if ( Fallback( relativePath, absolutePath ) is { } fallback )
			return fallback.FileSystem.GetSize( fallback.RelativePath );
		var (archivePath, internalPath) = FindArchivePath( absolutePath );

		if ( !string.IsNullOrEmpty( archivePath ) )
		{
			var archive = GetArchive( archivePath );
			return archive?.GetFileSize( internalPath ) ?? 0L;
		}

		return new FileInfo( absolutePath ).Length;
	}

	public DateTime GetModifiedTime( string relativePath )
	{
		var absolutePath = GetAbsolutePath( relativePath );
		var (archivePath, internalPath) = FindArchivePath( absolutePath );

		if ( !string.IsNullOrEmpty( archivePath ) )
		{
			var archive = GetArchive( archivePath );
			return archive?.GetModifiedTime() ?? DateTime.UnixEpoch;
		}

		return new FileInfo( absolutePath ).LastWriteTime;
	}

	public string[] GetFiles( string relativePath )
	{
		return GetFileSystemEntries( relativePath, false );
	}

	public string[] GetDirectories( string relativePath )
	{
		return GetFileSystemEntries( relativePath, true );
	}

	private string[] GetFileSystemEntries( string relativePath, bool directories )
	{
		var absolutePath = GetAbsolutePath( relativePath );
		var (archivePath, internalPath) = FindArchivePath( absolutePath, includeExplicitRoot: true );

		if ( !string.IsNullOrEmpty( archivePath ) )
		{
			var archive = GetArchive( archivePath );
			var entries = directories ? archive.GetDirectories( internalPath ) : archive.GetFiles( internalPath );
			return entries.Select( entry => Path.Combine( NormalizeSeparators( relativePath ), NormalizeSeparators( entry ) ) ).ToArray();
		}

		if ( directories )
		{
			var fileSystemDirectories = Directory.GetDirectories( absolutePath );
			var fileSystemArchives = Directory.GetFiles( absolutePath ).Where( x => archiveHandlers.ContainsKey( Path.GetExtension( x ) ) ).Select( x => x[..x.LastIndexOf( "." )] );

			return fileSystemDirectories.Concat( fileSystemArchives ).ToArray();
		}
		else
		{
			return Directory.GetFiles( absolutePath ).Where( x => !archiveHandlers.ContainsKey( Path.GetExtension( x ) ) ).ToArray();
		}
	}

	private IArchive GetArchive( string archivePath )
	{
		if ( archiveCache.TryGetValue( archivePath, out var archive ) )
		{
			return archive;
		}

		var extension = Path.GetExtension( archivePath );
		if ( archiveHandlers.TryGetValue( extension, out var handlerType ) )
		{
			archive = (IArchive)Activator.CreateInstance( handlerType, new[] { archivePath } );
			archiveCache[archivePath] = archive;
			return archive;
		}

		return null;
	}

	private (string ArchivePath, string InternalPath) FindArchivePath( string path, bool includeExplicitRoot = false )
	{
		path = ValidateAbsolutePath( path );
		var parts = Path.GetRelativePath( basePath, path ).Split( Path.DirectorySeparatorChar );
		var currentPath = basePath;

		for ( var index = 0; index < parts.Length; index++ )
		{
			var part = parts[index];
			currentPath = Path.Combine( currentPath, part );
			var parentPath = Path.GetDirectoryName( currentPath );
			if ( parentPath == null || !Directory.Exists( parentPath ) )
			{
				break;
			}

			foreach ( var candidate in Directory.EnumerateFiles( parentPath ) )
			{
				if ( !archiveHandlers.ContainsKey( Path.GetExtension( candidate ) ) )
					continue;

				var remainingPath = string.Join( Path.DirectorySeparatorChar, parts.Skip( index + 1 ) );
				var extensionlessMatch = string.Equals( Path.GetFileNameWithoutExtension( candidate ), part, StringComparison.OrdinalIgnoreCase );
				var explicitMatch = (includeExplicitRoot || remainingPath.Length > 0) && string.Equals( Path.GetFileName( candidate ), part, StringComparison.OrdinalIgnoreCase );
				if ( extensionlessMatch || explicitMatch )
				{
					return (candidate, remainingPath);
				}
			}
		}

		return (string.Empty, path);
	}

	public string GetAbsolutePath( string relativePath )
	{
		var normalizedPath = NormalizeSeparators( relativePath );
		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		var rootPrefix = Path.TrimEndingDirectorySeparator( basePath ) + Path.DirectorySeparatorChar;
		if ( string.Equals( normalizedPath, Path.TrimEndingDirectorySeparator( basePath ), comparison ) || normalizedPath.StartsWith( rootPrefix, comparison ) || (OperatingSystem.IsWindows() && Path.IsPathFullyQualified( normalizedPath )) )
			return ValidateAbsolutePath( normalizedPath );

		var path = normalizedPath.TrimStart( Path.DirectorySeparatorChar );
		return MatchCase( ValidateAbsolutePath( Path.Combine( basePath, path ) ) );
	}

	/// <summary>
	/// On case-sensitive hosts (Linux, case-sensitive macOS volumes) the original data mixes
	/// spellings (<c>Data/global/Speech</c> on the install, <c>data/global/speech</c> on the CD)
	/// while code requests one fixed spelling. Corrects every existing segment below the root to
	/// its on-disk spelling; an exact match wins, and the first missing segment and everything
	/// after it (e.g. a path inside an archive) stay as requested. Windows is case-insensitive.
	/// </summary>
	private string MatchCase( string absolutePath )
	{
		if ( OperatingSystem.IsWindows() || File.Exists( absolutePath ) || Directory.Exists( absolutePath ) )
			return absolutePath;
		var relativePath = Path.GetRelativePath( basePath, absolutePath );
		if ( relativePath == "." )
			return absolutePath;
		var parts = relativePath.Split( Path.DirectorySeparatorChar );
		var current = basePath;
		for ( var index = 0; index < parts.Length; index++ )
		{
			var exact = Path.Combine( current, parts[index] );
			if ( File.Exists( exact ) || Directory.Exists( exact ) )
			{
				current = exact;
				continue;
			}
			var match = Directory.Exists( current )
				? Directory.EnumerateFileSystemEntries( current )
					.Where( entry => string.Equals( Path.GetFileName( entry ), parts[index], StringComparison.OrdinalIgnoreCase ) )
					.OrderBy( entry => entry, StringComparer.Ordinal )
					.FirstOrDefault()
				: null;
			if ( match == null )
				return Path.Combine( [current, .. parts[index..]] );
			current = match;
		}
		return current;
	}

	public string GetRelativePath( string absolutePath )
	{
		var path = Path.GetRelativePath( basePath, ValidateAbsolutePath( absolutePath ) ).Replace( "\\", "/" );
		return $"/{path}";
	}

	private static string NormalizeSeparators( string path )
	{
		return path.Replace( '\\', Path.DirectorySeparatorChar ).Replace( '/', Path.DirectorySeparatorChar );
	}

	private string ValidateAbsolutePath( string path )
	{
		var absolutePath = Path.GetFullPath( NormalizeSeparators( path ) );
		var relativePath = Path.GetRelativePath( basePath, absolutePath );
		if ( Path.IsPathRooted( relativePath ) || relativePath == ".." || relativePath.StartsWith( $"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal ) )
			throw new ArgumentException( "Path must remain inside the filesystem root.", nameof( path ) );

		return absolutePath;
	}

	public bool IsArchive( string path )
	{
		var normalizedPath = NormalizeSeparators( path );
		var absolutePath = Path.IsPathRooted( normalizedPath ) ? ValidateAbsolutePath( normalizedPath ) : GetAbsolutePath( normalizedPath );
		var (archivePath, _) = FindArchivePath( absolutePath );

		return !string.IsNullOrEmpty( archivePath );
	}

	public FileSystemWatcher CreateWatcher( string relativeDir, string filter )
	{
		var directoryName = GetAbsolutePath( relativeDir );
		var watcher = new FileSystemWatcher( directoryName, filter );

		watcher.NotifyFilter = NotifyFilters.Attributes
							 | NotifyFilters.CreationTime
							 | NotifyFilters.DirectoryName
							 | NotifyFilters.FileName
							 | NotifyFilters.LastAccess
							 | NotifyFilters.LastWrite
							 | NotifyFilters.Security
							 | NotifyFilters.Size;

		watcher.EnableRaisingEvents = true;

		return watcher;
	}
}
