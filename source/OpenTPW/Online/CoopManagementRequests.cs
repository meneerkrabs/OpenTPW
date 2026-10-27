namespace OpenTPW;

/// <summary>[EXT:online-coop-commands] Which part of the park a guest's management entry changes (C4b): a target's entries go to the host one at a time, in their order.</summary>
internal enum ManagementTargetKind : byte
{
	/// <summary>The research lab: its efforts, its workload and its projects' order.</summary>
	Research = 1,
	/// <summary>The park's door, its entrance fee and its name.</summary>
	Finance = 2,
	/// <summary>The loans: the offers' refresh, a take and a repay, which so go in their order.</summary>
	Loans = 3,
	/// <summary>A test's own targets (the cap's test needs more targets than the windows have); no window makes one.</summary>
	Synthetic = 255
}

/// <summary>[EXT:online-coop-commands] One target of a guest's management queue (<see cref="CoopManagementRequests"/>): its kind and, for a kind of many, which one.</summary>
internal readonly record struct ManagementTarget( ManagementTargetKind Kind, int Id = 0 )
{
	public static ManagementTarget Research => new( ManagementTargetKind.Research );
	public static ManagementTarget Finance => new( ManagementTargetKind.Finance );
	public static ManagementTarget Loans => new( ManagementTargetKind.Loans );
}

/// <summary>[EXT:online-coop-commands] A control whose latest value is what its player wants (C4b): a run of its changes goes once, with the latest value.</summary>
internal enum ManagementKnobKind : byte
{
	/// <summary>A research category's effort (<see cref="ManagementKnob.Index"/> is the category).</summary>
	Effort = 1,
	Workload = 2,
	Fee = 3,
	Door = 4,
	Name = 5,
	/// <summary>The loan offers' refresh.</summary>
	Refresh = 6
}

/// <summary>[EXT:online-coop-commands] A control within a target (<see cref="ManagementKnobKind"/>), with its index where a target has several of a kind.</summary>
internal readonly record struct ManagementKnob( ManagementKnobKind Kind, int Index = 0 )
{
	public static ManagementKnob Effort( ResearchCategory category ) => new( ManagementKnobKind.Effort, (int)category );
	public static ManagementKnob Workload => new( ManagementKnobKind.Workload );
	public static ManagementKnob Fee => new( ManagementKnobKind.Fee );
	public static ManagementKnob Door => new( ManagementKnobKind.Door );
	public static ManagementKnob Name => new( ManagementKnobKind.Name );
	public static ManagementKnob Refresh => new( ManagementKnobKind.Refresh );
}

/// <summary>[EXT:online-coop-commands] One command of a window's action, for <see cref="CoopManagementRequests.Enqueue"/>: its target and its knob, if it has one.</summary>
internal readonly record struct ManagementItem( ManagementTarget Target, ManagementKnob? Knob, ParkCommand Command );

/// <summary>[EXT:online-coop-commands] What <see cref="CoopManagementRequests.Enqueue"/> did with a window's action: its entries (none when nothing was queued) and whether its targets were full.</summary>
internal readonly record struct ManagementEnqueued( IReadOnlyList<ManagementEntry> Entries, bool Full )
{
	public bool Queued => Entries.Count > 0;
}

/// <summary>[EXT:online-coop-commands] Where a guest's management entry stands.</summary>
internal enum ManagementEntryState : byte
{
	/// <summary>Queued: its target's earlier entries, the cap or the pacing hold it back.</summary>
	Unsent = 1,
	/// <summary>Sent to the host as a request of the session's ledger; its outcome has not come.</summary>
	Open = 2,
	/// <summary>Its outcome came (<see cref="ManagementOutcome"/>).</summary>
	Resolved = 3
}

/// <summary>[EXT:online-coop-commands] One command of a guest's management queue: its target, its knob, its waiter, and its request once sent.</summary>
internal sealed class ManagementEntry
{
	internal ManagementEntry( CoopManagementRequests queue, long order, ManagementTarget target, ManagementKnob? knob, ParkCommand command, ManagementWaiter? waiter )
	{
		Queue = queue;
		Order = order;
		Target = target;
		Knob = knob;
		Command = command;
		Waiter = waiter;
	}

