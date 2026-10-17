using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using OpenTPW.Scripting;

namespace OpenTPW.Tests;

/// <summary>
/// The persistence of the script mods (docs/SCRIPT-MODS.md, "Saves and versioning"): the park-resume section, its strict validation, the restore, the world hash, and a resumed park that continues like the
/// uninterrupted one.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ScriptModsSaveTests
{
	/// <summary>A mod that touches its state in every hook, draws with <c>rng</c> and changes what the guests do.</summary>
	internal const string Counter =
		"function bump(k)\n  if has(state, k) then\n    state[k] = state[k] + 1\n  else\n    state[k] = 1\n  end\nend\n" +
		"function scoreAttraction(guest, c, prev)\n  bump('s')\n  return prev + rng(3)\nend\n" +
		"function joinQueue(guest, a, prev)\n  bump('j')\n  return prev or (a.queueCount < a.queueLimit and rng(5) == 0)\nend\n" +
		"function leaveQueue(guest, a, prev)\n  bump('l')\n  return prev or a.waitedTurns > 40\nend\n" +
		"function wanderGoal(guest, prev)\n  bump('w')\n  return prev\nend\n";
	internal static readonly string[] CounterHooks = { "scoreAttraction", "joinQueue", "leaveQueue", "wanderGoal" };

	private TempScriptMods mods = null!;

	[TestInitialize]
	public void Initialize() => mods = new TempScriptMods();

	[TestCleanup]
	public void Cleanup() => mods.Dispose();

	private ModSet CounterSet() => mods.Add( "community.counter", Counter, CounterHooks ).Resolve();

	private static string Json<T>( T value ) => Encoding.UTF8.GetString( ParkResumeJson.Serialize( value ) );

	// ---- Capture and the section's shape ------------------------------------------------------

	[TestMethod]
	public void ASimulationWithoutScriptModsCapturesNoSection()
	{
		var park = new ParkScenario( new WorldSeed( 1 ) );
		park.Run( 3000 );
		Assert.IsNull( ScriptModsSave.Capture( park.Guests ) );
		Assert.IsNull( park.Guests.ScriptMods );
	}

	[TestMethod]
	public void TheSectionHoldsEachModsCanonicalStateCountersFlagAndVersionsKeyedById()
	{
		using var scope = OpenTpwExtensions.Use( mods.Add( "community.counter", Counter, CounterHooks ).Add( "community.idle", "function wanderGoal(guest, prev)\n  return prev\nend\n", "wanderGoal" ).Resolve() );
		var park = new ParkScenario( new WorldSeed( 1 ) );
		park.Run( GuestReport.TicksForDays( 30 ) );
		var section = ScriptModsSave.Capture( park.Guests )!;
		CollectionAssert.AreEqual( new[] { "community.counter", "community.idle" }, section.Mods.Keys.ToArray() );
		var counter = section.Mods["community.counter"];
		Assert.AreEqual( ScriptLimits.BehaviourVersion, counter.BehaviourVersion );
		Assert.AreEqual( ScriptLimits.ScriptApiVersion, counter.ApiVersion );
		Assert.AreEqual( ScriptState.Serialize( park.Guests.ScriptMods!.Mods[0].State ), counter.State );
		StringAssert.StartsWith( counter.State, "{\"" );
		Assert.IsTrue( counter.State.Contains( "\"s\":" ), counter.State );
		Assert.AreEqual( "{}", section.Mods["community.idle"].State );
		Assert.IsFalse( counter.Disabled );
		var round = ParkResumeJson.Deserialize<ScriptModsSaveData>( ParkResumeJson.Serialize( section ) );
		Assert.AreEqual( Json( section ), Json( round ), "the section survives the codec" );
		Assert.IsNull( ScriptModsSave.ShapeProblem( round, RecordsOf( park ) ) );
	}

	private static IReadOnlyList<ParkModRecord> RecordsOf( ParkScenario park ) =>
		ParkResumeMods.Capture( OpenTpwExtensions.Active ) ?? throw new AssertFailedException( "no mod is recorded" );

	[TestMethod]
	public void TheSectionIsRefusedWhenAnythingInItIsOffTheContract()
	{
		using var scope = OpenTpwExtensions.Use( CounterSet() );
		var park = new ParkScenario( new WorldSeed( 1 ) );
		park.Run( GuestReport.TicksForDays( 3 ) );
		var section = ScriptModsSave.Capture( park.Guests )!;
		var records = RecordsOf( park );
		var good = section.Mods["community.counter"];
		string? Problem( ScriptModSaveData entry ) => ScriptModsSave.ShapeProblem( new ScriptModsSaveData { Mods = new Dictionary<string, ScriptModSaveData> { ["community.counter"] = entry } }, records );

		Assert.IsNull( Problem( good ) );
		StringAssert.Contains( Problem( good with { BehaviourVersion = 0 } )!, "script behaviour version 0, but this build runs version 1" );
		StringAssert.Contains( Problem( good with { ApiVersion = "2.0.0" } )!, "script API 2.0.0" );
		StringAssert.Contains( Problem( good with { State = " " + good.State } )!, "not in canonical form" );
		StringAssert.Contains( Problem( good with { State = "{\"a\":" } )!, "state of community.counter is invalid" );
		StringAssert.Contains( Problem( good with { State = "[1]" } )!, "invalid" );
		StringAssert.Contains( Problem( good with { State = "{\"a\":1.5}" } )!, "invalid" );
		StringAssert.Contains( Problem( good with { State = null! } )!, "no state" );
		StringAssert.Contains( Problem( good with { RuntimeErrors = -1 } )!, "negative" );
		StringAssert.Contains( Problem( good with { RuntimeErrors = 3, Disabled = true } )!, "disabled with only 3" );
		StringAssert.Contains( Problem( good with { RuntimeErrors = 16 } )!, "is not disabled" );
		StringAssert.Contains( Problem( good with { RuntimeErrors = 10, BudgetFailures = 10, Disabled = true } )!, "above the limit" );
		Assert.IsNull( Problem( good with { RuntimeErrors = 5, BudgetFailures = 6, InvalidResults = 5, Disabled = true } ), "16 failures and the flag agree" );
		Assert.IsNull( Problem( good with { RuntimeErrors = 15 } ) );

		StringAssert.Contains( ScriptModsSave.ShapeProblem( new ScriptModsSaveData { Mods = new Dictionary<string, ScriptModSaveData> { ["community.other"] = good } }, records )!, "does not record" );
		StringAssert.Contains( ScriptModsSave.ShapeProblem( new ScriptModsSaveData { Mods = new Dictionary<string, ScriptModSaveData>() }, records )!, "no mod" );
		StringAssert.Contains( ScriptModsSave.ShapeProblem( new ScriptModsSaveData { Mods = new Dictionary<string, ScriptModSaveData> { ["community.counter"] = null! } }, records )!, "empty entry" );
		Assert.IsNull( ScriptModsSave.ShapeProblem( null, records ), "no section is fine on its own; the active set decides whether one is needed" );
	}

	[TestMethod]
	public void TheSectionNeedsTheIdentitySectionAndTheIdentitySectionAcceptsScriptModsInPlaceOfTheExtensionRecord()
	{
		using var scope = OpenTpwExtensions.Use( CounterSet() );
		var park = new ParkScenario( new WorldSeed( 1 ) );
		park.Run( GuestReport.TicksForDays( 3 ) );
		var records = RecordsOf( park );
		var identity = SmarterGuestsSave.Capture( park.Guests )!;
		var ids = park.Guests.Guests.Select( guest => guest.Id ).ToArray();
		Assert.IsNull( SmarterGuestsSave.ShapeProblem( identity, ids, records, scriptMods: true ) );
		StringAssert.Contains( SmarterGuestsSave.ShapeProblem( identity, ids, records, scriptMods: false )!, "does not record opentpw.smarter-guests", "without script mods the section still needs the extension's record" );
		StringAssert.Contains( SmarterGuestsSave.ShapeProblem( null, ids, records, scriptMods: true )!, "ScriptMods section but no SmarterGuests section" );
		Assert.IsNull( SmarterGuestsSave.ShapeProblem( null, ids, null, scriptMods: false ) );
	}

	// ---- Restore ------------------------------------------------------------------------------

	[TestMethod]
	public void RestoringPutsStateCountersAndTheFlagBackAndRefusesAnotherSet()
	{
		using var scope = OpenTpwExtensions.Use( CounterSet() );
		var source = new ParkScenario( new WorldSeed( 1 ) );
		source.Run( GuestReport.TicksForDays( 30 ) );
		var counter = source.Guests.ScriptMods!.Mods[0];
		Assert.IsTrue( counter.State.Count > 0 );
		counter.Restore( counter.State, 3, 4, 5, false );
		var section = ParkResumeJson.Deserialize<ScriptModsSaveData>( ParkResumeJson.Serialize( ScriptModsSave.Capture( source.Guests )! ) );

		var target = new ParkScenario( new WorldSeed( 1 ) );
		Assert.AreEqual( "{}", ScriptState.Serialize( target.Guests.ScriptMods!.Mods[0].State ) );
		ScriptModsSave.Restore( target.Guests, section );
		var restored = target.Guests.ScriptMods.Mods[0];
		Assert.AreEqual( ScriptState.Serialize( counter.State ), ScriptState.Serialize( restored.State ) );
		Assert.AreEqual( (3, 4, 5, false), (restored.RuntimeErrors, restored.BudgetFailures, restored.InvalidResults, restored.Disabled) );
		Assert.AreEqual( Json( section ), Json( ScriptModsSave.Capture( target.Guests )! ), "capturing the resumed simulation gives the same section" );

		// another set of mods than the one that wrote the section
		var wrong = new ScriptModsSaveData { Mods = new Dictionary<string, ScriptModSaveData> { ["community.other"] = section.Mods["community.counter"] } };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ScriptModsSave.Restore( target.Guests, wrong ) ).Message, "community.other" );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ScriptModsSave.Restore( target.Guests, null ) ).Message, "no script mod state" );
		var stale = new ScriptModsSaveData { Mods = new Dictionary<string, ScriptModSaveData> { ["community.counter"] = section.Mods["community.counter"] with { BehaviourVersion = 7 } } };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ScriptModsSave.Restore( target.Guests, stale ) ).Message, "behaviour version 7" );
		Assert.AreEqual( (3, 4, 5, false), (restored.RuntimeErrors, restored.BudgetFailures, restored.InvalidResults, restored.Disabled), "a refused section changes nothing" );

		using var off = OpenTpwExtensions.Use( ModSet.Defaults( OfficialExtensions.All ) );
		var plain = new ParkScenario( new WorldSeed( 1 ) );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ScriptModsSave.Restore( plain.Guests, section ) ).Message, "no script mod is active" );
		ScriptModsSave.Restore( plain.Guests, null );
	}

	[TestMethod]
	public void ActiveProblemNamesTheMismatchBetweenTheSectionAndTheActiveMods()
	{
		var set = CounterSet();
		var entry = new ScriptModSaveData { BehaviourVersion = 1, ApiVersion = ScriptLimits.ScriptApiVersion, State = "{}", RuntimeErrors = 0, BudgetFailures = 0, InvalidResults = 0, Disabled = false };
		Assert.IsNull( ScriptModsSave.ActiveProblem( new ScriptModsSaveData { Mods = new Dictionary<string, ScriptModSaveData> { ["community.counter"] = entry } }, set ) );
		StringAssert.Contains( ScriptModsSave.ActiveProblem( null, set )!, "no ScriptMods section" );
		StringAssert.Contains( ScriptModsSave.ActiveProblem( new ScriptModsSaveData { Mods = new Dictionary<string, ScriptModSaveData> { ["community.x"] = entry } }, set )!, "community.x" );
		StringAssert.Contains( ScriptModsSave.ActiveProblem( new ScriptModsSaveData { Mods = new Dictionary<string, ScriptModSaveData> { ["community.counter"] = entry } }, ModSet.Defaults( OfficialExtensions.All ) )!, "none" );
		Assert.IsNull( ScriptModsSave.ActiveProblem( null, ModSet.Defaults( OfficialExtensions.All ) ) );
	}

	// ---- The world hash -----------------------------------------------------------------------

	[TestMethod]
	public void TheSectionJoinsTheWorldHashOnlyWhileAScriptModIsOnSoTheSchemaVersionStays()
	{
		Assert.AreEqual( 21, WorldStateHash.SchemaVersion, "the off-path hash is unchanged, so no bump" );
		var seed = new WorldSeed( 1 );
		ulong Hash( GuestSimulation guests ) => WorldStateHash.Compute( new WorldStateSources { Seed = seed, Guests = guests } );
		var off = new ParkScenario( seed );
		off.Run( 4000 );
		ParkScenario on;
		using ( OpenTpwExtensions.Use( CounterSet() ) )
		{
			on = new ParkScenario( seed );
			on.Run( 4000 );
		}
		var mod = on.Guests.ScriptMods!.Mods[0];
		var before = Hash( on.Guests );
		Assert.AreNotEqual( Hash( off.Guests ), before );

		mod.Restore( mod.State, 1, 0, 0, false );
		var withError = Hash( on.Guests );
		Assert.AreNotEqual( before, withError, "a failure counter is part of it" );
		mod.Restore( mod.State, 0, 1, 0, false );
		Assert.AreNotEqual( withError, Hash( on.Guests ), "so is which counter" );
		mod.Restore( mod.State, 0, 0, 0, false );
		Assert.AreEqual( before, Hash( on.Guests ), "back to the start" );
		mod.Restore( mod.State, 0, 0, 16, true );
		Assert.AreNotEqual( before, Hash( on.Guests ), "the disabled flag" );
		mod.Restore( ScriptState.Parse( "{\"s\":1}" ), 0, 0, 0, false );
		var changedState = Hash( on.Guests );
		Assert.AreNotEqual( before, changedState, "the state" );
		mod.Restore( ScriptState.Parse( "{\"s\":2}" ), 0, 0, 0, false );
		Assert.AreNotEqual( changedState, Hash( on.Guests ) );
	}

	// ---- A resumed park continues like the uninterrupted one ----------------------------------

	[TestMethod]
	public void AResumedParkWithScriptModsContinuesLikeTheUninterruptedOne()
	{
		using var scope = OpenTpwExtensions.Use( CounterSet() );
		var seed = new WorldSeed( 7 );
		ulong Hash( GuestSnapshotTests.Rig rig ) => WorldStateHash.Compute( new WorldStateSources { Seed = seed, Guests = rig.Simulation } );
		var park = new GuestSnapshotTests.Rig();
		Assert.IsInstanceOfType( park.Simulation.Decisions, typeof( ScriptGuestDecisions ) );
		var tick = GuestSnapshotTests.RunUntil( park, rig => rig.Simulation.TickCount >= 4200, 0, 4201 );
		tick = GuestSnapshotTests.RunUntil( park, GuestSnapshotTests.RichMoment, tick, 9000 );
		Assert.IsTrue( GuestSnapshotTests.RichMoment( park ) );
		var snapshot = park.Simulation.CaptureSnapshot();
		var identity = ParkResumeJson.Deserialize<SmarterGuestsSaveData>( ParkResumeJson.Serialize( SmarterGuestsSave.Capture( park.Simulation )! ) );
		var section = ParkResumeJson.Deserialize<ScriptModsSaveData>( ParkResumeJson.Serialize( ScriptModsSave.Capture( park.Simulation )! ) );
		var saved = section.Mods["community.counter"];
		Assert.IsTrue( saved.State.Length > 10, saved.State );

		var resumed = new GuestSnapshotTests.Rig();
		resumed.Simulation.RestoreSnapshot( snapshot );
		SmarterGuestsSave.Restore( resumed.Simulation, identity );
		ScriptModsSave.Restore( resumed.Simulation, section );
		foreach ( var attraction in snapshot.Attractions )
		{
			var script = resumed.Rides.Single( ride => ride.Bridge.AttractionId == attraction.AttractionId ).Script;
			script[RideVariables.VAR_LETMEON] = attraction.LetMeOnWritten ? attraction.CalledGuest : 0;
		}
		Assert.AreEqual( Hash( park ), Hash( resumed ), "the resumed park hashes like the saved one, script state included" );
		var last = tick + 3000;
		for ( ; tick <= last; tick++ )
		{
			park.Step();
			GuestSnapshotTests.Drive( park, tick );
			resumed.Step();
			GuestSnapshotTests.Drive( resumed, tick );
			Assert.AreEqual( Hash( park ), Hash( resumed ), $"the parks diverged at tick {tick}" );
		}
		var (a, b) = (park.Simulation.ScriptMods!.Mods[0], resumed.Simulation.ScriptMods!.Mods[0]);
		Assert.AreEqual( ScriptState.Serialize( a.State ), ScriptState.Serialize( b.State ) );
		Assert.AreNotEqual( saved.State, ScriptState.Serialize( a.State ), "the mods kept counting after the save point" );
		Assert.AreEqual( 0, a.Failures, a.LastFailure );
	}

	// ---- The whole park save ------------------------------------------------------------------

	private UpgradeCatalogueDefaultsTests fixture = null!;
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly Type Fixture = typeof( UpgradeCatalogueDefaultsTests );

	private (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) World()
	{
		fixture = new();
		fixture.Setup();
		return ((Level, ParkEconomy, OriginalObject, OriginalObject, ParkObjectState))Fixture.GetMethod( "World", Private )!.Invoke( fixture, new object[] { "", 0, false } )!;
	}

	private static Dictionary<string, string> Prepare( (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) world ) =>
		(Dictionary<string, string>)Fixture.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;

	[TestMethod]
	public void AnOffParkSavesNoScriptSectionAndNoSmarterSection()
	{
		var world = World();
		try
		{
			var snapshot = world.Level.CaptureResumeSnapshot( null, "off", Prepare( world ) );
			Assert.IsNull( snapshot.ScriptMods );
			Assert.IsNull( snapshot.SmarterGuests );
			var json = JsonNode.Parse( Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( snapshot ) ) )!.AsObject();
			Assert.IsFalse( json.ContainsKey( "ScriptMods" ) );
			Assert.IsFalse( json.ContainsKey( "SmarterGuests" ) );
			Assert.IsFalse( json.ContainsKey( "Mods" ) );
		}
		finally
		{
			fixture.Cleanup();
		}
	}

	[TestMethod]
	public void AParkWithAScriptModSavesTheSectionTheIdentityAndTheRecordAndLoadsOnlyWithTheSameMod()
	{
		ParkResumeSnapshot on;
		var set = CounterSet();
		ObjectCatalog catalog;
		BalanceSettings settings;
		IEconomyObjectCatalog economyCatalog;
		Dictionary<string, string> assets;
		using ( OpenTpwExtensions.Use( set ) )
		{
			var world = World();
			try
			{
				assets = Prepare( world );
				Assert.IsNotNull( world.Level.Guests!.ScriptMods, "the level's guests were built with the script mod on" );
				on = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( world.Level.CaptureResumeSnapshot( null, "on", assets ) ) );
				(catalog, settings, economyCatalog) = (world.Level.Objects.Catalog, world.Economy.Settings, world.Economy.Catalog);
				Assert.AreEqual( "community.counter", on.Mods!.Single().Id, "the mod is recorded like any simulation-changing mod" );
				CollectionAssert.AreEqual( new[] { "community.counter" }, on.ScriptMods!.Mods.Keys.ToArray() );
				Assert.IsNotNull( on.SmarterGuests, "the guest identity state travels with it" );
				var json = JsonNode.Parse( Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( on ) ) )!.AsObject();
				Assert.IsTrue( json.ContainsKey( "ScriptMods" ) && json.ContainsKey( "SmarterGuests" ) );
				ParkResumeSaveFile.ValidateSnapshot( on, catalog, settings, economyCatalog, assets );
			}
			finally
			{
				fixture.Cleanup();
			}
			void Validate( ParkResumeSnapshot snapshot ) => ParkResumeSaveFile.ValidateSnapshot( snapshot, catalog, settings, economyCatalog, assets );
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Validate( on with { ScriptMods = null, SmarterGuests = null } ) ).Message, "no ScriptMods section" );
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Validate( on with { ScriptMods = null } ) ).Message, "SmarterGuests section but does not record", "an identity section alone is not a script mod's" );
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Validate( on with { SmarterGuests = null } ) ).Message, "no SmarterGuests section" );
			var entry = on.ScriptMods!.Mods["community.counter"];
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Validate( on with { ScriptMods = new ScriptModsSaveData { Mods = new Dictionary<string, ScriptModSaveData> { ["community.counter"] = entry with { BehaviourVersion = 2 } } } } ) ).Message,
				"script behaviour version 2, but this build runs version 1" );
		}

		// the mod off: the record is unmet
		var refused = Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateSnapshot( on, catalog, settings, economyCatalog, assets ) );
		StringAssert.Contains( refused.Message, "needs community.counter 1.0.0" );
		// and an off save under the mod on
		using var again = OpenTpwExtensions.Use( set );
		var offWorld = World();
		try
		{
			var off = offWorld.Level.CaptureResumeSnapshot( null, "off", Prepare( offWorld ) ) with { Mods = null, ScriptMods = null, SmarterGuests = null };
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateSnapshot( off, offWorld.Level.Objects.Catalog, offWorld.Economy.Settings, offWorld.Economy.Catalog, assets ) ).Message, "was saved without community.counter" );
		}
		finally
		{
			fixture.Cleanup();
		}
	}
}
