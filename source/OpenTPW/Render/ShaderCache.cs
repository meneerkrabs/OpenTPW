using System.Security.Cryptography;

namespace OpenTPW;

/// <summary>
/// The files of the compiled-shader cache (<c>&lt;config&gt;/cache/shaders</c>, docs/ENGINE-PERFORMANCE.md, "Kept SPIR-V"): each holds a format tag, the SHA-256 of
/// its payload and the payload, so a file damaged after it was written (cut short, overwritten) is refused before its bytes reach a compiler. A file is
/// written beside and moved into place, its time is refreshed whenever it is used, and files unused for <see cref="Lifetime"/> are removed when a new one
/// is written. Nothing here throws for a file problem: the caller compiles instead.
/// </summary>
internal static class ShaderCache
{
	private static ReadOnlySpan<byte> Tag => "OTPWSHC1"u8;
	private const int HeaderLength = 8 + 32;

	/// <summary>Files not used for this long are removed when a new one is written (an old install's sources, an old compiler).</summary>
	internal static readonly TimeSpan Lifetime = TimeSpan.FromDays( 30 );

	/// <summary>The payload of <paramref name="file"/> when it is whole and <paramref name="wellFormed"/> accepts it, else null.</summary>
	public static byte[]? Read( string file, Func<byte[], bool> wellFormed )
	{
		try
		{
			if ( !File.Exists( file ) )
				return null;
			var bytes = File.ReadAllBytes( file );
			if ( bytes.Length < HeaderLength || !bytes.AsSpan( 0, 8 ).SequenceEqual( Tag ) )
				return null;
			var payload = bytes[HeaderLength..];
			if ( !SHA256.HashData( payload ).AsSpan().SequenceEqual( bytes.AsSpan( 8, 32 ) ) || !wellFormed( payload ) )
				return null;
			// used now: the expiry counts from the last use, not from the write
			try
			{
				File.SetLastWriteTimeUtc( file, DateTime.UtcNow );
			}
			catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
			{
			}
			return payload;
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			return null;
		}
	}

	/// <summary>Keeps <paramref name="payload"/> as <paramref name="file"/> in <paramref name="directory"/>, then removes files unused for <see cref="Lifetime"/>.</summary>
	public static void Write( string directory, string file, byte[] payload )
	{
		try
		{
			Directory.CreateDirectory( directory );
			var bytes = new byte[HeaderLength + payload.Length];
			Tag.CopyTo( bytes );
			SHA256.HashData( payload ).CopyTo( bytes, 8 );
			payload.CopyTo( bytes, HeaderLength );
			// written beside and moved into place, so a reader never sees half a file
			var temporary = file + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
			File.WriteAllBytes( temporary, bytes );
			File.Move( temporary, file, overwrite: true );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			Log?.Trace( $"Shader cache not written ({exception.Message}); the shader is compiled again at the next start." );
			return;
		}
		RemoveUnused( directory );
	}

	/// <summary>Removes a kept file a compiler refused, so it is made again.</summary>
	public static void Delete( string? file )
	{
		if ( file == null )
			return;
		try
		{
			File.Delete( file );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			Log?.Trace( $"Shader cache file {Path.GetFileName( file )} not removed ({exception.Message})." );
		}
	}

	private static void RemoveUnused( string directory )
	{
		try
		{
			var expired = DateTime.UtcNow - Lifetime;
			foreach ( var old in Directory.EnumerateFiles( directory ) )
				if ( File.GetLastWriteTimeUtc( old ) < expired )
					File.Delete( old );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			Log?.Trace( $"Shader cache: unused files not removed ({exception.Message})." );
		}
	}
}
