using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The guests' cat_kids voices (tp.exe 0x4faa00 and its callers): the till, drinks and meals, the toilet thought, the pause, the vomit and a ride demolished under its rider.</summary>
[TestClass]
public class GuestVoiceTests
{
	private sealed class Setup
	{
		public readonly GuestPathGrid Grid = new( 12, 12 );
		public readonly GuestSimulation Simulation;
		public readonly List<(uint Event, int Guest)> Voices = new();

		public Setup()
		{
			for ( var x = 0; x < 10; x++ )
				Grid.SetPath( x, 0, true );
			Simulation = new GuestSimulation( Grid, GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
			Simulation.Voiced += ( guest, id ) => Voices.Add( (id, guest.Id) );
		}

		/// <summary>Spawns guests until one satisfies <paramref name="wanted"/> (the sequential thing ids make every residue come by).</summary>
		public Guest Spawn( System.Func<int, bool> wanted )
		{
			for ( var tries = 0; tries < 200; tries++ )
			{
				var guest = Simulation.SpawnInPark( 2, 0 );
				if ( wanted( guest.Id ) )
					return guest;
			}
			throw new System.InvalidOperationException( "no guest with the wanted id" );
		}

		public Guest Spawn() => Spawn( _ => true );

		public RideVisitorBridge Shop( int price, int thirst, int hunger, RideVisitorKind kind = RideVisitorKind.Shop )
		{
			var shop = new RideVisitorBridge( 5, "Shop", kind, 5, 0, 30, GuestNeeds.None, price ) { ThirstEffect = thirst, HungerEffect = hunger, EntranceCell = (2, 0), ExitCell = (2, 0) };
			Simulation.Register( shop );
			return shop;
		}

		public void Release( RideVisitorBridge ride, Guest guest ) => ( (IRideVisitorHost)Simulation ).OnVisitorReleased( ride, guest.Id );
	}

	[TestMethod]
	public void APaidVisitPlaysTheTillAndAFreeOneDoesNot()
	{
		var setup = new Setup();
		var paid = setup.Shop( 4, 0, 0 );
		var guest = setup.Spawn();
		setup.Release( paid, guest );
		CollectionAssert.AreEqual( new[] { GuestSimulation.TillVoiceEvent }, setup.Voices.Select( voice => voice.Event ).ToArray(), "0x4fe1a0 plays 0xD0 for a price that is not 0, whatever the guest's thing id" );
		var free = new RideVisitorBridge( 6, "Free", RideVisitorKind.Shop, 5, 0, 30, GuestNeeds.None, 0 ) { EntranceCell = (2, 0), ExitCell = (2, 0) };
		setup.Simulation.Register( free );
		setup.Voices.Clear();
		setup.Release( free, setup.Spawn() );
		Assert.AreEqual( 0, setup.Voices.Count );
	}

	[DataTestMethod]
	[DataRow( 30, 10, GuestSimulation.DrinkVoiceEvent )]
	[DataRow( 10, 30, GuestSimulation.MealVoiceEvent )]
	public void ADrinkOrAMealPlaysItsVoiceByTheLargerEffect( int thirst, int hunger, uint expected )
	{
		var setup = new Setup();
		var shop = setup.Shop( 0, thirst, hunger );
		setup.Release( shop, setup.Spawn() );
		CollectionAssert.AreEqual( new[] { expected }, setup.Voices.Select( voice => voice.Event ).ToArray() );
	}

	[TestMethod]
	public void EqualEffectsPlayNoDrinkOrMealVoice()
	{
		var setup = new Setup();
		var shop = setup.Shop( 0, 20, 20 );
		setup.Release( shop, setup.Spawn() );
		Assert.AreEqual( 0, setup.Voices.Count );
	}

	[TestMethod]
	public void ARideDemolishedUnderItsRiderPlaysTheRideRemovedVoiceForTheRiderOnly()
	{
		var setup = new Setup();
		var ride = setup.Shop( 0, 0, 0, RideVisitorKind.Ride );
		var rider = setup.Spawn();
		rider.AttractionId = ride.AttractionId;
		rider.State = GuestState.Using;
		var walker = setup.Spawn();
		walker.AttractionId = ride.AttractionId;
		walker.State = GuestState.GoingToRide;
		setup.Simulation.Unregister( ride );
		CollectionAssert.AreEqual( new[] { (GuestSimulation.RideRemovedVoiceEvent, rider.Id) }, setup.Voices, "0x4fb3f5: state 16 only, with no thing-id gate" );
	}

	[TestMethod]
	public void ThePauseVoiceIsGatedByTheThingIdsLowNibble()
	{
		var setup = new Setup();
		var start = typeof( GuestSimulation ).GetMethod( "StartActivity", BindingFlags.Instance | BindingFlags.NonPublic )!;
		var gated = new List<int>();
		for ( var count = 0; count < 40; count++ )
		{
			var guest = setup.Spawn();
			start.Invoke( setup.Simulation, new object[] { guest, GuestActivity.CantDecide } );
			if ( (guest.Id & 0xF) == 0 )
				gated.Add( guest.Id );
		}
		Assert.IsTrue( gated.Count >= 2, "the sequential ids reach a multiple of 16" );
		var cheering = setup.Spawn( id => (id & 0xF) == 0 );
		start.Invoke( setup.Simulation, new object[] { cheering, GuestActivity.Cheering } );
		CollectionAssert.AreEqual( gated.Select( id => (GuestSimulation.CantDecideVoiceEvent, id) ).ToArray(), setup.Voices.Where( voice => voice.Guest != cheering.Id ).ToArray(), "0x7E for activity 4 on ids with the low nibble clear only" );
		Assert.IsFalse( setup.Voices.Any( voice => voice.Guest == cheering.Id ), "cheering (activity 5) has no voice" );
	}

	[TestMethod]
	public void AVomitingGuestPlaysTheVomitVoiceAndAGuestNeedingTheToiletPlaysTheToiletVoiceOnGatedIdsOnly()
	{
		var rig = new QueueTests.Rig( queueCells: 0 );
		var voices = new List<(uint Event, int Guest)>();
		rig.Simulation.Voiced += ( guest, id ) => voices.Add( (id, guest.Id) );
		var sick = rig.Simulation.SpawnInPark( 2, 0 );
		sick.Nausea = 100;
		sick.Happiness = 50;
		var gated = new List<Guest>();
		var other = new List<Guest>();
		for ( var count = 0; count < 40; count++ )
		{
			var guest = rig.Simulation.SpawnInPark( 3, 0 );
			guest.Toilet = 100;
			guest.Happiness = 50;
			( (guest.Id & 0xF) == 0 ? gated : other ).Add( guest );
		}
		Assert.IsTrue( gated.Count >= 1 && other.Count >= 1 );
		rig.RunTurns( 600, () => { foreach ( var guest in gated.Concat( other ).Append( sick ) ) { guest.Toilet = guest == sick ? 0 : 100; if ( guest == sick ) guest.Nausea = 100; } } );
		Assert.IsTrue( voices.Any( voice => voice.Event == GuestSimulation.VomitVoiceEvent && voice.Guest == sick.Id ), "0xCC every time the guest vomits" );
		var toilet = voices.Where( voice => voice.Event == GuestSimulation.ToiletThoughtVoiceEvent ).Select( voice => voice.Guest ).Distinct().ToArray();
		Assert.IsTrue( toilet.Length > 0 && toilet.All( id => (id & 0xF) == 0 ), $"0x85 only for ids with the low nibble clear: {string.Join( ",", toilet )}" );
	}
}
