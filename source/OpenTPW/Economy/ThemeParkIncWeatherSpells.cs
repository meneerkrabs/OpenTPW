namespace OpenTPW;

/// <summary>The weather settings of Theme Park Inc's Standard.sam layers (<c>Seasons[]</c>, <c>Weather.YearsBeforeFirstFreakWeather</c>, <c>WeatherEffects.QualityFor*</c>).</summary>
/// <param name="Seasons">Four rows of the chances in percent for a heatwave, rain, a storm and snow, in the key order (<c>0xA024A0</c>..<c>0xA024DF</c>).</param>
/// <param name="TailWords">The three words that follow the table in the settings block (<c>0xA024E0</c>, <c>0xA024E4</c>, <c>0xA024E8</c>): the table's count and the online keys <c>Weather.DaysOfWarning</c> and <c>Weather.DaysBetweenChanges</c>, which row 4 reads as chances.</param>
/// <param name="YearsBeforeFirstFreakWeather"><c>Weather.YearsBeforeFirstFreakWeather</c> (<c>0xA024EC</c>).</param>
/// <param name="Quality">The quality ranges (low, high) of normal weather, a heatwave, snow, rain and lightning (<c>0xA02508</c>..<c>0xA02528</c>).</param>
public sealed record ThemeParkIncWeatherSettings( IReadOnlyList<int> Seasons, IReadOnlyList<int> TailWords, int YearsBeforeFirstFreakWeather,
	(int Low, int High) NormalQuality, (int Low, int High) HeatwaveQuality, (int Low, int High) SnowQuality, (int Low, int High) RainQuality, (int Low, int High) LightningQuality,
	int DropSpeed = 0, int MaxRaindrops = 0,
	// [DATA:levels/science/Standard.sam:WeatherEffects.*] the storm timers and the bolt's drawn lifetime; the
	// executable's own copies at 0x0A0252C..0x0A0253C hold the same Science numbers.
	int LightningCountdownSpeed = 10, int LightningCountdownFrom = 500, int LightningTime = 2000,
	int ThunderStartsThisFarAway = 10, int ThunderRandomiser = 2 )
{
	/// <summary>The settings of the layers, or null when they hold no weather (a synthetic file).</summary>
	public static ThemeParkIncWeatherSettings? Read( SamSettings standard )
	{
		if ( !standard.Contains( "Seasons[0].PercentageChanceForHeatwave" ) )
			return null;
		var rows = new List<int>();
		for ( var season = 0; season < 4; season++ )
			foreach ( var kind in new[] { "Heatwave", "Rain", "Storm", "Snow" } )
				rows.Add( standard.GetInt( $"Seasons[{season}].PercentageChanceFor{kind}", 0, optional: true ) );
		int Quality( string key ) => standard.GetInt( $"WeatherEffects.QualityFor{key}", 0, optional: true );
		// [BIN:TPI-EXE:0x64280F, 0x64289F, 0x642857 row reads] the roll reads the table by row, eax = row * 16 (four words of 4 bytes per row); the schema record Seasons has count 4
		// [BIN:TPI-EXE:0x419FB4 loader array count] the settings loader raises the count word of an array section to the highest row index read plus one (the compare at 0x419FB7, the store at 0x419FBB), so the Seasons count word is 4 for the files' rows 0 to 3 (it sits right after the rows: the roll's row 4 reads it and the years word at 0xA024EC follows two keys later); the keys it does not read (Weather.DaysOfWarning and DaysBetweenChanges, in Online_Standard.sam only) keep the zero of the settings block
		return new ThemeParkIncWeatherSettings( rows, new[] { 4, standard.GetInt( "Weather.DaysOfWarning", 0, optional: true ), standard.GetInt( "Weather.DaysBetweenChanges", 0, optional: true ) },
			standard.GetInt( "Weather.YearsBeforeFirstFreakWeather", 0, optional: true ),
			(Quality( "NormalLow" ), Quality( "NormalHigh" )), (Quality( "HeatWaveLow" ), Quality( "HeatWaveHigh" )), (Quality( "SnowLow" ), Quality( "SnowHigh" )),
			(Quality( "RainLow" ), Quality( "RainHigh" )), (Quality( "LightningLow" ), Quality( "LightningHigh" )),
			standard.GetInt( "Weather.SpeedOfChange", 0, optional: true ), standard.GetInt( "WeatherEffects.MaxRaindrops", 0, optional: true ),
			standard.GetInt( "WeatherEffects.LightningCountdownSpeed", 10, optional: true ),
			standard.GetInt( "WeatherEffects.LightningCountdownFrom", 500, optional: true ),
			standard.GetInt( "WeatherEffects.LightningTime", 2000, optional: true ),
			standard.GetInt( "WeatherEffects.ThunderStartsThisFarAway", 10, optional: true ),
			standard.GetInt( "WeatherEffects.ThunderRandomiser", 2, optional: true ) );
	}
}

