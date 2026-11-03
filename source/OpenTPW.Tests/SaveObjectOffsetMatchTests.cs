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
		fixture.Fixture( BuildCategory.Rides, out _, out _, out var first, out var second, out _, extra: "Info.MapOffsetX 0\nInfo.MapOffsetY 1\n" );
		var placement = first.Placement;
		Assert.AreEqual( ParkCellMap.CellWord( placement.X, placement.Y + 1 ), Level.OwnerWord( first ) );
		Assert.AreNotEqual( ParkCellMap.CellWord( placement.X, placement.Y ), Level.OwnerWord( first ) );
		Assert.AreEqual( first.ThingCell.Y, placement.Y + 1 );
		Assert.IsNotNull( second );
	}
}
