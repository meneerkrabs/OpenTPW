using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>How the game window occupies the screen.</summary>
public enum WindowMode
{
	Windowed,
	/// <summary>Borderless window covering the desktop at its current display mode (SDL "fullscreen desktop").</summary>
	Borderless,
	/// <summary>Exclusive fullscreen that switches the display mode (experimental; see docs/UPSCALING-DESIGN.md).</summary>
	Exclusive
}

/// <summary>How the 3D world target reaches the output size (docs/UPSCALING-DESIGN.md, M6-U1).</summary>
public enum UpscaleMode
{
	/// <summary>World renders at output size; the existing blit pass, no new filter.</summary>
	Native,
	/// <summary>World renders at the render scale; bilinear upscaling in the blit pass.</summary>
	Linear,
	/// <summary>World renders at the render scale; nearest-neighbour upscaling (deliberate retro option).</summary>
	Nearest
}

/// <summary>
/// User display preferences. Stored as JSON in the user configuration directory (never in park or
/// original saves). Missing fields keep their defaults; invalid values fall back with a diagnostic.
/// </summary>
public sealed record DisplaySettings
{
	public const int MinimumWindowWidth = 320;
	public const int MinimumWindowHeight = 200;
	public const int MaximumWindowSize = 16384;
	public const int MinimumRenderScale = 50;
	public const int MaximumRenderScale = 100;
	public const int MaximumUiScale = 8;
	public const string FileName = "display.json";

	/// <summary>Windowed size in logical (desktop) units; on HiDPI displays the drawable is larger.</summary>
	public int Width { get; init; } = 1280;
	public int Height { get; init; } = 720;
	public WindowMode Mode { get; init; } = WindowMode.Windowed;
	public UpscaleMode Upscale { get; init; } = UpscaleMode.Native;
	/// <summary>Percentage of the output width and height used for the 3D world (not of the pixel count).</summary>
	public int RenderScale { get; init; } = 100;
	/// <summary>Integer BF4 UI scale; 0 selects it from the output size (<see cref="UiScaling.Automatic"/>).</summary>
	public int UiScale { get; init; }

	public static DisplaySettings Default { get; } = new();

	/// <summary>Returns a valid copy; every replaced value adds a diagnostic.</summary>
	public DisplaySettings Validate( ICollection<string> diagnostics )
	{
		var result = this;
		if ( Width < MinimumWindowWidth || Height < MinimumWindowHeight || Width > MaximumWindowSize || Height > MaximumWindowSize )
		{
			diagnostics.Add( $"Window size {Width}x{Height} is outside {MinimumWindowWidth}x{MinimumWindowHeight}..{MaximumWindowSize}x{MaximumWindowSize}; using {Default.Width}x{Default.Height}." );
			result = result with { Width = Default.Width, Height = Default.Height };
		}
		if ( !Enum.IsDefined( Mode ) )
		{
			diagnostics.Add( $"Unknown window mode {(int)Mode}; using windowed." );
			result = result with { Mode = WindowMode.Windowed };
		}
		if ( !Enum.IsDefined( Upscale ) )
		{
			diagnostics.Add( $"Unknown upscale mode {(int)Upscale}; falling back to native." );
			result = result with { Upscale = UpscaleMode.Native, RenderScale = 100 };
		}
		if ( result.RenderScale < MinimumRenderScale || result.RenderScale > MaximumRenderScale )
		{
			diagnostics.Add( $"Render scale {result.RenderScale}% is outside {MinimumRenderScale}-{MaximumRenderScale}%; falling back to native." );
			result = result with { Upscale = UpscaleMode.Native, RenderScale = 100 };
		}
		if ( result.Upscale == UpscaleMode.Native && result.RenderScale != 100 )
			result = result with { RenderScale = 100 };
		if ( UiScale < 0 || UiScale > MaximumUiScale )
		{
			diagnostics.Add( $"UI scale {UiScale} is outside 0 (automatic)..{MaximumUiScale}; using automatic." );
			result = result with { UiScale = 0 };
		}
		return result;
	}

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	public string ToJson() => JsonSerializer.Serialize( this, JsonOptions ).Replace( "\r\n", "\n" );

	/// <summary>Parses JSON; malformed input returns the defaults with a diagnostic.</summary>
	public static DisplaySettings FromJson( string json, ICollection<string> diagnostics )
	{
		try
		{
			var settings = JsonSerializer.Deserialize<DisplaySettings>( json, JsonOptions );
			if ( settings != null )
				return settings.Validate( diagnostics );
			diagnostics.Add( "Display settings file is empty; using defaults." );
		}
		catch ( JsonException exception )
		{
			diagnostics.Add( $"Display settings file is invalid ({exception.Message}); using defaults." );
		}
		return Default;
	}

