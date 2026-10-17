namespace OpenTPW.ContentKit;

public static class Program
{
	public static int Main( string[] args ) => ContentKitCli.Run( args, Console.Out, Console.Error );
}
