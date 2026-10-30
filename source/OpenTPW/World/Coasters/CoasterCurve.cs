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
	/// <summary>The index of the knot that starts the sample's pair (sample <c>+0x2c</c>, the knot pointer): <see cref="Section"/> names its node.</summary>
	public int Knot;
}

/// <summary>
/// One knot of the track (COAST-plan §13.1): its position, the unit direction its tangents run along, its roll (the node's bank, <c>+0xd8</c>), the
/// node (section) it belongs to, on loop pylons the factor K, whether the next node carries node flag 0x40, and the roll of its node's posed track tag
/// (<see cref="TagRoll"/>, node <c>+0xe4</c>: written by the knot pass for a posed pylon without node flag 0x40 only, zero otherwise) and the point on the second rail
/// (<see cref="SecondRailPosition"/>). <see cref="Roll"/> is the node's bank (<c>+0xd8</c>), which the sampler and the slices no longer read.
/// </summary>
public readonly record struct CoasterKnot( SVector3 Position, SVector3 Direction, float Roll, int Section, bool LoopPylon = false, float LoopSideFactor = 0, bool NextIsLoopNode = false, bool Reversed = false, bool PreviousIsLoopNode = false, float TagRoll = 0, SVector3? SecondRail = null )
{
	/// <summary>
	/// The knot's point on the second rail (knot record <c>+0x40</c> of the second list, <c>0x430ed0</c>): one unit above the knot, two units along the posed tag's up row
	/// on a loop node, or (−sin a, 0, −cos a) on a model-less loop node. The roll of a sample or slice is the angle of this rail's point about the first rail's tangent.
	/// </summary>
	public SVector3 SecondRailPosition => SecondRail ?? Position + SVector3.UnitY;
}

/// <summary>
/// The knot pass of a node chain (<c>0x430ed0</c>): the knots in order (a posed pylon gives the knots of its tag table, every other node one), per node the index of
/// its first knot, its knot count and the knot on its track dummy (the table's centre, whose sample coordinate is the node's sample index, node <c>+0xfc</c>), the node
/// position (<c>+0x4c</c>), the automatic yaw and pitch of its tangent (<c>+0xd4</c>), its bank (<c>+0xd8</c>), the pose its pylon model took (null without one), whether
/// its pylon is turned back (node flag <c>0x10000000</c>) and whether it took the stack table.
/// </summary>
public sealed record CoasterKnotPass( CoasterKnot[] Knots, int[] FirstKnot, int[] KnotCount, int[] CentreKnot, SVector3[] Positions, float[] AutomaticYaw, float[] AutomaticPitch,
	float[] Banks, CoasterPylonPose?[] Poses, bool[] TurnedBack, bool[] StackTable )
{
	/// <summary>The last knot of node <paramref name="node"/>.</summary>
	public int LastKnot( int node ) => FirstKnot[node] + KnotCount[node] - 1;
}

/// <summary>
/// One cross-section slice of the drawn track mesh: the curve point at a parameter step of one knot pair, the yaw and
/// pitch of its tangent, the roll, the pair's section (the node the pair starts at) and the node its end knot belongs to
/// (<see cref="NextSection"/>: the same node for a pair between two tags of one pylon, the next node for a link). With the sampler's output it also holds its key (the knot number plus the
/// fraction, NaN without) and its sample coordinate (the lerp of the pair's two knot coordinates, NaN without), which the run table reads.
/// </summary>
public readonly record struct CoasterSlice( SVector3 Position, float Yaw, float Pitch, float Roll, int Section, float T, int NextSection = -1, float Key = float.NaN, float Coordinate = float.NaN )
{
	/// <summary>Whether the slice lies on a pair between two knots of one node (the tags of one pylon).</summary>
	public bool InsideNode => NextSection == Section;
}

/// <summary>
/// The sampler's output a chain's slices read to take their place on the run table: the runs, the sample coordinate of every knot of the chain and the index of the span's first knot along the chain.
/// </summary>
public sealed record CoasterSliceKeys( IReadOnlyList<CoasterExtrusionRun> Runs, IReadOnlyList<float> Coordinates, int FirstKnot = 0 )
{
	/// <summary>The slice at <paramref name="key"/> on knot pair <paramref name="pair"/> of the span whose first knot has the key <paramref name="baseKey"/>.</summary>
	// [BIN:TPW-EXE:0x43f8a6 slice coordinate] the slice's sample coordinate is the first knot's coordinate times (1 − t) plus the second knot's times t, with t the key minus the first knot's key
	internal CoasterSlice Slice( CoasterSliceFrames frames, int pair, float key, float baseKey )
	{
		var t = key - baseKey;
		var slice = frames.At( pair, t );
		var global = FirstKnot + pair;
		var coordinate = global + 1 < Coordinates.Count ? Coordinates[global] * (1f - t) + Coordinates[global + 1] * t : float.NaN;
		return slice with { Key = key, Coordinate = coordinate };
	}
}

/// <summary>
/// The slice frame (<c>0x43e6b0</c>) of the knot pairs of a chain: the Bézier point, the yaw and pitch of its derivative and the roll about it, at any parameter of a pair.
/// </summary>
internal sealed class CoasterSliceFrames
{
	private readonly IReadOnlyList<CoasterKnot> knots;
	private readonly (SVector3 Back, SVector3 Forward)[] controls;
	private readonly CoasterKnot[] rail;
	private readonly (SVector3 Back, SVector3 Forward)[] secondControls;
	private readonly CoasterSuspendedRolls? suspended;

