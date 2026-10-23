using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The saved simulation's restore a part a step (<c>Level.RestoreResumeSimulationSteps</c>, docs/COOP.md "The game's join"): the parts in the one call's order, the same park as
/// the one call, and a refused snapshot refused at the first step. Built on the level fixture of the upgrade-catalogue tests.
/// </summary>
[TestClass, DoNotParallelize]
public class ParkResumeSimulationStepsTests
{
	private UpgradeCatalogueDefaultsTests fixture = null!;
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly Type Fixture = typeof( UpgradeCatalogueDefaultsTests );

	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }

	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();

	private static void Set( object target, string name, object value ) => target.GetType().GetField( name, Private )!.SetValue( target, value );

	private (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) World() =>
		((Level, ParkEconomy, OriginalObject, OriginalObject, ParkObjectState))Fixture.GetMethod( "World", Private )!.Invoke( fixture, new object[] { "", 0, false } )!;

	private static Dictionary<string, string> Prepare( (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) world ) =>
		(Dictionary<string, string>)Fixture.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;

	/// <summary>A candidate of <paramref name="saved"/> as the resume builds it up to the simulation's restore: its objects placed with their saved seeds, its economy linked, its gate set up.</summary>
	private static (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) Candidate(
		(Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) source, ParkResumeSnapshot saved )
	{
		var restoredEconomy = ParkSaveFile.Restore( saved.Economy, EconomyTestData.Settings(), EconomyTestData.Catalog() );
		var candidateLevel = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		var objects = new ParkObjects( null!, new SandboxParkGrid() );
		Set( candidateLevel, "<Objects>k__BackingField", objects );
		Set( candidateLevel, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( restoredEconomy, saved.StartKind ) );
		Set( candidateLevel, "economyInstances", new Dictionary<OriginalObject, int>() );
		Set( candidateLevel, "resumeState", saved );
		var one = new OriginalObject( source.One.Entry, source.One.Placement, objects.ScriptWorld, seed: 1 ^ objects.ObjectScriptKey, open: true, attractionId: source.One.Runtime.AttractionId );
		var two = new OriginalObject( source.Two.Entry, source.Two.Placement, objects.ScriptWorld, seed: 2 ^ objects.ObjectScriptKey, open: true, attractionId: source.Two.Runtime.AttractionId );
		((List<OriginalObject>)objects.Objects).AddRange( new[] { one, two } );
		Set( objects, "nextSeed", 3 );
		var candidate = (Level: candidateLevel, Economy: restoredEconomy, One: one, Two: two, State: restoredEconomy.Objects.Single( item => item.Id == source.State.Id ));
		Prepare( candidate );
		typeof( Level ).GetMethod( "ConnectObjectsToEconomy", Private )!.Invoke( candidateLevel, null );
		typeof( Level ).GetMethod( "SetupParkGate", Private )!.Invoke( candidateLevel, null );
		return candidate;
	}

	private static IEnumerator<bool> Steps( Level level, ParkResumeSnapshot saved ) =>
		((IEnumerable<bool>)typeof( Level ).GetMethod( "RestoreResumeSimulationSteps", Private )!.Invoke( level, new object[] { saved } )!).GetEnumerator();

	private static string Saved( Level level, Dictionary<string, string> assets ) =>
		Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( level.CaptureResumeSnapshot( null, "steps", assets ) ) );

	[TestMethod]
	public void TheRestoreOverStepsRestoresTheParkTheOneCallRestores()
	{
		var source = World();
		source.One.Runtime.Simulate( .001 );
		var assets = Prepare( source );
		var saved = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( source.Level.CaptureResumeSnapshot( null, "steps", assets ) ) );

		var whole = Candidate( source, saved );
		typeof( Level ).GetMethod( "RestoreResumeSimulation", Private )!.Invoke( whole.Level, new object[] { saved } );

		var stepped = Candidate( source, saved );
		var restoredRuntime = System.Text.Json.JsonSerializer.Serialize( whole.One.Runtime.CaptureSnapshot() );
		Assert.AreNotEqual( restoredRuntime, System.Text.Json.JsonSerializer.Serialize( stepped.One.Runtime.CaptureSnapshot() ), "the candidate's object runs from its fresh state before the restore" );
		using var steps = Steps( stepped.Level, saved );
		var yielded = 0;
		while ( steps.MoveNext() )
		{
			yielded++;
			// the first step restores the objects' runtimes, the steps after it the rest in the one call's order
			if ( yielded == 1 )
				Assert.AreEqual( restoredRuntime, System.Text.Json.JsonSerializer.Serialize( stepped.One.Runtime.CaptureSnapshot() ), "the first step restores the objects' runtimes" );
		}
		// nine parts: the runtimes, the scripts, the routing, the guests, the weather, the staff, the coasters, the other rides and the drawn tracks
		Assert.AreEqual( 8, yielded, "a part a step, the last ending the enumeration" );
		Assert.AreEqual( Saved( whole.Level, assets ), Saved( stepped.Level, assets ), "the steps restore the park the one call restores" );
		Assert.AreEqual( whole.Level.Guests!.ComputeStateHash(), stepped.Level.Guests!.ComputeStateHash() );
		Assert.AreEqual( source.Level.Guests!.ComputeStateHash(), stepped.Level.Guests.ComputeStateHash() );
	}

	[TestMethod]
	public void ASnapshotTheObjectsCannotTakeIsRefusedAtTheFirstStepWithNothingRestored()
	{
		var source = World();
		source.One.Runtime.Simulate( .001 );
		var assets = Prepare( source );
		var saved = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( source.Level.CaptureResumeSnapshot( null, "steps", assets ) ) );
		var candidate = Candidate( source, saved );
		var fresh = System.Text.Json.JsonSerializer.Serialize( candidate.One.Runtime.CaptureSnapshot() );
		var refused = saved with { Runtimes = saved.Runtimes.Take( 1 ).ToList() };
		using var steps = Steps( candidate.Level, refused );
		var error = Assert.ThrowsException<InvalidDataException>( () => steps.MoveNext() );
		Assert.AreEqual( "The saved park cannot be rebuilt with the current objects.", error.Message );
		Assert.AreEqual( fresh, System.Text.Json.JsonSerializer.Serialize( candidate.One.Runtime.CaptureSnapshot() ), "a refused restore restores nothing" );
	}
}
