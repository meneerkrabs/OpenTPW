namespace OpenTPW;

/// <summary>
/// tp.exe's right-to-left pass: before a laid-out line is drawn, a line that holds a Hebrew character is turned into visual order by a
/// simple word reversal (docs/LANGUAGES.md, "Hebrew"). The string tables keep Hebrew in logical order; the pass is the text library's own
/// (no bidi algorithm, no usp10) and runs for any language, but only Hebrew text triggers it.
/// </summary>
/// <remarks>
/// The steps, all on the line's glyph records in place: the whole line is reversed; then each word (a run of characters other than space and
/// comma) without a Hebrew character is reversed back, so Latin words and numbers read left to right again; a Hebrew word that now starts with
/// a parenthesis has it swapped with its partner inside the word. Words keep their reversed order, so two Latin words in a Hebrew line change
/// places ("Direct Draw" shows as "Draw Direct"), as in the original.
/// </remarks>
// [BIN:TPW-EXE:0x6ae4b0 line reorder] called for every line by the drawing loop 0x6ae550 (the only drawer, from 0x6ae0f0 and 0x6ae1c0, behind the
// interface helpers 0x492750 and 0x4929f0 and a few direct widget calls at 0x48fbb0, 0x491e1a, 0x491e84, 0x491680); it leaves out the trailing records that the wrapper 0x6add90 marked 0x40 (the spaces a wrap consumed:
// OpenTPW's wrap drops those), reverses the rest (0x6ae440) and walks the words (0x6ae3d0 next word start, 0x6ae390 word end)
public static class TextVisualOrder
{
	/// <summary>The characters that make a line right to left: U+05B1..U+05EA (Hebrew points from hataf segol, and the letters).</summary>
	// [BIN:TPW-EXE:0x6ae410 Hebrew test] a record whose character is above 0x5b0 and below 0x5eb (0x6ae41f, 0x6ae426)
	public static bool IsHebrew( char character ) => character > '\u05B0' && character < '\u05EB';

	/// <summary>Word separators of the pass: space and comma only.</summary>
	// [BIN:TPW-EXE:0x6ae2e0 separator] 0x20 or 0x2c
	public static bool IsSeparator( char character ) => character is ' ' or ',';

	/// <summary>1 for an opening parenthesis, 2 for a closing one, else 0.</summary>
	// [BIN:TPW-EXE:0x6ae2b0 bracket kind] 0x28 gives 1, 0x29 gives 2
	private static int Bracket( char character ) => character == '(' ? 1 : character == ')' ? 2 : 0;

	/// <summary>Whether <paramref name="items"/> from <paramref name="start"/> to <paramref name="end"/> (exclusive) holds a Hebrew character.</summary>
	private static bool HasHebrew<T>( IList<T> items, Func<T, char> character, int start, int end )
	{
		for ( var index = start; index < end; index++ )
			if ( IsHebrew( character( items[index] ) ) )
				return true;
		return false;
	}

	/// <summary>Whether any character of <paramref name="text"/> makes its line right to left.</summary>
	public static bool HasHebrew( string text ) => text.Any( IsHebrew );

	/// <summary>Reorders one line (<paramref name="items"/>, in logical order) into the order tp.exe draws it, left to right.</summary>
	public static void ReorderLine<T>( IList<T> items, Func<T, char> character )
	{
		ArgumentNullException.ThrowIfNull( items );
		ArgumentNullException.ThrowIfNull( character );
		var end = items.Count;
		if ( end == 0 || !HasHebrew( items, character, 0, end ) )
			return;
		Reverse( items, 0, end );
		var word = NextWord( items, character, 0, end );
		while ( word >= 0 )
		{
			var wordEnd = WordEnd( items, character, word, end );
			if ( !HasHebrew( items, character, word, wordEnd ) )
				Reverse( items, word, wordEnd );
			else if ( Bracket( character( items[word] ) ) > 0 )
				SwapPartner( items, character, word, wordEnd );
			word = NextWord( items, character, wordEnd, end );
		}
	}

	/// <summary>The characters of <paramref name="text"/> (one line) in drawn order.</summary>
	public static string Reorder( string text )
	{
		var characters = text.ToCharArray();
		ReorderLine( characters, character => character );
		return new string( characters );
	}

	// [BIN:TPW-EXE:0x6ae440 reverse] swaps records from both ends of [start, end) towards the middle
	private static void Reverse<T>( IList<T> items, int start, int end )
	{
		for ( int low = start, high = end - 1; low < high; low++, high-- )
			(items[low], items[high]) = (items[high], items[low]);
	}

	// [BIN:TPW-EXE:0x6ae3d0 next word] none when the position is past the line's second-to-last record (0x6ae3da: start > end - 2 records),
	// else the first record from there on that is no separator
	private static int NextWord<T>( IList<T> items, Func<T, char> character, int position, int end )
	{
		if ( position > end - 2 || position >= end )
			return -1;
		for ( ; position < end; position++ )
			if ( !IsSeparator( character( items[position] ) ) )
				return position;
		return -1;
	}

	// [BIN:TPW-EXE:0x6ae390 word end] the first separator from the word's start on, or the line's end
	private static int WordEnd<T>( IList<T> items, Func<T, char> character, int position, int end )
	{
		for ( ; position < end; position++ )
			if ( IsSeparator( character( items[position] ) ) )
				return position;
		return end;
	}

	// [BIN:TPW-EXE:0x6ae300 bracket partner] counts the word's parentheses from its first record (same kind +1, the other kind -1) and swaps the
	// first record with the one where the count returns to 0; nothing when it never does
	private static void SwapPartner<T>( IList<T> items, Func<T, char> character, int first, int end )
	{
		var kind = Bracket( character( items[first] ) );
		var depth = 0;
		for ( var position = first; position < end; position++ )
		{
			var bracket = Bracket( character( items[position] ) );
			if ( bracket == kind )
				depth++;
			else if ( bracket != 0 )
				depth--;
			if ( depth == 0 )
			{
				(items[first], items[position]) = (items[position], items[first]);
				return;
			}
		}
	}
}
