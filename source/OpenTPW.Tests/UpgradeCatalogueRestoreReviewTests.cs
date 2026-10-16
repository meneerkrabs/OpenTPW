using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class UpgradeCatalogueRestoreReviewTests
{
	private UpgradeCatalogueDefaultsTests fixture = null!;
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }
	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();
	private static void Set( object target, string name, object value ) => target.GetType().GetField( name, Private )!.SetValue( target, value );
	private (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) World( int tier = 0 ) =>
		((Level, ParkEconomy, OriginalObject, OriginalObject, ParkObjectState))typeof( UpgradeCatalogueDefaultsTests ).GetMethod( "World", Private )!.Invoke( fixture, new object[] { "", tier, false } )!;
	private static Dictionary<string, string> Prepare( (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) world ) =>
		(Dictionary<string, string>)typeof( UpgradeCatalogueDefaultsTests ).GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;

	[DataTestMethod, DataRow( "seed" ), DataRow( "authored" ), DataRow( "stopped" )]
	public void ActualResumeRestoresSavedOperatingValuesOverDerivedTierMetadata( string state )
	{
		var source = World();
		if ( state == "authored" ) source.One.Runtime.Simulate( .001 );
		typeof( UpgradeCatalogueDefaultsTests ).GetMethod( "Complete", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { source, 1 } );
		if ( state == "stopped" ) source.One.Runtime.Stop();
		var assets = Prepare( source );
		var saved = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( source.Level.CaptureResumeSnapshot( null, "review", assets ) ) );
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
		Assert.AreEqual( 3, candidate.One.Runtime.GetVariable( RideVariables.VAR_CAPACITY ), "The candidate derives tier1 before VM restore." );
		typeof( Level ).GetMethod( "RestoreResumeSimulation", Private )!.Invoke( candidate.Level, new object[] { saved } );
		Assert.AreEqual( 3, candidate.One.Runtime.GetVariable( RideVariables.VAR_CAPACITY ), "the saved VM holds the completion's InitCapacity" );
		Assert.AreEqual( 20, candidate.One.Runtime.GetVariable( RideVariables.VAR_DURATION ) );
		Assert.AreEqual( 3, candidate.One.Visitors.Capacity );
		Assert.AreEqual( 200f, candidate.One.Visitors.Parameters.QueueWaitTimeConstant );
		Assert.AreEqual( 1, candidate.One.Runtime.CatalogueTier );
		Assert.AreEqual( 0, candidate.Two.Runtime.CatalogueTier );
		// Stopped scripts are intentionally absent from the existing script-world snapshot; their slots are not serialized.
		var recaptured = candidate.Level.CaptureResumeSnapshot( null, "review", assets );
		CollectionAssert.AreEqual( saved.Scripts.Scripts.Select( item => item.ScriptId ).ToArray(), recaptured.Scripts.Scripts.Select( item => item.ScriptId ).ToArray() );
		Assert.AreEqual( saved.Runtimes[0].State.Stopped, recaptured.Runtimes[0].State.Stopped );
		Assert.AreEqual( source.Level.Guests!.ComputeStateHash(), candidate.Level.Guests!.ComputeStateHash() );
	}
}
