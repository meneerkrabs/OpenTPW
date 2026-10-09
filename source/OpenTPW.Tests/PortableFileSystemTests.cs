using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class PortableFileSystemTests
{
	private string root = null!;
	private BaseFileSystem fileSystem = null!;
	private string originalGamePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		root = Path.Combine( Path.GetTempPath(), $"OpenTPW-files-{Guid.NewGuid():N}" );
		fileSystem = new BaseFileSystem( root );
		originalGamePath = Settings.Default.GamePath;
		Settings.Default.GamePath = root;
	}

	[TestCleanup]
	public void Cleanup()
	{
		Settings.Default.GamePath = originalGamePath;
		Directory.Delete( root, true );
	}

	[DataTestMethod]
	[DataRow( "nested/file.txt" )]
	[DataRow( @"nested\file.txt" )]
	[DataRow( "/nested/file.txt" )]
	[DataRow( @"\nested\file.txt" )]
	[DataRow( @"/nested\file.txt" )]
	public void CallerSeparatorsResolveToNativePaths( string path )
	{
		var expected = Path.Combine( root, "nested", "file.txt" );
		Assert.AreEqual( expected, fileSystem.GetAbsolutePath( path ) );
		Assert.AreEqual( expected, GameDir.GetPath( path ) );
		Assert.AreEqual( "/nested/file.txt", fileSystem.GetRelativePath( expected ) );
		Assert.AreEqual( "/nested/file.txt", fileSystem.GetRelativePath( expected.Replace( '/', '\\' ) ) );
	}

	[TestMethod]
	public void RootAndContainedParentSegmentsResolve()
	{
		Assert.AreEqual( root, fileSystem.GetAbsolutePath( "/" ) );
		Assert.AreEqual( root, fileSystem.GetAbsolutePath( @"\" ) );
		Assert.AreEqual( Path.Combine( root, "file.txt" ), fileSystem.GetAbsolutePath( @"nested\..\file.txt" ) );
		Assert.AreEqual( Path.Combine( root, "file.txt" ), GameDir.GetPath( "nested/../file.txt" ) );
	}

	[TestMethod]
	public void RootSeparatorsAreNormalizedWithoutLosingTheAbsoluteRoot()
	{
		var alternateRoot = root.Replace( Path.DirectorySeparatorChar, Path.DirectorySeparatorChar == '/' ? '\\' : '/' );
		var alternateFileSystem = new BaseFileSystem( alternateRoot );
		Assert.AreEqual( Path.Combine( root, "file.txt" ), alternateFileSystem.GetAbsolutePath( "file.txt" ) );
		Settings.Default.GamePath = alternateRoot;
		Assert.AreEqual( Path.Combine( root, "file.txt" ), GameDir.GetPath( "file.txt" ) );
	}

	[DataTestMethod]
	[DataRow( "../outside.txt" )]
	[DataRow( @"..\outside.txt" )]
	[DataRow( "/../outside.txt" )]
	[DataRow( @"\..\outside.txt" )]
	[DataRow( "nested/../../outside.txt" )]
	[DataRow( @"nested\..\..\outside.txt" )]
	public void TraversalOutsideRootIsRejected( string path )
	{
		Assert.ThrowsException<ArgumentException>( () => fileSystem.GetAbsolutePath( path ) );
		Assert.ThrowsException<ArgumentException>( () => fileSystem.OpenRead( path ) );
		Assert.ThrowsException<ArgumentException>( () => fileSystem.OpenWrite( path ) );
		Assert.ThrowsException<ArgumentException>( () => fileSystem.FileExists( path ) );
		Assert.ThrowsException<ArgumentException>( () => fileSystem.DirectoryExists( path ) );
		Assert.ThrowsException<ArgumentException>( () => fileSystem.GetFiles( path ) );
		Assert.ThrowsException<ArgumentException>( () => GameDir.GetPath( path ) );
	}

	[TestMethod]
	public void AbsolutePathsOutsideRootAreRejected()
	{
		var outside = root + "-sibling";
		Assert.ThrowsException<ArgumentException>( () => fileSystem.GetRelativePath( outside ) );
		Assert.ThrowsException<ArgumentException>( () => fileSystem.IsArchive( outside ) );
	}

	[TestMethod]
	public void ReadWriteAndEnumerateUseNativePaths()
	{
		var bytes = Encoding.ASCII.GetBytes( "portable file contents" );
		using ( var stream = fileSystem.OpenWrite( @"nested\file.txt" ) )
			stream.Write( bytes, 0, bytes.Length );

		CollectionAssert.AreEqual( bytes, fileSystem.ReadAllBytes( "/nested/file.txt" ) );
		Assert.AreEqual( "portable file contents", fileSystem.ReadAllText( @"\nested\file.txt" ) );
		Assert.IsTrue( fileSystem.FileExists( @"nested\file.txt" ) );
		Assert.IsTrue( fileSystem.DirectoryExists( "/nested" ) );
		Assert.AreEqual( (long)bytes.Length, fileSystem.GetSize( @"nested\file.txt" ) );
		Assert.AreEqual( File.GetLastWriteTime( Path.Combine( root, "nested", "file.txt" ) ), fileSystem.GetModifiedTime( "nested/file.txt" ) );
		CollectionAssert.AreEqual( new[] { Path.Combine( root, "nested", "file.txt" ) }, fileSystem.GetFiles( @"\nested" ) );
		CollectionAssert.AreEqual( new[] { Path.Combine( root, "nested" ) }, fileSystem.GetDirectories( "/" ) );
		Assert.IsFalse( fileSystem.IsArchive( Path.Combine( root, "nested", "file.txt" ) ) );
		using var watcher = fileSystem.CreateWatcher( @"\nested", "*.txt" );
		Assert.AreEqual( Path.Combine( root, "nested" ), watcher.Path );
	}

	[TestMethod]
	public void MissingFilesDoNotSilentlyReturnEmptyContents()
	{
		Assert.IsFalse( fileSystem.FileExists( @"missing\file.txt" ) );
		Assert.IsFalse( fileSystem.DirectoryExists( "missing" ) );
		Assert.ThrowsException<FileNotFoundException>( () => fileSystem.ReadAllText( "missing.txt" ) );
		Assert.ThrowsException<DirectoryNotFoundException>( () => fileSystem.ReadAllBytes( @"missing\file.txt" ) );
	}

	[TestMethod]
	public void EnumeratedAbsolutePathsRoundtripIntoFilesystemOperations()
	{
		Directory.CreateDirectory( Path.Combine( root, "nested" ) );
		File.WriteAllText( Path.Combine( root, "nested", "file.txt" ), "roundtrip" );
		var directory = fileSystem.GetDirectories( "/" )[0];
		var file = fileSystem.GetFiles( directory )[0];
		Assert.AreEqual( file, fileSystem.GetAbsolutePath( file ) );
		Assert.AreEqual( "roundtrip", fileSystem.ReadAllText( file ) );
		Assert.AreEqual( 9L, fileSystem.GetSize( file ) );
		Assert.ThrowsException<ArgumentException>( () => fileSystem.GetAbsolutePath( Path.Combine( root, "..", "outside.txt" ) ) );
	}

	private bool HostIsCaseSensitive()
	{
		var probe = Path.Combine( root, "case-probe" );
		File.WriteAllText( probe, "" );
		var sensitive = !File.Exists( Path.Combine( root, "CASE-PROBE" ) );
		File.Delete( probe );
		return sensitive;
	}

	[TestMethod]
	public void MixedCaseRequestsResolveToTheOnDiskSpelling()
	{
		// Install spelling Data/global/Speech, requested as the CD spelling data/global/speech.
		var directory = Path.Combine( root, "Data", "global", "Speech" );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, "speechHD.SDT" ), "bank" );

		Assert.AreEqual( "bank", fileSystem.ReadAllText( "/data/GLOBAL/speech/SPEECHhd.sdt" ) );
		Assert.IsTrue( fileSystem.FileExists( @"data\global\speech\speechhd.sdt" ) );
		Assert.IsTrue( fileSystem.DirectoryExists( "DATA/Global/SPEECH" ) );
		Assert.AreEqual( 4L, fileSystem.GetSize( "data/global/speech/speechhd.sdt" ) );
		CollectionAssert.AreEqual( new[] { Path.Combine( directory, "speechHD.SDT" ) }, fileSystem.GetFiles( "data/global/speech" ) );
		if ( !HostIsCaseSensitive() )
			return;
		Assert.AreEqual( Path.Combine( directory, "speechHD.SDT" ), fileSystem.GetAbsolutePath( "data/global/speech/speechhd.sdt" ) );
		// The first missing segment and everything after it keep the requested spelling.
		Assert.AreEqual( Path.Combine( root, "Data", "global", "New", "file.TXT" ), fileSystem.GetAbsolutePath( "DATA/Global/New/file.TXT" ) );
		using ( var stream = fileSystem.OpenWrite( "data/global/speech/new.txt" ) )
			stream.WriteByte( 1 );
		Assert.IsTrue( File.Exists( Path.Combine( directory, "new.txt" ) ) );
	}

	[TestMethod]
	public void ExactSpellingWinsOverCaseInsensitiveMatches()
	{
		if ( !HostIsCaseSensitive() )
			Assert.Inconclusive( "Needs a case-sensitive file system." );
		Directory.CreateDirectory( Path.Combine( root, "Sound" ) );
		Directory.CreateDirectory( Path.Combine( root, "sound" ) );
		File.WriteAllText( Path.Combine( root, "Sound", "a.txt" ), "upper" );
		File.WriteAllText( Path.Combine( root, "sound", "a.txt" ), "lower" );
		Assert.AreEqual( "upper", fileSystem.ReadAllText( "Sound/A.TXT" ) );
		Assert.AreEqual( "lower", fileSystem.ReadAllText( "sound/A.TXT" ) );
		// Neither spelling matches exactly: the ordinal-first on-disk entry is used.
		Assert.AreEqual( "upper", fileSystem.ReadAllText( "SOUND/a.txt" ) );
	}

	[TestMethod]
	public void ArchivesBelowMixedCaseDirectoriesResolve()
	{
		var directory = Path.Combine( root, "Levels", "Jungle" );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, "terrain.WAD" ), "archive contents" );
		fileSystem.RegisterArchiveHandler<PortableArchive>( ".wad" );
		Assert.AreEqual( "archive contents", fileSystem.ReadAllText( "/levels/jungle/terrain/nested/file.txt" ) );
		Assert.IsTrue( fileSystem.IsArchive( "levels/jungle/TERRAIN/nested/file.txt" ) );
	}

	[DataTestMethod]
	[DataRow( ".wad", ".WAD" )]
	[DataRow( ".WAD", ".wad" )]
	[DataRow( ".sdt", ".SdT" )]
	public void ArchiveDispatchPreservesAbsoluteRootAndIgnoresExtensionCase( string registeredExtension, string diskExtension )
	{
		var directory = Path.Combine( root, "levels" );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, "terrain" + diskExtension ), "archive contents" );
		fileSystem.RegisterArchiveHandler<PortableArchive>( registeredExtension );

		Assert.AreEqual( "archive contents", fileSystem.ReadAllText( @"\levels\terrain\nested\file.txt" ) );
		Assert.AreEqual( "archive contents", fileSystem.ReadAllText( "levels/terrain/nested/file.txt" ) );
		Assert.AreEqual( 16L, fileSystem.GetSize( @"levels\terrain\nested\file.txt" ) );
		Assert.AreEqual( DateTime.UnixEpoch, fileSystem.GetModifiedTime( "levels/terrain/nested/file.txt" ) );
		Assert.IsTrue( fileSystem.IsArchive( Path.Combine( directory, "terrain" ) ) );
		Assert.IsTrue( fileSystem.IsArchive( @"levels\terrain\nested\file.txt" ) );
		CollectionAssert.AreEqual( Array.Empty<string>(), fileSystem.GetFiles( "levels" ) );
		CollectionAssert.AreEqual( new[] { Path.Combine( directory, "terrain" ) }, fileSystem.GetDirectories( "levels" ) );
		var archiveDirectory = fileSystem.GetDirectories( "/levels" )[0];
		Assert.AreEqual( "archive contents", fileSystem.ReadAllText( Path.Combine( archiveDirectory, "nested", "file.txt" ) ) );
		Assert.AreEqual( 16L, fileSystem.GetSize( Path.Combine( archiveDirectory, "nested", "file.txt" ) ) );
		Assert.AreEqual( "archive contents", fileSystem.ReadAllText( "/levels/terrain/nested/file.txt" ) );
		CollectionAssert.AreEqual( new[] { Path.Combine( "levels", "terrain", "nested" ) }, fileSystem.GetDirectories( @"levels\terrain" ) );
		CollectionAssert.AreEqual( new[] { Path.Combine( "levels", "terrain", "nested", "file.txt" ) }, fileSystem.GetFiles( @"levels\terrain\nested" ) );
		Assert.ThrowsException<NotImplementedException>( () => fileSystem.OpenWrite( "levels/terrain/nested/file.txt" ) );
	}

	[TestMethod]
	public void SyntheticWadReadsNestedEntriesWithoutOriginalAssets()
	{
		var filename = Encoding.ASCII.GetBytes( "nested\\file.txt\0" );
		var contents = Encoding.ASCII.GetBytes( "synthetic WAD contents" );
		using ( var writer = new BinaryWriter( File.Create( Path.Combine( root, "terrain.WAD" ) ) ) )
		{
			writer.Write( Encoding.ASCII.GetBytes( "DWFB" ) );
			writer.Write( 1 );
			writer.Write( new byte[64] );
			writer.Write( 1 );
			writer.Write( 88 );
			writer.Write( 40 );
			writer.Write( 0 );
			writer.Write( 0 );
			writer.Write( 128 );
			writer.Write( filename.Length );
			writer.Write( 128 + filename.Length );
			writer.Write( contents.Length );
			writer.Write( 0 );
			writer.Write( contents.Length );
			writer.Write( new byte[12] );
			writer.Write( filename );
			writer.Write( contents );
		}

		fileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		foreach ( var path in new[] { "/", ".", root } )
		{
			CollectionAssert.AreEqual( Array.Empty<string>(), fileSystem.GetFiles( path ) );
			CollectionAssert.AreEqual( new[] { Path.Combine( root, "terrain" ) }, fileSystem.GetDirectories( path ) );
		}
		foreach ( var archiveRoot in new[] { "terrain", "terrain.WAD" } )
		{
			CollectionAssert.AreEqual( Array.Empty<string>(), fileSystem.GetFiles( archiveRoot ) );
			CollectionAssert.AreEqual( new[] { Path.Combine( archiveRoot, "nested" ) }, fileSystem.GetDirectories( archiveRoot ) );
		}
		CollectionAssert.AreEqual( File.ReadAllBytes( Path.Combine( root, "terrain.WAD" ) ), fileSystem.ReadAllBytes( "terrain.WAD" ), "bare archive reads still return the container" );
		CollectionAssert.AreEqual( contents, fileSystem.ReadAllBytes( @"terrain\nested\file.txt" ) );
		Assert.AreEqual( "synthetic WAD contents", fileSystem.ReadAllText( "/terrain/nested/file.txt" ) );
		Assert.AreEqual( (long)contents.Length, fileSystem.GetSize( "terrain/nested/file.txt" ) );
		CollectionAssert.AreEqual( new[] { Path.Combine( "terrain", "nested", "file.txt" ) }, fileSystem.GetFiles( @"terrain\nested" ) );
	}

	public sealed class PortableArchive : IArchive
	{
		private byte[] contents;

		public PortableArchive( string path )
		{
			contents = File.ReadAllBytes( path );
		}

		public void ReadFromStream( Stream stream )
		{
			using var buffer = new MemoryStream();
			stream.CopyTo( buffer );
			contents = buffer.ToArray();
		}

		public Stream OpenFile( string path )
		{
			Assert.AreEqual( Path.Combine( "nested", "file.txt" ), path );
			return new MemoryStream( contents, false );
		}

		public long GetFileSize( string path )
		{
			Assert.AreEqual( Path.Combine( "nested", "file.txt" ), path );
			return contents.Length;
		}

		public string[] GetFiles( string internalPath )
		{
			Assert.AreEqual( "nested", internalPath );
			return new[] { "file.txt" };
		}

		public string[] GetDirectories( string internalPath )
		{
			Assert.AreEqual( string.Empty, internalPath );
			return new[] { "nested" };
		}

		public ArchiveFile GetFile( string internalPath ) => throw new NotSupportedException();
		public byte[] GetData( int offset, int length ) => contents[offset..(offset + length)];
		public DateTime GetModifiedTime() => DateTime.UnixEpoch;
		public void Dispose() { }
	}
}
