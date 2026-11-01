using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>Shared visitor lifetime, fixed-host schedule and read-only render consumption. No original files or GPU are required.</summary>
[TestClass]
public class VisitorSpriteIntegrationTests
{
	private static GuestSimulation Simulation() => new( GuestTests.CreateCross(), GuestTests.CreateSettings(), 1 ) { ArrivalsEnabled = false };
	private static ulong Canonical( GuestSimulation guests )
	{
		var hash = new StateHasher();
		guests.AddCanonicalState( hash );
		return hash.Value;
	}
	private static void Sample( GuestSimulation simulation, int milliseconds )
	{
		simulation.SetBodySpriteClock( milliseconds );
		simulation.ScheduleBodySprites();
	}

	private static (Level Level, ParkEconomy Economy, GuestSimulation Guests) Scene( bool pooled = true )
	{
		Log ??= new();
		var economy = EconomyTestData.Park();
		var guests = Simulation();
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		if ( pooled )
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
		return (level, economy, guests);
	}

	private static void Set( object target, string field, object value ) => target.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	/// <summary>A real CPU model/animator with one synthetic body node, a real VM/bridge and no graphics object construction.</summary>
	private static (ParkObjects Objects, FlatRideBounceRiders Source, OriginalObjectRuntime Runtime) BodyScene( QueueTests.Rig rig )
	{
		var model = new ModelFile( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) );
		typeof( ModelFile ).GetProperty( nameof( ModelFile.DummyAttributes ) )!.SetValue( model, new[] { new ModelDummyAttribute( 0x800, 1, Array.Empty<byte>() ) } );
		var runtime = (OriginalObjectRuntime)RuntimeHelpers.GetUninitializedObject( typeof( OriginalObjectRuntime ) );
		Set( runtime, "<Model>k__BackingField", model );
		Set( runtime, "<Animator>k__BackingField", new ObjectAnimator( model ) );
		Set( runtime, "<Script>k__BackingField", rig.Script );
		Set( runtime, "<Visitors>k__BackingField", rig.Ride );
		var placed = (OriginalObject)RuntimeHelpers.GetUninitializedObject( typeof( OriginalObject ) );
		Set( placed, "<Runtime>k__BackingField", runtime );
		var objects = new ParkObjects( null!, new SandboxParkGrid() );
		((List<OriginalObject>)typeof( ParkObjects ).GetField( "objects", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( objects )!).Add( placed );
		return (objects, new FlatRideBounceRiders( objects ), runtime);
	}

