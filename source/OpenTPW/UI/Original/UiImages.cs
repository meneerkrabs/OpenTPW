using StbImageSharp;

namespace OpenTPW.UI.Original;

/// <summary>
/// CPU loading of original UI images. Pure pink (255, 0, 255) is the transparent key of the UI
/// textures (it fills every unused texel of ui.wad button/panel art); keyed texels become alpha 0
/// and take the colour of an opaque neighbour so linear filtering does not bleed pink into edges.
/// </summary>
public static class UiImages
{
	private static readonly Dictionary<string, Dictionary<string, string>> listings = new( StringComparer.OrdinalIgnoreCase );

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
		// [EXT:texture-pack] the optional local pack replaces interface art too (docs/TEXTURE-PACKS.md); UVs are relative, so a larger image drops in.
		if ( path.EndsWith( ".wct", StringComparison.OrdinalIgnoreCase ) && TexturePack.Find( path ) is { } replacement )
		{
			// The browser's shipped art arrives predecoded (docs/WEB.md); the pink key below applies to it exactly as to a PNG.
			if ( replacement.EndsWith( PredecodedUiArt.Extension, StringComparison.OrdinalIgnoreCase ) )
				(width, height, data) = PredecodedUiArt.Load( File.ReadAllBytes( replacement ) );
			else
			{
				var image = ImageResult.FromMemory( File.ReadAllBytes( replacement ), ColorComponents.RedGreenBlueAlpha );
				(width, height, data) = (image.Width, image.Height, image.Data);
			}
		}
		else if ( path.EndsWith( ".wct", StringComparison.OrdinalIgnoreCase ) )
		{
			var texture = new TextureFile( path ).Data;
			(width, height, data) = (texture.Width, texture.Height, (byte[])texture.Data.Clone());
		}
		else
			// png, tga and (Theme Park Inc) fsh from the game data
			(data, width, height) = Texture.DecodeImage( path );
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
		// [APPROX:UI-034] a fully opaque texture on a transparent (flag 0x2) slot keys out black with a soft edge — evidence needed: the original's render state for flagged slots
		if ( opaque )
			KeyOutBlackSoft( data, width, height );
		return (width, height, data);
	}

	/// <summary>Channel value up to which a black-keyed texel is fully transparent.</summary>
	public const int BlackKeyClear = 6;
	/// <summary>Channel value from which a black-keyed texel is fully opaque.</summary>
	public const int BlackKeySolid = 48;

	/// <summary>
	/// Black key for art drawn anti-aliased against black (the lobby panel's <c>ipan</c> discs fade into black
	/// over a few texels). A plain key leaves that dark fade as a hard, opaque rim, which shows as steps when
	/// the texture is magnified. Here texels whose brightest channel is at most <see cref="BlackKeyClear"/>
	/// become transparent, texels from <see cref="BlackKeySolid"/> stay opaque, and the texels between get a
	/// proportional alpha with their colour divided by it (un-premultiplied against black).
	/// </summary>
	public static void KeyOutBlackSoft( byte[] rgba, int width, int height )
	{
		for ( var pixel = 0; pixel < rgba.Length; pixel += 4 )
		{
			var brightest = Math.Max( rgba[pixel], Math.Max( rgba[pixel + 1], rgba[pixel + 2] ) );
			if ( brightest <= BlackKeyClear || brightest >= BlackKeySolid )
				continue;
			var alpha = (brightest - BlackKeyClear) / (float)(BlackKeySolid - BlackKeyClear);
			for ( var channel = 0; channel < 3; channel++ )
				rgba[pixel + channel] = (byte)Math.Min( 255, MathF.Round( rgba[pixel + channel] / alpha ) );
			rgba[pixel + 3] = (byte)MathF.Round( alpha * 255 );
		}
		// The fully transparent texels take a neighbour's colour for linear filtering.
		KeyOut( rgba, width, height, ( r, g, b ) => r <= BlackKeyClear && g <= BlackKeyClear && b <= BlackKeyClear );
	}

	// [EXT:ui-linear-filtering] the pink key plus neighbour colour bleed make UI images safe for linear filtering at any output size; the original drew at its few fixed modes, so no original behaviour exists at OpenTPW's resolutions
	public static void KeyOutPink( byte[] rgba, int width, int height ) =>
		KeyOut( rgba, width, height, ( r, g, b ) => r == 255 && g == 0 && b == 255 );

	/// <summary>Makes texels matching <paramref name="key"/> transparent and gives them an opaque neighbour's colour.</summary>
	public static void KeyOut( byte[] rgba, int width, int height, Func<byte, byte, byte, bool> key )
	{
		var keyed = new bool[width * height];
		var anyKeyed = false;
		for ( var index = 0; index < keyed.Length; index++ )
		{
			var pixel = index * 4;
			if ( key( rgba[pixel], rgba[pixel + 1], rgba[pixel + 2] ) )
			{
				keyed[index] = true;
				anyKeyed = true;
				rgba[pixel + 3] = 0;
			}
		}
		// Shipped interface art carries no keyed texels at all; the neighbour pass would visit every texel only to skip it.
		if ( !anyKeyed )
			return;
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
		if ( !listings.TryGetValue( directory, out var pathsByStem ) )
		{
			string[] files;
			try { files = FileSystem.GetFiles( directory ); }
			catch ( Exception exception ) when ( exception is DirectoryNotFoundException or FileNotFoundException or InvalidOperationException ) { files = Array.Empty<string>(); }
			pathsByStem = new Dictionary<string, string>( files.Length, StringComparer.OrdinalIgnoreCase );
			foreach ( var file in files )
				pathsByStem.TryAdd( Path.GetFileNameWithoutExtension( file ), file );
			listings[directory] = pathsByStem;
		}
		var stem = Path.GetFileNameWithoutExtension( name.AsSpan( 0, name.Length ).TrimEnd( '\0' ) );
		return pathsByStem.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue( stem, out var path ) ? path : null;
	}
}
