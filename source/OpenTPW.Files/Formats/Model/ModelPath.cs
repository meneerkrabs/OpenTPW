using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// A path of a geometry model: one 16-byte record of the table that the u16 at header 0x40 counts and the u32 at
/// 0xAC locates: <c>u32 kind, u16 point count, u16 (0 in the corpus), ptr → points (f32 x, y, z), u32 (0)</c>.
/// Kind bit 0x1 closes the path, bit 0x2 makes it cubic Bézier (<c>P0, (C, C, P)…</c>), else bit 0x8 makes it
/// linear and otherwise it is a Catmull-Rom spline. The corpus holds 18 paths in 15 distinct models: Bézier (kind 2 open,
/// kind 3 closed) and one linear (kind 8, the slide). A node follows a path when its parent is a dummy with type bit 0x8 (type 0x208,
/// "Spline path", "Kart_path01", "Flightpath"…) and its animation record has flag 0x200: the high 16 bits of
/// that dummy's stored index select the path, and the points are in the dummy's space.
/// <para>
/// [BIN:TPW-EXE:0x00471F1D path evaluation] The PC sampler 0x471860 turns a path position <c>v</c> (percent, see
/// <see cref="ModelPathParameters"/>) into <c>s = N · fmod(v + 1000, 100) · 0.01</c> with N = point count / 3 for
/// Bézier or the point count otherwise, minus 1 when the path is open; segment <c>i = trunc(s)</c> at fraction
/// <c>s − i</c> is evaluated by 0x474840 (Bézier, points 3i … 3i + 3), 0x474BF0 (linear, points i, i + 1) or
/// 0x474CC0 (Catmull-Rom, points i − 1 … i + 2), indices wrapping modulo the point count. With flag 0x400 the
/// direction is the Bézier derivative (0x474A20), the linear segment (0x474C70) or, for Catmull-Rom, the
/// normalised position at fraction + 0.1 itself (0x474CC0 again, as the code does).
/// </para>
/// </summary>
public sealed class ModelPath
{
	public const uint ClosedFlag = 0x1;
	public const uint BezierFlag = 0x2;
	public const uint LinearFlag = 0x8;

	public ModelPath( uint kind, IReadOnlyList<NVector3> points, ushort keys = 0, uint reserved = 0 )
	{
		Kind = kind;
		Points = points;
		Keys = keys;
		Reserved = reserved;
	}

	public uint Kind { get; }
	public IReadOnlyList<NVector3> Points { get; }
	/// <summary>The u16 at +6, not read by the sampler (0 in the corpus).</summary>
	public ushort Keys { get; }
	/// <summary>The u32 at +12, not read by the sampler (0 in the corpus).</summary>
	public uint Reserved { get; }

	public bool IsClosed => (Kind & ClosedFlag) != 0;
	public bool IsBezier => (Kind & BezierFlag) != 0;
	public bool IsLinear => !IsBezier && (Kind & LinearFlag) != 0;

	/// <summary>Segments a path position spans: point count (÷ 3 for Bézier), minus 1 when open.</summary>
	public int SegmentCount => (IsBezier ? Points.Count / 3 : Points.Count) - (IsClosed ? 0 : 1);

	/// <summary>
	/// Position (in the path dummy's space) at <paramref name="percent"/> along the path, and the unit direction the
	/// node faces with flag 0x400 (null when it has no length). False when the path has no segment or when the float
	/// polynomial does not keep the position finite (see the guard below).
	/// </summary>
	public bool TryEvaluate( float percent, out NVector3 position, out NVector3? direction )
	{
		position = default;
		direction = null;
		var segments = SegmentCount;
		if ( segments <= 0 || Points.Count == 0 || !float.IsFinite( percent ) )
			return false;
		// fmod in double (0x67B24A), stored as a float; N · f · 0.01f in extended precision, truncated by ftol.
		var wrapped = (float)((percent + 1000.0) % 100.0);
		var scaled = (double)segments * wrapped * (double)0.01f;
		var segment = (int)scaled;
		var fraction = (float)(scaled - segment);
		var start = IsBezier ? segment * 3 + 1 : segment;
		position = Point( start, fraction );
		if ( !float.IsFinite( position.X ) || !float.IsFinite( position.Y ) || !float.IsFinite( position.Z ) )
		{
			// OpenTPW fail-closed guard, not original behaviour: a finite percent on finite points can still overflow
			// the single-precision Horner form (control points within one factor of the float range), so refuse
			// instead of returning a NaN/∞ position. The sampler then leaves the node at its sampled or rest
			// translation (see ModelAnimationPlayer.LocalTransform).
			position = default;
			direction = null;
			return false;
		}
		var tangent = IsBezier ? BezierDerivative( start, fraction )
			: IsLinear ? At( start + 1 ) - At( start )
			: Point( start, fraction + 0.1f );
		var length = tangent.Length();
		if ( length > 0 && float.IsFinite( length ) )
			direction = tangent / length;
		return true;
	}

