namespace OpenTPW;

/// <summary>One sprite script of the original's table 0x0075A418 as a staff member plays it: the slot it sets, the frame each step shows and the script that follows the last step.</summary>
/// <param name="Next">The script that runs after the last step (itself: the script loops).</param>
public sealed record StaffScript( int Slot, IReadOnlyList<int> Frames, int Next );

/// <summary>
/// The staff scripts of tp.exe (the table 0x0075A418, indexed by script number), decoded from their opcode streams: each <c>FRAME n</c> (0x00476940) shows frame n of the slot and ends the
/// step, <c>SLOT s</c> (0x004768B0) sets the slot and <c>JMP</c> (0x00476020) goes to another script's start. Pure data: no assets, no GPU.
/// </summary>
public static class StaffScripts
{
	private static IReadOnlyList<int> Run( int count ) => Enumerable.Range( 0, count ).ToArray();
	private static IReadOnlyList<int> Twice( int count ) => Enumerable.Range( 0, count ).SelectMany( frame => new[] { frame, frame } ).ToArray();

	// [BIN:TPW-EXE:0x0074DB60 staff scripts] 1 walk: slot 1 frames 0-7 (0x0074DB60); 2 run: slot 2 frames 0-7 (0x0074DBC0); 3 stand: slot 0 frame 0 (0x0074DC20); 9 the patrol walk: slot 1, each frame twice (0x0074DAC0)
	// [BIN:TPW-EXE:0x0074E100 staff scripts] 10 research: slot 4 frames 0-3 then four steps of frame 0, looping (0x0074E100); 0x11 sweep: slot 4 frames 0-3 each twice (0x0074EDD8); 0x12 repair: slot 4 frames 0-7 (0x0074EE38)
	// [BIN:TPW-EXE:0x0074EEC0 staff scripts] 0x13 strike: slot 5 frames 0-3 then six steps of frame 0, looping (0x0074EEC0); 0x14 state 6: slot 3 frame 0 (0x0074EE98); 0x15 the dangling sprite: slot 6 frames 0-3 (0x0074ED98)
	// [BIN:TPW-EXE:0x0074EF30 staff scripts] 0x16 the guard's catch: slot 7 frames 0-7 once, then script 0x17: slot 8 frames 0-7 looping (0x0074EF90)
	private static readonly Dictionary<int, StaffScript> Table = new()
	{
		[1] = new( 1, Run( 8 ), 1 ),
		[2] = new( 2, Run( 8 ), 2 ),
		[3] = new( 0, new[] { 0 }, 3 ),
		[9] = new( 1, Twice( 8 ), 9 ),
		[0xA] = new( 4, new[] { 0, 1, 2, 3, 0, 0, 0, 0 }, 0xA ),
		[0x11] = new( 4, Twice( 4 ), 0x11 ),
		[0x12] = new( 4, Run( 8 ), 0x12 ),
		[0x13] = new( 5, new[] { 0, 1, 2, 3, 0, 0, 0, 0, 0, 0 }, 0x13 ),
		[0x14] = new( 3, new[] { 0 }, 0x14 ),
		[0x15] = new( 6, Run( 4 ), 0x15 ),
		[0x16] = new( 7, Run( 8 ), 0x17 ),
		[0x17] = new( 8, Run( 8 ), 0x17 )
	};

	/// <summary>The script with that number, or null for one this table does not hold (the entertainers' performance scripts, which follow the bank).</summary>
	public static StaffScript? Get( int script ) => Table.GetValueOrDefault( script );

	/// <summary>
	/// The frame an entertainer's performance program (program 0 of the table 0x0074F7C8, the only one any shipped entry uses) shows at its <paramref name="step"/>-th step on a slot of
	/// <paramref name="frames"/> frames: frames 0 to <paramref name="intro"/> once when it starts (only when the intro is above 0), then forever the frames from <paramref name="intro"/> to the last and,
	/// when <paramref name="hold"/> is above 0, frame 0 for hold + 1 steps.
	/// </summary>
	// [BIN:TPW-EXE:0x0074F638 performance program 0] SET flags 0x1200, SET frame register 0xD = 0; IF reg 0x11 > 0 (OP50 0x00476050 op 7 against 0): do { SLOTREG 0xD; reg 0xD += 1 } while reg 0xD <= reg 0x11 (OP3D0 0x004763D0 op 3: register against register) ENDIF (0x004762F0); loop (0x0074F5B0): reg 0xD = reg 0x11, do { SLOTREG 0xD; reg 0xD += 1 } while reg 0xD != reg 0xE (op 1; register 0xE is the slot's frames per direction, 0x00540C60); IF reg 0x12 > 0: reg 0 = 0, do { FRAME 0; reg 0 += 1 } while reg 0 <= reg 0x12 ENDIF; back to the loop
	public static int PerformanceFrame( int intro, int hold, int frames, int step )
	{
		if ( step < 0 || frames <= 0 )
			return 0;
		if ( intro > 0 )
		{
			if ( step <= intro )
				return step;
			step -= intro + 1;
		}
		var pass = Math.Max( 1, frames - Math.Clamp( intro, 0, frames - 1 ) ) + (hold > 0 ? hold + 1 : 0);
		var inPass = step % pass;
		var start = Math.Clamp( intro, 0, frames - 1 );
		return inPass < frames - start ? start + inPass : 0;
	}

