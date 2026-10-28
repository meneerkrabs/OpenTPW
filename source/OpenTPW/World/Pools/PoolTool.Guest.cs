namespace OpenTPW;

/// <summary>What a guest's finished pool stroke tells its player, as <c>ParkHud.ReportPoolStroke</c> tells the host at its release (docs/COOP.md, "A guest's pools").</summary>
/// <param name="Wrote">A cell was painted or erased, a slide piece laid or a bridge built: the lay sound.</param>
/// <param name="Refusal">The stroke's first refused cell, in the order its cells were tried, with the reason as the tool's cursor reads it.</param>
/// <param name="SlideRefusal">Why the first refused slide piece was refused.</param>
/// <param name="BridgeRefusal">Why the bridge was refused.</param>
/// <param name="SlideDone">The stroke laid a piece and finished its slide: the done message, and the slide mode ends.</param>
/// <param name="RightsRefused">The first of its requests the session's rights refused at its record, which the note names.</param>
/// <param name="RightsReason">That refusal's reason as the rights gave it ([EXT:online-coop-claims] another player's claim, C3c-1), which the note follows.</param>
/// <param name="NotTaken">The host took a request of it without a record (too fast, too many).</param>
/// <param name="Unknown">A request of it ended without a known outcome (an error, a lost answer).</param>
// [EXT:online-coop-commands] the stroke's report of a guest (C3b-3)
public sealed record PoolGuestReport( bool Wrote, ((int X, int Y) Cell, PoolBuildResult Reason)? Refusal, PoolSlideResult? SlideRefusal, PoolBridgeResult? BridgeRefusal, bool SlideDone,
	ParkCommand? RightsRefused, string? RightsReason, bool NotTaken, bool Unknown )
{
	/// <summary>[EXT:online-coop-undo] The reason the host's journal refused a slide's take-back for (C5), said by its note; null when taken, or when it met a finished slide (quiet).</summary>
	public string? TakeBackRefusal { get; init; }
	/// <summary>[EXT:online-coop-undo] The slide mode the stroke was drawn in (<see cref="PoolTool.InSlideSession"/>), so a late outcome acts on that slide mode alone (C5).</summary>
	public int SlideSession { get; init; }
	/// <summary>[EXT:online-coop-undo] The paint or erase mode a stroke's take-back was sent from (<see cref="PoolTool.InPaintSession"/>), which its rights refusal ends (C5-2).</summary>
	public int PaintSession { get; init; }
}

/// <summary>
/// [EXT:online-coop-commands] A guest's strokes (docs/COOP.md, "A guest's pools"; C3b-3). On a guest's park the tool's writes go to the host as requests, and one request a cell would outrun
/// the session's bucket, so a stroke's cells go in batches (<see cref="PoolCellsCommand"/>, <see cref="LayPoolSlidePiecesCommand"/>; a bridge and a slide's undo as their own commands):
/// <list type="bullet">
/// <item>the cells the host's tool would write, in that order, wait in one queue for every stroke (at most <see cref="MaximumUnsentCells"/>), and go from its head only, so a later stroke
/// never passes an earlier one's cells; a cell crossed while the queue is full is dropped, and the player is asked to wait;</item>
/// <item>a batch goes while fewer than <see cref="MaximumOpenRequests"/> pool requests of the guest are open in the session (counted from the session's requests, so across the tool's ends and
/// resyncs), a press's first at once and the others at most every <see cref="SendSeconds"/>;</item>
/// <item>a stroke reports once released, its batches all sent and every one back (<see cref="TakeGuestReports"/>): its first refusal is the first in the order its cells were tried, whatever order
/// the answers came in; an outcome that finished its slide drops its unsent cells;</item>
/// <item>the cells sent and waiting are the stroke's ghost (<see cref="GuestStrokeCells"/>) until their outcome; Escape, a right click or another tool drops the unsent cells, and the stroke
/// under the press and any that lost cells report nothing, as a cancelled stroke of the host's, their sent requests orphans.</item>
/// </list>
/// The host's tool writes at once, as before.
/// </summary>
public sealed partial class PoolTool
{
	/// <summary>The most pool requests of a guest open at once in the session.</summary>
	public const int MaximumOpenRequests = 4;
	/// <summary>The most cells a guest's strokes keep unsent at once: one batch.</summary>
	public const int MaximumUnsentCells = PoolCellsCommand.MaximumCells;
	/// <summary>How often a guest's stroke sends its cells at most, in seconds; a press's first cell goes at once.</summary>
	public const double SendSeconds = 0.2;

