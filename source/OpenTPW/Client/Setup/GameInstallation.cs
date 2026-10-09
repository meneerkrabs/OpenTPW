using System.Security.Cryptography;

namespace OpenTPW;

/// <summary>Whether a folder can serve as the original game data, and why (docs/SETUP.md).</summary>
public sealed record InstallationReport( string Path, string? DataDirectory, IReadOnlyList<string> Languages, IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings )
{
	/// <summary>The folder has the data OpenTPW needs; <see cref="Warnings"/> may still list missing optional parts.</summary>
	public bool IsUsable => Problems.Count == 0;
}

/// <summary>
/// Checks a candidate Theme Park World folder: an installation, a copy of the CD or a mounted CD all
/// work, because the CD's <c>Data</c> folder is complete game data. Spelling of folder names does not
/// matter (<see cref="GameLanguage.FindEntry"/>).
/// </summary>
public static class GameInstallation
{
	/// <summary>Language files the installer adds from the CD's language folders (banners, word filter).</summary>
	private static readonly string[] OptionalLanguageFiles = { "bankrupt.MD2", "congrats.MD2", "paused.MD2", "swears.txt" };

	public static InstallationReport Inspect( string? path )
	{
		var warnings = Array.Empty<string>();
		if ( string.IsNullOrWhiteSpace( path ) )
			return new( "", null, Array.Empty<string>(), new[] { "No folder selected." }, warnings );

		string root;
		try
		{
			root = System.IO.Path.GetFullPath( path.Trim() );
		}
		catch ( Exception exception ) when ( exception is ArgumentException or NotSupportedException or PathTooLongException )
		{
			return new( path, null, Array.Empty<string>(), new[] { $"'{path}' is not a valid folder path." }, warnings );
		}
		if ( File.Exists( root ) ) root = System.IO.Path.GetDirectoryName( root )!;
		if ( !Directory.Exists( root ) )
			return new( root, null, Array.Empty<string>(), new[] { "This folder does not exist." }, warnings );
		try
		{
			return InspectFolder( root );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or System.Security.SecurityException )
		{
			return new( root, null, Array.Empty<string>(), new[] { $"This folder cannot be read ({exception.Message})." }, warnings );
		}
	}

	private static InstallationReport InspectFolder( string root )
	{
		var problems = new List<string>();
		var warnings = new List<string>();

		// Picking the Data folder itself is a common mistake: use the folder that contains it.
		if ( GameLanguage.FindEntry( root, "data", true ) == null && LooksLikeDataFolder( root ) && Directory.GetParent( root ) is { } parent )
			root = parent.FullName;

		var dataDirectory = GameLanguage.FindEntry( root, "data", true );
		if ( dataDirectory == null )
		{
			problems.Add( "No Data folder: choose the folder where Theme Park World is installed, or the root of the CD." );
			return new( root, null, Array.Empty<string>(), problems, warnings );
		}

		// Exact negative identity for the supplied Sim Coaster retail corpus. Unknown or
		// modified editions are not classified from generic folders or missing assets.
		var standard = GameLanguage.FindEntry( dataDirectory, System.IO.Path.Combine( "levels", "Standard.sam" ), false );
		if ( standard != null && new FileInfo( standard ).Length == 45695 )
		{
			using var input = File.OpenRead( standard );
			if ( Convert.ToHexString( SHA256.HashData( input ) ).Equals(
				"8966C8647104FEB1878FF3C3D43F3B2AC6DC89856570F9AA5A5A0BF040E6749E", StringComparison.Ordinal ) )
				problems.Add( "These files match the supplied Theme Park Inc / Sim Coaster retail edition, which is not supported yet. Choose Theme Park World files." );
		}

		foreach ( var required in new[] { "levels", "global" } )
			if ( GameLanguage.FindEntry( dataDirectory, required, true ) == null )
				problems.Add( $"Data/{required} is missing; the game files are incomplete." );

		var languages = GameLanguage.FindLanguages( dataDirectory, null );
		if ( languages.Count == 0 )
			problems.Add( "Data/Language has no language folder (such as English); the game files are incomplete." );
		else
		{
			var missing = OptionalLanguageFiles.Where( file => !languages.Any( language => GameLanguage.FindEntry( dataDirectory, System.IO.Path.Combine( "Language", language, file ), false ) != null ) ).ToArray();
			if ( missing.Length > 0 )
				warnings.Add( $"The language folder lacks {string.Join( ", ", missing )}; the installer copies these from the CD, so some banners or the chat word filter may be missing." );
		}

		if ( GameLanguage.FindEntry( dataDirectory, "Movies", true ) == null )
			warnings.Add( "No movies were found; add the CD in the next step to play them." );
		if ( !IsWritable( root ) )
			warnings.Add( "This folder is read-only (for example a mounted CD); saved parks go to the OpenTPW settings folder instead." );

		return new( root, dataDirectory, languages, problems, warnings );
	}

	private static bool LooksLikeDataFolder( string directory ) =>
		GameLanguage.FindEntry( directory, "levels", true ) != null && GameLanguage.FindEntry( directory, "global", true ) != null;

	/// <summary>A heuristic from the drive type and permission bits; nothing is written to the game folder.</summary>
	private static bool IsWritable( string directory )
	{
		try
		{
			if ( new DriveInfo( directory ).DriveType == DriveType.CDRom )
				return false;
			const UnixFileMode anyWrite = UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
			return OperatingSystem.IsWindows() || (File.GetUnixFileMode( directory ) & anyWrite) != 0;
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or ArgumentException )
		{
			return true;
		}
	}
}
