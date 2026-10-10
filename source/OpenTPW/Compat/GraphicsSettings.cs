using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>Detail presets: the three original <c>low/med/high.sam</c> files, the OpenTPW Enhanced extension, or Custom values.</summary>
public enum GraphicsPreset
{
	Low,
	Medium,
	High,
	/// <summary>[EXT:COMPAT-GFX-ENHANCED] OpenTPW extension beyond the original High preset (docs/COMPATIBILITY.md).</summary>
	Enhanced,
	Custom
}

/// <summary>
/// Every option of the original detail files, by its <c>.sam</c> key (<c>GraphicalOptions.*</c> and
/// <c>GameOptions.*</c>). Value ranges are the legends in the files' own comments
/// ([DATA:low.sam/med.sam/high.sam:comment legend]).
/// </summary>
public sealed record OriginalDetailSettings
{
	/// <summary>0 Minimum, 1 Low, 2 Medium, 3 High.</summary>
	public int TextureQuality { get; init; }
	/// <summary>0 Point, 1 Bilinear, 2 Trilinear, 3 Anisotropic.</summary>
	public int TextureFiltering { get; init; }
	/// <summary>0 Off, 1 16x16, 2 32x32, 3 64x64.</summary>
	public int ProceduralTexturing { get; init; }
	/// <summary>0 Small, 1 High.</summary>
	public int ProceduralTextureCache { get; init; }
	public int AnimatingTextures { get; init; }
	/// <summary>1, 2 or 4 layers.</summary>
	public int SkyQuality { get; init; }
	public int SkyDetail { get; init; }
	public int SkyShadow { get; init; }
	public int MeshShadow { get; init; }
	/// <summary>0 Off, 1 Low, 2 Medium, 3 High.</summary>
	public int SpriteShadow { get; init; }
	public int TripleBuffer { get; init; }
	/// <summary>0 Vertex, 1 Depth.</summary>
	public int Fog { get; init; }
	public int Mipmap { get; init; }
	public int Render32 { get; init; }
	public int Texture32 { get; init; }
	public int BumpMapping { get; init; }
	/// <summary>0 Short, 1 Medium, 2 Long, 3 Maximum (first-person view).</summary>
	public int FirstPersonViewDistance { get; init; }
	public int Lighting { get; init; }
	/// <summary>Not described in the legend.</summary>
	public int ForceLowAlphaRef { get; init; }
	/// <summary>1..8.</summary>
	public int CoasterSmoothness { get; init; }
	public int CoasterTrackDetail { get; init; }
	/// <summary>500..2000. Simulation-affecting.</summary>
	public int ParticleDensity { get; init; }
	/// <summary>Not described in the legend. Simulation-affecting.</summary>
	public int TotalParticles { get; init; }
	/// <summary>0 -> 4, 1 -> 6, 2 -> 8, 3 -> MAX. Simulation-affecting.</summary>
	public int NumKids { get; init; }
	public int Weather { get; init; }
	/// <summary>Not described in the legend (lobby scenery count).</summary>
	public int LobbyObjects { get; init; }

