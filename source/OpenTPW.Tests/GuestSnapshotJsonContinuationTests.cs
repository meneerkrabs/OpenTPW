using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// Park-resume tests of the guest lane through the actual file codec: the whole
/// <see cref="GuestSimulationSnapshot"/> travels <see cref="ParkResumeJson.Serialize"/> →
/// <see cref="ParkResumeJson.Deserialize"/> (IncludeFields, so the tuple fields of routes, lanes, limbo and bounce
/// tables must survive) before any restore, and the restored park then continues identically to the uninterrupted
/// one for a thousand per-tick-compared ticks. The limbo/bounce slot tables are additionally driven through a
/// corpus-style protocol script and a command-dispatch script, because the idle driver never exercises those
/// opcodes. No serializer options are invented here: every byte goes through <see cref="ParkResumeJson"/>.
/// </summary>
[TestClass]
public class GuestSnapshotJsonContinuationTests
{
	/// <summary>
	/// Applies the saving script's own state (<see cref="RideVM.CaptureSnapshot"/> through the file codec) to a fresh script of the same code: the limbo
	/// table, the heap, the clocks and the variables are script state now, not bridge state.
	/// </summary>
	private static void RestoreScriptState( RideVM script, RideVM saved )
	{
		var loaded = ParkResumeJson.Deserialize<RideVMSnapshot>( ParkResumeJson.Serialize( saved.CaptureSnapshot() ) );
		loaded.ValidateSnapshot();
		script.RestoreSnapshot( loaded );
	}

	/// <summary>Restores the script-side protocol variables a bridge snapshot describes (the script slice owns
	/// them; VAR_LETMEOFF may hold an unconsumed release the next host step performs).</summary>
	private static void RestoreProtocolVariables( GuestSimulationSnapshot snapshot, GuestSnapshotTests.Rig resumed, GuestSnapshotTests.Rig saved )
	{
		foreach ( var attraction in snapshot.Attractions )
		{
			var savedScript = saved.Rides.Single( ride => ride.Bridge.AttractionId == attraction.AttractionId ).Script;
			var script = resumed.Rides.Single( ride => ride.Bridge.AttractionId == attraction.AttractionId ).Script;
			Assert.AreEqual( attraction.LetMeOnWritten ? attraction.CalledGuest : 0, savedScript[RideVariables.VAR_LETMEON],
				$"attraction {attraction.AttractionId}: the snapshot's called-guest bookkeeping matches the live VAR_LETMEON" );
			script[RideVariables.VAR_LETMEON] = attraction.LetMeOnWritten ? attraction.CalledGuest : 0;
			script[RideVariables.VAR_LETMEOFF] = savedScript[RideVariables.VAR_LETMEOFF];
		}
	}

	/// <summary>
	/// The whole guest snapshot survives the actual codec and a fresh park continues identically for a thousand
	/// ticks: tuple route/lane/queue/limbo/bounce values are compared raw, the restored queue order and rider
	/// order match, and the per-tick canonical hash of the uninterrupted and the JSON-restored park never differs.
	/// </summary>
	[TestMethod]
	public void WholeSnapshotSurvivesParkResumeJsonAndContinuesIdenticallyForAThousandTicks()
	{
		var park = new GuestSnapshotTests.Rig();
		var tick = GuestSnapshotTests.RunUntil( park, rig => rig.Simulation.TickCount >= 4200, 0, 4201 );
		foreach ( var guest in park.Simulation.Guests.Where( guest => guest.State is GuestState.WalkingAround or GuestState.GoingToRide or GuestState.ExitingRide ).Take( 3 ) )
			guest.ExitLevel = 1;
		tick = GuestSnapshotTests.RunUntil( park, GuestSnapshotTests.RichMoment, tick, 9000 );
		Assert.IsTrue( GuestSnapshotTests.RichMoment( park ), $"the accelerated harness reached a rich snapshot moment (tick {tick})" );

		var snapshot = park.Simulation.CaptureSnapshot();
		var loaded = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( snapshot ) );