	/// <summary>The slot and frame the script shows at its <paramref name="step"/>-th step (0-based), following a script that hands over to another.</summary>
	public static (int Slot, int Frame)? At( int script, int step )
	{
		if ( Get( script ) is not { } current || step < 0 )
			return null;
		while ( step >= current.Frames.Count && current.Next != script )
		{
			step -= current.Frames.Count;
			script = current.Next;
			current = Table[script];
		}
		return (current.Slot, current.Frames[step % current.Frames.Count]);
	}
}

/// <summary>
/// The sprite player's clock for one sprite (tp.exe <c>0x00475360</c>): the native scheduler runs on even 31 ms substeps. This staff adapter runs once per rendered frame, one step when the millisecond clock has passed the sprite's due time, and the due
/// time becomes the clock plus the sprite's interval (62 ms when it is created, <c>0x004758F0</c>); there is no catching up. A person's walk step sets the interval from its speed
/// (<see cref="IntervalFor"/>). Pure rules: no assets, no GPU.
/// </summary>
public sealed class SpritePlayer
{
	/// <summary>The interval a sprite is created with (<c>0x004758F0</c>: +0x80 = 0x3E).</summary>
	public const int DefaultInterval = 62;
	/// <summary>The longest interval the walk step sets (<c>0x004D41F4</c>).</summary>
	public const int LongestInterval = 250;
	/// <summary>The numerator of the interval (<c>0x007003A0</c>: 62.0).</summary>
	public const float IntervalNumerator = 62f;
	/// <summary>A step length in the walk step's 16.16 units times this is the speed v the interval divides by (<c>0x007006E8</c>).</summary>
	public const float SpeedPerUnit = 3.829657e-05f;

	private long due;

	public SpritePlayer( long nowMs ) => due = nowMs + DefaultInterval;

	/// <summary>Milliseconds between steps.</summary>
	public int Interval { get; private set; } = DefaultInterval;
	/// <summary>Steps taken since the script started (<see cref="Restart"/>).</summary>
	public int Steps { get; private set; }

	/// <summary>One frame's tick: the script steps when the clock has passed the due time (0x0047537B..0x00475405 compare with <c>jle</c>: strictly later).</summary>
	public void Tick( long nowMs )
	{
		if ( nowMs <= due )
			return;
		Steps++;
		due = nowMs + Interval;
	}

	/// <summary>A new script starts at its first step (the play call 0x00475B80); the due time and the interval stay.</summary>
	public void Restart() => Steps = 0;

	/// <summary>
	/// The interval a person's walk step asks for (<c>0x004FA2A0</c> → <c>0x004D41D0</c>, applied by <c>0x004D4190</c>): 62 over the speed, truncated, 0 (no change) when it truncates below 1 and 250 at most; the
	/// speed is the step length times <see cref="SpeedPerUnit"/>, doubled for the walk script (every clip but the run).
	/// </summary>
	/// <param name="stepLength">The step's length in 16.16 units (65536 a cell) in one park turn.</param>
	public static int IntervalFor( float stepLength, bool run )
	{
		var speed = stepLength * SpeedPerUnit;
		if ( !run )
			speed += speed;
		var interval = (int)(IntervalNumerator / speed);
		return float.IsFinite( speed ) && speed > 0f ? Math.Clamp( interval, 0, LongestInterval ) : 0;
	}

	/// <summary>Takes the interval a walk step asked for; 0 keeps the one the sprite has.</summary>
	public void SetInterval( int interval )
	{
		if ( interval > 0 )
			Interval = interval;
	}
}
