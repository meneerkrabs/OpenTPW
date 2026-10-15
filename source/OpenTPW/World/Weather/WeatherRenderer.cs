using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Draws the park's weather the way the original engine's <c>CRain</c> does (docs/AUDIO.md "Weather visuals"): one
/// eye-centred drop field of rain streaks or snow flakes, textured with the game's own `Data/Generic/Weather` images,
/// and a ribbon for every live lightning bolt. The inputs sit behind <see cref="IWeatherVisualSource"/> and two small
/// delegates, so the lobby can reuse the renderer later without ParkWeather or Level. The entity is created with the
/// weather itself (<see cref="ParkWeather.Created"/>).
/// </summary>
// [BIN:STP-PPC:engine_shared 0x1001CF38 CRain::GameLoad] the images are loaded from Data/Generic/Weather: raindrop.tga, snow.tga, lightning.tga (generic/sweather holds smaller copies; which set the park selects is not traced, so the full-size set is used)
// [BIN:STP-PPC:engine_shared 0x1001DE6C bolt ribbon] a camera-facing ribbon of half-width 1.3 / (branch depth + 1) textured with lightning.tga; opaque grey; no screen flash and no sky darkening in the park
// [BIN:TPW-EXE:0x567620 render states] the rain (0x57efd0) and the bolts (0x57ec50) are submitted with state 0x200D52: source alpha over one (low byte 0x52 -> SRCBLEND 5, DESTBLEND 2), blending on (0x100), the alpha test (0x400, reference 0x10 of 255, ALPHAFUNC greater-or-equal from 0x566840) and no depth writes (0x800); the snow (0x57f490) with 0x556: source alpha over inverse source alpha, blending and the alpha test on, depth writes on; the rain's 0x200D22 (one plus one, 0x57f1a7) applies only on the chips flagged at 0x566840 (engine flag 0x8bcbc8 bit 2)
// [APPROX:AUDIO-904] the rain field lives within +-4 original units (0.8 engine units) of the eye, so the camera's 0.1 near plane clips the nearest drops (those within 0.5 original units of the eye plane), where the original's near plane is 0.1 original units (0x54e86f -> 0x578be0: near 0.1, far 1000, fov 90; the park camera 0x578ca0 changes only the far plane) and its polygon clipper (clip codes 0x561bc0, clipping 0x562a70, called from 0x565bb0) cuts the streaks there, drawing only drops in front of the eye (0x57f1f0); the vertex buffer is sized once for 1024 drops plus 16 bolts of 512 segments, so a browser session allocates no further per-frame GPU memory
public sealed class WeatherRenderer : Entity
{
	/// <summary>The weather images relative to the game's data root, as the game file system reads them: any letter case, on any mount (the desktop's Data folder, the browser's /game/Data).</summary>
	internal static readonly string[] ImagePaths = { "/generic/weather/raindrop.tga", "/generic/weather/snow.tga", "/generic/weather/lightning.tga" };

	private const int VerticesPerQuad = 6;
	/// <summary>The rain's state 0x200D52: source alpha over one, no depth writes.</summary>
	internal const MaterialFlags RainFlags = MaterialFlags.DisableDepthWrite | MaterialFlags.DoubleSided | MaterialFlags.AdditiveBlend;
	/// <summary>The snow's state 0x556: alpha blend with depth writes.</summary>
	internal const MaterialFlags SnowFlags = MaterialFlags.DoubleSided;
	/// <summary>The bolts' state 0x200D52, as the rain's.</summary>
	internal const MaterialFlags BoltFlags = MaterialFlags.DisableDepthWrite | MaterialFlags.DoubleSided | MaterialFlags.AdditiveBlend;
	/// <summary>Worst-case vertex count: the drop field (rain or snow, one at a time) plus every bolt, allocated once.</summary>
	private static readonly int MaximumVertices = (WeatherVisualState.FieldSize + WeatherVisualState.MaximumBolts * WeatherVisualState.MaximumBoltSegments) * VerticesPerQuad;

	private readonly IWeatherVisualSource source;
	private readonly Func<SVector3, SVector3> originalToEngine;
	private readonly Func<bool> paused;
	private readonly float scale;
	private readonly WeatherVisualState visual = new();

	private Material<ObjectUniformBuffer>? rainMaterial;
	private Material<ObjectUniformBuffer>? snowMaterial;
	private Material<ObjectUniformBuffer>? boltMaterial;
	private DeviceBuffer? vertexBuffer;
	private readonly Vertex[] vertices = new Vertex[MaximumVertices];
	private readonly WeatherVertex[] scratch = new WeatherVertex[WeatherVisualState.FieldSize * VerticesPerQuad];
	private bool deleted;

	/// <summary>Drops and bolt ribbons drawn in the last frame (smoke instrumentation).</summary>
	public int DrawnDrops { get; private set; }
	public int DrawnBolts { get; private set; }

