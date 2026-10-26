using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// <c>base.lnd</c> and the procedural terrain texturer against the shipped data (docs/LND.md): the file identity and layout of all four themes,
/// the weight grid against the heightfield holes, and the texturer byte for byte against tp.exe's build routine 0x55F780. The golden hashes were
/// taken from the original routine run in the x86 oracle (tools/ppc-analysis/oracle_x86/terrain_golden.py); only hashes are kept here.
/// Inconclusive without OPENTPW_GAME_PATH; nothing of the game data is written.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ProceduralTerrainCorpusTests
{
	private BaseFileSystem? previous;

	// Parameters, grid size and the image sizes of the three sets (width x height in file order) per theme, as the shipped files hold them.
	private static readonly (string Theme, byte[] Parameters, string[] Sets)[] Pins =
	[
		("jungle", [48, 40, 40, 90, 68, 40, 97, 30, 55, 50],
			["212x212,192x192,242x242,192x192,222x222,192x212,192x292,282x282,222x222,242x242,192x192,222x222,192x212",
			 "106x106,96x96,121x121,96x96,111x111,96x106,96x146,141x141,111x111,121x121,96x96,111x111,96x106",
			 "53x53,48x48,60x60,48x48,55x55,48x53,48x73,70x70,55x55,60x60,48x48,55x55,48x53"]),
		("fantasy", [28, 50, 40, 12, 52, 40, 33, 30, 42, 43],
			["192x192,222x222,192x192,192x192,192x192,192x192,322x242,222x242,232x248,192x192,192x192,192x192,192x192",
			 "96x96,111x111,96x96,96x96,96x96,96x96,161x121,111x121,116x124,96x96,96x96,96x96,96x96",
			 "48x48,55x55,48x48,48x48,48x48,48x48,80x60,55x60,58x62,48x48,48x48,48x48,48x48"]),
		("hallow", [18, 10, 40, 30, 60, 40, 47, 10, 115, 60],
			["192x192,222x222,242x242,192x192,192x192,222x222,282x282,222x242,272x282,242x242,192x192,192x192,222x222",
			 "96x96,111x111,121x121,96x96,96x96,111x111,141x141,111x121,136x141,121x121,96x96,96x96,111x111",
			 "48x48,55x55,60x60,48x48,48x48,55x55,70x70,55x60,68x70,60x60,48x48,48x48,55x55"]),
		("space", [30, 0, 40, 50, 25, 50, 47, 50, 45, 60],
			["192x192,192x192,242x242,192x192,192x192,192x192,272x272,222x242,192x192,242x242,192x192,192x192,192x192",
			 "96x96,96x96,121x121,96x96,96x96,96x96,136x136,111x121,96x96,121x121,96x96,96x96,96x96",
			 "48x48,48x48,60x60,48x48,48x48,48x48,68x68,55x60,48x48,60x60,48x48,48x48,48x48"]),
	];

	[TestInitialize]
	public void Initialize()
	{
		previous = FileSystem;
		if ( ObjectCatalogCorpusTests.UseOriginalData( out _ ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the base.lnd corpus." );
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previous!;

	private static byte[] ReadLnd( string theme )
	{
		using var stream = FileSystem.OpenRead( $"/levels/{theme}/terrain/base.lnd" );
		using var copy = new MemoryStream();
		stream.CopyTo( copy );
		return copy.ToArray();
	}

	private static LndFile Load( string theme ) => new( new MemoryStream( ReadLnd( theme ) ) );

	private static string Sha( ReadOnlySpan<byte> bytes ) => Convert.ToHexString( SHA256.HashData( bytes ) ).ToLowerInvariant();

	[TestMethod]
	public void EveryThemesFileParsesToItsEndWithTheShippedLayout()
	{
		foreach ( var (theme, parameters, sets) in Pins )
		{
			var raw = ReadLnd( theme );
			var golden = ProceduralTerrainGolden.Themes.Single( item => item.Theme == theme );
			Assert.AreEqual( golden.FileSha256, Sha( raw ), $"{theme}: this is the base.lnd the golden hashes were taken from" );
			var lnd = new LndFile( new MemoryStream( raw ) ); // the reader refuses a parse that does not end at the file size
			Assert.AreEqual( 3, lnd.Version, theme );
			CollectionAssert.AreEqual( parameters, lnd.Parameters.ToArray(), theme );
			Assert.AreEqual( 384, lnd.GridWidth, theme );
			Assert.AreEqual( 344, lnd.GridHeight, theme );
			Assert.AreEqual( 95, lnd.CellsX, theme );
			Assert.AreEqual( 85, lnd.CellsY, theme );
			for ( var set = 0; set < 3; set++ )
			{
				Assert.AreEqual( 64 >> set, lnd.Sets[set].TextureSize, theme );
				Assert.AreEqual( sets[set], string.Join( ",", lnd.Sets[set].Images.Select( image => $"{image.Width}x{image.Height}" ) ), $"{theme} set {set}" );
				Assert.IsTrue( lnd.Sets[set].Images.Select( ( image, index ) => image.BytesPerPixel == (index < 6 ? 4 : 1) ).All( ok => ok ), $"{theme} set {set} pixel sizes" );
			}
			// every image tiles with the period (width - N, height - N): the last N columns and rows repeat the first ones
			foreach ( var set in lnd.Sets )
			{
				foreach ( var image in set.Images )
				{
					var pixels = image.Pixels.Span;
					var bytes = image.BytesPerPixel;
					var periodX = image.Width - set.TextureSize;
					var periodY = image.Height - set.TextureSize;
					for ( var y = 0; y < image.Height; y += 7 )
						for ( var x = 0; x < image.Width; x += 5 )
							for ( var channel = 0; channel < bytes; channel++ )
							{
								var here = pixels[(y * image.Width + x) * bytes + channel];
								if ( x < set.TextureSize )
									Assert.AreEqual( here, pixels[(y * image.Width + x + periodX) * bytes + channel], $"{theme} size {set.TextureSize}: column {x} repeats at {x + periodX} (row {y})" );
								if ( y < set.TextureSize )
									Assert.AreEqual( here, pixels[((y + periodY) * image.Width + x) * bytes + channel], $"{theme} size {set.TextureSize}: row {y} repeats at {y + periodY} (column {x})" );
							}
				}
			}
		}
	}

	[TestMethod]
	public void EveryNonHoleHeightfieldCellIsValidInTheWeightGrid()
	{
		foreach ( var (theme, _, _) in Pins )
		{
			var lnd = Load( theme );
			var field = OriginalPark.Load( theme, readShippedSave: false ).Heightfield;
			Assert.AreEqual( lnd.CellsY, field.CellCountZ, theme );
			Assert.AreEqual( lnd.CellsX + 1, field.CellCountX, $"{theme}: the heightfield is one column wider than the weight grid" );
			var valid = 0;
			var holes = 0;
			var holesValid = 0;
			for ( var z = 0; z < lnd.CellsY; z++ )
			{
				for ( var x = 0; x < lnd.CellsX; x++ )
				{
					var row = lnd.CellsY - z - 1;
					var inside = lnd.GridSample( 4 * x + 1, 4 * row + 1, 0 ) == 0xFF && lnd.GridSample( 4 * x + 2, 4 * row + 1, 0 ) == 0xFF
						&& lnd.GridSample( 4 * x + 1, 4 * row + 2, 0 ) == 0xFF && lnd.GridSample( 4 * x + 2, 4 * row + 2, 0 ) == 0xFF;
					valid += inside ? 1 : 0;
					if ( field.IsHole( x, z ) )
					{
						holes++;
						holesValid += inside ? 1 : 0;
					}
					else
					{
						Assert.IsTrue( inside, $"{theme}: the surface cell ({x}, {z}) has a valid weight cell" );
					}
				}
			}
			Console.WriteLine( $"LND {theme}: valid {valid} of {lnd.CellsX * lnd.CellsY}, holes {holes}, holes the grid marks valid {holesValid}" );
			Assert.IsTrue( valid > 6000, theme );
		}
	}

	[TestMethod]
	public void TheTexturerMatchesTheOriginalsBuildRoutineByteForByte()
	{
		var cells = 0;
		foreach ( var (theme, golden) in ProceduralTerrainGolden.Themes.Select( item => (item.Theme, item) ) )
		{
			var lnd = Load( theme );
			foreach ( var size in new[] { 16, 32, 64 } )
			{
				var texturer = new ProceduralTerrainTexturer( lnd, size );
				var buffer = new byte[texturer.TextureBytes];
				foreach ( var (_, x, z, sha) in golden.Cells.Where( cell => cell.Size == size ) )
				{
					texturer.Build( x, z, buffer );
					Assert.AreEqual( sha, Sha( buffer ), $"{theme} N={size} cell ({x}, {z})" );
					cells++;
				}
			}
		}
		Assert.IsTrue( cells > 200, $"{cells} golden cells" );
	}

	[TestMethod]
	public void TheGoldenSamplesIncludeEveryThemesGreyCells()
	{
		foreach ( var (theme, _, _) in Pins )
		{
			var lnd = Load( theme );
			var texturer = new ProceduralTerrainTexturer( lnd, 16 );
			var buffer = new byte[texturer.TextureBytes];
			var grey = Sha( Enumerable.Repeat( new byte[] { 0, 0x7F, 0x7F, 0x7F }, 256 ).SelectMany( item => item ).ToArray() );
			var greyCells = ProceduralTerrainGolden.Themes.Single( item => item.Theme == theme ).Cells.Where( cell => cell.Size == 16 && cell.Sha256 == grey ).ToArray();
			Assert.IsTrue( greyCells.Length >= 1, $"{theme}: the sample has an invalid cell" );
			foreach ( var cell in greyCells )
				Assert.IsFalse( texturer.Build( cell.X, cell.Z, buffer ), $"{theme} ({cell.X}, {cell.Z})" );
		}
	}

	[TestMethod]
	public void NeighbouringCellsContinueAcrossTheirEdges()
	{
		// Orientation (docs/LND.md): texel columns run along +x and buffer rows along +z. Across an edge the colour changes about as much as one texel inside the cell;
		// with a mirrored or turned cell it changes about 1.5 to 2 times as much (measured 13.7 against 12.7 here, and 20.7 to 28.4 for the mirrored readings).
		var lnd = Load( "jungle" );
		const int size = 32;
		var texturer = new ProceduralTerrainTexturer( lnd, size );
		var cells = new byte[16, 12][];
		for ( var x = 0; x < 16; x++ )
		{
			for ( var z = 0; z < 12; z++ )
			{
				cells[x, z] = new byte[texturer.TextureBytes];
				texturer.Build( 10 + x, 10 + z, cells[x, z] );
			}
		}
		double seam = 0, inner = 0;
		var count = 0;
		for ( var x = 0; x < 16; x++ )
		{
			for ( var z = 0; z < 12; z++ )
			{
				for ( var other = 0; other < size; other++ )
				{
					for ( var channel = 1; channel < 4; channel++ )
					{
						if ( x + 1 < 16 )
						{
							seam += Math.Abs( cells[x, z][(other * size + size - 1) * 4 + channel] - cells[x + 1, z][(other * size) * 4 + channel] );
							inner += Math.Abs( cells[x, z][(other * size + size - 2) * 4 + channel] - cells[x, z][(other * size + size - 1) * 4 + channel] );
							count++;
						}
						if ( z + 1 < 12 )
						{
							seam += Math.Abs( cells[x, z][((size - 1) * size + other) * 4 + channel] - cells[x, z + 1][other * 4 + channel] );
							inner += Math.Abs( cells[x, z][((size - 2) * size + other) * 4 + channel] - cells[x, z][((size - 1) * size + other) * 4 + channel] );
							count++;
						}
					}
				}
			}
		}
		Console.WriteLine( $"LND seam {seam / count:0.0}, inner {inner / count:0.0}" );
		Assert.IsTrue( seam < inner * 1.3, $"seam {seam / count:0.0} against inner {inner / count:0.0}" );
	}

	/// <summary>The heightfield's column 95 lies one past the 95 cells of the weight grid; tp.exe's routine builds it from the flat grid reads, and these hashes are what it wrote (x86 oracle, shipped jungle file).</summary>
	[TestMethod]
	public void TheColumnAfterTheWeightGridIsBuiltLikeTheOriginalBuildsIt()
	{
		var lnd = Load( "jungle" );
		(int Size, int X, int Z, string Sha256)[] golden =
		[
			(16, 95, 40, "8a1a810097a0a215a1bc2cc2b69c1d28332f0c279e5727e808e02e2be0bb9aa8"),
			(16, 95, 0, "0c655a1c205855b52a6bfeff4a4d3ec064fb3fa033394e2008499c7d818e6ed3"),
			(16, 95, 84, "642090cee6c0a252dc0162223adbce7f971bd34a70dbe4283d2ef839b2127cdf"),
			(32, 95, 40, "c3330812e85f4af05de7959de1b289b4cba7883f328a1bdb8bb1665c2b35d507"),
			(32, 95, 0, "47b32aff620bc92cdb7b5ffec7af02b8e9ead16fda39b8d50b164acfebbb13c1"),
			(32, 95, 84, "2c7b763b61082cc45039c0840bc9e9340a365b104d00585c0e347a79a86b4c77"),
		];
		foreach ( var (size, x, z, sha) in golden )
		{
			var texturer = new ProceduralTerrainTexturer( lnd, size );
			var buffer = new byte[texturer.TextureBytes];
			Assert.IsTrue( texturer.Build( x, z, buffer ), $"N={size} ({x}, {z})" );
			Assert.AreEqual( sha, Sha( buffer ), $"N={size} ({x}, {z})" );
		}
	}
}
