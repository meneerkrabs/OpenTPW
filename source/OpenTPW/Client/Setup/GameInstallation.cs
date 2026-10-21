namespace OpenTPW;

/// <summary>Whether a folder can serve as the original game data, and why (docs/SETUP.md).</summary>
public sealed record InstallationReport( string Path, string? DataDirectory, IReadOnlyList<string> Languages, IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings )
{
	/// <summary>The folder has the data OpenTPW needs; <see cref="Warnings"/> may still list missing optional parts.</summary>
	public bool IsUsable => Problems.Count == 0;
	public GameEdition? SelectedEdition { get; init; }
	/// <summary>The language of a single-language CD (the Spanish CD, the Korean Slim Pack CD) the folder is, from <see cref="GameEditions.DetectCdLanguage"/>; null for an installation or a CD with several languages.</summary>
	public string? CdLanguage { get; init; }
	public IReadOnlyList<string> Worlds { get; init; } = Array.Empty<string>();
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

	public static InstallationReport Inspect( string? path ) => InspectCore( path, null );

	public static InstallationReport Inspect( string? path, GameEdition edition ) => InspectCore( path, edition ) with { SelectedEdition = edition };

	private static InstallationReport InspectCore( string? path, GameEdition? edition )
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
			return InspectFolder( root, edition );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or System.Security.SecurityException )
		{
			return new( root, null, Array.Empty<string>(), new[] { $"This folder cannot be read ({exception.Message})." }, warnings );
		}
	}

	private static InstallationReport InspectFolder( string root, GameEdition? edition )
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
		if ( edition == null && GameEditions.HasKnownStandardSam( dataDirectory ) )
			problems.Add( "These files match the supplied Theme Park Inc / Sim Coaster retail edition, which is not supported yet. Choose Theme Park World files." );

		var detected = edition == null ? null : GameEditions.Detect( dataDirectory );
		// [EXT:tpi-viewer] the edition and world checks of the experimental Theme Park Inc selection
		var tpwThemes = GameEditions.ThemeParkWorldThemes;
		var worlds = edition == null ? Array.Empty<string>() : GameEditions.ThemeParkIncWorlds.Where( world => GameLanguage.FindEntry( dataDirectory, "levels/" + world, true ) != null ).ToArray();
		if ( edition != null )
		{
			var mixed = worlds.Length > 0 && tpwThemes.Any( world => GameLanguage.FindEntry( dataDirectory, "levels/" + world, true ) != null );
			if ( !Enum.IsDefined( edition.Value ) || mixed || edition == GameEdition.ThemeParkWorld && worlds.Length > 0 || detected!.Edition != edition || (edition == GameEdition.ThemeParkInc && worlds.Length == 0) )
				problems.Add( "This folder does not identify the selected edition, or mixes both editions. Choose matching game files." );
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

		return new( root, dataDirectory, languages, problems, warnings ) { SelectedEdition = edition, Worlds = worlds, CdLanguage = GameEditions.DetectCdLanguage( dataDirectory ).Language };
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
			if ( OperatingSystem.IsWindows() )
				return !IsUnderProgramFiles( directory );
			const UnixFileMode anyWrite = UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
			return (File.GetUnixFileMode( directory ) & anyWrite) != 0;
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or ArgumentException )
		{
			return true;
		}
	}

	/// <summary>OpenTPW runs without administrator rights, so a game under Program Files cannot take saves beside it.</summary>
	private static bool IsUnderProgramFiles( string directory )
	{
		var full = Path.TrimEndingDirectorySeparator( Path.GetFullPath( directory ) ) + Path.DirectorySeparatorChar;
		// a 32-bit process sees Program Files (x86) for both folders; ProgramW6432 names the 64-bit one
		return new[] { Environment.GetFolderPath( Environment.SpecialFolder.ProgramFiles ), Environment.GetFolderPath( Environment.SpecialFolder.ProgramFilesX86 ), Environment.GetEnvironmentVariable( "ProgramW6432" ) ?? "" }
			.Where( root => root.Length > 0 )
			.Any( root => full.StartsWith( Path.TrimEndingDirectorySeparator( root ) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ) );
	}
}
