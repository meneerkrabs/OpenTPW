using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Veldrid;
using Vortice.D3DCompiler;
using Vortice.Direct3D;

namespace OpenTPW;

/// <summary>
/// Direct3D 11 shader bytecode kept between runs (issue #754, docs/ENGINE-PERFORMANCE.md, "Kept Direct3D 11 bytecode"). Veldrid compiles the HLSL of every
/// new Direct3D 11 shader with the system HLSL compiler (<c>D3D11Shader.CompileCode</c>: <c>vs_5_0</c>/<c>ps_5_0</c>, entry point <c>main</c>,
/// <c>OptimizationLevel3</c>, no defines, include or source name) and takes bytecode that starts with <c>DXBC</c> as it is. <see cref="Get"/> makes the same
/// compile once and keeps its result in the cache folder under a SHA-256 of the HLSL, the profile, the flags and the compiler DLL's identity, so a
/// kept file is the bytecode the compiler would make now; a missing or malformed file is compiled again and replaced.
/// </summary>
[SupportedOSPlatform( "windows" )]
internal static class Direct3D11Bytecode
{
	private const ShaderFlags Flags = ShaderFlags.OptimizationLevel3;

	/// <summary>The bytecode of <paramref name="hlsl"/> for <paramref name="stage"/>, kept in <paramref name="directory"/>.</summary>
	public static byte[] Get( string directory, string hlsl, ShaderStages stage )
	{
		var profile = stage switch
		{
			ShaderStages.Vertex => "vs_5_0",
			ShaderStages.Fragment => "ps_5_0",
			_ => throw new ArgumentOutOfRangeException( nameof( stage ) )
		};
		// Veldrid's D3D11 backend hands the HLSL to the compiler as ASCII bytes (Veldrid.SPIRV GetBytes)
		var source = Encoding.ASCII.GetBytes( hlsl );
		var file = CompilerIdentity is { } compiler ? Path.Combine( directory, Key( source, profile, compiler ) + ".dxbc" ) : null;
		if ( file != null && Read( file ) is { } kept )
		{
			LoadProfile.Count( "hlsl cache hits" );
			return kept;
		}
		var bytecode = Compile( source, profile );
		LoadProfile.Count( "hlsl compiles" );
		if ( file != null )
			Write( directory, file, bytecode );
		return bytecode;
	}

	/// <summary>The compile Veldrid's <c>D3D11Shader.CompileCode</c> makes (Veldrid 4.9.0).</summary>
	internal static byte[] Compile( byte[] source, string profile )
	{
		using var profileSection = LoadProfile.Measure( "hlsl-compile" );
		Compiler.Compile( source, null, null, "main", null, profile, Flags, out Blob result, out Blob error );
		try
		{
			if ( result == null )
				throw new VeldridException( $"Failed to compile HLSL code: {(error == null ? "" : Encoding.ASCII.GetString( error.AsBytes() ))}" );
			return result.AsBytes();
		}
		finally
		{
			result?.Dispose();
			error?.Dispose();
		}
	}

	internal static string Key( byte[] source, string profile, string compiler )
	{
		using var hash = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
		hash.AppendData( Encoding.UTF8.GetBytes( $"opentpw-dxbc-cache 1\n{compiler}\n{profile}\n{Flags}\n" ) );
		hash.AppendData( source );
		return Convert.ToHexString( hash.GetHashAndReset() ).ToLowerInvariant();
	}

	private static readonly Lazy<string?> compilerIdentity = new( FindCompilerIdentity );

	/// <summary>The system HLSL compiler's version, size and time (it changes with Windows updates), or null when it cannot be read: then nothing is kept.</summary>
	internal static string? CompilerIdentity => compilerIdentity.Value;

	private static string? FindCompilerIdentity()
	{
		try
		{
			var path = Path.Combine( Environment.SystemDirectory, "d3dcompiler_47.dll" );
			if ( !File.Exists( path ) )
				return null;
			var info = new FileInfo( path );
			return $"{FileVersionInfo.GetVersionInfo( path ).FileVersion}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{typeof( Compiler ).Assembly.GetName().Version}";
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			return null;
		}
	}

	private static byte[]? Read( string file )
	{
		try
		{
			if ( !File.Exists( file ) )
				return null;
			var bytes = File.ReadAllBytes( file );
			// a DXBC container: the magic, a 16-byte checksum, version 1 and its own total size
			return bytes.Length >= 32 && bytes[0] == (byte)'D' && bytes[1] == (byte)'X' && bytes[2] == (byte)'B' && bytes[3] == (byte)'C'
				&& BitConverter.ToUInt32( bytes, 24 ) == (uint)bytes.Length ? bytes : null;
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			return null;
		}
	}

	private static void Write( string directory, string file, byte[] bytecode )
	{
		try
		{
			Directory.CreateDirectory( directory );
			var temporary = file + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
			File.WriteAllBytes( temporary, bytecode );
			File.Move( temporary, file, overwrite: true );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			Log?.Trace( $"Shader cache not written ({exception.Message}); the shaders are compiled at every start." );
		}
	}
}
