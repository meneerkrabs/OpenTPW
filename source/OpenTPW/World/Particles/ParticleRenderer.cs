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
// [APPROX:PTCL-905] OpenTPW groups the quads into three blend batches (alpha, source-alpha-over-one, one-plus-one), each in emitter slot order, depth-tested without depth writes and unsorted; the original submits one primitive per particle in that order, and whether its engine queue depth-sorts (tp.exe 0x565F90) is untraced; untextured particles with neither flag draw as alpha (the original's 0x1256 / 0x556 states are not mapped)
public sealed class ParticleRenderer : Entity
{
	private const int VerticesPerQuad = 6;
	/// <summary>The width factor of an untextured particle; the original multiplies by the projection's aspect (tp.exe DAT_008BCBCC, value untraced).</summary>
	private const float UntexturedWidthFactor = 1f;
	/// <summary>Cull margin around an emitter's particle bounds, in 1/64 original units (a particle's own size).</summary>
	private const int CullMargin = 16 * 64;

	/// <summary>The renderer of the current level, or null.</summary>
	public static ParticleRenderer? Current { get; private set; }

	private readonly ParticleSystem system;
	private readonly ParticleSpriteAtlas atlas;
	private readonly Func<SVector3, SVector3> originalToEngine;
	private readonly Func<bool> paused;
	private readonly float scale;
	private readonly List<ParticleSprite> sprites = new();
	private readonly List<ParticleEmitterBounds> bounds = new();
	private readonly HashSet<int> visibleEmitters = new();
	private readonly Vertex[] vertices;
	private readonly (int First, int Count)[] ranges = new (int First, int Count)[3];
	private Texture? atlasTexture;
	private Material<ObjectUniformBuffer>? alphaMaterial;
	private Material<ObjectUniformBuffer>? additiveMaterial;
	private Material<ObjectUniformBuffer>? oneOneMaterial;
	private DeviceBuffer? vertexBuffer;
	private bool deleted;

	/// <summary>Live particles and quads drawn in the last frame (smoke instrumentation).</summary>
	public int DrawnParticles { get; private set; }
	public ParticleSystem Particles => system;

	public ParticleRenderer( ParticleSystem system, ParticleSpriteAtlas atlas, Func<SVector3, SVector3> originalToEngine, Func<bool> paused,
		float engineUnitsPerOriginal = OriginalParkPlacement.ModelScale )
	{
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
			alphaMaterial = Prepare( atlasTexture, MaterialFlags.DisableDepthWrite | MaterialFlags.DoubleSided );
			additiveMaterial = Prepare( atlasTexture, MaterialFlags.DisableDepthWrite | MaterialFlags.DoubleSided | MaterialFlags.AdditiveBlend );
			oneOneMaterial = Prepare( atlasTexture, MaterialFlags.DisableDepthWrite | MaterialFlags.DoubleSided | MaterialFlags.OneOneBlend );
		}
		catch
		{
			Delete();
			throw;
		}
		Current = this;
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

		var vertexCount = 0;
		for ( var batchIndex = 0; batchIndex < ranges.Length; batchIndex++ )
		{
			var batch = (Batch)batchIndex;
			var first = vertexCount;
			foreach ( var sprite in sprites )
			{
				if ( BatchOf( sprite ) != batch || !visibleEmitters.Contains( sprite.Emitter ) )
					continue;
				if ( vertexCount + VerticesPerQuad > vertices.Length )
					break;
				if ( AddQuad( sprite, batch, right, up, ref vertexCount ) )
					DrawnParticles++;
			}
			ranges[(int)batch] = (first, vertexCount - first);
		}
		if ( vertexCount == 0 )
			return;

		var stride = (uint)Marshal.SizeOf<Vertex>();
		vertexBuffer ??= Device.ResourceFactory.CreateBuffer( new BufferDescription( (uint)vertices.Length * stride, BufferUsage.VertexBuffer | BufferUsage.Dynamic ) );
		var commandList = global::Global.Render.CommandList;
		commandList.UpdateBuffer( vertexBuffer, 0, ref vertices[0], (uint)vertexCount * stride );
		commandList.SetVertexBuffer( 0, vertexBuffer );
		var eye = Camera.Position.GetSystemVector3();
		if ( ranges[0].Count > 0 )
			Draw( commandList, alphaMaterial!, eye, ranges[0] );
		if ( ranges[1].Count > 0 )
			Draw( commandList, additiveMaterial!, eye, ranges[1] );
		if ( ranges[2].Count > 0 )
			Draw( commandList, oneOneMaterial!, eye, ranges[2] );
	}

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
		// [APPROX:PTCL-904] the width factor is the frame record's float +8 (textured) or the projection aspect DAT_008BCBCC (untextured) in the original; neither is traced, so a sprite uses its frame's pixel aspect and an untextured quad 1
		var (halfWidth, halfHeight) = HalfExtents( sprite.Size, scale, sprite.FrameCount, found.Width, found.Height );
		if ( !(halfWidth > 0 && halfHeight > 0) )
			return false;
		var angle = sprite.Spin * (MathF.Tau / 65536f);
		var (sin, cos) = MathF.SinCos( angle );
		var across = (right * cos + up * sin) * halfWidth;
		var vertical = (up * cos - right * sin) * halfHeight;

		var colour = sprite.Colour;
		var alpha = (int)(colour >> 24);
		var red = (int)((colour >> 16) & 0xFF);
		var green = (int)((colour >> 8) & 0xFF);
		var blue = (int)(colour & 0xFF);
		if ( batch == Batch.OneOne )
		{
			// one plus one adds the colour as is: the particle's alpha scales it instead of blending it
			red = red * alpha / 255; green = green * alpha / 255; blue = blue * alpha / 255; alpha = 255;
		}
		var tint = new SVector3( red, green, blue + 256 * alpha );

		Vertex Corner( SVector3 position, float u, float v ) => new( position, tint, new SVector2( u, v ) );
		var topLeft = Corner( centre - across + vertical, found.U0, found.V0 );
		var topRight = Corner( centre + across + vertical, found.U1, found.V0 );
		var bottomLeft = Corner( centre - across - vertical, found.U0, found.V1 );
		var bottomRight = Corner( centre + across - vertical, found.U1, found.V1 );
		vertices[vertexCount++] = topLeft; vertices[vertexCount++] = topRight; vertices[vertexCount++] = bottomLeft;
		vertices[vertexCount++] = topRight; vertices[vertexCount++] = bottomRight; vertices[vertexCount++] = bottomLeft;
		return true;
	}

	private static void Draw( CommandList commandList, Material<ObjectUniformBuffer> material, SVector3 eye, (int First, int Count) range )
	{
		material.Set( "ObjectUniformBuffer", new ObjectUniformBuffer
		{
			g_mModel = System.Numerics.Matrix4x4.Identity,
			g_mView = Camera.ViewMatrix,
			g_mProj = Camera.ProjMatrix,
			g_vCameraPos = eye,
			g_flTime = Time.Now
		} );
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
		if ( ReferenceEquals( Current, this ) )
			Current = null;
		alphaMaterial?.ReleaseOwnedResources();
		additiveMaterial?.ReleaseOwnedResources();
		oneOneMaterial?.ReleaseOwnedResources();
		alphaMaterial = additiveMaterial = oneOneMaterial = null;
		atlasTexture?.ReleaseOwned();
		atlasTexture = null;
		var buffer = vertexBuffer;
		vertexBuffer = null;
		if ( buffer != null )
			global::Global.Render.ScheduleDelete( buffer.Dispose );
	}
}
