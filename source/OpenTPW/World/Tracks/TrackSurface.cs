using System.Numerics;
using V3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>A boat's presentation pose: its height and pitch and roll in model units and radians (derived each frame, never saved).</summary>
public readonly record struct BoatPose( float Height, float Pitch, float Roll );

/// <summary>A kart's presentation pose: its height, pitch and roll, and the height of the shadow on the ground under it (model units and radians; derived each frame).</summary>
public readonly record struct KartPose( float Height, float Pitch, float Roll, float ShadowHeight );

/// <summary>
/// The surface a vehicle runs on over an object (docs/TRACKS.md): what the original's height sampler reads on a track piece, a station or a bumper ride in
/// object-local model units (one cell is 10, a tile 20, z up), and the four-probe pose of a vehicle above a surface function. The original samples four points at
/// ± the vehicle radius along the facing and across it and uses the mean as the height, the front and back difference as the pitch and the right and left
/// difference as the roll (<c>0x546280</c>).
/// </summary>
public sealed class TrackSurface
{
	/// <summary>One surface triangle in tile-local model units (x, y on the ground, z up).</summary>
	public readonly record struct Triangle( V3 A, V3 B, V3 C );

	/// <summary>The water height of the station tiles (the water plane of the track pieces), in model units above the tile's ground.</summary>
	public const float StationWaterHeight = 3.254f;

	/// <summary>The hover height of the space and fantasy karts above the surface, in model units.</summary>
	public const float HoverHeight = 1.6f;

	private readonly Triangle[] triangles;
	/// <summary>The sampler's face groups in order (a surface dummy record's mesh each): their triangle range and whether the first covering face answers.</summary>
	private readonly (int Start, int Count, bool FirstHit)[]? groups;
	/// <summary>The terrain sampler's height above the ground (the model header's +0xA0), or null for a face surface.</summary>
	private readonly float? terrainOffset;
	/// <summary>Whether a point no face covers gives the instance's own height (0 above the base) instead of no surface (the +0x2C mesh sampler).</summary>
	private readonly bool baseWhenUncovered;

	public TrackSurface( IEnumerable<Triangle> triangles ) => this.triangles = triangles.ToArray();

	/// <summary>The model header's +0xA0 that every face sample adds.</summary>
	private readonly float faceOffset;

	private TrackSurface( Triangle[] triangles, (int Start, int Count, bool FirstHit)[] groups, bool baseWhenUncovered, float faceOffset )
	{
		this.triangles = triangles;
		this.groups = groups;
		this.baseWhenUncovered = baseWhenUncovered;
		this.faceOffset = faceOffset;
	}

	private TrackSurface( float terrainOffset )
	{
		triangles = Array.Empty<Triangle>();
		this.terrainOffset = terrainOffset;
	}

	/// <summary>The sampler mode the original takes for this surface (tests and the docs).</summary>
	public TrackSamplerMode Mode => terrainOffset != null ? TrackSamplerMode.Terrain : groups == null ? TrackSamplerMode.Faces : baseWhenUncovered ? TrackSamplerMode.InstanceMesh : TrackSamplerMode.SurfaceRecords;

	/// <summary>The model header's +0xA0 that every sampler mode adds (<c>0x4511FA</c>, <c>0x451272</c>).</summary>
	public float HeaderOffset => terrainOffset ?? faceOffset;

	/// <summary>The height above the ground of a terrain-sampled object, or null.</summary>
	public float? TerrainOffset => terrainOffset;

