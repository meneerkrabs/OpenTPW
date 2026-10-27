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
	private readonly float scale;
	private readonly bool centred;

	/// <summary>The field of view of the span selection's rays in degrees: 90, the value of <c>[0x87AF58]</c> in the park camera (<c>0x42A990</c> to <c>0x578BE0</c>), whose half-height tangent is 1. OpenTPW's camera shows a half height of 0.5, so the rays reach twice as far to each side as the screen does.</summary>
	public const float FieldOfView = 90f;

	/// <param name="field">The heightfield.</param>
	/// <param name="engineUnitsPerOriginal">The engine units of one original unit: the park's <see cref="OriginalParkPlacement.ModelScale"/>, 1 in the lobby.</param>
	/// <param name="centred">Whether the engine space is the park's, centred on the field (<see cref="OriginalParkPlacement.GetOrigin"/>); the lobby's is the field's own space.</param>
	public TerrainViewSpans( ModelHeightfield field, float engineUnitsPerOriginal = OriginalParkPlacement.ModelScale, bool centred = true )
	{
		this.field = field;
		scale = engineUnitsPerOriginal;
		this.centred = centred;
		// [BIN:TPW-EXE:0x0056E6C0 landscape header] the span selection's lowest and highest height are the heightfield header's +0x20 and +0x24 (0x56B830 copies the raw floats into the landscape object's +0x30 and +0x34), the stored bracket and not the corners' own minimum and maximum
		lowHeight = field.StoredLowerHeight;
		highHeight = field.StoredUpperHeight;
	}

	public TerrainVisibleSpans Spans { get; } = new();

	/// <summary>The lowest and highest height the span selection is given: the heightfield header's stored bracket, as stored.</summary>
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
		if ( !(aspectWord > 0) || !TryGetPose( view, lookAlongNegativeZ, out var position, out var right, out var up, out var forward ) )
			return false;
		Span<float> camera = [position.X, position.Y, position.Z];
		Span<float> matrix = [right.X, right.Y, right.Z, up.X, up.Y, up.Z, forward.X, forward.Y, forward.Z];
		return Spans.Compute( camera, matrix, lowHeight, highHeight, RayFieldOfView, aspectWord, ViewDistance, 0, 0, field.CellCountX, field.CellCountZ, field.CellSizeX, field.CellSizeZ );
	}

	/// <summary>The field of view of the span selection's rays in degrees: <see cref="FieldOfView"/> in the park, the lobby's island field of view there.</summary>
	public float RayFieldOfView { get; set; } = FieldOfView;

	/// <summary>
	/// The camera's position and axes in the heightfield's own space (x, height, z) from the matrix <paramref name="view"/> (engine space of the mesh to the camera), in original units: the engine space of the mesh undone
	/// (<see cref="OriginalParkPlacement.ToEngine"/> in the park, the axes swapped only in the lobby).
	/// </summary>
	private bool TryGetPose( in SysMatrix view, bool lookAlongNegativeZ, out SysVector3 position, out SysVector3 right, out SysVector3 up, out SysVector3 forward )
	{
		position = right = up = forward = default;
		if ( !SysMatrix.Invert( view, out var world ) )
			return false;
		right = SysVector3.Normalize( SysVector3.TransformNormal( SysVector3.UnitX, world ) );
		up = SysVector3.Normalize( SysVector3.TransformNormal( SysVector3.UnitY, world ) );
		forward = SysVector3.Normalize( SysVector3.TransformNormal( lookAlongNegativeZ ? -SysVector3.UnitZ : SysVector3.UnitZ, world ) );
		var eye = world.Translation;
		var origin = centred ? OriginalParkPlacement.GetOrigin( field ) : System.Numerics.Vector2.Zero;
		// engine (x, y, z) is model (x, z, y) scaled and centred (OriginalParkPlacement.ToEngine); directions only swap the axes
		static SysVector3 Axis( SysVector3 v ) => new( v.X, v.Z, v.Y );
		right = Axis( right );
		up = Axis( up );
		forward = Axis( forward );
		position = new SysVector3( (eye.X + origin.X) / scale, eye.Z / scale, (eye.Y + origin.Y) / scale );
		return true;
	}

	/// <summary>
	/// The far argument the original's eye views give the projection setup (<see cref="TerrainEyeViewFar"/>) for the camera of <paramref name="view"/>, in original units, or null when the pose is unusable. <paramref name="aspectWord"/> is the display's
	/// height over its width and <paramref name="sine"/> and <paramref name="cosine"/> those of half the projection's field of view.
	/// </summary>
	public float? EyeViewFar( in SysMatrix view, float aspectWord, float sine, float cosine, bool lookAlongNegativeZ = true )
	{
		if ( !(aspectWord > 0) || !TryGetPose( view, lookAlongNegativeZ, out var position, out var right, out var up, out var forward ) )
			return null;
		var field = new TerrainEyeViewFar.Field( this.field.CellCountX, this.field.CellCountZ, this.field.CellSizeX, this.field.CellSizeZ, 0f, 0f, lowHeight, highHeight );
		return TerrainEyeViewFar.Compute( field, new TerrainEyeViewFar.View( position, right, up, forward, aspectWord, sine, cosine ), ViewDistance );
	}

	/// <summary>The view axis the projection puts in front of the camera: -Z when a point one unit along -Z has a positive w.</summary>
	public static bool LooksAlongNegativeZ( in SysMatrix projection ) => SysVector4.Transform( new SysVector4( 0, 0, -1, 1 ), projection ).W > 0;
}
