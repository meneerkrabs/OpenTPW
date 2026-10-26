namespace OpenTPW;

/// <summary>What a click of the area clear tool did.</summary>
public enum AreaClearClick
{
	/// <summary>The tool is off.</summary>
	Ignored,
	/// <summary>The first click stored the rectangle's first corner.</summary>
	CornerStored,
	/// <summary>The second click cleared the rectangle and ended the tool.</summary>
	Cleared
}

/// <summary>
/// The area clear tool (tp.exe tool mode 0x3A, the bulldozer: the buy window's Clear Land row and the Clear key): two clicks span a
/// rectangle; the second removes every object wholly inside it and every path cell in it (<see cref="AreaClear.Run"/>, one <see cref="ClearAreaCommand"/>), then the tool ends.
/// Queue cells and ride cells that are not wholly inside stay.
/// </summary>
// [BIN:TPW-EXE:0x0040C5E0 Clear key] the key builds the tool object 0x46C580(0x3A); the buy window's row −2 builds the same (0x4AC851)
// [BIN:TPW-EXE:0x00525DA3 area clear click] in mode 0x3A the first click (start −1) stores the cell as the corner; the second runs the check 0x424EC0 (it returns 1) and the clear 0x52BDD0 from the corner to the clicked cell, sets the start to −1 and ends the tool (SetMode(0, 0) 0x525E33)
public sealed class AreaClearTool
{
	private readonly Func<(int X, int Y), (int X, int Y), ParkCommandResult> clear;

	/// <param name="clear">
	/// Clears the rectangle between two corners (<see cref="AreaClear.Run"/>): [EXT:online-coop-commands] the level issues a <see cref="ClearAreaCommand"/>, whose value is the counts of
	/// objects and path cells it removed; a guest's request comes back later (<see cref="ParkCommandResult.IsPending"/>), and its counts are not known here.
	/// </param>
	public AreaClearTool( Func<(int X, int Y), (int X, int Y), ParkCommandResult> clear ) => this.clear = clear ?? throw new ArgumentNullException( nameof( clear ) );

	public bool IsActive { get; private set; }
	/// <summary>The cell the first click stored, null before it.</summary>
	public (int X, int Y)? Corner { get; private set; }
	/// <summary>The cell under the pointer, null off the park.</summary>
	public (int X, int Y)? HoverCell { get; private set; }
	/// <summary>Objects and path cells the last clear removed: (0, 0) after <see cref="Begin"/>, and for a guest's clear, which the host runs later.</summary>
	public (int Objects, int Paths) LastCleared { get; private set; }

	/// <summary>Starts the tool (SetMode 0x3A: no corner yet).</summary>
	public void Begin()
	{
		IsActive = true;
		Corner = null;
		HoverCell = null;
		LastCleared = (0, 0);
	}

	/// <summary>Ends the tool without clearing (right click, Escape, another tool).</summary>
	public void Cancel()
	{
		IsActive = false;
		Corner = null;
		HoverCell = null;
	}

	/// <summary>The pointer is over (<paramref name="x"/>, <paramref name="y"/>), or off the park when null.</summary>
	public void Hover( int? x, int? y ) => HoverCell = IsActive && x is { } cx && y is { } cy ? (cx, cy) : null;

	/// <summary>A click on (<paramref name="x"/>, <paramref name="y"/>): the first stores the corner, the second clears the rectangle and ends the tool.</summary>
	public AreaClearClick Click( int x, int y )
	{
		if ( !IsActive )
			return AreaClearClick.Ignored;
		if ( Corner is not { } corner )
		{
			Corner = (x, y);
			HoverCell = (x, y);
			return AreaClearClick.CornerStored;
		}
		// [EXT:online-coop-commands] the rectangle is one command (ClearAreaCommand); the tool ends whatever became of it, as tp.exe's does
		LastCleared = clear( corner, (x, y) ).As<(int Objects, int Paths)>();
		Cancel();
		return AreaClearClick.Cleared;
	}

	/// <summary>
	/// The ghost: the rectangle's edge cells from <see cref="Corner"/> to <see cref="HoverCell"/>, or the hovered cell before the first click.
	/// </summary>
	// [BIN:TPW-EXE:0x005234D0 area clear ghost] with a corner stored the preview lays the four edges of the rectangle with LayLine(0x13A) (the validator answers 0xD for mode 0x3A)
	public IEnumerable<(int X, int Y)> PreviewEdge()
	{
		if ( HoverCell is not { } b )
			yield break;
		if ( Corner is not { } a )
		{
			yield return b;
			yield break;
		}
		var (x0, x1) = (Math.Min( a.X, b.X ), Math.Max( a.X, b.X ));
		var (y0, y1) = (Math.Min( a.Y, b.Y ), Math.Max( a.Y, b.Y ));
		for ( var y = y0; y <= y1; y++ )
			for ( var x = x0; x <= x1; x++ )
				if ( x == x0 || x == x1 || y == y0 || y == y1 )
					yield return (x, y);
	}
}
