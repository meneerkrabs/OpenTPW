using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace OpenTPW.Tests;

/// <summary>
/// Pins every composed sign of the original data set against a frozen hash baseline: the SHA-256 of
/// the CPU RGBA halves <see cref="ObjectSigns.Get"/> returns (the public compose path, end to end) and
/// of the raw <c>.sgn</c> member, for all 75 signs of the four themes. The baseline was captured at
/// v0.7.13 (dc3b1aef) before the rasterizer's active-edge window; it is hashes only (no game data is
/// reproduced), stored in <c>Font/sign-hash-baseline-v0713-dc3b1aef.tsv</c>. Inconclusive without
/// <c>OPENTPW_GAME_PATH</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SignRasterCorpusHashTests
{
	private const string BaselineName = "sign-hash-baseline-v0713-dc3b1aef.tsv";

	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the sign raster corpus." );
		originalFileSystem = previous;
		initialized = true;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
	}

	private static string BaselineTsv()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var path = Path.Combine( directory.FullName, "Font", BaselineName );
			if ( File.Exists( path ) )
				return path;
		}
		Assert.Fail( $"{BaselineName} not found above the test directory." );
		return "";
	}

	private sealed record BaselineRow( string Theme, int InfoId, string ItemId, string DisplayName, string Line1, string Line2, string SgnSha, string LeftSha, string RightSha, int TextPixels );

	private static List<BaselineRow> ReadBaseline()
	{
		var rows = new List<BaselineRow>();
		foreach ( var line in File.ReadAllLines( BaselineTsv() ) )
		{
			if ( line.Length == 0 || line[0] == '#' || line.StartsWith( "theme\t" ) )
				continue;
			var column = line.Split( '\t' );
			Assert.AreEqual( 36, column.Length, $"baseline row must have 36 columns: {line}" );
			rows.Add( new BaselineRow( column[0], int.Parse( column[2] ), column[1], column[3], column[6], column[7], column[10], column[11], column[12], int.Parse( column[13] ) ) );
		}
		return rows;
	}

	[TestMethod]
	public void EveryComposedSignMatchesTheFrozenBaselineHashes()
	{
		var baseline = ReadBaseline();
		Assert.AreEqual( 75, baseline.Count, "the frozen corpus has 75 composed signs" );

		var entries = new Dictionary<(string Theme, int InfoId), ObjectCatalogEntry>();
		foreach ( var theme in new[] { "jungle", "hallow", "fantasy", "space" } )
		{
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				Assert.IsTrue( entries.TryAdd( (theme, entry.InfoId), entry ), $"duplicate info id {theme}/{entry.InfoId} in the catalog" );
			}
		}

		var mismatches = new List<string>();
		var composed = 0;
		foreach ( var expected in baseline )
		{
			Assert.AreEqual( $"{expected.Theme}/{expected.InfoId}", expected.ItemId, "baseline item ids are theme/infoId" );
			if ( !entries.TryGetValue( (expected.Theme, expected.InfoId), out var entry ) )
			{
				mismatches.Add( $"{expected.ItemId}: no catalog entry" );
				continue;
			}
			Assert.AreEqual( expected.DisplayName, entry.DisplayName, $"{expected.ItemId} display name" );
			Assert.AreEqual( (expected.Line1, expected.Line2), ObjectSigns.Lines( entry ), $"{expected.ItemId} sign lines" );

			var images = ObjectSigns.Get( entry );
			if ( images == null )
			{
				mismatches.Add( $"{expected.ItemId}: no sign images" );
				continue;
			}
			composed++;
			void Check( string what, string expectedHash, byte[] actual )
			{
				var actualHash = Convert.ToHexString( SHA256.HashData( actual ) ).ToLowerInvariant();
				if ( actualHash != expectedHash )
					mismatches.Add( $"{expected.ItemId} {what}: expected {expectedHash}, got {actualHash}" );
			}
			Check( "left half", expected.LeftSha, images.Left );
			Check( "right half", expected.RightSha, images.Right );
			if ( expected.TextPixels != images.TextPixelCount )
				mismatches.Add( $"{expected.ItemId} text pixels: expected {expected.TextPixels}, got {images.TextPixelCount}" );

			// The .sgn member the compose read, pinned by its raw bytes.
			var member = entry.FileSystem.GetFiles( entry.ArchivePath ).FirstOrDefault( name => name.EndsWith( ".sgn", StringComparison.OrdinalIgnoreCase ) );
			Assert.IsNotNull( member, $"{expected.ItemId}: no .sgn member in {entry.ArchivePath}" );
			var sgnPath = $"{entry.ArchivePath.TrimEnd( '/' )}/{Path.GetFileName( member )}";
			Check( "sgn member", expected.SgnSha, entry.FileSystem.ReadAllBytes( sgnPath ) );
		}

		Assert.AreEqual( baseline.Count, composed, "every baseline row composed" );
		CollectionAssert.AreEqual( Array.Empty<string>(), mismatches, string.Join( "\n", mismatches ) );
	}
}
