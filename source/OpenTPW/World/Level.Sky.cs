using Veldrid;
namespace OpenTPW;

public partial class Level
{
	private Sky? sky;
	private bool skyLoaded;
	private RgbaByte? backdropColour;

	/// <summary>
	/// The colour the world pass is cleared to before the sky: the level's fog colour, which the sky's haze and the distant objects fade into
	/// (<c>ThemeEngine.FogColour</c>, read once).
	/// </summary>
	internal RgbaByte BackdropColour => backdropColour ??= Sky.FogColour( LevelName );

	/// <summary>
	/// Draws the level's sky (docs/UI.md, UI-107) behind everything else; the sky is loaded on the first draw so a level that is only built
	/// and never shown (a staged resume candidate) loads no textures. Nothing is drawn for a level without sky files.
	/// </summary>
	internal void DrawSky()
	{
		if ( !skyLoaded )
		{
			skyLoaded = true;
			sky = Sky.TryCreate( LevelName );
		}
		sky?.Draw();
	}

	private void DisposeSky()
	{
		sky?.Dispose();
		sky = null;
	}
}
