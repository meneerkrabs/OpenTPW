using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>Sample flags (sample <c>+0</c>, COAST-plan §3.3).</summary>
[Flags]
public enum CoasterSampleFlags : uint
{
	None = 0,
	TerrorScream = 2,
	NormalScream = 4,
	/// <summary>Friction applies on this sample (trough coasters: the next segment climbs).</summary>
	Friction = 8
}

/// <summary>
/// One sample of the fitted track, the 56-byte record of the original (COAST-plan §3.3, §13.6): flags, world position
/// (10 units per cell, Y up), yaw/pitch (<c>+16/+20</c>) and the unit direction they give, roll (<c>+24</c>), speed
/// from the rating pass, force vector, the section (track node index) and the curve parameter. Everything downstream
/// of the curve reads samples only.
/// </summary>
public struct CoasterSample
{
	public CoasterSampleFlags Flags;
	public SVector3 Position;
	public float Yaw;
	public float Pitch;
	public SVector3 Direction;
	/// <summary>Roll angle in radians (sample <c>+24</c>), read as the bank by the rating step.</summary>
	public float Bank;
	public float Speed;
	public SVector3 Force;
	/// <summary>Index of the track node whose knot starts the sample's pair.</summary>
	public int Section;
	/// <summary>The Bézier parameter of the sample on its pair (sample <c>+52</c>).</summary>
	public float T;
}

/// <summary>
/// One knot of the track (COAST-plan §13.1): its position, the unit direction its tangents run along, its roll, the
/// node (section) it belongs to and, on loop pylons, the factor of the side toward a loop neighbour.
/// </summary>
public readonly record struct CoasterKnot( SVector3 Position, SVector3 Direction, float Roll, int Section, bool LoopPylon = false, float LoopSideFactor = 0 );

/// <summary>Samples of a closed track and their spacing in world units.</summary>
public sealed record CoasterSampling( CoasterSample[] Samples, float Spacing )
{
	public float Length => Samples.Length * Spacing;
}

/// <summary>Fits a closed curve through knots and samples it. Swappable; the rating pass, the motion and the renderer consume samples only.</summary>
public interface ICoasterCurve
{
	CoasterSampling Sample( IReadOnlyList<CoasterKnot> knots, float curviness );
}

/// <summary>
/// The original fit and sampler (COAST-plan §13.3–13.7): every knot gets Hermite tangents along its direction with
/// length fCurviness × the 0.7-height-weighted chord to each neighbour (loop pylons: K toward a loop neighbour, 1.7
/// toward plain track), controls P ± T/3, one cubic Bézier per knot pair (<c>0x41b00</c>), walked in parameter
/// steps of 0.05 with a sample every 3.0 world units of chord length (<c>0x44ff4</c>); yaw and pitch from the
/// derivative with the 4096-entry sine table.
/// </summary>
public sealed class HermiteCoasterCurve : ICoasterCurve
{
	/// <summary>Controller <c>+172</c>: the sample spacing, written only by the constructor.</summary>
	// [BIN:STP-PPC:0x10038AB8 controller constructor] controller +172 = 3.0 world units, the only write; COASTERSMOOTHNESS does not feed it
	public const float Spacing = 3.0f;
	/// <summary>The sampler's parameter step.</summary>
	// [BIN:STP-PPC:0x10044FF4 sampler] t advances by 0.05 per evaluation
	public const float ParameterStep = 0.05f;
	/// <summary>Weight of the height difference in a knot chord (<c>0x32a34</c>).</summary>
	public const float ChordHeightWeight = 0.7f;
	public const float LoopTangent = 2.1f;
	public const float LoopExitTangent = 1.7f;

	public static readonly HermiteCoasterCurve Instance = new();

	/// <summary>Knot chord <c>0x32990</c>: √(dx² + (0.7·dy)² + dz²).</summary>
	// [BIN:STP-PPC:0x10032990 knot chord] chords to the neighbouring knots, height difference weighted by 0.7; 1.0 without a neighbour
	public static float Chord( SVector3 a, SVector3 b )
	{
		var dx = b.X - a.X;
		var dy = b.Y * ChordHeightWeight - a.Y * ChordHeightWeight;
		var dz = b.Z - a.Z;
		return MathF.Sqrt( dx * dx + dy * dy + dz * dz );
	}

	/// <summary>The knot controls (<c>0x32298</c>): T = ±u·(C·chord/|u|), backward control P + T−/3, forward P + T+/3.</summary>
	// [BIN:STP-PPC:0x10032298 knot tangents] plain knot T− = −u·C·chord−/|u|, T+ = u·C·chord+/|u|; knot +52 = P + T−/3, +76 = P + T+/3
	public static (SVector3 Back, SVector3 Forward) KnotControls( SVector3 position, SVector3 direction, float backChord, float forwardChord, float backFactor, float forwardFactor )
	{
		var norm = direction.Length();
		var back = backFactor * backChord / norm;
		var forward = forwardFactor * forwardChord / norm;
		return (position + -direction * back / 3f, position + direction * forward / 3f);
	}

