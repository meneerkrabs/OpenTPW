namespace OpenTPW;

/// <summary>
/// Program entry point
/// </summary>
public class Program
{
	public static int Main( string[] args )
	{
		try
		{
			if ( InstallationDiscovery.RunChild( args ) is { } childExitCode )
				return childExitCode;
			LinuxNativeLibraries.Register();
			Game.Run( args );
			// Headless commands such as --m3-gate report failure through the exit code.
			return Environment.ExitCode;
		}
		catch ( Exception exception )
		{
			Console.Error.WriteLine( exception );
			return 1;
		}
	}
}