	internal CoopManagementRequests Queue { get; }
	/// <summary>Its place among the queue's entries (the cap sends the oldest first); a later value of its knob keeps it.</summary>
	public long Order { get; }
	public ManagementTarget Target { get; }
	public ManagementKnob? Knob { get; }
	/// <summary>The command, every value fixed when it was made; a later value of its knob replaces it while it is unsent.</summary>
	public ParkCommand Command { get; internal set; }
	public ManagementWaiter? Waiter { get; }
	public ManagementEntryState State { get; internal set; } = ManagementEntryState.Unsent;
	/// <summary>Its request to the host, once sent.</summary>
	public CoopGuestCommands.Request? Request { get; internal set; }
}

/// <summary>[EXT:online-coop-commands] What became of a guest's management entry (<see cref="ManagementOutcome"/>).</summary>
internal enum ManagementOutcomeKind : byte
{
	/// <summary>Its record applied here, and its command did what it asked.</summary>
	Accepted = 1,
	/// <summary>
	/// Refused, by the park when its record applied (<see cref="ManagementOutcome.Result"/> says why) or by the session's rights (the result's <see cref="ParkCommandResult.RightsRefused"/>),
	/// at its record or before it was sent.
	/// </summary>
	Refused = 2,
	/// <summary>The host took no record of it (too fast for the host's bucket, or another refusal without a record): as if never clicked.</summary>
	NotTaken = 3,
	/// <summary>Whether it applied is not known (its record is inside the cut of the snapshot a resync rebuilt the park from, the host forgot it, or it threw): the park shows it.</summary>
	Unknown = 4,
	/// <summary>The session ended before it resolved.</summary>
	Ended = 5
}

/// <summary>[EXT:online-coop-commands] What a management outcome says to its player (<see cref="ManagementOutcome.Note"/>).</summary>
internal enum ManagementNoteKind : byte
{
	None = 0,
	/// <summary>The session's rights refused it (<see cref="ManagementNote.Refusal"/> says whose words).</summary>
	Rights = 1,
	/// <summary>What it asked for is no longer there (a gone offer, a changed loan, a moved project), as the host's window says it.</summary>
	NotAvailable = 2,
	/// <summary>The park could not afford it (a repay), as the host's window says it.</summary>
	InsufficientFunds = 3,
	/// <summary>The host could not take it; the player tries again.</summary>
	NotTaken = 4,
	/// <summary>Whether it went through is not known.</summary>
	Unknown = 5
}

/// <summary>[EXT:online-coop-commands] A management outcome's note: its kind, and for a refusal by the rights, which (another player's claim, the right not given, the host's alone).</summary>
internal readonly record struct ManagementNote( ManagementNoteKind Kind, CoopRefusal Refusal = CoopRefusal.HostOnly )
{
	public static ManagementNote None => default;
}

/// <summary>[EXT:online-coop-commands] One resolved management entry: what became of it, and its command's result where its record applied (or the rights' refusal before it was sent).</summary>
internal sealed record ManagementOutcome( ManagementEntry Entry, ManagementOutcomeKind Kind, ParkCommandResult? Result = null )
{
	public ParkCommand Command => Entry.Command;

	public bool Accepted => Kind == ManagementOutcomeKind.Accepted;

	/// <summary>
	/// What it says when nothing else does (C4b, docs/COOP.md "A guest's management windows"): nothing when accepted (the park shows it) or for the session's end; a refusal by the rights their
	/// note; a park's refusal what the host's window says (a loan's take or repay, a stale project move or upgrade) and nothing where the host's window says nothing (the fee, the door, the efforts,
	/// the workload, the name, the refresh: the control's return is the feedback); a request the host could not take that it could not; an outcome not known that it is not known.
	/// </summary>
	public ManagementNote Note => Kind switch
	{
		ManagementOutcomeKind.NotTaken => new ManagementNote( ManagementNoteKind.NotTaken ),
		ManagementOutcomeKind.Unknown => new ManagementNote( ManagementNoteKind.Unknown ),
		ManagementOutcomeKind.Refused when Result is { RightsRefused: true } refused => new ManagementNote( ManagementNoteKind.Rights, CoopBuildRights.RefusalOf( refused.Reason ) ),
		ManagementOutcomeKind.Refused => ParkRefusalNote( Command, Result ),
		_ => ManagementNote.None
	};

	/// <summary>What a park's refusal of <paramref name="command"/> says: the loans' as their confirmation says it, a stale action that it is no longer there, anything else nothing.</summary>
	private static ManagementNote ParkRefusalNote( ParkCommand command, ParkCommandResult? result ) => command switch
	{
		TakeLoanCommand or RepayLoanCommand => new ManagementNote( result?.Value is LoanRefusal.Unaffordable ? ManagementNoteKind.InsufficientFunds : ManagementNoteKind.NotAvailable ),
		IStaleChecked when result?.Reason == ParkSettingCommand.StaleReason => new ManagementNote( ManagementNoteKind.NotAvailable ),
		_ => ManagementNote.None
	};
}

