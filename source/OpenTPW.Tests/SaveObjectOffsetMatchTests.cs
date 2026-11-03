using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>
/// An original save's RideSystem record names the placement anchor while the attraction thing's own cell (+5 and +7) is the anchor plus the type's rotated map offset, so the two are matched on the
/// thing's mTopLeft (tp.exe 0x4DB2DA); the two shipped objects with an offset, jungle/5x5rck and jungle/mammtunn ((0, 1)), were left without their thing.
/// </summary>
[TestClass]
public class SaveObjectOffsetMatchTests
{
	private static SaveAttraction Attraction( int id, int infoId, ushort x, ushort y, ushort topLeft ) => new SaveAttraction(
		0, id, new SaveThingBase( (ushort)(x * 256 + 128), (ushort)(y * 256 + 128), 0, 0 ), infoId, 0, default, 0, "", "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 )
	{ TopLeft = topLeft };

	private static SaveObject Record( int infoId, int x, int y ) => new( 0, infoId, x, y, 4, 4, 0, 0, 0 );

	[TestMethod]
	public void AnObjectWithAMapOffsetOfOneRowIsMatchedOnItsAnchorNotOnItsThingCell()
	{
		var record = Record( 1234, 40, 50 );
		var anchor = OriginalParkImport.AnchorWord( record );
		Assert.AreEqual( (50 << 7) + 40 + 1, anchor );
		var offset = Attraction( 7, 1234, 40, 51, anchor );
		var plain = Attraction( 8, 1234, 60, 61, OriginalParkImport.AnchorWord( Record( 1234, 60, 61 ) ) );
		var found = OriginalParkImport.FindAttraction( new List<SaveAttraction> { plain, offset }, record );
		Assert.AreEqual( 7, found?.Id, "the thing stands one row below its anchor (offset (0, 1)); the old match on its cell found none" );
		Assert.AreEqual( 8, OriginalParkImport.FindAttraction( new List<SaveAttraction> { plain, offset }, Record( 1234, 60, 61 ) )?.Id );
	}

	[TestMethod]
	public void TwoThingsOnTheSameAnchorOrNoneGiveNoMatchAndAnotherInfoIdNeverMatches()
	{
		var record = Record( 1234, 40, 50 );
		var anchor = OriginalParkImport.AnchorWord( record );
		Assert.IsNull( OriginalParkImport.FindAttraction( new[] { Attraction( 1, 1234, 40, 51, anchor ), Attraction( 2, 1234, 40, 51, anchor ) }, record ) );
		Assert.IsNull( OriginalParkImport.FindAttraction( new[] { Attraction( 1, 1235, 40, 51, anchor ) }, record ) );
		Assert.IsNull( OriginalParkImport.FindAttraction( System.Array.Empty<SaveAttraction>(), record ) );
	}
}

/// <summary>The owner word the placement writes on an object's cells is the cell word of its thing's own cell (tp.exe 0x528F13..0x528F26), not of the anchor.</summary>
[TestClass, DoNotParallelize]
public class OwnerWordOffsetTests
{
	private RideUpgradeIngressTests fixture = null!;

	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }
	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();

