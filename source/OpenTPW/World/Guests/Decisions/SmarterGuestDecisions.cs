namespace OpenTPW;

/// <summary>
/// The policy of the <c>opentpw.smarter-guests</c> extension (docs/SMARTER-GUESTS.md). Version 1.0.0 is the seam alone: every call delegates to <see cref="OriginalGuestDecisions"/>, so the guests
/// behave as the original and only the extension's own saved state (<see cref="SmarterGuestsState"/>) differs. The rules of the later phases land here, each one bumping the extension's
/// version, which is the algorithm identity a park save records. The policy draws nothing: any variation comes from <see cref="SmarterGuestsState.Trait"/>.
/// </summary>
// [EXT:smarter-guests] OpenTPW's own extension; the original game has no such policy
public sealed class SmarterGuestDecisions : IGuestDecisions
{
	private readonly IGuestDecisions original = OriginalGuestDecisions.Instance;

	public bool UsesExtensionState => true;

	public BoothDecision DecideBooth( in DecisionContext context, in GuestView guest, in BoothView booth ) => original.DecideBooth( context, guest, booth );

	public uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction ) => original.ScoreAttraction( context, guest, attraction );

	public bool BeatsInMajorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) => original.BeatsInMajorDecision( context, guest, score, best );

	public bool BeatsInMinorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) => original.BeatsInMinorDecision( context, guest, score, best );

	public QueueAdmission DecideAdmission( in DecisionContext context, in GuestView guest, in AdmissionView admission ) => original.DecideAdmission( context, guest, admission );

	public QueuePatienceDecision DecideQueuePatience( in DecisionContext context, in GuestView guest, in QueuePatienceView queue ) =>
		original.DecideQueuePatience( context, guest, queue );

	public PostUseDecision RememberUse( in DecisionContext context, in GuestView guest, in AttractionView ride ) => original.RememberUse( context, guest, ride );

	public GuestWander.Destination ChooseWanderGoal( in DecisionContext context, in GuestView guest, in GuestWander.Destination proposal ) =>
		original.ChooseWanderGoal( context, guest, proposal );
}
