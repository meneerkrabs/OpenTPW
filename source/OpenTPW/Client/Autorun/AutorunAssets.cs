using System.Text;
using System.Text.RegularExpressions;
using StbImageSharp;

namespace OpenTPW;

/// <summary>An 8-bit BMP of the original launcher decoded to RGBA (no transparency: the launcher copies bitmaps as they are).</summary>
internal sealed record AutorunBitmap( int Width, int Height, byte[] Rgba )
{
	public static AutorunBitmap Decode( byte[] bmp )
	{
		var image = ImageResult.FromMemory( bmp, ColorComponents.RedGreenBlueAlpha );
		return new AutorunBitmap( image.Width, image.Height, image.Data );
	}
}

/// <summary>The buttons of the original autorun dialog, in the script's tab order.</summary>
internal enum AutorunButtonId { Play, Install, Uninstall, Reinstall, TechSupport, Readme, Exit }

/// <summary>
/// The artwork and script values of the original CD's autorun launcher (<c>Autorun/*.tre</c>, docs/AUTORUN.md),
/// read from a CD or installation folder. Nothing is copied or cached: the files are decoded in memory.
/// </summary>
internal sealed class AutorunAssets
{
	/// <summary>Row positions of <c>autorun.cfg</c> (<c>nvPlayY</c> ... <c>nvQuitY</c>), used when the script is unreadable.</summary>
	// [DATA:Autorun/autorun.tre:autorun.cfg nvPlayY=61 nvInstallY=103 nvUninstallY=145 nvReinstallY=187 nvTechbuttonY=229 nvReadmeY=271 nvQuitY=313]
	private static readonly IReadOnlyDictionary<string, int> DefaultRows = new Dictionary<string, int>
	{
		["nvPlayY"] = 61, ["nvInstallY"] = 103, ["nvUninstallY"] = 145, ["nvReinstallY"] = 187, ["nvTechbuttonY"] = 229, ["nvReadmeY"] = 271, ["nvQuitY"] = 313,
	};

	/// <summary>Art file names inside the language archive (<c>.\autorun\NAME.bmp</c>).</summary>
	private static readonly (AutorunButtonId Id, string Name)[] ArtNames =
	[
		(AutorunButtonId.Play, "play"), (AutorunButtonId.Install, "install"), (AutorunButtonId.Uninstall, "uninst"), (AutorunButtonId.Reinstall, "reinst"),
		(AutorunButtonId.TechSupport, "tech"), (AutorunButtonId.Readme, "readme"), (AutorunButtonId.Exit, "quit"),
	];

	internal AutorunAssets( string folder, string language, AutorunBitmap background, IReadOnlyDictionary<AutorunButtonId, AutorunBitmap> art, IReadOnlyDictionary<string, int> rows )
	{
		Folder = folder;
		Language = language;
		Background = background;
		Art = art;
		Rows = rows;
	}

	/// <summary>The <c>Autorun</c> folder.</summary>
	public string Folder { get; }
	/// <summary>The folder that holds <c>Autorun</c>: the CD root (where <c>ReadMe.txt</c> and the language folders are).</summary>
	public string BaseFolder => Path.GetDirectoryName( Folder )!;
	/// <summary>The language whose archive was used (English when the requested one is absent).</summary>
	public string Language { get; }
	/// <summary><c>.\autorun\back.bmp</c> of the language archive: the 640x480 backdrop with the unavailable (dim) labels.</summary>
	public AutorunBitmap Background { get; }
	public IReadOnlyDictionary<AutorunButtonId, AutorunBitmap> Art { get; }
	/// <summary>The <c>nv*Y</c> values of the script.</summary>
	public IReadOnlyDictionary<string, int> Rows { get; }

	/// <summary>
	/// Languages whose script block moves Read-me and Exit up one row and hides Tech Support (every language of
	/// the CD except English, French and German).
	/// </summary>
	public static bool UsesShortLayout( string language ) =>
		!language.Equals( "English", StringComparison.OrdinalIgnoreCase ) && !language.Equals( "French", StringComparison.OrdinalIgnoreCase ) && !language.Equals( "German", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// The <c>Autorun</c> folder of a CD or installation: <paramref name="roots"/> in order, each itself or its parent
	/// (so a <c>Data</c> folder works), with <c>general.tre</c> inside. Null when there is none.
	/// </summary>
	public static string? FindFolder( IEnumerable<string?> roots )
	{
		foreach ( var root in roots )
		{
			if ( string.IsNullOrWhiteSpace( root ) || !Directory.Exists( root ) )
				continue;
			var full = Path.GetFullPath( root );
			foreach ( var candidate in new[] { full, Path.GetDirectoryName( full.TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar ) ) } )
			{
				if ( candidate == null )
					continue;
				try
				{
					var folder = GameLanguage.FindEntry( candidate, "Autorun", true );
					if ( folder != null && GameLanguage.FindEntry( folder, "general.tre", false ) != null )
						return folder;
				}
				catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
				{
				}
			}
		}
		return null;
	}

