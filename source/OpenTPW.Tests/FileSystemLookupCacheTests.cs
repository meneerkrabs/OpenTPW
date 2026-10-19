using System;
using System.IO;
using System.Linq;
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
	public void RememberedDiskAnswersCorrectTheCaseOfEveryPathAsBefore()
	{
		// the paths a park start asks for: members of the same archive and loose files below directories spelt in another case, each a miss of the case
		// correction that tests and lists the directories along it; the remembered tests and listings must give what asking the disk each time gives
		var directory = Folder();
		try
		{
			Directory.CreateDirectory( Path.Combine( directory, "Data", "Global", "Speech" ) );
			File.WriteAllText( Path.Combine( directory, "Data", "Global", "Speech", "Line.txt" ), "line" );
			File.WriteAllText( Path.Combine( directory, "levels", "Theme.pack" ), "packed" );
			var plain = Open( directory, cache: false );
			var cached = Open( directory, cache: true );
			var paths = new[]
			{
				"/data/global/speech/Line.txt", "/Data/Global/Speech/line.txt", "/DATA/global/SPEECH/LINE.TXT", "/data/global/missing/x.txt", "/data/absent.txt",
				"/levels/theme/inside.txt", "/levels/Theme/inside.txt", "/levels/theme/other/member.txt", "/levels/plain.txt", "/LEVELS/PLAIN.TXT",
			};
			foreach ( var _ in new[] { 0, 1 } )
				foreach ( var path in paths )
				{
					Assert.AreEqual( plain.GetAbsolutePath( path ), cached.GetAbsolutePath( path ), path );
					Assert.AreEqual( plain.IsArchive( path.TrimStart( '/' ) ), cached.IsArchive( path.TrimStart( '/' ) ), path );
					Assert.AreEqual( plain.FileExists( path ), cached.FileExists( path ), path );
				}
			Assert.AreEqual( "line", Read( cached, "/data/global/speech/line.txt" ) );
			Assert.AreEqual( "packed", Read( cached, "/levels/theme/inside.txt" ) );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	/// <summary>Corpus: every path a Jungle start reads below the theme, as asked and in other cases; inconclusive without OPENTPW_GAME_PATH.</summary>
	[TestMethod]
	public void OriginalJunglePathsResolveAsWithoutTheCache()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH is not set." );
		var data = OriginalParkImportTests.OriginalDataPath();
		BaseFileSystem Data( bool cache )
		{
			var fileSystem = new BaseFileSystem( data ) { CacheLookups = cache };
			fileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			return fileSystem;
		}
		var plain = Data( false );
		var cached = Data( true );
		var paths = new System.Collections.Generic.List<string>();
		void Walk( string directory, int depth )
		{
			foreach ( var file in plain.GetFiles( directory ) )
				paths.Add( plain.GetRelativePath( plain.GetAbsolutePath( file ) ) );
			if ( depth < 3 )
				foreach ( var child in plain.GetDirectories( directory ) )
					Walk( plain.GetRelativePath( plain.GetAbsolutePath( child ) ), depth + 1 );
		}
		Walk( "/levels/jungle", 0 );
		Assert.IsTrue( paths.Count > 100, $"{paths.Count} paths" );
		foreach ( var path in paths.SelectMany( path => new[] { path, path.ToLowerInvariant(), path.ToUpperInvariant() } ) )
		{
			Assert.AreEqual( plain.GetAbsolutePath( path ), cached.GetAbsolutePath( path ), path );
			Assert.AreEqual( plain.IsArchive( path.TrimStart( '/' ) ), cached.IsArchive( path.TrimStart( '/' ) ), path );
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
