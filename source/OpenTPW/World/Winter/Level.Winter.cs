using OpenTPW.Mods;

namespace OpenTPW;

// [EXT:winter-snow] The park's settled snow (docs/WINTER.md): presentation only. The cover is stepped by the weather's turns, eased per frame and read by the renderer as one uniform per
// draw. None of it is read by the simulation or part of a state hash; a park save keeps the cover as an optional unhashed field.
public partial class Level
{
	private SnowCover? snow;
	private ParkWeather? snowWeather;
	private double? resumedSnowCover;

	/// <summary>The park's settled snow, or null while the <c>opentpw.winter-snow</c> extension is off or the park has no weather yet.</summary>
	internal SnowCover? Snow => snow;

	/// <summary>
	/// The snow cover the lit shaders draw this frame (0 = bare, the picture as it always was); 0 outside a park, while the extension is off, and before the first weather step.
	/// </summary>
	internal float SnowUniform => snow == null ? 0f : (float)snow.Drawn;

	/// <summary>
	/// Starts the cover for the park's current weather: nothing while the extension is off, otherwise one cover (from the saved value of a resumed park) that the weather's turns step.
	/// Safe to call again; it follows a new weather instance (a restore replaces it) and never double-subscribes.
	/// </summary>
	private void AttachSnow()
	{
		if ( Weather == null || !FeatureGates.WinterSnow || ReferenceEquals( snowWeather, Weather ) )
			return;
		snowWeather = Weather;
		snow ??= new SnowCover( resumedSnowCover ?? 0 );
		resumedSnowCover = null;
		var cover = snow;
		Weather.TurnStepped += weather => cover.Step( weather );
	}

	/// <summary>One frame of the cover's easing toward the last turn's value, in real time (a paused park's cover stands still because its target does).</summary>
	private void UpdateSnow() => snow?.Ease( Math.Min( Time.Delta, EveningMaximumFrameSeconds ) );

	/// <summary>The saved cover of a park: the cover while it is above 0 and the extension is on, otherwise null (the field is then left out of the file).</summary>
	internal double? CaptureSnowCover() => snow is { Cover: > 0 } cover ? cover.Cover : null;
}
