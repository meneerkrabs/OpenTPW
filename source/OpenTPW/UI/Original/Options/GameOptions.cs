using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>Camera rotation of the Rotation option (UITEXT 350-352).</summary>
[JsonConverter( typeof( JsonStringEnumConverter ) )]
public enum RotationMode { Smooth, Ninety }

/// <summary>Camera scrolling of the Scroll option (UITEXT 353-355).</summary>
[JsonConverter( typeof( JsonStringEnumConverter ) )]
public enum ScrollMode { Pushscroll, RightButton }

/// <summary>
/// Player options from the original Game Options screen that have no home in the existing
/// configuration: volumes (0-10 steps, the original sliders' resolution is unknown) with their mute
/// toggles, and the gameplay switches (display options belong to <see cref="IDisplaySettings"/>,
/// the graphics preset to <see cref="IGraphicsSettings"/>). Stored as JSON next to the sandbox save
/// (<c>save/opentpw-options.json</c>); files written by older versions lack the newer fields and
/// get their defaults.
/// Consumed today: <see cref="MovieVolume"/>/<see cref="MovieOn"/> (start-up movies) and
/// <see cref="PopupHelp"/> (front end and HUD), <see cref="Advisor"/> and <see cref="Tutorial"/> (the automatic
/// advisor, <see cref="AutomaticAdvisor"/>). STORED ONLY, with no effect yet: the sound effects,
/// music and speech volumes and switches (no game audio mixes them), <see cref="Confirmations"/>, <see cref="RmbCancel"/>, <see cref="Rotation"/>
/// and <see cref="Scroll"/> (the camera and tools do not read them). They are kept so the options screen
/// round-trips the original settings; each consumer must start reading them when its feature exists.
/// </summary>
public sealed class GameOptions
{
	public const string FileName = "opentpw-options.json";
	public const int MaximumVolume = 10;

	// [APPROX:UI-030] 0..10 volume steps, default 8, popup help on — evidence needed: original options defaults
	public int SoundEffectsVolume { get; set; } = 8;
	public int MusicVolume { get; set; } = 8;
	public int SpeechVolume { get; set; } = 8;
	public int MovieVolume { get; set; } = 8;
	public bool PopupHelp { get; set; } = true;
	/// <summary>Mute toggles next to the four volume sliders; off means silent whatever the volume.</summary>
	public bool SoundEffectsOn { get; set; } = true;
	public bool MusicOn { get; set; } = true;
	public bool SpeechOn { get; set; } = true;
	public bool MovieOn { get; set; } = true;
	// [APPROX:UI-030] right-column defaults: all on, 90 degs rotation (as in the supplied capture), pushscroll
	public bool Advisor { get; set; } = true;
	public bool Tutorial { get; set; } = true;
	public bool Confirmations { get; set; } = true;
	public bool RmbCancel { get; set; } = true;
	public RotationMode Rotation { get; set; } = RotationMode.Ninety;
	public ScrollMode Scroll { get; set; } = ScrollMode.Pushscroll;

	public static GameOptions Current { get; set; } = new();

	/// <summary>Volume as a 0-1 gain.</summary>
	public static float Gain( int volume ) => Math.Clamp( volume, 0, MaximumVolume ) / (float)MaximumVolume;

	/// <summary>Volume as a 0-1 gain; 0 while the matching sound is switched off.</summary>
	public static float Gain( int volume, bool on ) => on ? Gain( volume ) : 0;

	[JsonIgnore] public float SoundEffectsGain => Gain( SoundEffectsVolume, SoundEffectsOn );
	[JsonIgnore] public float MusicGain => Gain( MusicVolume, MusicOn );
	[JsonIgnore] public float SpeechGain => Gain( SpeechVolume, SpeechOn );
	[JsonIgnore] public float MovieGain => Gain( MovieVolume, MovieOn );

	/// <summary>An independent copy (the options screen restores it on Cancel).</summary>
	public GameOptions Clone() => (GameOptions)MemberwiseClone();

	/// <summary>Overwrites every field with <paramref name="other"/>'s.</summary>
	public void CopyFrom( GameOptions other )
	{
		SoundEffectsVolume = other.SoundEffectsVolume;
		MusicVolume = other.MusicVolume;
		SpeechVolume = other.SpeechVolume;
		MovieVolume = other.MovieVolume;
		PopupHelp = other.PopupHelp;
		SoundEffectsOn = other.SoundEffectsOn;
		MusicOn = other.MusicOn;
		SpeechOn = other.SpeechOn;
		MovieOn = other.MovieOn;
		Advisor = other.Advisor;
		Tutorial = other.Tutorial;
		Confirmations = other.Confirmations;
		RmbCancel = other.RmbCancel;
		Rotation = other.Rotation;
		Scroll = other.Scroll;
	}