	/// <summary>
	/// The surface the original's height sampler reads on an object it finds under a probe (a track piece, a station, a bumper ride): <c>0x00450EA0</c> chooses
	/// the surface records (dummy records with type bit 2: their meshes' faces whose first word has bit 0x8000, the first covering face answering for a record with
	/// bit 4, else the highest), the terrain plus the header's +0xA0 (header flag 0x80 or a nonzero +0xA0) or the instance's collision mesh.
	/// </summary>
	// [BIN:TPW-EXE:0x00450EA0 sampler mode] mode 2 when the instance's +0x28 block has bit 8 (0x00450F20 gathers the meshes of the dummy records with bit 2), else mode 1 when the model flags have 0x80 or the header float +0xA0 is nonzero, else mode 4 (the instance's +0x2C mesh)
	// [BIN:TPW-EXE:0x004511A0 sample] mode 1: the terrain sampler 0x004527F0 (the landscape's corner heights over the cell's two triangles, not the .hmp) + model +0xA0; mode 2: 0x00451290(x, z, 0, 1); mode 4: 0x00451290(x, z, 1, 0); every mode adds +0xA0, and a NaN +0xA0 is reset to 0
	// [BIN:TPW-EXE:0x00451290 face sampler] mode 4 samples the +0x2C mesh (first covering face; none: the instance height +0x44), mode 2 walks the dummy records with bit 2 (mesh = the header's first dummy mesh +0x46 plus the record index) and takes the highest covering face, returning at once for a record with bit 4; −100 when nothing covers
	// [BIN:TPW-EXE:0x00451390 mesh faces] only faces whose first u16 has bit 0x8000 count; the height is the face plane's at the point (the normal at the low 15 bits, turned by the instance's 0/90/180/270 rotation)
	// [BIN:TPW-EXE:0x0046A5A0 mode-4 mesh] the instance mesh sampler (mode 4, +0x2C) reads the faces of the ground node the type loader chose through 0x469A80 (the node with the most ground vertices on the footprint's boundary; its flags 0x200 skip a node, 0x20000 count nothing, the header's 0x100 counts every ground vertex; the call's bits 2 and 4 are 0): only objects without a surface record, header flag 0x80 or +0xA0 reach it
	/// <param name="model">The object's model.</param>
	/// <param name="drapeLoaded">The model was loaded with the drape load flag (a kart or water track piece: <c>0x4629D0</c> sets header bit 0x80 on a model it loads draped), so the sampler takes the terrain mode when the model has no surface record.</param>
	// [BIN:TPW-EXE:0x0044A870 surface block] the loader's last step sets bit 8 of the instance's +0x28 block exactly when a dummy record has type bit 2, which is the test 0x450EA0 makes for mode 2: the surface-record rule here is the original's
	// [BIN:TPW-EXE:0x004629D0 drape header bit] a node with 0x20000 (every mesh node of a piece loaded with load flag 0x40, set by 0x461F10) sets header +0x30 bit 0x80, so every kart and water piece without a surface record is sampled in mode 1 (terrain at the probe + header +0xA0), even where +0xA0 is 0
	public static TrackSurface FromObject( ModelFile model, bool drapeLoaded = false, int groundNode = -1 )
	{
		ArgumentNullException.ThrowIfNull( model );
		var records = new List<(int Mesh, bool FirstHit)>();
		for ( var index = 0; index < model.DummyAttributes.Count; index++ )
		{
			var type = model.DummyAttributes[index].Type;
			var mesh = model.Counts.Unknown46 + index;
			if ( (type & 2) != 0 && mesh < model.Meshes.Count )
				records.Add( (mesh, (type & 4) != 0) );
		}
		if ( records.Count == 0 && (drapeLoaded || (model.HeaderFlags & 0x80) != 0 || model.HeaderHeight != 0 && !float.IsNaN( model.HeaderHeight )) )
			return new TrackSurface( Offset( model ) );
		var found = new List<Triangle>();
		var groups = new List<(int Start, int Count, bool FirstHit)>();
		void Add( int meshIndex, bool firstHit )
		{
			var mesh = model.Meshes[meshIndex];
			var node = mesh.NodeIndex >= 0 && mesh.NodeIndex < model.Nodes.Count ? mesh.NodeIndex : model.RootNodeIndex;
			var offset = NodeOffset( model, node );
			V3 Position( uint index ) => ObjectAssets.ConvertAxes( mesh.Vertices[(int)index].Position.GetSystemVector3() + offset );
			var start = found.Count;
			for ( var face = 0; face < mesh.FaceNormalIndices.Length && face * 3 + 2 < mesh.Indices.Length; face++ )
			{
				if ( (mesh.FaceNormalIndices[face] & 0x8000) == 0 )
					continue;
				found.Add( new Triangle( Position( mesh.Indices[face * 3] ), Position( mesh.Indices[face * 3 + 1] ), Position( mesh.Indices[face * 3 + 2] ) ) );
			}
			groups.Add( (start, found.Count - start, firstHit) );
		}
		if ( records.Count > 0 )
		{
			foreach ( var (mesh, firstHit) in records )
				Add( mesh, firstHit );
			return new TrackSurface( found.ToArray(), groups.ToArray(), baseWhenUncovered: false, Offset( model ) );
		}
		// [BIN:TPW-EXE:0x0046A5A0 ground node] the instance's +0x2C is the node 0x469A80 counted the most ground vertices on (ObjectHoardingGeometry.SelectGroundNode, the type loader's search; nothing when the type is loaded without the base flag or the level has no terrain): only that node's faces are the mode-4 mesh
		if ( groundNode >= 0 && groundNode < model.Nodes.Count && model.Nodes[groundNode].MeshIndex is >= 0 and var groundMesh && groundMesh < model.Meshes.Count )
			Add( groundMesh, true );
		// the collision mesh answers with its first covering face, and with the instance's own height where none covers
		return new TrackSurface( found.ToArray(), new[] { (0, found.Count, true) }, baseWhenUncovered: true, Offset( model ) );
	}

