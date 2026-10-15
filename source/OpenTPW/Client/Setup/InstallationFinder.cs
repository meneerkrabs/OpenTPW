using System.Runtime.Versioning;
using Microsoft.Win32;

namespace OpenTPW;

/// <summary>The host family whose folder conventions apply.</summary>
public enum InstallationPlatform { Windows, MacOS, Linux }

/// <summary>
/// Everything <see cref="InstallationFinder"/> looks at, so tests can replace the file system and registry.
/// Paths returned by the list delegates are full paths.
/// </summary>
public sealed record InstallationSearchContext(
	InstallationPlatform Platform,
	string HomeDirectory,
	string ExecutableDirectory,
	IReadOnlyList<string> DriveRoots,
	IReadOnlyList<string> VolumeRoots,
	IReadOnlyList<string> ProgramFilesRoots,
	Func<IEnumerable<string>> ReadRegistryPaths,
	Func<string, bool> DirectoryExists,
	Func<string, bool> FileExists,
	Func<string, IEnumerable<string>> ListDirectories );

/// <summary>
/// Lists folders that might contain Theme Park World, most likely first. The caller checks each one with
/// <see cref="GameInstallation.Inspect"/>, so this class only checks that a folder exists or that a disc
/// has a game marker (TP.ICD, TP.exe or a Data folder).
/// </summary>
public static class InstallationFinder
{
	/// <summary>Folders inside a Program Files root, where the installer or EA's releases put the game.</summary>
	private static readonly string[][] GameFolders =
	{
		new[] { "Bullfrog", "Theme Park World" },
		new[] { "Bullfrog Productions", "Theme Park World" },
		new[] { "EA Games", "Theme Park World" },
		new[] { "Electronic Arts", "Theme Park World" },
		new[] { "Bullfrog", "Sim Theme Park" },
		new[] { "EA Games", "Sim Theme Park" },
	};

	/// <summary>Registry keys searched for install paths, below HKLM and HKCU.</summary>
	private static readonly string[] RegistrySubkeys =
	{
		@"Software\Bullfrog Productions Ltd",
		@"Software\Electronic Arts",
		@"Software\WOW6432Node\Bullfrog Productions Ltd",
		@"Software\WOW6432Node\Electronic Arts",
	};

	private static readonly string[] ProgramFilesPrefixFolders = { "Program Files (x86)", "Program Files" };

	private const int MaximumRegistryDepth = 2;

	/// <summary>Folders that might contain the game on this computer, in priority order.</summary>
	public static IEnumerable<string> GetCandidates() => GetCandidates( CreateContext() );

	internal static IEnumerable<string> GetCandidates( InstallationSearchContext context )
	{
		var comparer = context.Platform == InstallationPlatform.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		var seen = new HashSet<string>( comparer );
		foreach ( var candidate in EnumerateCandidates( context ) )
		{
			var path = Path.TrimEndingDirectorySeparator( candidate );
			if ( path.Length == 0 || !seen.Add( path ) )
				continue;
			if ( Safely( () => context.DirectoryExists( path ), false ) )
				yield return path;
		}
	}

	private static IEnumerable<string> EnumerateCandidates( InstallationSearchContext context )
	{
		// The game may be unzipped inside its own folder, so the executable folder and its parent both count.
		var executableDirectory = Path.TrimEndingDirectorySeparator( context.ExecutableDirectory );
		yield return executableDirectory;
		if ( Path.GetDirectoryName( executableDirectory ) is { } parent )
			yield return parent;

		if ( context.Platform == InstallationPlatform.Windows )
		{
			foreach ( var path in RegistryCandidates( context ) )
				yield return path;
			foreach ( var root in context.ProgramFilesRoots )
				foreach ( var folder in GameFolders )
					yield return Combine( root, folder );
			foreach ( var drive in context.DriveRoots )
				if ( HasGameMarker( context, drive ) )
					yield return drive;
		}
		else
		{
			var wineDrive = Path.Combine( context.HomeDirectory, ".wine", "drive_c" );
			foreach ( var programFiles in ProgramFilesPrefixFolders )
				foreach ( var folder in GameFolders )
					yield return Combine( Path.Combine( wineDrive, programFiles ), folder );

			if ( context.Platform == InstallationPlatform.MacOS )
				foreach ( var bottle in Safely( () => context.ListDirectories( Path.Combine( context.HomeDirectory, "Library", "Application Support", "CrossOver", "Bottles" ) ) ) )
					foreach ( var programFiles in ProgramFilesPrefixFolders )
						foreach ( var folder in GameFolders )
							yield return Combine( Path.Combine( bottle, "drive_c", programFiles ), folder );

			yield return Path.Combine( context.HomeDirectory, "Games", "Theme Park World" );
			yield return Path.Combine( context.HomeDirectory, "Games", "theme-park-world" );
			yield return Path.Combine( context.HomeDirectory, "Theme Park World" );
		}

		foreach ( var volume in context.VolumeRoots )
			if ( HasGameMarker( context, volume ) || HasDataFolder( context, volume ) )
				yield return volume;
	}

