using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>
/// Texture packs: PNG replacements for original <c>.wct</c> textures, keyed by the
/// lower-case game path plus <c>.png</c> (e.g. <c>textures/levels/jungle/terrain/textures/jgr_bas1.wct.png</c>).
/// Packs can be installed in the player's config directory or bundled under <c>content/texture-packs</c>.
/// A local pack takes precedence over a bundled pack of the same name. <c>OPENTPW_TEXTURE_PACK</c>
/// (a pack's <c>textures</c> directory) overrides the setting.
/// </summary>
// [EXT:texture-pack] Upscaled replacement textures are an OpenTPW presentation option, not original behaviour.
public static class TexturePack
{
	/// <summary>The retired Clarity pack (removed October 17, 2026): a saved choice of it migrates to Original (<see cref="GraphicsSettings.Validate"/>).</summary>
	public const string RetiredClarityName = "clarity-enhanced";
	public const string EnhancedName = "enhanced";
	public const string ManifestFileName = "pack.json";
	public const string TexturesDirectoryName = "textures";
	public const int FormatVersion = 1;

	/// <summary>The active pack's <c>textures</c> directory, or null when original textures are used.</summary>
	public static string? Directory { get; set; } = Environment.GetEnvironmentVariable( "OPENTPW_TEXTURE_PACK" );

	/// <summary>Number of textures replaced so far.</summary>
	public static int Replaced { get; private set; }

	/// <summary>The pack names the options screen labels itself, in cycle order.</summary>
	public static readonly string[] KnownNames = { EnhancedName, "detailed" };

	/// <summary><c>&lt;config&gt;/texture-packs</c>, next to <c>display.json</c> and <c>graphics.json</c>; one directory per pack.</summary>
	public static string PacksDirectory() =>
		Path.Combine( Path.GetDirectoryName( DisplaySettings.GetDefaultPath() )!, "texture-packs" );

	/// <summary>Portable bundled packs next to the executable, also usable from the browser's virtual file system.</summary>
	public static string BundledPacksDirectory() => Path.Combine( AppContext.BaseDirectory, "content", "texture-packs" );

	/// <summary><c>&lt;config&gt;/texture-packs/&lt;name&gt;</c> (or under <paramref name="packsDirectory"/>).</summary>
	public static string PackDirectory( string name, string? packsDirectory = null ) =>
		Path.Combine( packsDirectory ?? PacksDirectory(), name );

	/// <summary>A pack name is one plain directory name: letters, digits, '-', '_' or '.', not starting with '.' (build staging directories do).</summary>
	public static bool IsValidName( string? name ) =>
		!string.IsNullOrEmpty( name ) && name[0] != '.' && name.All( character => char.IsAsciiLetterOrDigit( character ) || character is '-' or '_' or '.' );

