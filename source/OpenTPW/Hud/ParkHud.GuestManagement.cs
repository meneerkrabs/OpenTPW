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

	/// <summary>The notes of the guest's management outcomes the HUD says once its messages can be seen, oldest first (the newest <see cref="CoopManagementRequests.MaximumNotes"/>).</summary>
	private readonly Queue<ManagementNote> managementNotes = new();

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
	private bool QueueManagement( CoopManagementRequests queue, ManagementItem item, ManagementWaiter? waiter = null )
	{
		if ( level.Commands.RefusesHere( item.Command ) )
			return false;
		var enqueued = queue.Enqueue( new[] { item }, waiter );
		if ( enqueued.Full )
			level.NoteWaitingClick();
		return enqueued.Queued;
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
	/// Once a frame, before the windows' own updates: the door's outcomes (the advisor's event of an accepted one, whichever HUD queued it), the loans window's refreshes (each refills the list
	/// of the tab shown), the waiting loan confirmation's outcome, then the notes nobody waits for; every note is said once its messages can be seen.
	/// </summary>
	private void CompleteGuestManagement()
	{
		if ( level.CoopSession?.Management is { } queue )
		{
			foreach ( var outcome in queue.TakeOutcomes( queue.HudWaiter ) )
			{
				if ( outcome is { Accepted: true, Command: SetParkOpenCommand door } )
					// [BIN:TPW-EXE:0x00519EF0 park door routine] the advisor's game event 3 (open) or 4 (close), as the host's press posts it; a guest's only for its accepted request
					host?.ParkDoorPressed?.Invoke( door.Open );
				else
					NoteManagement( outcome.Note );
			}
			foreach ( var note in queue.TakeNotes() )
				NoteManagement( note.Note );
		}
		if ( loansWaiter is { } opening )
			foreach ( var outcome in opening.Queue.TakeOutcomes( opening ) )
				CompleteLoanRefresh( outcome );
		if ( loanConfirmation is { } waiting )
			foreach ( var outcome in waiting.Waiter.Queue.TakeOutcomes( waiting.Waiter ) )
				CompleteLoanConfirmation( waiting.Screen, outcome );
		SayManagementNotes();
	}

	/// <summary>Keeps <paramref name="note"/> to be said once the HUD's messages can be seen (nothing for none).</summary>
	private void NoteManagement( ManagementNote note )
	{
		if ( note.Kind == ManagementNoteKind.None )
			return;
		managementNotes.Enqueue( note );
		while ( managementNotes.Count > CoopManagementRequests.MaximumNotes )
			managementNotes.Dequeue();
	}

	/// <summary>
	/// Whether the HUD's feedback lines can be seen (<see cref="DrawFeedback"/>): no screen hides the park (the ride view, the research finale), the object window's info arm is in and no
	/// message panel is open.
	/// </summary>
	private bool FeedbackShows => !InfoArmOpen && OpenedMessage == null && !Stack.Screens.Any( screen => screen.HidesBelow );

	/// <summary>
	/// Says the kept notes, oldest first, one at a time while the feedback lines can be seen and have room (they keep <see cref="MaximumFeedbackLines"/>), so none expires or is pushed out
	/// unseen; a note shown already is not said again.
	/// </summary>
	private void SayManagementNotes()
	{
		while ( managementNotes.Count > 0 && FeedbackShows && messages.Count < MaximumFeedbackLines )
		{
			var text = ManagementNoteText( managementNotes.Dequeue() );
			if ( messages.All( message => message.Text != text ) )
				SayGuestNote( text );
		}
	}

	/// <summary>What a management note says: the rights' refusal, what the host's own windows say (no longer there, too little money), or the request's own fate.</summary>
	private string ManagementNoteText( ManagementNote note ) => note.Kind switch
	{
		ManagementNoteKind.Rights => strings.Extra( RefusalText( note.Refusal ) ),
		ManagementNoteKind.InsufficientFunds => strings[UIStrings.InsufficientFunds],
		ManagementNoteKind.NotAvailable => strings.Extra( OpenTpwText.NotAvailable ),
		ManagementNoteKind.NotTaken => strings.Extra( OpenTpwText.CoopRequestNotTaken ),
		_ => strings.Extra( OpenTpwText.CoopRequestUnknown )
	};

	/// <summary>A note about this player's own request, with the error sound.</summary>
	private void SayGuestNote( string text )
	{
		PostMessage( text );
		GameAudio.PlayUi( GameAudio.ErrorEvent );
	}
}