	public static GameOptions Load( string path )
	{
		try
		{
			if ( !File.Exists( path ) )
				return new GameOptions();
			return JsonSerializer.Deserialize<GameOptions>( File.ReadAllText( path ) )?.Normalized() ?? new GameOptions();
		}
		catch ( Exception exception ) when ( exception is JsonException or IOException or UnauthorizedAccessException )
		{
			Log?.Warning( $"Options file {path} ignored: {exception.Message}" );
			return new GameOptions();
		}
	}

	public void Save( string path )
	{
		var temporary = path + ".tmp";
		File.WriteAllText( temporary, JsonSerializer.Serialize( Normalized(), new JsonSerializerOptions { WriteIndented = true } ) );
		File.Move( temporary, path, true );
	}

	public GameOptions Normalized()
	{
		SoundEffectsVolume = Math.Clamp( SoundEffectsVolume, 0, MaximumVolume );
		MusicVolume = Math.Clamp( MusicVolume, 0, MaximumVolume );
		SpeechVolume = Math.Clamp( SpeechVolume, 0, MaximumVolume );
		MovieVolume = Math.Clamp( MovieVolume, 0, MaximumVolume );
		return this;
	}
}

/// <summary>
/// In-memory <see cref="IDisplaySettings"/> for tests and headless use (the renderer is the real
/// implementation). Offers the original resolution list plus common sizes; applying changes nothing
/// on screen; a confirmation stays pending until <see cref="Confirm"/>, <see cref="Revert"/> or
/// <see cref="ExpireConfirmation"/>.
/// </summary>
public sealed class StubDisplaySettings : IDisplaySettings
{
	private readonly List<string> diagnostics = new();
	private DisplaySettings? previous;

	public StubDisplaySettings( DisplaySettings? current = null, Point2? output = null )
	{
		Current = current ?? DisplaySettings.Default;
		Output = output ?? new Point2( Current.Width, Current.Height );
	}

	public Point2 Output { get; set; }
	public DisplaySettings Current { get; private set; }
	public int Applies { get; private set; }
	public Point2 CurrentResolution => new( Current.Width, Current.Height );
	public IReadOnlyList<Point2> GetResolutions( WindowMode mode ) =>
		DisplayModes.Original.Concat( DisplayModes.Common ).Append( CurrentResolution ).GroupBy( size => (size.X, size.Y) ).Select( group => group.First() )
			.OrderBy( size => size.X * size.Y ).ThenBy( size => size.X ).ToArray();
	public IReadOnlyList<int> RenderScalePresets => RenderScaling.Presets;
	public int MinimumRenderScale => DisplaySettings.MinimumRenderScale;
	public int MaximumRenderScale => DisplaySettings.MaximumRenderScale;
	public int MaximumUiScale => DisplaySettings.MaximumUiScale;
	public RenderScaleResult Effective => Current.Upscale == UpscaleMode.Native
		? new RenderScaleResult( UpscaleMode.Native, 100, UpscaleMode.Native, 100, Output, Output, null )
		: new RenderScaleResult( Current.Upscale, Current.RenderScale, Current.Upscale, Current.RenderScale,
			new Point2( Math.Max( 1, Output.X * Current.RenderScale / 100 ), Math.Max( 1, Output.Y * Current.RenderScale / 100 ) ), Output, null );
	public DisplayMetrics Metrics => new( Output, Output );
	public int EffectiveUiScale => UiScaling.Resolve( Current.UiScale, Output );
	public IReadOnlyList<string> Diagnostics => diagnostics;
	public bool IsConfirmationPending => previous != null;
	public int ConfirmationSecondsRemaining => previous == null ? 0 : 15;

	public event Action? Changed;
	public event Action<DisplaySettings>? Reverted;

	public void Apply( DisplaySettings settings )
	{
		previous = null;
		Current = settings.Validate( diagnostics );
		Applies++;
		Changed?.Invoke();
	}

	public void ApplyWithConfirmation( DisplaySettings settings, TimeSpan timeout )
	{
		var restore = previous ?? Current;
		Apply( settings );
		previous = restore;
	}

	public void Confirm() => previous = null;

	public void Revert()
	{
		if ( previous == null )
			return;
		var restore = previous;
		previous = null;
		Current = restore;
		Changed?.Invoke();
		Reverted?.Invoke( restore );
	}

	/// <summary>Simulates the confirmation timeout.</summary>
	public void ExpireConfirmation() => Revert();
}
