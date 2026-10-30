namespace OpenTPW;

/// <summary>What a track line command did (<see cref="LayTrackLineCommand"/>, <see cref="UndoTrackLineCommand"/>): the laid line (null for an undo), and whether the circuit was walked again and was then valid (null: no walk).</summary>
public sealed record TrackLineDone( SegmentResult? Segment, bool? Valid );

/// <summary>
/// What a track cut did (<see cref="CutTrackCommand"/>): the cut, whether the walk after it found a valid circuit (null: no walk), and on an open circuit the open end the line tool goes on from
/// (the walk to it writes the tiles' overlap words, so it is the command's).
/// </summary>
public sealed record TrackCutDone( TrackCutResult Cut, bool? Valid, TrackOpenEnd? Open );

/// <summary>
/// The line tool's opening on a kart or water ride whose circuit is open (the track button, <see cref="Level.EnterTrackTool"/>): the walk from the station to the track's open end, which collects
/// the corners the tool's Backspace takes back and sets every walked tile's overlap word (<see cref="TrackEditor.FindOpenEnd"/>). The value is the open end.
/// </summary>
public sealed record OpenTrackLineToolCommand( int Ride ) : ParkToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.OpenTrackLineTool;
	internal override bool IsWellFormed => Ride >= 0;
	internal override void WriteFields( ParkCommandWriter writer ) => writer.Int( Ride );
	internal override ParkCommandResult Execute( Level level ) => level.ExecuteOpenTrackLine( Ride );
}

/// <summary>
/// One committed line of a kart or water ride's track tool (<see cref="TrackLineWriter"/>, the ride window's track button on an open circuit): from (<paramref name="StartX"/>, <paramref name="StartY"/>) toward
/// (<paramref name="EndX"/>, <paramref name="EndY"/>), snapped by the writer, for the ride whose attraction id is <paramref name="Ride"/>. The circuit is walked again when the line laid or met a tile. The value is
/// <see cref="TrackLineDone"/>; the issuer ends or continues its tool from it (<see cref="Level.ContinueTrackTool"/>).
/// </summary>
public sealed record LayTrackLineCommand( int Ride, int StartX, int StartY, int EndX, int EndY ) : ParkToolCommand
{
	/// <summary>
	/// [EXT:online-coop-undo] In a shared park, the issuer's continuation after a line that closes the circuit (C5-3): its click's exactly Ctrl with no queue start pending (UI-031), so the line
	/// tool restarts without a start. The issuer's continuation follows it, and the host's undo journal reads it, so it knows the restart's first line from a line laid after the tool's end. Its
	/// own codec generation (<see cref="ParkCommandCodec.TrackToolVersion"/>) says it, so single player's lines, which never set it, keep their version-1 bytes.
	/// </summary>
	public bool KeepsTool { get; init; }

	public override ParkCommandKind Kind => ParkCommandKind.LayTrackLine;
	internal override bool IsWellFormed => Ride >= 0 && IsCell( StartX, StartY ) && IsCell( EndX, EndY );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Ride );
		writer.Int( StartX );
		writer.Int( StartY );
		writer.Int( EndX );
		writer.Int( EndY );
	}

	internal override ParkCommandResult Execute( Level level ) => level.ExecuteTrackLine( Ride, (StartX, StartY), (EndX, EndY), undo: false );
}

/// <summary>
/// Backspace in a kart or water ride's track tool: the line from (<paramref name="StartX"/>, <paramref name="StartY"/>) to (<paramref name="EndX"/>, <paramref name="EndY"/>) of the ride whose attraction id is
/// <paramref name="Ride"/> is taken back by the editor's directional clear (<see cref="TrackEditor.UndoLine"/>), each cleared tile refunded, and the circuit walked again.
/// </summary>
public sealed record UndoTrackLineCommand( int Ride, int StartX, int StartY, int EndX, int EndY, bool Last = false ) : ParkToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.UndoTrackLine;
	// a line the tool laid is axis-aligned and whole tiles long (TrackLineWriter.SnapEnd)
	internal override bool IsWellFormed => Ride >= 0 && IsCell( StartX, StartY ) && IsCell( EndX, EndY ) && (StartX == EndX || StartY == EndY)
		&& (EndX - StartX) % TrackStation.TileCells == 0 && (EndY - StartY) % TrackStation.TileCells == 0;
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Ride );
		writer.Int( StartX );
		writer.Int( StartY );
		writer.Int( EndX );
		writer.Int( EndY );
		// a trailing 1 only when set: the bytes of a line that is not the stack's first are the format's original ones (recorded logs decode)
		if ( Last )
			writer.Bool( true );
	}

	internal override ParkCommandResult Execute( Level level ) => level.ExecuteTrackLine( Ride, (StartX, StartY), (EndX, EndY), undo: true, last: Last );
}