	/// <summary>(.sam key, getter, setter) for every field, in file order.</summary>
	public static readonly IReadOnlyList<(string Key, Func<OriginalDetailSettings, int> Get, Func<OriginalDetailSettings, int, OriginalDetailSettings> Set)> Fields = new (string, Func<OriginalDetailSettings, int>, Func<OriginalDetailSettings, int, OriginalDetailSettings>)[]
	{
		("GraphicalOptions.TEXTUREQUALITY", s => s.TextureQuality, ( s, v ) => s with { TextureQuality = v }),
		("GraphicalOptions.TEXTUREFILTERING", s => s.TextureFiltering, ( s, v ) => s with { TextureFiltering = v }),
		("GraphicalOptions.PROCEDURALTEXTURING", s => s.ProceduralTexturing, ( s, v ) => s with { ProceduralTexturing = v }),
		("GraphicalOptions.PROCEDURALTEXTURECACHE", s => s.ProceduralTextureCache, ( s, v ) => s with { ProceduralTextureCache = v }),
		("GraphicalOptions.ANIMATINGTEXTURES", s => s.AnimatingTextures, ( s, v ) => s with { AnimatingTextures = v }),
		("GraphicalOptions.SKYQUALITY", s => s.SkyQuality, ( s, v ) => s with { SkyQuality = v }),
		("GraphicalOptions.SKYDETAIL", s => s.SkyDetail, ( s, v ) => s with { SkyDetail = v }),
		("GraphicalOptions.SKYSHADOW", s => s.SkyShadow, ( s, v ) => s with { SkyShadow = v }),
		("GraphicalOptions.MESHSHADOW", s => s.MeshShadow, ( s, v ) => s with { MeshShadow = v }),
		("GraphicalOptions.SPRITESHADOW", s => s.SpriteShadow, ( s, v ) => s with { SpriteShadow = v }),
		("GraphicalOptions.TRIPLEBUFFER", s => s.TripleBuffer, ( s, v ) => s with { TripleBuffer = v }),
		("GraphicalOptions.FOG", s => s.Fog, ( s, v ) => s with { Fog = v }),
		("GraphicalOptions.MIPMAP", s => s.Mipmap, ( s, v ) => s with { Mipmap = v }),
		("GraphicalOptions.RENDER32", s => s.Render32, ( s, v ) => s with { Render32 = v }),
		("GraphicalOptions.TEXTURE32", s => s.Texture32, ( s, v ) => s with { Texture32 = v }),
		("GraphicalOptions.BUMPMAPPING", s => s.BumpMapping, ( s, v ) => s with { BumpMapping = v }),
		("GraphicalOptions.FIRSTPERSONVIEWDISTANCE", s => s.FirstPersonViewDistance, ( s, v ) => s with { FirstPersonViewDistance = v }),
		("GraphicalOptions.LIGHTING", s => s.Lighting, ( s, v ) => s with { Lighting = v }),
		("GraphicalOptions.FORCELOWALPHAREF", s => s.ForceLowAlphaRef, ( s, v ) => s with { ForceLowAlphaRef = v }),
		("GameOptions.COASTERSMOOTHNESS", s => s.CoasterSmoothness, ( s, v ) => s with { CoasterSmoothness = v }),
		("GameOptions.COASTERTRACKDETAIL", s => s.CoasterTrackDetail, ( s, v ) => s with { CoasterTrackDetail = v }),
		("GameOptions.PARTICLEDENSITY", s => s.ParticleDensity, ( s, v ) => s with { ParticleDensity = v }),
		("GameOptions.TOTALPARTICLES", s => s.TotalParticles, ( s, v ) => s with { TotalParticles = v }),
		("GameOptions.NUMKIDS", s => s.NumKids, ( s, v ) => s with { NumKids = v }),
		("GameOptions.WEATHER", s => s.Weather, ( s, v ) => s with { Weather = v }),
		("GameOptions.LOBBYOBJECTS", s => s.LobbyObjects, ( s, v ) => s with { LobbyObjects = v }),
	};

	/// <summary>Keys whose values change the simulation (and must be recorded in save metadata when they deviate).</summary>
	public static readonly IReadOnlyList<string> SimulationKeys = new[] { "GameOptions.NUMKIDS", "GameOptions.PARTICLEDENSITY", "GameOptions.TOTALPARTICLES", "GameOptions.WEATHER" };

	/// <summary>Reads every known key from an original detail file; missing or malformed keys are reported.</summary>
	public static OriginalDetailSettings FromSam( SettingsFile file, ICollection<string> diagnostics )
	{
		var result = new OriginalDetailSettings();
		foreach ( var (key, _, set) in Fields )
		{
			var text = file[key];
			if ( text != null && int.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value ) )
				result = set( result, value );
			else
				diagnostics.Add( $"{key} is missing or not an integer." );
		}
		return result;
	}

	public int Get( string key ) => Fields.First( field => string.Equals( field.Key, key, StringComparison.OrdinalIgnoreCase ) ).Get( this );

	public OriginalDetailSettings With( string key, int value ) => Fields.First( field => string.Equals( field.Key, key, StringComparison.OrdinalIgnoreCase ) ).Set( this, value );
}

/// <summary>Sampler filtering OpenTPW can apply.</summary>
public enum TextureFilterMode
{
	Point,
	Bilinear,
	Trilinear,
	Anisotropic
}

/// <summary>What the renderer actually uses for a set of detail settings.</summary>
public sealed record RenderQuality( TextureFilterMode Filter, int MaxAnisotropy, bool Mipmaps, float ViewDistanceScale )
{
	/// <summary>The renderer's settings before the graphics options existed (anisotropic 16x, mipmaps, view distance 1).</summary>
	public static RenderQuality Legacy { get; } = new( TextureFilterMode.Anisotropic, 16, true, 1 );
}

