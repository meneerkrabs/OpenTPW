namespace OpenTPW;

/// <summary>
/// The park's weather: the original's weather thing (model 15), one quality re-rolled each cycle. The quality alone
/// decides the weather (<see cref="Snow"/>, <see cref="Rain"/>, <see cref="Lightning"/>), the drops ramp toward the
/// rain's target, lightning strikes count down and thunder follows a strike a few turns later. Only the sound side is
/// reproduced; the drops and bolts are not drawn.
/// </summary>
/// <remarks>
/// The sim advances once per park turn (248 ms, <see cref="ParkCalendar.Turn"/>), and the day is the calendar's
/// (<see cref="ParkCalendar.DayIndex"/>). The Mac binary's weather-tick gate is not located; the turn is the cadence
/// the fork's measured 3.99 ticks a second supports.
/// </remarks>
// [BIN:STP-PPC:0x10103138-0x101031ec the forecast and its apply] a forecast is posted DaysOfWarning ahead and applied when its day passes; the roll is scheduled from the last change
// [BIN:STP-PPC:0x101031f0-0x10103298 the roll schedule] a roll happens once the day is past lastChanged + max(0, DaysBetweenChanges - DaysOfWarning)
// [BIN:STP-PPC:0x1010386c-0x101038d4 the roll] a normal roll is Avg - Tol + rand % (2·Tol), or Avg when Tol <= 0; the exceptional branch (rand % 100 < Chance) gives rand % 100
// [BIN:STP-PPC:0x10103998-0x10103a80 converter] snow, rain and lightning bands; when both snow and rain hold, rand & 1 decides (even: snow)
// [BIN:STP-PPC:0x10103aa4-0x10103af8 target] intensity = (q − low) × 100 / (high − low) (100 when high = low); target drops = ((100 − intensity) × MaxRaindrops) / 100
// [BIN:STP-PPC:0x101030fc-0x101033c8 drop ramp] the drops move by SpeedOfChange toward the target and stop once within one step of it; a target of 0 then stops the rain
// [BIN:STP-PPC:0x101033cc-0x101034c4 lightning] while the lightning band holds, the countdown falls by LightningCountdownSpeed each turn and a strike resets it to rand % LightningCountdownFrom; the bolt count resets outside the band
// [BIN:STP-PPC:0x10103438-0x1010346c thunder] a strike sets the thunder countdown to max(0, ThunderStartsThisFarAway − bolts) + rand % ThunderRandomiser when it is idle (bolts fired this period, before the strike)
// [BIN:STP-PPC:0x101034c8-0x10103518 thunder at the top] the thunder plays at the last bolt's top (x, 300, z) when its countdown ends
// [APPROX:AUDIO-025] the December row reads past the four-season table on the Mac; this follows the fork's reading (Avg 4, Tolerance DaysOfWarning, Chance DaysBetweenChanges) — evidence needed: the Mac's row address for index 4
// [APPROX:AUDIO-026] the forecast's apply day is roll day + DaysOfWarning on the day index, and the roll is taken on the first turn of its day — evidence needed: the Mac's day comparison units ("now" against +0x20)
// [APPROX:AUDIO-027] the first quality is rolled at park entry from the entry day's season and the drops start at 0 — evidence needed: the Mac's initial roll (not traced)
// [APPROX:AUDIO-028] a snow quality's drops target is 0 (no rain voice); the snow branch's target is not traced
// [APPROX:AUDIO-029] thunder with a computed countdown of 0 plays at once — evidence needed: the Mac's thunder countdown test beside 0x10103438
// [APPROX:AUDIO-030] the random draws come from the park's weather stream, not the world LCG; the Mac's seed source is not traced
public sealed class ParkWeather
{
	/// <summary>The height of a bolt's top (<c>FUN_00512c50</c> sky point, the fork's and the Mac's 300).</summary>
	public const float BoltTopHeight = 300;

	private readonly WeatherSettings settings;
	private readonly DeterministicRandom random;
	private readonly int groundX;
	private readonly int groundZ;
	private readonly Queue<Vector3> strikes = new();
	private bool started;
	private long lastTurn;
	private long lastChanged;
	private long nextRollDay;
	private int? pendingQuality;
	private long applyDay;
	private int lightningCountdown;
	private int bolts;
	private int thunderCountdown;
	private Vector3 lastTop;

