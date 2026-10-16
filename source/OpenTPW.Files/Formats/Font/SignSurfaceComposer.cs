namespace OpenTPW;

/// <summary>CPU composition of the two native 128x128 sign destinations.</summary>
public static class SignSurfaceComposer
{
	// [EXT:sign-face-alpha-blend] the transparent faces are drawn with the engine's object material (alpha test below 0.1, alpha blend, no depth sorting), an OpenTPW design choice that needs no per-texture state word
	// [APPROX:COMPAT-032] tp.exe derives a textured face's state from its texture's alpha type (0x590C40: blend with an alpha test at 0xF0/255 or 0x10/255, additive, or opaque, all with depth writes) — evidence needed: the alpha type of the generated sign texture and a distant capture
	// [BIN:TPW-EXE:0x005ECB40 sign surface] the PC composes in a 256x256 ARGB surface (the destination size 128x128 doubled, 0x5ECB8C..0x5ECBBE), with two 256x256 glyph masks and 512x512 text rasters, and packs only the top 128 rows into the two 128x128 textures (the pack loops at 0x5ECF2C and 0x5ECFCE run the destination height, 128 rows, over the 256-wide rows)
	// [BIN:TPW-EXE:0x005EAB70 sign text raster] the text is drawn on an 8-bit gray DIB twice the mask's size (0x5EABE8..0x5EAC1C, top-down, a 256-entry gray palette; SetMapMode MM_TEXT 0x5EAC8E, SetBkColor 0 0x5EAF26, SetTextColor 0xFFFFFF 0x5EAF32 and the default TA_LEFT | TA_TOP alignment) and reduced to the mask by one 2x2 box: the four bytes are summed and shifted right by 2 (0x5EAFC3..0x5EAFE5)
	public const int Width = 256;
	/// <summary>The rows packed into the two textures (the destination height).</summary>
	public const int Height = 128;
	public const int HalfWidth = Width / 2;
	/// <summary>The rows of the native composition surface, glyph masks and shapes; the rows below <see cref="Height"/> feed the blur, morphology and relief at the seam and the relief's measured text extent, and are not packed.</summary>
	internal const int SurfaceHeight = Height * 2;
	private const int TextWidth = Width * 2;
	private const int TextHeight = SurfaceHeight * 2;

