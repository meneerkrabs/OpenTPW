namespace OpenTPW;

/// <summary>
/// A string table with robust lookup: the selected language's entry, else the English entry, else
/// the caller's internal name, so a missing or empty localized string never crashes or shows blank.
/// Each fallback is reported once in <see cref="Diagnostics"/>.
/// [APPROX:COMPAT-011] the fallback order is an OpenTPW choice — evidence needed: original behaviour
/// for missing strings.
/// </summary>
public sealed class LocalizedStringTable
{
	private readonly string[] primary;
	private readonly string[]? fallback;
	private readonly HashSet<int> reported = new();
	private readonly List<string> diagnostics = new();

	public LocalizedStringTable( string fileName, string languageName, string[]? primary, string[]? fallback )
	{
		FileName = fileName;
		LanguageName = languageName;
		this.primary = primary ?? Array.Empty<string>();
		this.fallback = fallback;
	}

	public string FileName { get; }
	public string LanguageName { get; }
	public int Count => primary.Length;
	public IReadOnlyList<string> Diagnostics => diagnostics;

	/// <summary>
	/// Loads <paramref name="fileName"/> from <paramref name="language"/> and, for fallback, from
	/// <paramref name="english"/> (skipped when it is the same folder). A table that cannot be read
	/// becomes empty with a diagnostic instead of throwing.
	/// </summary>
	public static LocalizedStringTable Load( GameLanguage language, GameLanguage? english, string fileName )
	{
		ArgumentNullException.ThrowIfNull( language );
		var problems = new List<string>();
		var primary = TryLoad( language, fileName, problems );
		var fallback = english == null || string.Equals( english.Directory, language.Directory, StringComparison.OrdinalIgnoreCase ) ? null : TryLoad( english, fileName, problems );
		var table = new LocalizedStringTable( fileName, language.Name, primary, fallback );
		table.diagnostics.AddRange( problems );
		return table;
	}

	private static string[]? TryLoad( GameLanguage language, string fileName, List<string> problems )
	{
		try
		{
			return language.LoadStrings( fileName ).Entries;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or ArgumentException )
		{
			problems.Add( $"{language.Name} {fileName} could not be read ({exception.Message})." );
			return null;
		}
	}

	/// <summary>
	/// The entry at <paramref name="index"/>. An entry that is empty here but not in English (an
	/// untranslated string) uses the English text; an entry empty in both is intentionally blank and
	/// stays empty; an index outside both tables (or unreadable tables) shows <paramref name="internalName"/>.
	/// </summary>
	public string Get( int index, string internalName )
	{
		var inPrimary = index >= 0 && index < primary.Length;
		if ( inPrimary && !string.IsNullOrEmpty( primary[index] ) )
			return primary[index];
		var inFallback = fallback != null && index >= 0 && index < fallback.Length;
		if ( inFallback && !string.IsNullOrEmpty( fallback![index] ) )
		{
			Report( index, $"{LanguageName} {FileName}[{index}] ({internalName}) is missing; using the English text." );
			return fallback[index];
		}
		if ( inPrimary || inFallback )
			return "";
		Report( index, $"{LanguageName} {FileName}[{index}] ({internalName}) does not exist in any language; showing its internal name." );
		return internalName;
	}

	private void Report( int index, string message )
	{
		if ( reported.Add( index ) )
			diagnostics.Add( message );
	}
}
