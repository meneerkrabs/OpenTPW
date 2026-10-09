using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class TpwsPayloadTests
{
	private static byte[] CreatePayload( IEnumerable<string> tags, int prefixLength = 16, int gap = 8 )
	{
		using var output = new MemoryStream();
		output.Write( new byte[prefixLength] );
		foreach ( var tag in tags )
		{
			output.Write( Encoding.ASCII.GetBytes( tag ) );
			output.Write( new byte[gap] );
		}
		return output.ToArray();
	}

	[TestMethod]
	public void ReportsPrefixAndMarkerDelimitedSectionsInOffsetOrder()
	{
		var payload = CreatePayload( SavePayloadLayout.ObservedEasymodeOrder, prefixLength: 10, gap: 6 );
		var layout = SavePayloadLayout.Parse( payload );
		Assert.AreEqual( payload.Length, layout.PayloadLength );
		Assert.AreEqual( 10, layout.PrefixLength );
		Assert.AreEqual( 17, layout.Sections.Count );
		Assert.IsTrue( layout.MatchesObservedEasymodeOrder );
		for ( var index = 0; index < layout.Sections.Count; index++ )
		{
			var section = layout.Sections[index];
			Assert.AreEqual( SavePayloadLayout.ObservedEasymodeOrder[index], section.Tag );
			Assert.AreEqual( 10 + index * 10, section.Offset );
			Assert.AreEqual( 10, section.Length );
		}
		Assert.AreEqual( new SavePayloadSection( "DLRW", 0x57524C44, 10, 10 ), layout.Sections[0] );
	}

	[TestMethod]
	public void AcceptsOtherOrderingWithoutClaimingObservedOrder()
	{
		var layout = SavePayloadLayout.Parse( CreatePayload( SavePayloadLayout.KnownSectionTags, prefixLength: 0, gap: 0 ) );
		Assert.AreEqual( 0, layout.PrefixLength );
		Assert.IsFalse( layout.MatchesObservedEasymodeOrder );
		CollectionAssert.AreEqual( SavePayloadLayout.KnownSectionTags.ToArray(), layout.Sections.Select( section => section.Tag ).ToArray() );
		Assert.IsTrue( layout.Sections.All( section => section.Length == 4 ) );
	}

	[TestMethod]
	public void ParsesThroughSaveReader()
	{
		var payload = CreatePayload( SavePayloadLayout.ObservedEasymodeOrder );
		using var reader = new SaveReader( new MemoryStream( SaveReaderTests.CreateContainer( payload, 400 ) ) );
		var layout = SavePayloadLayout.Parse( reader );
		Assert.AreEqual( payload.Length, layout.PayloadLength );
		Assert.AreEqual( 16, layout.PrefixLength );
	}

	[TestMethod]
	public void RejectsEmptyPayload()
	{
		Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( Array.Empty<byte>() ) );
	}

	[TestMethod]
	public void RejectsMissingMarker()
	{
		var payload = CreatePayload( SavePayloadLayout.ObservedEasymodeOrder.Where( tag => tag != "SYSR" ) );
		var exception = Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( payload ) );
		StringAssert.Contains( exception.Message, "SYSR" );
	}

	[TestMethod]
	public void RejectsDuplicateMarkerAsAmbiguous()
	{
		var payload = CreatePayload( SavePayloadLayout.ObservedEasymodeOrder.Append( "TRAP" ) );
		var exception = Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( payload ) );
		StringAssert.Contains( exception.Message, "TRAP" );
	}

	[TestMethod]
	public void RejectsUnalignedFalseMarkerInOpaqueBytes()
	{
		var payload = CreatePayload( SavePayloadLayout.ObservedEasymodeOrder );
		Encoding.ASCII.GetBytes( "xKOLC" ).CopyTo( payload, 3 );
		Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( payload ) );
	}

	[TestMethod]
	public void RejectsOverlappingMarkers()
	{
		var tags = SavePayloadLayout.ObservedEasymodeOrder.Where( tag => tag is not "ESSR" and not "RYLF" ).ToList();
		var payload = CreatePayload( tags ).Concat( Encoding.ASCII.GetBytes( "ESSRYLF" ) ).ToArray();
		var exception = Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( payload ) );
		StringAssert.Contains( exception.Message, "overlaps" );
	}

	[TestMethod]
	public void RejectsPayloadOverLimit()
	{
		var payload = new byte[SavePayloadLayout.MaximumPayloadBytes + 1];
		Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( payload ) );
	}

	[TestMethod]
	public void OriginalJungleEasyModePayloadMarkerLayout()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original read-only TPWI payload fixture." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		var path = Path.Combine( dataPath, "levels", "jungle", "Easymode.TPWI" );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( "The selected original Jungle Easymode.TPWI fixture is missing." );
		using var reader = new SaveReader( path );
		var payload = reader.ReadFile();
		Assert.AreEqual( "A3C9A28252C37AD49A8EB78E4A0C5E1D5229D01548FA35801DB67015D2589173", Convert.ToHexString( SHA256.HashData( payload ) ) );
		var layout = SavePayloadLayout.Parse( payload );
		Assert.AreEqual( 1608309, layout.PayloadLength );
		Assert.AreEqual( 1495462, layout.PrefixLength );
		Assert.AreEqual( "58521FF5B0804EEEA19358AC68CC3FC219BABB8BB3672E3687C892C21D75E3E5", Convert.ToHexString( SHA256.HashData( payload.AsSpan( 0, layout.PrefixLength ) ) ) );
		Assert.IsTrue( layout.MatchesObservedEasymodeOrder );
		var expected = new (string Tag, int Offset, int Length)[]
		{
			("DLRW", 1495462, 5456), ("CSPS", 1500918, 76256), ("TRAP", 1577174, 270), ("SSEM", 1577444, 12),
			("KOLC", 1577456, 8), ("TNAV", 1577464, 40), ("SYSG", 1577504, 17530), ("SYSR", 1595034, 48),
			("KART", 1595082, 456), ("RYLF", 1595538, 10860), ("ESSR", 1606398, 44), ("EMAK", 1606442, 20),
			("SAOC", 1606462, 376), ("SVDA", 1606838, 1449), ("NUOS", 1608287, 6), ("STHC", 1608293, 8),
			("CSDA", 1608301, 8),
		};
		CollectionAssert.AreEqual( expected, layout.Sections.Select( section => (section.Tag, section.Offset, section.Length) ).ToArray() );
		Assert.AreEqual( layout.PayloadLength, layout.PrefixLength + layout.Sections.Sum( section => section.Length ) );
	}
}
