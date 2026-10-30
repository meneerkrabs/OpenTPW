namespace OpenTPW;

/// <summary>
/// The multipliers of a brolly shop's attraction score, read from its .sam for the umbrellas extension's own objects only (docs/UMBRELLAS.md, "Demand"): <c>WeatherModifier.Rain/Storm/Snow/HeatWavePercentage</c> (0: no change)
/// and <c>GenerationModifier.KidScorePercentage</c> (every Theme Park World guest is a kid).
/// </summary>
// [BIN:TPI-EXE:0x6101e0 score multipliers] the weather word at type +0xF0 (snow), +0xF4 (rain), +0xF8 (storm), +0xFC (heat wave) and the generation word at +0x110 (kid), +0x114, +0x118
public readonly record struct UmbrellaShopTerms( int RainPercentage, int StormPercentage, int SnowPercentage, int HeatWavePercentage, int KidScorePercentage )
{
	/// <summary>The percentage of weather state <paramref name="weather"/> (<see cref="UmbrellaRules.WeatherHeatWave"/> to <see cref="UmbrellaRules.WeatherSnow"/>); 0 for none.</summary>
	public int PercentageOf( int weather ) => weather switch
	{
		UmbrellaRules.WeatherHeatWave => HeatWavePercentage,
		UmbrellaRules.WeatherRain => RainPercentage,
		UmbrellaRules.WeatherStorm => StormPercentage,
		UmbrellaRules.WeatherSnow => SnowPercentage,
		_ => 0
	};
}

/// <summary>What the weather step of the umbrella rule does to a guest on one of its updates (<see cref="UmbrellaRules.WeatherAction"/>).</summary>
public enum UmbrellaAction
{
	/// <summary>Nothing.</summary>
	None,
	/// <summary>An open umbrella (4) gets a close scheduled (state 1).</summary>
	ScheduleClose,
	/// <summary>A closed umbrella (2) gets an opening scheduled (state 3).</summary>
	ScheduleOpen,
	/// <summary>A pending close (1) is cancelled: back to open (4).</summary>
	CancelClose,
	/// <summary>A pending open (3) is cancelled: back to closed (2).</summary>
	CancelOpen,
	/// <summary>A pending change (1 or 3) is carried out when it is due and the guest is not inside an attraction.</summary>
	CarryOut
}

/// <summary>
/// The umbrella rules of Theme Park Inc as the umbrellas extension has them for Theme Park World guests (docs/UMBRELLAS.md): the weather states, the shop's score (the gate, the weather and generation percentages, the balloon factor with its
/// zero quirk), the day gate, the five umbrella states and what the weather does with them, the reaction delay and the colour of a new umbrella. Pure functions over plain numbers; the guest simulation applies them
/// (<see cref="GuestSimulation"/>). The labels: <c>[BIN:TPI-EXE:0x...]</c> for the traced rule, <c>[EXT:umbrellas-*]</c> for OpenTPW's own additions, <c>APPROX:UMB-nnn</c> where it guesses.
/// </summary>
public static class UmbrellaRules
{
	/// <summary><c>UsageInfo.AppearanceEffect</c> of a brolly shop: the guest gets an open umbrella (1 is a balloon and 2 a costume).</summary>
	public const int ShopAppearanceEffect = 3;

	/// <summary>No umbrella (the guest's own constructor value; nothing writes 0 again).</summary>
	public const int StateNone = 0;
	/// <summary>Open, and about to close (the close is scheduled and due at <see cref="Guest.UmbrellaDueTurn"/>).</summary>
	public const int StateClosing = 1;
	/// <summary>Owned and closed: not drawn.</summary>
	public const int StateClosed = 2;
	/// <summary>Closed, and about to open (due at <see cref="Guest.UmbrellaDueTurn"/>).</summary>
	public const int StateOpening = 3;
	/// <summary>Open.</summary>
	public const int StateOpen = 4;

	/// <summary>Theme Park Inc's weather states (the weather thing's word <c>+0x70</c>): 1 heat wave, 2 rain, 3 storm, 4 snow; anything else is none.</summary>
	public const int WeatherNone = 0, WeatherHeatWave = 1, WeatherRain = 2, WeatherStorm = 3, WeatherSnow = 4;

