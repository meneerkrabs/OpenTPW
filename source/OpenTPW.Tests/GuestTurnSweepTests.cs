using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The guests' thing update runs in the park turn's sweep only (tp.exe 0x00516380 calls 0x0050B360 for each thing, once per eight 31 ms substeps, 0x0054F668), newest thing first, and the turn ends with
/// the destroys and the arrival update (DET-014): the ticks between two turns run no state handler, no walk step and no draw of a guest.
/// </summary>
[TestClass]
public class GuestTurnSweepTests
{
	private const float Dt = 1f / FixedStepClock.TicksPerSecond;

	[TestMethod]
	public void AGuestsStateHandlerRunsOnTheTickThatStartsAParkTurnAndNotBetween()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 5, 5 );
		// a leaving guest with no walk to finish: its handler would send it on at once
		guest.State = GuestState.ExitingRide;
		guest.HasWaypoint = false;
		guest.StateTimer = 0;
		var ticks = 0;
		var draws = simulation.RandomState;
		while ( !simulation.NextTickStartsTurn )
		{
			simulation.Tick( Dt );
			ticks++;
			Assert.AreEqual( GuestState.ExitingRide, guest.State, $"tick {ticks}: no state handler runs between two turns" );
		}
		Assert.IsTrue( ticks >= 10, $"{ticks} ticks stand between two park turns" );
		Assert.AreEqual( draws, simulation.RandomState, "and none draws from the world generator" );
		Assert.AreEqual( ticks * (double)Dt, guest.TimeInParkSeconds!.Value, 1e-9, "the seconds still count every tick" );
		simulation.Tick( Dt );
		Assert.AreNotEqual( GuestState.ExitingRide, guest.State, "the turn's tick runs it" );
	}

	[TestMethod]
	public void TheGuestTheArrivalMakesIsNotUpdatedInTheTurnThatMadeIt()
	{
		var settings = GuestTests.CreateSettings();
		settings.ArrivalTimeBetween = 0;
		var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 3 ) { ArrivalClock = () => 1000 };
		// the vehicle-less arrival update: a call starts the group, the next turn's call makes a guest
		var turns = 0;
		Guest? made = null;
		for ( var tick = 0; tick < 200 && made == null; tick++ )
		{
			simulation.Tick( Dt );
			if ( simulation.Guests.Count > 0 )
			{
				made = simulation.Guests[0];
				turns = (int)simulation.ParkTurn;
			}
		}
		Assert.IsNotNull( made, "a group arrived" );
		Assert.AreEqual( 2, turns, "the first turn's update started the group, the second made its first guest" );
		Assert.AreEqual( 0.0, made!.TimeInParkSeconds, "the arrival update closes the turn: the guest it makes is not updated in that turn" );
		simulation.Tick( Dt );
		Assert.AreEqual( (double)Dt, made.TimeInParkSeconds!.Value, 1e-9, "the next tick counts its first seconds" );
	}

	[TestMethod]
	public void ThePoolsOwnStatesAndTheHarnessWaitKeepTheirPerTickStep()
	{
		// [EXT:pools-swim] a change room's exit wait counts seconds on every tick, as before: the original has no such state
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 5, 5 );
		guest.State = GuestState.ExitingRide;
		guest.StateTimer = 2.5f * Dt;
		simulation.Tick( Dt );
		simulation.Tick( Dt );
		Assert.AreEqual( GuestState.ExitingRide, guest.State );
		simulation.Tick( Dt );
		Assert.AreEqual( GuestState.DoingNothing, guest.State, "the timer ran out on the third tick, not at the next park turn" );
	}

	/// <summary>
	/// [BIN:TPW-EXE:0x004F9D77] the wander's dead end stamps only a person that is no staff thing (kinds 4 to 8 go to the patrol area search 0x00506F30), and every other write of +0x198 clears it: the staff
	/// carry no stranded mark, so the guests' stranding (<see cref="Guest.StrandedMark"/>) has no counterpart on <see cref="StaffMember"/>.
	/// </summary>
	[TestMethod]
	public void AStaffMemberCarriesNoStrandedMark()
	{
		var names = typeof( StaffMember ).GetProperties( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ).Select( property => property.Name )
			.Concat( typeof( StaffMember ).GetFields( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ).Select( field => field.Name ) );
		Assert.IsFalse( names.Any( name => name.Contains( "Strand", StringComparison.OrdinalIgnoreCase ) ) );
		Assert.IsTrue( typeof( Guest ).GetProperty( nameof( Guest.StrandedMark ) ) != null );
	}
}
