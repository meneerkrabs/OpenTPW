using System;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online;

namespace OpenTPW.Tests;

[TestClass]
public class TotpTests
{
	private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes( "12345678901234567890" );

	/// <summary>RFC 6238 Appendix B, SHA-1, last six of the eight published digits.</summary>
	[DataTestMethod]
	[DataRow( 59L, "287082" )]
	[DataRow( 1111111109L, "081804" )]
	[DataRow( 1111111111L, "050471" )]
	[DataRow( 1234567890L, "005924" )]
	[DataRow( 2000000000L, "279037" )]
	[DataRow( 20000000000L, "353130" )]
	public void CodesMatchTheRfcVectors( long unixSeconds, string expected )
	{
		Assert.AreEqual( expected, Totp.Code( RfcSecret, Totp.Step( DateTimeOffset.FromUnixTimeSeconds( unixSeconds ) ) ) );
	}

	[TestMethod]
	public void VerifyAcceptsOneStepEitherSideAndEachStepOnce()
	{
		var now = DateTimeOffset.FromUnixTimeSeconds( 1111111111 );
		var step = Totp.Step( now );
		foreach ( var offset in new[] { -1, 0, 1 } )
			Assert.AreEqual( step + offset, Totp.Verify( RfcSecret, Totp.Code( RfcSecret, step + offset ), now ), $"offset {offset}" );
		foreach ( var offset in new[] { -2, 2 } )
			Assert.IsNull( Totp.Verify( RfcSecret, Totp.Code( RfcSecret, step + offset ), now ), $"offset {offset}" );
		Assert.IsNull( Totp.Verify( RfcSecret, Totp.Code( RfcSecret, step ), now, lastUsedStep: step ), "a used code is refused" );
		Assert.AreEqual( step + 1, Totp.Verify( RfcSecret, Totp.Code( RfcSecret, step + 1 ), now, lastUsedStep: step ) );
		var spaced = Totp.Code( RfcSecret, step ).Insert( 3, " " );
		Assert.AreEqual( step, Totp.Verify( RfcSecret, spaced, now ), "a space in the middle is allowed" );
		foreach ( var bad in new[] { null, "", "12345", "1234567", "12a456", "１２３４５６" } )
			Assert.IsNull( Totp.Verify( RfcSecret, bad, now ), bad ?? "null" );
	}

	[TestMethod]
	public void Base32RoundTripsAndMatchesRfc4648()
	{
		Assert.AreEqual( "MZXW6YTBOI", Totp.ToBase32( Encoding.ASCII.GetBytes( "foobar" ) ) );
		Assert.AreEqual( "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Totp.ToBase32( RfcSecret ) );
		CollectionAssert.AreEqual( RfcSecret, Totp.FromBase32( "gezd gnbv gy3t qojq gezd gnbv gy3t qojq" ) );
		var secret = Totp.NewSecret();
		Assert.AreEqual( Totp.SecretBytes, secret.Length );
		CollectionAssert.AreEqual( secret, Totp.FromBase32( Totp.ToBase32( secret ) ) );
		Assert.ThrowsException<FormatException>( () => Totp.FromBase32( "ABC1" ) );
		Assert.AreEqual( "GEZD GNBV GY3T", Totp.Grouped( "GEZDGNBVGY3T" ) );
	}

	[TestMethod]
	public void TheUriEncodesIssuerAndNames()
	{
		var uri = Totp.Uri( "OpenTPW Official", "Ann Marie", "GEZDGNBV" );
		Assert.AreEqual( "otpauth://totp/OpenTPW%20Official:Ann%20Marie?secret=GEZDGNBV&issuer=OpenTPW%20Official&algorithm=SHA1&digits=6&period=30", uri );
		var parameters = Totp.Uri( "a&b=c", "d?e#f", "X" ).Split( '?', 2 )[1].Split( '&' ).Select( part => part.Split( '=' )[0] ).ToArray();
		CollectionAssert.AreEqual( new[] { "secret", "issuer", "algorithm", "digits", "period" }, parameters, "issuer and name cannot add parameters" );
	}
}
