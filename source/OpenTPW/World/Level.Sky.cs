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
	/// [EXT:evening] The backdrop and fog colour the world pass really uses: <see cref="BackdropColour"/> at full day (and with the extension off), darkened and tinted by the look of the moment at dusk and night.
	/// </summary>
	internal RgbaByte WorldBackdropColour
	{
		get
		{
			var colour = BackdropColour;
			if ( Look.IsDay )
				return colour;
			var tint = Look.FogTint;
			byte Scale( byte channel, float factor ) => (byte)Math.Clamp( (int)MathF.Round( channel * factor ), 0, 255 );
			return new RgbaByte( Scale( colour.R, tint.X ), Scale( colour.G, tint.Y ), Scale( colour.B, tint.Z ), colour.A );
		}
	}

	/// <summary>
	/// Draws the level's sky (docs/UI.md, UI-107) behind everything else; the sky is loaded on the first draw so a level that is only built
	/// and never shown (a staged resume candidate) loads no textures. Nothing is drawn for a level without sky files.
	/// </summary>
	internal void DrawSky()
	{
		PrepareSky();
		sky?.Draw( SceneClock.CappedFrameSeconds, Look.IsDay ? (System.Numerics.Vector3?)null : Look.SkyTint );
	}

	/// <summary>
	/// Loads the sky now if the first draw has not: its textures, colour grid, geometry and materials.
	/// [EXT:park-entry-preparation] a park built over frames runs this in a build step before it is entered, so the frame that enters it does
	/// not (about 4 ms in the browser). The same sky is made, from the same files, and nothing of it depends on the time it is made.
	/// </summary>
	internal void PrepareSky()
	{
		if ( skyLoaded )
			return;
		skyLoaded = true;
		sky = Sky.TryCreate( LevelName );
	}

	/// <summary>Whether the sky has been loaded (by <see cref="PrepareSky"/> or the first draw).</summary>
	internal bool SkyLoaded => skyLoaded;

	private void DisposeSky()
	{
		sky?.Dispose();
		sky = null;
	}
}