		// Raw tuple values: navigation routes and arrival lanes must come back as the same cells, not Item-pairs of zero.
		for ( var index = 0; index < snapshot.Guests.Length; index++ )
		{
			var expected = snapshot.Guests[index];
			var actual = loaded.Guests[index];
			Assert.AreEqual( expected.Id, actual.Id, $"guest {index}" );
			Assert.AreEqual( expected.Navigation.Cursor, actual.Navigation.Cursor, $"guest {index} cursor" );
			CollectionAssert.AreEqual( expected.Navigation.Route, actual.Navigation.Route, $"guest {index} route tuples" );
			CollectionAssert.AreEqual( expected.Navigation.AlsoOpen, actual.Navigation.AlsoOpen, $"guest {index} extra-open tuples" );
			if ( expected.Lane != null )
				CollectionAssert.AreEqual( expected.Lane, actual.Lane, $"guest {index} arrival-lane tuples" );
			Assert.AreEqual( expected.LaneIndex, actual.LaneIndex );
			Assert.AreEqual( expected.StateTimer, actual.StateTimer );
			Assert.AreEqual( expected.Boost, actual.Boost );
		}
		Assert.IsTrue( loaded.Guests.All( guest => guest.Lane == null ), "no guest of the current schema walks an arrival lane" );
		Assert.IsTrue( loaded.Guests.Any( guest => guest.State is GuestState.CrossingRoad or GuestState.CrossingRoadToPark or GuestState.GoingToTicketBooth or GuestState.WaitingAtBooth
			or GuestState.AtTicketBooth or GuestState.EnteringPark ), "the snapshot holds guests of the arrival walk" );
		CollectionAssert.AreEqual( snapshot.SeenQueueEdits, loaded.SeenQueueEdits, "the seen queue-edit tuples survive the codec" );
		for ( var index = 0; index < snapshot.Attractions.Length; index++ )
		{
			var expected = snapshot.Attractions[index];
			var actual = loaded.Attractions[index];
			var bridge = park.Rides.Single( ride => ride.Bridge.AttractionId == expected.AttractionId ).Bridge;
			CollectionAssert.AreEqual( expected.Queue, actual.Queue, $"attraction {expected.AttractionId} queue order" );
			CollectionAssert.AreEqual( expected.QueueCells, actual.QueueCells, $"attraction {expected.AttractionId} queue-cell tuples" );
			Assert.AreEqual( expected.JoinCell, actual.JoinCell, $"attraction {expected.AttractionId} join cell" );
			CollectionAssert.AreEqual( expected.Limbo, actual.Limbo, $"attraction {expected.AttractionId} limbo tuples" );
			CollectionAssert.AreEqual( expected.Bounce, actual.Bounce, $"attraction {expected.AttractionId} bounce tuples" );
			CollectionAssert.AreEqual( bridge.Queue.ToArray(), actual.Queue, "the captured order is the live linked-list order" );
		}

		// A fresh park — new grid, bridges, scripts and simulation — restored from the file bytes, never from the live DTO.
		var resumed = new GuestSnapshotTests.Rig();
		resumed.Simulation.RestoreSnapshot( loaded );
		RestoreProtocolVariables( loaded, resumed, park );
		Assert.AreEqual( GuestSnapshotTests.CanonicalHash( park ), GuestSnapshotTests.CanonicalHash( resumed ), "the JSON-restored park hashes the same as the saved one" );
		Assert.AreNotSame( park.Simulation.Guests[0], resumed.Simulation.Guests[0], "the restore materialised fresh guest objects" );
		foreach ( var attraction in loaded.Attractions )
		{
			var bridge = resumed.Rides.Single( ride => ride.Bridge.AttractionId == attraction.AttractionId ).Bridge;
			CollectionAssert.AreEqual( attraction.Queue, bridge.Queue.ToArray(), "the restored queue keeps front-first order" );
			CollectionAssert.AreEqual( attraction.OnRide, bridge.Riders.ToArray(), "the restored riders keep boarding order" );
		}

