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
/// Volumes and mute toggles control the mixer and movies. Advisor/tutorial/help control their
/// respective consumers. Camera rotation and scroll modes, deletion confirmations and tool
/// right-click cancellation are applied by the park camera and HUD.
/// </summary>
public sealed class GameOptions
{
	public const string FileName = "opentpw-options.json";
	/// <summary>Volumes are percentages, 0..100 ({on, percent} pairs in the original, shown as " %d %%").</summary>
	public const int MaximumVolume = 100;
	/// <summary>Options files written before the volumes became percentages (0..10 steps) carry no <see cref="VolumeFormat"/>.</summary>
	public const int PercentVolumeFormat = 2;

	/// <summary>Written with every save so a later load knows the volumes are percentages; an absent value marks the old 0..10 steps.</summary>
	public int VolumeFormat { get; set; } = PercentVolumeFormat;

	// [BIN:TPW-EXE:sound.sam DefaultVolume] defaults SFX 75, Music 60, Speech 75, Movie 100 (percent). The globals holding them are medium confidence.
	// The movie volume is scaled by 1023 / 100 for the movie device and plays as DirectSound attenuation (see MovieVolumeCurve).
	// [BIN:TPW-EXE:0x4a4490 volume sliders] the options volume sliders get SetRange(0,100) (0x66b47d: min 0, max 100), an integer value and line and page steps of 1 (slider constructor 0x66ad77/0x66adc9), so OpenTPW's 1 % steps over 0..100 are the original's granularity.
	/// <summary>0..100 controls the recovered FADEOBJ effect only. Null/absent uses the loaded sound.sam default.
	/// This host policy does not reconstruct an original saved preference or other quality-controlled hardware features.</summary>
	public int? SoundQuality { get; set; }
	public int SoundEffectsVolume { get; set; } = 75;
	public int MusicVolume { get; set; } = 60;
	public int SpeechVolume { get; set; } = 75;
	public int MovieVolume { get; set; } = 100;
	// [BIN:STP-PPC:0x10125B7C options defaults] TooltipsOn (+0x36), AdvisorOn (+0x34), TutorialOn (+0x35), ConfirmDeleteOn (+0x37) and RMBCancelOn (+0x39) are set to 1, RMBScrollOn (+0x38) to 0 (names from the serializer 0x1012653C)
	public bool PopupHelp { get; set; } = true;
	/// <summary>Mute toggles next to the four volume sliders; off means silent whatever the volume.</summary>
	public bool SoundEffectsOn { get; set; } = true;
	public bool MusicOn { get; set; } = true;
	public bool SpeechOn { get; set; } = true;
	public bool MovieOn { get; set; } = true;
	// [BIN:TPW-EXE:IsometricOn rotation] rotation 0 is " Smooth", 1 is " 90 degs", default 1 (Ninety).
	public bool Advisor { get; set; } = true;
	public bool Tutorial { get; set; } = true;
	public bool Confirmations { get; set; } = true;
	public bool RmbCancel { get; set; } = true;
	public RotationMode Rotation { get; set; } = RotationMode.Ninety;
	// [BIN:STP-PPC:0x10125B7C options defaults] RMBScrollOn = 0: push scrolling
	public ScrollMode Scroll { get; set; } = ScrollMode.Pushscroll;
	/// <summary>[EXT:periodic-autosave] saves an owned park to its own autosave slot every 5 minutes of play (<see cref="PeriodicAutosave"/>); on by default, not an original option.</summary>
	public bool Autosave { get; set; } = true;

	/// <summary>
	/// The park camera's zoom range: on (the default) it is the original's own 40..300 (tp.exe's mode flag 0x200, <see cref="ParkCameraMode.WideMinimumHeight"/>), off the
	/// normal 70..130. Files written before the option existed lack it and get the default.
	/// </summary>
	// [EXT:wide-zoom] the OpenTPW page's "Wide zoom" row; user-approved 2026-10-18
	public bool WideZoom { get; set; } = true;

	/// <summary>
	/// [EXT:evening] How the evening and night extension's day cycle runs while that extension is on (docs/EVENING.md): a full day and night in 10, 20 or 40 minutes of play, or always day, dusk or night.
	/// Presentation only. Files written before the option existed lack it and get the default; an unknown value is read as the default.
	/// </summary>
	public EveningPace Evening { get; set; } = EveningPaces.Default;

	public static GameOptions Current { get; set; } = new();

	/// <summary>Volume as a 0-1 gain.</summary>
	public static float Gain( int volume ) => Math.Clamp( volume, 0, MaximumVolume ) / (float)MaximumVolume;

	/// <summary>Volume as a 0-1 gain; 0 while the matching sound is switched off.</summary>
	public static float Gain( int volume, bool on ) => on ? Gain( volume ) : 0;

	[JsonIgnore] public float SoundEffectsGain => Gain( SoundEffectsVolume, SoundEffectsOn );
	[JsonIgnore] public float MusicGain => Gain( MusicVolume, MusicOn );
	[JsonIgnore] public float SpeechGain => Gain( SpeechVolume, SpeechOn );
	/// <summary>The movie amplitude: the PC player's DirectSound decibel curve (<see cref="MovieVolumeCurve"/>), not a linear gain.</summary>
	[JsonIgnore] public float MovieGain => MovieVolumeCurve.Gain( MovieVolume, MovieOn );

	/// <summary>An independent copy (the options screen restores it on Cancel).</summary>
	public GameOptions Clone() => (GameOptions)MemberwiseClone();

	/// <summary>Overwrites every field with <paramref name="other"/>'s.</summary>
	public void CopyFrom( GameOptions other )
	{
		SoundQuality = other.SoundQuality;
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
		WideZoom = other.WideZoom;
		Autosave = other.Autosave;
		Evening = other.Evening;
	}

	public static GameOptions Load( string path )
	{
		try
		{
			if ( !File.Exists( path ) )
				return new GameOptions();
			var text = File.ReadAllText( path );
			var loaded = JsonSerializer.Deserialize<GameOptions>( text );
			if ( loaded == null )
				return new GameOptions();
			using ( var document = JsonDocument.Parse( text ) )
				if ( document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty( nameof( VolumeFormat ), out _ ) )
					loaded.MigrateStepVolumes( document.RootElement );
			return loaded.Normalized();
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

	/// <summary>Old files stored 0..10 steps of 10 %: every volume present in the file becomes its percentage; absent ones keep their defaults.</summary>
	private void MigrateStepVolumes( JsonElement root )
	{
		int Percent( string name, int current ) =>
			root.TryGetProperty( name, out var value ) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32( out var steps )
				? Math.Clamp( steps, 0, 10 ) * 10 : current;
		SoundEffectsVolume = Percent( nameof( SoundEffectsVolume ), SoundEffectsVolume );
		MusicVolume = Percent( nameof( MusicVolume ), MusicVolume );
		SpeechVolume = Percent( nameof( SpeechVolume ), SpeechVolume );
		MovieVolume = Percent( nameof( MovieVolume ), MovieVolume );
	}

	public GameOptions Normalized()
	{
		VolumeFormat = PercentVolumeFormat;
		if ( SoundQuality is { } quality ) SoundQuality = Math.Clamp( quality, 0, 100 );
		SoundEffectsVolume = Math.Clamp( SoundEffectsVolume, 0, MaximumVolume );
		MusicVolume = Math.Clamp( MusicVolume, 0, MaximumVolume );
		SpeechVolume = Math.Clamp( SpeechVolume, 0, MaximumVolume );
		MovieVolume = Math.Clamp( MovieVolume, 0, MaximumVolume );
		Evening = EveningPaces.Validated( Evening );
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
