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
/// testable, GPU-free half — <see cref="WeatherForecastPresenter"/> draws it. The original's forecast message
/// handler is not traced, so the wording, cadence and surface are OpenTPW approximations (AUDIO-905..907); no
/// weather text exists in the shipped string tables to copy.
/// </summary>
// [EXT:forecast-wording] the forecast's kind text classifies the pending quality by the converter's bands only — the apply-time snow/rain coin flip has not happened, so both bands read "snow or rain", and the lightning band composes "thunderstorms"; the wording is OpenTPW's — evidence needed: the original's forecast message handler and its text
// [EXT:forecast-cadence] one message is posted when a forecast appears and one when it applies, plus one transient current-weather line on attach (a park entered or loaded with weather on screen); a park resumed mid-warning announces the pending forecast once, and the transient line shows for ChangedLineSeconds of simulation time — evidence needed: the original's forecast message cadence
public sealed class WeatherForecastState
{
	/// <summary>How long the transient changed/current line stays on screen, in seconds of simulation time.</summary>
	public const float ChangedLineSeconds = 6f;

	private readonly List<string> announcements = new();
	private (long ApplyDay, int Quality)? announced;
	private string? flashText;
	private float flashRemaining;
	private bool attached;

	/// <summary>The line to draw (null: nothing). While a transient line shows it wins over the countdown.</summary>
	public string? Banner { get; private set; }

	/// <summary>True while <see cref="Banner"/> is the transient changed/current line rather than the countdown.</summary>
	public bool IsTransient { get; private set; }

	/// <summary>
	/// The messages posted since the last call, in order: one when a forecast appears and one when it applies.
	/// At most one per step, so a park cannot be spammed by stepping.
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
	/// Advances the presentation by one observation of the weather: a newly seen pending forecast is announced
	/// once and shows the countdown, the forecast's apply (the simulation clears a pending forecast only by
	/// applying it, <see cref="ParkWeather.Step"/>) posts and shows the changed line, and
	/// <paramref name="deltaSeconds"/> of simulation time retire the transient line — 0 (a paused park) freezes
	/// it mid-display.
	/// </summary>
	public void Step( in WeatherForecastSample sample, float deltaSeconds )
	{
		if ( !attached )
		{
			// A park entered or loaded with weather on screen says so once; a dry, quiet park stays silent.
			attached = true;
			if ( !IsQuiet( sample.CurrentBands ) )
				ShowTransient( $"The weather: {KindText( sample.CurrentBands )}." );
		}

		string? countdown = null;
		if ( sample.ForecastQuality is int quality )
		{
			var due = Math.Max( 0, sample.ForecastApplyDay - sample.Day );
			countdown = $"Forecast: {KindText( sample.ForecastBands )} {DueText( due )}.";
			if ( announced != (sample.ForecastApplyDay, quality) )
			{
				announced = (sample.ForecastApplyDay, quality);
				announcements.Add( $"Weather forecast: {KindText( sample.ForecastBands )} {DueText( due )}." );
			}
		}
		else if ( announced is not null )
		{
			announced = null;
			ShowTransient( $"The weather has changed: {KindText( sample.CurrentBands )}." );
			announcements.Add( flashText! );
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
	// [EXT:forecast-wording] the kind wording is OpenTPW's; no weather text exists in the shipped Language tables (searched) — evidence needed: the original's forecast message handler
	internal static string KindText( WeatherQualityBands bands )
	{
		if ( bands.Lightning )
			return bands.Snow || bands.Rain ? "thunderstorms" : "thunder and lightning";
		if ( bands.Snow && bands.Rain )
			return "snow or rain";
		if ( bands.Snow )
			return "snow";
		if ( bands.Rain )
			return "rain";
		return "dry weather";
	}

	/// <summary>When a due day reads: today, tomorrow, or "in N days".</summary>
	internal static string DueText( long daysLeft ) => daysLeft switch
	{
		0 => "today",
		1 => "tomorrow",
		_ => $"in {daysLeft} days",
	};
}
