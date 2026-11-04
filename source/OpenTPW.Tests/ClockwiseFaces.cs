namespace OpenTPW.Tests;

/// <summary>Synthetic UI models are drawn only when their triangles are clockwise on the screen (tp.exe's face test 0x581320): this turns the shared synthetic quad's faces around.</summary>
internal static class ClockwiseFaces
{
	public static ModelFile Of( ModelFile model )
	{
		foreach ( var mesh in model.Meshes )
			for ( var face = 0; face + 2 < mesh.Indices.Length; face += 3 )
				(mesh.Indices[face + 1], mesh.Indices[face + 2]) = (mesh.Indices[face + 2], mesh.Indices[face + 1]);
		return model;
	}
}
