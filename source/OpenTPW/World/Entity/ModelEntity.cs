using Veldrid;

namespace OpenTPW;

public partial class ModelEntity : Entity
{
	public Model? Model { get; set; }

	/// <summary>Explicit model matrix (e.g. an MD2 node's composed hierarchy transform); overrides Position/Rotation/Scale.</summary>
	public System.Numerics.Matrix4x4? TransformOverride { get; set; }

	public ModelEntity()
	{
		Spawn();
	}

	public virtual void Spawn()
	{

	}

	protected override void OnRender()
	{
		if ( Model == null )
			return;

		var uniformBuffer = new ObjectUniformBuffer
		{
			g_mModel = TransformOverride ?? ModelMatrix,
			g_mView = Camera.ViewMatrix,
			g_mProj = Camera.ProjMatrix,
			g_vLightPos = Level?.SunLight?.Position ?? Vector3.Zero,
			g_vLightColor = Level?.SunLight?.Color ?? Vector3.One,
			g_vCameraPos = Camera.Position,
			g_flTime = Time.Now,

			_padding0 = 0,
			_padding1 = 0,
			// test.shader g_flViewDistanceScale: fog distance multiplier from the graphics settings (1 = unchanged).
			_padding2 = CompatibilityRuntime.RenderQuality.ViewDistanceScale,
		};

		Model.Material.Set( "ObjectUniformBuffer", uniformBuffer );
		Model.Draw();
	}
}