	/// <summary>
	/// The node's rotation rows when it faces <paramref name="direction"/> (0x470780): row 2 is the direction, row 0
	/// <c>normalize(up × direction)</c> and row 1 <c>direction × row 0</c>; <paramref name="up"/> is +Y unless the
	/// record also has rotation keys, which supply their row 1. When up and direction are parallel the original divides
	/// by zero; as an OpenTPW guard row 0 is then +X (or +Z for a direction along X) made perpendicular to the direction.
	/// Before a record's first rotation key OpenTPW passes the rest (or lower-channel) rotation's row 1, where the original
	/// reads the node's current row 1, which can be the previous tick's facing row; no corpus clip is known to reach that case.
	/// </summary>
	public static System.Numerics.Matrix4x4 Facing( NVector3 direction, NVector3 up )
	{
		var cross = NVector3.Cross( up, direction );
		if ( !(cross.LengthSquared() > 1e-12f) )
		{
			var axis = MathF.Abs( direction.X ) < 0.9f ? NVector3.UnitX : NVector3.UnitZ;
			cross = axis - direction * NVector3.Dot( direction, axis );
		}
		var right = NVector3.Normalize( cross );
		var newUp = NVector3.Cross( direction, right );
		return new System.Numerics.Matrix4x4(
			right.X, right.Y, right.Z, 0,
			newUp.X, newUp.Y, newUp.Z, 0,
			direction.X, direction.Y, direction.Z, 0,
			0, 0, 0, 1 );
	}

	private NVector3 At( int index ) => Points[(int)((uint)(index + Points.Count) % (uint)Points.Count)];

	private NVector3 Point( int start, float t )
	{
		// The evaluators move whole segments of t into the index (ftol) and keep the remainder.
		var whole = (int)t;
		t -= whole;
		if ( IsBezier )
		{
			var i = start + whole * 3;
			var (p0, p1, p2, p3) = (At( i - 1 ), At( i ), At( i + 1 ), At( i + 2 ));
			return p0 + t * (3 * (p1 - p0) + t * (3 * (p0 + p2) - 6 * p1 + t * (p3 - p0 + 3 * (p1 - p2))));
		}
		if ( IsLinear )
		{
			// Points i and i + 1 without wrapping in the original; a closed linear path (not in the corpus) would read
			// past its table there, so this wraps instead.
			var i = start + whole;
			return At( i ) * (1 - t) + At( i + 1 ) * t;
		}
		var j = start + whole;
		var (a, b, c, d) = (At( j - 1 ), At( j ), At( j + 1 ), At( j + 2 ));
		return 0.5f * (2 * b + t * ((c - a) + t * ((2 * a - 5 * b + 4 * c - d) + t * (d - a + 3 * (b - c)))));
	}

	private NVector3 BezierDerivative( int start, float t )
	{
		var whole = (int)t;
		t -= whole;
		var i = start + whole * 3;
		var (p0, p1, p2, p3) = (At( i - 1 ), At( i ), At( i + 1 ), At( i + 2 ));
		return 3 * (p1 - p0) + t * (2 * (3 * (p0 + p2) - 6 * p1) + t * 3 * (p3 - p0 + 3 * (p1 - p2)));
	}
}
