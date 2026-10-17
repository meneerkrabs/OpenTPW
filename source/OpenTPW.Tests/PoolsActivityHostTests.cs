using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using static OpenTPW.Tests.PoolsSwimSessionTests;

namespace OpenTPW.Tests;

/// <summary>
/// The host of pool activities in the swim session (docs/POOLS.md, "Activities") proven with a test-only fake activity: offer, start, continue, back in the water, end of session, abort, a save in the middle,
/// the hash, and no draws and no pin change when the registry is empty or offers nothing.
/// </summary>
[TestClass]
public class PoolsActivityHostTests
{
	/// <summary>A fake activity: it offers the water cell (7, 5) (or a dry cell beside the pool), keeps the guest there for <see cref="Duration"/> turns and then ends as <see cref="Result"/> says.</summary>
	internal sealed class Fake : IPoolActivity
	{
		public string Id { get; init; } = "test.fake";
		public bool Available { get; set; } = true;
		public bool Dry { get; init; }
		public int Duration { get; init; } = 8;
		public PoolActivityStep Result { get; init; } = PoolActivityStep.BackInWater;
		public bool Decline { get; set; }
		public int Slot { get; init; } = PoolSwimSlots.Lying;
		public int StartPercent { get; init; }
		public int Offers, Starts, Updates, Aborts;

		public int SpriteSlot( Guest guest ) => Slot;
		public int StartOfferPercent => StartPercent;

		public PoolActivityOffer? Offer( GuestSimulation simulation, Guest guest, PoolSwimRegion region )
		{
			Offers++;
			return Available ? new PoolActivityOffer( 4242, Dry ? 4 : 7, 5 ) : null;
		}

		public bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random )
		{
			if ( Decline )
				return false;
			Starts++;
			var (x, y) = PoolSwimRegion.Centre( (offer.X, offer.Y) );
			simulation.PlacePoolGuest( guest, x, y );
			return true;
		}

