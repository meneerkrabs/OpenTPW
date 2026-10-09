using System.Security.Cryptography;
using System.Text;

namespace OpenTPW.Server;

/// <summary>[EXT:ONLINE-051] PBKDF2-SHA256 (BCL) with a random 16-byte salt; only the hash is stored.</summary>
public static class PasswordHasher
{
	public const int MinimumLength = 8;
	public const int MaximumLength = 128;
	private const int SaltBytes = 16;
	private const int HashBytes = 32;

	public static bool IsAcceptable( string? password ) => password != null && password.Length is >= MinimumLength and <= MaximumLength;

	public static (string Salt, string Hash) Hash( string password, int iterations )
	{
		var salt = RandomNumberGenerator.GetBytes( SaltBytes );
		var hash = Rfc2898DeriveBytes.Pbkdf2( Encoding.UTF8.GetBytes( password ), salt, iterations, HashAlgorithmName.SHA256, HashBytes );
		return (Convert.ToBase64String( salt ), Convert.ToBase64String( hash ));
	}

	public static bool Verify( string password, string salt, string hash, int iterations )
	{
		byte[] saltBytes, expected;
		try
		{
			saltBytes = Convert.FromBase64String( salt );
			expected = Convert.FromBase64String( hash );
		}
		catch ( FormatException )
		{
			return false;
		}
		var actual = Rfc2898DeriveBytes.Pbkdf2( Encoding.UTF8.GetBytes( password ), saltBytes, iterations, HashAlgorithmName.SHA256, expected.Length );
		return CryptographicOperations.FixedTimeEquals( actual, expected );
	}

	/// <summary>Opaque random session token (256 bits) and the SHA-256 the server keeps instead of it.</summary>
	public static (string Token, string TokenHash) NewToken()
	{
		var token = Convert.ToBase64String( RandomNumberGenerator.GetBytes( 32 ) ).TrimEnd( '=' ).Replace( '+', '-' ).Replace( '/', '_' );
		return (token, HashToken( token ));
	}

	public static string HashToken( string token ) => Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( token ) ) );
}
