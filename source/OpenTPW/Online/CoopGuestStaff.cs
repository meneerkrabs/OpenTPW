namespace OpenTPW;

/// <summary>[EXT:online-coop-commands] What a guest's staff request is to its tool (C4c-2, <see cref="CoopGuestStaff"/>).</summary>
internal enum GuestStaffRequestKind : byte
{
	/// <summary>A hire (<see cref="BeginStaffHireCommand"/>) or a pick-up (<see cref="PickUpStaffCommand"/>): the player's click, which wants the tool.</summary>
	Start = 1,
	/// <summary>A Place tool's placement (<see cref="PlaceStaffCommand"/>) or a Carry tool's drop (<see cref="DropStaffCommand"/>) on the clicked cell.</summary>
	Click = 2,
	/// <summary>The end of a seat's state the player let go of: the hire's cancel (<see cref="CancelStaffHireCommand"/>) or the drop back to where the employee was picked up.</summary>
	Cleanup = 3
}

/// <summary>[EXT:online-coop-commands] A guest's staff request that resolved, with what it was to its tool (C4c-2).</summary>
internal readonly record struct GuestStaffOutcome( CoopGuestCommands.Request Request, GuestStaffRequestKind Kind );

/// <summary>
/// [EXT:online-coop-commands] A guest's staff tool as requests to the host (C4c-2, docs/COOP.md "A guest's staff"), owned by the session as its management queue is, so a resync's new HUD finds
/// it. It holds at most one open staff request of the guest's (a hire, a pick-up, a placement, a drop or a cleanup) and whether the player still wants a staff tool (<see cref="WantsTool"/>: set
/// by a hire's or a pick-up's click, cleared by letting go); the HUD's tool follows the seat's state in the park from them every frame. Its wait is its own, not the Move tool's
/// (<see cref="Level.WaitingToolStart"/>, which any tool on clears), so a staff tool that stays on while a request waits does not drop it. Its notes join the session's notes
/// (<see cref="CoopManagementRequests.NoteOutcome"/>).
/// </summary>
internal sealed class CoopGuestStaff
{
	/// <summary>The least time between two cleanup requests, in seconds: a seat's state the player let go of ends one request at a time, at most one a second.</summary>
	public const double CleanupIntervalSeconds = 1;

	private readonly CoopManagementRequests notes;
	private CoopGuestCommands.Request? open;
	private GuestStaffRequestKind openKind;
	private double? lastCleanup;

	/// <param name="notes">The session's management queue, whose notes the staff outcomes join (said once the HUD's lines can be seen).</param>
	public CoopGuestStaff( CoopManagementRequests notes ) => this.notes = notes ?? throw new ArgumentNullException( nameof( notes ) );

	/// <summary>Whether the player still wants a staff tool: a hire's or a pick-up's click sets it; letting go (Escape, a right click, another tool, a window that ends it) clears it.</summary>
	public bool WantsTool { get; set; }

	/// <summary>Whether a staff request of the guest's waits for its outcome to be taken: while it does, every staff click is no click, and the tool waits for it.</summary>
	public bool HasOpenRequest => open != null;

	/// <summary>Whether a hire or a pick-up the player still wants waits for the host: Escape and the park view's right click let go of it also while no staff tool is on yet.</summary>
	public bool StartWaits => WantsTool && open is { IsOpen: true } && openKind == GuestStaffRequestKind.Start;

	/// <summary>
	/// Whether something of the player's staff may not have been made and was not said (the session's end line, as <see cref="CoopManagementRequests.HasUnfinished"/>): a request not resolved yet,
	/// or one resolved as no success whose outcome the HUD has not taken yet.
	/// </summary>
	public bool HasUnfinished => open is { } request && (request.IsOpen || request.State != CoopGuestRequestState.InSnapshot && !request.Accepted);

	/// <summary>
	/// Takes <paramref name="request"/>, the guest's staff command the park's funnel sent, as the open request: the HUD's completion of the tools' requests skips it (<see
	/// cref="CoopGuestCommands.Request.Owner"/>). One staff request at a time: another open one is a caller's error.
	/// </summary>
	public void Adopt( CoopGuestCommands.Request request, GuestStaffRequestKind kind )
	{
		ArgumentNullException.ThrowIfNull( request );
		if ( open != null )
			throw new InvalidOperationException( "A staff request is open already." );
		request.Owner = this;
		open = request;
		openKind = kind;
	}

	/// <summary>
	/// The open request's outcome once it resolved, taken once (the HUD's, each frame before its tool follows the seat's state). A start's or a click's note joins the session's notes: a park's
	/// refusal the not-available note, the rights' their note, one the host did not take or whose fate is not known theirs; an accepted one, one inside a resync's cut and the session's end say
	/// nothing (the park and the tool show them). A cleanup says nothing: the player let go.
	/// </summary>
	public GuestStaffOutcome? TakeOutcome()
	{
		if ( open is not { IsOpen: false } request )
			return null;
		open = null;
		if ( openKind != GuestStaffRequestKind.Cleanup )
		{
			var kind = request.State switch
			{
				CoopGuestRequestState.Applied when request.Result is { Error: null } result => result.Accepted ? ManagementOutcomeKind.Accepted : ManagementOutcomeKind.Refused,
				CoopGuestRequestState.Refused => ManagementOutcomeKind.NotTaken,
				CoopGuestRequestState.InSnapshot => ManagementOutcomeKind.Accepted,
				CoopGuestRequestState.Ended => ManagementOutcomeKind.Ended,
				_ => ManagementOutcomeKind.Unknown
			};
			notes.NoteOutcome( request.Command, kind, request.Result );
		}
		return new GuestStaffOutcome( request, openKind );
	}

	/// <summary>The player let go of the staff tool: no tool comes back, and the seat's state is cleaned up after any open request.</summary>
	public void LetGo() => WantsTool = false;

	/// <summary>Whether a cleanup request may go at <paramref name="now"/> (the HUD's clock in seconds): not within a second of the last, across the session's resyncs.</summary>
	public bool MayClean( double now ) => lastCleanup is not { } last || now - last >= CleanupIntervalSeconds;

	/// <summary>A cleanup request went at <paramref name="now"/>.</summary>
	public void Cleaned( double now ) => lastCleanup = now;
}
