namespace OpenTPW.UI.Original;

/// <summary>
/// A window's property-table animation: a table of four-word steps that the window takes one at a time, each when more than its interval has passed since the last,
/// moving its rectangle's edges, setting its model's angle or jumping back in the table. The winning screen's banner is animated so (<see cref="WinningBanner"/>).
/// </summary>
// [BIN:TPW-EXE:0x0065E72D window animation start] stores the table (+0xE8), the interval in milliseconds (+0xF0) and the clock (+0xF4, 0x65968E), the step index (+0xEC) 0, and marks the window animating (+0x135 = 1)
// [BIN:TPW-EXE:0x0065E9A4 window animation step] on each window update (0x65C357) while animating: when the interval is strictly less than the clock minus the stamp, the stamp becomes the clock and the step at the index runs:
// -9999 ends (the edges move back by the summed offsets, the index is 0); -9998 sets the model angle (0x65F286) to the second word; -9997 sets the index to the second word (its immediate retry cannot step again,
// the stamp being the clock); -9996 calls 0x65D3A3 with the second word; any other step adds its four words to the left, top, right and bottom edges (0x65CC04) and to the summed offsets (+0xF8); all but -9999 and -9997 then advance the index
public sealed class UiWindowAnimation
{
	/// <summary>One table entry: a command word and three operands, or four edge offsets.</summary>
	public readonly record struct Step( short First, short Second, short Third, short Fourth )
	{
		public static Step Edges( short left, short top, short right, short bottom ) => new( left, top, right, bottom );
		public static Step Angle( short angle ) => new( SetAngle, angle, 0, 0 );
		public static Step JumpTo( short index ) => new( Jump, index, 0, 0 );
	}

	public const short End = -9999;
	public const short SetAngle = -9998;
	public const short Jump = -9997;
	/// <summary>The fourth command (0x65D3A3); no table OpenTPW plays uses it, so a table with it is refused.</summary>
	public const short Command = -9996;

	/// <summary>
	/// The winning screen's banner table (0x74FCF8, interval 35 ms): six steps narrowing the banner by 16 at each side, four lifting it by 4, a whole turn in eight
	/// angles of 512, four lowering it by 4 and six widening it back, then a jump to the start: the banner squeezes, rises, spins, falls and stretches again and again.
	/// </summary>
	// [BIN:TPW-EXE:0x0074FCF8 winning banner animation] 0x48AFA7 starts it on the banner with 0x65E72D(0x74FCF8, 0x23); entries 0-5 (16, 0, -16, 0), 6-9 (0, -4, 0, -4), 10-17 (-9998, 512 x 1..8), 18-21 (0, 4, 0, 4), 22-27 (-16, 0, 16, 0), 28 (-9997, 0)
	public static readonly IReadOnlyList<Step> WinningBanner = Array.AsReadOnly( Enumerable.Repeat( Step.Edges( 16, 0, -16, 0 ), 6 )
		.Concat( Enumerable.Repeat( Step.Edges( 0, -4, 0, -4 ), 4 ) )
		.Concat( Enumerable.Range( 1, 8 ).Select( turn => Step.Angle( (short)(512 * turn) ) ) )
		.Concat( Enumerable.Repeat( Step.Edges( 0, 4, 0, 4 ), 4 ) )
		.Concat( Enumerable.Repeat( Step.Edges( -16, 0, 16, 0 ), 6 ) )
		.Append( Step.JumpTo( 0 ) ).ToArray() );

	/// <summary>The winning banner's interval in milliseconds (0x23).</summary>
	public const int WinningBannerInterval = 35;

	private readonly IReadOnlyList<Step> table;
	private readonly int interval;
	private long stamp;
	private int index;

	public UiWindowAnimation( IReadOnlyList<Step> table, int intervalMilliseconds, long startMilliseconds )
	{
		ArgumentNullException.ThrowIfNull( table );
		if ( table.Count == 0 || table[^1].First is not (End or Jump) || table.Any( step => step.First == Command )
			|| table.Any( step => step.First == Jump && (step.Second < 0 || step.Second >= table.Count) ) )
			throw new ArgumentException( "A window animation table ends in an end or jump step, jumps within itself and has no 0x65D3A3 command.", nameof( table ) );
		this.table = table;
		interval = intervalMilliseconds;
		stamp = startMilliseconds;
		Active = true;
	}

	/// <summary>Whether the animation still runs (until an end step).</summary>
	public bool Active { get; private set; }

	/// <summary>The summed edge offsets: left, top, right and bottom.</summary>
	public (int Left, int Top, int Right, int Bottom) Offset { get; private set; }

	/// <summary>The model angle the table set last, 4096 a turn (0 until it sets one).</summary>
	public int Angle { get; private set; }

	/// <summary>The index of the step that runs next.</summary>
	public int Index => index;

	/// <summary>One window update at <paramref name="nowMilliseconds"/>: runs the next step when more than the interval has passed since the last one; true when a step ran.</summary>
	public bool Update( long nowMilliseconds )
	{
		if ( !Active || interval >= nowMilliseconds - stamp )
			return false;
		stamp = nowMilliseconds;
		var step = table[index];
		switch ( step.First )
		{
			case End:
				Active = false;
				index = 0;
				Offset = default;
				break;
			case SetAngle:
				Angle = step.Second;
				index++;
				break;
			case Jump:
				index = step.Second;
				break;
			default:
				Offset = (Offset.Left + step.First, Offset.Top + step.Second, Offset.Right + step.Third, Offset.Bottom + step.Fourth);
				index++;
				break;
		}
		return true;
	}
}
