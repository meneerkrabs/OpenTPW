using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>GUEST-016: what a staff member's walk step leaves for his sprite, in the original's own terms (tp.exe 0x004FA2A0, 0x004D41D0, 0x004FA870).</summary>
[TestClass]
public class StaffWalkPresentationTests
{
	private sealed class Fixture
	{
		public readonly GuestPathGrid Grid = new( 20, 20 );
		public readonly ParkEconomy Park = EconomyTestData.Park();
		public readonly StaffWorldSimulation World;
		public readonly StaffMember Member;

		public Fixture( StaffType type = StaffType.Handyman )
		{
			for ( var x = 0; x < 16; x++ )
				Grid.SetPath( x, 5, true );
			Park.OpenPark();
			World = new StaffWorldSimulation( Grid, () => Park.Staff.Members, _ => null, (0, 5) );
			Park.StaffWorld = World;
			Member = EconomyTestData.HireBest( Park, type );
			World.Synchronize();
		}

		public void Turn() => EconomyTestData.AdvanceToTurn( Park, Park.Turn + 1 );
	}

	[TestMethod]
	public void AWalkingMembersStepAsksForTheIntervalOfItsOwnLengthAndFacesTheWayItWent()
	{
		var f = new Fixture();
		var steps = 0;
		for ( var turn = 0; turn < 300 && steps < 20; turn++ )
		{
			var before = f.Member.Navigation is { } start ? (start.X, start.Y) : (0, 0);
			f.Turn();
			var navigation = f.Member.Navigation;
			// the walk step queues the interval (0 asks for none) on the wrapper; the next turn's pre-update applies it
			if ( navigation == null || f.Member.Sprite.PendingInterval <= 0 )
				continue;
			steps++;
			Assert.AreEqual( GuestSimulation.BodyIntervalForStep( before.Item1, before.Item2, navigation.X, navigation.Y, false ), f.Member.Sprite.PendingInterval, "the interval comes from the step's own displacement" );
			var (dx, dy) = ((double)navigation.X - before.Item1, (double)navigation.Y - before.Item2);
			if ( dx != 0 || dy != 0 )
				Assert.AreEqual( 1.0, Math.Sqrt( f.Member.HeadingX * (double)f.Member.HeadingX + f.Member.HeadingY * (double)f.Member.HeadingY ), 1e-5 );
		}
		Assert.IsTrue( steps > 0, "a patrolling handyman steps" );
	}

	[TestMethod]
	public void AWalkStepQueuesTheWalkUnlessTheSourceRestartsThereAndAStateEntryQueuesItsOwnTemplate()
	{
		var f = new Fixture();
		for ( var turn = 0; turn < 300 && f.Member.Sprite.PendingInterval <= 0; turn++ )
			f.Turn();
		Assert.IsTrue( f.Member.Sprite.PendingInterval > 0, "the member walked" );
		Assert.IsTrue( f.Member.TurnTick >= 0, "the turn's start point was taken" );
		f.Member.Job = StaffJob.Patrol;
		var method = typeof( StaffWorldSimulation ).GetMethod( "SetJob", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic )!;
		method.Invoke( null, [f.Park, f.Member, StaffJob.Idle, f.Park.Turn] );
		Assert.AreEqual( StaffSpriteEvents.Stand, f.Member.Sprite.PendingTemplate, "a state entry queues the state's own template (0x005054D0 state 0 queues 3)" );
	}

	[TestMethod]
	public void TheTurnStartPointIsTheSavedWalkPositionBeforeTheWalkSteps()
	{
		var f = new Fixture();
		f.Turn();
		f.Turn();
		var navigation = f.Member.Navigation!;
		Assert.AreNotEqual( (navigation.X, navigation.Y), (f.Member.TurnFromX, f.Member.TurnFromY), "a member that walked has moved from his turn's start point" );
		var previous = (navigation.X, navigation.Y);
		f.Turn();
		Assert.AreEqual( previous, (f.Member.TurnFromX, f.Member.TurnFromY), "the next turn starts where the last one ended" );
	}
}
