namespace OpenTPW;

/// <summary>
/// The faithful guests: a pure extraction of the original's decisions (docs/GUESTS.md) from <see cref="GuestSimulation"/>. It holds no state and draws nothing; the host passes in every
/// value the original takes from the generator.
/// </summary>
public sealed class OriginalGuestDecisions : IGuestDecisions
{
	public static OriginalGuestDecisions Instance { get; } = new();

	public bool ScoresDisconnectedQueues => true;

	/// <summary>The unmodified booth verdict, from the host's draw.</summary>
	// [BIN:TPW-EXE:0x004FF5B0 booth verdict] GP uses the one draw the host took first (BoothVerdict.Decide)
	public BoothDecision DecideBooth( in DecisionContext context, in GuestView guest, in BoothView booth ) =>
		new( BoothVerdict.Decide( booth.ParkPoints, booth.Fee, booth.Draw, context.Settings ) );

	/// <summary>The attraction score of the original (<see cref="AttractionScore.Compute"/>).</summary>
	public uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction ) =>
		AttractionScore.Compute( ScoreInputOf( context, guest, attraction ), context.Settings.DecisionWeights, context.Settings.DecisionVariables );

	/// <summary>What the original's score reads for this guest and attraction; the smarter policy scores the same input with its own queue match.</summary>
	internal static AttractionScoreInput ScoreInputOf( in DecisionContext context, in GuestView guest, in AttractionView attraction, uint? queueMatchOverride = null)
	{
		var settings = context.Settings;
		var type = settings.Types[Math.Min( guest.Type, settings.Types.Count - 1 )];
		return new AttractionScoreInput( guest.CellX, guest.CellY, attraction.BackCell.X, attraction.BackCell.Y, attraction.CellModifier, attraction.QueueLength, attraction.QueueSizeInCells,
			type.PreferredExcitement, attraction.LiveExcitement, (attraction.StaticExcitementLevel & 0xFF) != 0, guest.Thirst, guest.Hunger, guest.Toilet, guest.Nausea,
			attraction.ThirstEffect, attraction.HungerEffect, attraction.ProvidesRelief, attraction.AgeDays, context.Raining, attraction.IsIndoors,
			attraction.GoldenTicketCost, attraction.UpgradeCost, attraction.Id, attraction.SameTypeAsLastUsed, guest.UsedHistory, guest.TemporaryHistory, queueMatchOverride );
	}

	/// <summary>The original's comparison of the choice loop: a score of 10 or more that beats the best (signed), or ties it on an odd park turn.</summary>
	// [BIN:STP-PPC:0x100E90A8 choice loop] cmplwi 10 unsigned, cmpw signed against the best, the equal case gated by game +0x1da70c bit 0
	public bool BeatsInMajorDecision( in DecisionContext context, in GuestView guest, uint score, uint best )
	{
		if ( score < AttractionScore.CandidateFloor )
			return false;
		var (signed, current) = (unchecked( (int)score ), unchecked( (int)best ));
		return !(signed < current || (signed == current && (context.ParkTurn & 1) == 0));
	}

	/// <summary>The minor decision's comparison: it beats the best so far (unsigned, initially 0), or ties it on an odd park turn.</summary>
	// [BIN:TPW-EXE:0x004FD570 minor decision] kept when the score beats the best (starting at 0) or ties it on an odd turn
	public bool BeatsInMinorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) =>
		best < score || (score == best && (context.ParkTurn & 1) != 0);

	/// <summary>The excitement gate, then the queue limit (the checks of <see cref="RideVisitorBridge.JoinQueue"/> after the room test).</summary>
	public QueueAdmission DecideAdmission( in DecisionContext context, in GuestView guest, in AdmissionView admission )
	{
		if ( QueueJoinRules.Refusal( admission.ExcitementLevelWord, admission.JoinDifference ) is { } refusal )
			return new QueueAdmission( refusal );
		if ( admission.QueueCount >= admission.QueueLimit )
			return new QueueAdmission( QueueJoinResult.QueueTooLong );
		return QueueAdmission.Admit;
	}

	/// <summary>The original has no patience limit that can fire: the guest stays.</summary>
	// [BIN:TPW-EXE:0x004FFFF0 queue boredom] the needs branch returns before the boredom test, so no time limit is reachable (QueueTests); the hook is the seam of the smarter-guests patience rule
	public QueuePatienceDecision DecideQueuePatience( in DecisionContext context, in GuestView guest, in QueuePatienceView queue ) => QueuePatienceDecision.Stay;

	/// <summary>The original always pushes the attraction into the used history.</summary>
	// [BIN:STP-PPC:0x100E9FD4 used history] the end of a use pushes the attraction's id into the used history
	public PostUseDecision RememberUse( in DecisionContext context, in GuestView guest, in AttractionView ride ) => new( true );

	/// <summary>The original walks to the destination its search picked.</summary>
	public GuestWander.Destination ChooseWanderGoal( in DecisionContext context, in GuestView guest, in GuestWander.Destination proposal ) => proposal;
}
