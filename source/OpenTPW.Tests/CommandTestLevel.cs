using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OpenTPW.Tests;

/// <summary>A level with only the pieces the park tick and the economy commands touch (as LostParkTurnTests builds it).</summary>
internal static class CommandTestLevel
{
	public static Level Create( ParkEconomy economy, bool readOnly = false )
	{
		Log ??= new();
		var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 1 );
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		guests.AttachThings( pool );
		economy.Staff.AttachThings( pool );
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		void Set( string field, object value ) => typeof( Level ).GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, value );
		AttachPark( level, economy );
		Set( "<Guests>k__BackingField", guests );
		Set( "<Objects>k__BackingField", new ParkObjects( null!, new SandboxParkGrid() ) );
		Set( "<Seed>k__BackingField", WorldSeed.Default );
		Set( "economyInstances", new Dictionary<OriginalObject, int>() );
		Set( "simulationClock", new FixedStepClock() );
		Set( "things", pool );
		// a visit's details are not read by the commands: that there is a visit makes the park read-only
		if ( readOnly )
			Set( "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		return level;
	}

	/// <summary>Makes <paramref name="economy"/> the park of a test's <paramref name="level"/>, as the game's HUD and its level share one economy: the HUD's edits go through the level's commands.</summary>
	public static void AttachPark( Level level, ParkEconomy economy ) =>
		typeof( Level ).GetField( "<Park>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level,
			ParkEconomyRuntime.ForResume( economy, economy.Mode == ParkGameMode.InstantAction ? ParkStartKind.InstantAction : ParkStartKind.FullSimulation ) );

	public static ulong Hash( Level level ) => WorldStateHash.Compute( new WorldStateSources { Seed = level.Seed, Economy = level.Park?.Economy, Guests = level.Guests } );
}
