namespace OpenTPW;

/// <summary>
/// Legacy text-only RGBA canvas retained for direct callers and tests. Production ride and gate
/// signs use <see cref="SignSurfaceComposer"/>. Evidence (docs/COMPATIBILITY.md): sign models carry texture slots <c>sign1</c> (left
/// half) and <c>sign2</c> (right half) whose shared textures are "SIGN1"/"SIGN2" placeholders, so
/// the game generates them at runtime; each <c>.sgn</c> has two text slots (two lines) whose
/// LOGFONT em heights (jungle gate: 144 and 119 pixels) and vertical offsets (-25 and 97)
/// fit a 256-pixel-high canvas; <c>OBJECT_NAMES.str</c> begins with ride names split over two
/// entries ("Temple"/"Of Gloom", "Sun"/"God"), matching the two slots. Which entry pair belongs to which
/// object is superseded: the original derives the two lines from the object's single name by splitting it at the
/// space nearest the middle (<see cref="SplitAtMiddleSpace"/>), which gives "Temple"/"Of Gloom" and "Sun"/"God".
/// This legacy helper uses a 512x256 canvas, opaque stored paint RGB and a caller-supplied flat
/// background; its output is not the native surface path.
/// </summary>
public static class SignCanvas
{
	public const int Width = 512;
	public const int Height = 256;
	public const int HalfWidth = Width / 2;

	/// <summary>A line's text colour: the red, green and blue bytes of its colour block, or null when its colour mode is 0 and the line is not drawn.</summary>
	// [BIN:STP-PPC:0x100ABF14 sign compositor] each line is colour-blitted with its colour block's bytes (+0x430..+0x432 for the first line, +0x444..+0x446 for the second) only when its colour mode is 1 or 2
	public static (byte R, byte G, byte B)? SlotColor( SignFile sign, int index ) =>
		index >= 0 && index < sign.ColourBlocks.Count && sign.ColourModes[index] is 1 or 2 && sign.ColourBlocks[index] is { } block ? (block.R, block.G, block.B) : null;

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
		foreach ( var diagnostic in sign.Diagnostics ) diagnostics?.Add( diagnostic );
		if ( lines.Any( line => !string.IsNullOrWhiteSpace( line ) ) )
			diagnostics?.Add( "COMPAT-003: stored paint RGB is drawn opaque as a presentation policy; native material lighting, mask effects and layer compositing are not implemented." );
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
			if ( SlotColor( sign, index ) is not { } colour )
				continue;
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
			// [BIN:TPW-EXE:0x005EAB70 sign text] tp.exe imports no TextOut, DrawText or ExtTextOut; the sign text goes through Uniscribe (ScriptStringAnalyse with flag 0x80, ScriptStringGetLogicalWidths, the percent scaling, a second ScriptStringAnalyse with the game's advance array 0xF85F00, ScriptStringOut), and no kerning API is called
			// [BIN:USP10-DLL:0x6510CEDE ScriptPlace] no pair kerning on signs: the shipped Uniscribe places a Latin run by summing per-glyph advances from its width cache (generic routine 0x65113458, cache 0x6510F241) and never requests a kern table (GetFontData has six call sites, for JSTF 0x65110255, maxp 0x65110478, cmap 0x65110C95, OS/2 0x65111022, GSUB 0x65114CA9 and GSUB/GPOS/GDEF 0x65124809, none for kern, and the image holds no kern tag and imports no GetKerningPairs; the OpenType layout code belongs to the script entries 0x65124049..0x651249C9, not to the Latin ones, and the flagged fallback 0x6511308F sums GetTextExtentExPointW extents); every shipped font has only the legacy kern table (464 to 12,657 pairs) and no GPOS or GSUB table, so no path has a kern table or pair list to apply
			// [DATA:*.sgn:LOGFONT lfWidth] the line's width, narrowed until it fits (0x100A9F9C); centred on the canvas (the original's x = 256 - extent / 2 on its 512-wide text DC)
			var emPixels = Math.Max( 1, slot.EmHeightPixels );
			var line = text.Trim();
			var logFontWidth = SignTextLayout.FitLogFontWidth( font, line, emPixels, slot.LogFont.Width, Width, widthPercent: slot.HorizontalScalePercent );
			SignTextLayout.DrawLine( canvas, Width, Height, font, line, emPixels, colour, horizontalScale: SignTextLayout.LogFontHorizontalScale( font, emPixels, logFontWidth ), margin: 0, cellTop: slot.OffsetY, kerning: false, widthPercent: slot.HorizontalScalePercent );
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
	/// sign lines; empty strings outside the table. Not how objects pick their text (see <see cref="SplitAtMiddleSpace"/>).
	/// </summary>
	public static (string Line1, string Line2) ObjectNameLines( IReadOnlyList<string> objectNames, int pairIndex )
	{
		var first = pairIndex * 2;
		string At( int index ) => index >= 0 && index < objectNames.Count ? objectNames[index] : "";
		return (At( first ), At( first + 1 ));
	}

	/// <summary>
	/// The two sign lines of a one-string object name: the name is split at the space nearest its middle and the
	/// space is dropped; a name without a space is one line.
	/// </summary>
	// [BIN:STP-PPC:0x1018E254 sign text builder] with only one text, probes the UTF-16 string at len/2, then len/2-1, len/2+1, len/2-2, ... (len probes) for U+0020; the first hit splits it into the characters before it (line 1) and those after it (line 2); no hit leaves line 1 whole and line 2 empty
	public static (string Line1, string Line2) SplitAtMiddleSpace( string name )
	{
		ArgumentNullException.ThrowIfNull( name );
		var middle = name.Length / 2;
		for ( var probe = 0; probe < name.Length; probe++ )
		{
			var offset = (probe + 1) >> 1;
			var index = (probe & 1) == 0 ? middle + offset : middle - offset;
			if ( index >= 0 && index < name.Length && name[index] == ' ' )
				return (name[..index], name[(index + 1)..]);
		}
		return (name, "");
	}
}