	/// <summary>The sprite slots of the umbrella set (blue, rainbow, red and white), the modulus of the colour draw.</summary>
	// [BIN:TPI-EXE:0x690370 sprite variant draw] the draw is (world draw >> 2) modulo the slot count of the sprite bank (the word at +0x21E of the bank record: 3 for Spr_um)
	public const int Colours = 3;

	/// <summary><c>PeepInfo.WeatherModifierFrequency</c>: the weather rule runs while the guest's age in game days is a multiple of it.</summary>
	// [BIN:TPI-EXE:0xa01b60 weather frequency] shipped Standard.sam value 4 ("Modifiers get applied once every X days")
	public const int WeatherModifierFrequency = 4;
	/// <summary><c>PeepInfo.SlowestWeatherChangeResponse</c>: the reaction delay is below this many guest updates (an update is four park turns).</summary>
	// [BIN:TPI-EXE:0xa01b64 slowest response] shipped Standard.sam value 3
	public const int SlowestWeatherChangeResponse = 3;
	/// <summary><c>PeepInfo.DiscardBalloonPctModifier</c>: percent that scales the balloon's share of life in the score cut and in the reaction delay.</summary>
	// [BIN:TPI-EXE:0xa01b68 discard balloon percent] shipped Standard.sam value 100
	public const int DiscardBalloonPctModifier = 100;
	/// <summary>The life of a fresh full balloon, the divisor of its share (the word at <c>0x9d0610</c>, 255).</summary>
	public const int BalloonLifeScale = GuestAppearance.MaximumBalloonLife;

	// ---- Weather ---------------------------------------------------------------------------------------------------

	/// <summary>
	/// Theme Park World's weather as Theme Park Inc's state: lightning is a storm, snow is snow, rain without lightning is rain, else none. There is no heat wave in Theme Park World.
	/// </summary>
	// [EXT:umbrellas-weather-map] the mapping of ParkWeather's flags to the weather thing's word +0x70 of Theme Park Inc (the plan's mapping: Rain 2, Lightning 3, Snow 4)
	public static int WeatherStateOf( bool rain, bool lightning, bool snow ) => lightning ? WeatherStorm : snow ? WeatherSnow : rain ? WeatherRain : WeatherNone;

	/// <summary>Rain, storm or snow: the weather the rule treats as bad (the umbrella opens; the shop sells).</summary>
	public static bool IsBadWeather( int weather ) => weather is WeatherRain or WeatherStorm or WeatherSnow;

	// ---- The shop's score ------------------------------------------------------------------------------------------

	/// <summary>
	/// Whether a brolly shop scores 0 outright for a guest: it already has an umbrella (any state), or the weather is not rain, storm or snow.
	/// </summary>
	// [BIN:TPI-EXE:0x61038e umbrella shop gate] type +0x21C == 3: the guest's +0x238 nonzero gives 0; then the weather word +0x70 must be 2, 4 or 3 or the score is 0
	public static bool ShopScoresZero( int weather, int umbrellaState ) => umbrellaState != StateNone || !IsBadWeather( weather );

	/// <summary>
	/// Whether a balloon shop scores 0 for a guest: it holds a balloon (the model, or a life above 0) or has an open or opening umbrella (states 3 and 4). The balloon half is Theme Park Inc's alone (tp.exe's own score
	/// reads no appearance word), so the extension applies it only where it has a brolly shop or an umbrella in the park (<see cref="GuestSimulation"/>).
	/// </summary>
	// [BIN:TPI-EXE:0x6103ec balloon shop gate] type +0x21C == 1: +0x294 (the balloon model) nonzero or +0x298 (its life) above 0 gives 0, then +0x238 of 4 or 3 gives 0
	public static bool BalloonShopScoresZero( bool holdsBalloon, int balloonLife, int umbrellaState ) =>
		holdsBalloon || balloonLife > 0 || umbrellaState is StateOpening or StateOpen;

	/// <summary>The score times a weather or generation percentage over 100, in signed 32-bit arithmetic with the division truncated toward zero.</summary>
	// [BIN:TPI-EXE:0x6109d0 percentage step] imul by the percentage, then the multiply-high sequence 0x51EB851F, sar 5 and the sign fix: a signed division by 100
	public static uint Percent( uint score, int percentage ) => unchecked( (uint)(percentage * (int)score / 100) );

