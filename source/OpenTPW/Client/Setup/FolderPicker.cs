using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenTPW;

/// <summary>
/// Native "choose folder" dialog for the setup screen: osascript on macOS, zenity or kdialog on Linux and
/// IFileOpenDialog on Windows. Nothing is shown when no dialog tool exists; the caller then offers a text field.
/// </summary>
public static class FolderPicker
{
	private const int ErrorCancelled = unchecked((int)0x800704C7);
	private const uint FosPickFolders = 0x20;
	private const uint FosForceFileSystem = 0x40;
	private const int SigdnFileSystemPath = unchecked((int)0x80058000);

	/// <summary>
	/// This platform can offer a native picker. Tool availability is checked inside the
	/// cancellable picker child; querying UI capability never probes PATH or mounted files.
	/// </summary>
	public static bool IsAvailable => OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

	/// <summary>
	/// Shows the native "choose folder" dialog; returns the chosen absolute path, or null when cancelled or unavailable.
	/// Blocks the calling thread; the caller runs it on a background thread.
	/// </summary>
	public static string? Pick( string title, string? initialDirectory )
	{
		try
		{
			if ( OperatingSystem.IsMacOS() )
				return PickMacOs( title, initialDirectory );
			if ( OperatingSystem.IsLinux() )
				return PickLinux( title, initialDirectory );
			if ( OperatingSystem.IsWindows() )
				return PickWindowsOnStaThread( title, initialDirectory );
			return null;
		}
		catch ( Exception exception )
		{
			Log?.Warning( $"Folder picker failed: {exception.Message}" );
			return null;
		}
	}

	internal static string BuildAppleScript( string title, string? initial )
	{
		var script = $"POSIX path of (choose folder with prompt \"{EscapeAppleScript( title )}\"";
		if ( !string.IsNullOrWhiteSpace( initial ) )
			script += $" default location (POSIX file \"{EscapeAppleScript( initial )}\")";
		return script + ")";
	}

