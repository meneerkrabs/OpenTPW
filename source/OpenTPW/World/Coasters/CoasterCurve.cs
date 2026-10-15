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
/// node (section) it belongs to, on loop pylons the factor K, and whether the next node carries node flag 0x40.
/// </summary>
public readonly record struct CoasterKnot( SVector3 Position, SVector3 Direction, float Roll, int Section, bool LoopPylon = false, float LoopSideFactor = 0, bool NextIsLoopNode = false, bool Reversed = false, bool PreviousIsLoopNode = false );

/// <summary>
/// One cross-section slice of the drawn track mesh: the curve point at a parameter step of one knot pair, the yaw and
/// pitch of its tangent, the roll and the pair's section (the node the pair starts at).
/// </summary>
public readonly record struct CoasterSlice( SVector3 Position, float Yaw, float Pitch, float Roll, int Section, float T );

/// <summary>
/// Samples of a closed track and their spacing in world units. <see cref="KnotCoordinates"/> holds, per knot, the coordinate of the knot on the sample
/// list (<c>node +8</c> of the original: the index of the last sample before the knot plus the fraction of the spacing to the knot); the first knot is 0.
/// </summary>
public sealed record CoasterSampling( CoasterSample[] Samples, float Spacing, float[]? KnotCoordinates = null )
{
	public float Length => Samples.Length * Spacing;
	public float[] KnotCoordinates { get; init; } = KnotCoordinates ?? Array.Empty<float>();
}

