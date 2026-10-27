using System.Numerics;
using SVector3 = System.Numerics.Vector3;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTPW;

// [EXT:evening] OpenTPW's own evening and night extension (docs/EVENING.md). The original game's parks are always in daylight: its theme .sam files carry an ambient and a
// directional light level, but tp.exe never changes them over time. Everything in this file is presentation only: it reads the frame time and the pause state and is read by the
// renderer; nothing here is read by the park simulation, enters a state hash, or is saved in a park.

/// <summary>
/// How the day and night cycle runs (the OpenTPW options page, "Evening"): a full day and night in 10, 20 or 40 minutes of play, or a fixed look. Stored in
/// <c>opentpw-options.json</c> by name.
/// </summary>
[JsonConverter( typeof( EveningPaceConverter ) )]
public enum EveningPace
{
	/// <summary>A full day and night every 20 minutes of play (the default).</summary>
	Minutes20 = 0,
	/// <summary>A full day and night every 10 minutes of play.</summary>
	Minutes10 = 1,
	/// <summary>A full day and night every 40 minutes of play.</summary>
	Minutes40 = 2,
	/// <summary>Always full daylight (the extension changes nothing, lamps stay off).</summary>
	AlwaysDay = 3,
	/// <summary>Always dusk: the sun low and warm, the lamps lit.</summary>
	AlwaysDusk = 4,
	/// <summary>Always full night: the moon, dark ground, every lamp lit.</summary>
	AlwaysNight = 5
}

/// <summary>Reads the pace by name; a name this version does not know (a newer or hand-edited options file) is the default instead of an error that would discard the whole options file.</summary>
public sealed class EveningPaceConverter : JsonConverter<EveningPace>
{
	public override EveningPace Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		if ( reader.TokenType == JsonTokenType.String && Enum.TryParse<EveningPace>( reader.GetString(), ignoreCase: true, out var pace ) && Enum.IsDefined( pace ) && !int.TryParse( reader.GetString(), out _ ) )
			return pace;
		reader.Skip();
		return EveningPaces.Default;
	}

	public override void Write( Utf8JsonWriter writer, EveningPace value, JsonSerializerOptions options ) => writer.WriteStringValue( EveningPaces.Validated( value ).ToString() );
}

/// <summary>The cycle's pace values: their order on the options row, their lengths and the validation of a stored value.</summary>
public static class EveningPaces
{
	/// <summary>The default pace.</summary>
	public const EveningPace Default = EveningPace.Minutes20;

	/// <summary>The values in the order the options row cycles through them: the three cycle lengths, then the fixed looks.</summary>
	public static IReadOnlyList<EveningPace> Cycle { get; } = new[] { EveningPace.Minutes10, EveningPace.Minutes20, EveningPace.Minutes40, EveningPace.AlwaysDay, EveningPace.AlwaysDusk, EveningPace.AlwaysNight };

	/// <summary>The play time of one full day and night in seconds, or 0 for a fixed look.</summary>
	public static double CycleSeconds( this EveningPace pace ) => pace switch
	{
		EveningPace.Minutes10 => 600.0,
		EveningPace.Minutes20 => 1200.0,
		EveningPace.Minutes40 => 2400.0,
		_ => 0.0
	};

	/// <summary>Whether <paramref name="pace"/> is a fixed look (always day, dusk or night) rather than a running cycle.</summary>
	public static bool IsFixed( this EveningPace pace ) => pace.CycleSeconds() == 0.0;

	/// <summary><paramref name="pace"/> when it names a value, else the default (a hand-edited or newer options file).</summary>
	public static EveningPace Validated( EveningPace pace ) => Enum.IsDefined( pace ) ? pace : Default;
}

