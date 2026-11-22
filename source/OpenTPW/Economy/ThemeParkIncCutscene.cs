namespace OpenTPW;

/// <summary>
/// The world side of Theme Park Inc's camera flight: the attraction things whose scripts and camera seats it uses, and the ride view (docs/reverse/TPI-MISSIONS.md §5). A level implements it;
/// without one (a headless economy) the flight keeps its state and its things have no script, so it ends only by a skip.
/// </summary>
public interface IThemeParkIncCutsceneWorld
{
	/// <summary>The script instance of the attraction thing <paramref name="thing"/> (<c>0x6B0740</c>, by the thing's script handle <c>+0x2C</c>); null for none.</summary>
	RideVM? ScriptOf( ushort thing );
	/// <summary>The object type (<c>Info.Id</c>) of the live attraction thing <paramref name="thing"/>; null for none.</summary>
	int? TypeOf( ushort thing );
	/// <summary>
	/// Leaves any ride or walk camera (<c>0x50B4E0</c>) and puts the camera on the thing's seat 1 (<c>0x5E5B20(0, 1)</c>, the ride view of <c>0x4668C0</c>), with the pointer look of the ride view.
	/// </summary>
	void EnterView( ushort thing );
	/// <summary>Leaves any ride or walk camera (<c>0x50B4E0</c>), back to the park camera.</summary>
	void LeaveView();
}

/// <summary>The cutscene thing's save: the <c>CUTS</c> body's thing playing now (<c>0xA9D230</c>), 0 for none.</summary>
public sealed record ThemeParkIncCutsceneSnapshot( ushort AnimThing );

/// <summary>A camera flight's end as message 0x29 carries it: the thing and the result (script variable 0 when the end ran: 0 after the script's own end, the trigger after a skip).</summary>
public readonly record struct ThemeParkIncCameraEnd( ushort Thing, int Result, int Mode );

/// <summary>
/// Theme Park Inc's cutscene thing (park thing class 22, stream tag <c>CUTS</c>; docs/reverse/TPI-MISSIONS.md §5): a camera flight is the ride view on the target thing's seat 1 while the thing's own
/// script plays the flight. The start writes the trigger into the script's variable 0 (the camera path's script plays its <c>M</c> clip and sets variable 1 when the clip is done); each thing update
/// ends the flight once variable 1 is 1 or a skip is pending, and the end posts message 0x29 to the mission controller. During the flight the park clock runs at the mode's rate (1.0, 0.7 in mode 1)
/// and the interface is hidden under a full-screen input window; mode 2 (the zone arrival) is skippable.
/// </summary>
// [BIN:TPI-EXE:0x5D0090 cutscene thing] the thing base 0x637100(1, 0x16); the park factory builds it after the challenge system and before the mission controller (0x64860F) and keeps its id at park+0x1EAB72;
// the flight's state is global: 0xA9D230 the thing (u16), 0xA9D254 skip pending, 0xA9D255 skippable, 0xA9D256 the saved speed word, 0xA9D258 the mission window's countdown; +0x24 the mode
public sealed class ThemeParkIncCutscene
{
	/// <summary>The mode of the zone-arrival flyover: skippable.</summary>
	public const int ArrivalMode = 2;
	/// <summary>The mode of the Water World intro (4798): unskippable, at 0.7 of the clock.</summary>
	public const int IntroMode = 1;
	/// <summary>The speed word the start stores (<c>0xA10210</c>), which the clock is not re-derived from until the end.</summary>
	public const short FlightSpeedWord = 100;

	private readonly Action<ThemeParkIncCameraEnd> finished;

	/// <param name="finished">Message 0x29 (the mission controller's camera end), posted by every end.</param>
	public ThemeParkIncCutscene( Action<ThemeParkIncCameraEnd> finished ) => this.finished = finished ?? throw new ArgumentNullException( nameof( finished ) );

	/// <summary>The world the flights use; null in a headless economy.</summary>
	public IThemeParkIncCutsceneWorld? World { get; set; }

