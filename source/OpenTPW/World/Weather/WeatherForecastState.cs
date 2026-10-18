using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// The band membership of one weather quality, as the converter (<see cref="ParkWeather.ApplyQuality"/>) reads
/// it: the snow, rain and lightning bands. Both precipitation bands can hold for one quality — the converter
/// then breaks the tie with a random draw at apply time, which a forecast has not made yet.
/// </summary>
public readonly record struct WeatherQualityBands( bool Snow, bool Rain, bool Lightning );

/// <summary>
/// One observation of the weather the forecast presentation reads: the simulation day, the pending forecast
/// (its quality, the day it applies on and its band membership) and the current weather's resolved bands.
/// The current bands are the applied weather's own flags (rain only when it won the tie).
/// </summary>
public readonly record struct WeatherForecastSample(
	long Day,
	int? ForecastQuality,
	long ForecastApplyDay,
	WeatherQualityBands ForecastBands,
	WeatherQualityBands CurrentBands );

/// <summary>
/// The forecast's presentation state (docs/AUDIO.md "Weather visuals"): the countdown line a pending forecast
/// shows, the transient lines when the weather changes, and the messages posted at those moments. This is the
/// testable, GPU-free half — <see cref="WeatherForecastPresenter"/> draws it. The original shows no forecast or
/// change at all (<see cref="ParkWeather"/>, the forecast-presentation extension), so the wording, cadence and surface are
/// OpenTPW's own, worded in the selected language (<see cref="SupplementaryStrings"/>).
/// </summary>
// [EXT:forecast-wording] the forecast's kind text classifies the pending quality by the converter's bands only — the apply-time snow/rain coin flip has not happened, so both bands read "snow or rain", and the lightning band composes "thunderstorms"; the wording is OpenTPW's (no weather text exists in the shipped string tables)
// [EXT:forecast-cadence] news only: one message when a forecast of another kind of weather appears and one when the kind of weather changes, plus one transient current-weather line on attach (a park entered or loaded with weather on screen); the original rolls every cycle whatever the weather (0x512880), so a dry roll after dry weather shows and posts nothing; a park resumed mid-warning announces the pending forecast once, and the transient line shows for ChangedLineSeconds of simulation time
public sealed class WeatherForecastState
{
	private readonly Func<string> language;

	/// <param name="language">The language the lines are worded in when they are made (<see cref="SupplementaryStrings"/>); English when null.</param>
	public WeatherForecastState( Func<string>? language = null ) => this.language = language ?? (() => GameLanguage.DefaultLanguage);

	/// <summary>How long the transient changed/current line stays on screen, in seconds of simulation time.</summary>
	public const float ChangedLineSeconds = 6f;

	private readonly List<string> announcements = new();
	private (long ApplyDay, int Quality)? announced;
	/// <summary>The kind of weather last shown or announced (<see cref="KindKey"/> of the current bands).</summary>
	private OpenTpwText shownKind;
	private string? flashText;
	private float flashRemaining;
	private bool attached;

	/// <summary>The line to draw (null: nothing). While a transient line shows it wins over the countdown.</summary>
	public string? Banner { get; private set; }

	/// <summary>True while <see cref="Banner"/> is the transient changed/current line rather than the countdown.</summary>
	public bool IsTransient { get; private set; }

	/// <summary>
	/// The messages posted since the last call, in order: one when a forecast of another kind of weather appears and
	/// one when the kind of weather changes. At most those two per step (a change and the next forecast can come in
	/// one update), so a park cannot be spammed by stepping.
	/// </summary>
	public IReadOnlyList<string> TakeAnnouncements()
	{
		if ( announcements.Count == 0 )
			return Array.Empty<string>();
		var taken = announcements.ToArray();
		announcements.Clear();
		return taken;
	}

