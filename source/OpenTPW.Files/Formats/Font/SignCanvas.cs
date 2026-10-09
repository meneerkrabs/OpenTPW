namespace OpenTPW;

/// <summary>
/// Composes the text of an original sign into an RGBA canvas that maps onto the model's two sign
/// textures. Evidence (docs/COMPATIBILITY.md): sign models carry texture slots <c>sign1</c> (left
/// half) and <c>sign2</c> (right half) whose shared textures are "SIGN1"/"SIGN2" placeholders, so
/// the game generates them at runtime; each <c>.sgn</c> has two text slots (two lines) whose
/// LOGFONT em heights (jungle gate: 144 and 119 pixels) and vertical offsets (-25 and 97)
/// fit a 256-pixel-high canvas; <c>OBJECT_NAMES.str</c> begins with ride names split over two
/// entries ("Temple"/"Of Gloom", "Sun"/"God"), matching the two slots. Which entry pair belongs to which
/// object is not established (<c>Info.RideTypeStringIndex</c> is a ride type shared by all themes).
/// Approximations, labelled: the canvas is 512x256 (two 256x256 halves, whereas the native
/// output has two 128x128 destinations), lines are centred horizontally and shrunk
/// to fit the width, and the legacy tint interprets lighting coefficients as RGB. That tint is
/// disproven and retained as COMPAT-003 until the native mask, source-image and compositing path
/// is implemented. Bitmap metadata is available, but this renderer does not consume it.
/// </summary>
public static class SignCanvas
{
	// [APPROX:COMPAT-001] Legacy canvas; native text DIB/mask and final 128x128 destinations have distinct dimensions.
	public const int Width = 512;
	public const int Height = 256;
	public const int HalfWidth = Width / 2;

	/// <summary>Legacy COMPAT-003 tint approximation; these parameters are lighting coefficients, not RGB.</summary>
	public static (byte R, byte G, byte B) SlotColor( SignTextSlot slot )
	{
		// [APPROX:COMPAT-003] Disproven RGB interpretation retained pending native surface compositing.
		static byte Channel( float value ) => (byte)Math.Round( Math.Clamp( float.IsFinite( value ) ? value : 1, 0, 1 ) * 255 );
		return slot.Parameters.Count >= 5 ? (Channel( slot.Parameters[2] ), Channel( slot.Parameters[3] ), Channel( slot.Parameters[4] )) : ((byte)255, (byte)255, (byte)255);
	}

	/// <summary>
	/// Draws <paramref name="lines"/> (one per slot; null/empty lines are skipped) with each slot's
	/// font, em height and vertical offset. Missing fonts and characters without glyphs are reported in
	/// <paramref name="diagnostics"/> instead of throwing.
	/// </summary>
	public static byte[] Compose( SignFile sign, SignFontLibrary fonts, IReadOnlyList<string?> lines, (byte R, byte G, byte B, byte A) background, ICollection<string>? diagnostics = null )
	{
		ArgumentNullException.ThrowIfNull( sign );
		ArgumentNullException.ThrowIfNull( fonts );
		ArgumentNullException.ThrowIfNull( lines );
		foreach ( var diagnostic in sign.Diagnostics )
			diagnostics?.Add( diagnostic );
		if ( lines.Any( line => !string.IsNullOrWhiteSpace( line ) ) )
			diagnostics?.Add( "COMPAT-003: legacy sign tint approximation is active; original parameters 2..4 are shared material coefficients, not RGB. Native source-image lighting and mask compositing are not implemented." );
		var canvas = new byte[Width * Height * 4];
		for ( var i = 0; i < canvas.Length; i += 4 )
		{
			canvas[i] = background.R;
			canvas[i + 1] = background.G;
			canvas[i + 2] = background.B;
			canvas[i + 3] = background.A;
		}
		for ( var index = 0; index < sign.Slots.Count && index < lines.Count; index++ )
		{
			var text = lines[index];
			if ( string.IsNullOrWhiteSpace( text ) )
				continue;
			var slot = sign.Slots[index];
			// [DATA:*.sgn:slot font file / LOGFONT face]
			var font = fonts.Find( slot.FontFileName ) ?? fonts.Find( slot.FaceName );
			if ( font == null )
			{
				// Original data names one unshipped font (space megacost.sgn); the sign-font-substitution data correction fixes it.
				diagnostics?.Add( $"Sign slot {index}: font {slot.FontFileName} ('{slot.FaceName}') is not in fonts.wad; line skipped." );
				continue;
			}
			var missing = SignTextLayout.FindMissing( font, text );
			if ( missing.Count > 0 )
				diagnostics?.Add( $"Sign slot {index}: {slot.FontFileName} has no glyph for {string.Join( ", ", missing.Select( code => $"U+{code:X4}" ) )}; drawn as .notdef." );
			// [DATA:*.sgn:LOGFONT lfHeight] em height; [DATA:*.sgn:slot offset] cell top (TA_TOP reading is inferred from the values).
			// [APPROX:COMPAT-006] no pair kerning (GDI TextOut default) — evidence needed: binary text-output call site.
			// [APPROX:COMPAT-005] the horizontal-scale field is not applied — evidence needed: binary use of the field.
			SignTextLayout.DrawLine( canvas, Width, Height, font, text.Trim(), Math.Max( 1, slot.EmHeightPixels ), SlotColor( slot ), cellTop: slot.OffsetY, kerning: false );
		}
		return canvas;
	}

	/// <summary>Splits a canvas into its left (<c>sign1</c>) and right (<c>sign2</c>) 256x256 halves.</summary>
	public static (byte[] Left, byte[] Right) SplitHalves( byte[] canvas )
	{
		ArgumentNullException.ThrowIfNull( canvas );
		if ( canvas.Length != Width * Height * 4 )
			throw new ArgumentException( "Not a sign canvas.", nameof( canvas ) );
		var left = new byte[HalfWidth * Height * 4];
		var right = new byte[HalfWidth * Height * 4];
		for ( var y = 0; y < Height; y++ )
		{
			Buffer.BlockCopy( canvas, y * Width * 4, left, y * HalfWidth * 4, HalfWidth * 4 );
			Buffer.BlockCopy( canvas, (y * Width + HalfWidth) * 4, right, y * HalfWidth * 4, HalfWidth * 4 );
		}
		return (left, right);
	}

	/// <summary>
	/// Entry pair <paramref name="pairIndex"/> of <c>OBJECT_NAMES.str</c> (entries 2k and 2k+1) as two
	/// sign lines; empty strings outside the table. The object-to-pair mapping is unverified.
	/// </summary>
	public static (string Line1, string Line2) ObjectNameLines( IReadOnlyList<string> objectNames, int pairIndex )
	{
		var first = pairIndex * 2;
		string At( int index ) => index >= 0 && index < objectNames.Count ? objectNames[index] : "";
		return (At( first ), At( first + 1 ));
	}
}