/// <summary>
/// [EXT:online-coop-commands] A window's opening (or the HUD) that acts on its entries' outcomes (C4b): each outcome, accepted included, waits here in the order the entries resolved until the HUD
/// takes them (<see cref="CoopManagementRequests.TakeOutcomes"/>) or the opening's screen is removed (<see cref="CoopManagementRequests.Detach"/>). A window whose screen is reused makes a new one
/// at each opening, so a closed opening's entries never reach a reopened one.
/// </summary>
internal sealed class ManagementWaiter
{
	internal ManagementWaiter( CoopManagementRequests queue ) => Queue = queue;

	/// <summary>The queue whose entries it waits for: a window takes its outcomes there also once the session ended (each is then an end).</summary>
	public CoopManagementRequests Queue { get; }

	internal Queue<ManagementOutcome> Outcomes { get; } = new();

	/// <summary>Its opening's screen was removed: its entries' outcomes are notes now.</summary>
	public bool Detached { get; internal set; }
}

/// <summary>
/// [EXT:online-coop-commands] What a guest's management queue sends its commands through (the guest's park's <see cref="ParkCommands"/>; a test's own): the session's rights asked silently, the
/// rights' refusal said through the session's refusal path, and the issue that makes a request of the session's ledger.
/// </summary>
internal interface IManagementIssuer
{
	/// <summary>The rights' refusal of <paramref name="command"/> of this machine's player, asked silently; null when they allow it.</summary>
	ParkCommandResult? RightsRefusalOf( ParkCommand command );

	/// <summary>Says the rights' refusal of <paramref name="command"/> through the session's refusal path (coalesced for a run); false when the rights allow it.</summary>
	bool RefusesHere( ParkCommand command );

	/// <summary>Issues <paramref name="command"/> as this machine's player: a guest's goes to the host as a request (<see cref="ParkCommandResult.Request"/>).</summary>
	ParkCommandResult Issue( ParkCommand command );
}

/// <summary>
/// [EXT:online-coop-commands] A guest's management requests (C4b, docs/COOP.md "A guest's management windows"): the research, finance and loans windows' commands on their way to the host. The
/// session's, created with its ledger (<see cref="CoopGuestCommands"/>) and kept across resyncs. A window's action enqueues its commands whole and in order (<see cref="Enqueue"/>); each target's
/// entries go one at a time, in their order, at most <see cref="MaximumOpen"/> requests open across the targets (the oldest entry first) and at most one sent every <see cref="SendSeconds"/>
/// (<see cref="Pump"/>, once a frame from the session's step). A knob's later value takes the place of its unsent entry at the target's tail, so a drag or a run of clicks goes once with its latest
/// value. The queue follows its requests through the ledger's <see cref="CoopGuestCommands.Resolved"/>, without a HUD and without touching the park, and keeps their outcomes: a waiter's in order
/// until it takes them, anything else's note among the newest <see cref="MaximumNotes"/> notes for the HUD to say. A request the host could not take (too fast) is an outcome, never sent again
/// by the queue: the ledger's own resend is the only one.
/// </summary>
internal sealed class CoopManagementRequests
{
	/// <summary>How many of the guest's management requests are open at most, across every target.</summary>
	public const int MaximumOpen = 4;

	/// <summary>The least time between two management requests sent: 5 a second, well inside the host's refill of 20 a second, which the guest's tools share.</summary>
	public const double SendSeconds = 0.2;

	/// <summary>How many notes wait at most for <see cref="TakeNotes"/>: a guest without a HUD keeps the newest only.</summary>
	public const int MaximumNotes = 64;

	/// <summary>
	/// How many unsent entries a target holds at most, besides its open one: an action that would not fit is no click. A knob's run supersedes and never fills a target; only alternating actions
	/// faster than the host answers do. So the <see cref="HudWaiter"/> (whose door entries are all the finance target's) never holds more than this and one open entry's outcomes.
	/// </summary>
	public const int MaximumUnsent = 16;

	/// <summary>Each target's entries not resolved yet, in their order: at most the first is open.</summary>
	private readonly Dictionary<ManagementTarget, List<ManagementEntry>> targets = new();
	private readonly Queue<ManagementOutcome> notes = new();
	private long lastOrder;
	private double lastSend = double.NegativeInfinity;
	private int open;
	private bool ended;

