namespace OpenTPW;

/// <summary>
/// Theme Park Inc's walk step <c>0x60B460</c> (through <c>0x60B440</c>) around the steering step, shared by the guests (<c>GuestSimulation.StepThemeParkInc</c>) and the staff
/// (<c>ThemeParkIncStaffWorld.Walk</c>) of a park with Theme Park Inc's navigator (every Theme Park Inc park, docs/reverse/TPI-NAVIGATOR.md, NV3). The order of the routine:
/// the walk position, the steering step <c>0x63EEF0</c>, the position again, <c>mX/mY</c> = position &gt;&gt; 8 with the cell chain <c>0x637B70</c>, the stranded mark <c>+0x1B0</c> = 0; then 2
/// when mCantReachDest is set, 0 when the progress is ONE (with the heading, D1), else the heading, the run flag (D2), the walk or run template and the sprite interval, and 1. These are the
/// pure parts; the callers own the steering, the chain and the sprite.
/// </summary>
// [BIN:TPI-EXE:0x60B460 walk step] 0x63EF10 (the position) into x0, y0; 0x63EEF0; 0x63EF10 into x1, y1; 0x637B70(x1 >> 8, y1 >> 8); +0x1B0 = 0; +0x1A4 nonzero returns 2; 0x63EF70 == 0x10000 writes
// +0x2C = 0x91E074(x0 - x1, y1 - y0) & 0xFFFF and returns 0 (D1: tp.exe's 0x4FA2A0 returns 0 without the write); else the same write, the run flag, 0x4E4AB0 (the restart test) and 0x5D0F90 (the
// template), 0x5D10F0 (the interval) and 1
public static class ThemeParkIncWalkStep
{
	/// <summary>The word the run flag compares the speed bonus with (<c>0x9CFB20</c>: 0).</summary>
	public const int RunBonusFloor = 0;

	/// <summary>The template of a walking step, of a running one (the sprite programs <c>0x9AFBA8</c> and <c>0x9AFC04</c>, the table <c>0x9CDA94</c>'s entries 1 and 2).</summary>
	public const int WalkTemplate = 1, RunTemplate = 2;

	/// <summary>The person kinds the run flag reads (thing byte <c>+2</c>): a guest, a guard.</summary>
	public const int GuestKind = 1, GuardKind = 7;

	/// <summary>
	/// The heading word <c>+0x2C</c> a step from (<paramref name="x0"/>, <paramref name="y0"/>) to (<paramref name="x1"/>, <paramref name="y1"/>) writes, on arrival (D1) as while walking: the angle
	/// routine <c>0x91E074</c> (tp.exe's <c>0x6E7074</c>, the same code and table, <see cref="HeadingAngle.Of"/>) of (x0 − x1, y1 − y0), masked to a word. A step that did not move gives 0.
	/// </summary>
	// [BIN:TPI-EXE:0x60B4EC arrival heading] push y1 - y0, push x0 - x1, call 0x91E074, and eax, 0xFFFF, mov [esi + 0x2C]; the walking branch 0x60B51B pushes the same differences
	public static int Heading( int x0, int y0, int x1, int y1 ) => HeadingAngle.Of( unchecked( x0 - x1 ), unchecked( y1 - y0 ) ) & 0xFFFF;

	/// <summary>
	/// The run flag (D2): the speed bonus word <c>+0xD6</c> above <see cref="RunBonusFloor"/> (an unsigned word compare) and either a guest of generation 0 (<c>+0x2A4</c>: a kid) or a guard.
	/// Theme Park World's walk step (tp.exe <c>0x4FA2A0</c>) runs every guest with a bonus.
	/// </summary>
	// [BIN:TPI-EXE:0x60B541 run flag] cmp word +0xD6, [0x9CFB20]; jbe walks; kind byte +2 == 1 with +0x2A4 == 0, or kind 7, runs; the block 0x60B5A0..0x60B5BA, which picks the walk for a kind-1 grown-up,
	// is dead (a grown-up never runs)
	public static bool Runs( int bonus, int kind, int generation ) =>
		unchecked( (ushort)bonus ) > RunBonusFloor && (kind == GuestKind && generation == 0 || kind == GuardKind);

	/// <summary>
	/// The sprite interval of a walking step (<c>0x5D10F0</c> after <c>0x60B5F8</c>) at the game thread's control word 0x7F (<c>[EXT:hardware-d3d-path]</c>): the speed is the root of the step's
	/// squared length (32-bit integer arithmetic) times <c>0x93E5B4</c>, each rounded to a single, doubled unless running; the interval is 62.0 divided by it (rounded to a single) and truncated,
	/// below 0 giving 0 and above 0xFA 0xFA. A step that did not move divides by zero and gives 0, which queues no interval.
	/// </summary>
	// [BIN:TPI-EXE:0x60B5F8 step speed] dx, dy of the two positions; imul each, add (a dword); fild, fsqrt, fmuls 0x93E5B4 (3.829657e-05), fstps; without the run flag fadd st, st and fstps
	// [BIN:TPI-EXE:0x5D10F0 sprite interval] fld 0x93E11C (62.0), fdiv the speed, _ftol 0x86FAB0; negative stores 0, above 0xFA (unsigned) stores 0xFA, into the sprite wrapper's +0x10
	public static int Interval( int x0, int y0, int x1, int y1, bool run )
	{
		var dx = unchecked( x1 - x0 );
		var dy = unchecked( y1 - y0 );
		var squared = unchecked( dx * dx + dy * dy );
		// fsqrt at 24 bits: the double root of the 32-bit integer rounded once more to a single (53 >= 2 x 24 + 2, so the second rounding is the root's own)
		var speed = (float)Math.Sqrt( squared ) * SpritePlayer.SpeedPerUnit;
		if ( !run )
			speed += speed;
		var interval = ThemeParkIncX87.Ftol( SpritePlayer.IntervalNumerator / speed );
		return interval < 0 ? 0 : interval > SpritePlayer.LongestInterval ? SpritePlayer.LongestInterval : interval;
	}

	/// <summary>The unit vector of a heading word (presentation): (−sin, −cos) of its angle, as <see cref="HeadingAngle.Facing"/> turns an offset into one.</summary>
	// [EXT:deterministic-math] the sine and cosine of the quantised angle in IEEE double by DetMath, the same bits on every runtime (docs/DETERMINISM.md, "Browser and native")
	public static (float X, float Y) Vector( int heading )
	{
		var radians = (heading & (HeadingAngle.Turn - 1)) * (2 * Math.PI / HeadingAngle.Turn);
		return ((float)-DetMath.Sin( radians ), (float)-DetMath.Cos( radians ));
	}
}
