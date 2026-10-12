using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Pins the wavelet decoder's RGBA output for a deterministic synthetic WCT corpus covering every
/// aligned size class, alpha and half-scale flag combination, quantization variants and malformed
/// payloads. The pinned hashes were recorded from the original Func-based D4InverseTransform before
/// <see cref="TextureFile" /> decode switched to direct integer indexing; they must never change.
/// </summary>
[TestClass]
public class TextureDecoderIndexingSyntheticTests
{
	/// <summary>Byte-for-byte twin of the corpus generator used to record the pinned hashes.</summary>
	internal readonly record struct SynthCase( string Name, byte Flags, byte Alpha, byte Bpp, byte Ver, short W, short H, short Qy, short Qcb, short Qcr, short Qa, int ColorLen, int AlphaLen );

	internal static readonly SynthCase[] SynthCases =
	{
		new( "tiny-half", 0x00, 0, 0, 0, 1, 1, 0, 0, 0, 0, 8320, 4224 ),
		new( "tiny-half-alpha", 0x00, 1, 0, 0, 1, 1, 0, 0, 0, 0, 8320, 4224 ),
		new( "odd-half", 0x00, 0, 0, 0, 3, 5, 1, -1, 2, -2, 8320, 4224 ),
		new( "odd-half-alpha", 0x00, 1, 2, 1, 7, 4, -3, 5, -4, 6, 8320, 4224 ),
		new( "square8-half", 0x00, 0, 0, 0, 8, 8, 0, 0, 0, 0, 8320, 4224 ),
		new( "square8-full", 0x01, 0, 0, 0, 8, 8, 0, 0, 0, 0, 8320, 4224 ),
		new( "square8-full-alpha", 0x01, 1, 0, 0, 8, 8, 2, 1, -1, 3, 8320, 4224 ),
		new( "square16-half", 0x00, 0, 0, 0, 16, 16, 0, 0, 0, 0, 8704, 4480 ),
		new( "square16-full", 0x01, 0, 0, 0, 16, 16, 1, -2, 3, -4, 8704, 4480 ),
		new( "square16-half-a-q", 0x00, 1, 0, 0, 16, 16, -5, 7, -6, 8, 8704, 4480 ),
		new( "rect17x13-half", 0x00, 0, 0, 0, 17, 13, 0, 0, 0, 0, 10240, 5504 ),
		new( "rect17x13-full-a", 0x01, 1, 0, 0, 17, 13, 4, -4, 4, -4, 10240, 5504 ),
		new( "rect33x64-half", 0x00, 0, 0, 0, 33, 64, 0, 0, 0, 0, 16384, 9472 ),
		new( "rect64x33-half-a", 0x00, 1, 0, 0, 64, 33, -1, 1, -1, 1, 16384, 9472 ),
		new( "square64-half", 0x00, 0, 0, 0, 64, 64, 0, 0, 0, 0, 16384, 9472 ),
		new( "square64-full", 0x01, 0, 0, 0, 64, 64, 0, 0, 0, 0, 16384, 9472 ),
		new( "square64-full-a", 0x01, 1, 0, 0, 64, 64, 9, -9, 8, -8, 16384, 9472 ),
		new( "square128-half", 0x00, 0, 0, 0, 128, 128, 0, 0, 0, 0, 40960, 25600 ),
		new( "square128-half-a", 0x00, 1, 0, 0, 128, 128, 3, -3, 2, -2, 40960, 25600 ),
		new( "square128-full", 0x01, 0, 0, 0, 128, 128, 0, 0, 0, 0, 40960, 25600 ),
		new( "rect128x64-half", 0x00, 0, 0, 0, 128, 64, 0, 0, 0, 0, 40960, 25600 ),
		new( "square256-half", 0x00, 0, 0, 0, 256, 256, 0, 0, 0, 0, 139264, 90112 ),
		new( "square256-half-a", 0x00, 1, 0, 0, 256, 256, 1, -1, 1, -1, 139264, 90112 ),
		new( "square256-full", 0x01, 0, 0, 0, 256, 256, 0, 0, 0, 0, 139264, 90112 ),
		new( "strip256x1-half", 0x00, 0, 0, 0, 256, 1, 0, 0, 0, 0, 139264, 90112 ),
		new( "strip1x256-half-a", 0x00, 1, 0, 0, 1, 256, 0, 0, 0, 0, 139264, 90112 ),
		new( "unknown-flag-half", 0x12, 1, 0, 0, 32, 32, 0, 0, 0, 0, 10240, 5504 ),
		new( "unknown-flag-full", 0x13, 0, 0, 0, 32, 32, 0, 0, 0, 0, 10240, 5504 ),
		new( "trunc-color", 0x00, 0, 0, 0, 16, 16, 0, 0, 0, 0, 16, 16 ),
		new( "trunc-alpha", 0x00, 1, 0, 0, 16, 16, 0, 0, 0, 0, 8704, 8 ),
	};

