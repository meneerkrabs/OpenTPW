using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A signed arrival group produced by the simulation survives a whole-park file and the candidate's resume stages.
/// Uses the synthetic level/asset fixture shared by the catalogue and smarter-guests save tests; no original game or GPU.
/// </summary>
[TestClass, DoNotParallelize]
public class AdvisorArrivalResumeTests
{
	private UpgradeCatalogueDefaultsTests fixture = null!;
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly Type Fixture = typeof( UpgradeCatalogueDefaultsTests );

	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }

	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();

	private static void Set( object target, string name, object value ) => target.GetType().GetField( name, Private )!.SetValue( target, value );
	private static void Call( object target, string name, params object[] args ) => target.GetType().GetMethod( name, Private )!.Invoke( target, args );

	private (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) World() =>
		((Level, ParkEconomy, OriginalObject, OriginalObject, ParkObjectState))Fixture.GetMethod( "World", Private )!.Invoke( fixture, new object[] { "", 0, false } )!;

	private static Dictionary<string, string> Prepare( (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) world ) =>
		(Dictionary<string, string>)Fixture.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;

	[TestMethod]
	public void AnActualOverLimitArrivalSurvivesWholeParkSaveLoadAndCandidateResume()
	{
		var source = World();
		var assets = Prepare( source );
		var guests = source.Level.Guests!;
		guests.Settings.ExitLevelSeconds = 100000;
		guests.Settings.ExitLevelVarSeconds = 0;
		for ( var index = 0; index <= GuestSimulation.ArrivalParkLimit; index++ )
			guests.SpawnInPark( 1, 1 );
		using var advisor = new AutomaticAdvisor( new AdvisorController( AdvisorSnapshotTests.Balance() ), () => 700 );
		advisor.AttachLevel( source.Level ); // the real subscription, not a direct call to the advisor's event handler
		guests.ArrivalsEnabled = true;
		guests.ArrivalClock = () => 100;
		guests.Tick( 1f / FixedStepClock.TicksPerSecond );
		Assert.AreEqual( GuestSimulation.ArrivalParkLimit + 1, guests.Guests.Count, "the over-limit group creates no extra guest" );
		CollectionAssert.AreEqual( new[] { -1 }, advisor.ArrivalWaves.ToArray(), "the arrival event records limit minus guests without clamping" );
		var liveHash = source.Level.ComputeStateHash();
		var snapshot = source.Level.CaptureResumeSnapshot( advisor, "signed-arrival", assets );
		Assert.AreEqual( liveHash, source.Level.ComputeStateHash(), "capturing the advisor changes no gameplay state" );
		Assert.AreEqual( -1, snapshot.Guests.PendingArrivals );
		Assert.IsTrue( snapshot.Guests.DeliveringArrivals, "the save is taken before the next turn ends the empty group" );
		var path = Path.Combine( Path.GetTempPath(), $"opentpw-signed-arrival-{Guid.NewGuid():N}.otpw" );
		try
		{
			ParkResumeSaveFile.Save( path, snapshot );
			var saved = ParkResumeSaveFile.Load( path );
			CollectionAssert.AreEqual( new[] { -1 }, saved.Advisor!.ArrivalWaves!.ToArray() );
			ParkResumeSaveFile.ValidateSnapshot( saved, source.Level.Objects.Catalog, source.Economy.Settings, source.Economy.Catalog, assets );

			// The same isolated candidate stages used by GameFlow.ResumePark, without its GPU/menu/asset-loader shell.
			// Advisor validation happens before any live park could be replaced; this was the signed-history rejection.
			using var restoredAdvisor = new AutomaticAdvisor( new AdvisorController( AdvisorSnapshotTests.Balance() ), () => 0 );
			restoredAdvisor.ValidateSnapshot( saved.Advisor );
			var economy = ParkSaveFile.Restore( saved.Economy, source.Economy.Settings, source.Economy.Catalog );
			var candidate = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
			var objects = new ParkObjects( null!, new SandboxParkGrid() );
			Set( candidate, "<Objects>k__BackingField", objects );
			Set( candidate, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( economy, saved.StartKind ) );
			Set( candidate, "economyInstances", new Dictionary<OriginalObject, int>() );
			Set( candidate, "resumeState", saved );
			var one = new OriginalObject( source.One.Entry, source.One.Placement, objects.ScriptWorld, seed: 1 ^ objects.ObjectScriptKey, open: true, attractionId: source.One.Runtime.AttractionId );
			var two = new OriginalObject( source.Two.Entry, source.Two.Placement, objects.ScriptWorld, seed: 2 ^ objects.ObjectScriptKey, open: true, attractionId: source.Two.Runtime.AttractionId );
			((List<OriginalObject>)objects.Objects).AddRange( new[] { one, two } );
			Set( objects, "nextSeed", 3 );
			Prepare( (candidate, economy, one, two, economy.Objects.Single( item => item.Id == source.State.Id )) );
			Call( candidate, "ConnectObjectsToEconomy" );
			Call( candidate, "SetupParkGate" );
			Call( candidate, "RestoreResumeSimulation", saved );
			restoredAdvisor.AttachLevel( candidate );
			restoredAdvisor.RestoreSnapshot( saved.Advisor );
			CollectionAssert.AreEqual( new[] { -1 }, restoredAdvisor.ArrivalWaves.ToArray() );
			CollectionAssert.AreEqual( ParkResumeSaveFile.Serialize( saved ),
				ParkResumeSaveFile.Serialize( candidate.CaptureResumeSnapshot( restoredAdvisor, saved.DisplayName, assets, saved.SavedUtc ) ),
				"candidate capture keeps the complete saved state, including the signed pending group and advisor ring" );
			Assert.AreEqual( liveHash, source.Level.ComputeStateHash(), "building and restoring the candidate leaves the source park untouched" );

			var resumedGuests = candidate.Guests!;
			resumedGuests.Settings.ExitLevelSeconds = guests.Settings.ExitLevelSeconds;
			resumedGuests.Settings.ExitLevelVarSeconds = guests.Settings.ExitLevelVarSeconds;
			resumedGuests.ArrivalClock = () => 100;
			for ( var tick = 0; tick < 15; tick++ )
			{
				guests.Tick( 1f / FixedStepClock.TicksPerSecond );
				resumedGuests.Tick( 1f / FixedStepClock.TicksPerSecond );
			}
			Assert.IsFalse( resumedGuests.CaptureSnapshot().DeliveringArrivals, "the next turn ends the negative group without spawning anyone" );
			Assert.AreEqual( GuestSimulation.ArrivalParkLimit + 1, resumedGuests.Guests.Count );
			Assert.AreEqual( guests.ComputeStateHash(), resumedGuests.ComputeStateHash(), "the resumed simulation continues identically" );
			CollectionAssert.AreEqual( advisor.ArrivalWaves.ToArray(), restoredAdvisor.ArrivalWaves.ToArray() );
		}
		finally { File.Delete( path ); }
	}
}