/// <summary>
/// The look of the park at one moment of the day (everything the renderer needs): the sun's colour (which the shaders' directional term multiplies), how much of the original's 0.4
/// ambient is left, the tints of the sky, the fog and backdrop, the guests' sprites and the pool water, and how strongly the lamps shine. The look of full daylight
/// (<see cref="Day"/>) leaves every value as the original draws it, so a park at day is drawn exactly as without the extension.
/// </summary>
public readonly record struct EveningLook(
	float Daylight,
	SVector3 SunColour,
	float AmbientLevel,
	SVector3 SkyTint,
	SVector3 FogTint,
	SVector3 SpriteTint,
	SVector3 WaterTint,
	float LampLevel,
	SVector3 AmbientTint )
{
	/// <summary>Full daylight: the sun white, the whole ambient, no tint anywhere, the lamps off.</summary>
	public static EveningLook Day { get; } = new( 1f, SVector3.One, 1f, SVector3.One, SVector3.One, SVector3.One, SVector3.One, 0f, SVector3.One );

	/// <summary>[EXT:evening] The night tint of the self-lit materials that are no light source (water, signs, lava, beams, panels): the pool water's (<see cref="WaterTint"/>), the extension's other unlit content, which keeps a river as dark as the lit art round it; the sprites' own tint is brighter.</summary>
	public SVector3 UnlitTint => WaterTint;

	/// <summary>Whether this is full daylight, which the renderer draws exactly as the original does (every uniform stays as it was).</summary>
	public bool IsDay => Daylight >= 1f;

	/// <summary>
	/// The ambient scale for the shaders' <c>g_flAmbientLevel</c>: 0 at full daylight (the shaders read 0 as "unchanged", the 0.4 the original uses), else the fraction of that 0.4 that is left.
	/// </summary>
	public float AmbientUniform => IsDay ? 0f : AmbientLevel;

	/// <summary>
	/// The tinted ambient for <c>test.shader</c>'s <c>g_flAmbientR/G/B</c> (the padding after the time and the view distance): the share of the 0.4 per channel, so dusk is warm and night is blue. All zero at full
	/// daylight, which the shader reads as "unchanged"; a channel is never exactly 0 otherwise.
	/// </summary>
	public SVector3 AmbientColourUniform => IsDay ? SVector3.Zero : SVector3.Max( AmbientTint * AmbientLevel, new SVector3( 0.001f ) );

	// the moon's directional colour (the shaders' diffuse term at night) and the night floors of the tints; all linear 0..1 and tuned by eye later (docs/EVENING.md, "Tuning")
	private static readonly SVector3 Moon = new( 0.08f, 0.14f, 0.40f );
	private static readonly SVector3 SkyNight = new( 0.07f, 0.09f, 0.22f );
	private static readonly SVector3 FogNight = new( 0.05f, 0.07f, 0.16f );
	private static readonly SVector3 SpriteNight = new( 0.34f, 0.38f, 0.60f );
	private static readonly SVector3 AmbientNight = new( 0.55f, 0.80f, 1.35f );
	private static readonly SVector3 AmbientWarm = new( 1.00f, 0.64f, 0.44f );
	private static readonly SVector3 WaterNight = new( 0.20f, 0.28f, 0.46f );
	// what a low sun does to each: multiplied in with the weight of the dusk glow (0 at full day and at full night, 1 at the middle of the dusk)
	private static readonly SVector3 SunWarm = new( 1.80f, 0.85f, 0.30f );
	private static readonly SVector3 SkyWarm = new( 1.00f, 0.58f, 0.42f );
	private static readonly SVector3 FogWarm = new( 1.00f, 0.62f, 0.48f );
	private static readonly SVector3 SpriteWarm = new( 1.00f, 0.78f, 0.62f );
	private static readonly SVector3 WaterWarm = new( 1.00f, 0.85f, 0.78f );
	/// <summary>The power of the daylight that fades the sun (above 1, so dusk is dimmer than day; the low dusk sun stays strong enough to turn green ground warm).</summary>
	public const float SunFadePower = 1.8f;
	/// <summary>The ambient fraction left at full night: 0.35 of the original's 0.4, so the ground stays readable under the moon and the lamps.</summary>
	public const float NightAmbient = 0.35f;

	/// <summary>The look at <paramref name="daylight"/> (1 full day, 0 full night; clamped), with the dusk glow weighted by <see cref="Glow"/>.</summary>
	public static EveningLook At( float daylight )
	{
		daylight = float.IsNaN( daylight ) ? 1f : Math.Clamp( daylight, 0f, 1f );
		if ( daylight >= 1f )
			return Day;
		var glow = Glow( daylight );
		SVector3 Blend( SVector3 night, SVector3 warm ) => SVector3.Lerp( night, SVector3.One, daylight ) * SVector3.Lerp( SVector3.One, warm, glow );
		return new EveningLook(
			daylight,
			// the sun fades faster than the daylight (to the power 1.5), so the dusk is clearly dimmer than the day
			Vector3Lerp( Moon, SVector3.One, MathF.Pow( daylight, SunFadePower ) ) * Vector3Lerp( SVector3.One, SunWarm, glow ),
			Lerp( NightAmbient, 1f, MathF.Pow( daylight, 1.4f ) ),
			Blend( SkyNight, SkyWarm ),
			Blend( FogNight, FogWarm ),
			Blend( SpriteNight, SpriteWarm ),
			Blend( WaterNight, WaterWarm ),
			LampLevelAt( daylight ),
			Blend( AmbientNight, AmbientWarm ) );
	}

	/// <summary>The weight of the dusk glow: 0 at full day and at full night, 1 at <c>daylight</c> 0.5 (a bell, 4 d (1 - d)).</summary>
	public static float Glow( float daylight ) => 4f * daylight * ( 1f - daylight );

	/// <summary>How strongly the lamps shine: 0 while it is light (<c>daylight</c> 0.75 and more), growing smoothly to 1 at <c>daylight</c> 0.35 and less, so they come on as it gets dark and fade out at dawn.</summary>
	public static float LampLevelAt( float daylight ) => 1f - SmoothStep( ( daylight - 0.35f ) / 0.4f );

	private static SVector3 Vector3Lerp( SVector3 a, SVector3 b, float t ) => SVector3.Lerp( a, b, t );

	private static float Lerp( float a, float b, float t ) => a + ( b - a ) * t;

	/// <summary>The cubic smooth step of <paramref name="x"/> clamped to 0..1.</summary>
	public static float SmoothStep( float x )
	{
		x = Math.Clamp( x, 0f, 1f );
		return x * x * ( 3f - 2f * x );
	}

	/// <summary>The sky colour channels as the 0..1 fog colour the viewport clear and the distant objects fade into: <paramref name="colour"/> scaled by <see cref="FogTint"/>.</summary>
	public SVector3 TintFog( SVector3 colour ) => IsDay ? colour : colour * FogTint;
}