	private readonly record struct SynthPin( string Name, string? Sha, string? ErrorType, string? ErrorMessage );

	private static readonly SynthPin[] PinnedResults =
	{
		new( "tiny-half", "E3820096CB82366B860B8A4E668453A7AAAF423AF03BDF289FA308EA03A79332", null, null ),
		new( "tiny-half-alpha", "F97AAD596C11F2459B1DCEA269A572576C0286B3F193C678EF57A0083693CABC", null, null ),
		new( "odd-half", "0B1F310100C7B4EBB0A9906AC06E3F2353CDB7CDF5298E1AF6536D3F12310249", null, null ),
		new( "odd-half-alpha", "370D62D2BC48665C5F91F1FD2690095BD58496D761564E8FF3C9E1B3B4531C19", null, null ),
		new( "square8-half", "A5FEAFBBF2FCE16268BB6E665AE539551991964AF270B734D04FCAB71DE6ADC8", null, null ),
		new( "square8-full", "95C3916D776C4F91EDD64F29E27A8C1FE9BC68E90E319EF5DB9E16F8E0760A9A", null, null ),
		new( "square8-full-alpha", "94F80182D3BBAD728BADA26A15882061B7105A4A4D3B90C453338F27221AB00B", null, null ),
		new( "square16-half", "95E2C77303A0698E51B01DFE27C8987D676A45435FFE6D33489180106EA972F2", null, null ),
		new( "square16-full", "5C41177F2BA35E0CA5D138CB92D4F5A8B1BB9E9457DF92E1B124432E03E77A4F", null, null ),
		new( "square16-half-a-q", "30352951587B0BDC00E92C455793FDC8C59C7C24CAF9D50E3FBF151765404008", null, null ),
		new( "rect17x13-half", "400813B9D082BBA1A50D52B92BA964D9A4FDA81807B69111072B9CDC2E4A4233", null, null ),
		new( "rect17x13-full-a", "A163BBAAA299DA734C690071FB2BA8817D7C27FD69FA812B810FAAAB41899095", null, null ),
		new( "rect33x64-half", "F54B00D28369078444432FEA793A4713E70B58D5A3C636F769D32C1B965064E0", null, null ),
		new( "rect64x33-half-a", "5A225418DAF44046DBF1733A48303144EA370239BC6F88096A51A4477F8A3E0A", null, null ),
		new( "square64-half", "F70E305330F476FC2DB70E4300D00C7183BF829CEC2DD9552D2307C41E4E6445", null, null ),
		new( "square64-full", "30922B8D3B2D83B677827649ADB38E49FD5066A91B30E1D076E5C7284DB7D1F2", null, null ),
		new( "square64-full-a", "53D2C5B958A6A4B0BD45BE8A9C53B2C6DC27713550EF8180B33936652CDDCA8D", null, null ),
		new( "square128-half", "C4ADD3EBE009301621743CC66B7AF2938ED29ACC2CA77ABE92D09F347840BD5B", null, null ),
		new( "square128-half-a", "FB00D675A2DC630A4E986A7160ED2921ECC9E7FE9738A287B9A426503AF95D1B", null, null ),
		new( "square128-full", "8CA09944CA7172D5D2F8C4A512F96FA88AA1E49B6F291BC729C75E026A6010D5", null, null ),
		new( "rect128x64-half", "CCA64AE3D9100F843DAE42B9B67F532B130569795ACD60BF3A7741E126E0CCFB", null, null ),
		new( "square256-half", "A6C3FFD604C6C697979731E2D03410280A4216939DDFEA19C68EDE64498B7F19", null, null ),
		new( "square256-half-a", "0C6BA568A07EABB08713801DE7AE41C608CFCBD6374CB1FDBF7ACB8C87802DEE", null, null ),
		new( "square256-full", "545E9F4E6D4B8E79532106AF5C95137F4CE486CD642BF21E76262D3D85B7F9C1", null, null ),
		new( "strip256x1-half", "9BDFFDFF7C56E58C90948285121B9A76E9BF0E03C6396B8226383AC27427898F", null, null ),
		new( "strip1x256-half-a", "7C9CA6D8AAC33F55475C3FF61CCA1B25B9D5F2775B43F031F76FB796B60C723E", null, null ),
		new( "unknown-flag-half", "BC91D0D91F0EEFB128E17628AE546A5F5CEA2F39E19CD738FF177F9B06D59B3D", null, null ),
		new( "unknown-flag-full", "6F0CAF5A21DC751028FAAF1716E7F33A0D365A499BBDABC2D24CAEDC26602F66", null, null ),
		new( "trunc-color", null, "IndexOutOfRangeException", null ),
		new( "trunc-alpha", null, "IndexOutOfRangeException", null ),
		new( "lzss-garbage", null, "Exception", "Offset > stream length" ),
	};

