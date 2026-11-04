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
// [BIN:TPW-EXE:0x57C620 draw driver] one quad per particle in slot order 0..119, newest particle first, each with its own state word (ParticleState.WordOf, decoded by 0x567620); the render flags apply to untextured particles too
// [BIN:TPW-EXE:0x565590 immediate or kept] a batch whose state has bit 0x1000 or 0x2000 (the opaque 0x1256 particles) is drawn when it is submitted; every other one (0x556, 0x200552, 0x200522) is kept with the mean depth of its vertices and drawn by 0x582AD0 far to near together with the translucent world faces (WorldPass)
// [BIN:TPW-EXE:0x565F90 fog bit] every batch gets the state bit 0x100000 (D3DRS_FOGENABLE) while [0x8BCBC8] has 0x2000 (0x565FA6 to 0x565FB7), the particles too: the first-person views fog them with the engine's table fog
// [EXT:particle-blend-runs] OpenTPW draws the opaque quads in the original's order (0x565F90: slots ascending, newest particle first) as runs of one state through its shared material pipeline, and the deferred quads one run of consecutive same-state quads at a time in the sorted order of WorldPass, not one submission with its own state word per particle
// [APPROX:PTCL-905] the triangle sort inside a batch (0x565590 -> 0x565490) is not reproduced: a batch is one particle's two coplanar triangles, so it has no visible effect; a deferred quad's key is the mean z over w of its four corners, the same assumption as WorldTranslucency.Depth for the world's records, and ties keep the order the records were handed over where the original's quicksort (0x5829E0) does not; the point MAGFILTER of flags 0x20000 is COMPAT-010 ([EXT:texture-filter-samplers])
public sealed class ParticleRenderer : Entity
{
	private const int VerticesPerQuad = 6;
	/// <summary>The width factor of an untextured world particle: the original's clip-space factor DAT_008BCBCC (display height / width, 0x582EB0) is the square of an aspect-correct projection.</summary>
	private const float UntexturedWidthFactor = 1f;
	/// <summary>The display height over its width the original's aspect word 0x8BCBCC would hold when the target is empty (4:3).</summary>
	private const float DefaultDisplayAspect = 0.75f;
	/// <summary>Cull margin around an emitter's particle bounds, in 1/64 original units (a particle's own size).</summary>
	private const int CullMargin = 16 * 64;
	/// <summary>The number of draw kinds: the four blends of <see cref="ParticleState.Blend"/> times depth writes off (bit 2) times screen space (bit 3).</summary>
	internal const int KindCount = 16;

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
	// the draw kind of each quad (six vertices each) of the last frame, in the original's draw order: slots ascending, newest particle first
	private readonly int[] quadKinds;
	// and the key of its deferred record: the mean depth of its four corners (0 for a screen-space quad)
	private readonly float[] quadDepths;
	private System.Numerics.Matrix4x4 viewProjection;
	private float lastDepth;
	// consecutive quads of one kind that are drawn at once, as (kind, first vertex, vertex count)
	private readonly List<(int Kind, int First, int Count)> runs = new();
	// the screen-space quads of the frame, drawn last (RenderScreen), when a pass is open
	private readonly List<(int Kind, int First, int Count)> screenRuns = new();
	// the deferred quads copied in the pass's sorted order (one vertex buffer upload per pass, merged draws), their kinds and the pass they belong to
	private readonly Vertex[] sortedVertices;
	private readonly int[] sortedKinds;
	private DeviceBuffer? sortedBuffer;
	private int sortedCount;
	private long sortedPass = -1;
	private Texture? atlasTexture;
	private readonly Material<ObjectUniformBuffer>?[] materials = new Material<ObjectUniformBuffer>?[KindCount];
	private DeviceBuffer? vertexBuffer;
	private bool deleted;
	private int quadCount;
	private int screenQuads;
	// the draw kinds the last frame used: their uniform blocks are written once each
	private int kindsPresent;

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
		quadKinds = new int[system.Capacity];
		quadDepths = new float[system.Capacity];
		sortedVertices = new Vertex[system.Capacity * VerticesPerQuad];
		sortedKinds = new int[system.Capacity];
		Name = "Particles";
		try
		{
			// one atlas texture shared by the materials, which are made when a frame first needs a kind; a failure leaves no half-built entity or assets behind
			atlasTexture = new Texture( atlas.Pixels, atlas.Width, atlas.Height );
			// uploaded: this renderer reads only the frame positions from now on
			atlas.ReleasePixels();
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

	/// <summary>The draw kinds this renderer has made a material for (smoke tests).</summary>
	internal int MaterialCount => materials.Count( material => material != null );

	private Material<ObjectUniformBuffer> MaterialOf( int kind ) => materials[kind] ??= Prepare( atlasTexture!, FlagsForKind( kind ) );

	protected override void OnUpdate()
	{
		var seconds = WeatherVisualState.FrameSeconds( Time.Delta, paused() );
		system.Advance( seconds );
	}

	// [BIN:TPW-EXE:0x57C7C8 blend flags] the flags test (bit 4, then 0x2000) runs for every particle, untextured ones included
	// [BIN:STP-PPC:engine_shared 0x1ACC8 blend] see the class comment
	internal static int BatchIndexOf( int renderFlags ) =>
		(renderFlags & 0x2004) == 0x2004 ? 1
		: (renderFlags & 0x4) != 0 ? 2
		: 0;

	protected override void OnRender()
	{
		DrawnParticles = 0;
		quadCount = 0;
		screenQuads = 0;
		kindsPresent = 0;
		runs.Clear();
		screenRuns.Clear();
		if ( deleted || system.Snapshot( sprites ) == 0 )
			return;
		system.CollectBounds( bounds );
		visibleEmitters.Clear();
		viewProjection = Camera.ViewMatrix * Camera.ProjMatrix;
		foreach ( var emitter in bounds )
			if ( InView( emitter, viewProjection ) )
				visibleEmitters.Add( emitter.Slot );

		var view = Camera.ViewMatrix;
		var right = new SVector3( view.M11, view.M21, view.M31 );
		var up = new SVector3( view.M12, view.M22, view.M32 );

		var target = global::Global.Render.MultisampledFramebuffer;
		var screenAspect = target.Width > 0 ? (float)target.Height / target.Width : 1f;
		var vertexCount = 0;
		foreach ( var sprite in sprites )
		{
			var state = ParticleState.WordOf( sprite );
			var screenSpace = sprite.ScreenSpace;
			// screen-space emitters have no world bounds to cull by (the original leaves their bounds empty)
			if ( !screenSpace && !visibleEmitters.Contains( sprite.Emitter ) )
				continue;
			if ( vertexCount + VerticesPerQuad > vertices.Length )
				break;
			var first = vertexCount;
			var kind = KindOf( state, screenSpace );
			if ( !(screenSpace ? AddScreenQuad( sprite, kind, screenAspect, ref vertexCount ) : AddQuad( sprite, kind, right, up, screenAspect, ref vertexCount )) )
				continue;
			DrawnParticles++;
			if ( screenSpace )
				screenQuads++;
			quadDepths[quadCount] = screenSpace ? 0f : lastDepth;
			quadKinds[quadCount++] = kind;
			kindsPresent |= 1 << kind;
		}
		if ( vertexCount == 0 )
			return;

		var stride = Vertex.SizeInBytes;
		vertexBuffer ??= Device.ResourceFactory.CreateBuffer( new BufferDescription( (uint)vertices.Length * stride, BufferUsage.VertexBuffer | BufferUsage.Dynamic ) );
		var commandList = global::Global.Render.CommandList;
		commandList.UpdateBuffer( vertexBuffer, 0, ref vertices[0], (uint)vertexCount * stride );
		commandList.SetVertexBuffer( 0, vertexBuffer );
		var eye = Camera.Position.GetSystemVector3();
		// [BIN:TPW-EXE:0x576A00 scene pass order] the scene render calls the terrain (0x56F670, 0x570D90), the particle pass (0x57C620, 0x576CBD), the model list loop that queues the advisor's model (0x584B10, 0x584BE0), the sprite flush (0x58A660, 0x576D72), the weather (0x57FF60, 0x576DAB) and the frame flush (0x582170, 0x576DBE); the screen-space quads are drawn in the particle pass with the world's, so everything the HUD windows submit and the advisor's model lie over them in the original too (PTCL-909 retired)
		// [BIN:TPW-EXE:0x48A580 window paint] the frame step 0x4813B0 (0x54E71B, before the scene render) pumps the command queue (0x658E42, 0x48A5BE), posts message 2 to the root window (0x658CCC, 0x48A5D7: the priority queue 0xFAA5F4), sends the tick 0x1E (0x658F6C) and pumps the priority queue to its end (0x658F0B, 0x48A6BC), so the paint runs inside the call: the base handler 0x65C778 paints the children of a visible window (+0x48 bit 0) first, then sends message 3 and calls the window's draw slot (vtable +0); a control's draw queues its text bitmap as a sprite (0x588EF0 builds it, the quads of state 0x7060556, 0x58A26C) and the UI models' nodes the scene's walker (0x576910) lists; nothing is drawn at the paint, the scene pass of the same frame draws them
		// [BIN:TPW-EXE:0x57C620 draw order] one quad per particle in slot order 0..119, newest particle first, world and screen-space particles in the same loop, so the opaque runs follow that order
		// The uniform block is the same for every run of a material in this frame, so it is written once per material, however often the kinds alternate.
		for ( var kind = 0; kind < KindCount; kind++ )
			if ( (kindsPresent & (1 << kind)) != 0 )
				WriteUniforms( MaterialOf( kind ), kind, eye );

		// the opaque quads are drawn here, as the original draws a state with bit 0x1000 when it is submitted; the others join the pass's deferred list when one is open
		// [BIN:TPW-EXE:0x582AD0 deferred records] a kept batch's key is the mean depth of its vertices; a screen-space quad's z is its template's word (0 in every shipped template), the nearest key, so the original draws it after the world's records; OpenTPW draws all of them after the late group (RenderScreen), with no depth test, so they stay on top
		// [BIN:TPW-EXE:0x57C620 screen quads] the screen-space quads are not part of the world's list: they are drawn after the late group (RenderScreen), over everything the scene drew, as before the pass held the particles; without an open pass they are drawn here
		var pass = WorldPass.Active;
		for ( var quad = 0; quad < quadCount; quad++ )
		{
			var kind = quadKinds[quad];
			if ( IsScreenKind( kind ) )
				AddToRuns( screenRuns, kind, quad * VerticesPerQuad, VerticesPerQuad );
			else if ( pass && !IsImmediateKind( kind ) )
				WorldPass.SubmitParticle( this, quad, quadDepths[quad] );
			else
				AddToRuns( runs, kind, quad * VerticesPerQuad, VerticesPerQuad );
		}
		foreach ( var (kind, first, count) in runs )
			Draw( commandList, MaterialOf( kind ), (first, count) );
		if ( !pass )
			RenderScreen();
	}

	/// <summary>Draws the screen-space quads of this frame (no depth): after the scene's late group, so they lie over everything the scene drew.</summary>
	internal void RenderScreen()
	{
		if ( deleted || vertexBuffer == null || screenRuns.Count == 0 )
			return;
		var commandList = global::Global.Render.CommandList;
		commandList.SetVertexBuffer( 0, vertexBuffer );
		foreach ( var (kind, first, count) in screenRuns )
			Draw( commandList, MaterialOf( kind ), (first, count) );
		screenRuns.Clear();
	}

	/// <summary>
	/// The draw kind of a state word: its <see cref="ParticleState.Blend"/> (0 to 3), 4 when it writes no depth, 8 for a screen-space particle.
	/// </summary>
	internal static int KindOf( uint state, bool screenSpace ) =>
		(int)ParticleState.BlendOf( state ) | (ParticleState.WritesDepth( state ) ? 0 : 4) | (screenSpace ? 8 : 0);

	internal static bool IsScreenKind( int kind ) => (kind & 8) != 0;

	/// <summary>The kinds of the opaque states, which are drawn when they are submitted; the others are kept for the sorted list.</summary>
	internal static bool IsImmediateKind( int kind ) => (kind & 3) == (int)ParticleState.Blend.Opaque;

	internal static ParticleState.Blend BlendOfKind( int kind ) => (ParticleState.Blend)(kind & 3);

	internal static MaterialFlags FlagsForKind( int kind )
	{
		var flags = MaterialFlags.DoubleSided;
		flags |= BlendOfKind( kind ) switch
		{
			ParticleState.Blend.Opaque => MaterialFlags.OpaqueBlend,
			ParticleState.Blend.SourceAlphaOne => MaterialFlags.AdditiveBlend,
			ParticleState.Blend.OneOne => MaterialFlags.OneOneBlend,
			_ => MaterialFlags.None
		};
		// screen-space quads sit at the template's z (word +0x52, 0 in every shipped screen-space template): in front of the scene
		if ( IsScreenKind( kind ) )
			return flags | MaterialFlags.DisableDepth;
		return (kind & 4) != 0 ? flags | MaterialFlags.DisableDepthWrite : flags;
	}

	// Host adaptation: the final world-to-output blit alpha-blends; an opaque particle must
	// leave opaque alpha there too. This is not evidence that the native rewrites its alpha byte.
	internal static uint ColourForKind( uint colour, int kind ) => BlendOfKind( kind ) == ParticleState.Blend.Opaque ? colour | 0xFF000000u : colour;

	/// <summary>The kind of a quad of the last frame.</summary>
	internal int QuadKind( int quad ) => quadKinds[quad];

	/// <summary>
	/// Copies a deferred quad of the last frame into the sorted buffer of the pass <paramref name="pass"/> and returns its slot there (slots follow the order the pass hands the quads over, which is its sorted order);
	/// the first quad of a pass starts the buffer anew. <see cref="UploadSorted"/> sends the copies to the GPU once, before the pass's first draw.
	/// </summary>
	internal int AppendSorted( long pass, int quad )
	{
		if ( sortedPass != pass )
		{
			sortedPass = pass;
			sortedCount = 0;
		}
		Array.Copy( vertices, quad * VerticesPerQuad, sortedVertices, sortedCount * VerticesPerQuad, VerticesPerQuad );
		sortedKinds[sortedCount] = quadKinds[quad];
		return sortedCount++;
	}

	/// <summary>Uploads the sorted copies of the pass <paramref name="pass"/> (nothing when it made none).</summary>
	internal void UploadSorted( long pass, CommandList commandList )
	{
		if ( deleted || sortedPass != pass || sortedCount == 0 )
			return;
		var stride = Vertex.SizeInBytes;
		sortedBuffer ??= Device.ResourceFactory.CreateBuffer( new BufferDescription( (uint)sortedVertices.Length * stride, BufferUsage.VertexBuffer | BufferUsage.Dynamic ) );
		commandList.UpdateBuffer( sortedBuffer, 0, ref sortedVertices[0], (uint)sortedCount * stride );
	}

	/// <summary>Draws <paramref name="count"/> consecutive sorted slots from <paramref name="firstSlot"/>: neighbours of one kind are one draw, and the vertex buffer is bound once.</summary>
	internal void DrawSorted( int firstSlot, int count )
	{
		if ( deleted || sortedBuffer == null || count <= 0 )
			return;
		var commandList = global::Global.Render.CommandList;
		commandList.SetVertexBuffer( 0, sortedBuffer );
		var start = firstSlot;
		for ( var slot = firstSlot + 1; slot <= firstSlot + count; slot++ )
		{
			if ( slot < firstSlot + count && sortedKinds[slot] == sortedKinds[start] )
				continue;
			Draw( commandList, MaterialOf( sortedKinds[start] ), (start * VerticesPerQuad, (slot - start) * VerticesPerQuad) );
			start = slot;
		}
	}

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
	public int DrawnScreenParticles => screenQuads;

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

	/// <summary>
	/// Half-width and half-height of a quad in engine units: the height is size / 1024 original units for every particle, the width gets a factor (see <see cref="TexturedWidthFactor"/> and <see cref="UntexturedWidthFactor"/>).
	/// <paramref name="displayAspect"/> is the display's height over its width (the original's aspect word 0x8BCBCC).
	/// </summary>
	internal static (float HalfWidth, float HalfHeight) HalfExtents( int size, float scale, int frameCount, int frameWidth, int frameHeight, float displayAspect = DefaultDisplayAspect )
	{
		var halfHeight = size / 1024f * scale;
		return (halfHeight * (frameCount == 0 || frameHeight <= 0 ? UntexturedWidthFactor : TexturedWidthFactor( frameWidth, frameHeight, displayAspect )), halfHeight);
	}

	/// <summary>
	/// The width over the height of a textured world quad in world units. The original adds the quad's half extents in clip space, the height as is and the width times the frame record's float +8 (the frame's width over its height) with no
	/// display factor, and clip x spans the display's width where clip y spans its height, so the quad is the frame's aspect times the display's width over its height on the screen: in OpenTPW's aspect-correct world that is the frame's
	/// aspect divided by the display aspect word.
	/// </summary>
	// [BIN:TPW-EXE:0x588660 frame aspect] the sprite entry's float +8 is the frame's width / height as loaded (the shorts at +0xC and +0xE; 1.0 for a zero height) by the three frame loaders of the sprite set loader 0x587DB0 (0x588510 at 0x5885E7, 0x588660 at 0x5887A1, 0x588820 at 0x58891E and 0x588935), and no later store rescales it: the other stores of a float to +8 in the sprite module belong to other structures (0x585F10 the sky layer, 0x5890E0, 0x589410 and 0x589B50 the billboard vertex writers)
	// [BIN:TPW-EXE:0x57C71F quad width] half-width = half-height x float +8 for a textured quad (0x57C6B8-0x57C71B) and x [0x8BCBCC] for an untextured one, both added to clip x after the world-to-clip transform 0x578D20 (the full view-projection-screen matrix [0x87B088]): nothing multiplies the textured width by the display's height / width
	internal static float TexturedWidthFactor( int frameWidth, int frameHeight, float displayAspect ) =>
		frameHeight <= 0 ? UntexturedWidthFactor : (float)frameWidth / frameHeight / (displayAspect > 0 && float.IsFinite( displayAspect ) ? displayAspect : DefaultDisplayAspect);

	/// <summary>The half-width of a screen-space quad in clip units: the half-height times the frame's width over its height (textured, no display factor) or times the display's height over its width (untextured, the word 0x8BCBCC).</summary>
	// [BIN:TPW-EXE:0x57C71F screen quad width] flag 0x40000 keeps w = 1 and the position as is, so the half-width is the half-height x float +8 for a textured quad and x [0x8BCBCC] for an untextured one, in clip units that span the width and the height of the display alike
	internal static float ScreenHalfWidth( float halfHeight, int frameCount, int frameWidth, int frameHeight, float displayAspect ) =>
		halfHeight * (frameCount == 0 || frameHeight <= 0 ? displayAspect : (float)frameWidth / frameHeight);

	private bool AddQuad( in ParticleSprite sprite, int kind, SVector3 right, SVector3 up, float displayAspect, ref int vertexCount )
	{
		var frame = sprite.FrameCount == 0 ? atlas.White : atlas.Find( sprite.SpriteCode, sprite.Frame );
		if ( frame is not { } found )
			return false;
		var centre = originalToEngine( new SVector3( sprite.X, sprite.Y, sprite.Z ) / 64f );
		// [BIN:TPW-EXE:0x57C6B8 sprite size] half-height = (size << 1) * (1/1024) * 0.5 = size / 1024 original units for every particle; only the width gets a factor
		// [BIN:TPW-EXE:0x588660 frame aspect] the frame record's float +8 is the frame's width / height (the shorts at +0xC and +0xE), the factor a textured quad's half-width takes; an untextured quad takes DAT_008BCBCC, the display's height / width (0x582EB0)
		// [EXT:particle-world-scale] OpenTPW places a quad's half-height in world units under the camera's projection (the original's world size is the half-height / cos(fov / 2) under its 90 degree camera, 0x42A990 -> 0x578BE0), and an aspect-correct projection gives an untextured quad its square (DAT_008BCBCC, the display's height / width, 0x582EB0): resolution-independent sizing with no original value to copy
		// [BIN:TPW-EXE:0x57C71F quad width] the original adds the half-extents in clip space after its world-to-clip transform (x +- half-width, y +- half-height, w untouched): a textured quad's width factor is the frame record's float +8 (width / height, 0x588660) and nothing rescales it, so the quad is (frame width / height) x (display width / height) on the screen; an untextured quad's is the display's height / width, a square
		var (halfWidth, halfHeight) = HalfExtents( sprite.Size, scale, sprite.FrameCount, found.Width, found.Height, displayAspect );
		if ( !(halfWidth > 0 && halfHeight > 0) )
			return false;
		var angle = sprite.Spin * (MathF.Tau / 65536f);
		var (sin, cos) = MathF.SinCos( angle );
		var across = (right * cos + up * sin) * halfWidth;
		var vertical = (up * cos - right * sin) * halfHeight;

		var tint = Tint( ColourForKind( sprite.Colour, kind ) );

		Vertex Corner( SVector3 position, float u, float v ) => new( position, tint, new SVector2( u, v ) );
		var topLeft = Corner( centre - across + vertical, found.U0, found.V0 );
		var topRight = Corner( centre + across + vertical, found.U1, found.V0 );
		var bottomLeft = Corner( centre - across - vertical, found.U0, found.V1 );
		var bottomRight = Corner( centre + across - vertical, found.U1, found.V1 );
		vertices[vertexCount++] = topLeft; vertices[vertexCount++] = topRight; vertices[vertexCount++] = bottomLeft;
		vertices[vertexCount++] = topRight; vertices[vertexCount++] = bottomRight; vertices[vertexCount++] = bottomLeft;
		// the key of the deferred record: the mean depth of the four corners (the same z over w the world's records use)
		lastDepth = (WorldTranslucency.Depth( centre - across + vertical, viewProjection ) + WorldTranslucency.Depth( centre + across + vertical, viewProjection )
			+ WorldTranslucency.Depth( centre - across - vertical, viewProjection ) + WorldTranslucency.Depth( centre + across - vertical, viewProjection )) * 0.25f;
		return true;
	}

	/// <summary>
	/// A screen-space particle's quad, its vertices in clip space (drawn without the camera): the half-width is the half-height times the frame's width over its height (a textured quad) or the target's
	/// height over its width (an untextured quad, the aspect word 0x8BCBCC), as the original's.
	/// </summary>
	private bool AddScreenQuad( in ParticleSprite sprite, int kind, float screenAspect, ref int vertexCount )
	{
		var frame = sprite.FrameCount == 0 ? atlas.White : atlas.Find( sprite.SpriteCode, sprite.Frame );
		if ( frame is not { } found )
			return false;
		var halfHeight = ParticleScreenSpace.HalfHeight( sprite.Size );
		var halfWidth = ScreenHalfWidth( halfHeight, sprite.FrameCount, found.Width, found.Height, screenAspect );
		if ( !(halfWidth > 0 && halfHeight > 0) )
			return false;
		var (a, b, c, d) = ParticleScreenSpace.Corners( ParticleScreenSpace.Position( sprite.X, sprite.Z ), halfWidth, halfHeight, sprite.Spin );
		var tint = Tint( ColourForKind( sprite.Colour, kind ) );
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

	// [BIN:TPW-EXE:0x565F90 vertex colour] the vertex diffuse is the particle's colour as it is (the batch's colour word [rec+8] ANDed with the all-ones mask [[0x87A248]+0x2C], 0x566019 to 0x56602D), whatever the blend: stage 0 modulates the texture's colour and alpha with it,
	// so a one-plus-one particle adds the texture times the colour's RGB, the alpha only reaching the alpha test (the Mac engine scales the colour by the alpha instead)
	private static SVector3 Tint( uint colour )
	{
		var alpha = (int)(colour >> 24);
		var red = (int)((colour >> 16) & 0xFF);
		var green = (int)((colour >> 8) & 0xFF);
		var blue = (int)(colour & 0xFF);
		return new SVector3( red, green, blue + 256 * alpha );
	}

	// [EXT:evening] the world's alpha-blended and opaque particles (smoke, dust, steam) take the sprite tint at dusk and night like the guests do; the additive ones (fire, sparks, fireworks)
	// and the screen-space ones give off or belong to the screen's own light and are left as they are
	private static void WriteUniforms( Material<ObjectUniformBuffer> material, int kind, SVector3 eye ) =>
		material.Set( "ObjectUniformBuffer", UniformsFor( kind, eye ) );

	/// <summary>The uniform block of a draw kind: the camera (none for a screen-space kind), the alpha test of its state word, the first-person views' fog and the evening tint.</summary>
	internal static ObjectUniformBuffer UniformsFor( int kind, SVector3 eye )
	{
		var screenSpace = IsScreenKind( kind );
		var blend = BlendOfKind( kind );
		var state = blend switch
		{
			ParticleState.Blend.Opaque => ParticleState.Opaque,
			ParticleState.Blend.SourceAlphaOne => ParticleState.SourceAlphaOne,
			ParticleState.Blend.OneOne => ParticleState.OneOne,
			_ => ParticleState.Alpha
		};
		var uniforms = new ObjectUniformBuffer
		{
			g_mModel = System.Numerics.Matrix4x4.Identity,
			g_mView = screenSpace ? System.Numerics.Matrix4x4.Identity : Camera.ViewMatrix,
			g_mProj = screenSpace ? System.Numerics.Matrix4x4.Identity : Camera.ProjMatrix,
			g_vCameraPos = eye,
			g_flTime = Time.Now,
			// [BIN:TPW-EXE:0x567620 alpha test] bit 0x400 of the state enables the test, GREATEREQUAL against 0x10 (0xF0 with bit 0x200): the opaque 0x1256 has none, the blended states 0x556, 0x200552 and 0x200522 test at 0x10
			AlphaReference = ParticleState.AlphaReferenceOf( state )
		};
		// the table fog the first-person views turn on reaches every world quad (0x565F90 adds the fog bit to every batch while [0x8BCBC8] has 0x2000); a screen-space quad has w = 1 and is left out
		if ( !screenSpace )
			uniforms = uniforms.WithFog();
		if ( !screenSpace && blend is ParticleState.Blend.Opaque or ParticleState.Blend.Alpha )
			uniforms.WithSpriteTint( Level.Current?.Look ?? EveningLook.Day );
		return uniforms;
	}

	/// <summary>The particle draw calls since the frame stats were last read (<c>--frame-stats</c>).</summary>
	internal static int FrameDraws;

	private static void Draw( CommandList commandList, Material<ObjectUniformBuffer> material, (int First, int Count) range )
	{
		FrameDraws++;
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
		for ( var kind = 0; kind < KindCount; kind++ )
		{
			materials[kind]?.ReleaseOwnedResources();
			materials[kind] = null;
		}
		atlasTexture?.ReleaseOwned();
		atlasTexture = null;
		var buffer = vertexBuffer;
		vertexBuffer = null;
		if ( buffer != null )
			global::Global.Render.ScheduleDelete( buffer.Dispose );
		var sorted = sortedBuffer;
		sortedBuffer = null;
		if ( sorted != null )
			global::Global.Render.ScheduleDelete( sorted.Dispose );
	}
}
