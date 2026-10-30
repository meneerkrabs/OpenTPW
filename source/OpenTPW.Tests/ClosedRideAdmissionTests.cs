using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Asset-free host tests of the stand-point admission guard, separate from immediate close-time withdrawal.</summary>
[TestClass, DoNotParallelize]
public class ClosedRideAdmissionTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private GameEdition edition;
	[TestInitialize]
	public void Setup() { edition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkWorld ); Log ??= new(); }
	[TestCleanup]
	public void Cleanup() => GameEditions.Select( edition );

	[TestMethod]
	public void RuntimeCloseRefusesTheCalledGuestBeforeTheNextRideUpdate()
	{
		using var fixture = new RuntimeFixture();
		var runtime = fixture.Runtime;
		var ride = runtime.Visitors;
		fixture.CallHead( 101, 102 );
		Assert.AreEqual( 101, ride.CalledGuest );
		runtime.Close();
		Assert.IsFalse( runtime.IsOpen );
		var before = ParkResumeJson.Serialize( runtime.CaptureSnapshot() );
		Assert.IsFalse( ride.PresentForBoarding( 101 ) );
		Assert.IsFalse( ride.PresentForBoarding( 101 ), "Repeated presentation must not write an offer either." );
		Assert.AreEqual( 0, runtime.GetVariable( RideVariables.VAR_LETMEON ) );
		// [BIN:TPW-EXE:0x004DF300 close clears the loading slot] the close itself zeroes +0x6C; the refusal then touches nothing
		Assert.AreEqual( 0, ride.CalledGuest, "The close withdraws the call at once." );
		CollectionAssert.AreEqual( new[] { 101, 102 }, ride.Queue.ToArray() );
		CollectionAssert.AreEqual( new[] { "call 101" }, fixture.Host.Events );
		CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( runtime.CaptureSnapshot() ), "Refusal must not mutate either the bridge or the script." );
	}

	[TestMethod]
	public void ASnapshotRestoreKeepsTheUpgradeRequestOverASavedBreakdown()
	{
		// a save written before the upgrade request cleared a breakdown can hold a ride that is both broken and upgrading: the load keeps the upgrade
		using var fixture = new RuntimeFixture();
		var runtime = fixture.Runtime;
		var ride = runtime.Visitors;
		fixture.CallHead( 101 );
		runtime.Close();
		ride.StateWord.Store( RideObjectStates.Upgrading );
		ride.RestoreSnapshot( ride.CaptureSnapshot() with { BrokenDown = true, Condemned = false } );
		Assert.AreEqual( RideObjectStates.Idle, ride.StateWord.Value, "the breakdown is not copied back under the upgrade request (0x4E0086)" );
		Assert.AreEqual( RideObjectStates.Upgrading, ride.StateWord.Current );
		Assert.IsFalse( ride.IsBrokenDown );
		// without the upgrade request the saved breakdown still restores
		ride.StateWord.Store( RideObjectStates.Idle );
		ride.RestoreSnapshot( ride.CaptureSnapshot() with { BrokenDown = true, Condemned = false } );
		Assert.AreEqual( RideObjectStates.BrokenDown, ride.StateWord.Value );
	}

	[DataTestMethod]
	[DataRow( 1, false )]
	[DataRow( 4, false )]
	[DataRow( 1, true )]
	[DataRow( 4, true )]
	public void RetainedStateRefusesPresentationWithTheScriptBrokenFlagClear( int state, bool openWord )
	{
		using var fixture = new RuntimeFixture();
		var runtime = fixture.Runtime;
		var ride = runtime.Visitors;
		fixture.CallHead( 101 );
		runtime.Close();
		// Synthetic retained-state staging through the existing snapshot API. This is not an original-save import test.
		ride.RestoreSnapshot( ride.CaptureSnapshot() with { BrokenDown = true, Condemned = state == 4 } );
		ride.Attach( runtime.Script!, () => openWord );
		Assert.AreEqual( 0, runtime.GetVariable( RideVariables.VAR_BROKEN ) );
		Assert.IsTrue( ride.IsBrokenDown );
		Assert.AreEqual( state == 4, ride.IsCondemned );
		Assert.AreEqual( state == 1, ride.HoldsQueuedGuests );
		var before = ParkResumeJson.Serialize( runtime.CaptureSnapshot() );
		Assert.IsFalse( ride.PresentForBoarding( 101 ) );
		CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( runtime.CaptureSnapshot() ) );
	}

	[DataTestMethod]
	[DataRow( true, 0 )]
	[DataRow( false, 0 )]
	[DataRow( true, 1 )]
	[DataRow( false, 1 )]
	public void OpenAdmissionIgnoresTheChoicePredicateAndUnobservedScriptBroken( bool takesGuests, int scriptBroken )
	{
		using var fixture = new RuntimeFixture();
		var runtime = fixture.Runtime;
		var ride = runtime.Visitors;
		fixture.CallHead( 101 );
		ride.Attach( runtime.Script!, () => runtime.IsOpen, () => takesGuests );
		Assert.IsTrue( runtime.SetVariable( nameof( RideVariables.VAR_BROKEN ), scriptBroken ) );
		Assert.AreEqual( takesGuests, ride.IsOpen );
		Assert.IsFalse( ride.IsBrokenDown, "A script flag has not yet entered a retained state." );
		Assert.IsTrue( ride.PresentForBoarding( 101 ) );
		Assert.IsTrue( ride.PresentForBoarding( 101 ), "An open ride keeps its existing offer." );
		Assert.AreEqual( 101, runtime.GetVariable( RideVariables.VAR_LETMEON ) );
		Assert.AreEqual( 0, ride.BoardedTotal, "The script has not consumed the offer." );
	}

	[TestMethod]
	public void CloseDoesNotWithdrawAnAlreadyWrittenOfferInsidePresentation()
	{
		using var fixture = new RuntimeFixture();
		var runtime = fixture.Runtime;
		var ride = runtime.Visitors;
		fixture.CallHead( 101, 102 );
		Assert.IsTrue( ride.PresentForBoarding( 101 ) );
		runtime.Close();
		var before = ParkResumeJson.Serialize( runtime.CaptureSnapshot() );
		Assert.IsFalse( ride.PresentForBoarding( 101 ), "The guard also precedes the already-written shortcut." );
		CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( runtime.CaptureSnapshot() ) );
		Assert.AreEqual( 101, runtime.GetVariable( RideVariables.VAR_LETMEON ) );
		fixture.Host.Turn++;
		ride.HostStep();
		Assert.AreEqual( 0, runtime.GetVariable( RideVariables.VAR_LETMEON ) );
		Assert.AreEqual( (0, 0, 1), (ride.CalledGuest, ride.BoardedTotal, ride.Dismissed) );
		CollectionAssert.AreEqual( new[] { 102 }, ride.Queue.ToArray() );
	}

	[TestMethod]
	public void AConsumedGuestStillBoardsBeforeTheClosedRideDismissesItsNextHead()
	{
		using var fixture = new RuntimeFixture();
		var runtime = fixture.Runtime;
		var ride = runtime.Visitors;
		fixture.CallHead( 101, 102, 103 );
		Assert.IsTrue( ride.PresentForBoarding( 101 ) );
		Assert.IsTrue( runtime.SetVariable( nameof( RideVariables.VAR_LETMEON ), 0 ), "The idle script's consumption is staged explicitly." );
		Assert.IsTrue( runtime.SetVariable( nameof( RideVariables.VAR_ONRIDE ), 1 ) );
		runtime.Close();
		fixture.Host.Turn++;
		ride.HostStep();
		Assert.AreEqual( (1, 1, 0), (ride.BoardedTotal, ride.Dismissed, ride.CalledGuest) );
		CollectionAssert.AreEqual( new[] { 101 }, ride.CaptureSnapshot().OnRide );
		CollectionAssert.AreEqual( new[] { 103 }, ride.Queue.ToArray() );
		CollectionAssert.AreEqual( new[] { "call 101", "board 101", "away 102" }, fixture.Host.Events );
	}

	/// <summary>
	/// The actual runtime's bridge registered with the queue fixture's grid and real simulation, a guest sent to it, and the run
	/// stepped until the guest walks to the stand point (state 13). Returns the step that runs the simulation, the ride's host step and the script.
	/// </summary>
	private static (QueueTests.Rig Rig, Guest Guest, Action Step) WalkToStandPoint( RuntimeFixture fixture )
	{
		var (rig, guest, step) = CallGuest( fixture );
		for ( var tick = 0; tick < 4000 && guest.State != GuestState.Boarding; tick++ ) step();
		Assert.AreEqual( GuestState.Boarding, guest.State );
		Assert.IsTrue( guest.HasWaypoint, "The guest is still on its way to the stand point." );
		Assert.AreEqual( 0, fixture.Runtime.GetVariable( RideVariables.VAR_LETMEON ) );
		return (rig, guest, step);
	}

	/// <summary>As <see cref="WalkToStandPoint"/>, stopped at the call: the guest still stands in state 11 with its call flag set.</summary>
	private static (QueueTests.Rig Rig, Guest Guest, Action Step) CallGuest( RuntimeFixture fixture )
	{
		var runtime = fixture.Runtime;
		var ride = runtime.Visitors;
		var rig = new QueueTests.Rig();
		var simulation = rig.Simulation;
		simulation.Unregister( rig.Ride );
		ride.EntranceCell = rig.Ride.EntranceCell;
		ride.ExitCell = rig.Ride.ExitCell;
		ride.QueueFrontCell = rig.Ride.QueueFrontCell;
		ride.QueueEntranceDirection = rig.Ride.QueueEntranceDirection;
		ride.Parameters = rig.Ride.Parameters;
		simulation.Register( ride );
		var guest = simulation.SpawnInPark( 2, 0 );
		guest.AttractionId = ride.AttractionId;
		guest.State = GuestState.GoingToRide;
		void Step()
		{
			simulation.Tick( Tick );
			ride.HostStep();
			runtime.Script!.Advance( Tick );
		}
		for ( var step = 0; step < 4000 && ride.CalledGuest == 0; step++ ) Step();
		Assert.AreEqual( guest.Id, ride.CalledGuest );
		Assert.AreEqual( GuestState.Queueing, guest.State, "The call marks state 11; its next queue update starts the stand-point walk." );
		Assert.IsTrue( guest.QueueCalled );
		return (rig, guest, Step);
	}

	/// <summary>Guest-only subturns (no ride update and no script slice) until the guest leaves state 13.</summary>
	private static void ArriveAtStandPoint( QueueTests.Rig rig, Guest guest, Action? beforeTick = null )
	{
		for ( var step = 0; step < 4000 && guest.State == GuestState.Boarding; step++ )
		{
			beforeTick?.Invoke();
			rig.Simulation.Tick( Tick );
		}
		Assert.AreNotEqual( GuestState.Boarding, guest.State, "The stand-point walk ends in one presentation: tp.exe's state 13 leaves on every outcome (0x005006B0)." );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void LiveStandPointSubturnRechecksRuntimeCloseBeforeWritingTheOffer( bool close )
	{
		using var fixture = new RuntimeFixture();
		var runtime = fixture.Runtime;
		var ride = runtime.Visitors;
		var (rig, guest, step) = WalkToStandPoint( fixture );
		var simulation = rig.Simulation;
		if ( close ) runtime.Close();
		var before = ride.CaptureSnapshot();
		Assert.IsFalse( before.LetMeOnWritten );
		Assert.AreEqual( close ? 0 : guest.Id, before.CalledGuest, "The close (0x004DF300) clears the loading slot at once." );
		ArriveAtStandPoint( rig, guest );
		Assert.AreEqual( before.LastAdmissionTurn, ride.CaptureSnapshot().LastAdmissionTurn, "Only the guest's subturns ran." );
		Assert.AreEqual( close ? 0 : guest.Id, runtime.GetVariable( RideVariables.VAR_LETMEON ) );
		Assert.AreEqual( close ? 0 : guest.Id, ride.CalledGuest );
		Assert.AreEqual( (0, 0), (ride.BoardedTotal, ride.Dismissed) );
		CollectionAssert.AreEqual( new[] { guest.Id }, ride.Queue.ToArray() );
		if ( !close )
		{
			Assert.AreEqual( GuestState.WaitingToBoard, guest.State );
			return;
		}
		// [BIN:TPW-EXE:0x005007E7 AdmitPerson refusal] the refused guest walks back to its queue place (0x00501160, state 12); AdmitPerson's closed test leaves the ride alone
		Assert.AreEqual( GuestState.MovingUpQueue, guest.State );
		Assert.AreEqual( 0, guest.QueuePosition );
		Assert.IsTrue( guest.HasWaypoint );
		Assert.AreEqual( GuestSimulation.StandCellOf( ride ), guest.Navigation.Cell, "The refusal happened at the stand point." );
		CollectionAssert.AreEqual( ParkResumeJson.Serialize( before ), ParkResumeJson.Serialize( ride.CaptureSnapshot() ), "The live refusal leaves bridge state untouched." );
		for ( var tick = 0; tick < 4000 && guest.State == GuestState.MovingUpQueue; tick++ ) simulation.Tick( Tick );
		Assert.AreEqual( GuestState.Queueing, guest.State, "Back at its place the guest stands in the queue again (state 11)." );
		Assert.IsFalse( guest.QueueCalled, "The call was spent on the walk; the guest does not walk to the stand point again." );
		// The closed ride's next update dismisses its head (0x004E1220, 0x004E0450, 0x005012F0).
		for ( var tick = 0; tick < 4000 && guest.IsInQueue; tick++ ) step();
		Assert.AreEqual( GuestState.DoingNothing, guest.State );
		Assert.AreEqual( (0, 1), (ride.CalledGuest, ride.Dismissed) );
		Assert.AreEqual( 0, ride.Queue.Count() );
	}

	[TestMethod]
	public void AGuestWalkingWhenTheRideClosesAndReopensBeforeItsNextUpdateIsAdmittedOnArrival()
	{
		using var fixture = new RuntimeFixture();
		var runtime = fixture.Runtime;
		var ride = runtime.Visitors;
		var (rig, guest, _) = WalkToStandPoint( fixture );
		runtime.Close();
		Assert.AreEqual( 0, ride.CalledGuest, "The close (0x004DF300) clears the loading slot at once." );
		runtime.Open();
		var turn = ride.CaptureSnapshot().LastAdmissionTurn;
		ArriveAtStandPoint( rig, guest );
		Assert.AreEqual( turn, ride.CaptureSnapshot().LastAdmissionTurn, "No ride update ran between the close and the arrival." );
		// [BIN:TPW-EXE:0x004E0976 AdmitPerson admits the presenting guest] with the slot cleared AdmitPerson logs "admitting wrong person" and writes VAR_LETMEON for the guest that presents itself
		Assert.AreEqual( GuestState.WaitingToBoard, guest.State );
		Assert.AreEqual( guest.Id, runtime.GetVariable( RideVariables.VAR_LETMEON ) );
		Assert.AreEqual( guest.Id, ride.CalledGuest );
		Assert.AreEqual( 0, ride.Dismissed );
	}

	[TestMethod]
	public void ARefusedGuestIsCalledAgainWhenTheRideReopensBeforeItsNextUpdate()
	{
		using var fixture = new RuntimeFixture();
		var runtime = fixture.Runtime;
		var ride = runtime.Visitors;
		var (rig, guest, step) = WalkToStandPoint( fixture );
		var simulation = rig.Simulation;
		var calls = 0;
		ride.AdmissionChecked += ( _, check ) => calls += check.CalledGuest == guest.Id ? 1 : 0;
		runtime.Close();
		ArriveAtStandPoint( rig, guest );
		Assert.AreEqual( GuestState.MovingUpQueue, guest.State, "Refused at the closed ride, the guest walks back to its place." );
		for ( var tick = 0; tick < 4000 && guest.State == GuestState.MovingUpQueue; tick++ ) simulation.Tick( Tick );
		Assert.AreEqual( GuestState.Queueing, guest.State );
		Assert.AreEqual( (0, 0), (ride.CalledGuest, ride.Dismissed), "No ride update has run since the close." );
		runtime.Open();
		// The ride's next update finds nobody pending (the close cleared +0x6C) and its standing head at place 0: it calls the guest again (0x004E1220).
		for ( var tick = 0; tick < 4000 && guest.State != GuestState.WaitingToBoard; tick++ ) step();
		Assert.AreEqual( GuestState.WaitingToBoard, guest.State );
		Assert.AreEqual( guest.Id, runtime.GetVariable( RideVariables.VAR_LETMEON ) );
		Assert.AreEqual( (guest.Id, 0), (ride.CalledGuest, ride.Dismissed) );
		Assert.AreEqual( 1, calls, "The reopened ride called the guest again." );
	}

	[TestMethod]
	public void ACalledGuestStillStandingWaitsWhileTheRideIsClosedAndIsCalledAgainOnReopening()
	{
		using var fixture = new RuntimeFixture();
		var runtime = fixture.Runtime;
		var ride = runtime.Visitors;
		var (rig, guest, step) = CallGuest( fixture );
		var simulation = rig.Simulation;
		var calls = 0;
		ride.AdmissionChecked += ( _, check ) => calls += check.CalledGuest == guest.Id ? 1 : 0;
		runtime.Close();
		Assert.AreEqual( 0, ride.CalledGuest );
		var turn = simulation.ParkTurn;
		for ( var tick = 0; tick < 4000 && simulation.ParkTurn < turn + 3; tick++ ) simulation.Tick( Tick );
		// [BIN:TPW-EXE:0x005001A4 called without the loading slot] place 0 and the call flag set, but the ride's +0x6C no longer names the guest: its state-11 update ends there
		Assert.AreEqual( GuestState.Queueing, guest.State, "The guest does not walk to the stand point of a ride that no longer loads it." );
		Assert.IsTrue( guest.QueueCalled, "The flag stays set." );
		runtime.Open();
		for ( var tick = 0; tick < 4000 && guest.State != GuestState.WaitingToBoard; tick++ ) step();
		Assert.AreEqual( GuestState.WaitingToBoard, guest.State );
		Assert.AreEqual( (guest.Id, 0), (ride.CalledGuest, ride.Dismissed) );
		Assert.AreEqual( 1, calls, "The reopened ride called the guest a second time." );
	}

	[TestMethod]
	public void ARefusedGuestWithoutARouteBackToItsPlaceLeavesTheQueue()
	{
		var rig = new QueueTests.Rig();
		var ride = rig.Ride;
		var guest = rig.Send();
		for ( var tick = 0; tick < 8000 && guest.State != GuestState.Boarding; tick++ ) rig.Step();
		Assert.AreEqual( GuestState.Boarding, guest.State );
		Assert.IsTrue( guest.HasWaypoint );
		rig.Open = false;
		var standCell = GuestSimulation.StandCellOf( ride );
		var happiness = guest.Happiness;
		// Once the guest is inside the entrance cell, the cell turns into a type-4 cell (no step leaves it into a queue cell) and its other neighbours into
		// type-5 cells (no step enters them): the walk in ends, but no search leads back out (WalkEdges.Blocked, tp.exe 0x004D8750).
		ArriveAtStandPoint( rig, guest, () =>
		{
			if ( guest.Navigation.Cell != standCell || rig.Grid.Cells.RawTypeAt( standCell.X, standCell.Y ) == 4 )
				return;
			rig.Grid.Cells.SetType( standCell.X, standCell.Y, 4 );
			foreach ( var (dx, dy) in new[] { (-1, 0), (1, 0), (0, 1) } )
				rig.Grid.Cells.SetType( standCell.X + dx, standCell.Y + dy, 5 );
		} );
		Assert.AreEqual( 4, rig.Grid.Cells.RawTypeAt( standCell.X, standCell.Y ), "The guest stood in the entrance cell before it arrived." );
		// [BIN:TPW-EXE:0x0050082F rejoin failure] "Couldn't rejoin FOQ even!": unlink 0x004DDD20, dismissal 0x005012F0 with no thought, state 6
		Assert.AreEqual( GuestState.DoingNothing, guest.State );
		Assert.AreEqual( ride.AttractionId, guest.LastAttractionId );
		Assert.AreEqual( GuestThought.None, guest.Thought );
		Assert.AreEqual( Math.Max( 0, happiness - rig.Simulation.Settings.MediumHappinessChange ), guest.Happiness, 1e-4, "The dismissal's medium happiness drop." );
		Assert.AreEqual( 0, ride.Queue.Count() );
		Assert.AreEqual( (0, 0, 0), (ride.BoardedTotal, ride.Dismissed, rig.Script[RideVariables.VAR_LETMEON]), "The guest left by its own dismissal, not the ride's head step." );
	}

	[TestMethod]
	public void ABusyLetMeOnRefusesTheGuestClearsTheLoadingSlotAndCallsItAgainLater()
	{
		using var fixture = new RuntimeFixture();
		var runtime = fixture.Runtime;
		var ride = runtime.Visitors;
		var (rig, guest, step) = WalkToStandPoint( fixture );
		Assert.IsTrue( runtime.SetVariable( nameof( RideVariables.VAR_LETMEON ), 9999 ) );
		ArriveAtStandPoint( rig, guest );
		// [BIN:TPW-EXE:0x004E09B0 AdmitPerson clears the loading slot] the open path clears +0x6C before it reads the busy VAR_LETMEON, which it leaves alone
		Assert.AreEqual( GuestState.MovingUpQueue, guest.State );
		Assert.AreEqual( 0, ride.CalledGuest );
		Assert.AreEqual( 9999, runtime.GetVariable( RideVariables.VAR_LETMEON ) );
		CollectionAssert.AreEqual( new[] { guest.Id }, ride.Queue.ToArray() );
		Assert.IsTrue( runtime.SetVariable( nameof( RideVariables.VAR_LETMEON ), 0 ) );
		for ( var tick = 0; tick < 8000 && guest.State != GuestState.WaitingToBoard; tick++ ) step();
		Assert.AreEqual( GuestState.WaitingToBoard, guest.State, "Back in state 11 at position 0 the ride calls the guest again and it presents." );
		Assert.AreEqual( guest.Id, runtime.GetVariable( RideVariables.VAR_LETMEON ) );
		Assert.AreEqual( 0, ride.Dismissed );
	}

	[TestMethod]
	public void AClosedPricedShopTestsThePriceOnceForTheArrival()
	{
		var rig = new QueueTests.Rig( kind: RideVisitorKind.Shop );
		var ride = rig.Ride;
		ride.Sideshow = new SideshowTerms( 0, 5, 0 );
		ride.SpecialIngredient = 1;
		ride.InitCostOfGoods = 20;
		var payments = new RecordingPayments();
		var simulation = rig.Simulation;
		simulation.Payments = payments;
		var guest = rig.Send();
		guest.Money = 1000;
		for ( var tick = 0; tick < 8000 && guest.State != GuestState.Boarding; tick++ ) rig.Step();
		Assert.AreEqual( GuestState.Boarding, guest.State );
		Assert.AreEqual( 0, payments.Ticks.Count );
		rig.Open = false;
		var tickIndex = 0;
		payments.Tick = () => tickIndex;
		for ( ; tickIndex < 4000 && guest.State is GuestState.Boarding or GuestState.MovingUpQueue; tickIndex++ ) simulation.Tick( Tick );
		Assert.AreEqual( GuestState.Queueing, guest.State, "The closed shop refused the guest, which walked back to its place." );
		for ( var more = 0; more < 200; more++, tickIndex++ ) simulation.Tick( Tick );
		Assert.IsTrue( payments.Ticks.Count > 0, "The price test ran (QueueJoinRules.PriceStatistics records the special ingredient's category)." );
		// [BIN:TPW-EXE:0x00500715 price test once] state 13 runs 0x004FDE50 once, on its arrival, before AdmitPerson; the refusal leaves state 13 at once
		Assert.AreEqual( 1, payments.Ticks.Distinct().Count(), $"one price test, not one per tick: {string.Join( ", ", payments.Ticks )}" );
	}

	private sealed class RecordingPayments : IGuestPayments
	{
		public readonly List<int> Ticks = new();
		public Func<int> Tick = () => -1;
		public int AdmissionFee => 0;
		public bool TryPayAdmission( out int feePaid ) { feePaid = 0; return true; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) { pricePaid = 0; return true; }
		public void PriceTested( int category, int value ) => Ticks.Add( Tick() );
	}

	[TestMethod]
	public void TheRuntimeOpenerLeavesTheRetainedStateAsSetStateZeroDoes()
	{
		// IsBrokenDown stands for tp.exe's +0x19C test only while every opener writes the open word and VAR_RIDECLOSED together with SetState(0)
		using var fixture = new RuntimeFixture();
		var runtime = fixture.Runtime;
		var ride = runtime.Visitors;
		fixture.CallHead( 101 );
		runtime.Close();
		ride.RestoreSnapshot( ride.CaptureSnapshot() with { BrokenDown = true } );
		ride.Attach( runtime.Script!, () => runtime.IsOpen );
		Assert.IsTrue( ride.IsBrokenDown );
		Assert.IsFalse( ride.PresentForBoarding( 101 ) );
		runtime.Open();
		Assert.IsFalse( ride.IsBrokenDown, "The opener's VAR_RIDECLOSED 0 and open word 1 leave state 1 at once, as SetState(0) does." );
		Assert.IsTrue( ride.PresentForBoarding( 101 ) );
		Assert.AreEqual( 101, runtime.GetVariable( RideVariables.VAR_LETMEON ) );
	}

	private sealed class WalkingHost : IRideVisitorHost
	{
		public long Turn;
		public long ParkTurn => Turn;
		public bool WalksToBoard => true;
		public readonly List<string> Events = new();
		public void OnVisitorOffered( IRideVisitorBridge ride, int guestId ) => Events.Add( $"call {guestId}" );
		public void OnVisitorBoarded( IRideVisitorBridge ride, int guestId ) => Events.Add( $"board {guestId}" );
		public void OnVisitorReleased( IRideVisitorBridge ride, int guestId ) => Events.Add( $"release {guestId}" );
		public void OnVisitorTurnedAway( IRideVisitorBridge ride, int guestId ) => Events.Add( $"away {guestId}" );
	}

	private sealed class RuntimeFixture : IDisposable
	{
		private readonly string root = Path.Combine( Path.GetTempPath(), "opentpw-closed-admission-" + Guid.NewGuid().ToString( "N" ) );
		public OriginalObjectRuntime Runtime { get; }
		public WalkingHost Host { get; } = new();
		public RuntimeFixture()
		{
			Directory.CreateDirectory( root );
			var bytes = Md2ModelFileTests.CreateGeometry();
			BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x42 ), 1 );
			BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x44 ), 0 );
			BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x268 ), 0 );
			BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x78 ), 0x264 );
			foreach ( var at in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( at ), 0 );
			File.WriteAllBytes( Path.Combine( root, "model.md2" ), bytes );
			File.WriteAllBytes( Path.Combine( root, "script.rse" ), new RideVMTests.Asm().Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( Enum.GetNames<RideVariables>() ) );
			var settings = new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1100\nInfo.Name Admission\nInfo.IsChoosable 1\nUpgrades[0].InitCapacity 2" ) } );
			var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/admission.wad", settings, new ObjectShape( new[] { "*" } ), "/model.md2", null, "/script.rse", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
			Runtime = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 1 );
			Runtime.Visitors.Parameters = QueueParameters.Default;
			Runtime.Visitors.EntranceCell = (0, 0);
			Runtime.Visitors.Host = Host;
			var grid = new GuestPathGrid( 1, 1 );
			grid.SetPath( 0, 0, true );
			Runtime.Visitors.RecomputeQueue( grid );
			Runtime.Script!.Advance( Tick );
		}
		public void CallHead( params int[] guests )
		{
			foreach ( var guest in guests ) Assert.IsTrue( Runtime.Visitors.TryJoinQueue( guest ) );
			Runtime.Visitors.UpdateAdmission();
			Assert.AreEqual( guests[0], Runtime.Visitors.CalledGuest );
			Assert.AreEqual( 0, Runtime.GetVariable( RideVariables.VAR_LETMEON ) );
		}
		public void Dispose() { Runtime.Stop(); Directory.Delete( root, true ); }
	}
}
