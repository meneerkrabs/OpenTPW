using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class TypedQueueExitTests
{
	private sealed class Payments : IGuestPayments
	{
		public int AdmissionFee => 0;
		public bool Closed;
		public bool ParkClosed => Closed;
		public int Refusals;
		public int Purchases;
		public Action? OnRefusal;
		public bool TryPayAdmission( out int paid ) { paid = 0; return true; }
		public bool TryPayVisit( IRideVisitorBridge ride, long money, out int paid ) { Purchases++; paid = 0; return true; }
		public void WalkedAway( IRideVisitorBridge ride ) { Refusals++; OnRefusal?.Invoke(); }
	}

	private static QueueTests.Rig Rig( bool shop = false )
	{
		var rig = new QueueTests.Rig( capacity: 1, kind: shop ? RideVisitorKind.Shop : RideVisitorKind.Ride,
			parameters: new QueueParameters( true, false, false, 600, 60, 60, 1, 30 ) );
		rig.Ride.OwnerWord = ParkCellMap.CellWord( 2, 6 );
		rig.Grid.Cells.SetOwner( 2, 6, rig.Ride.OwnerWord );
		if ( shop ) rig.Ride.Sideshow = new SideshowTerms( 20, 900, 0 );
		return rig;
	}

	private static void Until( QueueTests.Rig rig, Func<bool> condition )
	{
		for ( var tick = 0; tick < 20000 && !condition(); tick++ ) rig.Step();
		Assert.IsTrue( condition(), "The real queue/ride update must reach the requested state." );
	}

	[TestMethod]
	public void PriceRefusalPreservesStandPositionAndExistingDismissalEffects()
	{
		var rig = Rig( shop: true );
		Guest guest;
		do
		{
			guest = rig.Send();
			// Send does not advance the simulation: these guests have never entered a queue.
			if ( (guest.Id & 7) != 0 ) guest.State = GuestState.Gone;
		} while ( (guest.Id & 7) != 0 );
		guest.Money = 10;
		var payments = new Payments();
		rig.Simulation.Payments = payments;
		(int X, int Y)? position = null;
		float happiness = 0;
		payments.OnRefusal = () => { position = (guest.Navigation.X, guest.Navigation.Y); happiness = guest.Happiness; };
		var voices = 0;
		rig.Simulation.Voiced += (person, sound) => { if (person == guest && sound == GuestSimulation.DismissalVoiceEvent) voices++; };
		Until( rig, () => payments.Refusals == 1 );
		Assert.IsNotNull( position );
		Assert.AreEqual( (2, 6), guest.Navigation.Cell );
		Assert.AreEqual( position.Value, (guest.Navigation.X, guest.Navigation.Y) );
		Assert.AreEqual( GuestState.DoingNothing, guest.State );
		Assert.AreEqual( GuestThought.Dissatisfied, guest.Thought );
		Assert.AreEqual( Math.Max( 0, happiness - rig.Simulation.Settings.MediumHappinessChange ), guest.Happiness, 0.001f );
		Assert.AreEqual( 1, voices );
		Assert.AreEqual( (0, 0, -1, 0), (payments.Purchases, rig.Ride.QueueLength, rig.Ride.GetQueuePosition( guest.Id ), guest.AttractionId) );
		Assert.AreEqual( 10L, guest.Money );
	}

	[DataTestMethod]
	[DataRow( "valid", true )]
	[DataRow( "zero-owner", false )]
	[DataRow( "wrong-owner", false )]
	[DataRow( "entrance-link", false )]
	[DataRow( "front-link", false )]
	[DataRow( "untyped", false )]
	public void CalledHeadUsesOwnedLinkedEntranceOrTheExistingFallback( string change, bool preserve )
	{
		var rig = Rig();
		var guest = rig.Send();
		Until( rig, () => guest.State == GuestState.WaitingToBoard );
		Assert.AreEqual( guest.Id, rig.Ride.CalledGuest );
		Assert.AreEqual( (2, 6), guest.Navigation.Cell );
		var position = (guest.Navigation.X, guest.Navigation.Y);
		var happiness = guest.Happiness;
		var thought = guest.Thought;
		var dismissed = rig.Ride.Dismissed;
		var voices = 0;
		rig.Simulation.Voiced += (person, sound) => { if (person == guest && sound == GuestSimulation.DismissalVoiceEvent) voices++; };
		switch ( change )
		{
			case "zero-owner": rig.Ride.OwnerWord = 0; break;
			case "wrong-owner": rig.Grid.Cells.SetOwner( 2, 6, (ushort)(rig.Ride.OwnerWord + 1) ); break;
			case "entrance-link": rig.Grid.Cells.SetLinks( 2, 6, 0 ); break;
			case "front-link": rig.Grid.Cells.SetLinks( 2, 5, 0 ); break;
			case "untyped": rig.Grid.Cells.SetType( 2, 6, ParkCellType.Empty ); break;
		}
		rig.Open = false;
		Until( rig, () => !guest.IsInQueue );
		Assert.AreEqual( preserve ? (2, 6) : (2, 5), guest.Navigation.Cell );
		if ( preserve ) Assert.AreEqual( position, (guest.Navigation.X, guest.Navigation.Y) );
		Assert.AreEqual( dismissed + 1, rig.Ride.Dismissed );
		Assert.AreEqual( 0, rig.Ride.QueueLength );
		Assert.AreEqual( 0, rig.Ride.CalledGuest );
		Assert.AreEqual( GuestState.DoingNothing, guest.State );
		Assert.AreEqual( thought, guest.Thought );
		Assert.AreEqual( Math.Max( 0, happiness - rig.Simulation.Settings.MediumHappinessChange ), guest.Happiness, 0.001f );
		Assert.AreEqual( 0, voices, "The ride-head callback retains its existing voice behavior." );
	}

	[TestMethod]
	public void PreservedEntranceCanDecideAndWalkAfterJsonResume()
	{
		var rig = Rig();
		// QueueTests.Rig only lays path row 0. Its inherited cross-park lanes end off that path,
		// so they are invalid and the no-gate fallback removes a leaving guest without walking.
		rig.Simulation.Settings.ArrivalLaneA = new[] { (0, 0), (1, 0), (2, 0), (3, 0), (4, 0) };
		var guest = rig.Send();
		Until( rig, () => guest.State == GuestState.WaitingToBoard );
		rig.Open = false;
		Until( rig, () => !guest.IsInQueue );
		Assert.AreEqual( (2, 6), guest.Navigation.Cell );
		var saved = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ) );
		var restored = Rig();
		restored.Simulation.Settings.ArrivalLaneA = rig.Simulation.Settings.ArrivalLaneA!.ToArray();
		restored.Open = false;
		restored.Ride.RestoreSnapshot( rig.Ride.CaptureSnapshot() );
		var validation = restored.Simulation.ValidateSnapshot( saved );
		Assert.IsTrue( validation.IsValid, string.Join( "; ", validation.Errors ) );
		restored.Simulation.RestoreSnapshot( saved );
		var script = ParkResumeJson.Deserialize<RideVMSnapshot>( ParkResumeJson.Serialize( rig.Script.CaptureSnapshot() ) );
		script.ValidateSnapshot();
		restored.Script.RestoreSnapshot( script );
		Assert.AreEqual( rig.Simulation.ComputeStateHash(), restored.Simulation.ComputeStateHash() );
		CollectionAssert.AreEqual( ParkResumeJson.Serialize( rig.Ride.CaptureSnapshot() ), ParkResumeJson.Serialize( restored.Ride.CaptureSnapshot() ) );
		var other = restored.Simulation.Guests.Single();
		Assert.AreEqual( (guest.Navigation.X, guest.Navigation.Y), (other.Navigation.X, other.Navigation.Y) );
		rig.Simulation.Payments = new Payments { Closed = true };
		restored.Simulation.Payments = new Payments { Closed = true };
		for ( var tick = 0; tick < 300 && guest.Navigation.Cell == (2, 6); tick++ )
		{
			rig.Step();
			restored.Step();
			Assert.AreNotEqual( GuestState.Gone, guest.State, "Continuation must walk, not use the no-gate removal fallback." );
			Assert.AreEqual( (guest.State, guest.Navigation.X, guest.Navigation.Y), (other.State, other.Navigation.X, other.Navigation.Y) );
			Assert.AreEqual( rig.Simulation.RandomState, restored.Simulation.RandomState );
			Assert.AreEqual( rig.Simulation.ComputeStateHash(), restored.Simulation.ComputeStateHash() );
			CollectionAssert.AreEqual( ParkResumeJson.Serialize( rig.Ride.CaptureSnapshot() ), ParkResumeJson.Serialize( restored.Ride.CaptureSnapshot() ) );
			CollectionAssert.AreEqual( ParkResumeJson.Serialize( rig.Script.CaptureSnapshot() ), ParkResumeJson.Serialize( restored.Script.CaptureSnapshot() ) );
		}
		Assert.AreNotEqual( (2, 6), guest.Navigation.Cell, "The next decision must actually leave the preserved entrance." );
	}
}