	/// <summary>Composes ARGB sign layers, then returns top-down RGBA for the two model textures.</summary>
	// [BIN:STP-PPC:0x100AC21C..0x100AC6B0] clear, optional board alphablt, style-2 paint, ordered style-1 paint/effects, split the top 128 rows.
	// [EXT:sign-coverage-precision] glyph coverage comes from OpenTPW's rasterizer instead of GDI masks and the blur sums in double precision (OpenTPW design choices)
	// [EXT:sign-relief-upsample] the strip resampler is tp.exe 0x5E80D0 (matched by DescimateStrip), which has no upsampling branch (a requested height above the strip divides by zero at 0x5E8364), so a strip shorter than the glyph rows is upsampled by nearest rows: an OpenTPW choice for a case the original does not define and where matching it would divide by zero
	// [APPROX:COMPAT-003] the relief's floating-point arithmetic (0x5EB8A0: normals, light and half vectors, the colour sum and the integer conversion) runs here in double precision with a float store where the original stores a float; the x87 precision control word tp.exe's thread runs at (DDSCL_FPUSETUP sets single precision, the process default is 53 bits) is not read, so single-precision rounding in the original may move a colour byte by one
	public static (byte[] Left, byte[] Right, int TextPixelCount) Compose( SignFile sign, SignFontLibrary fonts, IReadOnlyList<string?> lines, ICollection<string> diagnostics, SignPaint? firstPaintOverride = null )
	{
		ArgumentNullException.ThrowIfNull( sign );
		ArgumentNullException.ThrowIfNull( fonts );
		ArgumentNullException.ThrowIfNull( lines );
		ArgumentNullException.ThrowIfNull( diagnostics );
		if ( sign.Effects.Any( effect => effect.Style > 2 ) )
			throw new NotSupportedException( "Sign paint style is not supported by the compositor." );
		foreach ( var diagnostic in sign.Diagnostics )
			diagnostics.Add( diagnostic );

		// [BIN:STP-PPC:0x100AC21C] the compositor clears the surface to ARGB 0: outside the board, the paint and the effects a sign face is transparent, so the object shows through between the letters
		// the PC v2.0 gameplay video shows wood and sky between the letters of Belly Bounce, Azteekse Tekens and Wilde Aap (COMPAT-030, retired)
		var surface = new byte[Width * SurfaceHeight * 4];
		if ( sign.ExtraImage is { } board )
		{
			// [APPROX:COMPAT-018] GPU row orientation follows the decoded native container but awaits independent original-platform pixel parity. The PC reader's BILZ chunk tag is compared at 0x6193D0 (the inflate step); the inverse transform after it is read (D4 coefficients computed at start-up 0x591600..0x591690, 1-D inverse 0x592C50, YCbCr conversion 0x5935A0 in SignWaveletDecoder.ColourByte); open: the header scale position in 0x592E60, the alpha conversion and the x87 precision control at the conversion
			var pixels = board.IsWavelet ? SignWaveletDecoder.Decode( board ) : board.Payload;
			if ( board.Width != Width || board.Height != Height || board.BytesPerPixel != 4 || pixels.Length != Width * Height * 4 )
				throw new InvalidDataException( "Sign board dimensions or pixel format do not match the native destination." );
			for ( var at = 0; at < pixels.Length; at += 4 )
				AlphaBlit( surface, at, pixels, at );
		}

		var masks = new byte[SignFile.SlotCount][];
		var textPixels = 0;
		for ( var slot = 0; slot < masks.Length; slot++ )
		{
			masks[slot] = RenderMask( sign, fonts, lines, slot, diagnostics );
			textPixels += masks[slot].Count( value => value != 0 );
		}
		if ( textPixels != 0 )
			diagnostics.Add( "COMPAT-003: the relief arithmetic runs in double precision with float stores; the x87 precision control word of the original is not read." );

		// [BIN:TPW-EXE:0x005ECC69..0x005ECCC0 paint masks] each slot whose selector is nonzero gets its paint mask shaped from its glyph mask by its own paint record (0x5EBF10); a zero selector leaves an empty paint mask
		var paintMasks = new byte[masks.Length][];
		for ( var slot = 0; slot < masks.Length; slot++ )
			paintMasks[slot] = sign.Effects[slot].Style != 0 && (slot == 0 ? firstPaintOverride ?? sign.Paints[slot] : sign.Paints[slot]) is { } record
				? SignMaskShaping.Shape( masks[slot], Width, SurfaceHeight, record ) : new byte[Width * SurfaceHeight];

		// [BIN:STP-PPC:0x100AC25C..0x100AC49C] style 2 is drawn before either relief effect;
		// [BIN:TPW-EXE:0x005ECD8C..0x005ECDCE two style-2 masks] the two shaped paint masks are combined by maximum (copy 0x5E77C0, max 0x5E7EF0) and coloured with paint zero's RGBA
		if ( sign.Effects[0].Style == 2 && sign.Effects[1].Style == 2 )
		{
			var combined = new byte[Width * SurfaceHeight];
			for ( var i = 0; i < combined.Length; i++ )
				combined[i] = Math.Max( paintMasks[0][i], paintMasks[1][i] );
			Paint( surface, combined, firstPaintOverride ?? sign.Paints[0] );
		}
		else
		{
			for ( var slot = 0; slot < masks.Length; slot++ )
				if ( sign.Effects[slot].Style == 2 )
					Paint( surface, paintMasks[slot], slot == 0 ? firstPaintOverride ?? sign.Paints[slot] : sign.Paints[slot] );
		}

		for ( var step = 0; step < masks.Length; step++ )
		{
			var slot = sign.HeaderFlag == 0 ? step : masks.Length - step - 1;
			if ( sign.Effects[slot].Style == 1 )
				Paint( surface, paintMasks[slot], slot == 0 ? firstPaintOverride ?? sign.Paints[slot] : sign.Paints[slot] );
			Effect( surface, masks[slot], sign.SourceImages[slot], sign.Effects[slot], diagnostics );
		}

		// [BIN:TPW-EXE:0x005ECF2C pack] only the first Height rows of the surface reach the textures
		surface = surface[..(Width * Height * 4)];
		StraightenAlpha( surface );

		var left = new byte[HalfWidth * Height * 4];
		var right = new byte[left.Length];
		for ( var y = 0; y < Height; y++ )
		for ( var x = 0; x < Width; x++ )
		{
			var source = (y * Width + x) * 4;
			var destination = (y * HalfWidth + x % HalfWidth) * 4;
			var half = x < HalfWidth ? left : right;
			half[destination] = surface[source + 1];
			half[destination + 1] = surface[source + 2];
			half[destination + 2] = surface[source + 3];
			half[destination + 3] = surface[source];
		}
		return (left, right, textPixels);
	}

