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
			try
			{
				Game.Run( args );
			}
			finally
			{
				// the archives' kept files (issue #754) are closed before the process ends, as wherever archives may change
				WadArchive.CloseFileHandles();
			}
			// a normal return is a clean start; an exception skips this and leaves the script mods' crash marker (docs/SCRIPT-MODS.md)
			OpenTPW.Mods.ScriptSafeMode.CleanShutdown();
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
