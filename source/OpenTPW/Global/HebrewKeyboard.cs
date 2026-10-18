namespace OpenTPW;

/// <summary>
/// The Hebrew keyboard of tp.exe: while the language is Hebrew, every typed character from 0x20 up passes through a fixed table that turns the
/// lower-case Latin letters and a few punctuation keys into the Hebrew letters on the same keys of the Israeli layout (t is aleph, c bet), so a
/// Latin keyboard types Hebrew (docs/LANGUAGES.md, "Hebrew"). Capitals, digits and other characters pass unchanged.
/// </summary>
// [BIN:TPW-EXE:0x46b460 Hebrew keys] the main window procedure 0x46b600 calls it for WM_CHAR when 0x41a850 reports Hebrew (0x46b8c5) and the
// character is not below 0x20, before handing the character to the interface library (0x658c96)
public static class HebrewKeyboard
{
	private static readonly Dictionary<char, char> Keys = new()
	{
		['\''] = ',', [','] = 'ת', ['.'] = 'ץ', ['/'] = '.', [';'] = 'ף', ['`'] = ';',
		['a'] = 'ש', ['b'] = 'נ', ['c'] = 'ב', ['d'] = 'ג', ['e'] = 'ק', ['f'] = 'כ', ['g'] = 'ע', ['h'] = 'י', ['i'] = 'ן', ['j'] = 'ח', ['k'] = 'ל',
		['l'] = 'ך', ['m'] = 'צ', ['n'] = 'מ', ['o'] = 'ם', ['p'] = 'פ', ['q'] = '/', ['r'] = 'ר', ['s'] = 'ד', ['t'] = 'א', ['u'] = 'ו', ['v'] = 'ה',
		['w'] = '\'', ['x'] = 'ס', ['y'] = 'ט', ['z'] = 'ז',
	};

	/// <summary>The character tp.exe passes on for a typed <paramref name="character"/> in Hebrew.</summary>
	public static char Map( char character ) => character >= ' ' && Keys.TryGetValue( character, out var hebrew ) ? hebrew : character;

	/// <summary><paramref name="typed"/> as the Hebrew interface receives it.</summary>
	public static string Map( string typed ) => string.Create( typed.Length, typed, ( span, text ) =>
	{
		for ( var index = 0; index < text.Length; index++ )
			span[index] = Map( text[index] );
	} );
}
