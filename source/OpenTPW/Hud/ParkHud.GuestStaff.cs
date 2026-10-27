using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// [EXT:online-coop-commands] A guest's staff in a shared park (C4c-2, docs/COOP.md "A guest's staff"): the hire, the pick-up, the Place and Carry tools' clicks and the cleanup of what the
/// player let go of go to the host as requests of the session's staff controller (<see cref="CoopGuestStaff"/>), one at a time, and the staff tool follows the guest seat's state in the park
/// every frame; the dismissal's confirmation waits for the host's answer as a loan's does. A host's and a single player's staff act at once, as they always did.
/// </summary>
public sealed partial class ParkHud
{
	/// <summary>The guest's staff controller while this park follows a session's host; null for a host and a single player, whose staff tools act at once.</summary>
	private CoopGuestStaff? GuestStaff => level.Commands.SendsRequests && level.CoopSession?.GuestStaff is { } staff ? staff : null;

	/// <summary>Whether a guest's hire or pick-up the player still wants waits for the host: Escape and the park view's right click let go of it also with no staff tool on yet.</summary>
	private bool GuestStaffStartWaits => GuestStaff is { StartWaits: true };

	/// <summary>The staff request and tool the player saw before this frame takes outcomes: its press cannot place or drop a tool the answer arms or replaces.</summary>
	private bool frameStaffRequestWaited, frameStaffToolWasActive;

	/// <summary>A first staff start whose tool was not on before this frame's outcomes: Escape still lets it go before closing an object window.</summary>
	private bool frameStaffStartWithoutTool;

	/// <summary>Keeps the staff input's pre-outcome ownership, including an answer received by the park whose outcome the HUD has not taken yet.</summary>
	private void RememberGuestStaffInput()
	{
		frameStaffRequestWaited = GuestStaff?.HasOpenRequest == true;
		frameStaffToolWasActive = level.StaffPlacement is { IsActive: true };
		frameStaffStartWithoutTool = GuestStaffStartWaits && level.StaffPlacement is { IsActive: false };
	}

	/// <summary>Whether the last hire click said a note of its own (the rights', the wait's): the hire list's refusal then says nothing more.</summary>
	private bool hireNoteSaid;

	/// <summary>
	/// Whether the session's rights refuse a guest's staff action <paramref name="command"/>, asked before the action's own checks (the loans' order): the refusal's note joins the session's
	/// notes, said once the HUD's lines can be seen, with the error sound now.
	/// </summary>
	private bool RefusedByRights( ParkCommand command )
	{
		if ( level.Commands.RightsRefusalOf( command ) is not { } refusal )
			return false;
		if ( GuestManagement is { } queue )
		{
			queue.NoteRefused( new ManagementItem( new ManagementTarget( ManagementTargetKind.Staff ), null, command ), refusal );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
		}
		return true;
	}

	/// <summary>
	/// A guest's hire (C4c-2): the session's rights first, then one staff request at a time, then the host's own checks (bankruptcy, the role's room, the candidate still in the pool and placed
	/// by no other seat), then the start goes (<see cref="StartGuestStaff"/>) and the staff window closes. True when the start went.
	/// </summary>
	private bool GuestHire( CoopGuestStaff staff, ParkEconomy economy, StaffCandidate candidate )
	{
		var start = new BeginStaffHireCommand( candidate.Id );
		if ( RefusedByRights( start ) || StaffRequestWaits( staff ) )
		{
			hireNoteSaid = true;
			return false;
		}
		// a level without the Place tool (no guest world) hires nobody, as the host's
		if ( economy.IsBankrupt || !economy.Staff.CanHire( candidate.Type ) || economy.Staff.Candidates.All( item => item.Id != candidate.Id )
			|| economy.Staff.ReservedBy( candidate.Id ) is { } seat && seat != level.Commands.Player || level.StaffPlacement == null )
			return false;
		if ( !StartGuestStaff( staff, start ) )
			return false;
		CloseStaffWindow();
		return true;
	}

