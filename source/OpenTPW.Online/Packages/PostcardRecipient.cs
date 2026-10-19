namespace OpenTPW.Online.Packages;

/// <summary>[EXT:postcard-player-names] An address with '@' is e-mail; other recipients are player names.</summary>
public static class PostcardRecipient
{
	public const int MaximumEmailLength = 254;
	public static bool IsEmail( string value ) => value.Contains( '@' );

	/// <summary>Plain ASCII mailbox syntax, without display names, quoted locals, comments or header controls.</summary>
	public static bool IsValid( string? value )
	{
		if ( value == null ) return false;
		if ( !IsEmail( value ) ) return OnlineText.IsValidPlayerName( value );
		if ( value.Length > MaximumEmailLength || value.Any( c => c > 127 || char.IsWhiteSpace( c ) || char.IsControl( c ) ) ) return false;
		var parts = value.Split( '@' );
		if ( parts.Length != 2 || parts[0].Length is < 1 or > 64 || parts[0].StartsWith( '.' ) || parts[0].EndsWith( '.' ) || parts[0].Contains( ".." ) ) return false;
		if ( parts[0].Any( c => !char.IsAsciiLetterOrDigit( c ) && !".!#$%&'*+-/=?^_`{|}~".Contains( c ) ) ) return false;
		var labels = parts[1].Split( '.' );
		return labels.Length >= 2 && labels.All( label => label.Length is >= 1 and <= 63 && label[0] != '-' && label[^1] != '-'
			&& label.All( c => char.IsAsciiLetterOrDigit( c ) || c == '-' ) );
	}

	public static string Key( string value ) => IsEmail( value ) ? "email:" + value.ToUpperInvariant() : "player:" + OnlineText.NormalizeName( value );

	public static void RequireValid( IReadOnlyList<string>? recipients )
	{
		if ( recipients == null || recipients.Count is < 1 or > Postcard.MaximumRecipients )
			throw new InvalidDataException( $"A postcard needs 1-{Postcard.MaximumRecipients} recipients." );
		var keys = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var recipient in recipients )
		{
			if ( !IsValid( recipient ) ) throw new InvalidDataException( "Postcard recipient must be a valid e-mail address or player name." );
			if ( !keys.Add( Key( recipient ) ) ) throw new InvalidDataException( "Postcard lists a recipient twice." );
		}
	}
}
