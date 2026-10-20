using Veldrid;

namespace OpenTPW;

public partial class ModelEntity : Entity
{
	public Model? Model { get; set; }

	/// <summary>Explicit model matrix (e.g. an MD2 node's composed hierarchy transform); overrides Position/Rotation/Scale.</summary>
	public System.Numerics.Matrix4x4? TransformOverride { get; set; }

	/// <summary>Not drawn while set (a scene that is kept loaded but not shown, or an MD2 mesh whose node-state bit 0x10 is set).</summary>
	public bool Hidden { get; set; }

	/// <summary>Light position for scenes without a level sun (e.g. the online globe).</summary>
	public Vector3? LightPosition { get; set; }

	/// <summary>Fog distance multiplier for scenes that are not the park (e.g. the online globe, which has no fog).</summary>
	public float? ViewDistanceScale { get; set; }

	/// <summary>Skips the draw while the model's bounds lie wholly outside the view (off for the sky box around the camera).</summary>
	public bool Cullable { get; set; } = true;

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

		var model = TransformOverride ?? ModelMatrix;
		// [BIN:TPW-EXE:0x561A30 mesh view test] the original clips the eight corners of the mesh record's box (widened by half its extent per side) and skips the mesh when all lie outside one plane; the exact vertex bounds used here are tighter, and every stored box of the corpus contains its vertices (Md2MeshBoundsCorpusTests), so only meshes wholly out of view are skipped
		if ( Cullable && Model.Bounds is { } bounds && Frustum.IsOutside( bounds.Min, bounds.Max, model * Camera.ViewMatrix * Camera.ProjMatrix ) )
		{
			CulledParts++;
			return;
		}
		DrawnParts++;

		var uniformBuffer = new ObjectUniformBuffer
		{
			g_mModel = model,
			g_mView = Camera.ViewMatrix,
			g_mProj = Camera.ProjMatrix,
			g_vLightPos = LightPosition ?? Level?.SunLight?.Position ?? Vector3.Zero,
			g_vLightColor = FollowsTimeOfDay ? Level?.SunLight?.Color ?? Vector3.One : Vector3.One,
			g_vCameraPos = Camera.Position,
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

		SetObjectUniforms( uniformBuffer, model );
		Model.Draw();
	}

	/// <summary>
	/// Writes the draw's uniform block. A draw with point lights in reach (<see cref="PointLightSet.Select"/> over its world box) writes the lights in the same upload; with none
	/// in view, or on a shader that does not read them, it is the plain block as it always was.
	/// </summary>
	// [EXT:point-lights] the original's engine supports point lights but never creates one (docs/LIGHTS.md)
	private protected void SetObjectUniforms( in ObjectUniformBuffer uniforms, in System.Numerics.Matrix4x4 model )
	{
		var material = Model!.Material;
		if ( material.ReceivesPointLights && Level?.PointLights is { Count: > 0 } lights && Model.Bounds is { } bounds )
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
		material.Set( "ObjectUniformBuffer", uniforms );
	}
}