/// <summary>
/// The four presets (original Low/Medium/High from <c>Data/low.sam</c>, <c>med.sam</c>,
/// <c>high.sam</c>; Enhanced as a documented OpenTPW extension) and the mapping of each original
/// option onto what OpenTPW renders.
/// </summary>
public static class GraphicsPresets
{
	/// <summary>The original's default detail level for a machine with <paramref name="ramMegabytes"/> of memory and a <paramref name="cpuMegahertz"/> processor.</summary>
	// [BIN:STP-PPC:0x10125B7C default options] Gestalt 'ram ' in MB (64 when unavailable) and 'pclk' in MHz (266 when unavailable): Low below 65 MB or 301 MHz, Medium below 192 MB or 450 MHz, otherwise High
	public static GraphicsPreset DefaultFor( long ramMegabytes, long cpuMegahertz ) =>
		ramMegabytes < 65 || cpuMegahertz < 301 ? GraphicsPreset.Low
		: ramMegabytes < 192 || cpuMegahertz < 450 ? GraphicsPreset.Medium
		: GraphicsPreset.High;

	/// <summary>The default for this machine: its memory as .NET reports it.</summary>
	// [APPROX:COMPAT-013] the processor clock is taken as 450 MHz or faster, since .NET cannot read it portably — evidence needed: none for any machine that runs OpenTPW (all exceed 450 MHz)
	public static GraphicsPreset DefaultForThisMachine()
	{
		var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
		return DefaultFor( bytes > 0 ? bytes / (1024 * 1024) : 64, 450 );
	}

	public static readonly IReadOnlyDictionary<GraphicsPreset, string> OriginalFiles = new Dictionary<GraphicsPreset, string>
	{
		[GraphicsPreset.Low] = "/low.sam",
		[GraphicsPreset.Medium] = "/med.sam",
		[GraphicsPreset.High] = "/high.sam",
	};

	/// <summary>[EXT:COMPAT-GFX-ENHANCED] anisotropy used by the Enhanced preset (TEXTUREFILTERING 3 is never used by the original presets).</summary>
	public const int EnhancedAnisotropy = 16;
	/// <summary>[EXT:COMPAT-GFX-ENHANCED] view distance scale of the Enhanced preset (OpenTPW fog distance multiplier).</summary>
	public const float EnhancedViewDistanceScale = 2f;
	public const float MinimumViewDistanceScale = 0.5f;
	public const float MaximumViewDistanceScale = 4f;
	public const int MaximumAnisotropy = 16;

	/// <summary>
	/// Enhanced: the original High preset plus the community-style maxima, each at the top of its
	/// documented legend range: anisotropic filtering, maximum first-person view distance, coaster
	/// smoothness 8, 4 sky layers, high sprite shadows, procedural texturing 64x64, high texture quality.
	/// The simulation-affecting GameOptions (NUMKIDS, PARTICLEDENSITY, TOTALPARTICLES) are only raised
	/// (NUMKIDS 3 = MAX, PARTICLEDENSITY 2000) when the <c>enhanced-game-options</c> compatibility fix is on.
	/// </summary>
	public static OriginalDetailSettings Enhance( OriginalDetailSettings high, bool simulationOptions )
	{
		// [EXT:COMPAT-GFX-ENHANCED] user-selected deviation from the original High preset by design.
		var result = high with
		{
			TextureQuality = 3,
			TextureFiltering = 3,
			ProceduralTexturing = 3,
			SkyQuality = 4,
			SpriteShadow = 3,
			Mipmap = 1,
			Render32 = 1,
			Texture32 = 1,
			FirstPersonViewDistance = 3,
			CoasterSmoothness = 8,
			CoasterTrackDetail = 1,
		};
		// [EXT:COMPAT-FIX enhanced-game-options] simulation-affecting; recorded in save metadata.
		return simulationOptions ? result with { NumKids = 3, ParticleDensity = 2000 } : result;
	}