/// <summary>One weather spell the thing keeps: the month it starts, its kind (0 none, 1 heatwave, 2 rain, 3 storm, 4 snow), its quality target and its length in months.</summary>
public readonly record struct WeatherSpell( int Start, int Kind, int Quality, int Months );

/// <summary>The storm timers and last thunder position a save keeps (the thing's +0x58..+0x68).</summary>
public readonly record struct ThemeParkIncStormSnapshot( int Lightning, int Bolts, int ThunderCountdown, float X, float Y, float Z );

/// <summary>The running and rolled spells of an OpenTPW park, not a native weather record.</summary>
public readonly record struct ThemeParkIncWeatherSnapshot(
	[property: System.Text.Json.Serialization.JsonRequired] WeatherSpell Active,
	[property: System.Text.Json.Serialization.JsonRequired] WeatherSpell Next,
	[property: System.Text.Json.Serialization.JsonRequired] bool NextPending,
	[property: System.Text.Json.Serialization.JsonRequired] int SpellStarts,
	[property: System.Text.Json.Serialization.JsonRequired] int CurrentDrops,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] float? WindDirection = null,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )] bool PrecipitationStopped = false,
	// [DATA:weather thing +0x58..+0x68] the storm timers and the last thunder position; absent in older saves, which start a storm's timers at zero
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] ThemeParkIncStormSnapshot? Storm = null );

/// <summary>
/// Theme Park Inc's weather spells (Game.exe: the weather thing's month message 0x641F60, the roll 0x642770, the start 0x642A80). The thing keeps the spell it runs and the next one it has rolled; the state word
/// <c>+0x70</c> the arrival group reads is the running spell's kind. Drop counts advance with the park turn; this does not draw or play precipitation, wind or lightning.
/// </summary>
public sealed class ThemeParkIncWeatherSpells
{
	private readonly ThemeParkIncWeatherSettings settings;
	private WeatherSpell next;
	private bool nextPending;
	private WeatherSpell active;
	private ThemeParkIncStormState storm;
	private readonly List<WeatherStrike> strikes = new();

	public ThemeParkIncWeatherSpells( ThemeParkIncWeatherSettings settings ) => this.settings = settings;

	/// <summary>The weather thing's state word (<c>+0x70</c>): the running spell's kind, 1 heatwave, 2 rain, 3 storm, 4 snow, 0 none.</summary>
	public int State => active.Kind;
	public WeatherSpell Active => active;
	public WeatherSpell Next => next;
	public bool NextPending => nextPending;

	/// <summary>How many spells have started (0x642A80 runs at each); the attractions' weather message follows each start, so the level runs their closing when it changes.</summary>
	public int SpellStarts { get; private set; }

