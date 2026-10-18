using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The coaster tool's placement-area cell test (tp.exe <c>0x5243a6</c> with the predicates <c>0x535db0</c>, <c>0x536310</c>..<c>0x536450</c>, COAST-028).</summary>
[TestClass]
public class CoasterAreaCellTests
{
	private static ParkCellMap Map( byte type, ParkCellFlags flags = ParkCellFlags.None )
	{
		var map = new ParkCellMap( 8, 8 );
		map.SetType( 2, 2, type );
		map.SetFlags( 2, 2, flags );
		return map;
	}

	[TestMethod]
	[DataRow( (byte)0, true, "empty land" )]
	[DataRow( (byte)1, true, "a path without flag 0x20 stays markable" )]
	[DataRow( (byte)2, true, "water is left to the validator" )]
	[DataRow( (byte)3, false, "queue" )]
	[DataRow( (byte)4, false, "object footprint" )]
	[DataRow( (byte)7, false, "outside the park" )]
	[DataRow( (byte)9, false, "entrance" )]
	[DataRow( (byte)10, false, "exit" )]
	[DataRow( (byte)30, false, "the approach road" )]
	[DataRow( (byte)11, false, "kart track tile" )]
	[DataRow( (byte)13, false, "kart station tile" )]
	[DataRow( (byte)16, false, "water track tile" )]
	[DataRow( (byte)18, false, "water station tile" )]
	[DataRow( (byte)0x19, false, "fence post" )]
	public void TheCellTypeDecidesWhetherTheCellIsMarked( byte type, bool marked, string why )
		=> Assert.AreEqual( marked, Level.IsCoasterAreaCell( Map( type ), 2, 2 ), why );

	[TestMethod]
	public void TheCellFlagsRefuseAnUnownedCellAndAPathWithFlag20()
	{
		Assert.IsFalse( Level.IsCoasterAreaCell( Map( 0, ParkCellFlags.Unowned ), 2, 2 ), "0x535db0(0x40)" );
		Assert.IsFalse( Level.IsCoasterAreaCell( Map( 1, ParkCellFlags.NoModify ), 2, 2 ), "type 1 with flag 0x20" );
		Assert.IsTrue( Level.IsCoasterAreaCell( Map( 0, ParkCellFlags.NoModify ), 2, 2 ), "flag 0x20 matters on a path only" );
		Assert.IsFalse( Level.IsCoasterAreaCell( Map( 0 ), 20, 2 ), "outside the map" );
	}

	[TestMethod]
	public void AnExtensionCellStandsForItsHeadTile()
	{
		var map = Map( 12 );
		Assert.IsTrue( Level.IsCoasterAreaCell( map, 2, 2 ), "an extension without an owner word returns 0 (0x536400)" );
		map.SetOwner( 2, 2, 5 );
		Assert.IsFalse( Level.IsCoasterAreaCell( map, 2, 2 ) );
	}

	[TestMethod]
	[DataRow( -90f )]
	[DataRow( -20f )]
	[DataRow( 45f )]
	[DataRow( 200f )]
	public void TheParkCameraYawIsTheOriginalsCameraYawOfTheTiltDrag( float yawDegrees )
	{
		// The camera looks from Target + (cos, sin) * d at the target, so its forward direction is (-cos, -sin); its atan2(x, z) heading is the original's yaw 0x790a38 (0x443371).
		var radians = yawDegrees * MathF.PI / 180f;
		var heading = MathF.Atan2( -MathF.Cos( radians ), -MathF.Sin( radians ) );
		var original = ParkCameraMode.OriginalYaw( yawDegrees );
		var difference = MathF.IEEERemainder( heading - original, 2 * MathF.PI );
		Assert.AreEqual( 0f, difference, 1e-4f );
	}
}
