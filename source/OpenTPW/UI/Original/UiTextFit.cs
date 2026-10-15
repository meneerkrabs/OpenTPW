namespace OpenTPW.UI.Original;

/// <summary>
/// Chooses how button text fits its box. Candidates are the preferred font followed by smaller sizes of
/// its family; sizes are unscaled layout pixels. Every candidate is tried at every whole text scale up to
/// the current one (whole scales keep BF4 glyphs pixel-exact) and the largest drawn height that fits wins,
/// preferring the higher scale and then the earlier candidate on a tie. Only when nothing fits is the
/// smallest candidate wrapped at the current scale.
/// </summary>
// [EXT:fit-button-text] button labels shrink to fit; the original sized its fixed strings per language by hand
public static class UiTextFit
{
	public readonly record struct Choice( int Index, int Scale, bool Wrap );

	public static Choice Choose( int textScale, IReadOnlyList<(int Width, int Height)> sizes, float maximumWidth, float maximumHeight )
	{
		if ( sizes.Count == 0 )
			throw new ArgumentException( "At least one candidate size is needed.", nameof( sizes ) );
		var top = Math.Max( 1, textScale );
		Choice? best = null;
		var bestHeight = -1;
		for ( var scale = top; scale >= 1; scale-- )
			for ( var index = 0; index < sizes.Count; index++ )
				if ( Fits( sizes[index], scale, maximumWidth, maximumHeight ) && sizes[index].Height * scale > bestHeight )
				{
					best = new Choice( index, scale, false );
					bestHeight = sizes[index].Height * scale;
				}
		return best ?? new Choice( sizes.Count - 1, top, true );
	}

	private static bool Fits( (int Width, int Height) size, int scale, float maximumWidth, float maximumHeight ) =>
		size.Width * scale <= maximumWidth && size.Height * scale <= maximumHeight;
}
