using OpenTPW.Mods;

namespace OpenTPW;

// [EXT:evening] The park's time of day (docs/EVENING.md): presentation only. The clock advances with the play time, the look it gives (the sun, the ambient, the tints) is read by the renderer, and
// the lamps are one more point-light source. None of it is read by the simulation, saved in a park, or part of a state hash.
public partial class Level
{
	/// <summary>The longest frame the cycle counts as play time (a long hitch or a load must not jump the sun).</summary>
	internal const double EveningMaximumFrameSeconds = 0.25;

	/// <summary>
	/// [EXT:debug-evening] A pace chosen on the command line (<c>--evening-pace</c>) for this run only: read instead of the option, and never written to <see cref="GameOptions.Current"/>, so it is never saved.
	/// </summary>
	internal static EveningPace? EveningPaceOverride { get; set; }

	/// <summary>
	/// [EXT:perf-run] Performance hook of the browser harness (<c>tools/web-perf</c>): "night", "dusk" or "day" hold the evening look for the rest of the session (a run-scoped override like <c>--evening-pace</c>, never saved),
	/// "off" switches the evening extension off for the session, "on" switches it back on and clears the override. Returns false for another value.
	/// </summary>
	public static bool PerfEvening( string mode )
	{
		var enabled = mode != "off";
		OpenTpwExtensions.Activate( ModSet.Resolve( Array.Empty<DiscoveredMod>(),
			new ModSettings { Enabled = new Dictionary<string, bool> { [OfficialExtensions.Evening.Id] = enabled } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All ) );
		switch ( mode )
		{
			case "night": EveningPaceOverride = EveningPace.AlwaysNight; return true;
			case "dusk": EveningPaceOverride = EveningPace.AlwaysDusk; return true;
			case "day": EveningPaceOverride = EveningPace.AlwaysDay; return true;
			case "on": case "off": EveningPaceOverride = null; return true;
			default: return false;
		}
	}

	private EveningClock? eveningClock;
	private EveningLook? eveningLook;
	private EveningLampSource? eveningLamps;

	/// <summary>The look of the park now; full daylight (everything as the original draws it) unless the evening extension is on and it is not day. Null-safe for a level built without its constructor.</summary>
	internal EveningLook Look => eveningLook ?? EveningLook.Day;

	/// <summary>The park's day clock, or null while the evening extension is off.</summary>
	internal EveningClock? EveningDayClock => eveningClock;

	/// <summary>The evening lamps registered with the point lights, or null while the extension is off.</summary>
	internal EveningLampSource? EveningLampLights => eveningLamps;

	/// <summary>This frame's step of the day cycle, from the extension switch, the pace option, the frame time and the pause state.</summary>
	private void UpdateEvening() => AdvanceEvening( FeatureGates.Evening, EveningPaceOverride ?? GameOptions.Current.Evening, Time.Delta, SimulationTimeScale > 0f );

	/// <summary>
	/// Advances the day cycle by <paramref name="frameSeconds"/> of real time when the park is <paramref name="playing"/> (a paused park, whatever its speed setting, stands still; the cycle
	/// ignores the speed setting otherwise too) and sets the sun colour and the lamps for the new look. With the extension off nothing is created and the level draws as it always did.
	/// </summary>
	internal void AdvanceEvening( bool enabled, EveningPace pace, double frameSeconds, bool playing )
	{
		if ( !enabled )
		{
			if ( eveningClock != null )
				ResetEvening();
			return;
		}
		eveningClock ??= new EveningClock();
		eveningClock.Advance( playing ? Math.Min( Math.Max( frameSeconds, 0.0 ), EveningMaximumFrameSeconds ) : 0.0, pace );
		var look = eveningClock.Look( pace );
		eveningLook = look.IsDay ? null : look;
		if ( SunLight != null )
			SunLight.Color = look.SunColour;
		if ( eveningLamps == null && Objects != null && PointLights != null )
		{
			eveningLamps = new EveningLampSource( EveningLampTable.Shipped, () => Objects?.Objects ?? Array.Empty<OriginalObject>(), () => Look.LampLevel, () => Objects?.Version ?? 0 );
			PointLights.Add( eveningLamps );
		}
	}

	private void ResetEvening()
	{
		eveningClock = null;
		eveningLook = null;
		if ( eveningLamps != null )
			PointLights?.Remove( eveningLamps );
		eveningLamps = null;
		if ( SunLight != null )
			SunLight.Color = System.Numerics.Vector3.One;
	}
}
