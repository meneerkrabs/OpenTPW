using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;
using SVector4 = System.Numerics.Vector4;

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
// [BIN:TPW-EXE:0x57F1F0 weather submit] the rain and snow vertices go to the polygon path as clip coordinates, the bolts through the scene matrix: every vertex is drawn here from its screen position (x / w, y / w of the half screen, y down in the original) with an identity matrix, so the projection, the display aspect (0x8BCBCC) and the aspect-independent flake and streak sizes are the original's
// [BIN:TPW-EXE:0x578BE0 weather depth] the weather depth is written in the scene camera's depth space (the original's own depth value, z / w, sits where a world surface at the equivalent distance would) and nothing of the rain and snow is cut before the eye (a drop is drawn when its depth is above 0, 0x57F1F0, 0x57F490), the bolts are clipped at the original's 0.1 original units (the polygon clipper 0x561BC0, 0x562A70): no near plane of the weather's own and no change of the scene's; the vertex buffer is sized once (1024 drops plus 16 bolts of 1024 quads, so a browser session allocates no further per-frame memory)
// [BIN:TPW-EXE:0x57ec50 bolt ribbon] the vertex buffer is sized once for 1024 drops plus 16 bolts of 1024 quads (the original has no bound of its own), so a browser session allocates no further per-frame GPU memory
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
	private static readonly int MaximumVertices = (2 * WeatherVisualState.FieldSize + WeatherVisualState.MaximumBolts * WeatherVisualState.MaximumBoltSegments) * VerticesPerQuad;

	private readonly IWeatherVisualSource source;
	private readonly Func<SVector3, SVector3> originalToEngine;
	private readonly Func<float> timeScale;
	private readonly float scale;
	private readonly float originalFieldOfView;
	private readonly bool depthSplit;
	private readonly WeatherVisualState visual = new();
	private readonly WeatherBoltRibbon ribbon;
	// the per-frame inputs of the ribbon's projection, set before a frame's bolts are built (no closure allocated per frame)
	private System.Numerics.Matrix4x4 viewProjection;
	private float clipScale;

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
	/// <param name="timeScale">The simulation's time scale the visuals advance by (0 freezes them); the park passes its own, the lobby 1.</param>
	/// <param name="engineUnitsPerOriginal">Engine units per original unit: the park's models are drawn at <see cref="OriginalParkPlacement.ModelScale"/>, the lobby at 1.</param>
	/// <param name="depthSplit">Whether the original's depth value has its linear range (the park views; the lobby never sets the split, 0x578CA0 is not called there).</param>
	/// <param name="fieldOfView">The field of view in degrees the original builds the scene's projection with (the park's <see cref="OriginalProjection.FieldOfView"/>, the lobby's island field of view): the bolt ribbon's width is in the clip units of that projection.</param>
	public WeatherRenderer( IWeatherVisualSource source, Func<SVector3, SVector3> originalToEngine, Func<float> timeScale, float engineUnitsPerOriginal = OriginalParkPlacement.ModelScale, float fieldOfView = OriginalProjection.FieldOfView, bool depthSplit = true )
	{
		this.source = source;
		this.originalToEngine = originalToEngine;
		this.timeScale = timeScale;
		scale = engineUnitsPerOriginal;
		originalFieldOfView = fieldOfView;
		this.depthSplit = depthSplit;
		ribbon = new WeatherBoltRibbon( ProjectBoltPoint );
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
		var step = WeatherVisualState.FrameSeconds( Time.Delta, timeScale() );
		visual.Update( step, source, CurrentCamera(), drawFrames.Advance( Time.Delta ) > 0 );
	}

	/// <summary>The 60 Hz reference frames at which the snow jitter and the bolt flicker are redrawn (real time, paused or not).</summary>
	private readonly ReferenceFrameClock drawFrames = new();

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
			ToOriginalAxes( -new SVector3( view.M13, view.M23, view.M33 ) ),
			IsEyeView( Camera.Mode ) );
	}

	/// <summary>
	/// Whether the camera is in one of the original's first-person views, the ones that raise bits 0x16 of the camera-mode byte <c>0x790AB0</c>: the terrain camcorder (bit 0x2, OpenTPW's free walk) and the ride follow (bit 0x4). The rain's parallax factor
	/// is 0.3 in these views and 0.1 in the overview and the lobby.
	/// </summary>
	// [BIN:TPW-EXE:0x57fff8 rain parallax factor] CRain +0x3340 is 0.3 (0x3E99999A) while the camera-mode byte 0x790AB0 has any of 0x16, else the 0.1 the init left (0x57D1FC); the bits are the park view's modes: 0x42AB20 toggles 0x2 (the terrain camcorder, input event 2) and 0x4 (the ride follow, event 4) and clears them leaving a mode (0x42AB5E-0x42AB78: and dl, 0x41), and no instruction anywhere sets 0x10 (the byte's other stores or-in 0x100, 0x200, 0x800, 0x1000, 0x80 and 0x8, clear bits or store 0; the only wider writes read the byte back from a save stream, 0x42CE1B, 0x42CF61, or restore a copy, 0x42B561), so the factor is 0.3 exactly in the camcorder and in a ride
	// [BIN:TPW-EXE:0x54FAD7 eye views] the same test (0x790AB0 & 0x16) switches the park's fog on (0x54FAE9, bit 0x2000 of 0x8BCBC8) and the listener to the eye (0x54F915); OpenTPW's pair is the free walk (the camcorder's stand-in, [EXT:camcorder-first-person]) and the ride camera
	internal static bool IsEyeView( CameraMode? mode ) => mode is FreeWalkCameraMode or RideCameraMode;

	/// <summary>A bolt point (original units) in the clip space of the original's matrix: the scene's clip scaled so that w is sin(fov / 2) x the view depth in original units, y running down.</summary>
	private SVector4 ProjectBoltPoint( SVector3 original )
	{
		var clip = SVector4.Transform( new SVector4( originalToEngine( original ), 1f ), viewProjection );
		return new SVector4( clip.X * clipScale, -clip.Y * clipScale, clip.Z * clipScale, clip.W * clipScale );
	}

	protected override void OnRender()
	{
		DrawnDrops = 0;
		DrawnBolts = 0;
		if ( deleted )
			return;
		var camera = CurrentCamera();
		var target = global::Global.Render.MultisampledFramebuffer;
		// the display's height over its width, the original's aspect word 0x8BCBCC
		var displayAspect = target.Width > 0 && target.Height > 0 ? (float)target.Height / target.Width : 0.75f;
		var projection = Camera.ProjMatrix;
		var dropMaterial = visual.Snow ? snowMaterial : rainMaterial;
		var dropVertexCount = 0;
		var vertexCount = 0;

		// the original's depth split (WeatherProjection): its far argument is 1000 in the eye views and the span selection's farthest corner otherwise; the lobby has no linear range
		var far = IsEyeView( Camera.Mode ) ? WeatherProjection.OriginalFar : MathF.Max( 1f, MathF.Min( WeatherProjection.OriginalFar, projection.M43 / (projection.M33 + 1f) / scale ) );
		if ( dropMaterial != null && visual.DropCount > 0 )
		{
			var written = visual.Snow ? visual.WriteSnow( camera, scratch, displayAspect ) : visual.WriteRain( camera, scratch );
			var step = visual.Snow ? WeatherVisualState.SnowVerticesPerFlake : VerticesPerQuad / 2;
			Span<WeatherRibbonVertex> triangle = stackalloc WeatherRibbonVertex[3];
			Span<WeatherRibbonVertex> clipped = stackalloc WeatherRibbonVertex[8];
			for ( var index = 0; index + step <= written; index += step )
			{
				for ( var corner = 0; corner < step; corner++ )
				{
					var vertex = scratch[index + corner];
					triangle[corner] = new WeatherRibbonVertex( new SVector4( vertex.Position.X, vertex.Position.Y, vertex.Position.Z, vertex.W ), vertex.TexCoords, new SVector2( vertex.Grey, vertex.Alpha ) );
				}
				var inside = true;
				foreach ( var corner in triangle )
					inside &= corner.Clip.Z <= corner.Clip.W;
				var count = 3;
				var source = triangle;
				if ( !inside )
				{
					// a vertex beyond the far plane (clip z above w): the original's clipper cuts the triangle there
					count = WeatherProjection.ClipNear( triangle, clipped, -1f, 1f, 0f );
					source = clipped;
				}
				if ( count < 3 || vertexCount + (count - 2) * 3 > vertices.Length )
					continue;
				for ( var fan = 1; fan < count - 1; fan++ )
				{
					AddDropVertex( source[0], projection, far, ref vertexCount );
					AddDropVertex( source[fan], projection, far, ref vertexCount );
					AddDropVertex( source[fan + 1], projection, far, ref vertexCount );
				}
				DrawnDrops++;
			}
			dropVertexCount = vertexCount;
		}

		var boltStart = vertexCount;
		if ( boltMaterial != null && visual.Bolts.Count > 0 )
		{
			viewProjection = Camera.ViewMatrix * projection;
			clipScale = MathF.Sin( originalFieldOfView * MathF.PI / 360f ) / scale;
			foreach ( var bolt in visual.Bolts )
			{
				if ( AddBolt( bolt, ref vertexCount ) )
					DrawnBolts++;
			}
		}
		var boltVertexCount = vertexCount - boltStart;
		if ( vertexCount == 0 )
			return;

		var stride = Vertex.SizeInBytes;
		vertexBuffer ??= Device.ResourceFactory.CreateBuffer( new BufferDescription( (uint)MaximumVertices * stride, BufferUsage.VertexBuffer | BufferUsage.Dynamic ) );
		var commandList = global::Global.Render.CommandList;
		commandList.UpdateBuffer( vertexBuffer, 0, ref vertices[0], (uint)vertexCount * stride );
		commandList.SetVertexBuffer( 0, vertexBuffer );

		// every vertex is already in clip space (see WeatherProjection and weather.shader), so both draw with identity matrices
		if ( dropVertexCount > 0 )
			Draw( commandList, dropMaterial!, 0, dropVertexCount );
		if ( boltVertexCount > 0 )
			Draw( commandList, boltMaterial!, boltStart, boltVertexCount );
	}

	private void AddDropVertex( in WeatherRibbonVertex vertex, System.Numerics.Matrix4x4 projection, float far, ref int vertexCount )
	{
		var clip = vertex.Clip;
		var depth = WeatherProjection.WriteDepth( clip.Z, clip.W, projection, scale, far, depthSplit );
		// clip-space vertex (x w', y w', z w', w): the shader takes w from the tint's third component, so the device interpolates texture and colour perspective-correctly as the original's rhw does
		vertices[vertexCount++] = new Vertex( new SVector3( clip.X, -clip.Y, depth * clip.W ), new SVector3( vertex.Tint.X, vertex.Tint.Y, clip.W ), vertex.TexCoords );
	}

	private static void Draw( CommandList commandList, Material<ObjectUniformBuffer> material, int first, int count )
	{
		material.Set( "ObjectUniformBuffer", new ObjectUniformBuffer
		{
			g_mModel = System.Numerics.Matrix4x4.Identity,
			g_mView = System.Numerics.Matrix4x4.Identity,
			g_mProj = System.Numerics.Matrix4x4.Identity,
			g_vCameraPos = SVector3.Zero,
			g_flTime = Time.Now
		} );
		commandList.SetPipeline( material.Pipeline );
		var resourceSets = material.GetResourceSets();
		for ( uint index = 0; index < resourceSets.Length; ++index )
			commandList.SetGraphicsResourceSet( index, resourceSets[index] );
		commandList.Draw( (uint)count, 1, (uint)first, 0 );
	}

	/// <summary>Adds the ribbon quads of one bolt, from the walk's emitter calls through <see cref="WeatherBoltRibbon"/>; false when nothing fit.</summary>
	private bool AddBolt( WeatherBolt bolt, ref int vertexCount )
	{
		var grey = WeatherVisualState.BoltGrey( bolt );
		var any = false;
		var quads = 0;
		// the original zeroes the first depth's counter before the walk; a branch zeroes its own
		ribbon.ResetDepth( 0 );
		// the original's near plane in clip w (w = sin(fov / 2) x the view depth in original units)
		var originalNearW = WeatherProjection.OriginalNear * MathF.Sin( originalFieldOfView * MathF.PI / 360f );
		Span<WeatherRibbonVertex> polygon = stackalloc WeatherRibbonVertex[4];
		Span<WeatherRibbonVertex> clipped = stackalloc WeatherRibbonVertex[8];
		foreach ( var call in bolt.Calls )
		{
			if ( call.StartsBranch )
			{
				ribbon.ResetDepth( call.Depth );
				continue;
			}
			if ( ribbon.Emit( call.Depth, call.First, call.Second ) is not { } quad )
				continue;
			if ( ++quads > WeatherVisualState.MaximumBoltSegments )
				break;
			polygon[0] = quad.V0; polygon[1] = quad.V1; polygon[2] = quad.V2; polygon[3] = quad.V3;
			var count = 4;
			var source = polygon;
			var inside = true;
			foreach ( var corner in polygon )
				inside &= corner.Clip.W >= originalNearW;
			if ( !inside )
			{
				count = WeatherProjection.ClipNear( polygon, clipped, 0f, 1f, -originalNearW );
				source = clipped;
			}
			if ( count < 3 || vertexCount + (count - 2) * 3 > vertices.Length )
				continue;
			Vertex Corner( in WeatherRibbonVertex vertex )
			{
				var clip = vertex.Clip;
				var depth = float.IsNaN( clip.Z / clip.W ) ? 1f : MathF.Min( MathF.Max( clip.Z / clip.W, 0f ), 1f );
				return new Vertex( new SVector3( clip.X, -clip.Y, depth * clip.W ), new SVector3( grey, 1f, clip.W ), new SVector2( vertex.TexCoords.X, 1f - vertex.TexCoords.Y ) );
			}
			// the original's fan (0, 1, 2), (0, 2, 3) over the (clipped) polygon
			for ( var triangle = 1; triangle < count - 1; triangle++ )
			{
				vertices[vertexCount++] = Corner( source[0] );
				vertices[vertexCount++] = Corner( source[triangle] );
				vertices[vertexCount++] = Corner( source[triangle + 1] );
			}
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
	private readonly List<(SVector3 Top, SVector3 Ground)> points = new();
	private readonly List<WeatherStrike> strikes = new();

	// [BIN:STP-PPC:engine_shared 0x1001FCE4 drawn count] the drawn count is min(CurrentDrops, MaxRaindrops) of the field's 1024 points
	public int DropCount => Math.Min( weather.CurrentDrops, weather.MaxRaindrops );
	public bool Snow => weather.Snow;
	public float BoltLifetimeSeconds => weather.LightningTimeMilliseconds / 1000f;

	/// <summary>The strikes since the last call, in reusable storage valid until the next call, so a frame without strikes allocates nothing.</summary>
	public IReadOnlyList<WeatherStrike> TakeStrikes()
	{
		points.Clear();
		strikes.Clear();
		weather.DrainStrikePoints( points );
		foreach ( var (top, ground) in points )
			strikes.Add( new WeatherStrike( top, ground ) );
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
			_ = new WeatherRenderer( new ParkWeatherVisualSource( weather ), position => ToEngine( level, position ), () => level.SimulationTimeScale );
		};
	}

	/// <summary>An original position (Y up) in engine space: through the heightfield's centring when the level has one, else scaled only.</summary>
	private static SVector3 ToEngine( Level level, SVector3 position ) =>
		level.OriginalPark?.Heightfield is { } field
			? OriginalParkPlacement.ToEngine( field, position )
			: new SVector3( position.X, position.Z, position.Y ) * OriginalParkPlacement.ModelScale;
}