	public int CurrentDrops { get; private set; }
	private bool precipitationStopped;
	internal (int Mode, float? Speed) PrecipitationPresentation => precipitationStopped ? (2, null) : ThemeParkIncPrecipitation.Presentation( State );
	internal int RenderedDrops => PrecipitationPresentation.Mode == 2 ? 0 : Math.Clamp( CurrentDrops, 0, Math.Max( 0, settings.MaxRaindrops ) );
	internal bool FlatRainActive => PrecipitationPresentation.Mode == 0;

	// [BIN:TPI-EXE:0x64149B weather constructor] An older OpenTPW save had no wind angle.
	internal float WindDirection { get; private set; } = 90f;
	internal float WindForce => SpellStarts == 0 ? 0.5f : ThemeParkIncPrecipitation.WindForce( active.Quality );

	// [BIN:TPI-EXE:0x64229A rain volume request] The sound system clamps this request, not the weather thing.
	internal int RainVolumeRequest => settings.MaxRaindrops > 0 ? unchecked(CurrentDrops * 100) / settings.MaxRaindrops : 0;

	internal ThemeParkIncWeatherSnapshot Capture() => new( active, next, nextPending, SpellStarts, CurrentDrops,
		WindDirection == 90f ? null : WindDirection, precipitationStopped,
		storm == default ? null : new ThemeParkIncStormSnapshot( storm.LightningCountdown, storm.BoltCount, storm.ThunderCountdown,
			storm.ThunderPosition.X, storm.ThunderPosition.Y, storm.ThunderPosition.Z ) );

	// [BIN:TPI-EXE:0x642230 weather turn] The native thing steps the count before the calendar can start another spell.
	/// <summary>The map the strike geometry spans (set by the level); a park without one strikes at the origin.</summary>
	internal (int Width, int Height) MapCells { get; set; }

	/// <summary>The strikes the renderer has not taken yet (the storm's bolts).</summary>
	internal IReadOnlyList<WeatherStrike> TakeStrikes()
	{
		if ( strikes.Count == 0 )
			return Array.Empty<WeatherStrike>();
		var taken = strikes.ToArray();
		strikes.Clear();
		return taken;
	}

	internal int LightningTimeMilliseconds => settings.LightningTime;

	/// <summary>The thunder the last turn produced, at the bolt's top; the caller plays it there once.</summary>
	internal Vector3? TakeThunder()
	{
		if ( thunderTaken || thunderPosition is not { } position )
			return null;
		thunderTaken = true;
		return position;
	}
	private bool thunderTaken;
	private Vector3? thunderPosition;

	// [BIN:TPI-EXE:0x6422FE storm turn] The storm timers step with the drops on the same park turn, drawing
	// from the world generator (0x40A92F) in the order the native routine does.
	internal void OnTurn( Func<uint> draw )
	{
		var stormStep = ThemeParkIncStorm.Step( storm, State == 3, settings.LightningCountdownSpeed, (uint)Math.Max( 1, settings.LightningCountdownFrom ),
			settings.ThunderStartsThisFarAway, (uint)Math.Max( 1, settings.ThunderRandomiser ),
			() => ThemeParkIncStorm.Geometry( MapCells.Width, MapCells.Height, singlePrecision: true, draw ), draw );
		storm = stormStep.State;
		if ( stormStep.Strike is { } strike )
		{
			// only the renderer takes strikes (none in the browser or headless), so keep no more than the bolts it can draw at once
			if ( strikes.Count == WeatherVisualState.MaximumBolts )
				strikes.RemoveAt( 0 );
			strikes.Add( strike );
		}
		if ( stormStep.Thunder is { } thunder )
		{
			thunderPosition = thunder;
			thunderTaken = false;
		}
		if ( settings.DropSpeed <= 0 || settings.MaxRaindrops <= 0 )
			return;
		var target = ThemeParkIncPrecipitation.Target( active, settings.RainQuality, settings.SnowQuality, settings.MaxRaindrops );
		var step = ThemeParkIncPrecipitation.Step( CurrentDrops, target, settings.DropSpeed, settings.MaxRaindrops );
		CurrentDrops = step.Current;
		// [BIN:TPI-EXE:0x6422B8 precipitation stop] A zero target can stop the mode even
		// when the spell kind still says rain. The next spell sets the mode again.
		if ( step.Stop && State is 2 or 3 or 4 )
			precipitationStopped = true;
	}

