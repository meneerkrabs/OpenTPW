namespace OpenTPW;

/// <summary>
/// The park's weather: the original's weather thing (model 15), one quality re-rolled each cycle. The quality alone
/// decides the weather (<see cref="Snow"/>, <see cref="Rain"/>, <see cref="Lightning"/>), the drops ramp toward the
/// rain's target, lightning strikes count down and thunder follows a strike a few turns later. The sound side is
/// reproduced, and the drops, snow and bolts are drawn by the CRain port (<see cref="WeatherRenderer"/>).
/// </summary>
// [EXT:forecast-presentation] the PC has no forecast message: event type 2 (the forecast, built by 0x40f240 and broadcast by 0x40fb10) has a single subscriber, the weather thing itself (0x511fa9/0x511fb8, handler 0x5127a0, which only schedules or applies), and no weather text exists in the Language tables, so the forecast banner, wording and cadence are OpenTPW's own (AUDIO-905..907 are extensions); the drawn drops, snow and bolts follow the traced CRain with the approximations AUDIO-900..904
/// <remarks>
/// The sim advances once per park turn (248 ms, <see cref="ParkCalendar.Turn"/>), and the day is the calendar's
/// (<see cref="ParkCalendar.DayIndex"/>). The Mac binary's weather-tick gate is not located; the turn is the cadence
/// the fork's measured 3.99 ticks a second supports.
/// </remarks>
// [BIN:STP-PPC:0x10103138-0x101031ec the forecast and its apply] a forecast is posted DaysOfWarning ahead and applied when its day passes; the roll is scheduled from the last change
// [BIN:STP-PPC:0x101031f0-0x10103298 the roll schedule] a roll happens once the day is past lastChanged + max(0, DaysBetweenChanges - DaysOfWarning)
// [BIN:STP-PPC:0x1010386c-0x101038d4 the roll] a normal roll is Avg - Tol + rand % (2·Tol), or Avg when Tol <= 0; the exceptional branch (rand % 100 < Chance) gives rand % 100
// [BIN:STP-PPC:0x10103998-0x10103a80 converter] snow, rain and lightning bands; when both snow and rain hold, rand & 1 decides (even: snow)
// [BIN:STP-PPC:0x10103a90-0x10103b80 target] intensity = (q − low) × 100 / (high − low) (100 when high = low) with the snow band for snow and the rain band for rain; target drops = ((100 − intensity) × MaxRaindrops) / 100, so snow has drops too (the word the arrival group reads, ARRIVAL-002)
// [BIN:STP-PPC:0x101030fc-0x101033c8 drop ramp] the drops move by SpeedOfChange toward the target and stop once within one step of it; a target of 0 then stops the rain
// [BIN:STP-PPC:0x101033cc-0x101034c4 lightning] while the lightning band holds, the countdown falls by LightningCountdownSpeed each turn and a strike resets it to rand % LightningCountdownFrom; the bolt count resets outside the band
// [BIN:STP-PPC:0x10103438-0x1010346c thunder] a strike sets the thunder countdown to max(0, ThunderStartsThisFarAway − bolts) + rand % ThunderRandomiser when it is idle (bolts fired this period, before the strike)
// [BIN:STP-PPC:0x101034c8-0x10103518 thunder at the top] the thunder plays at the last bolt's top (x, 300, z) when its countdown ends
// [BIN:TPW-EXE:0x512e10 December's season row] December is row 4 of the season roll (season = the FILETIME date's month / 3, tp.exe 0x4f8770: wMonth 1-12, Mac 0x101037D4): its tolerance and chance are DaysOfWarning (+0x4B8) and DaysBetweenChanges (+0x4BC), and its average is the word at 0x7854f4 right after the Seasons array, which is the array's count slot: the .sam loader zeroes it (0x4013e0) and raises it to the highest stored element index + 1 on every value it stores (0x4017a0), so it holds the number of seasons present in the files (4 in every shipped Standard and Online_Standard layer)
// [BIN:TPW-EXE:0x51295c weather roll and apply] everything runs on game-clock timestamps (0x4f8690; the unit is 10^7 per second, 0x4f8770), not day indices: a roll happens when now >= lastChanged (+0x10) + max(0, DaysBetweenChanges - DaysOfWarning) days and no forecast is pending (flag +0x2c); the forecast handler (0x5127a0) stores the pending time now + DaysOfWarning days (+0x20), the quality (+0x28), the flag and mLastForecasted; the apply (now >= +0x20, unless mOverridden +0x64) runs the converter 0x512f00, which sets the quality and lastChanged = now (0x512f5b), and the same tick falls through to the roll test
// [BIN:TPW-EXE:0x511f70 weather constructor] quality = the season's roll (0x512e10), lastChanged = 0, drops, target and lightning 0, wind 90/0.5; the converter 0x512f00 is not called, so the constructed quality has no effect (no rain or snow) and, because lastChanged is 0, the first update rolls a forecast at once, applied DaysOfWarning days later
// [BIN:TPW-EXE:0x4f8690 weather clock] "now" is the calendar's FILETIME base plus floor(rate +0x1c (15000) x world turn +0x1da70c / 4) x 10^7 (0x4f8690 -> 0x5fc530), whole seconds that change only with the park turn, which is ParkCalendar.Seconds (turn x 3750); lastChanged = 0 (the year 1601) lies before every such time, so the first update rolls at once, as the first step does here
// [EXT:weather-stream] the weather's random draws (roll, lightning and thunder countdowns) come from the park's own weather stream; the PC draws them from the world LCG 0x516330 (world +0x1da708 = ror13(s * 0x19660D + 0x3C6EF35F), the rotated value stored, |value| returned; seeded from time(NULL) at world creation 0x51586b, setter 0x516370), shared by about 139 call sites, so reproducing its draw order needs every user of that LCG (docs/DETERMINISM.md)
public sealed class ParkWeather
{
	/// <summary>The height of a bolt's top (<c>FUN_00512c50</c> sky point, the fork's and the Mac's 300).</summary>
	public const float BoltTopHeight = 300;

