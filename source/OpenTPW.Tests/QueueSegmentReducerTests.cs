using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic contract vectors for ordered passes, not a claim of native executable playback.</summary>
[TestClass]
public class QueueSegmentReducerTests
{
	private static QueueSegmentReducer Reducer( ParkCellMap map, Func<bool>? spend = null, Action<ushort>? refund = null ) =>
		new( map, 17, ( x, y ) => false, spend ?? (() => true), _ => true, () => 10, refund ?? (_ => { }) );

	[DataTestMethod]
	[DataRow(1, 0, 64)] [DataRow(-1, 0, 4)] [DataRow(0, 1, 1)] [DataRow(0, -1, 16)]
	public void ConversionUsesWholeTypePassBeforeOwnerAndLinks( int dx, int dy, int reverse )
	{
		var map = new ParkCellMap( 12, 12 );
		var start = (X: 5, Y: 5); var mid = (X: 5 + dx, Y: 5 + dy); var end = (X: 5 + 2 * dx, Y: 5 + 2 * dy);
		map.SetType( start.X, start.Y, ParkCellType.Queue ); map.SetOwner( start.X, start.Y, 17 );
		map.SetType( mid.X, mid.Y, ParkCellType.Path ); map.SetOwner( mid.X, mid.Y, 99 );
		var result = Reducer( map ).Commit( start, end );
		Assert.AreEqual( 2, result.Built.Count );
		Assert.AreEqual( (byte)3, map.RawTypeAt( mid.X, mid.Y ) );
		Assert.AreEqual( (ushort)17, map.OwnerAt( mid.X, mid.Y ) );
		Assert.AreEqual( (byte)reverse, map.QueueLinkAt( mid.X, mid.Y ) );
		Assert.AreEqual( (byte)reverse, map.QueueLinkAt( start.X, start.Y ), "direction pass also fills first zero direction" );
		Assert.AreEqual( (byte)reverse, map.LinksAt( end.X, end.Y ) );
		Assert.AreEqual( (short)1, map.PlacementCountAt( start.X, start.Y ) );
	}

