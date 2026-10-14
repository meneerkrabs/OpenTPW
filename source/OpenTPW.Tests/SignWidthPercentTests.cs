using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>COMPAT-005: the slot's percent scales each character's width, truncated; COMPAT-017: the two 32-character sign lines.</summary>
[TestClass]
public class SignWidthPercentTests
{
	private static TrueTypeFont Font( params (ushort Advance, char Character)[] glyphs )
	{
		var builder = new TrueTypeFontTests.FontBuilder();
		foreach ( var (advance, character) in glyphs )
			builder.AddSimple( advance, character, TrueTypeFontTests.Square( 0, 0, 500, 500 ) );
		return TrueTypeFont.Parse( builder.Build() );
	}

	[TestMethod]
	public void EachCharacterWidthIsScaledByThePercentAndTruncated()
	{
		var font = Font( (600, 'A'), (700, 'B') );
		// 100 px em: A is 60 px, B 70 px.
		Assert.AreEqual( 130f, SignTextLayout.Measure( font, "AB", 100 ).Width, 1e-4f );
		Assert.AreEqual( 130f, SignTextLayout.Measure( font, "AB", 100, widthPercent: 100 ).Width, 1e-4f );
		Assert.AreEqual( 54f + 63f, SignTextLayout.Measure( font, "AB", 100, widthPercent: 90 ).Width, 1e-4f );
		Assert.AreEqual( 75f + 87f, SignTextLayout.Measure( font, "AB", 100, widthPercent: 125 ).Width, 1e-4f, "87.5 truncates to 87 per character" );
		Assert.AreEqual( 51f + 59f, SignTextLayout.Measure( font, "AB", 100, widthPercent: 85 ).Width, 1e-4f );
		// The horizontal LOGFONT scale applies first: at twice the width the characters are 120 and 140 px.
		Assert.AreEqual( 108f + 126f, SignTextLayout.Measure( font, "AB", 100, horizontalScale: 2, widthPercent: 90 ).Width, 1e-4f );
	}

	[TestMethod]
	public void ThePercentNarrowsTheFontSizeFit()
	{
		var font = Font( (600, 'A') );
		var line = new string( 'A', 10 );
		// Ten A at 100 px em are 600 px wide: over a 512 canvas the width must be narrowed, but at 80 percent (480 px) the line fits as it is.
		Assert.AreEqual( 0, SignTextLayout.FitLogFontWidth( font, line, 100, 0, 512, widthPercent: 80 ) );
		Assert.AreNotEqual( 0, SignTextLayout.FitLogFontWidth( font, line, 100, 0, 512, widthPercent: 100 ) );
	}

	[TestMethod]
	public void ThePercentChangesTheDrawnLine()
	{
		var font = Font( (600, 'A') );
		var narrow = SignTextLayout.Rasterize( font, "AAA", 100, widthPercent: 70 );
		var wide = SignTextLayout.Rasterize( font, "AAA", 100, widthPercent: 100 );
		Assert.IsTrue( narrow.Width < wide.Width, $"{narrow.Width} < {wide.Width}" );
	}

	[TestMethod]
	public void ASignLineKeepsThirtyTwoCharactersAndLineOneLosesATrailingDoubleHyphen()
	{
		Assert.AreEqual( 32, BonusNames.SignLineCharacters );
		Assert.AreEqual( "Mighty Mayhem", BonusNames.StripTrailingDashes( "Mighty Mayhem--" ) );
		Assert.AreEqual( "Mighty Mayhem-", BonusNames.StripTrailingDashes( "Mighty Mayhem-" ) );
		Assert.AreEqual( "-", BonusNames.StripTrailingDashes( "-" ) );
		Assert.AreEqual( "", BonusNames.StripTrailingDashes( "--" ) );
		Assert.AreEqual( "A--B", BonusNames.StripTrailingDashes( "A--B" ) );
		Assert.AreEqual( "x-", BonusNames.StripTrailingDashes( "x---" ) );
	}
}