		var lastTick = tick + 1000;
		for ( ; tick <= lastTick; tick++ )
		{
			park.Step();
			GuestSnapshotTests.Drive( park, tick );
			resumed.Step();
			GuestSnapshotTests.Drive( resumed, tick );
			Assert.AreEqual( GuestSnapshotTests.CanonicalHash( park ), GuestSnapshotTests.CanonicalHash( resumed ), $"the parks diverged at tick {tick} after the file roundtrip" );
		}
		Assert.AreEqual( park.Simulation.ComputeStateHash(), resumed.Simulation.ComputeStateHash() );
		Assert.AreEqual( park.Simulation.RandomState, resumed.Simulation.RandomState, "the explicit RNG state carried through the file" );
	}

	/// <summary>
	/// Saves written before the histories and the integer exit counter (schema 1) still restore: the histories are
	/// absent from the file and start empty, the seconds-based exit value is truncated toward zero into the counter,
	/// and a need grown past the old model's 150 cap migrates by clamping to the counter model's 100 ceiling. A
	/// current save (schema 2) that lost a history, carries a non-finite or fractional exit value, or a need past
	/// its cap, is rejected instead.
	/// </summary>
	[TestMethod]
	public void ASchemaOneSnapshotWithoutHistoriesStillRestoresWithTheFractionalExitValue()
	{
		var park = new GuestSnapshotTests.Rig();
		// early enough that no guest has left yet: a schema-1 save counts its guests, so their ids ascend (a later park reuses the slots of the guests that went home)
		GuestSnapshotTests.RunUntil( park, rig => rig.Simulation.TickCount >= 2400, 0, 2401 );
		var snapshot = park.Simulation.CaptureSnapshot();
		Assert.AreEqual( GuestSimulationSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion );
		Assert.IsTrue( snapshot.Guests.Any( guest => guest.UsedHistory is { Length: Guest.HistorySlots } ), "the current schema writes the histories" );

		// Downgrade to exactly what schema 1 wrote: no histories in the file, the exit value still the old seconds as a fraction,
		// and the old model's needs grown past the counter model's 100 ceiling (it capped them at 150).
		var schemaOne = snapshot with
		{
			SchemaVersion = 1,
			Guests = snapshot.Guests.Select( guest => guest with { UsedHistory = null, TemporaryHistory = null, ExitLevel = 37.8, Hunger = 140f, Thirst = 125.5f, Toilet = 101f } ).ToArray()
		};
		var loaded = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( schemaOne ) );
		Assert.IsNull( loaded.Guests[0].UsedHistory, "the codec keeps the histories absent" );

		var resumed = new GuestSnapshotTests.Rig();
		resumed.Simulation.RestoreSnapshot( loaded );
		var restored = resumed.Simulation.Guests.ToDictionary( guest => guest.Id );
		foreach ( var guest in loaded.Guests )
		{
			Assert.AreEqual( 37, restored[guest.Id].ExitLevel, $"guest {guest.Id}: the seconds fraction truncates toward zero into the counter" );
			Assert.AreEqual( 100f, restored[guest.Id].Hunger, $"guest {guest.Id}: a schema-1 need past the ceiling migrates by clamping" );
			Assert.AreEqual( 100f, restored[guest.Id].Thirst, $"guest {guest.Id}: a schema-1 need past the ceiling migrates by clamping" );
			Assert.AreEqual( 100f, restored[guest.Id].Toilet, $"guest {guest.Id}: a schema-1 need past the ceiling migrates by clamping" );
			CollectionAssert.AreEqual( new int[Guest.HistorySlots], restored[guest.Id].UsedHistory, $"guest {guest.Id}: the used history starts empty" );
			CollectionAssert.AreEqual( new int[Guest.HistorySlots], restored[guest.Id].TemporaryHistory, $"guest {guest.Id}: the temporary history starts empty" );
		}

		// A current save must be whole: losing a history or corrupting the exit value is rejected without touching the target.
		// (The corruptions land on the deserialized DTO — NaN never survives JSON, so the codec is out of the loop here.)
		var fromFile = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( snapshot ) );
		void Rejects( GuestSimulationSnapshot file, Func<GuestSnapshot, GuestSnapshot> corrupt, string message )
		{
			var fresh = new GuestSnapshotTests.Rig();
			var exception = Assert.ThrowsException<InvalidDataException>( () => fresh.Simulation.RestoreSnapshot( file with { Guests = file.Guests.Select( corrupt ).ToArray() } ) );
			StringAssert.Contains( exception.Message, message );
		}
		Rejects( fromFile, guest => guest with { UsedHistory = null }, "has no UsedHistory" );
		Rejects( fromFile, guest => guest with { TemporaryHistory = new[] { 1 } }, "TemporaryHistory" );
		Rejects( fromFile, guest => guest with { ExitLevel = double.NaN }, "does not fit" );
		Rejects( fromFile, guest => guest with { ExitLevel = 37.8 }, "is not a whole number of decisions" );
		Rejects( schemaOne, guest => guest with { Hunger = 150.5f }, "outside [0, 150]" );
	}

	/// <summary>
	/// A guest caught mid-walk up its queue (state 12, an original walk since WALK-006, moving) survives the codec and continues its walk identically: its search result's buffered waypoints and lengths are restored as
	/// saved. Queue walks are original walks too (WALK-006), so the harness's queues give such a walker early.
	/// </summary>
	[TestMethod]
	public void MidWalkNavigationSurvivesParkResumeJsonAndContinues()
	{
		var park = new GuestSnapshotTests.Rig();
		static bool MidWalk( Guest guest ) => guest.Navigation is { Original: true, Arrived: false, Count: > 0 } && guest.IsMoving && guest.State == GuestState.MovingUpQueue;
		var tick = GuestSnapshotTests.RunUntil( park, rig => rig.Simulation.Guests.Any( MidWalk ), 0, 6000 );
		var walker = park.Simulation.Guests.FirstOrDefault( MidWalk );
		Assert.IsNotNull( walker, $"the harness reached a mid-walk guest (tick {tick})" );

		var snapshot = park.Simulation.CaptureSnapshot();
		var loaded = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( snapshot ) );
		var saved = loaded.Guests.Single( guest => guest.Id == walker.Id );
		Assert.AreEqual( walker.Navigation.Index, saved.Navigation.Index, "the waypoint index survived the codec" );
		Assert.AreEqual( walker.Navigation.Count, saved.Navigation.Count, "the buffered count survived the codec" );
		Assert.IsNotNull( saved.Navigation.OriginalWalk, "the original walk's search state is saved" );
		Assert.AreEqual( walker.Navigation.PathCount, saved.Navigation.OriginalWalk!.PathCount );

		var resumed = new GuestSnapshotTests.Rig();
		resumed.Simulation.RestoreSnapshot( loaded );
		foreach ( var attraction in loaded.Attractions )
			resumed.Rides.Single( ride => ride.Bridge.AttractionId == attraction.AttractionId ).Script[RideVariables.VAR_LETMEON] =
				attraction.LetMeOnWritten ? attraction.CalledGuest : 0;
		var restoredWalker = resumed.Simulation.Find( walker.Id );
		Assert.IsNotNull( restoredWalker );
		Assert.AreNotSame( walker, restoredWalker );
		for ( var index = 0; index < saved.Navigation.Count; index++ )
			Assert.AreEqual( walker.Navigation.Buffer[index], restoredWalker.Navigation.Buffer[index], $"buffered waypoint {index}" );

		Assert.AreEqual( GuestSnapshotTests.CanonicalHash( park ), GuestSnapshotTests.CanonicalHash( resumed ) );
		var lastTick = tick + 400;
		for ( ; tick <= lastTick; tick++ )
		{
			park.Step();
			GuestSnapshotTests.Drive( park, tick );
			resumed.Step();
			GuestSnapshotTests.Drive( resumed, tick );
			Assert.AreEqual( GuestSnapshotTests.CanonicalHash( park ), GuestSnapshotTests.CanonicalHash( resumed ), $"the parks diverged at tick {tick} with a mid-walk walker restored" );
		}
	}

	/// <summary>A corpus-style protocol (Bee shop shape) that holds presented guests in LIMBO and BOUNCE instead of
	/// taking them straight off, releases expired ones through UNLIMBO/UNBOUNCE and hands walked-off guests back via
	/// WALKGET, so the bridge's timer tables are genuinely occupied at a snapshot moment.</summary>
	internal static RideVM LimboBounceScript( IRideScriptEffects effects )
	{
		var on = V( (int)RideVariables.VAR_LETMEON );
		var off = V( (int)RideVariables.VAR_LETMEOFF );
		var held = V( Array.IndexOf( GuestTests.ShopVariables, "VAR_PEEPID" ) );
		return new RideVM( new RideScriptFile( new MemoryStream( new Asm()
			.Label( "loop" )
			.I( Opcode.TEST, on )
			.I( Opcode.BRANCH_Z, "unlimbo" )
			.I( Opcode.LIMBOSPACE, held )
			.I( Opcode.BRANCH_Z, "bounce" )
			.I( Opcode.LIMBO, on, 40 )
			.I( Opcode.COPY, on, 0 )
			.I( Opcode.BRANCH, "unlimbo" )
			.Label( "bounce" )
			.I( Opcode.BOUNCE, on, 6 )
			.I( Opcode.COPY, on, 0 )
			.Label( "unlimbo" )
			.I( Opcode.UNLIMBO, held )
			.I( Opcode.BRANCH_Z, "unbounce" )
			.I( Opcode.WALKOFF, held )
			.Label( "unbounce" )
			.I( Opcode.UNBOUNCE, held )
			.I( Opcode.BRANCH_Z, "walkget" )
			.I( Opcode.COPY, off, held )
			.Label( "walkget" )
			.I( Opcode.WALKGET, held )
			.I( Opcode.BRANCH_Z, "idle" )
			.I( Opcode.COPY, off, held )
			.Label( "idle" )
			.I( Opcode.ENDSLICE )
			.I( Opcode.BRANCH, "loop" )
			.Build( GuestTests.ShopVariables ) ) ), new RideVMOptions { Effects = effects, Seed = 1 } );
	}

	/// <summary>
	/// A park whose rides hold guests in limbo and bounce slots survives the file codec and continues identically:
	/// the timer tables are non-empty at the save, they expire after the restore (releases and boardings move on),
	/// and a thousand per-tick hashes match the uninterrupted park.
	/// </summary>
	[TestMethod]
	public void LimboAndBounceOccupancySurvivesParkResumeJsonAndExpiresIdenticallyAfterRestore()
	{
		var park = new GuestSnapshotTests.Rig( LimboBounceScript );
		var tick = GuestSnapshotTests.RunUntil( park, rig => rig.Simulation.TickCount >= 4200, 0, 4201 );
		// Long limbo holds keep both tables' front filled; the 6 s bounce holds churn through the continuation.
		bool Occupied( GuestSnapshotTests.Rig rig ) => rig.Rides.All( ride => ride.Script.LimboCount > 0 )
			&& rig.Rides.Sum( ride => ride.Bridge.Riders.Count ) >= 3
			&& rig.Rides.Any( ride => ride.Bridge.CaptureSnapshot().Bounce.Any( slot => slot.Guest != 0 ) );
		// The original walk (WALK-004) reaches the three-way coincidence at tick 46,575 of this harness.
		tick = GuestSnapshotTests.RunUntil( park, Occupied, tick, 60000 );
		Assert.IsTrue( Occupied( park ), $"the harness reached rides with limbo and bounce occupancy (tick {tick})" );
		foreach ( var (bridge, script) in park.Rides )
			Assert.IsTrue( script.LimboCount > 0, $"{bridge.Name} holds limbo guests at the save" );

		var snapshot = park.Simulation.CaptureSnapshot();
		var loaded = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( snapshot ) );
		foreach ( var (_, script) in park.Rides )
			Assert.IsTrue( ParkResumeJson.Deserialize<RideVMSnapshot>( ParkResumeJson.Serialize( script.CaptureSnapshot() ) ).LimboValues!.Any( value => value != 0 ), "the script's limbo slots survived the codec occupied" );
		Assert.IsTrue( loaded.Attractions.Any( attraction => attraction.Bounce.Any( slot => slot.Guest != 0 ) ),
			"at least one ride's bounce slots survived the codec occupied" );

		// The resumed park's scripts must resume at the saving scripts' clocks: the deadlines are absolute milliseconds.
		var resumed = new GuestSnapshotTests.Rig( LimboBounceScript );
		foreach ( var (savedRide, resumedRide) in park.Rides.Zip( resumed.Rides ) )
			RestoreScriptState( resumedRide.Script, savedRide.Script );
		resumed.Simulation.RestoreSnapshot( loaded );
		RestoreProtocolVariables( loaded, resumed, park );
		Assert.AreEqual( GuestSnapshotTests.CanonicalHash( park ), GuestSnapshotTests.CanonicalHash( resumed ), "the limbo park restored from the file hashes the same" );

		var releasedBefore = resumed.Rides.Select( ride => ride.Bridge.ReleasedTotal ).ToArray();
		var lastTick = tick + 1000;
		for ( ; tick <= lastTick; tick++ )
		{
			park.Step();
			GuestSnapshotTests.Drive( park, tick );
			resumed.Step();
			GuestSnapshotTests.Drive( resumed, tick );
			Assert.AreEqual( GuestSnapshotTests.CanonicalHash( park ), GuestSnapshotTests.CanonicalHash( resumed ), $"the limbo parks diverged at tick {tick} after the file roundtrip" );
		}
		// The 6 s bounce deadlines fell due inside the continuation window and released identically.
		Assert.IsTrue( resumed.Rides.Any( ride => ride.Bridge.ReleasedTotal > releasedBefore[resumed.Rides.IndexOf( ride )] ),
			"a restored ride released held guests after the restore" );
		for ( var index = 0; index < resumed.Rides.Count; index++ )
		{
			Assert.AreEqual( park.Rides[index].Bridge.ReleasedTotal, resumed.Rides[index].Bridge.ReleasedTotal );
			Assert.AreEqual( park.Rides[index].Bridge.BoardedTotal, resumed.Rides[index].Bridge.BoardedTotal );
		}
		Assert.AreEqual( park.Simulation.ComputeStateHash(), resumed.Simulation.ComputeStateHash() );
	}

	// ---- Bridge-level limbo/bounce semantics through the codec --------------------------------

	/// <summary>Variables of the command-dispatch script: the RSE set plus a command register, an argument
	/// register, a duration register and a result scratch.</summary>
	private static readonly string[] DispatchVariables =
		Enum.GetNames<RideVariables>().Append( "VAR_PEEPID" ).Append( "VAR_CMD" ).Append( "VAR_ARG" ).Append( "VAR_DUR" ).Append( "VAR_RES" ).ToArray();
	private static readonly int CmdVar = Array.IndexOf( DispatchVariables, "VAR_CMD" );
	private static readonly int ArgVar = Array.IndexOf( DispatchVariables, "VAR_ARG" );
	private static readonly int DurVar = Array.IndexOf( DispatchVariables, "VAR_DUR" );
	private static readonly int ResVar = Array.IndexOf( DispatchVariables, "VAR_RES" );

	/// <summary>A one-command-per-tick dispatcher over the bridge's public effect opcodes: LIMBO/BOUNCE take the
	/// argument and duration registers, UNLIMBO/FORCEUNLIMBO/UNBOUNCE/FORCEUNBOUNCE/WALKGET write their guest into
	/// the argument register, command 8 is WALKOFF, and any other command value is WALKON (the walk table's entry that WALKOFF and WALKGET need, tp.exe 0x00556F40).</summary>
	private static RideVM DispatchScript( IRideScriptEffects effects )
	{
		var cmd = V( CmdVar );
		var arg = V( ArgVar );
		var dur = V( DurVar );
		var scratch = V( ResVar );
		return new RideVM( new RideScriptFile( new MemoryStream( new Asm()
			.Label( "loop" )
			.I( Opcode.TEST, cmd )
			.I( Opcode.BRANCH_Z, "idle" )
			.I( Opcode.SUB, scratch, cmd, 1 ).I( Opcode.TEST, scratch ).I( Opcode.BRANCH_Z, "c1" )
			.I( Opcode.SUB, scratch, cmd, 2 ).I( Opcode.TEST, scratch ).I( Opcode.BRANCH_Z, "c2" )
			.I( Opcode.SUB, scratch, cmd, 3 ).I( Opcode.TEST, scratch ).I( Opcode.BRANCH_Z, "c3" )
			.I( Opcode.SUB, scratch, cmd, 4 ).I( Opcode.TEST, scratch ).I( Opcode.BRANCH_Z, "c4" )
			.I( Opcode.SUB, scratch, cmd, 5 ).I( Opcode.TEST, scratch ).I( Opcode.BRANCH_Z, "c5" )
			.I( Opcode.SUB, scratch, cmd, 6 ).I( Opcode.TEST, scratch ).I( Opcode.BRANCH_Z, "c6" )
			.I( Opcode.SUB, scratch, cmd, 7 ).I( Opcode.TEST, scratch ).I( Opcode.BRANCH_Z, "c7" )
			.I( Opcode.SUB, scratch, cmd, 8 ).I( Opcode.TEST, scratch ).I( Opcode.BRANCH_Z, "c8" )
			.I( Opcode.BRANCH, "c9" )
			.Label( "c1" ).I( Opcode.LIMBO, arg, dur ).I( Opcode.COPY, cmd, 0 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" )
			.Label( "c2" ).I( Opcode.UNLIMBO, arg ).I( Opcode.COPY, cmd, 0 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" )
			.Label( "c3" ).I( Opcode.FORCEUNLIMBO, arg ).I( Opcode.COPY, cmd, 0 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" )
			.Label( "c4" ).I( Opcode.BOUNCE, arg, dur ).I( Opcode.COPY, cmd, 0 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" )
			.Label( "c5" ).I( Opcode.UNBOUNCE, arg ).I( Opcode.COPY, cmd, 0 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" )
			.Label( "c6" ).I( Opcode.FORCEUNBOUNCE, arg ).I( Opcode.COPY, cmd, 0 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" )
			.Label( "c7" ).I( Opcode.WALKGET, arg ).I( Opcode.COPY, cmd, 0 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" )
			.Label( "c8" ).I( Opcode.WALKOFF, arg ).I( Opcode.COPY, cmd, 0 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" )
			.Label( "c9" ).I( Opcode.WALKON, arg, 0, 0, 0, 0, 0, 0 ).I( Opcode.COPY, cmd, 0 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" )
			.Label( "idle" )
			.I( Opcode.ENDSLICE )
			.I( Opcode.BRANCH, "loop" )
			.Build( DispatchVariables ) ) ), new RideVMOptions { Effects = effects, Seed = 1, SliceBudget = 128 } );
	}

	/// <summary>Issues one command to a dispatch script and returns the argument register afterwards (the guest an
	/// output opcode wrote, or the input unchanged).</summary>
	private static int Command( RideVM script, int command, int argument, int duration = 0 )
	{
		script.Variables[ArgVar] = argument;
		script.Variables[DurVar] = duration;
		script.Variables[CmdVar] = command;
		script.Advance( GuestSnapshotTests.Tick );
		Assert.AreEqual( 0, script.Variables[CmdVar], "the dispatcher consumed the command in one slice" );
		return script.Variables[ArgVar];
	}

	private static void Idle( RideVM script, int ticks )
	{
		for ( var tick = 0; tick < ticks; tick++ )
			script.Advance( GuestSnapshotTests.Tick );
	}

	/// <summary>True when UNBOUNCE's 200 ms-of-every-second release window is open for a slot started at
	/// <paramref name="start"/> at time <paramref name="now"/> (32-bit whole milliseconds, C remainder).</summary>
	private static bool InUnbounceWindow( double start, double now ) =>
		unchecked( (int)(Math.Floor( now ) - Math.Floor( start )) ) % 1000 / 200 == 0;

	/// <summary>A bridge with a one-cell queue on a walkable grid, so its snapshots carry queue geometry.</summary>
	private static (RideVisitorBridge Bridge, RideVM Script) DispatchRig( int attractionId )
	{
		var grid = new GuestPathGrid( 4, 4 );
		grid.SetPath( 0, 0, true );
		var bridge = new RideVisitorBridge( attractionId, "Dispatch", RideVisitorKind.Ride, 10, 50, 30 ) { EntranceCell = (0, 0), ExitCell = (0, 0) };
		var script = DispatchScript( new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ) );
		bridge.Attach( script, () => true );
		bridge.RecomputeQueue( grid );
		return (bridge, script);
	}

	/// <summary>
	/// Limbo and bounce timers keep exact deadlines, expiry order, forcing and slot reuse through the codec: a live
	/// bridge and a bridge restored from JSON bytes of its own snapshot are driven with identical commands and must
	/// answer every one identically — unexpired holds refuse release, expired holds release in FIFO/index order,
	/// forcing takes what is held, freed interior slots keep their place and refill first, and trailing free slots
	/// are trimmed.
	/// </summary>
	[TestMethod]
	public void LimboAndBounceTimersKeepExactExpiryAndSlotOrderThroughParkResumeJson()
	{
		var (bridge, script) = DispatchRig( 31 );

		// Two limbo holds (2 s and 9 s) and two bounce holds (2 s and 9 s), one command (one tick) apart.
		Command( script, 1, 11, 2 );
		Command( script, 1, 12, 9 );
		Command( script, 4, 13, 2 );
		Command( script, 4, 14, 9 );
		// LIMBO is a pure script-table operation (TPW-EXE 0x554DE5): it boards nobody, only BOUNCE (a bridge opcode) does
		CollectionAssert.AreEqual( new[] { 13, 14 }, bridge.Riders.ToArray(), "BOUNCE boards its guests" );
		var captured = bridge.CaptureSnapshot();
		Assert.AreEqual( 0, captured.Limbo.Length, "the bridge keeps no limbo table" );
		var limboDeadline = (double)script.GetLimboSlot( 0 ).Expiry;
		var bounceStart = captured.Bounce[0].Start;
		var bounceDeadline = captured.Bounce[0].Until;
		Assert.AreEqual( 11, script.GetLimboSlot( 0 ).Value );
		Assert.AreEqual( 12, script.GetLimboSlot( 1 ).Value );
		Assert.AreEqual( 13, captured.Bounce[0].Guest );
		Assert.AreEqual( 14, captured.Bounce[1].Guest );
		Assert.IsTrue( limboDeadline - script.SchedulerMilliseconds is > 1900 and < 2100, $"LIMBO 2 s holds about two seconds (deadline {limboDeadline} at {script.SchedulerMilliseconds})" );
		Assert.IsTrue( bounceDeadline - bounceStart is > 1900 and < 2100, "BOUNCE 2 s holds about two seconds" );

		// The codec roundtrip, then a second bridge whose script resumes at the same clock (the script slice's job).
		var loaded = ParkResumeJson.Deserialize<RideVisitorBridgeSnapshot>( ParkResumeJson.Serialize( captured ) );
		var (restored, restoredScript) = DispatchRig( 31 );
		restored.RestoreSnapshot( loaded );
		RestoreScriptState( restoredScript, script );
		Assert.AreEqual( 11, restoredScript.GetLimboSlot( 0 ).Value, "the restored script holds the same limbo slots" );
		CollectionAssert.AreEqual( bridge.Queue.ToArray(), restored.Queue.ToArray() );
		CollectionAssert.AreEqual( bridge.Riders.ToArray(), restored.Riders.ToArray() );
		var restoredCaptured = restored.CaptureSnapshot();
		Assert.AreEqual( script.GetLimboSlot( 0 ), restoredScript.GetLimboSlot( 0 ), "the restored limbo deadlines match exactly" );
		Assert.AreEqual( script.GetLimboSlot( 1 ), restoredScript.GetLimboSlot( 1 ) );
		CollectionAssert.AreEqual( captured.Bounce, restoredCaptured.Bounce, "the restored bounce slot table matches exactly" );
		Assert.AreEqual( 0, RideVisitorBridge.ValidateSnapshot( restoredCaptured ).Count, "the restored bridge's own snapshot is valid" );

		// Before any deadline: neither expiry nor the release window returns a guest.
		Idle( script, 30 );
		Idle( restoredScript, 30 );
		Assert.AreEqual( 0, Command( script, 2, 0 ), "UNLIMBO holds a guest whose deadline has not passed" );
		Assert.AreEqual( 0, Command( restoredScript, 2, 0 ), "the restored bridge refuses the same release" );
		Assert.AreEqual( 0, Command( script, 5, 0 ), "UNBOUNCE holds a guest whose deadline has not passed" );
		Assert.AreEqual( 0, Command( restoredScript, 5, 0 ) );

		// Past the 2 s deadline, inside the release window: guest 11 (FIFO) and slot-0 guest 13 (index order) go.
		while ( !(Math.Floor( script.SchedulerMilliseconds ) > Math.Floor( limboDeadline ) + 16
			&& Math.Floor( script.TimeMilliseconds ) > Math.Floor( bounceDeadline )
			&& InUnbounceWindow( bounceStart, script.TimeMilliseconds )) )
		{
			Idle( script, 1 );
			Idle( restoredScript, 1 );
		}
		Assert.AreEqual( 11, Command( script, 2, 0 ), "UNLIMBO released the expired hold, front first" );
		Assert.AreEqual( 11, Command( restoredScript, 2, 0 ), "the restored bridge released the same hold" );
		Assert.AreEqual( 0, Command( script, 2, 0 ), "the 9 s limbo hold is still held" );
		Assert.AreEqual( 0, Command( restoredScript, 2, 0 ) );
		Assert.AreEqual( 13, Command( script, 5, 0 ), "UNBOUNCE released the expired slot in index order" );
		Assert.AreEqual( 13, Command( restoredScript, 5, 0 ) );
		Assert.AreEqual( 0, Command( script, 5, 0 ), "the 9 s bounce hold is still held" );
		Assert.AreEqual( 0, Command( restoredScript, 5, 0 ) );

		// Forcing takes what is held regardless of deadlines and windows.
		Assert.AreEqual( 12, Command( script, 3, 0 ), "FORCEUNLIMBO takes the remaining hold" );
		Assert.AreEqual( 12, Command( restoredScript, 3, 0 ) );
		Assert.AreEqual( 14, Command( script, 6, 0 ), "FORCEUNBOUNCE takes the remaining slot" );
		Assert.AreEqual( 14, Command( restoredScript, 6, 0 ) );

		// A boarded guest walks off: WALKOFF enqueues it (argument unchanged), WALKGET returns it FIFO. Guest 14 is still aboard from its BOUNCE (limbo boards nobody).
		Assert.AreEqual( 14, Command( script, 9, 14 ), "WALKON leaves the argument register unchanged" );
		Assert.AreEqual( 14, Command( restoredScript, 9, 14 ) );
		Assert.AreEqual( 14, Command( script, 8, 14 ), "WALKOFF leaves the argument register unchanged" );
		Assert.AreEqual( 14, Command( restoredScript, 8, 14 ) );
		// the walk update of the frame moves the finished walk off to state 4, the one WALKGET hands out (0x00557D80)
		bridge.WalkUpdate();
		restored.WalkUpdate();
		Assert.AreEqual( 14, Command( script, 7, 0 ), "WALKGET returned the walked-off guest" );
		Assert.AreEqual( 14, Command( restoredScript, 7, 0 ) );

		// Slot geometry through the codec: fill all three slots, free the first, and a new BOUNCE must refill the
		// freed interior slot (index order) while trailing free slots are trimmed away.
		foreach ( var guest in new[] { 21, 22, 23 } )
		{
			Command( script, 4, guest, 5 );
			Command( restoredScript, 4, guest, 5 );
		}
		Assert.AreEqual( 21, Command( script, 6, 0 ), "FORCEUNBOUNCE frees slot 0" );
		Assert.AreEqual( 21, Command( restoredScript, 6, 0 ) );
		var withHole = ParkResumeJson.Deserialize<RideVisitorBridgeSnapshot>( ParkResumeJson.Serialize( bridge.CaptureSnapshot() ) );
		Assert.AreEqual( 3, withHole.Bounce.Length, "the interior free slot keeps its place in the table" );
		Assert.AreEqual( 0, withHole.Bounce[0].Guest );
		Assert.AreEqual( 22, withHole.Bounce[1].Guest );
		Assert.AreEqual( 23, withHole.Bounce[2].Guest );
		Assert.AreNotEqual( 0, withHole.Bounce[^1].Guest, "no trailing free slot is saved" );
		Assert.AreEqual( 0, RideVisitorBridge.ValidateSnapshot( withHole ).Count, "a slot table with an interior hole is valid" );

		Command( script, 4, 24, 5 );
		Command( restoredScript, 4, 24, 5 );
		var refilled = ParkResumeJson.Deserialize<RideVisitorBridgeSnapshot>( ParkResumeJson.Serialize( bridge.CaptureSnapshot() ) );
		Assert.AreEqual( 24, refilled.Bounce[0].Guest, "a later BOUNCE fills the freed interior slot first" );
		Assert.AreEqual( 22, refilled.Bounce[1].Guest );
		Assert.AreEqual( 23, refilled.Bounce[2].Guest );
		var restoredRefilled = restored.CaptureSnapshot();
		for ( var slot = 0; slot < 3; slot++ )
		{
			Assert.AreEqual( refilled.Bounce[slot].Guest, restoredRefilled.Bounce[slot].Guest, $"bounce slot {slot} agrees" );
			Assert.AreEqual( refilled.Bounce[slot].Until, restoredRefilled.Bounce[slot].Until, $"bounce slot {slot} deadline agrees" );
		}
	}
}
