namespace OpenTPW;

/// <summary>
/// The pool paint tool of the <c>opentpw.pools</c> extension (docs/POOLS.md), CPU-only like <see cref="CellBuildTool"/>: press on a cell and drag to paint pool water over every cell the pointer
/// crosses, release to end the stroke. The erase mode takes pool cells back to empty cells the same way, refunding them. The tool stays on after a stroke so the player can paint
/// another; right click or Escape (<see cref="Cancel"/>) leaves it, Backspace (<see cref="Undo"/>) takes back the last stroke. The HUD and the tests drive it.
/// </summary>
// [EXT:pools-tool] OpenTPW's own tool; the original game has no pools
public sealed class PoolTool
{
	/// <summary>Strokes kept for <see cref="Undo"/>.</summary>
	public const int UndoCapacity = 64;

	private readonly PoolBuilder builder;
	private readonly List<PoolStroke> strokes = new();
	private readonly List<(int X, int Y)> pending = new();
	private (int X, int Y)? last;

	public PoolTool( PoolBuilder builder )
	{
		this.builder = builder ?? throw new ArgumentNullException( nameof( builder ) );
	}

	/// <summary><see cref="CellToolMode.Pool"/> while the tool is on, else <see cref="CellToolMode.None"/>.</summary>
	public CellToolMode Mode => IsActive ? CellToolMode.Pool : CellToolMode.None;
	public bool IsActive { get; private set; }
	/// <summary>True in the erase mode.</summary>
	public bool IsErasing { get; private set; }
	/// <summary>True between a press and its release.</summary>
	public bool IsDragging => last != null;
	public PoolBuilder Builder => builder;
	/// <summary>The cell under the pointer, set by <see cref="Hover"/>.</summary>
	public (int X, int Y)? HoverCell { get; private set; }
	/// <summary>What painting or erasing the hovered cell would do (no writes), or null without a hover.</summary>
	public PoolBuildResult? HoverResult { get; private set; }
	/// <summary>The last stroke that was released.</summary>
	public PoolStroke? LastStroke { get; private set; }
	/// <summary>The first refusal of the stroke in progress or of the last one, kept for the HUD's message.</summary>
	public ((int X, int Y) Cell, PoolBuildResult Reason)? LastRefusal { get; private set; }
	public int StrokeCount => strokes.Count;
	/// <summary>Raised when the tool turns off.</summary>
	public event Action? Ended;

	/// <summary>Turns the tool on in the paint or the erase mode (a mode change keeps the undo stack). False when the extension is off.</summary>
	public bool Begin( bool erase )
	{
		if ( !FeatureGates.Pools )
			return false;
		IsActive = true;
		IsErasing = erase;
		last = null;
		pending.Clear();
		LastRefusal = null;
		return true;
	}

	/// <summary>The cursor moved: the check of the cell under it. While a stroke is on, the cells it crossed are painted as they come.</summary>
	public void Hover( int x, int y )
	{
		if ( !IsActive )
			return;
		HoverCell = (x, y);
		HoverResult = IsErasing ? builder.CheckErase( x, y ) : builder.Check( x, y );
		if ( last != null )
			Drag( x, y );
	}

	/// <summary>The pointer left the park view.</summary>
	public void HoverNone()
	{
		HoverCell = null;
		HoverResult = null;
	}

	/// <summary>The press: starts a stroke on the cell and writes it.</summary>
	public PoolBuildResult? Press( int x, int y )
	{
		if ( !IsActive )
			return null;
		if ( last != null )
			Release();
		last = (x, y);
		pending.Clear();
		LastRefusal = null;
		return Write( x, y );
	}

	/// <summary>The pointer reached another cell during a stroke: every cell between the last one and it is written.</summary>
	public void Drag( int x, int y )
	{
		if ( !IsActive || last is not { } from || from == (x, y) )
			return;
		foreach ( var cell in PoolBuilder.DragLine( from, (x, y) ).Skip( 1 ) )
			Write( cell.X, cell.Y );
		last = (x, y);
	}

	/// <summary>The release: ends the stroke and keeps it for <see cref="Undo"/>. Returns it, or null when the stroke wrote nothing.</summary>
	public PoolStroke? Release()
	{
		if ( last == null )
			return null;
		last = null;
		if ( pending.Count == 0 )
			return null;
		var cells = pending.ToArray();
		pending.Clear();
		var stroke = new PoolStroke( IsErasing, cells,
			IsErasing ? 0 : cells.Length * builder.CellCost, IsErasing && builder.Economy != null ? cells.Length * (long)ParkEconomy.PoolCellCost : 0, LastRefusal );
		strokes.Add( stroke );
		if ( strokes.Count > UndoCapacity )
			strokes.RemoveAt( 0 );
		LastStroke = stroke;
		return stroke;
	}

	/// <summary>
	/// Takes back the last stroke: a paint stroke is erased again (each cell refunded) and an erase stroke painted again (each cell charged again). False when there is none.
	/// A cell that cannot be repainted (the ground changed, no money) stays as it is.
	/// </summary>
	public bool Undo()
	{
		if ( strokes.Count == 0 )
			return false;
		var stroke = strokes[^1];
		strokes.RemoveAt( strokes.Count - 1 );
		if ( stroke.Erase )
			builder.PaintCells( stroke.Cells );
		else
			builder.EraseCells( stroke.Cells.Reverse() );
		last = null;
		return true;
	}

	/// <summary>Right click, Escape or a window opening: turns the tool off without writing (a stroke in progress ends where it is).</summary>
	public void Cancel()
	{
		if ( !IsActive )
			return;
		Release();
		IsActive = false;
		IsErasing = false;
		HoverCell = null;
		HoverResult = null;
		strokes.Clear();
		Ended?.Invoke();
	}

	private PoolBuildResult Write( int x, int y )
	{
		var result = IsErasing ? builder.Erase( x, y ) : builder.Paint( x, y );
		if ( result == PoolBuildResult.Ok )
			pending.Add( (x, y) );
		else if ( result != PoolBuildResult.Existing )
			LastRefusal ??= ((x, y), result);
		return result;
	}
}