	/// <summary>
	/// The weather step of the score: multiplied by the object's percentage of the current weather when that is nonzero.
	/// </summary>
	// [BIN:TPI-EXE:0x6109d0 weather percentage] state 2 reads +0xF4, 4 reads +0xF0, 1 reads +0xFC and 3 reads +0xF8; a zero percentage leaves the score (the test before each multiply)
	public static uint ApplyWeather( uint score, int weather, in UmbrellaShopTerms terms )
	{
		var percentage = terms.PercentageOf( weather );
		return percentage == 0 ? score : Percent( score, percentage );
	}

	/// <summary>
	/// The percentage a held balloon leaves of an umbrella shop's score, or null for no cut: nothing without a balloon (a model, or a life above 0) or with life 0; a share of 100 - life x 100 / 255 (the balloon's
	/// remaining life as a percent) scaled by <paramref name="discardPercent"/> / 100 when that is nonzero. A share of exactly 0 (a full-life balloon) skips the cut: Theme Park Inc's own quirk, kept.
	/// </summary>
	// [BIN:TPI-EXE:0x610af1 balloon factor] +0x294 or +0x298 set and +0x298 nonzero: factor = 100 - life * 100 / 255 (unsigned); a result of 0 jumps past the cut (je 0x610b59); then DiscardBalloonPctModifier (0xA01B68) nonzero scales it by x / 100 (unsigned)
	public static int? BalloonFactor( bool holdsBalloon, int balloonLife, int discardPercent = DiscardBalloonPctModifier )
	{
		if ( (!holdsBalloon && balloonLife <= 0) || balloonLife == 0 )
			return null;
		var factor = 100 - (int)((uint)balloonLife * 100u / BalloonLifeScale);
		if ( factor == 0 )
			return null;
		if ( discardPercent != 0 )
			factor = (int)((uint)discardPercent * (uint)factor / 100u);
		return factor;
	}

	/// <summary>The score after the balloon's cut: times <see cref="BalloonFactor"/> over 100 (unsigned); unchanged without a cut. The product wraps at 32 bits before the division, as the original's <c>imul ecx, edi</c> does (<c>TPI-EXE:0x610b4a</c>).</summary>
	public static uint ApplyBalloonFactor( uint score, bool holdsBalloon, int balloonLife, int discardPercent = DiscardBalloonPctModifier ) =>
		BalloonFactor( holdsBalloon, balloonLife, discardPercent ) is { } factor ? unchecked( (uint)factor * score ) / 100u : score;

	// ---- The day gate and the states ---------------------------------------------------------------------------------

	/// <summary>
	/// Whether the weather rule runs for a guest of <paramref name="ageTurns"/> park turns: its age in game days, from the turns cut to a multiple of four, is a multiple of <paramref name="frequency"/>. That is
	/// the whole first game day of a visit and then one day in every fourth.
	/// </summary>
	// [BIN:TPI-EXE:0x611050 day gate] ((clock - creation stamp +0x1FC) >> 2 << 2) through the clock's time (0x409854), divided by 864 x 10^9 (a day in 100 ns) and taken modulo WeatherModifierFrequency (0xA01B60); a remainder other than 0 returns before anything is touched
	// [BIN:TPI-EXE:0x608a90 clock conversion] the clock turns the masked turns into 100 ns as scale x turns / 4 x 10^7 (_allmul, _aulldiv by 4, _allmul by 0x989680), and 0x611050 divides by 0xC92A69C000 (a day in 100 ns); the scale word +0x1C starts at 15000 in the constructor (0x607EB9, 0x607E90) and only a saved or changed calendar rate moves it, so a turn is 15000 / 4 = 3750 game seconds (ParkCalendar.SecondsPerTurn), 23.04 turns a day
	public static bool DayGateOpen( long ageTurns, int frequency = WeatherModifierFrequency )
	{
		if ( frequency <= 0 )
			return true;
		var days = (ageTurns >> 2 << 2) * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerDay;
		return days % frequency == 0;
	}

