using System.Numerics;

namespace OpenTPW;

/// <summary>
/// The far argument the original's eye views (the terrain camcorder and the ride follow, <c>[0x790AB0] &amp; 0x16</c>) hand to the projection setup <c>0x578CA0</c> every frame, which also sets the weather's depth split
/// (<c>0x583220</c>): the greatest clip depth of the heightfield cell boxes the view reaches, at least 100. A port of the box search of <c>0x56EA90</c> with <c>0x56E7E0</c> and the box test <c>0x561BC0</c>; the heightfield rows
/// and columns it marks on the way are the span tables of the procedural ground and are not needed here.
/// </summary>
internal static class TerrainEyeViewFar
{
	/// <summary>The start of the far argument, [0x7014C0]: 100 (stored in <c>[this + 0x1C]</c> at <c>0x56EAD3</c>).</summary>
	public const float Floor = 100f;

	/// <summary>The far argument when the view's cell rectangle lies outside the field: the 1000 of the first <c>0x578CA0</c> call (<c>0x56EAB6</c>), which the early returns (<c>0x56EBAA</c> to <c>0x56EBC5</c>) leave in place.</summary>
	public const float WholeRange = 1000f;

	/// <summary>The viewing distance the rectangle takes when the fog bit 0x2000 is clear (<c>0x43E60000</c>, <c>0x56EAAE</c>); the eye views set the bit, so they use <c>[0x8BD4C0]</c>.</summary>
	public const float RectangleWithoutFog = 460f;

	private const float NearPlane = 0.1f;
	private const float MatrixFar = 1000f;

	// [BIN:TPW-EXE:0x561BC0 box test] the outcode of a transformed corner: the 7-bit index sW sZ (|w| < |z|) sY (|w| < |y|) sX (|w| < |x|), made of the sign bits and the compares of the magnitudes as integers (add reg, reg; adc), selects
	// a byte of the table at 0x879970 that the initialiser 0x561840 builds (this is the table that code leaves, read by running it offline): bit 0x8 x > w, 0x4 x < -w, 0x20 y > w, 0x10 y < -w, 0x2 z > w, 0x1 z < 0 for a positive w, and a negative w sets the others
	private static readonly byte[] Outcodes = Convert.FromHexString(
		"00080004202820240008000410181014" + "020a0206222a2226020a0206121a1216" + "01090105212921250109010511191115" + "01090105212921250109010511191115"
		+ "3e3a3e362e2a2e263e3a3e361e1a1e16" + "3e3a3e362e2a2e263e3a3e361e1a1e16" + "3f3b3f372f2b2f273f3b3f371f1b1f17" + "3d393d352d292d253d393d351d191d15" );

	/// <summary>The outcode of one clip vertex (the table lookup of <c>0x561BC0</c>).</summary>
	internal static byte Outcode( float x, float y, float z, float w )
	{
		static uint Sign( float value ) => (uint)BitConverter.SingleToInt32Bits( value ) >> 31;
		static uint Magnitude( float value ) => (uint)BitConverter.SingleToInt32Bits( value ) << 1;
		var magnitudeW = Magnitude( w );
		var index = (Sign( w ) << 6) | (Sign( z ) << 5) | (magnitudeW < Magnitude( z ) ? 1u << 4 : 0u) | (Sign( y ) << 3) | (magnitudeW < Magnitude( y ) ? 1u << 2 : 0u) | (Sign( x ) << 1) | (magnitudeW < Magnitude( x ) ? 1u : 0u);
		return Outcodes[index];
	}

	/// <summary>The heightfield a view searches: its cell counts and sizes, origin and the stored lowest and highest height.</summary>
	internal readonly record struct Field( int CellsX, int CellsZ, float SizeX, float SizeZ, float OriginX, float OriginZ, float Low, float High );

	/// <summary>The camera in the heightfield's own space (x, height, z): its position and axes, the display's height over its width (the aspect word 0x8BCBCC) and the sine and cosine of half the field of view.</summary>
	internal readonly record struct View( Vector3 Position, Vector3 Right, Vector3 Up, Vector3 Forward, float Aspect, float Sine, float Cosine );

