namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-commands] The line writer the path and queue tools hold while they run: previews and start checks stay local (<see cref="Inner"/>, no writes), and each
/// commit and undo of a line becomes one command (<see cref="LayLineCommand"/>, <see cref="UndoLineCommand"/>) that the park's <see cref="ParkCommands"/> applies. The tool's
/// vertex stack, its start and its ghost stay the issuer's own state.
/// </summary>
public sealed class ParkCommandLineWriter : ICellLineWriter
{
	private readonly ParkCommands commands;

	/// <param name="inner">The writer whose preview and start check the tool shows (the park's path builder, or the ride's queue writer).</param>
	/// <param name="ride">The attraction id of the queue's ride, or -1 for the path tool.</param>
	public ParkCommandLineWriter( ICellLineWriter inner, ParkCommands commands, int ride )
	{
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
		// [EXT:online-coop-commands] a guest's line goes to the host (C3a-2): nothing is laid yet, and the queue writer's state waits with the tool for the line's outcome
		if ( result.Request is { } request )
			return new SegmentResult( start, Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Ok, false )
			{
				Start = start,
				Line = ParkPathBuilder.LayLine( start, Inner.SnapsToLine ? ParkPathBuilder.SnapEnd( start, end ) : end ),
				Waiting = new WaitingLineCommit( request, outcome => Complete( start, end, native, outcome ) )
			};
		return Complete( start, end, native, result );
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

	/// <summary>[EXT:online-coop-commands] A guest's segments are not its tool's to take back: an undo would name cells the host never laid for it (C3a-2).</summary>
	public bool CanUndo => !commands.SendsRequests;

	public void Undo( SegmentResult segment )
	{
		ArgumentNullException.ThrowIfNull( segment );
		if ( segment.QueueReceipt is { } receipt )
		{
			commands.Issue( new UndoQueueSegmentCommand( Ride, receipt.Start.X, receipt.Start.Y, receipt.End.X, receipt.End.Y ) );
			return;
		}
		commands.Issue( new UndoLineCommand( Mode, Ride, segment.Start.X, segment.Start.Y, segment.Built.ToArray(), segment.Existing.ToArray() ) );
	}
}

/// <summary>
/// [EXT:online-coop-commands] A line command a guest's tool sent to the host (C3a-2): its request, and what turns the request's outcome (its record's result in the guest's park) into the
/// line's segment, once.
/// </summary>
internal sealed record WaitingLineCommit( CoopGuestCommands.Request Request, Func<ParkCommandResult, SegmentResult> Complete );
