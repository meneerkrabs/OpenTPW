using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Draws the original particle system (docs/PARTICLES.md): camera-facing, spin-rotated quads of the `particles` sprite set,
/// blended the way the template's render flags say. It owns the <see cref="ParticleSystem"/>, advances it in 31 ms ticks
/// from the frame time and receives its effects from the object scripts' EVENT 1/2 requests
/// (<see cref="ParticleRendererModule"/>). The entity is created by the first request of a level and deleted with it.
/// </summary>
// [BIN:STP-PPC:engine_shared 0x1ACC8 particle draw] over the emitter slots, culled by bounding box; a camera-facing quad of half-size size/1024 times the sprite scale, rotated by the spin
// [BIN:STP-PPC:engine_shared 0x1ACC8 blend] untextured: SRC_ALPHA, ONE_MINUS_SRC_ALPHA; render flags 0x2004: SRC_ALPHA, ONE; flags 0x4: ONE, ONE; otherwise the sprite's own alpha blend
// [BIN:TPW-EXE:0x57C620 draw driver] one quad per particle in slot order 0..119, newest particle first, each with its own blend state; the render flags apply to untextured particles too
// [EXT:particle-blend-runs] OpenTPW draws the quads in the original's order (0x565F90: slots ascending, newest particle first) as runs of one blend (alpha, source-alpha-over-one, one-plus-one) through its shared material pipeline, not one submission with its own state word per particle
// [APPROX:PTCL-905] the complete per-particle state words (decoder 0x567620) are not reproduced; untextured non-additive world alpha0/>=F0 now uses opaque RGB and depth writes unless 4000, while textured modes, fractional alpha tests, additive depth and screen behavior remain unresolved: depth writes unless flags 0x4000 add 0x800, an untextured particle opaque (0x1256) when its alpha byte is 0 or >= 0xF0 and by alpha with an alpha test at 0x10/255 (0x556) otherwise, 0x200522 / 0x200552 for the add blends with that alpha test, MAGFILTER point for flags 0x20000, (the triangle depth sort 0x565590 -> 0x565490 acts within one particle's two coplanar triangles and has no visible effect); the particle shader still has no alpha test; the original sorts these batches into the same far-to-near list as the translucent world, OpenTPW draws them after that whole list (Level.SplitLate); the bounded untextured opaque world variant does not resolve those coupled states
public sealed class ParticleRenderer : Entity
{
	private const int VerticesPerQuad = 6;
	/// <summary>The width factor of an untextured world particle: the original's clip-space factor DAT_008BCBCC (display height / width, 0x582EB0) is the square of an aspect-correct projection.</summary>
	private const float UntexturedWidthFactor = 1f;
	/// <summary>Cull margin around an emitter's particle bounds, in 1/64 original units (a particle's own size).</summary>
	private const int CullMargin = 16 * 64;

	// one renderer per level: a staged resume candidate gets its own, and a rolled-back candidate's deletion leaves the live level's alone
	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Level, ParticleRenderer> byLevel = new();

	/// <summary>The renderer of a level, or null.</summary>
	/// <summary>True once the atlas's CPU pixels were dropped after the GPU upload (smoke tests).</summary>
	internal bool AtlasPixelsReleased => atlas.Pixels.Length == 0;

	public static ParticleRenderer? For( Level level ) => level != null && byLevel.TryGetValue( level, out var renderer ) ? renderer : null;

	private readonly ParticleSystem system;
	private readonly ParticleSpriteAtlas atlas;
	private readonly Func<SVector3, SVector3> originalToEngine;
	private readonly Func<bool> paused;
	private readonly float scale;
	private readonly List<ParticleSprite> sprites = new();
	private readonly List<ParticleEmitterBounds> bounds = new();
	private readonly HashSet<int> visibleEmitters = new();
	private readonly Vertex[] vertices;
	// consecutive quads of one kind (blend and world or screen-space), in the original's draw order: slots ascending, newest particle first
	private readonly List<(int Kind, int First, int Count)> runs = new();
	private Texture? atlasTexture;
	private Material<ObjectUniformBuffer>? alphaMaterial;
	private Material<ObjectUniformBuffer>? additiveMaterial;
	private Material<ObjectUniformBuffer>? oneOneMaterial;
	private Material<ObjectUniformBuffer>? screenAlphaMaterial;
	private Material<ObjectUniformBuffer>? screenAdditiveMaterial;
	private Material<ObjectUniformBuffer>? screenOneOneMaterial;
	private Material<ObjectUniformBuffer>? opaqueMaterial;
	private Material<ObjectUniformBuffer>? opaqueNoWriteMaterial;
	private DeviceBuffer? vertexBuffer;
	private bool deleted;

