using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// [EXT:online-coop-commands] A guest's management windows in a shared park (C4b, docs/COOP.md "A guest's management windows"): the research, finance and loans windows' actions go into the
/// session's management queue (<see cref="CoopManagementRequests"/>) once the session's rights allowed them, their controls draw what the player asked for until it came back, and the HUD takes
/// the outcomes every frame: the door's (the queue's own waiter), the loans window's opening's and the waiting loan confirmation's, then the notes nobody waits for. A host's and a single player's
/// windows issue at once, as they always did.
/// </summary>
public sealed partial class ParkHud
{
	/// <summary>The guest's management queue while this park follows a session's host; null for a host and a single player, whose windows issue at once.</summary>
	private CoopManagementRequests? GuestManagement => level.Commands.SendsRequests && level.CoopSession?.Management is { Ended: false } queue ? queue : null;

	/// <summary>
	/// The session's management queue this HUD last saw: its notes are said from there (the session's, so a resync's new HUD says what this one could not), also after the session ended,
	/// when the park no longer names it.
	/// </summary>
	private CoopManagementRequests? managementSeen;

	/// <summary>
	/// A management window's action: on a guest, queued once the session's rights allowed it (refused, their note is said once for a run, and nothing is queued; a full target is no click,
	/// whose waiting note is said once for a run); on a host or a single player, issued at once. True when it was queued or accepted.
	/// </summary>
	private bool IssueManagement( ManagementTarget target, ManagementKnob? knob, ParkCommand command, ManagementWaiter? waiter = null )
	{
		if ( GuestManagement is not { } queue )
			return level.Commands.Issue( command ).Accepted;
		return QueueManagement( queue, new ManagementItem( target, knob, command ), waiter );
	}

	/// <summary>A guest's management action into <paramref name="queue"/>, after the session's rights allowed it; a full target is no click.</summary>
	private bool QueueManagement( CoopManagementRequests queue, ManagementItem item, ManagementWaiter? waiter = null ) => QueueManagement( queue, new[] { item }, waiter ).Queued;

	/// <summary>What became of a guest's management click: queued, or refused with its note (none for an object's open-state action while another waits, which is no click).</summary>
	private readonly record struct ManagementClick( bool Queued, ManagementNote Note );

	/// <summary>
	/// A guest's management action of one or more commands into <paramref name="queue"/>, whole or not at all: refused by the session's rights (asked silently for each), or for want of room
	/// (<see cref="ManagementEnqueued.Full"/>), its note joins the session's notes, said once the HUD's lines can be seen (C4b-2), with the error sound now; an object's open-state action
	/// while another waits is no click (<see cref="ManagementEnqueued.Busy"/>).
	/// </summary>
	private ManagementClick QueueManagement( CoopManagementRequests queue, IReadOnlyList<ManagementItem> items, ManagementWaiter? waiter = null )
	{
		foreach ( var item in items )
			if ( level.Commands.RightsRefusalOf( item.Command ) is { } refusal )
			{
				queue.NoteRefused( item, refusal );
				GameAudio.PlayUi( GameAudio.ErrorEvent );
				return new ManagementClick( false, new ManagementOutcome( new ManagementEntry( queue, 0, item.Target, item.Knob, item.Command, null ), ManagementOutcomeKind.Refused, refusal ).Note );
			}
		var enqueued = queue.Enqueue( items, waiter );
		if ( enqueued.Full && items.Count > 0 )
		{
			queue.NoteRefused( items[0], null );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
			return new ManagementClick( false, new ManagementNote( ManagementNoteKind.Waiting ) );
		}
		return new ManagementClick( enqueued.Queued, ManagementNote.None );
	}

	/// <summary>The command of <paramref name="knob"/>'s latest entry not resolved yet on a guest (what its control draws), or null: the park's value.</summary>
	private ParkCommand? PendingManagement( ManagementTarget target, ManagementKnob knob ) => GuestManagement?.PendingCommand( target, knob );

