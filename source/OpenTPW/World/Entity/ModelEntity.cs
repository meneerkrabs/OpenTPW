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

	protected override void OnRender()
	{
		if ( Model == null || Hidden )
			return;

		var model = TransformOverride ?? ModelMatrix;
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
			g_vLightColor = Level?.SunLight?.Color ?? Vector3.One,
			g_vCameraPos = Camera.Position,
			g_flTime = Time.Now,

			_padding0 = 0,
			_padding1 = 0,
			// test.shader g_flViewDistanceScale: fog distance multiplier from the graphics settings (1 = unchanged).
			_padding2 = ViewDistanceScale ?? CompatibilityRuntime.RenderQuality.ViewDistanceScale,
		};

		Model.Material.Set( "ObjectUniformBuffer", uniformBuffer );
		Model.Draw();
	}
}
