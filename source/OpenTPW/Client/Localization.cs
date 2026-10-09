using System.Text;

namespace OpenTPW;

public static class Localization
{
	private static readonly LocalizedStringTable UIStrings;

	static Localization()
	{
		// Missing or untranslated entries fall back to English, then to the UIStrings name (docs/COMPATIBILITY.md).
		UIStrings = LocalizedStringTable.Load( GameLanguage.Current, GameLanguage.TryResolveEnglish( GameLanguage.Current ), "UITEXT.str" );
	}

	/// <summary>A UITEXT entry with the missing-string fallback; never throws for unknown indices.</summary>
	public static string Get( UIStrings id ) => UIStrings.Get( (int)id, id.ToString() );

	/// <summary>Fallbacks taken so far (each reported once).</summary>
	public static IReadOnlyList<string> Diagnostics => UIStrings.Diagnostics;

	private class LocalizationParser : BaseParser
	{
		public LocalizationParser( string input ) : base( input ) { }

		public string Parse()
		{
			var sb = new StringBuilder();

			while ( !EndOfFile() )
			{
				sb.Append( ConsumeWhile( c => c != '#' ) );

				if ( !EndOfFile() )
				{
					ConsumeChar(); // #

					var key = ConsumeWhile( x => !char.IsWhiteSpace( x ) );
					key = key.Trim();

					// An unknown key is shown as written rather than crashing the caller.
					if ( Enum.TryParse<UIStrings>( key, out var enumVal ) )
						sb.Append( Get( enumVal ) );
					else
						sb.Append( '#' ).Append( key );
				}
			}

			return sb.ToString();
		}
	}

	public static string Parse( string str )
	{
		var parser = new LocalizationParser( str );
		return parser.Parse();
	}
}
