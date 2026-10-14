using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>One vertex of the track mesh in original world units (Y up): position, normal, the cross-section U, the along-track V and the track texture index (<see cref="CoasterTrackMesh.InvalidTexture"/> for an invalid segment).</summary>
public readonly record struct CoasterMeshVertex( SVector3 Position, SVector3 Normal, float U, float V, int Texture );

/// <summary>
/// The finished (or partly built) track mesh: the coaster's cross section extruded along the curve slices. Every slice turns the section's
/// points by the slice frame (yaw, pitch, then roll) and moves them to the slice position; every edge of the section is a strip of quads
/// between two neighbouring slices whose U comes from the points and whose V grows by the slice distance divided by six.
/// </summary>
// [BIN:TPW-EXE:0x43fd60 slice vertices] each cross-section point (X, Y) is turned by the slice roll, then pitch, then yaw (table trigonometry) and added to the slice position; its normal (Nx, Ny) is turned the same way and normalised; U is the point's U (point +8) and V the running track coordinate (controller +0x74)
// [BIN:TPW-EXE:0x43f4e0 slice strips] for every slice after the first, 0x43f4e0 adds the distance to the previous slice times 1/6 to the running V (the constant -0.16666667 subtracted) and emits two triangles per cross-section edge between the previous and the new slice's vertices, the winding alternating with a parity flag
// [BIN:TPW-EXE:0x43f150 mesh chunks] the original fills a dynamic model in chunks and takes the fractional part of V whenever a chunk is flushed, so the texture repeats every six world units along the track
// [APPROX:COAST-026] the mesh reuses the original extrusion, but each segment emits its own vertices (no sharing across segments), draws both faces of every strip (the original culls by its edge winding) and places the cross section by the curve's own frame, not the second rail of 0x43e6b0 (COAST-017); the original's cross-section runs inside a knot pair (0x4402d0) are taken at segment granularity and its dry/wet blend sections are not drawn, and a segment of an invalid link shows a flat red texture where the original switches to uiInvalidCrossSection — evidence needed: the blend path of 0x43fd60 (param_9/param_10) run on a real flume track and the invalid cross section's consumers
public sealed class CoasterTrackMesh
{
	/// <summary>The texture index of a segment drawn as invalid (the renderer binds a flat red texture).</summary>
	public const int InvalidTexture = -1;
	/// <summary>World units of track per texture repeat along V.</summary>
	public const float MetersPerRepeat = 6f;

	public List<CoasterMeshVertex> Vertices { get; } = new();
	public List<uint> Indices { get; } = new();
	/// <summary>Slice pairs extruded (segments between consecutive slices).</summary>
	public int Segments { get; private set; }
	/// <summary>Track length in world units.</summary>
	public float Length { get; private set; }
	public int TriangleCount => Indices.Count / 3;

	/// <summary>
	/// The right and up axes of a slice (original axes, Y up): the right axis (cos yaw, 0, −sin yaw) and the up axis (sin yaw·sin pitch, cos pitch,
	/// cos yaw·sin pitch) of the yaw/pitch frame, turned by the roll in their plane (a point (1, 0) goes to right·cos + up·sin). The forward axis is
	/// (sin yaw·cos pitch, −sin pitch, cos yaw·cos pitch), as in <see cref="HermiteCoasterCurve.Orientation"/>.
	/// </summary>
	public static (SVector3 Right, SVector3 Up) Frame( float yaw, float pitch, float roll )
	{
		var (sy, cy) = MathF.SinCos( yaw );
		var (sp, cp) = MathF.SinCos( pitch );
		var right = new SVector3( cy, 0, -sy );
		var up = new SVector3( sy * sp, cp, cy * sp );
		var (sr, cr) = MathF.SinCos( roll );
		return (right * cr + up * sr, up * cr - right * sr);
	}

	/// <summary>
	/// Extrudes <paramref name="sections"/> along <paramref name="slices"/>. <paramref name="winch"/> holds the pairs (slice sections) that are chain lifts and
	/// <paramref name="invalid"/> the pairs drawn as invalid; a closed chain joins its last slice to the first.
	/// </summary>
	public static CoasterTrackMesh Build( CoasterCrossSections sections, IReadOnlyList<CoasterSlice> slices, bool closed, IReadOnlyCollection<int>? winch = null, IReadOnlyCollection<int>? invalid = null )
	{
		ArgumentNullException.ThrowIfNull( sections );
		ArgumentNullException.ThrowIfNull( slices );
		var mesh = new CoasterTrackMesh();
		if ( sections.IsEmpty || slices.Count < 2 )
			return mesh;
		var count = slices.Count;
		var segments = closed ? count : count - 1;
		var textureCount = Math.Max( 1, sections.Textures.Count );
		var v = 0f;
		for ( var index = 0; index < segments; index++ )
		{
			var a = slices[index];
			var b = slices[(index + 1) % count];
			var length = SVector3.Distance( a.Position, b.Position );
			var vb = v + length / MetersPerRepeat;
			mesh.Length += length;
			var isWinch = winch != null && winch.Contains( a.Section );
			var isInvalid = invalid != null && invalid.Contains( a.Section );
			var section = sections.SelectSection( (a.Pitch + b.Pitch) * 0.5f, isWinch );
			var (points, edges) = sections.Resolve( section );
			var (rightA, upA) = Frame( a.Yaw, a.Pitch, a.Roll );
			var (rightB, upB) = Frame( b.Yaw, b.Pitch, b.Roll );
			foreach ( var edge in edges )
			{
				if ( edge.From < 0 || edge.To < 0 || edge.From >= points.Count || edge.To >= points.Count )
					continue;
				var texture = isInvalid ? InvalidTexture : Math.Clamp( edge.Texture, 0, textureCount - 1 );
				var first = (uint)mesh.Vertices.Count;
				mesh.Vertices.Add( Vertex( points[edge.From], a.Position, rightA, upA, v, texture ) );
				mesh.Vertices.Add( Vertex( points[edge.To], a.Position, rightA, upA, v, texture ) );
				mesh.Vertices.Add( Vertex( points[edge.To], b.Position, rightB, upB, vb, texture ) );
				mesh.Vertices.Add( Vertex( points[edge.From], b.Position, rightB, upB, vb, texture ) );
				mesh.Indices.Add( first );
				mesh.Indices.Add( first + 1 );
				mesh.Indices.Add( first + 2 );
				mesh.Indices.Add( first );
				mesh.Indices.Add( first + 2 );
				mesh.Indices.Add( first + 3 );
			}
			mesh.Segments++;
			v = vb;
		}
		return mesh;
	}

	private static CoasterMeshVertex Vertex( CoasterSectionPoint point, SVector3 origin, SVector3 right, SVector3 up, float v, int texture )
	{
		var normal = right * point.Nx + up * point.Ny;
		normal = normal.LengthSquared() > 1e-8f ? SVector3.Normalize( normal ) : up;
		return new CoasterMeshVertex( origin + right * point.X + up * point.Y, normal, point.U, v, texture );
	}
}