	/// <summary>
	/// The position the instance gathering adds to a mesh's vertices: the node's own position plus those of its ancestors that have a parent themselves (the root's is left out), the
	/// translations only, in the original's order of addition.
	/// </summary>
	// [BIN:TPW-EXE:0x00450F20 instance faces] each vertex is the mesh's local vertex plus the node's position (+0x40/+0x44/+0x48) and, walking up while the ancestor itself has a parent (0x451001..0x45103F), that ancestor's position; the node's rotation and scale do not enter (no shipped surface mesh has any: the full rest transform gave the same vertices to 6e-6)
	private static V3 NodeOffset( ModelFile model, int node )
	{
		if ( node < 0 || node >= model.Nodes.Count || model.Nodes[node].ParentIndex < 0 )
			return V3.Zero;
		var own = model.Nodes[node].Transform;
		var (x, y, z) = (own.M41, own.M42, own.M43);
		for ( var parent = model.Nodes[node].ParentIndex; parent >= 0 && model.Nodes[parent].ParentIndex >= 0; parent = model.Nodes[parent].ParentIndex )
		{
			var transform = model.Nodes[parent].Transform;
			x = transform.M41 + x;
			y = transform.M42 + y;
			z = z + transform.M43;
		}
		return new V3( x, y, z );
	}

	/// <summary>
	/// The surface of a draped instance: every face vertex of a face surface moved up by <paramref name="lift"/> at its tile-local (x, y), as the drape moves the instance's
	/// vertices that the face sampler then reads (0x450F20 reads the instance's own vertex array). A terrain-mode surface has no faces and is returned as it is.
	/// </summary>
	// [BIN:TPW-EXE:0x00450F20 instance faces] modes 2 and 4 gather the vertices of the instance's own mesh copies (node +0x60 of the instance, plus the node translations), which 0x469A80 has already draped
	public TrackSurface Draped( Func<float, float, float> lift )
	{
		ArgumentNullException.ThrowIfNull( lift );
		if ( terrainOffset != null || triangles.Length == 0 )
			return this;
		V3 Move( V3 vertex ) => new( vertex.X, vertex.Y, vertex.Z + lift( vertex.X, vertex.Y ) );
		var moved = triangles.Select( triangle => new Triangle( Move( triangle.A ), Move( triangle.B ), Move( triangle.C ) ) ).ToArray();
		return groups == null ? new TrackSurface( moved ) : new TrackSurface( moved, groups, baseWhenUncovered, faceOffset );
	}

	private static float Offset( ModelFile model ) => float.IsNaN( model.HeaderHeight ) ? 0 : model.HeaderHeight;

	public int TriangleCount => triangles.Length;

