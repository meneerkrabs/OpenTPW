using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace OpenTPW;

/// <summary>One vertex of a bolt ribbon quad in clip space (x, y, z, w) with its texture coordinates, before the divide by w.</summary>
public readonly record struct WeatherRibbonVertex( NVector4 Clip, NVector2 TexCoords );

/// <summary>One quad of a bolt ribbon: the four vertices in the original's order (old start corner, old end corner, new end corner, new start corner), drawn as the fan (0, 1, 2) and (0, 2, 3).</summary>
public readonly record struct WeatherRibbonQuad( WeatherRibbonVertex V0, WeatherRibbonVertex V1, WeatherRibbonVertex V2, WeatherRibbonVertex V3 );

/// <summary>
/// The lightning ribbon emitter of tp.exe (<c>0x57DD10</c>), GPU-free: it is fed the bolt's walk as the original's emit calls (a point pair, or the closing call with no first point, at a branch depth)
/// and projects each point to clip space through a caller-supplied function, builds the ribbon in clip xy with mitred joins and returns its quads. The original keeps per branch depth the previous
/// projected point, its direction scaled to the ribbon width, a call counter and the previous corner pair; a quad is emitted when the point after next arrives, and the closing call emits the last
/// one from the corners two points back.
/// </summary>
// [BIN:TPW-EXE:0x57DD10 ribbon emitter] state per depth k (counter +0x9DA4 + 4k, previous point +0x9DE4 + 16k, scaled direction +0xA0E4 + 8k, corner pair +0x9EE4 + 32k); a call whose two points are identical is skipped; the first call of a depth projects both points (0x578D20 mode 0, the matrix [0x87B088]), keeps the second as the previous point and the direction first minus second scaled to 1.3 / ((k + 1) x its xy length) (+0xA16C = 1.3), and sets the corner pair to the previous point plus (d.y, -d.x) and minus it, and emits nothing
// [BIN:TPW-EXE:0x57DD10 ribbon joint] a following call projects its second point n only, takes the direction n minus the previous point scaled the same way (a zero length stops the call), sums it with the stored direction (a zero sum stops it), and emits the quad (stored corner 0, stored corner 1, p - s x perp, p + s x perp) with perp = (sum.y, -sum.x) for the corner at the previous point p (its z and w), s = 1.3 / ((k + 1) x |sum|); the new corner pair (p + s x (sum.y, -sum.x), p - s x (sum.y, -sum.x)) and the direction replace the stored ones and n becomes the previous point
// [BIN:TPW-EXE:0x57DD10 ribbon end] the closing call (first point null) projects its point n, takes n minus the previous point scaled to the width, and emits the quad (stored corner 0, stored corner 1, n - s x (dir.y, -dir.x)..., i.e. the end cap at n with n's own z and w) without storing anything; the vertex order is the quad's fan (0, 1, 2) and (0, 2, 3) with colour [+0xA19C] (0x57EE30) and texture corners (u0, v0), (u1, v0), (u1, v1), (u0, v1)
public sealed class WeatherBoltRibbon
{
	/// <summary>The ribbon half-width in clip units at depth 0 (+0xA16C, written by the CRain constructor): the width at branch depth k is this over k + 1.</summary>
	public const float HalfWidth = 1.3f;
	/// <summary>The deepest branch depth the original's walk reaches (+0xA1A0, 4) plus the trunk.</summary>
	public const int DepthSlots = WeatherVisualState.MaximumBranchDepth + 1;

	private readonly Func<NVector3, NVector4> project;
	private readonly int[] counter = new int[DepthSlots];
	private readonly NVector4[] point = new NVector4[DepthSlots];
	private readonly NVector2[] direction = new NVector2[DepthSlots];
	private readonly NVector4[] cornerStart = new NVector4[DepthSlots];
	private readonly NVector4[] cornerEnd = new NVector4[DepthSlots];

	/// <param name="project">The world point (original units) to clip space: the matrix the original's emitter uses ([0x87B088], view x projection x screen).</param>
	public WeatherBoltRibbon( Func<NVector3, NVector4> project ) => this.project = project;

	/// <summary>Starts a depth over: the original's branch start sets the counter of the new depth to 0.</summary>
	public void ResetDepth( int depth ) => counter[depth] = 0;

	/// <summary>
	/// One emit call at <paramref name="depth"/> with the walk's two points (<paramref name="first"/> null for the closing call). Returns the quad it emits, or null when the call emits none.
	/// </summary>
	public WeatherRibbonQuad? Emit( int depth, NVector3? first, NVector3 second )
	{
		// a call whose two points are the same point does nothing at all, not even the counter (the guard in front of the counter)
		if ( first is { } same && same == second )
			return null;
		var index = counter[depth]++;
		var scale = HalfWidth / (depth + 1);
		if ( index == 0 )
		{
			if ( first is not { } start )
				return null;
			var a = project( start );
			var b = project( second );
			point[depth] = b;
			var along = new NVector2( a.X - b.X, a.Y - b.Y );
			direction[depth] = along;
			var lengthSquared = along.X * along.X + along.Y * along.Y;
			if ( lengthSquared == 0 )
				return null;
			var factor = scale / MathF.Sqrt( lengthSquared );
			along = new NVector2( factor * along.X, factor * along.Y );
			direction[depth] = along;
			cornerEnd[depth] = b with { X = b.X - along.Y, Y = b.Y + along.X };
			cornerStart[depth] = b with { X = b.X + along.Y, Y = b.Y - along.X };
			return null;
		}

		var next = project( second );
		var previous = point[depth];
		var step = new NVector2( next.X - previous.X, next.Y - previous.Y );
		var stepSquared = step.X * step.X + step.Y * step.Y;
		if ( stepSquared == 0 )
			return null;
		var oldStart = cornerStart[depth];
		var oldEnd = cornerEnd[depth];
		if ( first == null )
		{
			// the closing call: the end cap at the last point with that point's own z and w
			var f = scale / MathF.Sqrt( stepSquared );
			var sx = f * step.X;
			var sy = f * step.Y;
			var capEnd = next with { X = next.X - sy, Y = next.Y + sx };
			var capStart = next with { X = next.X + sy, Y = next.Y - sx };
			return Quad( oldStart, oldEnd, capEnd, capStart );
		}

		var f2 = scale / MathF.Sqrt( stepSquared );
		var scaled = new NVector2( f2 * step.X, f2 * step.Y );
		var sum = new NVector2( direction[depth].X + scaled.X, direction[depth].Y + scaled.Y );
		var sumSquared = sum.X * sum.X + sum.Y * sum.Y;
		if ( sumSquared == 0 )
			return null;
		var g = scale / MathF.Sqrt( sumSquared );
		var offset = new NVector2( g * sum.X, g * sum.Y );
		var jointStart = previous with { X = previous.X + offset.Y, Y = previous.Y - offset.X };
		var jointEnd = previous with { X = previous.X - offset.Y, Y = previous.Y + offset.X };
		direction[depth] = scaled;
		point[depth] = next;
		cornerStart[depth] = jointStart;
		cornerEnd[depth] = jointEnd;
		return Quad( oldStart, oldEnd, jointEnd, jointStart );
	}

	private static WeatherRibbonQuad Quad( NVector4 oldStart, NVector4 oldEnd, NVector4 newEnd, NVector4 newStart ) =>
		new( new( oldStart, new NVector2( 0, 0 ) ), new( oldEnd, new NVector2( 1, 0 ) ), new( newEnd, new NVector2( 1, 1 ) ), new( newStart, new NVector2( 0, 1 ) ) );
}
