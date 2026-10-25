using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The visible-cell cache of the procedural terrain (tp.exe 0x572110 and 0x572440, docs/LND.md) and the option mapping around it. The slot order is the one the original's ring of nodes gives; no game data is needed.
/// </summary>
[TestClass]
public class ProceduralTextureCacheTests
{
	private static int[] Keys( params int[] cells ) => cells.Select( cell => ProceduralTextureCache.KeyOf( cell % 100, cell / 100, 0, 0 ) ).ToArray();

	private static (int[] Slots, bool[] Build) Resolve( ProceduralTextureCache cache, int[] keys, bool[]? dirty = null )
	{
		var slots = new int[keys.Length];
		var build = new bool[keys.Length];
		cache.Resolve( keys, slots, build, dirty ?? [] );
		return (slots, build);
	}

	[TestMethod]
	public void TheFirstFrameTakesTheNodesBackwardsFromTheMiddleOne()
	{
		var cache = new ProceduralTextureCache( 8 );
		var (slots, build) = Resolve( cache, Keys( 0, 1, 2, 3, 4, 5, 6, 7 ) );
		CollectionAssert.AreEqual( new[] { 3, 2, 1, 0, 7, 6, 5, 4 }, slots );
		Assert.IsTrue( build.All( built => built ) );
		Assert.AreEqual( 8, cache.Misses );
	}

	[TestMethod]
	public void ACellInViewKeepsItsSlotAndIsNotBuiltAgain()
	{
		var cache = new ProceduralTextureCache( 8 );
		var (first, _) = Resolve( cache, Keys( 0, 1, 2, 3 ) );
		var (again, build) = Resolve( cache, Keys( 0, 1, 2, 3 ) );
		CollectionAssert.AreEqual( first, again );
		Assert.IsFalse( build.Any( built => built ) );
		Assert.AreEqual( 4, cache.Hits );
		Assert.AreEqual( 4, cache.Misses );
	}

	[TestMethod]
	public void ScrollingBuildsOnlyTheNewCellsAndEvictsTheOnesOutOfView()
	{
		var cache = new ProceduralTextureCache( 6 );
		var (first, _) = Resolve( cache, Keys( 0, 1, 2, 3 ) );
		var (second, build) = Resolve( cache, Keys( 2, 3, 4, 5 ) );
		CollectionAssert.AreEqual( new[] { false, false, true, true }, build );
		Assert.AreEqual( first[2], second[0] );
		Assert.AreEqual( first[3], second[1] );
		// the new cells did not take the slots of the cells that stayed in view
		Assert.IsFalse( second.Skip( 2 ).Intersect( second.Take( 2 ) ).Any() );
		// and after the view moved on a third time, the cells of the first frame are the oldest, so they are the ones that go
		var (third, buildThird) = Resolve( cache, Keys( 4, 5, 6, 7 ) );
		Assert.IsTrue( buildThird[2] && buildThird[3] );
		Assert.IsTrue( third.Skip( 2 ).All( slot => first.Contains( slot ) ), "the oldest slots were taken" );
		Assert.AreEqual( -1, cache.SlotOf( Keys( 0 )[0] ), "the first frame's cells were taken by the third" );
	}

	[TestMethod]
	public void ADirtyCellInViewIsBuiltAgainInItsSlot()
	{
		var cache = new ProceduralTextureCache( 8 );
		var (first, _) = Resolve( cache, Keys( 0, 1, 2 ) );
		var (again, build) = Resolve( cache, Keys( 0, 1, 2 ), [false, true, false] );
		CollectionAssert.AreEqual( first, again );
		CollectionAssert.AreEqual( new[] { false, true, false }, build );
	}

	[TestMethod]
	public void AViewOfMoreCellsThanTheCacheHoldsReusesASlotInTheSameFrame()
	{
		var cache = new ProceduralTextureCache( 4 );
		var (slots, build) = Resolve( cache, Keys( 0, 1, 2, 3, 4, 5 ) );
		Assert.IsTrue( build.All( built => built ) );
		Assert.AreEqual( 4, slots.Distinct().Count() );
		Assert.AreEqual( slots[0], slots[4], "the fifth cell took the slot of the first" );
		Assert.AreEqual( -1, cache.SlotOf( Keys( 0 )[0] ), "the first cell lost its entry" );
	}

	[TestMethod]
	public void RandomViewsNeverShareASlotWhileTheyFitTheCache()
	{
		var random = new Random( 12345 );
		var cache = new ProceduralTextureCache( 300 );
		var owner = new System.Collections.Generic.Dictionary<int, int>();   // slot -> key that the slot was last given to
		for ( var frame = 0; frame < 400; frame++ )
		{
			// a window of cells that drifts, like a camera
			var x0 = random.Next( 0, 40 );
			var z0 = random.Next( 0, 40 );
			var width = random.Next( 5, 18 );
			var height = random.Next( 5, 17 );
			var keys = new System.Collections.Generic.List<int>();
			for ( var z = z0; z < z0 + height; z++ )
				for ( var x = x0; x < x0 + width; x++ )
					if ( random.Next( 10 ) != 0 )
						keys.Add( ProceduralTextureCache.KeyOf( x, z, 0, 0 ) );
			Assert.IsTrue( keys.Count <= 300 );
			var (slots, build) = Resolve( cache, keys.ToArray() );
			Assert.AreEqual( keys.Count, slots.Distinct().Count(), $"frame {frame}: two cells of one view share a slot" );
			for ( var index = 0; index < keys.Count; index++ )
			{
				Assert.AreEqual( slots[index], cache.SlotOf( keys[index] ), $"frame {frame}: the table agrees" );
				// a slot's pixels belong to the key it was last built for: a cell that hit finds its own pixels there
				if ( !build[index] )
					Assert.AreEqual( keys[index], owner[slots[index]], $"frame {frame}: a hit finds the pixels of its own cell" );
				else
					owner[slots[index]] = keys[index];
			}
		}
	}

