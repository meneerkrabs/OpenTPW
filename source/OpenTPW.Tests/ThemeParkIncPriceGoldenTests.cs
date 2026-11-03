using System;
using System.Globalization;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's guest price test against the executable's own <c>0x6120C0</c> run in the x86 oracle (ECON-062, finding X62): every row of <see cref="ThemeParkIncPriceGolden"/> is a synthetic guest, attraction and type
/// record, and <see cref="ThemeParkIncPriceTest"/> must reproduce the verdict, the three history rings the test raised and the perception statistics it recorded.
/// </summary>
[TestClass]
public class ThemeParkIncPriceGoldenTests
{
	private static float Single( string hex ) => BitConverter.UInt32BitsToSingle( Convert.ToUInt32( hex, 16 ) );

	[TestMethod]
	public void EveryOracleRowIsReproducedByThePort()
	{
		Assert.IsTrue( ThemeParkIncPriceGolden.Rows.Length > 60 );
		var verdicts = 0;
		foreach ( var row in ThemeParkIncPriceGolden.Rows )
		{
			var parts = row.Split( '|', StringSplitOptions.TrimEntries );
			var a = parts[0].Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Select( item => int.Parse( item, CultureInfo.InvariantCulture ) ).ToArray();
			var rest = parts[1].Split( '>', StringSplitOptions.TrimEntries );
			var meters = rest[0].Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Select( Single ).ToArray();
			var outputs = rest[1].Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Select( item => int.Parse( item, CultureInfo.InvariantCulture ) ).ToArray();
			var statistics = parts[2].Length == 0 ? [] : parts[2].Split( ',' ).Select( item => item.Split( ':' ) ).Select( pair => (int.Parse( pair[0], CultureInfo.InvariantCulture ), int.Parse( pair[1], CultureInfo.InvariantCulture )) ).ToArray();
			// kind price goods win expected thirst hunger vomit happy typeword rating preferred ingredient appearance money cheap
			var terms = new TpiPriceTerms( a[1], (TpiPriceKind)a[0], a[2], a[3], a[4], a[5], a[6], a[7], a[8], a[9], a[10], a[11], a[12], a[13] );
			var verdict = ThemeParkIncPriceTest.Decide( terms, meters[0], meters[1], meters[2], meters[3], unchecked( (uint)a[14] ), a[15] );
			Assert.AreEqual( outputs[0] != 0, verdict.TooExpensive, row );
			Assert.AreEqual( outputs[1] != 0, verdict.CheapPrice, row );
			Assert.AreEqual( outputs[2] != 0, verdict.ExpensivePrice, row );
			Assert.AreEqual( outputs[3] != 0, verdict.Declined, row );
			CollectionAssert.AreEqual( statistics, verdict.Statistics, row );
			verdicts += verdict.TooExpensive ? 1 : 0;
		}
		Assert.IsTrue( verdicts > 10 && verdicts < ThemeParkIncPriceGolden.Rows.Length - 10, "the rows hold both verdicts" );
	}
}