	/// <summary>The park's weather keys (<c>Standard.sam</c>, layered over the global ones).</summary>
	public ParkWeather( SamSettings standard, ulong seed, int cellsX, int cellsY )
	{
		settings = WeatherSettings.From( standard );
		random = new DeterministicRandom( seed );
		groundX = cellsX * 10;
		groundZ = cellsY * 10;
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
	/// <summary>The rain voice's level (0 to 100): the current drops over the cap (the Mac's rain tick).</summary>
	public int RainLevel => settings.MaxRaindrops > 0 ? Math.Clamp(CurrentDrops * 100 / settings.MaxRaindrops, 0, 100) : 0;

	/// <summary>Brings the weather up to <paramref name="tick"/> of the park's clock; each new park turn is one weather step.</summary>
	public void AdvanceTo( long tick )
	{
		var turn = ParkCalendar.Turn( tick );
		if ( !started )
		{
			started = true;
			lastTurn = turn;
			var day = ParkCalendar.DayIndex( tick );
			lastChanged = day;
			ApplyQuality( Roll( tick ) );
			nextRollDay = lastChanged + Math.Max( 0, settings.DaysBetweenChanges - settings.DaysOfWarning );
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

	/// <summary>Takes the bolt tops whose thunder has come due since the last call (the frame plays them).</summary>
	public IReadOnlyList<Vector3> TakeThunder()
	{
		var due = new List<Vector3>();
		while ( strikes.Count > 0 )
			due.Add( strikes.Dequeue() );
		return due;
	}

	internal void Step( long tick )
	{
		var day = ParkCalendar.DayIndex( tick );
		if ( pendingQuality is int next && day >= applyDay )
		{
			pendingQuality = null;
			lastChanged = day;
			ApplyQuality( next );
			nextRollDay = lastChanged + Math.Max( 0, settings.DaysBetweenChanges - settings.DaysOfWarning );
		}
		else if ( pendingQuality == null && day >= nextRollDay )
		{
			pendingQuality = Roll( tick );
			applyDay = day + settings.DaysOfWarning;
		}
		Ramp();
		Bolts();
	}

	/// <summary>The season's roll (<c>sub_1037d4</c>): exceptional weather with the season's chance, otherwise the season's average and tolerance.</summary>
	private int Roll( long tick )
	{
		var month = ParkCalendar.ToDate( tick ).Month;
		var season = month / 3;
		int average, tolerance, chance;
		if ( season < settings.Averages.Count )
		{
			average = settings.Averages[season];
			tolerance = settings.Tolerances[season];
			chance = settings.Chances[season];
		}
		else
		{
			// [APPROX:AUDIO-025] December reads past the table (the fork's reading of the Mac row)
			average = settings.Averages.Count;
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

	/// <summary>The converter (<c>sub_103910</c>): the quality's bands, the rain target and the lightning flag.</summary>
	internal void ApplyQuality( int quality )
	{
		var snowBand = quality >= settings.SnowLow && quality <= settings.SnowHigh;
		var rainBand = quality >= settings.RainLow && quality <= settings.RainHigh;
		var snow = snowBand && (!rainBand || random.Next( 2 ) == 0);
		Quality = quality;
		Snow = snow;
		Rain = rainBand && !snow;
		Lightning = quality >= settings.LightningLow && quality <= settings.LightningHigh;
		TargetDrops = 0;
		if ( Rain )
		{
			var width = settings.RainHigh - settings.RainLow;
			var intensity = width == 0 ? 100 : (quality - settings.RainLow) * 100 / width;
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

	/// <summary>The lightning countdown, the bolts, and the thunder countdown of one step.</summary>
	private void Bolts()
	{
		if ( thunderCountdown > 0 && --thunderCountdown == 0 )
			strikes.Enqueue( lastTop );
		if ( !Lightning )
		{
			bolts = 0;
			return;
		}
		lightningCountdown -= settings.LightningCountdownSpeed;
		if ( lightningCountdown > 0 )
			return;
		var x = 1 + random.Next( groundX );
		var z = 1 + random.Next( groundZ );
		lastTop = new Vector3( x + random.Next( 101 ) - 50, BoltTopHeight, z + random.Next( 101 ) - 50 );
		if ( thunderCountdown <= 0 )
		{
			thunderCountdown = Math.Max( 0, settings.ThunderStartsThisFarAway - bolts ) + random.Next( Math.Max( 1, settings.ThunderRandomiser ) );
			if ( thunderCountdown == 0 )
				strikes.Enqueue( lastTop );
		}
		bolts++;
		lightningCountdown = random.Next( Math.Max( 1, settings.LightningCountdownFrom ) );
	}

	/// <summary>The weather keys the sim reads, with the shipped values where a layer does not set them.</summary>
	private sealed record WeatherSettings(
		IReadOnlyList<int> Averages, IReadOnlyList<int> Tolerances, IReadOnlyList<int> Chances,
		int DaysOfWarning, int DaysBetweenChanges, int SpeedOfChange,
		int SnowLow, int SnowHigh, int RainLow, int RainHigh, int LightningLow, int LightningHigh,
		int LightningCountdownSpeed, int LightningCountdownFrom, int ThunderStartsThisFarAway, int ThunderRandomiser, int MaxRaindrops )
	{
		// The shipped values (the global layer of the original's Standard.sam; the jungle and the other themes restate them).
		private static readonly int[] DefaultAverages = { 75, 80, 90, 50 };
		private static readonly int[] DefaultTolerances = { 25, 20, 10, 15 };
		private static readonly int[] DefaultChances = { 10, 10, 5, 10 };

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
			return new WeatherSettings( averages, tolerances, chances,
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
				standard.GetInt( "WeatherEffects.MaxRaindrops", 600, optional: true ) );
		}
	}
}