	/// <summary>Whether the tool's writes go to the host as requests (a guest's park in a session, <see cref="ParkCommands.SendsRequests"/>); set by the level.</summary>
	internal Func<bool>? SendsRequests { get; set; }

	/// <summary>[EXT:online-coop-undo] Whether the slide mode's Backspace is a take-back the host decides (C5): in a shared park, the host's and a guest's alike.</summary>
	internal Func<bool>? TakesBack { get; set; }

	/// <summary>[EXT:online-coop-undo] On a session's host: the record its stream took last, the record of a piece the host just laid (C5).</summary>
	internal Func<long?>? HostRecord { get; set; }

	/// <summary>
	/// [EXT:online-coop-undo] The slide pieces this tool laid in a shared park (C5), oldest first: each one's tower, record and ordinal within its record, the step a take-back names. The slide
	/// mode's Backspace takes back the newest of its own tower's, never another player's piece.
	/// </summary>
	private readonly List<((int X, int Y) Tower, long Record, int Part)> ownPieces = new();

	/// <summary>[EXT:online-coop-undo] Each start of the slide mode (<see cref="BeginSlide"/>); a stroke keeps the one it was drawn in (C5).</summary>
	private int slideSession;

	/// <summary>[EXT:online-coop-undo] Whether the slide mode started as <paramref name="session"/> is still on (C5).</summary>
	internal bool InSlideSession( int session ) => IsActive && slideTower != null && session == slideSession;

	/// <summary>[EXT:online-coop-undo] Each start of the paint or erase mode (<see cref="Begin"/>); a stroke's take-back keeps the one it was sent from (C5-2).</summary>
	private int paintSession;

	/// <summary>[EXT:online-coop-undo] Whether the paint or erase mode started as <paramref name="session"/> is still on (C5-2).</summary>
	internal bool InPaintSession( int session ) => IsActive && slideTower == null && !bridgeMode && session == paintSession;

	/// <summary>
	/// [EXT:online-coop-undo] The guest's own strokes in a shared park that wrote something, oldest first, each by its name: the record of the batch that opened it (C5-2). Backspace takes back
	/// the newest; as the host's stroke stack, they go when the tool ends, and a stroke drawn before a resync is none of this tool's.
	/// </summary>
	private readonly List<long> ownStrokes = new();

	/// <summary>[EXT:online-coop-undo] Each run of the tool, from its start to its end (<see cref="Cancel"/>): a guest's stroke keeps the one it was drawn in (C5-2).</summary>
	private int toolRun;

	/// <summary>[EXT:online-coop-undo] The newest piece this tool laid on <paramref name="tower"/>'s slide, or null (C5).</summary>
	private (long Record, int Part)? NewestOwnPiece( (int X, int Y) tower )
	{
		var index = ownPieces.FindLastIndex( piece => piece.Tower == tower );
		return index >= 0 ? (ownPieces[index].Record, ownPieces[index].Part) : null;
	}
	/// <summary>How many tool requests of this guest are open in the session's requests, the pool tool's and the coaster tool's (<see cref="Level.OpenToolRequests"/>); set by the level.</summary>
	internal Func<int>? OpenRequests { get; set; }
	/// <summary>Asks the player to wait (<see cref="Level.NoteWaitingClick"/>); set by the level.</summary>
	internal Action? NoteWaiting { get; set; }

	private enum GuestWrite : byte { Cells, Slide, Bridge, SlideUndo, StrokeUndo }

