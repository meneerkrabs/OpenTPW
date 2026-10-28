using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>SAVE-923: the coaster train loop sound (tp.exe 0x4392A0, 0x439570, 0x439780, 0x43D5A6..0x43D7D8, 0x439670) with fake voices; nothing is played.</summary>
[TestClass]
public class CoasterTrainSoundTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private static readonly (int X, int Y) Depart = (20, 20);
	/// <summary>The EventMap variables of Jungle's coasters: two loops, then the parameter codes 17, 20, 19, 22 at positions 5 to 8.</summary>
	private static readonly Dictionary<int, int> EventMap = new() { [0] = 204, [1] = 216, [5] = 17, [6] = 20, [7] = 19, [8] = 22 };

	private sealed class Voice : ICoasterTrainVoice
	{
		public int Event;
		public readonly List<(int Code, int Value)> Parameters = new();
		public readonly List<Vector3> Places = new();
		public bool Stopped;
		public void MoveTo( Vector3 place ) => Places.Add( place );
		public void SetParameter( int code, int value ) => Parameters.Add( (code, value) );
		public void Stop() => Stopped = true;
	}

	private static (CoasterRide Ride, CoasterBuilder Builder, List<Voice> Voices, CoasterTrainSounds Sounds) NewRide()
	{
		var ride = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Test Coaster" );
		var voices = new List<Voice>();
		var sounds = new CoasterTrainSounds( ride, index => EventMap.TryGetValue( index, out var value ) ? value : 0, ( eventId, place ) =>
		{
			var voice = new Voice { Event = eventId };
			voice.Places.Add( place );
			voices.Add( voice );
			return voice;
		} );
		ride.TrainSounds = sounds;
		return (ride, new CoasterBuilder( ride, new CoasterSite( 64, 64 ) ), voices, sounds);
	}

	private static void Complete( CoasterBuilder builder )
	{
		CoasterTests.PlaceStation( builder, Depart );
		builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( Depart.X + dx, Depart.Y + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
	}

	[TestMethod]
	public void ASetUpTrainStartsTheTwoLoopsAndTakesStateTwoWithOneHundred()
	{
		var (ride, builder, voices, sounds) = NewRide();
		Complete( builder );
		Assert.AreEqual( ride.Trains.Count, sounds.SoundingTrains );
		// the rating pass places the train (slot setup) and the state change 1 → 2 sets every slot up again: two pairs, the first ended
		Assert.AreEqual( 4 * ride.Trains.Count, voices.Count, "events 0 and 1 of the EventMap, one pair per slot setup" );
		Assert.AreEqual( 204, voices[0].Event );
		Assert.AreEqual( 216, voices[1].Event );
		CollectionAssert.AreEqual( new[] { (19, 0x1E), (22, 100) }, voices[1].Parameters, "state 2 sends 0x1E on the state code (variable 7), then the occupancy 100 on variable 8; the first loop gets nothing" );
		Assert.AreEqual( 0, voices[0].Parameters.Count );
		Assert.IsTrue( voices[0].Stopped && voices[1].Stopped && !voices[2].Stopped, "the second setup ended the first pair (0x4392A0 starts with 0x439670)" );
		CollectionAssert.AreEqual( new[] { (22, 100) }, voices[3].Parameters, "the slot already held state 2, so the second pair's loop gets the occupancy only (0x43978A)" );
		Assert.AreEqual( 2u, ride.TrainSoundState( 0 ) );
	}

	[TestMethod]
	public void AnEqualStateStillSendsTheOccupancyAndTheFreshLoopOfAKnownStateGetsNoStateParameter()
	{
		var (ride, builder, voices, sounds) = NewRide();
		Complete( builder );
		var train = ride.Trains[0];
		voices.Clear();
		// the slot setup again (state 1 → 2 does this for every train): the slot already holds state 2, so the new second loop only gets the occupancy
		sounds.SetUp( train );
		Assert.AreEqual( 2, voices.Count );
		CollectionAssert.AreEqual( new[] { (22, 100) }, voices[1].Parameters, "0x43978A: an equal state skips the parameter and the store, 0x4397EE sends the occupancy anyway" );
		ride.SetTrainSoundState( 0, 0x10 );
		voices.Clear();
		sounds.SetUp( train );
		CollectionAssert.AreEqual( new[] { (19, 0x1E), (22, 100) }, voices[1].Parameters, "a slot that held another state sends the state parameter to the new loop" );
	}

	[TestMethod]
	public void TheLoopsCrossfadeBySpeedAndFollowTheSoundCarInWholeUnits()
	{
		var (ride, builder, voices, _) = NewRide();
		Complete( builder );
		for ( var tick = 0; tick < 240; tick++ )
			ride.Advance( Tick );
		voices = voices.Where( voice => !voice.Stopped ).ToList();
		var train = ride.Trains[0];
		var percent = (int)(train.Speed / ride.Definition.MaxSpeedAtMaxSetting * 100f);
		var first = voices[0].Parameters.Last();
		var second = voices[1].Parameters.Where( parameter => parameter.Code == 20 ).Last();
		Assert.AreEqual( (20, percent), first, "variable 6 on the first loop: the speed in percent of the fastest setting" );
		Assert.AreEqual( (20, 100 - percent), second, "and what is left to 100 on the second" );
		Assert.IsTrue( voices[0].Places.Count > 100 && voices[0].Places.Count == voices[1].Places.Count );
		foreach ( var place in voices[0].Places )
			Assert.IsTrue( place.X == MathF.Truncate( place.X ) && place.Y == MathF.Truncate( place.Y ) && place.Z == MathF.Truncate( place.Z ) );
		Assert.IsTrue( voices[0].Places.Skip( 1 ).Select( place => (place.X, place.Y, place.Z) ).Distinct().Count() > 5, "the sound car moves round the circuit" );
	}

	[TestMethod]
	public void EveryParameterTheTickSendsIsInTheTableAndTheValuesFollowTheStates()
	{
		var (ride, builder, voices, _) = NewRide();
		Complete( builder );
		var parameters = new HashSet<int>();
		var occupancy = new HashSet<int>();
		for ( var tick = 0; tick < 3000; tick++ )
			ride.Advance( Tick );
		foreach ( var (code, value) in voices.First( voice => voice.Event == 216 && !voice.Stopped ).Parameters )
		{
			if ( code == 19 )
				parameters.Add( value );
			if ( code == 22 )
				occupancy.Add( value );
		}
		Assert.IsTrue( parameters.IsSubsetOf( new[] { 0xF, 0x1E, 0x46, 0x5A, 0x32 } ), string.Join( ",", parameters ) );
		Assert.IsTrue( occupancy.IsSubsetOf( new[] { 25, 50, 75, 100 } ), string.Join( ",", occupancy ) );
		Assert.IsTrue( parameters.Count >= 2, "a circuit has braking, free and lift stretches: " + string.Join( ",", parameters ) );
	}

	[TestMethod]
	public void RemovingTheTrainsEndsTheirSounds()
	{
		var (ride, builder, voices, sounds) = NewRide();
		Complete( builder );
		Assert.IsTrue( voices.Skip( 2 * ride.Trains.Count ).All( voice => !voice.Stopped ) );
		Assert.IsTrue( builder.Reopen() );
		Assert.AreEqual( 0, ride.Trains.Count );
		Assert.IsTrue( voices.All( voice => voice.Stopped ), "ClearTrains tears every slot down (0x439670)" );
		Assert.AreEqual( 0, sounds.SoundingTrains );
	}

	[TestMethod]
	public void TheSoundsNeverTouchWhatTheSimulationHashes()
	{
		var (silent, silentBuilder, _, _) = NewRide();
		silent.TrainSounds = null;
		var (loud, loudBuilder, _, _) = NewRide();
		Complete( silentBuilder );
		Complete( loudBuilder );
		for ( var tick = 0; tick < 1500; tick++ )
		{
			silent.Advance( Tick );
			loud.Advance( Tick );
		}
		var left = new StateHasher();
		silent.AddCanonicalState( left );
		var right = new StateHasher();
		loud.AddCanonicalState( right );
		Assert.AreEqual( left.Value, right.Value );
	}

	/// <summary>Runs the state pass on a train that has the given inputs and returns what the second loop got in that call.</summary>
	private static List<(int Code, int Value)> StateOf( CoasterRide ride, CoasterTrainSounds sounds, List<Voice> voices, Action<CoasterTrain> setup )
	{
		var train = ride.Trains[0];
		sounds.SetUp( train );
		var voice = voices[^1];
		voice.Parameters.Clear();
		ride.SetTrainSoundState( 0, 0 );
		train.BrakingNow = false;
		Array.Clear( train.OnWinch );
		setup( train );
		sounds.StateTick();
		return voice.Parameters;
	}

	[TestMethod]
	public void TheStatePassFollowsTheOriginalsBranches()
	{
		var (ride, builder, voices, sounds) = NewRide();
		Complete( builder );
		ride.SetTrainSoundState( 0, 0 );
		var cars = ride.Definition.CarCount;

		// braking: state 8 (0x5A) with 50 × (round(x⁴) + 1), the rounding to nearest even
		foreach ( var (factor, expected) in new[] { (0f, 50), (0.4f, 50), (0.5f, 50), (0.51f, 100), (1f, 100) } )
		{
			ride.SetTrainSoundState( 0, 0 );
			ride.BrakeFactor = factor;
			var sent = StateOf( ride, sounds, voices, train => train.BrakingNow = true );
			CollectionAssert.AreEqual( new[] { (19, 0x5A), (22, expected) }, sent, $"x⁴ = {factor}" );
		}

		// a car on a winch: state 2 up to 0.3 of the speed cap, state 4 above, with 100 × cars on winch / the train's cars
		ride.SetTrainSoundState( 0, 0 );
		var slow = StateOf( ride, sounds, voices, train => { train.OnWinch[0] = true; train.Speed = 0.29f * ride.SpeedCap; } );
		CollectionAssert.AreEqual( new[] { (19, 0x1E), (22, 100 / cars) }, slow );
		ride.SetTrainSoundState( 0, 0 );
		var fast = StateOf( ride, sounds, voices, train => { train.OnWinch[0] = train.OnWinch[1] = true; train.Speed = 0.5f * ride.SpeedCap; } );
		CollectionAssert.AreEqual( new[] { (19, 0x46), (22, 200 / cars) }, fast );

		// no braking and no winch: free, state 0x10 (0x32) with 100
		ride.SetTrainSoundState( 0, 0 );
		var free = StateOf( ride, sounds, voices, train => train.Speed = 20 );
		CollectionAssert.AreEqual( new[] { (19, 0x32), (22, 100) }, free );

		// the same state again sends only the occupancy
		Assert.AreEqual( 0x10u, ride.TrainSoundState( 0 ) );
		voices[^1].Parameters.Clear();
		sounds.StateTick();
		CollectionAssert.AreEqual( new[] { (22, 100) }, voices[^1].Parameters, "the same state again sends only the occupancy, not an early return" );
	}
}
