using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// The tables of a bend car (<c>0x438ab0</c>): for every sample around the car the yaw and pitch of the track there relative to the car's own heading, and the elevation the car's mesh rises by,
/// indexed from the sample <see cref="Rear"/> entries before the car's own.
/// </summary>
public sealed class CoasterBendTables
{
	/// <summary>The capacity of the original's tables (0x80 entries each).</summary>
	public const int Capacity = 128;

	public float[] Yaw { get; } = new float[Capacity];
	public float[] Pitch { get; } = new float[Capacity];
	public float[] Elevation { get; } = new float[Capacity];
	/// <summary>The number of samples behind the car the tables cover: the index of the car's own entry (<c>*param_8</c> of 0x438ab0).</summary>
	public int Rear { get; internal set; }
	/// <summary>The entries written.</summary>
	public int Count { get; internal set; }
}

/// <summary>
/// The bend of a car whose type has <c>bBendCar</c> (<c>0x438800</c>, <c>0x438ab0</c>): a long car follows the curve of the track under it. Each vertex of its mesh, at its local Z along the car, takes the
/// yaw and pitch of the track at that distance relative to the car's heading, and rises by <c>fBendElevationLow</c> (<c>fBendElevationHigh</c> on a sample with flag 8, the chain lift).
/// </summary>
public static class CoasterCarBend
{
	// [BIN:TPW-EXE:0x43e480 yaw lerp] the shortest-arc lerp of two angles: (1 − f)·a + f·b when they are at most π apart (|a − b| against the single 0x6fe120), else the way over ±π: t = 2π − |a − b| (the single 0x6fe124), angle = (1 − f)·t + b for a negative a and t·f + a otherwise, wrapped to [−π, π) by fmod(angle + π, 2π) − π (the doubles 0x6fe128 and 0x6fe130, the single 0x6fe120)
	// [BIN:TPW-EXE:0x43e480 single steps] the thread's x87 rounds every add, subtract and multiply to a single (the 24-bit control word), so each step here is one single operation; the wrap's two additions are exact in double and round once, fmod is exact
	private static float AngleLerp( float a, float b, float f )
	{
		var distance = MathF.Abs( a - b );
		if ( distance <= 3.1415927f )
			return (1f - f) * a + b * f;
		var t = 6.2831855f - distance;
		var angle = a < 0f ? (1f - f) * t + b : t * f + a;
		var shifted = (float)((double)angle + 3.1415927410125732);
		return (float)(((double)shifted % 6.2831854820251465) - 3.1415927410125732);
	}

	/// <summary>acos clamped as the original clamps its dot product: below 0 it is 0 (a quarter turn at most), above 1 it is 1.</summary>
	// [BIN:TPW-EXE:0x438e43 dot clamp] the dot product of the two unit directions is set to 0 when it is below 0.0 and to 1.0 when it is above 1.0 before the acos (0x67b4e0 = atan2(sqrt((1 + x)(1 − x)), x)), so an angle never exceeds a quarter turn
	// [BIN:TPW-EXE:0x67b4e0 acos steps] the routine runs at the thread's 24-bit significand (the control word 0x7F is not the CRT's 0x27F, so the helper 0x686185 only restores round-to-nearest): 1 + x, 1 − x, their product and its square root each round to a single, and the fpatan result is stored as a single
	// [EXT:deterministic-math] fpatan in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
	private static float Angle( float dot )
	{
		if ( !(0f <= dot) )
			dot = 0f;
		if ( 1f < dot )
			dot = 1f;
		return (float)DetMath.Atan2( MathF.Sqrt( (1f + dot) * (1f - dot) ), dot );
	}

