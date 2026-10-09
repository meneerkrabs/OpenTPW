namespace OpenTPW.Online;

/// <summary>Write-to-temporary-then-rename so readers never see a half-written file.</summary>
public static class AtomicFile
{
	public static void Write( string path, ReadOnlySpan<byte> bytes )
	{
		var destination = Path.GetFullPath( path );
		var directory = Path.GetDirectoryName( destination );
		if ( !string.IsNullOrEmpty( directory ) )
			Directory.CreateDirectory( directory );
		var temporary = destination + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
		try
		{
			using ( var stream = new FileStream( temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None ) )
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
