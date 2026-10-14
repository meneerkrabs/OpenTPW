namespace OpenTPW;

/// <summary>
/// The old <c>System.Configuration</c> user settings (<see cref="Settings"/>), read safely: the browser has no
/// configuration system (reading <see cref="Settings.Default"/> there throws "Configuration system failed to initialize"),
/// and a damaged user.config on the desktop throws the same. Callers get null and fall back to setup.json.
/// </summary>
public static class LegacySettings
{
	/// <summary>The legacy game folder, or null in the browser or when the configuration cannot be read.</summary>
	public static string? GamePath => Read( settings => settings.GamePath );

	private static string? Read( Func<Settings, string?> read )
	{
		if ( OperatingSystem.IsBrowser() )
			return null;
		try
		{
			return read( Settings.Default );
		}
		catch ( Exception exception ) when ( exception is System.Configuration.ConfigurationException or PlatformNotSupportedException or TypeInitializationException )
		{
			Log.Warning( $"The legacy settings could not be read ({exception.Message}); using setup.json only." );
			return null;
		}
	}
}
