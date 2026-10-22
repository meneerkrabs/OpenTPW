using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Asset-free host contracts for PATH-007, not an oracle for the original executable's fill.</summary>
[TestClass]
public class ParkLandEdgeCaseTests
{
	private static readonly (int X, int Y) PostA = (21, 11), PostB = (13, 11);

	[DataTestMethod]
	[DataRow( 64, 64 )]
	[DataRow( 18, 18 )]
	public void ConcaveFillOnlyClearsOwnershipAndIsIdempotent( int width, int height )
	{
		// A rectangle with its south-east quarter removed; the concave vertex is (17, 17).
		var land = ParkLand.FromClicks( new ParkLandSetup( PostA, PostB, new[]
		{
			(0, 0), (4, 0), (4, 6), (-4, 6), (-4, 10), (-12, 10), (-12, 0), (-8, 0)
		} ) );
		AssertFill( land, width, height, ( x, y ) =>
			(x >= 10 && x <= 25 && y >= 12 && y <= 17)
			|| (x >= 10 && x <= 17 && y >= 18 && y <= 21)
			|| (x == 18 && y == 18) );
	}

	[TestMethod]
	public void TwoConcaveCornersDoNotFillTheOpenNotch()
	{
		// A U-shaped park: notch x 13..21, y 15..21 remains open to the south.
		var land = ParkLand.FromClicks( new ParkLandSetup( PostA, PostB, new[]
		{
			(0, 0), (4, 0), (4, 10), (0, 10), (0, 4), (-8, 4), (-8, 10), (-12, 10), (-12, 0), (-8, 0)
		} ) );
		AssertFill( land, 64, 64, ( x, y ) =>
			(x >= 10 && x <= 25 && y >= 12 && y <= 15)
			|| ((x >= 10 && x <= 13 || x >= 22 && x <= 25) && y >= 16 && y <= 21)
			|| ((x == 14 || x == 21) && y == 16) );
	}

	private static void AssertFill( ParkLand land, int width, int height, Func<int, int, bool> claimed )
	{
		var cells = new ParkCellMap( width, height );
		const ParkCellFlags kept = ParkCellFlags.NoModify | ParkCellFlags.EntranceArea | ParkCellFlags.MapBit04;
		for ( var y = 0; y < height; y++ )
			for ( var x = 0; x < width; x++ )
				cells.SetFlags( x, y, kept | ParkCellFlags.Unowned );
		cells.SetFlags( 0, 0, kept ); // previously owned outside the ring must stay owned
		cells.SetType( 12, 14, ParkCellType.Queue );
		cells.SetLinks( 12, 14, 4 );
		cells.SetQueueLink( 12, 14, 64 );
		cells.SetOwner( 12, 14, 42 );
		cells.SetPlacementCount( 12, 14, 3 );
		var stamp = cells.StampCounter;
		land.ClaimEnclosedCells( cells );
		for ( var y = 0; y < height; y++ )
			for ( var x = 0; x < width; x++ )
			{
				var expected = kept | (claimed( x, y ) || (x, y) == (0, 0) ? ParkCellFlags.None : ParkCellFlags.Unowned);
				Assert.AreEqual( expected, cells.FlagsAt( x, y ), $"ownership and unrelated flags at ({x}, {y})" );
			}
		Assert.AreEqual( ParkCellType.Queue, cells.TypeAt( 12, 14 ) );
		Assert.AreEqual( (byte)4, cells.LinksAt( 12, 14 ) );
		Assert.AreEqual( (byte)64, cells.QueueLinkAt( 12, 14 ) );
		Assert.AreEqual( (ushort)42, cells.OwnerAt( 12, 14 ) );
		Assert.AreEqual( (short)3, cells.PlacementCountAt( 12, 14 ) );
		Assert.AreEqual( stamp, cells.StampCounter, "ownership does not change cell types" );
		var version = cells.Version;
		land.ClaimEnclosedCells( cells );
		Assert.AreEqual( version, cells.Version, "a second fill performs no writes" );
	}

	[TestMethod]
	public void CyclicImportedLinksTerminateWithoutReachingPostB()
	{
		// A synthetic four-post cycle east of the gate, disconnected from post B.
		var links = new Dictionary<(int, int), byte>
		{
			[(21, 11)] = 4 | 16, [(23, 11)] = 64 | 16,
			[(23, 13)] = 1 | 64, [(21, 13)] = 1 | 4
		};
		var calls = 0;
		var land = ParkLand.FromFenceLinks( PostA, PostB, ( x, y ) =>
		{
			// Fail synchronously if the traversal guard regresses; never hang the test runner.
			Assert.IsTrue( ++calls <= ParkLand.MaximumPosts, "the link walk must remain bounded" );
			return links.TryGetValue( (x, y), out var bits ) ? bits : (byte?)null;
		} );
		Assert.IsTrue( calls > 0 );
		Assert.IsNull( land );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void GateCrossingCommandRefusesWithoutChargingOrMutating( bool reversed )
	{
		var land = ParkLand.FromClicks( new ParkLandSetup( PostA, PostB, new[]
		{
			(0, 0), (4, 0), (4, 10), (-12, 10), (-12, 0), (-8, 0)
		} ) );
		var cells = new ParkCellMap( 64, 64 );
		land.ApplyToNewPark( cells );
		cells.SetLand( land );
		var economy = new ParkEconomy(
			new BalanceSettings( new SamSettings( new[] { SamDocument.Parse( EconomyTestData.StandardText( 50000 ), "Standard.sam" ) } ),
				null, null, "jungle", false ), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		var before = Enumerable.Range( 0, 64 * 64 ).Select( i => cells.FlagsAt( i % 64, i / 64 ) ).ToArray();
		var version = cells.Version;
		var balance = economy.Balance;
		var command = reversed ? new BuyLandCommand( 19, 15, 15, 7 ) : new BuyLandCommand( 15, 7, 19, 15 );
		var result = command.Apply( cells, economy, ( _, _ ) => false );
		Assert.IsFalse( result.Accepted );
		Assert.AreEqual( new LandPurchaseCheck( LandPurchaseRefusal.Gate, 0, 0 ), result.Value );
		Assert.AreEqual( balance, economy.Balance );
		Assert.AreEqual( version, cells.Version );
		Assert.AreSame( land, cells.Land );
		CollectionAssert.AreEqual( before, Enumerable.Range( 0, 64 * 64 ).Select( i => cells.FlagsAt( i % 64, i / 64 ) ).ToArray() );
	}
}
