using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using OpenTPW.Scripting;

namespace OpenTPW.Tests;

/// <summary>GUEST-002: score a real disconnected queue back before the major decision asks for its route.</summary>
[TestClass]
[DoNotParallelize]
public class DisconnectedQueueScoringTests
{
	private sealed class Rig
	{
		public readonly GuestPathGrid Grid = new( 12, 12 );
		public readonly GuestSimulation Simulation;
		public readonly RideVisitorBridge Older;
		public readonly RideVisitorBridge Newer;
		public readonly RideVM OlderScript;
		public readonly RideVM NewerScript;

		public Rig( IGuestDecisions? policy = null, bool disconnected = true, bool neverConnected = false )
		{
			for ( var x = 0; x <= 7; x++ )
				Grid.SetPath( x, 1, true );
			Grid.SetPath( 3, 2, true );
			if ( !neverConnected )
				Grid.SetPath( 3, 3, true );
			var settings = GuestTests.CreateSettings();
			settings.ExitLevelSeconds = 100000;
			settings.ExitLevelVarSeconds = 0;
			Simulation = new GuestSimulation( Grid, settings, 7, policy ?? OriginalGuestDecisions.Instance ) { ArrivalsEnabled = false };
			Older = new RideVisitorBridge( 1, "Older disconnected candidate", RideVisitorKind.Ride, 5, 55, 30 )
			{
				EntranceCell = (2, 6), ExitCell = (2, 6), QueueFrontCell = (2, 5), QueueEntranceDirection = 2,
				AgeDays = 3, Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 )
			};
			OlderScript = GuestSnapshotTests.IdleScript( new VisitorRideScriptEffects( Older, UnimplementedRideScriptEffects.Instance ) );
			Older.Attach( OlderScript, () => true );
			Assert.AreEqual( 3, QueuePaths.TryExtend( Grid, Older, new[] { (2, 5), (2, 4), (2, 3) } ) );
			QueuePaths.TryConnect( Grid, Older );
			if ( neverConnected )
				Assert.IsNull( Older.JoinCell );
			else
				Assert.AreEqual( (3, 3), Older.JoinCell );
			Simulation.Register( Older );
			Newer = new RideVisitorBridge( 2, "Newer reachable candidate", RideVisitorKind.Ride, 5, 55, 30 )
			{
				EntranceCell = (7, 1), ExitCell = (7, 1), QueueFrontCell = (7, 1)
			};
			NewerScript = GuestSnapshotTests.IdleScript( new VisitorRideScriptEffects( Newer, UnimplementedRideScriptEffects.Instance ) );
			Newer.Attach( NewerScript, () => true );
			Simulation.Register( Newer );
			OlderScript.Advance( GuestSnapshotTests.Tick );
			NewerScript.Advance( GuestSnapshotTests.Tick );
			if ( disconnected && !neverConnected )
				Disconnect();
		}

		public void Disconnect()
		{
			Grid.SetPath( 3, 3, false );
			Older.RecomputeQueue( Grid );
			Assert.IsNull( Older.JoinCell );
			Assert.AreEqual( (2, 3), Older.QueueBackCell );
			Assert.AreEqual( (byte)ParkCellType.Queue, Grid.Cells.RawTypeAt( 2, 3 ) );
		}

		public Guest Spawn() => Simulation.SpawnInPark( 0, 1 );

