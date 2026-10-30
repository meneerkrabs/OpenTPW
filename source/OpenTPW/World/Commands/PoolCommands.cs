namespace OpenTPW;

/// <summary>What one write of the pool tool did to a cell (<see cref="PaintPoolCellCommand"/>, <see cref="ErasePoolCellCommand"/>): the builder's verdict and the money it moved (a refund is negative).</summary>
public readonly record struct PoolCellWrite( PoolBuildResult Result, long Money );

/// <summary>What one piece of the slide tool did (<see cref="LayPoolSlidePieceCommand"/>): the slide world's verdict and the piece's charge.</summary>
public readonly record struct PoolSlideWrite( PoolSlideResult Result, long Money );

/// <summary>
/// [EXT:online-coop-commands] A command of the pools extension's tool (<see cref="PoolTool"/>, docs/POOLS.md): it acts on the level's pool builder, slides and bridges; a park without them (a visit)
/// refuses it, and the builders refuse every write while the extension is off.
/// </summary>
public abstract record PoolToolCommand : ParkToolCommand
{
	internal sealed override ParkCommandResult Execute( Level level ) => Apply( level.PoolBuilder, level.PoolSlides, level.PoolBridges );

	/// <summary>Runs the command on the pool builder, the slides and the bridges: accepted, or rejected without a change.</summary>
	internal abstract ParkCommandResult Apply( PoolBuilder? builder, PoolSlideWorld? slides, PoolBridgeWorld? bridges );

	/// <summary>Applies a pool command straight to a pool builder, slides and bridges, outside a level's command funnel (the tool's tests); any other command is refused.</summary>
	public static ParkCommandResult ApplyTo( ParkCommand command, PoolBuilder? builder, PoolSlideWorld? slides, PoolBridgeWorld? bridges ) =>
		command is PoolToolCommand pool ? pool.Apply( builder, slides, bridges ) : ParkCommandResult.Rejected( $"{command.Kind} is no pool command." );

	private protected static ParkCommandResult NoPools => ParkCommandResult.Rejected( "The park builds no pools." );
}

/// <summary>
/// [EXT:online-coop-undo] A paint or erase command of a pool stroke, which a shared park's undo journal gathers into its strokes (C5-2): the first command of a stroke says so
/// (<see cref="First"/>, its own codec generation, <see cref="ParkCommandCodec.StrokeVersion"/>), and the journal names the stroke by its record.
/// </summary>
public interface IPoolStrokeCommand
{
	/// <summary>Whether the command opens a stroke: the host's first cell of a press, a guest's first batch. Only a shared park's tools set it.</summary>
	bool First { get; }

	/// <summary>Whether it erases (else it paints).</summary>
	bool Erases { get; }

	/// <summary>Whether it paints with the river option.</summary>
	bool PaintsRiver { get; }

	/// <summary>The same command as a stroke's first.</summary>
	ParkCommand AsFirst();
}

/// <summary>
/// [EXT:pools-tool] One cell of a paint stroke (<see cref="PoolTool"/>: the press and each cell a drag crosses): the cell becomes pool water and is charged, or, with <paramref name="River"/>, a pool cell already
/// there takes the river flag. The value is the write (<see cref="PoolCellWrite"/>), accepted or not.
/// </summary>
public sealed record PaintPoolCellCommand( int X, int Y, bool River ) : PoolToolCommand, IPoolStrokeCommand
{
	/// <inheritdoc/>
	public bool First { get; init; }
	bool IPoolStrokeCommand.Erases => false;
	bool IPoolStrokeCommand.PaintsRiver => River;
	ParkCommand IPoolStrokeCommand.AsFirst() => this with { First = true };

	public override ParkCommandKind Kind => ParkCommandKind.PaintPoolCell;
	internal override bool IsWellFormed => IsCell( X, Y );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( X );
		writer.Int( Y );
		writer.Bool( River );
	}

	internal override ParkCommandResult Apply( PoolBuilder? builder, PoolSlideWorld? slides, PoolBridgeWorld? bridges )
	{
		if ( builder == null )
			return NoPools;
		var result = builder.Paint( X, Y, River );
		var write = new PoolCellWrite( result, result == PoolBuildResult.Ok ? builder.LastCharged : 0 );
		return result is PoolBuildResult.Ok or PoolBuildResult.RiverMarked ? ParkCommandResult.Ok( write ) : new ParkCommandResult( false, $"The pool cell is not painted: {result}.", write );
	}
}

