namespace OpenTPW;

public class Sky : ModelEntity
{
	public override void Spawn()
	{
		base.Spawn();

		var texture = new Texture( [79, 214, 255, 255], 1, 1 );

		var material = new Material<ObjectUniformBuffer>( "content/shaders/unlit.shader" );
		material.Set( "Color", texture );

		Model = Primitives.Cube.GenerateModel( material );
		Scale = new Vector3( -10000f );
		// The camera is always inside the sky box, so testing it against the view would never skip the draw.
		Cullable = false;
	}
}
