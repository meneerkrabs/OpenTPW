using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Draws the park's weather around the camera: one camera-following drop field for the rain or snow
/// (<see cref="ParkWeather.CurrentDrops"/> of them, snow when <see cref="ParkWeather.Snow"/> holds) and a
/// jittered bolt for every strike the simulation reports, taken from <see cref="ParkWeather.TakeBoltTops"/>
/// at the strike the thunder countdown started from. The entity is created with the weather itself
/// (<see cref="ParkWeather.Created"/>); its motion runs on simulation time, so a paused park freezes the
/// drops and bolts mid-flight.
/// </summary>
// [APPROX:AUDIO-903] the visuals advance with the frame delta scaled by the HUD's simulation speed, so a paused park freezes the drops and the bolt mid-flight; the original's pause behaviour for the weather renderer is not traced
// [APPROX:AUDIO-904] the drop field and its vertex buffer are sized once for the cap; a browser session allocates no further per-frame GPU memory
public sealed class WeatherRenderer : Entity
{
	/// <summary>Length of a rain streak along its fall, in engine units.</summary>
	public const float RainStreakLength = 1.6f;
	/// <summary>Width of a rain streak, in engine units.</summary>
	public const float RainStreakWidth = 0.07f;
	/// <summary>Edge length of a snowflake quad, in engine units.</summary>
	public const float SnowFlakeSize = 0.24f;
	/// <summary>Width of the bolt's ribbon, in engine units.</summary>
	public const float BoltRibbonWidth = 0.45f;
	/// <summary>Alpha of a rain streak's head; the tail fades toward <see cref="RainTailAlpha"/>.</summary>
	public const float RainHeadAlpha = 0.45f;
	public const float RainTailAlpha = 0.10f;
	public const float SnowAlpha = 0.85f;
	/// <summary>Upper bound of one visual step's delta, so a hitch after a stall cannot teleport the field.</summary>
	public const float MaximumStepSeconds = 0.25f;

	// Both windings per quad, so every quad survives back-face culling whatever the clip-space convention.
	private const int VerticesPerQuad = 12;
	/// <summary>Worst-case vertex count: the drop cap plus every bolt flash, quads only, allocated once.</summary>
	private static readonly int MaximumVertices =
		(WeatherVisualState.MaximumDrops + WeatherVisualState.MaximumBoltFlashes * (WeatherVisualState.BoltPointCount - 1)) * VerticesPerQuad;

	private readonly ParkWeather weather;
	private readonly WeatherVisualState visual = new( VisualSeed );
	/// <summary>The presentation field's draw stream seed; it shares nothing with the park's simulation streams.</summary>
	public const ulong VisualSeed = 0x51EA7E5D5EEDUL;

	private Material<ObjectUniformBuffer>? material;
	private DeviceBuffer? vertexBuffer;
	private Vertex[] vertices = new Vertex[MaximumVertices];
	private int vertexCount;
	private bool deleted;

	/// <summary>Drops and bolt ribbons drawn in the last frame (smoke instrumentation).</summary>
	public int DrawnDrops { get; private set; }
	public int DrawnBolts { get; private set; }

	public WeatherRenderer( ParkWeather weather )
	{
		this.weather = weather;
		Name = "Weather";
		// Prepared with the entity — a resumed park creates it inside its load scope, a fresh park on its
		// first simulation step — so no gameplay frame pays the shader compile and pipeline build mid-rain.
		material = new Material<ObjectUniformBuffer>( "content/shaders/weather.shader", MaterialFlags.DisableDepthWrite );
	}

	protected override void OnUpdate()
	{
		var field = Level.OriginalPark?.Heightfield;
		foreach ( var top in weather.TakeBoltTops() )
		{
			var engineTop = field != null ? OriginalParkPlacement.ToEngine( field, top.GetSystemVector3() ) : top.GetSystemVector3();
			var ground = GroundUnder( field, top );
			visual.AddBoltFlash( engineTop, ground );
		}
		// Simulation-scaled time: a paused park (scale 0) freezes the drops and bolts where they are.
		var scale = Level.SimulationTimeScale;
		var delta = Math.Clamp( Time.Delta, 0f, MaximumStepSeconds ) * (float.IsFinite( scale ) && scale > 0f ? scale : 0f);
		visual.Step( delta, weather.CurrentDrops, weather.Snow, VolumeCentre() );
	}

	/// <summary>The engine point on the heightfield under a model-space bolt top; without a heightfield, the flat plane.</summary>
	private static SVector3 GroundUnder( ModelHeightfield? field, Vector3 top )
	{
		if ( field == null )
			return new SVector3( top.X, top.Y, 0 );
		var cellX = Math.Clamp( (int)MathF.Floor( top.X / field.CellSizeX ), 0, field.CellCountX - 1 );
		var cellY = Math.Clamp( (int)MathF.Floor( top.Z / field.CellSizeZ ), 0, field.CellCountZ - 1 );
		return GuestRenderer.CellToEngine( field, cellX, cellY );
	}

	/// <summary>The drop volume follows what the camera looks at: the park camera's target, else its position.</summary>
	private static Vector3 VolumeCentre() => Camera.Mode is ParkCameraMode park ? park.Target : Camera.Position;