	[TestMethod]
	public void TheKeyIsTheRowAndColumnInsideTheRectangle()
	{
		Assert.AreEqual( (7 << 8) + 3, ProceduralTextureCache.KeyOf( 13, 27, 10, 20 ) );
		Assert.AreEqual( -1, ProceduralTextureCache.KeyOf( 9, 27, 10, 20 ), "left of the rectangle" );
		Assert.AreEqual( -1, ProceduralTextureCache.KeyOf( 10, 20 + 128, 10, 20 ), "more than 128 rows" );
	}

	[TestMethod]
	public void TheOptionsMapToTheOriginalsSizesAndCaches()
	{
		const long enough = 512L * 1024 * 1024;
		Assert.AreEqual( new ProceduralTerrainSettings( 0, 1500 ), ProceduralTerrainSettings.For( 0, 0, enough ) );
		Assert.AreEqual( new ProceduralTerrainSettings( 16, 1500 ), ProceduralTerrainSettings.For( 1, 0, enough ) );
		Assert.AreEqual( new ProceduralTerrainSettings( 32, 2000 ), ProceduralTerrainSettings.For( 2, 1, enough ) );
		Assert.AreEqual( new ProceduralTerrainSettings( 64, 2500 ), ProceduralTerrainSettings.For( 3, 2, enough ) );
		Assert.IsFalse( ProceduralTerrainSettings.For( 0, 2, enough ).Enabled );
		Assert.IsFalse( ProceduralTerrainSettings.For( 7, 2, enough ).Enabled, "a value the option apply does not know leaves the texturing off" );
	}

	[TestMethod]
	public void LittleMemoryShrinksTheTextures()
	{
		Assert.AreEqual( 16, ProceduralTerrainSettings.For( 3, 0, 40L * 1024 * 1024 ).Size, "below 48 MB every size is 16" );
		Assert.AreEqual( 16, ProceduralTerrainSettings.For( 2, 0, 40L * 1024 * 1024 ).Size );
		Assert.AreEqual( 32, ProceduralTerrainSettings.For( 3, 0, 60L * 1024 * 1024 ).Size, "below 64 MB a 64 becomes 32" );
		Assert.AreEqual( 32, ProceduralTerrainSettings.For( 2, 0, 60L * 1024 * 1024 ).Size );
		Assert.AreEqual( 64, ProceduralTerrainSettings.For( 3, 0, 64L * 1024 * 1024 ).Size );
		Assert.AreEqual( 0, ProceduralTerrainSettings.For( 0, 0, 1 ).Size, "off stays off" );
	}

	[TestMethod]
	public void TheRectangleFollowsTheLevelsHeightfieldKeys()
	{
		var shared = SamDocument.Parse( "MapInfo.HeightfieldXStart 0\nMapInfo.HeightfieldYStart 0\nMapInfo.HeightfieldWidth 95\nMapInfo.HeightfieldHeight 84\nThemeEngine.ProceduralTexturingXOffset -1\nThemeEngine.ProceduralTexturingYOffset -1\n", "synthetic" );
		Assert.AreEqual( new ProceduralTerrainRectangle( 0, 0, 95, 84 ), ProceduralTerrainRectangle.From( new SamSettings( [shared] ) ), "a negative offset adds nothing, the end is inclusive" );
		var offset = SamDocument.Parse( "MapInfo.HeightfieldXStart 2\nMapInfo.HeightfieldYStart 3\nMapInfo.HeightfieldWidth 10\nMapInfo.HeightfieldHeight 20\nThemeEngine.ProceduralTexturingXOffset 4\nThemeEngine.ProceduralTexturingYOffset 5\n", "synthetic" );
		Assert.AreEqual( new ProceduralTerrainRectangle( 2, 3, 16, 28 ), ProceduralTerrainRectangle.From( new SamSettings( [offset] ) ) );
		Assert.IsNull( ProceduralTerrainRectangle.From( new SamSettings( [SamDocument.Parse( "Other.Key 1\n", "synthetic" )] ) ), "a level without the keys has no rectangle" );
		var rectangle = new ProceduralTerrainRectangle( 0, 0, 95, 84 );
		Assert.IsTrue( rectangle.Contains( 95, 84 ) );
		Assert.IsFalse( rectangle.Contains( 96, 0 ) );
		Assert.IsFalse( rectangle.Contains( 0, 85 ) );
	}

	[TestMethod]
	public void TheAtlasPacksTheCacheIntoPagesOfAtMost2048Texels()
	{
		Assert.AreEqual( 1, ProceduralTerrainAtlas.PageCountFor( 16, 2500 ) );
		Assert.AreEqual( 1, ProceduralTerrainAtlas.PageCountFor( 32, 2500 ) );
		Assert.AreEqual( 3, ProceduralTerrainAtlas.PageCountFor( 64, 2500 ), "1024 tiles of 64 texels fill a page" );
		Assert.AreEqual( 2, ProceduralTerrainAtlas.PageCountFor( 64, 1500 ) );
		Assert.AreEqual( 1, ProceduralTerrainAtlas.PageCountFor( 64, 1024 ) );
	}
}