	internal static IReadOnlyList<string> BuildLinuxArguments( string tool, string title, string? initial )
	{
		switch ( tool )
		{
			case "zenity":
			{
				var arguments = new List<string> { "--file-selection", "--directory", $"--title={title}" };
				if ( !string.IsNullOrWhiteSpace( initial ) )
					arguments.Add( $"--filename={(initial.EndsWith( '/' ) ? initial : initial + "/")}" );
				return arguments;
			}
			case "kdialog":
			{
				// No shell expands "~", so the home folder is passed as a full path.
				var start = string.IsNullOrWhiteSpace( initial ) ? Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ) : initial;
				return new List<string> { "--getexistingdirectory", start, "--title", title };
			}
			default:
				throw new ArgumentException( $"Unknown folder dialog tool '{tool}'.", nameof( tool ) );
		}
	}

	/// <summary>Turns the dialog's stdout into a path: the trailing newline and slash go, "/" stays.</summary>
	internal static string? ParseOutput( string stdout )
	{
		var path = stdout.Trim( '\r', '\n' );
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;
		if ( path.Length > 1 )
		{
			path = path.TrimEnd( '/' );
			if ( path.Length == 0 )
				path = "/";
		}
		return path;
	}

	private static string EscapeAppleScript( string value ) =>
		value.Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" );

	private static string? PickMacOs( string title, string? initialDirectory )
	{
		var arguments = new[] { "-e", BuildAppleScript( title, initialDirectory ) };
		var result = RunProcess( "/usr/bin/osascript", arguments );
		if ( result == null )
			return null;
		if ( result.ExitCode == 0 )
			return ParseOutput( result.Stdout );
		// osascript exits 1 with error -128 when the user cancels.
		if ( result.ExitCode != 1 && !result.Stderr.Contains( "-128" ) )
			Log?.Warning( $"Folder dialog failed with exit code {result.ExitCode}: {result.Stderr.Trim()}" );
		return null;
	}

	private static string? PickLinux( string title, string? initialDirectory )
	{
		var dialog = FindLinuxDialog();
		if ( dialog == null )
			return null;
		var result = RunProcess( dialog.Value.Path, BuildLinuxArguments( dialog.Value.Tool, title, initialDirectory ) );
		// Both tools exit non-zero when the user cancels.
		if ( result == null || result.ExitCode != 0 )
			return null;
		return ParseOutput( result.Stdout );
	}

	/// <summary>Prefers zenity, then kdialog; the executable is found on PATH without a shell.</summary>
	private static (string Path, string Tool)? FindLinuxDialog()
	{
		foreach ( var tool in new[] { "zenity", "kdialog" } )
		{
			var path = FindExecutable( tool );
			if ( path != null )
				return (path, tool);
		}
		return null;
	}

	private static string? FindExecutable( string name )
	{
		foreach ( var directory in (Environment.GetEnvironmentVariable( "PATH" ) ?? "").Split( Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries ) )
		{
			try
			{
				var candidate = Path.Combine( directory, name );
				if ( File.Exists( candidate ) )
					return candidate;
			}
			catch ( Exception exception ) when ( exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException )
			{
				// Skip a PATH entry that cannot be read.
			}
		}
		return null;
	}

	private sealed record ProcessResult( int ExitCode, string Stdout, string Stderr );

	/// <summary>Runs a tool to completion without a timeout (the user is choosing); null when it cannot start.</summary>
	private static ProcessResult? RunProcess( string fileName, IEnumerable<string> arguments )
	{
		var info = new ProcessStartInfo( fileName )
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
		};
		foreach ( var argument in arguments )
			info.ArgumentList.Add( argument );

		try
		{
			using var process = Process.Start( info );
			if ( process == null )
				return null;
			var stderr = process.StandardError.ReadToEndAsync();
			var stdout = process.StandardOutput.ReadToEnd();
			process.WaitForExit();
			return new ProcessResult( process.ExitCode, stdout, stderr.GetAwaiter().GetResult() );
		}
		catch ( Exception exception ) when ( exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException )
		{
			Log?.Warning( $"Folder dialog {fileName} could not run: {exception.Message}" );
			return null;
		}
	}

	[SupportedOSPlatform( "windows" )]
	private static string? PickWindowsOnStaThread( string title, string? initialDirectory )
	{
		string? result = null;
		var thread = new Thread( () => result = ShowWindowsDialog( title, initialDirectory ) );
		thread.SetApartmentState( ApartmentState.STA );
		thread.Start();
		thread.Join();
		return result;
	}

	[SupportedOSPlatform( "windows" )]
	private static string? ShowWindowsDialog( string title, string? initialDirectory )
	{
		try
		{
			var dialog = (NativeFileDialog)new NativeFileOpenDialog();
			try
			{
				dialog.GetOptions( out var options );
				Check( dialog.SetOptions( options | FosPickFolders | FosForceFileSystem ) );
				Check( dialog.SetTitle( title ) );

				if ( !string.IsNullOrWhiteSpace( initialDirectory ) )
				{
					var iid = NativeShellItemIid;
					if ( SHCreateItemFromParsingName( initialDirectory, IntPtr.Zero, in iid, out var folder ) == 0 )
					{
						dialog.SetFolder( folder );
						Marshal.FinalReleaseComObject( folder );
					}
				}

				var hr = dialog.Show( IntPtr.Zero );
				if ( hr == ErrorCancelled )
					return null;
				Check( hr );

				Check( dialog.GetResult( out var item ) );
				try
				{
					Check( item.GetDisplayName( SigdnFileSystemPath, out var namePointer ) );
					try
					{
						return Marshal.PtrToStringUni( namePointer );
					}
					finally
					{
						Marshal.FreeCoTaskMem( namePointer );
					}
				}
				finally
				{
					Marshal.FinalReleaseComObject( item );
				}
			}
			finally
			{
				Marshal.FinalReleaseComObject( dialog );
			}
		}
		catch ( Exception exception )
		{
			// An exception here would end the process, because this runs on its own thread.
			Log?.Warning( $"Windows folder dialog failed: {exception.Message}" );
			return null;
		}
	}

	private static void Check( int hr )
	{
		if ( hr != 0 )
			Marshal.ThrowExceptionForHR( hr );
	}

	private static readonly Guid NativeShellItemIid = new( "43826d1e-e718-42ee-bc55-a1e261c37bfe" );

	[DllImport( "shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true )]
	[SupportedOSPlatform( "windows" )]
	private static extern int SHCreateItemFromParsingName( string path, IntPtr bindContext, in Guid riid, [MarshalAs( UnmanagedType.Interface )] out NativeShellItem item );

	/// <summary>Not sealed, so the cast to <see cref="NativeFileDialog"/> compiles.</summary>
	[ComImport, Guid( "DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7" ), ClassInterface( ClassInterfaceType.None )]
	private class NativeFileOpenDialog { }

	/// <summary>IFileDialog (IFileOpenDialog derives from it); the vtable order is fixed, so unused slots stay declared.</summary>
	[ComImport, Guid( "42F85136-DB7E-439C-85F1-E4075D135FC8" ), InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
	private interface NativeFileDialog
	{
		[PreserveSig] int Show( IntPtr parent );
		[PreserveSig] int SetFileTypes( uint count, IntPtr filterSpecs );
		[PreserveSig] int SetFileTypeIndex( uint index );
		[PreserveSig] int GetFileTypeIndex( out uint index );
		[PreserveSig] int Advise( IntPtr events, out uint cookie );
		[PreserveSig] int Unadvise( uint cookie );
		[PreserveSig] int SetOptions( uint options );
		[PreserveSig] int GetOptions( out uint options );
		[PreserveSig] int SetDefaultFolder( NativeShellItem item );
		[PreserveSig] int SetFolder( NativeShellItem item );
		[PreserveSig] int GetFolder( out NativeShellItem item );
		[PreserveSig] int GetCurrentSelection( out NativeShellItem item );
		[PreserveSig] int SetFileName( [MarshalAs( UnmanagedType.LPWStr )] string name );
		[PreserveSig] int GetFileName( out IntPtr name );
		[PreserveSig] int SetTitle( [MarshalAs( UnmanagedType.LPWStr )] string title );
		[PreserveSig] int SetOkButtonLabel( [MarshalAs( UnmanagedType.LPWStr )] string label );
		[PreserveSig] int SetFileNameLabel( [MarshalAs( UnmanagedType.LPWStr )] string label );
		[PreserveSig] int GetResult( out NativeShellItem item );
		[PreserveSig] int AddPlace( NativeShellItem item, int placement );
		[PreserveSig] int SetDefaultExtension( [MarshalAs( UnmanagedType.LPWStr )] string extension );
		[PreserveSig] int Close( int hr );
		[PreserveSig] int SetClientGuid( in Guid guid );
		[PreserveSig] int ClearClientData();
		[PreserveSig] int SetFilter( IntPtr filter );
	}

	[ComImport, Guid( "43826D1E-E718-42EE-BC55-A1E261C37BFE" ), InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
	private interface NativeShellItem
	{
		[PreserveSig] int BindToHandler( IntPtr bindContext, in Guid handler, in Guid riid, out IntPtr value );
		[PreserveSig] int GetParent( out NativeShellItem parent );
		[PreserveSig] int GetDisplayName( int name, out IntPtr displayName );
		[PreserveSig] int GetAttributes( uint mask, out uint attributes );
		[PreserveSig] int Compare( NativeShellItem other, uint hint, out int order );
	}
}
