namespace OpenTPW;

/// <summary>
/// [EXT:park-entry-preparation] An entity that can do the one-time work of its first update ahead, in the steps of a park built over frames (the resources it makes on first use:
/// models, textures, materials, meshes), so the frame that enters the park does not. Presentation only: the steps read the park and never change its state, the entity's place among
/// the scene's entities or what its first update draws; whatever they leave undone its first update does as before.
/// </summary>
internal interface IFirstFramePreparation
{
	/// <summary>The work, one part a step.</summary>
	IEnumerable<bool> PrepareFirstFrame();
}
