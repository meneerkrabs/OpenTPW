namespace OpenTPW;

/// <summary>
/// The patrol area tool (tp.exe mouse mode 0x36, the click handler <c>0x00524960</c>): started from the employee view's button (<c>0x004B5B50</c> with 1), the
/// first click on the map stores a corner, the second sets the member's rectangle between the two corners and ends the tool.
/// </summary>
public sealed class PatrolAreaTool
{
	private readonly Func<ParkEconomy?> economy;
	private readonly Func<ParkCommand, ParkCommandResult> issue;

	/// <param name="issue">The park's commands (<see cref="ParkCommands.Issue"/>): the second click sets the area as a <see cref="SetPatrolAreaCommand"/>.</param>
	public PatrolAreaTool( Func<ParkEconomy?> economy, Func<ParkCommand, ParkCommandResult> issue )
	{
		this.economy = economy;
		this.issue = issue ?? throw new ArgumentNullException( nameof( issue ) );
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

	/// <summary>A click on the map: the first stores a corner, the second sets the rectangle and ends the tool. False when nothing happened.</summary>
	// [BIN:TPW-EXE:0x00524960 patrol tool] mode 0x36: with no stored corner the click's cell is stored; otherwise the corners are ordered (the smaller x and y as the first corner) and 0x00506E50 sets them on the selected member, the corner is reset and the tool ends (0x0052F200(0, 0))
	public bool Click( int x, int y )
	{
		if ( !IsActive || !InMap( x, y ) )
			return false;
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

	/// <summary>The cells on the edge of the rectangle the next click would set (empty before the first corner or without a hovered cell).</summary>
	public IEnumerable<(int X, int Y)> PreviewEdge()
	{
		if ( FirstCorner is not { } a || HoverCell is not { } b )
			yield break;
		var (x0, x1) = (Math.Min( a.X, b.X ), Math.Max( a.X, b.X ));
		var (y0, y1) = (Math.Min( a.Y, b.Y ), Math.Max( a.Y, b.Y ));
		for ( var x = x0; x <= x1; x++ )
			for ( var y = y0; y <= y1; y++ )
				if ( x == x0 || x == x1 || y == y0 || y == y1 )
					yield return (x, y);
	}

	private static bool InMap( int x, int y ) => (uint)x < ParkLitter.MapSize && (uint)y < ParkLitter.MapSize;
}
