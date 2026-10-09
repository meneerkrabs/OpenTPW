namespace OpenTPW;

/// <summary>
/// Performs operations that allow the quick, easy access of Theme Park World game files.
/// </summary>
public static class GameDir
{
	/// <summary>
	/// Joins <see cref="Settings.Default.GamePath"/> to <param name="path">path</param>.
	/// </summary>
	/// <param name="path"></param>
	/// <returns></returns>
	public static string GetPath( string path )
	{
		var root = Path.GetFullPath( Settings.Default.GamePath.Replace( '\\', Path.DirectorySeparatorChar ).Replace( '/', Path.DirectorySeparatorChar ) );
		var relativePath = path.Replace( '\\', Path.DirectorySeparatorChar ).Replace( '/', Path.DirectorySeparatorChar ).TrimStart( Path.DirectorySeparatorChar );
		var absolutePath = Path.GetFullPath( Path.Combine( root, relativePath ) );
		var resolvedRelativePath = Path.GetRelativePath( root, absolutePath );
		if ( Path.IsPathRooted( resolvedRelativePath ) || resolvedRelativePath == ".." || resolvedRelativePath.StartsWith( $"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal ) )
			throw new ArgumentException( "Path must remain inside the game directory.", nameof( path ) );

		return absolutePath;
	}
}
