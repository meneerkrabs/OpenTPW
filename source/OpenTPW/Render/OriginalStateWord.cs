namespace OpenTPW;

/// <summary>
/// The original's render state word as tp.exe's decoder <c>0x567620</c> reads it. Every primitive record carries one (<c>record+4</c>, built by the face
/// state builder <c>0x581790</c> for model faces and fixed for the 2D sprite quads of <c>0x58A010</c>); the decoder writes only the bits that changed
/// since the last primitive. This type decodes the bits whose meaning is read; bits it does not name (for example 0x800000, 0x1000000, 0x2000000 and
/// 0x4000000, which the decoder handles only as dither, sub-pixel and perspective switches) have no effect on the picture here. Caveats: stage 0's alpha operation is
/// MODULATE only when the device supports it, else SELECTARG1 (texture alpha, 0x566A4D-0x566A66); and when the engine flag bit 31 ([0x8BCBC8]) is set the decoder forces the blend factors and blend
/// enable (0x567930-0x567992); that override is not modelled and no setter of the flag was found.
/// </summary>
// [BIN:TPW-EXE:0x567620 state decoder] 0x400 -> ALPHATESTENABLE; 0x200 picks the second alpha reference of the pair at 0x7012D8 (0xF0, else 0x10); 0x100 -> ALPHABLENDENABLE (0x567992); low byte: SRCBLEND = (w >> 4) & 15, DESTBLEND = w & 15 (0x5679B0);
// 0x8000 -> ZENABLE off; 0x800 -> ZWRITEENABLE off; 0x80000 -> specular; 0x100000 -> fog; 0x400000 -> ADDRESS wrap with a linear MINFILTER (else clamp with a point MINFILTER, 0x567910);
// 0x20000 -> MAGFILTER point (else linear); 0x40000 -> MIPFILTER none (else the device's mode); 0x10000 -> stage 0 colour MODULATE2X (else MODULATE); 0x4000 -> stage 1 on (0x5678F6)
internal readonly record struct OriginalStateWord( uint Word )
{
	/// <summary>
	/// The state word of every quad of the sprite draw <c>0x58A010</c> (the control text bitmaps, sprites): the page texture's alpha-type state that
	/// <c>0x58A20C</c> asks <c>0x590C40</c> for is overwritten by this constant at <c>0x58A26C</c>.
	/// </summary>
	// [BIN:TPW-EXE:0x58a26c sprite quad state] mov [ebp-0x54], 0x7060556 (record state, 0x565F90 -> 0x565240 stores it at record+4)
	public const uint SpriteQuad = 0x0706_0556;

	public bool AlphaTest => (Word & 0x400) != 0;

	/// <summary>The alpha reference: 0xF0 when bit 0x200 is set, else 0x10 (the pair at <c>0x7012D8</c> the device probe <c>0x567270</c> picks).</summary>
	public int AlphaReference => (Word & 0x200) != 0 ? 0xF0 : 0x10;

	public bool AlphaBlend => (Word & 0x100) != 0;

	/// <summary>Direct3D blend factor numbers: 5 SRCALPHA, 6 INVSRCALPHA, 2 ONE.</summary>
	public int SourceBlend => (int)((Word >> 4) & 0xF);

	public int DestinationBlend => (int)(Word & 0xF);

	public bool DepthTest => (Word & 0x8000) == 0;

	public bool DepthWrite => (Word & 0x800) == 0;

	public bool Fog => (Word & 0x100000) != 0;

	public bool Wrap => (Word & 0x400000) != 0;

	/// <summary>True for a point MAGFILTER, false for linear.</summary>
	public bool MagnifyPoint => (Word & 0x20000) != 0;

	/// <summary>True for MIPFILTER none; otherwise the device's mip mode applies.</summary>
	public bool MipNone => (Word & 0x40000) != 0;

	/// <summary>The MINFILTER: linear for a wrapping state, point for a clamping one.</summary>
	public bool MinifyLinear => Wrap;

	/// <summary>Stage 0 colour operation: MODULATE2X (5) with bit 0x10000, else MODULATE (4).</summary>
	public int ColorOperation => (Word & 0x10000) != 0 ? 5 : 4;
}

/// <summary>
/// The texel a text control's one-colour glyph blit writes into its bitmap (display formats 4-4-4-4 and 8-8-8-8) before the sprite quads draw it with <see cref="OriginalStateWord.SpriteQuad"/>.
/// </summary>
// [BIN:TPW-EXE:0x6b4aa0 text blit class] the canvas class comes from the table at 0x782d38 by (format flag, destination type, source kind); an RGB bitmap (flag 0x20 clear, 0x4925d0) takes the class at 0xfb1ef0
// (vtable 0x70a1a8): its 8-8-8-8 blitter 0x6b24b0 writes, for every glyph sample of coverage c > 0 (0-15), the pixel (alpha = c * 17, colour RGB) and leaves coverage 0 untouched (the bitmap was cleared to zero, 0x48fb64);
// the 4-4-4-4 blitter 0x6b21f0 writes alpha nibble c. A colour alpha below 0xff (the title shadow's 0x80) scales the coverage first: level = c * alpha / 255, rounded down, and a level of 0 writes nothing (0x6b26d0-0x6b2702).
public static class OriginalTextTexel
{
	/// <summary>The 8-bit texel alpha of a glyph sample of <paramref name="coverage"/> (0-15) drawn with a colour alpha of 0xff.</summary>
	public static int Alpha( int coverage ) => coverage <= 0 ? 0 : Math.Min( coverage, 15 ) * 17;

	/// <summary>The 8-bit texel alpha of a glyph sample drawn with the colour alpha <paramref name="colorAlpha"/>; 0 means the texel is not written.</summary>
	public static int Alpha( int coverage, int colorAlpha ) => coverage <= 0 || colorAlpha <= 0 ? 0 : Math.Min( coverage, 15 ) * Math.Min( colorAlpha, 255 ) / 255 * 17;

	/// <summary>The screen alpha of the quad pixel: texel alpha times the control colour's alpha, then the SRCALPHA blend (stage 0 alpha MODULATE, 0x566840); the alpha test (0x10) drops lower values.</summary>
	public static float BlendAlpha( int texelAlpha, int diffuseAlpha ) => texelAlpha * diffuseAlpha / 65025f;

	public static bool PassesAlphaTest( int texelAlpha, int diffuseAlpha ) => texelAlpha * diffuseAlpha / 255 >= 0x10;
}
