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
/// <see cref="TextureFile" /> decode switched to direct integer indexing; the indexing rewrite must not change them. The cases whose flag
/// bit 0 is clear (planar rows, the interface art) were re-recorded once, when their invented "half-scale" row layout gave way to the planar
/// rows tp.exe's decoder 0x593ab0 reads (<see cref="TexturePlanarRowsTests" />); the interleaved cases (flag bit 0 set) are the original pins.
/// </summary>
[TestClass]
public class TextureDecoderIndexingSyntheticTests
{
	/// <summary>Byte-for-byte twin of the corpus generator used to record the pinned hashes.</summary>
	internal readonly record struct SynthCase( string Name, byte Flags, byte Alpha, byte Bpp, byte Ver, short W, short H, short Qy, short Qcb, short Qcr, short Qa, int ColorLen, int AlphaLen );

	// The alpha cases carry 0x20 bits per pixel (a header alpha byte of 1 and no alpha plane no longer decodes one: the loader 0x564C00 reads the plane exactly at 32 bits per pixel).
	// Qcb and Qcr are the header's two chroma shorts in file order. The decoder takes the first chroma plane's scale from the second short and the
	// second plane's from the first (see TextureFile), so each case below lists the pair the other way round from when its hash was recorded:
	// the pinned bytes are unchanged, which shows the plane to header mapping is the only thing that moved.
	internal static readonly SynthCase[] SynthCases =
	{
		new( "tiny-half", 0x00, 0, 0, 0, 1, 1, 0, 0, 0, 0, 8320, 4224 ),
		new( "tiny-half-alpha", 0x00, 1, 0x20, 0, 1, 1, 0, 0, 0, 0, 8320, 4224 ),
		new( "odd-half", 0x00, 0, 0, 0, 3, 5, 1, 2, -1, -2, 8320, 4224 ),
		new( "odd-half-alpha", 0x00, 1, 0x20, 1, 7, 4, -3, -4, 5, 6, 8320, 4224 ),
		new( "square8-half", 0x00, 0, 0, 0, 8, 8, 0, 0, 0, 0, 8320, 4224 ),
		new( "square8-full", 0x01, 0, 0, 0, 8, 8, 0, 0, 0, 0, 8320, 4224 ),
		new( "square8-full-alpha", 0x01, 1, 0x20, 0, 8, 8, 2, -1, 1, 3, 8320, 4224 ),
		new( "square16-half", 0x00, 0, 0, 0, 16, 16, 0, 0, 0, 0, 8704, 4480 ),
		new( "square16-full", 0x01, 0, 0, 0, 16, 16, 1, 3, -2, -4, 8704, 4480 ),
		new( "square16-half-a-q", 0x00, 1, 0x20, 0, 16, 16, -5, -6, 7, 8, 8704, 4480 ),
		new( "rect17x13-half", 0x00, 0, 0, 0, 17, 13, 0, 0, 0, 0, 10240, 5504 ),
		new( "rect17x13-full-a", 0x01, 1, 0x20, 0, 17, 13, 4, 4, -4, -4, 10240, 5504 ),
		new( "rect33x64-half", 0x00, 0, 0, 0, 33, 64, 0, 0, 0, 0, 16384, 9472 ),
		new( "rect64x33-half-a", 0x00, 1, 0x20, 0, 64, 33, -1, -1, 1, 1, 16384, 9472 ),
		new( "square64-half", 0x00, 0, 0, 0, 64, 64, 0, 0, 0, 0, 16384, 9472 ),
		new( "square64-full", 0x01, 0, 0, 0, 64, 64, 0, 0, 0, 0, 16384, 9472 ),
		new( "square64-full-a", 0x01, 1, 0x20, 0, 64, 64, 9, 8, -9, -8, 16384, 9472 ),
		new( "square128-half", 0x00, 0, 0, 0, 128, 128, 0, 0, 0, 0, 40960, 25600 ),
		new( "square128-half-a", 0x00, 1, 0x20, 0, 128, 128, 3, 2, -3, -2, 40960, 25600 ),
		new( "square128-full", 0x01, 0, 0, 0, 128, 128, 0, 0, 0, 0, 40960, 25600 ),
		new( "rect128x64-half", 0x00, 0, 0, 0, 128, 64, 0, 0, 0, 0, 40960, 25600 ),
		new( "square256-half", 0x00, 0, 0, 0, 256, 256, 0, 0, 0, 0, 139264, 90112 ),
		new( "square256-half-a", 0x00, 1, 0x20, 0, 256, 256, 1, 1, -1, -1, 139264, 90112 ),
		new( "square256-full", 0x01, 0, 0, 0, 256, 256, 0, 0, 0, 0, 139264, 90112 ),
		new( "strip256x1-half", 0x00, 0, 0, 0, 256, 1, 0, 0, 0, 0, 139264, 90112 ),
		new( "strip1x256-half-a", 0x00, 1, 0x20, 0, 1, 256, 0, 0, 0, 0, 139264, 90112 ),
		new( "unknown-flag-half", 0x12, 1, 0x20, 0, 32, 32, 0, 0, 0, 0, 10240, 5504 ),
		new( "unknown-flag-full", 0x13, 0, 0, 0, 32, 32, 0, 0, 0, 0, 10240, 5504 ),
		new( "trunc-color", 0x00, 0, 0, 0, 16, 16, 0, 0, 0, 0, 16, 16 ),
		new( "trunc-alpha", 0x00, 1, 0x20, 0, 16, 16, 0, 0, 0, 0, 8704, 8 ),
	};