	/// <summary>The loop-pylon tangent factor K = (C(1 − x²) + 2.1x²)(1 − w) + 2.1w (<c>0x3232c..0x325d0</c>).</summary>
	// [BIN:STP-PPC:0x1003232C loop knot factor] x = clamp(|heading · yaw axis|, 0, 1); w = (1 + cos 2πu)/2 with u = clamp(2·fmod(loop rotate, 0.5), 0, 1); table trigonometry
	public static float LoopKnotFactor( float curviness, float side, float loopRotate )
	{
		var x = Math.Clamp( MathF.Abs( side ), 0f, 1f );
		var x2 = x * x;
		var k = MathF.FusedMultiplyAdd( curviness, 1f - x2, LoopTangent * x2 );
		var u = Math.Clamp( 2f * (loopRotate % 0.5f), 0f, 1f );
		var w = (1f + CoasterTrig.Sin( MathF.FusedMultiplyAdd( CoasterTrig.TwoPi, u, CoasterTrig.HalfPi ) )) * 0.5f;
		return MathF.FusedMultiplyAdd( k, 1f - w, LoopTangent * w );
	}

	/// <summary>One axis of the cubic Bézier <c>0x41b00</c> in its power-form operation order.</summary>
	// [BIN:STP-PPC:0x10041B00 segment curve] Q(t) = P0 + t(3P1 − 3P0) + t²(3P0 − 6P1 + 3P2) + t³(−P0 + 3P1 − 3P2 + P3), fused single operations
	public static float BezierComponent( float p0, float p1, float p2, float p3, float t )
	{
		var c1 = 3f * p1;
		var c2 = -6f * p1;
		var c3 = MathF.FusedMultiplyAdd( -1f, p0, c1 );
		c2 = MathF.FusedMultiplyAdd( 3f, p0, c2 );
		c3 = MathF.FusedMultiplyAdd( -3f, p2, c3 );
		c2 = MathF.FusedMultiplyAdd( 3f, p2, c2 );
		c1 = MathF.FusedMultiplyAdd( -3f, p0, c1 );
		c3 = p3 + c3;
		c2 = t * c2;
		c3 = t * c3;
		c2 = t * c2;
		c3 = t * c3;
		var value = MathF.FusedMultiplyAdd( t, c3, c2 );
		value = MathF.FusedMultiplyAdd( t, c1, value );
		return p0 + value;
	}

	public static SVector3 Bezier( SVector3 p0, SVector3 p1, SVector3 p2, SVector3 p3, float t ) => new(
		BezierComponent( p0.X, p1.X, p2.X, p3.X, t ), BezierComponent( p0.Y, p1.Y, p2.Y, p3.Y, t ), BezierComponent( p0.Z, p1.Z, p2.Z, p3.Z, t ) );

	/// <summary>The Bézier derivative (<c>0x41c0c</c>).</summary>
	public static SVector3 Derivative( SVector3 p0, SVector3 p1, SVector3 p2, SVector3 p3, float t )
	{
		var u = 1f - t;
		return 3f * u * u * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * t * t * (p3 - p2);
	}

	/// <summary>Yaw = atan2(T.x, T.z); T turned by −yaw (table trigonometry), pitch = atan2(−T.y, T.z′) (<c>0x41e24</c>).</summary>
	// [BIN:STP-PPC:0x10041E24 sample yaw and pitch] yaw = atan2(T.x, T.z) of the derivative; T rotated by −yaw with the sine table; pitch = atan2(−T.y', T.z')
	public static (float Yaw, float Pitch) Orientation( SVector3 tangent )
	{
		var yaw = MathF.Atan2( tangent.X, tangent.Z );
		var z = MathF.FusedMultiplyAdd( tangent.X, CoasterTrig.Sin( yaw ), tangent.Z * CoasterTrig.Cos( yaw ) );
		return (yaw, MathF.Atan2( -tangent.Y, z ));
	}