	/// <summary>
	/// The layers blend into a surface that started at ARGB 0, so a texel of partial alpha holds colour already scaled by that
	/// alpha. The engine blends with source alpha, which would scale it again (dark fringes, a shadow far too dark, mips that
	/// average towards black): the colour is divided back by the alpha, and fully transparent texels take the colour of their
	/// neighbours (a few dilation passes, then the mean colour) so that mip levels and filtering do not pull edges to black.
	/// </summary>
	// [EXT:sign-straight-alpha] the native GPU consumer of these texels is untraced; straight alpha with bled edges is the form the engine's alpha blend needs, and it equals the composed texels wherever alpha is 255
	internal static void StraightenAlpha( byte[] surface )
	{
		var count = surface.Length / 4;
		long sumR = 0, sumG = 0, sumB = 0, covered = 0;
		for ( var i = 0; i < count; i++ )
		{
			var at = i * 4;
			var alpha = surface[at];
			if ( alpha == 0 || alpha == 255 ) { if ( alpha == 255 ) { sumR += surface[at + 1]; sumG += surface[at + 2]; sumB += surface[at + 3]; covered++; } continue; }
			for ( var channel = 1; channel < 4; channel++ )
				surface[at + channel] = (byte)Math.Min( 255, (surface[at + channel] * 255 + alpha / 2) / alpha );
			sumR += surface[at + 1]; sumG += surface[at + 2]; sumB += surface[at + 3]; covered++;
		}
		if ( covered == 0 ) return;
		var coloured = new bool[count];
		for ( var i = 0; i < count; i++ )
			coloured[i] = surface[i * 4] != 0;
		for ( var pass = 0; pass < 8; pass++ )
		{
			var fill = new List<(int Index, byte R, byte G, byte B)>();
			for ( var y = 0; y < Height; y++ )
			for ( var x = 0; x < Width; x++ )
			{
				var index = y * Width + x;
				if ( coloured[index] ) continue;
				int r = 0, g = 0, b = 0, n = 0;
				for ( var dy = -1; dy <= 1; dy++ )
				for ( var dx = -1; dx <= 1; dx++ )
				{
					var nx = x + dx;
					var ny = y + dy;
					if ( nx < 0 || ny < 0 || nx >= Width || ny >= Height ) continue;
					var neighbour = ny * Width + nx;
					if ( !coloured[neighbour] ) continue;
					r += surface[neighbour * 4 + 1]; g += surface[neighbour * 4 + 2]; b += surface[neighbour * 4 + 3]; n++;
				}
				if ( n > 0 ) fill.Add( (index, (byte)(r / n), (byte)(g / n), (byte)(b / n)) );
			}
			if ( fill.Count == 0 ) break;
			foreach ( var (index, r, g, b) in fill )
			{
				surface[index * 4 + 1] = r; surface[index * 4 + 2] = g; surface[index * 4 + 3] = b;
				coloured[index] = true;
			}
		}
		var mean = ((byte)(sumR / covered), (byte)(sumG / covered), (byte)(sumB / covered));
		for ( var i = 0; i < count; i++ )
			if ( !coloured[i] )
			{
				surface[i * 4 + 1] = mean.Item1; surface[i * 4 + 2] = mean.Item2; surface[i * 4 + 3] = mean.Item3;
			}
	}

