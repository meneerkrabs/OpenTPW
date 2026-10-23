using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The guests' thoughts as tp.exe sets them (GUEST-006, docs/GUESTS.md "Thoughts"): the pick of the needs phase (<c>0x004FC8A0</c>), the thought object's
/// bubble with its 12 turns and its cooldown by priority (<c>0x0050BE80</c>, <c>0x0050BE40</c>).
/// </summary>
[TestClass]
public class GuestThoughtTests
{
	[TestMethod]
	public void ThePickTakesNeedsThenLitterThenHappiness()
	{
		GuestThought? Pick( float hunger = 0, float thirst = 0, float toilet = 0, float illness = 0, float happiness = 50, float messy = 0 ) =>
			GuestThoughts.Pick( hunger, thirst, toilet, illness, happiness, messy );
		Assert.AreEqual( GuestThought.HungryAndThirsty, Pick( hunger: 91, thirst: 91 ), "both bytes above 90" );
		Assert.AreEqual( null, Pick( hunger: 90.9f, thirst: 99 ), "a byte of 90 is not above 90, and 99 is not above 99" );
		Assert.AreEqual( GuestThought.Hungry, Pick( hunger: 100, thirst: 50 ) );
		Assert.AreEqual( GuestThought.Thirsty, Pick( thirst: 100 ) );
		Assert.AreEqual( GuestThought.NeedToilet, Pick( toilet: 100 ) );
		Assert.AreEqual( GuestThought.FeelingSick, Pick( illness: 100 ) );
		Assert.AreEqual( GuestThought.NeedToilet, Pick( toilet: 100, illness: 100, messy: 1 ), "needs come first" );
		Assert.AreEqual( GuestThought.TooMessy, Pick( messy: 0.21f, happiness: 100 ), "litter before happiness" );
		Assert.AreEqual( null, Pick( messy: 0.2f ), "0.2 is not above 0.2" );
		Assert.AreEqual( GuestThought.TooMessy, Pick( messy: float.PositiveInfinity ), "litter on a block without paths" );
		Assert.AreEqual( GuestThought.Happy, Pick( happiness: 100 ) );
		Assert.AreEqual( GuestThought.Unhappy, Pick( happiness: 9.9f ) );
		Assert.AreEqual( null, Pick( happiness: 10 ), "10 to 99 keeps the thought" );
		Assert.AreEqual( null, Pick( happiness: 99.9f ) );
	}

	[TestMethod]
	public void ABubbleWaitsForTheNewThoughtsPriority()
	{
		Assert.AreEqual( (1, 0, 3, 2, 0), (GuestThoughts.Priority( 1 ), GuestThoughts.Priority( 5 ), GuestThoughts.Priority( 9 ), GuestThoughts.Priority( 19 ),
			GuestThoughts.Priority( 22 )) );
		Assert.IsTrue( GuestThoughts.TakesBubble( (int)GuestThought.Pleased, 100, 100 ), "priority 0: at once" );
		Assert.IsFalse( GuestThoughts.TakesBubble( (int)GuestThought.Hungry, 119, 100 ) );
		Assert.IsTrue( GuestThoughts.TakesBubble( (int)GuestThought.Hungry, 120, 100 ), "priority 1: 20 turns" );
		Assert.IsFalse( GuestThoughts.TakesBubble( (int)GuestThought.Happy, 159, 100 ) );
		Assert.IsTrue( GuestThoughts.TakesBubble( (int)GuestThought.Happy, 160, 100 ), "priority 3: 60 turns" );
		Assert.IsFalse( GuestThoughts.TakesBubble( 23, 1000, 0 ), "not a known thought" );
		Assert.IsFalse( GuestThoughts.TakesBubble( 0, 1000, 0 ) );
	}