	public CoasterSliceFrames( IReadOnlyList<CoasterKnot> knots, float curviness, bool closed, CoasterSuspendedRolls? suspended )
	{
		this.knots = knots;
		this.suspended = suspended;
		controls = HermiteCoasterCurve.Controls( knots, curviness, closed );
		rail = HermiteCoasterCurve.SecondRail( knots );
		secondControls = HermiteCoasterCurve.Controls( rail, curviness, closed );
	}

	/// <summary>The slice of knot pair <paramref name="pair"/> (to the next knot) at parameter <paramref name="t"/>.</summary>
	public CoasterSlice At( int pair, float t )
	{
		var next = pair + 1;
		var position = HermiteCoasterCurve.Bezier( knots[pair].Position, controls[pair].Forward, controls[next].Back, knots[next].Position, t );
		var tangent = HermiteCoasterCurve.Derivative( knots[pair].Position, controls[pair].Forward, controls[next].Back, knots[next].Position, t );
		var (yaw, pitch) = tangent.LengthSquared() > 0 ? CoasterTrack.PcOrientation( tangent ) : (0f, 0f);
		var second = HermiteCoasterCurve.Bezier( rail[pair].Position, secondControls[pair].Forward, secondControls[next].Back, rail[next].Position, t );
		var roll = suspended is { } rolls
			? HermiteCoasterCurve.FrameRoll( position, second, yaw, pitch, knots[pair], knots[next], t, rolls.Samples, rolls.Coordinate( pair ), rolls.Coordinate( next ) )
			: HermiteCoasterCurve.FrameRoll( position, second, yaw, pitch, knots[pair], knots[next], t );
		return new CoasterSlice( position, yaw, pitch, roll, knots[pair].Section, t, knots[next].Section );
	}
}

