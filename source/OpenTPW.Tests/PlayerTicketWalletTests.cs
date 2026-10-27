using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// UI-051: the park spends from its player's ticket account while it is part of its progress, and the HUD counter reads the account's earned tickets less its spent count
/// (tp.exe 0x5AF5D0, the purchase check 0x4D4AD0 and the spend 0x5AF740).
/// </summary>
[TestClass]
[DoNotParallelize]
public class PlayerTicketWalletTests
{
	/// <summary>The jungle test catalogue's mystery item: three golden tickets (the first copy), then cash.</summary>
	private const int TicketItem = 1112;

	private static ParkEconomyRuntime Runtime() => ParkEconomyRuntime.ForResume( EconomyTestData.Park( initialCash: 100000 ), ParkStartKind.FullSimulation );

	private static string NewProfile( out string root, Action<PlayerTicketLedger>? fill = null )
	{
		root = Directory.CreateTempSubdirectory( "opentpw-wallet-" ).FullName;
		var path = Path.Combine( root, PlayerTicketFile.FileName );
		var ledger = new PlayerTicketLedger();
		fill?.Invoke( ledger );
		PlayerTicketFile.Save( path, ledger.Capture() );
		return path;
	}

	[TestMethod]
	public void ThePlayersEarnedTicketsLessItsSpentCountPayForTheFirstCopyAndSurviveTheFile()
	{
		var path = NewProfile( out var root, ledger =>
		{
			// three tickets earned in other parks of the same player
			ledger.RecordEarned( "hallow", GoldenTicketKind.Visitors );
			ledger.RecordEarned( "fantasy", GoldenTicketKind.Visitors );
			ledger.RecordEarned( "space", GoldenTicketKind.Visitors );
		} );
		try
		{
			var runtime = Runtime();
			using ( var owner = new PlayerProgressRuntimeOwner( path, "jungle", PlayerTicketFile.ReadIfPresent( path )!, runtime, () => true, _ => Assert.Fail() ) )
			{
				var park = runtime.Economy;
				Assert.AreEqual( 0, park.Objectives.GoldenTickets.Count, "the park itself has earned none" );
				Assert.AreEqual( 3, park.GoldenTicketsAvailable, "the counter is the player's earned tickets less the spent count" );
				Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( TicketItem, out _ ) );
				Assert.AreEqual( 3, owner.Ledger.SpentTickets, "the purchase adds its cost to the player's spent count" );
				Assert.IsTrue( owner.Ledger.OwnsPurchasedItem( TicketItem ) );
				Assert.AreEqual( 0, park.GoldenTicketsAvailable );
				Assert.AreEqual( 3, park.TicketsSpent, "the park keeps its own history" );
				var cash = park.Balance;
				Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( TicketItem, out _ ), "a later copy takes the cash path" );
				Assert.IsTrue( park.Balance < cash );
				Assert.AreEqual( 3, owner.Ledger.SpentTickets, "a cash copy spends no tickets" );
				Assert.IsFalse( owner.Dirty, "the purchase was saved" );
			}
			var saved = PlayerTicketFile.LoadIfPresent( path )!;
			Assert.AreEqual( 3, saved.SpentTickets );
			Assert.AreEqual( 0, saved.AvailableTickets );
			Assert.IsTrue( saved.OwnsPurchasedItem( TicketItem ) );
			// another park of the same player finds the item bought: its first copy is cash too
			var other = Runtime();
			using var owner2 = new PlayerProgressRuntimeOwner( path, "space", PlayerTicketFile.ReadIfPresent( path )!, other, () => true, _ => Assert.Fail() );
			Assert.IsNull( other.Economy.ListedTicketCost( TicketItem ), "the player's bought set holds it" );
		}
		finally { Directory.Delete( root, true ); }
	}

	[TestMethod]
	public void AParkRefusesATicketItemTheAccountCannotPayEvenWithTicketsOfItsOwn()
	{
		var path = NewProfile( out var root );
		try
		{
			var runtime = Runtime();
			using var owner = new PlayerProgressRuntimeOwner( path, "jungle", PlayerTicketFile.ReadIfPresent( path )!, runtime, () => true, _ => Assert.Fail() );
			Assert.AreEqual( 0, runtime.Economy.GoldenTicketsAvailable );
			Assert.AreEqual( ParkEconomy.PurchaseResult.NotEnoughGoldenTickets, runtime.Economy.TryBuild( TicketItem, out _ ) );
			Assert.AreEqual( 0, owner.Ledger.SpentTickets );
		}
		finally { Directory.Delete( root, true ); }
	}

	/// <summary>A park saved before purchases went through the account: its earlier ticket items enter the account once, by identity, never twice.</summary>
	[TestMethod]
	public void AnOlderParksTicketPurchasesEnterTheAccountOnce()
	{
		var path = NewProfile( out var root );
		try
		{
			var runtime = Runtime();
			var park = runtime.Economy;
			// the older park: three tickets won, the item bought out of the park's own count
			foreach ( var kind in new[] { GoldenTicketKind.Visitors, GoldenTicketKind.PeopleInPark, GoldenTicketKind.Happiness } )
				park.Objectives.AwardCheatTicket( kind );
			Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( TicketItem, out _ ) );
			Assert.AreEqual( 3, park.TicketsSpent );
			using ( var owner = new PlayerProgressRuntimeOwner( path, "jungle", PlayerTicketFile.ReadIfPresent( path )!, runtime, () => true, _ => Assert.Fail() ) )
			{
				Assert.AreEqual( 3, owner.Ledger.EarnedTickets, "the park's tickets reconcile into the account" );
				Assert.AreEqual( 3, owner.Ledger.SpentTickets, "and so does its purchase" );
				Assert.AreEqual( 0, park.GoldenTicketsAvailable, "the counter still reads the park's history" );
			}
			var again = ParkEconomyRuntime.ForResume( park, ParkStartKind.FullSimulation );
			using var second = new PlayerProgressRuntimeOwner( path, "jungle", PlayerTicketFile.ReadIfPresent( path )!, again, () => true, _ => Assert.Fail() );
			Assert.AreEqual( 3, second.Ledger.SpentTickets, "a second attach does not charge the purchase again" );
			Assert.AreEqual( 0, park.GoldenTicketsAvailable );
		}
		finally { Directory.Delete( root, true ); }
	}

	/// <summary>A park in a co-op session is outside its player's progress: it spends its own tickets and leaves the account alone.</summary>
	[TestMethod]
	public void ASharedParkSpendsItsOwnTicketsAndTheAccountIsUntouched()
	{
		var path = NewProfile( out var root, ledger => ledger.RecordEarned( "hallow", GoldenTicketKind.Visitors ) );
		try
		{
			var runtime = Runtime();
			var shared = true;
			using var owner = new PlayerProgressRuntimeOwner( path, "jungle", PlayerTicketFile.ReadIfPresent( path )!, runtime, () => true, _ => Assert.Fail(), () => shared );
			Assert.AreEqual( 0, runtime.Economy.GoldenTicketsAvailable, "the park counts its own tickets while shared" );
			Assert.AreEqual( ParkEconomy.PurchaseResult.NotEnoughGoldenTickets, runtime.Economy.TryBuild( TicketItem, out _ ) );
			Assert.AreEqual( 0, owner.Ledger.SpentTickets );
		}
		finally { Directory.Delete( root, true ); }
	}
}
