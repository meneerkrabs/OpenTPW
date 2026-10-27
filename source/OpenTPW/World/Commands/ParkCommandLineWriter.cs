namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-commands] The line writer the path and queue tools hold while they run: previews and start checks stay local (<see cref="Inner"/>, no writes), and each
/// commit and undo of a line becomes one command (<see cref="LayLineCommand"/>, <see cref="UndoLineCommand"/>) that the park's <see cref="ParkCommands"/> applies. The tool's
/// vertex stack, its start and its ghost stay the issuer's own state. [EXT:online-coop-undo] In a shared park a segment keeps the record that laid it, and its undo is a take-back the
/// host decides (<see cref="TakeBackCommand"/>, C5): a guest's goes to the host and waits, the host's runs at once; either pops the segment only once taken.
/// </summary>
public sealed class ParkCommandLineWriter : ICellLineWriter, IGuestUndoWriter
{
	private readonly ParkCommands commands;
	private readonly Func<int>? selectedRide;
	private readonly Func<long?>? hostRecord;

	/// <param name="inner">The writer whose preview and start check the tool shows (the park's path builder, or the ride's queue writer).</param>
	/// <param name="ride">The attraction id of the queue's ride, or -1 for the path tool.</param>
	/// <param name="selectedRide">For the path tool: the attraction id of the ride the issuer has selected (-1 for none), read when the tool takes a segment back, so every peer's Back re-evaluates the same ride (<see cref="UndoLineCommand"/>).</param>
	/// <param name="hostRecord">[EXT:online-coop-undo] On a session's host: the record its stream took last, the record of a line the host just laid (C5); null elsewhere.</param>
	public ParkCommandLineWriter( ICellLineWriter inner, ParkCommands commands, int ride, Func<int>? selectedRide = null, Func<long?>? hostRecord = null )
	{
		this.selectedRide = selectedRide;
		this.hostRecord = hostRecord;
		Inner = inner ?? throw new ArgumentNullException( nameof( inner ) );
		this.commands = commands ?? throw new ArgumentNullException( nameof( commands ) );
		Ride = ride;
	}

	/// <summary>The writer the commands are executed through and the previews read.</summary>
	public ICellLineWriter Inner { get; }
	/// <summary>The queue's ride (attraction id), or -1.</summary>
	public int Ride { get; }
	public CellToolMode Mode => Inner.Mode;
	public bool SnapsToLine => Inner.SnapsToLine;
	public void ResetToolSession() => Inner.ResetToolSession();

	/// <summary>The writer a tool holds, without this layer (the queue writer of a queue tool, the track writers): what callers that ask for a writer's type read.</summary>
	public static ICellLineWriter? Unwrap( ICellLineWriter? writer ) => writer is ParkCommandLineWriter proxy ? proxy.Inner : writer;