	/// <param name="source">The drop count, weather kind and bolt tops, in original units.</param>
	/// <param name="originalToEngine">Maps an original position (Y up) to engine space; the park maps through its heightfield's origin.</param>
	/// <param name="paused">Whether the visuals freeze (the engine's dt-zeroing flag); the park passes its paused state.</param>
	/// <param name="engineUnitsPerOriginal">Engine units per original unit: the park's models are drawn at <see cref="OriginalParkPlacement.ModelScale"/>, the lobby at 1.</param>
	public WeatherRenderer( IWeatherVisualSource source, Func<SVector3, SVector3> originalToEngine, Func<bool> paused, float engineUnitsPerOriginal = OriginalParkPlacement.ModelScale )
	{
		this.source = source;
		this.originalToEngine = originalToEngine;
		this.paused = paused;
		scale = engineUnitsPerOriginal;
		Name = "Weather";
		// Prepared with the entity — a resumed park creates it inside its load scope, a fresh park on its
		// first simulation step — so no gameplay frame pays the shader compile and pipeline build mid-rain.
		rainMaterial = Prepare( ImagePaths[0], RainFlags );
		snowMaterial = Prepare( ImagePaths[1], SnowFlags );
		boltMaterial = Prepare( ImagePaths[2], BoltFlags );
	}

	/// <summary>A material textured with one weather image from the game data, or null when the game folder lacks it (nothing of that kind is drawn then).</summary>
	private static Material<ObjectUniformBuffer>? Prepare( string image, MaterialFlags flags )
	{
		Texture texture;
		try
		{
			texture = new Texture( image );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException )
		{
			Log.Warning( $"Weather image {image} is not available ({exception.Message}); that weather is not drawn." );
			return null;
		}
		var material = new Material<ObjectUniformBuffer>( "content/shaders/weather.shader", flags );
		material.Set( "Color", texture );
		return material;
	}

	protected override void OnUpdate()
	{
		var step = WeatherVisualState.FrameSeconds( Time.Delta, paused() );
		visual.Update( step, source, CurrentCamera() );
	}

	/// <summary>The engine camera as the field reads it: eye and view axes in original units (Y up).</summary>
	private WeatherCamera CurrentCamera()
	{
		var view = Camera.ViewMatrix;
		static SVector3 ToOriginalAxes( SVector3 engine ) => new( engine.X, engine.Z, engine.Y );
		var eye = Camera.Position.GetSystemVector3();
		return new WeatherCamera(
			ToOriginalAxes( eye ) / scale,
			ToOriginalAxes( new SVector3( view.M11, view.M21, view.M31 ) ),
			ToOriginalAxes( new SVector3( view.M12, view.M22, view.M32 ) ),
			ToOriginalAxes( -new SVector3( view.M13, view.M23, view.M33 ) ) );
	}

	protected override void OnRender()
	{
		DrawnDrops = 0;
		DrawnBolts = 0;
		if ( deleted )
			return;
		var camera = CurrentCamera();
		var dropMaterial = visual.Snow ? snowMaterial : rainMaterial;
		var dropVertexCount = 0;
		var vertexCount = 0;

		if ( dropMaterial != null && visual.DropCount > 0 )
		{
			var written = visual.Snow ? visual.WriteSnow( camera, scratch ) : visual.WriteRain( camera, scratch );
			// Camera space to Veldrid view space: z runs away from the viewer, so depth becomes -z; original units become engine units.
			for ( var index = 0; index < written; index++ )
			{
				var vertex = scratch[index];
				vertices[vertexCount++] = new Vertex( new SVector3( vertex.Position.X, vertex.Position.Y, -vertex.Position.Z ) * scale,
					new SVector3( vertex.Grey, vertex.Alpha, 0 ), vertex.TexCoords );
			}
			dropVertexCount = written;
			DrawnDrops = written / VerticesPerQuad;
		}

		var boltStart = vertexCount;
		if ( boltMaterial != null )
			foreach ( var bolt in visual.Bolts )
			{
				if ( AddBolt( bolt, ref vertexCount ) )
					DrawnBolts++;
			}
		var boltVertexCount = vertexCount - boltStart;
		if ( vertexCount == 0 )
			return;

		var stride = (uint)Marshal.SizeOf<Vertex>();
		vertexBuffer ??= Device.ResourceFactory.CreateBuffer( new BufferDescription( (uint)MaximumVertices * stride, BufferUsage.VertexBuffer | BufferUsage.Dynamic ) );
		var commandList = global::Global.Render.CommandList;
		commandList.UpdateBuffer( vertexBuffer, 0, ref vertices[0], (uint)vertexCount * stride );
		commandList.SetVertexBuffer( 0, vertexBuffer );

		// The drops are already in view space, so they draw with an identity view; the bolts live in the world.
		if ( dropVertexCount > 0 )
			Draw( commandList, dropMaterial!, SVector3.Zero, System.Numerics.Matrix4x4.Identity, 0, dropVertexCount );
		if ( boltVertexCount > 0 )
			Draw( commandList, boltMaterial!, Camera.Position.GetSystemVector3(), Camera.ViewMatrix, boltStart, boltVertexCount );
	}

