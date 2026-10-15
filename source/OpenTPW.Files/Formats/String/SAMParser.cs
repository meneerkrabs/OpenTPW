namespace OpenTPW;

/// <summary>
/// Reads the <c>Key Value comment</c> lines of an original <c>.sam</c> settings file. Each line is
/// parsed on its own: a key with no value on its line yields no pair (it never takes the next line's
/// key as its value). The value is the first token after the key, or the text between double quotes
/// when it starts with one (<c>Info.Name "Inca Totem"</c>, <c>SkyObjects[0].Filename ""</c>); the rest
/// of the line is the designers' comment. <c>#</c> and <c>//</c> lines are comments, and the rows of
/// blocks between <c>---</c> lines (<c>Info.Shape</c>, <c>Info.Hoarding</c>) are skipped; a block left open
/// at the end of the file is rejected, as <c>ObjectSettingsFile</c> rejects it. This is the same
/// line grammar <c>ObjectSettingsFile</c> and <c>SamDocument</c> read; it is taken from the shipped
/// files, not traced in the original executable.
/// </summary>
public class SAMParser : BaseParser
{
	public SAMParser( string input ) : base( input ) { }

	public List<SettingsPair> Parse()
	{
		var settings = new List<SettingsPair>();
		var inBlock = false;

		foreach ( var rawLine in Input.Replace( "\r\n", "\n" ).Replace( '\r', '\n' ).Split( '\n' ) )
		{
			var line = rawLine.Trim();
			if ( line == "---" )
			{
				inBlock = !inBlock;
				continue;
			}

			if ( inBlock || line.Length == 0 || line[0] == '#' || line.StartsWith( "//", StringComparison.Ordinal ) )
				continue;

			if ( ParsePair( line ) is { } pair )
				settings.Add( pair );
		}

		if ( inBlock )
			throw new InvalidDataException( "SAM block is not closed with ---." );

		Position = Input.Length;
		return settings;
	}

	protected static SettingsPair? ParsePair( string line )
	{
		var keyEnd = 0;
		while ( keyEnd < line.Length && !char.IsWhiteSpace( line[keyEnd] ) )
			keyEnd++;

		var key = line[..keyEnd];
		var rest = line[keyEnd..].TrimStart();
		if ( rest.Length == 0 || rest[0] == '#' )
			return null;

		if ( rest[0] == '"' )
		{
			// A quoted value may be empty; an unclosed quote runs to the end of the line.
			var close = rest.IndexOf( '"', 1 );
			return new SettingsPair( key, close < 0 ? rest[1..] : rest[1..close] );
		}

		var valueEnd = 0;
		while ( valueEnd < rest.Length && !char.IsWhiteSpace( rest[valueEnd] ) && rest[valueEnd] != '#' )
			valueEnd++;

		return valueEnd == 0 ? null : new SettingsPair( key, rest[..valueEnd] );
	}
}