/// <summary>[EXT:pools-tool] One cell of an erase stroke: a pool cell becomes an empty cell again and is refunded. The value is the write (<see cref="PoolCellWrite"/>), accepted or not.</summary>
public sealed record ErasePoolCellCommand( int X, int Y ) : PoolToolCommand, IPoolStrokeCommand
{
	/// <inheritdoc/>
	public bool First { get; init; }
	bool IPoolStrokeCommand.Erases => true;
	bool IPoolStrokeCommand.PaintsRiver => false;
	ParkCommand IPoolStrokeCommand.AsFirst() => this with { First = true };

	public override ParkCommandKind Kind => ParkCommandKind.ErasePoolCell;
	internal override bool IsWellFormed => IsCell( X, Y );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( X );
		writer.Int( Y );
	}

	internal override ParkCommandResult Apply( PoolBuilder? builder, PoolSlideWorld? slides, PoolBridgeWorld? bridges )
	{
		if ( builder == null )
			return NoPools;
		var result = builder.Erase( X, Y );
		var write = new PoolCellWrite( result, result == PoolBuildResult.Ok ? builder.LastCharged : 0 );
		return result == PoolBuildResult.Ok ? ParkCommandResult.Ok( write ) : new ParkCommandResult( false, $"The pool cell is not erased: {result}.", write );
	}
}

/// <summary>
/// [EXT:pools-tool] Backspace in the pool tool: takes back a stroke. An erase stroke's <paramref name="Cells"/> are painted again (each charged again, a cell that cannot be paid for ends it); a paint stroke's
/// cells lose the river flag they took (<paramref name="RiverMarked"/>) and its painted cells are erased again, the last first, each refunded. The value is the undoing stroke (<see cref="PoolStroke"/>).
/// </summary>
public sealed record UndoPoolStrokeCommand( bool Erase, IReadOnlyList<(int X, int Y)> Cells, IReadOnlyList<(int X, int Y)> RiverMarked ) : PoolToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.UndoPoolStroke;
	internal override bool IsWellFormed => Cells.Count <= ParkCommandCodec.MaximumCells && RiverMarked.Count <= ParkCommandCodec.MaximumCells && !(Erase && RiverMarked.Count > 0)
		&& Cells.All( cell => IsCell( cell.X, cell.Y ) ) && RiverMarked.All( cell => IsCell( cell.X, cell.Y ) );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Bool( Erase );
		writer.Cells( Cells );
		writer.Cells( RiverMarked );
	}

	internal override ParkCommandResult Apply( PoolBuilder? builder, PoolSlideWorld? slides, PoolBridgeWorld? bridges )
	{
		if ( builder == null )
			return NoPools;
		if ( Erase )
			return ParkCommandResult.Ok( builder.PaintCells( Cells ) );
		// [EXT:pools-river] cells that only got the river flag lose it; painted cells go (their flag with them)
		foreach ( var (x, y) in RiverMarked )
			if ( builder.Cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater )
				builder.Cells.SetFlags( x, y, builder.Cells.FlagsAt( x, y ) & ~ParkCellFlags.PoolRiver );
		return ParkCommandResult.Ok( builder.EraseCells( Cells.Reverse() ) );
	}

	/// <summary>
	/// The commands that take <paramref name="stroke"/> back: one when both of its lists fit a command (<see cref="ParkCommandCodec.MaximumCells"/>), otherwise chunks that write in the order the one
	/// command would. An erase stroke's cells are painted again in order, a chunk at a time, and the issuer stops after a chunk that ran out of money (<see cref="PoolStroke.RanOutOfMoney"/>); a paint stroke's
	/// river marks go first, then its cells from the end, each chunk erased last first.
	/// </summary>
	public static IEnumerable<UndoPoolStrokeCommand> For( PoolStroke stroke )
	{
		ArgumentNullException.ThrowIfNull( stroke );
		var size = ParkCommandCodec.MaximumCells;
		var none = Array.Empty<(int X, int Y)>();
		if ( stroke.Cells.Count <= size && stroke.RiverMarked.Count <= size )
		{
			yield return new UndoPoolStrokeCommand( stroke.Erase, stroke.Cells, stroke.RiverMarked );
			yield break;
		}
		if ( stroke.Erase )
		{
			for ( var start = 0; start < stroke.Cells.Count; start += size )
				yield return new UndoPoolStrokeCommand( true, stroke.Cells.Skip( start ).Take( size ).ToArray(), none );
			yield break;
		}
		for ( var start = 0; start < stroke.RiverMarked.Count; start += size )
			yield return new UndoPoolStrokeCommand( false, none, stroke.RiverMarked.Skip( start ).Take( size ).ToArray() );
		for ( var end = stroke.Cells.Count; end > 0; end -= size )
			yield return new UndoPoolStrokeCommand( false, stroke.Cells.Skip( Math.Max( 0, end - size ) ).Take( end - Math.Max( 0, end - size ) ).ToArray(), none );
	}

	/// <summary>Equal when every field and both cell lists are equal (a record compares lists by reference).</summary>
	public bool Equals( UndoPoolStrokeCommand? other ) => other is not null && Player == other.Player && Erase == other.Erase && Cells.SequenceEqual( other.Cells )
		&& RiverMarked.SequenceEqual( other.RiverMarked );

	public override int GetHashCode() => (Erase ? 1 : 0) ^ Cells.Count ^ (RiverMarked.Count << 8);
}