	/// <summary>The entrance fee the entry price window draws: a guest's latest fee not resolved yet, else the park's.</summary>
	private long DrawnEntranceFee => PendingManagement( ManagementTarget.Finance, ManagementKnob.Fee ) is SetEntranceFeeCommand fee ? fee.Fee : FinanceEconomy?.EntranceFee ?? 0;

	/// <summary>Whether the door is drawn open: a guest's latest door request not resolved yet, else the park's door.</summary>
	private bool DrawnParkOpen => PendingManagement( ManagementTarget.Finance, ManagementKnob.Door ) is SetParkOpenCommand door ? door.Open : FinanceEconomy is { IsParkOpen: true };

	/// <summary>The park's name as the entry price window draws it: a guest's latest name not resolved yet, else the park's.</summary>
	private string DrawnParkName => PendingManagement( ManagementTarget.Finance, ManagementKnob.Name ) is RenameParkCommand name
		? new ParkNameLines( name.Line1, name.Line2 ).DisplayName : level.ParkName?.DisplayName ?? "";

	/// <summary>A research category's effort as its slider draws it: a guest's latest effort not resolved yet, else the lab's.</summary>
	private int DrawnResearchEffort( ResearchCategory category ) =>
		PendingManagement( ManagementTarget.Research, ManagementKnob.Effort( category ) ) is SetResearchEffortCommand effort ? effort.Effort : ResearchEconomy?.Research.Effort[(int)category] ?? 0;

	/// <summary>The lab's workload as the knob projects it: a guest's latest workload not resolved yet, else the lab's.</summary>
	private int DrawnResearchWorkload =>
		PendingManagement( ManagementTarget.Research, ManagementKnob.Workload ) is SetResearchWorkloadCommand workload ? workload.Workload : ResearchEconomy?.Research.Workload ?? MinimumKnobWorkload;

	/// <summary>Whether a guest's workload request has not resolved yet: the knob follows its player until it has.</summary>
	private bool ResearchWorkloadWaits => PendingManagement( ManagementTarget.Research, ManagementKnob.Workload ) != null;

	/// <summary>Whether a guest's project move of <paramref name="category"/> has not resolved yet: the category's arrows take no click meanwhile, so each names the project its window shows.</summary>
	private bool ResearchMoveWaits( ResearchCategory category ) =>
		GuestManagement?.HasPending( ManagementTarget.Research, command => command is MoveResearchCommand move && move.Category == category ) == true;

	/// <summary>
	/// A guest's door click or key: the state asked for, unless the door is drawn in it already (the keys ask for explicit states; the button for the opposite of the drawn one). The advisor's
	/// event follows the request's accepted outcome (<see cref="CompleteGuestManagement"/>), whichever HUD is current then.
	/// </summary>
	private void RequestParkDoor( CoopManagementRequests queue, bool opens )
	{
		if ( opens == DrawnParkOpen )
			return;
		QueueManagement( queue, new ManagementItem( ManagementTarget.Finance, ManagementKnob.Door, new SetParkOpenCommand( opens ) ), queue.HudWaiter );
	}