	/// <summary><c>0xA9D230</c>: the thing whose flight plays, 0 for none.</summary>
	public ushort AnimThing { get; private set; }
	/// <summary><c>0xA9D254</c>: a skip is pending; the next update ends the flight.</summary>
	public bool SkipPending { get; private set; }
	/// <summary><c>0xA9D255</c>: the flight may be skipped (mode 2).</summary>
	public bool Skippable { get; private set; }
	/// <summary><c>+0x24</c>: the flight's mode (0 obstacle and completion cameras, 1 the Water World intro, 2 the zone arrival).</summary>
	public int Mode { get; private set; }
	/// <summary><c>0xA9D258</c>: the mission window's countdown after a mode-2 end (1 at the end, 2 the same update, 3 the next: the window opens and it is 0 again).</summary>
	public int WindowCountdown { get; private set; }

	/// <summary>
	/// The park clock's rate while a flight that this session started is on (<c>0xA03BC0</c>'s rate: 1.0, or 0.7 in mode 1), null otherwise. A flight restored from a save runs at the speed it was
	/// saved with: the load does not set the rate.
	/// </summary>
	public double? ClockRate { get; private set; }

	/// <summary>Whether the full-screen input window of the flight is up (<c>+0x28</c>): the interface is hidden and takes no input but the skip.</summary>
	public bool OverlayOpen { get; private set; }

	/// <summary>Whether a flight plays (<see cref="AnimThing"/> is not 0).</summary>
	public bool Flying => AnimThing != 0;

	/// <summary>The mission window's requests so far (the countdown reaching 3, <c>0xA61810-&gt;0x55A7F0</c>). Not saved.</summary>
	public int MissionWindowRequests { get; private set; }

	/// <summary>The zone switches the 1750 gate's end asked for (<c>0x6A26B0(2, 0)</c>), which a later package performs. Not saved.</summary>
	public List<int> ZoneSwitchRequests { get; } = new();

	/// <summary>The panels the flight asked for (0x26D at the 1703 lake's start), for the mission window's package. Not saved.</summary>
	public List<int> PanelRequests { get; } = new();

	/// <summary>Called at a mode-2 end before message 0x29: the mission controller's <c>0x5F1760</c> (the first briefing after the intro).</summary>
	public Action? ArrivalEnded { get; set; }

	/// <summary>The current zone index for the 1750 gate's end (<c>0xA06558 +0x14</c>: 0 Arabian, 1 Science, 2 Water).</summary>
	public Func<int>? ViewedZone { get; set; }

