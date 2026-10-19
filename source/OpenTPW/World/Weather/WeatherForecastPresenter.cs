using System.Runtime.CompilerServices;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// Presents the weather's forecast (docs/AUDIO.md "Weather visuals"): the countdown of a pending forecast
/// and the transient changed/current lines of <see cref="WeatherForecastState"/>, drawn as one banner line at
/// the top centre of the screen. The entity is created with the weather itself (<see cref="ParkWeather.Created"/>)
/// and reads the simulation each frame — the forecast, its apply day, the current day and the weather's bands —
/// so pause, park end and reload follow the weather's own state. The banner is an OpenTPW presentation: the
/// original shows no forecast (the forecast-presentation extension).
/// </summary>
// [EXT:forecast-banner] the forecast shows as a banner line at the top centre, drawn in the overlay pass with the original GAME8AA BF4 font above every screen (the chat overlay's channel); the original warns on no surface: its forecast event's only subscriber, the weather's handler 0x5127a0, posts nothing
public sealed class WeatherForecastPresenter : Entity
{
	/// <summary>Upper bound of one step's delta, so a hitch after a stall cannot jump the transient line's clock.</summary>
	private const float MaximumStepSeconds = 0.25f;
	private const int Margin = 16;
	private const int Padding = 6;
	private static readonly RgbaByte BackgroundColor = new( 16, 24, 48, 200 );
	private static readonly RgbaByte TextColor = new( 255, 255, 255, 255 );

	private readonly ParkWeather weather;
	private readonly WeatherForecastState forecast = new( () => GameLanguage.IsSelected ? GameLanguage.Current.Name : GameLanguage.DefaultLanguage );
	private readonly TextBatch batch = new();
	private FontAtlas? font;
	private TextRenderer? renderer;
	private TextLayoutResult? layout;
	private string? lastText;
	private int fontVersion = -1;
	private bool deleted;

	/// <summary>The line drawn in the last overlay pass, or null (smoke instrumentation).</summary>
	public string? DrawnBanner { get; private set; }

	/// <summary>
	/// Smoke instrumentation: the front-end smoke test reads window titles back from the screen, and the banner (a strip at the top centre)
	/// overlaps a title in languages whose title font sits higher (the Korean Research Lab title), so the smoke turns the banner off.
	/// </summary>
	internal static bool BannerHidden { get; set; }

	public WeatherForecastPresenter( ParkWeather weather )
	{
		this.weather = weather;
		Name = "WeatherForecast";
		global::Global.Render.OnOverlayRender += DrawBanner;
	}

	protected override void OnUpdate()
	{
		var quality = weather.Forecast;
		var bands = weather.ForecastBands;
		// Simulation-scaled time: a paused park (scale 0) freezes the transient line mid-display.
		var scale = Level.SimulationTimeScale;
		var delta = Math.Clamp( Time.Delta, 0f, MaximumStepSeconds ) * (float.IsFinite( scale ) && scale > 0f ? scale : 0f);
		forecast.Step( new WeatherForecastSample( weather.CurrentDay, quality, weather.ForecastApplyDay, bands, weather.CurrentBands ), delta );
		foreach ( var announcement in forecast.TakeAnnouncements() )
			Log.Trace( $"Weather: {announcement}" );
	}

	/// <summary>Draws the banner into the current overlay target: one laid-out line, one rectangle, no per-frame GPU growth.</summary>
	private void DrawBanner()
	{
		DrawnBanner = null;
		if ( deleted || BannerHidden || Level.Current != Level || Level.HidesInterface || forecast.Banner is not { } text )
			return;
		DrawnBanner = text;
		// The selected language's own font, reloaded when the language changes (the chat overlay's pattern).
		if ( font == null || fontVersion != GameLanguage.Version )
		{
			font = new FontAtlas( GameLanguage.Current.LoadFont( "GAME8AA.bf4" ) );
			fontVersion = GameLanguage.Version;
			layout = null;
		}
		renderer ??= new TextRenderer( global::Global.Render.OutputDescription );
		// A single unwrapped line; relaid out only when the text changes.
		if ( text != lastText || layout == null )
		{
			layout = TextLayout.Create( font, text );
			lastText = text;
		}
		var framebuffer = global::Global.Render.OverlayFramebuffer;
		var scale = Screen.UiScale;
		var width = (layout.Width + Padding * 2) * scale;
		var height = (layout.Height + Padding * 2) * scale;
		var x = Math.Max( 0, ((int)framebuffer.Width - width) / 2 );
		var y = Margin * scale;
		batch.Clear();
		batch.AddRectangle( x, y, width, height, BackgroundColor );
		batch.AddText( font, layout, x + Padding * scale, y + Padding * scale, TextColor, scale );
		renderer.Draw( global::Global.Render.CommandList, batch, framebuffer.Width, framebuffer.Height );
	}

	protected override void OnDelete()
	{
		deleted = true;
		global::Global.Render.OnOverlayRender -= DrawBanner;
		var textRenderer = renderer;
		renderer = null;
		if ( textRenderer != null )
			global::Global.Render.ScheduleDelete( textRenderer.Dispose );
	}
}

/// <summary>Creates the weather's forecast presenter with the weather itself: the simulation stays free of presentation concerns.</summary>
internal static class WeatherForecastPresenterModule
{
	[ModuleInitializer]
	internal static void Attach()
	{
		ParkWeather.Created += weather =>
		{
			// A host without a renderer (plain simulation tests) and constructions outside a park
			// stay entity-free; the staged-candidate scope adopts the entity like any other.
			if ( global::Global.Render == null || Level.Current?.Park == null )
				return;
			_ = new WeatherForecastPresenter( weather );
		};
	}
}