/// <summary>
/// The second click of a kart or water ride's track edit tool (<see cref="TrackEditWriter"/>, the track button on a closed circuit): the track of the ride whose attraction id is <paramref name="Ride"/> is cut
/// between the tile the first click picked (<paramref name="FirstX"/>, <paramref name="FirstY"/>) and the clicked one (<paramref name="SecondX"/>, <paramref name="SecondY"/>), the station repaired and the
/// circuit walked again. The value is <see cref="TrackCutDone"/>.
/// </summary>
public sealed record CutTrackCommand( int Ride, int FirstX, int FirstY, int SecondX, int SecondY ) : ParkToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.CutTrack;
	internal override bool IsWellFormed => Ride >= 0 && IsCell( FirstX, FirstY ) && IsCell( SecondX, SecondY );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Ride );
		writer.Int( FirstX );
		writer.Int( FirstY );
		writer.Int( SecondX );
		writer.Int( SecondY );
	}

	internal override ParkCommandResult Execute( Level level ) => level.ExecuteTrackCut( Ride, (FirstX, FirstY), (SecondX, SecondY) );
}

/// <summary>
/// [EXT:online-coop-commands] The line writer a kart or water ride's track tool holds: previews and the start check stay local (<see cref="Inner"/>, no writes), and each commit and undo of a line becomes one
/// command (<see cref="LayTrackLineCommand"/>, <see cref="UndoTrackLineCommand"/>). When the command walked the circuit to a valid one, the tool moves on as the walk once moved it
/// (<see cref="Level.ContinueTrackTool"/>: it ends, follows the queue or restarts), on the issuer's machine only. A guest's line and Backspace go to the host as requests under the ride's claim
/// (C3c-1b): the tool waits for each outcome, which lays or takes back the line and moves the tool on from there. [EXT:online-coop-undo] In a shared park a line keeps the record that laid it and
/// carries its click's restart (<see cref="LayTrackLineCommand.KeepsTool"/>), which its continuation follows; its Backspace is a take-back the host decides (<see cref="TakeBackCommand"/>, C5-3):
/// a guest's goes to the host and waits, the host's runs at once; either pops the line only once taken.
/// </summary>
public sealed class ParkCommandTrackWriter : ICellLineWriter, ITakeBackWriter
{
	private readonly ParkCommands commands;
	private readonly Action<bool?> continueTool;
	private readonly Func<bool>? keepsTool;
	private readonly Func<long?>? hostRecord;

	/// <param name="inner">The ride's line writer, whose previews the tool shows.</param>
	/// <param name="ride">The attraction id of the ride whose track the tool lays.</param>
	/// <param name="continueTool">The issuer's tool step after a walk that found a valid circuit: [EXT:online-coop-undo] in a shared park with the restart its line carried, else null (the click's own).</param>
	/// <param name="keepsTool">[EXT:online-coop-undo] The issuer's restart after a closing line, read at its click (exactly Ctrl and no queue start pending, UI-031); a shared park's line carries it (C5-3).</param>
	/// <param name="hostRecord">[EXT:online-coop-undo] On a session's host: the record its stream took last, the record of a line the host just laid (C5-3); null elsewhere.</param>
	public ParkCommandTrackWriter( ICellLineWriter inner, ParkCommands commands, int ride, Action<bool?> continueTool, Func<bool>? keepsTool = null, Func<long?>? hostRecord = null )
	{
		Inner = inner ?? throw new ArgumentNullException( nameof( inner ) );
		this.commands = commands ?? throw new ArgumentNullException( nameof( commands ) );
		this.continueTool = continueTool ?? throw new ArgumentNullException( nameof( continueTool ) );
		this.keepsTool = keepsTool;
		this.hostRecord = hostRecord;
		Ride = ride;
	}

	/// <summary>The ride's line writer the previews read.</summary>
	public ICellLineWriter Inner { get; }
	/// <summary>The attraction id of the ride.</summary>
	public int Ride { get; }
	public CellToolMode Mode => Inner.Mode;
	public bool SnapsToLine => Inner.SnapsToLine;
	public int VertexCapacity => Inner.VertexCapacity;

