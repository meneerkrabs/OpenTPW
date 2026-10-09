using System.Text;

namespace OpenTPW.Online;

/// <summary>Validation of player names and user-written text shared by files, server and client.</summary>
public static class OnlineText
{
	// [EXT:ONLINE-010] Name rules are an OpenTPW choice; the original login names were
	// ThemeParkWorld.com accounts whose rules are unknown.
	public const int MinimumNameLength = 3;
	public const int MaximumNameLength = 20;

	public static bool IsValidPlayerName( string? name )
	{
		if ( name == null || name.Length < MinimumNameLength || name.Length > MaximumNameLength )
			return false;
		if ( !name.IsNormalized( NormalizationForm.FormC ) || name[0] == ' ' || name[^1] == ' ' || name.Contains( "  ", StringComparison.Ordinal ) )
			return false;
		foreach ( var c in name )
		{
			if ( !(char.IsLetterOrDigit( c ) || c is ' ' or '_' or '-' or '.') )
				return false;
		}
		return true;
	}

	public static string RequirePlayerName( string? name, string what )
	{
		if ( !IsValidPlayerName( name ) )
			throw new InvalidDataException( $"{what} must be {MinimumNameLength}-{MaximumNameLength} letters, digits, spaces, '_', '-' or '.'." );
		return name!;
	}

	/// <summary>Case-insensitive identity of a player name.</summary>
	public static string NormalizeName( string name ) => name.Normalize( NormalizationForm.FormKC ).ToUpperInvariant();

	/// <summary>Text with a length cap; control characters are rejected except newlines when allowed.</summary>
	public static string RequireText( string? text, int maximumLength, bool allowNewlines, bool allowEmpty, string what )
	{
		if ( text == null )
			throw new InvalidDataException( $"{what} is missing." );
		if ( text.Length > maximumLength )
			throw new InvalidDataException( $"{what} is longer than {maximumLength} characters." );
		if ( !allowEmpty && string.IsNullOrWhiteSpace( text ) )
			throw new InvalidDataException( $"{what} is empty." );
		for ( var index = 0; index < text.Length; index++ )
		{
			var c = text[index];
			if ( c == '\n' && allowNewlines )
				continue;
			if ( char.IsHighSurrogate( c ) && index + 1 < text.Length && char.IsLowSurrogate( text[index + 1] ) )
			{
				index++;
				continue;
			}
			// Unpaired surrogates, controls and bidirectional overrides are rejected.
			if ( char.IsControl( c ) || char.IsSurrogate( c ) || c is '\u2028' or '\u2029' or '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E' )
				throw new InvalidDataException( $"{what} contains a control character." );
		}
		return text;
	}
}