	/// <summary>A guest's stroke: what it writes, its batches in the order they go, and what became of it.</summary>
	private sealed class GuestStroke( GuestWrite write, bool erase, bool river, (int X, int Y)? tower, int direction )
	{
		public GuestWrite Write { get; } = write;
		/// <summary>[EXT:online-coop-undo] The slide mode it was drawn in (C5); for a stroke's take-back, the paint or erase mode it was sent from (C5-2).</summary>
		public int Session { get; init; }
		/// <summary>[EXT:online-coop-undo] The tool's run it was drawn in (<see cref="toolRun"/>): a late outcome of a run the tool has left names no stroke of the new one (C5-2).</summary>
		public int Run { get; init; }
		public bool Erase { get; } = erase;
		public bool River { get; } = river;
		public (int X, int Y)? Tower { get; } = tower;
		public int Direction { get; } = direction;
		public List<GuestBatch> Batches { get; } = new();
		public bool Released;
		/// <summary>An outcome finished its slide: it sends nothing more.</summary>
		public bool Finished;
		/// <summary>It lost unsent cells to a cancel: it reports nothing.</summary>
		public bool Discarded;
		/// <summary>A cell of it was dropped for a full queue: the player was asked to wait, and it takes no more.</summary>
		public bool Dropped;
		/// <summary>It takes no more cells: a cell of it was dropped for a full queue, or the funnel refused a batch of it here (the guest's own rights), after which it sends nothing more either.</summary>
		public bool Stopped;
		/// <summary>
		/// [EXT:online-coop-undo] In a shared park, its name once a batch that opened it came back with a record (C5-2): its later batches go only then, and while none has, the next batch opens
		/// it again (the host could not take the last one).
		/// </summary>
		public long? Name;
		/// <summary>[EXT:online-coop-undo] The batch that opens it, sent and not yet back (C5-2).</summary>
		public GuestBatch? Opening;
		/// <summary>[EXT:online-coop-undo] The cells it took, which a shared park bounds (<see cref="SessionStrokeCells"/>, C5-2).</summary>
		public int Cells;
	}

	/// <summary>One request of a guest's stroke: its cells, and once sent its request; resolved once its outcome is in (or it is dropped).</summary>
	private sealed class GuestBatch( GuestStroke stroke, bool first )
	{
		public GuestStroke Stroke { get; } = stroke;
		public bool First { get; } = first;
		public List<(int X, int Y)> Cells { get; } = new();
		/// <summary>The command it went as, once sent or refused here.</summary>
		public ParkCommand? Command;
		public CoopGuestCommands.Request? Request;
		/// <summary>[EXT:online-coop-undo] The own piece a slide's take-back names, chosen when it goes (C5).</summary>
		public (long Record, int Part)? Piece;
		/// <summary>[EXT:online-coop-undo] Whether it opens its stroke (the first marker), and the own stroke a stroke's take-back names, chosen when it goes (C5-2).</summary>
		public bool Opens;
		public long? StrokeName;
		/// <summary>The funnel's answer when it refused the batch itself (the guest's own rights): no request went.</summary>
		public ParkCommandResult? Local;
		public bool Resolved;
	}

	private readonly List<GuestBatch> unsentBatches = new();
	private readonly List<GuestBatch> sentBatches = new();
	private readonly List<PoolGuestReport> guestReports = new();
	private GuestStroke? guestStroke;
	private double lastGuestSend = double.NegativeInfinity;

	/// <summary>The cells of a guest's strokes sent or waiting to be sent, until their outcome: the strokes' ghost.</summary>
	public IEnumerable<(int X, int Y)> GuestStrokeCells => unsentBatches.Concat( sentBatches )
		.Where( batch => batch.Stroke.Write is GuestWrite.Cells or GuestWrite.Slide ).SelectMany( batch => batch.Cells );

	/// <summary>
	/// Set while the tool hands a batch of a guest's stroke to the funnel: a refusal of the session's rights then is the stroke report's to say, once for the stroke, and not the session's
	/// note (<c>CoopSession.OnRefused</c>).
	/// </summary>
	internal bool SendingGuestBatch { get; private set; }

	/// <summary>Whether a guest's stroke has cells unsent or requests open.</summary>
	public bool HasGuestBatches => unsentBatches.Count > 0 || sentBatches.Count > 0;

	private int UnsentCells => unsentBatches.Sum( batch => batch.Cells.Count );

	/// <summary>A guest's press: a new stroke whose first batch holds the cell (or the bridge) and goes at once.</summary>
	private void BeginGuestStroke( int x, int y )
	{
		var write = bridgeMode ? GuestWrite.Bridge : slideTower != null ? GuestWrite.Slide : GuestWrite.Cells;
		guestStroke = new GuestStroke( write, IsErasing, IsRiver, slideTower, bridgeDirection ) { Session = slideSession, Run = toolRun };
		AddGuestCell( x, y );
	}