	/// <summary>How many entries are open (sent, not resolved).</summary>
	public int OpenCount => open;

	/// <summary>
	/// The queue's own waiter, for what the HUD does with an outcome (the advisor's event of an accepted door): never detached, so whichever HUD is current takes its outcomes, a resync's new
	/// HUD included. It keeps every outcome until a HUD takes it, as any live waiter does: only a HUD's door clicks queue for it, so it never holds more than its player's clicks, and the
	/// current HUD takes them every frame.
	/// </summary>
	public ManagementWaiter HudWaiter { get; }

	public CoopManagementRequests() => HudWaiter = new ManagementWaiter( this );

	/// <summary>A waiter for a window's opening, which its screen's removal detaches (<see cref="Detach"/>).</summary>
	public ManagementWaiter NewWaiter() => new( this );

	/// <summary>Whether the session ended: nothing is queued or sent any more.</summary>
	public bool Ended => ended;

	/// <summary>Every entry not resolved yet, oldest first.</summary>
	public IEnumerable<ManagementEntry> Pending => targets.Values.SelectMany( list => list ).OrderBy( entry => entry.Order );

	/// <summary>
	/// Queues a window action's commands whole and in order, for <paramref name="waiter"/> (none: their outcomes are notes). Every command must be well formed: a malformed one is a window's
	/// bug, so the whole batch is refused (an error in the log, and a strict run throws) and nothing is queued. A knob's command takes the place of its target's last entry when that is unsent,
	/// of the same knob and of the same waiter; anything else is appended, and a batch that would leave a target with more than <see cref="MaximumUnsent"/> unsent entries is not queued at all
	/// (<see cref="ManagementEnqueued.Full"/>).
	/// </summary>
	public ManagementEnqueued Enqueue( IReadOnlyList<ManagementItem> batch, ManagementWaiter? waiter = null )
	{
		ArgumentNullException.ThrowIfNull( batch );
		if ( waiter != null && waiter.Queue != this )
			throw new ArgumentException( "The waiter is another queue's.", nameof( waiter ) );
		if ( ended || batch.Count == 0 )
			return new ManagementEnqueued( Array.Empty<ManagementEntry>(), false );
		foreach ( var item in batch )
		{
			if ( item.Command is { IsWellFormed: true } )
				continue;
			var what = item.Command?.Kind.ToString() ?? "missing";
			Log?.Error( $"A management window queued a malformed {what} command; its action was dropped." );
			if ( ParkCommands.Strict )
				throw new InvalidOperationException( $"A management window queued a malformed {what} command." );
			return new ManagementEnqueued( Array.Empty<ManagementEntry>(), false );
		}
		if ( !Fits( batch, waiter ) )
			return new ManagementEnqueued( Array.Empty<ManagementEntry>(), true );
		var entries = new List<ManagementEntry>( batch.Count );
		foreach ( var item in batch )
		{
			if ( !targets.TryGetValue( item.Target, out var list ) )
				targets[item.Target] = list = new List<ManagementEntry>();
			if ( item.Knob is { } knob && list.Count > 0 && list[^1] is { State: ManagementEntryState.Unsent } tail && tail.Knob == knob && ReferenceEquals( tail.Waiter, waiter ) )
			{
				tail.Command = item.Command;
				entries.Add( tail );
				continue;
			}
			var entry = new ManagementEntry( this, ++lastOrder, item.Target, item.Knob, item.Command, waiter );
			list.Add( entry );
			entries.Add( entry );
		}
		return new ManagementEnqueued( entries, false );
	}

	/// <summary>Whether <paramref name="batch"/> fits whole: each target it appends to keeps at most <see cref="MaximumUnsent"/> unsent entries, after the batch's own supersessions.</summary>
	private bool Fits( IReadOnlyList<ManagementItem> batch, ManagementWaiter? waiter )
	{
		// each target's tail as the batch's earlier items leave it (its knob, whether it is unsent, its waiter) and the entries the batch appends there
		var tails = new Dictionary<ManagementTarget, (ManagementKnob? Knob, bool Unsent, ManagementWaiter? Waiter, int Appended)>();
		foreach ( var item in batch )
		{
			if ( !tails.TryGetValue( item.Target, out var tail ) )
				tail = targets.TryGetValue( item.Target, out var list ) && list.Count > 0
					? (list[^1].Knob, list[^1].State == ManagementEntryState.Unsent, list[^1].Waiter, 0)
					: (null, false, null, 0);
			var supersedes = item.Knob is { } knob && tail.Unsent && tail.Knob == knob && ReferenceEquals( tail.Waiter, waiter );
			tails[item.Target] = (item.Knob, true, waiter, tail.Appended + (supersedes ? 0 : 1));
		}
		return tails.All( pair => pair.Value.Appended == 0 || UnsentCount( pair.Key ) + pair.Value.Appended <= MaximumUnsent );
	}