	/// <summary>The highest surface height under the tile-local point (<paramref name="x"/>, <paramref name="y"/>), or null when no triangle covers it.</summary>
	public float? HeightAt( float x, float y )
	{
		if ( terrainOffset is { } offset )
			return offset;
		if ( groups != null )
		{
			float? best = null;
			foreach ( var (start, count, firstHit) in groups )
			{
				float? height = null;
				for ( var index = start; index < start + count; index++ )
				{
					if ( Cover( triangles[index], x, y, oneSided: true ) is not { } z )
						continue;
					if ( height == null || z > height )
						height = z;
					if ( firstHit )
						break;
				}
				if ( height is { } found && (best == null || found > best) )
				{
					if ( firstHit )
						return found + faceOffset;
					best = found;
				}
			}
			return best is { } top ? top + faceOffset : baseWhenUncovered ? faceOffset : null;
		}
		float? highest = null;
		foreach ( var triangle in triangles )
		{
			if ( Cover( triangle, x, y ) is { } z && (highest == null || z > highest) )
				highest = z;
		}
		return highest;
	}

	/// <summary>The height of <paramref name="triangle"/>'s plane at the point, or null when the triangle does not cover it.</summary>
	private static float? Cover( Triangle triangle, float x, float y, bool oneSided = false )
	{
		var (a, b, c) = (triangle.A, triangle.B, triangle.C);
		// Native object faces use directed edges in X/Z, mapped here to X/Y by ConvertAxes.
		// The plain triangle-list constructor remains a two-sided presentation helper.
		if ( oneSided && (!InsideDirectedEdge( c, a, x, y ) || !InsideDirectedEdge( b, c, x, y ) || !InsideDirectedEdge( a, b, x, y )) )
			return null;
		var denominator = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
		if ( MathF.Abs( denominator ) < 1e-9f )
			return null;
		var u = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / denominator;
		var v = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / denominator;
		var w = 1 - u - v;
		const float edge = -1e-4f;
		if ( u < edge || v < edge || w < edge )
			return null;
		return u * a.Z + v * b.Z + w * c.Z;
	}

	// [BIN:TPW-EXE:0x004514CF directed face edges] for the edges B → A of a face taken in the order V2 → V0, V1 → V2, V0 → V1 (0x4514C7..0x45153A) the value is ((dz × px − (dz × Bx + dx × Bz)) + dx × pz) with dz = Az − Bz and dx = Bx − Ax (0x4514CF..0x45151D), compared with 0.0 (0x6FE3DC at 0x45151F): below it the point is outside. The thread runs at the control word 0x7F (24-bit mantissa, COAST-032, X87Precision), so each x87 result is rounded to a single, which is what these float operations do in this order for values in the normal range (the x87 keeps its extended exponent, so a result that would be a subnormal single differs; park coordinates never produce one); the tests emulate the instruction sequence with the x87 type and compare
	internal static bool InsideDirectedEdge( V3 previous, V3 next, float x, float y )
	{
		var dx = previous.X - next.X;
		var dy = next.Y - previous.Y;
		var origin = dy * previous.X + dx * previous.Y;
		return (dy * x - origin) + dx * y >= 0;
	}

	/// <summary>
	/// A boat's pose from four probes: ± <paramref name="radius"/> along its facing (<paramref name="heading"/>, 512ths of a turn clockwise from +Y) and across it,
	/// each read through <paramref name="heightAt"/> (model units, absolute). The height is the mean, the pitch the rise from back to front over the probe
	/// distance 2R, the roll the rise from left to right.
	/// </summary>
	public static BoatPose Pose( float x, float y, int heading, float radius, Func<float, float, float> heightAt )
	{
		var angle = heading * 2 * MathF.PI / BumpMath.Turn;
		var (forwardX, forwardY) = (MathF.Sin( angle ), MathF.Cos( angle )); // [DET:presentation] the renderer's boat and kart pose
		var (rightX, rightY) = (MathF.Cos( angle ), -MathF.Sin( angle )); // [DET:presentation] the renderer's boat and kart pose
		var front = heightAt( x + forwardX * radius, y + forwardY * radius );
		var back = heightAt( x - forwardX * radius, y - forwardY * radius );
		var right = heightAt( x + rightX * radius, y + rightY * radius );
		var left = heightAt( x - rightX * radius, y - rightY * radius );
		return new BoatPose( (front + back + right + left) / 4, MathF.Atan2( front - back, 2 * radius ), MathF.Atan2( right - left, 2 * radius ) ); // [DET:presentation] the renderer's boat and kart pose
	}

