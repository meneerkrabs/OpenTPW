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
		runtime.Close();
		Assert.IsFalse( runtime.IsOpen );
		var before = ParkResumeJson.Serialize( runtime.CaptureSnapshot() );
		Assert.IsFalse( ride.PresentForBoarding( 101 ) );
		Assert.IsFalse( ride.PresentForBoarding( 101 ), "Repeated presentation must not write an offer either." );
		Assert.AreEqual( 0, runtime.GetVariable( RideVariables.VAR_LETMEON ) );
		Assert.AreEqual( 101, ride.CalledGuest, "Immediate close-time withdrawal is a separate residual." );
		CollectionAssert.AreEqual( new[] { 101, 102 }, ride.Queue.ToArray() );
		CollectionAssert.AreEqual( new[] { "call 101" }, fixture.Host.Events );
		CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( runtime.CaptureSnapshot() ), "Refusal must not mutate either the bridge or the script." );
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

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void LiveStandPointSubturnRechecksRuntimeCloseBeforeWritingTheOffer( bool close )
	{
		using var fixture = new RuntimeFixture();
		var runtime = fixture.Runtime;
		var ride = runtime.Visitors;
		// Reuse the queue fixture's grid and real simulation, but register the actual runtime's bridge and idle script.
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
		Assert.AreEqual( 0, runtime.GetVariable( RideVariables.VAR_LETMEON ) );
		// A synthetic busy slot keeps state 13 at the actual stand point while the natural queue walk finishes.
		Assert.IsTrue( runtime.SetVariable( nameof( RideVariables.VAR_LETMEON ), 9999 ) );
		for ( var step = 0; step < 4000 && (guest.State != GuestState.Boarding || guest.HasWaypoint); step++ ) Step();
		Assert.IsFalse( guest.HasWaypoint, "The real queue walk must reach the stand point." );
		Assert.AreEqual( GuestState.Boarding, guest.State );
		Assert.IsTrue( guest.Navigation.Arrived && !guest.Navigation.Stuck );
		Assert.AreEqual( GuestSimulation.StandPoint( ride ), (guest.Navigation.TargetX, guest.Navigation.TargetY) );
		Assert.AreEqual( GuestSimulation.StandCellOf( ride ), guest.Navigation.Cell );
		if ( simulation.NextTickStartsTurn ) Step();
		Assert.IsFalse( simulation.NextTickStartsTurn );
		Assert.IsTrue( runtime.SetVariable( nameof( RideVariables.VAR_LETMEON ), 0 ) );
		if ( close ) runtime.Close();
		var turn = simulation.ParkTurn;
		var before = ride.CaptureSnapshot();
		Assert.IsFalse( before.LetMeOnWritten );
		Assert.AreEqual( GuestState.Boarding, guest.State, "Close occurs before any successful presentation." );
		simulation.Tick( Tick ); // No ride update: only the guest's subturn can present the offer.
		Assert.AreEqual( turn, simulation.ParkTurn );
		Assert.AreEqual( before.LastAdmissionTurn, ride.CaptureSnapshot().LastAdmissionTurn );
		Assert.AreEqual( close ? GuestState.Boarding : GuestState.WaitingToBoard, guest.State );
		Assert.AreEqual( close ? 0 : guest.Id, runtime.GetVariable( RideVariables.VAR_LETMEON ) );
		Assert.AreEqual( guest.Id, ride.CalledGuest );
		Assert.AreEqual( (0, 0), (ride.BoardedTotal, ride.Dismissed) );
		CollectionAssert.AreEqual( new[] { guest.Id }, ride.Queue.ToArray() );
		if ( close )
			CollectionAssert.AreEqual( ParkResumeJson.Serialize( before ), ParkResumeJson.Serialize( ride.CaptureSnapshot() ), "The live refusal leaves bridge state untouched." );
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
