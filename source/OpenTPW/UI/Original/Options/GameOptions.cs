using System.Text.Json;

namespace OpenTPW;

/// <summary>
/// Player options from the original Game Options screen that have no home in the existing
/// configuration: volumes (0–10 steps, the original sliders' resolution is unknown), popup help, and
/// OpenTPW's pending display choices for the stub display settings. Stored as JSON next to the
/// sandbox save (<c>save/opentpw-options.json</c>); audio code reads the volumes from here.
/// </summary>
public sealed class GameOptions
{
	public const string FileName = "opentpw-options.json";
	public const int MaximumVolume = 10;

	public int SoundEffectsVolume { get; set; } = 8;
	public int MusicVolume { get; set; } = 8;
	public int SpeechVolume { get; set; } = 8;
	public int MovieVolume { get; set; } = 8;
	public bool PopupHelp { get; set; } = true;
	public DisplayWindowMode WindowMode { get; set; } = DisplayWindowMode.Windowed;
	public DisplayUpscaleMethod UpscaleMethod { get; set; } = DisplayUpscaleMethod.Native;
	public int RenderScalePercent { get; set; } = 100;
	public float UiScale { get; set; } = 1f;

	public static GameOptions Current { get; set; } = new();

	/// <summary>Volume as a 0–1 gain.</summary>
	public static float Gain( int volume ) => Math.Clamp( volume, 0, MaximumVolume ) / (float)MaximumVolume;

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
		RenderScalePercent = Math.Clamp( RenderScalePercent, 50, 100 );
		UiScale = Math.Clamp( float.IsFinite( UiScale ) ? UiScale : 1f, 0.5f, 2f );
		return this;
	}
}

/// <summary>
/// Stub <see cref="IDisplaySettings"/> until the display slice lands: offers the original
/// resolution list (UITEXT 340–346) plus common modern sizes, stores the window size in the existing
/// <c>GameWindowSize</c> setting and everything else in <see cref="GameOptions"/>; nothing changes
/// live, so <see cref="Apply"/> reports <see cref="DisplayApplyResult.RestartRequired"/>.
/// </summary>
public sealed class StubDisplaySettings : IDisplaySettings
{
	public static readonly DisplayResolution[] OriginalResolutions =
	{
		new( 400, 300 ), new( 512, 384 ), new( 640, 480 ), new( 800, 600 ), new( 1024, 768 ), new( 1280, 1024 ), new( 1600, 1200 )
	};

	private readonly GameOptions options;
	private readonly Action<DisplayResolution>? persistResolution;
	private DisplayResolution applied;
	private (DisplayWindowMode, DisplayUpscaleMethod, int, float) appliedRest;

	public StubDisplaySettings( GameOptions options, DisplayResolution current, DisplayResolution output, Action<DisplayResolution>? persistResolution )
	{
		this.options = options;
		this.persistResolution = persistResolution;
		applied = Resolution = current;
		OutputSize = output;
		appliedRest = (options.WindowMode, options.UpscaleMethod, options.RenderScalePercent, options.UiScale);
		AvailableResolutions = OriginalResolutions.Concat( new DisplayResolution[] { new( 1280, 720 ), new( 1920, 1080 ), new( 2560, 1440 ), current } )
			.Distinct().OrderBy( size => size.Width ).ThenBy( size => size.Height ).ToArray();
	}

	public IReadOnlyList<DisplayResolution> AvailableResolutions { get; }
	public DisplayResolution Resolution { get; set; }
	public DisplayWindowMode WindowMode { get => options.WindowMode; set => options.WindowMode = value; }
	public DisplayUpscaleMethod UpscaleMethod { get => options.UpscaleMethod; set => options.UpscaleMethod = value; }
	public IReadOnlyList<int> RenderScalePresets { get; } = new[] { 77, 67, 59, 50 };
	public int RenderScalePercent { get => options.RenderScalePercent; set => options.RenderScalePercent = Math.Clamp( value, 50, 100 ); }
	public float UiScale { get => options.UiScale; set => options.UiScale = Math.Clamp( value, 0.5f, 2f ); }
	public DisplayResolution EffectiveInternalSize => UpscaleMethod == DisplayUpscaleMethod.Native ? OutputSize
		: new DisplayResolution( Math.Max( 1, OutputSize.Width * RenderScalePercent / 100 ), Math.Max( 1, OutputSize.Height * RenderScalePercent / 100 ) );
	public DisplayResolution OutputSize { get; }
	public string? FallbackReason => UpscaleMethod == DisplayUpscaleMethod.Native ? null : "upscaling is not implemented in this build";

	public DisplayApplyResult Apply()
	{
		var rest = (options.WindowMode, options.UpscaleMethod, options.RenderScalePercent, options.UiScale);
		if ( Resolution == applied && rest == appliedRest )
			return DisplayApplyResult.Unchanged;
		if ( Resolution != applied )
			persistResolution?.Invoke( Resolution );
		applied = Resolution;
		appliedRest = rest;
		return DisplayApplyResult.RestartRequired;
	}

	public void Confirm() { }

	public void Revert()
	{
		Resolution = applied;
		(options.WindowMode, options.UpscaleMethod, options.RenderScalePercent, options.UiScale) = appliedRest;
	}
}
