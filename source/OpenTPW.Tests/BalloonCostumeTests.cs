using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Balloons and costumes (ECON-044, docs/GUESTS.md): the use routine's appearance step (tp.exe <c>0x004FE1E0</c>), the balloon put away on a ride and given
/// back after it (<c>0x00501DB0</c> states 16 and 15), its life counted down on the needs phase (<c>0x00501650</c>), the balloon prank (<c>0x004FF0F5</c>) and
/// the challenge percentages (<c>0x004D1746</c>).
/// </summary>
[TestClass]
public class BalloonCostumeTests
{
	private sealed class Rig
	{
		public readonly GuestPathGrid Grid = new( 16, 16 );
		public readonly GuestSettings Settings = GuestTests.CreateSettings();
		public readonly GuestSimulation Simulation;

		public Rig()
		{
			for ( var x = 0; x < 12; x++ )
				Grid.SetPath( x, 5, true );
			Settings.ExitLevelSeconds = 10_000;
			Simulation = new GuestSimulation( Grid, Settings, 5 ) { ArrivalsEnabled = false };
		}

		public IRideVisitorHost Host => Simulation;

		public static RideVisitorBridge Shop( int id, int appearance ) =>
			new( id, "Shop", RideVisitorKind.Shop, 2, 0, 10 ) { AppearanceEffect = appearance, ExitCell = (6, 5) };

		/// <summary>One visit: boarded (state 16), then released (the use routine, then state 15).</summary>
		public void Visit( Guest guest, RideVisitorBridge shop )
		{
			Host.OnVisitorBoarded( shop, guest.Id );
			Host.OnVisitorReleased( shop, guest.Id );
		}
	}

	[TestMethod]
	public void ABalloonLivesQualityTimes255Over100Within25And255()
	{
		Assert.AreEqual( 127, GuestAppearance.BalloonLife( GuestAppearance.BuiltQualityOfGoods ), "the build's quality 50: 12750 / 100" );
		Assert.AreEqual( 25, GuestAppearance.BalloonLife( 0 ) );
		Assert.AreEqual( 25, GuestAppearance.BalloonLife( 9 ), "2295 / 100 = 22 is raised to 25" );
		Assert.AreEqual( 25, GuestAppearance.BalloonLife( 10 ) );
		Assert.AreEqual( 255, GuestAppearance.BalloonLife( 100 ) );
		Assert.AreEqual( 255, GuestAppearance.BalloonLife( 200 ), "510 is cut to 255" );
		Assert.AreEqual( 25, GuestAppearance.BalloonLife( 256 ), "the quality is a byte" );
	}

	[TestMethod]
	public void ABalloonsLifeFollowsTheShopsOwnQualityOfGoods()
	{
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		var balloons = Rig.Shop( 1, GuestAppearance.Balloon );
		balloons.QualityOfGoods = 100;
		rig.Visit( guest, balloons );
		Assert.AreEqual( (true, 255), (guest.HasBalloon, guest.BalloonLife), "the shop window's top quality step 100 (0x004B0AA0) gives a 255-phase balloon (0x004FE615)" );
		balloons.QualityOfGoods = 0;
		rig.Visit( guest, balloons );
		Assert.AreEqual( 25, guest.BalloonLife, "quality 0: the floor of 25" );
	}

	[TestMethod]
	public void ABalloonShopGivesABalloonThatARideputsAwayAndGivesBack()
	{
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		var balloons = Rig.Shop( 1, GuestAppearance.Balloon );
		var ride = Rig.Shop( 2, 0 );
		rig.Visit( guest, balloons );
		Assert.AreEqual( (true, 127), (guest.HasBalloon, guest.BalloonLife) );
		rig.Host.OnVisitorBoarded( ride, guest.Id );
		Assert.AreEqual( (false, 127), (guest.HasBalloon, guest.BalloonLife), "the balloon is put away on the ride (state 16)" );
		rig.Host.OnVisitorReleased( ride, guest.Id );
		Assert.IsTrue( guest.HasBalloon, "and given back after it (state 15)" );
		guest.BalloonLife = 3;
		rig.Visit( guest, balloons );
		Assert.AreEqual( (true, 127), (guest.HasBalloon, guest.BalloonLife), "a second balloon replaces the first and its life" );
		guest.BalloonLife = 0;
		guest.HasBalloon = false;
		rig.Visit( guest, ride );
		Assert.IsFalse( guest.HasBalloon, "no life, no balloon back" );
	}