	[TestMethod]
	public void AHungryGuestThinksItOnSomePhaseAndItsBubbleGoesAfterTwelveTurns()
	{
		var settings = GuestTests.CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 9 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 6, 5 );
		// the first bubble needs turn ≥ stamp 0 + 20 × priority 1
		while ( simulation.ParkTurn < 2 * GuestThoughts.CooldownTurns )
		{
			guest.Hunger = 0;
			simulation.Tick( GuestSnapshotTests.Tick );
		}
		long thoughtAt = -1;
		for ( var tick = 0; tick < 60 * 60 * 4 && thoughtAt < 0; tick++ )
		{
			guest.Hunger = 100;
			simulation.Tick( GuestSnapshotTests.Tick );
			if ( guest.Thought == GuestThought.Hungry )
				thoughtAt = simulation.ParkTurn;
		}
		Assert.IsTrue( thoughtAt > 0, "within four minutes the one-in-ten pick comes" );
		Assert.IsTrue( NeedsUpdate.IsPhase( (uint)thoughtAt, guest.Id ), "on the guest's needs phase" );
		Assert.AreEqual( (GuestThought.Hungry, thoughtAt), (guest.BubbleThought, guest.BubbleTurn), "the first bubble" );
		long gone = -1;
		for ( var tick = 0; tick < 60 * 60 && gone < 0; tick++ )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			if ( guest.BubbleThought == GuestThought.None )
				gone = simulation.ParkTurn;
		}
		Assert.IsTrue( gone > thoughtAt + GuestThoughts.BubbleTurns && gone <= thoughtAt + GuestThoughts.BubbleTurns + 4, $"{thoughtAt} -> {gone}: the first needs phase past 12 turns" );
		Assert.AreEqual( GuestThought.Hungry, guest.Thought, "the thought stays" );

		var restored = new GuestSimulation( GuestTests.CreateCross(), settings, 9 ) { ArrivalsEnabled = false };
		guest.BubbleThought = GuestThought.Hungry;
		restored.RestoreSnapshot( simulation.CaptureSnapshot() );
		var copy = restored.Guests.Single( item => item.Id == guest.Id );
		Assert.AreEqual( (guest.BubbleThought, guest.BubbleTurn), (copy.BubbleThought, copy.BubbleTurn), "the bubble is saved" );
	}

	[TestMethod]
	public void LitterInTheGuestsBlockMakesItThinkTooMessy()
	{
		var settings = GuestTests.CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		var litter = new ParkLitter();
		var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 9 ) { ArrivalsEnabled = false, Litter = litter };
		var guest = simulation.SpawnInPark( 6, 5 );
		var seen = false;
		for ( var tick = 0; tick < 60 * 60 * 4 && !seen; tick++ )
		{
			// litter on every path cell of the guest's 4 × 4 block
			for ( var x = guest.CellX & ~3; x < (guest.CellX & ~3) + 4; x++ )
				for ( var y = guest.CellY & ~3; y < (guest.CellY & ~3) + 4; y++ )
					if ( simulation.Grid.IsWalkable( x, y ) && litter.TypeAt( x, y ) == LitterType.None )
						litter.Drop( x, y, (LitterType)1 );
			guest.Happiness = 50;
			simulation.Tick( GuestSnapshotTests.Tick );
			seen = guest.Thought == GuestThought.TooMessy;
		}
		Assert.IsTrue( seen );
	}

	private sealed class SideshowPayments( bool won ) : IGuestPayments
	{
		public int AdmissionFee => 0;
		public bool TryPayAdmission( out int feePaid ) { feePaid = 0; return true; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) { pricePaid = 0; return true; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid, out bool? result )
		{
			result = won;
			return TryPayVisit( attraction, guestMoney, out pricePaid );
		}
	}

	[TestMethod]
	public void ASideshowThinksItsResultAndARideOrShopNothing()
	{
		foreach ( var won in new[] { true, false } )
		{
			var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 9 ) { ArrivalsEnabled = false, Payments = new SideshowPayments( won ) };
			IRideVisitorHost host = simulation;
			var guest = simulation.SpawnInPark( 6, 5 );
			guest.Happiness = 100;
			var sideshow = new RideVisitorBridge( 1, "Sideshow", RideVisitorKind.Sideshow, 2, 50, 10 ) { ExitCell = (6, 5) };
			host.OnVisitorBoarded( sideshow, guest.Id );
			Assert.AreEqual( won, guest.SideshowWon );
			host.OnVisitorReleased( sideshow, guest.Id );
			Assert.AreEqual( won ? GuestThought.Pleased : GuestThought.Dissatisfied, guest.Thought, $"won {won}, whatever the happiness did" );
			Assert.IsNull( guest.SideshowWon );
			guest.Thought = GuestThought.Hungry;
			var ride = new RideVisitorBridge( 2, "Ride", RideVisitorKind.Ride, 2, 50, 10 ) { ExitCell = (6, 5) };
			host.OnVisitorBoarded( ride, guest.Id );
			host.OnVisitorReleased( ride, guest.Id );
			Assert.AreEqual( GuestThought.Hungry, guest.Thought, "a ride sets none" );
		}
	}

	[TestMethod]
	public void ALostSideshowSkipsTheUseRoutineForTheMediumLoss()
	{
		// tp.exe 0x004FDA0D: a lost play goes to "Person lost this sideshow" (0x004FDB8D), the medium happiness step, not the use routine 0x004FE1E0
		var settings = GuestTests.CreateSettings();
		var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 9 ) { ArrivalsEnabled = false, Payments = new SideshowPayments( false ) };
		IRideVisitorHost host = simulation;
		var guest = simulation.SpawnInPark( 6, 5 );
		guest.Happiness = 50;
		guest.Litter = 0;
		var sideshow = new RideVisitorBridge( 1, "Sideshow", RideVisitorKind.Sideshow, 2, 50, 10 ) { ExitCell = (6, 5), LitterEffect = 20, HappinessEffect = 30 };
		host.OnVisitorBoarded( sideshow, guest.Id );
		host.OnVisitorReleased( sideshow, guest.Id );
		Assert.AreEqual( 50f - (settings.MediumHappinessChange & 0xFF), guest.Happiness, "only the medium loss" );
		Assert.AreEqual( 0f, guest.Litter, "no use effects" );
		Assert.AreEqual( GuestThought.Dissatisfied, guest.Thought );
	}

	[TestMethod]
	public void OldSavesLoseTheInventedThoughtsAndAFutureBubbleIsRefused()
	{
		var settings = GuestTests.CreateSettings();
		var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 9 ) { ArrivalsEnabled = false };
		var happy = simulation.SpawnInPark( 6, 5 );
		var okay = simulation.SpawnInPark( 6, 5 );
		happy.Thought = GuestThought.SuperHappy;
		okay.Thought = GuestThought.Okay;
		var snapshot = simulation.CaptureSnapshot();
		var old = snapshot with { SchemaVersion = 6 };
		var restored = new GuestSimulation( GuestTests.CreateCross(), settings, 9 ) { ArrivalsEnabled = false };
		restored.RestoreSnapshot( old );
		Assert.AreEqual( (GuestThought.Happy, GuestThought.None), (restored.Find( happy.Id )!.Thought, restored.Find( okay.Id )!.Thought ) );

		var future = snapshot with { Guests = snapshot.Guests.Select( guest => guest with { BubbleTurn = long.MaxValue } ).ToArray() };
		Assert.ThrowsException<System.IO.InvalidDataException>( () => new GuestSimulation( GuestTests.CreateCross(), settings, 9 ).RestoreSnapshot( future ) );
	}

	[TestMethod]
	public void ABubbleShownWhereTheGuestsTurnLeadsTheirTicksTurnRestoresAndRunsOnAsTheOriginal()
	{
		var settings = GuestTests.CreateSettings();
		var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 9 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 6, 5 );
		// an economy 5 ticks ahead (one that started at an original save's turn): the guests cut their turns where it does, so in some ticks their park turn is one past their own tick's
		const int phase = 5;
		simulation.AlignTurns( phase );
		while ( simulation.ParkTurn == ParkCalendar.Turn( simulation.TickCount ) )
			simulation.Tick( GuestSnapshotTests.Tick );
		Assert.AreEqual( ParkCalendar.Turn( simulation.TickCount ) + 1, simulation.ParkTurn, "the park turn leads by one" );
		// a thought shown in such a tick stamps its bubble with the park turn (Think)
		guest.BubbleThought = GuestThought.Happy;
		guest.BubbleTurn = simulation.ParkTurn;
		var snapshot = simulation.CaptureSnapshot();
		var restored = new GuestSimulation( GuestTests.CreateCross(), settings, 9 ) { ArrivalsEnabled = false };
		restored.RestoreSnapshot( snapshot );
		Assert.AreEqual( (GuestThought.Happy, simulation.ParkTurn), (restored.Find( guest.Id )!.BubbleThought, restored.Find( guest.Id )!.BubbleTurn ), "the bubble is restored" );
		// aligned again as the level aligns it before its next running tick, the restored park runs on as the original, the bubble's end included
		restored.AlignTurns( restored.TickCount + phase );
		Assert.AreEqual( simulation.ParkTurn, restored.ParkTurn, "the same park turn once aligned" );
		for ( var tick = 0; tick < (GuestThoughts.BubbleTurns + 2) * 15; tick++ )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			restored.Tick( GuestSnapshotTests.Tick );
		}
		Assert.AreEqual( System.Text.Json.JsonSerializer.Serialize( simulation.CaptureSnapshot() ), System.Text.Json.JsonSerializer.Serialize( restored.CaptureSnapshot() ), "the same park after the bubble's turns" );
		// a turn no phase reaches is still refused
		var later = snapshot with { Guests = snapshot.Guests.Select( item => item with { BubbleTurn = ParkCalendar.Turn( snapshot.TickCount ) + 2 } ).ToArray() };
		Assert.ThrowsException<System.IO.InvalidDataException>( () => new GuestSimulation( GuestTests.CreateCross(), settings, 9 ).RestoreSnapshot( later ) );
	}
}
