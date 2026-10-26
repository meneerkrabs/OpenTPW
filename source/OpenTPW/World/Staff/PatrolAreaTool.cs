namespace OpenTPW;

/// <summary>
/// The patrol area tool (tp.exe mouse mode 0x36, the click handler <c>0x00524960</c>): started from the employee view's button (<c>0x004B5B50</c> with 1), the
/// first click on the map stores a corner, the second sets the member's rectangle between the two corners and ends the tool.
/// </summary>
public sealed class PatrolAreaTool
{
	private readonly Func<ParkEconomy?> economy;
	private readonly Func<ParkCommand, ParkCommandResult> issue;
	private readonly Func<ParkCellMap?>? cells;

	/// <param name="issue">The park's commands (<see cref="ParkCommands.Issue"/>): the second click sets the area as a <see cref="SetPatrolAreaCommand"/>.</param>
	/// <param name="cells">The park's cells, for the preview's verdict (<see cref="PreviewRefused"/>).</param>
	public PatrolAreaTool( Func<ParkEconomy?> economy, Func<ParkCommand, ParkCommandResult> issue, Func<ParkCellMap?>? cells = null )
	{
		this.economy = economy;
		this.issue = issue ?? throw new ArgumentNullException( nameof( issue ) );
		this.cells = cells;
	}

	/// <summary>The member whose area the tool draws, 0 when the tool is not running.</summary>
	public int MemberId { get; private set; }
	public bool IsActive => MemberId != 0;
	/// <summary>The corner the first click stored, null before it.</summary>
	public (int X, int Y)? FirstCorner { get; private set; }
	/// <summary>The cell under the mouse, for the rectangle's preview.</summary>
	public (int X, int Y)? HoverCell { get; private set; }

	/// <summary>Starts the tool for <paramref name="member"/> (the employee view's set button).</summary>
	public bool Begin( StaffMember member )
	{
		if ( economy() is not { } park || park.Staff.Members.All( item => item.Id != member.Id ) )
			return false;
		MemberId = member.Id;
		FirstCorner = null;
		HoverCell = null;
		return true;
	}

	public void Hover( int? x, int? y ) => HoverCell = x is { } cx && y is { } cy && InMap( cx, cy ) ? (cx, cy) : null;

	/// <summary>The cell of a click the refusal latch refused (the second click on a red rectangle) until <see cref="TakeRefusal"/> takes it.</summary>
	private (int X, int Y)? refusedAt;

	/// <summary>The cell of the last click that was refused, once: the HUD plays the refusal cue for it.</summary>
	public (int X, int Y)? TakeRefusal()
	{
		var cell = refusedAt;
		refusedAt = null;
		return cell;
	}

	/// <summary>A click on the map: the first stores a corner, the second sets the rectangle and ends the tool; the second on a red rectangle ends it and sets nothing. False when nothing happened.</summary>
	// [BIN:TPW-EXE:0x00524960 patrol tool] mode 0x36: with no stored corner the click's cell is stored; otherwise the corners are ordered (the smaller x and y as the first corner) and 0x00506E50 sets them on the selected member, the corner is reset and the tool ends (0x0052F200(0, 0))
	// [BIN:TPW-EXE:0x00524A63 refused ghost] a red ghost cell sets the refusal latch 0x816D48 (0x533931 → 0x527940(1)); the release handler tests it at 0x524A63, plays cat_ui 0xAF (0x524AA7) and goes through the jump table 0x52766C / byte table 0x527694, whose index 8 (mode 0x36) leads to 0x5266A3 = 0x52F200(0, 0): the tool ends and nothing is set
	public bool Click( int x, int y )
	{
		if ( !IsActive || !InMap( x, y ) )
			return false;
		if ( FirstCorner != null && PreviewRefused )
		{
			refusedAt = (x, y);
			Cancel();
			return true;
		}
		if ( economy()?.Staff.Members.FirstOrDefault( item => item.Id == MemberId ) is not { } member )
		{
			Cancel();
			return false;
		}
		if ( FirstCorner is not { } first )
		{
			FirstCorner = (x, y);
			return true;
		}
		issue( new SetPatrolAreaCommand( member.Id, first.X, first.Y, x, y ) );
		Cancel();
		return true;
	}

	/// <summary>Ends the tool without a change (right click, Escape, another tool).</summary>
	public void Cancel()
	{
		MemberId = 0;
		FirstCorner = null;
		HoverCell = null;
	}

