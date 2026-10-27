using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Asset-free contracts of the park land beside the oracle cases (<see cref="ParkFenceGoldenTests"/>): bounded walks and the refusal of a purchase on which the original's fence routines fault.</summary>
[TestClass]
public class ParkLandEdgeCaseTests
{
	private static readonly (int X, int Y) PostA = (21, 11), PostB = (13, 11);

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
			// the layer is read once per cell, then the walk runs on it: it stops at the guard, whatever the links say
			Assert.IsTrue( ++calls <= 128 * 128, "the layer is read once per cell" );
			return links.TryGetValue( (x, y), out var bits ) ? bits : (byte?)null;
		} );
		Assert.AreEqual( 128 * 128, calls );
		Assert.IsNull( land );
	}

	/// <summary>
	/// A rectangle over the gate's open side is accepted by the original's validator and the first purchase runs; the second one, over the first and the gate, leaves the old park outside the fence the walk
	/// from post A finds, so the fill scans to the edge of the map and tp.exe crashes there (oracle case "buy over the open side of the gate (rectangle)"). OpenTPW refuses it: nothing is charged or written.
	/// </summary>
	[TestMethod]
	public void APurchaseTheOriginalCrashesOnIsRefusedWithoutChargingOrMutating()
	{
		var cells = new ParkCellMap( 128, 128 );
		var land = ParkLand.NewPark( cells, new ParkLandSetup( PostA, PostB, new[] { (0, 0), (4, 0), (4, 10), (-12, 10), (-12, 0), (-8, 0) } ) )!;
		cells.SetLand( land );
		var economy = new ParkEconomy(
			new BalanceSettings( new SamSettings( new[] { SamDocument.Parse( EconomyTestData.StandardText( 50000 ), "Standard.sam" ) } ),
				null, null, "jungle", false ), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		Assert.IsTrue( new BuyLandCommand( 15, 7, 19, 15 ).Apply( cells, economy, ( _, _ ) => false ).Accepted, "the first rectangle in front of the gate is bought" );
		var before = Enumerable.Range( 0, 128 * 128 ).Select( i => cells.FlagsAt( i % 128, i / 128 ) ).ToArray();
		var ring = land.Ring.ToArray();
		var balance = economy.Balance;
		var version = cells.Version;
		var result = new BuyLandCommand( 11, 7, 23, 15 ).Apply( cells, economy, ( _, _ ) => false );
		Assert.IsFalse( result.Accepted );
		Assert.AreEqual( LandPurchaseRefusal.Fence, ((LandPurchaseCheck)result.Value!).Refusal );
		Assert.AreEqual( balance, economy.Balance );
		Assert.AreEqual( version, cells.Version );
		Assert.AreSame( land, cells.Land );
		CollectionAssert.AreEqual( ring, land.Ring.ToArray() );
		CollectionAssert.AreEqual( before, Enumerable.Range( 0, 128 * 128 ).Select( i => cells.FlagsAt( i % 128, i / 128 ) ).ToArray() );
	}
}
