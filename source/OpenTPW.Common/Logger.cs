namespace OpenTPW;

/// <summary>
/// Handles all debug logging functionality.
/// </summary>
public class Logger
{
	public enum Level
	{
		Trace,
		Info,
		Warning,
		Error
	};

	public void Trace( object obj ) => Log( obj?.ToString(), Level.Trace );
	public void Info( object obj ) => Log( obj?.ToString(), Level.Info );
	public void Info( object obj, bool quiet = false ) => Log( obj?.ToString(), Level.Info, quiet );
	public void Warning( object obj ) => Log( obj?.ToString(), Level.Warning );
	public void Error( object obj ) => Log( obj?.ToString(), Level.Error );

	public delegate void LogDelegate( Level severity, string logText );
	public static LogDelegate? OnLog;
	public static LogDelegate? QuietLog;

	[ThreadStatic] private static List<(Level Severity, string Text, bool Quiet)>? captured;

	/// <summary>
	/// Holds what this thread logs until the returned scope is disposed, instead of writing it: work done ahead of time (a park build's preparation steps) can hand its
	/// messages to <see cref="Replay"/> when its result is used, so the log reads as if the work had run then; messages of work whose result is never used are dropped.
	/// </summary>
	public static CaptureScope BeginCapture()
	{
		var scope = new CaptureScope( captured );
		captured = scope.Messages;
		return scope;
	}

	/// <summary>Writes messages held by a <see cref="BeginCapture"/> scope, in their order.</summary>
	public static void Replay( IEnumerable<(Level Severity, string Text, bool Quiet)> messages )
	{
		foreach ( var (severity, text, quiet) in messages )
			Log( text, severity, quiet );
	}

	/// <summary>An open <see cref="BeginCapture"/>; disposing it restores the capture it was opened in (or none).</summary>
	public sealed class CaptureScope : IDisposable
	{
		private readonly List<(Level Severity, string Text, bool Quiet)>? outer;
		internal CaptureScope( List<(Level Severity, string Text, bool Quiet)>? outer ) => this.outer = outer;
		public List<(Level Severity, string Text, bool Quiet)> Messages { get; } = new();
		public void Dispose() => captured = outer;
	}

	private static void Log( string? str, Level severity = Level.Trace, bool quiet = false )
	{
		if ( str == null )
			return;

		if ( captured != null )
		{
			captured.Add( (severity, str, quiet) );
			return;
		}

		if( quiet )
			QuietLog?.Invoke( severity, str );

		// The browser console has no colours (docs/WEB.md).
		if ( !OperatingSystem.IsBrowser() )
			Console.ForegroundColor = SeverityToConsoleColor( severity );
		Console.WriteLine( $"[{DateTime.Now.ToLongTimeString()}] {str}" );

		OnLog?.Invoke( severity, str );
	}

	private static ConsoleColor SeverityToConsoleColor( Level severity ) => severity switch
	{
		Level.Error => ConsoleColor.DarkRed,
		Level.Warning => ConsoleColor.Red,
		Level.Trace => ConsoleColor.DarkGray,
		_ => ConsoleColor.White,
	};
}