		public void Step()
		{
			Simulation.Tick( GuestSnapshotTests.Tick );
			Older.HostStep();
			Newer.HostStep();
			OlderScript.Advance( GuestSnapshotTests.Tick );
			NewerScript.Advance( GuestSnapshotTests.Tick );
		}
	}

	private static ulong CanonicalHash( GuestSimulation simulation )
	{
		var hash = new StateHasher();
		simulation.AddCanonicalState( hash );
		return hash.Value;
	}

	private sealed record Observation( byte[] Guests, byte[] OlderVm, byte[] NewerVm, ulong Canonical, ulong Legacy, ulong Random );

	private static Observation Observe( Rig rig ) => new(
		ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ), ParkResumeJson.Serialize( rig.OlderScript.CaptureSnapshot() ),
		ParkResumeJson.Serialize( rig.NewerScript.CaptureSnapshot() ), CanonicalHash( rig.Simulation ), rig.Simulation.ComputeStateHash(), rig.Simulation.RandomState );

	private static void Equal( Observation expected, Observation actual )
	{
		CollectionAssert.AreEqual( expected.Guests, actual.Guests, "guest, navigation and bridge snapshots" );
		CollectionAssert.AreEqual( expected.OlderVm, actual.OlderVm, "older ride VM" );
		CollectionAssert.AreEqual( expected.NewerVm, actual.NewerVm, "newer ride VM" );
		Assert.AreEqual( expected.Canonical, actual.Canonical, "canonical guest-system hash" );
		Assert.AreEqual( expected.Legacy, actual.Legacy, "legacy guest-system hash" );
		Assert.AreEqual( expected.Random, actual.Random, "world generator" );
	}

	private static void FailedLaterRoute( Guest guest )
	{
		Assert.IsTrue( guest.Navigation.Stuck, "the later higher score caused a real failed route request" );
		Assert.IsTrue( guest.Navigation.Arrived );
		Assert.AreEqual( 0, guest.Navigation.Count );
		Assert.AreEqual( GuestNavigation.Centre( (2, 3) ), (guest.Navigation.TargetX, guest.Navigation.TargetY), "failed request keeps the disconnected back as its navigation destination" );
	}

	[TestMethod]
	public void DisconnectedTypedBackKeepsActualViewAndHistoryFacts()
	{
		var rig = new Rig( disconnected: false );
		var guest = rig.Spawn();
		var connected = rig.Simulation.ViewOf( rig.Older, guest );
		var score = rig.Simulation.ScoreAttraction( guest, rig.Older );
		rig.Disconnect();
		Assert.AreEqual( connected with { HasJoinCell = false }, rig.Simulation.ViewOf( rig.Older, guest ) );
		Assert.AreEqual( score, rig.Simulation.ScoreAttraction( guest, rig.Older ) );
		Assert.IsTrue( score > rig.Simulation.ScoreAttraction( guest, rig.Newer ) );
		guest.UsedHistory[0] = rig.Older.AttractionId;
		Assert.IsTrue( rig.Simulation.ViewOf( rig.Older, guest ).SameTypeAsLastUsed );
		Assert.AreEqual( 0u, rig.Simulation.ScoreAttraction( guest, rig.Older ), "the enriched view must still carry the same-type history exclusion" );
	}

	[TestMethod]
	public void NewlyBuiltNeverConnectedTypedBackUsesTheSameBoundedScoreContract()
	{
		var rig = new Rig( neverConnected: true );
		var guest = rig.Spawn();
		Assert.IsNull( rig.Older.JoinCell );
		Assert.AreEqual( (byte)ParkCellType.Queue, rig.Grid.Cells.RawTypeAt( 2, 3 ) );
		var view = rig.Simulation.ViewOf( rig.Older, guest );
		Assert.IsFalse( view.HasJoinCell );
		Assert.AreEqual( (2, 3), view.BackCell );
		Assert.AreEqual( 3, view.QueueSizeInCells );
		Assert.IsTrue( rig.Simulation.ScoreAttraction( guest, rig.Older ) > rig.Simulation.ScoreAttraction( guest, rig.Newer ) );
		Assert.AreSame( rig.Newer, rig.Simulation.Choose( guest ) );
		FailedLaterRoute( guest );
	}

	[TestMethod]
	public void MajorChoiceKeepsEarlierReachableWinnerButLaterFailureSticksTheWalk()
	{
		var rig = new Rig();
		var guest = rig.Spawn();
		Assert.IsTrue( rig.Simulation.ScoreAttraction( guest, rig.Older ) > rig.Simulation.ScoreAttraction( guest, rig.Newer ) );
		Assert.AreSame( rig.Newer, rig.Simulation.Choose( guest ), "newest first: reachable newer candidate wins before the older candidate's route fails" );
		FailedLaterRoute( guest );
	}

	[TestMethod]
	public void PeekChoicePreservesEveryGuestQueueNavigationAndVmFieldBeforeAndAfterFailure()
	{
		var rig = new Rig();
		var guest = rig.Spawn();
		guest.AttractionId = rig.Newer.AttractionId;
		foreach ( var afterFailure in new[] { false, true } )
		{
			if ( afterFailure )
				Assert.AreSame( rig.Newer, rig.Simulation.Choose( guest ) );
			var before = Observe( rig );
			Assert.AreSame( rig.Newer, rig.Simulation.PeekChoice( guest ) );
			Assert.AreSame( rig.Newer, rig.Simulation.PeekChoice( guest ), "repeated peeks stay observational" );
			Equal( before, Observe( rig ) );
		}
	}

	private static Guest ReachMajorChoice( Rig rig )
	{
		// Synthetic timing and branch-draw staging, not a claim about naturally wandering guests: keep the park empty until
		// the next update is an eligible major-decision turn outside the future guest's needs phase (no preceding thought draw).
		var nextId = rig.Simulation.CaptureSnapshot().NextGuestId;
		for ( var tick = 0; tick < 1200 && (rig.Simulation.ParkTurn < 32 || !rig.Simulation.NextTickStartsTurn
			|| NeedsUpdate.IsPhase( (uint)(rig.Simulation.ParkTurn + 1), nextId )); tick++ )
			rig.Step();
		Assert.IsTrue( rig.Simulation.ParkTurn >= 32 && rig.Simulation.NextTickStartsTurn );
		var guest = rig.Spawn();
		Assert.AreEqual( nextId, guest.Id );
		Assert.IsFalse( NeedsUpdate.IsPhase( (uint)(rig.Simulation.ParkTurn + 1), guest.Id ) );
		Assert.AreEqual( (0, 1), guest.Cell );
		guest.QueueStandingSinceTurn = 0;
		guest.Happiness = 60;
		guest.Nausea = guest.Litter = 0;
		const uint decisionState = 2;
		Assert.AreEqual( 0u, new WorldRng( decisionState ).NextWord() % 3, "the staged next draw selects the major branch" );
		rig.Simulation.RestoreRandomState( decisionState );
		Assert.IsTrue( rig.Simulation.ScoreAttraction( guest, rig.Older ) > rig.Simulation.ScoreAttraction( guest, rig.Newer ), "ordering is pinned at the actual decision position" );
		rig.Step();
		Assert.AreEqual( GuestState.GoingToRide, guest.State, "one real state-6 update ran the major decision" );
		Assert.AreEqual( rig.Newer.AttractionId, guest.AttractionId, "the real decision assigns its reachable winner despite the later failed request" );
		FailedLaterRoute( guest );
		return guest;
	}

	[TestMethod]
	public void ActualMajorDecisionRetainsWinnerAndJsonRestoresTheFailedWalk()
	{
		var source = new Rig();
		var guest = ReachMajorChoice( source );
		var loaded = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( source.Simulation.CaptureSnapshot() ) );
		var resumed = new Rig(); // Rebuild the same typed map and links; the guest codec does not serialize the park layout.
		var validation = resumed.Simulation.ValidateSnapshot( loaded );
		Assert.IsTrue( validation.IsValid, string.Join( "; ", validation.Errors ) );
		resumed.Simulation.RestoreSnapshot( loaded );
		foreach ( var (from, to) in new[] { (source.OlderScript, resumed.OlderScript), (source.NewerScript, resumed.NewerScript) } )
		{
			var vm = ParkResumeJson.Deserialize<RideVMSnapshot>( ParkResumeJson.Serialize( from.CaptureSnapshot() ) );
			vm.ValidateSnapshot();
			to.RestoreSnapshot( vm );
		}
		var restoredGuest = resumed.Simulation.Guests.Single( item => item.Id == guest.Id );
		Assert.AreEqual( resumed.Newer.AttractionId, restoredGuest.AttractionId );
		FailedLaterRoute( restoredGuest );
		Equal( Observe( source ), Observe( resumed ) );
		Assert.AreEqual( source.Simulation.ScoreAttraction( guest, source.Older ), resumed.Simulation.ScoreAttraction( restoredGuest, resumed.Older ) );
		for ( var tick = 0; tick < 180; tick++ )
		{
			source.Step();
			resumed.Step();
			Equal( Observe( source ), Observe( resumed ) );
		}
	}

	// Negative interface control only: unlike RideVisitorBridge's one-cell fallback, a custom bridge may really have no cells.
	private sealed class MissingBackBridge : IRideVisitorBridge
	{
		public IRideVisitorHost? Host { get; set; }
		public int AttractionId => 99;
		public string Name => "Unsupported";
		public RideVisitorKind Kind => RideVisitorKind.Shop;
		public bool IsOpen => true;
		public int Capacity => 1;
		public int ExcitementLevel => 55;
		public int AttractionValue => 0;
		public GuestNeeds Satisfies => GuestNeeds.None;
		public int Price => 0;
		public (int X, int Y) EntranceCell => (2, 3);
		public (int X, int Y) ExitCell => (0, 0);
		public int QueueLength => 0;
		public int MaximumQueueLength => 0;
		public uint QueueLimit => 0;
		public bool HasQueue => false;
		public bool ProvidesRelief => false;
		public bool IsBroken => false;
		public bool IsBrokenDown => false;
		public bool ReliefWornOut => false;
		public bool IsRunning => false;
		public int RidersOnBoard => 0;
		public int Speed => 0;
		public IReadOnlyList<(int X, int Y)> QueueCells => Array.Empty<(int X, int Y)>();
		public int QueueSizeInCells => 1;
		public (int X, int Y)? JoinCell => null;
		public int QueueEditCount => 0;
		public int CalledGuest => 0;
		public bool TryJoinQueue( int guestId ) => false;
		public QueueJoinResult JoinQueue( int guestId, int? preferredExcitement = null ) => QueueJoinResult.Closed;
		public QueueJoinResult CheckJoinRoom( int guestId ) => QueueJoinResult.Closed;
		public void AppendToQueue( int guestId ) { }
		public int JoinDifference( int preferredExcitement ) => 0;
		public int GetQueuePosition( int guestId ) => -1;
		public bool LeaveQueue( int guestId ) => false;
		public bool RecomputeQueue( GuestPathGrid grid ) => false;
		public int DirectionTowardsFront( int index ) => 0;
		public bool PresentForBoarding( int guestId ) => false;
	}

	[TestMethod]
	public void EmptyBackIsExcludedBeforeViewsScoresAndMajorRoutes()
	{
		var rig = new Rig();
		var guest = rig.Spawn();
		guest.UsedHistory[0] = rig.Older.AttractionId;
		var missing = new MissingBackBridge();
		Assert.AreEqual( 0, missing.QueueCells.Count );
		Assert.AreEqual( 0u, rig.Simulation.ScoreAttraction( guest, missing ) );
		Assert.AreEqual( (0, 0), rig.Simulation.ViewOf( missing, guest ).BackCell );
		rig.Simulation.Register( missing );
		Assert.AreSame( rig.Newer, rig.Simulation.PeekChoice( guest ) );
		Assert.AreSame( rig.Newer, rig.Simulation.Choose( guest ) );
		Assert.IsFalse( guest.Navigation.Stuck );
	}

	[TestMethod]
	public void UntypedFallbackBacksKeepZeroScoresAndIdentityOnlyViews()
	{
		var rig = new Rig();
		var guest = rig.Spawn();
		var missing = new RideVisitorBridge( 3, "Untyped fallback", RideVisitorKind.Ride, 5, 55, 30 ) { EntranceCell = (9, 9) };
		Assert.AreEqual( 1, missing.QueueCells.Count, "an unrecomputed concrete bridge exposes its entrance fallback" );
		Assert.AreEqual( 0u, rig.Simulation.ScoreAttraction( guest, missing ) );
		Assert.AreEqual( (0, 0), rig.Simulation.ViewOf( missing, guest ).BackCell );
		rig.Simulation.Register( missing );
		guest.UsedHistory[0] = rig.Older.AttractionId; // Suppress the other disconnected candidate to isolate the untyped fallback.
		foreach ( var raw in new byte[] { 0, ParkCellTypes.Entrance, ParkCellTypes.Exit, 255 } )
		{
			rig.Grid.Cells.SetType( 9, 9, raw );
			missing.RecomputeQueue( rig.Grid );
			Assert.IsNull( missing.JoinCell );
			Assert.AreEqual( (9, 9), missing.QueueBackCell, "the bridge's one-cell fallback is not a typed queue" );
			Assert.AreEqual( 0u, rig.Simulation.ScoreAttraction( guest, missing ) );
			var view = rig.Simulation.ViewOf( missing, guest );
			Assert.IsFalse( view.HasJoinCell );
			Assert.AreEqual( (0, 0), view.BackCell );
			Assert.AreEqual( 0, view.LiveExcitement );
			Assert.AreEqual( 0u, view.AgeDays );
			var before = Observe( rig );
			Assert.AreSame( rig.Newer, rig.Simulation.PeekChoice( guest ) );
			Equal( before, Observe( rig ) );
			Assert.AreSame( rig.Newer, rig.Simulation.Choose( guest ) );
			Assert.IsFalse( guest.Navigation.Stuck, "the untyped fallback never requests a route" );
		}
		// An actual connected path fallback still scores under the existing contract.
		rig.Grid.SetPath( 9, 9, true );
		missing.RecomputeQueue( rig.Grid );
		Assert.AreEqual( (9, 9), missing.JoinCell );
		Assert.IsTrue( rig.Simulation.ScoreAttraction( guest, missing ) > 0 );
	}

	[TestMethod]
	public void SmarterAndLegacyCustomPoliciesKeepHostEligibilityAndDirectSmarterFallback()
	{
		var faithful = new Rig();
		var faithfulGuest = faithful.Spawn();
		var facts = faithful.Simulation.ViewOf( faithful.Older, faithfulGuest );
		var context = new DecisionContext( faithful.Simulation.Settings, faithful.Simulation.ParkTurn, false, 7, 12, 12 );
		var smarter = new SmarterGuestDecisions();
		Assert.AreEqual( OriginalGuestDecisions.Instance.ScoreAttraction( context, faithful.Simulation.ViewOf( faithfulGuest ), facts ),
			smarter.ScoreAttraction( context, faithful.Simulation.ViewOf( faithfulGuest ), facts ), "direct smarter-policy fallback remains the original formula, not an invented zero guard" );
		foreach ( IGuestDecisions policy in new IGuestDecisions[] { smarter, new SmarterGuestsTests.DelegatingDecisions( OriginalGuestDecisions.Instance ) } )
		{
			Assert.IsFalse( policy.ScoresDisconnectedQueues, "existing implementers opt out by default" );
			var rig = new Rig( policy );
			var guest = rig.Spawn();
			Assert.AreEqual( 0u, rig.Simulation.ScoreAttraction( guest, rig.Older ), "host still skips a disconnected candidate" );
			Assert.AreEqual( (0, 0), rig.Simulation.ViewOf( rig.Older, guest ).BackCell );
			var before = Observe( rig );
			Assert.AreSame( rig.Newer, rig.Simulation.PeekChoice( guest ) );
			Equal( before, Observe( rig ) );
			Assert.AreSame( rig.Newer, rig.Simulation.Choose( guest ) );
			Assert.IsFalse( guest.Navigation.Stuck, "no route is requested for the skipped older candidate" );
		}
	}

	[TestMethod]
	public void RealScriptHooksSeeFalseJoinFlagAndRealFactsOnlyOverOptedInBasePolicy()
	{
		using var mods = new TempScriptMods().Add( "community.disconnected", """
function scoreAttraction(guest, c, prev)
  if c.id == 1 then
    if c.hasJoinCell then return 0 end
    if c.backX == 2 and c.backY == 3 and c.queueSizeInCells == 3 and c.ageDays == 3 and c.excitement == 55 then
      return prev + 1
    end
    return 0
  end
  return prev
end
""", "scoreAttraction" );
		foreach ( IGuestDecisions inner in new IGuestDecisions[] { OriginalGuestDecisions.Instance, new SmarterGuestDecisions() } )
		{
			var policy = new ScriptGuestDecisions( inner, new ScriptModRuntime( mods.Resolve().Mods ) );
			Assert.AreEqual( inner.ScoresDisconnectedQueues, policy.ScoresDisconnectedQueues );
			var rig = new Rig( policy );
			var guest = rig.Spawn();
			var view = rig.Simulation.ViewOf( rig.Older, guest );
			Assert.IsFalse( view.HasJoinCell, "script API flag continues to mean attached path" );
			var context = new DecisionContext( rig.Simulation.Settings, rig.Simulation.ParkTurn, false, 7, 12, 12 );
			var expected = inner.ScoresDisconnectedQueues ? inner.ScoreAttraction( context, rig.Simulation.ViewOf( guest ), view ) + 1 : 0;
			var calls = policy.Runtime.Mods.Single().Stats().Calls;
			Assert.AreEqual( expected, rig.Simulation.ScoreAttraction( guest, rig.Older ), "the real runtime hook reads the view and prev; the smarter host skips it" );
			Assert.AreEqual( calls + (inner.ScoresDisconnectedQueues ? 1 : 0), policy.Runtime.Mods.Single().Stats().Calls, "the host capability controls whether this genuine hook is called" );
			var before = Observe( rig );
			Assert.AreSame( rig.Newer, rig.Simulation.PeekChoice( guest ) );
			Equal( before, Observe( rig ) );
			Assert.AreSame( rig.Newer, rig.Simulation.Choose( guest ) );
			Assert.AreEqual( inner.ScoresDisconnectedQueues, guest.Navigation.Stuck );
			Assert.AreEqual( 0L, policy.Runtime.Mods.Single().Failures, "the hook executed successfully rather than falling back after an error" );
		}
	}
}