	/// <summary>Adds a cell to the guest's stroke: to its batch still waiting, or a new one. False when the stroke takes no more (its slide is finished).</summary>
	private bool AddGuestCell( int x, int y )
	{
		if ( guestStroke is not { } stroke || stroke.Finished || stroke.Stopped )
			return false;
		// [EXT:online-coop-undo] in a shared park a stroke that took its most cells ends, and the drag goes on as a new one (C5-2)
		if ( stroke.Write == GuestWrite.Cells && TakesBack?.Invoke() == true && stroke.Cells >= StrokeCellBound )
		{
			ReleaseGuestStroke();
			guestStroke = stroke = new GuestStroke( GuestWrite.Cells, stroke.Erase, stroke.River, null, 0 ) { Session = slideSession, Run = toolRun };
		}
		stroke.Cells++;
		// a full queue stops the stroke at the cell it could not take: later cells would leave a hole (a slide's next piece would not follow its last)
		if ( UnsentCells >= MaximumUnsentCells )
		{
			stroke.Dropped = true;
			stroke.Stopped = true;
			NoteWaiting?.Invoke();
			return false;
		}
		var tail = stroke.Batches.Count > 0 ? stroke.Batches[^1] : null;
		if ( tail == null || tail.Request != null || tail.Resolved || tail.Cells.Count >= PoolCellsCommand.MaximumCells || stroke.Write == GuestWrite.Bridge )
		{
			tail = new GuestBatch( stroke, first: stroke.Batches.Count == 0 );
			stroke.Batches.Add( tail );
			unsentBatches.Add( tail );
		}
		tail.Cells.Add( (x, y) );
		return true;
	}

	/// <summary>The guest's press ends: its stroke reports once its batches are sent and back.</summary>
	private void ReleaseGuestStroke()
	{
		if ( guestStroke is not { } stroke )
			return;
		guestStroke = null;
		stroke.Released = true;
		TryFinishGuestStroke( stroke );
	}

	/// <summary>
	/// [EXT:online-coop-undo] The paint or erase mode's Backspace of a guest in a shared park (C5-2): the take-back of its newest own stroke goes behind the strokes' cells, one at a time (a
	/// press while one waits asks the player to wait), and names the stroke when it goes.
	/// </summary>
	private bool QueueGuestStrokeUndo()
	{
		if ( unsentBatches.Concat( sentBatches ).Any( batch => batch.Stroke.Write == GuestWrite.StrokeUndo ) )
		{
			NoteWaiting?.Invoke();
			return false;
		}
		var stroke = new GuestStroke( GuestWrite.StrokeUndo, IsErasing, IsRiver, null, 0 ) { Released = true, Session = paintSession, Run = toolRun };
		var batch = new GuestBatch( stroke, first: true );
		stroke.Batches.Add( batch );
		unsentBatches.Add( batch );
		return true;
	}

	/// <summary>The slide mode's Backspace of a guest: the undo goes behind the stroke's cells, one at a time (a press while one waits asks the player to wait).</summary>
	private bool QueueGuestSlideUndo( (int X, int Y) tower )
	{
		if ( unsentBatches.Concat( sentBatches ).Any( batch => batch.Stroke.Write == GuestWrite.SlideUndo ) )
		{
			NoteWaiting?.Invoke();
			return false;
		}
		var stroke = new GuestStroke( GuestWrite.SlideUndo, false, false, tower, 0 ) { Released = true, Session = slideSession };
		var batch = new GuestBatch( stroke, first: true );
		stroke.Batches.Add( batch );
		unsentBatches.Add( batch );
		return true;
	}

