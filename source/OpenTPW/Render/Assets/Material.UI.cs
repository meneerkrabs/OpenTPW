namespace OpenTPW;

partial class Material
{
	/// <summary>
	/// Default shader for 2D UI objects
	/// </summary>
	public static Material UI = new Material( "content/shaders/ui.shader", MaterialFlags.DisableDepth ).MarkAsProcessLifetime();

	/// <summary>
	/// Default shader for 3D objects
	/// </summary>
	public static Material Default = new Material<ObjectUniformBuffer>( "content/shaders/3d.shader" ).MarkAsProcessLifetime();

	/// <summary>
	/// Set on materials every scene shares for the whole process. A rejected candidate's rollback skips
	/// them instead of releasing them like candidate-only assets.
	/// </summary>
	internal bool IsProcessLifetime { get; private set; }

	/// <summary>
	/// Records this material and the shader it owns as process-lifetime shared, at the moment the shared
	/// statics are created. Rollback checks this instance flag instead of comparing against the statics:
	/// a candidate can be the first to create them (so the start snapshot doesn't contain them), reading
	/// them during cleanup could run their initializer and allocate assets mid-rollback, and the shader
	/// registers before its material, so an identity exemption on the material alone would release the
	/// shader out from under it.
	/// </summary>
	internal Material MarkAsProcessLifetime()
	{
		IsProcessLifetime = true;
		Shader.MarkAsProcessLifetime();
		return this;
	}
}
