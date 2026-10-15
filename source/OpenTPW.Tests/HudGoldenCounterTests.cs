using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>The HUD's golden key and ticket counters (tp.exe 0x4a0fb0, message 0x1e).</summary>
[TestClass]
public class HudGoldenCounterTests
{
	private static ParkEconomy ParkWithTickets( ParkGameMode mode, int count )
	{
		var park = EconomyTestData.Park( mode: mode );
		var tickets = Enum.GetValues<GoldenTicketKind>().Take( count ).ToArray();
		park.Objectives.Restore( 0, 0, null, new Dictionary<int, int>(), Array.Empty<int>(), tickets );
		return park;
	}

	[TestMethod]
	public void TheKeyCounterShowsThePlayersLedgerKeys()
	{
		// 0x5af680: the player's earned tickets / 3 + the extra keys, not the park's own tickets
		var park = ParkWithTickets( ParkGameMode.FullSimulation, 2 );
		Assert.AreEqual( 4, new EconomyParkStatus( () => park, () => 4 ).GoldenKeys );
		Assert.AreEqual( 0, new EconomyParkStatus( () => park, () => 0 ).GoldenKeys );
		var ledger = new PlayerTicketLedger();
		foreach ( var kind in new[] { GoldenTicketKind.Visitors, GoldenTicketKind.Happiness, GoldenTicketKind.ProfitYear } )
			ledger.RecordEarned( "jungle", kind );
		Assert.AreEqual( 1, new EconomyParkStatus( () => park, () => ledger.Keys ).GoldenKeys );
	}

	[TestMethod]
	public void WithoutALedgerTheParksOwnTicketsGiveTheKeys()
	{
		var park = ParkWithTickets( ParkGameMode.FullSimulation, 6 );
		Assert.AreEqual( 2, new EconomyParkStatus( () => park ).GoldenKeys );
		Assert.AreEqual( 2, new EconomyParkStatus( () => park, () => null ).GoldenKeys );
		Assert.AreEqual( 0, new EconomyParkStatus( () => null, () => 5 ).GoldenKeys, "no economy" );
	}

	[TestMethod]
	public void InstantActionHidesTheKeyCounterButNotTheTickets()
	{
		// 0x4a0ffa: game type 2 skips the key rows (0x34/0x36); the ticket rows (0x35/0x37) are always refreshed
		var park = ParkWithTickets( ParkGameMode.InstantAction, 3 );
		var status = new EconomyParkStatus( () => park, () => 7 );
		Assert.AreEqual( 0, status.GoldenKeys );
		Assert.AreEqual( park.GoldenTicketsAvailable, status.GoldenTickets );
	}

	[TestMethod]
	public void CountersUseTheOriginalFormat()
	{
		// the UTF-16 "%d  x" at 0x752ef8
		Assert.AreEqual( "1  x", ParkHud.CounterText( 1 ) );
		Assert.AreEqual( "12  x", ParkHud.CounterText( 12 ) );
	}
}