	public CoasterSampling Sample( IReadOnlyList<CoasterKnot> knots, float curviness )
	{
		ArgumentNullException.ThrowIfNull( knots );
		var count = knots.Count;
		if ( count < 2 )
			throw new ArgumentException( "A closed track needs at least two knots.", nameof( knots ) );
		var controls = new (SVector3 Back, SVector3 Forward)[count];
		for ( var index = 0; index < count; index++ )
		{
			var knot = knots[index];
			var previous = knots[(index - 1 + count) % count];
			var next = knots[(index + 1) % count];
			// A loop pylon's side toward another loop pylon uses K, toward plain track 1.7; plain knots use fCurviness.
			var backFactor = knot.LoopPylon ? previous.LoopPylon ? knot.LoopSideFactor : LoopExitTangent : curviness;
			var forwardFactor = knot.LoopPylon ? next.LoopPylon ? knot.LoopSideFactor : LoopExitTangent : curviness;
			controls[index] = KnotControls( knot.Position, knot.Direction, Chord( previous.Position, knot.Position ), Chord( knot.Position, next.Position ), backFactor, forwardFactor );
		}
		var samples = new List<CoasterSample>();
		SVector3 Point( int pair, float t )
		{
			var next = (pair + 1) % count;
			return Bezier( knots[pair].Position, controls[pair].Forward, controls[next].Back, knots[next].Position, t );
		}
		void Emit( int pair, float t, SVector3 position )
		{
			var next = (pair + 1) % count;
			var tangent = Derivative( knots[pair].Position, controls[pair].Forward, controls[next].Back, knots[next].Position, Math.Clamp( t, 0f, 1f ) );
			var (yaw, pitch) = Orientation( tangent );
			// [APPROX:COAST-017] the roll is interpolated between the knots' rolls (the node bank on pylons, 0 on pylon-less nodes) instead of the second rail B(t) of 0x10041F80; the pylon-less second rail offset from node +224 and the pylon tag frames are not bounded — evidence needed: node +224 on pylon-less nodes and the pylon mesh tags (COAST-015)
			var roll = knots[pair].Roll + (knots[next].Roll - knots[pair].Roll) * Math.Clamp( t, 0f, 1f );
			samples.Add( new CoasterSample
			{
				Position = position,
				Yaw = yaw,
				Pitch = pitch,
				Direction = tangent.LengthSquared() > 0 ? SVector3.Normalize( tangent ) : SVector3.UnitZ,
				Bank = roll,
				Section = knots[pair].Section,
				T = t
			} );
		}

		// [BIN:STP-PPC:0x10044FF4 sampler] from t = 0 (sample 0 = the first knot) walk t in steps of 0.05, sum the chord lengths, place a sample on the current chord where the sum reaches 3.0 (fraction (3 − sum before)/d), restart the sum and realign t to the next multiple of 0.05
		// [APPROX:COAST-019] the closing pair (last knot back to the first) is walked like every other pair to its end; the original stops after the first sample on it (t may pass 1) and may duplicate the last sample — evidence needed: the closing-pair branch 0x10044EA4 and the count fix-up 0x10045858..0x10045918 run on a real track
		var pair = 0;
		var previousPoint = Point( 0, 0 );
		var tPrevious = 0f;
		var t = ParameterStep;
		var distance = 0f;
		var distanceBefore = 0f;
		Emit( 0, 0, previousPoint );
		while ( pair < count )
		{
			var current = Point( pair, t );
			var d = SVector3.Distance( current, previousPoint );
			distance += d;
			if ( distance >= Spacing && d > 0 )
			{
				var a = (Spacing - distanceBefore) / d;
				var b = 1f - a;
				tPrevious = MathF.FusedMultiplyAdd( tPrevious, b, t * a );
				previousPoint = new SVector3( MathF.FusedMultiplyAdd( previousPoint.X, b, current.X * a ),
					MathF.FusedMultiplyAdd( previousPoint.Y, b, current.Y * a ), MathF.FusedMultiplyAdd( previousPoint.Z, b, current.Z * a ) );
				distance = 0;
				var stepNow = ParameterStep - tPrevious % ParameterStep;
				t = tPrevious + stepNow;
				Emit( pair, tPrevious, previousPoint );
			}
			else
			{
				tPrevious = t;
				t += ParameterStep;
				previousPoint = current;
			}
			if ( t > 1f )
			{
				pair++;
				tPrevious -= 1f;
				t -= 1f;
			}
			distanceBefore = distance;
		}
		// A last sample closer to the first than half the spacing merges into the wrap.
		if ( samples.Count > 2 && SVector3.Distance( samples[^1].Position, samples[0].Position ) < Spacing / 2 )
			samples.RemoveAt( samples.Count - 1 );
		return new CoasterSampling( samples.ToArray(), Spacing );
	}
}

/// <summary>
/// The table trigonometry of the coaster code (<c>0xa3cc8</c>): 4096 entries T[k] = sin(f32(k/4096)·2π),
/// sin a = T[trunc(a·4096/2π) &amp; 4095], cos a = sin(π/2 + a).
/// </summary>
public static class CoasterTrig
{
	public const int Size = 4096;
	public static readonly float TwoPi = (float)(2 * Math.PI);
	public static readonly float HalfPi = (float)(Math.PI / 2);
	private static readonly float Scale = (float)Size / TwoPi;
	// [BIN:STP-PPC:0x100A3CC8 sine table] 4096 entries sin(f32(f32(k / 4096) × 2π)) rounded to single; index trunc(a × 4096/2π) & 4095 (built from 0x1004B72C, size at 0x1004B720)
	private static readonly float[] Table = Enumerable.Range( 0, Size ).Select( k => (float)Math.Sin( (float)((float)k / Size) * TwoPi ) ).ToArray();

	public static float Entry( int index ) => Table[index & (Size - 1)];
	public static float Sin( float angle ) => Table[(int)(angle * Scale) & (Size - 1)];
	public static float Cos( float angle ) => Table[(int)((HalfPi + angle) * Scale) & (Size - 1)];
}
