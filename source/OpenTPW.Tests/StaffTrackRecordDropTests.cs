using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// GUEST-013: the staff drop and place click refuse a cell whose second-layer record is of a track kind (tp.exe 0x46CC40 and 0x46C8E0 through 0x5363F0, 0x536450 and 0x53AD30/60/90),
/// whatever the cell's own type says.
/// </summary>
[TestClass]
public class StaffTrackRecordDropTests
{
	private static ParkCellMap Cells( out TrackCellLayer tracks )
	{
		var cells = new ParkCellMap( 16, 16 );
		tracks = cells.Tracks;
		return cells;
	}

	[TestMethod]
	public void APathCellOverATrackTileIsRefusedByItsRecordThoughItsOwnTypeIsADropType()
	{
		var cells = Cells( out var tracks );
		// a straight kart tile whose origin is (4, 4), with a path over its first cell: the cell's own type (1) is a drop type, its record (11) is not
		cells.SetType( 4, 4, ParkCellType.Path );
		tracks.Set( 4, 4, new TrackCell { Type = TrackCellTypes.KartOrigin, Parent = -1 } );
		Assert.AreEqual( (byte)ParkCellType.Path, cells.RawTypeAt( 4, 4 ) );
		Assert.IsTrue( StaffPlacementRules.IsDropCellType( cells.RawTypeAt( 4, 4 ) ) );
		Assert.IsFalse( StaffPlacementRules.IsValidDropCell( cells, 4, 4, null ), "kind 11 record" );
		Assert.IsTrue( StaffPlacementRules.IsValidDropCell( cells, 8, 8, null ), "control: open ground" );
	}

	[TestMethod]
	public void EveryTrackKindIsRefusedDirectlyAndAnExtensionAnswersThroughItsParent()
	{
		foreach ( var kind in new byte[] { 11, 13, 16, 18, 25 } )
		{
			var cells = Cells( out var tracks );
			tracks.Set( 4, 4, new TrackCell { Type = kind, Parent = -1 } );
			tracks.Set( 5, 4, new TrackCell { Type = TrackCellTypes.Extension, Parent = tracks.IndexOf( 4, 4 ) } );
			cells.SetType( 5, 4, ParkCellType.Queue );
			Assert.IsFalse( StaffPlacementRules.IsValidDropCell( cells, 4, 4, null ), $"kind {kind}" );
			Assert.IsFalse( StaffPlacementRules.IsValidDropCell( cells, 5, 4, null ), $"extension of kind {kind}" );
		}
	}

	[TestMethod]
	public void AnExtensionWithoutAParentOrWithAnUntypedParentIsNoTrack()
	{
		var cells = Cells( out var tracks );
		// parent word 0 (here -1) is no track (0x536400..0x53640A); a parent record of no track kind (here an extension) is none either
		tracks.Set( 4, 4, new TrackCell { Type = TrackCellTypes.Extension, Parent = -1 } );
		tracks.Set( 6, 4, new TrackCell { Type = TrackCellTypes.Extension, Parent = tracks.IndexOf( 4, 4 ) } );
		Assert.IsTrue( StaffPlacementRules.IsValidDropCell( cells, 4, 4, null ) );
		Assert.IsTrue( StaffPlacementRules.IsValidDropCell( cells, 6, 4, null ) );
	}

	[TestMethod]
	public void ARemovedTileGivesItsCellsBack()
	{
		var cells = Cells( out var tracks );
		cells.SetType( 4, 4, ParkCellType.Path );
		tracks.Set( 4, 4, new TrackCell { Type = TrackCellTypes.WaterOrigin, Parent = -1 } );
		Assert.IsFalse( StaffPlacementRules.IsValidDropCell( cells, 4, 4, null ) );
		tracks.Clear( 4, 4 );
		Assert.IsTrue( StaffPlacementRules.IsValidDropCell( cells, 4, 4, null ) );
	}

	[TestMethod]
	public void ACellOnAFencePostOrBesideItIsRefusedAsAHoardingRecord()
	{
		// a fence post is a record of kind 0x19, and the three cells beside it are its extensions (0x53AD30, 0x53AD60): the drop and place refuse them though their own type is a path
		var cells = new ParkCellMap( 8, 8 );
		cells.SetType( 5, 3, ParkCellType.Path );
		cells.SetType( 7, 3, ParkCellType.Path );
		cells.SetLand( new ParkLand( (2, 2), (0, 2), new (int X, int Y)[] { (2, 2), (4, 2), (4, 4), (2, 4), (0, 4), (0, 2) } ) );
		Assert.AreEqual( FenceLayer.PostType, QueueCellValidator.StructureTypeAt( cells, 5, 3 ) );
		Assert.IsFalse( StaffPlacementRules.IsValidDropCell( cells, 5, 3, null ) );
		Assert.IsTrue( StaffPlacementRules.IsValidDropCell( cells, 7, 3, null ), "control: a path cell away from the fence" );
	}
}
