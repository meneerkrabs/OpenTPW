namespace OpenTPW;

/// <summary>Path comparisons that see through symbolic links, shared by the checks that keep OpenTPW's writes out of the game's own folders.</summary>
public static class PathLinks
{
	/// <summary>
	/// The full path of <paramref name="path"/> with every symbolic link along it resolved (segments that do not exist yet are kept as given), without a
	/// trailing separator.
	/// </summary>
	public static string ResolveLinks( string path ) => ResolveLinks( path, 0 );

	private static string ResolveLinks( string path, int depth )
	{
		var full = Path.TrimEndingDirectorySeparator( Path.GetFullPath( path ) );
		var current = Path.GetPathRoot( full ) ?? "";
		foreach ( var segment in full[current.Length..].Split( Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries ) )
		{
			current = Path.Combine( current, segment );
			FileSystemInfo entry = Directory.Exists( current ) ? new DirectoryInfo( current ) : new FileInfo( current );
			if ( !entry.Exists || entry.LinkTarget == null )
				continue;
			if ( depth >= 40 )
				throw new IOException( $"Too many symbolic links in '{path}'." );
			// a link's target may itself run through links (/var -> /private/var on macOS)
			var target = Path.IsPathRooted( entry.LinkTarget ) ? entry.LinkTarget : Path.Combine( Path.GetDirectoryName( current ) ?? "", entry.LinkTarget );
			current = ResolveLinks( target, depth + 1 );
		}
		return Path.TrimEndingDirectorySeparator( current );
	}
}
