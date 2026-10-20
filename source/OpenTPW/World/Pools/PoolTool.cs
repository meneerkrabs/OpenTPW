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
	private readonly Func<ParkCommand, ParkCommandResult> issue;
	private readonly List<PoolStroke> strokes = new();
	private readonly List<(int X, int Y)> pending = new();
	private readonly List<(int X, int Y)> pendingMarked = new();
	private long pendingMoney;
	private (int X, int Y)? last;
	private (int X, int Y)? slideTower;

	/// <param name="issue">[EXT:online-coop-commands] Hands each write to the park's command funnel: a painted or erased cell, a stroke's undo, a slide piece and a bridge are commands
	/// (<see cref="PoolToolCommand"/>); the strokes, the drag and the undo stack stay the tool's.</param>
	public PoolTool( PoolBuilder builder, Func<ParkCommand, ParkCommandResult> issue )
	{
		this.builder = builder ?? throw new ArgumentNullException( nameof( builder ) );
		this.issue = issue ?? throw new ArgumentNullException( nameof( issue ) );
	}

	/// <summary>[EXT:pools-slides] The slides the tool lays in its slide mode; set by the level.</summary>
	public PoolSlideWorld? Slides { get; set; }
	/// <summary>The buildable pool bridges; set by the level.</summary>
	public PoolBridgeWorld? Bridges { get; set; }
	private bool bridgeMode;
	private int bridgeDirection;
	/// <summary>Bridge mode uses one click per bridge and never paints water.</summary>
	public bool IsBridge => IsActive && bridgeMode;
	public PoolBridgeResult? HoverBridgeResult { get; private set; }
	public PoolBridgeResult? LastBridgeRefusal { get; private set; }
	/// <summary>True in the slide mode (<see cref="BeginSlide"/>): presses lay the pieces of the tower's slide, one cell at a time.</summary>
	public bool IsSlide => IsActive && slideTower != null;
	/// <summary>The anchor of the tower whose slide the tool lays.</summary>
	public (int X, int Y)? SlideTower => slideTower;
	/// <summary>Why the slide mode refused the hovered cell (null when it would take it or outside the slide mode).</summary>
	public PoolSlideResult? HoverSlideResult { get; private set; }
	/// <summary>The first slide refusal of the stroke in progress or of the last one.</summary>
	public PoolSlideResult? LastSlideRefusal { get; private set; }
	/// <summary>True after the slide mode ended because the slide was finished (a splash piece over the pool).</summary>
	public bool SlideCompleted { get; private set; }

	/// <summary><see cref="CellToolMode.Pool"/> while the tool is on, else <see cref="CellToolMode.None"/>.</summary>
	public CellToolMode Mode => IsActive ? CellToolMode.Pool : CellToolMode.None;
	public bool IsActive { get; private set; }
	/// <summary>True in the erase mode.</summary>
	public bool IsErasing { get; private set; }
	/// <summary>[EXT:pools-river] True while the river option is on: painted cells also get the river flag (docs/POOLS.md, "Lazy river"). Never in the erase mode.</summary>
	public bool IsRiver { get; private set; }
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

	/// <summary>Turns the tool on in the paint or the erase mode; a mode change while the tool is on keeps the undo stack. False when the extension is off.</summary>
	public bool Begin( bool erase, bool river = false )
	{
		if ( !FeatureGates.Pools )
			return false;
		Release();
		IsActive = true;
		bridgeMode = false;
		HoverCell = null;
		HoverResult = null;
		HoverSlideResult = null;
		HoverBridgeResult = null;
		LastBridgeRefusal = null;
		IsErasing = erase;
		slideTower = null;
		SlideCompleted = false;
		IsRiver = river && !erase;
		last = null;
		pending.Clear();
		pendingMarked.Clear();
		pendingMoney = 0;
		LastRefusal = null;
		return true;
	}

	/// <summary>Starts single-click placement or removal. The anchor is the first land approach cell; directions follow GuestPathGrid.</summary>
	public bool BeginBridge( int direction, bool erase = false )
	{
		if ( Bridges == null || direction < 0 || direction > 3 || !FeatureGates.Pools )
			return false;
		if ( !Begin( erase ) )
			return false;
		bridgeMode = true;
		bridgeDirection = direction;
		HoverCell = null;
		HoverResult = null;
		return true;
	}

	/// <summary>
	/// [EXT:pools-slides] Turns the tool on in the slide mode for the slide of the tower anchored at (<paramref name="towerX"/>, <paramref name="towerY"/>): each cell pressed or dragged over becomes the next
	/// piece of the track (the first one a start cell of the tower, a pool cell ending it with the splash piece). False when the extension is off, the park has no slides or the slide is complete.
	/// </summary>
	public bool BeginSlide( int towerX, int towerY )
	{
		if ( !FeatureGates.Pools || Slides == null || Slides.TowerAt( towerX, towerY ) == null || Slides.Find( PoolSlide.KeyOf( towerX, towerY ) ) is { IsComplete: true } )
			return false;
		Release();
		IsActive = true;
		bridgeMode = false;
		HoverCell = null;
		HoverResult = null;
		HoverSlideResult = null;
		HoverBridgeResult = null;
		LastBridgeRefusal = null;
		IsErasing = false;
		slideTower = (towerX, towerY);
		SlideCompleted = false;
		last = null;
		pending.Clear();
		pendingMoney = 0;
		LastRefusal = null;
		LastSlideRefusal = null;
		return true;
	}

	/// <summary>The cursor moved: the check of the cell under it. While a stroke is on, the cells it crossed are painted as they come.</summary>
	public void Hover( int x, int y )
	{
		if ( !IsActive )
			return;
		HoverCell = (x, y);
		if ( bridgeMode )
		{
			var result = IsErasing ? Bridges!.CheckRemove( x, y ) : Bridges!.Check( x, y, bridgeDirection );
			HoverBridgeResult = result == PoolBridgeResult.Ok ? null : result;
			HoverResult = Map( result );
			return;
		}
		if ( slideTower is { } tower && Slides != null )
		{
			var slide = Slides.Check( tower.X, tower.Y, x, y );
			HoverSlideResult = slide == PoolSlideResult.Ok ? null : slide;
			HoverResult = Map( slide );
			if ( last != null )
				Drag( x, y );
			return;
		}
		HoverResult = IsErasing ? builder.CheckErase( x, y ) : builder.Check( x, y );
		if ( IsRiver && HoverResult == PoolBuildResult.Existing )
			HoverResult = PoolBuildResult.Ok;
		if ( last != null )
			Drag( x, y );
	}

	/// <summary>The pointer left the park view.</summary>
	public void HoverNone()
	{
		// a drag that leaves the park view ends there: coming back with the button held does not join the two places with a line of water
		Release();
		HoverCell = null;
		HoverResult = null;
		HoverBridgeResult = null;
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
		pendingMarked.Clear();
		pendingMoney = 0;
		LastRefusal = null;
		LastSlideRefusal = null;
		LastBridgeRefusal = null;
		return Write( x, y );
	}

	/// <summary>The pointer reached another cell during a stroke: every cell between the last one and it is written.</summary>
	public void Drag( int x, int y )
	{
		if ( !IsActive || bridgeMode || last is not { } from || from == (x, y) )
			return;
		foreach ( var cell in PoolBuilder.DragLine( from, (x, y) ).Skip( 1 ) )
		{
			// [EXT:pools-slides] a slide the splash has finished takes no more cells: the rest of the drag is not a refusal
			if ( slideTower is { } laid && Slides?.Find( PoolSlide.KeyOf( laid.X, laid.Y ) ) is { IsComplete: true } )
				break;
			Write( cell.X, cell.Y );
		}
		last = (x, y);
	}

	/// <summary>The release: ends the stroke and keeps it for <see cref="Undo"/>. Returns it, or null when the stroke wrote nothing.</summary>
	public PoolStroke? Release()
	{
		if ( last == null )
			return null;
		last = null;
		if ( pending.Count == 0 && pendingMarked.Count == 0 )
			return null;
		var cells = pending.ToArray();
		pending.Clear();
		// [EXT:pools-slides] a finished slide ends the slide mode
		if ( slideTower is { } laid && Slides?.Find( PoolSlide.KeyOf( laid.X, laid.Y ) ) is { IsComplete: true } )
		{
			var money = pendingMoney;
			pendingMoney = 0;
			LastStroke = new PoolStroke( false, cells, money, 0, LastRefusal );
			SlideCompleted = true;
			IsActive = false;
			slideTower = null;
			HoverCell = null;
			HoverResult = null;
			strokes.Clear();
			Ended?.Invoke();
			return LastStroke;
		}
		var stroke = new PoolStroke( IsErasing, cells, IsErasing ? 0 : pendingMoney, IsErasing ? -pendingMoney : 0, LastRefusal ) { RiverMarked = pendingMarked.ToArray() };
		pendingMarked.Clear();
		pendingMoney = 0;
		// [EXT:pools-slides] the pieces of a slide are taken back by Backspace in the slide mode, not by the pool strokes' undo (which would erase the pool cells of the track)
		if ( slideTower != null || bridgeMode )
		{
			LastStroke = stroke;
			return stroke;
		}
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
		// Bridge removals require current occupancy checks. Use the removal row; never undo a bridge as a pool-water stroke.
		if ( bridgeMode )
			return false;
		// [EXT:pools-slides] in the slide mode Backspace takes back the last piece
		if ( slideTower is { } tower )
		{
			last = null;
			return Slides != null && issue( new UndoPoolSlidePieceCommand( tower.X, tower.Y ) ).Accepted;
		}
		if ( strokes.Count == 0 )
			return false;
		var stroke = strokes[^1];
		strokes.RemoveAt( strokes.Count - 1 );
		// [EXT:online-coop-commands] the stroke's cells are painted or erased again by commands (UndoPoolStrokeCommand): one, or chunks for a stroke longer than a command takes;
		// a repaint that ran out of money ends the undo, as the one write did
		foreach ( var chunk in UndoPoolStrokeCommand.For( stroke ) )
		{
			if ( issue( chunk ).Value is PoolStroke { RanOutOfMoney: true } )
				break;
		}
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
		bridgeMode = false;
		HoverBridgeResult = null;
		LastBridgeRefusal = null;
		IsErasing = false;
		slideTower = null;
		IsRiver = false;
		HoverCell = null;
		HoverResult = null;
		HoverSlideResult = null;
		strokes.Clear();
		Ended?.Invoke();
	}

	/// <summary>The build result the HUD's cursor and markers read for a slide result.</summary>
	private static PoolBuildResult Map( PoolSlideResult result ) => result switch
	{
		PoolSlideResult.Ok => PoolBuildResult.Ok,
		PoolSlideResult.NotEnoughMoney => PoolBuildResult.NotEnoughMoney,
		PoolSlideResult.ExtensionOff => PoolBuildResult.ExtensionOff,
		_ => PoolBuildResult.SlideRefused
	};

	private static PoolBuildResult Map( PoolBridgeResult result ) => result switch
	{
		PoolBridgeResult.Ok => PoolBuildResult.Ok,
		PoolBridgeResult.ExtensionOff => PoolBuildResult.ExtensionOff,
		PoolBridgeResult.OutsideTerrain => PoolBuildResult.OutsideTerrain,
		PoolBridgeResult.NotOwned => PoolBuildResult.NotOwned,
		PoolBridgeResult.Occupied => PoolBuildResult.Occupied,
		PoolBridgeResult.TooSteep => PoolBuildResult.TooSteep,
		PoolBridgeResult.Standing => PoolBuildResult.Standing,
		PoolBridgeResult.NotEnoughMoney => PoolBuildResult.NotEnoughMoney,
		_ => PoolBuildResult.Terrain
	};

	private PoolBuildResult Write( int x, int y )
	{
		// [EXT:online-coop-commands] every write is a command; a command the funnel refuses on its own (a peer's rights) answers as a cell taken
		if ( bridgeMode )
		{
			var bridge = issue( IsErasing ? new RemovePoolBridgeCommand( x, y ) : new PlacePoolBridgeCommand( x, y, bridgeDirection ) ).Value is PoolBridgeResult answer
				? answer : PoolBridgeResult.Occupied;
			var mapped = Map( bridge );
			if ( bridge == PoolBridgeResult.Ok )
				pending.Add( (x, y) );
			else
			{
				LastRefusal ??= ((x, y), mapped);
				LastBridgeRefusal ??= bridge;
			}
			return mapped;
		}
		if ( slideTower is { } tower && Slides != null )
		{
			if ( Slides.Find( PoolSlide.KeyOf( tower.X, tower.Y ) ) is { IsComplete: true } )
				return PoolBuildResult.Existing;
			var piece = issue( new LayPoolSlidePieceCommand( tower.X, tower.Y, x, y ) ).Value is PoolSlideWrite laid ? laid : new PoolSlideWrite( PoolSlideResult.Occupied, 0 );
			var slide = piece.Result;
			if ( slide == PoolSlideResult.Ok )
			{
				pending.Add( (x, y) );
				pendingMoney += piece.Money;
				return PoolBuildResult.Ok;
			}
			var mapped = Map( slide );
			LastRefusal ??= ((x, y), mapped);
			LastSlideRefusal ??= slide;
			return mapped;
		}
		var write = issue( IsErasing ? new ErasePoolCellCommand( x, y ) : new PaintPoolCellCommand( x, y, IsRiver ) ).Value is PoolCellWrite written
			? written : new PoolCellWrite( PoolBuildResult.Occupied, 0 );
		var result = write.Result;
		if ( result == PoolBuildResult.Ok )
		{
			pending.Add( (x, y) );
			pendingMoney += write.Money;
		}
		else if ( result == PoolBuildResult.RiverMarked )
			pendingMarked.Add( (x, y) );
		else if ( result != PoolBuildResult.Existing )
			LastRefusal ??= ((x, y), result);
		return result;
	}
}