	/// <summary>
	/// What the weather rule does to an umbrella in <paramref name="state"/>: in bad weather a closed one (2) gets an opening scheduled, a pending close (1) is cancelled and a pending open (3) is carried out when due; in clear
	/// weather, or none, an open one (4) gets a close scheduled, a pending open (3) is cancelled and a pending close (1) is carried out when due. Nothing for no umbrella.
	/// </summary>
	// [BIN:TPI-EXE:0x611050 umbrella transitions] bad weather (the jump table 0x611324 for 1..4 skips the first branch): the table 0x611334 by state - 1 sets 4 (0x6111E3), schedules 3 (0x6111EF), calls 0x614010 (0x611206) or adds UmbrellaModifier; the other weather: state 1 calls 0x614010, 3 sets 2 (0x6111FA), 4 schedules 1 (0x61112A)
	public static UmbrellaAction WeatherAction( int state, bool badWeather ) => (badWeather, state) switch
	{
		(true, StateClosed) => UmbrellaAction.ScheduleOpen,
		(true, StateClosing) => UmbrellaAction.CancelClose,
		(true, StateOpening) => UmbrellaAction.CarryOut,
		(false, StateOpen) => UmbrellaAction.ScheduleClose,
		(false, StateOpening) => UmbrellaAction.CancelOpen,
		(false, StateClosing) => UmbrellaAction.CarryOut,
		_ => UmbrellaAction.None
	};

	/// <summary>
	/// The state after entering an attraction: an umbrella with a sprite (open, or open until a due close: states 4 and 1) is closed (2); a closed one (2), a pending open (3, which has no sprite yet) and none stay as they are.
	/// Leaving reopens nothing; the weather rule does, on a qualifying day.
	/// </summary>
	// [BIN:TPI-EXE:0x6142e0 close umbrella] only while the sprite object +0x240 exists: state := 2 and the sprite is destroyed; the caller 0x61A7EA runs it for every nonzero state
	public static int StateAfterEntering( int state ) => state is StateOpen or StateClosing ? StateClosed : state;

	/// <summary>Whether the umbrella has a sprite in <paramref name="state"/> (open, or open until a due close: states 4 and 1).</summary>
	// [BIN:TPI-EXE:0x612ed0 sprite test] the purchase tests +0x240 (the sprite object), which exists exactly in these two states
	public static bool HasSprite( int state ) => state is StateOpen or StateClosing;

	/// <summary>
	/// The walking speed term (the guest's +0xC2: 25 or 50, set by a toilet need, a relief visit or the walk to the booth) for a guest in umbrella <paramref name="state"/>: 0 while the umbrella is open or pending (states 1, 3 and 4);
	/// the term as it is otherwise, and a term of 0 stays 0.
	/// </summary>
	// [BIN:TPI-EXE:0x60b3f0 speed term setter] for a guest (the class byte +2 is 1) with a nonzero term index: the umbrella words 4, 1 and 3 make the index 0 before the word table 0x9CFB20 (0, 25, 50) is read; the callers are the constructor, the relief visit, the needs phase and the walk to the booth
	public static int SpeedTerm( int state, int term ) => term != 0 && state is StateOpen or StateClosing or StateOpening ? 0 : term;

	// ---- The track-cell stand-down -------------------------------------------------------------------------------------

	/// <summary>
	/// Whether a guest standing on a cell whose second-layer track record has <paramref name="recordType"/> (and, for an extension record, whose head record has <paramref name="headType"/>) is held back by the
	/// scheduling and the carrying out of an umbrella change: the record's type is a kart or water origin or one of their extensions (11, 12, 16, 17) and the record the tests end on is a track cell.
	/// </summary>
	// [BIN:TPI-EXE:0x613ea0 track-cell stand-down] both tests of the scheduling (0x67F890 through the tile record's first word, then through an extension's owner word to its head, ending in the track predicate 0x6856D0: types 0xB, 0xD, 0x10, 0x12, 0x19; and 0x685680 on the record of the same cell index: types 0xB, 0xC, 0x10, 0x11) must hold; the carrying out 0x614010 repeats them
	public static bool OnTrackCell( byte recordType, byte headType )
	{
		if ( recordType is not (TrackCellTypes.KartOrigin or TrackCellTypes.Extension or TrackCellTypes.WaterOrigin or 0x11) )
			return false;
		return ParkCellTypes.IsTrackCell( ParkCellTypes.IsTrackExtension( recordType ) ? headType : recordType );
	}