	/// <summary>
	/// Plays the flight of <paramref name="thing"/> (<c>0x5D0400</c>): refused while another plays and for thing 0 (no thing); else the thing becomes the flight's, the camera goes on its seat 1, the clock runs at the mode's rate,
	/// the input window opens and the thing's script variable 0 becomes <paramref name="trigger"/>.
	/// </summary>
	// [BIN:TPI-EXE:0x5D0400 cutscene play] [0xA9D230] != 0: logs "Attempt to play anim when one is already playing! Haroogha Haroogha" and returns false; else 0xA9D255 = 0xA9D254 = 0 and 0xA9D258 = 0, the cursor
	// centred (0x509C20), 0xA9D230 = thing, 0x50B4E0 (leave any ride or walk camera), the HUD control 0x40's state kept in +0x2C, 0x5E5B20(0, 1) on the thing (seat 1, 0x4668C0), the camera words 0xBE2104/D4/D8
	// kept in +0x18/+0x1C/+0x20; +0x24 = mode; mode 1: fog (550, 599), 0xBE2104 = 600, rate 0.7 (float 0x3F333333); mode 2: fog (135, 150), 0xBE2104 = 450, 0xA9D255 = 1, rate 1.0; else fog (135, 150), 450, rate 1.0;
	// 0xA03BC0's rate set (0x41C000) with the speed word kept in 0xA9D256 and 0xA10210 = 100 ("It was: %f", "It is now: %f"); the interface locked (0x50AF20, 0x503BA0, 0x7BAE30), the input window
	// 0x84BE91(1, 1, 0xB71B07, 0, 0, 0x7FF, 0x5FF) in +0x28 with the procedure 0x5D03B0 when skippable; "playing anim %d"; 0x6B0780(0x6B0740(thing+0x2C), 0, trigger); the 1703 lake (0x647850(0x6A7) == thing) opens
	// panel 0x26D; returns true
	public bool Play( ushort thing, int trigger, int mode )
	{
		if ( AnimThing != 0 )
		{
			Log?.Trace( "Attempt to play anim when one is already playing! Haroogha Haroogha" );
			return false;
		}
		// [BIN:TPI-EXE:0x648DF0 thing table] slot 0 of the thing table 0xAA18B0 is never a thing (the reset clears its pointer and the free list starts at slot 1), and 0x5D0400 has no refusal for it:
		// with thing 0 it stores 0xA9D230 = 0, which no update ends (0x5D02F0 tests it), reads the null slot (0x5D0448) and faults in 0x5E5B20 (thing +0x16)
		// [EXT:original-undefined-reads] OpenTPW refuses a flight of thing 0 as it refuses a second flight: nothing changes and no input window opens (a mission controller that asked for it
		// keeps waiting for message 0x29, so that mission cannot complete)
		if ( thing == 0 )
			return false;
		Skippable = false;
		SkipPending = false;
		WindowCountdown = 0;
		AnimThing = thing;
		World?.EnterView( thing );
		Mode = mode;
		Skippable = mode == ArrivalMode;
		// the rate is the single-precision constant widened to the clock's double
		ClockRate = mode == IntroMode ? (double)0.7f : 1.0;
		OverlayOpen = true;
		Log?.Trace( $"playing anim {trigger}" );
		SetVariable( World?.ScriptOf( thing ), 0, trigger );
		if ( World?.TypeOf( thing ) == 1703 )
			PanelRequests.Add( 0x26D );
		return true;
	}

	/// <summary>
	/// A skip (<c>0x5D0290</c>: the input window's Escape, the right button's release, the menu key): only a skippable flight takes it; the next update ends the flight.
	/// </summary>
	// [BIN:TPI-EXE:0x5D0290 skip] 0xA9D255 set: 0xA9D254 = 1; reached from the input window's procedure 0x5D03B0 (message 0x1000A with key 0x1B), the window procedure's WM_RBUTTONUP (0x4D0A38, jump table 0x4D1110)
	// and the key action of the binding "menu" (0x42FCD0, which skips instead of opening the menu while a flight plays)
	public void RequestSkip()
	{
		if ( Skippable )
			SkipPending = true;
	}

	/// <summary>The intro window's end of the flight (<c>0x5D02C0</c>), whatever its mode.</summary>
	// [BIN:TPI-EXE:0x5D02C0 forced skip] [0xA9D230] != 0: 0xA9D254 = 1; called by the intro object 0xA5EB30 (0x542E00, 0x5435EF)
	public void ForceSkip()
	{
		if ( AnimThing != 0 )
			SkipPending = true;
	}

	/// <summary>
	/// The cutscene thing's update (<c>0x5D02F0</c>), once per world pass after the mission controller's: the flight ends once its script's variable 1 is 1 or a skip is pending; then the mission window's
	/// countdown counts.
	/// </summary>
	// [BIN:TPI-EXE:0x5D02F0 cutscene update] [0xA9D230] != 0: inst = 0x6B0740(thing+0x2C); 0x6B0C90(inst, 1) == 1 sets 0xA9D254; 0xA9D254 set: the end 0x5D0840(inst); then [0xA9D258] != 0: ++, and at 3 it is 0 again
	// and the mission window opens (0xA61810->0x55A7F0)
	public void Update()
	{
		if ( AnimThing != 0 )
		{
			var script = World?.ScriptOf( AnimThing );
			if ( Variable( script, 1 ) == 1 )
				SkipPending = true;
			if ( SkipPending )
				End( script );
		}
		if ( WindowCountdown == 0 )
			return;
		WindowCountdown++;
		if ( WindowCountdown < 3 )
			return;
		WindowCountdown = 0;
		MissionWindowRequests++;
	}