	internal static byte[] RenderMask( SignFile sign, SignFontLibrary fonts, IReadOnlyList<string?> lines, int index, ICollection<string> diagnostics )
	{
		var mask = new byte[Width * SurfaceHeight];
		if ( index >= lines.Count || string.IsNullOrWhiteSpace( lines[index] ) )
			return mask;
		var slot = sign.Slots[index];
		var font = fonts.Find( slot.FontFileName ) ?? fonts.Find( slot.FaceName );
		if ( font == null )
		{
			diagnostics.Add( $"Sign slot {index}: font {slot.FontFileName} ('{slot.FaceName}') is not in fonts.wad; line skipped." );
			return mask;
		}
		var line = lines[index]!.Trim();
		var missing = SignTextLayout.FindMissing( font, line );
		if ( missing.Count > 0 )
			diagnostics.Add( $"Sign slot {index}: {slot.FontFileName} has no glyph for {string.Join( ", ", missing.Select( code => $"U+{code:X4}" ) )}; drawn as .notdef." );
		var text = new byte[TextWidth * TextHeight * 4];
		var em = Math.Max( 1, slot.EmHeightPixels );
		var width = SignTextLayout.FitLogFontWidth( font, line, em, slot.LogFont.Width, TextWidth, widthPercent: slot.HorizontalScalePercent );
		SignTextLayout.DrawLine( text, TextWidth, TextHeight, font, line, em, (255, 255, 255),
			horizontalScale: SignTextLayout.LogFontHorizontalScale( font, em, width ), margin: 0, cellTop: slot.OffsetY, kerning: false, widthPercent: slot.HorizontalScalePercent );
		return ReduceText( text );
	}

	/// <summary>The mask of a 512x512 text raster (RGBA, coverage in the alpha byte): every 2x2 block of coverage bytes becomes one mask byte.</summary>
	// [BIN:TPW-EXE:0x005EAFC3 text reduction] each mask pixel is the sum of the four DIB bytes of its 2x2 block shifted right by 2 (0x5EAFCD..0x5EAFE5; the DIB is 8-bit gray, so a byte is the coverage)
	internal static byte[] ReduceText( byte[] text )
	{
		var mask = new byte[Width * SurfaceHeight];
		for ( var y = 0; y < SurfaceHeight; y++ )
		for ( var x = 0; x < Width; x++ )
		{
			var top = ((2 * y) * TextWidth + 2 * x) * 4 + 3;
			mask[y * Width + x] = (byte)((text[top] + text[top + 4] + text[top + TextWidth * 4] + text[top + TextWidth * 4 + 4]) >> 2);
		}
		return mask;
	}

	// [BIN:STP-PPC:0xAB634->0xA9178] the paint record's offsets (+12/+16) are applied against the bitmap's width and height; they move the paint mask before it is coloured, which makes the drop shadow of Wilde Aap (offset 6, 6) and the shadow under the gate text (offset -1, 1); the effect layers keep the unshifted mask (PC v2.0 video)
	// [BIN:TPW-EXE:0x005E7F60 colour blit] the shaped paint mask (SignMaskShaping, 0x5EBF10) is coloured: alpha (mask * A) >> 8 kept as the maximum, each channel lerped towards (C * A) >> 8
	private static void Paint( byte[] destination, byte[] mask, SignPaint? paint )
	{
		if ( paint == null ) return;
		for ( var i = 0; i < mask.Length; i++ )
		{
			var alpha = mask[i] * paint.A >> 8;
			var at = i * 4;
			destination[at] = (byte)Math.Max( destination[at], alpha );
			BlendChannel( destination, at + 1, paint.R * paint.A >> 8, alpha );
			BlendChannel( destination, at + 2, paint.G * paint.A >> 8, alpha );
			BlendChannel( destination, at + 3, paint.B * paint.A >> 8, alpha );
		}
	}

	/// <summary>The mask translated by (dx, dy) pixels (right and down are positive); pixels moved off the surface are lost, the uncovered edge is empty.</summary>
	internal static byte[] Shift( byte[] mask, int dx, int dy ) => dx == 0 && dy == 0 ? mask : SignMaskShaping.Shift( mask, Width, SurfaceHeight, dx, dy );