	[TestMethod]
	public void ACostumeShopPutsACostumeOnAndTakesItBack()
	{
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		var costumes = Rig.Shop( 1, GuestAppearance.Costume );
		rig.Visit( guest, costumes );
		Assert.IsTrue( guest.InCostume );
		rig.Visit( guest, Rig.Shop( 2, 0 ) );
		Assert.IsTrue( guest.InCostume, "another shop leaves it on" );
		rig.Visit( guest, costumes );
		Assert.IsFalse( guest.InCostume, "\"Customer returning a costume\"" );
	}

	[TestMethod]
	public void TheBalloonLosesALifeOnEveryNeedsPhaseAndGoesAtZero()
	{
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		guest.HasBalloon = true;
		guest.BalloonLife = 3;
		var changes = new List<(long Turn, int Life)>();
		var life = guest.BalloonLife;
		for ( var tick = 0; tick < 60 * 60 && guest.HasBalloon; tick++ )
		{
			rig.Simulation.Tick( GuestSnapshotTests.Tick );
			if ( guest.BalloonLife != life )
				changes.Add( (rig.Simulation.ParkTurn, life = guest.BalloonLife) );
		}
		Assert.AreEqual( 3, changes.Count, string.Join( ", ", changes ) );
		CollectionAssert.AreEqual( new[] { 2, 1, 0 }, changes.Select( change => change.Life ).ToArray() );
		Assert.IsTrue( changes.All( change => NeedsUpdate.IsPhase( (uint)change.Turn, guest.Id ) ), "only on the guest's needs phase" );
		Assert.AreEqual( 4, changes[1].Turn - changes[0].Turn, "every fourth park turn" );
		Assert.IsFalse( guest.HasBalloon, "the balloon goes at 0" );
	}

	[TestMethod]
	public void ABalloonPranksterBurstsAnotherGuestsBalloonOnItsCell()
	{
		var rig = new Rig();
		var prankster = rig.Simulation.SpawnInPark( 6, 5 );
		var victim = rig.Simulation.SpawnInPark( 6, 5 );
		var pranks = new List<GuestDecision.Prank>();
		rig.Simulation.Pranked += ( guest, prank ) => pranks.Add( prank );
		var prank = typeof( GuestSimulation ).GetMethod( "Prank", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )!;
		Assert.IsFalse( (bool)prank.Invoke( rig.Simulation, [prankster, GuestDecision.Prank.Balloon, LitterType.None] )!, "no balloon on the cell, no prank" );
		prankster.HasBalloon = true;
		prankster.BalloonLife = 50;
		Assert.IsFalse( (bool)prank.Invoke( rig.Simulation, [prankster, GuestDecision.Prank.Balloon, LitterType.None] )!, "its own balloon does not count" );
		victim.HasBalloon = true;
		victim.BalloonLife = 50;
		var happiness = prankster.Happiness;
		Assert.IsTrue( (bool)prank.Invoke( rig.Simulation, [prankster, GuestDecision.Prank.Balloon, LitterType.None] )! );
		Assert.AreEqual( (false, 50), (victim.HasBalloon, victim.BalloonLife), "the balloon bursts; its life stays (0x004FE950)" );
		CollectionAssert.AreEqual( new[] { GuestDecision.Prank.Balloon }, pranks );
		Assert.AreEqual( MeterMath.Add( happiness, 1f ), prankster.Happiness, 1e-6f );
		rig.Visit( victim, Rig.Shop( 2, 0 ) );
		Assert.IsTrue( victim.HasBalloon, "a burst balloon with life comes back after the next ride" );
	}

	[TestMethod]
	public void TheBalloonPranksterTakesTheBalloonOfTheGuestThatEnteredTheCellLast()
	{
		// the cell's thing list is head-inserted on every cell change (0x0050B6A0, 0x004D91F0) and the prank takes the first balloon holder on it (0x004FF1CD)
		var rig = new Rig();
		var prankster = rig.Simulation.SpawnInPark( 6, 5 );
		var older = rig.Simulation.SpawnInPark( 6, 5 );
		var newer = rig.Simulation.SpawnInPark( 6, 5 );
		foreach ( var holder in new[] { older, newer, prankster } )
			(holder.HasBalloon, holder.BalloonLife) = (true, 50);
		var prank = typeof( GuestSimulation ).GetMethod( "Prank", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )!;
		Assert.IsTrue( (bool)prank.Invoke( rig.Simulation, [prankster, GuestDecision.Prank.Balloon, LitterType.None] )! );
		Assert.AreEqual( (true, false), (older.HasBalloon, newer.HasBalloon), "the later entrant loses its balloon" );
	}