	// [BIN:TPI-EXE:0x5D0840 cutscene end] the HUD control 0x40 restored from +0x2C; the saved speed word (0xA9D256, when not 0) clamped to 10..400 into 0xA10210 and the rate to word × 0.0125 (0x93A23C), 0xA9D256 = 0;
	// with the input window (+0x28): its destruction (0x85049F(.., 4, 0, 0)), the camera words back from +0x18/+0x1C/+0x20, 0x50B4E0 (the ride view left) and 0x50AC90 (the interface shown); result =
	// 0x6B0C90(inst, 0); 0xA9D230 = 0; 0x6B0780(inst, 0, 0) and (inst, 1, 0); the overview map's check (0xA6E158, presentation); +0x24 == 2: the controller's 0x5F1760, 0xA9D258 = 1, +0x24 = 0; then message
	// 0x29 (0x436330) with the thing and the result (0x5D0996); the current zone 1 with a thing of type 0x6D6 (1750): 0x6A26B0(2, 0); then the fog and the refreshes (0x6F2940, 0x4A5E30, 0x648C90, 0x624960)
	private void End( RideVM? script )
	{
		if ( OverlayOpen )
		{
			OverlayOpen = false;
			World?.LeaveView();
		}
		ClockRate = null;
		// 0xA9D254 stays set: only the next start clears it
		var result = Variable( script, 0 );
		var thing = AnimThing;
		AnimThing = 0;
		SetVariable( script, 0, 0 );
		SetVariable( script, 1, 0 );
		var mode = Mode;
		if ( Mode == ArrivalMode )
		{
			ArrivalEnded?.Invoke();
			WindowCountdown = 1;
			Mode = 0;
		}
		finished( new ThemeParkIncCameraEnd( thing, result, mode ) );
		if ( ViewedZone?.Invoke() == 1 && World?.TypeOf( thing ) == 1750 )
			ZoneSwitchRequests.Add( 2 );
	}

	// [BIN:TPI-EXE:0x6B0780 script variable setter] nothing without the subsystem, without an instance or for an index outside 0..+0x8C; else the instance's array (+0x1C) takes the value
	private static void SetVariable( RideVM? script, int index, int value )
	{
		if ( script != null && index >= 0 && index < script.Variables.Length )
			script.Variables[index] = value;
	}

	// [BIN:TPI-EXE:0x6B0C90 script variable getter] 0 without the subsystem, without an instance or for an index outside 0..+0x8C
	private static int Variable( RideVM? script, int index ) => script != null && index >= 0 && index < script.Variables.Length ? script.Variables[index] : 0;

	/// <summary>The zone-arrival flyover's camera path type for zone index <paramref name="zone"/>: 3798 Arabian, 1798 Science, 2798 Water, 4798 the Water World intro; null for another index.</summary>
	// [BIN:TPI-EXE:0x6A45B3 arrival cameras] the jump table 0x6A46CC by the zone index: 0 -> 0x6A463D (0xED6), 1 -> 0x6A45BA (0x706), 2 -> 0x6A4602 (0xAEE), 3 -> 0x6A4674 (0x12BE)
	public static int? ArrivalCameraType( int zone ) => zone switch { 0 => 3798, 1 => 1798, 2 => 2798, 3 => 4798, _ => null };