	/// <summary>
	/// Sends what the guest's strokes have waiting, from the queue's head: while fewer than <see cref="MaximumOpenRequests"/> pool requests are open, a press's first batch at once and any other
	/// at most every <see cref="SendSeconds"/> of <paramref name="now"/> (the HUD's clock). The HUD calls it every frame.
	/// </summary>
	public void SendGuestBatches( double now )
	{
		while ( unsentBatches.Count > 0 )
		{
			var batch = unsentBatches[0];
			if ( (OpenRequests?.Invoke() ?? 0) >= MaximumOpenRequests || (!batch.First && now - lastGuestSend < SendSeconds) )
				return;
			// [EXT:online-coop-undo] a slide's take-back waits for the pieces sent before it, so it names the newest piece the tool laid (C5), and a stroke's for the cells sent before it (C5-2)
			if ( batch.Stroke.Write == GuestWrite.SlideUndo && sentBatches.Any( item => item.Stroke.Write == GuestWrite.Slide ) )
				return;
			if ( batch.Stroke.Write == GuestWrite.StrokeUndo && sentBatches.Any( item => item.Stroke.Write == GuestWrite.Cells ) )
				return;
			// [EXT:online-coop-undo] in a shared park a stroke's later batches wait for the answer of the batch that opens it, whose record names the stroke (applied: accepted, or refused by
			// the park); with no record in it (the host could not take it), the next batch opens the stroke again (C5-2)
			if ( batch.Stroke is { Write: GuestWrite.Cells, Name: null } opening && TakesBack?.Invoke() == true )
			{
				if ( opening.Opening?.Request is { } sent )
				{
					if ( sent.IsOpen )
						return;
					if ( sent.State == CoopGuestRequestState.Applied )
						opening.Name = sent.Record;
				}
				if ( opening.Name == null )
				{
					batch.Opens = true;
					opening.Opening = batch;
				}
			}
			unsentBatches.RemoveAt( 0 );
			var stroke = batch.Stroke;
			if ( stroke.Write == GuestWrite.SlideUndo )
			{
				// with no piece of its own on the slide, or on a slide finished since (its pieces go only with its tower), Backspace takes nothing back, quietly, as the host's
				var tower = stroke.Tower!.Value;
				if ( NewestOwnPiece( tower ) is not { } piece || Slides?.Find( PoolSlide.KeyOf( tower.X, tower.Y ) ) is { IsComplete: true } )
				{
					batch.Resolved = true;
					TryFinishGuestStroke( stroke );
					continue;
				}
				batch.Piece = piece;
			}
			if ( stroke.Write == GuestWrite.StrokeUndo )
			{
				// with no stroke of its own that wrote something, Backspace takes nothing back, quietly, as the host's with none
				if ( ownStrokes.Count == 0 )
				{
					batch.Resolved = true;
					TryFinishGuestStroke( stroke );
					continue;
				}
				batch.StrokeName = ownStrokes[^1];
			}
			lastGuestSend = now;
			batch.Command = GuestCommand( batch );
			ParkCommandResult result;
			SendingGuestBatch = true;
			try
			{
				result = issue( batch.Command );
			}
			finally
			{
				SendingGuestBatch = false;
			}
			if ( result.Request is { } request )
			{
				batch.Request = request;
				sentBatches.Add( batch );
				continue;
			}
			// the funnel refused it here (the guest's own rights: the grant went meanwhile): the stroke takes and sends nothing more, and its report says why
			batch.Local = result;
			batch.Resolved = true;
			stroke.Stopped = true;
			DropUnsent( stroke );
			TryFinishGuestStroke( stroke );
		}
	}

	private static ParkCommand GuestCommand( GuestBatch batch ) => batch.Stroke.Write switch
	{
		GuestWrite.Slide => new LayPoolSlidePiecesCommand( batch.Stroke.Tower!.Value.X, batch.Stroke.Tower.Value.Y, batch.Cells.ToArray() ),
		GuestWrite.Bridge => new PlacePoolBridgeCommand( batch.Cells[0].X, batch.Cells[0].Y, batch.Stroke.Direction ),
		// [EXT:online-coop-undo] a take-back of the tool's own newest piece, which the host's journal decides (C5), or of its own newest stroke (C5-2)
		GuestWrite.SlideUndo => new TakeBackCommand( false, batch.Piece!.Value.Record, batch.Piece.Value.Part, -1, -1 ),
		GuestWrite.StrokeUndo => new TakeBackCommand( true, batch.StrokeName!.Value, -1, -1, -1 ),
		_ => new PoolCellsCommand( batch.Stroke.Erase, batch.Stroke.River, batch.Cells.ToArray() ) { First = batch.Opens }
	};