	/// <summary>
	/// The far argument of the frame for <paramref name="viewDistance"/> (<c>[0x8BD4C0]</c>, FIRSTPERSONVIEWDISTANCE): <see cref="WholeRange"/> when the rectangle around the camera misses the field, else at least <see cref="Floor"/> and the
	/// greatest clip z of the corners of the boxes the view reaches; <paramref name="wholeField"/> searches the whole field (bit 0x20 of <c>[0x8BCBC8]</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x56EA90 eye view cells] with bit 0x20 of [0x8BCBC8] set the whole field (0, 0, cols, rows) is searched and the rectangle skipped (0x56EB03 to 0x56EB21; only a clear of the bit was found, 0x54E8D3, no setter, so the park views leave it clear and <c>wholeField</c> defaults to false); otherwise the rectangle is the camera x and z less and plus half the viewing distance (0x56EB2B to 0x56EBA4: (x - S / 2) x [this+0x10], (x + S / 2 + 0.999) x [this+0x10], the reciprocal cell sizes, each truncated by 0x67A830); it is dropped when it starts at or beyond the last column or row or ends before the first (0x56EBA8 to 0x56EBC5), else clamped to the field and handed to 0x56E7E0
	public static float Compute( in Field field, in View view, float viewDistance, bool wholeField = false )
	{
		if ( wholeField )
		{
			var whole = Floor;
			Visit( field, view, 0, 0, field.CellsX, field.CellsZ, ref whole );
			return whole;
		}
		var inverseX = 1f / field.SizeX;
		var inverseZ = 1f / field.SizeZ;
		var half = viewDistance * 0.5f;
		var x0 = (int)((view.Position.X - half) * inverseX);
		var x1 = (int)((view.Position.X + half - -0.999f) * inverseX);
		var z0 = (int)((view.Position.Z - half) * inverseZ);
		var z1 = (int)((view.Position.Z + half - -0.999f) * inverseZ);
		if ( x0 >= field.CellsX || z0 >= field.CellsZ || x1 < 0 || z1 < 0 )
			return WholeRange;
		x0 = Math.Max( x0, 0 );
		z0 = Math.Max( z0, 0 );
		x1 = Math.Min( x1, field.CellsX );
		z1 = Math.Min( z1, field.CellsZ );
		var far = Floor;
		Visit( field, view, x0, z0, x1, z1, ref far );
		return far;
	}

	// [BIN:TPW-EXE:0x56E7E0 cell boxes] the box over the cells x0..x1 and z0..z1 (cells times the sizes plus the origin) between the stored lowest and highest height has its eight corners transformed by the matrix [0x87B088] (checked separately against 0x578CA0 + 0x5783B0 + 0x579330 run offline) (clip coordinates of the projection built with far 1000, 0x56EAC0) and tested by 0x561BC0: every corner beyond one plane (the outcodes' AND) drops the box; a box that is wholly inside, or one cell, ends the search and raises the far argument to the greatest clip z of its corners (0x56EA60 to 0x56EA75); any other splits at the middle cell into up to four quarters (0x56E9B9 to 0x56EA22)
	private static void Visit( in Field field, in View view, int x0, int z0, int x1, int z1, ref float far )
	{
		Span<Vector4> corners = stackalloc Vector4[8];
		var index = 0;
		Span<int> columns = [x0, x1];
		Span<int> rows = [z0, z1];
		Span<float> heights = [field.Low, field.High];
		foreach ( var x in columns )
			foreach ( var z in rows )
				foreach ( var y in heights )
					corners[index++] = Clip( view, new Vector3( x * field.SizeX + field.OriginX, y, z * field.SizeZ + field.OriginZ ) );
		byte and = 0xFF, or = 0;
		foreach ( var corner in corners )
		{
			var code = Outcode( corner.X, corner.Y, corner.Z, corner.W );
			and &= code;
			or |= code;
		}
		if ( and != 0 )
			return;
		if ( or == 0 || (x1 - x0) * (z1 - z0) == 1 )
		{
			foreach ( var corner in corners )
				if ( far < corner.Z )
					far = corner.Z;
			return;
		}
		var xm = (x1 + x0) >> 1;
		var zm = (z1 + z0) >> 1;
		if ( xm > x0 )
		{
			if ( zm > z0 )
				Visit( field, view, x0, z0, xm, zm, ref far );
			if ( z1 > zm )
				Visit( field, view, x0, zm, xm, z1, ref far );
		}
		if ( x1 > xm )
		{
			if ( zm > z0 )
				Visit( field, view, xm, z0, x1, zm, ref far );
			if ( z1 > zm )
				Visit( field, view, xm, zm, x1, z1, ref far );
		}
	}

	/// <summary>
	/// A point of the heightfield's space in the clip space of the matrix [0x87B088]: w is sin(fov / 2) times the view depth, x and y are scaled by 2 cos(fov / 2) (x also by the aspect word, y down) and z is
	/// sin(fov / 2) / (1 - near / far) times the depth beyond the near plane, with near 0.1 and far 1000 (the projection <c>0x578CA0(1000)</c> built before <c>0x5783B0</c> composed the matrix).
	/// </summary>
	internal static Vector4 Clip( in View view, Vector3 point )
	{
		var delta = point - view.Position;
		var depth = Vector3.Dot( delta, view.Forward );
		var m10 = view.Sine / (1f - NearPlane / MatrixFar);
		return new Vector4( 2f * view.Aspect * view.Cosine * Vector3.Dot( delta, view.Right ), -2f * view.Cosine * Vector3.Dot( delta, view.Up ), m10 * (depth - NearPlane), view.Sine * depth );
	}
}
