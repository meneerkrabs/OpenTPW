namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-commands] A guest's coaster tool commands on their way to the host (docs/COOP.md, "A guest's coaster tool (C3c-2)"): one queue in order, the level's, so it outlives the panel
/// that filled it (a drag's put-back and end and the tool's end go after the panel closed). It sends from its head while fewer than <see cref="Level.OpenToolRequestLimit"/> tool requests of the
/// guest are open in the session (the pool tool's counted too), a drag's step at most every <see cref="StepSeconds"/> and anything else at once. A drag's step replaces the unsent step of the same
/// field of that drag, wherever it waits, so a drag holds at most one unsent step per field besides its press and its end. Each command may carry what its outcome does, run once it is back.
/// A drag's end settles the money against the pylon as the drag's steps left it, so it goes only once every command before it came back, and nothing goes after it until it is back. A drag's last
/// value of a field, or its end, that the host found too fast goes again, first, <see cref="RetrySeconds"/> later, as often as it takes; one whose outcome is not known goes again at most
/// <see cref="MaximumRetries"/> times. Any other loss of them gives the drag up, and its panel ends (<see cref="CoasterGuestAction.DragLost"/>): the claim's release ends the edit the host holds.
/// </summary>
internal sealed class CoasterRequestQueue
{
	/// <summary>The least time between two drag steps sent: the drag's latest value goes at most this often (0.2 s, as the pool tool's batches).</summary>
	public const double StepSeconds = 0.2;

	/// <summary>How often a drag's last value of a field, or its end, goes again after its outcome was not known.</summary>
	public const int MaximumRetries = 5;

	/// <summary>How long a drag's last value of a field, or its end, that the host found too fast waits before it goes again: the host's bucket of the guest's requests refills meanwhile.</summary>
	public const double RetrySeconds = 0.5;

	/// <summary>One queued command: its drag (0 for none), a step's field, the request once sent, what its outcome does, how often it went again and when it may go again.</summary>
	private sealed class Item
	{
		public required ParkCommand Command;
		public int Drag;
		public CoasterNodeField? Field;
		public CoopGuestCommands.Request? Request;
		public Action<CoasterOutcome>? Completed;
		public int Retries;
		/// <summary>When it may go again (NaN: <see cref="RetrySeconds"/> from the next send).</summary>
		public double RetryAt = double.NegativeInfinity;

		/// <summary>Whether this is a drag's end, which settles what the drag's steps left.</summary>
		public bool IsDragEnd => Drag > 0 && Command is EndCoasterNodeEditCommand;
	}

	private readonly List<Item> unsent = new();
	private readonly List<Item> sent = new();
	private double lastStep = double.NegativeInfinity;
	private int lastDrag;

	/// <summary>A number for a new drag, unique in this queue: its steps, its end and their retries are that drag's alone, whichever panel queued them.</summary>
	public int NewDrag() => ++lastDrag;

	/// <summary>How many commands went again after the host took no record of them or their outcome was not known (the tests read it).</summary>
	internal int Retried { get; private set; }

	/// <summary>Whether a command waits to be sent: the guest's claims and its track or coaster tool entries wait for it (<see cref="Level.SettleClaim"/>).</summary>
	public bool HasUnsent => unsent.Count > 0;

	/// <summary>Whether anything of the queue is not back yet.</summary>
	public bool IsEmpty => unsent.Count == 0 && sent.Count == 0;

	/// <summary>
	/// Queues <paramref name="command"/> behind what waits, for <paramref name="drag"/> (0 for none); <paramref name="completed"/> runs once with the outcome (<see cref="CoasterOutcome"/>).
	/// </summary>
	public void Enqueue( ParkCommand command, int drag = 0, Action<CoasterOutcome>? completed = null )
	{
		ArgumentNullException.ThrowIfNull( command );
		unsent.Add( new Item { Command = command, Drag = drag, Completed = completed } );
	}

	/// <summary>A step of <paramref name="drag"/>: it replaces the unsent step of the same field of that drag, else it waits behind the queue.</summary>
	public void Step( int drag, EditCoasterNodeCommand step )
	{
		ArgumentNullException.ThrowIfNull( step );
		var waiting = unsent.FindIndex( item => item.Drag == drag && item.Field == step.Field );
		if ( waiting >= 0 )
		{
			unsent[waiting].Command = step;
			return;
		}
		unsent.Add( new Item { Command = step, Drag = drag, Field = step.Field } );
	}

	/// <summary>Whether <paramref name="drag"/>'s press has not been sent yet.</summary>
	public bool PressUnsent( int drag ) => unsent.Any( item => item.Drag == drag && item.Command is BeginCoasterNodeEditCommand );

	/// <summary>Drops what of <paramref name="drag"/> was not sent yet: its steps, and its press when that did not go (the drag then never began at the host).</summary>
	public void DropUnsent( int drag ) => unsent.RemoveAll( item => item.Drag == drag );