	/// <summary>Registry values that name a folder, or a file whose folder holds the game.</summary>
	private static IEnumerable<string> RegistryCandidates( InstallationSearchContext context )
	{
		foreach ( var value in Safely( context.ReadRegistryPaths ) )
		{
			var path = value.Trim().Trim( '"' );
			if ( path.Length == 0 )
				continue;
			if ( path.EndsWith( ".exe", StringComparison.OrdinalIgnoreCase ) || path.EndsWith( ".icd", StringComparison.OrdinalIgnoreCase ) )
			{
				if ( Path.GetDirectoryName( path ) is { } directory )
					yield return directory;
			}
			else
				yield return path;
		}
	}

	private static bool HasGameMarker( InstallationSearchContext context, string root ) =>
		// Discs mounted on Linux often show ISO 9660 names in lower case.
		Safely( () => new[] { "TP.ICD", "TP.exe", "tp.icd", "tp.exe", "TP.EXE" }.Any( marker => context.FileExists( Path.Combine( root, marker ) ) ), false );

	private static bool HasDataFolder( InstallationSearchContext context, string root ) =>
		Safely( context.ListDirectories, root ).Any( folder => string.Equals( Path.GetFileName( folder ), "Data", StringComparison.OrdinalIgnoreCase ) );

	private static string Combine( string root, string[] segments ) => segments.Aggregate( root, ( path, segment ) => Path.Combine( path, segment ) );

	/// <summary>A file-system or registry problem with one folder skips that folder instead of failing the search.</summary>
	private static bool Safely( Func<bool> check, bool fallback )
	{
		try
		{
			return check();
		}
		catch ( Exception exception ) when ( IsRecoverable( exception ) )
		{
			return fallback;
		}
	}

	private static IReadOnlyList<string> Safely( Func<IEnumerable<string>> list )
	{
		try
		{
			return list().ToArray();
		}
		catch ( Exception exception ) when ( IsRecoverable( exception ) )
		{
			return Array.Empty<string>();
		}
	}

	private static IReadOnlyList<string> Safely( Func<string, IEnumerable<string>> list, string root ) => Safely( () => list( root ) );

	private static bool IsRecoverable( Exception exception ) =>
		exception is UnauthorizedAccessException or IOException or System.Security.SecurityException or ArgumentException;

	private static InstallationSearchContext CreateContext()
	{
		var platform = OperatingSystem.IsWindows() ? InstallationPlatform.Windows : OperatingSystem.IsMacOS() ? InstallationPlatform.MacOS : InstallationPlatform.Linux;
		var home = Environment.GetFolderPath( Environment.SpecialFolder.UserProfile );

		var programFiles = new[] { Environment.GetFolderPath( Environment.SpecialFolder.ProgramFiles ), Environment.GetFolderPath( Environment.SpecialFolder.ProgramFilesX86 ) }
			.Where( path => !string.IsNullOrWhiteSpace( path ) ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();

		var drives = new List<string>();
		if ( platform == InstallationPlatform.Windows )
			drives.AddRange( Safely( () => DriveInfo.GetDrives()
				.Where( drive => drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.CDRom && drive.Name.Length > 0 && char.ToUpperInvariant( drive.Name[0] ) is >= 'D' and <= 'Z' )
				.Select( drive => drive.RootDirectory.FullName ) ) );

		var volumeParents = platform switch
		{
			InstallationPlatform.MacOS => new[] { "/Volumes" },
			InstallationPlatform.Linux => new[] { Path.Combine( "/media", Environment.UserName ), Path.Combine( "/run/media", Environment.UserName ), "/mnt" },
			_ => Array.Empty<string>(),
		};
		var volumes = volumeParents.SelectMany( parent => Safely( () => Directory.EnumerateDirectories( parent ) ) ).ToArray();

		return new InstallationSearchContext(
			platform,
			home,
			AppContext.BaseDirectory,
			drives,
			volumes,
			programFiles,
			() => OperatingSystem.IsWindows() ? ReadRegistryValues() : Array.Empty<string>(),
			Directory.Exists,
			File.Exists,
			path => Directory.EnumerateDirectories( path ) );
	}

	/// <summary>
	/// Every string value under the game's registry keys, two levels deep. The original installer's value
	/// names are not known, so values are not filtered by name; <see cref="RegistryCandidates"/> keeps only
	/// paths that exist.
	/// </summary>
	[SupportedOSPlatform( "windows" )]
	private static IEnumerable<string> ReadRegistryValues()
	{
		var values = new List<string>();
		foreach ( var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser } )
			foreach ( var subkey in RegistrySubkeys )
			{
				try
				{
					using var baseKey = RegistryKey.OpenBaseKey( hive, RegistryView.Default );
					using var key = baseKey.OpenSubKey( subkey );
					if ( key != null )
						CollectValues( key, 0, values );
				}
				catch ( Exception exception ) when ( IsRecoverable( exception ) )
				{
				}
			}
		return values;
	}

	[SupportedOSPlatform( "windows" )]
	private static void CollectValues( RegistryKey key, int depth, List<string> values )
	{
		foreach ( var name in key.GetValueNames() )
			if ( key.GetValue( name ) is string value )
				values.Add( value );

		if ( depth >= MaximumRegistryDepth )
			return;
		foreach ( var subkey in key.GetSubKeyNames() )
		{
			try
			{
				using var child = key.OpenSubKey( subkey );
				if ( child != null )
					CollectValues( child, depth + 1, values );
			}
			catch ( Exception exception ) when ( IsRecoverable( exception ) )
			{
			}
		}
	}
}