	/// <summary>
	/// User configuration directory: <c>OPENTPW_CONFIG_DIR</c>, else the platform application-data
	/// directory (<c>~/.config/OpenTPW</c> on macOS/Linux, <c>%APPDATA%\OpenTPW</c> on Windows).
	/// </summary>
	public static string GetDefaultPath()
	{
		var directory = Environment.GetEnvironmentVariable( "OPENTPW_CONFIG_DIR" );
		if ( string.IsNullOrWhiteSpace( directory ) )
			directory = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify ), "OpenTPW" );
		return Path.Combine( directory, FileName );
	}

	public static DisplaySettings Load( string path, ICollection<string> diagnostics )
	{
		if ( !File.Exists( path ) )
			return Default;
		try
		{
			return FromJson( File.ReadAllText( path ), diagnostics );
		}
		catch ( IOException exception )
		{
			diagnostics.Add( $"Display settings could not be read ({exception.Message}); using defaults." );
			return Default;
		}
	}

	public void Save( string path )
	{
		Directory.CreateDirectory( Path.GetDirectoryName( Path.GetFullPath( path ) )! );
		var temporary = path + ".tmp";
		File.WriteAllText( temporary, ToJson() );
		File.Move( temporary, path, overwrite: true );
	}

	/// <summary>
	/// Applies <c>--resolution WxH</c>, <c>--fullscreen</c>, <c>--fullscreen-exclusive</c>, <c>--windowed</c>,
	/// <c>--upscale native|linear|nearest</c>, <c>--render-scale &lt;percent&gt;</c> and <c>--ui-scale auto|N</c>.
	/// Malformed values throw <see cref="ArgumentException"/>; well-formed but unsupported values fall
	/// back through <see cref="Validate"/> with a diagnostic.
	/// </summary>
	public DisplaySettings ApplyCommandLine( string[] args, ICollection<string> diagnostics )
	{
		var result = this;
		var resolution = GetOption( args, "--resolution", "a size such as 2560x1440" );
		if ( resolution != null )
		{
			if ( !TryParseSize( resolution, out var width, out var height ) )
				throw new ArgumentException( $"--resolution expects WIDTHxHEIGHT such as 2560x1440, not '{resolution}'." );
			result = result with { Width = width, Height = height };
		}
		var modes = new[] { "--windowed", "--fullscreen", "--fullscreen-exclusive" }.Where( args.Contains ).ToArray();
		if ( modes.Length > 1 )
			throw new ArgumentException( $"Choose one of {string.Join( ", ", modes )}." );
		if ( modes.Length == 1 )
			result = result with { Mode = modes[0] switch { "--windowed" => WindowMode.Windowed, "--fullscreen" => WindowMode.Borderless, _ => WindowMode.Exclusive } };
		var upscale = GetOption( args, "--upscale", "native, linear or nearest" );
		if ( upscale != null )
		{
			if ( !Enum.TryParse<UpscaleMode>( upscale, ignoreCase: true, out var mode ) || !Enum.IsDefined( mode ) || int.TryParse( upscale, out _ ) )
				throw new ArgumentException( $"--upscale expects native, linear or nearest, not '{upscale}'." );
			result = result with { Upscale = mode };
		}
		var scale = GetOption( args, "--render-scale", "a percentage from 50 to 100" );
		if ( scale != null )
		{
			if ( !int.TryParse( scale.TrimEnd( '%' ), NumberStyles.None, CultureInfo.InvariantCulture, out var percent ) )
				throw new ArgumentException( $"--render-scale expects a whole percentage such as 67, not '{scale}'." );
			result = result with { RenderScale = percent };
			// A scale without a method selects the portable default method rather than being ignored.
			if ( upscale == null && result.Upscale == UpscaleMode.Native && percent != 100 )
				result = result with { Upscale = UpscaleMode.Linear };
		}
		else if ( upscale != null && result.Upscale != UpscaleMode.Native && result.RenderScale == 100 )
			result = result with { RenderScale = RenderScaling.DefaultPreset };
		if ( upscale != null && result.Upscale == UpscaleMode.Native )
			result = result with { RenderScale = 100 };
		var uiScale = GetOption( args, "--ui-scale", "auto or a whole number" );
		if ( uiScale != null )
		{
			if ( uiScale.Equals( "auto", StringComparison.OrdinalIgnoreCase ) )
				result = result with { UiScale = 0 };
			else if ( int.TryParse( uiScale, NumberStyles.None, CultureInfo.InvariantCulture, out var value ) )
				result = result with { UiScale = value };
			else
				throw new ArgumentException( $"--ui-scale expects auto or a whole number, not '{uiScale}'." );
		}
		return result.Validate( diagnostics );
	}

	public static bool TryParseSize( string text, out int width, out int height )
	{
		width = height = 0;
		var parts = text.Split( 'x', 'X', '×' );
		return parts.Length == 2
			&& int.TryParse( parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out width )
			&& int.TryParse( parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out height );
	}

	private static string? GetOption( string[] args, string name, string description )
	{
		var index = Array.IndexOf( args, name );
		if ( index < 0 )
			return null;
		if ( index + 1 >= args.Length || args[index + 1].StartsWith( "--" ) )
			throw new ArgumentException( $"{name} requires {description}." );
		return args[index + 1];
	}

	public string Describe() =>
		$"{Mode} {Width}x{Height}, upscale {Upscale} {RenderScale}%, UI scale {(UiScale == 0 ? "auto" : UiScale.ToString( CultureInfo.InvariantCulture ))}";
}
