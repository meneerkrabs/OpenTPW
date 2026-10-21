using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The world update of a lost park (tp.exe 0x00516380, DET-013): only the thing walk stops; the turn's tail, the arrival update among it, goes on.</summary>
[TestClass]
public class LostParkTurnTests
{
	/// <summary>A level with only the pieces the simulation tick touches (as ThingSweepTests builds it).</summary>
	private static (Level Level, ParkEconomy Economy, GuestSimulation Guests) CreateLevel()
	{
		Log ??= new();
		var economy = EconomyTestData.Park();
		var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 1 );
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
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

	/// <summary>
	/// A lost park (world state 4) still runs the turn's tail: tp.exe 0x00516380 jumps over the thing walk only (0x51639E), and the arrival update (0x4D7B20 → 0x4CF3E0) tests no world state.
	/// </summary>
	[TestMethod]
	public void ALostParkStillRunsItsArrivalUpdateEveryTurn()
	{
		var (level, economy, guests) = CreateLevel();
		typeof( ParkEconomy ).GetProperty( nameof( ParkEconomy.IsBankrupt ) )!.SetValue( economy, true );
		Assert.IsTrue( economy.IsBankrupt );
		var before = guests.Guests.Count;
		var turn = economy.Turn;
		for ( var tick = 0; tick < 14880 / 10 * 4; tick++ )
			level.AdvanceSimulation( FixedStepClock.TickDuration );
		Assert.IsTrue( economy.Turn - turn > 100, "the turn counter goes on" );
		Assert.IsTrue( guests.Guests.Count > before, "the arrival update ran in the lost park: a group arrived" );
	}
	[TestMethod]
	public void LostParkTurnsStillAdvanceDisplayAgeWhileGuestSecondsRemainStopped()
	{
		var (level, economy, guests) = CreateLevel();
		guests.ArrivalsEnabled = false;
		var guest = guests.SpawnInPark( 5, 5 );
		typeof( ParkEconomy ).GetProperty( nameof( ParkEconomy.IsBankrupt ) )!.SetValue( economy, true );
		var turn = economy.Turn;
		for ( var tick = 0; tick < 400; tick++ )
		{
			level.AdvanceSimulation( FixedStepClock.TickDuration );
			Assert.AreEqual( economy.Turn - turn, guest.DisplayTimeInParkTurns, "the existing host still begins each swept turn, even when it skips the guest walk" );
			Assert.AreEqual( 0d, guest.TimeInParkSeconds, "this display-only fix must not advance gameplay duration in a lost park" );
		}
	}

}