	/// <summary>
	/// Once a frame, after the screens took the frame's input (and under the research finale): the door's outcomes (the advisor's event of an accepted one, whichever HUD queued it), the loans
	/// window's refreshes (each refills the list of the tab shown), the waiting loan confirmation's outcome, then the notes nobody waits for; every note is said once its messages can be seen.
	/// </summary>
	private void CompleteGuestManagement()
	{
		if ( level.CoopSession?.Management is { } queue )
		{
			managementSeen = queue;
			foreach ( var outcome in queue.TakeOutcomes( queue.HudWaiter ) )
			{
				if ( outcome is { Accepted: true, Command: SetParkOpenCommand door } )
					// [BIN:TPW-EXE:0x00519EF0 park door routine] the advisor's game event 3 (open) or 4 (close), as the host's press posts it; a guest's only for its accepted request
					host?.ParkDoorPressed?.Invoke( door.Open );
				else
					NoteManagement( outcome );
			}
		}
		if ( loansWaiter is { } opening )
			foreach ( var outcome in opening.Queue.TakeOutcomes( opening ) )
				CompleteLoanRefresh( outcome );
		if ( loanConfirmation is { } waiting )
			foreach ( var outcome in waiting.Waiter.Queue.TakeOutcomes( waiting.Waiter ) )
				CompleteLoanConfirmation( waiting.Screen, outcome );
		// [EXT:online-coop-commands] the upgrade list's waiting row (C4b-2)
		CompleteGuestUpgrade();
		SayManagementNotes();
	}

	/// <summary>Keeps <paramref name="outcome"/>'s note among the session's notes, to be said once the HUD's lines can be seen (nothing for none).</summary>
	private static void NoteManagement( ManagementOutcome outcome ) => outcome.Entry.Queue.AddNote( outcome );

	/// <summary>
	/// Whether the HUD's feedback lines can be seen (<see cref="DrawFeedback"/>): no screen hides the park (the ride view, the research finale), the object window's info arm is in and no
	/// message panel is open.
	/// </summary>
	private bool FeedbackShows => !InfoArmOpen && OpenedMessage == null && !Stack.Screens.Any( screen => screen.HidesBelow );

	/// <summary>
	/// Says the session's notes, oldest first, one at a time while the feedback lines can be seen and have room (they keep <see cref="MaximumFeedbackLines"/>), so none expires or is pushed
	/// out unseen; until then they wait in the session's queue. Each entry's note is said: a note whose words a line shows already renews that line (its time starts again, with the error
	/// sound once a frame) instead of adding a second. A rights' note belongs to the run of the session's refusals: inside the run's notice time it is the run's line already, and one said
	/// here keeps the session's own note of the same run from following it (a right taken back with entries queued is said once, whether a record or the queue's send refused them).
	/// </summary>
	private void SayManagementNotes()
	{
		if ( managementSeen is not { } queue )
			return;
		var sounded = new HashSet<string>();
		while ( FeedbackShows && queue.PeekNote() is { } outcome )
		{
			var note = outcome.Note;
			var text = ManagementNoteText( note );
			var shown = messages.FindIndex( message => message.Text == text );
			if ( note.Kind == ManagementNoteKind.Rights && Time.Now - lastSessionRefusalNotice < SessionRefusalNoticeSeconds && shown >= 0 )
			{
				queue.TakeNote();
				continue;
			}
			if ( shown >= 0 )
				messages[shown] = (text, 0);
			else if ( messages.Count < MaximumFeedbackLines )
				PostMessage( text );
			else
				return;
			queue.TakeNote();
			if ( sounded.Add( text ) )
				GameAudio.PlayUi( GameAudio.ErrorEvent );
			if ( note.Kind == ManagementNoteKind.Rights )
				lastSessionRefusalNotice = Time.Now;
		}
	}

	/// <summary>What a management note says: the rights' refusal, what the host's own windows say (no longer there, too little money), or the request's own fate.</summary>
	private string ManagementNoteText( ManagementNote note ) => note.Kind switch
	{
		ManagementNoteKind.Rights => strings.Extra( RefusalText( note.Refusal ) ),
		ManagementNoteKind.Waiting => strings.Extra( OpenTpwText.CoopWaitingForHost ),
		ManagementNoteKind.InsufficientFunds => strings[UIStrings.InsufficientFunds],
		ManagementNoteKind.NotAvailable => strings.Extra( OpenTpwText.NotAvailable ),
		ManagementNoteKind.NotTaken => strings.Extra( OpenTpwText.CoopRequestNotTaken ),
		_ => strings.Extra( OpenTpwText.CoopRequestUnknown )
	};

}
