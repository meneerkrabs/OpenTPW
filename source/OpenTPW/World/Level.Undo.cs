namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-undo] The predictions of a take-back's inverse (docs/COOP.md, "Conflicts and undo (C5)"): the host runs the step's own undo on a disposable copy of the cells
/// (<see cref="ParkCellMap.CopyForPrediction"/>) before it decides, and takes the step back only when the undo would do exactly the step's work back: every cell of its footprint taken back
/// as the step left it, nothing standing in the way, and nothing else changed but the links of the cells beside them. The copy's writes follow the undo's own code in order, so the
/// prediction and the take-back cannot differ.
/// </summary>
public sealed partial class Level
{
	/// <summary>[EXT:online-coop-undo] The host's undo journal while this park is a session's host (C5), set by its stream; null otherwise.</summary>
	internal CoopUndoJournal? UndoJournal { get; set; }

	/// <summary>
	/// Whether the path line <paramref name="segment"/> laid can be taken back exactly now (its built cells cleared, its crossed cells counted down by one), with the cells the undo would
	/// change in <paramref name="trace"/>.
	/// </summary>
	internal bool PredictPathLineUndo( SegmentResult segment, HashSet<(int X, int Y)> trace )
	{
		if ( Guests?.Grid.Cells is not { } cells || Objects == null )
			return false;
		var footprint = segment.Built.Concat( segment.Existing ).ToArray();
		// the step still there: every cell of it a path (a cell another command emptied, even one taken back since, leaves the undo nothing to remove), nothing standing on a cell it clears
		// (a crossed cell is only counted down: a bridge over it stays as it is)
		if ( footprint.Any( cell => cells.TypeAt( cell.X, cell.Y ) != ParkCellType.Path ) || segment.Built.Any( cell => StandsOn( cells, cell ) ) )
			return false;
		var copy = cells.CopyForPrediction();
		copy.Changed = ( x, y, _ ) => trace.Add( (x, y) );
		new ParkPathBuilder( copy, null, (ParkEconomy?)null, Objects.Grid ).Undo( segment );
		return segment.Built.All( cell => copy.RawTypeAt( cell.X, cell.Y ) == 0 )
			&& segment.Existing.All( cell => copy.TypeAt( cell.X, cell.Y ) == ParkCellType.Path && copy.PlacementCountAt( cell.X, cell.Y ) == cells.PlacementCountAt( cell.X, cell.Y ) - 1 )
			&& OnlyLinksBeside( cells, copy, trace, footprint.ToHashSet() );
	}

	/// <summary>
	/// Whether the legacy queue line <paramref name="segment"/> of the ride whose attraction id is <paramref name="ride"/> can be taken back exactly now: the queue's removal from its first built
	/// cell (<see cref="QueuePaths.RemoveFrom"/>, through the cell's own ride as the removal resolves it) would take exactly its built cells, no more and no fewer, with the cells it would
	/// change in <paramref name="trace"/>. The ride's line is walked on a copy (<see cref="RideVisitorBridge.QueueView"/>), so nothing in the park changes.
	/// </summary>
	internal bool PredictQueueLineUndo( int ride, SegmentResult segment, HashSet<(int X, int Y)> trace )
	{
		if ( Guests is not { } guests || segment.Built.Count == 0 )
			return false;
		var cells = guests.Grid.Cells;
		var first = segment.Built[0];
		if ( !guests.Grid.IsQueue( first.X, first.Y ) || cells.FlagsAt( first.X, first.Y ).HasFlag( ParkCellFlags.NoModify ) )
			return false;
		if ( QueueOwnerAt( first.X, first.Y ) is not { Visitors: RideVisitorBridge bridge } owner || owner.Runtime.AttractionId != ride )
			return false;
		var line = bridge.QueueView( guests.Grid ).QueueCells;
		var index = -1;
		for ( var i = 0; i < line.Count; i++ )
		{
			if ( line[i] == first )
				index = i;
		}
		if ( index < 0 )
			return false;
		var removed = line.Skip( index ).ToHashSet();
		if ( !removed.SetEquals( segment.Built ) || removed.Any( cell => StandsOn( cells, cell ) ) )
			return false;
		// the removal's own writes (GuestPathGrid.ClearQueue, then the flag 0x20 off), on the copy
		var copy = cells.CopyForPrediction();
		copy.Changed = ( x, y, _ ) => trace.Add( (x, y) );
		foreach ( var (x, y) in line.Skip( index ) )
		{
			PathLinkWriter.Unlink( copy, x, y, cardinalOnly: true );
			copy.SetType( x, y, ParkCellType.Empty );
			copy.SetFlags( x, y, copy.FlagsAt( x, y ) & ~ParkCellFlags.NoModify );
		}
		return OnlyLinksBeside( cells, copy, trace, removed );
	}