	/// <summary>
	/// A guest's pick-up from the employee window (C4c-2): the session's rights first, then one staff request at a time, then the host's own check (the employee may be picked up), then the start
	/// goes (<see cref="StartGuestStaff"/>) and the window closes. True when the start went.
	/// </summary>
	private bool GuestPickUp( CoopGuestStaff staff, StaffMember member )
	{
		var start = new PickUpStaffCommand( member.Id );
		if ( RefusedByRights( start ) || StaffRequestWaits( staff ) || level.Guests?.Grid.Cells is not { } cells || !StaffPlacementRules.CanBePickedUp( member, cells )
			|| !StartGuestStaff( staff, start ) )
			return false;
		CloseEmployeeWindow();
		return true;
	}

	/// <summary>Whether a staff request of the guest's waits, so a staff click is no click: the waiting note, said once for a run.</summary>
	private bool StaffRequestWaits( CoopGuestStaff staff )
	{
		if ( !staff.HasOpenRequest )
			return false;
		level.NoteWaitingClick();
		return true;
	}

	/// <summary>
	/// A guest's hire or pick-up going to the host (C4c-2), its rights and checks passed with no staff request waiting: every non-staff tool and its waits end first (a Move tool's end goes
	/// before the start), a staff tool already on stays on, inert, until the start's outcome; then the start goes and the player wants the tool. True when it went.
	/// </summary>
	private bool StartGuestStaff( CoopGuestStaff staff, ParkCommand start )
	{
		level.EndBuildTools();
		if ( level.Commands.Issue( start ).Request is not { } request )
			return false;
		staff.Adopt( request, GuestStaffRequestKind.Start );
		staff.WantsTool = true;
		return true;
	}

	/// <summary>
	/// A guest's click with its staff tool on the park cell (<paramref name="x"/>, <paramref name="y"/>) (C4c-2): while a staff request waits it is no click (the waiting note, once for a run);
	/// a cell that takes no one does nothing, as the host's click; a Place tool whose candidate can no longer be hired lets go, as the host's click cancels it (<see cref="StaffPlacement.Click"/>);
	/// otherwise the placement or the drop goes to the host, and the tool stays on, inert, until its outcome.
	/// </summary>
	private void ClickGuestStaff( CoopGuestStaff staff, StaffPlacement tool, int x, int y )
	{
		if ( StaffRequestWaits( staff ) || !tool.IsValid( x, y ) || level.Park?.Economy is not { } economy )
			return;
		ParkCommand click;
		if ( tool is { Kind: StaffToolKind.Place, Candidate: { } candidate } )
		{
			if ( economy.IsBankrupt || economy.Staff.Candidates.All( item => item.Id != candidate.Id ) || !economy.Staff.CanHire( candidate.Type ) )
			{
				tool.Cancel();
				return;
			}
			click = new PlaceStaffCommand( candidate.Id, x, y );
		}
		else if ( tool is { Kind: StaffToolKind.Carry, Carried: { } member } )
			click = new DropStaffCommand( member.Id, x, y );
		else
			return;
		if ( level.Commands.Issue( click ).Request is { } request )
			staff.Adopt( request, GuestStaffRequestKind.Click );
	}

