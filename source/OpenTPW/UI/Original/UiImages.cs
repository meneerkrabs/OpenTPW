using StbImageSharp;

namespace OpenTPW.UI.Original;

/// <summary>
/// CPU loading of original UI images. Pure pink (255, 0, 255) is the transparent key of the UI
/// textures (it fills every unused texel of ui.wad button/panel art); keyed texels become alpha 0
/// and take the colour of an opaque neighbour so linear filtering does not bleed pink into edges.
/// </summary>
public static class UiImages
{
	private static readonly Dictionary<string, string[]> listings = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Prefix of an image path on the host file system (local art overrides) rather than in the game data.</summary>
	public const string HostPrefix = "file:";

	public static (int Width, int Height, byte[] Rgba) Load( string path )
	{
		int width, height;
		byte[] data;
		if ( path.StartsWith( HostPrefix, StringComparison.Ordinal ) )
		{
			// Local art keeps its own alpha; the pink key is a convention of the original textures only.
			var image = ImageResult.FromMemory( File.ReadAllBytes( path[HostPrefix.Length..] ), ColorComponents.RedGreenBlueAlpha );
			if ( image.Data.Length != image.Width * image.Height * 4 )
				throw new InvalidDataException( $"UI image {path} has an unexpected size." );
			return (image.Width, image.Height, image.Data);
		}
		if ( path.EndsWith( ".wct", StringComparison.OrdinalIgnoreCase ) )
		{
			var texture = new TextureFile( path ).Data;
			(width, height, data) = (texture.Width, texture.Height, (byte[])texture.Data.Clone());
		}
		else
		{
			var image = ImageResult.FromMemory( FileSystem.ReadAllBytes( path ), ColorComponents.RedGreenBlueAlpha );
			(width, height, data) = (image.Width, image.Height, image.Data);
		}
		if ( data.Length != width * height * 4 )
			throw new InvalidDataException( $"UI image {path} has an unexpected size." );
		KeyOutPink( data, width, height );
		return (width, height, data);
	}

	/// <summary>Suffix of an image path whose texture slot has flag 0x2 (see <see cref="TransparentFlag"/>).</summary>
	public const string BlackKeySuffix = "#black-key";

	/// <summary>
	/// Texture slot flag 0x2. Of the 185 ui.wad slots that set it, 184 textures carry their own alpha; the only
	/// opaque one, <c>ipan</c> in <c>f_lobbutbg</c>, has a black background that the lobby panel shows through.
	/// The same texture without the flag (w_dialog, f_train) is drawn opaque.
	/// </summary>
	public const uint TransparentFlag = 0x2;

	/// <summary>Loads <paramref name="path"/>, which may carry <see cref="BlackKeySuffix"/>.</summary>
	public static (int Width, int Height, byte[] Rgba) LoadKeyed( string path )
	{
		if ( !path.EndsWith( BlackKeySuffix, StringComparison.Ordinal ) )
			return Load( path );
		var (width, height, data) = Load( path[..^BlackKeySuffix.Length] );
		var opaque = true;
		for ( var pixel = 3; pixel < data.Length && opaque; pixel += 4 )
			opaque = data[pixel] == 255;
		// [APPROX:UI-034] a fully opaque texture on a transparent (flag 0x2) slot keys out black — evidence needed: the original's render state for flagged slots
		if ( opaque )
			KeyOut( data, width, height, ( r, g, b ) => r <= 6 && g <= 6 && b <= 6 );
		return (width, height, data);
	}

	// [APPROX:UI-005] pink key + neighbour colour bleed for linear filtering — evidence needed: capture of UI edges at other resolutions
	public static void KeyOutPink( byte[] rgba, int width, int height ) =>
		KeyOut( rgba, width, height, ( r, g, b ) => r == 255 && g == 0 && b == 255 );

	/// <summary>Makes texels matching <paramref name="key"/> transparent and gives them an opaque neighbour's colour.</summary>
	public static void KeyOut( byte[] rgba, int width, int height, Func<byte, byte, byte, bool> key )
	{
		var keyed = new bool[width * height];
		for ( var index = 0; index < keyed.Length; index++ )
		{
			var pixel = index * 4;
			if ( key( rgba[pixel], rgba[pixel + 1], rgba[pixel + 2] ) )
			{
				keyed[index] = true;
				rgba[pixel + 3] = 0;
			}
		}
		for ( var y = 0; y < height; y++ )
		{
			for ( var x = 0; x < width; x++ )
			{
				if ( !keyed[y * width + x] )
					continue;
				int r = 0, g = 0, b = 0, count = 0;
				for ( var dy = -1; dy <= 1; dy++ )
				{
					for ( var dx = -1; dx <= 1; dx++ )
					{
						var nx = x + dx;
						var ny = y + dy;
						if ( nx < 0 || ny < 0 || nx >= width || ny >= height || keyed[ny * width + nx] )
							continue;
						var pixel = (ny * width + nx) * 4;
						r += rgba[pixel];
						g += rgba[pixel + 1];
						b += rgba[pixel + 2];
						count++;
					}
				}
				var target = (y * width + x) * 4;
				rgba[target] = (byte)(count == 0 ? 0 : r / count);
				rgba[target + 1] = (byte)(count == 0 ? 0 : g / count);
				rgba[target + 2] = (byte)(count == 0 ? 0 : b / count);
			}
		}
	}

	/// <summary>
	/// Resolves a model material name (e.g. <c>b_okay</c>) to a file in <paramref name="directory"/>
	/// (default <c>/ui/textures</c>), case-insensitively; null when the original has no such texture.
	/// </summary>
	public static string? Resolve( string name, string directory = "/ui/textures" )
	{
		if ( !listings.TryGetValue( directory, out var files ) )
		{
			try { files = FileSystem.GetFiles( directory ); }
			catch ( Exception exception ) when ( exception is DirectoryNotFoundException or FileNotFoundException or InvalidOperationException ) { files = Array.Empty<string>(); }
			listings[directory] = files;
		}
		var stem = Path.GetFileNameWithoutExtension( name.TrimEnd( '\0' ) );
		return files.FirstOrDefault( file => string.Equals( Path.GetFileNameWithoutExtension( file ), stem, StringComparison.OrdinalIgnoreCase ) );
	}
}
