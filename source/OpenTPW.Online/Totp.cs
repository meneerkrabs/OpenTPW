using System.Security.Cryptography;
using System.Text;

namespace OpenTPW.Online;

/// <summary>
/// Time-based one-time passwords (RFC 6238) as authenticator apps compute them: HMAC-SHA1, 30-second steps,
/// six digits. The shared secret is exchanged as base32 (RFC 4648) inside an <c>otpauth://</c> URI.
/// </summary>
public static class Totp
{
	public const int SecretBytes = 20;
	public const int Digits = 6;
	public const int StepSeconds = 30;
	/// <summary>Steps accepted on each side of the current one, for clocks that are a little off.</summary>
	public const int Window = 1;

	private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

	public static byte[] NewSecret() => RandomNumberGenerator.GetBytes( SecretBytes );

	public static long Step( DateTimeOffset time ) => time.ToUnixTimeSeconds() / StepSeconds;

	/// <summary>The code for one time step.</summary>
	public static string Code( ReadOnlySpan<byte> secret, long step )
	{
		Span<byte> counter = stackalloc byte[8];
		for ( var index = 7; index >= 0; index-- )
		{
			counter[index] = (byte)step;
			step >>= 8;
		}
		Span<byte> hash = stackalloc byte[20];
		HMACSHA1.HashData( secret, counter, hash );
		var offset = hash[19] & 0x0F;
		var value = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
		return (value % 1_000_000).ToString( "D6" );
	}

	/// <summary>
	/// The time step <paramref name="code"/> belongs to within <see cref="Window"/> of <paramref name="now"/>, or null. Codes
	/// are compared in constant time; a step at or before <paramref name="lastUsedStep"/> is refused so a code works once.
	/// </summary>
	public static long? Verify( ReadOnlySpan<byte> secret, string? code, DateTimeOffset now, long lastUsedStep = long.MinValue )
	{
		code = Normalize( code );
		if ( code == null )
			return null;
		var given = Encoding.ASCII.GetBytes( code );
		var current = Step( now );
		long? match = null;
		for ( var step = current - Window; step <= current + Window; step++ )
		{
			var matches = CryptographicOperations.FixedTimeEquals( given, Encoding.ASCII.GetBytes( Code( secret, step ) ) );
			if ( matches && step > lastUsedStep && match == null )
				match = step;
		}
		return match;
	}

	/// <summary>Six digits with any spaces removed, or null.</summary>
	public static string? Normalize( string? code )
	{
		if ( code == null )
			return null;
		code = code.Replace( " ", "" );
		return code.Length == Digits && code.All( char.IsAsciiDigit ) ? code : null;
	}

	public static string ToBase32( ReadOnlySpan<byte> bytes )
	{
		var text = new StringBuilder( (bytes.Length * 8 + 4) / 5 );
		int buffer = 0, bits = 0;
		foreach ( var value in bytes )
		{
			buffer = (buffer << 8) | value;
			bits += 8;
			while ( bits >= 5 )
			{
				text.Append( Base32Alphabet[(buffer >> (bits - 5)) & 31] );
				bits -= 5;
			}
		}
		if ( bits > 0 )
			text.Append( Base32Alphabet[(buffer << (5 - bits)) & 31] );
		return text.ToString();
	}

	/// <summary>Decodes base32 without padding, ignoring case, spaces and '-'; throws on any other character.</summary>
	public static byte[] FromBase32( string text )
	{
		var bytes = new List<byte>( text.Length * 5 / 8 );
		int buffer = 0, bits = 0;
		foreach ( var character in text.ToUpperInvariant() )
		{
			if ( character is ' ' or '-' or '=' )
				continue;
			var value = Base32Alphabet.IndexOf( character );
			if ( value < 0 )
				throw new FormatException( "Not a base32 character." );
			buffer = (buffer << 5) | value;
			bits += 5;
			if ( bits >= 8 )
			{
				bytes.Add( (byte)(buffer >> (bits - 8)) );
				bits -= 8;
			}
		}
		return bytes.ToArray();
	}

	/// <summary>The secret as an authenticator app shows it: base32 in groups of four.</summary>
	public static string Grouped( string base32 ) => string.Join( ' ', base32.Chunk( 4 ).Select( group => new string( group ) ) );

	/// <summary>The <c>otpauth://totp/</c> URI an authenticator app scans: label "issuer:account" and the issuer, both URL-encoded.</summary>
	public static string Uri( string issuer, string account, string base32 ) =>
		$"otpauth://totp/{System.Uri.EscapeDataString( issuer )}:{System.Uri.EscapeDataString( account )}?secret={base32}"
		+ $"&issuer={System.Uri.EscapeDataString( issuer )}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
}