/// <summary>
/// [EXT:pools-slides] One piece of a slide (the slide mode's press and each cell its drag crosses): the next piece of the slide of the tower anchored at (<paramref name="TowerX"/>, <paramref name="TowerY"/>)
/// is laid on (<paramref name="X"/>, <paramref name="Y"/>) and charged. The value is the write (<see cref="PoolSlideWrite"/>), accepted or not.
/// </summary>
public sealed record LayPoolSlidePieceCommand( int TowerX, int TowerY, int X, int Y ) : PoolToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.LayPoolSlidePiece;
	internal override bool IsWellFormed => IsCell( TowerX, TowerY ) && IsCell( X, Y );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( TowerX );
		writer.Int( TowerY );
		writer.Int( X );
		writer.Int( Y );
	}

	internal override ParkCommandResult Apply( PoolBuilder? builder, PoolSlideWorld? slides, PoolBridgeWorld? bridges )
	{
		if ( slides == null )
			return NoPools;
		var result = slides.AddCell( TowerX, TowerY, X, Y );
		var write = new PoolSlideWrite( result, result == PoolSlideResult.Ok ? slides.LastCharged : 0 );
		return result == PoolSlideResult.Ok ? ParkCommandResult.Ok( write ) : new ParkCommandResult( false, $"The slide piece is not laid: {result}.", write );
	}
}

/// <summary>[EXT:pools-slides] Backspace in the slide mode: the last piece of the slide of the tower anchored at (<paramref name="TowerX"/>, <paramref name="TowerY"/>) is taken back and refunded.</summary>
public sealed record UndoPoolSlidePieceCommand( int TowerX, int TowerY ) : PoolToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.UndoPoolSlidePiece;
	internal override bool IsWellFormed => IsCell( TowerX, TowerY );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( TowerX );
		writer.Int( TowerY );
	}

	internal override ParkCommandResult Apply( PoolBuilder? builder, PoolSlideWorld? slides, PoolBridgeWorld? bridges ) =>
		slides?.RemoveLastPiece( TowerX, TowerY ) == true ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( "No slide piece to take back." );
}

/// <summary>[EXT:pools-bridges] The bridge mode's click: a bridge from the approach cell (<paramref name="X"/>, <paramref name="Y"/>) in <paramref name="Direction"/> (0 to 3) is built and charged. The value is the verdict.</summary>
public sealed record PlacePoolBridgeCommand( int X, int Y, int Direction ) : PoolToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.PlacePoolBridge;
	internal override bool IsWellFormed => IsCell( X, Y ) && Direction is >= 0 and <= 3;
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( X );
		writer.Int( Y );
		writer.Int( Direction );
	}

	internal override ParkCommandResult Apply( PoolBuilder? builder, PoolSlideWorld? slides, PoolBridgeWorld? bridges )
	{
		if ( bridges == null )
			return NoPools;
		var result = bridges.TryAdd( X, Y, Direction );
		return new ParkCommandResult( result == PoolBridgeResult.Ok, result == PoolBridgeResult.Ok ? null : $"The bridge is not built: {result}.", result );
	}
}

