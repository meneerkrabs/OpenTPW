namespace OpenTPW.UI.Original;

/// <summary>
/// Chooses how button text fits its box. Candidates are the preferred font followed by smaller sizes of
/// its family; sizes are unscaled layout pixels. Order: the largest candidate that fits at the current
/// text scale, then the smallest candidate at a lower whole scale (so BF4 glyphs stay pixel-exact), then
/// the smallest candidate wrapped at the current scale.
/// </summary>
// [EXT:fit-button-text] button labels shrink to fit; the original sized its fixed strings per language by hand
public static class UiTextFit
{
	public readonly record struct Choice( int Index, int Scale, bool Wrap );

	public static Choice Choose( int textScale, IReadOnlyList<(int Width, int Height)> sizes, float maximumWidth, float maximumHeight )
	{
		if ( sizes.Count == 0 )
			throw new ArgumentException( "At least one candidate size is needed.", nameof( sizes ) );
		var scale = Math.Max( 1, textScale );
		for ( var index = 0; index < sizes.Count; index++ )
			if ( Fits( sizes[index], scale, maximumWidth, maximumHeight ) )
				return new Choice( index, scale, false );
		var smallest = sizes.Count - 1;
		for ( var lower = scale - 1; lower >= 1; lower-- )
			if ( Fits( sizes[smallest], lower, maximumWidth, maximumHeight ) )
				return new Choice( smallest, lower, false );
		return new Choice( smallest, scale, true );
	}

	private static bool Fits( (int Width, int Height) size, int scale, float maximumWidth, float maximumHeight ) =>
		size.Width * scale <= maximumWidth && size.Height * scale <= maximumHeight;
}