	private static List<RideVisitorBridge.WalkRecord?> WalkTable( RideVisitorBridge ride ) =>
		(List<RideVisitorBridge.WalkRecord?>)typeof( RideVisitorBridge ).GetField( "walkTable", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( ride )!;

	private static void AddBounce( RideVisitorBridge ride, int guest ) =>
		((List<(int Guest, double Until, double Start)>)typeof( RideVisitorBridge ).GetField( "bouncing", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( ride )!).Add( (guest, 1e9, 0) );

	[TestMethod]
	public void CreationOwnsAnInvisibleWalkBodyBeforeAnyBounceEntryExists()
	{
		var simulation = Simulation();
		simulation.SetBodySpriteClock( 1000 );
		var guest = simulation.SpawnInPark( 4, 5 );
		Assert.AreEqual( VisitorSpriteTemplate.Walk, guest.BodySprite.Player!.ActiveTemplate );
		Assert.AreEqual( VisitorSpriteTemplate.Stand, guest.BodySprite.PendingTemplate!.Value, "state 6 is queued after construction" );
		Assert.AreEqual( 1062, guest.BodySprite.Player.NextDue );
		Assert.IsNull( guest.BodySprite.Player.Output );
		Sample( simulation, 1063 );
		Assert.AreEqual( new VisitorSpriteFrame( 1, 0 ), guest.BodySprite.Player.Output!.Value );
	}

	[TestMethod]
	public void AdmissionRetainsOrDeletesAccordingToRideHandlesSprite()
	{
		foreach ( var retains in new[] { false, true } )
		{
			var rig = new QueueTests.Rig();
			rig.Ride.RideHandlesSprite = retains;
			var guest = rig.Send();
			guest.BodySprite.ApplyPending();
			guest.BodySprite.Player!.ApplyInterval( 137 );
			Sample( rig.Simulation, 63 );
			var before = guest.BodySprite.Player.CaptureSnapshot();
			((IRideVisitorHost)rig.Simulation).OnVisitorBoarded( rig.Ride, guest.Id );
			if ( retains )
			{
				Assert.AreEqual( before, guest.BodySprite.Player!.CaptureSnapshot() );
				Assert.AreEqual( VisitorSpriteTemplate.Stand, guest.BodySprite.PendingTemplate!.Value );
			}
			else
				Assert.IsNull( guest.BodySprite.Player );
		}
	}

	/// <summary>
	/// A shop whose UsageInfo.RideHandlesSprite keeps the body (the Burger Shop and every other shipped shop that sets it) serves its customer in view: through the whole use the guest stands still in its stand
	/// cell with the body it boarded with (tp.exe 0x0050212B: kept, stand template, +0x28 = 1; 0x004FA087 then leaves it where it is), and the script's let-off puts it back among the walking guests.
	/// </summary>
	[TestMethod]
	public void AShopThatKeepsTheBodyServesItsCustomerInViewAtTheStandPoint()
	{
		var rig = new QueueTests.Rig( satisfies: GuestNeeds.Hunger, capacity: 1, takesGuests: true, kind: RideVisitorKind.Shop );
		rig.Ride.RideHandlesSprite = true;
		var guest = rig.Send();
		for ( var tick = 0; tick < 120 * 60 && guest.State != GuestState.Using; tick++ )
			rig.Step();
		Assert.AreEqual( GuestState.Using, guest.State, "the shop took the guest" );
		var body = guest.BodySprite.Player;
		Assert.IsNotNull( body, "the body is kept" );
		var stand = GuestSimulation.StandCellOf( rig.Ride );
		var (x, y) = (guest.X, guest.Y);
		Assert.AreEqual( stand, guest.Navigation.Cell, "the customer stands in the entrance cell, at the stand point it walked to" );
		var servedTicks = 0;
		while ( guest.State == GuestState.Using && servedTicks < 10 * 60 )
		{
			Assert.IsTrue( rig.Simulation.ShowsKeptBody( guest ), $"in view after {servedTicks} ticks" );
			Assert.IsFalse( guest.IsVisible, "still a rider for every other renderer (balloon, thought, pick)" );
			Assert.AreEqual( (x, y), (guest.X, guest.Y), "the body does not move while it is served" );
			Assert.AreSame( body, guest.BodySprite.Player );
			rig.Step();
			servedTicks++;
		}
		Assert.AreEqual( GuestState.ExitingRide, guest.State, "the let-off released the guest" );
		Assert.IsTrue( servedTicks is >= 60 and <= 76, $"the script's WAIT 1000 and the next ride update (up to one 248 ms park turn, tp.exe 0x004E1410) serve the guest ({servedTicks} ticks)" );
		Assert.IsFalse( rig.Simulation.ShowsKeptBody( guest ) );
		Assert.IsTrue( guest.IsVisible );
		Assert.AreSame( body, guest.BodySprite.Player, "the released guest walks on with the same body" );
	}

	[TestMethod]
	public void BounceHeightTimeAndBrokenRequestsDoNotReplacePreviouslyEmittedOutput()
	{
		var rig = new QueueTests.Rig();
		rig.Ride.RideHandlesSprite = true;
		var guest = rig.Send();
		((IRideVisitorHost)rig.Simulation).OnVisitorBoarded( rig.Ride, guest.Id );
		guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Run );
		guest.BodySprite.QueueInterval( 137 );
		guest.BodySprite.ApplyPending();
		Sample( rig.Simulation, 63 );
		Sample( rig.Simulation, 201 );
		var before = guest.BodySprite.Player!.CaptureSnapshot();
		var pose = BounceRiders.Pose( guest.Id, rig.Ride.AttractionId, SVector3.Zero, 0, 987654, 120, 0, true );
		var prepared = FlatRideBounceRiders.ApplyBodyOutput( rig.Simulation, pose );
		Assert.AreEqual( (2, 1), (prepared.Slot, prepared.Step), "broken changes requested program, not displayed output" );
		Assert.AreEqual( before, guest.BodySprite.Player.CaptureSnapshot() );
		Assert.AreEqual( VisitorSpriteTemplate.Stand, guest.BodySprite.PendingTemplate!.Value );
		guest.BodySprite.ApplyPending();
		Assert.AreEqual( before.Output, guest.BodySprite.Player.Output );
		Sample( rig.Simulation, before.NextDue + 1 );
		Assert.AreEqual( new VisitorSpriteFrame( 0, 0 ), guest.BodySprite.Player.Output!.Value );
		Assert.AreEqual( 137, guest.BodySprite.Player.Interval );
	}

	[TestMethod]
	public void MissingBodyBecomesVisibleOnlyAfterARealSchedulerEmission()
	{
		var rig = new QueueTests.Rig();
		var guest = rig.Send();
		((IRideVisitorHost)rig.Simulation).OnVisitorBoarded( rig.Ride, guest.Id );
		rig.Simulation.SetBodySpriteClock( 1000 );
		var pose = BounceRiders.Pose( guest.Id, rig.Ride.AttractionId, SVector3.Zero, 0, 500000, 0, 0, false );
		Assert.IsFalse( FlatRideBounceRiders.ApplyBodyOutput( rig.Simulation, pose ).Visible );
		Assert.AreEqual( 1062, guest.BodySprite.Player!.NextDue, "creation time is the body call, never bounce elapsed" );
		Sample( rig.Simulation, 1062 );
		Assert.IsFalse( FlatRideBounceRiders.ApplyBodyOutput( rig.Simulation, pose ).Visible );
		Sample( rig.Simulation, 1063 );
		var visible = FlatRideBounceRiders.ApplyBodyOutput( rig.Simulation, pose );
		Assert.IsTrue( visible.Visible );
		Assert.AreEqual( (2, 0), (visible.Slot, visible.Step) );
	}

	[TestMethod]
	public void ZeroOneOrTwoRendererCollectionsCannotAdvanceOrApplyBodyState()
	{
		var rig = new QueueTests.Rig();
		rig.Ride.RideHandlesSprite = true;
		var guest = rig.Send();
		((IRideVisitorHost)rig.Simulation).OnVisitorBoarded( rig.Ride, guest.Id );
		var source = new FlatRideBounceRiders( new ParkObjects( null!, new SandboxParkGrid() ) );
		var cache = (Dictionary<int, BounceRiderPose>)typeof( FlatRideBounceRiders ).GetField( "prepared", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( source )!;
		cache[guest.Id] = FlatRideBounceRiders.ApplyBodyOutput( rig.Simulation, new BounceRiderPose( guest.Id, rig.Ride.AttractionId, SVector3.Zero, 0, false, 0 ) );
		var before = guest.BodySprite.CaptureSnapshot();
		foreach ( var renders in new[] { 0, 1, 2 } )
		{
			for ( var render = 0; render < renders; render++ )
			{
				var result = new Dictionary<int, BounceRiderPose>();
				source.Collect( result );
				Assert.AreEqual( cache[guest.Id], result[guest.Id] );
			}
			Assert.AreEqual( before, guest.BodySprite.CaptureSnapshot() );
		}
		Sample( rig.Simulation, 63 );
		Assert.IsNotNull( guest.BodySprite.Player!.Output, "a hidden/no-render body is still scheduled" );
	}

	[TestMethod]
	public void FixedHostSamplingIsIndependentOfTheNumberOfTicksInAnAdvanceBatch()
	{
		var single = Scene();
		var batched = Scene();
		var a = single.Guests.SpawnInPark( 4, 5 );
		var b = batched.Guests.SpawnInPark( 4, 5 );
		for ( var tick = 0; tick < 120; tick++ )
			single.Level.AdvanceSimulation( FixedStepClock.TickDuration );
		for ( var batch = 0; batch < 15; batch++ )
			batched.Level.AdvanceSimulation( 8 * FixedStepClock.TickDuration );
		Assert.AreEqual( a.BodySprite.CaptureSnapshot(), b.BodySprite.CaptureSnapshot() );
		Assert.AreEqual( single.Guests.BodySpriteClockMilliseconds, batched.Guests.BodySpriteClockMilliseconds );
		Assert.AreEqual( Canonical( single.Guests ), Canonical( batched.Guests ) );
		Assert.AreEqual( single.Guests.RandomState, batched.Guests.RandomState );
	}

	[TestMethod]
	public void TheSpritePassFollowsTheRideScriptsAndRunsBeforeTheSweptGuestsApplyTheirPendingRequests()
	{
		foreach ( var pooled in new[] { true, false } )
		{
			var scene = Scene( pooled );
			var guest = scene.Guests.SpawnInPark( 4, 5 );
			guest.State = GuestState.Using; // no state handler can enqueue another request during the fixture
			while ( !scene.Economy.NextTickStartsTurn )
				scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
			guest.BodySprite.Player!.ApplyTemplate( VisitorSpriteTemplate.Run );
			guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Stand );
			// Make this specific opportunity due, without altering the production clock or guest update.
			var before = guest.BodySprite.Player.CaptureSnapshot();
			guest.BodySprite.RestoreSnapshot( guest.BodySprite.CaptureSnapshot() with { Player = before with { NextDue = 0 } } );
			scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
			Assert.AreEqual( VisitorSpriteTemplate.Stand, guest.BodySprite.Player.ActiveTemplate );
			// [BIN:TPW-EXE:0x0054F5FB] the sprite pass is inside the substep after the RSSE tick and before the world update: the swept (pooled) turn applies the pending request after it, so the run program still emits;
			// the non-pooled host's per-tick guest update comes before the pass (it stands for the world update), so the stand program emits
			Assert.AreEqual( pooled ? 2 : 0, guest.BodySprite.Player.Output!.Value.Slot, $"pooled {pooled}" );
		}
	}

	[TestMethod]
	public void PausedSchedulingStopsAndLostParkSchedulingContinuesWithoutApplyingRequests()
	{
		var scene = Scene();
		var guest = scene.Guests.SpawnInPark( 4, 5 );
		guest.State = GuestState.Using;
		guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Run );
		guest.BodySprite.ApplyPending();
		for ( var tick = 0; tick < 8; tick++ )
			scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
		scene.Economy.Speed = GameSpeed.Paused;
		guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Stand );
		var frozen = guest.BodySprite.CaptureSnapshot();
		for ( var tick = 0; tick < 60; tick++ )
			scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
		Assert.AreEqual( frozen, guest.BodySprite.CaptureSnapshot() );
		scene.Economy.Speed = GameSpeed.Normal;
		typeof( ParkEconomy ).GetProperty( nameof( ParkEconomy.IsBankrupt ) )!.SetValue( scene.Economy, true );
		for ( var tick = 0; tick < 60; tick++ )
			scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
		Assert.AreNotEqual( frozen.Player!.NextDue, guest.BodySprite.Player!.NextDue );
		Assert.AreEqual( VisitorSpriteTemplate.Run, guest.BodySprite.Player.ActiveTemplate );
		Assert.AreEqual( VisitorSpriteTemplate.Stand, guest.BodySprite.PendingTemplate!.Value );
	}

	[TestMethod]
	public void HiddenRideBodyDeletionAndLaterPoseRecreationHaveDistinctLifetimes()
	{
		var rig = new QueueTests.Rig();
		rig.Ride.RideHandlesSprite = true;
		var guest = rig.Send();
		((IRideVisitorHost)rig.Simulation).OnVisitorBoarded( rig.Ride, guest.Id );
		guest.BodySprite.Player!.ApplyInterval( 137 );
		guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Cheer );
		rig.Simulation.HideRideBody( guest.Id, rig.Ride.AttractionId );
		Assert.IsNull( guest.BodySprite.Player );
		Assert.AreEqual( VisitorSpriteTemplate.Cheer, guest.BodySprite.PendingTemplate!.Value );
		rig.Simulation.SetBodySpriteClock( 5000 );
		rig.Simulation.RequestRideBody( guest.Id, rig.Ride.AttractionId, VisitorSpriteTemplate.Walk );
		Assert.AreEqual( 5062, guest.BodySprite.Player!.NextDue );
		Assert.AreEqual( 62, guest.BodySprite.Player.Interval );
		Assert.IsNull( guest.BodySprite.Player.Output );
	}

	[TestMethod]
	public void RetainedAndRecreatedCostumeBodiesChooseExactlyOneRenderer()
	{
		foreach ( var retained in new[] { false, true } )
		{
			var rig = new QueueTests.Rig( kind: RideVisitorKind.Shop );
			rig.Ride.AppearanceEffect = GuestAppearance.Costume;
			rig.Ride.RideHandlesSprite = retained;
			var guest = rig.Send();
			var host = (IRideVisitorHost)rig.Simulation;
			host.OnVisitorBoarded( rig.Ride, guest.Id );
			host.OnVisitorReleased( rig.Ride, guest.Id );
			Assert.IsTrue( guest.InCostume );
			Assert.AreEqual( !retained, guest.BodySpriteInCostume );
			GuestRenderer Consumer( bool costume )
			{
				var renderer = (GuestRenderer)RuntimeHelpers.GetUninitializedObject( typeof( GuestRenderer ) );
				typeof( GuestRenderer ).GetProperty( nameof( GuestRenderer.Draws ) )!.SetValue( renderer, (Func<Guest, bool>)(person => person.InCostume == costume) );
				typeof( GuestRenderer ).GetProperty( nameof( GuestRenderer.DrawsRideBody ) )!.SetValue( renderer, (Func<Guest, bool>)(person => person.BodySpriteInCostume == costume) );
				return renderer;
			}
			var kid = Consumer( false );
			var costume = Consumer( true );
			Assert.AreEqual( retained, kid.AcceptsBodyConsumer( guest, true ) );
			Assert.AreEqual( !retained, costume.AcceptsBodyConsumer( guest, true ) );
			Assert.IsFalse( kid.AcceptsBodyConsumer( guest, false ), "ordinary walker selection is unchanged" );
			Assert.IsTrue( costume.AcceptsBodyConsumer( guest, false ) );
		}
	}

	[TestMethod]
	public void BodyPresentationPersistsWithoutAffectingCanonicalOrLegacyHashesOrRandomState()
	{
		var simulation = Simulation();
		var guest = simulation.SpawnInPark( 4, 5 );
		var canonical = Canonical( simulation );
		var legacy = simulation.ComputeStateHash();
		var random = simulation.RandomState;
		guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Cheer );
		guest.BodySprite.QueueInterval( 137 );
		guest.BodySprite.ApplyPending();
		Sample( simulation, 999 );
		guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Program8 );
		guest.BodySpriteInCostume = true;
		var snapshot = JsonSerializer.Deserialize<GuestSimulationSnapshot>( JsonSerializer.Serialize( simulation.CaptureSnapshot() ) )!;
		var restored = Simulation();
		restored.RestoreSnapshot( snapshot );
		Assert.AreEqual( guest.BodySprite.CaptureSnapshot(), restored.Guests.Single().BodySprite.CaptureSnapshot() );
		Assert.IsTrue( restored.Guests.Single().BodySpriteInCostume );
		Assert.AreEqual( 999, restored.BodySpriteClockMilliseconds );
		Assert.AreEqual( canonical, Canonical( simulation ) );
		Assert.AreEqual( legacy, simulation.ComputeStateHash() );
		Assert.AreEqual( random, simulation.RandomState );
		Assert.AreEqual( canonical, Canonical( restored ) );
		var due = guest.BodySprite.Player!.NextDue;
		guest.BodySprite.ApplyPending();
		restored.Guests.Single().BodySprite.ApplyPending();
		Sample( simulation, due );
		Sample( restored, due );
		Assert.AreEqual( new VisitorSpriteFrame( 12, 0 ), guest.BodySprite.Player.Output!.Value, "pending finite program preserves old output at equality" );
		Sample( simulation, due + 1 );
		Sample( restored, due + 1 );
		Assert.AreEqual( guest.BodySprite.CaptureSnapshot(), restored.Guests.Single().BodySprite.CaptureSnapshot() );
		Assert.AreEqual( new VisitorSpriteFrame( 4, 0 ), guest.BodySprite.Player.Output!.Value );
		Assert.AreEqual( canonical, Canonical( restored ) );
		guest.State = GuestState.WalkingAround;
		Assert.AreNotEqual( canonical, Canonical( simulation ), "existing gameplay state remains hashed" );
		guest.State = GuestState.DoingNothing;
		guest.Navigation.X++;
		Assert.AreNotEqual( canonical, Canonical( simulation ), "existing movement remains hashed" );
	}

	[TestMethod]
	public void OlderSnapshotsReconstructAtLoadWhileMalformedPartialHistoryIsRejected()
	{
		var simulation = Simulation();
		simulation.SpawnInPark( 4, 5 );
		var snapshot = simulation.CaptureSnapshot();
		var older = snapshot with { BodySpriteClockMilliseconds = null, Balloons = null, Guests = snapshot.Guests.Select( guest => guest with { BodySprite = null } ).ToArray() };
		var restored = Simulation();
		restored.RestoreSnapshot( older );
		var guest = restored.Guests.Single();
		Assert.IsTrue( guest.BodySpriteHistoryReconstructed );
		Assert.IsNull( guest.BodySprite.Player!.Output );
		Assert.AreEqual( 62, guest.BodySprite.Player.NextDue );
		var before = Canonical( restored );
		Assert.ThrowsException<InvalidDataException>( () => restored.RestoreSnapshot( snapshot with { BodySpriteClockMilliseconds = null } ) );
		Assert.AreEqual( before, Canonical( restored ) );
		var invalidBody = snapshot.Guests[0].BodySprite! with { PendingTemplate = (VisitorSpriteTemplate)999 };
		Assert.ThrowsException<InvalidDataException>( () => restored.RestoreSnapshot( snapshot with { Guests = new[] { snapshot.Guests[0] with { BodySprite = invalidBody } } } ) );
		Assert.AreEqual( before, Canonical( restored ) );
	}

	[TestMethod]
	public void RemovedIdentityAndParkTeardownCannotLeakBodyHistory()
	{
		var simulation = Simulation();
		var old = simulation.SpawnInPark( 4, 5 );
		old.BodySprite.QueueInterval( 137 );
		old.State = GuestState.Gone;
		simulation.TickTurn( 1f / 60 );
		Assert.IsNull( old.BodySprite.Player );
		Assert.AreEqual( 0, old.BodySprite.PendingInterval );
		var fresh = simulation.SpawnInPark( 4, 5 );
		Assert.AreEqual( old.Id, fresh.Id );
		Assert.IsFalse( ReferenceEquals( old, fresh ) );
		Assert.AreEqual( 62, fresh.BodySprite.Player!.Interval );
		Assert.IsNull( fresh.BodySprite.Player.Output );
		simulation.ClearBodySprites();
		Assert.AreEqual( new VisitorSpriteStateSnapshot( null, null, 0 ), fresh.BodySprite.CaptureSnapshot() );
	}

	[TestMethod]
	public void IntegerWalkIntervalsUseTheRunPredicateWithoutChangingMovementInputs()
	{
		Assert.AreEqual( 61, GuestSimulation.BodyIntervalForStep( 0, 0, 26214, 0, true ) );
		Assert.AreEqual( 30, GuestSimulation.BodyIntervalForStep( 0, 0, 26214, 0, false ) );
		Assert.AreEqual( 185, GuestSimulation.BodyIntervalForStep( 0, 0, 8704, 0, true ), "the quotient is not rounded to binary32 (which would produce 186)" );
		Assert.AreEqual( 185, GuestSimulation.BodyIntervalForStep( 0, 0, 4352, 0, false ) );
		Assert.AreEqual( 250, GuestSimulation.BodyIntervalForStep( 0, 0, 100, 0, false ) );
		Assert.AreEqual( 0, GuestSimulation.BodyIntervalForStep( 0, 0, 0, 0, true ) );
		Assert.AreEqual( 0, GuestSimulation.BodyIntervalForStep( 0, 0, 50000, 0, true ), "int32 squared-distance overflow is not widened" );
	}
	[TestMethod]
	public void GuardHoldDeletesAndSetDownCreatesANewBodyBeforeItsRoute()
	{
		var simulation = Simulation();
		var guest = simulation.SpawnInPark( 4, 5 );
		guest.BodySprite.Player!.ApplyInterval( 137 );
		guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Cheer );
		((IGuardGuests)simulation).Hold( guest.Id );
		Assert.IsNull( guest.BodySprite.Player );
		Assert.AreEqual( VisitorSpriteTemplate.Cheer, guest.BodySprite.PendingTemplate!.Value );
		simulation.SetBodySpriteClock( 5000 );
		((IGuardGuests)simulation).Release( guest.Id, (4, 5) );
		Assert.AreEqual( VisitorSpriteTemplate.Walk, guest.BodySprite.Player!.ActiveTemplate );
		Assert.AreEqual( 62, guest.BodySprite.Player.Interval );
		Assert.AreEqual( 5062, guest.BodySprite.Player.NextDue );
		Assert.IsNull( guest.BodySprite.Player.Output );
	}

	[TestMethod]
	public void RideRemovalUsesTerminalStandWithoutRecreatingARetainedBody()
	{
		foreach ( var retained in new[] { false, true } )
		{
			var rig = new QueueTests.Rig();
			rig.Ride.RideHandlesSprite = retained;
			var guest = rig.Send();
			guest.BodySprite.Player!.ApplyInterval( 137 );
			((IRideVisitorHost)rig.Simulation).OnVisitorBoarded( rig.Ride, guest.Id );
			rig.Simulation.SetBodySpriteClock( 1000 );
			rig.Simulation.Unregister( rig.Ride );
			Assert.AreEqual( GuestState.DoingNothing, guest.State );
			Assert.AreEqual( retained ? 137 : 62, guest.BodySprite.Player!.Interval );
			Assert.AreEqual( retained ? 62 : 1062, guest.BodySprite.Player.NextDue );
			Assert.AreEqual( VisitorSpriteTemplate.Stand, guest.BodySprite.PendingTemplate!.Value );
		}
	}

	[TestMethod]
	public void HiddenWalkFollowingBounceDeletesTheBodyAndItsEarlierPreparedPose()
	{
		foreach ( var action in new[] { 1, 2, 4 } )
		{
			var rig = new QueueTests.Rig();
			rig.Ride.RideHandlesSprite = true;
			var guest = rig.Send();
			((IRideVisitorHost)rig.Simulation).OnVisitorBoarded( rig.Ride, guest.Id );
			Sample( rig.Simulation, 63 );
			var scene = BodyScene( rig );
			AddBounce( rig.Ride, guest.Id );
			WalkTable( rig.Ride ).Add( new( guest.Id, 1, 1, 1, 1, action, 1, 2, 0, 0 ) { Heading = 7 } );
			scene.Source.Prepare( rig.Simulation );
			var poses = new Dictionary<int, BounceRiderPose>();
			scene.Source.Collect( poses );
			Assert.IsNull( guest.BodySprite.Player );
			Assert.IsFalse( poses.ContainsKey( guest.Id ), "the later explicit hide wins over BOUNCE's earlier output" );
			WalkTable( rig.Ride ).Clear();
			scene.Source.Prepare( rig.Simulation );
			Assert.IsNotNull( guest.BodySprite.Player );
			Assert.IsNull( guest.BodySprite.Player.Output, "later BOUNCE recreates; it cannot reuse the deleted output" );
		}
	}

	[TestMethod]
	public void UnknownWalkAndMissingBounceNodeDoNotDeleteOrRestartSharedHistory()
	{
		var rig = new QueueTests.Rig();
		rig.Ride.RideHandlesSprite = true;
		var guest = rig.Send();
		((IRideVisitorHost)rig.Simulation).OnVisitorBoarded( rig.Ride, guest.Id );
		guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Run );
		guest.BodySprite.ApplyPending();
		Sample( rig.Simulation, 63 );
		var scene = BodyScene( rig );
		AddBounce( rig.Ride, guest.Id );
		WalkTable( rig.Ride ).Add( new( guest.Id, 1, 1, 1, 1, 99, 1, 2, 0, 0 ) { Heading = 7 } );
		var before = guest.BodySprite.CaptureSnapshot();
		scene.Source.Prepare( rig.Simulation );
		var poses = new Dictionary<int, BounceRiderPose>();
		scene.Source.Collect( poses );
		Assert.IsTrue( poses.ContainsKey( guest.Id ), "an unknown action makes no body call and leaves the prior BOUNCE pose" );
		Assert.AreEqual( before, guest.BodySprite.CaptureSnapshot() );
		var last = poses[guest.Id];
		WalkTable( rig.Ride ).Clear();
		typeof( ModelFile ).GetProperty( nameof( ModelFile.DummyAttributes ) )!.SetValue( scene.Runtime.Model, Array.Empty<ModelDummyAttribute>() );
		scene.Source.Prepare( rig.Simulation );
		poses.Clear();
		scene.Source.Collect( poses );
		// tp.exe's bounce pass skips a slot whose node the model lacks (0x00557B26), so the kept body is not placed again and its sprite stays where it was last placed (0x00542010)
		Assert.AreEqual( 1, poses.Count, "the kept body is held where the bounce last placed it" );
		Assert.AreEqual( last.Feet, poses[guest.Id].Feet );
		Assert.AreEqual( before, guest.BodySprite.CaptureSnapshot(), "holding makes no body request" );
	}

	[TestMethod]
	public void ZeroElapsedHostPauseDoesNotCreateRequestOrUpdateWalkHeadings()
	{
		var rig = new QueueTests.Rig();
		var guest = rig.Send();
		((IRideVisitorHost)rig.Simulation).OnVisitorBoarded( rig.Ride, guest.Id );
		var body = BodyScene( rig );
		var walker = new RideVisitorBridge.WalkRecord( guest.Id, 1, 1, 1, 1, 6, 1, 2, 0, 0 ) { Heading = 7 };
		WalkTable( rig.Ride ).Add( walker );
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "simulationClock", new FixedStepClock() );
		Set( level, "<Objects>k__BackingField", body.Objects );
		Set( level, "<Guests>k__BackingField", rig.Simulation );
		var before = guest.BodySprite.CaptureSnapshot();
		level.AdvanceSimulation( 0 );
		Assert.AreEqual( before, guest.BodySprite.CaptureSnapshot() );
		Assert.AreEqual( 7, walker.Heading );
		// A positive sub-tick elapsed runs no tick and so no pose phase either: the phase ends every fixed tick, so the bodies follow the ticks, not the frames (docs/DETERMINISM.md, "Frame batching").
		level.AdvanceSimulation( 0.001 );
		Assert.AreEqual( before, guest.BodySprite.CaptureSnapshot() );
		Assert.AreEqual( 7, walker.Heading );
		// The tick's pose phase creates and requests without inventing a scheduler opportunity.
		typeof( Level ).GetMethod( "PrepareRideBodyPoses", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, null );
		Assert.IsNotNull( guest.BodySprite.Player );
		Assert.AreEqual( VisitorSpriteTemplate.Cheer, guest.BodySprite.Player.ActiveTemplate );
		Assert.AreNotEqual( 7, walker.Heading );
		Assert.IsNull( guest.BodySprite.Player.Output );
		before = guest.BodySprite.CaptureSnapshot();
		var heading = walker.Heading;
		level.AdvanceSimulation( 0 );
		Assert.AreEqual( before, guest.BodySprite.CaptureSnapshot() );
		Assert.AreEqual( heading, walker.Heading );
	}

	[TestMethod]
	public void WalkHeadingAndGeometryStayPresentationOnlyWhilePreparedFramesUseTheSharedPlayer()
	{
		var rig = new QueueTests.Rig();
		var guest = rig.Send();
		((IRideVisitorHost)rig.Simulation).OnVisitorBoarded( rig.Ride, guest.Id );
		var scene = BodyScene( rig );
		WalkTable( rig.Ride ).Add( new( guest.Id, 1, 1, 1, 1, 6, 1, 2, 0, 0 ) { Heading = 7 } );
		var saved = JsonSerializer.Serialize( rig.Ride.CaptureSnapshot() );
		var hash = Canonical( rig.Simulation );
		var legacy = rig.Simulation.ComputeStateHash();
		var oldPoses = new Dictionary<int, BounceRiderPose>();
		FlatRideBounceRiders.CollectWalkers( oldPoses, scene.Runtime, _ => System.Numerics.Matrix4x4.Identity, new() );
		var newPoses = new Dictionary<int, BounceRiderPose>();
		FlatRideBounceRiders.CollectWalkers( newPoses, scene.Runtime, _ => System.Numerics.Matrix4x4.Identity, new(),
			( id, template ) => rig.Simulation.RequestRideBody( id, rig.Ride.AttractionId, (VisitorSpriteTemplate)template ),
			id => rig.Simulation.HideRideBody( id, rig.Ride.AttractionId ) );
		Assert.AreEqual( oldPoses[guest.Id].Feet, newPoses[guest.Id].Feet );
		Assert.AreEqual( oldPoses[guest.Id].Sector, newPoses[guest.Id].Sector );
		Assert.IsFalse( oldPoses[guest.Id].UsesSpriteState, "provider-free geometry helpers retain their fallback" );
		Assert.IsTrue( oldPoses[guest.Id].Visible );
		Assert.IsTrue( newPoses[guest.Id].UsesSpriteState );
		Assert.IsFalse( newPoses[guest.Id].Visible, "a supplied provider with no FRAME never falls back" );
		Assert.AreEqual( saved, JsonSerializer.Serialize( rig.Ride.CaptureSnapshot() ) );
		Assert.AreEqual( hash, Canonical( rig.Simulation ) );
		Assert.AreEqual( legacy, rig.Simulation.ComputeStateHash() );
		WalkTable( rig.Ride )[0]!.State = 3;
		Assert.AreNotEqual( hash, Canonical( rig.Simulation ), "the existing walker state remains hashed" );
	}

	[DataTestMethod]
	[DataRow( 5, 4 )]
	[DataRow( 6, 12 )]
	public void PreparedWalkUsesFiniteBodyFramesAndDoesNotReplayTheCompletedProgram( int action, int slot )
	{
		var rig = new QueueTests.Rig();
		var guest = rig.Send();
		((IRideVisitorHost)rig.Simulation).OnVisitorBoarded( rig.Ride, guest.Id );
		guest.BodySprite.ApplyPending(); // Clear the wrapper request left by boarding before the missing body is created.
		var scene = BodyScene( rig );
		WalkTable( rig.Ride ).Add( new( guest.Id, 1, 1, 1, 1, action, 1, 2, 0, 0 ) );
		var poses = new Dictionary<int, BounceRiderPose>();
		scene.Source.Prepare( rig.Simulation );
		scene.Source.Collect( poses );
		Assert.IsTrue( poses[guest.Id].UsesSpriteState );
		Assert.IsFalse( poses[guest.Id].Visible );
		guest.BodySprite.ApplyPending();
		var hash = Canonical( rig.Simulation );
		var random = rig.Simulation.RandomState;
		for ( var index = 0; index < 7; index++ )
		{
			Sample( rig.Simulation, guest.BodySprite.Player!.NextDue + 1 );
			scene.Source.Prepare( rig.Simulation );
			poses.Clear();
			scene.Source.Collect( poses );
			var pose = poses[guest.Id];
			Assert.IsTrue( pose.Visible );
			Assert.AreEqual( index < 4 ? slot : 0, pose.Slot );
			Assert.AreEqual( index < 4 ? index : 0, pose.Step );
			var before = guest.BodySprite.CaptureSnapshot();
			scene.Source.Collect( poses );
			scene.Source.Collect( poses );
			Assert.AreEqual( pose, poses[guest.Id] );
			Assert.AreEqual( before, guest.BodySprite.CaptureSnapshot() );
			Assert.IsNull( guest.BodySprite.PendingTemplate, "matching active identity does not restart the finite tail" );
		}
		Assert.AreEqual( hash, Canonical( rig.Simulation ) );
		Assert.AreEqual( random, rig.Simulation.RandomState );
	}

	[TestMethod]
	public void WalkAfterBounceKeepsLastOutputUntilItsQueuedTemplateEmitsAndBodyJsonContinues()
	{
		var rig = new QueueTests.Rig();
		rig.Ride.RideHandlesSprite = true;
		var guest = rig.Send();
		((IRideVisitorHost)rig.Simulation).OnVisitorBoarded( rig.Ride, guest.Id );
		guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Run );
		guest.BodySprite.ApplyPending();
		Sample( rig.Simulation, guest.BodySprite.Player!.NextDue + 1 );
		var scene = BodyScene( rig );
		AddBounce( rig.Ride, guest.Id );
		WalkTable( rig.Ride ).Add( new( guest.Id, 1, 1, 1, 1, 6, 1, 2, 0, 0 ) );
		var due = guest.BodySprite.Player.NextDue;
		scene.Source.Prepare( rig.Simulation );
		var poses = new Dictionary<int, BounceRiderPose>();
		scene.Source.Collect( poses );
		Assert.AreEqual( VisitorSpriteTemplate.Cheer, guest.BodySprite.PendingTemplate, "WALK's later request wins" );
		Assert.AreEqual( 2, poses[guest.Id].Slot, "queued cheer must not replace the already emitted run frame" );
		Assert.AreEqual( 0, poses[guest.Id].Step );
		Assert.AreEqual( due, guest.BodySprite.Player.NextDue );
		// Round-trip the supported body-history record; the host clock remains in the same epoch.
		var saved = JsonSerializer.Deserialize<VisitorSpriteStateSnapshot>( JsonSerializer.Serialize( guest.BodySprite.CaptureSnapshot() ) )!;
		guest.BodySprite.ApplyPending();
		Sample( rig.Simulation, due );
		scene.Source.Prepare( rig.Simulation );
		scene.Source.Collect( poses );
		Assert.AreEqual( 2, poses[guest.Id].Slot, "due equality retains output even after template application" );
		Sample( rig.Simulation, due + 1 );
		scene.Source.Prepare( rig.Simulation );
		scene.Source.Collect( poses );
		var expected = poses[guest.Id];
		var expectedHistory = guest.BodySprite.CaptureSnapshot();
		Assert.AreEqual( 12, expected.Slot );
		Assert.AreEqual( 0, expected.Step );
		guest.BodySprite.RestoreSnapshot( saved );
		guest.BodySprite.ApplyPending();
		Sample( rig.Simulation, due + 1 );
		scene.Source.Prepare( rig.Simulation );
		scene.Source.Collect( poses );
		Assert.AreEqual( expected, poses[guest.Id] );
		Assert.AreEqual( expectedHistory, guest.BodySprite.CaptureSnapshot() );
	}

	[TestMethod]
	public void ExistingNavigationStepQueuesOnlyForWalkingAndIsNeverSteppedOrDrawnTwice()
	{
		foreach ( var outcome in new[] { WalkResult.Walking, WalkResult.Arrived, WalkResult.Stuck } )
		foreach ( var run in new[] { false, true } )
		{
			var simulation = Simulation();
			var guest = simulation.SpawnInPark( 4, 5 );
			guest.MaxSpeed = 7733;
			guest.MaxForce = 15466;
			guest.ToiletBonus = run ? -1 : 0; // the original predicate reads the low unsigned word
			guest.SmoothedSpeed = run ? 0 : 999; // not the run predicate
			var navigation = guest.Navigation;
			if ( outcome == WalkResult.Arrived )
				navigation.SetDestination( navigation.X, navigation.Y, [] );
			else
			{
				var target = GuestNavigation.Centre( outcome == WalkResult.Walking ? (10, 5) : (1, 1) );
				Assert.AreEqual( outcome == WalkResult.Walking, navigation.Navigate( simulation.Grid.Cells, target.X, target.Y ) );
			}
			guest.BodySprite.Player!.ApplyTemplate( VisitorSpriteTemplate.Stand );
			guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Cheer );
			guest.BodySprite.QueueInterval( 137 );
			var before = guest.BodySprite.CaptureSnapshot();
			var (beforeX, beforeY) = (navigation.X, navigation.Y);
			var expected = ParkSaveFile.NavigationData.Capture( navigation ).Restore();
			Assert.AreEqual( outcome, expected.Step( guest.MaxSpeed, guest.MaxForce, simulation.Grid.Cells, expected.IsOnWalk, _ => (0, 0) ) );
			var random = simulation.RandomState;
			var actual = (WalkResult)typeof( GuestSimulation ).GetMethod( "Step", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( simulation, new object[] { guest } )!;
			Assert.AreEqual( outcome, actual );
			Assert.AreEqual( JsonSerializer.Serialize( ParkSaveFile.NavigationData.Capture( expected ) ), JsonSerializer.Serialize( ParkSaveFile.NavigationData.Capture( navigation ) ), "exactly the existing single navigation step" );
			Assert.AreEqual( random, simulation.RandomState );
			if ( outcome == WalkResult.Walking )
			{
				Assert.AreEqual( run ? VisitorSpriteTemplate.Run : VisitorSpriteTemplate.Walk, guest.BodySprite.PendingTemplate!.Value );
				Assert.AreEqual( GuestSimulation.BodyIntervalForStep( beforeX, beforeY, navigation.X, navigation.Y, run ), guest.BodySprite.PendingInterval );
				Assert.AreEqual( before.Player, guest.BodySprite.Player.CaptureSnapshot(), "a walk only queues; no emission or application" );
			}
			else
				Assert.AreEqual( before, guest.BodySprite.CaptureSnapshot(), "arrived and stuck paths do not overwrite pending body history" );
		}
	}

}
