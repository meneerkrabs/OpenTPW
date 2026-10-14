using System.Numerics;
using V3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>A boat's presentation pose: its height and pitch and roll in model units and radians (derived each frame, never saved).</summary>
public readonly record struct BoatPose( float Height, float Pitch, float Roll );

/// <summary>
/// The surface a boat floats on (docs/TRACKS.md): the water faces of a track piece in tile-local model units (one cell is 10, a tile 20, z up), and the
/// four-probe height of a boat above a surface function. The original samples four points at ± the vehicle radius along the facing and across it, takes the
/// highest surface triangle under each point (else the terrain plus the object's base height), and uses the mean as the height, the front and back difference
/// as the pitch and the right and left difference as the roll (<c>0x546280</c>).
/// </summary>
public sealed class TrackSurface
{
	/// <summary>One surface triangle in tile-local model units (x, y on the ground, z up).</summary>
	public readonly record struct Triangle( V3 A, V3 B, V3 C );

	/// <summary>The water height of the station tiles (the water plane of the track pieces), in model units above the tile's ground.</summary>
	public const float StationWaterHeight = 3.254f;

	private readonly Triangle[] triangles;

	public TrackSurface( IEnumerable<Triangle> triangles ) => this.triangles = triangles.ToArray();

	public int TriangleCount => triangles.Length;

	/// <summary>
	/// The surface faces of a piece mesh: every face of a mesh part whose material is water (<c>wr_water…</c>, <c>wr_whirl</c>), baked with the rest transforms.
	/// </summary>
	// [APPROX:TRACK-001] the surface faces are the pieces' water meshes; the original takes the faces whose first u16 has bit 0x8000 (0x00546280), which OpenTPW's mesh reader does not expose — evidence needed: the face flag word of the MD2 faces and the surface object lookup
	public static TrackSurface FromModel( ModelFile model )
	{
		var rest = ModelAnimationPlayer.ComputeRestTransforms( model );
		var found = new List<Triangle>();
		foreach ( var mesh in model.Meshes )
		{
			if ( mesh.Indices.Length < 3 || mesh.Materials.Length == 0 )
				continue;
			var node = mesh.NodeIndex >= 0 && mesh.NodeIndex < model.Nodes.Count ? mesh.NodeIndex : model.RootNodeIndex;
			var bake = node >= 0 ? rest[node] : Matrix4x4.Identity;
			V3 Position( uint index ) => ObjectAssets.ConvertAxes( V3.Transform( mesh.Vertices[(int)index].Position.GetSystemVector3(), bake ) );
			for ( var corner = 0; corner + 2 < mesh.Indices.Length; corner += 3 )
			{
				var material = mesh.Vertices[(int)mesh.Indices[corner]].TextureIndex;
				if ( material >= mesh.Materials.Length || !IsWater( mesh.Materials[(int)material].Name ) )
					continue;
				found.Add( new Triangle( Position( mesh.Indices[corner] ), Position( mesh.Indices[corner + 1] ), Position( mesh.Indices[corner + 2] ) ) );
			}
		}
		return new TrackSurface( found );
	}

	private static bool IsWater( string name ) => name.Contains( "water", StringComparison.OrdinalIgnoreCase ) || name.Contains( "whirl", StringComparison.OrdinalIgnoreCase );

	/// <summary>The highest surface height under the tile-local point (<paramref name="x"/>, <paramref name="y"/>), or null when no triangle covers it.</summary>
	public float? HeightAt( float x, float y )
	{
		float? best = null;
		foreach ( var triangle in triangles )
		{
			var (a, b, c) = (triangle.A, triangle.B, triangle.C);
			var denominator = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
			if ( MathF.Abs( denominator ) < 1e-9f )
				continue;
			var u = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / denominator;
			var v = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / denominator;
			var w = 1 - u - v;
			const float edge = -1e-4f;
			if ( u < edge || v < edge || w < edge )
				continue;
			var z = u * a.Z + v * b.Z + w * c.Z;
			if ( best == null || z > best )
				best = z;
		}
		return best;
	}

	/// <summary>
	/// A boat's pose from four probes: ± <paramref name="radius"/> along its facing (<paramref name="heading"/>, 512ths of a turn clockwise from +Y) and across it,
	/// each read through <paramref name="heightAt"/> (model units, absolute). The height is the mean, the pitch the rise from back to front over the probe
	/// distance 2R, the roll the rise from left to right.
	/// </summary>
	public static BoatPose Pose( float x, float y, int heading, float radius, Func<float, float, float> heightAt )
	{
		var angle = heading * 2 * MathF.PI / BumpMath.Turn;
		var (forwardX, forwardY) = (MathF.Sin( angle ), MathF.Cos( angle ));
		var (rightX, rightY) = (MathF.Cos( angle ), -MathF.Sin( angle ));
		var front = heightAt( x + forwardX * radius, y + forwardY * radius );
		var back = heightAt( x - forwardX * radius, y - forwardY * radius );
		var right = heightAt( x + rightX * radius, y + rightY * radius );
		var left = heightAt( x - rightX * radius, y - rightY * radius );
		return new BoatPose( (front + back + right + left) / 4, MathF.Atan2( front - back, 2 * radius ), MathF.Atan2( right - left, 2 * radius ) );
	}
}
