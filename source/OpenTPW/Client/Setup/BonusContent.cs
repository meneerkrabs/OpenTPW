using System.IO.Compression;
using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>Where the official bonus content root came from, for the startup log.</summary>
public enum BonusPathSource { CommandLine, Environment, Saved, ConfigFolder }

/// <summary>
/// Official bonus content (docs/OBJECTS.md) without a flag: chooses the bonus root at start-up and imports
/// the original "Bonus content" folder or zip into <c>&lt;config&gt;/bonus</c> (docs/SETUP.md). Only
/// <c>levels/&lt;theme&gt;/&lt;category&gt;/_name_N.wad</c> files are copied; the game folder is never written.
/// </summary>
// [EXT:bonus-content] Setup-managed copy of the official bonus WADs; the original game only had the CD and install folders
public static class BonusContent
{
	/// <summary>Folder name inside the user configuration directory.</summary>
	public const string FolderName = "bonus";

	private static readonly Regex BonusFileName = new( @"^_.*_.*\.wad$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant );

	/// <summary>
	/// The bonus root: <c>--bonus-data</c>, then <c>OPENTPW_BONUS_DATA</c>, then the saved <c>BonusPath</c>, then
	/// <c>&lt;config&gt;/bonus</c> when it holds a levels folder. An empty saved path means the player removed the
	/// bonus content, which also stops the configuration folder from being used. Null when there is none.
	/// </summary>
	public static (string Path, BonusPathSource Source)? ResolveRoot( string? commandLine, string? environment, string? saved, string configDirectory )
	{
		if ( !string.IsNullOrWhiteSpace( commandLine ) )
			return (Path.GetFullPath( commandLine ), BonusPathSource.CommandLine);
		if ( !string.IsNullOrWhiteSpace( environment ) )
			return (Path.GetFullPath( environment ), BonusPathSource.Environment);
		if ( saved != null && saved.Trim().Length == 0 )
			return null;
		if ( Usable( saved ) is { } savedPath )
			return (savedPath, BonusPathSource.Saved);
		if ( Usable( Path.Combine( configDirectory, FolderName ) ) is { } imported )
			return (imported, BonusPathSource.ConfigFolder);
		return null;
	}

	private static string? Usable( string? path )
	{
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			return null;
		var full = Path.GetFullPath( path );
		return ObjectCatalog.FindBonusLevelsParent( full ) != null ? full : null;
	}

	/// <summary>How many bonus WADs a folder holds (0 when it is not one, or does not exist).</summary>
	public static int Validate( string root )
	{
		if ( !Directory.Exists( root ) )
			return 0;
		return Directory.EnumerateFiles( root, "*", SearchOption.AllDirectories ).Count( file => ParseBonusPath( Path.GetRelativePath( root, file ) ) != null );
	}

	/// <summary>
	/// Copies the bonus WADs of a .zip file or a folder into <c>configDirectory/bonus</c> and returns how many were
	/// imported. The files are written to a sibling folder first and swapped in only when everything succeeded; when
	/// nothing matches, or an entry escapes the target, the previous import stays untouched.
	/// </summary>
	public static int Import( string source, string configDirectory )
	{
		Directory.CreateDirectory( configDirectory );
		var target = Path.Combine( configDirectory, FolderName );
		var staging = Path.Combine( configDirectory, FolderName + ".importing-" + Guid.NewGuid().ToString( "N" ) );
		try
		{
			int count;
			if ( File.Exists( source ) )
				count = ExtractArchive( source, staging );
			else if ( Directory.Exists( source ) )
				count = CopyFolder( source, staging );
			else
				throw new DirectoryNotFoundException( $"Bonus content '{source}' does not exist." );
			if ( count == 0 )
				return 0;

			// The previous import moves aside first, so a failed swap can be undone.
			var retired = Directory.Exists( target ) ? target + ".replaced-" + Guid.NewGuid().ToString( "N" ) : null;
			if ( retired != null )
				Directory.Move( target, retired );
			try
			{
				Directory.Move( staging, target );
			}
			catch
			{
				if ( retired != null )
					Directory.Move( retired, target );
				throw;
			}
			if ( retired != null )
				Directory.Delete( retired, true );
			return count;
		}
		finally
		{
			if ( Directory.Exists( staging ) )
				Directory.Delete( staging, true );
		}
	}

	/// <summary>Maps a path relative to a bonus source (zip entry or file) to its theme, category and file name.</summary>
	internal static (string Theme, string Category, string FileName)? ParseBonusPath( string relativePath )
	{
		var segments = relativePath.Replace( '\\', '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries );
		var levels = Array.FindIndex( segments, segment => string.Equals( segment, "levels", StringComparison.OrdinalIgnoreCase ) );
		if ( levels < 0 || segments.Length - levels != 4 )
			return null;
		var fileName = segments[^1];
		if ( !BonusFileName.IsMatch( fileName ) )
			return null;
		return (segments[levels + 1], segments[levels + 2], fileName);
	}

	/// <summary>True for an absolute path or one with a ".." segment, which must never be written.</summary>
	internal static bool Escapes( string relativePath )
	{
		var normalized = relativePath.Replace( '\\', '/' );
		var drive = normalized.Length >= 2 && normalized[1] == ':';
		return normalized.StartsWith( '/' ) || drive || normalized.Split( '/' ).Contains( ".." );
	}

	private static int ExtractArchive( string archivePath, string staging )
	{
		using var archive = ZipFile.OpenRead( archivePath );
		foreach ( var entry in archive.Entries )
			if ( Escapes( entry.FullName ) )
				throw new InvalidDataException( $"Zip entry '{entry.FullName}' points outside the bonus folder; the archive was refused." );

		var copied = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var entry in archive.Entries )
		{
			if ( ParseBonusPath( entry.FullName ) is not { } bonus )
				continue;
			var destination = Path.Combine( staging, "levels", bonus.Theme, bonus.Category, bonus.FileName );
			Directory.CreateDirectory( Path.GetDirectoryName( destination )! );
			entry.ExtractToFile( destination, overwrite: true );
			copied.Add( destination );
		}
		return copied.Count;
	}

	private static int CopyFolder( string source, string staging )
	{
		var copied = 0;
		foreach ( var file in Directory.EnumerateFiles( source, "*", SearchOption.AllDirectories ) )
		{
			if ( ParseBonusPath( Path.GetRelativePath( source, file ) ) is not { } bonus )
				continue;
			var destination = Path.Combine( staging, "levels", bonus.Theme, bonus.Category, bonus.FileName );
			Directory.CreateDirectory( Path.GetDirectoryName( destination )! );
			File.Copy( file, destination, overwrite: true );
			copied++;
		}
		return copied;
	}
}
