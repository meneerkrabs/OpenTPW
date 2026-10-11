namespace OpenTPW;

/// <summary>CPU composition of the two native 128x128 sign destinations.</summary>
public static class SignSurfaceComposer
{
	// [APPROX:COMPAT-001] native destination dimensions are integrated; original GPU UV orientation and lower DIB rows still require a pixel oracle.
	public const int Width = 256;
	public const int Height = 128;
	public const int HalfWidth = Width / 2;
	private const int TextWidth = Width * 2;
	private const int TextHeight = Height * 2;

	/// <summary>Composes ARGB sign layers, then returns top-down RGBA for the two model textures.</summary>
	// [BIN:STP-PPC:0x100AC21C..0x100AC6B0] clear, optional board alphablt, style-2 paint, ordered style-1 paint/effects, split the top 128 rows.
	// [APPROX:COMPAT-003] OpenTPW coverage and unshaped paint masks replace native GDI, morphology and Gaussian masks; relief samples and material arithmetic follow the identified native path.
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

		// [APPROX:COMPAT-030] the native compositor clears to transparent; an opaque backing stands in for untraced downstream board coverage.
		var surface = new byte[Width * Height * 4];
		var backing = BackingColor( sign );
		for ( var at = 0; at < surface.Length; at += 4 )
		{
			surface[at] = 255;
			surface[at + 1] = backing.R;
			surface[at + 2] = backing.G;
			surface[at + 3] = backing.B;
		}
		if ( sign.ExtraImage is { } board )
		{
			// [APPROX:COMPAT-018] wavelet pixels and GPU row orientation follow the decoded native container but await independent original-platform pixel parity.
			var pixels = board.IsWavelet ? SignWaveletDecoder.Decode( board ) : board.Payload;
			if ( board.Width != Width || board.Height != Height || board.BytesPerPixel != 4 || pixels.Length != surface.Length )
				throw new InvalidDataException( "Sign board dimensions or pixel format do not match the native destination." );
			for ( var at = 0; at < surface.Length; at += 4 )
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
			diagnostics.Add( "COMPAT-003: original GDI coverage, mask morphology, Gaussian filtering and exact PowerPC float rounding remain unverified." );

		// [BIN:STP-PPC:0x100AC25C..0x100AC49C] style 2 is drawn before either relief effect;
		// two style-2 masks combine by maximum and take paint zero's RGBA.
		if ( sign.Effects[0].Style == 2 && sign.Effects[1].Style == 2 )
		{
			var combined = new byte[Width * Height];
			for ( var i = 0; i < combined.Length; i++ )
				combined[i] = Math.Max( masks[0][i], masks[1][i] );
			Paint( surface, combined, firstPaintOverride ?? sign.Paints[0] );
		}
		else
		{
			for ( var slot = 0; slot < masks.Length; slot++ )
				if ( sign.Effects[slot].Style == 2 )
					Paint( surface, masks[slot], slot == 0 ? firstPaintOverride ?? sign.Paints[slot] : sign.Paints[slot] );
		}

		for ( var step = 0; step < masks.Length; step++ )
		{
			var slot = sign.HeaderFlag == 0 ? step : masks.Length - step - 1;
			if ( sign.Effects[slot].Style == 1 )
				Paint( surface, masks[slot], slot == 0 ? firstPaintOverride ?? sign.Paints[slot] : sign.Paints[slot] );
			Effect( surface, masks[slot], sign.SourceImages[slot], sign.Effects[slot], diagnostics );
		}

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

	private static (byte R, byte G, byte B) BackingColor( SignFile sign )
	{
		const byte darkR = 38, darkG = 28, darkB = 18;
		const byte lightR = 214, lightG = 190, lightB = 140;
		static double Luma( byte r, byte g, byte b ) => 0.299 * r + 0.587 * g + 0.114 * b;
		var textLumas = Enumerable.Range( 0, sign.Slots.Count )
			.Select( index => SignCanvas.SlotColor( sign, index ) )
			.Where( colour => colour != null )
			.Select( colour => Luma( colour!.Value.R, colour.Value.G, colour.Value.B ) ).ToArray();
		if ( textLumas.Length == 0 ) return (darkR, darkG, darkB);
		var darkContrast = textLumas.Min( luma => Math.Abs( luma - Luma( darkR, darkG, darkB ) ) );
		var lightContrast = textLumas.Min( luma => Math.Abs( luma - Luma( lightR, lightG, lightB ) ) );
		return lightContrast > darkContrast ? (lightR, lightG, lightB) : (darkR, darkG, darkB);
	}

	private static byte[] RenderMask( SignFile sign, SignFontLibrary fonts, IReadOnlyList<string?> lines, int index, ICollection<string> diagnostics )
	{
		var mask = new byte[Width * Height];
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
		var width = SignTextLayout.FitLogFontWidth( font, line, em, slot.LogFont.Width, TextWidth );
		SignTextLayout.DrawLine( text, TextWidth, TextHeight, font, line, em, (255, 255, 255),
			horizontalScale: SignTextLayout.LogFontHorizontalScale( font, em, width ), margin: 0, cellTop: slot.OffsetY, kerning: false );
		// [BIN:STP-PPC:0x100A9F9C] the 512-wide text DIB is reduced to the 256-wide mask by two-stage integer averaging.
		for ( var y = 0; y < Height; y++ )
		for ( var x = 0; x < Width; x++ )
		{
			var top = ((2 * y) * TextWidth + 2 * x) * 4 + 3;
			var row0 = (text[top] + text[top + 4]) / 2;
			var row1 = (text[top + TextWidth * 4] + text[top + TextWidth * 4 + 4]) / 2;
			mask[y * Width + x] = (byte)((row0 + row1) / 2);
		}
		return mask;
	}

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

	private static void Effect( byte[] destination, byte[] mask, SignBitmap strip, SignEffect effect, ICollection<string> diagnostics )
	{
		if ( !mask.Any( value => value != 0 ) ) return;
		if ( strip.BytesPerPixel != 4 || strip.Payload.Length != strip.Width * strip.Height * 4 || effect.Parameters.Any( value => !float.IsFinite( value ) ) )
			throw new NotSupportedException( "Sign material pixels or coefficients are not supported by the compositor." );
		var top = Height;
		var bottom = -1;
		for ( var y = 0; y < Height; y++ )
			if ( mask.AsSpan( y * Width, Width ).IndexOfAnyExcept( (byte)0 ) >= 0 )
			{
				top = Math.Min( top, y );
				bottom = y;
			}
		var extent = bottom - top + 1;
		if ( extent <= 0 ) return;
		var materialStrip = DescimateStrip( strip, extent );
		if ( extent > strip.Height )
			diagnostics.Add( "COMPAT-003: relief source upsampling uses nearest rows; native descimate upsampling is not established." );
		var material = effect.Material;
		var p = effect.Parameters;
		var radians = 0.01745329238474369f;
		var theta = -p[6] * radians;
		var phi = p[7] * radians;
		var lx = MathF.Cos( phi ) * MathF.Cos( theta );
		var ly = -MathF.Sin( phi ) * MathF.Cos( theta );
		var lz = -MathF.Sin( theta );
		var lightLength = MathF.Sqrt( lx * lx + ly * ly + lz * lz );
		lx /= lightLength; ly /= lightLength; lz /= lightLength;
		var hx = lx * 0.5f;
		var hy = ly * 0.5f;
		var hz = (lz + 1) * 0.5f;
		var halfLength = MathF.Sqrt( hx * hx + hy * hy + hz * hz );
		hx /= halfLength; hy /= halfLength; hz /= halfLength;
		for ( var y = top; y <= bottom; y++ )
		for ( var x = 0; x < Width; x++ )
		{
			var at = y * Width + x;
			var alpha = mask[at];
			if ( alpha == 0 ) continue;
			// [BIN:STP-PPC:0x100AB1C8,0x100AB3B8..0x100AB444] 16-pixel horizontal tiling and measured-height vertical phase.
			var source = ((y - top) * strip.Width + x % strip.Width) * 4;
			var right = mask[y * Width + Math.Min( Width - 1, x + 1 )];
			var below = mask[Math.Min( Height - 1, y + 1 ) * Width + x];
			var dx = (right - alpha) * p[0] / 255f;
			var dy = (below - alpha) * p[0] / 255f;
			var normalLength = MathF.Sqrt( dx * dx + dy * dy + 1 );
			var nx = dx / normalLength;
			var ny = dy / normalLength;
			var nz = 1 / normalLength;
			var diffuseDot = nx * lx + ny * ly + nz * lz;
			var specularDot = nx * hx + ny * hy + nz * hz;
			var strength = material.Base + (diffuseDot > 0 ? material.Diffuse * diffuseDot : 0);
			var white = specularDot > 0 ? material.WhiteSpecular * MathF.Pow( specularDot, material.SpecularExponent ) : 0;
			var target = at * 4;
			destination[target] = Math.Max( destination[target], alpha );
			for ( var channel = 0; channel < 3; channel++ )
			{
				var colour = (byte)Math.Clamp( (int)(materialStrip[source + channel + 1] * strength + 255 * white), 0, 255 );
				BlendChannel( destination, target + channel + 1, colour, alpha );
			}
		}
	}

	/// <summary>The identified two-pass bucket average; unchanged width makes its horizontal pass an identity.</summary>
	// [BIN:STP-PPC:0x1003F338 Bitmap::descimate] fixed-point buckets and integer channel averages; upsampling remains outside the proved branch.
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