		public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random )
		{
			Updates++;
			guest.PoolActivityPhase = 1;
			return ++guest.PoolActivityTurns >= Duration ? Result : PoolActivityStep.Continue;
		}

		public void Abort( GuestSimulation simulation, Guest guest ) => Aborts++;
	}

	private static (Rig Rig, Guest Guest, Fake Activity) Swimmer( Fake? activity = null, bool register = true )
	{
		var rig = new Rig();
		activity ??= new Fake();
		if ( register )
			rig.Simulation.RegisterPoolActivity( activity );
		var guest = rig.Send();
		return (rig, guest, activity);
	}

	private static ulong HashOf( Rig rig )
	{
		var hasher = new StateHasher();
		rig.Simulation.AddCanonicalState( hasher );
		return hasher.Value;
	}

	[TestMethod]
	public void ASwimmerIsOfferedStartsKeepsGoingAndComesBackInTheWater()
	{
		var (rig, guest, activity) = Swimmer();
		rig.Until( () => guest.State == GuestState.PoolActivity, 1500 );
		Assert.AreEqual( 1, activity.Starts );
		Assert.AreEqual( "test.fake", guest.PoolActivityId );
		Assert.AreEqual( 4242, guest.PoolActivityTarget );
		Assert.AreEqual( (7, 5), guest.Navigation.Cell, "the activity placed the guest" );
		Assert.IsTrue( guest.InSwimSession && guest.InPoolActivity && guest.IsVisible, "state 25 is part of the session and visible" );
		Assert.IsFalse( guest.IsSwimming );
		Assert.AreEqual( rig.Ride.AttractionId, guest.AttractionId );
		Assert.AreEqual( PoolSwimSlots.Lying, rig.Simulation.PoolActivityOf( guest )!.SpriteSlot( guest ) );
		Assert.AreEqual( 1, rig.Simulation.GetStatistics().Walking, "counted with the walkers" );
		var swimLeft = guest.SwimTurns;
		rig.Turns( 3 );
		Assert.AreEqual( GuestState.PoolActivity, guest.State, "it continues" );
		Assert.AreEqual( swimLeft, guest.SwimTurns, "the swim does not run out during an activity" );
		rig.Until( () => guest.State != GuestState.PoolActivity, 40 );
		Assert.AreEqual( GuestState.Swimming, guest.State, "back in the water" );
		Assert.IsNull( guest.PoolActivityId );
		Assert.AreEqual( (0, 0, 0, 0, 0), (guest.PoolActivityTarget, guest.PoolActivityPhase, guest.PoolActivityTurns, guest.PoolActivityX, guest.PoolActivityY) );
		Assert.IsTrue( rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ) );
		rig.Until( () => !guest.InSwimSession, 1500 );
		Assert.AreEqual( 0, guest.AttractionId, "the session still ends on the path" );
	}

	[TestMethod]
	public void AnActivityThatEndsTheSessionSendsTheGuestToClimbOutAndOffTheDeckIntoTheWater()
	{
		var (rig, guest, activity) = Swimmer( new Fake { Result = PoolActivityStep.EndSession, Dry = true } );
		rig.Until( () => guest.State == GuestState.PoolActivity, 1500 );
		Assert.IsFalse( rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ), "on the deck" );
		rig.Until( () => guest.State != GuestState.PoolActivity, 40 );
		Assert.AreEqual( GuestState.ClimbingOut, guest.State );
		Assert.IsTrue( rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ), "the host does not let a guest swim on dry land" );
		rig.Until( () => !guest.InSwimSession, 800 );
		Assert.AreEqual( 1, activity.Starts );
	}

	[TestMethod]
	public void ADecliningActivityLeavesTheSwimmerSwimming()
	{
		var (rig, guest, activity) = Swimmer( new Fake { Decline = true } );
		rig.Until( () => activity.Offers > 5, 200 );
		rig.Turns( 100 );
		Assert.AreEqual( 0, activity.Starts );
		Assert.IsTrue( guest.State is GuestState.Swimming or GuestState.ClimbingOut or GuestState.ChangingOut or GuestState.ExitingRide or GuestState.DoingNothing );
		Assert.IsNull( guest.PoolActivityId );
	}

	[TestMethod]
	public void AShowerLikeActivityIsOfferedAtTheStartOfTheSessionOnly()
	{
		var (rig, guest, activity) = Swimmer( new Fake { StartPercent = 100, Duration = 5 } );
		rig.Until( () => guest.InSwimSession, 400 );
		Assert.AreEqual( GuestState.PoolActivity, guest.State, "100 percent: it showers as the swim starts" );
		rig.Until( () => guest.State == GuestState.Swimming, 30 );
		rig.Turns( 150 );
		Assert.AreEqual( 1, activity.Starts, "and is not offered again during the swim" );
	}

	[TestMethod]
	public void NoRegistryOrNothingToOfferDrawsNothingAndKeepsEveryHash()
	{
		// the same run three ways: no registry, an activity that offers nothing, and an activity that offers nothing at every turn
		var empty = Swimmer( register: false );
		var nothing = Swimmer( new Fake { Available = false } );
		var hashes = new List<(ulong Empty, ulong Nothing)>();
		var randoms = new List<(uint Empty, uint Nothing)>();
		for ( var turn = 0; turn < 700; turn++ )
		{
			empty.Rig.Turns( 1 );
			nothing.Rig.Turns( 1 );
			hashes.Add( (HashOf( empty.Rig ), HashOf( nothing.Rig )) );
			randoms.Add( (empty.Guest.SwimRandom, nothing.Guest.SwimRandom) );
		}
		Assert.IsTrue( hashes.All( pair => pair.Empty == pair.Nothing ), "an activity that offers nothing changes no hash" );
		Assert.IsTrue( randoms.All( pair => pair.Empty == pair.Nothing ), "and draws nothing from the swimmer's stream" );
		Assert.IsTrue( nothing.Activity.Offers > 100, "it was asked each turn of the swim" );
		Assert.AreEqual( 0, nothing.Activity.Starts );
	}

	[TestMethod]
	public void AnActivityJoinsTheHashOnlyWhileAGuestDoesIt()
	{
		var (rig, guest, _) = Swimmer();
		rig.Until( () => guest.State == GuestState.PoolActivity, 1500 );
		var doing = HashOf( rig );
		var phase = guest.PoolActivityPhase;
		guest.PoolActivityPhase = 7;
		Assert.AreNotEqual( doing, HashOf( rig ), "the phase is part of the digest" );
		guest.PoolActivityPhase = phase;
		Assert.AreEqual( doing, HashOf( rig ) );
		rig.Until( () => guest.State == GuestState.Swimming, 40 );
		var swimming = HashOf( rig );
		guest.PoolActivityPhase = 3;
		Assert.AreEqual( swimming, HashOf( rig ), "outside state 25 the fields are not hashed" );
	}

	[TestMethod]
	public void ARunWithTheSameRegistryIsRepeatable()
	{
		ulong Run()
		{
			var (rig, guest, activity) = Swimmer();
			rig.Turns( 700 );
			return HashOf( rig ) ^ (ulong)activity.Starts ^ guest.SwimRandom;
		}
		Assert.AreEqual( Run(), Run() );
	}

	[TestMethod]
	public void AnActivityIsAbortedWhenTheRoomIsDemolishedThePoolGoesTheParkClosesOrTheTargetIsErased()
	{
		foreach ( var how in new[] { "demolish", "pool erased", "park closed", "target erased" } )
		{
			var (rig, guest, activity) = Swimmer( new Fake { Duration = 400 } );
			var payments = new QueueTests.ClosedPayments();
			rig.Simulation.Payments = payments;
			rig.Until( () => guest.State == GuestState.PoolActivity, 1500 );
			switch ( how )
			{
				case "demolish": rig.Simulation.Unregister( rig.Ride ); break;
				case "pool erased": rig.Cells.SetType( 5, 3, (byte)0 ); break;
				case "park closed": payments.Closed = true; break;
				default: rig.Simulation.AbortPoolActivitiesOf( 4242 ); break;
			}
			rig.Turns( 2 );
			Assert.AreEqual( 1, activity.Aborts, how );
			Assert.IsFalse( guest.InPoolActivity, how );
			Assert.IsNull( guest.PoolActivityId, how );
			Assert.AreEqual( 0, guest.PoolActivityTurns, how );
			rig.Turns( 30 );
			Assert.IsFalse( guest.InSwimSession, $"{how}: the session is over" );
			Assert.AreEqual( 0, guest.AttractionId, how );
		}
	}

	[TestMethod]
	public void AnUnknownActivityIdLetsTheGuestLeaveTheWater()
	{
		var (rig, guest, _) = Swimmer();
		rig.Until( () => guest.State == GuestState.PoolActivity, 1500 );
		rig.Simulation.ClearPoolActivities();
		rig.Turns( 2 );
		Assert.IsFalse( guest.InPoolActivity );
		rig.Turns( 30 );
		Assert.IsFalse( guest.InSwimSession );
	}

	[TestMethod]
	public void ASaveInTheMiddleOfAnActivityRestoresAndContinuesWithTheSameHash()
	{
		var (rig, guest, _) = Swimmer( new Fake { Duration = 30 } );
		rig.Until( () => guest.State == GuestState.PoolActivity && guest.PoolActivityTurns >= 4, 1500 );
		var json = System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ) );
		Assert.IsTrue( json.Contains( "PoolActivityId", StringComparison.Ordinal ) );
		var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( System.Text.Encoding.UTF8.GetBytes( json ) );
		Assert.AreEqual( json, System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( snapshot ) ), "the snapshot round-trips as JSON" );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( snapshot ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( snapshot ).Errors ) );
		var copy = new Rig();
		copy.Simulation.RegisterPoolActivity( new Fake { Duration = 30 } );
		copy.Simulation.RestoreSnapshot( snapshot );
		Assert.AreEqual( HashOf( rig ), HashOf( copy ), "the restored simulation hashes as the original" );
		var twin = copy.Simulation.Guests.Single();
		Assert.AreEqual( (guest.State, guest.PoolActivityId, guest.PoolActivityTarget, guest.PoolActivityPhase, guest.PoolActivityTurns, guest.PoolActivityX, guest.PoolActivityY),
			(twin.State, twin.PoolActivityId, twin.PoolActivityTarget, twin.PoolActivityPhase, twin.PoolActivityTurns, twin.PoolActivityX, twin.PoolActivityY) );
		for ( var tick = 0; tick < 40 * 60 && (guest.InPoolActivity || twin.InPoolActivity); tick++ )
		{
			rig.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			copy.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			Assert.AreEqual( (guest.State, guest.PoolActivityTurns, guest.Navigation.X, guest.Navigation.Y), (twin.State, twin.PoolActivityTurns, twin.Navigation.X, twin.Navigation.Y), $"tick {tick}" );
		}
		Assert.AreEqual( GuestState.Swimming, twin.State );
	}

	[TestMethod]
	public void TheValidatorRefusesAnActivityThatCannotBeRestored()
	{
		var (rig, guest, _) = Swimmer();
		rig.Until( () => guest.State == GuestState.PoolActivity, 1500 );
		var good = rig.Simulation.CaptureSnapshot();
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( good ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( good ).Errors ) );
		GuestSimulationSnapshot With( Func<GuestSnapshot, GuestSnapshot> edit ) => good with { Guests = good.Guests.Select( edit ).ToArray() };
		Assert.IsFalse( rig.Simulation.ValidateSnapshot( With( item => item with { PoolActivityId = null } ) ).IsValid, "state 25 without an id" );
		Assert.IsFalse( rig.Simulation.ValidateSnapshot( With( item => item with { PoolActivityTurns = -1 } ) ).IsValid, "negative turns" );
		Assert.IsFalse( rig.Simulation.ValidateSnapshot( With( item => item with { PoolActivityX = -5 << 16 } ) ).IsValid, "a point outside the map" );
		Assert.IsFalse( rig.Simulation.ValidateSnapshot( With( item => item with { AttractionId = 0 } ) ).IsValid, "no changing room" );
		Assert.IsFalse( rig.Simulation.ValidateSnapshot( With( item => item with { State = GuestState.Swimming } ) ).IsValid, "activity fields in the water" );
	}

	[TestMethod]
	public void AnOlderSnapshotWithoutActivityFieldsWritesNone()
	{
		var rig = new Rig();
		rig.Send();
		rig.Turns( 5 );
		var json = System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ) );
		Assert.IsFalse( json.Contains( "PoolActivity", StringComparison.Ordinal ), "absent outside state 25, so every older JSON equals this one's" );
	}
}

