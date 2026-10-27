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
			var image = ImageResult.FromMemory( File.ReadAllBytes( replacement ), ColorComponents.RedGreenBlueAlpha );
			(width, height, data) = (image.Width, image.Height, image.Data);
		}
		else if ( path.EndsWith( ".wct", StringComparison.OrdinalIgnoreCase ) )
		{
			var file = new TextureFile( path );
			var texture = file.Data;
			(width, height, data) = (texture.Width, texture.Height, (byte[])texture.Data.Clone());
			// [BIN:TPW-EXE:0x590C40 state word] a texture with an alpha plane and a zero alpha byte (ipan, smallguage) is state 0x2756 whatever slot or model draws it: alpha blend with an alpha test at 0xF0/255
			if ( file.AlphaType == TextureAlphaType.Cutout )
				CutOut( data, width, height );
		}
		else
			// png, tga and (Theme Park Inc) fsh from the game data
			(data, width, height) = Texture.DecodeImage( path );
		if ( data.Length != width * height * 4 )
			throw new InvalidDataException( $"UI image {path} has an unexpected size." );
		KeyOutPink( data, width, height );
		return (width, height, data);
	}

	/// <summary>
	/// Texture slot flag 0x2. OpenTPW ignores it: state bit 0x800000 of the state builder (0x581790) is bit 0x2 of the material word +0xC, which no shipped material sets, and its only reader is the sort-key override at 0x58109E (UI-034). Of the 185 ui.wad slots
	/// that set it, all 185 textures carry their own alpha plane; <c>ipan</c> (the lobby panel's <c>f_lobbutbg</c>, and <c>w_dialog</c> and <c>f_train</c> without the flag) and <c>smallguage</c> are cut-outs.
	/// </summary>
	// [BIN:TPW-EXE:0x581db0 UI part state] a UI node's parts go through the scene batcher (the walker 0x576910 marks them with 0x8bb624 = 0x1000000, 0x581cd0 appends them, 0x581db0 -> 0x581790 -> 0x590c40), so every UI texture draws with the state word of its own alpha type and a cut-out gets the 0xF0/255 test; state bit 0x800000 (material word bit 0x2, never set) only re-keys the part's depth sort (0x58109e)
	public const uint TransparentFlag = 0x2;

	/// <summary>The alpha test of a cut-out texture (state 0x2756, reference 0xF0/255): texels below it are not drawn, the others keep their alpha; discarded texels take a neighbour's colour so linear filtering does not bleed them in.</summary>
	public static void CutOut( byte[] rgba, int width, int height )
	{
		var below = false;
		for ( var pixel = 3; pixel < rgba.Length && !below; pixel += 4 )
			below = rgba[pixel] < CutOutReference;
		if ( !below )
			return;
		KeyOutAlpha( rgba, width, height, CutOutReference );
	}

	/// <summary>The alpha reference of a cut-out texture, 0xF0 (the table at 0x7012D8, index 2).</summary>
	public const byte CutOutReference = 0xF0;

	// [EXT:ui-linear-filtering] the pink key plus neighbour colour bleed make UI images safe for linear filtering at any output size; the original drew at its few fixed modes, so no original behaviour exists at OpenTPW's resolutions
	public static void KeyOutPink( byte[] rgba, int width, int height ) =>
		KeyOut( rgba, width, height, ( r, g, b ) => r == 255 && g == 0 && b == 255 );

	/// <summary>Makes texels matching <paramref name="key"/> transparent and gives them an opaque neighbour's colour.</summary>
	public static void KeyOut( byte[] rgba, int width, int height, Func<byte, byte, byte, bool> key ) =>
		KeyOut( rgba, width, height, ( r, g, b, _ ) => key( r, g, b ) );

	/// <summary>Makes texels with an alpha below <paramref name="reference"/> transparent and gives them an opaque neighbour's colour.</summary>
	public static void KeyOutAlpha( byte[] rgba, int width, int height, byte reference ) =>
		KeyOut( rgba, width, height, ( _, _, _, a ) => a < reference );

	private static void KeyOut( byte[] rgba, int width, int height, Func<byte, byte, byte, byte, bool> key )
	{
		var keyed = new bool[width * height];
		var anyKeyed = false;
		for ( var index = 0; index < keyed.Length; index++ )
		{
			var pixel = index * 4;
			if ( key( rgba[pixel], rgba[pixel + 1], rgba[pixel + 2], rgba[pixel + 3] ) )
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

	/// <summary>Forgets the cached directory listings (tests that switch the data folder).</summary>
	internal static void ResetListings() => listings.Clear();

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
