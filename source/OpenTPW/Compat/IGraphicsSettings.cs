namespace OpenTPW;

/// <summary>
/// UI-agnostic graphics/detail options API for the frontend Options screen (the original game's
/// Low/Medium/High detail choice plus the OpenTPW Enhanced preset). Get it from <see cref="Instance"/>.
/// Settings persist in <c>graphics.json</c> in the user config directory, never in saves.
/// Filtering/mipmap changes take effect at the next start (<see cref="RestartRequired"/>); the view
/// distance applies immediately.
/// </summary>
public interface IGraphicsSettings
{
	public static IGraphicsSettings? Instance { get; internal set; }

	/// <summary>Stored settings (preset, custom values, anisotropy, view distance).</summary>
	GraphicsSettings Current { get; }
	/// <summary>Option values in effect: the original file's values for Low/Medium/High, High plus extensions for Enhanced, or the custom values.</summary>
	OriginalDetailSettings Detail { get; }
	/// <summary>What the renderer uses (filter, anisotropy, mipmaps, view distance).</summary>
	RenderQuality Effective { get; }
	/// <summary>Selectable presets (Low/Medium/High only when the original files were readable).</summary>
	IReadOnlyList<GraphicsPreset> Presets { get; }
	/// <summary>The option values a preset selects (null when its original file is missing).</summary>
	OriginalDetailSettings? GetPresetValues( GraphicsPreset preset );
	/// <summary>Per-option support note keyed by the original <c>.sam</c> key.</summary>
	IReadOnlyDictionary<string, string> Support { get; }
	/// <summary>True when an applied change needs a restart to reach the renderer.</summary>
	bool RestartRequired { get; }
	IReadOnlyList<string> Diagnostics { get; }

	/// <summary>Validates, applies and saves.</summary>
	void Apply( GraphicsSettings settings );
	event Action? Changed;
}

/// <summary>Default <see cref="IGraphicsSettings"/> implementation (no renderer dependency, so it is testable).</summary>
public sealed class GraphicsSettingsService : IGraphicsSettings
{
	private readonly Dictionary<GraphicsPreset, OriginalDetailSettings> originals = new();
	private readonly List<string> diagnostics = new();
	private readonly string? path;
	private readonly Func<CompatibilityFlags> flags;
	private readonly RenderQuality startupQuality;

	/// <param name="readOriginal">Opens an original preset file by data-relative path (e.g. <c>/high.sam</c>); null when missing.</param>
	/// <param name="path">Where to persist; null never writes (smoke tests).</param>
	public GraphicsSettingsService( Func<string, Stream?> readOriginal, GraphicsSettings settings, string? path, Func<CompatibilityFlags> flags )
	{
		this.path = path;
		this.flags = flags;
		foreach ( var (preset, file) in GraphicsPresets.OriginalFiles )
		{
			try
			{
				using var stream = readOriginal( file );
				if ( stream == null )
				{
					diagnostics.Add( $"Original detail file {file} is missing; the {preset} preset is unavailable." );
					continue;
				}
				var fileDiagnostics = new List<string>();
				originals[preset] = OriginalDetailSettings.FromSam( new SettingsFile( stream ), fileDiagnostics );
				diagnostics.AddRange( fileDiagnostics.Select( text => $"{file}: {text}" ) );
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException )
			{
				diagnostics.Add( $"Original detail file {file} could not be read ({exception.Message})." );
			}
		}
		Current = settings.Validate( diagnostics );
		if ( Current.Preset is not GraphicsPreset.Custom && !originals.ContainsKey( Current.Preset == GraphicsPreset.Enhanced ? GraphicsPreset.High : Current.Preset ) )
			diagnostics.Add( $"Graphics preset {Current.Preset} needs original data that is missing; the renderer keeps its built-in settings." );
		startupQuality = Effective;
	}

	public GraphicsSettings Current { get; private set; }

	public OriginalDetailSettings Detail => Resolve( Current ) ?? new OriginalDetailSettings();

	public RenderQuality Effective
	{
		get
		{
			var detail = Resolve( Current );
			if ( detail == null )
				return RenderQuality.Legacy with { ViewDistanceScale = Current.ViewDistanceScale };
			var viewDistance = Current.Preset switch
			{
				GraphicsPreset.Enhanced => GraphicsPresets.EnhancedViewDistanceScale,
				GraphicsPreset.Custom => Current.ViewDistanceScale,
				// Original presets keep OpenTPW's existing fog distance (scale 1).
				_ => 1f
			};
			return GraphicsPresets.ToRenderQuality( detail, Current.Preset == GraphicsPreset.Enhanced ? GraphicsPresets.EnhancedAnisotropy : Current.Anisotropy, viewDistance );
		}
	}

	public IReadOnlyList<GraphicsPreset> Presets =>
		Enum.GetValues<GraphicsPreset>().Where( preset => preset == GraphicsPreset.Custom || originals.ContainsKey( preset == GraphicsPreset.Enhanced ? GraphicsPreset.High : preset ) ).ToArray();

	public OriginalDetailSettings? GetPresetValues( GraphicsPreset preset ) => preset switch
	{
		GraphicsPreset.Custom => Current.Detail,
		GraphicsPreset.Enhanced => originals.TryGetValue( GraphicsPreset.High, out var high ) ? GraphicsPresets.Enhance( high, flags().IsEnabled( CompatibilityFixes.EnhancedGameOptions ) ) : null,
		_ => originals.TryGetValue( preset, out var values ) ? values : null
	};

	private OriginalDetailSettings? Resolve( GraphicsSettings settings )
	{
		if ( settings.Preset != GraphicsPreset.Custom )
			return GetPresetValues( settings.Preset );
		var custom = settings.Detail;
		// Simulation-affecting options only deviate from the original High values with the enhanced-game-options fix.
		if ( custom == null || flags().IsEnabled( CompatibilityFixes.EnhancedGameOptions ) || !originals.TryGetValue( GraphicsPreset.High, out var high ) )
			return custom;
		foreach ( var key in OriginalDetailSettings.SimulationKeys )
			custom = custom.With( key, high.Get( key ) );
		return custom;
	}

	public IReadOnlyDictionary<string, string> Support => GraphicsPresets.Support;

	public bool RestartRequired
	{
		get
		{
			var now = Effective;
			return now.Filter != startupQuality.Filter || now.MaxAnisotropy != startupQuality.MaxAnisotropy || now.Mipmaps != startupQuality.Mipmaps;
		}
	}

	public IReadOnlyList<string> Diagnostics => diagnostics;

	public event Action? Changed;

	public void Apply( GraphicsSettings settings )
	{
		Current = settings.Validate( diagnostics );
		if ( path != null )
			Current.Save( path );
		Changed?.Invoke();
	}

	/// <summary>
	/// Simulation-affecting values that deviate from the original preset they are based on (the
	/// economy slice records these with <see cref="CompatibilityFlags.ToMetadata"/>).
	/// </summary>
	public IReadOnlyList<string> SimulationDeviations()
	{
		var reference = Current.Preset switch
		{
			GraphicsPreset.Enhanced => GraphicsPreset.High,
			GraphicsPreset.Custom => GraphicsPreset.High,
			_ => Current.Preset
		};
		if ( !originals.TryGetValue( reference, out var original ) )
			return Array.Empty<string>();
		var detail = Detail;
		return OriginalDetailSettings.SimulationKeys.Where( key => detail.Get( key ) != original.Get( key ) )
			.Select( key => $"{key}={detail.Get( key )} (original {reference} {original.Get( key )})" ).ToArray();
	}
}
