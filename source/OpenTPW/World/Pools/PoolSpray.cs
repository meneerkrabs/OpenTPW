using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>One droplet of a water jet's spray: where it is (engine space), how large and how opaque.</summary>
public readonly record struct PoolDroplet( SVector3 Position, float Size, float Alpha );

/// <summary>
/// The spray of a water jet or splash cannon (docs/POOLS.md, "Splash cannon and water jets"), a pure function of the time: every droplet flies a parabola from the nozzle over the water in front of
/// the jet and falls to the water level, then starts again, each with its own range, sideways spread and phase from a fixed integer hash. Presentation only: nothing here is read by the simulation.
/// </summary>
// [EXT:pools-attractions] OpenTPW's own spray; the original game has no pools
public static class PoolSpray
{
	/// <summary>Droplets in the air per jet.</summary>
	public const int Droplets = 36;
	/// <summary>Seconds a droplet takes from the nozzle to the water.</summary>
	public const float FlightSeconds = 0.9f;
	/// <summary>The most a droplet travels from the nozzle along the jet's direction, in engine units (about 1.5 cells).</summary>
	public const float Reach = 3.0f;
	/// <summary>The height of the arc's top above the nozzle, in engine units.</summary>
	public const float Arc = 1.4f;
	/// <summary>The sideways spread at the end of a flight, in engine units either side.</summary>
	public const float Spread = 0.55f;

	private static uint Hash( int droplet, int salt )
	{
		var x = (uint)(droplet * 0x9E3779B1u + salt * 0x85EBCA6Bu);
		x ^= x >> 15;
		x *= 0x2C1B3C6Du;
		x ^= x >> 12;
		x *= 0x297A2D39u;
		x ^= x >> 15;
		return x;
	}

	private static float Unit( int droplet, int salt ) => (Hash( droplet, salt ) & 0xFFFF) / 65535f;

	/// <summary>
	/// The droplets of a jet at a time. <paramref name="nozzle"/> is where the water leaves (engine units), <paramref name="direction"/> the unit vector on the ground it is thrown along, and
	/// <paramref name="waterLevel"/> the height the droplets land at.
	/// </summary>
	public static IEnumerable<PoolDroplet> At( SVector3 nozzle, SVector2 direction, float waterLevel, double seconds )
	{
		var side = new SVector2( -direction.Y, direction.X );
		for ( var i = 0; i < Droplets; i++ )
		{
			var phase = (float)((seconds / FlightSeconds + (double)i / Droplets) % 1.0);
			var range = Reach * (0.45f + 0.55f * Unit( i, 1 ));
			var lateral = (Unit( i, 2 ) - 0.5f) * 2f * Spread;
			var top = Arc * (0.6f + 0.4f * Unit( i, 3 ));
			var along = range * phase;
			var height = nozzle.Z + (waterLevel - nozzle.Z) * phase * phase + top * 4f * phase * (1f - phase);
			var ground = new SVector2( nozzle.X, nozzle.Y ) + direction * along + side * (lateral * phase);
			var fade = phase < 0.85f ? 1f : (1f - phase) / 0.15f;
			yield return new PoolDroplet( new SVector3( ground.X, ground.Y, height ), 0.05f + 0.05f * Unit( i, 4 ), 0.85f * fade );
		}
	}
}

/// <summary>A jet the spray renderer draws: where the water leaves, which way, and the water level it falls to.</summary>
public readonly record struct PoolSpraySource( SVector3 Nozzle, SVector2 Direction, float WaterLevel );

/// <summary>
/// Draws the spray of the park's water jets (<see cref="PoolSpray"/>): small white-blue camera-facing quads through the particle shader, alpha blended. Presentation only; GPU code, so only the droplet
/// function is unit tested (the screenshots in reports/ show the result).
/// </summary>
// [EXT:pools-attractions] OpenTPW's own spray; the original game has no pools
public sealed class PoolSprayRenderer : Entity
{
	private const int VerticesPerQuad = 6;
	private readonly Func<IEnumerable<PoolSpraySource>> sources;
	private readonly Func<double> seconds;
	private readonly Vertex[] vertices = new Vertex[PoolSpray.Droplets * 64 * VerticesPerQuad];
	private Texture? white;
	private Material<ObjectUniformBuffer>? material;
	private DeviceBuffer? vertexBuffer;
	private bool deleted;