	public SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor ) => Inner.Preview( start, cursor );

	public bool CanStart( (int X, int Y) cell ) => Inner.CanStart( cell );

	public SegmentResult Commit( (int X, int Y) start, (int X, int Y) end )
	{
		var native = Inner is QueueLineWriter queue && queue.CanUseNative( start, end );
		var result = commands.Issue( native ? new QueueSegmentCommand( Ride, start.X, start.Y, end.X, end.Y ) : new LayLineCommand( Mode, Ride, start.X, start.Y, end.X, end.Y ) );
		// [EXT:online-coop-commands] a guest's line goes to the host (C3a-2): nothing is laid yet, and the queue writer's state waits with the tool for the line's outcome;
		// [EXT:online-coop-undo] its segment keeps its request's record (C5)
		if ( result.Request is { } request )
			return new SegmentResult( start, Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Ok, false )
			{
				Start = start,
				Line = ParkPathBuilder.LayLine( start, Inner.SnapsToLine ? ParkPathBuilder.SnapEnd( start, end ) : end ),
				Waiting = new WaitingLineCommit( request, outcome => Complete( start, end, native, outcome ) with { Record = request.Record } )
			};
		// [EXT:online-coop-undo] the host's segment keeps the record its stream just took (C5)
		var laid = Complete( start, end, native, result );
		return result.Accepted && commands.SessionRules && hostRecord?.Invoke() is { } record ? laid with { Record = record } : laid;
	}

	/// <summary>
	/// The segment a line's command laid, from its result: at once, or [EXT:online-coop-commands] a guest's request's once its record applied in the guest's park (C3a-2). The queue writer's
	/// state follows what the command did.
	/// </summary>
	private SegmentResult Complete( (int X, int Y) start, (int X, int Y) end, bool native, ParkCommandResult result )
	{
		if ( Inner is QueueLineWriter activeQueue )
		{
			if ( native && result.As<SegmentResult>() is { } segment ) activeQueue.RememberNativeResult( segment );
			else activeQueue.ResetToolSession();
		}
		// a refused command lays nothing: the tool sees a line stopped at its first cell; one that threw while it ran may have laid some (CommandRefused stays false)
		return result.As<SegmentResult>() ?? new SegmentResult( start, Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Occupied, false )
		{
			Start = start,
			Line = ParkPathBuilder.LayLine( start, Inner.SnapsToLine ? ParkPathBuilder.SnapEnd( start, end ) : end ),
			StoppedAt = start,
			CommandRefused = result.Error == null
		};
	}

	/// <summary>[EXT:online-coop-undo] Every player's segments are its tool's to take back; in a shared park through the host's journal (C5).</summary>
	public bool CanUndo => true;

	/// <summary>[EXT:online-coop-undo] Whether the tool's Backspace is a take-back the host decides (C5): in a shared park, the host's and a guest's alike.</summary>
	internal bool TakesBack => commands.SessionRules || commands.SendsRequests;

	/// <summary>[EXT:online-coop-undo] Whether <paramref name="segment"/> did anything a take-back takes back: a cell it built, or for a path a cell it crossed (C5); one that laid nothing pops at once, as in single player.</summary>
	internal bool HasWork( SegmentResult segment ) => segment.Built.Count > 0 || (Mode == CellToolMode.Path && segment.Existing.Count > 0);

	/// <summary>[EXT:online-coop-undo] The take-back of <paramref name="segment"/> by its record (C5): the issuer's selected ride rides along, which the host uses for its own alone.</summary>
	private TakeBackCommand TakeBackOf( long record ) => new( false, record, -1, -1, Mode == CellToolMode.Path ? Math.Max( -1, selectedRide?.Invoke() ?? -1 ) : -1 );

	/// <summary>[EXT:online-coop-undo] The host's own take-back of <paramref name="segment"/> in a shared park (C5): its result, at once.</summary>
	internal ParkCommandResult TakeBack( SegmentResult segment ) => segment.Record is { } record
		? commands.Issue( TakeBackOf( record ) ) : ParkCommandResult.Rejected( TakeBackCommand.NotOwnReason );

	bool IGuestUndoWriter.UndoesByRequest => commands.SendsRequests;

	CoopGuestCommands.Request? IGuestUndoWriter.SendUndo( SegmentResult segment ) => segment.Record is { } record ? commands.Issue( TakeBackOf( record ) ).Request : null;

	bool IGuestUndoWriter.CompleteUndo( ParkCommandResult outcome, Action pop )
	{
		ArgumentNullException.ThrowIfNull( outcome );
		ArgumentNullException.ThrowIfNull( pop );
		if ( !outcome.Accepted )
			return false;
		pop();
		return true;
	}

	public void Undo( SegmentResult segment )
	{
		ArgumentNullException.ThrowIfNull( segment );
		if ( segment.QueueReceipt is { } receipt )
		{
			commands.Issue( new UndoQueueSegmentCommand( Ride, receipt.Start.X, receipt.Start.Y, receipt.End.X, receipt.End.Y ) );
			return;
		}
		commands.Issue( new UndoLineCommand( Mode, Mode == CellToolMode.Path ? Math.Max( -1, selectedRide?.Invoke() ?? -1 ) : Ride, segment.Start.X, segment.Start.Y, segment.Built.ToArray(), segment.Existing.ToArray() ) );
	}
}

/// <summary>
/// [EXT:online-coop-commands] A line command a guest's tool sent to the host (C3a-2): its request, and what turns the request's outcome (its record's result in the guest's park) into the
/// line's segment, once.
/// </summary>
internal sealed record WaitingLineCommit( CoopGuestCommands.Request Request, Func<ParkCommandResult, SegmentResult> Complete );
