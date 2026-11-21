using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Veldrid;

namespace OpenTPW.Tests;

/// <summary>
/// Issue #829: on Direct3D 11 a partial update of a dynamic buffer became a GPU copy into it, outside the paths D3D11 documents for D3D11_USAGE_DYNAMIC;
/// <see cref="DynamicBufferUpload"/> widens such an update to the whole buffer so Veldrid writes it with Map(WRITE_DISCARD).
/// </summary>
[TestClass]
public class DynamicBufferUploadTests
{
	[TestMethod]
	public void APartialWriteIntoADynamicBufferBecomesAWholeBufferWriteOnDirect3D11()
	{
		Assert.AreEqual( 16384u, DynamicBufferUpload.UploadBytes( BufferUsage.VertexBuffer | BufferUsage.Dynamic, 16384, 600, wholeBuffer: true ) );
		Assert.AreEqual( 2048u, DynamicBufferUpload.UploadBytes( BufferUsage.UniformBuffer | BufferUsage.Dynamic, 2048, 304, wholeBuffer: true ), "a per-draw uniform block" );
	}

	[TestMethod]
	public void WholeWritesStaticBuffersAndOtherBackendsKeepTheCallersCount()
	{
		Assert.AreEqual( 2048u, DynamicBufferUpload.UploadBytes( BufferUsage.VertexBuffer | BufferUsage.Dynamic, 2048, 2048, wholeBuffer: true ), "already the whole buffer" );
		Assert.AreEqual( 600u, DynamicBufferUpload.UploadBytes( BufferUsage.VertexBuffer, 16384, 600, wholeBuffer: true ), "a static buffer takes UpdateSubresource" );
		Assert.AreEqual( 600u, DynamicBufferUpload.UploadBytes( BufferUsage.VertexBuffer | BufferUsage.Dynamic, 16384, 600, wholeBuffer: false ), "Metal, Vulkan and WebGL" );
	}

	[TestMethod]
	public void TheWidenedCopyStartsWithTheCallersBytesAndCoversTheBuffer()
	{
		var source = new[] { 0x11223344u, 0x55667788u, 0x99AABBCCu };
		var widened = DynamicBufferUpload.Widen( ref source[0], 12, 64 );
		Assert.IsTrue( widened.Length >= 64 );
		CollectionAssert.AreEqual( source.SelectMany( BitConverter.GetBytes ).ToArray(), widened.Take( 12 ).ToArray() );
		var larger = DynamicBufferUpload.Widen( ref source[1], 4, 4096 );
		Assert.IsTrue( larger.Length >= 4096, "the scratch copy grows to the buffer" );
		CollectionAssert.AreEqual( BitConverter.GetBytes( source[1] ), larger.Take( 4 ).ToArray() );
	}

	[TestMethod]
	public void NoRendererUpdatesABufferAroundTheHelper()
	{
		var root = new DirectoryInfo( AppContext.BaseDirectory );
		while ( root != null && !Directory.Exists( Path.Combine( root.FullName, "source", "OpenTPW" ) ) )
			root = root.Parent;
		Assert.IsNotNull( root, "the repository root" );
		var sources = new[] { "OpenTPW", "OpenTPW.Common", "OpenTPW.ModKit" }.Select( name => Path.Combine( root!.FullName, "source", name ) );
		var helper = Path.Combine( root.FullName, "source", "OpenTPW", "Render", "DynamicBufferUpload.cs" );
		var direct = new Regex( @"\.UpdateBuffer\s*(<[^>]*>\s*)?\(" );
		var offenders = sources.SelectMany( directory => Directory.EnumerateFiles( directory, "*.cs", SearchOption.AllDirectories ) )
			.Where( path => !path.Contains( $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}" ) && !path.Contains( $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}" ) )
			.Where( path => !string.Equals( path, helper, StringComparison.Ordinal ) )
			.Where( path => direct.IsMatch( File.ReadAllText( path ) ) )
			.Select( path => Path.GetRelativePath( root.FullName, path ) )
			.ToArray();
		CollectionAssert.AreEqual( Array.Empty<string>(), offenders, "buffer writes go through DynamicBufferUpload.Update: " + string.Join( ", ", offenders ) );
	}
}
