using System.Text.Json;

namespace OpenTPW;

/// <summary>
/// The chosen text language, in <c>language.json</c> beside <c>display.json</c> (docs/LANGUAGES.md). It replaces the .NET
/// user settings file, whose folder depends on where the program is unpacked, so a choice survived only until the
/// next release was unpacked elsewhere. That old setting is still read when <c>language.json</c> has no choice.
/// </summary>
// [EXT:language-settings] where the choice is stored is an OpenTPW matter; the original read its language from the install
public sealed record LanguageSettings( string? Language = null, string? LanguageDataPath = null )
{
	public const string FileName = "language.json";

	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

	public static string GetDefaultPath() => Path.Combine( Path.GetDirectoryName( DisplaySettings.GetDefaultPath() )!, FileName );

	/// <summary>The file's choice; a missing file is no choice, an unreadable one adds a diagnostic.</summary>
	public static LanguageSettings Load( string path, ICollection<string> diagnostics )
	{
		if ( !File.Exists( path ) )
			return new LanguageSettings();
		try
		{
			var settings = JsonSerializer.Deserialize<LanguageSettings>( File.ReadAllText( path ), JsonOptions );
			return settings == null ? new LanguageSettings() : settings with { Language = Clean( settings.Language ), LanguageDataPath = Clean( settings.LanguageDataPath ) };
		}
		catch ( Exception exception ) when ( exception is JsonException or IOException or UnauthorizedAccessException )
		{
			diagnostics.Add( $"Language settings could not be read ({exception.Message}); using the default language." );
			return new LanguageSettings();
		}
	}

	public void Save( string path )
	{
		Directory.CreateDirectory( Path.GetDirectoryName( Path.GetFullPath( path ) )! );
		var temporary = path + ".tmp";
		File.WriteAllText( temporary, JsonSerializer.Serialize( this, JsonOptions ).Replace( "\r\n", "\n" ) );
		File.Move( temporary, path, overwrite: true );
	}

	/// <summary>The first non-empty value: command line, environment, this file, then the old user setting.</summary>
	public static string? Choose( params string?[] sources ) => sources.Select( Clean ).FirstOrDefault( value => value != null );

	private static string? Clean( string? value ) => string.IsNullOrWhiteSpace( value ) ? null : value.Trim();
}
