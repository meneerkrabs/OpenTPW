namespace OpenTPW;

/// <summary>An RGB colour in 0 to 255 per channel.</summary>
public readonly record struct PoolColour( int R, int G, int B )
{
	/// <summary>The colour as shader floats (0 to 1).</summary>
	public System.Numerics.Vector3 ToVector() => new( R / 255f, G / 255f, B / 255f );
	internal PoolColour Scaled( float factor ) => new( Math.Clamp( (int)MathF.Round( R * factor ), 0, 255 ), Math.Clamp( (int)MathF.Round( G * factor ), 0, 255 ), Math.Clamp( (int)MathF.Round( B * factor ), 0, 255 ) );
	internal PoolColour Mixed( PoolColour other, float amount ) => new( (int)MathF.Round( R + (other.R - R) * amount ), (int)MathF.Round( G + (other.G - G) * amount ), (int)MathF.Round( B + (other.B - B) * amount ) );
}

/// <summary>How the coping slabs are drawn (<see cref="PoolTextures.Coping"/>).</summary>
public enum PoolCopingPattern
{
	/// <summary>Plain slabs with a little speckle.</summary>
	Stone,
	/// <summary>Wooden planks with grain, in the slabs' bevelled frames.</summary>
	Planks,
	/// <summary>Dark stone with patches of moss.</summary>
	MossyStone,
	/// <summary>Metal panels with a rivet in each corner.</summary>
	RivetedPanels,
	/// <summary>Pastel tiles in a checker of two or three colours.</summary>
	CandyTiles
}

/// <summary>
/// The look of a pool in one theme: the coping and wall textures (generated in code from these parameters) and the water's colours (docs/POOLS.md, "Styles"). Everything that varies between
/// themes is in this record, so <see cref="PoolStyles"/> is the one table to extend (Theme Park Inc worlds can get their own rows later).
/// </summary>
/// <param name="Id">The style's name; the theme it is for.</param>
/// <param name="Coping">The coping slabs' pattern.</param>
/// <param name="CopingBase">The coping's main colour.</param>
/// <param name="CopingAccent">The coping's second colour (moss, the second candy tile, the rivets, the wood grain).</param>
/// <param name="CopingJoint">The joints between coping slabs.</param>
/// <param name="Wall">The tile colour of the pool wall and floor.</param>
/// <param name="WallJoint">The joints between wall tiles.</param>
/// <param name="WaterDeep">The water's colour where it is deep and far from the edge.</param>
/// <param name="WaterShallow">The water's colour near the edge and in the caustic highlights.</param>
/// <param name="Glow">How much the water lights itself (0 none, 1 strongly emissive).</param>
// [EXT:pools-style] OpenTPW's own pool styles; the original game has no pools
public sealed record PoolStyle( string Id, PoolCopingPattern Coping, PoolColour CopingBase, PoolColour CopingAccent, PoolColour CopingJoint,
	PoolColour Wall, PoolColour WallJoint, PoolColour WaterDeep, PoolColour WaterShallow, float Glow );