	// [BIN:TPW-EXE:0x005EB820 relief] for each effect the shape (SignEffectShape: the mask word's erosion levels, then the Gaussian of the first parameter) is built from the glyph coverage, and 0x5EB8A0 shades every pixel whose coverage byte is not 0 into an ARGB bitmap that is alpha-blitted onto the surface (0x5E7D70)
	internal static void Effect( byte[] destination, byte[] mask, SignBitmap strip, SignEffect effect, ICollection<string> diagnostics )
	{
		if ( !mask.Any( value => value != 0 ) ) return;
		if ( strip.BytesPerPixel != 4 || strip.Payload.Length != strip.Width * strip.Height * 4 || effect.Parameters.Any( value => !float.IsFinite( value ) ) )
			throw new NotSupportedException( "Sign material pixels or coefficients are not supported by the compositor." );
		// [BIN:TPW-EXE:0x005EB035 text extent] the first row with a nonzero coverage byte before its last pixel is the top, the last such row plus one is the bottom (0x5EB04E..0x5EB07C; a text of one row leaves the bottom at -1, which the original cannot shade), and the extent is bottom - top (0x5ECCC5); the mask is the whole 256-row surface mask
		var top = SurfaceHeight;
		var bottom = -1;
		for ( var y = 0; y < SurfaceHeight; y++ )
			if ( mask.AsSpan( y * Width, Width - 1 ).IndexOfAnyExcept( (byte)0 ) >= 0 )
			{
				top = Math.Min( top, y );
				bottom = y;
			}
		var extent = bottom - top + 1;
		if ( extent <= 0 ) return;
		var materialStrip = DescimateStrip( strip, extent );
		if ( extent > strip.Height )
			diagnostics.Add( "COMPAT-003: relief source upsampling uses nearest rows; the native resampler (0x5E80D0) has no upsampling branch." );
		var shape = SignEffectShape.Build( mask, Width, SurfaceHeight, effect.MaskWord, effect.Parameters[1] );
		var material = effect.Material;
		var p = effect.Parameters;
		// [BIN:TPW-EXE:0x005EB920 light] theta = p6 * -0.0174532924 and phi = p7 * 0.0174532924 (0x702F50, 0x702F54); the light is (cos(phi) cos(theta), -sin(phi) cos(theta), -sin(theta)) over its length, each component stored as a float, and the half vector is (lx / 2, ly / 2, (lz + 1) / 2) over its length (0x702F40 = 1.0, 0x702F58 = -1.0, 0x702F5C = 0.5)
		var theta = (double)p[6] * -0.01745329238474369f;
		var phi = (double)p[7] * 0.01745329238474369f;
		var ax = Math.Cos( phi ) * (float)Math.Cos( theta );
		var ay = -(Math.Sin( phi ) * (float)Math.Cos( theta ));
		var az = -Math.Sin( (float)theta );
		var lightScale = 1.0 / Math.Sqrt( ax * ax + az * az + ay * ay );
		var lx = (float)(lightScale * ax);
		var ly = (float)(lightScale * ay);
		var lz = (float)(lightScale * az);
		var hx = lx * 0.5f;
		var hy = ly * 0.5f;
		var hz = (lz + 1) * 0.5f;
		var halfScale = (float)(1.0 / Math.Sqrt( (double)hx * hx + (double)hz * hz + (double)hy * hy ));
		var halfX = hx * halfScale;
		var halfY = hy * halfScale;
		var halfZ = hz * halfScale;
		const double reciprocal255 = 0.003921568859368563;
		for ( var y = top; y <= bottom; y++ )
		for ( var x = 0; x < Width; x++ )
		{
			var at = y * Width + x;
			var alpha = mask[at];
			if ( alpha == 0 ) continue;
			// [BIN:TPW-EXE:0x005EB8A0 relief normal] the shape differences to the next byte and to the byte one row below are read without any clamp (the last column reads the first pixel of the next row; a read past the end of the shape is heap memory in the original and 0 here), scaled by p0 and by 1/255 (0x702F60), and the normal is (dx, dy, 1) over sqrt(dx^2 + dy^2 + 1), each component a float
			var right = at + 1 < shape.Length ? shape[at + 1] : 0;
			var below = at + Width < shape.Length ? shape[at + Width] : 0;
			var dx = (right - shape[at]) * (double)p[0] * reciprocal255;
			var dy = (below - shape[at]) * (double)p[0] * reciprocal255;
			var normalScale = (float)(1.0 / Math.Sqrt( dy * dy + dx * dx + 1.0 ));
			var nx = (float)(normalScale * dx);
			var ny = (float)(normalScale * dy);
			var nz = normalScale;
			var diffuseDot = (float)((double)nz * lz + (double)ny * ly + (double)nx * lx);
			var specularDot = (float)((double)nx * halfX + (double)ny * halfY + (double)nz * halfZ);
			// [BIN:TPW-EXE:0x005EB8A0 relief colour] the strip's bytes 1..3 over 255 times p2; a positive diffuse dot adds dot * p3 times the colour; a positive specular dot adds pow(dot, p5) * p4 to every channel; each sum stays a float, is multiplied by 255, clamped to 0..255 (0x5EBE50) and truncated (_ftol)
			// [BIN:STP-PPC:0x100AB1C8,0x100AB3B8..0x100AB444] 16-pixel horizontal tiling and measured-height vertical phase.
			var source = ((y - top) * strip.Width + x % strip.Width) * 4;
			var target = at * 4;
			destination[target] = Math.Max( destination[target], alpha );
			var diffuse = diffuseDot > 0 ? (double)diffuseDot * material.Diffuse : 0.0;
			var specular = specularDot > 0 ? (float)(Math.Pow( specularDot, material.SpecularExponent ) * material.WhiteSpecular) : 0f;
			for ( var channel = 0; channel < 3; channel++ )
			{
				var unit = (float)(materialStrip[source + channel + 1] * reciprocal255);
				var sum = unit * material.Base;
				if ( diffuseDot > 0 )
					sum += (float)(diffuse * unit);
				if ( specularDot > 0 )
					sum += specular;
				var scaled = Math.Clamp( sum * 255f, 0f, 255f );
				BlendChannel( destination, target + channel + 1, (byte)(int)scaled, alpha );
			}
		}
	}