/// <summary>The ambient effects and the drawn height of activities (docs/POOLS.md, "Activities").</summary>
[TestClass]
public class PoolsActivityAmbientTests
{
	/// <summary>An ambient effect that counts its calls per guest and adds a happiness point of its own.</summary>
	private sealed class AmbientFake : IPoolActivity, IPoolAmbient
	{
		public string Id => "test.ambient";
		public readonly Dictionary<int, int> Calls = new();
		public readonly List<GuestState> States = new();
		public float? Height;
		public PoolActivityOffer? Offer( GuestSimulation simulation, Guest guest, PoolSwimRegion region ) => null;
		public bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random ) => false;
		public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random ) => PoolActivityStep.Continue;
		public void Abort( GuestSimulation simulation, Guest guest ) { }
		public float? FeetHeight( GuestSimulation simulation, Guest guest, float defaultHeight ) => Height is { } lift ? defaultHeight + lift : null;
		public void Ambient( GuestSimulation simulation, Guest guest, PoolSwimRegion region )
		{
			Calls[guest.Id] = Calls.GetValueOrDefault( guest.Id ) + 1;
			States.Add( guest.State );
		}
	}

	[TestMethod]
	public void AnAmbientEffectActsOncePerTurnOnFreeSwimmersOnly()
	{
		var rig = new Rig();
		var ambient = new AmbientFake();
		var fake = new PoolsActivityHostTests.Fake();
		rig.Simulation.RegisterPoolActivity( ambient );
		rig.Simulation.RegisterPoolActivity( fake );
		var guest = rig.Send();
		var swimTurns = 0;
		var activityTurns = 0;
		rig.Until( () =>
		{
			if ( guest.State == GuestState.Swimming )
				swimTurns++;
			if ( guest.State == GuestState.PoolActivity )
				activityTurns++;
			return !guest.InSwimSession && swimTurns > 0;
		}, 2500 );
		Assert.IsTrue( ambient.States.All( state => state == GuestState.Swimming ), "never while in an activity, climbing out or changing back" );
		Assert.IsTrue( ambient.Calls.GetValueOrDefault( guest.Id ) is var calls && calls > 100 && calls <= swimTurns, $"{calls} calls in {swimTurns} swimming turns (a turn that took an offer has none)" );
		Assert.IsTrue( activityTurns > 0, "and the swimmer did take an offer" );
	}

	[TestMethod]
	public void WithoutAmbientActivitiesNothingChanges()
	{
		ulong Run( bool withInertAmbient )
		{
			var rig = new Rig();
			if ( withInertAmbient )
				rig.Simulation.RegisterPoolActivity( new AmbientFake() );
			var guest = rig.Send();
			rig.Turns( 600 );
			var hasher = new StateHasher();
			rig.Simulation.AddCanonicalState( hasher );
			return hasher.Value ^ guest.SwimRandom;
		}
		Assert.AreEqual( Run( false ), Run( true ), "an effect that changes nothing changes no hash and draws nothing" );
	}

	[TestMethod]
	public void AnActivityCanSetTheDrawnHeightOfItsGuestOnly()
	{
		var rig = new Rig();
		var fake = new AmbientFake { Height = 6f };
		rig.Simulation.RegisterPoolActivity( fake );
		var guest = rig.Send();
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		Assert.AreEqual( 2.5f, rig.Simulation.PoolFeetHeight( guest, 2.5f ), "a free swimmer is drawn at the default height" );
		(guest.State, guest.PoolActivityId) = (GuestState.PoolActivity, "test.ambient");
		Assert.AreEqual( 8.5f, rig.Simulation.PoolFeetHeight( guest, 2.5f ), "in an activity the activity's height wins" );
		fake.Height = null;
		Assert.AreEqual( 2.5f, rig.Simulation.PoolFeetHeight( guest, 2.5f ), "null keeps the default" );
		(guest.State, guest.PoolActivityId) = (GuestState.Swimming, null);
	}
}
