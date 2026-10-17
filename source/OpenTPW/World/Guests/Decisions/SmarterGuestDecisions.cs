namespace OpenTPW;

/// <summary>
/// The policy of the <c>opentpw.smarter-guests</c> extension (docs/SMARTER-GUESTS.md). Version 1.2.0 adds spreading (phase 3: an expected-wait queue term for every attraction, a stable per-guest preference per attraction instance and hysteresis in the minor decision, see <see cref="SmarterGuestsRules.ExpectedWait256"/>); 1.1.0 is value for money (phase 2): a guest no longer walks to a shop or sideshow whose price it expects the stand to
/// refuse (<see cref="SmarterGuestsRules.WouldRefuse"/>, with a per-guest price sensitivity trait), goes home after <see cref="SmarterGuestsRules.StallsBeforeGoingHome"/> decisions in a row that this rule emptied, and
/// the booth weighs the entrance fee against a park value that counts the open rides and sideshows (<see cref="SmarterGuestsRules.BoothParkValue"/>). Everything else still delegates to
/// <see cref="OriginalGuestDecisions"/>. Later phases add their rules here, each one bumping the extension's version, which is the algorithm identity a park save records. The policy draws nothing and holds no
/// state: any variation comes from <see cref="SmarterGuestsState.Trait"/>, and the run of price-emptied decisions is the host's.
/// </summary>
// [EXT:smarter-guests] OpenTPW's own extension; the original game has no such policy
public sealed class SmarterGuestDecisions : IGuestDecisions
{
	private readonly IGuestDecisions original = OriginalGuestDecisions.Instance;

	public bool UsesExtensionState => true;

	public bool UsesValueForMoney => true;

	public bool UsesSpreading => true;

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
		var score = SpreadScore( context, guest, attraction );
		return score == 0 || !Refuses( context, guest, attraction ) ? score : 0;
	}

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
		// a long expected wait lowers the score but never takes a candidate the original would have chosen below the choice floor: giving up on a queue is the patience rule's job (phase 4), not the choice's
		if ( score < AttractionScore.CandidateFloor && OriginalGuestDecisions.Instance.ScoreAttraction( context, guest, attraction ) >= AttractionScore.CandidateFloor )
			return AttractionScore.CandidateFloor;
		return score;
	}

	/// <summary>A walking guest keeps a ride it is heading for unless the best ride in reach beats it by the hysteresis margin; a shop, toilet or sideshow on the way stays an impulse stop, as in the original.</summary>
	// [EXT:smarter-guests-spread] hysteresis between rides: the original switches on any scoring ride, which makes guests change their minds on the way; measured, the same rule on shops cut revenue in a shop-and-toilet park
	public bool SwitchesTarget( in DecisionContext context, in GuestView guest, in AttractionView target, uint targetScore, in AttractionView candidate, uint candidateScore ) =>
		target.Kind != RideVisitorKind.Ride || candidate.Kind != RideVisitorKind.Ride || SmarterGuestsRules.BeatsTargetByMargin( targetScore, candidateScore );

	private static bool Refuses( in DecisionContext context, in GuestView guest, in AttractionView attraction ) =>
		SmarterGuestsRules.IsPriced( attraction )
		&& SmarterGuestsRules.WouldRefuse( attraction, guest.Money, SmarterGuestsRules.SensitivityPercent( context.Trait( guest, SmarterGuestsRules.PriceSensitivityTrait ) ) );

	/// <summary>True for a stand the price rule zeroed that the original would have let the guest choose (it scores at least the choice floor).</summary>
	public bool PriceTurnedAway( in DecisionContext context, in GuestView guest, in AttractionView attraction ) =>
		Refuses( context, guest, attraction ) && original.ScoreAttraction( context, guest, attraction ) >= AttractionScore.CandidateFloor;

	/// <summary>Goes home, thinking the original's "too expensive" thought, once the run of price-emptied decisions reaches the limit.</summary>
	// [EXT:smarter-guests-value] the original guest, refused at the stand, loses happiness until it leaves; this one is not sent to the stand and, with nothing else to do, leaves with the same thought (thought 6, [BIN:TPW-EXE:0x00500756 price refusal])
	public PriceStallDecision DecidePriceStall( in DecisionContext context, in GuestView guest, int consecutive ) =>
		consecutive >= SmarterGuestsRules.StallsBeforeGoingHome ? new PriceStallDecision( true, GuestThought.Dissatisfied ) : PriceStallDecision.Stay;

	public bool BeatsInMajorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) => original.BeatsInMajorDecision( context, guest, score, best );

	/// <summary>The original's comparison. A stand the price rule zeroed can still tie the initial 0 on an odd park turn, as any zero score can; overriding that was measured and moves other parks for the worse (docs/SMARTER-GUESTS.md).</summary>
	public bool BeatsInMinorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) => original.BeatsInMinorDecision( context, guest, score, best );

	public QueueAdmission DecideAdmission( in DecisionContext context, in GuestView guest, in AdmissionView admission ) => original.DecideAdmission( context, guest, admission );

	public QueuePatienceDecision DecideQueuePatience( in DecisionContext context, in GuestView guest, in QueuePatienceView queue ) =>
		original.DecideQueuePatience( context, guest, queue );

	public PostUseDecision RememberUse( in DecisionContext context, in GuestView guest, in AttractionView ride ) => original.RememberUse( context, guest, ride );

	public GuestWander.Destination ChooseWanderGoal( in DecisionContext context, in GuestView guest, in GuestWander.Destination proposal ) =>
		original.ChooseWanderGoal( context, guest, proposal );
}
