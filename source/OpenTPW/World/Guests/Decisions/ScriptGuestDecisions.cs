using OpenTPW.Scripting;

namespace OpenTPW;

/// <summary>
/// The policy that runs the enabled script mods' hooks over a base policy (the faithful guests or the smarter-guests policy; docs/SCRIPT-MODS.md, "Hook API v1"). Every decision first takes the base
/// policy's answer; a hook that a mod implements then gets it as <c>prev</c> and returns the answer to use, mod after mod in the resolved, saved order, each receiving the previous result. A mod that
/// fails (a runtime error, its budget or a result outside the hook's type and range) returns <c>prev</c>, so it undoes neither the base policy nor the mods before it. The host still owns every effect
/// and every <see cref="WorldRng"/> draw; a script has the stateless <c>rng</c> of its call identity (park seed, mod id, hook, guest birth id, park turn, draw ordinal) and nothing else.
/// <list type="bullet">
/// <item><c>scoreAttraction(guest, candidate, prev)</c> replaces <see cref="IGuestDecisions.ScoreAttraction"/>'s score (an int, 0 to <see cref="ScriptLimits.MaxScore"/>).</item>
/// <item><c>joinQueue(guest, attraction, prev)</c> replaces <see cref="IGuestDecisions.DecideAdmission"/>'s verdict (a bool, true admits).</item>
/// <item><c>leaveQueue(guest, attraction, prev)</c> replaces <see cref="IGuestDecisions.DecideQueuePatience"/>'s verdict (a bool, true leaves).</item>
/// <item><c>wanderGoal(guest, prev)</c> replaces <see cref="IGuestDecisions.ChooseWanderGoal"/>'s goal (an int, <c>y * 1024 + x</c> of a cell inside the map).</item>
/// </list>
/// The booth, the comparison rules and the use memory are not hooks in API 1; they pass through. The policy needs the guests' birth ids, so it brings the extension state
/// (<see cref="UsesExtensionState"/>) without any of the smarter-guests rules.
/// </summary>
// [EXT:script-mods] OpenTPW's own scripted mods (TPW Script); the original game had no mods
internal sealed class ScriptGuestDecisions : IGuestDecisions
{
	private readonly IGuestDecisions inner;

	public ScriptGuestDecisions( IGuestDecisions inner, ScriptModRuntime runtime )
	{
		this.inner = inner ?? throw new ArgumentNullException( nameof( inner ) );
		Runtime = runtime ?? throw new ArgumentNullException( nameof( runtime ) );
	}

	/// <summary>The mods, their state and their counters.</summary>
	public ScriptModRuntime Runtime { get; }

	/// <summary>The base policy this one wraps.</summary>
	public IGuestDecisions Inner => inner;

	/// <summary>Script mods need the birth ids (and the park seed) that the guest identity state gives; the base policy may need it too.</summary>
	public bool UsesExtensionState => true;

	public bool ScoresDisconnectedQueues => inner.ScoresDisconnectedQueues;

	public bool UsesValueForMoney => inner.UsesValueForMoney;

	public bool UsesSpreading => inner.UsesSpreading;

	public bool UsesPlanning => inner.UsesPlanning;

	public bool UsesPlanHorizon => inner.UsesPlanHorizon;

	public bool UsesPathComfort => inner.UsesPathComfort;

	public bool UsesBinCarry => inner.UsesBinCarry;

	public int BinSearchRadius( in DecisionContext context, in GuestView guest ) => inner.BinSearchRadius( context, guest );

	public int BinMaxRouteCells( in DecisionContext context, in GuestView guest ) => inner.BinMaxRouteCells( context, guest );

	/// <summary>A mod's <c>leaveQueue</c> hook can answer at any time, so the host asks every turn; without one the base policy's answer stands.</summary>
	public int QueuePatienceQuietTurns( in DecisionContext context, ulong birthId ) => Runtime.Runs( ScriptHooks.LeaveQueue ) ? -1 : inner.QueuePatienceQuietTurns( context, birthId );

	public PathDecision ChoosePath( in DecisionContext context, in GuestView guest, in WanderPathView path ) => inner.ChoosePath( context, guest, path );

	public int AlternativeLookAfterTurns( in DecisionContext context, in GuestView guest ) => inner.AlternativeLookAfterTurns( context, guest );

	public bool SwitchesTarget( in DecisionContext context, in GuestView guest, in AttractionView target, uint targetScore, in AttractionView candidate, uint candidateScore ) =>
		inner.SwitchesTarget( context, guest, target, targetScore, candidate, candidateScore );

	public bool PriceTurnedAway( in DecisionContext context, in GuestView guest, in AttractionView attraction ) => inner.PriceTurnedAway( context, guest, attraction );

	public PriceStallDecision DecidePriceStall( in DecisionContext context, in GuestView guest, int consecutive ) => inner.DecidePriceStall( context, guest, consecutive );

	public void BeginTick()
	{
		inner.BeginTick();
		Runtime.BeginTick();
	}

	public void BeginDecision()
	{
		inner.BeginDecision();
		Runtime.BeginDecision( ScriptHooks.ScoreAttraction );
	}

	public void EndDecision()
	{
		Runtime.EndDecision();
		inner.EndDecision();
	}

	private static ScriptCallIdentity IdentityOf( in DecisionContext context, in GuestView guest ) =>
		new( unchecked( (long)context.ParkSeed ), unchecked( (long)guest.BirthId ), context.ParkTurn );

