using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class GuestRideBubbleTests
{
	private static GuestSimulation Simulation()
	{
		var settings = GuestTests.CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		return new( GuestTests.CreateCross(), settings, 9 ) { ArrivalsEnabled = false };
	}
	private static void Think( GuestSimulation simulation, Guest guest, GuestThought thought ) =>
		typeof( GuestSimulation ).GetMethod( "Think", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( simulation, new object[] { guest, thought } );
	private static void Set( object target, string field, object value ) =>
		target.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );
	private static Level Bind( GuestSimulation simulation )
	{
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "<Guests>k__BackingField", simulation );
		level.BindGuestThoughtPresentation( simulation );
		return level;
	}
	private static PrototypeRide Ride( Level level )
	{
		var ride = (PrototypeRide)RuntimeHelpers.GetUninitializedObject( typeof( PrototypeRide ) );
		Set( level, "<PlacedRide>k__BackingField", ride );
		return ride;
	}

	[TestMethod]
	public void DeniedBubbleWorkPreservesExistingPresentationAndStillNotifiesThoughtObserver()
	{
		var simulation = Simulation();
		var guest = simulation.SpawnInPark( 6, 5 );
		var observer = new Thoughts();
		simulation.Observer = observer;
		Think( simulation, guest, GuestThought.Pleased );
		Assert.AreEqual( GuestThought.Pleased, guest.BubbleThought, "absent provider permits bubbles" );
		var stamp = guest.BubbleTurn;
		simulation.ThoughtBubblesAllowed = () => false;
		Think( simulation, guest, GuestThought.Hungry );
		Assert.AreEqual( GuestThought.Hungry, guest.Thought );
		Assert.AreEqual( (GuestThought.Pleased, stamp), (guest.BubbleThought, guest.BubbleTurn) );
		CollectionAssert.AreEqual( new[] { GuestThought.Pleased, GuestThought.Hungry }, observer.Seen );
		simulation.ThoughtBubblesAllowed = () => true;
		Think( simulation, guest, GuestThought.Hungry );
		Assert.AreEqual( GuestThought.None, guest.BubbleThought, "reenabling preserves the ordinary priority cooldown" );
		Assert.AreEqual( stamp, guest.BubbleTurn );
		Think( simulation, guest, GuestThought.Pleased );
		Assert.AreEqual( GuestThought.Pleased, guest.BubbleThought, "priority zero may replace immediately" );
	}

	[TestMethod]
	public void NormalNeedsTicksKeepHashesRandomnessVoicesAndThoughtsEqualAcrossPresentationGates()
	{
		var visible = Simulation(); var hidden = Simulation();
		for ( var i = 0; i < 16; i++ ) { visible.SpawnInPark( 6, 5 ); hidden.SpawnInPark( 6, 5 ); }
		var a = visible.Guests.Last(); var b = hidden.Guests.Last();
		Assert.AreEqual( 0, a.Id & 15, "exercise a thought-voice eligible identity" );
		hidden.ThoughtBubblesAllowed = () => false;
		var voicesA = new List<(int, uint)>(); var voicesB = new List<(int, uint)>();
		visible.Voiced += (guest, id) => voicesA.Add( (guest.Id, id) );
		hidden.Voiced += (guest, id) => voicesB.Add( (guest.Id, id) );
		var observerA = new Thoughts(); var observerB = new Thoughts();
		visible.Observer = observerA; hidden.Observer = observerB;
		var bubbleSeen = false;
		for ( var tick = 0; tick < 60 * 120; tick++ )
		{
			a.Hunger = b.Hunger = 0;
			a.Thirst = b.Thirst = 0;
			a.Toilet = b.Toilet = 100;
			visible.Tick( GuestSnapshotTests.Tick ); hidden.Tick( GuestSnapshotTests.Tick );
			bubbleSeen |= a.BubbleThought != b.BubbleThought;
			Assert.AreEqual( visible.RandomState, hidden.RandomState );
			Assert.AreEqual( visible.ComputeStateHash(), hidden.ComputeStateHash() );
			Assert.AreEqual( WorldStateHash.Compute( new() { Guests = visible } ), WorldStateHash.Compute( new() { Guests = hidden } ) );
		}
		Assert.IsTrue( bubbleSeen, "the test must exercise different bubble presentation" );
		Assert.IsTrue( observerA.Seen.Count > 0 );
		CollectionAssert.AreEqual( observerA.Seen, observerB.Seen );
		Assert.IsTrue( voicesA.Any( voice => voice.Item2 == GuestSimulation.ToiletThoughtVoiceEvent ), "actual needs-phase voice must be exercised" );
		CollectionAssert.AreEqual( voicesA, voicesB );
		Assert.AreEqual( a.Thought, b.Thought );
	}

	[TestMethod]
	public void ExistingBubblesExpireNormallyWhileTheGateIsDenied()
	{
		var simulation = Simulation(); var guest = simulation.SpawnInPark( 6, 5 );
		Think( simulation, guest, GuestThought.Pleased );
		Assert.AreEqual( GuestThought.Pleased, guest.BubbleThought );
		simulation.ThoughtBubblesAllowed = () => false;
		while ( simulation.ParkTurn <= GuestThoughts.BubbleTurns + 4 ) simulation.Tick( GuestSnapshotTests.Tick );
		Assert.AreEqual( GuestThought.None, guest.BubbleThought );
		Assert.AreEqual( 0L, guest.BubbleTurn, "denied thoughts do not refresh the bubble timestamp" );
	}

	[TestMethod]
	public void ActualRideEntryExitAndRetiredSimulationBindingAreLevelScoped()
	{
		var previous = Camera.Mode;
		try
		{
			Camera.Mode = new ParkCameraMode();
			var first = Simulation(); var second = Simulation();
			var level = Bind( first ); var other = Bind( second );
			var ride = Ride( level );
			var guest = first.SpawnInPark( 6, 5 );
			Assert.IsTrue( first.ThoughtBubblesAllowed!() );
			Assert.IsTrue( level.EnterRideView( ride ) );
			Assert.IsFalse( first.ThoughtBubblesAllowed!() );
			Think( first, guest, GuestThought.Pleased );
			Assert.AreEqual( GuestThought.Pleased, guest.Thought );
			Assert.AreEqual( GuestThought.None, guest.BubbleThought );
			Assert.IsTrue( second.ThoughtBubblesAllowed!(), "global camera changes do not gate another level" );
			Assert.IsFalse( level.EnterRideView( ride ), "failed repeated entry preserves ownership" );
			Assert.IsFalse( first.ThoughtBubblesAllowed!() );
			level.ExitRideView();
			Assert.IsTrue( first.ThoughtBubblesAllowed!() );
			Think( first, guest, GuestThought.Pleased );
			Assert.AreEqual( GuestThought.Pleased, guest.BubbleThought );
			var replacement = Simulation();
			Set( level, "<Guests>k__BackingField", replacement ); level.BindGuestThoughtPresentation( replacement );
			var before = ParkResumeJson.Serialize( replacement.CaptureSnapshot() );
			Assert.IsFalse( first.ThoughtBubblesAllowed!(), "retired delegates stop before consulting current camera state" );
			Assert.IsTrue( replacement.ThoughtBubblesAllowed!() );
			Assert.IsTrue( level.EnterRideView( ride ) );
			Assert.IsFalse( replacement.ThoughtBubblesAllowed!() );
			Assert.IsFalse( first.ThoughtBubblesAllowed!() );
			CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( replacement.CaptureSnapshot() ) );
			level.ExitRideView();
			Assert.IsTrue( second.ThoughtBubblesAllowed!() );
			GC.KeepAlive( other );
		}
		finally { Camera.Mode = previous; }
	}

	/// <summary>
	/// [BIN:TPW-EXE:0x0050BE8D] 0x50BE80 skips the bubble work while a bit of 0x16 of the camera word 0x790AB0 is set: the ride camera (4) and the walk camera (2), for every person's thought object, the staff's as well.
	/// </summary>
	[TestMethod]
	public void TheWalkCameraGatesTheGuestsAndTheStaffsBubblesAsTheRideViewDoes()
	{
		var simulation = Simulation();
		var level = Bind( simulation );
		var park = EconomyTestData.Park();
		var world = new StaffWorldSimulation( new GuestPathGrid( 12, 12 ), () => park.Staff.Members, _ => null, null );
		Set( level, "<StaffWorld>k__BackingField", world );
		level.BindStaffThoughtPresentation( world );
		var member = EconomyTestData.HireBest( park, StaffType.Handyman );
		var guest = simulation.SpawnInPark( 6, 5 );
		void StaffThink( int thought ) => typeof( StaffWorldSimulation ).GetMethod( "Think", BindingFlags.Instance | BindingFlags.NonPublic, new[] { typeof( StaffMember ), typeof( int ), typeof( long ) } )!
			.Invoke( world, new object[] { member, thought, 5L } );

		Assert.IsTrue( simulation.ThoughtBubblesAllowed!() && world.ThoughtBubblesAllowed!() );
		level.UseFreeWalkForTests( (FreeWalkCameraMode)RuntimeHelpers.GetUninitializedObject( typeof( FreeWalkCameraMode ) ) );
		Assert.IsFalse( simulation.ThoughtBubblesAllowed!(), "the walk camera is bit 2 of the camera word" );
		Assert.IsFalse( world.ThoughtBubblesAllowed!() );
		Think( simulation, guest, GuestThought.Pleased );
		Assert.AreEqual( (GuestThought.Pleased, GuestThought.None), (guest.Thought, guest.BubbleThought), "the thought is stored, no bubble starts" );
		StaffThink( StaffThoughts.Tired );
		Assert.AreEqual( (StaffThoughts.Tired, 0), (member.Thought, member.BubbleThought), "a staff member's thought follows the same gate" );
		level.UseFreeWalkForTests( null );
		Assert.IsTrue( simulation.ThoughtBubblesAllowed!() && world.ThoughtBubblesAllowed!() );
		StaffThink( StaffThoughts.Tired );
		Assert.AreEqual( StaffThoughts.Tired, member.BubbleThought );
		Think( simulation, guest, GuestThought.Pleased );
		Assert.AreEqual( GuestThought.Pleased, guest.BubbleThought );
	}

	[TestMethod]
	public void RestoreKeepsTheUnsavedBindingAndOnlineVisitsAloneStillShowBubbles()
	{
		var simulation = Simulation(); simulation.OnlineVisit = true;
		var guest = simulation.SpawnInPark( 6, 5 );
		Think( simulation, guest, GuestThought.Pleased );
		Assert.AreEqual( GuestThought.Pleased, guest.BubbleThought );
		var bytes = ParkResumeJson.Serialize( simulation.CaptureSnapshot() );
		var restored = Simulation(); var level = Bind( restored );
		var gate = restored.ThoughtBubblesAllowed;
		restored.RestoreSnapshot( ParkResumeJson.Deserialize<GuestSimulationSnapshot>( bytes ) );
		Assert.AreSame( gate, restored.ThoughtBubblesAllowed );
		var copy = restored.Guests.Single();
		Assert.AreEqual( (guest.Thought, guest.BubbleThought, guest.BubbleTurn), (copy.Thought, copy.BubbleThought, copy.BubbleTurn) );
		CollectionAssert.AreEqual( bytes, ParkResumeJson.Serialize( restored.CaptureSnapshot() ) );
		GC.KeepAlive( level );
	}

	private sealed class Thoughts : IGuestObserver
	{
		public readonly List<GuestThought> Seen = new();
		public void Thought( Guest guest, GuestThought thought ) => Seen.Add( thought );
		public void Admitted( Guest guest, int fee ) { }
		public void Spent( Guest guest, int amount, int attractionId ) { }
		public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) { }
		public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { }
		public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) { }
		public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns ) { }
		public void Decided( Guest guest, bool chose ) { }
		public void Leaving( Guest guest, DepartureCause cause ) { }
		public void Departed( Guest guest ) { }
		public void Recalled( Guest guest ) { }
		public void Restored( GuestSimulation simulation ) { }
		public void Ticked( GuestSimulation simulation, float deltaSeconds ) { }
	}
}