	protected override void OnRender()
	{
		DrawnDrops = 0;
		DrawnBolts = 0;
		if ( deleted || material == null || (visual.DropCount == 0 && visual.Flashes.Count == 0) )
			return;
		var view = Camera.ViewMatrix;
		var right = new SVector3( view.M11, view.M21, view.M31 );
		var up = new SVector3( view.M12, view.M22, view.M32 );
		// Streaks and ribbons widen along the camera's flattened right; flakes use the full billboard axes.
		var flatRight = SVector3.Normalize( new SVector3( right.X, right.Y, 0 ) + new SVector3( 1e-6f, 0, 0 ) );
		vertexCount = 0;

		foreach ( var drop in visual.Drops )
		{
			var position = drop.Position.GetSystemVector3();
			if ( weather.Snow )
			{
				var half = SnowFlakeSize / 2f;
				var corner = position - right * half - up * half;
				AddQuad(
					corner + right * SnowFlakeSize, corner, corner + up * SnowFlakeSize, corner + right * SnowFlakeSize + up * SnowFlakeSize,
					SnowTint, SnowAlpha, SnowAlpha, SnowAlpha, SnowAlpha );
			}
			else
			{
				var half = RainStreakWidth / 2f;
				var head = position + new SVector3( 0, 0, RainStreakLength );
				AddQuad(
					head - flatRight * half, head + flatRight * half, position - flatRight * half, position + flatRight * half,
					RainTint, RainHeadAlpha, RainHeadAlpha, RainTailAlpha, RainTailAlpha );
			}
			DrawnDrops++;
		}

		foreach ( var flash in visual.Flashes )
		{
			var points = visual.BoltPoints( flash );
			var fade = 1f - flash.Age / WeatherVisualState.BoltFlashSeconds;
			var half = BoltRibbonWidth / 2f;
			for ( var segment = 0; segment + 1 < points.Length; segment++ )
			{
				var upper = points[segment].GetSystemVector3();
				var lower = points[segment + 1].GetSystemVector3();
				AddQuad(
					upper - flatRight * half, upper + flatRight * half, lower - flatRight * half, lower + flatRight * half,
					BoltTint, fade, fade, fade, fade );
			}
			DrawnBolts++;
		}

		var stride = (uint)Marshal.SizeOf<Vertex>();
		vertexBuffer ??= Device.ResourceFactory.CreateBuffer( new BufferDescription( (uint)MaximumVertices * stride, BufferUsage.VertexBuffer | BufferUsage.Dynamic ) );
		var commandList = global::Global.Render.CommandList;
		commandList.UpdateBuffer( vertexBuffer, 0, ref vertices[0], (uint)vertexCount * stride );

		material.Set( "ObjectUniformBuffer", new ObjectUniformBuffer
		{
			g_mModel = System.Numerics.Matrix4x4.Identity,
			g_mView = Camera.ViewMatrix,
			g_mProj = Camera.ProjMatrix,
			g_vCameraPos = Camera.Position,
			g_flTime = Time.Now
		} );
		commandList.SetVertexBuffer( 0, vertexBuffer );
		commandList.SetPipeline( material.Pipeline );
		var resourceSets = material.GetResourceSets();
		for ( uint index = 0; index < resourceSets.Length; ++index )
			commandList.SetGraphicsResourceSet( index, resourceSets[index] );
		commandList.Draw( (uint)vertexCount );
	}

	private static readonly SVector3 RainTint = new( 0.72f, 0.80f, 0.92f );
	private static readonly SVector3 SnowTint = new( 0.95f, 0.96f, 1.00f );
	private static readonly SVector3 BoltTint = new( 0.93f, 0.92f, 1.00f );

	/// <summary>Writes one quad (both windings) with a per-corner alpha; the tint rides the vertex's normal slot.</summary>
	private void AddQuad( SVector3 a, SVector3 b, SVector3 c, SVector3 d, SVector3 tint, float alphaA, float alphaB, float alphaC, float alphaD )
	{
		Vertex Corner( SVector3 position, float alpha ) => new( position, tint, new SVector2( alpha, 0 ) );
		// a b / c d as (top-left, top-right, bottom-left, bottom-right) in the callers' order.
		vertices[vertexCount++] = Corner( a, alphaA ); vertices[vertexCount++] = Corner( b, alphaB ); vertices[vertexCount++] = Corner( c, alphaC );
		vertices[vertexCount++] = Corner( b, alphaB ); vertices[vertexCount++] = Corner( d, alphaD ); vertices[vertexCount++] = Corner( c, alphaC );
		vertices[vertexCount++] = Corner( a, alphaA ); vertices[vertexCount++] = Corner( c, alphaC ); vertices[vertexCount++] = Corner( b, alphaB );
		vertices[vertexCount++] = Corner( b, alphaB ); vertices[vertexCount++] = Corner( c, alphaC ); vertices[vertexCount++] = Corner( d, alphaD );
	}

	protected override void OnDelete()
	{
		deleted = true;
		material?.ReleaseOwnedResources();
		material = null;
		var buffer = vertexBuffer;
		vertexBuffer = null;
		if ( buffer != null )
			global::Global.Render.ScheduleDelete( buffer.Dispose );
	}
}

/// <summary>Creates the weather's renderer with the weather itself: the park's simulation stays free of render concerns.</summary>
internal static class WeatherRendererModule
{
	[ModuleInitializer]
	internal static void Attach()
	{
		ParkWeather.Created += weather =>
		{
			// A host without a renderer (plain simulation tests) and constructions outside a park
			// stay entity-free; the staged-candidate scope adopts the entity like any other.
			if ( global::Global.Render == null || Level.Current?.Park == null )
				return;
			_ = new WeatherRenderer( weather );
		};
	}
}
