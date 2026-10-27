namespace OpenTPW;

/// <summary>
/// The policy of the <c>opentpw.smarter-guests</c> extension (docs/SMARTER-GUESTS.md). Version 1.6.0 adds the litter bins (phase 6b: a guest with litter looks eight cells around it for a bin, not three, <see cref="SmarterBinCarry"/>). Version 1.5.0 added clean and quiet paths (phase 6: a wander goal on or beside litter or in a crowd is swapped for a cleaner or quieter cell of the same walk, and a guest standing in litter or a crowd complains with a small, rate-limited happiness cost; see <see cref="SmarterPathComfort"/>); 1.4.0 adds planning needs (phase 5: a service whose need will cross its level before the guest is done with its ride is scored as if it had, a service on the guest's way gets a small detour step, and shortly after a meal a ride that would make the guest sick scores lower; see <see cref="SmarterGuestsRules.Planned"/> and <see cref="SmarterGuestsRules.TooMuchAfterMeal"/>); 1.3.0 adds queue patience (phase 4: a guest does not join a queue whose expected wait, counting only the queue ahead, exceeds its patience and gives up after waiting too long, each only when another ride it could choose is clearly quicker; see <see cref="SmarterGuestsRules.JoinPatienceTurns"/>); 1.2.0 adds spreading (phase 3: an expected-wait queue term for every attraction, a stable per-guest preference per attraction instance and hysteresis in the minor decision, see <see cref="SmarterGuestsRules.ExpectedWait256"/>); 1.1.0 is value for money (phase 2): a guest no longer walks to a shop or sideshow whose price it expects the stand to
/// refuse (<see cref="SmarterGuestsRules.WouldRefuse"/>, with a per-guest price sensitivity trait), goes home after <see cref="SmarterGuestsRules.StallsBeforeGoingHome"/> decisions in a row that this rule emptied, and
/// the booth weighs the entrance fee against a park value that counts the open rides and sideshows (<see cref="SmarterGuestsRules.BoothParkValue"/>). Everything else still delegates to
/// <see cref="OriginalGuestDecisions"/>. Later phases add their rules here, each one bumping the extension's version, which is the algorithm identity a park save records. The policy draws nothing and holds no
/// state: any variation comes from <see cref="SmarterGuestsState.Trait"/>, and the run of price-emptied decisions is the host's.
/// </summary>
// [EXT:smarter-guests] OpenTPW's own extension; the original game has no such policy
public sealed class SmarterGuestDecisions : IGuestDecisions
{
	private readonly IGuestDecisions original = OriginalGuestDecisions.Instance;

	/// <summary>Which of the switchable rules (phases 5 and 6) run: all of them in the shipped extension; fewer only in a measurement or a test (<see cref="SmarterRuleSet"/>).</summary>
	private readonly SmarterRuleSet rules;

	/// <summary>The phase 5 part of <see cref="rules"/>.</summary>
	private readonly SmarterRuleSet planning;

	public SmarterGuestDecisions() : this( SmarterRuleSet.All )
	{
	}

	public SmarterGuestDecisions( SmarterRuleSet rules )
	{
		this.rules = rules;
		planning = rules & SmarterRuleSet.Phase5;
	}

	public bool UsesExtensionState => true;

	// [EXT:smarter-guests] the planning, patience and expected-wait rules read a queue's join cell, so this policy keeps scoring only the attractions that have one (the original scores a disconnected real queue back, OriginalGuestDecisions)
	public bool ScoresDisconnectedQueues => false;

	public bool UsesValueForMoney => true;

	public bool UsesSpreading => true;

	public bool UsesPlanning => planning != SmarterRuleSet.None;

	public bool UsesPathComfort => (rules & SmarterRuleSet.Paths) != 0;

	public bool UsesBinCarry => rules.HasFlag( SmarterRuleSet.Bins );

	// [EXT:smarter-guests-litter] the original's bin search and walk, over a larger radius
	public int BinSearchRadius( in DecisionContext context, in GuestView guest ) => SmarterBinCarry.Radius( guest );

	public int BinMaxRouteCells( in DecisionContext context, in GuestView guest ) => SmarterBinCarry.MaxRouteCells;

