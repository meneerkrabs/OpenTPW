namespace OpenTPW.Online;

/// <summary>
/// Write-to-temporary-then-rename so readers never see a half-written file. On Unix the file is readable and writable
/// by its owner only (0600): the server's files hold password hashes and authenticator secrets.
/// </summary>
public static class AtomicFile
{
	private static FileStreamOptions CreateOptions() => new()
	{
		Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
		// Not in the browser build, whose files live in the page's sandbox (and where Unix modes may not be supported).
		UnixCreateMode = OperatingSystem.IsWindows() || OperatingSystem.IsBrowser() ? null : UnixFileMode.UserRead | UnixFileMode.UserWrite,
	};

	public static void Write( string path, ReadOnlySpan<byte> bytes )
	{
		var destination = Path.GetFullPath( path );
		var directory = Path.GetDirectoryName( destination );
		if ( !string.IsNullOrEmpty( directory ) )
			Directory.CreateDirectory( directory );
		var temporary = destination + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
		try
		{
			using ( var stream = new FileStream( temporary, CreateOptions() ) )
			{
				stream.Write( bytes );
				stream.Flush( true );
			}
			File.Move( temporary, destination, true );
		}
		finally
		{
			if ( File.Exists( temporary ) )
				File.Delete( temporary );
		}
	}
}