	/// <summary>
	/// Renderer mapping. Filtering and mipmaps follow the legend ([DATA:*.sam:TEXTURE_FILTERING 0 Point,
	/// 1 Bilinear, 2 Trilinear, 3 Anisotropic; MIPMAP]); the anisotropy degree for value 3 and the view
	/// distance are OpenTPW extensions.
	/// </summary>
	public static RenderQuality ToRenderQuality( OriginalDetailSettings detail, int anisotropy, float viewDistanceScale )
	{
		var filter = detail.TextureFiltering switch
		{
			<= 0 => TextureFilterMode.Point,
			1 => TextureFilterMode.Bilinear,
			2 => TextureFilterMode.Trilinear,
			_ => TextureFilterMode.Anisotropic
		};
		return new RenderQuality( filter, filter == TextureFilterMode.Anisotropic ? Math.Clamp( anisotropy, 1, MaximumAnisotropy ) : 1, detail.Mipmap != 0,
			Math.Clamp( viewDistanceScale, MinimumViewDistanceScale, MaximumViewDistanceScale ) );
	}

	/// <summary>How each original option is handled by OpenTPW today (docs/COMPATIBILITY.md mirrors this).</summary>
	public static readonly IReadOnlyDictionary<string, string> Support = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
	{
		["GraphicalOptions.TEXTUREFILTERING"] = "Applied: sampler filter (point/bilinear/trilinear/anisotropic).",
		["GraphicalOptions.MIPMAP"] = "Applied: mip levels sampled or not.",
		["GraphicalOptions.FIRSTPERSONVIEWDISTANCE"] = "Stored only: OpenTPW has no first-person view yet.",
		["GraphicalOptions.FOG"] = "Not applied: OpenTPW always uses per-pixel depth fog (the original presets all select 1 = depth).",
		["GraphicalOptions.TEXTUREQUALITY"] = "Not applied: textures are always uploaded at full resolution.",
		["GraphicalOptions.PROCEDURALTEXTURING"] = "Not applied: no procedural terrain texturing yet.",
		["GraphicalOptions.PROCEDURALTEXTURECACHE"] = "Not applied: no procedural texturing yet.",
		["GraphicalOptions.ANIMATINGTEXTURES"] = "Not applied: texture animation is not implemented.",
		["GraphicalOptions.SKYQUALITY"] = "Not applied: sky layers are not implemented.",
		["GraphicalOptions.SKYDETAIL"] = "Not applied.",
		["GraphicalOptions.SKYSHADOW"] = "Not applied: no cloud shadows.",
		["GraphicalOptions.MESHSHADOW"] = "Not applied: no shadows.",
		["GraphicalOptions.SPRITESHADOW"] = "Not applied: no guest sprites/shadows yet.",
		["GraphicalOptions.TRIPLEBUFFER"] = "Not applied: presentation is managed by the swapchain.",
		["GraphicalOptions.RENDER32"] = "Not needed: OpenTPW always renders 32-bit.",
		["GraphicalOptions.TEXTURE32"] = "Not needed: textures are always 32-bit.",
		["GraphicalOptions.BUMPMAPPING"] = "Not applied.",
		["GraphicalOptions.LIGHTING"] = "Not applied: OpenTPW always lights meshes.",
		["GraphicalOptions.FORCELOWALPHAREF"] = "Not applied: meaning unknown.",
		["GameOptions.COASTERSMOOTHNESS"] = "Exposed for the rides slice (coaster track tessellation); not consumed yet.",
		["GameOptions.COASTERTRACKDETAIL"] = "Exposed for the rides slice; not consumed yet.",
		["GameOptions.PARTICLEDENSITY"] = "Exposed (simulation-affecting); no particle system yet.",
		["GameOptions.TOTALPARTICLES"] = "Exposed (simulation-affecting); no particle system yet.",
		["GameOptions.NUMKIDS"] = "Exposed for the guests slice (simulation-affecting).",
		["GameOptions.WEATHER"] = "Exposed (simulation-affecting); no weather yet.",
		["GameOptions.LOBBYOBJECTS"] = "Exposed for the frontend lobby; not consumed yet.",
	};
}

/// <summary>
/// User graphics options, stored as <c>graphics.json</c> next to <c>display.json</c> (never in saves).
/// <see cref="Detail"/> holds the original option values; for Low/Medium/High they are re-read from
/// the original files, for Custom the stored values are used.
/// </summary>
public sealed record GraphicsSettings
{
	public const string FileName = "graphics.json";

