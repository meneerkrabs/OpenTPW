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

	/// <summary><c>&lt;config&gt;/texture-packs/enhanced</c>, next to <c>display.json</c> and <c>graphics.json</c>.</summary>
	public static string DefaultPackDirectory() =>
		Path.Combine( Path.GetDirectoryName( DisplaySettings.GetDefaultPath() )!, "texture-packs", DefaultName );

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

	/// <summary>True when the default pack exists and is usable.</summary>
	public static bool IsInstalled( string? packDirectory = null ) => Open( packDirectory ?? DefaultPackDirectory(), new List<string>() ) != null;

	/// <summary>
	/// Startup: activates the default pack when <paramref name="enabled"/> and the pack is usable;
	/// an <c>OPENTPW_TEXTURE_PACK</c> override is left untouched.
	/// </summary>
	public static void Activate( bool enabled, ICollection<string> diagnostics, string? packDirectory = null )
	{
		if ( !string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "OPENTPW_TEXTURE_PACK" ) ) )
			return;
		Directory = null;
		if ( !enabled )
			return;
		packDirectory ??= DefaultPackDirectory();
		var manifest = Open( packDirectory, diagnostics );
		if ( manifest == null )
			return;
		Directory = Path.Combine( packDirectory, TexturesDirectoryName );
		Log?.Trace( $"Texture pack: {manifest.Textures} textures at {manifest.Scale}x ({manifest.Upscaler}, model {manifest.Model})." );
	}
}

/// <summary>Contents of <c>pack.json</c>, written last when a pack is built.</summary>
public sealed record TexturePackManifest
{
	public int Format { get; init; } = TexturePack.FormatVersion;
	public int Scale { get; init; }
	public string Upscaler { get; init; } = "";
	public string Model { get; init; } = "";
	public int Textures { get; init; }
	public int SkippedSmall { get; init; }
	public int SkippedLowDetail { get; init; }
	public int SkippedInterface { get; init; }
	public int SkippedChromaKey { get; init; }
	public int SkippedUnreadable { get; init; }
	public int MinimumSize { get; init; }
	public string Subtree { get; init; } = "";
	public DateTime CreatedUtc { get; init; }
}