/// <summary>[EXT:pools-bridges] The bridge removal mode's click: the bridge over (<paramref name="X"/>, <paramref name="Y"/>) is taken down and refunded. The value is the verdict.</summary>
public sealed record RemovePoolBridgeCommand( int X, int Y ) : PoolToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.RemovePoolBridge;
	internal override bool IsWellFormed => IsCell( X, Y );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( X );
		writer.Int( Y );
	}

	internal override ParkCommandResult Apply( PoolBuilder? builder, PoolSlideWorld? slides, PoolBridgeWorld? bridges )
	{
		if ( bridges == null )
			return NoPools;
		var result = bridges.RemoveAt( X, Y );
		return new ParkCommandResult( result == PoolBridgeResult.Ok, result == PoolBridgeResult.Ok ? null : $"The bridge is not removed: {result}.", result );
	}
}

/// <summary>
/// [EXT:online-coop-commands] A guest's batch of a paint or erase stroke (docs/COOP.md, "A guest's pools"): a guest's tool sends the cells its stroke crosses in batches, since one request a
/// cell would outrun the session's request bucket. Each cell is written in order exactly as <see cref="PaintPoolCellCommand"/> or <see cref="ErasePoolCellCommand"/> writes it, every cell
/// tried as the host's tool tries them (no stop at a cell that cannot be paid for). The value is a <see cref="PoolCellWrite"/> per cell; accepted when a cell changed.
/// </summary>
public sealed record PoolCellsCommand( bool Erase, bool River, IReadOnlyList<(int X, int Y)> Cells ) : PoolToolCommand, IPoolStrokeCommand
{
	/// <inheritdoc/>
	public bool First { get; init; }
	bool IPoolStrokeCommand.Erases => Erase;
	bool IPoolStrokeCommand.PaintsRiver => River;
	ParkCommand IPoolStrokeCommand.AsFirst() => this with { First = true };

	/// <summary>The most cells a batch carries: 200 cells of a map up to 256 cells across take at most 800 bytes, within the session's 1 KiB request.</summary>
	public const int MaximumCells = 200;

	public override ParkCommandKind Kind => ParkCommandKind.PoolCells;
	internal override bool IsWellFormed => Cells.Count is > 0 and <= MaximumCells && !(Erase && River) && Cells.All( cell => IsCell( cell.X, cell.Y ) );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Bool( Erase );
		writer.Bool( River );
		writer.Cells( Cells );
	}

	internal override ParkCommandResult Apply( PoolBuilder? builder, PoolSlideWorld? slides, PoolBridgeWorld? bridges )
	{
		if ( builder == null )
			return NoPools;
		var writes = new PoolCellWrite[Cells.Count];
		for ( var index = 0; index < Cells.Count; index++ )
		{
			var (x, y) = Cells[index];
			var result = Erase ? builder.Erase( x, y ) : builder.Paint( x, y, River );
			writes[index] = new PoolCellWrite( result, result == PoolBuildResult.Ok ? builder.LastCharged : 0 );
		}
		if ( writes.Any( write => write.Result is PoolBuildResult.Ok or PoolBuildResult.RiverMarked ) )
			return ParkCommandResult.Ok( writes );
		var refused = writes.Select( write => write.Result ).FirstOrDefault( result => result != PoolBuildResult.Existing, PoolBuildResult.Existing );
		return new ParkCommandResult( false, refused == PoolBuildResult.Existing ? "No pool cell changed." : $"No pool cell is {(Erase ? "erased" : "painted")}: {refused}.", writes );
	}

	/// <summary>Equal when every field and the cells are equal (a record compares lists by reference).</summary>
	public bool Equals( PoolCellsCommand? other ) => other is not null && Player == other.Player && Erase == other.Erase && River == other.River && First == other.First
		&& Cells.SequenceEqual( other.Cells );

	public override int GetHashCode() => (Erase ? 1 : 0) ^ (River ? 2 : 0) ^ (First ? 4 : 0) ^ (Cells.Count << 3);
}

/// <summary>[EXT:online-coop-commands] What a guest's batch of slide pieces did (<see cref="LayPoolSlidePiecesCommand"/>): a write per cell tried, and whether the slide is finished after it.</summary>
public sealed record PoolSlideBatch( IReadOnlyList<PoolSlideWrite> Writes, bool Finished );