	/// <summary>
	/// The game state's zone-arrival flyover (<c>0x6A43D8</c>): for a world entered without a save, or with the career's own zone save, the zone's camera path flies (anim 1, mode 2; the Water World intro
	/// mode 1) while the park clock is still on its first day; later days log "GameStateGame: RELEASE - NOT Intro, days &gt; 1, CUTSCENE DISABLED!!!". Answers whether a flight started.
	/// </summary>
	/// <param name="days">Whole days of the park clock (<c>0x608C70</c> over <c>0xC92A69C000</c>).</param>
	/// <param name="findAttraction">The first attraction thing of a type (<c>0x647850</c>), 0 for none.</param>
	/// <param name="introPlayed">The intro's "has played already" byte (<c>0xA5EB41</c>), read for the intro zone only.</param>
	// [BIN:TPI-EXE:0x6A44D5 arrival flyover] the flag 0xB8C02C (set by the zone entry, 0x6A3FC0 or 0x6A3D08 for a career zone save; cleared at 0x6A44AF for a loaded game without one) is required; zone index != 3:
	// days = 0x608C70 / 0xC92A69C000 (__alldiv); days >= 1 logs "GameStateGame: RELEASE - NOT Intro, days > 1, CUTSCENE DISABLED!!!" and "Man, time was %i..." and plays nothing; zone 3 with 0xA5EB41 set logs
	// "GameStateGame: RELEASE - INTRO, has played already, CUTSCENE DISABLED!!!" and the intro object plays (0x401CA8(1)); else the flag clears and the zone's camera path (0x647850) plays 0x5D0400(thing, 1, 2), the intro's
	// (thing, 1, 1); before the 1798 path 0xA14558 = 500.0 (a camera word, presentation)
	public bool PlayArrival( int zone, long days, Func<int, ushort> findAttraction, bool introPlayed = false )
	{
		ArgumentNullException.ThrowIfNull( findAttraction );
		if ( zone != 3 && days >= 1 )
		{
			Log?.Trace( "GameStateGame: RELEASE - NOT Intro, days > 1, CUTSCENE DISABLED!!!" );
			Log?.Trace( $"Man, time was {days}..." );
			return false;
		}
		if ( zone == 3 && introPlayed )
		{
			Log?.Trace( "GameStateGame: RELEASE - INTRO, has played already, CUTSCENE DISABLED!!!" );
			return false;
		}
		if ( ArrivalCameraType( zone ) is not { } type || findAttraction( type ) is var thing && thing == 0 )
			return false;
		return Play( thing, 1, zone == 3 ? IntroMode : ArrivalMode );
	}

	internal ThemeParkIncCutsceneSnapshot Capture() => new( AnimThing );

	/// <summary>
	/// Restores the saved thing (<c>0x5D0160</c>): only the flight's thing comes back. Mode, skip, rate, camera and input window are not saved, so a flight restored mid-way waits for its script's variable 1
	/// and then ends without leaving a view, posting message 0x29 as any end. The skip flag starts clear, as in the original's first load of a process.
	/// </summary>
	// [BIN:TPI-EXE:0x5D0160 cutscene save] versions 0 and 1: the thing base and the u16 0xA9D230; the park loader clears 0xA9D230 before reading it (0x64D63C); +0x24, +0x28 and the speed word are not saved
	// [BIN:TPI-EXE:0x64D62E cutscene load] the loader allocates the thing (0x638120(0x30)) and sets only 0xA9D230 and +0x28 to 0: the constructor 0x5D0090, which clears 0xA9D254, 0xA9D255, 0xA9D256 and 0xA9D258, does not run,
	// and only a start (0x5D0400) clears them otherwise. A load in a new process finds them at 0; a load in the same session keeps the last flight's, and 0xA9D254 stays 1 after any end, so there a restored
	// flight ends at its first update. OpenTPW's load builds a new park with a new cutscene thing, which is the new process's load: the globals are 0
	internal void Restore( ThemeParkIncCutsceneSnapshot snapshot )
	{
		AnimThing = snapshot.AnimThing;
		// the globals 0xA9D254/0xA9D255 and +0x24 as a new process's load finds them
		Mode = 0;
		Skippable = false;
		SkipPending = false;
		ClockRate = null;
		OverlayOpen = false;
	}
}
