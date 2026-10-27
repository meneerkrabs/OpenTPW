using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The path and queue cell validator against tp.exe's own routine 0x535670 (docs/reverse/ORACLE.md, X36; QUEUE-012): every row of <see cref="QueueCellValidatorGolden"/> is a combination of cell type,
/// flags, structure record, links, owner, line state and latches the executable answered in the x86 oracle.
/// </summary>
[TestClass]
public class QueueCellValidatorTests
{
	[TestMethod]
	public void TheValidatorAnswersAndSetsTheLatchesAsTheExecutableDoes()
	{
		Assert.IsTrue( QueueCellValidatorGolden.Rows.Length > 500 );
		foreach ( var row in QueueCellValidatorGolden.Rows )
		{
			var parts = row.Split( ' ' );
			int V( int index ) => int.Parse( parts[index] );
			var cell = new CellValidatorInput( V( 0 ), (byte)V( 1 ), (ushort)V( 2 ), (byte)V( 3 ), (byte)V( 4 ), (byte)V( 5 ), V( 6 ) != 0, V( 7 ) != 0, V( 8 ) != 0, (byte)V( 11 ), V( 12 ), V( 0 ) );
			var latches = new CellValidatorLatches { Refuse = V( 9 ) != 0, Pending = V( 10 ) != 0 };
			var answer = QueueCellValidator.Check( cell, ref latches );
			var expected = (V( 14 ), V( 15 ) != 0, V( 16 ) != 0, V( 17 ) != 0, V( 18 ) != 0);
			Assert.AreEqual( expected, (answer, latches.Refuse, latches.Pending, latches.EndsOnPath, latches.MeetsOwn), row );
		}
	}

	/// <summary>A queue cell on a fence post or on one of the three cells beside it is refused whoever owns the land (the structure record is a post, or an extension of one).</summary>
	[TestMethod]
	public void AQueueCellOnAHoardingPostOrItsExtensionIsRefusedOnOwnedLandToo()
	{
		var cells = new ParkCellMap( 128, 128 );
		var land = ParkLand.NewPark( cells, new ParkLandSetup( (51, 17), (43, 17), new (int X, int Y)[] { (0, 0), (4, 0), (4, 10), (-12, 10), (-12, 0), (-8, 0) } ) );
		Assert.IsNotNull( land );
		cells.SetLand( land );
		var posts = 0;
		for ( var y = 0; y < 128; y++ )
		{
			for ( var x = 0; x < 128; x++ )
			{
				if ( !land!.CountsAsStructure( x, y, 128, 128 ) )
					continue;
				var cell = new CellValidatorInput( 3, 0, (ushort)cells.FlagsAt( x, y ), QueueCellValidator.StructureTypeAt( cells, x, y ), 0, 0, false, false, false, 0, 0, 3 );
				var latches = new CellValidatorLatches();
				if ( !cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ) )
				{
					Assert.AreEqual( 1, QueueCellValidator.Check( cell, ref latches ), $"({x}, {y})" );
					posts++;
				}
			}
		}
		Assert.IsTrue( posts > 20, "the fence has owned post and extension cells" );
	}
}