/// <summary>
/// Fits the closed curve through the knots of a finished track and samples it. The knots run from the first station node to the station end node, which
/// stands on the first one: the closing pair of the curve runs from the end knot to the second knot. Swappable; the rating pass, the motion and the
/// renderer consume samples only.
/// </summary>
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
	// [APPROX:COAST-032] single-precision steps; the original takes dy = fmsubs(0.7, b.y, 0.7·a.y) with one rounding (0x10032A64), sums the squares with double fmadd and rounds the double sqrt once (frsp) — evidence needed: an isolated oracle run of 0x10032990 (it calls the sqrt import)
	public static float Chord( SVector3 a, SVector3 b )
	{
		var dx = b.X - a.X;
		var dy = b.Y * ChordHeightWeight - a.Y * ChordHeightWeight;
		var dz = b.Z - a.Z;
		return MathF.Sqrt( dx * dx + dy * dy + dz * dz );
	}

	/// <summary>The knot controls (<c>0x32298</c>): T = ±u·(C·chord/|u|), backward control P + T−/3, forward P + T+/3.</summary>
	// [BIN:STP-PPC:0x10032298 knot tangents] plain knot T− = −u·C·chord−/|u|, T+ = u·C·chord+/|u|; knot +52 = P + T−/3, +76 = P + T+/3
	// [APPROX:COAST-032] |u| is a single-precision length; the original's knot-control norms (0x100326DC..0x10032760) mix double and fused steps (compare CoasterRating.MixedLength) — evidence needed: an isolated oracle run of 0x10032298
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

	/// <summary>One axis of the Bézier derivative <c>0x41c0c</c> in its power-form operation order.</summary>
	// [BIN:STP-PPC:0x10041C0C segment derivative] Q'(t) = c1 + fmadds(t, t·(3·c3), t·(2·c2)) with c1 = 3P1 − 3P0, c2 = 3P0 − 6P1 + 3P2, c3 = −P0 + 3P1 − 3P2 + P3 built by the same fused steps as 0x10041B00 (2.0 at −13340(r2))
	public static float DerivativeComponent( float p0, float p1, float p2, float p3, float t )
	{
		var c1 = 3f * p1;
		var c2 = -6f * p1;
		var c3 = MathF.FusedMultiplyAdd( -1f, p0, c1 );
		c2 = MathF.FusedMultiplyAdd( 3f, p0, c2 );
		c1 = MathF.FusedMultiplyAdd( -3f, p0, c1 );
		c3 = MathF.FusedMultiplyAdd( -3f, p2, c3 );
		c2 = MathF.FusedMultiplyAdd( 3f, p2, c2 );
		c3 = p3 + c3;
		c2 = 2f * c2;
		c3 = 3f * c3;
		c2 = t * c2;
		c3 = t * c3;
		return c1 + MathF.FusedMultiplyAdd( t, c3, c2 );
	}

	/// <summary>The Bézier derivative (<c>0x41c0c</c>).</summary>
	public static SVector3 Derivative( SVector3 p0, SVector3 p1, SVector3 p2, SVector3 p3, float t ) => new(
		DerivativeComponent( p0.X, p1.X, p2.X, p3.X, t ), DerivativeComponent( p0.Y, p1.Y, p2.Y, p3.Y, t ), DerivativeComponent( p0.Z, p1.Z, p2.Z, p3.Z, t ) );

	/// <summary>Yaw = atan2(T.x, T.z); T turned by −yaw (table trigonometry), pitch = atan2(−T.y, T.z′) (<c>0x41e24</c>).</summary>
	// [BIN:STP-PPC:0x10041E24 sample yaw and pitch] yaw = atan2(T.x, T.z) of the derivative; T rotated by −yaw with the sine table; pitch = atan2(−T.y', T.z')
	public static (float Yaw, float Pitch) Orientation( SVector3 tangent )
	{
		var yaw = MathF.Atan2( tangent.X, tangent.Z );
		var z = MathF.FusedMultiplyAdd( tangent.X, CoasterTrig.Sin( yaw ), tangent.Z * CoasterTrig.Cos( yaw ) );
		return (yaw, MathF.Atan2( -tangent.Y, z ));
	}

	/// <summary>
	/// The knot controls of every knot; the first knot has no previous neighbour and the last none after it (a closed chain's last knot, the station
	/// end node, has the second knot as its next one), so those chords are 1.0.
	/// </summary>
	private static (SVector3 Back, SVector3 Forward)[] Controls( IReadOnlyList<CoasterKnot> knots, float curviness, bool closed )
	{
		var count = knots.Count;
		var controls = new (SVector3 Back, SVector3 Forward)[count];
		for ( var index = 0; index < count; index++ )
		{
			var knot = knots[index];
			// [BIN:STP-PPC:0x100326DC loop knot sides] with node flag 0x80 clear a loop pylon's back side always uses K and its forward side K when the next node has flag 0x40, else 1.7 (0x80 set mirrors this, 0x10032600..0x100326D8); plain knots use fCurviness
			// the first pylon of a loop pair (node flag 0x80) mirrors the rule: its forward side always uses K and its back side K only after a loop node
			var backFactor = knot.LoopPylon ? (knot.Reversed ? (knot.PreviousIsLoopNode ? knot.LoopSideFactor : LoopExitTangent) : knot.LoopSideFactor) : curviness;
			var forwardFactor = knot.LoopPylon ? (knot.Reversed ? knot.LoopSideFactor : knot.NextIsLoopNode ? knot.LoopSideFactor : LoopExitTangent) : curviness;
			// [BIN:STP-PPC:0x10032990 knot chord] without a neighbour the chord is 1.0
			var backChord = index > 0 ? Chord( knots[index - 1].Position, knot.Position ) : 1f;
			var forwardChord = index < count - 1 ? Chord( knot.Position, knots[index + 1].Position ) : closed && count > 1 ? Chord( knot.Position, knots[1].Position ) : 1f;
			controls[index] = KnotControls( knot.Position, knot.Direction, backChord, forwardChord, backFactor, forwardFactor );
		}
		return controls;
	}

	/// <summary>Slices per knot pair of the drawn track mesh.</summary>
	// [BIN:TPW-EXE:0x43f030 mesh slice parameters] the mesh builder 0x43eb70 walks the nodes; for the link to the next node 0x43f030 lists 0xb values i/10 over one knot pair (plus run boundaries where the cross section changes), and 0x43f4e0 emits one slice per value
	public const int SliceSegments = 10;

	/// <summary>
	/// The cross-section slices of the track mesh (<c>0x43f4e0</c>): the curve evaluated at k/<paramref name="segments"/> of every knot pair
	/// (<c>0x43e1b0</c>), the yaw and pitch of the Bézier derivative and the knot-interpolated roll. The chain ends with its last knot's own slice: a
	/// finished track's last knot is the station end node on the first knot, so the slices close the circuit without a pair of their own (the mesh
	/// builder pairs every node with its next node until there is none), and a track still being built ends at its build cursor.
	/// </summary>
	// [BIN:TPW-EXE:0x43e6b0 slice frame] the slice position is the cubic Bézier of the knot pair (0x43e1b0: P0, P0 + T+/3, P1 + T−/3, P1 at +0x40/+0x4c/+0x34/+0x40), its yaw and pitch come from the tangent and its roll from the second rail; the roll here is the interpolated knot roll (APPROX:COAST-017)
	// [APPROX:COAST-026] the mesh slices use the 10 uniform steps per knot pair of the original but not its extra slices at cross-section changes (the dry/wet blends of the flumes) or at pylon-mesh track tags (one knot per node here) — evidence needed: the run list builder 0x4402d0 and the blend path of 0x43fd60 run on a real flume track
	public static CoasterSlice[] Slices( IReadOnlyList<CoasterKnot> knots, float curviness, bool closed, int segments = SliceSegments )
	{
		ArgumentNullException.ThrowIfNull( knots );
		var count = knots.Count;
		if ( count < 2 || segments < 1 )
			return Array.Empty<CoasterSlice>();
		var controls = Controls( knots, curviness, closed );
		var pairs = count - 1;
		var slices = new List<CoasterSlice>( pairs * segments + 1 );
		for ( var pair = 0; pair < pairs; pair++ )
		{
			var next = pair + 1;
			var last = pair == pairs - 1 ? segments : segments - 1;
			for ( var step = 0; step <= last; step++ )
			{
				var t = (float)step / segments;
				var position = Bezier( knots[pair].Position, controls[pair].Forward, controls[next].Back, knots[next].Position, t );
				var tangent = Derivative( knots[pair].Position, controls[pair].Forward, controls[next].Back, knots[next].Position, t );
				var (yaw, pitch) = tangent.LengthSquared() > 0 ? Orientation( tangent ) : (0f, 0f);
				var roll = knots[pair].Roll + (knots[next].Roll - knots[pair].Roll) * t;
				slices.Add( new CoasterSlice( position, yaw, pitch, roll, knots[pair].Section, t ) );
			}
		}
		return slices.ToArray();
	}

	public CoasterSampling Sample( IReadOnlyList<CoasterKnot> knots, float curviness )
	{
		ArgumentNullException.ThrowIfNull( knots );
		var count = knots.Count;
		if ( count < 3 )
			throw new ArgumentException( "A closed track needs its station's knots and the station end knot.", nameof( knots ) );
		var controls = Controls( knots, curviness, closed: true );
		var samples = new List<CoasterSample>();
		// The closing pair runs from the last knot (the station end node, on the first knot) to the second knot.
		int NextKnot( int pair ) => pair == count - 1 ? 1 : pair + 1;
		var coordinates = new float[count];
		SVector3 Point( int pair, float t )
		{
			var next = NextKnot( pair );
			return Bezier( knots[pair].Position, controls[pair].Forward, controls[next].Back, knots[next].Position, t );
		}
		void Emit( int pair, float t, SVector3 position )
		{
			// [BIN:STP-PPC:0x10045320 sample before its pair] while the sample's t is below 0 its record moves to the previous knot pair (when the +20 link is not null) and t grows by 1 regardless (0x10045320..0x1004535C)
			while ( t < 0 )
			{
				if ( pair > 0 )
					pair--;
				t += 1f;
			}
			var next = NextKnot( pair );
			var tangent = Derivative( knots[pair].Position, controls[pair].Forward, controls[next].Back, knots[next].Position, Math.Clamp( t, 0f, 1f ) );
			var (yaw, pitch) = Orientation( tangent );
			// [APPROX:COAST-017] the roll is interpolated between the knots' rolls instead of the second rail B(t) of the slice frame: at pylon-less knots the PC offset is (0, 1, 0), roll 0, as here (0x4313f0), and (−sin a, 0, −cos a) only on loop-flagged ones (0x43137c..0x4313e6); between knots the second rail needs the pylon tag frames (COAST-015) — evidence needed: the pylon mesh tags
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
		// [BIN:TPW-EXE:0x440c10 closing pair] the walk covers every pair of the node list; at the end node (no next node) the closing pair runs from the last knot to the second node's first knot (head +0x64), emits ONE sample (t may pass 1: no pair follows, 0x4416f0), then the count n is the number of samples written, the last sample is copied into slot n (0x441783) and n grows by one when n ≤ trunc(c) + (frac(c) ≠ 0) with c the coordinate of the end knot (ctl +0xa8, 0x4417a0..0x4417d5)
		// [BIN:TPW-EXE:0x440c10 knot coordinate] a knot's coordinate is the index of the last sample before it plus (the distance walked since that sample + the distance from the last walked point to the knot) / spacing (0x4410e0..), the first knot's is 0; the station window and the swing's fade read the coordinates of S2 and the end node
		// [BIN:TPW-EXE:0x441783 slot n copy] the copy of the last sample in slot n (the car point of the last sample reads it, 0x4409b0) is the controller's Slot(n): the point after the last sample is the last sample itself (CoasterRide.SampleAt); the growth of n is applied literally and cannot happen here, the end knot's coordinate lying less than one sample past the last one before it
		var pair = 0;
		var previousPoint = Point( 0, 0 );
		var tPrevious = 0f;
		var t = ParameterStep;
		var distance = 0f;
		var distanceBefore = 0f;
		Emit( 0, 0, previousPoint );
		var closing = false;
		while ( true )
		{
			closing = pair == count - 1;
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
				// The closing pair gets one sample; then the walk ends.
				if ( closing )
					break;
			}
			else
			{
				tPrevious = t;
				t += ParameterStep;
				previousPoint = current;
			}
			if ( t > 1f && !closing )
			{
				// The pair's end knot lies on the sample list after the last sample, by the distance walked since it plus the rest of the pair.
				coordinates[pair + 1] = samples.Count - 1 + (distance + SVector3.Distance( knots[pair + 1].Position, previousPoint )) / Spacing;
				pair++;
				tPrevious -= 1f;
				t -= 1f;
			}
			distanceBefore = distance;
			// A closing pair that never accumulates a spacing (a track whose end knot stands on its second knot) ends the walk as well.
			if ( closing && t > 4f )
				break;
		}
		// [BIN:TPW-EXE:0x4417d0 sample count] n ≤ trunc(c) + (frac(c) ≠ 0) adds one to n, the extra slot being the copy of the last sample
		var endCoordinate = coordinates[count - 1];
		var endIndex = (int)MathF.Truncate( endCoordinate ) + (endCoordinate - MathF.Truncate( endCoordinate ) != 0 ? 1 : 0);
		if ( samples.Count <= endIndex )
			samples.Add( samples[^1] );
		return new CoasterSampling( samples.ToArray(), Spacing, coordinates );
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

	// ---- The PC runtime table (tp.exe 0x4708d0) -------------------------------------------------------

	/// <summary>The scale of the PC table, <c>0x7b095c</c> = f32(4096 · f32(1/2π)), the constant 0x6feb9c being f32(0.15915494).</summary>
	// [BIN:TPW-EXE:0x4708e5 table scale] 0x4708d0(4096): mask 0x7b0960 = 4095, count 0x7b4968 = 4096, scale 0x7b095c = f32(4096 · [0x6feb9c]) with [0x6feb9c] = 0x3e22f983 = f32(1/2π)
	public static readonly float PcScale = 4096f * 0.15915494f;
	/// <summary>The PC table: T[k] = f32(fsin(k/4096 · f32(2π))), the product exact in extended precision (<c>0x47090f..0x47092d</c>, [0x6feba0] = 0x40c90fdb).</summary>
	// [BIN:TPW-EXE:0x47090f table entries] T[k] = f32(fsin(k / 4096.0f · [0x6feba0])), the division and the product in x87 extended precision (both exact for k < 4096)
	private static readonly float[] PcTable = Enumerable.Range( 0, Size ).Select( k => (float)Math.Sin( (k / 4096.0) * (double)6.2831855f ) ).ToArray();
	/// <summary>−π/2 as single (<c>0x6fdf78</c>, <c>0x6fe040</c>), subtracted to turn a sine index into a cosine index.</summary>
	private const float MinusHalfPi = -1.5707964f;

	/// <summary>Entry <paramref name="index"/> of the PC table (masked like the lookups).</summary>
	public static float PcEntry( int index ) => PcTable[index & (Size - 1)];

	/// <summary>sin a with the index rounded to nearest even, the default <c>fistp</c> of the module's lookups (the swing, <c>0x43c7be</c>).</summary>
	// [BIN:TPW-EXE:0x43c7be nearest index] index = fistp(f32(scale · a)) & mask under the default control word (round to nearest even); the Mac lookup truncates
	public static float PcSin( float angle ) => PcTable[(int)MathF.Round( PcScale * angle ) & (Size - 1)];
	/// <summary>cos a with the nearest index: the index of f32((a − (−π/2)) · scale), the difference carried unrounded (<c>0x43c7d0..0x43c7e5</c>).</summary>
	public static float PcCos( float angle ) => PcTable[(int)MathF.Round( (float)(((double)angle - MinusHalfPi) * PcScale) ) & (Size - 1)];

	/// <summary>sin a with the truncating index of the self-intersection pass, which runs with the rounding mode set to truncate (<c>0x439ac4</c>).</summary>
	public static float PcSinTruncated( float angle ) => PcTable[(int)(angle * PcScale) & (Size - 1)];
	/// <summary>cos a with the truncating index: the pass stores f32(a − (−π/2)) first, then f32(scale · that) (<c>0x43a33b..0x43a35b</c>).</summary>
	public static float PcCosTruncated( float angle ) => PcTable[(int)(PcScale * (angle - MinusHalfPi)) & (Size - 1)];
}
