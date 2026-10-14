using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Q1 of the kart and water track trace: the piece tables, the four adjustments and the controller templates (docs/TRACKS.md).</summary>
[TestClass]
public class TrackSelectorTests
{
	private const byte Water = TrackCellTypes.WaterOrigin;
	private const byte Kart = TrackCellTypes.KartOrigin;

	[TestMethod]
	public void EveryOneOfTheSixteenMasksHasItsTableEntry()
	{
		var expected = new (byte Mask, int Piece, int Rotation)[]
		{
			(0x00, 0, 0),
			(0x10, 1, 0), (0x40, 1, 90), (0x01, 1, 180), (0x04, 1, 270),
			(0x11, 2, 0), (0x44, 2, 90),
			(0x14, 3, 0), (0x50, 3, 90), (0x41, 3, 180), (0x05, 3, 270),
			(0x15, 5, 0), (0x45, 6, 0), (0x51, 7, 0), (0x54, 8, 0),
			(0x55, 4, 0)
		};
		Assert.AreEqual( 16, expected.Length );
		foreach ( var (mask, piece, rotation) in expected )
		{
			var chosen = TrackPieceSelector.Select( Water, mask, 0, 0 );
			Assert.AreEqual( (4, piece, rotation), (chosen.Family, chosen.Piece, chosen.Rotation), $"water mask 0x{mask:X2}" );
			var kart = TrackPieceSelector.Select( Kart, mask, 0, 0 );
			Assert.AreEqual( (3, piece, rotation), (kart.Family, kart.Piece, kart.Rotation), $"kart mask 0x{mask:X2}" );
		}
		CollectionAssert.AreEquivalent( Enumerable.Range( 0, 256 ).Where( bits => (bits & ~0x55) == 0 ).Select( bits => (byte)bits ).ToArray(),
			expected.Select( item => item.Mask ).ToArray(), "the 16 masks are every combination of the four arm bits" );
	}

	[TestMethod]
	public void SubsetMatchingFindsTheFirstEntryWhenThereIsNoExactMask()
	{
		// a mask with a stray bit has no exact entry: the first table entry whose mask lies inside it wins (the straight before the stubs)
		Assert.AreEqual( 2, TrackPieceSelector.Lookup( 0x91, out var rotation ), "0x91 contains the straight 0x11" );
		Assert.AreEqual( 0, rotation );
		Assert.AreEqual( 0, TrackPieceSelector.Lookup( 0x80, out _ ), "no arm at all selects the empty piece" );
	}

	[TestMethod]
	public void StraightsFollowTheFlowAndBridgeVariantsAddTheirOffsets()
	{
		// 1. a straight whose flow is −X or +Y turns 180°
		Assert.AreEqual( 0, TrackPieceSelector.Select( Water, 0x11, TrackHeading.NegY, 0 ).Rotation );
		Assert.AreEqual( 180, TrackPieceSelector.Select( Water, 0x11, TrackHeading.PosY, 0 ).Rotation );
		Assert.AreEqual( 90, TrackPieceSelector.Select( Water, 0x44, TrackHeading.PosX, 0 ).Rotation );
		Assert.AreEqual( 270, TrackPieceSelector.Select( Water, 0x44, TrackHeading.NegX, 0 ).Rotation );
		// 2. bridge flags: 1 adds 9, 2 adds 12, 8 adds 15, 4 selects 18 — straights, stubs and the empty piece only
		Assert.AreEqual( 11, TrackPieceSelector.Select( Water, 0x11, 0, 0x1 ).Piece );
		Assert.AreEqual( 14, TrackPieceSelector.Select( Water, 0x11, 0, 0x2 ).Piece );
		Assert.AreEqual( 17, TrackPieceSelector.Select( Water, 0x11, 0, 0x8 ).Piece );
		Assert.AreEqual( 18, TrackPieceSelector.Select( Water, 0x11, 0, 0x4 ).Piece );
		Assert.AreEqual( 10, TrackPieceSelector.Select( Water, 0x10, 0, 0x1 ).Piece, "an end stub becomes the bridge stub" );
		Assert.AreEqual( 3, TrackPieceSelector.Select( Water, 0x14, 0, 0x1 ).Piece, "a corner ignores the bridge flag" );
	}