	[DataTestMethod]
	[DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)]
	public void FirstLinkPassUsesExactEntranceAndDirectionBeforeDirectionFill( int direction )
	{
		var map = new ParkCellMap( 12, 12 );
		var step = GuestPathGrid.Directions[direction];
		var entry = (X: 5 + step.DX, Y: 5 + step.DY);
		var end = (X: 5 - step.DX, Y: 5 - step.DY);
		var toward = GuestPathGrid.LinkValue( direction );
		var back = PathLinkWriter.Opposite( toward );
		map.SetType( 5, 5, ParkCellType.Queue ); map.SetOwner( 5, 5, 17 );
		map.SetType( entry.X, entry.Y, ParkCellTypes.Entrance ); map.SetOwner( entry.X, entry.Y, 17 );
		map.SetQueueLink( entry.X, entry.Y, back );
		var reducer = new QueueSegmentReducer( map, 17, (_, _) => false, () => false, _ => true, () => 10, _ => { }, entrance: () => entry );
		reducer.Commit( (5, 5), end );
		Assert.AreEqual( toward, (byte)(map.LinksAt( 5, 5 ) & toward) );
		Assert.AreEqual( back, map.LinksAt( entry.X, entry.Y ) );
		Assert.AreEqual( toward, map.QueueLinkAt( 5, 5 ), "first direction is filled even after later debit failure" );

		map.SetLinks( 5, 5, 0 ); map.SetLinks( entry.X, entry.Y, 0 );
		map.SetQueueLink( entry.X, entry.Y, toward );
		reducer.Commit( (5, 5), end );
		Assert.AreEqual( (byte)0, (byte)(map.LinksAt( 5, 5 ) & toward), "wrong entrance orientation does not link" );
		Assert.AreEqual( (byte)0, map.LinksAt( entry.X, entry.Y ) );
		map.SetQueueLink( entry.X, entry.Y, back );
		new QueueSegmentReducer( map, 17, (_, _) => false, () => false, _ => true, () => 10, _ => { }, entrance: () => (9, 9) ).Commit( (5, 5), end );
		Assert.AreEqual( (byte)0, map.LinksAt( entry.X, entry.Y ), "nearby entrance is not selected entry" );
	}

	[TestMethod]
	public void DestructiveCashFailureRetainsFullEndpointAndLaterPassWrites()
	{
		var map = new ParkCellMap( 12, 12 );
		map.SetType( 2, 3, ParkCellType.Queue ); map.SetOwner( 2, 3, 17 );
		map.SetType( 3, 3, ParkCellType.Path ); map.SetLinks( 3, 3, 2 );
		map.SetType( 4, 2, ParkCellType.Path ); map.SetLinks( 4, 2, 32 );
		map.SetType( 4, 3, ParkCellType.Path );
		var result = Reducer( map, () => false ).Commit( (2, 3), (5, 3) );
		Assert.AreEqual( (3, 3), result.StoppedAt );
		Assert.AreEqual( (5, 3), result.QueueReceipt!.End );
		Assert.AreEqual( 0, result.Built.Count );
		Assert.AreEqual( (byte)0, map.RawTypeAt( 3, 3 ) );
		Assert.AreEqual( (byte)0, map.LinksAt( 4, 2 ) );
		Assert.AreEqual( (byte)1, map.RawTypeAt( 4, 3 ), "untouched type suffix" );
		foreach ( var x in new[] { 3, 4, 5 } )
		{
			Assert.AreEqual( (byte)64, map.QueueLinkAt( x, 3 ) );
			Assert.AreEqual( (ushort)17, map.OwnerAt( x, 3 ) );
		}
	}

	[TestMethod]
	public void UndoKeepsEmptyResidueContinuesPastItAndSkipsPreviousPathCorner()
	{
		var map = new ParkCellMap( 12, 12 );
		map.SetType( 2, 3, ParkCellType.Queue ); map.SetOwner( 2, 3, 17 );
		var payments = 0; var refunds = new List<ushort>();
		var reducer = Reducer( map, () => ++payments == 1, refunds.Add );
		var result = reducer.Commit( (2, 3), (5, 3) );
		reducer.Undo( result.QueueReceipt! );
		Assert.AreEqual( 1, refunds.Count );
		Assert.AreEqual( (byte)0, map.RawTypeAt( 3, 3 ) );
		Assert.AreEqual( (ushort)17, map.OwnerAt( 4, 3 ), "failed empty residue survives undo" );
		Assert.AreEqual( (byte)3, map.RawTypeAt( 2, 3 ), "start counter decrements but keeps front" );
		map.SetType( 2, 3, ParkCellType.Path );
		var before = map.Version;
		reducer.Undo( result.QueueReceipt! );
		Assert.AreEqual( before, map.Version, "previous path corner suppresses entire clear pass" );
	}

	[TestMethod]
	public void TerminalPathBypassesNoModifyButInteriorPathDoesNotAndPreviewNeverWrites()
	{
		var map = new ParkCellMap( 12, 12 );
		map.SetType( 2, 3, ParkCellType.Queue );
		map.SetType( 4, 3, ParkCellType.Path ); map.SetFlags( 4, 3, ParkCellFlags.NoModify );
		var before = map.Version;
		Assert.IsTrue( Reducer( map ).Preview( (2, 3), (4, 3) ).Completed );
		Assert.AreEqual( CellBuildResult.NoModify, Reducer( map ).Preview( (2, 3), (5, 3) ).StoppedBy );
		Assert.AreEqual( before, map.Version );
		Reducer( map ).Commit( (2, 3), (4, 3) );
		Assert.AreEqual( (byte)1, map.RawTypeAt( 4, 3 ) );
		Assert.AreEqual( (byte)64, map.QueueLinkAt( 4, 3 ) );
		Assert.IsTrue( map.FlagsAt( 4, 3 ).HasFlag( ParkCellFlags.NoModify ) );
	}
	[TestMethod]
	public void CounterOverflowDomainIsExplicitAndForeignOwnerGhostRefuses()
	{
		var map = new ParkCellMap( 8, 8 );
		map.SetType( 2, 2, ParkCellType.Queue ); map.SetOwner( 2, 2, 99 );
		Assert.AreEqual( CellBuildResult.Occupied, Reducer( map ).Preview( (2, 2), (3, 2) ).StoppedBy );
		map.SetOwner( 2, 2, 17 ); map.SetPlacementCount( 2, 2, short.MaxValue );
		var before = map.Version;
		Assert.IsFalse( Reducer( map ).Supports( (2, 2), (3, 2) ) );
		Assert.ThrowsException<InvalidOperationException>( () => Reducer( map ).Commit( (2, 2), (3, 2) ) );
		Assert.AreEqual( before, map.Version );
		map.SetPlacementCount( 2, 2, short.MaxValue - 1 );
		var reducer = Reducer( map ); var segment = reducer.Commit( (2, 2), (3, 2) );
		reducer.Undo( segment.QueueReceipt! );
		Assert.AreEqual( (short)(short.MaxValue - 1), map.PlacementCountAt( 2, 2 ) );
	}

	[TestMethod]
	public void TerminalSuccessHasNoActiveToolReceipt()
	{
		var map = new ParkCellMap( 8, 8 );
		map.SetType( 2, 2, ParkCellType.Queue ); map.SetType( 4, 2, ParkCellType.Path );
		Assert.IsNull( Reducer( map ).Commit( (2, 2), (4, 2) ).QueueReceipt );
	}

	[TestMethod]
	public void FailureBeforeTerminalPathKeepsLatchClearDespiteFullRefreshPass()
	{
		var map = new ParkCellMap( 8, 8 );
		map.SetType( 2, 2, ParkCellType.Queue ); map.SetType( 4, 2, ParkCellType.Path );
		var result = Reducer( map, () => false ).Commit( (2, 2), (4, 2) );
		Assert.AreEqual( false, result.QueueFinalEndsTool );
		Assert.IsNotNull( result.QueueReceipt );
		Assert.AreEqual( (byte)64, map.QueueLinkAt( 4, 2 ), "later direction pass still reaches terminal path" );
	}

	[DataTestMethod]
	[DataRow(75, 75, true)] [DataRow(74, 75, false)] [DataRow(0, 0, true)]
	[DataRow(int.MinValue, 1, true)]
	public void AuthoritativePerCellSpendingUsesSignedWrappedSubtraction( int cash, int cost, bool succeeds )
	{
		var economy = EconomyTestData.CellCostPark( cash, 1, cost );
		var map = new ParkCellMap( 8, 8 ); map.SetType( 2, 2, ParkCellType.Queue ); map.SetType( 3, 2, ParkCellType.Path );
		var reducer = new QueueSegmentReducer( map, 17, (_, _) => false,
			() => economy.TrySpendCell( CellPurchase.Queue ) == ParkEconomy.PurchaseResult.Ok,
			economy.CanPreviewLine, () => economy.CellCost( CellPurchase.Queue ), _ => { } );
		var result = reducer.Commit( (2, 2), (4, 2) );
		Assert.AreEqual( succeeds ? (byte)3 : (byte)0, map.RawTypeAt( 3, 2 ) );
		Assert.AreEqual( (ushort)17, map.OwnerAt( 3, 2 ), "metadata passes run even after debit refusal" );
		if ( !succeeds ) Assert.AreEqual( (long)cash, economy.Balance );
	}

	[TestMethod]
	public void NativeRefundUsesLowProductWordAndUnsignedDivision()
	{
		var economy = EconomyTestData.CellCostPark( 50000, 1, int.MaxValue );
		var id = economy.RegisterExisting( 1100 ).Id;
		Assert.IsTrue( economy.TryGetObject( id, out var ride ) );
		var percent = economy.ScrapPercent( ride! );
		var expected = unchecked((uint)(int.MaxValue * percent)) / 100U;
		Assert.AreEqual( (long)expected, economy.RefundQueueCellNative( id ) );
		Assert.AreEqual( 0L, economy.RefundQueueCellNative( 99999 ) );
	}

	[DataTestMethod]
	[DataRow(0, 0, true)] [DataRow(17, 0, true)] [DataRow(99, 0, false)]
	[DataRow(17, 2, true)] [DataRow(17, 10, false)]
	public void LinkingTestsRawPredecessorOwnerAndAllEightBitsBeforeOwnerPass( int owner, int links, bool connects )
	{
		var map = new ParkCellMap( 8, 8 ); map.SetType( 2, 2, ParkCellType.Queue );
		map.SetOwner( 2, 2, (ushort)owner ); map.SetLinks( 2, 2, (byte)links );
		Reducer( map ).Commit( (2, 2), (3, 2) );
		Assert.AreEqual( connects ? (byte)64 : (byte)0, map.LinksAt( 3, 2 ) );
		Assert.AreEqual( (ushort)17, map.OwnerAt( 2, 2 ), "owner pass follows link test" );
	}

	[TestMethod]
	public void UndoCardinalBendStillClearsWhenDiagonalBitsAlsoExist()
	{
		var map = new ParkCellMap( 8, 8 ); map.SetType( 2, 2, ParkCellType.Queue );
		var reducer = Reducer( map ); var result = reducer.Commit( (2, 2), (3, 2) );
		map.SetLinks( 3, 2, 0x43 ); map.SetPlacementCount( 3, 2, 5 );
		reducer.Undo( result.QueueReceipt! );
		Assert.AreEqual( (byte)0, map.RawTypeAt( 3, 2 ) );
	}


	[TestMethod]
	public void BackReevaluatesTheRideEvenWhenThePriorCornerIsAPathAndTheSecondClickIsSuppressed()
	{
		// tp.exe 0x52FE50: 0x4DE1F0 (0x52FFEC) runs after every undo that popped a point; a prior corner that is a path cell only suppresses the second removal click
		var map = new ParkCellMap( 12, 12 );
		map.SetType( 5, 5, ParkCellType.Path );
		map.SetType( 5, 6, ParkCellType.Queue ); map.SetOwner( 5, 6, 17 );
		map.SetType( 5, 7, ParkCellType.Queue ); map.SetOwner( 5, 7, 17 );
		var edits = 0;
		var reducer = new QueueSegmentReducer( map, 17, ( x, y ) => false, () => true, _ => true, () => 10, _ => { }, changed: () => edits++ );
		reducer.Undo( new QueueSegmentReceipt( (5, 5), (5, 7) ) );
		Assert.AreEqual( 1, edits, "the re-evaluation runs once" );
		Assert.AreEqual( (byte)3, map.RawTypeAt( 5, 6 ), "nothing was cleared" );
		Assert.AreEqual( (byte)3, map.RawTypeAt( 5, 7 ) );
	}

	[TestMethod]
	public void ALowerPriorityObjectOnTheLineIsDemolishedByTheFirstPassAndItsCellIsLaid()
	{
		// tp.exe 0x53452F: the commit's first line pass (cell op 0x87) demolishes the object on a cell whose owner's priority is below the tool's; the validator lets only such objects through (0x535D66)
		var map = new ParkCellMap( 12, 12 );
		map.SetType( 5, 5, ParkCellType.Queue ); map.SetOwner( 5, 5, 17 );
		map.SetType( 5, 6, ParkCellTypes.Object ); map.SetOwner( 5, 6, 99 );
		var demolished = new List<(int X, int Y)>();
		var order = new List<string>();
		var reducer = new QueueSegmentReducer( map, 17, ( x, y ) => false, () => { order.Add( "spend" ); return true; }, _ => true, () => 10, _ => { },
			givesWay: ( x, y ) => map.RawTypeAt( x, y ) == ParkCellTypes.Object,
			demolish: ( x, y ) => { order.Add( "demolish" ); demolished.Add( (x, y) ); map.SetType( x, y, ParkCellType.Empty ); map.SetOwner( x, y, 0 ); } );
		Assert.IsTrue( reducer.Supports( (5, 5), (5, 7) ) );
		var preview = reducer.Preview( (5, 5), (5, 7) );
		Assert.AreEqual( CellBuildResult.Ok, preview.StoppedBy );
		Assert.AreEqual( 2, preview.Built.Count, "the object's cell counts as a cell to lay" );
		Assert.AreEqual( 0, demolished.Count, "a preview demolishes nothing" );
		var result = reducer.Commit( (5, 5), (5, 7) );
		CollectionAssert.AreEqual( new[] { (5, 6) }, demolished );
		Assert.AreEqual( "demolish", order[0], "the demolition pass precedes the type pass" );
		Assert.AreEqual( 2, result.Built.Count );
		Assert.AreEqual( (byte)3, map.RawTypeAt( 5, 6 ) );
		Assert.AreEqual( (ushort)17, map.OwnerAt( 5, 6 ) );
		Assert.AreEqual( (byte)3, map.RawTypeAt( 5, 7 ) );

		var refusing = new ParkCellMap( 12, 12 );
		refusing.SetType( 5, 5, ParkCellType.Queue ); refusing.SetOwner( 5, 5, 17 );
		refusing.SetType( 5, 6, ParkCellTypes.Object );
		Assert.IsFalse( new QueueSegmentReducer( refusing, 17, ( x, y ) => false, () => true, _ => true, () => 10, _ => { } ).Supports( (5, 5), (5, 7) ), "an object nothing gives way for stays outside the domain" );
	}

	[TestMethod]
	public void AWorldThatTypesNoFootprintsDemolishesTheObjectOnAnEmptyTypedCell()
	{
		// a level without typed footprints (the Theme Park Inc slice) holds the object in its object list while the cell map says empty: the cell still gives way and the first pass demolishes
		var map = new ParkCellMap( 12, 12 );
		map.SetType( 5, 5, ParkCellType.Queue ); map.SetOwner( 5, 5, 17 );
		var objectAt = (X: 5, Y: 6);
		var demolished = new List<(int X, int Y)>();
		var reducer = new QueueSegmentReducer( map, 17, ( x, y ) => false, () => true, _ => true, () => 10, _ => { },
			givesWay: ( x, y ) => (x, y) == objectAt, demolish: ( x, y ) => demolished.Add( (x, y) ) );
		reducer.Commit( (5, 5), (5, 7) );
		CollectionAssert.AreEqual( new[] { objectAt }, demolished );
		Assert.AreEqual( (byte)3, map.RawTypeAt( 5, 6 ) );
	}
}