	public BoothDecision DecideBooth( in DecisionContext context, in GuestView guest, in BoothView booth ) => inner.DecideBooth( context, guest, booth );

	public uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction )
	{
		var score = inner.ScoreAttraction( context, guest, attraction );
		if ( !Runtime.Runs( ScriptHooks.ScoreAttraction ) )
			return score;
		var guestView = ScriptHookViews.Guest( context, guest );
		var candidate = ScriptHookViews.Attraction( attraction );
		var identity = IdentityOf( context, guest );
		var spec = ScriptResultSpec.Int( 0, ScriptLimits.MaxScore );
		ScriptValue current = ScriptValue.FromInt( score );
		foreach ( var mod in Runtime.Mods )
			current = mod.Call( ScriptHooks.ScoreAttraction, [guestView, candidate, current], current, spec, identity );
		return (uint)current.AsInt;
	}

	public bool BeatsInMajorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) => inner.BeatsInMajorDecision( context, guest, score, best );

	public bool BeatsInMinorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) => inner.BeatsInMinorDecision( context, guest, score, best );

	public QueueAdmission DecideAdmission( in DecisionContext context, in GuestView guest, in AdmissionView admission )
	{
		var basis = inner.DecideAdmission( context, guest, admission );
		if ( !Runtime.Runs( ScriptHooks.JoinQueue ) )
			return basis;
		var guestView = ScriptHookViews.Guest( context, guest );
		var view = ScriptHookViews.Admission( admission );
		var identity = IdentityOf( context, guest );
		var admits = ScriptValue.FromBool( basis.Admits );
		foreach ( var mod in Runtime.Mods )
			admits = mod.Call( ScriptHooks.JoinQueue, [guestView, view, admits], admits, ScriptResultSpec.Bool, identity );
		// the verdict keeps the base policy's refusal (which excitement gate, or the limit) while the answer is still its own; a flipped answer is a plain admission or the queue-too-long refusal
		if ( admits.AsBool == basis.Admits )
			return basis;
		return admits.AsBool ? QueueAdmission.Admit : new QueueAdmission( QueueJoinResult.QueueTooLong );
	}

	public QueuePatienceDecision DecideQueuePatience( in DecisionContext context, in GuestView guest, in QueuePatienceView queue )
	{
		var basis = inner.DecideQueuePatience( context, guest, queue );
		if ( !Runtime.Runs( ScriptHooks.LeaveQueue ) )
			return basis;
		var guestView = ScriptHookViews.Guest( context, guest );
		var view = ScriptHookViews.Queue( context, queue );
		var identity = IdentityOf( context, guest );
		var leaves = ScriptValue.FromBool( basis.Leave );
		foreach ( var mod in Runtime.Mods )
			leaves = mod.Call( ScriptHooks.LeaveQueue, [guestView, view, leaves], leaves, ScriptResultSpec.Bool, identity );
		if ( leaves.AsBool == basis.Leave )
			return basis;
		return leaves.AsBool ? new QueuePatienceDecision( true, GuestThought.Bored ) : QueuePatienceDecision.Stay;
	}

	public PostUseDecision RememberUse( in DecisionContext context, in GuestView guest, in AttractionView ride ) => inner.RememberUse( context, guest, ride );

	public GuestWander.Destination ChooseWanderGoal( in DecisionContext context, in GuestView guest, in GuestWander.Destination proposal )
	{
		var basis = inner.ChooseWanderGoal( context, guest, proposal );
		if ( !Runtime.Runs( ScriptHooks.WanderGoal ) || context.GridWidth <= 0 || context.GridHeight <= 0 )
			return basis;
		var guestView = ScriptHookViews.Guest( context, guest );
		var identity = IdentityOf( context, guest );
		var width = context.GridWidth;
		var height = context.GridHeight;
		// a goal is addressed by its cell alone; the proposal keeps its sub-cell point while a mod leaves the goal where it is
		var start = GoalOf( basis.Cell );
		var spec = ScriptResultSpec.Int( 0, (long)(ScriptLimits.GoalStride - 1) * ScriptLimits.GoalStride + ScriptLimits.GoalStride - 1 );
		bool InsideMap( long goal ) => goal % ScriptLimits.GoalStride < width && goal / ScriptLimits.GoalStride < height;
		var current = ScriptValue.FromInt( start );
		foreach ( var mod in Runtime.Mods )
			current = mod.Call( ScriptHooks.WanderGoal, [guestView, current], current, spec, identity, InsideMap );
		if ( current.AsInt == start )
			return basis;
		var goal = current.AsInt;
		return new GuestWander.Destination( ((int)(goal % ScriptLimits.GoalStride), (int)(goal / ScriptLimits.GoalStride)), GuestWander.CentreByte, GuestWander.CentreByte );
	}

	private static long GoalOf( (int X, int Y) cell ) => (long)cell.Y * ScriptLimits.GoalStride + cell.X;
}

/// <summary>The hook names of API 1 (<see cref="ScriptLimits.HookParameters"/>).</summary>
internal static class ScriptHooks
{
	public const string ScoreAttraction = "scoreAttraction";
	public const string JoinQueue = "joinQueue";
	public const string LeaveQueue = "leaveQueue";
	public const string WanderGoal = "wanderGoal";
}