	[TestMethod]
	public void CornersAndCrossingsPickTheirSecondFamily()
	{
		// 3. the second corner mesh serves the other flow around each bend (types 11 and 16 only)
		foreach ( var (link, mask) in new (byte, byte)[] { (0x40, 0x50), (0x10, 0x14), (0x01, 0x41), (0x04, 0x05) } )
		{
			Assert.AreEqual( 19, TrackPieceSelector.Select( Water, mask, link, 0 ).Piece, $"B2 for flow 0x{link:X2} over mask 0x{mask:X2}" );
			Assert.AreEqual( 19, TrackPieceSelector.Select( Kart, mask, link, 0 ).Piece );
		}
		Assert.AreEqual( 3, TrackPieceSelector.Select( Water, 0x14, 0x04, 0 ).Piece, "the other flow keeps the first corner" );
		// 4. water crossings: link 0x14 turns 90°, 0x41 adds 16, 0x50 turns 270° and adds 16; the kart table has no second family
		Assert.AreEqual( (4, 90), (TrackPieceSelector.Select( Water, 0x55, 0x14, 0 ).Piece, TrackPieceSelector.Select( Water, 0x55, 0x14, 0 ).Rotation) );
		Assert.AreEqual( 20, TrackPieceSelector.Select( Water, 0x55, 0x41, 0 ).Piece );
		var third = TrackPieceSelector.Select( Water, 0x15, 0x50, 0 );
		Assert.AreEqual( (21, 270), (third.Piece, third.Rotation) );
		Assert.AreEqual( 4, TrackPieceSelector.Select( Kart, 0x55, 0x41, 0 ).Piece );
	}

	[TestMethod]
	public void TypesWithoutKitDrawNothing()
	{
		foreach ( byte type in new byte[] { 4, 5, 7, 9, 10, TrackCellTypes.Extension, 17, TrackCellTypes.KartStart, TrackCellTypes.WaterStart } )
			Assert.IsTrue( TrackPieceSelector.Select( type, 0x11, 0, 0 ).IsNone, $"type {type}" );
		Assert.AreEqual( 3, TrackPieceSelector.Select( 3, 0x11, 0, 0 ).Family, "type 3 uses the kart table" );
		Assert.AreEqual( 4, TrackPieceSelector.Select( TrackCellTypes.WaterAlternate, 0x11, 0, 0 ).Family );
	}

	[TestMethod]
	public void PieceTablesHoldTheTracedEntries()
	{
		var water = TrackPieceSelector.PieceTable( TrackFamily.Water );
		var kart = TrackPieceSelector.PieceTable( TrackFamily.Kart );
		Assert.AreEqual( 25, water.Count );
		Assert.AreEqual( 20, kart.Count );
		CollectionAssert.AreEqual( new[] { "", "Q", "S", "B", "X", "X", "X", "X", "X", "H", "H", "H", "H_U", "H_U", "H_U", "H_D", "H_D", "H_D", "H_A", "B2", "X2", "X2", "X2", "X2", "X2" },
			water.Select( entry => entry.Name ).ToArray() );
		CollectionAssert.AreEqual( new byte[] { 0, 0x01, 0x11, 0, 0x55, 0x15, 0x54, 0x51, 0x45, 0, 0x01, 0x11, 0, 0x01, 0x11, 0, 0x01, 0x11, 0, 0, 0x55, 0x15, 0x54, 0x51, 0x45 },
			water.Select( entry => entry.ArmMask ).ToArray() );
		CollectionAssert.AreEqual( water.Take( 20 ).ToArray(), kart.ToArray(), "the kart table is the water table's first 20 entries" );
	}

	[TestMethod]
	public void ControllerTemplatesMatchTheOriginalTable()
	{
		Assert.AreEqual( 14, BumperTemplates.All.Count );
		var water = BumperTemplates.Get( -5 );
		Assert.AreEqual( (-1, 1024, 1281, 4, 9, 970, 1000, 7, 14, 980, 64, 512), (water.Laps, water.VehicleRadius, water.HalfWidth, water.Accel.Min, water.Accel.Max, water.Damping.Min, water.Damping.Max, water.Turn.Min, water.Turn.Max, water.Restitution.Min, water.MaxVehicles, water.BuoyRadius) );
		Assert.AreEqual( 257, water.Slack );
		var kart = BumperTemplates.Get( -4 );
		Assert.AreEqual( (-3, 900, 2048, 16, 30, 960, 950, 18, 34, 900, 8, 2048), (kart.Laps, kart.VehicleRadius, kart.HalfWidth, kart.Accel.Min, kart.Accel.Max, kart.Damping.Min, kart.Damping.Max, kart.Turn.Min, kart.Turn.Max, kart.Restitution.Min, kart.MaxVehicles, kart.BuoyRadius) );
		Assert.AreEqual( 1148, kart.Slack );
		foreach ( var type in new[] { -5, -8, -12, -13 } )
		{
			Assert.IsTrue( BumperTemplates.Get( type ).IsWater );
			Assert.AreEqual( water with { Type = type }, BumperTemplates.Get( type ), "the four water types hold identical values" );
		}
		foreach ( var type in new[] { -4, -7, -9, -10 } )
			Assert.IsTrue( BumperTemplates.Get( type ).IsKart );
		Assert.IsNull( BumperTemplates.TryGet( 0 ) );
		Assert.IsNull( BumperTemplates.TryGet( -15 ) );
		Assert.AreEqual( -5, BumperTemplates.ForRide( 2, -5 )!.Type );
		Assert.IsNull( BumperTemplates.ForRide( 2, -4 ), "a kart type is not a water ride's" );
	}
}
