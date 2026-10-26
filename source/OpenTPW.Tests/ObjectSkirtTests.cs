using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// The skirt (procedural base) the type loader reads from an object model's floor (tp.exe <c>0x0046A5A0</c> → <c>0x0046AA10</c>, mask <c>0x0052C420</c>, coordinate pass <c>0x00467310</c>;
/// docs/OBJECTS.md, "Floors"): the edge mask, the entries, the opaque and clamped slots.
/// </summary>
[TestClass]
public class ObjectSkirtTests
{
	private static ObjectMapGrid Shape( params string[] rows ) =>
		ObjectMapGrid.Parse( rows, symbol => ObjectShapeSymbols.TryGet( symbol, out var cell ) ? (cell.CellType, cell.Direction) : null )!;

	private static byte[] EdgeMask( ObjectMapGrid shape ) => SkirtReading.Mask( new SkirtShape( shape.Width, shape.Height, shape.RawTypes ) );

	private static string Mask( byte[] mask, int columns, int rows ) =>
		string.Join( "/", Enumerable.Range( 0, rows ).Select( row => string.Concat( Enumerable.Range( 0, columns ).Select( column => (char)('0' + SkirtReading.MaskValue( mask, column * 16 + row )) ) ) ) );

	[TestMethod]
	public void TheMaskMarksEveryFloorCellThatLacksAFloorNeighbourOnAnySide()
	{
		// a 3 x 3 floor: only the middle cell has all four neighbours of a floor type (4, 9, 10, 0x17)
		Assert.AreEqual( "111/101/111", Mask( EdgeMask( Shape( "***", "***", "***" ) ), 3, 3 ) );
		// entrances (9), exits (10) and track ends (0x17) are floor neighbours too; '+' (upgrade, 11), '#' (16) and '@' (1) are not
		Assert.AreEqual( "111/101/111", Mask( EdgeMask( Shape( "*N*", "8*6", "*S*" ) ), 3, 3 ) );
		Assert.AreEqual( "111/101/111", Mask( EdgeMask( Shape( "***", "*#*", "***" ) ), 3, 3 ), "the '#' cell is no floor neighbour and no floor cell itself: the ring around it is all edge, it has no mask" );
	}

	[TestMethod]
	public void TheMaskIsIndexedByColumnTimesSixteenPlusRowAndReadsPastTheLastColumnAndRow()
	{
		// the mask byte of column c, row r sits at c × 16 + r; the shape's own width and height words follow the 256 bytes
		var mask = EdgeMask( Shape( "**", "**" ) );
		Assert.AreEqual( 1, mask[1 * 16 + 1] );
		Assert.AreEqual( 0, SkirtReading.MaskValue( mask, 3 * 16 + 3 ) );
		Assert.AreEqual( 2, SkirtReading.MaskValue( mask, 256 ), "index 256 reads the width word's first byte" );
		Assert.AreEqual( 2, SkirtReading.MaskValue( mask, 260 ), "index 260 reads the height word's first byte" );
		Assert.AreEqual( 0, SkirtReading.MaskValue( mask, 257 ), "the width word's second byte" );
		Assert.AreEqual( 0, SkirtReading.MaskValue( mask, -16 ) );
		Assert.AreEqual( SkirtReading.MaskSpan, mask.Length );
	}

	[TestMethod]
	public void AColumnPastTheStrideReadsTheNextRowsFirstCell()
	{
		// a 20-wide floor row followed by a floor row: the "column + 1 <= width" read at column 19 is column 20 = the next row's first cell, so row 0's last cell sees a neighbour there
		var wide = Shape( new string( '*', 20 ), new string( '*', 20 ) );
		Assert.AreEqual( 20, wide.Width );
		// for the second text row (row 0 after the swap) the right neighbour of column 19 is row 0's own column 20 = row 1's first cell (a floor cell)
		Assert.AreEqual( 4, wide.TypeAtFlat( 20, 0 ) );
		Assert.AreEqual( 4, wide.TypeAtFlat( 0, 1 ) );
		Assert.AreEqual( 0, wide.TypeAtFlat( 20, 1 ), "past the last stored cell of a two-row shape: an unwritten cell" );
		Assert.AreEqual( 0, wide.TypeAtFlat( 0, -1 ) );
	}

	[TestMethod]
	public void ANonFloorTypeBreaksTheRunOnItsSides()
	{
		var mask = EdgeMask( Shape( "*****", "*****", "**.**", "*****", "*****" ) );
		Assert.AreEqual( "11111/10101/11011/10101/11111", Mask( mask, 5, 5 ) );
	}

	[TestMethod]
	public void TheDefaultEntryIsTheOriginalsAndAnEntryNeedsBit80ToHaveASkirt()
	{
		Assert.AreEqual( new ObjectSkirtEntry( 1, 10000 ), ObjectSkirtEntry.None );
		Assert.IsFalse( ObjectSkirtEntry.None.HasSkirt );
		var entry = new ObjectSkirtEntry( 0xE8, 3 );
		Assert.IsTrue( entry.HasSkirt );
		Assert.IsTrue( entry.Flipped );
		Assert.AreEqual( 0x28, entry.Orientation );
	}
}