	/// <summary>
	/// Advances the presentation by one observation of the weather: a change of the kind of weather (a forecast's
	/// apply) posts and shows the changed line, a newly seen pending forecast of another kind of weather is
	/// announced once and shows the countdown, and <paramref name="deltaSeconds"/> of simulation time retire the
	/// transient line — 0 (a paused park) freezes it mid-display. A forecast or an apply that keeps the kind of
	/// weather (dry after dry) is no news and shows nothing.
	/// </summary>
	public void Step( in WeatherForecastSample sample, float deltaSeconds )
	{
		var currentKind = KindKey( sample.CurrentBands );
		if ( !attached )
		{
			// A park entered or loaded with weather on screen says so once; a dry, quiet park stays silent.
			attached = true;
			shownKind = currentKind;
			if ( !IsQuiet( sample.CurrentBands ) )
				ShowTransient( Text( OpenTpwText.WeatherNow, KindText( sample.CurrentBands ) ) );
		}
		else if ( currentKind != shownKind )
		{
			shownKind = currentKind;
			ShowTransient( Text( OpenTpwText.WeatherChanged, KindText( sample.CurrentBands ) ) );
			announcements.Add( flashText! );
		}

		string? countdown = null;
		if ( sample.ForecastQuality is int quality && KindKey( sample.ForecastBands ) != currentKind )
		{
			var due = Math.Max( 0, sample.ForecastApplyDay - sample.Day );
			var kind = KindText( sample.ForecastBands );
			var when = DueText( due );
			countdown = Text( OpenTpwText.WeatherForecastLine, kind, when );
			if ( announced != (sample.ForecastApplyDay, quality) )
			{
				announced = (sample.ForecastApplyDay, quality);
				announcements.Add( Text( OpenTpwText.WeatherForecastMessage, kind, when ) );
			}
		}

		if ( flashRemaining > 0 )
		{
			flashRemaining -= float.IsFinite( deltaSeconds ) && deltaSeconds > 0 ? deltaSeconds : 0;
			if ( flashRemaining <= 0 )
				flashText = null;
		}
		Banner = flashText ?? countdown;
		IsTransient = flashText != null;
	}

	private void ShowTransient( string text )
	{
		flashText = text;
		flashRemaining = ChangedLineSeconds;
	}

	/// <summary>Whether nothing falls in any band: no drops, no bolts.</summary>
	internal static bool IsQuiet( WeatherQualityBands bands ) => !bands.Snow && !bands.Rain && !bands.Lightning;

	/// <summary>
	/// The weather kind a set of bands shows as: thunderstorms over precipitation, thunder alone, the snow/rain
	/// tie still undecided as "snow or rain" (the apply-time draw has not happened for a forecast), then snow, rain
	/// and dry weather.
	/// </summary>
	// [EXT:forecast-wording] the kind wording is OpenTPW's; no weather text exists in the shipped Language tables (searched), and the original shows no forecast
	internal string KindText( WeatherQualityBands bands ) => Text( KindKey( bands ) );

	internal static OpenTpwText KindKey( WeatherQualityBands bands )
	{
		if ( bands.Lightning )
			return bands.Snow || bands.Rain ? OpenTpwText.WeatherThunderstorms : OpenTpwText.WeatherThunderAndLightning;
		if ( bands.Snow && bands.Rain )
			return OpenTpwText.WeatherSnowOrRain;
		if ( bands.Snow )
			return OpenTpwText.WeatherSnow;
		if ( bands.Rain )
			return OpenTpwText.WeatherRain;
		return OpenTpwText.WeatherDry;
	}

	/// <summary>When a due day reads: today, tomorrow, or "in N days".</summary>
	internal string DueText( long daysLeft ) => daysLeft switch
	{
		0 => Text( OpenTpwText.WeatherToday ),
		1 => Text( OpenTpwText.WeatherTomorrow ),
		_ => Text( OpenTpwText.WeatherInDays, daysLeft ),
	};

	private string Text( OpenTpwText key, params object[] arguments ) =>
		string.Format( System.Globalization.CultureInfo.InvariantCulture, SupplementaryStrings.Get( key, language() ), arguments );
}
