using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public sealed class UiImagesResolutionTests
{
	private BaseFileSystem? previousFileSystem;
	private string root = null!;

	[TestInitialize]
	public void Initialize()
	{
		previousFileSystem = FileSystem;
		root = Path.Combine( Path.GetTempPath(), "opentpw-ui-resolution-" + Guid.NewGuid().ToString( "N" ) );
		FileSystem = new BaseFileSystem( root );
		FileSystem.RegisterArchiveHandler<ResolutionArchive>( ".images" );
		ResolutionArchive.EnumerationCount = 0;
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previousFileSystem!;
		Directory.Delete( root, true );
	}

	private string WriteListing( params string[] files )
	{
		var name = "images-" + Guid.NewGuid().ToString( "N" );
		File.WriteAllLines( Path.Combine( root, name + ".images" ), files );
		return "/" + name;
	}

	private static string? LegacyResolve( string name, string[] files )
	{
		var stem = Path.GetFileNameWithoutExtension( name.TrimEnd( '\0' ) );
		return files.FirstOrDefault( file => string.Equals( Path.GetFileNameWithoutExtension( file ), stem, StringComparison.OrdinalIgnoreCase ) );
	}

	[TestMethod]
	public void NamesMatchTheFirstListedStemAcrossCaseExtensionsPathsAndNulSuffixes()
	{
		var directory = WriteListing( "B_OKAY.WCT", "b_okay.png", "nested/b_okay.tga", "cash.Trend.wct", "plain", ".wct", "!frame.wct" );
		var expectedFiles = FileSystem.GetFiles( directory );
		ResolutionArchive.EnumerationCount = 0;
		foreach ( var name in new[] { "b_okay", "B_OKAY.png", "materials/b_Okay.tga", "b_okay\0\0", "cash.Trend", "cash.Trend.tga",
			"plain", "plain\0", "", ".png", "!FRAME.wct", "absent", "materials/absent.jpg" } )
		{
			Assert.AreEqual( LegacyResolve( name, expectedFiles ), UiImages.Resolve( name, directory ), name );
			Assert.AreEqual( LegacyResolve( name, expectedFiles ), UiImages.Resolve( name, directory.ToUpperInvariant() ), name );
		}
		Assert.AreEqual( expectedFiles[0], UiImages.Resolve( "b_okay", directory ) );
		Assert.AreEqual( 1, ResolutionArchive.EnumerationCount, "all warm lookups share one directory listing, including different directory casing" );
	}

	[TestMethod]
	public void MissingAndEmptyDirectoriesKeepTheirCachedNullResults()
	{
		var empty = WriteListing();
		Assert.IsNull( UiImages.Resolve( "absent", empty ) );
		Assert.IsNull( UiImages.Resolve( "absent.wct", empty ) );
		Assert.AreEqual( 1, ResolutionArchive.EnumerationCount );

		var missing = "/missing-" + Guid.NewGuid().ToString( "N" );
		Assert.IsNull( UiImages.Resolve( "late", missing ) );
		Directory.CreateDirectory( Path.Combine( root, missing[1..] ) );
		File.WriteAllBytes( Path.Combine( root, missing[1..], "late.wct" ), Array.Empty<byte>() );
		Assert.IsNull( UiImages.Resolve( "late", missing ), "the existing cache does not re-enumerate a previously missing directory" );
	}

	[TestMethod]
	public void ReplacingTheFileSystemPreservesTheExistingDirectoryCacheLifetime()
	{
		var directory = WriteListing( "first.wct" );
		var first = UiImages.Resolve( "first", directory );
		Assert.IsNotNull( first );
		FileSystem = new BaseFileSystem( Path.Combine( root, "replacement" ) );
		Assert.AreEqual( first, UiImages.Resolve( "FIRST.png", directory ) );
		Assert.AreEqual( 1, ResolutionArchive.EnumerationCount );
	}

	[TestMethod]
	public void WarmLookupsAllocateLessThanTheFormerRepeatedStemScan()
	{
		var files = Enumerable.Range( 0, 128 ).Select( index => $"button{index}.wct" ).ToArray();
		var directory = WriteListing( files );
		var expectedFiles = FileSystem.GetFiles( directory );
		const string name = "materials/BUTTON127.png\0\0";
		var expected = LegacyResolve( name, expectedFiles );
		for ( var index = 0; index < 256; index++ )
		{
			UiImages.Resolve( name, directory );
			LegacyResolve( name, expectedFiles );
		}
		string? result = null;
		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( var index = 0; index < 1000; index++ )
			result = UiImages.Resolve( name, directory );
		var indexedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.AreEqual( expected, result );
		before = GC.GetAllocatedBytesForCurrentThread();
		for ( var index = 0; index < 1000; index++ )
			result = LegacyResolve( name, expectedFiles );
		var scannedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.AreEqual( expected, result );
		Console.WriteLine( $"1,000 warm UI resolutions: indexed {indexedBytes} bytes; former scan {scannedBytes} bytes." );
		Assert.IsTrue( indexedBytes < scannedBytes, "warm resolution should not repeatedly allocate the directory entries' stems" );
	}

	public sealed class ResolutionArchive : IArchive
	{
		private readonly string[] files;
		public static int EnumerationCount { get; set; }
		public ResolutionArchive( string path ) => files = File.ReadAllLines( path );
		public string[] GetFiles( string internalPath ) { EnumerationCount++; return files; }
		public string[] GetDirectories( string internalPath ) => Array.Empty<string>();
		public void ReadFromStream( Stream stream ) => throw new NotSupportedException();
		public ArchiveFile GetFile( string internalPath ) => throw new NotSupportedException();
		public byte[] GetData( int offset, int length ) => throw new NotSupportedException();
		public Stream OpenFile( string path ) => throw new NotSupportedException();
		public long GetFileSize( string path ) => throw new NotSupportedException();
		public DateTime GetModifiedTime() => DateTime.UnixEpoch;
		public void Dispose() { }
	}
}