	[TestMethod]
	public void AHeldGuestLosesItsBalloonAndTheStateIsSaved()
	{
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		var other = rig.Simulation.SpawnInPark( 7, 5 );
		rig.Visit( other, Rig.Shop( 1, GuestAppearance.Balloon ) );
		rig.Visit( other, Rig.Shop( 3, GuestAppearance.Costume ) );
		guest.HasBalloon = true;
		guest.BalloonLife = 9;
		( (IGuardGuests)rig.Simulation ).Hold( guest.Id );
		Assert.AreEqual( (false, 9), (guest.HasBalloon, guest.BalloonLife) );

		var snapshot = rig.Simulation.CaptureSnapshot();
		var restored = new Rig();
		restored.Simulation.RestoreSnapshot( snapshot );
		var copy = restored.Simulation.Guests.Single( item => item.Id == other.Id );
		Assert.AreEqual( (true, 127, true), (copy.HasBalloon, copy.BalloonLife, copy.InCostume) );
		Assert.AreEqual( rig.Simulation.ComputeStateHash(), restored.Simulation.ComputeStateHash() );
	}

	[TestMethod]
	public void TheChallengePercentagesCountTheGuestsInThePark()
	{
		var rig = new Rig();
		var economy = new GuestEconomyBridge( () => null!, rig.Simulation );
		Assert.AreEqual( (0, 0), (economy.KidsWithBalloonsPercent, economy.KidsWithCostumesPercent), "0 without guests" );
		var guests = Enumerable.Range( 0, 3 ).Select( _ => rig.Simulation.SpawnInPark( 6, 5 ) ).ToList();
		guests[0].HasBalloon = true;
		guests[0].BalloonLife = 1;
		guests[1].InCostume = true;
		guests[2].InCostume = true;
		Assert.AreEqual( (33, 66), (economy.KidsWithBalloonsPercent, economy.KidsWithCostumesPercent), "100 × count / in-park guests, truncated" );
	}

	[TestMethod]
	public void ABalloonFloatsOverAStandingGuestAndDipsBehindAWalkingOne()
	{
		Assert.AreEqual( 1f, BalloonRenderer.Lift( 5f, 5f, 5f, 5f, 0, 0 ), 1e-6f, "standing, bob entry 0" );
		Assert.AreEqual( 1f + 0.2f * 1.5f, BalloonRenderer.Lift( 5f, 5f, 5f, 5f, 7, 3 ), 1e-6f, "entry (7 + 3) % 20 = 10, the top of the bob" );
		Assert.AreEqual( 1f + 0.02f * 1.5f, BalloonRenderer.Lift( 5f, 5f, 5f, 5f, 20, 1 ), 1e-6f, "the entry wraps at 20" );
		// A trail 0.05 cells behind on x and 0.05 on y: (0.5 + 0.5) units / 0.5 lower it by 2.
		Assert.AreEqual( -1f, BalloonRenderer.Lift( 5f, 5f, 5.05f, 4.95f, 0, 0 ), 1e-5f );
	}

	[TestMethod]
	public void TheTrailingPointLagsBehindTheDrawnGuest()
	{
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 2, 5 );
		Assert.AreEqual( (guest.X, guest.Y), rig.Simulation.DrawPositionBefore( guest, BalloonRenderer.TrailTurns ), "before any step, the guest's own point" );
		for ( var tick = 0; tick < 60 * 60; tick++ )
		{
			rig.Simulation.Tick( GuestSnapshotTests.Tick );
			if ( guest.IsMoving && MathF.Abs( guest.X - guest.StepFromX ) + MathF.Abs( guest.Y - guest.StepFromY ) > 1e-3f )
				break;
		}
		Assert.IsTrue( guest.IsMoving, "the guest walks" );
		var now = rig.Simulation.DrawPositionBefore( guest, 0f );
		Assert.AreEqual( (guest.DrawX, guest.DrawY), now, "no lag: the drawn point" );
		var (trailX, trailY) = rig.Simulation.DrawPositionBefore( guest, BalloonRenderer.TrailTurns );
		var step = (X: guest.X - guest.StepFromX, Y: guest.Y - guest.StepFromY);
		Assert.AreEqual( now.X - step.X * BalloonRenderer.TrailTurns, trailX, 1e-4f, "0.35 of the step behind" );
		Assert.AreEqual( now.Y - step.Y * BalloonRenderer.TrailTurns, trailY, 1e-4f );
	}
}
