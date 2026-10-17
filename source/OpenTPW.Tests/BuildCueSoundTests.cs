using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The size-classed cat_ui cues of a placement (tp.exe 0x528a70) and of a demolition (0x527ee0).</summary>
[TestClass]
public class BuildCueSoundTests
{
	[DataTestMethod]
	[DataRow( 1, 142u )]
	[DataRow( 3, 142u )]
	[DataRow( 4, 141u )]
	[DataRow( 7, 141u )]
	[DataRow( 8, 143u )]
	[DataRow( 15, 143u )]
	[DataRow( 16, 144u )]
	[DataRow( 90, 144u )]
	public void APlacementCueFollowsTheCellCountInTheOriginalsOrder( int cells, uint expected ) => Assert.AreEqual( expected, Level.PlacementCueEvent( cells ) );

	[DataTestMethod]
	[DataRow( 0, 150u )]
	[DataRow( 3, 150u )]
	[DataRow( 4, 151u )]
	[DataRow( 7, 151u )]
	[DataRow( 8, 152u )]
	[DataRow( 15, 152u )]
	[DataRow( 16, 153u )]
	public void ADemolitionCueFollowsTheCellCount( int cells, uint expected ) => Assert.AreEqual( expected, Level.DemolitionCueEvent( cells ) );

	[TestMethod]
	public void EveryCueExistsInTheUiCatalogue()
	{
		// the shipped cat_ui holds 141-144 and 150-153 (data, checked by the corpus tests); the classes never leave that set
		var all = Enumerable.Range( 0, 40 ).SelectMany( cells => new[] { Level.PlacementCueEvent( cells ), Level.DemolitionCueEvent( cells ) } ).Distinct().OrderBy( id => id ).ToArray();
		CollectionAssert.AreEqual( new[] { 141u, 142u, 143u, 144u, 150u, 151u, 152u, 153u }, all );
	}

	[TestMethod]
	public void ThePlacementCueIsAtTheAnchorCellTimesTen()
	{
		var shape = new ObjectShape( new[] { "***", "***" } );
		// the click less half the shape (3 / 2 = 1 across, 2 / 2 = 1 down), times 10 sound units, height 0
		Assert.AreEqual( new Vector3( 90, 0, 190 ), Level.PlacementCuePlace( shape, 10, 20 ) );
	}

	[TestMethod]
	public void TheDemolitionCueIsAtTheShapeSizeTimesTenNotAtTheObject()
	{
		// the original reads its loop counters, so the cue is at (width x 10, 0, height x 10)
		Assert.AreEqual( new Vector3( 30, 0, 20 ), Level.DemolitionCuePlace( new ObjectShape( new[] { "***", "***" } ) ) );
	}

	[TestMethod]
	public void APlacementCountsTheTypeFourShapeCellsOnTheMap()
	{
		var shape = new ObjectShape( new[] { "***", "*@*" } );
		var cells = new ParkCellMap( 8, 8 );
		Assert.AreEqual( 5, Level.PlacementCueCells( cells, shape, 2, 2, 0 ), "the '@' path cell is type 1, not 4" );
		Assert.AreEqual( 5, Level.PlacementCueCells( null, shape, 2, 2, 90 ), "without a map every cell counts" );
		Assert.AreEqual( 3, Level.PlacementCueCells( cells, shape, 6, 6, 0 ), "only the cells on the map are counted (4d8300)" );
	}

	[TestMethod]
	public void ADemolitionCountsTheFootprintCellsWhoseMapTypeIsFour()
	{
		var shape = new ObjectShape( new[] { "***", "***" } );
		var cells = new ParkCellMap( 8, 8 );
		var footprint = ObjectFootprint.GetCells( shape, 1, 1, 0 ).ToArray();
		foreach ( var (x, y, _) in footprint )
			cells.SetType( x, y, ParkCellTypes.Object );
		Assert.AreEqual( 6, Level.DemolitionCueCells( cells, footprint ) );
		cells.SetType( 1, 1, ParkCellType.Path );
		Assert.AreEqual( 5, Level.DemolitionCueCells( cells, footprint ), "a cell another tool took over no longer counts" );
	}
}