	private static void Draw( CommandList commandList, Material<ObjectUniformBuffer> material, SVector3 eye, System.Numerics.Matrix4x4 view, int first, int count )
	{
		material.Set( "ObjectUniformBuffer", new ObjectUniformBuffer
		{
			g_mModel = System.Numerics.Matrix4x4.Identity,
			g_mView = view,
			g_mProj = Camera.ProjMatrix,
			g_vCameraPos = eye,
			g_flTime = Time.Now
		} );
		commandList.SetPipeline( material.Pipeline );
		var resourceSets = material.GetResourceSets();
		for ( uint index = 0; index < resourceSets.Length; ++index )
			commandList.SetGraphicsResourceSet( index, resourceSets[index] );
		commandList.Draw( (uint)count, 1, (uint)first, 0 );
	}

	/// <summary>Adds the ribbon quads of one bolt in engine world space; false when nothing fit.</summary>
	private bool AddBolt( WeatherBolt bolt, ref int vertexCount )
	{
		var eye = Camera.Position.GetSystemVector3();
		var grey = WeatherVisualState.BoltGrey( bolt );
		var any = false;
		foreach ( var segment in bolt.Segments )
		{
			if ( vertexCount + VerticesPerQuad > vertices.Length )
				break;
			var a = originalToEngine( segment.A );
			var b = originalToEngine( segment.B );
			var along = b - a;
			var side = SVector3.Cross( along, eye - (a + b) * 0.5f );
			if ( side.LengthSquared() < 1e-12f )
				continue;
			side = SVector3.Normalize( side ) * (WeatherVisualState.BoltWidth( segment.Depth ) * scale);
			Vertex Corner( SVector3 position, float u, float v ) => new( position, new SVector3( grey, 1f, 0 ), new SVector2( u, v ) );
			var topLeft = Corner( a - side, 0, 0 );
			var topRight = Corner( a + side, 1, 0 );
			var bottomLeft = Corner( b - side, 0, 1 );
			var bottomRight = Corner( b + side, 1, 1 );
			vertices[vertexCount++] = topLeft; vertices[vertexCount++] = topRight; vertices[vertexCount++] = bottomLeft;
			vertices[vertexCount++] = topRight; vertices[vertexCount++] = bottomRight; vertices[vertexCount++] = bottomLeft;
			any = true;
		}
		return any;
	}

	protected override void OnDelete()
	{
		deleted = true;
		rainMaterial?.ReleaseOwnedResources();
		snowMaterial?.ReleaseOwnedResources();
		boltMaterial?.ReleaseOwnedResources();
		rainMaterial = snowMaterial = boltMaterial = null;
		var buffer = vertexBuffer;
		vertexBuffer = null;
		if ( buffer != null )
			global::Global.Render.ScheduleDelete( buffer.Dispose );
	}
}

/// <summary>The park's weather as the renderer reads it: the simulated drops, kind and bolt tops, with the bolt lifetime from the settings.</summary>
internal sealed class ParkWeatherVisualSource( ParkWeather weather ) : IWeatherVisualSource
{
	private readonly List<SVector3> tops = new();
	private readonly List<WeatherStrike> strikes = new();

	// [BIN:STP-PPC:engine_shared 0x1001FCE4 drawn count] the drawn count is min(CurrentDrops, MaxRaindrops) of the field's 1024 points
	public int DropCount => Math.Min( weather.CurrentDrops, weather.MaxRaindrops );
	public bool Snow => weather.Snow;
	public float BoltLifetimeSeconds => weather.LightningTimeMilliseconds / 1000f;

	/// <summary>The strikes since the last call, in reusable storage valid until the next call, so a frame without strikes allocates nothing.</summary>
	public IReadOnlyList<WeatherStrike> TakeStrikes()
	{
		tops.Clear();
		strikes.Clear();
		weather.DrainBoltTops( tops );
		foreach ( var top in tops )
			strikes.Add( WeatherStrike.Below( top ) );
		return strikes;
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
			var level = Level.Current;
			_ = new WeatherRenderer( new ParkWeatherVisualSource( weather ), position => ToEngine( level, position ), () => !(level.SimulationTimeScale > 0f) );
		};
	}

	/// <summary>An original position (Y up) in engine space: through the heightfield's centring when the level has one, else scaled only.</summary>
	private static SVector3 ToEngine( Level level, SVector3 position ) =>
		level.OriginalPark?.Heightfield is { } field
			? OriginalParkPlacement.ToEngine( field, position )
			: new SVector3( position.X, position.Z, position.Y ) * OriginalParkPlacement.ModelScale;
}