	public bool UsesPlanHorizon => planning.HasFlag( SmarterRuleSet.Planning );

	/// <summary>
	/// The give-up test needs a wait above the guest's patience (<see cref="SmarterGuestsRules.GivesUp"/>: <c>waited x 256 &gt; PatienceTurns256</c>), so before <c>floor(PatienceTurns256 / 256) + 1</c> turns the answer is the original's: stay.
	/// </summary>
	public int QueuePatienceQuietTurns( in DecisionContext context, ulong birthId ) =>
		(int)Math.Min( int.MaxValue - 1, SmarterGuestsRules.PatienceTurns256( SmarterGuestsRules.GiveUpTurns, SmarterGuestsRules.PatiencePercent( context.Trait( birthId, SmarterGuestsRules.PatienceTrait ) ) ) / 256 + 1 );

	/// <summary>The turn this guest's give-up test starts (its own patience band), so the host scans the park's rides for it only from then on.</summary>
	public int AlternativeLookAfterTurns( in DecisionContext context, in GuestView guest ) =>
		(int)Math.Min( int.MaxValue, SmarterGuestsRules.PatienceTurns256( SmarterGuestsRules.GiveUpTurns, SmarterGuestsRules.PatiencePercent( context.Trait( guest, SmarterGuestsRules.PatienceTrait ) ) ) / 256 );

	/// <summary>The entrance fee against the park's worth: the original's verdict over a park value that counts the open attractions.</summary>
	// [EXT:smarter-guests-value] the host's draw and the rating are the original's; only the rating part of the value is scaled, by the open attractions the host counted (85 to 115 percent)
	public BoothDecision DecideBooth( in DecisionContext context, in GuestView guest, in BoothView booth )
	{
		var value = SmarterGuestsRules.BoothParkValue( BoothVerdict.ParkValue( booth.ParkPoints, booth.Draw, context.Settings ), booth.OpenAttractions, context.Settings );
		return new( BoothVerdict.Decide( value, booth.Fee, context.Settings ) );
	}