	internal static bool IsValid( ThemeParkIncWeatherSnapshot state ) =>
		state.CurrentDrops >= 0 && (state.Active.Kind is not (0 or 1) || state.CurrentDrops == 0)
		&& (state.SpellStarts == 0 ? state.Active == default : state.SpellStarts > 0 && IsRolledSpell( state.Active ))
		&& (state.NextPending || state.SpellStarts > 0 ? IsRolledSpell( state.Next ) : state.Next == default)
		&& (state.WindDirection is null || (state.SpellStarts > 0 && state.WindDirection is >= 0f and < 360f))
		&& (!state.PrecipitationStopped || (state.Active.Kind is 2 or 3 or 4 && state.CurrentDrops == 0))
		&& (state.Storm is not { } storm || (storm.Bolts >= 0 && float.IsFinite( storm.X ) && float.IsFinite( storm.Y ) && float.IsFinite( storm.Z )));

	// [BIN:TPI-EXE:0x6427A8 spell roll] The roll stores month + 1 without wrapping it. Keep a pending 13 if the running simulation produced it.
	private static bool IsRolledSpell( WeatherSpell spell ) =>
		spell.Start is >= 2 and <= 13 && spell.Kind is >= 0 and <= 4 && spell.Months is >= 1 and <= 3;

	internal void Restore( ThemeParkIncWeatherSnapshot state )
	{
		if ( !IsValid( state ) )
			throw new InvalidDataException( "Invalid Theme Park Inc weather snapshot." );
		if ( state.CurrentDrops > Math.Max( 0, settings.MaxRaindrops )
			|| (state.CurrentDrops != 0 && (settings.DropSpeed <= 0 || state.CurrentDrops % settings.DropSpeed != 0)) )
			throw new InvalidDataException( "Theme Park Inc weather drops do not match the settings." );
		if ( state.PrecipitationStopped && (settings.DropSpeed <= 0 || settings.MaxRaindrops <= 0
			|| ThemeParkIncPrecipitation.Target( state.Active, settings.RainQuality, settings.SnowQuality, settings.MaxRaindrops ) != 0) )
			throw new InvalidDataException( "The saved Theme Park Inc precipitation stop does not match its spell." );
		(active, next, nextPending, SpellStarts, CurrentDrops) = (state.Active, state.Next, state.NextPending, state.SpellStarts, state.CurrentDrops);
		storm = state.Storm is { } saved
			? new ThemeParkIncStormState( saved.Lightning, saved.Bolts, saved.ThunderCountdown, new Vector3( saved.X, saved.Y, saved.Z ) )
			: default;
		WindDirection = state.WindDirection ?? 90f;
		precipitationStopped = state.PrecipitationStopped;
	}

