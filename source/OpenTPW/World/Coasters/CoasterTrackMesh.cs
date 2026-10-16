using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>One vertex of the track mesh in original world units (Y up): position, normal, the cross-section U, the along-track V and the track texture index.</summary>
public readonly record struct CoasterMeshVertex( SVector3 Position, SVector3 Normal, float U, float V, int Texture );

/// <summary>
/// The finished (or partly built) track mesh: the coaster's cross section extruded along the curve slices. Every slice turns the section's
/// points by the slice frame (yaw, pitch, then roll) and moves them to the slice position as one ring of vertices, which the strips before and
/// after it share; every edge of the section is a strip of two triangles between two neighbouring rings, wound as the original winds them, whose U
/// comes from the points and whose V grows by the slice distance divided by six. A ring is emitted again where the section changes.
/// </summary>
// [BIN:TPW-EXE:0x43fd60 slice vertices] each cross-section point (X, Y) is turned by the slice roll, then pitch, then yaw (table trigonometry) and added to the slice position; its normal (Nx, Ny) is turned the same way and normalised; U is the point's U (point +8) and V the running track coordinate (controller +0x74)
// [BIN:TPW-EXE:0x43f4e0 slice strips] for every slice after the first, 0x43f4e0 adds the distance to the previous slice times 1/6 to the running V (the constant -0.16666667 subtracted), emits the slice's ring (one vertex per section point, 0x43fd60) and two triangles per cross-section edge (texture, from, to) between the previous ring's vertices (Pf, Pt) and the new ring's (Cf, Ct): (Pf, Cf, Pt) and (Ct, Pt, Cf) when the parity flag 0x7923e4 is clear, (Pf, Cf, Ct) and (Ct, Pt, Pf) when it is set, the flag toggling after every edge for the whole rebuild
// [BIN:TPW-EXE:0x43f5e2 ring re-emission] when the slice's cross-section run changes (the run pointer advances), when the emitter switches between the run lists (normal, winch 0x7923f0 or the marker run 0x7925e8) and when the dynamic model's chunk is flushed, the previous slice's ring is emitted again with the new section first, so the strips of a section never join another section's ring
// [BIN:TPW-EXE:0x43f150 mesh chunks] the original fills a dynamic model in chunks and takes the fractional part of V whenever a chunk is flushed, so the texture repeats every six world units along the track
// [BIN:TPW-EXE:0x43f4e0 marker links] a link whose node has crossings (+0xb0 nonzero) or node flag 0x1000000, or whose next node has 0x800000 or 0x2000 (the build cursor's spare node), is drawn with the marker run: the copy of the invalid cross section (definition +0x80) with every edge's texture set to the coaster's texture count − 1 + v, v = 2 (red.tga) for crossings or a next node with 0x4000, 3 (blue.tga) for a next node with 0x800000/0x2000 alone, 0x7925d0 (always 0) for a node with 0x1000000 alone
// [APPROX:COAST-026] vertices are shared within each texture (a point an edge of another texture also uses gets its own vertex, the shader takes one texture per vertex) where the original's ring is one vertex per point for every texture; the strips leaving the marker run join a ring emitted again, where the original continues from the marker ring
public sealed class CoasterTrackMesh
{
	/// <summary>World units of track per texture repeat along V.</summary>
	public const float MetersPerRepeat = 6f;
	/// <summary>The marker textures the loader appends after the coaster's own textures (<c>0x42f390</c>, table <c>0x74cde0</c>): <c>m_erase.tga</c>, <c>red.tga</c>, <c>blue.tga</c>.</summary>
	// [BIN:TPW-EXE:0x42f390 marker textures] the track model's texture list is the coaster's asTextureData entries followed by the three entries of 0x74cde0 (count 0x74ce10 = 3): m_erase.tga, red.tga and blue.tga, each with texture flags 7 (self-illuminated)
	public static readonly string[] MarkerTextureNames = { "m_erase", "red", "blue" };
	/// <summary>The marker variant of a crossing or refused link (texture count − 1 + 2: <c>red.tga</c>).</summary>
	public const int RefusedMarker = 2;
	/// <summary>The marker variant of the link to the build cursor (texture count − 1 + 3: <c>blue.tga</c>).</summary>
	public const int CursorMarker = 3;