	/// <summary>
	/// A guest's staff tool follows its seat's staff state (C4c-2), each frame after the outcomes and outside the simulation tick (a paused park runs it too), whenever no staff request waits:
	/// with a pending hire or an employee in hand the player still wants and no other tool on, the matching tool is on (armed for that candidate or employee if it is not); otherwise that state
	/// ends by its cleanup request (the hire's cancel, or the drop back to where the employee was picked up), one at a time, at most one a second; with neither, a staff tool goes off and the
	/// wish with it. So an accepted start arms its tool, a refused start gives the old tool back while its state remains, an accepted placement or drop ends the tool, and a right taken back,
	/// a departure's release or a dismissal turns it off; across a resync the new park's state is read the same way.
	/// </summary>
	private void FollowGuestStaff( CoopGuestStaff staff )
	{
		if ( staff.TakeOutcome() is { } outcome )
			CompleteGuestStaff( staff, outcome );
		if ( staff.HasOpenRequest )
			return;
		var tool = level.StaffPlacement;
		var seat = level.Commands.Player;
		var economy = level.Park?.Economy;
		var pending = economy?.Staff.PendingHireOf( seat ) ?? 0;
		var hand = level.StaffHands?.Of( seat );
		if ( pending <= 0 && hand == null )
		{
			tool?.Disarm();
			staff.WantsTool = false;
			return;
		}
		if ( staff.WantsTool && tool != null && economy != null && (tool.IsActive || level.NoToolActive) && ArmGuestStaff( tool, economy, pending, hand ) )
			return;
		// not wanted, another tool on, or nothing the tool could be armed for: the state ends
		tool?.Disarm();
		if ( !staff.MayClean( Time.Now ) )
			return;
		ParkCommand cleanup = hand is { } held ? new DropStaffCommand( held.Staff, held.X, held.Y ) : new CancelStaffHireCommand();
		// a cleanup the rights refuse is the host's to end: a right taken back releases the seat's state in its own record
		if ( !level.Commands.MayIssue( cleanup ) )
			return;
		staff.Cleaned( Time.Now );
		if ( level.Commands.Issue( cleanup ).Request is { } request )
			staff.Adopt( request, GuestStaffRequestKind.Cleanup );
	}

	/// <summary>Arms <paramref name="tool"/> for the seat's state: the Carry tool for the employee in hand, else the Place tool for the pending hire's candidate. False when the park holds neither.</summary>
	private static bool ArmGuestStaff( StaffPlacement tool, ParkEconomy economy, int pending, (int Staff, int X, int Y)? hand )
	{
		if ( hand is { } held )
		{
			if ( tool is { Kind: StaffToolKind.Carry, Carried: { } carried } && carried.Id == held.Staff )
				return true;
			if ( economy.Staff.Members.FirstOrDefault( member => member.Id == held.Staff ) is not { } member )
				return false;
			tool.ArmCarry( member, (held.X, held.Y) );
			return true;
		}
		if ( tool is { Kind: StaffToolKind.Place, Candidate: { } placing } && placing.Id == pending )
			return true;
		if ( economy.Staff.Candidates.FirstOrDefault( item => item.Id == pending ) is not { } candidate )
			return false;
		tool.ArmPlace( candidate );
		return true;
	}

	/// <summary>
	/// A staff request's outcome, before the tool follows the seat's state: a refused placement is told apart by what it leaves. A bad cell keeps the tool (the pending hire stands and can still
	/// be hired); a candidate gone or no longer hireable (another seat placed the role's last employee, bankruptcy, a full thing pool) lets go, so the rule ends the tool and sends one cancel, as
	/// the host's click cancels it. A refused drop keeps the Carry tool; every other outcome is the seat's state.
	/// </summary>
	private void CompleteGuestStaff( CoopGuestStaff staff, GuestStaffOutcome outcome )
	{
		if ( outcome is { Kind: GuestStaffRequestKind.Click, Request: { State: CoopGuestRequestState.Applied, Result.Accepted: false, Command: PlaceStaffCommand place } }
			&& !GuestHireStands( place.Candidate ) )
			staff.LetGo();
	}

	/// <summary>Whether the seat's pending hire is still <paramref name="candidate"/> and the park could hire it now (the placement's own checks but the cell's).</summary>
	private bool GuestHireStands( int candidate ) => level.Park?.Economy is { IsBankrupt: false } economy && economy.Staff.PendingHireOf( level.Commands.Player ) == candidate
		&& economy.Staff.Candidates.FirstOrDefault( item => item.Id == candidate ) is { } pending && economy.Staff.CanHire( pending.Type ) && economy.Staff.Things is not { NextId: 0 };

	/// <summary>A guest's dismissal confirmation that waits for the host's answer after its Yes, and the waiter of its fire (C4c-2).</summary>
	private (UiScreen Screen, ManagementWaiter Waiter)? dismissalConfirmation;