	/// <summary>Live particles and quads drawn in the last frame (smoke instrumentation).</summary>
	public int DrawnParticles { get; private set; }
	public ParticleSystem Particles => system;

	public ParticleRenderer( ParticleSystem system, ParticleSpriteAtlas atlas, Func<SVector3, SVector3> originalToEngine, Func<bool> paused,
		float engineUnitsPerOriginal = OriginalParkPlacement.ModelScale, Level? level = null )
	{
		if ( level != null )
			Level = level;
		this.system = system;
		this.atlas = atlas;
		this.originalToEngine = originalToEngine;
		this.paused = paused;
		scale = engineUnitsPerOriginal;
		vertices = new Vertex[system.Capacity * VerticesPerQuad];
		Name = "Particles";
		try
		{
			// one atlas texture shared by the three materials; a failure leaves no half-built entity or assets behind
			atlasTexture = new Texture( atlas.Pixels, atlas.Width, atlas.Height );
			// uploaded: this renderer reads only the frame positions from now on
			atlas.ReleasePixels();
			alphaMaterial = Prepare( atlasTexture, MaterialFlags.DisableDepthWrite | MaterialFlags.DoubleSided );
			additiveMaterial = Prepare( atlasTexture, MaterialFlags.DisableDepthWrite | MaterialFlags.DoubleSided | MaterialFlags.AdditiveBlend );
			opaqueMaterial = Prepare( atlasTexture, FlagsForKind( 6 ) );
			opaqueNoWriteMaterial = Prepare( atlasTexture, FlagsForKind( 7 ) );
			oneOneMaterial = Prepare( atlasTexture, MaterialFlags.DisableDepthWrite | MaterialFlags.DoubleSided | MaterialFlags.OneOneBlend );
			// screen-space quads sit at the template's z (word +0x52, 0 in every shipped screen-space template): in front of the scene
			screenAlphaMaterial = Prepare( atlasTexture, MaterialFlags.DisableDepth | MaterialFlags.DoubleSided );
			screenAdditiveMaterial = Prepare( atlasTexture, MaterialFlags.DisableDepth | MaterialFlags.DoubleSided | MaterialFlags.AdditiveBlend );
			screenOneOneMaterial = Prepare( atlasTexture, MaterialFlags.DisableDepth | MaterialFlags.DoubleSided | MaterialFlags.OneOneBlend );
		}
		catch
		{
			Delete();
			throw;
		}
		if ( Level != null )
			byLevel.AddOrUpdate( Level, this );
	}

	private static Material<ObjectUniformBuffer> Prepare( Texture atlas, MaterialFlags flags )
	{
		var material = new Material<ObjectUniformBuffer>( "content/shaders/particle.shader", flags );
		material.Set( "Color", atlas );
		return material;
	}

	protected override void OnUpdate()
	{
		var seconds = WeatherVisualState.FrameSeconds( Time.Delta, paused() );
		system.Advance( seconds );
	}

	private enum Batch { Alpha, Additive, OneOne }

	// [BIN:TPW-EXE:0x57C7C8 blend flags] the flags test (bit 4, then 0x2000) runs for every particle, untextured ones included
	// [BIN:STP-PPC:engine_shared 0x1ACC8 blend] see the class comment
	internal static int BatchIndexOf( int renderFlags ) =>
		(renderFlags & 0x2004) == 0x2004 ? 1
		: (renderFlags & 0x4) != 0 ? 2
		: 0;

	private static Batch BatchOf( in ParticleSprite sprite ) => (Batch)BatchIndexOf( sprite.RenderFlags );