	/// <summary>Usable local and bundled packs: the known ones first, then the rest alphabetically, without duplicate names.</summary>
	public static IReadOnlyList<string> InstalledPacks( string? packsDirectory = null, string? bundledPacksDirectory = null ) =>
		UsablePacks( packsDirectory ?? PacksDirectory() )
			.Concat( UsablePacks( bundledPacksDirectory ?? BundledPacksDirectory() ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.OrderBy( name => Array.IndexOf( KnownNames, name ) is var index and >= 0 ? index : KnownNames.Length )
			.ThenBy( name => name, StringComparer.OrdinalIgnoreCase )
			.ToArray();

	private static IReadOnlyList<string> UsablePacks( string packsDirectory )
	{
		if ( !System.IO.Directory.Exists( packsDirectory ) )
			return Array.Empty<string>();
		try
		{
			return System.IO.Directory.EnumerateDirectories( packsDirectory )
				.Select( directory => Path.GetFileName( directory ) )
				.Where( name => IsValidName( name ) && Open( PackDirectory( name, packsDirectory ), new List<string>() ) != null )
				.ToArray();
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			return Array.Empty<string>();
		}
	}

	/// <summary>True when an enhanced world choice (any non-empty valid pack name) is active.</summary>
	public static bool Enhanced { get; private set; }

	/// <summary>The replacement PNG for a game texture path in the active pack, or null when there is none.</summary>
	public static string? Find( string gamePath )
	{
		if ( string.IsNullOrEmpty( Directory ) )
			return null;
		var packed = Path.Combine( Directory, RelativeFileName( gamePath ) );
		return File.Exists( packed ) ? ReplacedFile( packed ) : null;
	}

	private static string ReplacedFile( string file )
	{
		if ( Replaced++ == 0 )
			Log?.Trace( $"Texture pack: replacing original textures from {Directory}." );
		return file;
	}

	/// <summary>Pack-relative file name for a game path: lower case, forward slashes, no leading slash, plus <c>.png</c>.</summary>
	public static string RelativeFileName( string gamePath ) =>
		gamePath.Replace( '\\', '/' ).TrimStart( '/' ).ToLowerInvariant() + ".png";

	/// <summary>Reads and checks a pack's manifest; null (with a reason) when the pack is missing or unusable.</summary>
	public static TexturePackManifest? Open( string packDirectory, ICollection<string> diagnostics )
	{
		var manifestPath = Path.Combine( packDirectory, ManifestFileName );
		if ( !File.Exists( manifestPath ) )
		{
			diagnostics.Add( $"No texture pack at {packDirectory}; build one with --build-texture-pack (docs/TEXTURE-PACKS.md)." );
			return null;
		}
		try
		{
			var manifest = JsonSerializer.Deserialize<TexturePackManifest>( File.ReadAllText( manifestPath ) );
			if ( manifest == null || manifest.Format != FormatVersion )
			{
				diagnostics.Add( $"Texture pack {packDirectory} has format {manifest?.Format.ToString() ?? "?"}, expected {FormatVersion}; rebuild it." );
				return null;
			}
			if ( !System.IO.Directory.Exists( Path.Combine( packDirectory, TexturesDirectoryName ) ) )
			{
				diagnostics.Add( $"Texture pack {packDirectory} has no {TexturesDirectoryName} directory; rebuild it." );
				return null;
			}
			return manifest;
		}
		catch ( Exception exception ) when ( exception is IOException or JsonException or UnauthorizedAccessException )
		{
			diagnostics.Add( $"Texture pack {packDirectory} could not be read ({exception.Message})." );
			return null;
		}
	}

	private static (string Directory, TexturePackManifest Manifest)? Resolve( string name, ICollection<string> diagnostics, string packsDirectory, string bundledPacksDirectory )
	{
		foreach ( var root in new[] { packsDirectory, bundledPacksDirectory }.Distinct( StringComparer.Ordinal ) )
		{
			var directory = PackDirectory( name, root );
			if ( System.IO.Directory.Exists( directory ) && Open( directory, diagnostics ) is { } manifest )
				return (directory, manifest);
		}
		return null;
	}

	/// <summary>
	/// Selects a local pack, then the bundled pack of the same name. An empty name means original world textures;
	/// A missing pack keeps original world textures. An <c>OPENTPW_TEXTURE_PACK</c>
	/// override pins its textures directory. Textures already loaded keep their pixels until
	/// <see cref="TexturePackSwitch"/> reloads them.
	/// </summary>
	public static void Activate( string? name, ICollection<string> diagnostics, string? packsDirectory = null, string? bundledPacksDirectory = null )
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_TEXTURE_PACK" ) is { Length: > 0 } environmentOverride )
		{
			Directory = environmentOverride;
			Enhanced = true;
			return;
		}
		Directory = null;
		Enhanced = false;
		if ( string.IsNullOrEmpty( name ) )
			return;
		if ( !IsValidName( name ) )
		{
			diagnostics.Add( $"Texture pack name '{name}' is not a plain directory name; using the original textures." );
			return;
		}
		Enhanced = true;
		var resolved = Resolve( name, diagnostics, packsDirectory ?? PacksDirectory(), bundledPacksDirectory ?? BundledPacksDirectory() );
		if ( resolved == null )
		{
			const string fallback = "original textures";
			if ( name != EnhancedName )
				diagnostics.Add( $"No usable texture pack '{name}'; using {fallback}. Install a pack or build one with --build-texture-pack." );
			Log?.Trace( $"Texture pack '{name}': {fallback}; no usable local or bundled pack." );
			return;
		}
		var (packDirectory, manifest) = resolved.Value;
		Directory = Path.Combine( packDirectory, TexturesDirectoryName );
		var resolution = manifest.MixedScale ? "mixed scales" : $"{manifest.Scale}x";
		Log?.Trace( $"Texture pack '{name}': {manifest.Textures} textures at {resolution} ({manifest.Upscaler}, model {manifest.Model}{(!string.IsNullOrEmpty( manifest.PrepassModel ) ? $", pre-pass {manifest.PrepassModel}" : "")})." );
	}
}

/// <summary>Contents of <c>pack.json</c>, written last when a pack is built.</summary>
public sealed record TexturePackManifest
{
	public int Format { get; init; } = TexturePack.FormatVersion;
	public int Scale { get; init; }
	/// <summary>True for curated packs whose replacements use more than one upscale ratio; dimensions come from each PNG.</summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public bool MixedScale { get; init; }
	public string Upscaler { get; init; } = "";
	public string Model { get; init; } = "";
	/// <summary>Model used for interface art; empty when interface art stayed original.</summary>
	public string InterfaceModel { get; init; } = "";
	/// <summary>ONNX de-artifact model run on world textures before upscaling; empty when there was no pre-pass.</summary>
	public string PrepassModel { get; init; } = "";
	public int Textures { get; init; }
	/// <summary>Interface textures built in this run (part of <see cref="Textures"/>).</summary>
	public int InterfaceTextures { get; init; }
	public int SkippedSmall { get; init; }
	public int SkippedLowDetail { get; init; }
	public int SkippedInterface { get; init; }
	public int SkippedChromaKey { get; init; }
	public int SkippedUnreadable { get; init; }
	public int MinimumSize { get; init; }
	public string Subtree { get; init; } = "";
	public DateTime CreatedUtc { get; init; }
}
