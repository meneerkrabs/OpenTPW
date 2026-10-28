using Veldrid;

namespace OpenTPW;

public partial class ModelEntity : Entity
{
	public Model? Model { get; set; }

	/// <summary>Explicit model matrix (e.g. an MD2 node's composed hierarchy transform); overrides Position/Rotation/Scale.</summary>
	public System.Numerics.Matrix4x4? TransformOverride { get; set; }

	/// <summary>Not drawn while set (a scene that is kept loaded but not shown, or an MD2 mesh whose node-state bit 0x10 is set).</summary>
	public bool Hidden { get; set; }

	/// <summary>The model matrix the draw uses.</summary>
	internal System.Numerics.Matrix4x4 DrawMatrix => TransformOverride ?? ModelMatrix;

	/// <summary>Light position for scenes without a level sun (e.g. the online globe).</summary>
	public Vector3? LightPosition { get; set; }

	/// <summary>Fog distance multiplier for scenes that are not the park (e.g. the online globe, which has no fog).</summary>
	public float? ViewDistanceScale { get; set; }

	/// <summary>Skips the draw while the model's bounds lie wholly outside the view (off for the sky box around the camera).</summary>
	public bool Cullable { get; set; } = true;

	/// <summary>
	/// Called when the draw finds the model inside the view, before the first draw of the frame, as the original's mesh prepare calls the render callback of the mesh object
	/// (<see cref="OriginalObject.PresentDrawn"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x57A750 mesh prepare] the prepare of a mesh object (a virtual method; the thunk 0x57AC63 reaches it, which is why no instruction calls through +0x140) loads the callback word the render callback 0x463030 was stored in at +0x140 (0x4638BD) and calls it with the mesh object (0x57A7E6..0x57A7FC) when the object's frame word differs from the current frame's [0x8BCC40] (0x57A7ED), so once per frame, before the object's meshes are tested one by one (0x57A5A6..0x57A685, 0x561A30) and drawn
	internal Action? Prepared { get; set; }

	/// <summary>Model parts drawn and skipped as outside the view since the counters were last read (<c>--frame-stats</c>).</summary>
	internal static int DrawnParts, CulledParts;

	public ModelEntity()
	{
		Spawn();
	}

	public virtual void Spawn()
	{

	}

	/// <summary>
	/// [EXT:evening] Whether the evening extension darkens this entity at dusk and night (the sun colour and the ambient it draws with). The build tools' placement markers say no, so they stay readable.
	/// </summary>
	protected virtual bool FollowsTimeOfDay => true;

	/// <summary>
	/// [EXT:winter-snow-surfaces] How much of the park's settled snow cover (<see cref="Level.SnowUniform"/>) this entity shows, written per draw into test.shader's <c>g_flSnowCover</c>:
	/// 0 (the default) shows none, <see cref="SnowCover.GroundSurface"/> is full cover on up-facing surfaces, a negative value (<see cref="SnowCover.PathSurface"/>) reads as slush. Only entities
	/// drawn with test.shader set it (ground, paths, objects, track and coaster pieces); cars, markers, signs, lobby scenes and every other shader keep 0, so their draws are unchanged.
	/// </summary>
	public float SnowSurface { get; set; }

	/// <summary>
	/// [EXT:winter-snow-surfaces] The snow noise follows the model's own coordinates instead of the world's: for parts on an animated node of a ride or object, so the patches turn and move with the part
	/// (world-space noise would crawl over a rotating part). Written as 2 added to a positive cover; test.shader reads a cover above 1.5 as "model space".
	/// </summary>
	public bool SnowNoiseInModelSpace { get; set; }

	private float SnowUniformFor( float cover )
	{
		var value = SnowSurface != 0f ? cover * SnowSurface : 0f;
		return value > 0f && SnowNoiseInModelSpace ? value + 2f : value;
	}

	protected override void OnRender()
	{
		if ( Model == null || Hidden )
			return;

		var model = DrawMatrix;
		// [BIN:TPW-EXE:0x561A30 mesh view test] the original clips the eight corners of the mesh record's box (widened by half its extent per side) and skips the mesh when all lie outside one plane; the exact vertex bounds used here are tighter, and every stored box of the corpus contains its vertices (Md2MeshBoundsCorpusTests), so only meshes wholly out of view are skipped
		if ( Cullable && Model.Bounds is { } bounds && Frustum.IsOutside( bounds.Min, bounds.Max, model * Camera.ViewMatrix * Camera.ProjMatrix ) )
		{
			CulledParts++;
			return;
		}
		DrawnParts++;
		Prepared?.Invoke();

		if ( Model.HasGeometry )
			DrawModel( Model, model );
		// [BIN:TPW-EXE:0x582AD0 deferred records] the faces with a translucent texture are drawn after everything else, far to near (WorldPass); outside a level's pass (nothing collects them) they are drawn here
		for ( var index = 0; index < Model.Companions.Count; index++ )
		{
			var companion = Model.Companions[index];
			// a slot whose texture frame is not translucent now (a texture-frame animation) is drawn at once, like the original's immediate state words
			if ( !Model.CompanionIsTranslucent( index ) || !WorldPass.Submit( this, companion, model ) )
				DrawModel( companion, model );
		}
	}