	/// <summary>How many of <paramref name="target"/>'s entries are unsent.</summary>
	private int UnsentCount( ManagementTarget target ) => targets.TryGetValue( target, out var list ) ? list.Count( entry => entry.State == ManagementEntryState.Unsent ) : 0;

	/// <summary>The command of the latest entry of <paramref name="knob"/> not resolved yet (unsent or open), or null: what its control draws instead of the park's value.</summary>
	public ParkCommand? PendingCommand( ManagementTarget target, ManagementKnob knob ) =>
		targets.TryGetValue( target, out var list ) ? list.LastOrDefault( entry => entry.Knob == knob )?.Command : null;

	/// <summary>Whether an entry of <paramref name="target"/> that <paramref name="matches"/> is not resolved yet.</summary>
	public bool HasPending( ManagementTarget target, Func<ParkCommand, bool> matches ) =>
		targets.TryGetValue( target, out var list ) && list.Any( entry => matches( entry.Command ) );

	/// <summary>
	/// Once a frame, while the guest follows its host's stream with an unfenced ledger, against the session's current park: the oldest unsent entry at the head of a target without an open entry
	/// is first asked whether the rights still allow it (a right taken back before the send refuses it at once, without a request), then sent, while fewer than <see cref="MaximumOpen"/> are
	/// open and <see cref="SendSeconds"/> passed since the last send.
	/// </summary>
	public void Pump( IManagementIssuer issuer, double now )
	{
		ArgumentNullException.ThrowIfNull( issuer );
		while ( !ended && Head() is { } entry )
		{
			if ( issuer.RightsRefusalOf( entry.Command ) is { } refusal )
			{
				// an entry without a waiter says the rights' note through the session's refusal path alone (coalesced for a run); a waiter's outcome says it to its window
				if ( entry.Waiter == null )
				{
					issuer.RefusesHere( entry.Command );
					Resolve( entry, new ManagementOutcome( entry, ManagementOutcomeKind.Refused, refusal ), note: false );
				}
				else
					Resolve( entry, new ManagementOutcome( entry, ManagementOutcomeKind.Refused, refusal ) );
				continue;
			}
			if ( open >= MaximumOpen || now - lastSend < SendSeconds )
				return;
			Send( issuer, entry, now );
			return;
		}
	}

	/// <summary>The oldest unsent entry at the head of a target without an open entry, or null.</summary>
	private ManagementEntry? Head()
	{
		ManagementEntry? head = null;
		foreach ( var list in targets.Values )
			if ( list.Count > 0 && list[0].State == ManagementEntryState.Unsent && (head == null || list[0].Order < head.Order) )
				head = list[0];
		return head;
	}

	private void Send( IManagementIssuer issuer, ManagementEntry entry, double now )
	{
		lastSend = now;
		var result = issuer.Issue( entry.Command );
		if ( result.Request is not { } request )
		{
			// not sent after all: the rights refused it here (they were asked a moment ago), or the session takes no requests any more
			Resolve( entry, new ManagementOutcome( entry, result.RightsRefused ? ManagementOutcomeKind.Refused : ManagementOutcomeKind.Ended, result.RightsRefused ? result : null ) );
			return;
		}
		entry.State = ManagementEntryState.Open;
		entry.Request = request;
		open++;
		// the queue owns the request: the HUD's completion of the tools' requests skips it
		request.Owner = entry;
		// a request that resolved inside its own send resolved before the queue owned it
		if ( !request.IsOpen )
			Resolved( request );
	}

