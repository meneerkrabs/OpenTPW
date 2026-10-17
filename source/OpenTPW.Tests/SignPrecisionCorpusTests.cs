using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// The x87 precision control the sign generator ran at (24 bits, see <see cref="X87Precision"/>) against the other two the
/// instruction set offers (53 and 64): the composed halves of all 75 corpus signs are compared byte by byte. Inconclusive
/// without <c>OPENTPW_GAME_PATH</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SignPrecisionCorpusTests
{
	[TestMethod]
	public void TheCorpusUnderEachPrecisionControl()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the sign precision corpus." );
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			var report = new List<string>();
			var signs = 0;
			var different = new Dictionary<int, int>();
			var largest = new Dictionary<int, int>();
			foreach ( var theme in new[] { "jungle", "hallow", "fantasy", "space" } )
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				var sign = SignTextRenderer.LoadSignFile( entry.ArchivePath, entry.FileSystem );
				if ( sign == null ) continue;
				var (line1, line2) = ObjectSigns.Lines( entry );
				signs++;
				byte[] Compose( int bits ) => X87Precision.With( bits, () =>
				{
					var composed = SignTextRenderer.Shared.ComposeSign( sign, new[] { line1, line2 }, default );
					return composed.Left.Concat( composed.Right ).ToArray();
				} );
				var reference = Compose( 24 );
				foreach ( var bits in new[] { 53, 64 } )
				{
					var other = Compose( bits );
					var worst = 0;
					for ( var i = 0; i < reference.Length; i++ )
						worst = Math.Max( worst, Math.Abs( reference[i] - other[i] ) );
					if ( worst != 0 )
					{
						different[bits] = different.GetValueOrDefault( bits ) + 1;
						largest[bits] = Math.Max( largest.GetValueOrDefault( bits ), worst );
						report.Add( $"{theme}/{entry.InfoId} at {bits}: largest byte difference {worst}" );
					}
				}
			}
			Console.WriteLine( $"PRECISION-CORPUS signs={signs} differing53={different.GetValueOrDefault( 53 )} (max {largest.GetValueOrDefault( 53 )}) differing64={different.GetValueOrDefault( 64 )} (max {largest.GetValueOrDefault( 64 )})" );
			foreach ( var line in report )
				Console.WriteLine( "PRECISION-CORPUS " + line );
			Assert.IsTrue( signs >= 75, "the 75 corpus signs compose" );
			Assert.IsTrue( different.GetValueOrDefault( 53 ) > 0, "the precision control is observable in the composed signs, so matching it matters" );
		}
		finally
		{
			FileSystem = previous;
			ObjectCatalog.BonusDataRoot = null;
		}
	}
}
