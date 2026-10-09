using OpenTPW.Online.Packages;

namespace OpenTPW;

/// <summary>Offline sharing commands, run before save folders or graphics are created.</summary>
public static class OnlineCommands
{
	public static bool Run( string[] args, string gameDirectory, OnlineFolders folders, Action<string> report )
	{
		var export = Option( args, "--export-park" );
		var import = Option( args, "--import-park" );
		if ( export == null && import == null )
			return false;
		if ( export != null && import != null || args.Contains( "--visit-park" ) )
			throw new ArgumentException( "Choose one of --export-park, --import-park or --visit-park." );
		if ( export != null )
		{
			var path = ValidateOutputPath( export, gameDirectory );
			var level = Option( args, "--load-original-level" );
			var park = level == null ? null : OriginalPark.Load( level );
			var snapshot = park == null ? ParkSnapshotBuilder.FromSandbox( "jungle", null ) : ParkSnapshotBuilder.FromOriginal( park, null );
			var package = ParkSharing.CreatePackage( snapshot, "OpenTPW " + snapshot.Level, "Offline startup snapshot", "OpenTPW", park?.Map );
			package.Save( path );
			report( "Exported park: " + path );
			return true;
		}
		var imported = Import( import!, gameDirectory, folders );
		report( ParkSharing.Describe( imported.Visit ) );
		report( "Imported park: " + imported.Path );
		return true;
	}

	public static (string Path, ParkVisitInfo Visit) Import( string source, string gameDirectory, OnlineFolders folders )
	{
		var package = ParkPackage.Load( source );
		var visit = ParkSharing.PrepareVisit( package );
		var path = ValidateOutputPath( Path.Combine( folders.Root, "visited", package.Manifest.PackageId.ToString( "N" ) + ParkPackage.FileExtension ), gameDirectory );
		package.Save( path );
		return (path, visit);
	}

	/// <summary>Output must stay outside the installation, including through existing symbolic links.</summary>
	public static string ValidateOutputPath( string output, string gameDirectory )
	{
		var path = Path.GetFullPath( output );
		var destination = ResolveExistingLinks( path );
		var game = ResolveExistingLinks( Path.GetFullPath( gameDirectory ) ).TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar );
		var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		if ( destination.Equals( game, comparison ) || destination.StartsWith( game + Path.DirectorySeparatorChar, comparison ) )
			throw new ArgumentException( "Online output must be outside the original game's installation." );
		return path;
	}

	private static string ResolveExistingLinks( string fullPath, int depth = 0 )
	{
		if ( depth > 40 )
			throw new IOException( "Online output contains too many symbolic links." );
		var root = Path.GetPathRoot( fullPath )!;
		var current = root;
		foreach ( var part in fullPath[root.Length..].Split( Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries ) )
		{
			current = Path.Combine( current, part );
			FileSystemInfo item = Directory.Exists( current ) ? new DirectoryInfo( current ) : new FileInfo( current );
			if ( item.LinkTarget != null )
				current = ResolveExistingLinks( item.ResolveLinkTarget( true )?.FullName ?? throw new IOException( "Cannot resolve online output link." ), depth + 1 );
		}
		return Path.GetFullPath( current );
	}

	private static string? Option( string[] args, string name )
	{
		var index = Array.IndexOf( args, name );
		if ( index < 0 )
			return null;
		if ( index + 1 >= args.Length || args[index + 1].StartsWith( "--" ) )
			throw new ArgumentException( name + " requires a file or level name." );
		return args[index + 1];
	}
}