/// <summary>
/// [EXT:online-coop-commands] A guest's batch of a slide stroke: each cell as <see cref="LayPoolSlidePieceCommand"/> lays it, in order, for the tower anchored at (<paramref name="TowerX"/>,
/// <paramref name="TowerY"/>). Once the slide is finished (its splash piece over the pool) no further cell is tried, and a batch that meets a finished slide tries none: neither is a refusal,
/// as the host's tool writes nothing more once its slide is finished. The value is a <see cref="PoolSlideBatch"/>; accepted when a piece was laid.
/// </summary>
public sealed record LayPoolSlidePiecesCommand( int TowerX, int TowerY, IReadOnlyList<(int X, int Y)> Cells ) : PoolToolCommand
{
	public override ParkCommandKind Kind => ParkCommandKind.LayPoolSlidePieces;
	internal override bool IsWellFormed => IsCell( TowerX, TowerY ) && Cells.Count is > 0 and <= PoolCellsCommand.MaximumCells && Cells.All( cell => IsCell( cell.X, cell.Y ) );
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( TowerX );
		writer.Int( TowerY );
		writer.Cells( Cells );
	}

	internal override ParkCommandResult Apply( PoolBuilder? builder, PoolSlideWorld? slides, PoolBridgeWorld? bridges )
	{
		if ( slides == null )
			return NoPools;
		var writes = new List<PoolSlideWrite>();
		foreach ( var (x, y) in Cells )
		{
			if ( Finished( slides ) )
				break;
			var result = slides.AddCell( TowerX, TowerY, x, y );
			writes.Add( new PoolSlideWrite( result, result == PoolSlideResult.Ok ? slides.LastCharged : 0 ) );
		}
		var batch = new PoolSlideBatch( writes, Finished( slides ) );
		if ( writes.Any( write => write.Result == PoolSlideResult.Ok ) )
			return ParkCommandResult.Ok( batch );
		return new ParkCommandResult( false, writes.Count == 0 ? "The slide is finished." : $"No slide piece is laid: {writes[0].Result}.", batch );
	}

	private bool Finished( PoolSlideWorld slides ) => slides.Find( PoolSlide.KeyOf( TowerX, TowerY ) ) is { IsComplete: true };

	/// <summary>Equal when every field and the cells are equal (a record compares lists by reference).</summary>
	public bool Equals( LayPoolSlidePiecesCommand? other ) => other is not null && Player == other.Player && TowerX == other.TowerX && TowerY == other.TowerY && Cells.SequenceEqual( other.Cells );

	public override int GetHashCode() => TowerX ^ (TowerY << 8) ^ (Cells.Count << 16);
}

/// <summary>
/// [EXT:pools-waves] The wave machine settings window's tick: the machine whose attraction id is <paramref name="Attraction"/> runs waves (<paramref name="On"/>) of <paramref name="Height"/> 1 to 3 with a rest of
/// <paramref name="Interval"/> 0 to 2 (<see cref="Level.SetPoolWaves"/>). An unchanged choice writes nothing, so a machine still running the default keeps its cell as it was.
/// </summary>
public sealed record SetPoolWavesCommand( int Attraction, bool On, int Height, int Interval ) : ParkToolCommand
{
	/// <summary>The refusal of a machine that is gone or has no water beside it, which a guest's queue says as no longer there ([EXT:online-coop-commands] C5-2).</summary>
	internal const string NoMachineReason = "The wave machine is gone or has no water beside it.";
	/// <summary>The refusal of settings the machine has already (another player set the same meanwhile), which says nothing.</summary>
	internal const string UnchangedReason = "The wave settings are unchanged.";

	public override ParkCommandKind Kind => ParkCommandKind.SetPoolWaves;
	internal override bool IsWellFormed => Attraction >= 0 && Height is >= 1 and <= 3 && Interval is >= 0 and <= 2;
	internal override void WriteFields( ParkCommandWriter writer )
	{
		writer.Int( Attraction );
		writer.Bool( On );
		writer.Int( Height );
		writer.Int( Interval );
	}

	internal override ParkCommandResult Execute( Level level )
	{
		if ( level.ResolveCommandObject( Attraction ) is not { } item || level.PoolWavesOf( item ) is not { } current )
			return ParkCommandResult.Rejected( NoMachineReason );
		var settings = new PoolWaveSettings( On, Height, Interval );
		if ( current == settings )
			return ParkCommandResult.Rejected( UnchangedReason );
		return level.SetPoolWaves( item, settings ) ? ParkCommandResult.Ok( settings ) : ParkCommandResult.Rejected( $"Attraction {Attraction} takes no wave settings." );
	}
}