/// <summary>
/// The park's time of day (docs/EVENING.md): a phase from 0 to 1 over one full day and night, which starts at noon (0) and advances with the <b>play time</b>: the real frame time while
/// the park runs, nothing while it is paused (the pause menu, a speed setting of Paused, the map and the other windows that stop the park; <c>Level.SimulationTimeScale</c> is 0), and
/// the same at every park speed (a park day of the original is about 6 seconds, which would be a strobe light).
/// </summary>
public sealed class EveningClock
{
	/// <summary>The fraction of a day on each side of noon that is still full daylight (3 minutes before and after at the default pace of 20).</summary>
	public const double FullDayHalf = 0.15;
	/// <summary>The distance from noon at which it is full night (the dusk runs from 0.15 to 0.30; night lasts until 0.70, then the dawn runs back).</summary>
	public const double FullNightFrom = 0.30;

	/// <summary>The phase in a day, 0 (noon) up to but excluding 1: 0.25 is about the middle of the dusk, 0.5 midnight, 0.75 about the middle of the dawn.</summary>
	public double Phase { get; private set; }

	/// <summary>Sets the phase (wrapped into 0..1); a non-finite value is noon.</summary>
	public void SetPhase( double phase ) => Phase = double.IsFinite( phase ) ? phase - Math.Floor( phase ) : 0.0;

	/// <summary>Moves the cycle on by <paramref name="playSeconds"/> of play time at <paramref name="pace"/>; a fixed look and a negative or non-finite step change nothing.</summary>
	public void Advance( double playSeconds, EveningPace pace )
	{
		var length = pace.CycleSeconds();
		if ( length <= 0.0 || !double.IsFinite( playSeconds ) || playSeconds <= 0.0 )
			return;
		SetPhase( Phase + playSeconds / length );
	}

	/// <summary>How light it is at <paramref name="phase"/>: 1 around noon, 0 at night, a smooth curve between (<see cref="FullDayHalf"/> and <see cref="FullNightFrom"/> from noon, mirrored round midnight).</summary>
	public static float Daylight( double phase )
	{
		phase = double.IsFinite( phase ) ? phase - Math.Floor( phase ) : 0.0;
		var distance = Math.Min( phase, 1.0 - phase );
		if ( distance <= FullDayHalf )
			return 1f;
		if ( distance >= FullNightFrom )
			return 0f;
		return 1f - EveningLook.SmoothStep( (float)( ( distance - FullDayHalf ) / ( FullNightFrom - FullDayHalf ) ) );
	}

	/// <summary>The daylight of a fixed pace: 1, the middle of the dusk (0.5), or 0.</summary>
	public static float FixedDaylight( EveningPace pace ) => pace switch
	{
		EveningPace.AlwaysDusk => 0.5f,
		EveningPace.AlwaysNight => 0f,
		_ => 1f
	};

	/// <summary>The look of the park at <paramref name="phase"/> under <paramref name="pace"/>: a fixed pace ignores the phase.</summary>
	public static EveningLook LookFor( EveningPace pace, double phase ) => EveningLook.At( pace.IsFixed() ? FixedDaylight( pace ) : Daylight( phase ) );

	/// <summary>The look now.</summary>
	public EveningLook Look( EveningPace pace ) => LookFor( pace, Phase );
}