	public SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor ) => Inner.Preview( start, cursor );

	public bool CanStart( (int X, int Y) cell ) => Inner.CanStart( cell );

	/// <summary>[EXT:online-coop-undo] Whether the tool's Backspace is a take-back the host decides (C5-3): in a shared park, the host's and a guest's alike.</summary>
	internal bool TakesBack => commands.SessionRules || commands.SendsRequests;

	public SegmentResult Commit( (int X, int Y) start, (int X, int Y) end )
	{
		// [EXT:online-coop-undo] a shared park's line carries its click's restart, which its continuation follows and the host's journal reads (C5-3); single player's lines keep their bytes
		var keeps = TakesBack && keepsTool?.Invoke() == true;
		var result = commands.Issue( new LayTrackLineCommand( Ride, start.X, start.Y, end.X, end.Y ) { KeepsTool = keeps } );
		// a guest's line goes to the host (C3c-1b): nothing is laid yet, and the tool waits for the line's outcome, whose walk moves the tool on from there; [EXT:online-coop-undo] its segment
		// keeps its request's record (C5-3)
		if ( result.Request is { } request )
			return new SegmentResult( start, Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Ok, false )
			{
				Start = start,
				Line = LineCells( start, TrackLineWriter.SnapEnd( start, end ) ),
				Waiting = new WaitingLineCommit( request, outcome => Complete( start, end, outcome, keeps ) with { Record = request.Record } )
			};
		// [EXT:online-coop-undo] the host's segment keeps the record its stream just took, read before the continuation (C5-3)
		var record = result.Accepted && commands.SessionRules ? hostRecord?.Invoke() : null;
		var laid = Complete( start, end, result, keeps );
		return record is { } taken ? laid with { Record = taken } : laid;
	}

	/// <summary>The segment a line's command laid, from its result (at once, or a guest's request's once its record applied in the guest's park), after the tool's step when the walk found the circuit valid.</summary>
	private SegmentResult Complete( (int X, int Y) start, (int X, int Y) end, ParkCommandResult result, bool keeps )
	{
		if ( result.Value is TrackLineDone { Segment: { } segment } done )
		{
			if ( done.Valid == true )
				continueTool( TakesBack ? keeps : null );
			return segment;
		}
		// a refused command lays nothing: the tool sees a line stopped at its first cell; one that threw while it ran may have laid some (CommandRefused stays false)
		var snapped = TrackLineWriter.SnapEnd( start, end );
		return new SegmentResult( snapped, Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Occupied, false )
		{
			Start = start,
			Line = LineCells( start, snapped ),
			StoppedAt = start,
			CommandRefused = result.Error == null
		};
	}

	/// <summary>The cells of the tiles of the line from <paramref name="start"/> to <paramref name="end"/>.</summary>
	private static IReadOnlyList<(int X, int Y)> LineCells( (int X, int Y) start, (int X, int Y) end ) =>
		TrackLineWriter.LayLine( start, end ).SelectMany( tile => TrackStation.TileCellsOf( tile.X, tile.Y ) ).ToArray();

	public void Undo( SegmentResult segment )
	{
		ArgumentNullException.ThrowIfNull( segment );
		if ( commands.Issue( UndoOf( segment ) ).Value is TrackLineDone { Valid: true } )
			continueTool( null );
	}

	private UndoTrackLineCommand UndoOf( SegmentResult segment ) => new( Ride, segment.Start.X, segment.Start.Y, segment.SnappedEnd.X, segment.SnappedEnd.Y, segment.IsFirstLine );

	/// <summary>
	/// [EXT:online-coop-undo] The take-back of a line by its record (C5-3): a request names no ride and no first-line flag, which the host's journal derives from the line's own step.
	/// </summary>
	private static TakeBackCommand TakeBackOf( long record ) => new( false, record, -1, -1, -1 );

	bool ITakeBackWriter.TakesBack => TakesBack;

	/// <summary>[EXT:online-coop-undo] Every line that reaches the stack laid or met a tile; a segment the tool loaded (no record) was laid by no step of its player's, which its Backspace says (C5-3).</summary>
	bool ITakeBackWriter.HasWork( SegmentResult segment ) => segment.Record == null || segment.Built.Count > 0 || segment.Existing.Count > 0;

	ParkCommandResult ITakeBackWriter.TakeBack( SegmentResult segment ) => segment.Record is { } record
		? commands.Issue( TakeBackOf( record ) ) : ParkCommandResult.Rejected( TakeBackCommand.NotOwnReason );

	/// <summary>[EXT:online-coop-undo] The host's taken take-back moves the tool on as its undo's walk once moved it (C5-3).</summary>
	void ITakeBackWriter.AfterTakenBack( ParkCommandResult taken )
	{
		if ( taken.Value is TrackLineDone { Valid: true } )
			continueTool( null );
	}

	/// <summary>A guest's Backspace goes to the host first: the ride's claim makes its track the guest's alone, so its last line is the guest's to take back (C3c-1b).</summary>
	bool IGuestUndoWriter.UndoesByRequest => commands.SendsRequests;

	CoopGuestCommands.Request? IGuestUndoWriter.SendUndo( SegmentResult segment )
	{
		ArgumentNullException.ThrowIfNull( segment );
		// [EXT:online-coop-undo] in a shared park a take-back by the line's record (C5-3)
		if ( TakesBack )
			return segment.Record is { } record ? commands.Issue( TakeBackOf( record ) ).Request : null;
		return commands.Issue( UndoOf( segment ) ).Request;
	}

	bool IGuestUndoWriter.CompleteUndo( ParkCommandResult outcome, Action pop )
	{
		ArgumentNullException.ThrowIfNull( outcome );
		ArgumentNullException.ThrowIfNull( pop );
		// a taken take-back's result is its inverse's
		if ( outcome.Value is not TrackLineDone done )
			return false;
		pop();
		if ( done.Valid == true )
			continueTool( null );
		return true;
	}
}