	[TestMethod]
	public void TheOwnerWordFollowsTheThingCellOfAnObjectWithAMapOffset()
	{
		var hud = fixture.Fixture( BuildCategory.Rides, out var level, out _, out var first, out var second, out _, extra: "Info.MapOffsetX 0\nInfo.MapOffsetY 1\n" );
		var placement = first.Placement;
		Assert.AreEqual( ParkCellMap.CellWord( placement.X, placement.Y + 1 ), Level.OwnerWord( first ) );
		Assert.AreNotEqual( ParkCellMap.CellWord( placement.X, placement.Y ), Level.OwnerWord( first ) );
		Assert.AreEqual( first.ThingCell.Y, placement.Y + 1 );
		Assert.IsNotNull( second );
		Assert.IsNotNull( hud );
		// the guest registration hands the thing cell on as the position the guests find the object at (tp.exe 0x4DB33C, +5/+7)
		typeof( ObjectCatalogEntry ).GetField( "<Shape>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( first.Entry, new ObjectShape( new[] { "*2*", "***" } ) );
		var cells = level.Guests!.Grid;
		for ( var y = 0; y < 32; y++ )
			for ( var x = 0; x < 32; x++ )
				cells.SetPath( x, y, true );
		typeof( Level ).GetMethod( "RegisterObjectWithGuests", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.Invoke( level, new object[] { first } );
		if ( first.Visitors.PositionCell is { } position )
			Assert.AreEqual( first.ThingCell, position );
		else
			Assert.Inconclusive( "The fixture's object has no entrance, so it is not registered with the guests." );
	}

	[TestMethod]
	public void AnOldSaveWhoseFootprintHoldsTheAnchorWordIsRewrittenWithTheThingCellsWordOnLoad()
	{
		fixture.Fixture( BuildCategory.Rides, out var level, out _, out var first, out _, out _, extra: "Info.MapOffsetX 0\nInfo.MapOffsetY 1\n" );
		var cells = level.Guests!.Grid.Cells;
		var anchor = ParkCellMap.CellWord( first.Placement.X, first.Placement.Y );
		var thing = Level.OwnerWord( first );
		Assert.AreNotEqual( anchor, thing );
		var footprint = first.Cells.ToList();
		Assert.IsTrue( footprint.Count > 0 );
		// an OpenTPW save made before the owner word followed the thing cell: every footprint cell names the anchor
		foreach ( var cell in footprint )
			cells.SetOwner( cell.X, cell.Y, anchor );
		cells.WriteFootprint( footprint, thing, first.Placement.Rotation );
		foreach ( var cell in footprint )
			Assert.AreEqual( thing, cells.OwnerAt( cell.X, cell.Y ), $"({cell.X}, {cell.Y})" );
	}
}

/// <summary>The imports read an offset object's thing through <c>mTopLeft</c>: the attraction stands one row below its record's anchor.</summary>
[TestClass, DoNotParallelize]
public class OffsetAttractionImportTests
{
	private static OriginalParkImport OffsetPark() => OriginalSaveImportTests.Park( OriginalSaveImportTests.Settings(), bellyBounce: SaveTestPayloads.Attraction(
		new SaveTestPayloads.AttractionFields { PositionX = 1 * 256 + 128, PositionY = 2 * 256 + 128, TopLeft = (1 << 7) + 1 + 1, Angle = 0, Price = 30, Level = 0, Repair = 80, LifeGauge = 90, Next = 11 } ) );

	[TestMethod]
	public void TheRecordFindsItsThingAndTheEconomyKeepsItsStateThoughTheThingStandsOffTheAnchor()
	{
		var save = OffsetPark();
		var record = save.PlacedObjects.Single().Record;
		var thing = save.FindAttraction( record );
		Assert.IsNotNull( thing, "the thing's own cell is (1, 2), its mTopLeft the record's anchor (1, 1)" );
		Assert.AreEqual( 2, thing!.Value.CellY );
		Assert.IsNull( save.ChainOrderProblem, "the chain names every record's thing once" );
		var economy = new ParkEconomy( OriginalSaveImportTests.Settings(), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var import = OriginalEconomyImport.Apply( economy, save );
		Assert.IsTrue( economy.Objects.Any( item => item.InfoId == record.InfoId && item.Repair == 80 ), "the object keeps its economy state" );
		Assert.IsNotNull( import );
	}

	[TestMethod]
	public void TheThingIsListedAtItsOwnCellNotAtTheAnchor()
	{
		var save = OffsetPark();
		var record = save.PlacedObjects.Single().Record;
		Assert.AreEqual( 1, save.AttractionsAt( record.X, record.Y + 1 ).Count, "the thing is listed at its own cell" );
		Assert.AreEqual( 0, save.AttractionsAt( record.X, record.Y ).Count );
	}

}
