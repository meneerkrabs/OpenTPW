using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Hud;

/// <summary>
/// Composes the map window's surface on the CPU, as the original writes its cells, outlines, areas and track lines into the surface it then shows (<c>0x5F2380</c>, <c>0x5F2FD0</c>): the theme's map image
/// (or the sandbox's grid) is the base, the rectangles of <see cref="ParkMapCanvas.Prims"/> are blended over it in the order the render drew them, and the pixels of the track layer's line routine go through
/// the original's 32 bit blend (<see cref="ParkMapLine.Blend"/>) coverage 0 included.
/// </summary>
internal static class ParkMapBaker
{
	/// <summary>The masks of the 32 bit surface: A8R8G8B8, the fourth keeps the destination's alpha (<c>[surface+0xC]</c>, <c>0x5EFDB7</c>).</summary>
	// [BIN:TPW-EXE:0x5efd21 surface masks] the line routine reads the three channel masks and the fourth mask from the surface description; the map surface takes its D3D format from the table 0x8BD558[[0x8BD554]] (0x5F1F81..0x5F1FCC): A4R4G4B4 or A8R8G8B8 by the option bit 0x400000 of [0x7A1A8C] (0x44E1EB..0x44E202; the software renderer forces 8888, 0x58DACA). OpenTPW's textures are always 32 bit, [EXT:texture32-always], so the 32 bit variant is the only one used; the 4444 surface of the original's default presets (TEXTURE32 0, a 16 bpp surface: the original then draws the fills, outlines and frames with the 16 bit drawer, vtable 0x703680 set at 0x617390, as well as the line) and the 8, 16 and 24 bit line variants (0x5edad0, 0x5ee580, 0x5ef040, jump table 0x5f3218) are not ported
	private const uint Red = 0x00FF0000, Green = 0x0000FF00, Blue = 0x000000FF, Alpha = 0xFF000000;

	/// <summary>Copies <paramref name="baseRgba"/> (or clears the surface when null), then draws every rectangle.</summary>
	public static void Bake( UiPixelSurface surface, byte[]? baseRgba, IReadOnlyList<MapPrim> prims )
	{
		var rgba = surface.Rgba;
		if ( baseRgba != null && baseRgba.Length == rgba.Length )
			Buffer.BlockCopy( baseRgba, 0, rgba, 0, rgba.Length );
		else
			Array.Clear( rgba );
		var width = surface.Width;
		var height = surface.Height;
		for ( var index = 0; index < prims.Count; index++ )
		{
			var prim = prims[index];
			if ( prim.Line )
			{
				var (x, y) = ((int)prim.X, (int)prim.Y);
				if ( (uint)x >= (uint)width || (uint)y >= (uint)height )
					continue;
				var at = (y * width + x) * 4;
				var source = ((uint)prim.Color.R << 16) | ((uint)prim.Color.G << 8) | prim.Color.B;
				var destination = ((uint)rgba[at + 3] << 24) | ((uint)rgba[at] << 16) | ((uint)rgba[at + 1] << 8) | rgba[at + 2];
				var result = ParkMapLine.Blend( source, destination, prim.Color.A, Red, Green, Blue, Alpha );
				rgba[at] = (byte)(result >> 16);
				rgba[at + 1] = (byte)(result >> 8);
				rgba[at + 2] = (byte)result;
				rgba[at + 3] = (byte)(result >> 24);
				continue;
			}
			var x0 = Math.Max( 0, (int)MathF.Floor( prim.X ) );
			var y0 = Math.Max( 0, (int)MathF.Floor( prim.Y ) );
			var x1 = Math.Min( width, (int)MathF.Floor( prim.X + prim.Width ) );
			var y1 = Math.Min( height, (int)MathF.Floor( prim.Y + prim.Height ) );
			// [BIN:TPW-EXE:0x5f23cd blended fills] the fills (slot +0x20, 0x615cd0) and the outlines (slots +8 and +0xc, 0x615380 and 0x615520) are drawn with the blend flag (flags 8, 0x5f23cd; test 0x18): per channel out = d + (((s & m) - (d & m)) * a >> 8) with the alpha a of the colour record's byte +3, the destination's alpha kept; so even a = 255 gives s - 1 where s > d
			var a = prim.Color.A;
			for ( var y = y0; y < y1; y++ )
				for ( var x = x0; x < x1; x++ )
				{
					var at = (y * width + x) * 4;
					rgba[at] = (byte)(rgba[at] + (((prim.Color.R - rgba[at]) * a) >> 8));
					rgba[at + 1] = (byte)(rgba[at + 1] + (((prim.Color.G - rgba[at + 1]) * a) >> 8));
					rgba[at + 2] = (byte)(rgba[at + 2] + (((prim.Color.B - rgba[at + 2]) * a) >> 8));
				}
		}
		surface.Touch();
	}

	/// <summary>The theme's map image as a surface base, nearest-sampled to the surface's size when it differs.</summary>
	public static byte[] Resample( byte[] rgba, int width, int height, int targetWidth, int targetHeight )
	{
		if ( width == targetWidth && height == targetHeight )
			return rgba;
		var result = new byte[targetWidth * targetHeight * 4];
		for ( var y = 0; y < targetHeight; y++ )
			for ( var x = 0; x < targetWidth; x++ )
				Buffer.BlockCopy( rgba, ((y * height / targetHeight) * width + x * width / targetWidth) * 4, result, (y * targetWidth + x) * 4, 4 );
		return result;
	}

	/// <summary>The sandbox's base: a solid green with each cell shaded by its ground height (<c>[EXT:sandbox-map]</c>, OpenTPW content, there is no theme image).</summary>
	public static byte[] SandboxBase( int size, MapSurface surface, Func<int, int, float> height )
	{
		var scratch = new UiPixelSurface( size, size );
		var rgba = scratch.Rgba;
		for ( var index = 0; index < rgba.Length; index += 4 )
		{
			rgba[index] = 15;
			rgba[index + 1] = 81;
			rgba[index + 2] = 26;
			rgba[index + 3] = 255;
		}
		var prims = new List<MapPrim>();
		for ( var y = 0; y < surface.Height; y++ )
			for ( var x = 0; x < surface.Width; x++ )
			{
				var shade = (byte)Math.Clamp( 75 + height( x, y ) * 3 + (x + y) % 2 * 5, 35, 150 );
				prims.Add( new MapPrim( surface.X( x ), surface.Y( y ), surface.CellWidth, surface.CellHeight, new RgbaByte( 20, shade, 30, 255 ) ) );
			}
		Bake( scratch, (byte[])rgba.Clone(), prims );
		return rgba;
	}
}
