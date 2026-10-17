using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The park turn as the original runs it (tp.exe 0x00516380, DET-013): the turn counter advances, then every live thing is updated newest first by its type, then the gate retry, the turn's
/// deferred destroys and the arrival update, and only then the economy's calendar tail. The ticks between turns update nothing of that and free no thing.
/// </summary>
[TestClass]
public class ThingSweepTests
{
	/// <summary>A staff world that records the order of the members' updates and what the guests had done by then.</summary>
	private sealed class RecordingWorld( Func<IReadOnlyList<(int Id, double? Seconds)>> guests ) : IStaffWorld
	{
		public List<(int ThingId, long Turn, IReadOnlyList<(int Id, double? Seconds)> Guests)> Updates { get; } = new();
		public int Begins { get; private set; }
		public int Ends { get; private set; }
		public void UpdateStaff( ParkEconomy economy, long turn ) => throw new AssertFailedException( "a swept turn updates the members one by one" );
		public void BeginStaffTurn( ParkEconomy economy, long turn ) => Begins++;
		public void UpdateStaffMember( ParkEconomy economy, StaffMember member, long turn ) => Updates.Add( (member.ThingId, turn, guests()) );
		public void EndStaffTurn( ParkEconomy economy, long turn ) => Ends++;
	}

	private sealed record Scene( Level Level, ParkEconomy Economy, GuestSimulation Guests, ThingPool Pool, RecordingWorld World, StaffMember First, StaffMember Second, Guest Early, Guest Late );

