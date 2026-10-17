using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using OpenTPW.Scripting;

namespace OpenTPW.Tests;

/// <summary>
/// Phase 4 of the script mods (docs/SCRIPT-MODS.md, "Hook API v1"): the hooks on the decision seam, the chain of mods, the views, the random numbers, the budgets and the failure counters.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ScriptHooksTests
{
	private TempScriptMods mods = null!;
	private readonly List<TempScriptMods> extra = new();

	[TestInitialize]
	public void Initialize() => mods = new TempScriptMods();

	[TestCleanup]
	public void Cleanup()
	{
		mods.Dispose();
		foreach ( var item in extra )
			item.Dispose();
	}

	/// <summary>A policy over one new community mod with <paramref name="script"/> and <paramref name="hooks"/>.</summary>
	private ScriptGuestDecisions Single( string script, params string[] hooks )
	{
		var fresh = new TempScriptMods().Add( "community.single", script, hooks );
		extra.Add( fresh );
		return Policy( fresh.Resolve() );
	}

	private static DecisionContext Context( long turn = 10, ulong seed = 99 ) => new( GuestTests.CreateSettings(), turn, false, seed, 20, 10 );

	private static GuestView Guest( int id = 5, ulong birth = 7, float happiness = 60 ) =>
		new( id, birth, 0, happiness, 10, 20, 30, 40, 100, 3, 4, new[] { 9, 0, 0, 0 }, new[] { 0, 0, 0, 0 } );

	private static AttractionView Attraction( int id = 2, int queueLength = 3, uint queueLimit = 30 ) =>
		new( id, 1, RideVisitorKind.Ride, true, (5, 6), (5, 7), 0, queueLength, 5, queueLimit, 55, 2, 0, 0, false, 4, false, 0, 0, true, false, false );

	private static ScriptGuestDecisions Policy( ModSet set, ScriptHostOptions? options = null, IGuestDecisions? inner = null ) =>
		new( inner ?? OriginalGuestDecisions.Instance, new ScriptModRuntime( set.Mods, options ) );

	private static uint Basis( DecisionContext context, GuestView guest, AttractionView attraction ) => OriginalGuestDecisions.Instance.ScoreAttraction( context, guest, attraction );

	private const string Plus5 = "function scoreAttraction(guest, c, prev)\n  return prev + 5\nend\n";
	private const string Times2 = "function scoreAttraction(guest, c, prev)\n  return prev * 2\nend\n";

	// ---- The chain ----------------------------------------------------------------------------

	[TestMethod]
	public void ScoreAttractionChainsTheModsInTheResolvedOrderEachSeeingTheResultOfTheOneBefore()
	{
		mods.Add( "community.a", Plus5, "scoreAttraction" ).Add( "community.b", Times2, "scoreAttraction" );
		var (context, guest, attraction) = (Context(), Guest(), Attraction());
		var basis = Basis( context, guest, attraction );
		Assert.IsTrue( basis > 0 );
		Assert.AreEqual( (basis + 5) * 2, Policy( mods.Resolve( new[] { "community.a", "community.b" } ) ).ScoreAttraction( context, guest, attraction ), "a then b" );
		Assert.AreEqual( basis * 2 + 5, Policy( mods.Resolve( new[] { "community.b", "community.a" } ) ).ScoreAttraction( context, guest, attraction ), "b then a" );
	}

	[TestMethod]
	public void AFailingModReturnsPrevSoTheModsBeforeAndAfterItStillCount()
	{
		mods.Add( "community.a", Plus5, "scoreAttraction" )
			.Add( "community.b", "function scoreAttraction(guest, c, prev)\n  state.n = 1\n  return prev // (prev - prev)\nend\n", "scoreAttraction" )
			.Add( "community.c", Times2, "scoreAttraction" );
		var policy = Policy( mods.Resolve( new[] { "community.a", "community.b", "community.c" } ) );
		var (context, guest, attraction) = (Context(), Guest(), Attraction());
		Assert.AreEqual( (Basis( context, guest, attraction ) + 5) * 2, policy.ScoreAttraction( context, guest, attraction ) );
		var failing = policy.Runtime.Mods.Single( mod => mod.Id == "community.b" );
		Assert.AreEqual( 1, failing.RuntimeErrors );
		Assert.AreEqual( "{}", ScriptState.Serialize( failing.State ), "the failed call's write to state rolled back" );
		Assert.AreEqual( 0, policy.Runtime.Mods.Single( mod => mod.Id == "community.a" ).Failures );
		StringAssert.Contains( failing.LastFailure!, "scoreAttraction" );
	}

	[TestMethod]
	public void AnInvalidResultAndAnOverrunAreFailuresToo()
	{
		mods.Add( "community.range", "function scoreAttraction(guest, c, prev)\n  state.x = 1\n  return 4294967296\nend\n", "scoreAttraction" )
			.Add( "community.negative", "function scoreAttraction(guest, c, prev)\n  return -1\nend\n", "scoreAttraction" )
			.Add( "community.loop", "function scoreAttraction(guest, c, prev)\n  while true do\n  end\n  return 1\nend\n", "scoreAttraction" );
		var policy = Policy( mods.Resolve() );
		var (context, guest, attraction) = (Context(), Guest(), Attraction());
		Assert.AreEqual( Basis( context, guest, attraction ), policy.ScoreAttraction( context, guest, attraction ), "every failing mod returned prev" );
		Assert.AreEqual( (0, 0, 1), Counters( policy, "community.range" ) );
		Assert.AreEqual( (0, 0, 1), Counters( policy, "community.negative" ) );
		Assert.AreEqual( (0, 1, 0), Counters( policy, "community.loop" ) );
		Assert.AreEqual( "{}", ScriptState.Serialize( policy.Runtime.Mods.Single( mod => mod.Id == "community.range" ).State ) );
		Assert.AreEqual( uint.MaxValue, Single( "function scoreAttraction(guest, c, prev)\n  return 4294967295\nend\n", "scoreAttraction" ).ScoreAttraction( context, guest, attraction ), "the top of the uint range is valid" );
	}

	private static (long Runtime, long Budget, long Invalid) Counters( ScriptGuestDecisions policy, string id )
	{
		var mod = policy.Runtime.Mods.Single( item => item.Id == id );
		return (mod.RuntimeErrors, mod.BudgetFailures, mod.InvalidResults);
	}

	[TestMethod]
	public void StateCommitsOnSuccessAndOnlyThen()
	{
		mods.Add( "community.count", "function scoreAttraction(guest, c, prev)\n  if has(state, \"n\") then\n    state.n = state.n + 1\n  else\n    state.n = 1\n  end\n  if state.n == 3 then\n    return prev // 0\n  end\n  return prev\nend\n", "scoreAttraction" );
		var policy = Policy( mods.Resolve() );
		var (context, guest, attraction) = (Context(), Guest(), Attraction());
		var states = new List<string>();
		for ( var call = 0; call < 5; call++ )
		{
			policy.ScoreAttraction( context, guest, attraction );
			states.Add( ScriptState.Serialize( policy.Runtime.Mods[0].State ) );
		}
		// the third call fails and leaves n at 2; the fourth then raises it to 3 again and fails again
		CollectionAssert.AreEqual( new[] { "{\"n\":1}", "{\"n\":2}", "{\"n\":2}", "{\"n\":2}", "{\"n\":2}" }, states );
		Assert.AreEqual( 3, policy.Runtime.Mods[0].RuntimeErrors );
	}

	// ---- Budgets and the disabled flag --------------------------------------------------------

	private const string Busy = "function scoreAttraction(guest, c, prev)\n  local s = 0\n  for i = 1, 20 do\n    s = s + i\n  end\n  return prev + s\nend\n";

	[TestMethod]
	public void ARunOutOfTickBudgetIsNoFailureAndCountsOncePerTickAndTheNextTickStartsAfresh()
	{
		mods.Add( "community.busy", Busy, "scoreAttraction" );
		var policy = Policy( mods.Resolve(), new ScriptHostOptions { StepsPerCall = 1000, StepsPerTick = 300 } );
		var (context, guest, attraction) = (Context(), Guest(), Attraction());
		var basis = Basis( context, guest, attraction );
		var results = Enumerable.Range( 0, 12 ).Select( _ => policy.ScoreAttraction( context, guest, attraction ) ).ToArray();
		var mod = policy.Runtime.Mods[0];
		Assert.IsTrue( results[0] == basis + 210 && results[^1] == basis, $"the first calls ran, the last found the budget gone ({string.Join( ",", results )})" );
		Assert.AreEqual( 1, mod.TicksOverBudget, "one overrun for the tick, however many calls were skipped" );
		Assert.AreEqual( 0, mod.Failures, "an overrun is not a failure" );
		policy.BeginTick();
		Assert.AreEqual( basis + 210, policy.ScoreAttraction( context, guest, attraction ), "a new tick has its full budget" );
		for ( var call = 0; call < 12; call++ )
			policy.ScoreAttraction( context, guest, attraction );
		Assert.AreEqual( 2, mod.TicksOverBudget );
		Assert.IsFalse( mod.Disabled );
	}

	[TestMethod]
	public void BusyTicksNeverDisableAModHoweverManyThereAre()
	{
		mods.Add( "community.busy", Busy, "scoreAttraction" );
		var policy = Policy( mods.Resolve(), new ScriptHostOptions { StepsPerCall = 1000, StepsPerTick = 300 } );
		var (context, guest, attraction) = (Context(), Guest(), Attraction());
		for ( var tick = 0; tick < 100; tick++ )
		{
			policy.BeginTick();
			for ( var call = 0; call < 8; call++ )
				policy.ScoreAttraction( context, guest, attraction );
		}
		var mod = policy.Runtime.Mods[0];
		Assert.AreEqual( 100, mod.TicksOverBudget );
		Assert.AreEqual( 0, mod.Failures );
		Assert.IsFalse( mod.Disabled );
		Assert.IsTrue( policy.Runtime.Runs( ScriptHooks.ScoreAttraction ) );
	}

	[TestMethod]
	public void AnOverrunInsideTheCallBudgetStillFailsTheCall()
	{
		mods.Add( "community.loop", "function scoreAttraction(guest, c, prev)\n  while true do\n  end\n  return 1\nend\n", "scoreAttraction" );
		var policy = Policy( mods.Resolve(), new ScriptHostOptions { StepsPerCall = 1000, StepsPerTick = 50_000 } );
		policy.ScoreAttraction( Context(), Guest(), Attraction() );
		var mod = policy.Runtime.Mods[0];
		Assert.AreEqual( (0, 1, 0), (mod.RuntimeErrors, mod.BudgetFailures, mod.InvalidResults) );
		Assert.AreEqual( 0, mod.TicksOverBudget );
	}

	[TestMethod]
	public void ADecisionIsAdmittedOrSkippedAsAWholeSoItsScoresAreNeverMixed()
	{
		mods.Add( "community.busy", Busy, "scoreAttraction" );
		var policy = Policy( mods.Resolve(), new ScriptHostOptions { StepsPerCall = 1000, StepsPerTick = 300 } );
		var (context, guest) = (Context(), Guest());
		var candidates = Enumerable.Range( 1, 9 ).Select( id => Attraction( id: id ) ).ToArray();
		uint[] Decide()
		{
			policy.BeginDecision();
			try { return candidates.Select( candidate => policy.ScoreAttraction( context, guest, candidate ) ).ToArray(); }
			finally { policy.EndDecision(); }
		}
		var basis = candidates.Select( candidate => Basis( context, guest, candidate ) ).ToArray();
		var first = Decide();
		CollectionAssert.AreEqual( basis.Select( score => score + 210 ).ToArray(), first, "all nine candidates have the mod's score although the tick budget (300) holds three calls" );
		var mod = policy.Runtime.Mods[0];
		Assert.AreEqual( 0, mod.TicksOverBudget );
		CollectionAssert.AreEqual( basis, Decide(), "the budget is gone, so the next decision is the base policy's for every candidate" );
		Assert.AreEqual( 1, mod.TicksOverBudget );
		Decide();
		Assert.AreEqual( 1, mod.TicksOverBudget, "once per tick" );
		policy.BeginTick();
		CollectionAssert.AreEqual( first, Decide(), "a new tick admits the mod again" );
		Assert.AreEqual( 0, mod.Failures );
	}

	[TestMethod]
	public void TheHostBracketsEveryCandidateLoopAsOneDecision()
	{
		var spy = new SpyDecisions();
		var park = new ParkScenario( new WorldSeed( 1 ), null, spy );
		park.Run( GuestReport.TicksForDays( 30 ) );
		Assert.IsTrue( spy.Decisions > 5 && spy.Scores > 20, $"{spy.Decisions} decisions, {spy.Scores} scores" );
		Assert.AreEqual( 0, spy.Violations, "every score came between a BeginDecision and its EndDecision" );
		Assert.IsFalse( spy.Open );
	}

	private sealed class SpyDecisions : IGuestDecisions
	{
		private readonly IGuestDecisions inner = OriginalGuestDecisions.Instance;
		public int Decisions, Scores, Violations;
		public bool Open;
		public void BeginDecision() { Open = true; Decisions++; }
		public void EndDecision() => Open = false;
		public uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction ) { Scores++; Violations += Open ? 0 : 1; return inner.ScoreAttraction( context, guest, attraction ); }
		public BoothDecision DecideBooth( in DecisionContext context, in GuestView guest, in BoothView booth ) => inner.DecideBooth( context, guest, booth );
		public bool BeatsInMajorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) => inner.BeatsInMajorDecision( context, guest, score, best );
		public bool BeatsInMinorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) => inner.BeatsInMinorDecision( context, guest, score, best );
		public QueueAdmission DecideAdmission( in DecisionContext context, in GuestView guest, in AdmissionView admission ) => inner.DecideAdmission( context, guest, admission );
		public QueuePatienceDecision DecideQueuePatience( in DecisionContext context, in GuestView guest, in QueuePatienceView queue ) => inner.DecideQueuePatience( context, guest, queue );
		public PostUseDecision RememberUse( in DecisionContext context, in GuestView guest, in AttractionView ride ) => inner.RememberUse( context, guest, ride );
		public GuestWander.Destination ChooseWanderGoal( in DecisionContext context, in GuestView guest, in GuestWander.Destination proposal ) => inner.ChooseWanderGoal( context, guest, proposal );
	}

	[TestMethod]
	public void ACrowdedParkNeverDisablesTheStateMod()
	{
		// the scenario scaled up: a group every few turns, so far more guests decide at once than in the report's calibration (about 30 to 90 guests)
		const string State = "function bump(kind, id)\n  local key = kind .. '-' .. id\n  if has(state, key) then\n    state[key] = state[key] + 1\n  else\n    state[key] = 1\n  end\nend\nfunction scoreAttraction(guest, c, prev)\n  bump('s', c.id)\n  return prev\nend\n";
		using var scope = OpenTpwExtensions.Use( mods.Add( "community.state", State, "scoreAttraction" ).Resolve() );
		var park = new ParkScenario( new WorldSeed( 1 ), new ParkScenarioOptions { ArrivalTimeBetween = 1, AdmissionFee = 0 } );
		var peak = 0;
		for ( var chunk = 0; chunk < 40; chunk++ )
		{
			park.Run( 343 );
			peak = Math.Max( peak, park.Guests.Guests.Count );
		}
		var mod = park.Guests.ScriptMods!.Mods[0];
		var stats = mod.Stats();
		Assert.IsTrue( peak > 150, $"a crowd of {peak}" );
		Assert.AreEqual( 0, mod.Failures, mod.LastFailure );
		Assert.IsFalse( mod.Disabled );
		Assert.IsTrue( stats.MaxTickSteps < ScriptLimits.DefaultStepsPerTick, $"busiest tick {stats.MaxTickSteps} steps, {stats.MaxTickCalls} calls" );
	}

	[TestMethod]
	public void AScriptPolicyPassedExplicitlyIsSavedAndHashedLikeOneChosenByTheModSet()
	{
		var runtime = new ScriptModRuntime( mods.Add( "community.a", Plus5, "scoreAttraction" ).Resolve().Mods );
		var policy = new ScriptGuestDecisions( OriginalGuestDecisions.Instance, runtime );
		var park = new ParkScenario( new WorldSeed( 1 ), null, policy );
		Assert.AreSame( runtime, park.Guests.ScriptMods );
		Assert.IsNotNull( park.Guests.SmarterGuests );
		park.Run( GuestReport.TicksForDays( 20 ) );
		Assert.IsNotNull( ScriptModsSave.Capture( park.Guests ) );
		var before = WorldStateHash.Compute( new WorldStateSources { Seed = new WorldSeed( 1 ), Guests = park.Guests } );
		runtime.Mods[0].Restore( runtime.Mods[0].State, 0, 0, 0, false, 5 );
		Assert.AreNotEqual( before, WorldStateHash.Compute( new WorldStateSources { Seed = new WorldSeed( 1 ), Guests = park.Guests } ), "the explicit policy's state is hashed (ticks over budget included)" );
	}

	[TestMethod]
	public void AModIsDisabledAfterTheLimitAndItsHooksStopRunning()
	{
		Assert.AreEqual( 16, ScriptLimits.DisableAfterFailures );
		mods.Add( "community.broken", "function scoreAttraction(guest, c, prev)\n  state.calls = 1\n  return prev // 0\nend\n", "scoreAttraction" );
		var policy = Policy( mods.Resolve() );
		var (context, guest, attraction) = (Context(), Guest(), Attraction());
		var mod = policy.Runtime.Mods[0];
		for ( var call = 1; call < ScriptLimits.DisableAfterFailures; call++ )
		{
			policy.ScoreAttraction( context, guest, attraction );
			Assert.IsFalse( mod.Disabled, $"after {call} failures" );
		}
		Assert.IsTrue( policy.Runtime.Runs( ScriptHooks.ScoreAttraction ) );
		policy.ScoreAttraction( context, guest, attraction );
		Assert.IsTrue( mod.Disabled );
		Assert.AreEqual( 16, mod.Failures );
		Assert.IsFalse( policy.Runtime.Runs( ScriptHooks.ScoreAttraction ), "a disabled mod no longer counts as running" );
		var calls = mod.Stats().Calls;
		for ( var call = 0; call < 10; call++ )
			Assert.AreEqual( Basis( context, guest, attraction ), policy.ScoreAttraction( context, guest, attraction ) );
		Assert.AreEqual( calls, mod.Stats().Calls, "its hooks no longer run" );
		Assert.AreEqual( 16, mod.Failures, "and no more failures are counted" );
	}

	// ---- The other hooks ----------------------------------------------------------------------

	private static AdmissionView Admission( int difference, int queueCount = 3, uint limit = 30, int word = 2 ) => new( Attraction( queueLength: queueCount, queueLimit: limit ), word, difference, queueCount, limit );

	[TestMethod]
	public void JoinQueueReplacesTheVerdictAndKeepsTheBasePoliciesReasonWhileItAgrees()
	{
		var (context, guest) = (Context(), Guest());
		var refused = Admission( difference: 50 );
		var basis = OriginalGuestDecisions.Instance.DecideAdmission( context, guest, refused );
		Assert.IsFalse( basis.Admits );
		Assert.AreNotEqual( QueueJoinResult.QueueTooLong, basis.Result, "an excitement refusal" );
		var admitted = Admission( difference: 10 );
		Assert.IsTrue( OriginalGuestDecisions.Instance.DecideAdmission( context, guest, admitted ).Admits );

		const string Pass = "function joinQueue(guest, a, prev)\n  return prev\nend\n";
		const string Yes = "function joinQueue(guest, a, prev)\n  return true\nend\n";
		const string No = "function joinQueue(guest, a, prev)\n  return false\nend\n";
		Assert.AreEqual( basis, Single( Pass, "joinQueue" ).DecideAdmission( context, guest, refused ), "prev keeps the base reason" );
		Assert.AreEqual( QueueAdmission.Admit, Single( Yes, "joinQueue" ).DecideAdmission( context, guest, refused ), "a mod admits a guest the game refused" );
		Assert.AreEqual( new QueueAdmission( QueueJoinResult.QueueTooLong ), Single( No, "joinQueue" ).DecideAdmission( context, guest, admitted ), "a mod refuses a guest the game admitted" );
		Assert.AreEqual( basis, Single( No, "joinQueue" ).DecideAdmission( context, guest, refused ), "a mod that agrees with a refusal leaves the reason" );
		// the fields the hook sees
		Assert.IsTrue( Single( "function joinQueue(guest, a, prev)\n  return a.joinDifference == 50 and a.queueCount == 3 and a.queueLimit == 30 and a.excitementWord == 2 and a.id == 2\nend\n", "joinQueue" ).DecideAdmission( context, guest, refused ).Admits );
	}

	[TestMethod]
	public void LeaveQueueReplacesTheVerdictWithAThoughtWhenItFlipsToLeave()
	{
		var (context, guest) = (Context( turn: 100 ), Guest());
		var view = new QueuePatienceView( Attraction(), 40, 2, 6 );
		Assert.AreEqual( QueuePatienceDecision.Stay, OriginalGuestDecisions.Instance.DecideQueuePatience( context, guest, view ) );
		var leave = Single( "function leaveQueue(guest, a, prev)\n  return a.waitedTurns == 60 and a.position == 2 and a.queueCount == 6 and a.joinTurn == 40\nend\n", "leaveQueue" );
		var decision = leave.DecideQueuePatience( context, guest, view );
		Assert.IsTrue( decision.Leave );
		Assert.AreEqual( GuestThought.Bored, decision.Thought );
		var stay = Single( "function leaveQueue(guest, a, prev)\n  return prev\nend\n", "leaveQueue" );
		Assert.AreEqual( QueuePatienceDecision.Stay, stay.DecideQueuePatience( context, guest, view ) );
	}

	[TestMethod]
	public void WanderGoalIsACellIdAndMustLieInsideTheMap()
	{
		var (context, guest) = (Context(), Guest());
		var proposal = new GuestWander.Destination( (3, 4), 100, 120 );
		GuestWander.Destination Run( string body ) => Single( $"function wanderGoal(guest, prev)\n{body}\nend\n", "wanderGoal" ).ChooseWanderGoal( context, guest, proposal );

		Assert.AreEqual( proposal, Run( "  return prev" ), "an unchanged goal keeps the proposal and its sub-cell point" );
		Assert.AreEqual( new GuestWander.Destination( (2, 7), GuestWander.CentreByte, GuestWander.CentreByte ), Run( "  return 7 * 1024 + 2" ) );
		Assert.AreEqual( new GuestWander.Destination( (19, 9), GuestWander.CentreByte, GuestWander.CentreByte ), Run( "  return 9 * 1024 + 19" ), "the last cell of a 20 by 10 map" );
		Assert.AreEqual( proposal, Run( "  return 9 * 1024 + 20" ), "one past the width is an invalid result: prev" );
		Assert.AreEqual( proposal, Run( "  return 10 * 1024" ), "one past the height" );
		Assert.AreEqual( proposal, Run( "  return -1" ), "outside the hook's range" );
		Assert.AreEqual( (19, 9), Run( "  return (guest.mapHeight - 1) * 1024 + guest.mapWidth - 1" ).Cell, "the guest sees the map size" );
		var guestSees = Run( "  return guest.cellY * 1024 + guest.cellX" );
		Assert.AreEqual( (3, 4), guestSees.Cell, "the guest's own cell is the goal" );
		// no map size known: the hook does not run
		var unknownMap = new DecisionContext( GuestTests.CreateSettings(), 10, false, 99 );
		Assert.AreEqual( proposal, Single( "function wanderGoal(guest, prev)\n  return 7\nend\n", "wanderGoal" ).ChooseWanderGoal( unknownMap, guest, proposal ) );
	}

	[TestMethod]
	public void AGoalTheHostRefusesCountsAsAnInvalidResultAndRollsBackState()
	{
		var policy = Single( "function wanderGoal(guest, prev)\n  state.last = 1\n  return 9 * 1024 + 500\nend\n", "wanderGoal" );
		var proposal = new GuestWander.Destination( (3, 4), 100, 120 );
		Assert.AreEqual( proposal, policy.ChooseWanderGoal( Context(), Guest(), proposal ) );
		Assert.AreEqual( (0, 0, 1), Counters( policy, "community.single" ) );
		Assert.AreEqual( "{}", ScriptState.Serialize( policy.Runtime.Mods[0].State ) );
	}

	[TestMethod]
	public void OtherDecisionsPassThroughUntouched()
	{
		var policy = Single( Plus5, "scoreAttraction" );
		var (context, guest) = (Context(), Guest());
		Assert.AreEqual( OriginalGuestDecisions.Instance.BeatsInMajorDecision( context, guest, 50, 40 ), policy.BeatsInMajorDecision( context, guest, 50, 40 ) );
		Assert.AreEqual( OriginalGuestDecisions.Instance.BeatsInMinorDecision( context, guest, 50, 50 ), policy.BeatsInMinorDecision( context, guest, 50, 50 ) );
		Assert.AreEqual( OriginalGuestDecisions.Instance.RememberUse( context, guest, Attraction() ), policy.RememberUse( context, guest, Attraction() ) );
		Assert.AreEqual( OriginalGuestDecisions.Instance.DecideBooth( context, guest, new BoothView( 100, 20, 7 ) ), policy.DecideBooth( context, guest, new BoothView( 100, 20, 7 ) ) );
		Assert.IsTrue( policy.UsesExtensionState );
	}

	[TestMethod]
	public void TheScriptPolicyWrapsTheSmarterPolicyToo()
	{
		var smarter = new SmarterGuestDecisions();
		var policy = Policy( mods.Add( "community.a", Plus5, "scoreAttraction" ).Resolve(), inner: smarter );
		var (context, guest, attraction) = (Context(), Guest(), Attraction());
		Assert.AreEqual( smarter.ScoreAttraction( context, guest, attraction ) + 5, policy.ScoreAttraction( context, guest, attraction ) );
		Assert.AreSame( smarter, policy.Inner );
	}

	// ---- Views and random numbers -------------------------------------------------------------

	[TestMethod]
	public void ViewsAreReadOnlyTablesWithAsciiKeysAndStableIds()
	{
		var (context, guest, attraction) = (Context( turn: 77 ), Guest( id: 12, birth: 3, happiness: 61.9f ), Attraction( id: 8 ));
		foreach (var view in new[] { ScriptHookViews.Guest( context, guest ), ScriptHookViews.Attraction( attraction ), ScriptHookViews.Admission( Admission( 50 ) ), ScriptHookViews.Queue( context, new QueuePatienceView( attraction, 40, 2, 6 ) ) } )
		{
			var table = view.AsTable;
			Assert.IsTrue( table.ReadOnly );
			Assert.IsTrue( table.Count is > 15 and < 40 );
			foreach ( var key in table.SortedKeys() )
				Assert.IsTrue( key.Length is > 0 and <= ScriptLimits.MaxStateKeyLength && key.All( c => c is >= ' ' and <= '~' ), key );
		}
		var guestTable = ScriptHookViews.Guest( context, guest ).AsTable;
		long Int( ScriptTable table, string key ) => table.TryGet( key, out var value ) ? value.AsInt : throw new AssertFailedException( key );
		Assert.AreEqual( 12, Int( guestTable, "id" ) );
		Assert.AreEqual( 3, Int( guestTable, "birthId" ) );
		Assert.AreEqual( 61, Int( guestTable, "happiness" ), "meters are the whole number the original tests" );
		Assert.AreEqual( 77, Int( guestTable, "turn" ) );
		Assert.AreEqual( 20, Int( guestTable, "mapWidth" ) );
		Assert.AreEqual( 9, guestTable.TryGet( "usedHistory", out var history ) ? history.AsTable[1].AsInt : -1 );
		Assert.AreEqual( 4, history.AsTable.Count );
		var attractionTable = ScriptHookViews.Attraction( attraction ).AsTable;
		Assert.AreEqual( 8, Int( attractionTable, "id" ) );
		Assert.AreEqual( 5, Int( attractionTable, "backX" ) );
		Assert.AreEqual( 30, Int( attractionTable, "queueLimit" ) );
	}

	[TestMethod]
	public void ViewsReachTheScriptWithTheirValues()
	{
		var policy = Single( "function scoreAttraction(guest, c, prev)\n  return guest.id * 1000000 + c.id * 1000 + guest.happiness + guest.usedHistory[1] * 10\nend\n", "scoreAttraction" );
		Assert.AreEqual( (uint)(12 * 1_000_000 + 8 * 1000 + 61 + 90), policy.ScoreAttraction( Context(), Guest( id: 12, happiness: 61.9f ), Attraction( id: 8 ) ) );
	}

	[TestMethod]
	public void RngHashesTheHookNameAndTheDocumentedCallIdentity()
	{
		// the function is called `pick`; the random numbers are those of the hook `scoreAttraction`
		var policy = Single( "function pick(guest, c, prev)\n  return rng(1000) * 1000 + rng(1000)\nend\n", "scoreAttraction=pick" );
		var context = Context( turn: 4242, seed: 123456789 );
		var score = policy.ScoreAttraction( context, Guest( birth: 31 ), Attraction() );
		Assert.AreEqual( ScriptRng.Draw( 123456789, "community.single", "scoreAttraction", 31, 4242, 0, 1000 ) * 1000 + ScriptRng.Draw( 123456789, "community.single", "scoreAttraction", 31, 4242, 1, 1000 ), score );
		Assert.AreEqual( score, policy.ScoreAttraction( context, Guest( birth: 31 ), Attraction() ), "stateless: the same identity gives the same numbers again" );
		Assert.AreNotEqual( score, policy.ScoreAttraction( context, Guest( birth: 32 ), Attraction() ), "another guest" );
		Assert.AreNotEqual( score, policy.ScoreAttraction( Context( turn: 4243, seed: 123456789 ), Guest( birth: 31 ), Attraction() ), "another turn" );
		Assert.AreNotEqual( score, policy.ScoreAttraction( Context( turn: 4242, seed: 5 ), Guest( birth: 31 ), Attraction() ), "another park" );
	}

	[TestMethod]
	public void ModsThatDrawNothingFromTheWorldGeneratorLeaveItAlone()
	{
		// a policy has no WorldRng at all: the interface and the script host take none, so a script's rng cannot move the park's stream
		var type = typeof( ScriptGuestDecisions );
		Assert.IsFalse( type.GetFields( System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public ).Any( field => field.FieldType == typeof( WorldRng ) ) );
	}

	// ---- The simulation -----------------------------------------------------------------------

	[TestMethod]
	public void WithoutAScriptModNothingIsWrappedBuiltSavedOrHashed()
	{
		var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
		Assert.AreSame( OriginalGuestDecisions.Instance, guests.Decisions );
		Assert.IsNull( guests.ScriptMods );
		Assert.IsNull( guests.SmarterGuests );
		Assert.IsNull( ScriptModsSave.Capture( guests ) );
	}

	[TestMethod]
	public void AScriptModBringsTheBirthIdsButNoSmarterRules()
	{
		using var scope = OpenTpwExtensions.Use( mods.Add( "community.a", Plus5, "scoreAttraction" ).Resolve() );
		var park = new ParkScenario( new WorldSeed( 1 ) );
		var policy = (ScriptGuestDecisions)park.Guests.Decisions;
		Assert.AreSame( OriginalGuestDecisions.Instance, policy.Inner, "the faithful rules underneath" );
		Assert.IsNotNull( park.Guests.SmarterGuests, "the identity state is on" );
		Assert.IsFalse( FeatureGates.SmarterGuests, "the extension is not" );
		park.Run( 3000 );
		Assert.IsTrue( park.Guests.Guests.Count > 0 && park.Guests.Guests.All( guest => guest.BirthId != 0 ) );
	}

	[TestMethod]
	public void ScriptModsStackOnTheSmarterGuestsPolicyWhenThatExtensionIsOn()
	{
		using var scope = OpenTpwExtensions.Use( ModSet.Resolve( mods.Add( "community.a", Plus5, "scoreAttraction" ).Discover(), new ModSettings { Enabled = new Dictionary<string, bool> { ["community.a"] = true, [OfficialExtensions.SmarterGuests.Id] = true } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All ) );
		var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
		Assert.IsInstanceOfType( ((ScriptGuestDecisions)guests.Decisions).Inner, typeof( SmarterGuestDecisions ) );
	}

	[TestMethod]
	public void AModThatAnswersPrevEverywhereLeavesTheGuestsExactlyAsTheyWere()
	{
		const string Passes = "function scoreAttraction(guest, c, prev)\n  return prev\nend\nfunction joinQueue(guest, a, prev)\n  return prev\nend\nfunction leaveQueue(guest, a, prev)\n  return prev\nend\nfunction wanderGoal(guest, prev)\n  return prev\nend\n";
		var off = new ParkScenario( new WorldSeed( 3 ) );
		off.Run( 9000 );
		ParkScenario on;
		using ( OpenTpwExtensions.Use( mods.Add( "community.pass", Passes, "scoreAttraction", "joinQueue", "leaveQueue", "wanderGoal" ).Resolve() ) )
		{
			on = new ParkScenario( new WorldSeed( 3 ) );
			on.Run( 9000 );
		}
		Assert.AreEqual( off.Hash, on.Hash, "the same guests, tick for tick" );
		Assert.AreEqual( off.Guests.RandomState, on.Guests.RandomState, "the world generator" );
		var stats = on.Guests.ScriptMods!.Stats().Single();
		Assert.IsTrue( stats.Calls > 100 && stats.RuntimeErrors + stats.BudgetFailures + stats.InvalidResults == 0, $"{stats.Calls} calls" );
	}

	[TestMethod]
	public void TheExampleModChangesTheScenarioAndKeepsItsCountersInState()
	{
		var changed = 0;
		long tolerated = 0;
		foreach ( var seed in new ulong[] { 1, 2, 3, 4 } )
		{
			var off = new ParkScenario( new WorldSeed( seed ) );
			off.Run( GuestReport.TicksForDays( 40 ) );
			ParkScenario on;
			using ( OpenTpwExtensions.Use( TempScriptMods.Example() ) )
			{
				on = new ParkScenario( new WorldSeed( seed ) );
				on.Run( GuestReport.TicksForDays( 40 ) );
			}
			changed += off.Hash != on.Hash ? 1 : 0;
			var mod = on.Guests.ScriptMods!.Mods.Single();
			Assert.AreEqual( 0, mod.Failures, mod.LastFailure );
			tolerated += mod.State.TryGet( "tolerated", out var count ) ? count.AsInt : 0;
		}
		Assert.IsTrue( changed >= 3, $"the example changed {changed} of 4 runs" );
		Assert.IsTrue( tolerated > 0, "guests joined a ride the game would have refused" );
	}

	[TestMethod]
	public void TheExampleModDeclaresTheHooksItImplements()
	{
		var mod = TempScriptMods.Example().Mods.Single();
		CollectionAssert.AreEquivalent( new[] { "joinQueue", "leaveQueue" }, mod.Scripts!.Hooks.Keys.ToArray() );
		foreach ( var hook in mod.Scripts.Hooks.Keys )
			Assert.IsTrue( ScriptLimits.HookParameters.ContainsKey( hook ) );
	}
}
