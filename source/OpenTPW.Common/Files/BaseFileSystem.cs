using System.Collections.Concurrent;
using System.Text;

namespace OpenTPW;

/// <summary>Loose files that stand in for files of a <see cref="BaseFileSystem"/> (mod overrides, docs/MODS.md).</summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
public interface IFileOverrides
{
	/// <summary>The file on disk that replaces <paramref name="relativePath"/> (root-relative, '/' separators, any case), or false.</summary>
	bool TryGetFile( string relativePath, out string absolutePath );
}

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
	/// Remembers path lookups (the case correction of <see cref="GetAbsolutePath"/> and the archive search behind every read and
	/// listing) until <see cref="InvalidateLookups"/>. Each lookup otherwise lists the directories along the path; with many small
	/// files in archives that was a tenth of a park start in the browser (docs/BROWSER-PERFORMANCE.md). Only for a root whose files do
	/// not change behind its back (the game data): whoever adds files there calls <see cref="InvalidateLookups"/>, and writes through
	/// <see cref="OpenWrite"/> do so themselves. Lookups may run on several threads: each answer carries the generation it was found
	/// in, and one found before an invalidation is neither returned nor kept after it.
	/// </summary>
	public bool CacheLookups { get; init; }
	private readonly ConcurrentDictionary<string, (int Generation, string Value)> absoluteLookups = new();
	private readonly ConcurrentDictionary<(string Path, bool ExplicitRoot), (int Generation, (string ArchivePath, string InternalPath) Value)> archiveLookups = new();
	// What the searches behind both ask the disk: whether a path exists, and a directory's entries (null for no directory). A path inside an archive
	// is a miss of the case correction, which then tests and lists every directory along it; a park start asks that for hundreds of members of
	// the same few directories, each answer a walk of the browser's file system (docs/BROWSER-PERFORMANCE.md, "Event hitches").
	private readonly ConcurrentDictionary<string, (int Generation, bool Value)> existsLookups = new();
	private readonly ConcurrentDictionary<(string Directory, bool FilesOnly), (int Generation, string[]? Value)> listingLookups = new();
	private int lookupGeneration;

	/// <summary>
	/// Counts the invalidations (<see cref="InvalidateLookups"/>, and every change of <see cref="Overrides"/> or <see cref="ArchiveRedirect"/>): what was read from this
	/// file system under another generation may differ from what it reads now.
	/// </summary>
	public int LookupGeneration => Volatile.Read( ref lookupGeneration );

	/// <summary>Forgets the remembered lookups (<see cref="CacheLookups"/>): call after files are added below the root.</summary>
	public void InvalidateLookups()
	{
		Interlocked.Increment( ref lookupGeneration );
		absoluteLookups.Clear();
		archiveLookups.Clear();
		existsLookups.Clear();
		listingLookups.Clear();
	}

	/// <summary>Whether a file or directory is at <paramref name="absolutePath"/> on disk; remembered with <see cref="CacheLookups"/>.</summary>
	private bool ExistsOnDisk( string absolutePath ) =>
		CacheLookups ? Remembered( existsLookups, absolutePath, path => File.Exists( path ) || Directory.Exists( path ) ) : File.Exists( absolutePath ) || Directory.Exists( absolutePath );

	/// <summary>The files (or all entries) of a directory on disk in the order the system lists them, or null when it is no directory; remembered with <see cref="CacheLookups"/>.</summary>
	private string[]? DiskEntries( string directory, bool filesOnly ) =>
		CacheLookups ? Remembered( listingLookups, (directory, filesOnly), key => ListDisk( key.Item1, key.Item2 ) ) : ListDisk( directory, filesOnly );

	private static string[]? ListDisk( string directory, bool filesOnly ) =>
		!Directory.Exists( directory ) ? null : filesOnly ? Directory.EnumerateFiles( directory ).ToArray() : Directory.EnumerateFileSystemEntries( directory ).ToArray();

	/// <summary>The remembered answer for <paramref name="key"/> of this generation, else a fresh one, kept only if no invalidation came between.</summary>
	private TValue Remembered<TKey, TValue>( ConcurrentDictionary<TKey, (int Generation, TValue Value)> lookups, TKey key, Func<TKey, TValue> find ) where TKey : notnull
	{
		var generation = Volatile.Read( ref lookupGeneration );
		if ( lookups.TryGetValue( key, out var known ) && known.Generation == generation )
			return known.Value;
		var value = find( key );
		if ( Volatile.Read( ref lookupGeneration ) == generation )
			lookups[key] = (generation, value);
		return value;
	}

	/// <summary>
	/// Read-only fallback for files missing from this root (e.g. <c>--cd-data</c> movies and music):
	/// maps a relative path to another file system and its on-disk (case-corrected) relative path, or
	/// null. Only <see cref="OpenRead"/>, <see cref="FileExists"/> and <see cref="GetSize"/> consult it;
	/// directory listings stay those of this root.
	/// </summary>
	public Func<string, (BaseFileSystem FileSystem, string RelativePath)?>? FallbackResolver { get; set; }

	/// <summary>
	/// Replacement files consulted before the root's own: <see cref="OpenRead"/>, <see cref="FileExists"/>, <see cref="GetSize"/> and
	/// <see cref="GetModifiedTime"/> of a path they know read the replacement instead. Directory listings and writes stay those of this root.
	/// Null (the default) changes nothing. Set once at startup, before the first lookup that should see it; call
	/// <see cref="InvalidateLookups"/> after changing it.
	/// </summary>
	// [EXT:mods] OpenTPW's own mod system; the original game had no mods
	public IFileOverrides? Overrides
	{
		get => overrides;
		set
		{
			overrides = value;
			Interlocked.Increment( ref lookupGeneration );
		}
	}
	private IFileOverrides? overrides;

	/// <summary>
	/// Archives of this root that another archive file stands in for: called with the absolute path of an archive found below the root (e.g.
	/// <c>…/data/ui.wad</c>), it returns the archive to read instead (the selected language's own <c>ui.wad</c>, docs/LANGUAGES.md) or null for the
	/// root's own. Every read, listing and size of a path inside the archive goes to the replacement. Null (the default) changes nothing. Call
	/// <see cref="InvalidateLookups"/> after changing it.
	/// </summary>
	public Func<string, string?>? ArchiveRedirect
	{
		get => archiveRedirect;
		set
		{
			archiveRedirect = value;
			Interlocked.Increment( ref lookupGeneration );
		}
	}
	private Func<string, string?>? archiveRedirect;

	private string? Overridden( string relativePath ) =>
		Overrides != null && Overrides.TryGetFile( GetRelativePath( GetAbsolutePath( relativePath ) ), out var file ) ? file : null;

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
		if ( Overridden( relativePath ) != null )
			return true;
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
		if ( CacheLookups )
			InvalidateLookups();
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
		if ( Overridden( relativePath ) is { } overridden )
			return File.Open( overridden, FileMode.Open, FileAccess.Read, FileShare.Read );
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
		if ( Overridden( relativePath ) is { } overridden )
			return new FileInfo( overridden ).Length;
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
		if ( Overridden( relativePath ) is { } overridden )
			return new FileInfo( overridden ).LastWriteTime;
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

	private (string ArchivePath, string InternalPath) FindArchivePath( string path, bool includeExplicitRoot = false ) =>
		CacheLookups ? Remembered( archiveLookups, (Path: path, ExplicitRoot: includeExplicitRoot), key => SearchArchivePath( key.Item1, key.Item2 ) ) : SearchArchivePath( path, includeExplicitRoot );

	private (string ArchivePath, string InternalPath) SearchArchivePath( string path, bool includeExplicitRoot )
	{
		path = ValidateAbsolutePath( path );
		var parts = Path.GetRelativePath( basePath, path ).Split( Path.DirectorySeparatorChar );
		var currentPath = basePath;

		for ( var index = 0; index < parts.Length; index++ )
		{
			var part = parts[index];
			currentPath = Path.Combine( currentPath, part );
			var parentPath = Path.GetDirectoryName( currentPath );
			var parentFiles = parentPath == null ? null : DiskEntries( parentPath, filesOnly: true );
			if ( parentFiles == null )
			{
				break;
			}

			foreach ( var candidate in parentFiles )
			{
				if ( !archiveHandlers.ContainsKey( Path.GetExtension( candidate ) ) )
					continue;

				var remainingPath = string.Join( Path.DirectorySeparatorChar, parts.Skip( index + 1 ) );
				var extensionlessMatch = string.Equals( Path.GetFileNameWithoutExtension( candidate ), part, StringComparison.OrdinalIgnoreCase );
				var explicitMatch = (includeExplicitRoot || remainingPath.Length > 0) && string.Equals( Path.GetFileName( candidate ), part, StringComparison.OrdinalIgnoreCase );
				if ( extensionlessMatch || explicitMatch )
				{
					return (ArchiveRedirect?.Invoke( candidate ) ?? candidate, remainingPath);
				}
			}
		}

		return (string.Empty, path);
	}

	public string GetAbsolutePath( string relativePath ) =>
		CacheLookups ? Remembered( absoluteLookups, relativePath, ResolveAbsolutePath ) : ResolveAbsolutePath( relativePath );

	private string ResolveAbsolutePath( string relativePath )
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
		if ( OperatingSystem.IsWindows() )
			return absolutePath;
		// With remembered answers the walk below asks the disk about each directory along the path once, and a path that exists comes out of it as
		// requested; testing the whole path first would ask once more for every path, and a park start resolves hundreds of archive members.
		if ( (!CacheLookups || Path.EndsInDirectorySeparator( absolutePath )) && ExistsOnDisk( absolutePath ) )
			return absolutePath;
		var relativePath = Path.GetRelativePath( basePath, absolutePath );
		if ( relativePath == "." )
			return absolutePath;
		var parts = relativePath.Split( Path.DirectorySeparatorChar );
		var current = basePath;
		for ( var index = 0; index < parts.Length; index++ )
		{
			var exact = Path.Combine( current, parts[index] );
			if ( ExistsOnDisk( exact ) )
			{
				current = exact;
				continue;
			}
			var match = DiskEntries( current, filesOnly: false ) is { } entries
				? entries
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

	/// <summary>
	/// A watcher for the directory, not yet started: subscribe to its events, then set
	/// <see cref="FileSystemWatcher.EnableRaisingEvents"/>. On macOS starting a watcher calls <c>sync()</c>, which
	/// waits for every mounted file system, so it should only happen where watching is actually wanted.
	/// </summary>
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

		return watcher;
	}
}