	/// <summary>The stand-down of <see cref="OnTrackCell(byte, byte)"/> for the cell (<paramref name="x"/>, <paramref name="y"/>) of a track layer: an extension record goes on to its origin cell through its parent index.</summary>
	public static bool OnTrackCell( TrackCellLayer tracks, int x, int y )
	{
		var cell = tracks.Get( x, y );
		if ( cell.Type == 0 )
			return false;
		byte head = 0;
		if ( ParkCellTypes.IsTrackExtension( cell.Type ) && cell.Parent >= 0 && cell.Parent < tracks.Width * tracks.Height )
		{
			var (hx, hy) = tracks.CellOf( cell.Parent );
			head = tracks.Get( hx, hy ).Type;
		}
		return OnTrackCell( cell.Type, head );
	}

	// ---- The reaction delay ------------------------------------------------------------------------------------------

	/// <summary>
	/// The guest updates (four park turns each) a scheduled change waits before it is due: a per-guest hash of (guest id, park turn) modulo <paramref name="slowest"/>, lengthened by the held balloon's share of life x
	/// <paramref name="discardPercent"/> / 100 (the delay plus its share of itself). Nothing here draws from any shared random stream.
	/// </summary>
	// [BIN:TPI-EXE:0x613ea0 reaction delay] CRT rand() modulo SlowestWeatherChangeResponse (0xA01B64); with a balloon (+0x294 or +0x298) of life > 0: share = life * 100 / 255, x DiscardBalloonPctModifier / 100 when nonzero, delay += delay * share / 100 (unsigned); the due time is the clock >> 2 plus the delay
	// [BIN:TPI-EXE:0x6a151b rand seed] the one srand of the executable (0x870D2B, the CRT's per-thread seed word +0x14) is srand(GetTickCount()) in the start-up routine (0x6A1514..0x6A151B); the generator is the CRT's linear congruential rand() (0x870D38: x * 0x343FD + 0x269EC3, bits 16..30), shared with 96 other call sites, so the original's delay is a function of the wall-clock start time and of every earlier rand() call, not of the park
	// [APPROX:UMB-001] the original's delay is not reproducible even by the original (time-seeded, one stream shared with the whole game) and OpenTPW takes no draw for presentation or side rules from the shared streams: a SplitMix64 hash of (guest id, park turn) replaces it, with the same bound and the same uniform spread
	public static int Delay( int guestId, long parkTurn, bool holdsBalloon, int balloonLife, int slowest = SlowestWeatherChangeResponse, int discardPercent = DiscardBalloonPctModifier )
	{
		if ( slowest <= 0 )
			return 0;
		var delay = (int)(Mix( (ulong)(uint)guestId << 32 ^ (ulong)parkTurn ) % (uint)slowest);
		if ( (holdsBalloon || balloonLife > 0) && balloonLife != 0 )
		{
			var share = (uint)balloonLife * 100u / BalloonLifeScale;
			if ( share != 0 )
			{
				if ( discardPercent != 0 )
					share = (uint)discardPercent * share / 100u;
				delay += (int)((uint)delay * share / 100u);
			}
		}
		return delay;
	}

	/// <summary>The park turn a change scheduled at <paramref name="parkTurn"/> is due: the update index (the turn >> 2) plus the delay, as a park turn (a multiple of four).</summary>
	public static long DueTurn( long parkTurn, int delay ) => ((parkTurn >> 2) + delay) << 2;

	/// <summary>Whether a scheduled change is due at <paramref name="parkTurn"/>: the update index has reached the due one.</summary>
	// [BIN:TPI-EXE:0x61409c due test] (clock >> 2) below the due word +0x23C returns without a change
	public static bool IsDue( long parkTurn, long dueTurn ) => (parkTurn >> 2) >= (dueTurn >> 2);

	private static ulong Mix( ulong value )
	{
		value += 0x9E3779B97F4A7C15UL;
		value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
		value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
		return value ^ (value >> 31);
	}

	// ---- The colour ------------------------------------------------------------------------------------------------

	/// <summary>The colour (sprite slot) of a new umbrella from one draw of the world generator: the draw shifted right by 2, modulo <see cref="Colours"/>.</summary>
	// [BIN:TPI-EXE:0x612ea9 umbrella purchase] the use routine's AppearanceEffect 3 sets the state 3, draws the colour with 0x40a2b3(0x13, 0) = 0x690370 (world draw >> 2 modulo the bank's slots, 3), then creates the sprite and sets the state 4
	public static int Colour( uint draw ) => (int)((draw >> 2) % Colours);
}