	private readonly record struct SynthPin( string Name, string? Sha, string? ErrorType, string? ErrorMessage );

	private static readonly SynthPin[] PinnedResults =
	{
		new( "tiny-half", "525639075A0CC52AA641792054875D6A387D65579AFE9A45B5E5BC48F17B3B50", null, null ),
		new( "tiny-half-alpha", "9E4C7874D66C0639EB5774D5B4302CE5FCCE6843965EF2F5B193195FEFE7B288", null, null ),
		new( "odd-half", "DB6EF7E34E8A48C86F290199965FB40D62E798A9203F983123137E2B58096862", null, null ),
		new( "odd-half-alpha", "976D6F16D6264A1B93C420D3D8668FFDB341CD810E8AE9DC50CC709CFF116485", null, null ),
		new( "square8-half", "252B88B54F78DEB73835FA7F01D5FB70CE5DA7AEA202445B792618D350919D60", null, null ),
		new( "square8-full", "95C3916D776C4F91EDD64F29E27A8C1FE9BC68E90E319EF5DB9E16F8E0760A9A", null, null ),
		new( "square8-full-alpha", "94F80182D3BBAD728BADA26A15882061B7105A4A4D3B90C453338F27221AB00B", null, null ),
		new( "square16-half", "D74690F2E4F8842F8A41B2F5516BC6B38DB2681FA42D182BE6A5003BBC586A81", null, null ),
		new( "square16-full", "5C41177F2BA35E0CA5D138CB92D4F5A8B1BB9E9457DF92E1B124432E03E77A4F", null, null ),
		new( "square16-half-a-q", "18AF440E2FACF0D1B8085F63AE9537ED5D019543E1CF7099EE6C6339F466C068", null, null ),
		new( "rect17x13-half", "2FBCD67B51FBEC945873DDE46812DB96FBBC71D33441773F282AADCCA516064D", null, null ),
		new( "rect17x13-full-a", "A163BBAAA299DA734C690071FB2BA8817D7C27FD69FA812B810FAAAB41899095", null, null ),
		new( "rect33x64-half", "FE132AAB0BDA698C5628523DF070D04FEDD302914473AC091EA5291930516F7B", null, null ),
		new( "rect64x33-half-a", "2D32E64B4A175653A3681B5E35EFFF6491013BECC4C59165E6E2DA314A727D75", null, null ),
		new( "square64-half", "46BD18972F74A36AF1D69D7481FFF023F54C6FC57CEF2CBE4F4326568AE52833", null, null ),
		new( "square64-full", "30922B8D3B2D83B677827649ADB38E49FD5066A91B30E1D076E5C7284DB7D1F2", null, null ),
		new( "square64-full-a", "53D2C5B958A6A4B0BD45BE8A9C53B2C6DC27713550EF8180B33936652CDDCA8D", null, null ),
		new( "square128-half", "4AD536044076FF0370B8B35E7FD8CE6F91D5668FBC7EB279F93A32C27CC0A11D", null, null ),
		new( "square128-half-a", "61E1B492DE3C0D3991270F06BFD43AFC8DFF3E383F200369165A6D8C7AA70DCC", null, null ),
		new( "square128-full", "8CA09944CA7172D5D2F8C4A512F96FA88AA1E49B6F291BC729C75E026A6010D5", null, null ),
		new( "rect128x64-half", "C4AEFF61FCE846E59A594E1DC3488F06C2F400C163ABD9B696DA1AD2C31088DE", null, null ),
		new( "square256-half", "C8B57B80126CF9FD6A2319EF12E65D719D732ADA98EBDAF6915A22B614FAF5CA", null, null ),
		new( "square256-half-a", "816FE6E468E94F4CD36CF097C46E7FBF52A68A50B46E7B82E0D03CA79F9653D9", null, null ),
		new( "square256-full", "545E9F4E6D4B8E79532106AF5C95137F4CE486CD642BF21E76262D3D85B7F9C1", null, null ),
		new( "strip256x1-half", "7C765C99D47DC535F972C23306023EE356540E239AE137DD3E3BC4E2360A3905", null, null ),
		new( "strip1x256-half-a", "B66347AF05B6B8CA312261F9C812EE1BCAA70D5C3109637B3D590BE23B199E34", null, null ),
		new( "unknown-flag-half", "6F4DA92822003446DE40D8FCEFAE2FEA4DBDBB5E4C81DD77C76654495E1CCD44", null, null ),
		new( "unknown-flag-full", "6F0CAF5A21DC751028FAAF1716E7F33A0D365A499BBDABC2D24CAEDC26602F66", null, null ),
		new( "trunc-color", null, "InvalidDataException", null ),
		new( "trunc-alpha", null, "InvalidDataException", null ),
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
		// The stream size counts the whole chunk, this 28-byte header included (as every retail BILZ chunk does).
		w.Write( 0x1C + (int)deflated.Length );
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