	/// <summary>
	/// The bend tables of a car at <paramref name="coordinate"/> on the sample list (<c>0x438ab0</c>): the samples a car's rear and front extents reach (extent over the spacing, plus one), each as the
	/// interpolated point at the car's own fraction, its yaw and pitch against the car's direction and its elevation.
	/// </summary>
	/// <param name="samples">The controller's samples; the point after the last one is the last itself.</param>
	/// <param name="rearExtent">The car type's rear extent (type <c>+0x28</c>).</param>
	/// <param name="frontExtent">The car type's front extent (type <c>+0x24</c>).</param>
	/// <param name="forward">The car's direction (car <c>+0x10..+0x18</c>, the third row of its matrix).</param>
	// [BIN:TPW-EXE:0x438ab0 bend tables] with f the fraction and i the whole part of the coordinate: rear = trunc(rear extent / spacing) + 1 entries before the car's, clamped to i, front = trunc(front extent / spacing) + 1 clamped to n − 1 − i, the missing ones zero; per sample k the point k + f (lerp of samples k and k + 1) relative to the car's point (lerp of i and i + 1), its horizontal angle against the car's heading (acos of the dot of the unit directions, negative when the cross is positive) and the angle of its (y, z) turned by its own yaw against the car's pitch frame (turned by the sample yaw lerp); the elevation is Low or High (flag 8) lerped by f; samples behind use the negated car direction
	public static CoasterBendTables Tables( IReadOnlyList<CoasterSample> samples, float spacing, float elevationLow, float elevationHigh, float rearExtent, float frontExtent, NVector3 forward, float coordinate )
	{
		var tables = new CoasterBendTables();
		var count = samples.Count;
		if ( count < 1 || !(spacing > 0) || !float.IsFinite( coordinate ) || coordinate < 0 )
			return tables;
		CoasterSample Sample( int index ) => samples[Math.Clamp( index, 0, count - 1 )];
		var whole = (int)MathF.Truncate( coordinate );
		var f = coordinate - MathF.Truncate( coordinate );
		var last = count - 1;
		var rear = (int)(rearExtent / spacing) + 1;
		var behind = Math.Min( rear, whole );
		var first = whole - behind;
		var wantAhead = (int)(frontExtent / spacing) + 1;
		var ahead = Math.Max( 0, Math.Min( wantAhead, last - whole ) );
		var g = 1f - f;
		var a = Sample( whole );
		var b = Sample( whole + 1 );
		var point = new NVector3( b.Position.X * f + a.Position.X * g, b.Position.Y * f + a.Position.Y * g, b.Position.Z * f + a.Position.Z * g );
		var yaw = AngleLerp( a.Yaw, b.Yaw, f );
		// the car's heading: the horizontal part of its direction, and its (y, z) after turning by the sample yaw
		var h = MathF.Sqrt( forward.X * forward.X + forward.Z * forward.Z );
		// a car pointing straight up or down has no heading (the original divides by zero): its tables stay empty and the mesh is not bent
		if ( !(h > 1e-6f) )
			return tables;
		var carX = forward.X / h;
		var carZ = forward.Z / h;
		var turned = CoasterTrack.TurnY( forward, -yaw );
		var carQ = MathF.Sqrt( forward.Y * forward.Y + turned.Z * turned.Z );
		if ( !(carQ > 1e-6f) )
			return tables;
		var carY = forward.Y / carQ;
		var carPitchZ = turned.Z / carQ;
		float Elevation( int k ) =>
			((Sample( k ).Flags & CoasterSampleFlags.Friction) != 0 ? elevationHigh : elevationLow) * g
			+ ((Sample( k + 1 ).Flags & CoasterSampleFlags.Friction) != 0 ? elevationHigh : elevationLow) * f;
		var at = 0;
		void Write( float yawDelta, float pitchDelta, float elevation )
		{
			if ( at >= CoasterBendTables.Capacity )
				return;
			tables.Yaw[at] = yawDelta;
			tables.Pitch[at] = pitchDelta;
			tables.Elevation[at] = elevation;
			at++;
		}
		// one sample's yaw and pitch against the car (sign tests: behind negate on a positive cross, in front a positive cross for the yaw and a negative one for the pitch)
		(float Yaw, float Pitch) Measure( int k, float x, float y, float z, bool behindCar )
		{
			var s = Sample( k );
			var n = Sample( k + 1 );
			var dx = n.Position.X * f + s.Position.X * g - point.X;
			var dy = n.Position.Y * f + s.Position.Y * g - point.Y;
			var dz = n.Position.Z * f + s.Position.Z * g - point.Z;
			var horizontal = MathF.Sqrt( dx * dx + dz * dz );
			// a point straight above or below the car's (or on it) has no heading: the original divides by zero there, here the sample adds no bend
			if ( !(horizontal > 1e-6f) )
				return (0f, 0f);
			var dxn = dx / horizontal;
			var dzn = dz / horizontal;
			// [EXT:deterministic-math] fpatan in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
			var sampleYaw = DetMathF.Atan2( dx, dz );
			var rotated = CoasterTrack.TurnY( new NVector3( dx, dy, dz ), -sampleYaw );
			var vertical = MathF.Sqrt( rotated.Z * rotated.Z + dy * dy );
			if ( !(vertical > 1e-6f) )
				return (0f, 0f);
			var sy = dy / vertical;
			var sz = rotated.Z / vertical;
			var yawDelta = Angle( x * dxn + z * dzn );
			if ( x * dzn - z * dxn > 0f )
				yawDelta = -yawDelta;
			var pitchDelta = Angle( y * sy + carPitchZ * sz );
			var cross = y * sz - carPitchZ * sy;
			if ( behindCar ? cross > 0f : cross < 0f )
				pitchDelta = -pitchDelta;
			return (yawDelta, pitchDelta);
		}
		for ( var index = 0; index < rear - behind; index++ )
			Write( 0, 0, 0 );
		for ( var k = first; k < whole && behind != 0; k++ )
		{
			var (yawDelta, pitchDelta) = Measure( k, -carX, -carY, -carZ, behindCar: true );
			Write( yawDelta, pitchDelta, Elevation( k ) );
		}
		Write( 0, 0, Elevation( whole ) );
		for ( var k = whole + 1; k <= whole + ahead; k++ )
		{
			var (yawDelta, pitchDelta) = Measure( k, carX, carY, carZ, behindCar: false );
			Write( yawDelta, pitchDelta, Elevation( k ) );
		}
		for ( var index = 0; index < wantAhead - ahead; index++ )
			Write( 0, 0, 0 );
		tables.Rear = rear;
		tables.Count = at;
		return tables;
	}