	private readonly WeatherSettings settings;
	private readonly DeterministicRandom random;
	private readonly bool wintryDecembers;
	private readonly bool alwaysWinter;
	private readonly int groundX;
	private readonly int groundZ;
	private readonly Queue<Vector3> strikes = new();
	private readonly Queue<(Vector3 Top, Vector3 Ground)> boltTops = new();
	private bool started;
	private long lastTurn;
	private long lastChanged;
	/// <summary>Game seconds (<see cref="ParkCalendar.Seconds"/>) from which the next forecast may be rolled (the original's lastChanged + max(0, DaysBetweenChanges - DaysOfWarning) days).</summary>
	private long nextRollAt;
	private int? pendingQuality;
	/// <summary>Game seconds at which the pending forecast applies (the pending time +0x20: the roll's time + DaysOfWarning days).</summary>
	private long applyAt;
	private int lightningCountdown;
	private int bolts;
	private int thunderCountdown;
	private Vector3 lastTop;

	/// <summary>
	/// Raised once for every weather the simulation creates, so a host can attach presentation (the visual
	/// renderer) without the simulation knowing it. Not part of snapshots: the visual side is rebuilt with the weather.
	/// </summary>
	public static event Action<ParkWeather>? Created;

	/// <summary>
	/// The park's weather keys (<c>Standard.sam</c>, layered over the global ones). <paramref name="wintryDecembers"/> ([EXT:winter-weather], <c>opentpw.winter-weather</c>, off by default) turns a December roll's rain and thunderstorm
	/// into snow in a park whose own snow band is empty (jungle and fantasy; see <see cref="ApplyQuality"/>). <paramref name="alwaysWinter"/> ([EXT:winter-always], <c>opentpw.winter-always</c>, off by default) makes every roll of the
	/// year December's (<see cref="Roll"/>) and wintry, in every park that can snow from it (jungle, fantasy and Halloween; not space, which has neither a rain nor a snow band); it implies the wintry Decembers, so both on is the same as it alone.
	/// </summary>
	public ParkWeather( SamSettings standard, ulong seed, int cellsX, int cellsY, bool wintryDecembers = false, bool alwaysWinter = false )
	{
		this.wintryDecembers = wintryDecembers;
		this.alwaysWinter = alwaysWinter;
		settings = WeatherSettings.From( standard );
		random = new DeterministicRandom( seed );
		groundX = cellsX * 10;
		groundZ = cellsY * 10;
		Created?.Invoke( this );
	}

	/// <summary>The quality, 1 to 100 (the weather thing's <c>mQuality</c>).</summary>
	public int Quality { get; private set; }
	public bool Snow { get; private set; }
	public bool Rain { get; private set; }
	/// <summary>Whether the quality is in the lightning band (the thing's flag at +0x38).</summary>
	public bool Lightning { get; private set; }
	/// <summary>Drops on screen now, ramped toward the target.</summary>
	public int CurrentDrops { get; private set; }
	/// <summary>The drops the rain is ramping to (<c>+0x34</c>).</summary>
	public int TargetDrops { get; private set; }
	/// <summary>How long a drawn bolt lives, in milliseconds (<c>WeatherEffects.LightningTime</c>, 2000 in the shipped Standard.sam).</summary>
	// [DATA:Standard.sam:WeatherEffects.LightningTime] 2000 ms in every shipped layer; presentation only, never read by the simulation
	public int LightningTimeMilliseconds => settings.LightningTime;
	/// <summary>The most drops the renderer may draw (<c>WeatherEffects.MaxRaindrops</c>).</summary>
	public int MaxRaindrops => settings.MaxRaindrops;
	/// <summary>The level-driven rain voice (0 to 100): the current drops over the cap, in any weather band until the drops reach 0
	/// (the Mac's ramp sends it at 0x103394..0x1033a4 regardless of the band; the snow stop at 0x103ce0 ends a separate voice).</summary>
	public int RainLevel => settings.MaxRaindrops > 0 ? Math.Clamp(CurrentDrops * 100 / settings.MaxRaindrops, 0, 100) : 0;

	/// <summary>Brings the weather up to <paramref name="tick"/> of the park's clock; each new park turn is one weather step.</summary>
	public void AdvanceTo( long tick )
	{
		var turn = ParkCalendar.Turn( tick );
		if ( !started )
		{
			started = true;
			lastTurn = turn;
			CurrentDay = ParkCalendar.DayIndex( tick );
			// [BIN:TPW-EXE:0x511f70 weather constructor] the constructed quality is the season's roll but is never converted (no rain or snow), and lastChanged is 0, so the first update rolls a forecast at once
			Quality = Roll( tick );
			lastChanged = 0;
			nextRollAt = 0;
			return;
		}
		while ( lastTurn < turn )
		{
			lastTurn++;
			Step( ParkCalendar.TickOfTurn( lastTurn ) );
		}
	}

	/// <summary>The quality the forecast says is coming (rolled, applied when its day comes), or null.</summary>
	public int? Forecast => pendingQuality;

