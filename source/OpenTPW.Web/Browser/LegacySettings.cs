namespace OpenTPW;

/// <summary>
/// Browsers have no desktop user.config. Keep the shared audio and game-files callers on their
/// setup.json fallback without compiling or loading System.Configuration into the browser.
/// </summary>
public static class LegacySettings
{
	public static string? GamePath => null;
}
