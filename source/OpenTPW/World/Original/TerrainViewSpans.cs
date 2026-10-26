namespace OpenTPW;

using SysMatrix = System.Numerics.Matrix4x4;
using SysVector3 = System.Numerics.Vector3;
using SysVector4 = System.Numerics.Vector4;

/// <summary>
/// The visible spans of a level's heightfield for the camera of the frame (<see cref="TerrainVisibleSpans"/>) with the values the original gives <c>0x56BB00</c>: the camera's pose in the field's own space,
/// the park camera's 90 degree rays, the heightfield header's stored lowest and highest height and the ray distance of FIRSTPERSONVIEWDISTANCE. The farthest corner distance it finds is the park view's far plane
/// (<c>0x578CA0</c>, docs/LND.md "Fog and far plane"), so a level computes it every frame whether or not its ground has procedural textures.
/// </summary>
internal sealed class TerrainViewSpans
{
	private readonly ModelHeightfield field;
	private readonly float lowHeight, highHeight;

	/// <summary>The field of view of the span selection's rays in degrees: 90, the value of <c>[0x87AF58]</c> in the park camera (<c>0x42A990</c> to <c>0x578BE0</c>), whose half-height tangent is 1. OpenTPW's camera shows a half height of 0.5, so the rays reach twice as far to each side as the screen does.</summary>
	public const float FieldOfView = 90f;

	public TerrainViewSpans( ModelHeightfield field )
	{
		this.field = field;
		// [BIN:TPW-EXE:0x0056E6C0 landscape header] the span selection's lowest and highest height are the heightfield header's +0x20 and +0x24 (0x56B830 reads them into the landscape object's +0x30 and +0x34), the stored bracket and not the corners' own minimum and maximum
		lowHeight = MathF.Truncate( field.StoredLowerHeight );
		highHeight = MathF.Truncate( field.StoredUpperHeight );
	}

	public TerrainVisibleSpans Spans { get; } = new();

	/// <summary>The lowest and highest height the span selection is given: the heightfield header's stored bracket, truncated to whole units.</summary>
	public (float Low, float High) Heights => (lowHeight, highHeight);

	/// <summary>
	/// The distance the original's rays reach in original units (<c>0x8BD4C0</c>, set by FIRSTPERSONVIEWDISTANCE through <c>0x44D8F0</c>: 200, 325, 450 or 700); a ray's ground point is at most half of it from the camera.
	/// </summary>
	public float ViewDistance { get; set; } = 700f;

	/// <summary>The farthest corner distance of the last update in original units, at least 10: the far plane of the park view.</summary>
	public float FarthestCorner => Spans.FarthestCorner;

	/// <summary>
	/// Computes the spans of the camera of <paramref name="view"/> (the matrix from the mesh's engine space to the camera): its position and axes in the heightfield's own space (the engine space of the mesh undone: x, height, z).
	/// <paramref name="aspectWord"/> is the display's height over its width; <paramref name="lookAlongNegativeZ"/> says which view axis points forward (the right handed look-at: -Z).
	/// </summary>
	public bool Update( in SysMatrix view, float aspectWord, bool lookAlongNegativeZ = true )
	{
		if ( !SysMatrix.Invert( view, out var world ) || !(aspectWord > 0) )
			return false;
		var right = SysVector3.Normalize( SysVector3.TransformNormal( SysVector3.UnitX, world ) );
		var up = SysVector3.Normalize( SysVector3.TransformNormal( SysVector3.UnitY, world ) );
		var forward = SysVector3.Normalize( SysVector3.TransformNormal( lookAlongNegativeZ ? -SysVector3.UnitZ : SysVector3.UnitZ, world ) );
		var eye = world.Translation;
		var origin = OriginalParkPlacement.GetOrigin( field );
		const float scale = OriginalParkPlacement.ModelScale;
		// engine (x, y, z) is model (x, z, y) scaled and centred (OriginalParkPlacement.ToEngine); directions only swap the axes
		static (float, float, float) Axis( SysVector3 v ) => (v.X, v.Z, v.Y);
		var (rx, ry, rz) = Axis( right );
		var (ux, uy, uz) = Axis( up );
		var (fx, fy, fz) = Axis( forward );
		Span<float> camera = [(eye.X + origin.X) / scale, eye.Z / scale, (eye.Y + origin.Y) / scale];
		Span<float> matrix = [rx, ry, rz, ux, uy, uz, fx, fy, fz];
		return Spans.Compute( camera, matrix, lowHeight, highHeight, FieldOfView, aspectWord, ViewDistance, 0, 0, field.CellCountX, field.CellCountZ, field.CellSizeX, field.CellSizeZ );
	}

	/// <summary>The view axis the projection puts in front of the camera: -Z when a point one unit along -Z has a positive w.</summary>
	public static bool LooksAlongNegativeZ( in SysMatrix projection ) => SysVector4.Transform( new SysVector4( 0, 0, -1, 1 ), projection ).W > 0;
}
