using System.Numerics;

namespace OpenTPW;

public class Water : ModelEntity
{
	public override void Spawn()
	{
		var texture = new Texture( "lobby/terrain/textures/jri_lak3.wct", TextureFlags.Wrap );

		var material = new Material<ObjectUniformBuffer>( "content/shaders/water.shader" );
		material.Set( "Color", texture );

		Model = Primitives.Plane.GenerateModel( material );
	}

	protected override void OnRender()
	{
		if ( Model == null )
			return;

		var uniformBuffer = new ObjectUniformBuffer
		{
			g_mModel = ModelMatrix,
			g_mView = Camera.ViewMatrix,
			g_mProj = Camera.ProjMatrix,
			g_vLightPos = Level.SunLight.Position,
			g_vLightColor = Level.SunLight.Color,
			g_vCameraPos = Camera.Position,

			_padding0 = 0,
			_padding1 = 0,
			g_flTime = Time.Now
		}.WithFog();

		Model.Material.Set( "ObjectUniformBuffer", uniformBuffer );
		Model.Draw();
	}
}