	/// <summary>
	/// A vertex bent along the tables (<c>0x438800</c>): its table position from its local Z (the rear count times the spacing plus Z, over the spacing), the elevation added to its height, then turned about X by
	/// the pitch and about Y by the yaw, each lerped between the two entries around it.
	/// </summary>
	// [BIN:TPW-EXE:0x438800 bend vertex] (local_60c + z) / spacing with local_60c = rear × spacing is split in the whole index and the fraction; y += elevation; the vector is turned about X by the pitch lerp (0x4314f0) and about Y by the yaw lerp (0x431570), all three coordinates stored (the model's z moves with the turn)
	public static NVector3 Bend( CoasterBendTables tables, float spacing, NVector3 vertex )
	{
		if ( tables.Count < 2 || !(spacing > 0) )
			return vertex;
		var position = (tables.Rear * spacing + vertex.Z) / spacing;
		if ( !float.IsFinite( position ) )
			return vertex;
		var whole = (int)MathF.Truncate( position );
		var f = position - MathF.Truncate( position );
		// the original has no bound: a vertex beyond the tables reads their neighbours, here it takes the end entries
		whole = Math.Clamp( whole, 0, tables.Count - 2 );
		if ( position < 0 )
			f = 0;
		float Lerp( float[] table ) => table[whole + 1] * f + table[whole] * (1f - f);
		var lifted = new NVector3( vertex.X, Lerp( tables.Elevation ) + vertex.Y, vertex.Z );
		return CoasterTrack.TurnY( CoasterTrack.TurnX( lifted, Lerp( tables.Pitch ) ), Lerp( tables.Yaw ) );
	}
}