	private static Scene CreateScene( StaffType firstType = StaffType.Handyman )
	{
		Log ??= new();
		var economy = EconomyTestData.Park();
		var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 1 );
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		guests.AttachThings( pool );
		economy.Staff.AttachThings( pool );
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		void Set( string field, object value ) => typeof( Level ).GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, value );
		Set( "<Park>k__BackingField", ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ) );
		Set( "<Guests>k__BackingField", guests );
		Set( "<Objects>k__BackingField", new ParkObjects( null!, new SandboxParkGrid() ) );
		Set( "<Seed>k__BackingField", WorldSeed.Default );
		Set( "economyInstances", new Dictionary<OriginalObject, int>() );
		Set( "simulationClock", new FixedStepClock() );
		Set( "things", pool );
		// built in this order, so the pool's list holds (newest first) the late guest, the second member, the early guest, the first member
		var first = EconomyTestData.HireBest( economy, firstType );
		var early = guests.SpawnInPark( 4, 5 );
		var second = EconomyTestData.HireBest( economy, StaffType.Guard );
		var late = guests.SpawnInPark( 15, 5 );
		var world = new RecordingWorld( () => new[] { early, late }.Select( guest => (guest.Id, guest.TimeInParkSeconds) ).ToArray() );
		economy.StaffWorld = world;
		return new Scene( level, economy, guests, pool, world, first, second, early, late );
	}

	/// <summary>Ticks to the one that starts the next park turn (not run) and returns the number of ticks run.</summary>
	private static int TicksToTurn( Scene scene )
	{
		var ticks = 0;
		while ( !(scene.Guests.NextTickStartsTurn && scene.Economy.NextTickStartsTurn) )
		{
			scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
			ticks++;
		}
		return ticks;
	}

	[TestMethod]
	public void ASweptTurnUpdatesTheThingsNewestFirstAcrossTheClasses()
	{
		var scene = CreateScene();
		Assert.IsTrue( scene.Late.Id > scene.Second.ThingId && scene.Second.ThingId > scene.Early.Id && scene.Early.Id > scene.First.ThingId, "the fixture built them in this order" );
		TicksToTurn( scene );
		var before = new[] { scene.Early, scene.Late }.ToDictionary( guest => guest.Id, guest => guest.TimeInParkSeconds );
		scene.World.Updates.Clear();
		scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
		Assert.AreEqual( 1, scene.World.Begins );
		Assert.AreEqual( 1, scene.World.Ends );
		// the second member comes after the late guest and before the early one: the late guest has been updated by then, the early guest has not
		var second = scene.World.Updates.Single( update => update.ThingId == scene.Second.ThingId );
		Assert.IsTrue( second.Guests.Single( guest => guest.Id == scene.Late.Id ).Seconds > before[scene.Late.Id], "the newer guest is updated before the member" );
		Assert.AreEqual( before[scene.Early.Id], second.Guests.Single( guest => guest.Id == scene.Early.Id ).Seconds, "the older guest is updated after the member" );
		var first = scene.World.Updates.Single( update => update.ThingId == scene.First.ThingId );
		Assert.IsTrue( first.Guests.Single( guest => guest.Id == scene.Early.Id ).Seconds > before[scene.Early.Id], "the oldest member comes last" );
		CollectionAssert.AreEqual( new[] { scene.Second.ThingId, scene.First.ThingId }, scene.World.Updates.Select( update => update.ThingId ).ToArray(), "newest first" );
		Assert.IsTrue( scene.World.Updates.All( update => update.Turn == scene.Economy.Turn ), "the members see the turn that has just begun" );
	}

	[TestMethod]
	public void TheTicksBetweenTurnsSweepNothingAndFreeNothing()
	{
		var scene = CreateScene();
		TicksToTurn( scene );
		scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
		scene.World.Updates.Clear();
		// a thing asked for its destruction during the turn waits for the next turn's end
		var extra = scene.Pool.Allocate( SaveThingModel.Attraction );
		scene.Pool.QueueDestroy( extra );
		var turn = scene.Economy.Turn;
		for ( var tick = 0; tick < 10; tick++ )
		{
			scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
			Assert.AreEqual( turn, scene.Economy.Turn, "no new turn yet" );
			Assert.IsTrue( scene.Pool.IsUsed( extra ), $"tick {tick}: the destroy request waits" );
			Assert.IsTrue( scene.Pool.IsQueued( extra ) );
		}
		Assert.AreEqual( 0, scene.World.Updates.Count, "no member is updated between turns" );
		TicksToTurn( scene );
		scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
		Assert.IsFalse( scene.Pool.IsUsed( extra ), "the next turn's end frees it" );
		Assert.AreEqual( 2, scene.World.Updates.Count );
	}

	[TestMethod]
	public void AThingThatAsksToGoDuringTheSweepIsNotUpdatedAgainThatTurn()
	{
		var scene = CreateScene();
		TicksToTurn( scene );
		// the member updated first (newer than the early guest) asks for the early guest's destruction: the sweep skips it
		var guestId = scene.Early.Id;
		scene.World.Updates.Clear();
		var seconds = scene.Early.TimeInParkSeconds;
		scene.Pool.QueueDestroy( guestId );
		scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
		Assert.AreEqual( seconds, scene.Early.TimeInParkSeconds, "a thing that has asked to be destroyed is not swept" );
	}

	[TestMethod]
	public void AResearcherResearchesOnEveryTwentiethTurnInTheSweep()
	{
		var scene = CreateScene( StaffType.Researcher );
		scene.Economy.StaffWorld = null;
		var current = scene.Economy.Research.Current( ResearchCategory.Ride )!;
		long Progress() => scene.Economy.Research.GetProgress( current );
		var start = Progress();
		long lastProgress = start;
		var researchTurns = new List<long>();
		for ( var tick = 0; tick < 62 * 15; tick++ )
		{
			scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
			if ( Progress() != lastProgress )
			{
				researchTurns.Add( scene.Economy.Turn );
				lastProgress = Progress();
			}
		}
		Assert.IsTrue( researchTurns.Count >= 2, "researched at least twice" );
		Assert.IsTrue( researchTurns.All( turn => turn % ParkResearch.TurnsPerResearch == 0 ), string.Join( ",", researchTurns ) );
	}

	[TestMethod]
	public void TheEconomyRunsItsOwnThingUpdateWithoutASweep()
	{
		var scene = CreateScene();
		var economy = scene.Economy;
		economy.StaffWorld = null;
		var swept = new List<(long Turn, bool Lost)>();
		economy.ThingSweep = new RecordingSweep( swept );
		EconomyTestData.AdvanceToTurn( economy, 3 );
		CollectionAssert.AreEqual( new[] { (1L, false), (2L, false), (3L, false) }, swept );
	}

	private sealed class RecordingSweep( List<(long Turn, bool Lost)> turns ) : IThingSweep
	{
		public void Run( ParkEconomy economy, long turn, bool lost ) => turns.Add( (turn, lost) );
	}

	[TestMethod]
	public void ASweptTickMustStartATurn()
	{
		var scene = CreateScene();
		Assert.IsFalse( scene.Guests.NextTickStartsTurn );
		Assert.ThrowsException<InvalidOperationException>( () => scene.Guests.BeginSweepTick( (float)FixedStepClock.TickDuration ) );
	}

	[TestMethod]
	public void AGuestThatLeavesIsFreedAtTheTurnsEndAndTheSweepVisitsEachGuestOnce()
	{
		var scene = CreateScene();
		TicksToTurn( scene );
		scene.Guests.BeginSweepTick( (float)FixedStepClock.TickDuration );
		Assert.IsTrue( scene.Guests.SweepGuest( scene.Late.Id ) );
		Assert.IsFalse( scene.Guests.SweepGuest( scene.Late.Id ), "a guest is swept once" );
		Assert.IsFalse( scene.Guests.SweepGuest( scene.Second.ThingId ), "a staff member is no guest" );
		scene.Early.State = GuestState.Gone;
		Assert.IsTrue( scene.Guests.SweepGuest( scene.Early.Id ) );
		Assert.IsTrue( scene.Pool.IsQueued( scene.Early.Id ), "the leaving guest asked for its destruction at once" );
		Assert.IsNull( scene.Guests.Find( scene.Early.Id ) );
		Assert.IsTrue( scene.Pool.IsUsed( scene.Early.Id ), "freed only at the turn's end" );
		scene.Guests.FinishSweep();
		scene.Pool.DestroyQueued();
		Assert.IsFalse( scene.Pool.IsUsed( scene.Early.Id ) );
	}
}