	/// <summary>Draws <paramref name="drawn"/> (this entity's model or one of its translucent companions) with this entity's uniforms at <paramref name="model"/>.</summary>
	internal void DrawModel( Model drawn, System.Numerics.Matrix4x4 model )
	{
		// [BIN:TPW-EXE:0x565590 -> 0x565490 deferred batch] the triangles of a translucent part are drawn far to near by the sum of their corner depths
		drawn.OrderTriangles( model, model * Camera.ViewMatrix * Camera.ProjMatrix, WorldPass.PassId );

		var uniformBuffer = new ObjectUniformBuffer
		{
			g_mModel = model,
			g_mView = Camera.ViewMatrix,
			g_mProj = Camera.ProjMatrix,
			g_vLightPos = LightPosition ?? Level?.SunLight?.Position ?? Vector3.Zero,
			g_vLightColor = FollowsTimeOfDay ? Level?.SunLight?.Color ?? Vector3.One : Vector3.One,
			// [EXT:evening] test.shader never reads the camera position: at dusk and night it carries the tint of a self-lit material that is no light source (the shader reads it only while the tinted ambient is set)
			g_vCameraPos = FollowsTimeOfDay && Level?.Look is { IsDay: false } look ? look.UnlitTint : Camera.Position,
			g_flTime = Time.Now,

			// [EXT:evening] g_flAmbientLevel: 0 (unchanged) unless the evening extension has darkened the park
			_padding0 = FollowsTimeOfDay ? Level?.Look.AmbientUniform ?? 0f : 0f,
			_padding1 = 0,
			// test.shader g_flViewDistanceScale: fog distance multiplier from the graphics settings (1 = unchanged).
			_padding2 = ViewDistanceScale ?? CompatibilityRuntime.RenderQuality.ViewDistanceScale,
			// [EXT:evening] test.shader's tinted ambient (zero at day and with the extension off)
			_padding3 = FollowsTimeOfDay ? ( Level?.Look.AmbientColourUniform.X ?? 0f ) : 0f,
			_padding4 = FollowsTimeOfDay ? ( Level?.Look.AmbientColourUniform.Y ?? 0f ) : 0f,
			_padding5 = FollowsTimeOfDay ? ( Level?.Look.AmbientColourUniform.Z ?? 0f ) : 0f,
			// [EXT:winter-snow-surfaces] test.shader's g_flSnowCover: the park's cover times this entity's surface factor (0 with no snow, which is what the padding always was)
			_padding6 = SnowUniformFor( Level?.SnowUniform ?? 0f ),
		}.WithFog();

		SetObjectUniforms( uniformBuffer, model, drawn );
		drawn.Draw( model );
	}

	/// <summary>
	/// Writes the draw's uniform block. A draw with point lights in reach (<see cref="PointLightSet.Select"/> over its world box) writes the lights in the same upload; with none
	/// in view, or on a shader that does not read them, it is the plain block as it always was.
	/// </summary>
	// [EXT:point-lights] the original's engine supports point lights but never creates one (docs/LIGHTS.md)
	private protected void SetObjectUniforms( in ObjectUniformBuffer uniforms, in System.Numerics.Matrix4x4 model ) => SetObjectUniforms( uniforms, model, Model! );

	private void SetObjectUniforms( in ObjectUniformBuffer uniforms, in System.Numerics.Matrix4x4 model, Model drawn )
	{
		var material = drawn.Material;
		if ( material.ReceivesPointLights && Level?.PointLights is { Count: > 0 } lights && drawn.Bounds is { } bounds )
		{
			PointLightSet.TransformBox( bounds.Min, bounds.Max, model, out var boxMin, out var boxMax );
			Span<PointLightGpu> chosen = stackalloc PointLightGpu[PointLightSet.MaxPerDraw];
			var count = lights.Select( boxMin, boxMax, Camera.Position.GetSystemVector3(), chosen );
			if ( count > 0 )
			{
				// the 784-byte block is built only for a draw that a light reaches
				var lit = new LitObjectUniformBuffer { Base = uniforms };
				chosen[..count].CopyTo( lit.Lights );
				material.Set( "ObjectUniformBuffer", ref lit, count );
				return;
			}
		}
		material.SetUnchanged( "ObjectUniformBuffer", uniforms );
	}
}