	/// <summary>
	/// The month message: starts the rolled spell when its month is reached, and rolls the next one when the running spell's last month is. <paramref name="month"/> is the date's month, 1 to 12; <paramref name="years"/>
	/// the years since the calendar's start; <paramref name="draw"/> answers a number below its bound.
	/// Returns only a newly rolled spell, not a pending spell or one that just started.
	/// </summary>
	// [BIN:TPI-EXE:0x641FC4 month message] the message type 0xC: the date's month (0x401A14, its third argument) against the rolled spell's start (+0x14): reached and pending (+0x24) starts it (0x408FBC to 0x642A80: the record is copied to +0x28..+0x34 and +0x70 takes its kind); a spell still pending ends the message; else the running spell's last month, ((start + months - 1) mod 12) + 1 (0x642084), against the month mod 12 + 1 (0x642087): reached rolls the next spell (0x642770, 0x401EA1)
	// [EXT:tpi-weather-spell-seed] user-approved 2026-10-30: the roll's numbers come from the economy's world stream, where the original's rand() (0x870D38) is seeded from the clock (srand(GetTickCount()), 0x6A151B) and shared with 96 call sites
	public WeatherSpell? OnMonth( int month, int years, Func<int, int> draw )
	{
		if ( month >= next.Start && nextPending )
		{
			active = next;
			nextPending = false;
			SpellStarts++;
			precipitationStopped = false;
			// [BIN:TPI-EXE:0x642C78 dry spell] The mode-2 helper at 0x642EE9 resets the count immediately.
			if ( active.Kind is 0 or 1 )
				CurrentDrops = 0;
			// [BIN:TPI-EXE:0x642CDF spell wind draw] This uses the world generator, not CRT rand().
			// It is part of spell start, before this month message can roll the next spell.
			WindDirection = ThemeParkIncPrecipitation.NextWindDirection( WindDirection, (uint)draw( 90 ) );
		}
		if ( nextPending )
			return null;
		// the divisions are the C compiler's signed ones: the running spell's start and length are 0 before the first spell, which makes the last month ((0 + 0 - 1) rem 12) + 1 = 0
		var last = (active.Start + active.Months - 1) % 12 + 1;
		if ( month % 12 + 1 < last )
			return null;
		next = Roll( month, years, draw );
		nextPending = true;
		return next;
	}

	/// <summary>The roll (<c>0x642770</c>).</summary>
	// [BIN:TPI-EXE:0x6427A8 spell roll] the month m (1 to 12) stored plus 1 is the spell's start; q = m mod 12 + 1, the row is q / 3 and the position q mod 3 (0 is 3); a third month (position 3) of a year from YearsBeforeFirstFreakWeather on draws r mod 100 against the row's chances in turn (heatwave, rain, storm, snow, each taken off r when missed), a spell of 3 months; any other roll, or a miss, is normal weather for the months left in the quarter; the quality is a draw below (high - low) plus low, from the kind's range (heatwave 0xA0250C/10, rain 0xA02520/1C, storm = lightning 0xA02528/24, snow 0xA02518/14, normal 0xA02504/08)
	// [BIN:TPI-EXE:0x6427F1 row 4] November (q = 12) reads the row 4: the words after the table (the count, DaysOfWarning, DaysBetweenChanges) and the years setting as its chances
	// [BIN:TPI-EXE:0x419FB4 loader array count] row 4 reads the count word (4, written by the loader), the two online-only keys (0 offline) and the years setting
	private WeatherSpell Roll( int month, int years, Func<int, int> draw )
	{
		var q = month % 12 + 1;
		var row = q / 3;
		var position = q % 3 == 0 ? 3 : q % 3;
		var start = month + 1;
		if ( position == 3 && years >= settings.YearsBeforeFirstFreakWeather )
		{
			var chances = new int[4];
			for ( var index = 0; index < 4; index++ )
			{
				var word = row * 4 + index;
				chances[index] = word < 16 ? settings.Seasons[word] : word - 16 < 3 ? settings.TailWords[word - 16] : settings.YearsBeforeFirstFreakWeather;
			}
			var r = draw( 100 );
			var kind = 0;
			for ( var index = 0; index < 4 && kind == 0; index++ )
			{
				if ( r < chances[index] )
					kind = index + 1;
				else
					r -= chances[index];
			}
			if ( kind != 0 )
			{
				var (low, high) = kind switch { 1 => settings.HeatwaveQuality, 2 => settings.RainQuality, 3 => settings.LightningQuality, _ => settings.SnowQuality };
				return new WeatherSpell( start, kind, QualityIn( low, high, draw ), 3 );
			}
		}
		return new WeatherSpell( start, 0, QualityIn( settings.NormalQuality.Low, settings.NormalQuality.High, draw ), position );
	}

	private static int QualityIn( int low, int high, Func<int, int> draw ) => high - low > 0 ? draw( high - low ) + low : low;
}