	[TestMethod]
	public void SyntheticCorpusDecodesToOriginalBytes()
	{
		for ( var i = 0; i < SynthCases.Length; i++ )
		{
			var c = SynthCases[i];
			var wct = BuildWct( c.Flags, c.Alpha, c.Bpp, c.Ver, c.W, c.H, c.Qy, c.Qcb, c.Qcr, c.Qa,
				c.ColorLen, c.AlphaLen, 0xC0FFEEu + (uint)i * 7919u, 0x5EED00u + (uint)i * 104729u );
			var pin = PinnedResults[i];

			if ( pin.Sha != null )
			{
				var texture = new TextureFile( new MemoryStream( wct ) ).Data;
				Assert.AreEqual( c.W, texture.Width, $"{c.Name} width" );
				Assert.AreEqual( c.H, texture.Height, $"{c.Name} height" );
				Assert.AreEqual( pin.Sha, Convert.ToHexString( SHA256.HashData( texture.Data ) ), $"{c.Name} RGBA bytes changed against the pre-rewrite decoder" );
			}
			else
			{
				var caught = TryDecode( wct );
				Assert.IsNotNull( caught, $"{c.Name} used to throw {pin.ErrorType}" );
				Assert.AreEqual( pin.ErrorType, caught!.GetType().Name, $"{c.Name} failure mode changed" );
				if ( pin.ErrorMessage != null )
					Assert.AreEqual( pin.ErrorMessage, caught.Message, $"{c.Name} failure message changed" );
			}
		}
	}

