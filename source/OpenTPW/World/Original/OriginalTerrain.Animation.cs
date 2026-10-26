namespace OpenTPW;

/// <summary>The terrain's texture-coordinate clip (<see cref="TerrainUvAnimator"/>): kept apart from the terrain's build.</summary>
public sealed partial class OriginalTerrain
{
	private TerrainUvAnimator? uvAnimator;

	/// <summary>The park's rate-scaled clock in milliseconds (<see cref="SceneClock"/>); null reads the real frame time. The clip stands still while the park is paused.</summary>
	internal Func<uint>? SceneMilliseconds { get; set; }

	/// <summary>The terrain's clip animator, or null without a base clip (every theme but the Jungle); for tests.</summary>
	internal TerrainUvAnimator? UvAnimator => uvAnimator;

	protected override void OnUpdate() => uvAnimator?.Update( SceneMilliseconds?.Invoke(), Time.Delta );
}