	/// <summary>
	/// Sends from the head: while <paramref name="openRequests"/> stays below <paramref name="limit"/>, each command at once and a step only when <see cref="StepSeconds"/> of
	/// <paramref name="now"/> passed since the last step went; a drag's end only once every command sent before it came back, and nothing while a drag's end is out. <paramref name="issue"/> is told
	/// whether the command's outcome has a completion (its panel says a refusal itself). A command refused before it went (the rights) completes at once with its result. Stops at the first that
	/// may not go yet, so the order holds.
	/// </summary>
	public void Send( Func<ParkCommand, bool, ParkCommandResult> issue, Func<int> openRequests, int limit, double now )
	{
		ArgumentNullException.ThrowIfNull( issue );
		ArgumentNullException.ThrowIfNull( openRequests );
		while ( unsent.Count > 0 )
		{
			var item = unsent[0];
			if ( sent.Any( other => other.IsDragEnd ) || (item.IsDragEnd && sent.Count > 0) )
				return;
			if ( double.IsNaN( item.RetryAt ) )
				item.RetryAt = now + RetrySeconds;
			if ( now < item.RetryAt || openRequests() >= limit || (item.Field != null && now - lastStep < StepSeconds) )
				return;
			unsent.RemoveAt( 0 );
			if ( item.Field != null )
				lastStep = now;
			var result = issue( item.Command, item.Completed != null );
			if ( result.Request is { } request )
			{
				item.Request = request;
				sent.Add( item );
				continue;
			}
			item.Completed?.Invoke( new CoasterOutcome( result, false, false ) );
		}
	}

	/// <summary>
	/// A guest request resolved (the HUD's drain): when it is one of this queue's, its completion runs with its outcome. A drag's end, or a drag's step whose field no later command of the drag
	/// sets, goes again instead, before anything else, when the host found it too fast (<see cref="RetrySeconds"/> later, as often as it takes) or its outcome is not known (at once, at most
	/// <see cref="MaximumRetries"/> times): the end then settles the drag's last values (setting a value again, or ending a drag that ended, changes nothing more). Any other loss of them gives
	/// the drag up (<see cref="CoasterGuestAction.DragLost"/>). True when it was this queue's.
	/// </summary>
	public bool Complete( CoopGuestCommands.Request request )
	{
		ArgumentNullException.ThrowIfNull( request );
		var index = sent.FindIndex( item => ReferenceEquals( item.Request, request ) );
		if ( index < 0 )
			return false;
		var item = sent[index];
		sent.RemoveAt( index );
		var outcome = request switch
		{
			{ State: CoopGuestRequestState.Applied, Result: { Error: null } result } => new CoasterOutcome( result, false, false ),
			{ State: CoopGuestRequestState.Refused } => new CoasterOutcome( null, true, false ),
			_ => new CoasterOutcome( null, false, true )
		};
		if ( (outcome.NotTaken || outcome.Unknown) && (item.IsDragEnd || (item.Field is { } field && !SetLater( item.Drag, field, index ))) )
		{
			var tooFast = request is { State: CoopGuestRequestState.Refused, Refusal: CoopGuestRefusal.TooFast };
			if ( tooFast || (outcome.Unknown && item.Retries < MaximumRetries) )
			{
				item.Retries++;
				item.Request = null;
				item.RetryAt = tooFast ? double.NaN : double.NegativeInfinity;
				unsent.Insert( 0, item );
				Retried++;
				return true;
			}
			// given up (a park that takes no commands now, a seat lost, an outcome not known again and again): the drag is lost, and its panel ends
			Reports.Add( new CoasterGuestReport( CoasterGuestAction.DragLost, outcome, Open: false, Closing: false ) { Drag = item.Drag } );
		}
		item.Completed?.Invoke( outcome );
		return true;
	}

	/// <summary>Whether a command of <paramref name="drag"/> sent after the one that was at <paramref name="sentIndex"/>, or waiting to go, sets <paramref name="field"/>.</summary>
	private bool SetLater( int drag, CoasterNodeField field, int sentIndex ) =>
		sent.Skip( sentIndex ).Any( item => item.Drag == drag && item.Field == field ) || unsent.Any( item => item.Drag == drag && item.Field == field );

	/// <summary>The outcomes of the guest's waited actions (<see cref="CoasterGuestReport"/>) the HUD has not said yet, oldest first: the queue's, so a closed panel's still come back.</summary>
	internal List<CoasterGuestReport> Reports { get; } = new();

	/// <summary>The reports since the last call, oldest first.</summary>
	public IReadOnlyList<CoasterGuestReport> TakeReports()
	{
		if ( Reports.Count == 0 )
			return Array.Empty<CoasterGuestReport>();
		var taken = Reports.ToArray();
		Reports.Clear();
		return taken;
	}

	/// <summary>Drops everything, sent or not, and what was not said: the session ended, so no outcome comes.</summary>
	public void Clear()
	{
		unsent.Clear();
		sent.Clear();
		Reports.Clear();
	}
}

/// <summary>
/// [EXT:online-coop-commands] What became of a guest's coaster tool command (C3c-2): its result (in the guest's park once its record applied there, or here when the rights refused it before it went),
/// or none, when the host took no record of it (<see cref="NotTaken"/>) or its outcome is not known (<see cref="Unknown"/>: an error, a lost answer, a snapshot, the session's end).
/// </summary>
internal readonly record struct CoasterOutcome( ParkCommandResult? Result, bool NotTaken, bool Unknown );