	/// <summary>
	/// A kart's pose from the four probes of the original: ± <paramref name="radius"/> along the kart's drawn facing (<paramref name="heading"/>, 512ths of a turn
	/// clockwise from +Y) and across it. The height is the mean of the four, the pitch the rise from back to front over 2R and the roll the rise from left to right.
	/// A hovering kart (space and fantasy) reads the front and back probes only for its height, has no roll of its own, floats <see cref="HoverHeight"/> above the
	/// surface with a sine bob of ±0.43 units (period 512 / 6 ticks) and banks into turns by yaw / 13 × π/2; its shadow lies on the mean of all four probes.
	/// </summary>
	// [BIN:TPW-EXE:0x00546280 kart pose] probes at (sin h, cos h) × R and (cos h, −sin h) × R (R = vehicle radius / 307.2); pitch = atan2(2R, front − back) style difference over 2R, roll likewise for right and left; flag 0x800 uses (front + back) / 2 and no roll, flag 0x400 adds 1.6 + sine[age × 6] × 0.0016667 (the 256-scaled table), flag 0x1000 adds (yaw rate / 13) × π/2 to the roll, the shadow (0x2000) stands on the four-probe mean
	// [BIN:TPW-EXE:0x005469C3 kart bank] 0x5469C3..0x5469ED: roll += (yaw + α·Δyaw) / 13 × π/2 with no clamp before the roll − π/2 at 0x546A3D; the original's roll is the negative of this pose's slope roll (roll = −atan((right − left) / 2R) + bank), so the bank's sign relative to the slope roll is the original's and the absolute roll sign is OpenTPW's rendering convention
	// [EXT:kart-roll-convention] the absolute sign of the roll is a rendering convention: this pose has roll = atan((right − left) / 2R) − yaw / 13 × π/2, the negative of the original's roll
	public static KartPose KartPose( float x, float y, int heading, float radius, bool hover, bool bank, int age, int yawRate, Func<float, float, float> heightAt )
	{
		var angle = heading * 2 * MathF.PI / BumpMath.Turn;
		var (forwardX, forwardY) = (MathF.Sin( angle ), MathF.Cos( angle )); // [DET:presentation] the renderer's boat and kart pose
		var (rightX, rightY) = (MathF.Cos( angle ), -MathF.Sin( angle )); // [DET:presentation] the renderer's boat and kart pose
		var front = heightAt( x + forwardX * radius, y + forwardY * radius );
		var back = heightAt( x - forwardX * radius, y - forwardY * radius );
		var right = heightAt( x + rightX * radius, y + rightY * radius );
		var left = heightAt( x - rightX * radius, y - rightY * radius );
		var mean = (front + back + right + left) / 4;
		var pitch = MathF.Atan2( front - back, 2 * radius ); // [DET:presentation] the renderer's boat and kart pose
		var roll = hover ? 0f : MathF.Atan2( right - left, 2 * radius ); // [DET:presentation] the renderer's boat and kart pose
		var height = hover ? (front + back) / 2 + HoverHeight + BumpMath.Sin( age * 6 ) * 0.0016666667f : mean;
		if ( bank )
			roll += -yawRate / 13f * MathF.PI / 2;
		return new KartPose( height, pitch, roll, mean );
	}
}

/// <summary>The height sampler the original takes on an object (<c>0x00450EA0</c>), and OpenTPW's former surfaces.</summary>
public enum TrackSamplerMode
{
	/// <summary>Mode 1: the terrain plus the model header's +0xA0.</summary>
	Terrain,
	/// <summary>Mode 2: the meshes of the dummy records with bit 2, their 0x8000 faces.</summary>
	SurfaceRecords,
	/// <summary>Mode 4: the instance's collision mesh (+0x2C), the instance height where no face covers.</summary>
	InstanceMesh,
	/// <summary>A plain list of triangles (tests).</summary>
	Faces
}
