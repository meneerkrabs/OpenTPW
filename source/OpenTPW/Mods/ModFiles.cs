using System.Runtime.InteropServices;

namespace OpenTPW.Mods;

/// <summary>
/// Hostile-file guards for mod folders and <c>mods.json</c> (docs/MODS.md): only regular files are read, never through
/// a symbolic link, and never more bytes than a document may have. A FIFO, device or socket named like a mod file would
/// otherwise block or never end, and a link could point anywhere.
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
internal static class ModFiles
{
	private const int TypeMask = 0xF000;
	private const int RegularFile = 0x8000;

	/// <summary>Null when <paramref name="path"/> is a regular file that is not a link, else the reason it is not.</summary>
	public static string? NotRegularFile( string path )
	{
		var info = new FileInfo( path );
		if ( !info.Exists )
			return Directory.Exists( path ) ? "is a folder, not a file" : "does not exist";
		if ( info.LinkTarget != null || info.Attributes.HasFlag( FileAttributes.ReparsePoint ) )
			return "is a symbolic link or reparse point";
		if ( info.Attributes.HasFlag( FileAttributes.Device ) )
			return "is a device, not a regular file";
		if ( OperatingSystem.IsWindows() || OperatingSystem.IsBrowser() )
			return null;
		// .NET reports FIFOs, devices and sockets as ordinary files on Unix; the file type comes from lstat.
		var type = UnixFileType( path );
		return type == null || type == RegularFile ? null : "is not a regular file (a FIFO, device or socket)";
	}

	/// <summary>
	/// The whole file, refused when it is not a regular file or longer than <paramref name="maximum"/> bytes. The stream
	/// is read up to one byte past the limit, so a file that grows after the check cannot exhaust memory.
	/// </summary>
	public static byte[] ReadBounded( string path, int maximum, string what )
	{
		if ( NotRegularFile( path ) is { } reason )
			throw new InvalidDataException( $"{what} {reason}." );
		using var stream = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read );
		var buffer = new byte[Math.Min( (long)maximum + 1, Math.Max( stream.Length + 1, 4096 ) )];
		var total = 0;
		while ( true )
		{
			if ( total == buffer.Length )
			{
				if ( buffer.Length > maximum )
					throw new InvalidDataException( $"{what} is larger than {maximum} bytes." );
				Array.Resize( ref buffer, (int)Math.Min( (long)maximum + 1, buffer.Length * 2L ) );
			}
			var read = stream.Read( buffer, total, buffer.Length - total );
			if ( read == 0 )
				break;
			total += read;
		}
		if ( total > maximum )
			throw new InvalidDataException( $"{what} is larger than {maximum} bytes." );
		return buffer[..total];
	}

	/// <summary>
	/// The lstat file-type bits through the runtime's own native shim (<c>libSystem.Native</c>, which normalises them to
	/// the POSIX values on every Unix), or null when it is unavailable; the guard then relies on the attribute checks.
	/// </summary>
	private static int? UnixFileType( string path )
	{
		try
		{
			// FileStatus begins with Flags and Mode (two int32s); the buffer leaves room for the rest of the structure.
			var status = new byte[512];
			return LStat( path, status ) == 0 ? BitConverter.ToInt32( status, 4 ) & TypeMask : null;
		}
		catch ( Exception exception ) when ( exception is DllNotFoundException or EntryPointNotFoundException )
		{
			return null;
		}
	}

	[DllImport( "libSystem.Native", EntryPoint = "SystemNative_LStat" )]
	private static extern int LStat( [MarshalAs( UnmanagedType.LPUTF8Str )] string path, byte[] status );
}
