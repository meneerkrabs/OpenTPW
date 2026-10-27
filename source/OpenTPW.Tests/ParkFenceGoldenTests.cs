using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The park fence against tp.exe's own routines (docs/reverse/ORACLE.md F30): the cases of <see cref="ParkFenceGolden"/> were produced by running the executable's fence set-up 0x5323F0, the Buy Land validator
/// 0x531A50 and the purchase 0x5327E0 in the x86 oracle on synthetic 128 x 128 maps. <see cref="FenceEngine"/> and <see cref="ParkLand"/> must leave every cell flag word and the fence's structure records
/// (type, links, owner word, walk direction) as the executable does, answer the validator the same, and refuse the purchases on which the executable hangs or crashes.
/// </summary>
[TestClass]
public class ParkFenceGoldenTests
{
	private const int Side = 128;

	internal static ParkCellMap MapOf( ParkFenceGolden.Case golden )
	{
		var cells = new ParkCellMap( Side, Side );
		foreach ( var (type, x0, y0, width, height) in golden.Terrain )
			for ( var y = y0; y < Math.Min( Side, y0 + height ); y++ )
				for ( var x = x0; x < Math.Min( Side, x0 + width ); x++ )
					cells.SetType( x, y, (byte)type );
		foreach ( var (x, y) in golden.NoModify )
		{
			cells.SetType( x, y, ParkCellType.Path );
			cells.SetFlags( x, y, ParkCellFlags.NoModify );
		}
		return cells;
	}

	internal static string LayerHash( FenceLayer layer )
	{
		var bytes = new byte[Side * Side * 5];
		for ( var index = 0; index < Side * Side; index++ )
		{
			bytes[index * 5] = layer.Type[index];
			bytes[index * 5 + 1] = layer.Links[index];
			bytes[index * 5 + 2] = layer.Direction[index];
			bytes[index * 5 + 3] = (byte)layer.Owner[index];
			bytes[index * 5 + 4] = (byte)(layer.Owner[index] >> 8);
		}
		return Convert.ToHexString( SHA256.HashData( bytes ) ).ToLowerInvariant();
	}

	internal static int[] FlagRuns( ParkCellMap cells )
	{
		var runs = new List<int>();
		int? previous = null;
		for ( var index = 0; index < Side * Side; index++ )
		{
			var flags = (int)cells.FlagsAt( index % Side, index / Side );
			if ( flags == previous )
				runs[^2]++;
			else
			{
				runs.AddRange( new[] { index, 1, flags } );
				previous = flags;
			}
		}
		return runs.ToArray();
	}

	[TestMethod]
	public void TheFenceAndItsPurchasesLeaveTheCellsTheExecutableLeaves()
	{
		Assert.IsTrue( ParkFenceGolden.Cases.Length >= 10 );
		foreach ( var golden in ParkFenceGolden.Cases )
		{
			var cells = MapOf( golden );
			var land = ParkLand.NewPark( cells, new ParkLandSetup( golden.PostA, golden.PostB, golden.Clicks ) );
			Assert.IsNotNull( land, golden.Name );
			cells.SetLand( land );
			foreach ( var buy in golden.Buys )
			{
				var before = FlagRuns( cells );
				var check = land!.CheckPurchase( cells, buy.First, buy.Second, ( x, y ) => cells.RawTypeAt( x, y ) is 7 or 0x1E, 100, null );
				var where = $"{golden.Name}: buy {buy.First}-{buy.Second}";
				if ( !buy.Accepted )
				{
					Assert.IsFalse( check.Allowed, where );
					Assert.AreNotEqual( LandPurchaseRefusal.Fence, check.Refusal, where );
					continue;
				}
				Assert.AreEqual( buy.Cells, check.Cells, where );
				if ( buy.Faults )
				{
					Assert.AreEqual( LandPurchaseRefusal.Fence, check.Refusal, where );
					Assert.IsFalse( land.Purchase( cells, buy.First, buy.Second ), where );
					CollectionAssert.AreEqual( before, FlagRuns( cells ), $"{where}: a refused purchase writes nothing" );
					continue;
				}
				Assert.IsTrue( check.Allowed, where );
				Assert.IsTrue( land.Purchase( cells, buy.First, buy.Second ), where );
			}
			CollectionAssert.AreEqual( golden.FlagRuns, FlagRuns( cells ), $"{golden.Name}: cell flags" );
			Assert.AreEqual( golden.LayerSha256, LayerHash( land!.LayerFor( Side, Side ) ), $"{golden.Name}: fence records" );
			Assert.AreEqual( golden.OwnedCells, Enumerable.Range( 0, Side * Side ).Count( index => !cells.FlagsAt( index % Side, index / Side ).HasFlag( ParkCellFlags.Unowned ) ), golden.Name );
			Assert.AreEqual( golden.Posts, land.LayerFor( Side, Side ).Type.Count( type => type == FenceLayer.PostType ), golden.Name );
		}
	}
}
