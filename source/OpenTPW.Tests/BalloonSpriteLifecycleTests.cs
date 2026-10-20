using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class BalloonSpriteLifecycleTests
{
	private static (GuestSimulation Simulation, Guest Guest) Rig()
	{
		var grid = new GuestPathGrid( 16, 16 );
		for ( var x = 0; x < 12; x++ ) grid.SetPath( x, 5, true );
		var simulation = new GuestSimulation( grid, GuestTests.CreateSettings(), 5 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 6, 5 );
		guest.BalloonLife = 127;
		return (simulation, guest);
	}

	private static void Pass( GuestSimulation simulation, int milliseconds )
	{
		simulation.SetBodySpriteClock( milliseconds );
		simulation.ScheduleBodySprites();
	}

	[TestMethod]
	public void DrawOrderKeepsGuestInterleavingAndIndependentReplacementBeforeOrphans()
	{
		var (sim, first) = Rig();
		var second = sim.SpawnInPark( 7, 5 );
		sim.CreateBalloon( first ); sim.BurstBalloon( first );
		var detached = sim.BalloonPlayers.Single();
		sim.CreateBalloon( second ); var secondHeld = sim.BalloonPlayers.Last();
		sim.CreateBalloon( first ); var replacement = sim.BalloonPlayers.Last();
		CollectionAssert.AreEqual( new[] { secondHeld.Identity, detached.Identity, replacement.Identity },
			BalloonRenderer.InDrawOrder( new[] { second, first }, sim.BalloonPlayers ).Select( player => player.Identity ).ToArray() );
		CollectionAssert.AreEqual( new[] { detached.Identity, replacement.Identity, secondHeld.Identity },
			BalloonRenderer.InDrawOrder( new[] { first }, sim.BalloonPlayers ).Select( player => player.Identity ).ToArray() );
		Assert.IsNull( detached.OwnerId ); Assert.AreEqual( first.Id, replacement.OwnerId );
	}

	[TestMethod]
	public void CreateIsInvisibleUntilStrictDueAndBurstPreservesTheHeldDeadline()
	{
		var (sim, guest) = Rig();
		sim.SetBodySpriteClock( 100 ); sim.CreateBalloon( guest );
		var player = sim.BalloonPlayers.Single();
		Assert.IsFalse( player.Visible ); Assert.AreEqual( 162, player.Deadline );
		Pass( sim, 162 ); Assert.IsFalse( player.Visible );
		Pass( sim, 163 ); Assert.IsTrue( player.Visible ); Assert.AreEqual( 0, player.Frame );
		sim.SetBodySpriteClock( 170 ); sim.BurstBalloon( guest );
		Assert.AreEqual( 225, player.Deadline ); Assert.AreEqual( 0, player.Frame ); Assert.AreEqual( 255, player.Alpha );
		Pass( sim, 225 ); Assert.AreEqual( 0, player.Frame );
		Pass( sim, 226 ); Assert.AreEqual( (1, 250, 288), (player.Frame, player.Alpha, player.Deadline) );
	}

	[TestMethod]
	public void AStallAdvancesOneFrameAndTheLastAlphaSurvivesUntilNextDuePass()
	{
		var (sim, guest) = Rig(); sim.CreateBalloon( guest ); sim.BurstBalloon( guest );
		Pass( sim, 1000 ); var player = sim.BalloonPlayers.Single();
		Assert.AreEqual( 250, player.Alpha ); Assert.AreEqual( 1062, player.Deadline );
		Pass( sim, 1062 ); Assert.AreEqual( 250, player.Alpha );
		for ( var i = 1; i < 13; i++ ) { Pass( sim, 1000 + i * 63 ); Assert.AreEqual( 250 - 20 * i, player.Alpha ); }
		Assert.IsTrue( player.Visible ); Assert.AreEqual( 10, player.Alpha );
		Pass( sim, player.Deadline ); Assert.IsTrue( player.Visible );
		Pass( sim, player.Deadline + 1 ); Assert.IsFalse( player.Visible ); Assert.IsTrue( player.Ended );
		Assert.AreEqual( 1, sim.BalloonPlayers.Count );
		Pass( sim, player.Deadline + 1 ); Assert.AreEqual( 0, sim.BalloonPlayers.Count );
	}

	[TestMethod]
	public void SignedDueComparisonPreservesNativeWrapRatherThanElapsedSubtraction()
	{
		var player = new BalloonSpriteState { Deadline = int.MaxValue };
		player.Tick( int.MinValue ); Assert.IsFalse( player.Visible );
		player.Deadline = int.MinValue;
		player.Tick( int.MaxValue ); Assert.IsTrue( player.Visible );
		Assert.AreEqual( unchecked( int.MaxValue + 62 ), player.Deadline );
	}

	[TestMethod]
	public void BurstThenCreateCannotOverwriteDetachedPlayerAndRideDeleteLeavesItAlone()
	{
		var (sim, guest) = Rig(); sim.CreateBalloon( guest ); Pass( sim, 63 );
		var first = sim.BalloonPlayers.Single(); first.HasPosition = true; first.X = 7; first.Lift = 2;
		sim.BurstBalloon( guest ); sim.CreateBalloon( guest );
		Assert.AreEqual( 2, sim.BalloonPlayers.Count ); Assert.IsNull( first.OwnerId );
		Assert.AreNotEqual( first.Identity, sim.BalloonPlayers.Last().Identity );
		sim.DeleteBalloon( guest ); Assert.AreSame( first, sim.BalloonPlayers.Single() );
		Assert.AreEqual( (7f, 2f), (first.X, first.Lift) ); Assert.IsFalse( guest.HasBalloon );
	}

	[TestMethod]
	public void GuardBurstsButBoardingDeletesWithoutChangingLifeOrRandomState()
	{
		var (sim, guest) = Rig(); sim.CreateBalloon( guest );
		var rng = sim.RandomState;
		((IGuardGuests)sim).Hold( guest.Id );
		Assert.IsTrue( sim.BalloonPlayers.Single().PendingBurst );
		Assert.IsFalse( guest.HasBalloon ); Assert.AreEqual( 127, guest.BalloonLife );
		Assert.AreEqual( rng, sim.RandomState );
		var (other, rider) = Rig(); other.CreateBalloon( rider );
		var ride = new RideVisitorBridge( 1, "Ride", RideVisitorKind.Shop, 2, 0, 10 );
		((IRideVisitorHost)other).OnVisitorBoarded( ride, rider.Id );
		Assert.AreEqual( 0, other.BalloonPlayers.Count ); Assert.AreEqual( 127, rider.BalloonLife );
	}

	[TestMethod]
	public void JsonSnapshotKeepsPendingAndDetachedHistoryWithoutAliasingOrEventReplay()
	{
		var (sim, guest) = Rig(); sim.CreateBalloon( guest ); Pass( sim, 63 );
		sim.BurstBalloon( guest ); sim.CreateBalloon( guest );
		var captured = sim.CaptureSnapshot();
		var json = JsonSerializer.Serialize( captured );
		var saved = JsonSerializer.Deserialize<GuestSimulationSnapshot>( json )!;
		var (copy, _) = Rig(); copy.RestoreSnapshot( saved );
		Assert.AreEqual( json, JsonSerializer.Serialize( copy.CaptureSnapshot() ) );
		Pass( copy, 126 ); Assert.AreEqual( 250, copy.BalloonPlayers.First().Alpha );
		Assert.AreEqual( -1, saved.Balloons!.Players.First().BurstStep );
		Assert.AreEqual( -1, captured.Balloons!.Players.First().BurstStep );
		Assert.AreEqual( -1, sim.BalloonPlayers.First().BurstStep );
		Assert.AreEqual( sim.ComputeStateHash(), copy.ComputeStateHash(), "presentation is not legacy gameplay state" );
	}

	[TestMethod]
	public void PresentationSnapshotRejectsMissingClockDuplicateIdentityAndNonFiniteGeometry()
	{
		var (sim, guest) = Rig(); sim.CreateBalloon( guest );
		var saved = sim.CaptureSnapshot();
		Assert.IsFalse( sim.ValidateSnapshot( saved with { BodySpriteClockMilliseconds = null } ).IsValid );
		var node = System.Text.Json.Nodes.JsonNode.Parse( JsonSerializer.Serialize( saved ) )!;
		node["Balloons"]!["Players"]![0]!.AsObject().Remove( "Deadline" );
		Assert.ThrowsException<JsonException>( () => JsonSerializer.Deserialize<GuestSimulationSnapshot>( node.ToJsonString() ) );
		var player = saved.Balloons!.Players.Single();
		Assert.IsFalse( sim.ValidateSnapshot( saved with { Balloons = saved.Balloons with { Players = [player, player] } } ).IsValid );
		player.ColourGuestId++;
		Assert.IsFalse( sim.ValidateSnapshot( saved ).IsValid );
		player.ColourGuestId--;
		player.X = float.NaN;
		Assert.IsFalse( sim.ValidateSnapshot( saved ).IsValid );
		Assert.IsTrue( float.IsFinite( sim.BalloonPlayers.Single().X ) );
	}

	[TestMethod]
	public void OldSnapshotExplicitlyReconstructsHeldOnlyAndDoesNotReplayBurst()
	{
		var (sim, guest) = Rig(); sim.CreateBalloon( guest ); sim.BurstBalloon( guest ); sim.CreateBalloon( guest );
		var saved = sim.CaptureSnapshot() with { Balloons = null };
		var (copy, _) = Rig(); copy.RestoreSnapshot( saved );
		Assert.AreEqual( 1, copy.BalloonPlayers.Count ); Assert.IsFalse( copy.BalloonPlayers.Single().Visible );
		Assert.IsTrue( copy.CaptureSnapshot().Balloons!.HistoryReconstructed );
	}
	private static (Level Level, ParkEconomy Economy, GuestSimulation Guests) Scene()
	{
		Log ??= new();
		var economy = EconomyTestData.Park();
		var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 1 ) { ArrivalsEnabled = false };
		var pool = new ThingPool(); pool.AllocateWorldSetup(); guests.AttachThings( pool ); economy.Staff.AttachThings( pool );
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		void Set( string field, object value ) => typeof( Level ).GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, value );
		Set( "<Park>k__BackingField", ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ) );
		Set( "<Guests>k__BackingField", guests ); Set( "<Objects>k__BackingField", new ParkObjects( null!, new SandboxParkGrid() ) );
		Set( "<Seed>k__BackingField", WorldSeed.Default ); Set( "economyInstances", new Dictionary<OriginalObject, int>() );
		Set( "simulationClock", new FixedStepClock() ); Set( "things", pool );
		return (level, economy, guests);
	}

	[TestMethod]
	public void ActualHostPausesPlayersButLostParkStillSchedulesDetachedBursts()
	{
		var scene = Scene(); var guest = scene.Guests.SpawnInPark( 4, 5 ); guest.BalloonLife = 127;
		scene.Guests.CreateBalloon( guest ); scene.Guests.BurstBalloon( guest );
		var before = JsonSerializer.Serialize( scene.Guests.CaptureSnapshot().Balloons );
		scene.Economy.Speed = GameSpeed.Paused;
		for ( var i = 0; i < 20; i++ ) scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
		Assert.AreEqual( before, JsonSerializer.Serialize( scene.Guests.CaptureSnapshot().Balloons ) );
		scene.Economy.Speed = GameSpeed.Normal;
		typeof( ParkEconomy ).GetProperty( nameof( ParkEconomy.IsBankrupt ) )!.SetValue( scene.Economy, true );
		for ( var i = 0; i < 12; i++ ) scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
		Assert.IsTrue( scene.Guests.BalloonPlayers.Single().BurstStep >= 0 );
	}

	[TestMethod]
	public void ActualHostBatchingWithoutRenderingProducesIdenticalPlayers()
	{
		var a = Scene(); var b = Scene();
		foreach ( var scene in new[] { a, b } )
		{
			var guest = scene.Guests.SpawnInPark( 4, 5 ); guest.BalloonLife = 127;
			scene.Guests.CreateBalloon( guest ); scene.Guests.BurstBalloon( guest );
		}
		for ( var i = 0; i < 24; i++ ) a.Level.AdvanceSimulation( FixedStepClock.TickDuration );
		for ( var i = 0; i < 3; i++ ) b.Level.AdvanceSimulation( 8 * FixedStepClock.TickDuration );
		Assert.AreEqual( JsonSerializer.Serialize( a.Guests.CaptureSnapshot().Balloons ), JsonSerializer.Serialize( b.Guests.CaptureSnapshot().Balloons ) );
		var before = JsonSerializer.Serialize( a.Guests.CaptureSnapshot().Balloons );
		foreach ( var player in a.Guests.BalloonPlayers ) { _ = player.Frame; _ = player.Alpha; _ = player.Visible; }
		a.Level.AdvanceSimulation( 0 );
		Assert.AreEqual( before, JsonSerializer.Serialize( a.Guests.CaptureSnapshot().Balloons ) );
	}

	[TestMethod]
	public void SnapshotRestoresAlphaTenAndHaltedStateAndPresentationDoesNotChangeGameplayHashes()
	{
		var (sim, guest) = Rig(); sim.CreateBalloon( guest ); sim.BurstBalloon( guest );
		ulong Canonical() { var hash = new StateHasher(); sim.AddCanonicalState( hash ); return hash.Value; }
		var canonical = Canonical(); var legacy = sim.ComputeStateHash();
		for ( var i = 1; i <= 13; i++ ) Pass( sim, i * 63 );
		Assert.AreEqual( 10, sim.BalloonPlayers.Single().Alpha );
		foreach ( var halted in new[] { false, true } )
		{
			if ( halted ) Pass( sim, sim.BalloonPlayers.Single().Deadline + 1 );
			var saved = sim.CaptureSnapshot(); var (copy, _) = Rig(); copy.RestoreSnapshot( saved );
			Assert.AreEqual( JsonSerializer.Serialize( saved.Balloons ), JsonSerializer.Serialize( copy.CaptureSnapshot().Balloons ) );
			Assert.AreEqual( halted, copy.BalloonPlayers.Single().Ended );
		}
		Assert.AreEqual( canonical, Canonical() ); Assert.AreEqual( legacy, sim.ComputeStateHash() );
	}

}