	/// <summary>
	/// A request of the guest resolved (the HUD's drain, outside any command): true when it is one of the strokes' batches, whose stroke may then report. An outcome that finished its slide
	/// drops the stroke's unsent cells.
	/// </summary>
	internal bool CompleteGuestRequest( CoopGuestCommands.Request request )
	{
		var batch = sentBatches.FirstOrDefault( item => ReferenceEquals( item.Request, request ) );
		if ( batch == null )
			return false;
		sentBatches.Remove( batch );
		batch.Resolved = true;
		if ( request.Result?.Value is PoolSlideBatch { Finished: true } )
		{
			batch.Stroke.Finished = true;
			DropUnsent( batch.Stroke );
		}
		// [EXT:online-coop-undo] the pieces a batch laid are the tool's own, each by its record and ordinal, while the slide mode it was drawn in is on (a late outcome of an earlier one adds
		// none to a new one's); a taken take-back's piece is no longer (C5)
		if ( batch.Stroke is { Write: GuestWrite.Slide, Tower: { } tower } stroke && InSlideSession( stroke.Session ) && request is { Accepted: true, Result.Value: PoolSlideBatch laid } )
		{
			var count = laid.Writes.Count( write => write.Result == PoolSlideResult.Ok );
			for ( var ordinal = 0; ordinal < count; ordinal++ )
				ownPieces.Add( (tower, request.Record, ordinal) );
		}
		else if ( batch is { Stroke.Write: GuestWrite.SlideUndo, Piece: { } taken } && request.Accepted )
			ownPieces.RemoveAll( piece => piece.Record == taken.Record && piece.Part == taken.Part );
		// [EXT:online-coop-undo] a batch that opened its stroke names it by its record when the host applied it, accepted or refused by the park; a stroke that wrote is the tool's own while
		// the tool's run it was drawn in goes on; a taken take-back's stroke is no longer (C5-2)
		if ( batch.Stroke.Write == GuestWrite.Cells )
		{
			if ( batch.Opens && request.State == CoopGuestRequestState.Applied )
				batch.Stroke.Name = request.Record;
			if ( batch.Stroke.Name is { } name && batch.Stroke.Run == toolRun && request is { Accepted: true, Result.Value: PoolCellWrite[] } && (ownStrokes.Count == 0 || ownStrokes[^1] != name) )
				ownStrokes.Add( name );
		}
		else if ( batch is { Stroke.Write: GuestWrite.StrokeUndo, StrokeName: { } takenStroke } && request.Accepted )
			ownStrokes.Remove( takenStroke );
		TryFinishGuestStroke( batch.Stroke );
		return true;
	}

	/// <summary>The reports of the guest's strokes that finished since the last call, oldest first.</summary>
	public IReadOnlyList<PoolGuestReport> TakeGuestReports()
	{
		if ( guestReports.Count == 0 )
			return Array.Empty<PoolGuestReport>();
		var taken = guestReports.ToArray();
		guestReports.Clear();
		return taken;
	}

	/// <summary>Drops the unsent batches of <paramref name="stroke"/>: they count as resolved, with nothing sent.</summary>
	private void DropUnsent( GuestStroke stroke )
	{
		foreach ( var batch in unsentBatches.Where( item => ReferenceEquals( item.Stroke, stroke ) ).ToArray() )
		{
			unsentBatches.Remove( batch );
			batch.Resolved = true;
		}
	}

	/// <summary>The guest's stroke under the press, if any, reports nothing: Escape, a right click or another tool ended it, as the host's cancelled stroke reports nothing.</summary>
	private void DiscardGuestStroke()
	{
		if ( guestStroke != null )
			guestStroke.Discarded = true;
	}

	/// <summary>Escape, a right click or another tool: the unsent batches go; a stroke that lost any reports nothing, and its sent requests are orphans of the HUD's drain.</summary>
	private void DiscardUnsentGuestBatches()
	{
		foreach ( var batch in unsentBatches )
		{
			batch.Resolved = true;
			batch.Stroke.Discarded = true;
		}
		unsentBatches.Clear();
		sentBatches.RemoveAll( batch => batch.Stroke.Discarded );
		if ( guestStroke is { Discarded: true } )
			guestStroke = null;
	}

	/// <summary>[EXT:online-coop-commands] The session ended (<see cref="Level.EndGuestWaits"/>): the tool ends, and the guest's strokes go without a report.</summary>
	internal void EndGuestStrokes()
	{
		Cancel();
		unsentBatches.Clear();
		sentBatches.Clear();
		guestReports.Clear();
		guestStroke = null;
	}