/// <summary>
/// What the roll of a slice or car frame reads on a suspended coaster (<c>0x43e6b0</c> with the controller's flag 8): the controller's samples and the sample coordinate of each knot
/// (<see cref="CoasterSampling.KnotCoordinates"/>), <paramref name="KnotOffset"/> being the index of the first knot of the slices' span.
/// </summary>
public sealed record CoasterSuspendedRolls( IReadOnlyList<CoasterSample> Samples, IReadOnlyList<float> Coordinates, int KnotOffset = 0 )
{
	/// <summary>The coordinate of the span's knot <paramref name="index"/> (0 when the knot has none).</summary>
	public float Coordinate( int index ) => KnotOffset + index < Coordinates.Count ? Coordinates[KnotOffset + index] : 0f;
}

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

	/// <summary>Knot chord (<c>0x432840</c>): √((dx² + (0.7·dy)²) + dz²) with the two height products rounded first.</summary>
	// [BIN:TPW-EXE:0x00432840 knot chord] the chord to a neighbouring knot is fsqrt of ((dx · dx + dy · dy) + dz · dz) with dx and dz the position differences and dy the difference of the two heights each multiplied by f32 0.7 (0x6fded8, 0x4328d8..0x4328f5); the squares go through a store to double and to single (exact at the thread's 24 bits, [BIN:TPW-EXE:0x00563914 control word]), so every step is one single operation in this order; the result is stored as a single (0x432954) and is 1.0 without a neighbour (0x43295f); the oracle's rows replay it (ORACLE.md X40)
	public static float Chord( SVector3 a, SVector3 b )
	{
		var dx = b.X - a.X;
		var dy = b.Y * ChordHeightWeight - a.Y * ChordHeightWeight;
		var dz = b.Z - a.Z;
		return CoasterX87.Length( dx, dy, dz );
	}

	/// <summary>The knot controls (<c>0x432430</c>): T = ±u·(F·chord/|u|), backward control P + T−/3, forward control P + T+/3, each step one single operation.</summary>
	// [BIN:TPW-EXE:0x004327c4 knot controls] the vector is scaled by s = (F · chord) / sqrt((u.x² + u.y²) + u.z²) (0x432755..0x432796, 0x4327a2..0x4327ce; F is fCurviness, the loop factor K or 1.7, whose product with the chord is commutative), each component of the scaled vector is multiplied by f32(−1/3) (0x6fdebc) and subtracted from the knot's position (0x4327df..0x432836); the back vector is −u, the forward one u, so back = P − ((−u · s) · −1/3) and forward = P − ((u · s) · −1/3), the oracle's rows replay both (ORACLE.md X40)
	public static (SVector3 Back, SVector3 Forward) KnotControls( SVector3 position, SVector3 direction, float backChord, float forwardChord, float backFactor, float forwardFactor )
	{
		var norm = CoasterX87.Length( direction.X, direction.Y, direction.Z );
		var back = backFactor * backChord / norm;
		var forward = forwardFactor * forwardChord / norm;
		const float Third = -0.33333334f;
		static float Control( float origin, float component, float scale ) => origin - component * scale * Third;
		return (new SVector3( Control( position.X, -direction.X, back ), Control( position.Y, -direction.Y, back ), Control( position.Z, -direction.Z, back ) ),
			new SVector3( Control( position.X, direction.X, forward ), Control( position.Y, direction.Y, forward ), Control( position.Z, direction.Z, forward ) ));
	}

	/// <summary>The loop-pylon tangent factor K = (1 − w)·((1 − x²)·C + 2.1·x²) + 2.1·w (<c>0x432430</c>).</summary>
	// [BIN:TPW-EXE:0x43255c loop knot factor] x = clamp(|heading · yaw axis|, 0, 1); k = f32((1 − x²)·C − x²·f32(−2.1)) stored as single (0x4325b4..0x4325d0, constant 0x6fdec4); w = (T + 1)·0.5 with T the PC table entry at the nearest index (fistp) of (2π·u − (−π/2))·scale, u = clamp(2·fmod(loop rotate, 0.5), 0, 1) (0x4325d9..0x43264a); K = f32((1 − w)·k − w·f32(−2.1)) (0x432650..0x432665)
	// [BIN:TPW-EXE:0x004325b4 loop knot arithmetic] every fmul, fsub and fadd of the routine rounds to a single (the thread's 24-bit precision, [BIN:TPW-EXE:0x00563914 control word]): x², 1 − x², its product with C and x² times −2.1 and their difference; 2π·u, the difference with −π/2 and the scaled index; T + 1, w, 1 − w and the final products and difference; fmod and the doubling of its result are exact
	public static float LoopKnotFactor( float curviness, float side, float loopRotate )
	{
		var x = Math.Clamp( MathF.Abs( side ), 0f, 1f );
		var x2 = x * x;
		var k = (1f - x2) * curviness - x2 * LoopTangentConstant;
		var u = Math.Clamp( 2f * (loopRotate % 0.5f), 0f, 1f );
		var index = (int)MathF.Round( (u * TwoPiSingle - MinusHalfPiSingle) * CoasterTrig.PcScale ) & (CoasterTrig.Size - 1);
		var w = (CoasterTrig.PcEntry( index ) + 1f) * 0.5f;
		return (1f - w) * k - w * LoopTangentConstant;
	}

	/// <summary>The constant at <c>0x6fdec4</c>, f32(−2.1).</summary>
	private const float LoopTangentConstant = -2.1f;
	private const float TwoPiSingle = 6.2831855f;
	private const float MinusHalfPiSingle = -1.5707964f;

	/// <summary>The curve point (<c>0x43e1b0</c>) of one axis in the order the PC routine evaluates it: Horner steps in t over c3, c2 and c1, each operation one single.</summary>
	// [BIN:TPW-EXE:0x0043e1b0 curve point] Q = (((c3 · t + c2) · t + c1) · t) + P0 with c2 = P1 · −6 − (P0 + P2) · −3 and, on x, c3 = P3 − ((P2 · 3 + P1 · −3) + P0), c1 = P0 · −3 − P1 · −3 (0x43e1ba..0x43e235), on y and z c3 = ((P1 · 3 − P0) − P2 · 3) + P3, c1 = P1 · 3 − P0 · 3 (0x43e237..0x43e30a); P1 is the first knot's forward control (+0x4c), P2 the second's back control (+0x34); the constants are the singles 3, −3, −6 (0x6fe110, 0x6fe114, 0x6fe118) and every operation rounds to a single (the thread's 24-bit precision, [BIN:TPW-EXE:0x00563914 control word]); the oracle's rows replay the routine (ORACLE.md X40)
	public static float BezierComponent( float p0, float p1, float p2, float p3, float t, bool firstAxis )
	{
		var c3 = firstAxis ? p3 - (p2 * 3f + p1 * -3f + p0) : p1 * 3f - p0 - p2 * 3f + p3;
		var c2 = p1 * -6f - (p0 + p2) * -3f;
		var c1 = firstAxis ? p0 * -3f - p1 * -3f : p1 * 3f - p0 * 3f;
		return ((c3 * t + c2) * t + c1) * t + p0;
	}

	public static SVector3 Bezier( SVector3 p0, SVector3 p1, SVector3 p2, SVector3 p3, float t ) => new(
		BezierComponent( p0.X, p1.X, p2.X, p3.X, t, true ), BezierComponent( p0.Y, p1.Y, p2.Y, p3.Y, t, false ), BezierComponent( p0.Z, p1.Z, p2.Z, p3.Z, t, false ) );

	/// <summary>One axis of the curve's derivative (<c>0x43e320</c>) in the PC routine's operation order.</summary>
	// [BIN:TPW-EXE:0x0043e320 curve derivative] Q' = c1 − ((c2 · −2 − (c3 · t) · 3) · t) with the c1, c2 and c3 of the point routine (0x43e32a..0x43e475): x and y use c3 = P3 − ((P2 · 3 + P1 · −3) + P0) and c1 = P0 · −3 − P1 · −3, z uses c3 = ((P1 · 3 − P0) − P2 · 3) + P3 and c1 = P1 · 3 − P0 · 3; the oracle's rows replay the routine (ORACLE.md X40)
	public static float DerivativeComponent( float p0, float p1, float p2, float p3, float t, bool firstTwoAxes )
	{
		var c3 = firstTwoAxes ? p3 - (p2 * 3f + p1 * -3f + p0) : p1 * 3f - p0 - p2 * 3f + p3;
		var c2 = p1 * -6f - (p2 + p0) * -3f;
		var c1 = firstTwoAxes ? p0 * -3f - p1 * -3f : p1 * 3f - p0 * 3f;
		return c1 - (c2 * -2f - c3 * t * 3f) * t;
	}

	public static SVector3 Derivative( SVector3 p0, SVector3 p1, SVector3 p2, SVector3 p3, float t ) => new(
		DerivativeComponent( p0.X, p1.X, p2.X, p3.X, t, true ), DerivativeComponent( p0.Y, p1.Y, p2.Y, p3.Y, t, true ), DerivativeComponent( p0.Z, p1.Z, p2.Z, p3.Z, t, false ) );

	/// <summary>Yaw = atan2(T.x, T.z); T turned by −yaw (table trigonometry), pitch = atan2(−T.y, T.z′) (<c>0x41e24</c>).</summary>
	// [BIN:STP-PPC:0x10041E24 sample yaw and pitch] yaw = atan2(T.x, T.z) of the derivative; T rotated by −yaw with the sine table; pitch = atan2(−T.y', T.z')
	public static (float Yaw, float Pitch) Orientation( SVector3 tangent )
	{
		// [EXT:deterministic-math] fpatan in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
		var yaw = DetMathF.Atan2( tangent.X, tangent.Z );
		var z = MathF.FusedMultiplyAdd( tangent.X, CoasterTrig.Sin( yaw ), tangent.Z * CoasterTrig.Cos( yaw ) );
		return (yaw, DetMathF.Atan2( -tangent.Y, z ));
	}

	/// <summary>The factor each side of a knot's controls takes: fCurviness, on a loop pylon K or 1.7.</summary>
	// [BIN:TPW-EXE:0x00432671 loop knot sides] with node flag 0x80 clear a loop pylon's back side always uses K and its forward side K when the next node has flag 0x40, else 1.7 (0x4326d5..0x432753); with node flag 0x80 set the back side uses K only when the previous node has flag 0x40, else 1.7, and the forward side K (0x432673..0x4326d0); a plain knot uses fCurviness on both sides (0x432755..0x4327c4)
	internal static (float Back, float Forward) SideFactors( bool loopPylon, bool reversed, bool previousIsLoopNode, bool nextIsLoopNode, float k, float curviness )
	{
		if ( !loopPylon )
			return (curviness, curviness);
		return reversed ? (previousIsLoopNode ? k : LoopExitTangent, k) : (k, nextIsLoopNode ? k : LoopExitTangent);
	}

	/// <summary>
	/// The knot controls of every knot (<c>0x4329f5</c>): the first knot's back chord and the last knot's forward chord are 1.0, because the chord routine reads the raw
	/// node links and the station end node has no next link (+0x64 is null). <paramref name="closed"/> is kept for the callers' sake; the rule is the same for every chain.
	/// </summary>
	internal static (SVector3 Back, SVector3 Forward)[] Controls( IReadOnlyList<CoasterKnot> knots, float curviness, bool closed )
	{
		var count = knots.Count;
		var controls = new (SVector3 Back, SVector3 Forward)[count];
		for ( var index = 0; index < count; index++ )
		{
			var knot = knots[index];
			var (backFactor, forwardFactor) = SideFactors( knot.LoopPylon, knot.Reversed, knot.PreviousIsLoopNode, knot.NextIsLoopNode, knot.LoopSideFactor, curviness );
			// [BIN:TPW-EXE:0x004329f5 knot chords] the back chord of the first knot and the forward chord of the last one are 1.0: the chord routine takes the neighbour across a node boundary from the raw links (+0x68 / +0x64), and the station end node of a closed circuit has no next link (the rebuild walks the +0x64 links from the head to a null, 0x43eb70), so the end knot's forward control is its direction over fCurviness times 1.0 and the closing pair's curve starts from it (the sampler takes S1's first knot as its end, 0x440ede)
			var backChord = index > 0 ? Chord( knots[index - 1].Position, knot.Position ) : 1f;
			var forwardChord = index < count - 1 ? Chord( knot.Position, knots[index + 1].Position ) : 1f;
			controls[index] = KnotControls( knot.Position, knot.Direction, backChord, forwardChord, backFactor, forwardFactor );
		}
		return controls;
	}

	/// <summary>
	/// The knot the closing pair of a closed chain runs to: the first knot of the second node (S1), the knot after the first node's knots (<c>0x440c10</c>: head
	/// <c>+0x64</c>'s knot table).
	/// </summary>
	public static int ClosingKnot( IReadOnlyList<CoasterKnot> knots )
	{
		for ( var index = 1; index < knots.Count; index++ )
		{
			if ( knots[index].Section != knots[0].Section )
				return index;
		}
		return Math.Min( 1, knots.Count - 1 );
	}

	/// <summary>The knots with their positions replaced by the points of the second rail (the second list of the knot records, <c>0x430ed0</c>), whose controls the curve fits like the first rail's.</summary>
	// [BIN:TPW-EXE:0x4321c0 second rail controls] every knot's controls are built twice with the same direction (0x432430): once from the first rail's records with the chords of 0x432840 over the first list, once from the second list (+8 of the node's table) with the chords over the second rail's own points; the loop factors and the flags come from the node, so the second rail is the same fit through its own points
	internal static CoasterKnot[] SecondRail( IReadOnlyList<CoasterKnot> knots )
	{
		var rail = new CoasterKnot[knots.Count];
		for ( var index = 0; index < rail.Length; index++ )
			rail[index] = knots[index] with { Position = knots[index].SecondRailPosition };
		return rail;
	}

	/// <summary>The roll of a frame: the second rail's point relative to the first rail's, turned by −yaw about Y and −pitch about X with the PC table, then atan2(−x, y).</summary>
	// [BIN:TPW-EXE:0x43e6b0 second rail roll] d = second(t) − first(t) (0x43e1b0 on each rail), x′ = dx cos(−yaw) + dz sin(−yaw), z′ = dz cos(−yaw) − dx sin(−yaw), y′ = dy cos(−pitch) − z′ sin(−pitch) with the nearest-index table lookups (0x7b0968), roll = fpatan(−x′, y′) (0x46f230) into the frame's +0xc; the sampler 0x440c10 takes the same angle for the sample's roll (+0x18 of the 56-byte record) from the sample's own point (0x44121f..0x4413bc)
	public static float SecondRailRoll( SVector3 firstPoint, SVector3 secondPoint, float yaw, float pitch ) => (float)SecondRailAngle( firstPoint, secondPoint, yaw, pitch );

	private static double SecondRailAngle( SVector3 firstPoint, SVector3 secondPoint, float yaw, float pitch )
	{
		var difference = secondPoint - firstPoint;
		difference = CoasterTrack.TurnY( difference, -yaw );
		difference = CoasterTrack.TurnX( difference, -pitch );
		// [EXT:deterministic-math] fpatan in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
		return DetMath.Atan2( -difference.X, difference.Y );
	}

	/// <summary>
	/// The roll of a slice or a car frame (<c>0x43e6b0</c>): the second rail's roll plus the lerp of the two nodes' tag rolls by the fractional part of the nodes' ordinals' lerp, or on a suspended
	/// coaster the lerp of the swinging samples' rolls at the two knots' coordinates plus the second rail's roll.
	/// </summary>
	// [BIN:TPW-EXE:0x43e6b0 tag roll lerp] without controller flag 8: u = ordinal(A node)·(1 − t) + ordinal(B node)·t (node +0 from 0x444700; the knot records' +0xc fraction is read as 0: the records come zeroed from 0x4344c0 and no store to it was found in 0x430000..0x448000), f = frac(u) clamped to [0, 1] (the frac of the float u, 0x67b280), roll += (1 − f)·roll(A node, +0xe4) + f·roll(B node): a pair inside one node takes its roll, a link between consecutive nodes lerps by t
	// [BIN:TPW-EXE:0x43e86e suspended roll] with flag 8 (the controller's suspended-coaster flag) the roll is the sample roll at ftol(coordinate of B)·t + the sample roll at ftol(coordinate of A)·(1 − t) plus the second rail's roll; the samples hold the swing there (0x43c450)
	internal static float FrameRoll( SVector3 firstPoint, SVector3 secondPoint, float yaw, float pitch, in CoasterKnot a, in CoasterKnot b, float t,
		IReadOnlyList<CoasterSample>? suspendedSamples = null, float coordinateA = 0, float coordinateB = 0 )
	{
		var angle = SecondRailAngle( firstPoint, secondPoint, yaw, pitch );
		var oneMinusT = 1f - t;
		if ( suspendedSamples != null )
		{
			if ( suspendedSamples.Count == 0 )
				return (float)angle;
			int Index( float coordinate ) => Math.Clamp( (int)coordinate, 0, suspendedSamples.Count - 1 );
			return (float)(suspendedSamples[Index( coordinateB )].Bank * (double)t + suspendedSamples[Index( coordinateA )].Bank * (double)oneMinusT + angle);
		}
		var ordinal = (float)(b.Section * (double)t + a.Section * (double)oneMinusT);
		var fraction = (double)ordinal - Math.Truncate( (double)ordinal );
		if ( fraction <= 0 )
			fraction = 0;
		if ( !(fraction < 1) )
			fraction = 1;
		return (float)((1.0 - fraction) * a.TagRoll + b.TagRoll * fraction + (float)angle);
	}

	/// <summary>Slices per knot pair of the drawn track mesh.</summary>
	// [BIN:TPW-EXE:0x43f030 mesh slice parameters] the mesh builder 0x43eb70 walks the nodes; for the link to the next node 0x43f030 lists 0xb values i/10 over one knot pair (plus run boundaries where the cross section changes), and 0x43f4e0 emits one slice per value
	public const int SliceSegments = 10;

	/// <summary>
	/// The cross-section slices of the track mesh (<c>0x43f4e0</c>): the curve evaluated at k/<paramref name="segments"/> of every link between two nodes and at
	/// the knots alone of a pair between two tags of one pylon (<c>0x43e1b0</c>), the yaw and pitch of the Bézier derivative and the knot-interpolated roll. The
	/// chain ends with its last knot's own slice: a finished track's last knot is the station end node on S0's track dummy, so the slices close the circuit
	/// without a pair of their own (the mesh builder pairs every node with its next node until there is none), and a track still being built ends at its build cursor.
	/// </summary>
	// [BIN:TPW-EXE:0x43e6b0 slice frame] the slice position is the cubic Bézier of the knot pair (0x43e1b0: P0, P0 + T+/3, P1 + T−/3, P1 at +0x40/+0x4c/+0x34/+0x40), its yaw and pitch come from the tangent and its roll from the second rail plus the tag roll lerp (FrameRoll)
	// [BIN:TPW-EXE:0x43eb70 slice lists] for every node the mesh builder lists, when the node has more than one knot, one value per knot over its own knot pairs (0x43f030 with the node as both ends: a value per knot, scaled by the count − 1), then 0xb values i/10 over the link to its next node; the slice emitter 0x43f4e0 skips each list's first value, the previous list's last
	// [BIN:TPW-EXE:0x43f030 run boundaries] with the run table of 0x4402d0 (CoasterExtrusionRunBuilder) the lists also hold every run key that lies strictly between two neighbouring values of a list, in key order (0x43f0f3..0x43f127); a slice at such a key takes the frame and the sample coordinate of its place on the knot pair like any other (0x43f4e0)
	public static CoasterSlice[] Slices( IReadOnlyList<CoasterKnot> knots, float curviness, bool closed, int segments = SliceSegments, CoasterSuspendedRolls? suspended = null, CoasterSliceKeys? keys = null )
	{
		ArgumentNullException.ThrowIfNull( knots );
		var count = knots.Count;
		if ( count < 2 || segments < 1 )
			return Array.Empty<CoasterSlice>();
		var frames = new CoasterSliceFrames( knots, curviness, closed, suspended );
		var pairs = count - 1;
		var slices = new List<CoasterSlice>( pairs * segments + 1 );
		for ( var pair = 0; pair < pairs; pair++ )
		{
			var next = pair + 1;
			// a pair between two tags of one pylon gets the slices of its knots alone
			var steps = knots[pair].Section == knots[next].Section ? 1 : segments;
			var last = pair == pairs - 1 ? steps : steps - 1;
			if ( keys == null )
			{
				for ( var step = 0; step <= last; step++ )
					slices.Add( frames.At( pair, (float)step / steps ) );
				continue;
			}
			// [BIN:TPW-EXE:0x43f030 list values] the values of a link are the key of its first knot plus i/10 (single operations: i/10 times the span 1.0, plus the key), of a pair inside a node the key plus i; the frame's parameter is the value minus the key of the first knot (0x43f7da..0x43f8d5, 0x43f8da..0x43f96b)
			var baseKey = (float)(keys.FirstKnot + pair);
			var run = 0;
			while ( run < keys.Runs.Count && keys.Runs[run].Key < baseKey )
				run++;
			float Value( int step ) => steps == 1 ? baseKey + step : (float)step / steps * 1f + baseKey;
			for ( var step = 0; step <= last; step++ )
			{
				var value = Value( step );
				slices.Add( keys.Slice( frames, pair, value, baseKey ) );
				if ( step >= steps )
					continue;
				var following = Value( step + 1 );
				while ( run < keys.Runs.Count && keys.Runs[run].Key >= value && keys.Runs[run].Key < following )
				{
					var key = keys.Runs[run].Key;
					if ( key != value )
						slices.Add( keys.Slice( frames, pair, key, baseKey ) );
					run++;
				}
			}
		}
		return slices.ToArray();
	}

	/// <summary>
	/// The cross-section runs of a chain's track mesh (<c>0x4402d0</c>) over the sampler's output for the chain's knots: the frame at a sample coordinate is the slice frame of the knot pair the coordinate lies on,
	/// and its key the pair's knot number plus the fraction to the next knot. Null without a table.
	/// </summary>
	// [BIN:TPW-EXE:0x440a30 run key] the sample index is the whole part of the coordinate; the walk starts at the knot record the sample points at (sample +0x2c) and goes on while the next knot's coordinate (+8) lies below the coordinate; the fraction is (c − this knot's) / (next's − this knot's), 0 when there is no next knot, and the key the knot's number (+0, the knot's index along the chain) plus the fraction (0x440a63..0x440ac2)
	// [BIN:TPW-EXE:0x43c0a0 frame at a coordinate] the frame of a coordinate takes the same pair and fraction: the slice frame 0x43e6b0 over the two knot records (and the second rail's), with the fraction as its parameter (0x43c0de..0x43c144)
	public static List<CoasterExtrusionRun> ExtrusionRuns( CoasterCrossSections sections, IReadOnlyList<CoasterKnot> knots, float curviness, CoasterSampling sampling, CoasterSuspendedRolls? suspended = null )
	{
		ArgumentNullException.ThrowIfNull( sections );
		ArgumentNullException.ThrowIfNull( knots );
		ArgumentNullException.ThrowIfNull( sampling );
		var count = knots.Count;
		var coordinates = sampling.KnotCoordinates;
		if ( count < 3 || coordinates.Length < count || sampling.Samples.Length == 0 || sections.Selects.Count == 0 )
			return new List<CoasterExtrusionRun>();
		var frames = new CoasterSliceFrames( knots, curviness, true, suspended );
		(int Pair, float Ratio) Locate( float position )
		{
			var index = Math.Clamp( (int)position, 0, sampling.Samples.Length - 1 );
			var piece = Math.Clamp( sampling.Samples[index].Knot, 0, count - 1 );
			while ( piece + 1 < count && coordinates[piece + 1] < position )
				piece++;
			var ratio = piece + 1 < count ? (position - coordinates[piece]) / (coordinates[piece + 1] - coordinates[piece]) : 0f;
			return (piece, ratio);
		}
		float KeyAt( float position )
		{
			var (pair, ratio) = Locate( position );
			return (float)(pair + (double)ratio);
		}
		CoasterExtrusionRunBuilder.Frame FrameAt( float position )
		{
			var (pair, ratio) = Locate( position );
			var slice = frames.At( Math.Min( pair, count - 2 ), ratio );
			return new CoasterExtrusionRunBuilder.Frame( slice.Yaw, slice.Pitch, slice.Roll );
		}
		return CoasterExtrusionRunBuilder.Build( sections, coordinates[count - 1], FrameAt, KeyAt );
	}

	public CoasterSampling Sample( IReadOnlyList<CoasterKnot> knots, float curviness )
	{
		ArgumentNullException.ThrowIfNull( knots );
		var count = knots.Count;
		if ( count < 3 )
			throw new ArgumentException( "A closed track needs its station's knots and the station end knot.", nameof( knots ) );
		var controls = Controls( knots, curviness, closed: true );
		var rail = SecondRail( knots );
		var secondControls = Controls( rail, curviness, closed: true );
		var samples = new List<CoasterSample>();
		// The closing pair runs from the last knot (the station end node, on S0's track dummy) to the first knot of S1.
		var closingKnot = ClosingKnot( knots );
		int NextKnot( int pair ) => pair == count - 1 ? closingKnot : pair + 1;
		var coordinates = new float[count];
		SVector3 Point( int pair, float t )
		{
			var next = NextKnot( pair );
			return Bezier( knots[pair].Position, controls[pair].Forward, controls[next].Back, knots[next].Position, t );
		}
		void Emit( int pair, float t, SVector3 position, bool closingSample = false )
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
			// [BIN:TPW-EXE:0x4411db sample angles] the sampler stores the sample's yaw and pitch through 0x43e590, whose table lookups round to the nearest index (fistp under the default control word)
			var (yaw, pitch) = tangent.LengthSquared() > 0 ? CoasterTrack.PcOrientation( tangent ) : (0f, 0f);
			// [BIN:TPW-EXE:0x440c10 closing sample roll] the sample taken on the closing pair (no next node, 0x440c10 line `local_a0 = 0` before 0x43e590) gets roll 0, whatever the rolls of the end node and S1 are
			// [BIN:TPW-EXE:0x440c10 sample roll] every other sample's roll (+0x18) is the second rail's roll about the slice tangent: the second rail's point at the sample's t on the pair (0x43e1b0 over the second records) relative to the sample's own point, turned by −yaw and −pitch of the tangent (0x43e590) and atan2(−x, y) (0x46f230); the node's tag roll (+0xe4) is not added here, only in the slice frames 0x43e6b0
			var roll = 0f;
			if ( !closingSample )
			{
				var clamped = Math.Clamp( t, 0f, 1f );
				var second = Bezier( rail[pair].Position, secondControls[pair].Forward, secondControls[next].Back, rail[next].Position, clamped );
				var (frameYaw, framePitch) = tangent.LengthSquared() > 0 ? CoasterTrack.PcOrientation( tangent ) : (0f, 0f);
				roll = SecondRailRoll( position, second, frameYaw, framePitch );
			}
			samples.Add( new CoasterSample
			{
				Position = position,
				Yaw = yaw,
				Pitch = pitch,
				Direction = tangent.LengthSquared() > 0 ? SVector3.Normalize( tangent ) : SVector3.UnitZ,
				Bank = roll,
				Section = knots[pair].Section,
				T = t,
				Knot = pair
			} );
		}

		// [BIN:STP-PPC:0x10044FF4 sampler] from t = 0 (sample 0 = the first knot) walk t in steps of 0.05, sum the chord lengths, place a sample on the current chord where the sum reaches 3.0 (fraction (3 − sum before)/d), restart the sum and realign t to the next multiple of 0.05
		// [BIN:TPW-EXE:0x440c10 closing pair] the walk covers every pair of the node list; at the end node (no next node) the closing pair runs from the last knot to the second node's first knot (head +0x64), emits ONE sample (t may pass 1: no pair follows, 0x4416f0), then the count n is the number of samples written, the last sample is copied into slot n (0x441783) and n grows by one when n ≤ trunc(c) + (frac(c) ≠ 0) with c the coordinate of the end knot (ctl +0xa8, 0x4417a0..0x4417d5)
		// [BIN:TPW-EXE:0x440c10 knot coordinate] a knot's coordinate is the index of the last sample before it plus (the distance walked since that sample + the length of the projected rest from the last walked point to the knot, 0x4410e0) / spacing, the first knot's is 0; the station window and the swing's fade read the coordinates of S2 and the end node
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
			// [BIN:TPW-EXE:0x00440f14 sample step] the step's length is fsqrt of ((dx² + dy²) + dz²) with the differences of the previous point and the new curve point stored as singles (0x440f14..0x440f5a), added to the running sum (0x440f5f..0x440f62)
			var d = CoasterX87.Length( previousPoint.X - current.X, previousPoint.Y - current.Y, previousPoint.Z - current.Z );
			distance += d;
			if ( distance >= Spacing && d > 0 )
			{
				var a = (Spacing - distanceBefore) / d;
				var b = 1f - a;
				// [BIN:TPW-EXE:0x00440fc8 sample point] the sample point and its parameter are the previous values times (1 − a) plus a times the new ones, the two products rounded to singles before the sum (0x440fd0..0x44100b), not a fused step
				tPrevious = tPrevious * b + a * t;
				previousPoint = new SVector3( previousPoint.X * b + a * current.X, previousPoint.Y * b + a * current.Y, previousPoint.Z * b + a * current.Z );
				distance = 0;
				var stepNow = ParameterStep - tPrevious % ParameterStep;
				t = tPrevious + stepNow;
				Emit( pair, tPrevious, previousPoint, closing );
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
				// [BIN:TPW-EXE:0x4410e0 knot projection] the rest (knot − last walked point) is scaled by the cosine between its direction and the direction from the last walked point to the new pair's curve point at the new t (t − 1), unless the rest's normalised x is NaN (_isnan 0x67b410, a zero rest)
				var rest = knots[pair + 1].Position - previousPoint;
				var restLength = CoasterX87.Length( rest.X, rest.Y, rest.Z );
				// [BIN:TPW-EXE:0x004414ca closing projection] when the pair the walk enters is the closing pair (its end node is null) the routine jumps over the projection (0x4414ca -> 0x4417eb -> 0x4415e4): the end knot's coordinate takes the rest unscaled
				if ( restLength > 0 && pair + 1 < count - 1 )
				{
					var toCurve = Point( pair + 1, t - 1f ) - previousPoint;
					var curveLength = CoasterX87.Length( toCurve.X, toCurve.Y, toCurve.Z );
					// a zero direction gives the original a NaN cosine (and a NaN coordinate); the rest is kept unscaled then
					if ( curveLength > 0 )
					{
						// [BIN:TPW-EXE:0x00441509 knot projection] the cosine is ((u.x · v.x + u.y · v.y) + u.z · v.z) of the unit vectors (each component divided by its length, 0x441554..0x441599) and scales the rest's components (0x4415b1..0x4415df)
						var cosine = rest.X / restLength * (toCurve.X / curveLength) + rest.Y / restLength * (toCurve.Y / curveLength) + rest.Z / restLength * (toCurve.Z / curveLength);
						rest = new SVector3( rest.X * cosine, rest.Y * cosine, rest.Z * cosine );
					}
				}
				coordinates[pair + 1] = samples.Count - 1 + (CoasterX87.Length( rest.X, rest.Y, rest.Z ) + distance) / Spacing;
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
	private static readonly float[] Table = Enumerable.Range( 0, Size ).Select( k => (float)DetMath.Sin( (float)((float)k / Size) * TwoPi ) ).ToArray();

	public static float Entry( int index ) => Table[index & (Size - 1)];
	public static float Sin( float angle ) => Table[(int)(angle * Scale) & (Size - 1)];
	public static float Cos( float angle ) => Table[(int)((HalfPi + angle) * Scale) & (Size - 1)];

	// ---- The PC runtime table (tp.exe 0x4708d0) -------------------------------------------------------

	/// <summary>The scale of the PC table, <c>0x7b095c</c> = f32(4096 · f32(1/2π)), the constant 0x6feb9c being f32(0.15915494).</summary>
	// [BIN:TPW-EXE:0x4708e5 table scale] 0x4708d0(4096): mask 0x7b0960 = 4095, count 0x7b4968 = 4096, scale 0x7b095c = f32(4096 · [0x6feb9c]) with [0x6feb9c] = 0x3e22f983 = f32(1/2π)
	public static readonly float PcScale = 4096f * 0.15915494f;
	/// <summary>The PC table: T[k] = f32(fsin(k/4096 · f32(2π))), the product exact in extended precision (<c>0x47090f..0x47092d</c>, [0x6feba0] = 0x40c90fdb).</summary>
	// [BIN:TPW-EXE:0x47090f table entries] T[k] = f32(fsin(k / 4096.0f · [0x6feba0])), the division and the product in x87 extended precision (both exact for k < 4096)
	private static readonly float[] PcTable = Enumerable.Range( 0, Size ).Select( k => (float)DetMath.Sin( (k / 4096.0) * (double)6.2831855f ) ).ToArray();
	/// <summary>−π/2 as single (<c>0x6fdf78</c>, <c>0x6fe040</c>), subtracted to turn a sine index into a cosine index.</summary>
	private const float MinusHalfPi = -1.5707964f;

	/// <summary>Entry <paramref name="index"/> of the PC table (masked like the lookups).</summary>
	public static float PcEntry( int index ) => PcTable[index & (Size - 1)];

	/// <summary>sin a with the index rounded to nearest even, the default <c>fistp</c> of the module's lookups (the swing, <c>0x43c7be</c>).</summary>
	// [BIN:TPW-EXE:0x43c7be nearest index] index = fistp(f32(scale · a)) & mask under the default control word (round to nearest even); the Mac lookup truncates
	public static float PcSin( float angle ) => PcTable[(int)MathF.Round( PcScale * angle ) & (Size - 1)];
	/// <summary>cos a with the nearest index: the index of f32((a − (−π/2)) · scale), the subtraction and the product rounded to singles (<c>0x43c7d0..0x43c7e5</c>).</summary>
	// [BIN:TPW-EXE:0x43c7d0 cos index] fld a; fsub [0x6fe040] (the single −π/2); fmul [0x7b095c]; fstp dword; fistp: the subtraction and the product each round to a single at the thread's 24-bit precision ([BIN:TPW-EXE:0x00563914 control word]), where an extended difference would not
	public static float PcCos( float angle ) => PcTable[(int)MathF.Round( (angle - MinusHalfPi) * PcScale ) & (Size - 1)];

	/// <summary>sin a with the truncating index of the self-intersection pass, which runs with the rounding mode set to truncate (<c>0x439ac4</c>): the product is a single rounded toward zero, then fistp truncates.</summary>
	public static float PcSinTruncated( float angle ) => PcTable[Trunc24.ToInt( Trunc24.Mul( angle, PcScale ) ) & (Size - 1)];
	/// <summary>cos a with the truncating index: the pass stores f32(a − (−π/2)) first, then f32(scale · that), both rounded toward zero (<c>0x43a33b..0x43a35b</c>).</summary>
	public static float PcCosTruncated( float angle ) => PcTable[Trunc24.ToInt( Trunc24.Mul( PcScale, Trunc24.Sub( angle, MinusHalfPi ) ) ) & (Size - 1)];
}
