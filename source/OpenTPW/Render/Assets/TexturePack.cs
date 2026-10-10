using System.Text.Json;

namespace OpenTPW;

/// <summary>
/// Optional local texture pack: PNG replacements for original <c>.wct</c> textures, keyed by the
/// lower-case game path plus <c>.png</c> (e.g. <c>textures/levels/jungle/terrain/textures/jgr_bas1.wct.png</c>).
/// A pack is generated on the player's machine from their own installation
/// (<see cref="TexturePackBuilder"/>) and is never shipped. With no active pack every texture is the
/// original; <c>OPENTPW_TEXTURE_PACK</c> (a pack's <c>textures</c> directory) overrides the setting
/// for tests.
/// </summary>
// [EXT:texture-pack] Upscaled replacement textures are an OpenTPW presentation option, not original behaviour.
public static class TexturePack
{
	public const string DefaultName = "enhanced";
	public const string ManifestFileName = "pack.json";
	public const string TexturesDirectoryName = "textures";
	public const int FormatVersion = 1;

	/// <summary>The active pack's <c>textures</c> directory, or null when original textures are used.</summary>
	public static string? Directory { get; set; } = Environment.GetEnvironmentVariable( "OPENTPW_TEXTURE_PACK" );

	/// <summary>Number of textures replaced so far.</summary>
	public static int Replaced { get; private set; }

	/// <summary>The pack names the options screen labels itself, in cycle order: <c>enhanced</c> (clean) and <c>detailed</c>.</summary>
	public static readonly string[] KnownNames = { DefaultName, "detailed" };

	/// <summary><c>&lt;config&gt;/texture-packs</c>, next to <c>display.json</c> and <c>graphics.json</c>; one directory per pack.</summary>
	public static string PacksDirectory() =>
		Path.Combine( Path.GetDirectoryName( DisplaySettings.GetDefaultPath() )!, "texture-packs" );

	/// <summary><c>&lt;config&gt;/texture-packs/enhanced</c>.</summary>
	public static string DefaultPackDirectory() => PackDirectory( DefaultName );

	/// <summary><c>&lt;config&gt;/texture-packs/&lt;name&gt;</c> (or under <paramref name="packsDirectory"/>).</summary>
	public static string PackDirectory( string name, string? packsDirectory = null ) =>
		Path.Combine( packsDirectory ?? PacksDirectory(), name );

	/// <summary>A pack name is one plain directory name: letters, digits, '-', '_' or '.', not starting with '.' (build staging directories do).</summary>
	public static bool IsValidName( string? name ) =>
		!string.IsNullOrEmpty( name ) && name[0] != '.' && name.All( character => char.IsAsciiLetterOrDigit( character ) || character is '-' or '_' or '.' );

	/// <summary>Names of the usable packs under <paramref name="packsDirectory"/>: the known ones first, then the rest alphabetically.</summary>
	public static IReadOnlyList<string> InstalledPacks( string? packsDirectory = null )
	{
		packsDirectory ??= PacksDirectory();
		if ( !System.IO.Directory.Exists( packsDirectory ) )
			return Array.Empty<string>();
		try
		{
			return System.IO.Directory.EnumerateDirectories( packsDirectory )
				.Select( directory => Path.GetFileName( directory ) )
				.Where( name => IsValidName( name ) && Open( PackDirectory( name, packsDirectory ), new List<string>() ) != null )
				.OrderBy( name => Array.IndexOf( KnownNames, name ) is var index and >= 0 ? index : KnownNames.Length )
				.ThenBy( name => name, StringComparer.OrdinalIgnoreCase )
				.ToArray();
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			return Array.Empty<string>();
		}
	}

	/// <summary>The replacement PNG for a game texture path, or null when there is none.</summary>
	public static string? Find( string gamePath )
	{
		if ( string.IsNullOrEmpty( Directory ) )
			return null;
		var file = Path.Combine( Directory, RelativeFileName( gamePath ) );
		if ( !File.Exists( file ) )
			return null;
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

	/// <summary>True when the pack at <paramref name="packDirectory"/> (default: the <c>enhanced</c> pack) exists and is usable.</summary>
	public static bool IsInstalled( string? packDirectory = null ) => Open( packDirectory ?? DefaultPackDirectory(), new List<string>() ) != null;

	/// <summary>
	/// Selects the pack named <paramref name="name"/> under <paramref name="packsDirectory"/> (default: the config directory);
	/// an empty name means original textures. A missing or unusable pack leaves the originals active and adds a diagnostic.
	/// An <c>OPENTPW_TEXTURE_PACK</c> override is left untouched. Textures already loaded keep their pixels until
	/// <see cref="TexturePackSwitch"/> reloads them.
	/// </summary>
	public static void Activate( string? name, ICollection<string> diagnostics, string? packsDirectory = null )
	{
		if ( !string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "OPENTPW_TEXTURE_PACK" ) ) )
			return;
		Directory = null;
		if ( string.IsNullOrEmpty( name ) )
			return;
		if ( !IsValidName( name ) )
		{
			diagnostics.Add( $"Texture pack name '{name}' is not a plain directory name; using the original textures." );
			return;
		}
		var packDirectory = PackDirectory( name, packsDirectory );
		var manifest = Open( packDirectory, diagnostics );
		if ( manifest == null )
			return;
		Directory = Path.Combine( packDirectory, TexturesDirectoryName );
		Log?.Trace( $"Texture pack '{name}': {manifest.Textures} textures at {manifest.Scale}x ({manifest.Upscaler}, model {manifest.Model}{(manifest.PrepassModel.Length > 0 ? $", pre-pass {manifest.PrepassModel}" : "")})." );
	}
}

/// <summary>Contents of <c>pack.json</c>, written last when a pack is built.</summary>
public sealed record TexturePackManifest
{
	public int Format { get; init; } = TexturePack.FormatVersion;
	public int Scale { get; init; }
	public string Upscaler { get; init; } = "";
	public string Model { get; init; } = "";
	/// <summary>Model used for interface art; empty when interface art stayed original.</summary>
	public string InterfaceModel { get; init; } = "";
	/// <summary>ONNX de-artifact model run on world textures before upscaling; empty when there was no pre-pass.</summary>
	public string PrepassModel { get; init; } = "";
	/// <summary>Textures copied from the hero art directory over the automatic upscale in this run (part of <see cref="Textures"/>).</summary>
	public int HeroTextures { get; init; }
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