	/// <summary>The default detail level is picked for this machine (<see cref="GraphicsPresets.DefaultFor"/>).</summary>
	public GraphicsPreset Preset { get; init; } = GraphicsPresets.DefaultForThisMachine();
	/// <summary>Only used for <see cref="GraphicsPreset.Custom"/>.</summary>
	public OriginalDetailSettings? Detail { get; init; }
	/// <summary>[EXT:COMPAT-GFX-ANISOTROPY] degree used when filtering is anisotropic (1..16).</summary>
	public int Anisotropy { get; init; } = GraphicsPresets.EnhancedAnisotropy;
	/// <summary>[EXT:COMPAT-GFX-VIEWDISTANCE] OpenTPW fog distance multiplier; 1 = unchanged.</summary>
	public float ViewDistanceScale { get; init; } = 1;
	/// <summary>
	/// [EXT:texture-pack] Name of the locally built texture pack to use, a directory under <c>texture-packs</c>
	/// (<c>enhanced</c>, <c>detailed</c>, ...; docs/TEXTURE-PACKS.md); empty = original textures. Stored as <c>"TexturePack"</c>.
	/// </summary>
	[JsonPropertyName( "TexturePack" )]
	public string TexturePackName { get; init; } = "";
	/// <summary>Legacy boolean of earlier versions: <c>true</c> reads as <c>"enhanced"</c> (<see cref="Validate"/>); never written.</summary>
	[JsonPropertyName( "EnhancedTextures" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public bool? EnhancedTextures { get; init; }

	public static GraphicsSettings Default { get; } = new();

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	public GraphicsSettings Validate( ICollection<string> diagnostics )
	{
		var result = this;
		if ( result.TexturePackName == null )
			result = result with { TexturePackName = "" };
		if ( result.EnhancedTextures != null )
		{
			// Migration: the old boolean selected the pack that was then the only one.
			result = result with { TexturePackName = result.TexturePackName.Length == 0 && result.EnhancedTextures == true ? TexturePack.DefaultName : result.TexturePackName, EnhancedTextures = null };
		}
		if ( result.TexturePackName.Length > 0 && !TexturePack.IsValidName( result.TexturePackName ) )
		{
			diagnostics.Add( $"Texture pack name '{result.TexturePackName}' is not a plain directory name; using the original textures." );
			result = result with { TexturePackName = "" };
		}
		if ( !Enum.IsDefined( Preset ) )
		{
			diagnostics.Add( $"Unknown graphics preset {(int)Preset}; using {Default.Preset}." );
			result = result with { Preset = Default.Preset };
		}
		if ( result.Preset == GraphicsPreset.Custom && result.Detail == null )
		{
			diagnostics.Add( "Custom graphics preset has no values; using High." );
			result = result with { Preset = GraphicsPreset.High };
		}
		if ( result.Anisotropy is < 1 or > GraphicsPresets.MaximumAnisotropy )
		{
			diagnostics.Add( $"Anisotropy {result.Anisotropy} is outside 1..{GraphicsPresets.MaximumAnisotropy}; using {GraphicsPresets.EnhancedAnisotropy}." );
			result = result with { Anisotropy = GraphicsPresets.EnhancedAnisotropy };
		}
		if ( !float.IsFinite( result.ViewDistanceScale ) || result.ViewDistanceScale < GraphicsPresets.MinimumViewDistanceScale || result.ViewDistanceScale > GraphicsPresets.MaximumViewDistanceScale )
		{
			diagnostics.Add( $"View distance scale {result.ViewDistanceScale} is outside {GraphicsPresets.MinimumViewDistanceScale}..{GraphicsPresets.MaximumViewDistanceScale}; using 1." );
			result = result with { ViewDistanceScale = 1 };
		}
		return result;
	}

	public string ToJson() => JsonSerializer.Serialize( this, JsonOptions ).Replace( "\r\n", "\n" );

	public static GraphicsSettings FromJson( string json, ICollection<string> diagnostics )
	{
		try
		{
			var settings = JsonSerializer.Deserialize<GraphicsSettings>( json, JsonOptions );
			if ( settings != null )
				return settings.Validate( diagnostics );
			diagnostics.Add( "Graphics settings file is empty; using defaults." );
		}
		catch ( JsonException exception )
		{
			diagnostics.Add( $"Graphics settings file is invalid ({exception.Message}); using defaults." );
		}
		return Default;
	}

	public static string GetDefaultPath() => Path.Combine( Path.GetDirectoryName( DisplaySettings.GetDefaultPath() )!, FileName );

	public static GraphicsSettings Load( string path, ICollection<string> diagnostics )
	{
		if ( !File.Exists( path ) )
			return Default;
		try
		{
			return FromJson( File.ReadAllText( path ), diagnostics );
		}
		catch ( IOException exception )
		{
			diagnostics.Add( $"Graphics settings could not be read ({exception.Message}); using defaults." );
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
}