	/// <summary>The original's score, zeroed for a shop or sideshow the guest expects to refuse.</summary>
	// [EXT:smarter-guests-value] price enters the choice: a score of 0 is below the choice floor, so the guest does not walk to a stand that will turn it away (the stand-point test stays the host's)
	public uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction )
	{
		// [EXT:smarter-guests-planning] a service is scored on the needs the guest will have (planning) or has one step more of when the stop is on its way (detour); a ride, and a service no rule concerns, on the guest as it is
		var score = SpreadScore( context, Seen( context, guest, attraction ), attraction );
		score = score == 0 || !Refuses( context, guest, attraction ) ? score : 0;
		// [EXT:smarter-guests-planning] shortly after a meal a ride that would make this guest sick scores lower (never below the choice floor when it was above it)
		return planning.HasFlag( SmarterRuleSet.MealTime ) && score >= AttractionScore.CandidateFloor && SmarterGuestsRules.TooMuchAfterMeal( context, guest, attraction ) ? SmarterGuestsRules.LowerAfterMeal( score ) : score;
	}

	/// <summary>The guest as it scores this attraction: planned and nudged for a service (<see cref="SmarterGuestsRules.Planned"/>), as it is otherwise.</summary>
	private GuestView Seen( in DecisionContext context, in GuestView guest, in AttractionView attraction ) =>
		UsesPlanning ? SmarterGuestsRules.Planned( guest, attraction, context.ParkTurn, planning.HasFlag( SmarterRuleSet.Planning ), planning.HasFlag( SmarterRuleSet.Detour ) ) : guest;

	/// <summary>
	/// The original's score with the queue match replaced by the expected wait (every attraction, at any distance: guests ahead plus guests walking there, over the observed throughput), scaled by the guest's
	/// stable preference for this attraction instance. An attraction without a join cell, or a view with no estimate, is scored by the original alone.
	/// </summary>
	// [EXT:smarter-guests-spread] the queue term counts everywhere and by the time a guest would wait; a stateless 94-106 percent preference lets equal rides share guests with no decision randomness
	private static uint SpreadScore( in DecisionContext context, in GuestView guest, in AttractionView attraction )
	{
		if ( !attraction.HasJoinCell || SmarterGuestsRules.ExpectedWait256( attraction ) is not { } wait )
			return OriginalGuestDecisions.Instance.ScoreAttraction( context, guest, attraction );
		var input = OriginalGuestDecisions.ScoreInputOf( context, guest, attraction, SmarterGuestsRules.QueueMatchOf( wait ) );
		var score = AttractionScore.Compute( input, context.Settings.DecisionWeights, context.Settings.DecisionVariables );
		if ( score != 0 )
			score = SmarterGuestsRules.ApplyPreference( score, SmarterGuestsRules.PreferencePercent( context.Trait( guest, SmarterGuestsRules.PreferenceTrait ), attraction.Id ) );
		// a long expected wait lowers the score but never takes a candidate the original would have chosen below the choice floor: giving up on a queue is the patience rule's job (phase 4), not the choice's.
		// The same floor keeps a ride the guest has not used lately and would be admitted to (the guest "has nothing to do" otherwise). Among the candidates kept at the floor the order of their own scores stays:
		// floor + score / 5, so the wait and the preference still rank them (and a genuine score of 10 is never ranked below one that was lifted by more than a point)
		if ( score < AttractionScore.CandidateFloor && (OriginalGuestDecisions.Instance.ScoreAttraction( context, guest, attraction ) >= AttractionScore.CandidateFloor || IsUnusedAdmissibleRide( context, guest, attraction )) )
			return AttractionScore.CandidateFloor + score / SmarterGuestsRules.FloorOrderDivisor;
		return score;
	}

	/// <summary>A ride that is in neither of the guest's histories, is not of the type it used last and whose excitement the admission gate would let it through to.</summary>
	private static bool IsUnusedAdmissibleRide( in DecisionContext context, in GuestView guest, in AttractionView attraction ) =>
		SmarterGuestsRules.IsUnusedAdmissibleRide( context, guest, attraction );

	/// <summary>A walking guest keeps a ride it is heading for unless the best ride in reach beats it by the hysteresis margin; a shop, toilet or sideshow on the way stays an impulse stop, as in the original.</summary>
	// [EXT:smarter-guests-spread] hysteresis between rides: the original switches on any scoring ride, which makes guests change their minds on the way; measured, the same rule on shops cut revenue in a shop-and-toilet park
	public bool SwitchesTarget( in DecisionContext context, in GuestView guest, in AttractionView target, uint targetScore, in AttractionView candidate, uint candidateScore ) =>
		target.Kind != RideVisitorKind.Ride || candidate.Kind != RideVisitorKind.Ride || SmarterGuestsRules.BeatsTargetByMargin( targetScore, candidateScore );

	private static bool Refuses( in DecisionContext context, in GuestView guest, in AttractionView attraction ) =>
		SmarterGuestsRules.IsPriced( attraction )
		&& SmarterGuestsRules.WouldRefuse( attraction, guest.Money, SmarterGuestsRules.SensitivityPercent( context.Trait( guest, SmarterGuestsRules.PriceSensitivityTrait ) ) );

	/// <summary>
	/// True for a stand the price rule zeroed that the original would have let the guest choose (it scores at least the choice floor). Judged on the guest as <see cref="ScoreAttraction"/> sees it (planned), so the two agree on
	/// which stands the price emptied; the price itself is the real guest's (the host computed the willingness from its real meters, which is what the stand-point test uses).
	/// </summary>
	public bool PriceTurnedAway( in DecisionContext context, in GuestView guest, in AttractionView attraction ) =>
		Refuses( context, guest, attraction ) && original.ScoreAttraction( context, Seen( context, guest, attraction ), attraction ) >= AttractionScore.CandidateFloor;

	/// <summary>Goes home, thinking the original's "too expensive" thought, once the run of price-emptied decisions reaches the limit.</summary>
	// [EXT:smarter-guests-value] the original guest, refused at the stand, loses happiness until it leaves; this one is not sent to the stand and, with nothing else to do, leaves with the same thought (thought 6, [BIN:TPW-EXE:0x00500756 price refusal])
	public PriceStallDecision DecidePriceStall( in DecisionContext context, in GuestView guest, int consecutive ) =>
		consecutive >= SmarterGuestsRules.StallsBeforeGoingHome ? new PriceStallDecision( true, GuestThought.Dissatisfied ) : PriceStallDecision.Stay;

	public bool BeatsInMajorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) => original.BeatsInMajorDecision( context, guest, score, best );

	/// <summary>The original's comparison. A stand the price rule zeroed can still tie the initial 0 on an odd park turn, as any zero score can; overriding that was measured and moves other parks for the worse (docs/SMARTER-GUESTS.md).</summary>
	public bool BeatsInMinorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) => original.BeatsInMinorDecision( context, guest, score, best );

	/// <summary>
	/// The original's gates, then patience: a guest that would wait longer than it is willing to is not admitted ("queue too long", the original's refusal and thought, so the statistics, the temporary history and the
	/// guest's next choice are the host's existing ones).
	/// </summary>
	// [EXT:smarter-guests-patience] the expected wait is the phase 3 estimate for a guest at the back (the queue plus the walkers), against the base × the guest's patience band
	public QueueAdmission DecideAdmission( in DecisionContext context, in GuestView guest, in AdmissionView admission )
	{
		var basis = original.DecideAdmission( context, guest, admission );
		if ( !basis.Admits || !SmarterGuestsRules.Concerns( admission.Attraction ) )
			return basis;
		var percent = SmarterGuestsRules.PatiencePercent( context.Trait( guest, SmarterGuestsRules.PatienceTrait ) );
		// at the back the guest is already on the queue's last cell: the walkers still on their way will stand behind it, so only the queue ahead counts
		return SmarterGuestsRules.WaitExceedsPatience( admission.Attraction with { Reservations = 0 }, admission.BestAlternativeWait256, percent ) ? new QueueAdmission( QueueJoinResult.QueueTooLong ) : basis;
	}

	/// <summary>Gives up on a queue after waiting longer than the guest's patience, thinking that the queue is too long; the host dismisses it (the medium happiness step of the original dismissal) as an impatient exit.</summary>
	// [EXT:smarter-guests-patience] runs after the original's leave tests, never for a guest called forward or in an interlude (the host's call sites); never for the front of the queue, never for a shop, sideshow or toilet, and only for a ride that has a clearly shorter alternative
	public QueuePatienceDecision DecideQueuePatience( in DecisionContext context, in GuestView guest, in QueuePatienceView queue )
	{
		var basis = original.DecideQueuePatience( context, guest, queue );
		if ( basis.Leave || queue.JoinTurn < 0 || !SmarterGuestsRules.Concerns( queue.Attraction ) )
			return basis;
		var percent = SmarterGuestsRules.PatiencePercent( context.Trait( guest, SmarterGuestsRules.PatienceTrait ) );
		return SmarterGuestsRules.GivesUp( context.ParkTurn - queue.JoinTurn, queue.Attraction, queue.Position, queue.BestAlternativeWait256, percent ) ? new QueuePatienceDecision( true, GuestThought.QueueTooLong ) : basis;
	}

	public PostUseDecision RememberUse( in DecisionContext context, in GuestView guest, in AttractionView ride ) => original.RememberUse( context, guest, ride );

	/// <summary>The wander goal weighted by the litter and the crowd on the cells of the walk, and a complaint of a guest standing in litter or a crowd.</summary>
	// [EXT:smarter-guests-paths] the original's walk and draws are the host's; the policy only names a cleaner or quieter cell of that walk and a small, rate-limited complaint (SmarterPathComfort)
	public PathDecision ChoosePath( in DecisionContext context, in GuestView guest, in WanderPathView path ) =>
		SmarterPathComfort.Decide( context, guest, path, rules.HasFlag( SmarterRuleSet.Litter ), rules.HasFlag( SmarterRuleSet.Crowding ) );

	public GuestWander.Destination ChooseWanderGoal( in DecisionContext context, in GuestView guest, in GuestWander.Destination proposal ) =>
		original.ChooseWanderGoal( context, guest, proposal );
}
