using System.Numerics;
using V3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>A boat's presentation pose: its height and pitch and roll in model units and radians (derived each frame, never saved).</summary>
public readonly record struct BoatPose( float Height, float Pitch, float Roll );

/// <summary>A kart's presentation pose: its height, pitch and roll, and the height of the shadow on the ground under it (model units and radians; derived each frame).</summary>
public readonly record struct KartPose( float Height, float Pitch, float Roll, float ShadowHeight );

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

	/// <summary>Model units one step of a height profile byte stands for (the kart floor 7 or 8 steps, the walls 14, a bridge deck 20 to 26).</summary>
	public const float ProfileStep = 0.4f;
	/// <summary>Model units per profile sample: 5 samples to a 10-unit cell.</summary>
	public const float ProfileSample = 2f;
	/// <summary>The hover height of the space and fantasy karts above the surface, in model units.</summary>
	public const float HoverHeight = 1.6f;

	private readonly Triangle[] triangles;
	private readonly byte[]? profile;
	private readonly int profileWidth;
	private readonly int profileHeight;

	public TrackSurface( IEnumerable<Triangle> triangles ) => this.triangles = triangles.ToArray();

	private TrackSurface( byte[] profile, int width, int height )
	{
		triangles = Array.Empty<Triangle>();
		this.profile = profile;
		profileWidth = width;
		profileHeight = height;
	}

	public int TriangleCount => profile == null ? triangles.Length : profile.Count( sample => sample != 0 );

	/// <summary>
	/// The surface of a kart piece from its height profile (<c>.hmp</c>): each sample is 2 model units wide (row = tile-local y, column = tile-local x) and stands
	/// 0.4 units per step high; a zero sample is outside the piece and has no surface.
	/// </summary>
	// [APPROX:TRACK-001] the kart surface is the piece's .hmp profile, which matches the mesh's top surface sample for sample (floor 7–8 steps = 2.8–3.2 units, walls 14, deck 20–26) in every checked piece; the original's surface is the mesh faces with bit 0x8000 (0x00546280, 0x004511A0), which the mesh reader does not expose
	public static TrackSurface FromProfile( HmpFile file )
	{
		ArgumentNullException.ThrowIfNull( file );
		return new TrackSurface( file.Profile.ToArray(), file.ProfileWidth, file.ProfileHeight );
	}

	/// <summary>The profile surface of raw samples (row-major, <paramref name="width"/> columns), for tests.</summary>
	public static TrackSurface FromSamples( byte[] samples, int width, int height ) => new( samples, width, height );

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
		if ( profile != null )
		{
			var column = (int)MathF.Floor( x / ProfileSample );
			var row = (int)MathF.Floor( y / ProfileSample );
			if ( column < 0 || row < 0 || column >= profileWidth || row >= profileHeight )
				return null;
			var sample = profile[row * profileWidth + column];
			return sample == 0 ? null : sample * ProfileStep;
		}
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

	/// <summary>
	/// A kart's pose from the four probes of the original: ± <paramref name="radius"/> along the kart's drawn facing (<paramref name="heading"/>, 512ths of a turn
	/// clockwise from +Y) and across it. The height is the mean of the four, the pitch the rise from back to front over 2R and the roll the rise from left to right.
	/// A hovering kart (space and fantasy) reads the front and back probes only for its height, has no roll of its own, floats <see cref="HoverHeight"/> above the
	/// surface with a sine bob of ±0.43 units (period 512 / 6 ticks) and banks into turns by yaw / 13 × π/2; its shadow lies on the mean of all four probes.
	/// </summary>
	// [BIN:TPW-EXE:0x00546280 kart pose] probes at (sin h, cos h) × R and (cos h, −sin h) × R (R = vehicle radius / 307.2); pitch = atan2(2R, front − back) style difference over 2R, roll likewise for right and left; flag 0x800 uses (front + back) / 2 and no roll, flag 0x400 adds 1.6 + sine[age × 6] × 0.0016667 (the 256-scaled table), flag 0x1000 adds (yaw rate / 13) × π/2 to the roll, the shadow (0x2000) stands on the four-probe mean
	// [APPROX:TRACK-024] the bank's sign (into the turn) and a clamp of ±0.6 radians are OpenTPW's: the original's unclamped term can exceed a quarter turn on a hard yaw
	public static KartPose KartPose( float x, float y, int heading, float radius, bool hover, bool bank, int age, int yawRate, Func<float, float, float> heightAt )
	{
		var angle = heading * 2 * MathF.PI / BumpMath.Turn;
		var (forwardX, forwardY) = (MathF.Sin( angle ), MathF.Cos( angle ));
		var (rightX, rightY) = (MathF.Cos( angle ), -MathF.Sin( angle ));
		var front = heightAt( x + forwardX * radius, y + forwardY * radius );
		var back = heightAt( x - forwardX * radius, y - forwardY * radius );
		var right = heightAt( x + rightX * radius, y + rightY * radius );
		var left = heightAt( x - rightX * radius, y - rightY * radius );
		var mean = (front + back + right + left) / 4;
		var pitch = MathF.Atan2( front - back, 2 * radius );
		var roll = hover ? 0f : MathF.Atan2( right - left, 2 * radius );
		var height = hover ? (front + back) / 2 + HoverHeight + BumpMath.Sin( age * 6 ) * 0.0016666667f : mean;
		if ( bank )
			roll += Math.Clamp( -yawRate / 13f * MathF.PI / 2, -0.6f, 0.6f );
		return new KartPose( height, pitch, roll, mean );
	}
}