	protected override void OnRender()
	{
		DrawnParticles = 0;
		if ( deleted || system.Snapshot( sprites ) == 0 )
			return;
		system.CollectBounds( bounds );
		visibleEmitters.Clear();
		var viewProjection = Camera.ViewMatrix * Camera.ProjMatrix;
		foreach ( var emitter in bounds )
			if ( InView( emitter, viewProjection ) )
				visibleEmitters.Add( emitter.Slot );

		var view = Camera.ViewMatrix;
		var right = new SVector3( view.M11, view.M21, view.M31 );
		var up = new SVector3( view.M12, view.M22, view.M32 );

		var target = global::Global.Render.MultisampledFramebuffer;
		var screenAspect = target.Width > 0 ? (float)target.Height / target.Width : 1f;
		var vertexCount = 0;
		runs.Clear();
		foreach ( var sprite in sprites )
		{
			var batch = BatchOf( sprite );
			var screenSpace = sprite.ScreenSpace;
			// screen-space emitters have no world bounds to cull by (the original leaves their bounds empty)
			if ( !screenSpace && !visibleEmitters.Contains( sprite.Emitter ) )
				continue;
			if ( vertexCount + VerticesPerQuad > vertices.Length )
				break;
			var first = vertexCount;
			if ( !(screenSpace ? AddScreenQuad( sprite, batch, screenAspect, ref vertexCount ) : AddQuad( sprite, batch, right, up, ref vertexCount )) )
				continue;
			DrawnParticles++;
			AddToRuns( runs, RenderKindOf( sprite ), first, vertexCount - first );
		}
		if ( vertexCount == 0 )
			return;

		var stride = Vertex.SizeInBytes;
		vertexBuffer ??= Device.ResourceFactory.CreateBuffer( new BufferDescription( (uint)vertices.Length * stride, BufferUsage.VertexBuffer | BufferUsage.Dynamic ) );
		var commandList = global::Global.Render.CommandList;
		commandList.UpdateBuffer( vertexBuffer, 0, ref vertices[0], (uint)vertexCount * stride );
		commandList.SetVertexBuffer( 0, vertexBuffer );
		var eye = Camera.Position.GetSystemVector3();
		// [APPROX:PTCL-909] the screen-space quads are drawn inside this world pass, so the HUD and the advisor's image draw over them; the original's pass 0x57C620 sits in the scene view render 0x576a00 after the object lists and the view's callback (+0x78) and before the weather (0x57FF60), and which later view or callback draws the HUD windows and the advisor's model is not read
		// [BIN:TPW-EXE:0x57C620 draw order] one quad per particle in slot order 0..119, newest particle first, world and screen-space particles in the same loop, so the runs follow that order and the kinds interleave as the particles do
		// The uniform block is the same for every run of a material in this frame, so it is written once per material, however often the kinds alternate.
		var uniformsWritten = 0;
		foreach ( var (kind, first, count) in runs )
		{
			var material = kind switch
			{
				0 => alphaMaterial, 1 => additiveMaterial, 2 => oneOneMaterial,
				3 => screenAlphaMaterial, 4 => screenAdditiveMaterial, 5 => screenOneOneMaterial,
				6 => opaqueMaterial, _ => opaqueNoWriteMaterial
			};
			if ( (uniformsWritten & (1 << kind)) == 0 )
			{
				WriteUniforms( material!, eye, screenSpace: IsScreenKind( kind ), tinted: kind == 0 || kind is 6 or 7 );
				uniformsWritten |= 1 << kind;
			}
			Draw( commandList, material!, (first, count) );
		}
	}

	/// <summary>The run kind of a quad: its blend batch, plus 3 for a screen-space particle.</summary>
	internal static int KindOf( int batch, bool screenSpace ) => batch + (screenSpace ? 3 : 0);
	private static int KindOf( Batch batch, bool screenSpace ) => KindOf( (int)batch, screenSpace );