	/// <summary>The cells on the edge of the rectangle the next click would set (the hovered cell alone before the first corner, empty without a hovered cell).</summary>
	// [BIN:TPW-EXE:0x005234d0 patrol tool hover] mode 0x36 lays the line op 0x136 (LayLine 0x536100 with the ghost bit) before the first click on the hovered cell alone, and after it along the four edges of the ordered rectangle
	public IEnumerable<(int X, int Y)> PreviewEdge()
	{
		if ( HoverCell is not { } hovered )
			return Array.Empty<(int X, int Y)>();
		return EdgeCells( FirstCorner ?? hovered, hovered );
	}

	/// <summary>
	/// The cells the line layer marks for a rectangle between two corners: the top row between its corners, the right column, the bottom row between its corners and the left column
	/// (the rows start one cell in from the left corner and end one cell before the right one unless the rectangle is one column wide), so the whole border once; a cell off the map is not marked.
	/// </summary>
	// [BIN:TPW-EXE:0x004b3640 patrol area highlight] the staff locator draws the located staff member's area with four LayLine passes of the op 0x136: (x0 + 1, y0) to (x1 - 1, y0), (x1, y0) to (x1, y1), (x0 + 1, y1) to (x1 - 1, y1), (x0, y1) to (x0, y0); 0x00506E70 copies the two corner words, which 0x00524960 stored ordered (the smaller x and y first); the one-column case keeps x0 and x1; 0x005234d0 lays the same four lines between the first click and the hovered cell
	public static IEnumerable<(int X, int Y)> EdgeCells( (int X, int Y) a, (int X, int Y) b )
	{
		var (x0, x1) = (Math.Min( a.X, b.X ), Math.Max( a.X, b.X ));
		var (y0, y1) = (Math.Min( a.Y, b.Y ), Math.Max( a.Y, b.Y ));
		var marked = new HashSet<(int X, int Y)>();
		foreach ( var (x, y) in Line( x0 != x1 ? x0 + 1 : x0, y0, x0 != x1 ? x1 - 1 : x1, y0 ).Concat( Line( x1, y0, x1, y1 ) )
			.Concat( Line( x0 != x1 ? x0 + 1 : x0, y1, x0 != x1 ? x1 - 1 : x1, y1 ) ).Concat( Line( x0, y1, x0, y0 ) ) )
		{
			if ( InMap( x, y ) && marked.Add( (x, y) ) )
				yield return (x, y);
		}
	}

	/// <summary>The line layer's straight line (0x536100): from the start to the end along the longer axis, both ends included, whichever comes first.</summary>
	private static IEnumerable<(int X, int Y)> Line( int x0, int y0, int x1, int y1 )
	{
		var horizontal = Math.Abs( y1 - y0 ) < Math.Abs( x1 - x0 );
		var step = horizontal ? Math.Sign( x1 - x0 ) : Math.Sign( y1 - y0 );
		var (x, y) = (x0, y0);
		while ( horizontal ? x != x1 : y != y1 )
		{
			yield return (x, y);
			if ( horizontal )
				x += step;
			else
				y += step;
		}
		yield return (x, y);
	}

	/// <summary>Whether the preview is red: the rectangle between the first corner and the hovered cell has no cell the member could stand on (every cell on the map is unowned or a park fence post, <see cref="ParkLand.IsPost"/>).</summary>
	// [BIN:TPW-EXE:0x00532a50 patrol rectangle test] counts the map cells of the rectangle and those with flag 0x40 (unowned land, 0x535DB0) or whose second-layer record is a fence post (kind 0x19, 0x53AD60); 0x005234d0 stores "every one counted" in 0x816D50 and the validator 0x535670 answers that flag for the op 0x36, so SetCell 0x532FC0 draws the red face (texture 1) for every cell of the four lines; before the first click the flag is 0 and the hovered cell is blue
	public bool PreviewRefused
	{
		get
		{
			if ( FirstCorner is not { } a || HoverCell is not { } b || cells?.Invoke() is not { } map )
				return false;
			int total = 0, blocked = 0;
			for ( var y = Math.Min( a.Y, b.Y ); y <= Math.Max( a.Y, b.Y ); y++ )
			{
				for ( var x = Math.Min( a.X, b.X ); x <= Math.Max( a.X, b.X ); x++ )
				{
					if ( !map.InBounds( x, y ) )
						continue;
					total++;
					if ( (map.FlagsAt( x, y ) & ParkCellFlags.Unowned) != 0 || map.Land?.IsPost( x, y ) == true )
						blocked++;
				}
			}
			return blocked == total;
		}
	}

	private static bool InMap( int x, int y ) => (uint)x < ParkLitter.MapSize && (uint)y < ParkLitter.MapSize;
}