	/// <summary>Reads the backdrop and button art. Null (with the reason in <paramref name="problem"/>) when the archives do not hold them.</summary>
	public static AutorunAssets? Load( string folder, string requestedLanguage, out string? problem )
	{
		problem = null;
		try
		{
			var languages = requestedLanguage.Equals( "English", StringComparison.OrdinalIgnoreCase ) ? new[] { "English" } : new[] { requestedLanguage, "English" };
			var reasons = new List<string>();
			foreach ( var language in languages )
			{
				if ( GameLanguage.FindEntry( folder, language + ".tre", false ) is not { } languageFile )
				{
					reasons.Add( $"{language}.tre is missing" );
					continue;
				}
				AutorunAssets? loaded;
				string? reason;
				try
				{
					loaded = LoadLanguage( folder, language, languageFile, out reason );
				}
				catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
				{
					(loaded, reason) = (null, exception.Message);
				}
				if ( loaded != null )
					return loaded;
				reasons.Add( reason! );
				Log?.Warning( $"Autorun: {language} launcher art is unusable ({reason})." );
			}
			problem = string.Join( "; ", reasons );
			return null;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException )
		{
			problem = exception.Message;
			return null;
		}
	}

	/// <summary>One language archive; null when it lacks the backdrop or the Play and Exit art, without which the launcher could neither continue nor quit.</summary>
	private static AutorunAssets? LoadLanguage( string folder, string language, string languageFile, out string? reason )
	{
		reason = null;
		var archive = TreArchive.Open( languageFile );
		var background = archive.Read( @".\autorun\back.bmp" );
		if ( background == null )
		{
			reason = $"{Path.GetFileName( languageFile )} has no backdrop";
			return null;
		}
		var art = new Dictionary<AutorunButtonId, AutorunBitmap>();
		foreach ( var (id, name) in ArtNames )
			if ( archive.Read( $@".\autorun\{name}.bmp" ) is { } bytes )
				art[id] = AutorunBitmap.Decode( bytes );
		var back = AutorunBitmap.Decode( background );
		if ( back.Width != AutorunView.Width || back.Height != AutorunView.Height )
		{
			reason = $"the backdrop is {back.Width}x{back.Height}, not {AutorunView.Width}x{AutorunView.Height}";
			return null;
		}
		if ( !art.ContainsKey( AutorunButtonId.Play ) || !art.ContainsKey( AutorunButtonId.Exit ) )
		{
			reason = $"{Path.GetFileName( languageFile )} has no Play or Exit button art";
			return null;
		}
		return new AutorunAssets( folder, language, back, art, ReadRows( folder ) );
	}

	/// <summary>The <c>nv*Y</c> row values of <c>autorun.tre</c>'s script, falling back to the known ones.</summary>
	private static IReadOnlyDictionary<string, int> ReadRows( string folder )
	{
		try
		{
			if ( GameLanguage.FindEntry( folder, "autorun.tre", false ) is { } path
				&& TreArchive.Open( path ).Read( "autorun.cfg" ) is { } script )
			{
				var rows = new Dictionary<string, int>( DefaultRows );
				foreach ( Match match in Regex.Matches( Encoding.Latin1.GetString( script ), @"^\s*(nv\w+Y)\.value\s*=\s*(\d+)\s*;", RegexOptions.Multiline ) )
					if ( rows.ContainsKey( match.Groups[1].Value ) && int.TryParse( match.Groups[2].Value, out var value ) && value is >= 0 and < AutorunView.Height )
						rows[match.Groups[1].Value] = value;
				return rows;
			}
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
		}
		return DefaultRows;
	}

	/// <summary>
	/// The CD's read-me for a language: <c>&lt;CD&gt;/&lt;Language&gt;/ReadMe.txt</c> (the script's <c>svLangBaseDir</c>), else
	/// the CD root's <c>ReadMe.txt</c>. Null when neither exists.
	/// </summary>
	public string? FindReadme( string? gameFolder = null )
	{
		foreach ( var (root, relative) in new (string?, string)[] { (BaseFolder, $"{Language}/ReadMe.txt"), (gameFolder, $"{Language}/ReadMe.txt"), (BaseFolder, "ReadMe.txt"), (gameFolder, "ReadMe.txt") } )
		{
			if ( root == null || !Directory.Exists( root ) )
				continue;
			try
			{
				if ( GameLanguage.FindEntry( root, relative, false ) is { } path )
					return path;
			}
			catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
			{
			}
		}
		return null;
	}
}
