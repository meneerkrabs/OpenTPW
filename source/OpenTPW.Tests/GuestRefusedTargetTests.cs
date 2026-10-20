using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>GUEST-004's bounded full-room refusal: a live target until a real major choice or explicit replacement/removal.</summary>
[TestClass]
public class GuestRefusedTargetTests
{
	private const int RideId = 1;
	private const int OtherRideId = 2;

	private static readonly (GuestState State, GuestActivity Activity)[] RetainedStates =
	{
		(GuestState.DoingNothing, GuestActivity.None),
		(GuestState.DoingNothing, GuestActivity.Cheering),
		(GuestState.DoingNothing, GuestActivity.Vomiting),
		(GuestState.WalkingAround, GuestActivity.None),
		(GuestState.LeavingPark, GuestActivity.None),
		(GuestState.CrossingRoadHome, GuestActivity.None),
		(GuestState.WalkingToVehicle, GuestActivity.None),
		(GuestState.WaitingToGoHome, GuestActivity.None)
	};

	private sealed class Recorder : IGuestObserver
	{
		public readonly List<(int Guest, QueueEndReason Reason)> Rejections = new();
		public readonly List<(int Guest, QueueEndReason Reason)> Ends = new();
		public readonly List<(int Guest, bool Chose)> Decisions = new();
		public readonly List<int> Recalls = new();
		public void Admitted( Guest guest, int fee ) { }
		public void Spent( Guest guest, int amount, int attractionId ) { }
		public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) => Rejections.Add( (guest.Id, reason) );
		public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { }
		public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) { }
		public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns ) => Ends.Add( (guest.Id, reason) );
		public void Decided( Guest guest, bool chose ) => Decisions.Add( (guest.Id, chose) );
		public void Leaving( Guest guest, DepartureCause cause ) { }
		public void Departed( Guest guest ) { }
		public void Recalled( Guest guest ) => Recalls.Add( guest.Id );
		public void Restored( GuestSimulation simulation ) { }
		public void Ticked( GuestSimulation simulation, float deltaSeconds ) { }
	}

	private static QueueTests.Rig NewRig( int queueCells = 0, QueueParameters? parameters = null, int excitement = 55 ) =>
		new( queueCells: queueCells, parameters: parameters, excitement: excitement, configure: settings =>
		{
			settings.Types = new() { new GuestType( 70, 300, 30 ) };
			settings.ExitLevelSeconds = 100000;
			settings.ExitLevelVarSeconds = 0;
		} ) { KeepNeedsEmpty = true };

	private static void Until( QueueTests.Rig rig, Func<bool> done, string message, int ticks = 6000 )
	{
		for ( var tick = 0; !done() && tick < ticks; tick++ )
			rig.Step();
		Assert.IsTrue( done(), message );
	}

	private static QueueTests.Rig FullQueue()
	{
		var rig = NewRig();
		rig.HoldAdmission();
		Assert.AreEqual( 4, rig.Ride.QueueRoom, "one real path-cell queue" );
		var queued = Enumerable.Range( 0, rig.Ride.QueueRoom ).Select( _ => rig.Send() ).ToArray();
		Until( rig, () => rig.Ride.QueueLength == queued.Length, "all four real guests joined" );
		Assert.IsTrue( queued.All( guest => guest.IsInQueue ) );
		Assert.IsTrue( rig.Ride.QueueLength < rig.Ride.QueueLimit, "the physical room, not the queue limit, refuses the next guest" );
		return rig;
	}

	private static QueueTests.Rig Refused( out Guest guest, out Recorder recorder )
	{
		var rig = FullQueue();
		recorder = new Recorder();
		rig.Simulation.Observer = recorder;
		var subject = rig.Send();
		Until( rig, () => subject.State != GuestState.GoingToRide, "the fifth real guest reached the full room" );
		Assert.AreEqual( GuestState.DoingNothing, subject.State );
		Assert.AreEqual( RideId, subject.AttractionId );
		CollectionAssert.AreEqual( new[] { (subject.Id, QueueEndReason.Full) }, recorder.Rejections.ToArray() );
		Valid( rig.Simulation, rig.Simulation.CaptureSnapshot() );
		guest = subject;
		return rig;
	}

	private static RideVisitorBridge AddAlternative( QueueTests.Rig rig, out RideVM script )
	{
		var ride = new RideVisitorBridge( OtherRideId, "Alternative", RideVisitorKind.Ride, 5, 70, 30 )
		{
			EntranceCell = (3, 0), ExitCell = (3, 0), QueueFrontCell = (3, 0)
		};
		script = GuestSnapshotTests.IdleScript( new VisitorRideScriptEffects( ride, UnimplementedRideScriptEffects.Instance ) );
		ride.Attach( script, () => true );
		rig.Simulation.Register( ride );
		script.Advance( GuestSnapshotTests.Tick );
		return ride;
	}

	private static ulong CanonicalHash( GuestSimulation simulation )
	{
		var hash = new StateHasher();
		simulation.AddCanonicalState( hash );
		return hash.Value;
	}

	private sealed record Observation( byte[] Guests, byte[] Script, ulong Canonical, ulong Legacy, ulong Random );
	private static Observation Observe( QueueTests.Rig rig ) => new(
		ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ), ParkResumeJson.Serialize( rig.Script.CaptureSnapshot() ),
		CanonicalHash( rig.Simulation ), rig.Simulation.ComputeStateHash(), rig.Simulation.RandomState );

	private static void Unchanged( QueueTests.Rig rig, Observation before, string message )
	{
		var after = Observe( rig );
		CollectionAssert.AreEqual( before.Guests, after.Guests, message + ": all guest, navigation and queue fields" );
		CollectionAssert.AreEqual( before.Script, after.Script, message + ": script protocol" );
		Assert.AreEqual( before.Canonical, after.Canonical, message + ": canonical hash" );
		Assert.AreEqual( before.Legacy, after.Legacy, message + ": legacy hash" );
		Assert.AreEqual( before.Random, after.Random, message + ": world generator" );
	}

	private static GuestSimulationSnapshot RoundTrip( GuestSimulationSnapshot snapshot ) =>
		ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( snapshot ) );

	private static GuestSimulationSnapshot WithGuest( GuestSimulationSnapshot snapshot, int id, Func<GuestSnapshot, GuestSnapshot> change ) =>
		snapshot with { Guests = snapshot.Guests.Select( guest => guest.Id == id ? change( guest ) : guest ).ToArray() };

	private static void Valid( GuestSimulation simulation, GuestSimulationSnapshot snapshot )
	{
		var validation = simulation.ValidateSnapshot( snapshot );
		Assert.IsTrue( validation.IsValid, string.Join( "; ", validation.Errors ) );
	}

	private static QueueTests.Rig Resume( QueueTests.Rig source, GuestSimulationSnapshot? snapshot = null )
	{
		var resumed = NewRig();
		var loaded = RoundTrip( snapshot ?? source.Simulation.CaptureSnapshot() );
		Valid( resumed.Simulation, loaded );
		resumed.Simulation.RestoreSnapshot( loaded );
		var script = ParkResumeJson.Deserialize<RideVMSnapshot>( ParkResumeJson.Serialize( source.Script.CaptureSnapshot() ) );
		script.ValidateSnapshot();
		resumed.Script.RestoreSnapshot( script );
		return resumed;
	}

	// Joint baseline: main 9b8943a9 (ExitRide + balloon presentation), observed on all desktop and browser modes.
	private static readonly IReadOnlyDictionary<ulong, ulong> JointMainProjectionPins = new Dictionary<ulong, ulong>
	{
		[0x0FAA0BA10A2E2B43UL] = 0x18440BAB5DB3D443UL,
		[0x29CE354FAB027ABFUL] = 0xF3F895E3EF2F969BUL,
		[0x23A31F409845D585UL] = 0x347F0BA1F0DF5DFBUL,
		[0x9429690D329115B6UL] = 0x925B87221BE9AF53UL,
		[0x7B6BD0C81ED66C35UL] = 0x8171CE2F7537C034UL,
		[0x6FDBB79D8B9377BBUL] = 0xC9F860B54E30EB11UL,
		[0x4F2841D3F85402FCUL] = 0x0C36671C5B99F0CDUL,
		[0x4098DC9D36A6D8CDUL] = 0xBCD73D82A2F5D0D4UL,
		[0xEB1C231F596B30D2UL] = 0x2C8B850C069307BBUL,
		[0x547522BF092EEC0CUL] = 0x0E6B528D92E00145UL,
		[0x48F18D1BED795051UL] = 0x2BCAC8B1DC03F064UL,
		[0xE88AD1079B2559A5UL] = 0x5CFE6494C7706870UL,
		[0xBD827362B8D9DC5CUL] = 0x1B5829884FE0659CUL,
		[0x905024CE0B91E577UL] = 0x1A54C3057DBEED2EUL,
		[0xF85EB64C10598DB6UL] = 0x995AD10275D35532UL,
		[0x3D741AAF6928D531UL] = 0x2FB7EFA85DBAD719UL,
		[0x5AC8D8A47AAC35D2UL] = 0x7539BB3BB9729317UL,
		[0x4AAC7DD831EFD113UL] = 0x3BAFF5A07D0A82F6UL,
		[0x812FCC248B729C28UL] = 0x071D0789F44BE723UL,
		[0xEF00F0D64A9077FEUL] = 0xD2AF869CDF9A7D3DUL,
	};

	internal static ulong VerifyRetainedTargetHash( GuestSimulation simulation, Func<ulong> compute, WorldStateSources? sources = null )
	{
		var actual = compute();
		var directBefore = DirectHash( simulation, sources );
		Assert.AreEqual( directBefore, actual, "direct and delegate hashes agree before projection" );
		var targets = string.Join( ";", simulation.Guests.Where( guest => guest.AttractionId != 0 ).Select( guest => $"{guest.Id}:{guest.AttractionId}:{guest.State}:{guest.Activity}:{guest.IsInQueue}" ) );
		var retained = simulation.Guests.Where( guest => guest.AttractionId != 0 && !guest.IsInQueue
			&& RetainedStates.Contains( (guest.State, guest.Activity) ) )
			.Select( guest => (Guest: guest, Target: guest.AttractionId) ).ToArray();
		foreach ( var pair in retained ) pair.Guest.AttractionId = 0;
		ulong projected, repeatedProjection;
		string projectedBeforeFinally, repeatedBeforeFinally;
		try
		{
			var directProjected = DirectHash( simulation, sources );
			projected = compute();
			Assert.AreEqual( directProjected, projected, $"direct and delegate hashes agree while targets are zeroed; direct=0x{directProjected:X16}; delegated=0x{projected:X16}" );
			projectedBeforeFinally = projected.ToString( "X16" );
			repeatedProjection = compute();
			repeatedBeforeFinally = repeatedProjection.ToString( "X16" );
			Assert.AreEqual( projectedBeforeFinally, repeatedBeforeFinally, $"target-only projection repeated inside try; actual=0x{actual:X16}; targets={targets}" );
		}
		finally { foreach ( var pair in retained ) pair.Guest.AttractionId = pair.Target; }
		Assert.AreEqual( actual, compute(), "diagnostic projection restores the actual hash" );
		Assert.AreEqual( actual, DirectHash( simulation, sources ), "direct hash after restoration equals the original delegate hash" );
		Assert.AreEqual( projectedBeforeFinally, projected.ToString( "X16" ), "the first projected scalar survives finally and restoration" );
		Assert.AreEqual( repeatedBeforeFinally, repeatedProjection.ToString( "X16" ), "the repeated projected scalar survives finally and restoration" );
		Assert.AreEqual( projected, repeatedProjection, "target-only projection is stable while the selected IDs remain cleared" );
		if ( JointMainProjectionPins.TryGetValue( actual, out var mainPin ) )
			Assert.AreEqual( mainPin, projected, "target-only projection retains the independently observed joint-main baseline" );
		Console.WriteLine( $"GUEST004_JOINT actual=0x{actual:X16} projected=0x{projected:X16} retained={retained.Length}" );
		return actual; // All existing pin assertions still compare the real, unmodified hash.
	}

	private static ulong DirectHash( GuestSimulation simulation, WorldStateSources? sources ) =>
		sources == null ? simulation.ComputeStateHash() : WorldStateHash.Compute( sources );

	[TestMethod]
	public void FixedRunDiffersFromTheLegacyPinOnlyByRetainedTargetIdentity()
	{
		var current = new DeterminismTests.SyntheticPark( WorldSeed.Default );
		var legacy = new DeterminismTests.SyntheticPark( WorldSeed.Default );
		var retainedTicks = 0;
		for ( var tick = 0; tick < DeterminismTests.PinnedRunTicks; tick++ )
		{
			current.Run( 1 );
			legacy.Run( 1 );
			// Test-only replay of the previous refusal's target clearing. Production hashing is unchanged.
			foreach ( var guest in legacy.Guests.Guests.Where( HasRetainedTarget ) ) guest.AttractionId = 0;
			var retained = current.Guests.Guests.Where( HasRetainedTarget ).Select( guest => (Guest: guest, Target: guest.AttractionId) ).ToArray();
			retainedTicks += retained.Length;
			var currentHash = Hash( current );
			foreach ( var pair in retained ) pair.Guest.AttractionId = 0;
			try
			{
				Assert.AreEqual( Hash( legacy ), Hash( current ), $"tick {tick}: all world fields except retained target identity" );
				Assert.AreEqual( legacy.Guests.RandomState, current.Guests.RandomState, $"tick {tick}: world RNG" );
			}
			finally { foreach ( var pair in retained ) pair.Guest.AttractionId = pair.Target; }
			Assert.AreEqual( currentHash, Hash( current ), "diagnostic projection must restore the entire current hash" );
		}
		Assert.IsTrue( retainedTicks > 0, "the fixed run must exercise full-room retained targets" );
		Assert.AreEqual( 0xF3F895E3EF2F969BUL, Hash( legacy ), $"legacy replay: 0x{Hash( legacy ):X16}" );
		static ulong Hash( DeterminismTests.SyntheticPark park ) => WorldStateHash.Compute( new WorldStateSources { Seed = park.Seed, Economy = park.Economy, Guests = park.Guests, Scripts = park.Scripts } );
		static bool HasRetainedTarget( Guest guest ) => guest.AttractionId != 0 && !guest.IsInQueue
			&& RetainedStates.Contains( (guest.State, guest.Activity) );
	}

	[TestMethod]
	public void FullRoomRetainsTargetWithoutThoughtHistoryStampOrHappinessChanges()
	{
		var rig = FullQueue();
		// State 10 can reach the back between park turns: isolate the refusal from periodic needs and history decay.
		while ( rig.Simulation.NextTickStartsTurn )
			rig.Step();
		var observer = new Recorder();
		rig.Simulation.Observer = observer;
		var guest = rig.Send();
		guest.Thought = guest.BubbleThought = GuestThought.Hungry;
		guest.QueueStandingSinceTurn = 17;
		guest.TemporaryHistory[0] = 27;
		guest.UsedHistory[0] = 28;
		rig.Step();
		Assert.AreEqual( GuestState.DoingNothing, guest.State );
		Assert.AreEqual( RideId, guest.AttractionId );
		Assert.AreEqual( GuestThought.Hungry, guest.Thought );
		Assert.AreEqual( GuestThought.Hungry, guest.BubbleThought );
		Assert.AreEqual( 60f, guest.Happiness );
		Assert.AreEqual( 17L, guest.QueueStandingSinceTurn );
		CollectionAssert.AreEqual( new[] { 27, 0, 0, 0 }, guest.TemporaryHistory );
		CollectionAssert.AreEqual( new[] { 28, 0, 0, 0 }, guest.UsedHistory );
		Assert.AreEqual( -1, guest.QueuePosition );
		Assert.AreEqual( -1L, guest.QueueJoinTurn );
		Assert.AreEqual( 4, rig.Ride.QueueLength );
		CollectionAssert.AreEqual( new[] { (guest.Id, QueueEndReason.Full) }, observer.Rejections.ToArray() );
		Assert.AreEqual( 0, observer.Ends.Count );
	}

	[TestMethod]
	public void OtherAdmissionRefusalsStillClearTheTarget()
	{
		foreach ( var (excitement, thought) in new[] { (130, GuestThought.Scared), (1, GuestThought.Bored) } )
		{
			var rig = NewRig( excitement: excitement );
			var guest = rig.Send();
			guest.QueueStandingSinceTurn = 17;
			rig.Step();
			Assert.AreEqual( GuestState.DoingNothing, guest.State );
			Assert.AreEqual( 0, guest.AttractionId );
			Assert.AreEqual( thought, guest.Thought );
			Assert.AreEqual( RideId, guest.TemporaryHistory[0] );
			Assert.AreEqual( 0L, guest.QueueStandingSinceTurn );
			Assert.AreEqual( 60f, guest.Happiness );
		}
		var limited = NewRig( queueCells: 5, parameters: new QueueParameters( true, false, false, 10, 60, 60, 1, 30 ) );
		limited.HoldAdmission();
		var fillers = Enumerable.Range( 0, (int)limited.Ride.QueueLimit ).Select( _ => limited.Send() ).ToArray();
		Until( limited, () => limited.Ride.QueueLength == fillers.Length, "real guests reached the queue limit" );
		Assert.IsTrue( limited.Ride.QueueLength < limited.Ride.QueueRoom );
		var refused = limited.Send();
		Until( limited, () => refused.State != GuestState.GoingToRide, "the limit refused the next real guest" );
		Assert.AreEqual( 0, refused.AttractionId );
		Assert.AreEqual( GuestThought.QueueTooLong, refused.Thought );
		Assert.AreEqual( RideId, refused.TemporaryHistory[0] );
		Valid( limited.Simulation, limited.Simulation.CaptureSnapshot() );
	}

	[TestMethod]
	public void PeekChoiceIsObservationalWithAndWithoutAReachableAlternative()
	{
		var rig = Refused( out var guest, out _ );
		var before = Observe( rig );
		Assert.IsNull( rig.Simulation.PeekChoice( guest ) );
		Unchanged( rig, before, "unsuccessful peek" );
		var alternative = AddAlternative( rig, out var script );
		before = Observe( rig );
		var scriptBefore = ParkResumeJson.Serialize( script.CaptureSnapshot() );
		Assert.AreSame( alternative, rig.Simulation.PeekChoice( guest ) );
		Unchanged( rig, before, "successful peek" );
		CollectionAssert.AreEqual( scriptBefore, ParkResumeJson.Serialize( script.CaptureSnapshot() ), "the candidate script is observational too" );
		Assert.AreEqual( RideId, guest.AttractionId );
	}

	[TestMethod]
	public void OnlyATrueMajorChoiceClearsOrReplacesTheRefusedTarget()
	{
		foreach ( var success in new[] { false, true } )
		{
			var rig = Refused( out var guest, out var observer );
			var alternative = success ? AddAlternative( rig, out _ ) : null;
			for ( var tick = 0; !observer.Decisions.Any( item => item.Guest == guest.Id ) && tick < 12000; tick++ )
			{
				Assert.AreEqual( RideId, guest.AttractionId, "no earlier idle, activity or wander update may clear it" );
				rig.Step();
			}
			CollectionAssert.AreEqual( new[] { (guest.Id, success) }, observer.Decisions.Where( item => item.Guest == guest.Id ).ToArray() );
			Assert.AreEqual( success ? OtherRideId : 0, guest.AttractionId );
			Assert.AreEqual( success ? GuestState.GoingToRide : GuestState.DoingNothing, guest.State );
			Assert.AreEqual( success ? GuestActivity.None : GuestActivity.CantDecide, guest.Activity );
			var happiness = guest.Happiness;
			rig.Simulation.Unregister( rig.Ride );
			Assert.AreEqual( happiness, guest.Happiness, "removal of the previous target no longer matches" );
			if ( alternative != null )
			{
				rig.Simulation.Unregister( alternative );
				Assert.AreEqual( happiness - 5, guest.Happiness, "the replacement target does match" );
			}
		}
	}

	[TestMethod]
	public void WatchingActivitiesAndGatedWanderingKeepTheRefusedTarget()
	{
		var rig = Refused( out var guest, out var observer );
		rig.Simulation.ShowCells = () => new[] { (3, 0) };
		rig.RunTurns( 40, () => Assert.AreEqual( RideId, guest.AttractionId ) );
		Assert.AreEqual( GuestState.DoingNothing, guest.State );
		Assert.AreEqual( 0, observer.Decisions.Count );
		foreach ( var activity in new[] { GuestActivity.Cheering, GuestActivity.Vomiting } )
		{
			guest.Activity = activity;
			guest.InterludeTurn = rig.Simulation.ParkTurn;
			rig.RunTurns( 11, () => Assert.AreEqual( RideId, guest.AttractionId ) );
			Assert.AreEqual( GuestActivity.None, guest.Activity );
		}
		rig.Simulation.ShowCells = null;
		var wandered = false;
		for ( var tick = 0; !wandered && tick < 1200; tick++ )
		{
			guest.QueueStandingSinceTurn = rig.Simulation.ParkTurn; // hold the major-choice gate, not the wander branch
			rig.Step();
			Assert.AreEqual( RideId, guest.AttractionId );
			wandered = guest.State == GuestState.WalkingAround;
		}
		Assert.IsTrue( wandered );
		Assert.AreEqual( 0, observer.Decisions.Count );
		Valid( rig.Simulation, rig.Simulation.CaptureSnapshot() );
	}

	[TestMethod]
	public void BinReplacementAndGuardCaptureEndTheOldTargetsRemovalEffect()
	{
		var noBin = Refused( out var carrying, out var observer );
		noBin.Simulation.Bins = () => Array.Empty<LitterBin>();
		noBin.Simulation.ShowCells = () => new[] { (3, 0) }; // stop before a later major choice
		carrying.Litter = 90;
		Until( noBin, () => carrying.Litter == 0, "the unsuccessful search dropped the litter" );
		Assert.AreEqual( RideId, carrying.AttractionId, "a bin search that finds nothing does not replace the target" );
		Assert.AreEqual( 0, carrying.BinId );
		Assert.AreEqual( 0, observer.Decisions.Count );

		var rig = Refused( out var guest, out _ );
		rig.Simulation.Bins = () => new[] { new LitterBin( 91, (3, 1), (3, 0) ) };
		guest.Litter = 90;
		Until( rig, () => guest.State == GuestState.ThrowingAwayLitter, "state 6 selected the reachable bin" );
		Assert.AreEqual( 91, guest.BinId );
		Assert.AreEqual( 0, guest.AttractionId );
		Valid( rig.Simulation, rig.Simulation.CaptureSnapshot() );
		var happiness = guest.Happiness;
		rig.Simulation.Unregister( rig.Ride );
		Assert.AreEqual( happiness, guest.Happiness );
		Assert.AreEqual( GuestState.ThrowingAwayLitter, guest.State );
		Assert.AreEqual( 91, guest.BinId );
		rig.Simulation.BinRemoved( 91 );
		Assert.AreEqual( happiness - 5, guest.Happiness );
		Assert.AreEqual( 0, guest.BinId );
		Assert.AreEqual( GuestState.DoingNothing, guest.State );

		rig = Refused( out guest, out _ );
		((IGuardGuests)rig.Simulation).Hold( guest.Id );
		Assert.AreEqual( GuestState.HeldByGuard, guest.State );
		Assert.AreEqual( 0, guest.AttractionId );
		Valid( rig.Simulation, rig.Simulation.CaptureSnapshot() );
		happiness = guest.Happiness;
		rig.Simulation.Unregister( rig.Ride );
		Assert.AreEqual( happiness, guest.Happiness );
		Assert.AreEqual( GuestState.HeldByGuard, guest.State );
	}

	[TestMethod]
	public void DepartureAndRecallRetainTheTargetUntilRemovalOrTheNextChoice()
	{
		foreach ( var recall in new[] { false, true } )
		{
			var rig = Refused( out var guest, out var observer );
			// A tiny all-path gate and four reachable stop cells, without arrival vehicles or generated arrivals.
			for ( var x = 0; x <= 1; x++ )
				for ( var y = 1; y <= 3; y++ )
					rig.Grid.SetPath( x, y, true );
			rig.Simulation.Settings.ArrivalLaneA = new[] { (0, 3), (1, 3), (1, 2), (1, 1), (1, 0) };
			rig.Simulation.Settings.ArrivalLaneB = null;
			guest.ExitLevel = 1;
			rig.Simulation.ShowCells = () => new[] { (3, 0) }; // wait in state 6 until the needs phase makes the exit counter zero
			Until( rig, () => guest.State == GuestState.LeavingPark, "state 6 started the actual departure" );
			rig.Simulation.ShowCells = null;
			Assert.AreEqual( RideId, guest.AttractionId );
			if ( recall )
			{
				guest.ExitLevel = 10000;
				rig.Simulation.ShowCells = () => new[] { (guest.CellX + 1, guest.CellY) };
				Until( rig, () => observer.Recalls.Contains( guest.Id ), "the departure was recalled on the next park turn" );
				Assert.AreEqual( GuestState.DoingNothing, guest.State );
				Assert.AreEqual( RideId, guest.AttractionId );
				Assert.AreEqual( 0, observer.Decisions.Count );
			}
			else
			{
				var seen = new HashSet<GuestState> { guest.State };
				for ( var tick = 0; guest.State != GuestState.WaitingToGoHome && tick < 6000; tick++ )
				{
					rig.Step();
					Assert.AreEqual( RideId, guest.AttractionId );
					if ( seen.Add( guest.State ) )
						Valid( rig.Simulation, RoundTrip( rig.Simulation.CaptureSnapshot() ) );
				}
				Assert.AreEqual( GuestState.WaitingToGoHome, guest.State );
				foreach ( var state in new[] { GuestState.LeavingPark, GuestState.CrossingRoadHome, GuestState.WalkingToVehicle, GuestState.WaitingToGoHome } )
					Assert.IsTrue( seen.Contains( state ), $"actual departure reached {state}" );
			}
			var happiness = guest.Happiness;
			rig.Simulation.Unregister( rig.Ride );
			Assert.AreEqual( happiness - 5, guest.Happiness );
			Assert.AreEqual( 0, guest.AttractionId );
			Assert.AreEqual( GuestState.DoingNothing, guest.State );
		}
	}

	[TestMethod]
	public void RemovingTheRetainedTargetResetsOnlyMatchedGuestsAndChargesTheSmallByteStep()
	{
		foreach ( var (state, activity) in RetainedStates )
		{
			var rig = Refused( out var guest, out var observer );
			rig.Simulation.Settings.SmallHappinessChange = 261; // low byte 5
			rig.Simulation.Settings.MediumHappinessChange = 273; // low byte 17
			AddAlternative( rig, out _ );
			var other = rig.Simulation.SpawnInPark( 8, 0 );
			other.AttractionId = OtherRideId;
			other.Activity = GuestActivity.Cheering;
			var otherBefore = ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot().Guests.Single( item => item.Id == other.Id ) );
			guest.State = state;
			guest.Activity = activity;
			guest.Happiness = activity == GuestActivity.Vomiting ? 3 : 60;
			var happiness = guest.Happiness;
			var position = (guest.CellX, guest.CellY, guest.X, guest.Y);
			var queued = rig.Simulation.Guests.Where( item => item.IsInQueue ).ToArray();
			var queuedHappiness = queued.Select( item => item.Happiness ).ToArray();
			rig.Simulation.Unregister( rig.Ride );
			Assert.AreEqual( Math.Max( 0, happiness - 5 ), guest.Happiness, $"{state}/{activity}: small step only, clamped" );
			Assert.AreEqual( GuestState.DoingNothing, guest.State );
			Assert.AreEqual( GuestActivity.None, guest.Activity );
			Assert.AreEqual( 0, guest.AttractionId );
			Assert.AreEqual( position, (guest.CellX, guest.CellY, guest.X, guest.Y), "a nonqueued target is not teleported" );
			Assert.IsFalse( observer.Ends.Any( item => item.Guest == guest.Id ), "the refused guest never joined" );
			for ( var index = 0; index < queued.Length; index++ )
			{
				Assert.AreEqual( queuedHappiness[index] - 17 - 5, queued[index].Happiness, "existing queued removal still charges both steps" );
				Assert.IsFalse( queued[index].IsInQueue );
				Assert.AreEqual( 0, queued[index].AttractionId );
				Assert.IsTrue( observer.Ends.Contains( (queued[index].Id, QueueEndReason.AttractionRemoved) ) );
			}
			CollectionAssert.AreEqual( otherBefore, ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot().Guests.Single( item => item.Id == other.Id ) ), "unrelated activity is untouched" );
			rig.Simulation.Unregister( rig.Ride );
			Assert.AreEqual( Math.Max( 0, happiness - 5 ), guest.Happiness, "a second removal does not charge again" );
		}
	}

	[TestMethod]
	public void RealRefusalRoundTripsAndContinuesWithBothHashesAndQueueMembership()
	{
		var original = Refused( out var guest, out _ );
		var resumed = Resume( original );
		Assert.AreNotSame( guest, resumed.Simulation.Find( guest.Id ) );
		Assert.AreEqual( RideId, resumed.Simulation.Find( guest.Id )!.AttractionId );
		for ( var tick = 0; tick < 600; tick++ )
		{
			Assert.AreEqual( CanonicalHash( original.Simulation ), CanonicalHash( resumed.Simulation ), $"canonical tick {tick}" );
			Assert.AreEqual( original.Simulation.ComputeStateHash(), resumed.Simulation.ComputeStateHash(), $"legacy tick {tick}" );
			Assert.AreEqual( original.Simulation.RandomState, resumed.Simulation.RandomState, $"random tick {tick}" );
			CollectionAssert.AreEqual( original.Ride.CaptureSnapshot().Queue, resumed.Ride.CaptureSnapshot().Queue );
			original.Step();
			resumed.Step();
		}
		Assert.AreEqual( CanonicalHash( original.Simulation ), CanonicalHash( resumed.Simulation ), "last continued tick" );
		Assert.AreEqual( original.Simulation.ComputeStateHash(), resumed.Simulation.ComputeStateHash(), "last continued tick" );
		Valid( resumed.Simulation, resumed.Simulation.CaptureSnapshot() );

		// Restore again at the refusal boundary, then remove before any new major choice.
		original = Refused( out guest, out _ );
		resumed = Resume( original );
		original.Simulation.Unregister( original.Ride );
		resumed.Simulation.Unregister( resumed.Ride );
		Assert.AreEqual( 55f, resumed.Simulation.Find( guest.Id )!.Happiness );
		Assert.AreEqual( CanonicalHash( original.Simulation ), CanonicalHash( resumed.Simulation ) );
		Assert.AreEqual( original.Simulation.ComputeStateHash(), resumed.Simulation.ComputeStateHash() );
	}

	[TestMethod]
	public void OwnFormatEnvelopeCodecPreservesEveryRetainedStateActivityPair()
	{
		var envelope = ParkResumeSaveFileTests.Envelope();
		var id = envelope.Guests.Guests.First( guest => guest.QueuePosition < 0 && guest.State != GuestState.Using ).Id;
		foreach ( var (state, activity) in RetainedStates )
		{
			var guests = WithGuest( envelope.Guests, id, guest => guest with { State = state, Activity = activity, AttractionId = RideId, BinId = 0 } );
			var loaded = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( envelope with { Guests = guests } ) );
			var subject = loaded.Guests.Guests.Single( guest => guest.Id == id );
			Assert.AreEqual( (state, activity, RideId), (subject.State, subject.Activity, subject.AttractionId) );
		}
	}

	[TestMethod]
	public void RefusalRoundTripsThroughEveryAllowedStateActivityPairAndStillMatchesRemoval()
	{
		var source = Refused( out var guest, out _ );
		var snapshot = source.Simulation.CaptureSnapshot();
		foreach ( var (state, activity) in RetainedStates )
		{
			var saved = WithGuest( snapshot, guest.Id, item => item with { State = state, Activity = activity } );
			var restored = Resume( source, saved );
			var subject = restored.Simulation.Find( guest.Id )!;
			Assert.AreEqual( (state, activity, RideId), (subject.State, subject.Activity, subject.AttractionId) );
			var before = subject.Happiness;
			restored.Simulation.Unregister( restored.Ride );
			Assert.AreEqual( before - 5, subject.Happiness );
			Assert.AreEqual( (GuestState.DoingNothing, GuestActivity.None, 0), (subject.State, subject.Activity, subject.AttractionId) );
			Valid( restored.Simulation, restored.Simulation.CaptureSnapshot() );
		}
	}

	[TestMethod]
	public void InvalidRetainedBindingsAreRejectedAtomicallyAfterTheFileCodec()
	{
		var rig = Refused( out var guest, out _ );
		var snapshot = rig.Simulation.CaptureSnapshot();
		var invalid = new List<GuestSimulationSnapshot>();
		foreach ( var state in new[] { GuestState.CrossingRoad, GuestState.CrossingRoadToPark, GuestState.GoingToTicketBooth,
			GuestState.WaitingAtBooth, GuestState.AtTicketBooth, GuestState.EnteringPark, GuestState.ThrowingAwayLitter,
			GuestState.ExitingRide, GuestState.HeldByGuard, GuestState.Gone, GuestState.Using, GuestState.Queueing } )
			invalid.Add( WithGuest( snapshot, guest.Id, item => item with { State = state, BinId = state == GuestState.ThrowingAwayLitter ? 91 : 0 } ) );
		invalid.Add( WithGuest( snapshot, guest.Id, item => item with { Activity = GuestActivity.CantDecide } ) );
		invalid.Add( WithGuest( snapshot, guest.Id, item => item with { Activity = (GuestActivity)6 } ) );
		invalid.Add( WithGuest( snapshot, guest.Id, item => item with { AttractionId = 999 } ) );
		invalid.Add( WithGuest( snapshot, guest.Id, item => item with { QueuePosition = 0 } ) );
		foreach ( var state in RetainedStates.Select( pair => pair.State ).Distinct().Where( state => state != GuestState.DoingNothing ) )
			foreach ( var activity in new[] { GuestActivity.Cheering, GuestActivity.Vomiting, GuestActivity.CantDecide } )
				invalid.Add( WithGuest( snapshot, guest.Id, item => item with { State = state, Activity = activity } ) );
		var queuedId = snapshot.Guests.First( item => item.QueuePosition >= 0 ).Id;
		invalid.Add( WithGuest( snapshot, queuedId, item => item with { State = GuestState.DoingNothing, QueuePosition = -1 } ) );
		var before = Observe( rig );
		foreach ( var corrupt in invalid )
		{
			var loaded = RoundTrip( corrupt );
			Assert.IsFalse( rig.Simulation.ValidateSnapshot( loaded ).IsValid );
			Unchanged( rig, before, "validation is observational" );
			Assert.ThrowsException<InvalidDataException>( () => rig.Simulation.RestoreSnapshot( loaded ) );
			Assert.AreSame( guest, rig.Simulation.Find( guest.Id ), "failed restore does not replace even the live guest object" );
			Unchanged( rig, before, "rejected restore is atomic" );
		}
	}

	[TestMethod]
	public void RetainedTargetParticipatesInBothHashesAndOldClearedSavesStayCleared()
	{
		var rig = Refused( out var guest, out _ );
		var canonical = CanonicalHash( rig.Simulation );
		var legacy = rig.Simulation.ComputeStateHash();
		guest.AttractionId = 0;
		Assert.AreNotEqual( canonical, CanonicalHash( rig.Simulation ) );
		Assert.AreNotEqual( legacy, rig.Simulation.ComputeStateHash() );
		Assert.AreEqual( RideId, guest.LastAttractionId );
		var restored = Resume( rig );
		var subject = restored.Simulation.Find( guest.Id )!;
		Assert.AreEqual( 0, subject.AttractionId, "the previous remembered visit is not a missing target migration" );
		var happiness = subject.Happiness;
		restored.Simulation.Unregister( restored.Ride );
		Assert.AreEqual( happiness, subject.Happiness );
	}
}