/// <summary>The shipped skirts; inconclusive without OPENTPW_GAME_PATH.</summary>
[TestClass]
[DoNotParallelize]
public class ObjectSkirtCorpusTests
{
	private static readonly string[] Themes = { "jungle", "fantasy", "hallow", "space" };
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the skirt corpus." );
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

	internal static ModelFile ModelOf( ObjectCatalogEntry entry )
	{
		using var stream = entry.FileSystem.OpenRead( entry.ModelPath );
		return new ModelFile( stream );
	}

	internal static ModelHeightfield FieldOf( string theme ) => new ModelFile( $"/levels/{theme}/terrain/base.MD2" ).Heightfield!;

	internal static IEnumerable<ObjectCatalogEntry> NonFixedTypes( string theme ) => ObjectCatalog.Load( theme ).Entries.Where( entry => !entry.IsBonus && !entry.IsFixedItem );

	[TestMethod]
	public void TheShippedTypesGetTheSkirtCellsThePlanCounted()
	{
		// per theme: non-fixed types, types with skirt cells, skirt cells and the cells whose floor art is flipped (the probe of the 0x46AA10 reading: 60/57/61/59 types, 394/370/437/437 cells, 88/97/106/129 flipped)
		var expected = new Dictionary<string, (int Types, int WithCells, int Cells, int Flipped)>
		{
			["jungle"] = (64, 60, 394, 88), ["fantasy"] = (60, 57, 370, 97), ["hallow"] = (64, 61, 437, 106), ["space"] = (62, 59, 437, 129)
		};
		foreach ( var theme in Themes )
		{
			var terrain = HoardingTerrain.Of( FieldOf( theme ) );
			int types = 0, withCells = 0, cells = 0, flipped = 0;
			foreach ( var entry in NonFixedTypes( theme ) )
			{
				var skirt = ObjectSkirt.Build( entry.Settings, ModelOf( entry ), terrain )!;
				types++;
				var count = skirt.Cells.Count();
				cells += count;
				flipped += skirt.FlippedCells;
				Assert.AreEqual( skirt.Cells.Count( cell => cell.Entry.Flipped ), skirt.FlippedCells, $"{theme}/{entry.ArchiveName}" );
				if ( count > 0 )
					withCells++;
				Assert.AreEqual( 0, skirt.MissingFaceCells, $"{theme}/{entry.ArchiveName}: every skirt cell finds a floor face (the original's error never fires)" );
				var slots = skirt.Cells.Select( cell => cell.Entry.Slot ).ToList();
				CollectionAssert.AreEqual( slots.Distinct().ToList(), slots, $"{theme}/{entry.ArchiveName}: no two skirt cells share a texture slot" );
				foreach ( var cell in skirt.Cells )
					Assert.IsTrue( skirt.OpaqueSlots[cell.Entry.Slot], $"{theme}/{entry.ArchiveName}: the slot of a skirt cell is opaque" );
			}
			Assert.AreEqual( expected[theme], (types, withCells, cells, flipped), theme );
		}
	}

	[TestMethod]
	public void TheSkirtSlotsAreMostlyTranslucentGrassFringeArt()
	{
		// the alpha type of the texture of every skirt cell's slot (translucent / cut-out / opaque), the reason the floors left the sorted pass: 361/27/6, 314/44/12, 363/49/25 and 347/24/66 (the plan's probe, which also used float arithmetic per operation, the original's 24-bit x87 mode)
		var expected = new Dictionary<string, (int Translucent, int Cutout, int Opaque)>
		{
			["jungle"] = (361, 27, 6), ["fantasy"] = (314, 44, 12), ["hallow"] = (363, 49, 25), ["space"] = (347, 24, 66)
		};
		foreach ( var theme in Themes )
		{
			var terrain = HoardingTerrain.Of( FieldOf( theme ) );
			int translucent = 0, cutout = 0, opaque = 0;
			foreach ( var entry in NonFixedTypes( theme ) )
			{
				var model = ModelOf( entry );
				foreach ( var cell in ObjectSkirt.Build( entry.Settings, model, terrain )!.Cells )
				{
					var resolved = ObjectAssets.ResolveTexture( entry, Path.GetFileNameWithoutExtension( model.Textures[cell.Entry.Slot].FrameNames[0] ) );
					Assert.IsNotNull( resolved, $"{theme}/{entry.ArchiveName}: slot {cell.Entry.Slot}" );
					using var stream = resolved.Value.FileSystem.OpenRead( resolved.Value.Path );
					switch ( TextureFile.ReadAlphaType( stream ) )
					{
						case TextureAlphaType.Translucent: translucent++; break;
						case TextureAlphaType.Cutout: cutout++; break;
						default: opaque++; break;
					}
				}
			}
			Assert.AreEqual( expected[theme], (translucent, cutout, opaque), theme );
		}
	}