	/// <summary>The day the pending forecast applies on; valid while <see cref="Forecast"/> holds one.</summary>
	internal long ForecastApplyDay => applyAt / ParkCalendar.SecondsPerDay;

	/// <summary>The day index the weather's schedule last stepped to (the forecast countdown's "now").</summary>
	internal long CurrentDay { get; private set; }

	/// <summary>The forecast's warning distance in days (<c>Weather.DaysOfWarning</c>).</summary>
	internal int DaysOfWarning => settings.DaysOfWarning;

	/// <summary>Whether the weather has taken its first step, so its schedule is valid.</summary>
	internal bool Started => started;

	/// <summary>
	/// The bands a quality falls in, as the converter (<see cref="ApplyQuality"/>) reads them. Both precipitation
	/// bands can hold — the forecast presentation reads this before the apply-time tie-break is drawn.
	/// </summary>
	internal WeatherQualityBands BandsOf( int quality ) => new(
		quality >= settings.SnowLow && quality <= settings.SnowHigh,
		quality >= settings.RainLow && quality <= settings.RainHigh,
		quality >= settings.LightningLow && quality <= settings.LightningHigh );

	/// <summary>
	/// The bands the pending forecast will apply as: <see cref="BandsOf"/> of <see cref="Forecast"/>, except that a forecast the wintry Decembers option will turn into snow (the roll was made in December, a rain band, and the park's own snow band is
	/// empty: see <see cref="ApplyQuality"/>; with the always winter option, a roll of any month) reads as plain snow, so the forecast does not announce thunderstorms and then snow. Default bands while no forecast is pending.
	/// </summary>
	// [EXT:winter-weather] the forecast follows the option: it says what the converter will make of the quality; [EXT:winter-always] likewise in every month
	internal WeatherQualityBands ForecastBands => pendingQuality is { } quality ? ForecastBandsOf( quality, applyAt ) : default;

	/// <summary>The bands a forecast of <paramref name="quality"/> applying at <paramref name="applyAtSeconds"/> will have (see <see cref="ForecastBands"/>).</summary>
	internal WeatherQualityBands ForecastBandsOf( int quality, long applyAtSeconds )
	{
		var bands = BandsOf( quality );
		return bands.Rain && IsWintryRoll( applyAtSeconds ) && settings.SnowBandIsEmpty ? new WeatherQualityBands( true, false, false ) : AsWinterSnow( bands );
	}

	/// <summary>
	/// The current weather's bands as the forecast presentation shows them: the applied flags, except that with the always winter option on snow with lightning and no rain (Halloween, whose snow band is its lightning band) reads as plain
	/// snow, as <see cref="ForecastBandsOf"/> reads its forecast.
	/// </summary>
	internal WeatherQualityBands CurrentBands => AsWinterSnow( new WeatherQualityBands( Snow, Rain, Lightning ) );

	/// <summary>
	/// [EXT:winter-always] Snow with lightning and no rain reads as plain snow while the always winter option is on, so a Halloween park's winter does not announce thunderstorms all year. The winter shops read that weather as snow too
	/// (<see cref="Winter.WinterRules.WeatherStateOf"/>); the umbrellas and the rain mood keep reading it as a storm. Off, the bands are returned unchanged (main's presentation, December included).
	/// </summary>
	// [EXT:winter-always] presentation only: the flags, the rolls and the pinned runs are untouched
	private WeatherQualityBands AsWinterSnow( WeatherQualityBands bands ) =>
		alwaysWinter && bands is { Snow: true, Lightning: true, Rain: false } ? new WeatherQualityBands( true, false, false ) : bands;

	/// <summary>Whether the always winter option can hold a park with these weather keys on December's row: whether it can snow from that row (see <see cref="HoldsWinterRow"/>). Space cannot.</summary>
	// [EXT:winter-always] the winter shops' all-year demand follows the same test, so space parks keep their month-by-month demand
	public static bool CanSnowFromTheWinterRow( SamSettings standard ) => WeatherSettings.From( standard ).CanSnowFromTheWinterRow;

	/// <summary>
	/// Whether the roll of the forecast applying at <paramref name="applyAtSeconds"/> is wintry: always while the always winter option is on ([EXT:winter-always]), otherwise when the wintry Decembers option is on and the roll was made in
	/// December (the roll is <c>DaysOfWarning</c> days before the apply, so no month needs saving).
	/// </summary>
	private bool IsWintryRoll( long applyAtSeconds ) =>
		alwaysWinter || (wintryDecembers && ParkCalendar.Epoch.AddSeconds( applyAtSeconds - settings.DaysOfWarning * ParkCalendar.SecondsPerDay ).Month == 12);

	/// <summary>Whether the always winter option holds this park's rolls on December's row: it is on and the park can snow from that row (a snow band of its own, or a rain band the wintry conversion turns into snow).</summary>
	// [EXT:winter-always] space has neither band, so December's row would only give it dry lightning all year: its seasons are left alone
	private bool HoldsWinterRow => alwaysWinter && settings.CanSnowFromTheWinterRow;

	/// <summary>
	/// Smoke-only (<c>[EXT:debug-forecast]</c>): posts a forecast of <paramref name="quality"/> the warning
	/// distance ahead, exactly as the natural roll would, so captures can show the forecast presentation. The
	/// countdown, apply and the weather itself still come from the normal machinery.
	/// </summary>
	internal void DebugPostForecast( int quality )
	{
		if ( !started )
			throw new InvalidOperationException( "The weather has not taken its first step, so a debug forecast has no day to apply on." );
		pendingQuality = Math.Clamp( quality, 1, 100 );
		applyAt = lastTurn * ParkCalendar.SecondsPerTurn + settings.DaysOfWarning * ParkCalendar.SecondsPerDay;
	}

