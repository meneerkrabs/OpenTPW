using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class PlayerTicketPurchaseTests
{
	private static PlayerTicketLedger Ledger()
	{
		var ledger = new PlayerTicketLedger();
		ledger.Reconcile( "jungle", Enum.GetValues<GoldenTicketKind>() );
		ledger.RecordCheated( "space", GoldenTicketKind.Visitors, false );
		return ledger;
	}
	private static byte[] Bytes( PlayerTicketLedger ledger ) => PlayerTicketFile.Serialize( ledger.Capture() );

	[TestMethod]
	public void PurchaseDebitsOnceAndPreservesEarnedKeysHoldersAndCheatedProvenance()
	{
		var ledger = Ledger(); var before = ledger.Capture(); var keys = ledger.Keys;
		Assert.AreEqual( TicketPurchaseOutcome.Purchased, ledger.TryPurchase( 1303, 2 ) );
		Assert.AreEqual( 2, ledger.SpentTickets ); Assert.AreEqual( before.Earned.Count - 2, ledger.AvailableTickets );
		Assert.IsTrue( ledger.OwnsPurchasedItem( 1303 ) ); Assert.IsFalse( ledger.OwnsPurchasedItem( 2303 ) );
		Assert.AreEqual( keys, ledger.Keys ); Assert.AreEqual( before.ExtraKeys, ledger.ExtraKeys );
		CollectionAssert.AreEqual( before.Earned.ToArray(), ledger.Capture().Earned.ToArray() );
		CollectionAssert.AreEqual( before.Holders!.ToArray(), ledger.Capture().Holders!.ToArray() );
		CollectionAssert.AreEqual( before.Cheated!.ToArray(), ledger.Capture().Cheated!.ToArray() );
		var bought = Bytes( ledger );
		Assert.AreEqual( TicketPurchaseOutcome.AlreadyOwned, ledger.TryPurchase( 1303, int.MaxValue ) );
		CollectionAssert.AreEqual( bought, Bytes( ledger ) );
		Assert.AreEqual( TicketPurchaseOutcome.Purchased, ledger.TryPurchase( 2303, ledger.AvailableTickets ) );
		Assert.AreEqual( 0, ledger.AvailableTickets ); Assert.AreEqual( keys, ledger.Keys );
		var exhausted = Bytes( ledger );
		Assert.AreEqual( TicketPurchaseOutcome.InsufficientTickets, ledger.TryPurchase( 3303, 1 ) );
		CollectionAssert.AreEqual( exhausted, Bytes( ledger ) );
	}

	[DataTestMethod]
	[DataRow( -1, 1 )]
	[DataRow( 1303, 0 )]
	[DataRow( 1303, -1 )]
	public void InvalidPurchaseHasNoPartialMutation( int id, int cost )
	{
		var ledger = Ledger(); var before = Bytes( ledger );
		Assert.ThrowsException<InvalidDataException>( () => ledger.TryPurchase( id, cost ) );
		CollectionAssert.AreEqual( before, Bytes( ledger ) );
	}

	[TestMethod]
	public void InsufficientHugeCostAndEmptyLedgerCannotSpend()
	{
		foreach ( var ledger in new[] { Ledger(), new PlayerTicketLedger() } )
		{
			var before = Bytes( ledger );
			Assert.AreEqual( TicketPurchaseOutcome.InsufficientTickets, ledger.TryPurchase( int.MaxValue, int.MaxValue ) );
			CollectionAssert.AreEqual( before, Bytes( ledger ) );
		}
	}

	[TestMethod]
	public void KnownPurchasesRoundTripWithStableOrderingAndDefensiveCopies()
	{
		var source = new[] { new KnownTicketPurchase( 2303, 2 ), new KnownTicketPurchase( 1303, 1 ) };
		var ledger = PlayerTicketLedger.Restore( Ledger().Capture() with { SpentTickets = 5, KnownPurchases = source } );
		source[0] = new( 9999, 2 );
		var snapshot = ledger.Capture();
		CollectionAssert.AreEqual( new[] { 1303, 2303 }, snapshot.KnownPurchases!.Select( p => p.InfoId ).ToArray() );
		Assert.ThrowsException<NotSupportedException>( () => ((IList<KnownTicketPurchase>)snapshot.KnownPurchases)[0] = new( 7777, 1 ) );
		var bytes = Bytes( ledger ); Assert.AreEqual( 2, JsonNode.Parse( bytes )!["Version"]!.GetValue<int>() );
		var restored = PlayerTicketFile.Deserialize( bytes );
		CollectionAssert.AreEqual( bytes, Bytes( restored ) );
		Assert.IsTrue( restored.OwnsPurchasedItem( 1303 ) ); Assert.IsTrue( restored.OwnsPurchasedItem( 2303 ) );
		Assert.IsFalse( restored.OwnsPurchasedItem( 9999 ) );
		Assert.AreEqual( TicketPurchaseOutcome.Purchased, restored.TryPurchase( 0, 1 ) );
		Assert.IsFalse( ledger.OwnsPurchasedItem( 0 ) ); Assert.AreEqual( 2, snapshot.KnownPurchases.Count );
		Assert.AreEqual( 6, restored.SpentTickets ); // Two legacy unknown tickets remain spent.
	}

	[TestMethod]
	public void LegacyScalarRoundTripsAsVersionOneWithoutInventedOwnership()
	{
		var ledger = PlayerTicketLedger.Restore( Ledger().Capture() with { SpentTickets = 4 } );
		var bytes = Bytes( ledger ); var json = JsonNode.Parse( bytes )!;
		Assert.AreEqual( 1, json["Version"]!.GetValue<int>() ); Assert.IsNull( json["KnownPurchases"] );
		var restored = PlayerTicketFile.Deserialize( bytes );
		CollectionAssert.AreEqual( bytes, Bytes( restored ) );
		Assert.IsNull( restored.Capture().KnownPurchases ); Assert.IsFalse( restored.OwnsPurchasedItem( 1303 ) );
		Assert.AreEqual( TicketPurchaseOutcome.Purchased, restored.TryPurchase( 1303, 2 ) );
		Assert.AreEqual( 6, restored.SpentTickets ); Assert.AreEqual( 1, restored.Capture().KnownPurchases!.Count );
	}

	[TestMethod]
	public void LegacyLiteralVersionOneBytesAndUnknownSpendingRemainUnchanged()
	{
		const string legacy = "{\"Format\":\"opentpw-player-progress\",\"Version\":1,\"Mode\":\"FullSimulation\",\"ExtraKeys\":0,\"SpentTickets\":1,\"Earned\":[{\"Theme\":\"jungle\",\"Kind\":\"Visitors\"}]}";
		var bytes = Encoding.UTF8.GetBytes( legacy ); var ledger = PlayerTicketFile.Deserialize( bytes );
		Assert.AreEqual( 1, ledger.SpentTickets ); Assert.IsNull( ledger.Capture().KnownPurchases );
		CollectionAssert.AreEqual( bytes, Bytes( ledger ) );
	}

	[TestMethod]
	public void InvalidSnapshotPurchaseListsAreRejectedBeforeSerialization()
	{
		var snapshot = Ledger().Capture() with { SpentTickets = 3 };
		var invalid = new KnownTicketPurchase[][] {
			new[] { new KnownTicketPurchase( -1, 1 ) }, new[] { new KnownTicketPurchase( 1, 0 ) },
			new[] { new KnownTicketPurchase( 1, -1 ) }, new[] { new KnownTicketPurchase( 1, int.MaxValue ) },
			new[] { new KnownTicketPurchase( 1, 2 ), new KnownTicketPurchase( 2, 2 ) },
			new[] { new KnownTicketPurchase( 1, 1 ), new KnownTicketPurchase( 1, 1 ) },
			new KnownTicketPurchase[] { null! }, Enumerable.Range( 0, 31 ).Select( i => new KnownTicketPurchase( i, 1 ) ).ToArray() };
		foreach ( var list in invalid )
			Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Serialize( snapshot with { KnownPurchases = list } ) );
	}

	[TestMethod]
	public void VersionAndRequiredPurchaseFieldsAreValidated()
	{
		var ledger = Ledger(); ledger.TryPurchase( 1303, 1 );
		var original = Encoding.UTF8.GetString( Bytes( ledger ) );
		foreach ( var version in new[] { 0, 1, 3 } )
		{
			var json = JsonNode.Parse( original )!; json["Version"] = version;
			Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ) );
		}
		foreach ( var field in new[] { "InfoId", "Cost" } )
		{
			var json = JsonNode.Parse( original )!; json["KnownPurchases"]![0]!.AsObject().Remove( field );
			Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ) );
		}
		foreach ( var value in new[] { "null", "[]", "[null]" } )
		{
			var json = JsonNode.Parse( original )!; json["KnownPurchases"] = JsonNode.Parse( value );
			Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) ) );
		}
	}
}