	// [BIN:TPW-EXE:0x57C6FA-0x57C71B] Untextured alpha0 or >=F0 selects opaque1256.
	// Additive flags override afterward (0x57C7C8); 4000 adds no-depth-write (0x57C7F4).
	// Textured and screen-space paths deliberately retain their existing behavior.
	internal static int RenderKindOf( in ParticleSprite sprite )
	{
		var batch = BatchIndexOf( sprite.RenderFlags );
		var alpha = sprite.Colour >> 24;
		return !sprite.ScreenSpace && sprite.FrameCount == 0 && batch == 0 && (alpha == 0 || alpha >= 0xF0)
			? ((sprite.RenderFlags & 0x4000) == 0 ? 6 : 7)
			: KindOf( batch, sprite.ScreenSpace );
	}

	internal static bool IsScreenKind( int kind ) => kind is >= 3 and <= 5;
	internal static MaterialFlags FlagsForKind( int kind ) => MaterialFlags.OpaqueBlend | MaterialFlags.DoubleSided
		| (kind == 7 ? MaterialFlags.DisableDepthWrite : MaterialFlags.None);

	// Host adaptation: the final world-to-output blit alpha-blends; an opaque particle must
	// leave opaque alpha there too. This is not evidence that the native rewrites its alpha byte.
	internal static uint ColourForKind( uint colour, int kind ) => kind is 6 or 7 ? colour | 0xFF000000u : colour;

	/// <summary>
	/// Appends a quad's vertices to the draw order: the original submits one primitive per particle in that order, so OpenTPW keeps the
	/// order and merges only a neighbour that shares the state.
	/// </summary>
	internal static void AddToRuns( List<(int Kind, int First, int Count)> runs, int kind, int first, int count )
	{
		if ( runs.Count > 0 && runs[^1].Kind == kind && runs[^1].First + runs[^1].Count == first )
			runs[^1] = (kind, runs[^1].First, runs[^1].Count + count);
		else
			runs.Add( (kind, first, count) );
	}

	/// <summary>The screen-space draw count of the last frame (tests and smokes).</summary>
	public int DrawnScreenParticles => runs.Where( run => IsScreenKind( run.Kind ) ).Sum( run => run.Count ) / VerticesPerQuad;

