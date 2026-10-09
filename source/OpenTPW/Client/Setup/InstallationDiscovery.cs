using System.Diagnostics;
using System.Text.Json;

namespace OpenTPW;

/// <summary>Setup operation metadata; dialog selections are inspected separately before use.</summary>
internal sealed record InstallationDiscoveryResult( IReadOnlyList<InstallationReport> Reports, bool TimedOut, string? Error );

/// <summary>
/// Filesystem probes can block inside the OS on an unavailable mounted drive. Isolate discovery
/// and inspection in killable children, rather than accumulating uncancellable worker threads.
/// At most one search, one inspection and one user-controlled picker can be outstanding, so a kernel-stalled automatic
/// search cannot prevent the player from validating an available local folder.
/// </summary>
internal static class InstallationDiscovery
{
	internal const string SearchCommand = "--internal-setup-search";
	internal const string InspectCommand = "--internal-setup-inspect";
	internal const string PickerCommand = "--internal-setup-picker";
	internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds( 3 );
	private static readonly SemaphoreSlim SearchSlot = new( 1, 1 );
	private static readonly SemaphoreSlim InspectionSlot = new( 1, 1 );
	private static readonly SemaphoreSlim PickerSlot = new( 1, 1 );

	internal static int? RunChild( string[] args )
	{
		if ( args.Length == 0 || args[0] is not (SearchCommand or InspectCommand or PickerCommand) )
			return null;
		if ( args[0] == SearchCommand && args.Length != 1 || args[0] == InspectCommand && args.Length != 2 || args[0] == PickerCommand && args.Length != 3 )
			return 2;
		if ( args[0] == PickerCommand )
		{
			if ( FolderPicker.Pick( args[1], args[2].Length > 0 ? args[2] : null ) is { } chosen )
				Console.WriteLine( JsonSerializer.Serialize( new InstallationReport( chosen, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>() ) ) );
			return 0;
		}
		var paths = args[0] == SearchCommand ? InstallationFinder.GetCandidates() : new[] { args[1] };
		var count = 0;
		foreach ( var path in paths )
		{
			var report = GameInstallation.Inspect( path );
			if ( args[0] == InspectCommand || report.IsUsable )
			{
				Console.WriteLine( JsonSerializer.Serialize( report ) );
				Console.Out.Flush();
				if ( ++count == 32 )
					break;
			}
		}
		return 0;
	}

	internal static Task<InstallationDiscoveryResult> SearchAsync( TimeSpan? timeout = null, CancellationToken cancellation = default ) =>
		RunAsync( ChildStart( SearchCommand ), timeout ?? DefaultTimeout, cancellation, search: true );

	internal static Task<InstallationDiscoveryResult> InspectAsync( string path, TimeSpan? timeout = null, CancellationToken cancellation = default ) =>
		RunAsync( ChildStart( InspectCommand, path ), timeout ?? DefaultTimeout, cancellation );

	internal static Task<InstallationDiscoveryResult> PickAsync( string title, string? initial, CancellationToken cancellation ) =>
		RunAsync( ChildStart( PickerCommand, title, initial ?? "" ), Timeout.InfiniteTimeSpan, cancellation, picker: true );

	private static ProcessStartInfo ChildStart( params string[] args )
	{
		var executable = Environment.ProcessPath ?? throw new InvalidOperationException( "The process executable is unavailable." );
		var info = new ProcessStartInfo( executable ) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
		if ( string.Equals( Path.GetFileNameWithoutExtension( executable ), "dotnet", StringComparison.OrdinalIgnoreCase ) )
			info.ArgumentList.Add( typeof( Program ).Assembly.Location );
		foreach ( var argument in args )
			info.ArgumentList.Add( argument );
		return info;
	}

	/// <summary>Internal process seam also exercises timeout/cancellation without probing live mounts.</summary>
	internal static async Task<InstallationDiscoveryResult> RunAsync( ProcessStartInfo info, TimeSpan timeout, CancellationToken cancellation = default, bool search = false, bool picker = false )
	{
		var slot = picker ? PickerSlot : search ? SearchSlot : InspectionSlot;
		Process? process = null;
		var ownsSlot = false;
		var reports = new List<InstallationReport>();
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource( cancellation );
		deadline.CancelAfter( timeout );
		try
		{
			await slot.WaitAsync( deadline.Token ).ConfigureAwait( false );
			ownsSlot = true;
			deadline.Token.ThrowIfCancellationRequested();
			process = Process.Start( info ) ?? throw new InvalidOperationException( "Installation search could not start." );
			var stderr = process.StandardError.ReadToEndAsync( deadline.Token );
			while ( await process.StandardOutput.ReadLineAsync( deadline.Token ).ConfigureAwait( false ) is { } line )
			{
				if ( line.Length > 65536 || reports.Count == 32 )
					throw new InvalidDataException( "Installation search output exceeded its bound." );
				if ( JsonSerializer.Deserialize<InstallationReport>( line ) is { } report )
					reports.Add( report );
			}
			await process.WaitForExitAsync( deadline.Token ).ConfigureAwait( false );
			var error = await stderr.ConfigureAwait( false );
			return new( reports, false, process.ExitCode == 0 ? null : error.Trim() );
		}
		catch ( OperationCanceledException )
		{
			return new( reports, !cancellation.IsCancellationRequested, cancellation.IsCancellationRequested ? "Search cancelled." : "Automatic search timed out." );
		}
		catch ( Exception exception ) when ( exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or JsonException )
		{
			return new( reports, false, exception.Message );
		}
		finally
		{
			if ( process != null )
			{
				// Transfer ownership before requesting termination. Kill/status errors must
				// neither override the result nor release an unconfirmed live-process slot.
				var held = process;
				process = null;
				_ = ReleaseAfterExitAsync( () => held.Kill( entireProcessTree: true ), () => held.WaitForExitAsync(), held.Dispose, slot );
			}
			else if ( ownsSlot )
				slot.Release();
		}
	}

	internal static async Task ReleaseAfterExitAsync( Action kill, Func<Task> waitForExit, Action dispose, SemaphoreSlim slot )
	{
		try { kill(); }
		catch ( Exception exception ) when ( exception is InvalidOperationException or System.ComponentModel.Win32Exception )
		{
			Trace.TraceWarning( $"Installation probe termination: {exception.Message}" );
		}
		try { await waitForExit().ConfigureAwait( false ); }
		catch ( Exception exception ) when ( exception is InvalidOperationException or System.ComponentModel.Win32Exception )
		{
			// Exit is unconfirmed. Retain the slot rather than admitting another child.
			Trace.TraceWarning( $"Installation probe exit could not be confirmed: {exception.Message}" );
			return;
		}
		dispose();
		slot.Release();
	}
}
