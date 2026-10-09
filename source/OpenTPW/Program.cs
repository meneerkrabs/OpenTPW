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
			return 0;
		}
		catch ( Exception exception )
		{
			Console.Error.WriteLine( exception );
			return 1;
		}
	}
}
