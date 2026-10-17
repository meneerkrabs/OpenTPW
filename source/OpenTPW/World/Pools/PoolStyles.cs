namespace OpenTPW;

/// <summary>
/// The pool styles of the four Theme Park World parks, in one table (docs/POOLS.md, "Styles"), picked by the level's theme the way objects pick theirs. A theme without a row (a Theme Park Inc
/// world, until it gets styles) gets <see cref="Generic"/>.
/// </summary>
// [EXT:pools-style] OpenTPW's own pool styles; the original game has no pools
public static class PoolStyles
{
	/// <summary>Stone and wood coping round sandy tiles; lagoon green-blue water.</summary>
	public static PoolStyle Jungle { get; } = new( "jungle", PoolCopingPattern.Planks,
		CopingBase: new( 156, 108, 62 ), CopingAccent: new( 112, 74, 40 ), CopingJoint: new( 64, 40, 22 ),
		Wall: new( 214, 198, 154 ), WallJoint: new( 150, 134, 100 ),
		WaterDeep: new( 6, 84, 92 ), WaterShallow: new( 82, 224, 188 ), Glow: 0f );

	/// <summary>Dark mossy stone; murky green-purple water with a faint glow.</summary>
	public static PoolStyle Hallow { get; } = new( "hallow", PoolCopingPattern.MossyStone,
		CopingBase: new( 66, 62, 74 ), CopingAccent: new( 74, 112, 58 ), CopingJoint: new( 30, 28, 36 ),
		Wall: new( 58, 56, 70 ), WallJoint: new( 28, 26, 36 ),
		WaterDeep: new( 36, 22, 60 ), WaterShallow: new( 120, 148, 70 ), Glow: 0.3f );

	/// <summary>Riveted metal panels; bright cyan water with a soft emissive glow.</summary>
	public static PoolStyle Space { get; } = new( "space", PoolCopingPattern.RivetedPanels,
		CopingBase: new( 132, 142, 156 ), CopingAccent: new( 214, 222, 232 ), CopingJoint: new( 56, 66, 82 ),
		Wall: new( 88, 108, 130 ), WallJoint: new( 40, 52, 70 ),
		WaterDeep: new( 0, 100, 150 ), WaterShallow: new( 96, 255, 255 ), Glow: 0.65f );

	/// <summary>Pastel candy tiles; pink-lilac water.</summary>
	public static PoolStyle Fantasy { get; } = new( "fantasy", PoolCopingPattern.CandyTiles,
		CopingBase: new( 250, 190, 218 ), CopingAccent: new( 196, 178, 240 ), CopingJoint: new( 255, 244, 250 ),
		Wall: new( 246, 206, 228 ), WallJoint: new( 255, 246, 252 ),
		WaterDeep: new( 120, 130, 226 ), WaterShallow: new( 226, 190, 255 ), Glow: 0.12f );

	/// <summary>The look of a pool with no theme of its own: pale stone and blue water.</summary>
	public static PoolStyle Generic { get; } = new( "generic", PoolCopingPattern.Stone,
		CopingBase: new( 226, 222, 212 ), CopingAccent: new( 200, 196, 186 ), CopingJoint: new( 176, 180, 182 ),
		Wall: new( 92, 176, 206 ), WallJoint: new( 214, 232, 238 ),
		WaterDeep: new( 14, 96, 150 ), WaterShallow: new( 102, 210, 235 ), Glow: 0f );

	/// <summary>Every style, in the order of the themes.</summary>
	public static IReadOnlyList<PoolStyle> All { get; } = new[] { Jungle, Hallow, Space, Fantasy, Generic };

	/// <summary>The style of a theme ("jungle", "hallow", "space", "fantasy"; case ignored), or <see cref="Generic"/> for another or no theme.</summary>
	public static PoolStyle ForTheme( string? theme ) =>
		All.FirstOrDefault( style => style != Generic && string.Equals( style.Id, theme, StringComparison.OrdinalIgnoreCase ) ) ?? Generic;
}
