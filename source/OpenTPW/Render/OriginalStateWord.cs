namespace OpenTPW;

/// <summary>
/// The original's render state word as tp.exe's decoder <c>0x567620</c> reads it. Every primitive record carries one (<c>record+4</c>, built by the face
/// state builder <c>0x581790</c> for model faces and fixed for the 2D sprite quads of <c>0x58A010</c>); the decoder writes only the bits that changed
/// since the last primitive. This type decodes the bits whose meaning is read; bits it does not name (for example 0x800000, 0x1000000, 0x2000000 and
/// 0x4000000, which the decoder handles only as dither, sub-pixel and perspective switches) have no effect on the picture here.
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