	/// <summary>The identified two-pass bucket average; unchanged width makes its horizontal pass an identity.</summary>
	// [BIN:STP-PPC:0x1003F338 Bitmap::descimate] fixed-point buckets and integer channel averages
	// [BIN:TPW-EXE:0x5E80D0 strip resampler] the PC twin: step = ((src << 16) + 0x8000) / dst, a horizontal then a vertical bucket average ((sum & 0xFFFF) / count); a destination taller than the source gives a zero count and the division at 0x5E8364 faults, so the upsampling below is OpenTPW's
	public static byte[] DescimateStrip( SignBitmap strip, int height )
	{
		if ( height <= 0 || strip.Width <= 0 || strip.Height <= 0 || strip.BytesPerPixel != 4 || strip.Payload.Length != strip.Width * strip.Height * 4 )
			throw new InvalidDataException( "Invalid sign material strip." );
		var output = new byte[strip.Width * height * 4];
		if ( height > strip.Height )
		{
			for ( var row = 0; row < height; row++ )
				Buffer.BlockCopy( strip.Payload, Math.Min( strip.Height - 1, row * strip.Height / height ) * strip.Width * 4,
					output, row * strip.Width * 4, strip.Width * 4 );
			return output;
		}
		var step = ((strip.Height << 16) + 32768) / height;
		for ( var row = 0; row < height; row++ )
		{
			var first = row * step >> 16;
			var last = Math.Min( strip.Height, (row + 1) * step >> 16 );
			var count = Math.Max( 1, last - first );
			for ( var x = 0; x < strip.Width; x++ )
			for ( var channel = 0; channel < 4; channel++ )
			{
				var sum = 0;
				for ( var sourceRow = first; sourceRow < first + count; sourceRow++ )
					sum += strip.Payload[(sourceRow * strip.Width + x) * 4 + channel];
				output[(row * strip.Width + x) * 4 + channel] = (byte)((sum & 65535) / count);
			}
		}
		return output;
	}

	private static void AlphaBlit( byte[] destination, int at, byte[] source, int sourceAt )
	{
		var alpha = source[sourceAt];
		destination[at] = Math.Max( destination[at], alpha );
		for ( var channel = 1; channel < 4; channel++ )
			BlendChannel( destination, at + channel, source[sourceAt + channel], alpha );
	}

	private static void BlendChannel( byte[] destination, int at, int source, int alpha ) =>
		destination[at] = (byte)Math.Clamp( destination[at] + ((alpha * (source - destination[at])) >> 8), 0, 255 );
}