	/// <summary>Reports <paramref name="stroke"/> once it is released and each of its batches is back or dropped: what it wrote, its first refusal in the order its cells were tried, its notes.</summary>
	private void TryFinishGuestStroke( GuestStroke stroke )
	{
		if ( !stroke.Released || stroke.Discarded || stroke.Batches.Any( batch => !batch.Resolved ) )
			return;
		var wrote = false;
		// a batch of this stroke laid the splash: it laid a piece, and the slide was finished after it (a batch applies whole, so its last piece finished it)
		var laidSplash = false;
		((int X, int Y) Cell, PoolBuildResult Reason)? refusal = null;
		PoolSlideResult? slideRefusal = null;
		PoolBridgeResult? bridgeRefusal = null;
		ParkCommand? rightsRefused = null;
		string? rightsReason = null;
		string? takeBackRefusal = null;
		var notTaken = false;
		var unknown = false;
		foreach ( var batch in stroke.Batches )
		{
			if ( batch.Request is not { } request )
			{
				// refused here before it went: by the rights (said once for the stroke, the session's own note leaves the pool tool's to it; the HUD says nothing for a slide undo that met a
				// finished slide) or because no request could go
				if ( batch.Local is { RightsRefused: true } local )
				{
					if ( rightsRefused == null )
						(rightsRefused, rightsReason) = (batch.Command, local.Reason);
				}
				else if ( batch.Local != null )
					notTaken = true;
				continue;
			}
			switch ( request.State )
			{
				case CoopGuestRequestState.Applied when request.Result is { Error: null } result:
					if ( result.RightsRefused )
					{
						if ( rightsRefused == null )
							(rightsRefused, rightsReason) = (request.Command, result.Reason);
						break;
					}
					// [EXT:online-coop-undo] a slide's take-back the journal refused says why, but one that met a finished slide says nothing, as the host's Backspace there (C5); a stroke's says
					// every verdict (C5-2)
					if ( request.Command is TakeBackCommand && !result.Accepted && TakeBackCommand.VerdictOf( result.Reason ) is { } verdict
						&& (verdict != TakeBackVerdict.InTheWay || stroke.Write == GuestWrite.StrokeUndo) )
						takeBackRefusal = result.Reason;
					switch ( result.Value )
					{
						case PoolCellWrite[] writes:
							for ( var index = 0; index < writes.Length && index < batch.Cells.Count; index++ )
							{
								if ( writes[index].Result == PoolBuildResult.Ok )
									wrote = true;
								else if ( writes[index].Result is not (PoolBuildResult.RiverMarked or PoolBuildResult.Existing) )
									refusal ??= (batch.Cells[index], writes[index].Result);
							}
							break;
						case PoolSlideBatch slide:
							for ( var index = 0; index < slide.Writes.Count && index < batch.Cells.Count; index++ )
							{
								if ( slide.Writes[index].Result == PoolSlideResult.Ok )
									wrote = true;
								else
								{
									refusal ??= (batch.Cells[index], Map( slide.Writes[index].Result ));
									slideRefusal ??= slide.Writes[index].Result;
								}
							}
							laidSplash |= slide.Finished && slide.Writes.Any( write => write.Result == PoolSlideResult.Ok );
							break;
						case PoolBridgeResult bridge when stroke.Write == GuestWrite.Bridge:
							if ( bridge == PoolBridgeResult.Ok )
								wrote = true;
							else
							{
								refusal ??= (batch.Cells[0], Map( bridge ));
								bridgeRefusal ??= bridge;
							}
							break;
					}
					break;
				case CoopGuestRequestState.Applied:
				case CoopGuestRequestState.Unknown:
					unknown = true;
					break;
				case CoopGuestRequestState.Refused:
					notTaken = true;
					break;
			}
		}
		// done when this stroke laid the splash and the slide still stands finished now (a later record may have taken its tower away), as the host's release reads the slide at once
		var done = stroke.Write == GuestWrite.Slide && laidSplash && stroke.Tower is { } laidTower && Slides?.Find( PoolSlide.KeyOf( laidTower.X, laidTower.Y ) ) is { IsComplete: true };
		// a slide this stroke finished ends the slide mode it is still in, as the host's release ends it
		if ( done && slideTower is { } tower && tower == stroke.Tower )
		{
			SlideCompleted = true;
			IsActive = false;
			slideTower = null;
			HoverCell = null;
			HoverResult = null;
			strokes.Clear();
			Ended?.Invoke();
		}
		// a slide or stroke undo plays nothing and says only its notes, as the host's Backspace says nothing
		if ( stroke.Write is GuestWrite.SlideUndo or GuestWrite.StrokeUndo )
			(wrote, refusal) = (false, null);
		guestReports.Add( new PoolGuestReport( wrote, refusal, slideRefusal, bridgeRefusal, done, rightsRefused, rightsReason, notTaken, unknown )
		{
			TakeBackRefusal = takeBackRefusal,
			SlideSession = stroke.Write == GuestWrite.StrokeUndo ? 0 : stroke.Session,
			PaintSession = stroke.Write == GuestWrite.StrokeUndo ? stroke.Session : 0
		} );
	}
}
