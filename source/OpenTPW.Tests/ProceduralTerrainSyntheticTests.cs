using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The texturer against tp.exe's build routine 0x55F780 on reproducible synthetic levels (<see cref="SyntheticTerrain"/>), without any game data, so CI
/// pins what the with-data corpus test pins: the 16.16 weight interpolation on gradient weights, general random ramps, per-cell window offsets,
/// N = 64, every run mode and the grey fallback. The hashes come from the original routine run in the x86 oracle
/// (tools/ppc-analysis/oracle_x86/terrain_synthetic_golden.py).
/// </summary>
[TestClass]
public class ProceduralTerrainSyntheticTests
{
	private static string Sha( ReadOnlySpan<byte> bytes ) => Convert.ToHexString( SHA256.HashData( bytes ) ).ToLowerInvariant();

	[TestMethod]
	public void TheTexturerMatchesTheOriginalOnSyntheticLevels()
	{
		var levels = new Dictionary<int, LndFile>();
		foreach ( var (seed, mix, size, grey, x, z, sha) in ProceduralTerrainSyntheticGolden.Cells )
		{
			if ( !levels.TryGetValue( seed, out var lnd ) )
				levels[seed] = lnd = SyntheticTerrain.Load( seed, mix, grey );
			var texturer = new ProceduralTerrainTexturer( lnd, size );
			var buffer = new byte[texturer.TextureBytes];
			var built = texturer.Build( x, z, buffer );
			Assert.AreEqual( sha, Sha( buffer ), $"seed {seed} {mix} N={size} cell ({x}, {z})" );
			Assert.AreEqual( !(grey && x == 1 && z == 1), built, $"seed {seed} cell ({x}, {z}) validity" );
		}
	}

	[TestMethod]
	public void TheSyntheticSampleCoversEverySizeMixAndTheGreyFallback()
	{
		var cells = ProceduralTerrainSyntheticGolden.Cells;
		Assert.AreEqual( 54, cells.Length );
		foreach ( var size in new[] { 16, 32, 64 } )
		{
			Assert.IsTrue( SyntheticTerrain.Mixes.All( mix => cells.Any( cell => cell.Size == size && cell.Mix == mix && !cell.Grey ) ), $"every mix at N={size}" );
			Assert.IsTrue( cells.Any( cell => cell.Size == size && cell.Grey ), $"a grey level at N={size}" );
		}
		Assert.AreEqual( cells.Length, cells.Select( cell => cell.Sha256 ).Distinct().Count(), "no two cells share a texture" );
	}
}