	[TestMethod]
	public void SyntheticNonBilzChunkKeepsItsOriginalFailureMode()
	{
		var rawColor = BuildChannelPayload( 64, 0xABADF00Du );
		using var ms = new MemoryStream();
		using var w = new BinaryWriter( ms );
		w.Write( (byte)0x00 );
		w.Write( (byte)0 );
		w.Write( (byte)0 );
		w.Write( (byte)0 );
		w.Write( (short)16 );
		w.Write( (short)16 );
		w.Write( (short)0 );
		w.Write( (short)0 );
		w.Write( (short)0 );
		w.Write( (short)0 );
		w.Write( rawColor.Length );
		w.Write( 0 );
		w.Write( 0 );
		w.Write( rawColor );

		var pin = PinnedResults[PinnedResults.Length - 1];
		var caught = TryDecode( ms.ToArray() );
		Assert.IsNotNull( caught, $"{pin.Name} used to throw {pin.ErrorType}" );
		Assert.AreEqual( pin.ErrorType, caught!.GetType().Name, $"{pin.Name} failure mode changed" );
		Assert.AreEqual( pin.ErrorMessage, caught.Message, $"{pin.Name} failure message changed" );
	}

	private static Exception? TryDecode( byte[] wct )
	{
		try
		{
			new TextureFile( new MemoryStream( wct ) );
			return null;
		}
		catch ( Exception ex )
		{
			return ex;
		}
	}

	// ---------------------------------------------------------------------------
	// Synthetic WCT construction. Kept byte-identical to the generator that
	// produced the pinned hashes: BILZ(zlib) chunks around deterministic
	// payloads that include -128 Int16 escape pairs.
	// ---------------------------------------------------------------------------

	private static uint lcgState;

	private static byte NextByte()
	{
		lcgState = unchecked( lcgState * 1664525u + 1013904223u );
		return (byte)(lcgState >> 24);
	}

	private static short NextShort()
	{
		lcgState = unchecked( lcgState * 1664525u + 1013904223u );
		return (short)(lcgState >> 16);
	}

	private static byte[] BuildChannelPayload( int length, uint seed )
	{
		lcgState = seed;
		using var ms = new MemoryStream();
		for ( var i = 0; i < length; i++ )
		{
			if ( (i % 97) == 13 )
			{
				ms.WriteByte( 0x80 );
				var v = NextShort();
				ms.WriteByte( (byte)(v & 0xFF) );
				ms.WriteByte( (byte)((v >> 8) & 0xFF) );
			}
			else
			{
				var b = NextByte();
				ms.WriteByte( b == 0x80 ? (byte)0x7F : b );
			}
		}
		return ms.ToArray();
	}

	private static byte[] WrapBilz( byte[] raw )
	{
		using var deflated = new MemoryStream();
		using ( var zlib = new ZLibStream( deflated, CompressionLevel.Optimal, true ) )
			zlib.Write( raw );
		using var ms = new MemoryStream();
		using var w = new BinaryWriter( ms );
		w.Write( Encoding.ASCII.GetBytes( "BILZ" ) );
		w.Write( raw.Length );
		w.Write( (int)deflated.Length );
		w.Write( 15 );
		w.Write( 9 );
		w.Write( 0 );
		w.Write( 0 );
		w.Write( deflated.ToArray() );
		return ms.ToArray();
	}

	// Internal so the scratch-allocation tests can build the same synthetic corpus.
	internal static byte[] BuildWct( byte flags, byte hasAlpha, byte bpp, byte version, short width, short height,
		short qy, short qcb, short qcr, short qa, int colorLen, int alphaLen, uint colorSeed, uint alphaSeed )
	{
		var color = WrapBilz( BuildChannelPayload( colorLen, colorSeed ) );
		var alpha = WrapBilz( BuildChannelPayload( alphaLen, alphaSeed ) );
		using var ms = new MemoryStream();
		using var w = new BinaryWriter( ms );
		w.Write( flags );
		w.Write( hasAlpha );
		w.Write( bpp );
		w.Write( version );
		w.Write( width );
		w.Write( height );
		w.Write( qy );
		w.Write( qcb );
		w.Write( qcr );
		w.Write( qa );
		w.Write( color.Length );
		w.Write( hasAlpha == 1 ? alpha.Length : 0 );
		w.Write( 0 );
		w.Write( color );
		if ( hasAlpha == 1 )
			w.Write( alpha );
		return ms.ToArray();
	}
}
