using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The park's land against the shipped data: every theme's Hoardings.sam lays a fence, and the Jungle Easymode save's
/// own map flags 0x40 and hoardings check the fill model and the computed starting land. Inconclusive without
/// OPENTPW_GAME_PATH; nothing of the game data is written.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkLandOriginalDataTests
{
	private BaseFileSystem? previous;

	[TestInitialize]
	public void Initialize()
	{
		previous = FileSystem;
		if ( ObjectCatalogCorpusTests.UseOriginalData( out _ ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the shipped fences." );
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previous!;

	private static ParkLandSetup Setup( string theme )
	{
		var settings = GuestSettings.Load( theme );
		var setup = ParkLandSetup.FromLevel( settings.EntranceA, settings.EntranceB, FileSystem.ReadAllText( $"/levels/{theme}/Hoardings.sam" ) );
		Assert.IsNotNull( setup, theme );
		return setup!;
	}

	private static bool Owned( ParkCellMap cells, int x, int y ) => !cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );

	[TestMethod]
	public void EveryThemesHoardingsLayAFenceFromPostAToPostB()
	{
		foreach ( var theme in new[] { "jungle", "hallow", "space", "fantasy" } )
		{
			var setup = Setup( theme );
			Assert.AreEqual( (51, 17), setup.PostA, theme );
			Assert.AreEqual( (43, 17), setup.PostB, theme );
			var land = ParkLand.FromClicks( setup );
			var park = OriginalPark.Load( theme, readShippedSave: false );
			var cells = ParkCellMap.FromOriginal( park.Map, null, setup );
			Assert.IsNotNull( cells.Land, theme );
			var owned = Enumerable.Range( 0, 128 * 128 ).Count( index => Owned( cells, index % 128, index / 128 ) );
			Assert.IsTrue( owned > 1000 && owned < 4000, $"{theme}: {owned} owned cells" );
			// the entrance's path is owned and so is the strip at the gate, the land north of the gate is not
			Assert.IsTrue( Owned( cells, 47, 17 ) && Owned( cells, 48, 17 ) && Owned( cells, 45, 17 ) && Owned( cells, 50, 18 ), theme );
			Assert.IsFalse( Owned( cells, 47, 15 ), theme );
			Assert.AreEqual( land.Ring.Count, cells.Land!.Ring.Count, theme );
		}
	}

	/// <summary>
	/// The Easymode park's land is bigger than the Jungle's starting land (land was bought before it was saved): every cell of
	/// the computed starting land is owned in the save, which owns 674 cells more inside its own, larger fence.
	/// </summary>
	[TestMethod]
	public void TheComputedStartingLandIsInsideTheEasymodeParksLand()
	{
		var park = OriginalPark.Load( "jungle" );
		var save = park.Save!;
		var setup = Setup( "jungle" );
		var computed = ParkCellMap.FromOriginal( park.Map, null, setup, park.Heightfield );
		int both = 0, onlyComputed = 0, onlySaved = 0;
		for ( var y = 0; y < 128; y++ )
		{
			for ( var x = 0; x < 128; x++ )
			{
				var saved = (save.Cells[x, y].MapFlags & 0x40) == 0;
				var mine = Owned( computed, x, y );
				both += saved && mine ? 1 : 0;
				onlyComputed += !saved && mine ? 1 : 0;
				onlySaved += saved && !mine ? 1 : 0;
			}
		}
		Assert.AreEqual( (1832, 0, 674), (both, onlyComputed, onlySaved), "owned in both, only computed, only saved" );
	}

	/// <summary>
	/// The fill model against the original: the Easymode save's own fence, walked from post A by its hoardings' links, owns
	/// exactly the cells the save has without flag 0x40 (with the gate strip and the fixed cells), cell for cell.
	/// </summary>
	[TestMethod]
	public void TheEasymodeFenceOwnsExactlyTheSavedLand()
	{
		var park = OriginalPark.Load( "jungle" );
		var save = park.Save!;
		var setup = Setup( "jungle" );
		var imported = ParkCellMap.FromOriginal( park.Map, save, setup );
		Assert.IsNotNull( imported.Land );
		Assert.AreEqual( 143, imported.Land!.Ring.Count, "the save's fence posts from post A to post B" );
		Assert.AreEqual( save.Cells.CountX * save.Cells.CountY, Enumerable.Range( 0, 128 * 128 ).Count( index =>
			Owned( imported, index % 128, index / 128 ) == ((save.Cells[index % 128, index / 128].MapFlags & 0x40) == 0) ), "the import keeps the saved flags" );
		Assert.AreEqual( 143, Enumerable.Range( 0, 128 * 128 ).Count( index => save.Cells[index % 128, index / 128].StructureType == SaveCell.HoardingType ),
			"every saved hoarding is on the ring" );

		// the same fence laid on a fresh map of the level owns the same cells
		var fresh = ParkCellMap.FromOriginal( park.Map, null );
		imported.Land.ApplyToNewPark( fresh );
		var equal = Enumerable.Range( 0, 128 * 128 ).Count( index => Owned( fresh, index % 128, index / 128 ) == Owned( imported, index % 128, index / 128 ) );
		Assert.AreEqual( 128 * 128, equal, "cells whose ownership the fill model reproduces" );

		// the world init's typing (0x536490: the cells without terrain unowned) under the same fence owns the same cells, flags and types together
		var typed = ParkCellMap.FromOriginal( park.Map, null, park.Heightfield );
		imported.Land.ApplyToNewPark( typed );
		Assert.AreEqual( 128 * 128, Enumerable.Range( 0, 128 * 128 ).Count( index => Owned( typed, index % 128, index / 128 ) == ((save.Cells[index % 128, index / 128].MapFlags & 0x40) == 0) ),
			"the typed map under the save's fence owns the saved land" );
	}

	/// <summary>The types 7 and 0x1e the Buy Land fence may not run along, from the MAP bits and the heightfield's holes, against the Easymode save's own map types.</summary>
	[TestMethod]
	public void TheBarrierTypesMatchTheSavedMapTypes()
	{
		var park = OriginalPark.Load( "jungle" );
		var save = park.Save!;
		int matched = 0, total = 0;
		for ( var y = 0; y < 128; y++ )
		{
			for ( var x = 0; x < 128; x++ )
			{
				var savedType = save.Cells[x, y].MapType;
				var barrier = ParkLand.IsBarrier( park.Map, park.Heightfield, x, y );
				if ( savedType is not (2 or 7 or 0x1e) && !barrier )
					continue;
				total++;
				matched += barrier == (savedType is 7 or 0x1e) ? 1 : 0;
			}
		}
		Assert.AreEqual( total, matched, $"{matched} of {total} cells of types 2, 7 and 0x1e" );
	}
}