	/// <summary>
	/// Whether the native queue segment from <paramref name="start"/> to <paramref name="end"/> of the ride whose attraction id is <paramref name="ride"/> can be taken back exactly now: the native
	/// undo (<see cref="QueueSegmentReducer.Undo"/>) run on a copy clears each footprint cell the segment created and counts down each it counted up (<paramref name="before"/>: the cells' types
	/// before the segment), skips none, and changes nothing else but the links beside them; a counted-up bend corner it clears instead is allowed only when <paramref name="sharedBend"/> says
	/// it is the member's own earlier segment's. The cells it would change go in <paramref name="trace"/>.
	/// </summary>
	internal bool PredictQueueSegmentUndo( int ride, (int X, int Y) start, (int X, int Y) end, IReadOnlySet<(int X, int Y)> footprint, IReadOnlyDictionary<(int X, int Y), byte> before,
		HashSet<(int X, int Y)> trace, Func<(int X, int Y), bool> sharedBend )
	{
		if ( Guests is not { } guests || ResolveCommandRide( ride ) is not { } bridge )
			return false;
		var cells = guests.Grid.Cells;
		// the step still there: every cell of it a queue cell
		if ( footprint.Any( cell => cells.RawTypeAt( cell.X, cell.Y ) != (byte)ParkCellType.Queue ) )
			return false;
		var copy = cells.CopyForPrediction();
		copy.Changed = ( x, y, _ ) => trace.Add( (x, y) );
		var reducer = new QueueSegmentReducer( copy, bridge.OwnerWord, ( x, y ) => IsReadOnlyVisit || IsQueueBlocked( x, y ), () => true, _ => true,
			() => Park?.Economy.CellCost( CellPurchase.Queue ) ?? 0, _ => { }, () => { }, default, _ => { }, () => null );
		try
		{
			reducer.Undo( new QueueSegmentReceipt( start, end ) );
		}
		catch ( InvalidOperationException )
		{
			return false;
		}
		foreach ( var cell in footprint )
		{
			var cleared = copy.RawTypeAt( cell.X, cell.Y ) == 0;
			// nothing standing on a cell it clears (one it counts down stays as it is)
			if ( cleared && StandsOn( cells, cell ) )
				return false;
			var countedDown = copy.RawTypeAt( cell.X, cell.Y ) == cells.RawTypeAt( cell.X, cell.Y ) && copy.PlacementCountAt( cell.X, cell.Y ) == cells.PlacementCountAt( cell.X, cell.Y ) - 1;
			var created = !before.TryGetValue( cell, out var type ) || type != (byte)ParkCellType.Queue;
			if ( created ? !cleared : !(countedDown || (cleared && sharedBend( cell ))) )
				return false;
		}
		return OnlyLinksBeside( cells, copy, trace, footprint );
	}

	/// <summary>
	/// [EXT:online-coop-undo] Whether a pool stroke's inverse <paramref name="inverse"/> would take the stroke back whole now (C5-2): its own code (<see cref="UndoPoolStrokeCommand"/>) runs on a
	/// copy of the cells with a copy of the bank's balance (<see cref="CellPredictionMoney"/>), the rules it asks bound to the copy where they read cells (the last water cell of an attraction's
	/// water side, the land a slide reserves) and to the park where they read the guests, the staff and the objects, which the inverse does not change. Every erased cell must be painted again,
	/// each meeting the paint's refusals and both money tests in its order against the balance the earlier cells left; or every river mark's flag must come off and every painted cell be erased,
	/// none refused; and nothing outside the stroke's <paramref name="footprint"/> may change.
	/// </summary>
	internal bool PredictPoolStrokeUndo( UndoPoolStrokeCommand inverse, IReadOnlySet<(int X, int Y)> footprint )
	{
		if ( PoolBuilder == null || Guests?.Grid.Cells is not { } cells || Objects == null )
			return false;
		// a river mark the inverse would pass over (no pool cell any more, or no river flag on it) is no exact take-back
		if ( !inverse.Erase && inverse.RiverMarked.Any( cell => cells.RawTypeAt( cell.X, cell.Y ) != ParkCellTypes.PoolWater
			|| !cells.FlagsAt( cell.X, cell.Y ).HasFlag( ParkCellFlags.PoolRiver ) ) )
			return false;
		var copy = cells.CopyForPrediction();
		var trace = new HashSet<(int X, int Y)>();
		copy.Changed = ( x, y, _ ) => trace.Add( (x, y) );
		var predicted = new PoolBuilder( copy, () => null, Objects.Grid, ( x, y ) => IsPathBlockedOn( x, y, copy ), IsStoodOn, ( x, y ) => PoolEraseBlock( x, y, copy ) )
		{
			PredictionMoney = Park?.Economy is { } economy ? CellPredictionMoney.Of( economy, CellPurchase.Pool ) : null
		};
		if ( inverse.Apply( predicted, null, null ).Value is not PoolStroke done || done.FirstRefusal != null || done.RanOutOfMoney || done.Cells.Count != inverse.Cells.Count )
			return false;
		return trace.All( footprint.Contains );
	}

	/// <summary>Whether something stands on <paramref name="cell"/> that no path or queue step lays: an object's footprint, a pool bridge, a slide piece.</summary>
	private bool StandsOn( ParkCellMap cells, (int X, int Y) cell ) =>
		Objects?.FindAt( cell.X, cell.Y ) != null || cells.PoolBridges?.Covers( cell.X, cell.Y ) == true || PoolSlides?.SlideAt( cell.X, cell.Y ) != null;

	/// <summary>Whether every traced cell outside <paramref name="footprint"/> changed on the copy in its links alone (the unlinking of a removed cell's neighbours).</summary>
	private static bool OnlyLinksBeside( ParkCellMap cells, ParkCellMap copy, IEnumerable<(int X, int Y)> trace, IReadOnlySet<(int X, int Y)> footprint ) =>
		trace.Where( cell => !footprint.Contains( cell ) ).All( cell => (cells.FieldsAt( cell.X, cell.Y ) with { Links = 0 }) == (copy.FieldsAt( cell.X, cell.Y ) with { Links = 0 }) );
}