	[TestMethod]
	public void BellyBouncesTenEntriesAreThePinnedReading()
	{
		var entry = NonFixedTypes( "jungle" ).Single( candidate => candidate.ArchiveName == "bouncy" );
		var skirt = ObjectSkirt.Build( entry.Settings, ModelOf( entry ), HoardingTerrain.Of( FieldOf( "jungle" ) ) )!;
		Assert.IsTrue( skirt.HasBase );
		Assert.AreEqual( (3, 4), (skirt.Width, skirt.Height) );
		CollectionAssert.AreEqual( new[] { 0xC8, 0xE0, 0x90, 0xA0, 0x88, 0xA0, 0x88, 0x80, 0x90, 0xE0 }, skirt.Cells.Select( cell => cell.Entry.Code ).ToArray() );
		CollectionAssert.AreEqual( new[] { 11, 10, 9, 8, 6, 5, 3, 2, 1, 0 }, skirt.Cells.Select( cell => cell.Entry.Slot ).ToArray() );
		Assert.AreEqual( 2, skirt.Entries.Count( candidate => !candidate.HasSkirt ), "the two interior cells keep the default entry" );
	}

	[TestMethod]
	public void LavajumpHasTheBaseBitButNoSkirtCellAndTheMysteryBoxIsNotExported()
	{
		var terrain = HoardingTerrain.Of( FieldOf( "jungle" ) );
		var lavajump = NonFixedTypes( "jungle" ).Single( candidate => candidate.ArchiveName == "lavajump" );
		var skirt = ObjectSkirt.Build( lavajump.Settings, ModelOf( lavajump ), terrain )!;
		Assert.IsTrue( skirt.HasBase );
		Assert.AreEqual( 0, skirt.Cells.Count() );
		var mystery = NonFixedTypes( "jungle" ).Single( candidate => candidate.ArchiveName == "mystery" );
		var model = ModelOf( mystery );
		Assert.AreEqual( 1u, model.HeaderFlags, "no 'has procedural base' bit" );
		var none = ObjectSkirt.Build( mystery.Settings, model, terrain )!;
		Assert.IsFalse( none.HasBase );
		Assert.AreEqual( 0, none.Cells.Count() );
	}

	[TestMethod]
	public void EveryExportedNonFixedModelHasTheHeaderBitAndAFixedItemHasNoSkirt()
	{
		foreach ( var theme in Themes )
		{
			var terrain = HoardingTerrain.Of( FieldOf( theme ) );
			foreach ( var entry in NonFixedTypes( theme ) )
			{
				var model = ModelOf( entry );
				var skirt = ObjectSkirt.Build( entry.Settings, model, terrain )!;
				if ( entry.ArchiveName == "mystery" )
					continue;
				Assert.AreNotEqual( 0u, model.HeaderFlags & 8, $"{theme}/{entry.ArchiveName}: the exporter's 'has procedural base' option" );
				Assert.AreEqual( ObjectHoardingGeometry.SelectGroundNode( model, ObjectMapGrid.ShapeOf( entry.Settings ), terrain ).Node >= 0, skirt.HasBase, $"{theme}/{entry.ArchiveName}" );
			}
			foreach ( var fixedItem in ObjectCatalog.Load( theme ).Entries.Where( entry => !entry.IsBonus && entry.IsFixedItem ) )
				Assert.IsNull( ObjectSkirt.Build( fixedItem.Settings, ModelOf( fixedItem ), terrain ), $"{theme}/{fixedItem.ArchiveName}: loaded without the base flags" );
		}
	}

	[TestMethod]
	public void TheHeightMapOfEveryTypeHasItsShapesSizeSoTheInstanceGridIsTheShape()
	{
		// the instance constructor walks the grid of the type's +0xCC, the height map record: its width and height are the shape's (0x00451640 regenerates a map of another size), and the
		// skirt buffer is as large as the shape (+0xF0/+0xF4)
		var maps = new ObjectHeightMaps();
		var checkedTypes = 0;
		foreach ( var theme in Themes )
		{
			var terrain = HoardingTerrain.Of( FieldOf( theme ) );
			foreach ( var entry in NonFixedTypes( theme ) )
			{
				var shape = ObjectMapGrid.ShapeOf( entry.Settings )!;
				var map = maps.MapOf( entry );
				Assert.IsNotNull( map, $"{theme}/{entry.ArchiveName}" );
				Assert.AreEqual( (shape.Width, shape.Height), (map.Width, map.Height), $"{theme}/{entry.ArchiveName}" );
				var skirt = ObjectSkirt.Build( entry.Settings, ModelOf( entry ), terrain )!;
				Assert.AreEqual( (shape.Width, shape.Height), (skirt.Width, skirt.Height) );
				checkedTypes++;
			}
		}
		Assert.AreEqual( 64 + 60 + 64 + 62, checkedTypes );
	}
}
