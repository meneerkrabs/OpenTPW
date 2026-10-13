using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The remembered path lookups of <see cref="BaseFileSystem.CacheLookups"/>: the same answers, until files are added.</summary>
[TestClass]
public class FileSystemLookupCacheTests
{
	/// <summary>An archive format for the test: a ".pack" file that holds one member, "inside.txt", with the pack's own text.</summary>
	private sealed class PackArchive : IArchive
	{
		private readonly byte[] contents;
		public PackArchive( string path ) => contents = File.ReadAllBytes( path );
		public void Dispose() { }
		public string[] GetFiles( string internalPath ) => new[] { "inside.txt" };
		public string[] GetDirectories( string internalPath ) => Array.Empty<string>();
		public ArchiveFile GetFile( string internalPath ) => throw new NotSupportedException();
		public byte[] GetData( int offset, int length ) => contents[offset..(offset + length)];
		public Stream OpenFile( string path ) => new MemoryStream( contents );
		public long GetFileSize( string path ) => contents.Length;
		public DateTime GetModifiedTime() => DateTime.UnixEpoch;
		public void ReadFromStream( Stream stream ) => throw new NotSupportedException();
	}

	private static string Folder()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-lookups-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( directory, "levels" ) );
		File.WriteAllText( Path.Combine( directory, "levels", "plain.txt" ), "plain" );
		return directory;
	}

	private static BaseFileSystem Open( string directory, bool cache )
	{
		var fileSystem = new BaseFileSystem( directory ) { CacheLookups = cache };
		fileSystem.RegisterArchiveHandler<PackArchive>( ".pack" );
		return fileSystem;
	}

	private static string Read( BaseFileSystem fileSystem, string path ) => Encoding.ASCII.GetString( fileSystem.ReadAllBytes( path ) );

	[TestMethod]
	public void CachedLookupsGiveTheSameAnswers()
	{
		var directory = Folder();
		try
		{
			File.WriteAllText( Path.Combine( directory, "levels", "theme.pack" ), "packed" );
			var plain = Open( directory, cache: false );
			var cached = Open( directory, cache: true );
			foreach ( var path in new[] { "/levels/plain.txt", "/levels/theme/inside.txt", "levels/theme/inside.txt" } )
			{
				Assert.AreEqual( plain.GetAbsolutePath( path ), cached.GetAbsolutePath( path ), path );
				Assert.AreEqual( plain.IsArchive( path.TrimStart( '/' ) ), cached.IsArchive( path.TrimStart( '/' ) ), path );
				Assert.AreEqual( Read( plain, path ), Read( cached, path ), path );
				Assert.AreEqual( Read( plain, path ), Read( cached, path ), $"{path}, remembered" );
			}
			CollectionAssert.AreEqual( plain.GetFiles( "/levels/theme" ), cached.GetFiles( "/levels/theme" ) );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public void AddedFilesAreFoundAfterTheLookupsAreInvalidated()
	{
		var directory = Folder();
		try
		{
			var cached = Open( directory, cache: true );
			Assert.IsFalse( cached.IsArchive( "levels/theme/inside.txt" ), "no theme archive yet" );
			File.WriteAllText( Path.Combine( directory, "levels", "theme.pack" ), "packed" );
			Assert.IsFalse( cached.IsArchive( "levels/theme/inside.txt" ), "a remembered lookup does not see a file added behind its back" );
			cached.InvalidateLookups();
			Assert.IsTrue( cached.IsArchive( "levels/theme/inside.txt" ) );
			Assert.AreEqual( "packed", Read( cached, "/levels/theme/inside.txt" ) );
			Assert.IsTrue( Open( directory, cache: false ).IsArchive( "levels/theme/inside.txt" ), "without the cache it is seen at once" );

			// A write through the file system forgets the lookups itself.
			Assert.IsFalse( cached.IsArchive( "levels/other/inside.txt" ) );
			File.WriteAllText( Path.Combine( directory, "levels", "other.pack" ), "other" );
			using ( var stream = cached.OpenWrite( "/levels/note.txt" ) )
				stream.WriteByte( 1 );
			Assert.IsTrue( cached.IsArchive( "levels/other/inside.txt" ) );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}
}