	/// <summary>
	/// The session's ledger resolved <paramref name="request"/> (<see cref="CoopGuestCommands.Resolved"/>, inside the stream's application or the link's pump): an open entry's resolves with it,
	/// freeing its target and its place in the cap, without touching the park.
	/// </summary>
	internal void Resolved( CoopGuestCommands.Request request )
	{
		if ( request.Owner is not ManagementEntry entry || entry.Queue != this || entry.State != ManagementEntryState.Open )
			return;
		var outcome = request.State switch
		{
			CoopGuestRequestState.Applied when request.Result is { Error: null } result => new ManagementOutcome( entry, result.Accepted ? ManagementOutcomeKind.Accepted : ManagementOutcomeKind.Refused, result ),
			CoopGuestRequestState.Refused => new ManagementOutcome( entry, ManagementOutcomeKind.NotTaken ),
			CoopGuestRequestState.Ended => new ManagementOutcome( entry, ManagementOutcomeKind.Ended ),
			_ => new ManagementOutcome( entry, ManagementOutcomeKind.Unknown, request.Result )
		};
		Resolve( entry, outcome );
	}

	/// <summary>Resolves <paramref name="entry"/>: its outcome goes to its live waiter, else (when <paramref name="note"/>) to the notes if it says anything.</summary>
	private void Resolve( ManagementEntry entry, ManagementOutcome outcome, bool note = true )
	{
		if ( entry.State == ManagementEntryState.Open )
			open--;
		entry.State = ManagementEntryState.Resolved;
		if ( targets.TryGetValue( entry.Target, out var list ) )
		{
			list.Remove( entry );
			if ( list.Count == 0 )
				targets.Remove( entry.Target );
		}
		if ( entry.Waiter is { Detached: false } waiter )
			waiter.Outcomes.Enqueue( outcome );
		else if ( note )
			AddNote( outcome );
	}

	/// <summary>
	/// Keeps <paramref name="outcome"/>'s note (if it says anything) among the newest <see cref="MaximumNotes"/>, for the HUD to say once its lines can be seen: an outcome nobody waits for,
	/// or one a waiter turned into a note (a failure under a covering screen, a refresh's refusal). The notes are the session's, so a resync's new HUD says what the old one could not.
	/// </summary>
	internal void AddNote( ManagementOutcome outcome )
	{
		if ( outcome.Note.Kind == ManagementNoteKind.None )
			return;
		notes.Enqueue( outcome );
		while ( notes.Count > MaximumNotes )
			notes.Dequeue();
	}

	/// <summary>The oldest note not said yet, or null; it stays until <see cref="TakeNote"/>.</summary>
	public ManagementOutcome? PeekNote() => notes.Count > 0 ? notes.Peek() : null;

	/// <summary>Takes the oldest note not said yet, or null.</summary>
	public ManagementOutcome? TakeNote() => notes.Count > 0 ? notes.Dequeue() : null;

	/// <summary><paramref name="waiter"/>'s outcomes not taken yet, in the order their entries resolved.</summary>
	public IReadOnlyList<ManagementOutcome> TakeOutcomes( ManagementWaiter waiter )
	{
		ArgumentNullException.ThrowIfNull( waiter );
		if ( waiter.Outcomes.Count == 0 )
			return Array.Empty<ManagementOutcome>();
		var taken = waiter.Outcomes.ToArray();
		waiter.Outcomes.Clear();
		return taken;
	}

	/// <summary>
	/// <paramref name="waiter"/>'s opening ended (its screen was removed: Escape, Back, a close, the HUD's teardown at a resync's swap): its outcomes not taken yet become notes, in order, and so
	/// do those of its entries that resolve later.
	/// </summary>
	public void Detach( ManagementWaiter waiter )
	{
		ArgumentNullException.ThrowIfNull( waiter );
		if ( waiter.Detached || waiter == HudWaiter )
			return;
		waiter.Detached = true;
		while ( waiter.Outcomes.TryDequeue( out var outcome ) )
			AddNote( outcome );
	}

	/// <summary>Every note not said yet, oldest first, each once (a headless guest's and the tests' view; the HUD takes them one at a time, <see cref="TakeNote"/>).</summary>
	public IReadOnlyList<ManagementOutcome> TakeNotes()
	{
		if ( notes.Count == 0 )
			return Array.Empty<ManagementOutcome>();
		var taken = notes.ToArray();
		notes.Clear();
		return taken;
	}

	/// <summary>The session ended: every entry not resolved yet resolves as ended, silently (a waiter still hears it), and nothing more is queued.</summary>
	public void End()
	{
		if ( ended )
			return;
		ended = true;
		foreach ( var entry in Pending.ToArray() )
			Resolve( entry, new ManagementOutcome( entry, ManagementOutcomeKind.Ended ) );
		targets.Clear();
		open = 0;
	}
}