	/// <summary>The texture index of a marker variant on a coaster with <paramref name="textureCount"/> textures of its own.</summary>
	public static int MarkerTexture( int textureCount, int variant ) => textureCount - 1 + variant;

	public List<CoasterMeshVertex> Vertices { get; } = new();
	public List<uint> Indices { get; } = new();
	/// <summary>Slice pairs extruded (segments between consecutive slices).</summary>
	public int Segments { get; private set; }
	/// <summary>Rings emitted (one per slice, plus one again at every section change).</summary>
	public int Rings { get; private set; }
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
	/// Extrudes <paramref name="sections"/> along <paramref name="slices"/>. <paramref name="winch"/> holds the nodes (slice sections) whose links are chain lifts;
	/// <paramref name="marker"/> gives the marker variant of a pair (its node and the node its end knot belongs to: the same node inside a pylon), null for the
	/// normal section. A closed chain joins its last slice to the first.
	/// </summary>
	public static CoasterTrackMesh Build( CoasterCrossSections sections, IReadOnlyList<CoasterSlice> slices, bool closed, IReadOnlyCollection<int>? winch = null, Func<int, int, int?>? marker = null )
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
		var parity = false;
		(int Section, int Marker)? ringIdentity = null;
		var previous = new Dictionary<(int Point, int Texture), uint>();
		var current = new Dictionary<(int Point, int Texture), uint>();
		for ( var index = 0; index < segments; index++ )
		{
			var a = slices[index];
			var b = slices[(index + 1) % count];
			var length = SVector3.Distance( a.Position, b.Position );
			var vb = v + length / MetersPerRepeat;
			mesh.Length += length;
			var next = a.NextSection >= 0 ? a.NextSection : b.Section;
			var variant = marker?.Invoke( a.Section, next );
			var isWinch = winch != null && winch.Contains( a.Section );
			var section = variant != null ? sections.InvalidSectionIndex : sections.SelectSection( (a.Pitch + b.Pitch) * 0.5f, isWinch );
			var (points, edges) = sections.Resolve( section );
			var identity = (section, variant ?? -1);
			var (rightA, upA) = Frame( a.Yaw, a.Pitch, a.Roll );
			var (rightB, upB) = Frame( b.Yaw, b.Pitch, b.Roll );
			if ( ringIdentity != identity )
			{
				// a new section (or run list) starts on the previous slice's ring emitted again
				previous.Clear();
				mesh.Rings++;
				ringIdentity = identity;
			}
			current.Clear();
			mesh.Rings++;
			uint Ring( Dictionary<(int, int), uint> ring, int point, int texture, SVector3 origin, SVector3 right, SVector3 up, float vertexV )
			{
				if ( ring.TryGetValue( (point, texture), out var existing ) )
					return existing;
				var created = (uint)mesh.Vertices.Count;
				mesh.Vertices.Add( Vertex( points[point], origin, right, up, vertexV, texture ) );
				ring[(point, texture)] = created;
				return created;
			}
			foreach ( var edge in edges )
			{
				if ( edge.From < 0 || edge.To < 0 || edge.From >= points.Count || edge.To >= points.Count )
					continue;
				var texture = variant is int markerVariant ? MarkerTexture( textureCount, markerVariant ) : Math.Clamp( edge.Texture, 0, textureCount - 1 );
				var pf = Ring( previous, edge.From, texture, a.Position, rightA, upA, v );
				var pt = Ring( previous, edge.To, texture, a.Position, rightA, upA, v );
				var cf = Ring( current, edge.From, texture, b.Position, rightB, upB, vb );
				var ct = Ring( current, edge.To, texture, b.Position, rightB, upB, vb );
				if ( !parity )
					mesh.Indices.AddRange( new[] { pf, cf, pt, ct, pt, cf } );
				else
					mesh.Indices.AddRange( new[] { pf, cf, ct, ct, pt, pf } );
				parity = !parity;
			}
			(previous, current) = (current, previous);
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