	/// <summary>
	/// [EXT:cheats-weather] The weather cheat: a forecast with no warning, applied at once at park tick <paramref name="tick"/> with the bookkeeping of <see cref="Step"/>'s apply (the pending forecast is
	/// replaced, the change is now, and the next roll comes <c>DaysBetweenChanges - DaysOfWarning</c> days later). The kind is set explicitly in every theme, snow too, with no tie-break draw; the drops
	/// ramp toward the target at the usual <c>SpeedOfChange</c> a turn and lightning strikes on its own clock. False while the weather has not taken its first step.
	/// </summary>
	// [EXT:cheats-weather] the original's converter (0x512f00) derives the kind from a rolled quality and the park's bands (snow only where the park's snow band holds a quality, not in the jungle or fantasy); the cheat sets the kind and a fixed quality and drop target instead: clear 100 and none, rain half the drops, a thunderstorm three quarters with lightning, snow half
	internal bool ApplyCheat( CheatWeather kind, long tick )
	{
		if ( !started || !Enum.IsDefined( kind ) )
			return false;
		pendingQuality = null;
		lastChanged = ParkCalendar.Seconds( tick );
		nextRollAt = lastChanged + Math.Max( 0, settings.DaysBetweenChanges - settings.DaysOfWarning ) * ParkCalendar.SecondsPerDay;
		(Quality, Snow, Rain, Lightning, TargetDrops) = kind switch
		{
			CheatWeather.Clear => (100, false, false, false, 0),
			CheatWeather.Rain => (CheatQuality( settings.RainLow, settings.RainHigh, 30 ), false, true, false, settings.MaxRaindrops / 2),
			CheatWeather.Thunderstorm => (CheatQuality( settings.LightningLow, settings.LightningHigh, 10 ), false, true, true, settings.MaxRaindrops * 3 / 4),
			_ => (CheatQuality( settings.SnowLow, settings.SnowHigh, 20 ), true, false, false, settings.MaxRaindrops / 2),
		};
		return true;
	}

	/// <summary>The middle of a band's qualities, or <paramref name="fallback"/> for a band that holds none (jungle snow); always 1..100.</summary>
	private static int CheatQuality( int low, int high, int fallback ) =>
		high < low || high < 1 || low > 100 ? fallback : Math.Clamp( (Math.Max( low, 1 ) + Math.Min( high, 100 )) / 2, 1, 100 );

	/// <summary>Takes the bolt tops whose thunder has come due since the last call (the frame plays them).</summary>
	public IReadOnlyList<Vector3> TakeThunder()
	{
		var due = new List<Vector3>();
		while ( strikes.Count > 0 )
			due.Add( strikes.Dequeue() );
		return due;
	}

	/// <summary>Takes the bolt tops struck since the last call, at the moment of the strike (the frame draws them).</summary>
	public IReadOnlyList<Vector3> TakeBoltTops()
	{
		var struck = new List<Vector3>();
		while ( boltTops.Count > 0 )
			struck.Add( boltTops.Dequeue().Top );
		return struck;
	}

	/// <summary>Moves the bolt tops struck since the last call into <paramref name="into"/> as System.Numerics vectors, allocating nothing.</summary>
	internal void DrainBoltTops( List<System.Numerics.Vector3> into )
	{
		while ( boltTops.Count > 0 )
			into.Add( boltTops.Dequeue().Top.GetSystemVector3() );
	}

	/// <summary>Moves the bolts struck since the last call into <paramref name="into"/> as sky and ground points (System.Numerics vectors, original units), allocating nothing.</summary>
	internal void DrainStrikePoints( List<(System.Numerics.Vector3 Top, System.Numerics.Vector3 Ground)> into )
	{
		while ( boltTops.Count > 0 )
		{
			var strike = boltTops.Dequeue();
			into.Add( (strike.Top.GetSystemVector3(), strike.Ground.GetSystemVector3()) );
		}
	}

	/// <summary>Bound for the pending bolt tops: strikes come at most once a park turn, and hosts drain them per frame.</summary>
	public const int MaximumPendingBoltTops = 64;

	/// <summary>One bolt top with thunder already due but not yet played, in strike order.</summary>
	public readonly record struct ThunderStrikeSnapshot( float X, float Y, float Z );

	/// <summary>
	/// The weather's own-format state for a park save: the draw stream's whole state, the sim's position in
	/// it (started flag, last turn, change/roll/apply schedule), the current quality's converted bands and
	/// drop target, the ramped drops, the lightning/thunder clocks and the thunder strikes still waiting to
	/// be played. Deep-copied on capture; validated against the destination's settings before restore, so a
	/// restore continues the same weather instead of re-rolling from the seed.
	/// </summary>
	public sealed record ParkWeatherSnapshot
	{
		public required ulong RandomState { get; init; }
		public required bool Started { get; init; }
		public required long LastTurn { get; init; }
		/// <summary>Game seconds of the last applied change (0 until the first forecast applies; the schedule is in game seconds, saves from before it held day indices).</summary>
		public required long LastChanged { get; init; }
		/// <summary>Game seconds from which the next forecast may be rolled (0: at the first step).</summary>
		public required long NextRollDay { get; init; }
		/// <summary>The forecast's rolled quality, null while none is pending (<see cref="Forecast"/>).</summary>
		public int? PendingQuality { get; init; }
		/// <summary>The game second the pending quality applies at; 0 while none is pending.</summary>
		public required long ApplyDay { get; init; }
		public required int Quality { get; init; }
		public required bool Snow { get; init; }
		public required bool Rain { get; init; }
		public required bool Lightning { get; init; }
		public required int CurrentDrops { get; init; }
		public required int TargetDrops { get; init; }
		public required int LightningCountdown { get; init; }
		public required int Bolts { get; init; }
		public required int ThunderCountdown { get; init; }
		/// <summary>The last strike's bolt top, the position the pending thunder plays at.</summary>
		public required ThunderStrikeSnapshot LastTop { get; init; }
		public required IReadOnlyList<ThunderStrikeSnapshot> PendingStrikes { get; init; }
		/// <summary>Bound for <see cref="PendingStrikes"/>: at most one thunder per park turn can be due, and frames drain them.</summary>
		public const int MaximumPendingStrikes = 4096;
	}