	/// <summary>False when all eight corners of the emitter's particle bounds lie outside one frustum plane.</summary>
	private bool InView( ParticleEmitterBounds emitter, System.Numerics.Matrix4x4 viewProjection )
	{
		var outsideLeft = true; var outsideRight = true; var outsideBottom = true; var outsideTop = true; var outsideNear = true; var outsideFar = true;
		for ( var corner = 0; corner < 8; corner++ )
		{
			var x = (corner & 1) == 0 ? emitter.MinX - CullMargin : emitter.MaxX + CullMargin;
			var y = (corner & 2) == 0 ? emitter.MinY - CullMargin : emitter.MaxY + CullMargin;
			var z = (corner & 4) == 0 ? emitter.MinZ - CullMargin : emitter.MaxZ + CullMargin;
			var clip = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( originalToEngine( new SVector3( x, y, z ) / 64f ), 1f ), viewProjection );
			outsideLeft &= clip.X < -clip.W; outsideRight &= clip.X > clip.W;
			outsideBottom &= clip.Y < -clip.W; outsideTop &= clip.Y > clip.W;
			outsideNear &= clip.Z < 0; outsideFar &= clip.Z > clip.W;
		}
		return !(outsideLeft || outsideRight || outsideBottom || outsideTop || outsideNear || outsideFar);
	}

	/// <summary>Half-width and half-height of a quad in engine units: the height is size / 1024 original units for every particle, the width gets a factor (see the call site).</summary>
	internal static (float HalfWidth, float HalfHeight) HalfExtents( int size, float scale, int frameCount, int frameWidth, int frameHeight )
	{
		var halfHeight = size / 1024f * scale;
		return (halfHeight * (frameCount == 0 || frameHeight <= 0 ? UntexturedWidthFactor : (float)frameWidth / frameHeight), halfHeight);
	}

	private bool AddQuad( in ParticleSprite sprite, Batch batch, SVector3 right, SVector3 up, ref int vertexCount )
	{
		var frame = sprite.FrameCount == 0 ? atlas.White : atlas.Find( sprite.SpriteCode, sprite.Frame );
		if ( frame is not { } found )
			return false;
		var centre = originalToEngine( new SVector3( sprite.X, sprite.Y, sprite.Z ) / 64f );
		// [BIN:TPW-EXE:0x57C6B8 sprite size] half-height = (size << 1) * (1/1024) * 0.5 = size / 1024 original units for every particle; only the width gets a factor
		// [BIN:TPW-EXE:0x588660 frame aspect] the frame record's float +8 is the frame's width / height (the shorts at +0xC and +0xE), the factor a textured quad's half-width takes; an untextured quad takes DAT_008BCBCC, the display's height / width (0x582EB0)
		// [EXT:particle-world-scale] OpenTPW places a quad's half-height in world units under its own 55 degree camera (the original's world size is the half-height / cos(fov / 2) under its 90 degree camera, 0x42A990 -> 0x578BE0), and an aspect-correct projection gives an untextured quad its square (DAT_008BCBCC, the display's height / width, 0x582EB0): resolution-independent sizing with no original value to copy
		// [APPROX:PTCL-904] the original adds the half-extents in clip space after its world-to-clip transform (0x57C6xx: x +- half-width, y +- half-height, w untouched) and a textured quad's width factor is the frame record's float +8 (width / height, 0x588660) with no height / width factor, so it is (width / height) x (display width / height) on the screen unless something rescales +8 after the load (nothing found); OpenTPW uses the frame's pixel aspect
		var (halfWidth, halfHeight) = HalfExtents( sprite.Size, scale, sprite.FrameCount, found.Width, found.Height );
		if ( !(halfWidth > 0 && halfHeight > 0) )
			return false;
		var angle = sprite.Spin * (MathF.Tau / 65536f);
		var (sin, cos) = MathF.SinCos( angle );
		var across = (right * cos + up * sin) * halfWidth;
		var vertical = (up * cos - right * sin) * halfHeight;

		var tint = Tint( ColourForKind( sprite.Colour, RenderKindOf( sprite ) ), batch );

		Vertex Corner( SVector3 position, float u, float v ) => new( position, tint, new SVector2( u, v ) );
		var topLeft = Corner( centre - across + vertical, found.U0, found.V0 );
		var topRight = Corner( centre + across + vertical, found.U1, found.V0 );
		var bottomLeft = Corner( centre - across - vertical, found.U0, found.V1 );
		var bottomRight = Corner( centre + across - vertical, found.U1, found.V1 );
		vertices[vertexCount++] = topLeft; vertices[vertexCount++] = topRight; vertices[vertexCount++] = bottomLeft;
		vertices[vertexCount++] = topRight; vertices[vertexCount++] = bottomRight; vertices[vertexCount++] = bottomLeft;
		return true;
	}

	/// <summary>
	/// A screen-space particle's quad, its vertices in clip space (drawn without the camera). The width gets the frame's pixel aspect times the
	/// target's height / width, so a square frame stays square on the screen.
	/// </summary>
	// [EXT:particle-world-scale] a screen-space quad's width also takes the target's height / width, which keeps a frame's pixels square at any resolution (resolution independence)
	// [APPROX:PTCL-904] the original's screen-space half-width is the half-height times the frame's width / height (textured) or times DAT_008BCBCC, height / width (untextured), added in clip space with w = 1 (flag 0x40000), with no target height / width factor on a textured quad
	private bool AddScreenQuad( in ParticleSprite sprite, Batch batch, float screenAspect, ref int vertexCount )
	{
		var frame = sprite.FrameCount == 0 ? atlas.White : atlas.Find( sprite.SpriteCode, sprite.Frame );
		if ( frame is not { } found )
			return false;
		var halfHeight = ParticleScreenSpace.HalfHeight( sprite.Size );
		var halfWidth = halfHeight * screenAspect * (sprite.FrameCount == 0 || found.Height <= 0 ? UntexturedWidthFactor : (float)found.Width / found.Height);
		if ( !(halfWidth > 0 && halfHeight > 0) )
			return false;
		var (a, b, c, d) = ParticleScreenSpace.Corners( ParticleScreenSpace.Position( sprite.X, sprite.Z ), halfWidth, halfHeight, sprite.Spin );
		var tint = Tint( sprite.Colour, batch );
		Vertex Corner( SVector2 screen, float u, float v )
		{
			var clip = ParticleScreenSpace.ToClip( screen );
			return new( new SVector3( clip.X, clip.Y, 0f ), tint, new SVector2( u, v ) );
		}
		var topLeft = Corner( a, found.U0, found.V0 );
		var topRight = Corner( b, found.U1, found.V0 );
		var bottomRight = Corner( c, found.U1, found.V1 );
		var bottomLeft = Corner( d, found.U0, found.V1 );
		vertices[vertexCount++] = topLeft; vertices[vertexCount++] = topRight; vertices[vertexCount++] = bottomLeft;
		vertices[vertexCount++] = topRight; vertices[vertexCount++] = bottomRight; vertices[vertexCount++] = bottomLeft;
		return true;
	}

	private static SVector3 Tint( uint colour, Batch batch )
	{
		var alpha = (int)(colour >> 24);
		var red = (int)((colour >> 16) & 0xFF);
		var green = (int)((colour >> 8) & 0xFF);
		var blue = (int)(colour & 0xFF);
		if ( batch == Batch.OneOne )
		{
			// one plus one adds the colour as is: the particle's alpha scales it instead of blending it
			red = red * alpha / 255; green = green * alpha / 255; blue = blue * alpha / 255; alpha = 255;
		}
		return new SVector3( red, green, blue + 256 * alpha );
	}

	// [EXT:evening] <paramref name="tinted"/>: the world's alpha-blended particles (smoke, dust, steam) take the sprite tint at dusk and night like the guests do; the additive ones (fire, sparks, fireworks)
	// and the screen-space ones give off or belong to the screen's own light and are left as they are
	private static void WriteUniforms( Material<ObjectUniformBuffer> material, SVector3 eye, bool screenSpace, bool tinted )
	{
		var uniforms = new ObjectUniformBuffer
		{
			g_mModel = System.Numerics.Matrix4x4.Identity,
			g_mView = screenSpace ? System.Numerics.Matrix4x4.Identity : Camera.ViewMatrix,
			g_mProj = screenSpace ? System.Numerics.Matrix4x4.Identity : Camera.ProjMatrix,
			g_vCameraPos = eye,
			g_flTime = Time.Now
		};
		if ( tinted )
			uniforms.WithSpriteTint( Level.Current?.Look ?? EveningLook.Day );
		material.Set( "ObjectUniformBuffer", uniforms );
	}

	private static void Draw( CommandList commandList, Material<ObjectUniformBuffer> material, (int First, int Count) range )
	{
		commandList.SetPipeline( material.Pipeline );
		var resourceSets = material.GetResourceSets();
		for ( uint index = 0; index < resourceSets.Length; ++index )
			commandList.SetGraphicsResourceSet( index, resourceSets[index] );
		commandList.Draw( (uint)range.Count, 1, (uint)range.First, 0 );
	}

	protected override void OnDelete()
	{
		deleted = true;
		system.Clear();
		if ( Level != null && byLevel.TryGetValue( Level, out var registered ) && ReferenceEquals( registered, this ) )
			byLevel.Remove( Level );
		opaqueMaterial?.ReleaseOwnedResources();
		opaqueNoWriteMaterial?.ReleaseOwnedResources();
		opaqueMaterial = opaqueNoWriteMaterial = null;
		alphaMaterial?.ReleaseOwnedResources();
		additiveMaterial?.ReleaseOwnedResources();
		oneOneMaterial?.ReleaseOwnedResources();
		screenAlphaMaterial?.ReleaseOwnedResources();
		screenAdditiveMaterial?.ReleaseOwnedResources();
		screenOneOneMaterial?.ReleaseOwnedResources();
		alphaMaterial = additiveMaterial = oneOneMaterial = screenAlphaMaterial = screenAdditiveMaterial = screenOneOneMaterial = null;
		atlasTexture?.ReleaseOwned();
		atlasTexture = null;
		var buffer = vertexBuffer;
		vertexBuffer = null;
		if ( buffer != null )
			global::Global.Render.ScheduleDelete( buffer.Dispose );
	}
}
