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
		var problems = new List<string>();
		var warnings = new List<string>();
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
		if ( !Directory.Exists( root ) )
			return new( root, null, Array.Empty<string>(), new[] { "This folder does not exist." }, warnings );

		// Picking the Data folder itself is a common mistake: use the folder that contains it.
		if ( GameLanguage.FindEntry( root, "data", true ) == null && LooksLikeDataFolder( root ) && Directory.GetParent( root ) is { } parent )
			root = parent.FullName;

		var dataDirectory = GameLanguage.FindEntry( root, "data", true );
		if ( dataDirectory == null )
		{
			problems.Add( "No Data folder: choose the folder where Theme Park World is installed, or the root of the CD." );
			return new( root, null, Array.Empty<string>(), problems, warnings );
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
			warnings.Add( "This folder is read-only (for example a mounted CD), so saved parks cannot be stored here yet." );

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