	/// <summary>The weather's state for a park save (the draw stream, the schedule, the bands and the lightning clocks).</summary>
	public ParkWeatherSnapshot CaptureSnapshot() => new()
	{
		RandomState = random.State,
		Started = started,
		LastTurn = lastTurn,
		LastChanged = lastChanged,
		NextRollDay = nextRollAt,
		PendingQuality = pendingQuality,
		ApplyDay = applyAt,
		Quality = Quality,
		Snow = Snow,
		Rain = Rain,
		Lightning = Lightning,
		CurrentDrops = CurrentDrops,
		TargetDrops = TargetDrops,
		LightningCountdown = lightningCountdown,
		Bolts = bolts,
		ThunderCountdown = thunderCountdown,
		LastTop = new ThunderStrikeSnapshot( lastTop.X, lastTop.Y, lastTop.Z ),
		PendingStrikes = strikes.Select( strike => new ThunderStrikeSnapshot( strike.X, strike.Y, strike.Z ) ).ToArray()
	};

	/// <summary>
	/// Checks a snapshot against this weather's invariants without touching it. A weather that has not started
	/// must be pristine; a started one carries a valid quality, drop counts within the settings' cap, a
	/// non-negative schedule, and the converter's snow/rain exclusions. Uses this instance's settings, so a
	/// snapshot is validated against the destination park's own <c>Standard.sam</c> layer.
	/// </summary>
	public void ValidateSnapshot( ParkWeatherSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		if ( snapshot.PendingStrikes is null )
			throw new InvalidDataException( "Park weather snapshot is inconsistent: missing pending thunder strikes." );
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"Park weather snapshot is inconsistent: {message}." );
		}
		Require( snapshot.PendingStrikes.Count <= ParkWeatherSnapshot.MaximumPendingStrikes, "too many pending thunder strikes" );
		Require( snapshot.PendingStrikes.All( strike => float.IsFinite( strike.X ) && float.IsFinite( strike.Y ) && float.IsFinite( strike.Z ) ), "a bolt top is not finite" );
		Require( float.IsFinite( snapshot.LastTop.X ) && float.IsFinite( snapshot.LastTop.Y ) && float.IsFinite( snapshot.LastTop.Z ), "the last bolt top is not finite" );
		if ( !snapshot.Started )
		{
			Require( snapshot.Quality == 0 && !snapshot.Snow && !snapshot.Rain && !snapshot.Lightning
				&& snapshot.CurrentDrops == 0 && snapshot.TargetDrops == 0
				&& snapshot.LightningCountdown == 0 && snapshot.Bolts == 0 && snapshot.ThunderCountdown == 0
				&& snapshot.LastTurn == 0 && snapshot.LastChanged == 0 && snapshot.NextRollDay == 0 && snapshot.ApplyDay == 0
				&& snapshot.PendingQuality is null && snapshot.PendingStrikes.Count == 0,
				"a weather that has not started must be pristine" );
			return;
		}
		Require( snapshot.Quality is >= 1 and <= 100, "quality outside 1..100" );
		Require( !snapshot.Snow || !snapshot.Rain, "snow and rain at once (the converter keeps them apart)" );
		Require( snapshot.CurrentDrops >= 0 && snapshot.CurrentDrops <= settings.MaxRaindrops
			&& snapshot.TargetDrops >= 0 && snapshot.TargetDrops <= settings.MaxRaindrops,
			$"drop counts outside 0..{settings.MaxRaindrops}" );
		Require( snapshot.LastTurn >= 0 && snapshot.LastChanged >= 0 && snapshot.NextRollDay >= 0 && snapshot.ApplyDay >= 0, "negative schedule" );
		Require( snapshot.NextRollDay >= snapshot.LastChanged, "the next roll precedes the last change" );
		Require( snapshot.PendingQuality is null || snapshot.PendingQuality is >= 1 and <= 100, "pending quality outside 1..100" );
		Require( snapshot.PendingQuality is null || snapshot.ApplyDay >= snapshot.LastChanged, "the forecast applies before the last change" );
		Require( snapshot.LightningCountdown >= 0 && snapshot.LightningCountdown < Math.Max( 1, settings.LightningCountdownFrom ), "lightning countdown outside its band" );
		Require( snapshot.Bolts >= 0 && snapshot.ThunderCountdown >= 0, "negative bolt or thunder count" );
	}

	/// <summary>
	/// A save from before the timestamp schedule holds day indices in <c>LastChanged</c>, <c>NextRollDay</c> and <c>ApplyDay</c>; a current one holds game
	/// seconds. A started weather whose park is at least a day old and whose three values all lie within the days that can have passed plus the
	/// forecast and cycle lengths (<c>DayIndex + DaysOfWarning + DaysBetweenChanges + 1</c>) can only be day indices (game seconds that small would be
	/// under a day after the park's start), so they are multiplied by <see cref="ParkCalendar.SecondsPerDay"/>.
	/// </summary>
	internal ParkWeatherSnapshot MigrateSchedule( ParkWeatherSnapshot snapshot )
	{
		if ( !snapshot.Started || snapshot.LastTurn < 0 || snapshot.LastTurn > long.MaxValue / ParkCalendar.SecondsPerTurn
			|| snapshot.LastTurn * ParkCalendar.SecondsPerTurn < ParkCalendar.SecondsPerDay )
			return snapshot;
		var limit = ParkCalendar.DayIndex( ParkCalendar.TickOfTurn( snapshot.LastTurn ) ) + settings.DaysOfWarning + settings.DaysBetweenChanges + 1;
		if ( Math.Max( snapshot.LastChanged, Math.Max( snapshot.NextRollDay, snapshot.ApplyDay ) ) > limit )
			return snapshot;
		return snapshot with
		{
			LastChanged = snapshot.LastChanged * ParkCalendar.SecondsPerDay,
			NextRollDay = snapshot.NextRollDay * ParkCalendar.SecondsPerDay,
			ApplyDay = snapshot.ApplyDay * ParkCalendar.SecondsPerDay
		};
	}

	/// <summary>
	/// Continues this weather (a fresh instance of the same park, seed and settings) from a validated
	/// snapshot: the draw stream, the schedule, the converted state and the due thunder all resume instead
	/// of re-rolling. Call before the first <see cref="AdvanceTo"/> of the restored level.
	/// </summary>
	public void RestoreSnapshot( ParkWeatherSnapshot snapshot )
	{
		snapshot = MigrateSchedule( snapshot );
		ValidateSnapshot( snapshot );
		random.Restore( snapshot.RandomState );
		started = snapshot.Started;
		lastTurn = snapshot.LastTurn;
		// The countdown's "now" resumes with the schedule: a restore mid-warning reads its true distance
		// on the first frame, before the next turn (248 ms) steps the day.
		CurrentDay = ParkCalendar.DayIndex( ParkCalendar.TickOfTurn( snapshot.LastTurn ) );
		lastChanged = snapshot.LastChanged;
		nextRollAt = snapshot.NextRollDay;
		pendingQuality = snapshot.PendingQuality;
		applyAt = snapshot.ApplyDay;
		Quality = snapshot.Quality;
		Snow = snapshot.Snow;
		Rain = snapshot.Rain;
		Lightning = snapshot.Lightning;
		CurrentDrops = snapshot.CurrentDrops;
		TargetDrops = snapshot.TargetDrops;
		lightningCountdown = snapshot.LightningCountdown;
		bolts = snapshot.Bolts;
		thunderCountdown = snapshot.ThunderCountdown;
		lastTop = new Vector3( snapshot.LastTop.X, snapshot.LastTop.Y, snapshot.LastTop.Z );
		strikes.Clear();
		// The visual bolt queue is presentation-only; a restored weather starts its flashes fresh.
		boltTops.Clear();
		foreach ( var strike in snapshot.PendingStrikes )
			strikes.Enqueue( new Vector3( strike.X, strike.Y, strike.Z ) );
	}

	internal void Step( long tick )
	{
		var now = ParkCalendar.Seconds( tick );
		CurrentDay = ParkCalendar.DayIndex( tick );
		// [BIN:TPW-EXE:0x5128a0 weather apply] a pending forecast applies once now reaches its time: the converter sets the quality and lastChanged = now
		if ( pendingQuality is int next && now >= applyAt )
		{
			pendingQuality = null;
			lastChanged = now;
			// [EXT:winter-weather] the roll that made the forecast was DaysOfWarning days before it applies (the forecast's apply time is the roll's time plus the warning), so its month needs nothing saved
			ApplyQuality( next, IsWintryRoll( applyAt ) );
			nextRollAt = lastChanged + Math.Max( 0, settings.DaysBetweenChanges - settings.DaysOfWarning ) * ParkCalendar.SecondsPerDay;
		}
		// [BIN:TPW-EXE:0x51295c weather roll] with no forecast pending (also in the tick that just applied one) the roll is due at lastChanged + max(0, DaysBetweenChanges - DaysOfWarning) days
		if ( pendingQuality == null && now >= nextRollAt )
		{
			pendingQuality = Roll( tick );
			applyAt = now + settings.DaysOfWarning * ParkCalendar.SecondsPerDay;
		}
		Ramp();
		Bolts();
		TurnStepped?.Invoke( this );
	}

	/// <summary>
	/// Raised at the end of every weather step (one park turn), after the drops and bolts moved. A presentation host (the snow cover, <see cref="SnowCover"/>) follows the weather's own turn
	/// sequence through it; nothing in the simulation subscribes, and it is not part of snapshots.
	/// </summary>
	internal event Action<ParkWeather>? TurnStepped;

	/// <summary>
	/// The season's roll (<c>sub_1037d4</c>): exceptional weather with the season's chance, otherwise the season's average and tolerance. With the always winter option on ([EXT:winter-always]) every month takes December's row (row 4,
	/// one past the table) in a park that can snow from it, so the whole year rolls like a December; the draws are the same in number and kind.
	/// </summary>
	internal int Roll( long tick )
	{
		var month = ParkCalendar.ToDate( tick ).Month;
		// [EXT:winter-always] the season index tp.exe derives from the FILETIME month (0x4f8770, read by the roll 0x512e10) is held at December's, whose row is the word at 0x7854f4 after the Seasons table at 0x7854c4 (12-byte rows)
		var season = HoldsWinterRow ? DecemberSeason : month / 3;
		int average, tolerance, chance;
		if ( season < settings.Averages.Count )
		{
			average = settings.Averages[season];
			tolerance = settings.Tolerances[season];
			chance = settings.Chances[season];
		}
		else
		{
			// [BIN:TPW-EXE:0x512e10 December's row] one row past the table: the average is the array's count slot (0x7854f4)
			average = settings.SeasonCount;
			tolerance = settings.DaysOfWarning;
			chance = settings.DaysBetweenChanges;
		}
		int quality;
		if ( random.Chance( chance ) )
			quality = random.Next( 100 );
		else
			quality = tolerance <= 0 ? average : average - tolerance + random.Next( 2 * tolerance );
		return Math.Clamp( quality, 1, 100 );
	}

	/// <summary>
	/// The converter (<c>sub_103910</c>): the quality's bands, the rain target and the lightning flag. <paramref name="wintryDecember"/> is true only when the wintry Decembers option is on and the roll was made in December
	/// ([EXT:winter-weather]): a quality in the rain band with no snow band of the park's own (jungle and fantasy: snow -1..-1) is then snow, not rain, and without lightning, so the weather is a snowfall instead of a thunderstorm. The
	/// drops follow the rain band's intensity exactly as the storm's did, so the arrival factor is unchanged, and the converter draws nothing for it (the lightning clock's draws stop with the lightning, so the weather stream moves on from then).
	/// </summary>
	// [EXT:winter-weather] OpenTPW's own option; tp.exe's converter (0x512f00) has no such branch, and the original's jungle and fantasy parks never snow
	internal void ApplyQuality( int quality, bool wintryDecember = false )
	{
		// [EXT:winter-always] the always winter option makes every roll a wintry one (IsWintryRoll), through the same conversion, so with both options on nothing is converted twice
		var snowBand = quality >= settings.SnowLow && quality <= settings.SnowHigh;
		var rainBand = quality >= settings.RainLow && quality <= settings.RainHigh;
		var snow = snowBand && (!rainBand || random.Next( 2 ) == 0);
		// only a park whose own snow band holds no quality at all (jungle and fantasy: -1..-1); the Halloween park snows by its own band, so its December rolls are left as they are
		var wintry = (wintryDecembers || alwaysWinter) && wintryDecember && settings.SnowBandIsEmpty && rainBand;
		snow |= wintry;
		Quality = quality;
		Snow = snow;
		Rain = rainBand && !snow;
		Lightning = !wintry && quality >= settings.LightningLow && quality <= settings.LightningHigh;
		TargetDrops = 0;
		// Both bands compute the target the same way from their own range (0x103a90..0x103afc snow, 0x103b18..0x103b80 rain); only rain starts the rain voice.
		if ( Snow || Rain )
		{
			// a wintry December's snow has no band of its own: it takes the rain band's range, so its drops are the storm's
			var snowRange = Snow && !wintry;
			var low = snowRange ? settings.SnowLow : settings.RainLow;
			var width = (snowRange ? settings.SnowHigh : settings.RainHigh) - low;
			var intensity = width == 0 ? 100 : (quality - low) * 100 / width;
			TargetDrops = Math.Min( (100 - intensity) * settings.MaxRaindrops / 100, settings.MaxRaindrops );
		}
	}

	/// <summary>One step of the drops toward the target: a move of SpeedOfChange, none once within one step (so 588 settles at 580).</summary>
	private void Ramp()
	{
		var step = settings.SpeedOfChange;
		var difference = TargetDrops - CurrentDrops;
		if ( Math.Abs( difference ) >= step )
			CurrentDrops = Math.Max( 0, CurrentDrops + Math.Sign( difference ) * step );
		else if ( TargetDrops == 0 )
			CurrentDrops = 0;
	}

	/// <summary>The lightning countdown, the bolts, and then the thunder countdown of one step.</summary>
	// [BIN:STP-PPC:0x101033CC weather lightning] the lightning countdown drops by LightningCountdownSpeed and strikes only below 0 (bge skips at 0); a strike arms the thunder only while its countdown is at most 0, at max(0, ThunderStartsThisFarAway - bolts) + rand % ThunderRandomiser (+0x4E8/+0x4EC)
	// [BIN:STP-PPC:0x101034D8 weather thunder] after the lightning, every step (also outside the lightning band) the thunder countdown drops by one and the thunder plays at the stored top only when it reaches exactly 0: a computed 0 never plays, a 1 plays on the strike's own step
	private void Bolts()
	{
		if ( Lightning )
		{
			lightningCountdown -= settings.LightningCountdownSpeed;
			if ( lightningCountdown < 0 )
			{
				var x = 1 + random.Next( groundX );
				var z = 1 + random.Next( groundZ );
				lastTop = new Vector3( x + random.Next( 101 ) - 50, BoltTopHeight, z + random.Next( 101 ) - 50 );
				// The bolt shows at the strike itself; its thunder follows later through the strikes queue.
				// [BIN:TPW-EXE:0x512C50 strike points] the original's bolt runs from the ground point (x, 0, z) it drew to the sky point 300 up and offset by up to 50 in x and z (0x512880 hands both to 0x580320), not straight down from the sky point
				boltTops.Enqueue( (lastTop, new Vector3( x, 0, z )) );
				while ( boltTops.Count > MaximumPendingBoltTops )
					boltTops.Dequeue();
				if ( thunderCountdown <= 0 )
					thunderCountdown = Math.Max( 0, settings.ThunderStartsThisFarAway - bolts ) + random.Next( Math.Max( 1, settings.ThunderRandomiser ) );
				bolts++;
				lightningCountdown = random.Next( Math.Max( 1, settings.LightningCountdownFrom ) );
			}
		}
		else
			bolts = 0;
		// A countdown already at 0 stays there: the original's decrement would take it below 0, where it never plays either.
		if ( thunderCountdown > 0 && --thunderCountdown == 0 )
			strikes.Enqueue( lastTop );
	}

	/// <summary>December's season index: <c>12 / 3</c>, one past the four rows of the shipped table (<see cref="Roll"/>).</summary>
	private const int DecemberSeason = 4;

	/// <summary>The weather keys the sim reads, with the shipped values where a layer does not set them.</summary>
	private sealed record WeatherSettings(
		IReadOnlyList<int> Averages, IReadOnlyList<int> Tolerances, IReadOnlyList<int> Chances,
		int SeasonCount, int DaysOfWarning, int DaysBetweenChanges, int SpeedOfChange,
		int SnowLow, int SnowHigh, int RainLow, int RainHigh, int LightningLow, int LightningHigh,
		int LightningCountdownSpeed, int LightningCountdownFrom, int ThunderStartsThisFarAway, int ThunderRandomiser, int MaxRaindrops, int LightningTime )
	{
		// The shipped values (the global layer of the original's Standard.sam; the jungle and the other themes restate them).
		private static readonly int[] DefaultAverages = { 75, 80, 90, 50 };
		private static readonly int[] DefaultTolerances = { 25, 20, 10, 15 };
		private static readonly int[] DefaultChances = { 10, 10, 5, 10 };

		/// <summary>Whether no quality from 1 to 100 lies in the snow band: the park never snows by its own weather (the shipped jungle and fantasy keys are -1 and -1).</summary>
		public bool SnowBandIsEmpty => SnowHigh < SnowLow || SnowHigh < 1 || SnowLow > 100;

		/// <summary>Whether no quality from 1 to 100 lies in the rain band (the shipped space keys are -1 and -1).</summary>
		public bool RainBandIsEmpty => RainHigh < RainLow || RainHigh < 1 || RainLow > 100;

		/// <summary>[EXT:winter-always] Whether the park can snow at all under the always winter option: a snow band of its own (Halloween) or a rain band the wintry conversion turns into snow (jungle and fantasy). Space has neither.</summary>
		public bool CanSnowFromTheWinterRow => !SnowBandIsEmpty || !RainBandIsEmpty;

		public static WeatherSettings From( SamSettings standard )
		{
			var averages = new int[DefaultAverages.Length];
			var tolerances = new int[DefaultTolerances.Length];
			var chances = new int[DefaultChances.Length];
			for ( var season = 0; season < averages.Length; season++ )
			{
				averages[season] = standard.GetInt( $"Seasons[{season}].AvgWeatherQuality", DefaultAverages[season], optional: true );
				tolerances[season] = standard.GetInt( $"Seasons[{season}].NormalTolerance", DefaultTolerances[season], optional: true );
				chances[season] = standard.GetInt( $"Seasons[{season}].ChanceForExceptionalWeather", DefaultChances[season], optional: true );
			}
			// The array's count slot is the highest stored index + 1 over every Seasons[i] key (0x4017a0); the shipped layers all store 4 seasons.
			var stored = new[] { "AvgWeatherQuality", "NormalTolerance", "ChanceForExceptionalWeather" }
				.SelectMany( suffix => standard.GetIndices( "Seasons", suffix ) ).DefaultIfEmpty( -1 ).Max();
			var seasonCount = stored >= 0 ? stored + 1 : DefaultAverages.Length;
			return new WeatherSettings( averages, tolerances, chances, seasonCount,
				standard.GetInt( "Weather.DaysOfWarning", 4, optional: true ),
				standard.GetInt( "Weather.DaysBetweenChanges", 7, optional: true ),
				standard.GetInt( "Weather.SpeedOfChange", 20, optional: true ),
				standard.GetInt( "WeatherEffects.QualityForSnowLow", -1, optional: true ),
				standard.GetInt( "WeatherEffects.QualityForSnowHigh", -1, optional: true ),
				standard.GetInt( "WeatherEffects.QualityForRainLow", 0, optional: true ),
				standard.GetInt( "WeatherEffects.QualityForRainHigh", 40, optional: true ),
				standard.GetInt( "WeatherEffects.QualityForLightningLow", 0, optional: true ),
				standard.GetInt( "WeatherEffects.QualityForLightningHigh", 15, optional: true ),
				standard.GetInt( "WeatherEffects.LightningCountdownSpeed", 10, optional: true ),
				standard.GetInt( "WeatherEffects.LightningCountdownFrom", 500, optional: true ),
				standard.GetInt( "WeatherEffects.ThunderStartsThisFarAway", 10, optional: true ),
				standard.GetInt( "WeatherEffects.ThunderRandomiser", 2, optional: true ),
				standard.GetInt( "WeatherEffects.MaxRaindrops", 600, optional: true ),
				standard.GetInt( "WeatherEffects.LightningTime", 2000, optional: true ) );
		}
	}
}