	/// <summary>Droplets drawn in the last frame (smoke instrumentation); the buffer holds the sprays of 64 jets, any more are not drawn.</summary>
	public int DrawnDroplets { get; private set; }

	public PoolSprayRenderer( Func<IEnumerable<PoolSpraySource>> sources, Func<double> seconds )
	{
		this.sources = sources ?? throw new ArgumentNullException( nameof( sources ) );
		this.seconds = seconds ?? throw new ArgumentNullException( nameof( seconds ) );
		Name = "Pool spray";
		white = new Texture( Enumerable.Repeat( (byte)255, 2 * 2 * 4 ).ToArray(), 2, 2 );
		material = new Material<ObjectUniformBuffer>( "content/shaders/particle.shader", MaterialFlags.DisableDepthWrite | MaterialFlags.DoubleSided );
		material.Set( "Color", white );
	}

	protected override void OnRender()
	{
		DrawnDroplets = 0;
		if ( deleted || material == null )
			return;
		var view = Camera.ViewMatrix;
		var right = new SVector3( view.M11, view.M21, view.M31 );
		var up = new SVector3( view.M12, view.M22, view.M32 );
		var count = 0;
		var time = seconds();
		foreach ( var source in sources() )
			foreach ( var droplet in PoolSpray.At( source.Nozzle, source.Direction, source.WaterLevel, time ) )
			{
				if ( count + VerticesPerQuad > vertices.Length )
					break;
				var half = droplet.Size;
				var alpha = (int)(droplet.Alpha * 255);
				// a pale blue droplet: red and green 205, blue 255, with the alpha in the blue slot (the particle shader's colour packing)
				var tint = new SVector3( 205, 232, 255 + 256 * alpha );
				Vertex Corner( float x, float y ) => new( droplet.Position + right * (x * half) + up * (y * half), tint, new SVector2( 0.5f, 0.5f ) );
				vertices[count++] = Corner( -1, 1 ); vertices[count++] = Corner( 1, 1 ); vertices[count++] = Corner( -1, -1 );
				vertices[count++] = Corner( 1, 1 ); vertices[count++] = Corner( 1, -1 ); vertices[count++] = Corner( -1, -1 );
				DrawnDroplets++;
			}
		if ( count == 0 )
			return;
		var stride = Vertex.SizeInBytes;
		vertexBuffer ??= Device.ResourceFactory.CreateBuffer( new BufferDescription( (uint)vertices.Length * stride, BufferUsage.VertexBuffer | BufferUsage.Dynamic ) );
		var commandList = global::Global.Render.CommandList;
		commandList.UpdateBuffer( vertexBuffer, 0, ref vertices[0], (uint)count * stride );
		commandList.SetVertexBuffer( 0, vertexBuffer );
		material.Set( "ObjectUniformBuffer", new ObjectUniformBuffer { g_mModel = System.Numerics.Matrix4x4.Identity, g_mView = Camera.ViewMatrix, g_mProj = Camera.ProjMatrix, g_vCameraPos = Camera.Position.GetSystemVector3(), g_flTime = Time.Now }.WithSpriteTint( Level.Current?.Look ?? EveningLook.Day ) ); // [EXT:evening] the droplets take the sprite tint at dusk and night
		commandList.SetPipeline( material.Pipeline );
		var resourceSets = material.GetResourceSets();
		for ( uint index = 0; index < resourceSets.Length; ++index )
			commandList.SetGraphicsResourceSet( index, resourceSets[index] );
		commandList.Draw( (uint)count, 1, 0, 0 );
	}

	protected override void OnDelete()
	{
		deleted = true;
		material?.ReleaseOwnedResources();
		material = null;
		white?.ReleaseOwned();
		white = null;
		var buffer = vertexBuffer;
		vertexBuffer = null;
		if ( buffer != null )
			global::Global.Render.ScheduleDelete( buffer.Dispose );
	}
}
