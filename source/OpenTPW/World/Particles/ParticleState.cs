namespace OpenTPW;

/// <summary>How the original submits one particle (docs/PARTICLES.md, "Drawing"): the render state word of its batch (<see cref="WordOf"/>), decoded by the state decoder 0x567620
/// (<see cref="OriginalStateWord"/>) into a blend, an alpha test and a depth write, and the pass that draws it (<see cref="DrawsImmediately"/>).</summary>
internal static class ParticleState
{
	/// <summary>No blend, no alpha test, depth writes: an untextured particle whose alpha byte is 0 or at least 0xF0.</summary>
	public const uint Opaque = 0x1256;

	/// <summary>Source alpha over inverse source alpha with the alpha test at 0x10 of 255 and depth writes: a sprite frame (every particle sprite texture is flagged translucent) and an untextured particle of a fractional alpha.</summary>
	public const uint Alpha = 0x556;

	/// <summary>Source alpha over one (flags 0x2004), the alpha test at 0x10, depth writes.</summary>
	public const uint SourceAlphaOne = 0x200552;

	/// <summary>One plus one (flags 0x4 without 0x2000), the alpha test at 0x10, depth writes.</summary>
	public const uint OneOne = 0x200522;

	/// <summary>The depth-write-off bit of a state word (D3DRS_ZWRITEENABLE).</summary>
	public const uint NoDepthWrite = 0x800;

	/// <summary>The blends the state words of particles decode to.</summary>
	public enum Blend { Opaque, Alpha, SourceAlphaOne, OneOne }

	/// <summary>
	/// The state word of a particle's batch. Sprite frames are always translucent: the loader that registers the particles' sprite set (0x541310) sets a global that makes every set it loads go through 0x5882E0, which clears the
	/// cut-out type bit 0x100 of each frame's texture and sets 0x200 (state 0x556 from 0x590C40), so no particle takes 0x2756.
	/// </summary>
	// [BIN:TPW-EXE:0x57C6FA untextured state] a particle without a sprite frame (the handle [edi+0x10] is -1) is 0x1256 when the alpha byte of its colour [edi+0xC] is 0 or at least 0xF0, else 0x556 (0x57C6FA to 0x57C71B)
	// [BIN:TPW-EXE:0x57C748 textured state] a sprite frame's state is 0x590C40 of its texture: flags & 0x300 of 0x100 give 0x2756, 0x200 give 0x556, 0x300 give 0x200552, else 0x1256; the frames' flags are 0x1000040 | 0x10200000 | 0x100 (0x588410, from 0x5885E2 and 0x5887AA) until 0x5882E0 (called at 0x5411B6 for every set 0x541310 loads, [0x877354] = 1 from 0x54135D) trades 0x100 for 0x200
	// [BIN:TPW-EXE:0x57C7C8 render flags] flags (+0x18) bit 4 replaces the state by 0x200522, plus 0x30 (0x200552) when bit 0x2000 is set (0x57C7C8 to 0x57C7DD), for textured and untextured particles alike; bit 0x20000 adds the point MAGFILTER bit 0x20000 of the state (0x57C7E4 to 0x57C7ED); bit 0x4000 adds the no-depth-write bit 0x800 (0x57C7F1 to 0x57C800)
	// [DATA:Tp2.plb] the shipped templates' render flags are 0, 0x4, 0x2000 and 0x2004 only (105 templates): no template sets 0x20000 or 0x4000
	internal static uint WordOf( int renderFlags, uint colour, bool textured )
	{
		uint state;
		if ( textured )
			state = Alpha;
		else
		{
			var alpha = colour >> 24;
			state = alpha == 0 || alpha >= 0xF0 ? Opaque : Alpha;
		}
		if ( (renderFlags & 0x4) != 0 )
			state = (renderFlags & 0x2000) != 0 ? SourceAlphaOne : OneOne;
		if ( (renderFlags & 0x20000) != 0 )
			state |= 0x20000;
		if ( (renderFlags & 0x4000) != 0 )
			state |= NoDepthWrite;
		return state;
	}

	/// <summary>The state word of <paramref name="sprite"/> (a particle with no frames is untextured).</summary>
	internal static uint WordOf( in ParticleSprite sprite ) => WordOf( sprite.RenderFlags, sprite.Colour, textured: sprite.FrameCount != 0 );

	/// <summary>The blend of a state word: the low byte's source and destination factors (0x56: source alpha and inverse source alpha, 0x52: source alpha and one, 0x22: one and one) with ALPHABLENDENABLE (0x100); none is opaque.</summary>
	internal static Blend BlendOf( uint state )
	{
		if ( (state & 0x100) == 0 )
			return Blend.Opaque;
		return (state & 0xFF) switch { 0x22 => Blend.OneOne, 0x52 => Blend.SourceAlphaOne, _ => Blend.Alpha };
	}

	/// <summary>The state word's alpha test (bit 0x400) as the reference the shader compares the modulated alpha with, in 0..1 (0xF0 with bit 0x200, else 0x10 of 255); 0 for no test.</summary>
	internal static float AlphaReferenceOf( uint state ) => (state & 0x400) == 0 ? 0f : new OriginalStateWord( state ).AlphaReference / 255f;

	/// <summary>Whether the state word's draw writes depth (no bit 0x800).</summary>
	internal static bool WritesDepth( uint state ) => (state & NoDepthWrite) == 0;

	/// <summary>
	/// Whether the renderer draws a state word when it is submitted: the states with bit 0x1000 or 0x2000, of which a particle only has 0x1256 (the opaque ones). Every other particle is kept by 0x565590 and drawn by 0x582AD0 in the deferred list,
	/// far to near with the translucent world faces (<see cref="WorldPass"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x565590 immediate or kept] the batch's state & the renderer's mask [[0x879AB0]+0xC] (0x3000, 0x5667D0) is not zero: drawn at once (0x56588A); else kept with the sum of its vertex depths (+0x30 over +0x20, 0x5656F3) for 0x582AD0
	internal static bool DrawsImmediately( uint state ) => WorldTranslucency.DrawsImmediately( state );
}
