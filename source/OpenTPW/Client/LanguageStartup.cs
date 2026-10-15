namespace OpenTPW;

/// <summary>
/// Choosing the text language at start-up, shared by the desktop (<c>Game.Run</c>) and the browser build
/// (docs/LANGUAGES.md, docs/WEB.md): which CD folder offers more languages, which language is current.
/// </summary>
// [EXT:language-settings] where the choice is stored and offered is an OpenTPW matter; the original read its language from the install
public static class LanguageStartup
{
	/// <summary>
	/// <paramref name="cdFolder"/> (or, when null, the folder holding <paramref name="dataDirectory"/>) when it adds languages to the
	/// installation, else null. A game folder copied from the CD keeps the other languages in <c>&lt;Lang&gt;/data</c> beside Data.
	/// </summary>
	public static string? CdLanguageOverlay( string dataDirectory, string? cdFolder )
	{
		var cd = string.IsNullOrWhiteSpace( cdFolder ) ? Path.GetDirectoryName( Path.TrimEndingDirectorySeparator( Path.GetFullPath( dataDirectory ) ) ) : cdFolder;
		if ( string.IsNullOrWhiteSpace( cd ) || !Directory.Exists( cd ) )
			return null;
		try
		{
			return GameLanguage.FindLanguages( dataDirectory, cd ).Count > GameLanguage.FindLanguages( dataDirectory, null ).Count ? cd : null;
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			return null;
		}
	}

	/// <summary>
	/// Sets <see cref="GameLanguage.AvailableOverlay"/> and <see cref="GameLanguage.Current"/>. Without separate language data the
	/// CD folder (<paramref name="cdFolder"/>, else the one beside Data) serves as the overlay when it adds languages.
	/// </summary>
	/// <param name="language">The requested language, or null for the default.</param>
	/// <param name="languageData">A separate language data folder, or null.</param>
	/// <param name="fallbackToDefault">
	/// Whether a language or overlay that is missing falls back to the default language with a warning instead of throwing
	/// (the browser, where a saved choice may name a language the chosen folder no longer has).
	/// </param>
	public static void Apply( string dataDirectory, string? language, string? languageData, string? cdFolder, bool fallbackToDefault, Action<string> warn )
	{
		if ( string.IsNullOrWhiteSpace( languageData ) && CdLanguageOverlay( dataDirectory, cdFolder ) is { } cdLanguages )
			languageData = cdLanguages;
		GameLanguage.AvailableOverlay = string.IsNullOrWhiteSpace( languageData ) ? null : languageData;
		if ( !string.IsNullOrWhiteSpace( language ) || !string.IsNullOrWhiteSpace( languageData ) )
		{
			try
			{
				GameLanguage.Current = GameLanguage.Resolve( dataDirectory, language, languageData );
				return;
			}
			catch ( DirectoryNotFoundException exception ) when ( fallbackToDefault )
			{
				warn( $"{exception.Message} Using the default language." );
				// A saved overlay that is gone must not be offered; the default then comes from the installation alone.
				if ( !string.IsNullOrWhiteSpace( languageData ) && !Directory.Exists( languageData ) )
					GameLanguage.AvailableOverlay = null;
			}
		}
		try { GameLanguage.Current = GameLanguage.Resolve( dataDirectory, null, GameLanguage.AvailableOverlay ); }
		catch ( DirectoryNotFoundException exception ) { warn( exception.Message ); }
	}
}