	/// <summary>
	/// A guest's dismissal with the Confirmations option on (C4c-2): the employee window closed first, as the host's; the yes/no box asks, and after its Yes the session's rights are asked again
	/// (a right taken back since says its note and closes it), then the host's own check (the employee still employed), then the fire is queued and the box waits with its question and a waiting
	/// line, its buttons inert, until the outcome comes (<see cref="CompleteDismissalConfirmation"/>). Escape closes it, and its outcome is then a note.
	/// </summary>
	private void ConfirmGuestDismissal( int staffId )
	{
		var waiting = false;
		UiScreen? confirmation = null;
		confirmation = UiDialogs.Message( "confirmDismissStaff",
			() => waiting ? $"{strings[UIStrings.DismissEmployee]} {strings.Extra( OpenTpwText.CoopWaitingForHost )}" : strings[UIStrings.DismissEmployee],
			(() => strings.Value( UIStrings.Yes ), () =>
			{
				if ( waiting )
					return;
				// a session that ended meanwhile dismisses nobody: the box closes
				if ( GuestManagement is not { } live )
					Stack.Pop();
				else
					waiting = QueueGuestDismissal( live, confirmation!, staffId );
			}),
			(() => strings.Value( UIStrings.No ), () =>
			{
				if ( !waiting )
					Stack.Pop();
			}) );
		confirmation.Back = () => Stack.Pop();
		confirmation.Removed = () =>
		{
			if ( dismissalConfirmation is { } held && ReferenceEquals( held.Screen, confirmation ) )
			{
				dismissalConfirmation = null;
				held.Waiter.Queue.Detach( held.Waiter );
			}
		};
		Stack.Push( confirmation );
	}

	/// <summary>The guest's Yes: the rights again, the host's own check, then the fire queued with the box's waiter. True when the box waits.</summary>
	private bool QueueGuestDismissal( CoopManagementRequests queue, UiScreen confirmation, int staffId )
	{
		var fire = new FireStaffCommand( staffId );
		var item = new ManagementItem( ManagementTarget.Staff( staffId ), null, fire );
		if ( level.Commands.RightsRefusalOf( fire ) is { } refusal )
		{
			queue.NoteRefused( item, refusal );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
			Stack.Pop();
			return false;
		}
		// the employee left meanwhile: nothing to dismiss, as the host's dismissal of a member gone does nothing
		if ( level.IsReadOnlyVisit || StaffMembers.All( member => member.Id != staffId ) )
		{
			Stack.Pop();
			return false;
		}
		var waiter = queue.NewWaiter();
		var enqueued = queue.Enqueue( new[] { item }, waiter );
		if ( !enqueued.Queued )
		{
			queue.Detach( waiter );
			if ( enqueued.Full )
			{
				queue.NoteRefused( item, null );
				GameAudio.PlayUi( GameAudio.ErrorEvent );
			}
			Stack.Pop();
			return false;
		}
		dismissalConfirmation = (confirmation, waiter);
		return true;
	}

	/// <summary>
	/// A guest's fire came back (C4c-2): the box goes first, wherever it stands. Accepted, the employee has left the roster (its window closed before the box); the session's end says nothing
	/// more; a refusal (being moved, gone, the rights at the host) is said as a note.
	/// </summary>
	private void CompleteDismissalConfirmation( UiScreen confirmation, ManagementWaiter waiter, ManagementOutcome outcome )
	{
		dismissalConfirmation = null;
		waiter.Queue.Detach( waiter );
		Stack.RemoveWhere( screen => ReferenceEquals( screen, confirmation ) );
		NoteManagement( outcome );
	}

	/// <summary>The patrol tool's area on a guest (<see cref="Level.GuestPatrolArea"/>): into the management queue after the session's rights, drawn as asked until it comes back.</summary>
	private bool QueueGuestPatrolArea( SetPatrolAreaCommand patrol ) => GuestManagement is { } queue
		? QueueManagement( queue, new ManagementItem( ManagementTarget.Staff( patrol.Staff ), ManagementKnob.Patrol, patrol ) )
		: level.Commands.Issue( patrol ).Accepted;
}
