namespace OpenTPW;

/// <summary>The weather settings of Theme Park Inc's Standard.sam layers (<c>Seasons[]</c>, <c>Weather.YearsBeforeFirstFreakWeather</c>, <c>WeatherEffects.QualityFor*</c>).</summary>
/// <param name="Seasons">Four rows of the chances in percent for a heatwave, rain, a storm and snow, in the key order (<c>0xA024A0</c>..<c>0xA024DF</c>).</param>
/// <param name="TailWords">The three words that follow the table in the settings block (<c>0xA024E0</c>, <c>0xA024E4</c>, <c>0xA024E8</c>): the table's count and the online keys <c>Weather.DaysOfWarning</c> and <c>Weather.DaysBetweenChanges</c>, which row 4 reads as chances.</param>
/// <param name="YearsBeforeFirstFreakWeather"><c>Weather.YearsBeforeFirstFreakWeather</c> (<c>0xA024EC</c>).</param>
/// <param name="Quality">The quality ranges (low, high) of normal weather, a heatwave, snow, rain and lightning (<c>0xA02508</c>..<c>0xA02528</c>).</param>
public sealed record ThemeParkIncWeatherSettings( IReadOnlyList<int> Seasons, IReadOnlyList<int> TailWords, int YearsBeforeFirstFreakWeather,
	(int Low, int High) NormalQuality, (int Low, int High) HeatwaveQuality, (int Low, int High) SnowQuality, (int Low, int High) RainQuality, (int Low, int High) LightningQuality )
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
		// [BIN:TPI-EXE:0x642A27 settings tail] the words after the four rows are the table's count (4) and the two online-only keys, 0 when a file does not set them (an absent key reading 0 is not read in the loader)
		return new ThemeParkIncWeatherSettings( rows, new[] { 4, standard.GetInt( "Weather.DaysOfWarning", 0, optional: true ), standard.GetInt( "Weather.DaysBetweenChanges", 0, optional: true ) },
			standard.GetInt( "Weather.YearsBeforeFirstFreakWeather", 0, optional: true ),
			(Quality( "NormalLow" ), Quality( "NormalHigh" )), (Quality( "HeatWaveLow" ), Quality( "HeatWaveHigh" )), (Quality( "SnowLow" ), Quality( "SnowHigh" )),
			(Quality( "RainLow" ), Quality( "RainHigh" )), (Quality( "LightningLow" ), Quality( "LightningHigh" )) );
	}
}

/// <summary>One weather spell the thing keeps: the month it starts, its kind (0 none, 1 heatwave, 2 rain, 3 storm, 4 snow), its quality target and its length in months.</summary>
public readonly record struct WeatherSpell( int Start, int Kind, int Quality, int Months );

/// <summary>
/// Theme Park Inc's weather spells (Game.exe: the weather thing's month message 0x641F60, the roll 0x642770, the start 0x642A80). The thing keeps the spell it runs and the next one it has rolled; the state word
/// <c>+0x70</c> the arrival group reads is the running spell's kind. Only the spells are ported: the drops, the wind and the lightning that follow a spell's start are not.
/// </summary>
public sealed class ThemeParkIncWeatherSpells
{
	private readonly ThemeParkIncWeatherSettings settings;
	private WeatherSpell next;
	private bool nextPending;
	private WeatherSpell active;

	public ThemeParkIncWeatherSpells( ThemeParkIncWeatherSettings settings ) => this.settings = settings;

	/// <summary>The weather thing's state word (<c>+0x70</c>): the running spell's kind, 1 heatwave, 2 rain, 3 storm, 4 snow, 0 none.</summary>
	public int State => active.Kind;
	public WeatherSpell Active => active;
	public WeatherSpell Next => next;
	public bool NextPending => nextPending;

	/// <summary>
	/// The month message: starts the rolled spell when its month is reached, and rolls the next one when the running spell's last month is. <paramref name="month"/> is the date's month, 1 to 12; <paramref name="years"/>
	/// the years since the calendar's start; <paramref name="draw"/> answers a number below its bound.
	/// </summary>
	// [BIN:TPI-EXE:0x641FC4 month message] the message type 0xC: the date's month (0x401A14, its third argument) against the rolled spell's start (+0x14): reached and pending (+0x24) starts it (0x408FBC to 0x642A80: the record is copied to +0x28..+0x34 and +0x70 takes its kind); a spell still pending ends the message; else the running spell's last month, ((start + months - 1) mod 12) + 1 (0x642084), against the month mod 12 + 1 (0x642087): reached rolls the next spell (0x642770, 0x401EA1)
	// [EXT:tpi-weather-spell-seed] user-approved 2026-10-30: the roll's numbers come from the economy's world stream, where the original's rand() (0x870D38) is seeded from the clock (srand(GetTickCount()), 0x6A151B) and shared with 96 call sites
	public void OnMonth( int month, int years, Func<int, int> draw )
	{
		if ( month >= next.Start && nextPending )
		{
			active = next;
			nextPending = false;
		}
		if ( nextPending )
			return;
		// the divisions are the C compiler's signed ones: the running spell's start and length are 0 before the first spell, which makes the last month ((0 + 0 - 1) rem 12) + 1 = 0
		var last = (active.Start + active.Months - 1) % 12 + 1;
		if ( month % 12 + 1 < last )
			return;
		next = Roll( month, years, draw );
		nextPending = true;
	}

	/// <summary>The roll (<c>0x642770</c>).</summary>
	// [BIN:TPI-EXE:0x6427A8 spell roll] the month m (1 to 12) stored plus 1 is the spell's start; q = m mod 12 + 1, the row is q / 3 and the position q mod 3 (0 is 3); a third month (position 3) of a year from YearsBeforeFirstFreakWeather on draws r mod 100 against the row's chances in turn (heatwave, rain, storm, snow, each taken off r when missed), a spell of 3 months; any other roll, or a miss, is normal weather for the months left in the quarter; the quality is a draw below (high - low) plus low, from the kind's range (heatwave 0xA0250C/10, rain 0xA02520/1C, storm = lightning 0xA02528/24, snow 0xA02518/14, normal 0xA02504/08)
	// [BIN:TPI-EXE:0x6427F1 row 4] November (q = 12) reads the row 4: the words after the table (the count, DaysOfWarning, DaysBetweenChanges) and the years setting as its chances
	// [APPROX:ARRIVAL-008] the three words after the Seasons table are the count and two online-only keys that the offline data does not set: their offline values are not read (an absent key in the loader), so row 4 takes the count 4 and 0, 0
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
