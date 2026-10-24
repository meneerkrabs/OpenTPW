using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The pool ground rule against the shipped land (docs/POOLS.md, "The ground rule"): how much of each theme's starting land is level enough for a pool cell.
/// Inconclusive without OPENTPW_GAME_PATH; nothing of the game data is written.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PoolsTerrainCorpusTests
{
	private BaseFileSystem? previous;

	[TestInitialize]
	public void Initialize()
	{
		previous = FileSystem;
		if ( ObjectCatalogCorpusTests.UseOriginalData( out _ ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the shipped land." );
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previous!;

	/// <summary>The corner height spread of every owned, allowed cell of a theme's starting land, in engine units.</summary>
	private static float[] Spreads( string theme, bool ownedOnly = true )
	{
		var settings = GuestSettings.Load( theme );
		var setup = ParkLandSetup.FromLevel( settings.EntranceA, settings.EntranceB, FileSystem.ReadAllText( $"/levels/{theme}/Hoardings.sam" ) )!;
		var park = OriginalPark.Load( theme, readShippedSave: false );
		var cells = ParkCellMap.FromOriginal( park.Map, null, setup, park.Heightfield );
		var grid = new OriginalParkGrid( park );
		var spreads = new System.Collections.Generic.List<float>();
		for ( var y = 0; y < grid.Height; y++ )
			for ( var x = 0; x < grid.Width; x++ )
				if ( (!ownedOnly || !cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned )) && grid.CheckTerrain( x, y ) == OriginalPlacementResult.Allowed && cells.RawTypeAt( x, y ) == 0 )
					spreads.Add( PoolBuilder.CornerSpread( grid, x, y ) );
		return spreads.ToArray();
	}

	[TestMethod]
	public void EveryThemeHasLevelGroundForPoolsAndNotEverywhere()
	{
		foreach ( var theme in new[] { "jungle", "hallow", "space", "fantasy" } )
		{
			var all = Spreads( theme, ownedOnly: false ).Order().ToArray();
			Console.WriteLine( $"POOLSPREAD all {theme}: cells {all.Length}, <=0.2 {all.Count( spread => spread <= 0.2f ) / (float)all.Length:P1}, <=0.25 {all.Count( spread => spread <= 0.25f ) / (float)all.Length:P1}, exactly level {all.Count( spread => spread == 0 ) / (float)all.Length:P1}, refused {all.Count( spread => spread > PoolBuilder.MaximumCornerSpread )} of {all.Length}, max {all[^1]:0.00}" );
			Assert.IsTrue( all.Count( spread => spread > PoolBuilder.MaximumCornerSpread ) < all.Length / 100, $"{theme}: the rule refuses under one cell in a hundred of the buildable land" );
			var spreads = Spreads( theme );
			Assert.IsTrue( spreads.Length > 500, $"{theme}: {spreads.Length} free owned cells" );
			var level = spreads.Count( spread => spread <= PoolBuilder.MaximumCornerSpread ) / (float)spreads.Length;
			var sorted = spreads.Order().ToArray();
			Console.WriteLine( $"POOLSPREAD {theme}: cells {spreads.Length}, level share {level:P1}, p10 {sorted[sorted.Length / 10]:0.000}, p25 {sorted[sorted.Length / 4]:0.000}, p50 {sorted[sorted.Length / 2]:0.000}, p75 {sorted[sorted.Length * 3 / 4]:0.000}, p90 {sorted[sorted.Length * 9 / 10]:0.000}, max {sorted[^1]:0.000}" );
			Assert.IsTrue( level > 0.99f, $"{theme}: {level:P1} of the starting land is level enough" );
		}
	}
}
