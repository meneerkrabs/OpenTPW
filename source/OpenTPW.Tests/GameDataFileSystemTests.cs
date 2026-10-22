using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text;

namespace OpenTPW.Tests;

/// <summary>
/// Issue #754: the desktop's game-data file system remembers its path lookups (GameDataFileSystem.Create), so reading the members of an archive
/// again does not list the directories along their paths again (docs/ENGINE-PERFORMANCE.md, "Start and build hitches").
/// </summary>
[TestClass]
[DoNotParallelize]
public class GameDataFileSystemTests
{
	[TestCleanup]
	public void Cleanup() => LoadProfile.Enable( false );

	[TestMethod]
	public void RepeatedReadsListEachDirectoryOnce()
	{
		Log = new();
		var data = Directory.CreateTempSubdirectory( "opentpw-data-" ).FullName;
		try
		{
			Directory.CreateDirectory( Path.Combine( data, "levels", "jungle" ) );
			File.WriteAllText( Path.Combine( data, "levels", "jungle", "global.sam" ), "loose" );
			WriteWad( Path.Combine( data, "levels", "jungle", "rides.wad" ), "member" );
			var files = GameDataFileSystem.Create( data );
			Assert.IsTrue( files.CacheLookups );

			LoadProfile.Enable( true );
			Assert.AreEqual( "member", files.ReadAllText( "levels/jungle/rides/file.txt" ) );
			Assert.AreEqual( "loose", files.ReadAllText( "levels/jungle/global.sam" ) );
			var first = LoadProfile.Counters().TryGetValue( "directory listings", out var listed ) ? listed : 0;
			Assert.IsTrue( first > 0, "the first reads list the directories along their paths" );
			for ( var read = 0; read < 10; read++ )
			{
				Assert.AreEqual( "member", files.ReadAllText( "levels/jungle/rides/file.txt" ) );
				Assert.AreEqual( "loose", files.ReadAllText( "levels/jungle/global.sam" ) );
			}
			Assert.AreEqual( first, LoadProfile.Counters()["directory listings"], "later reads of the same paths list nothing again" );

			// a file added below the root is found once the lookups are invalidated, as the language and mod switches do
			File.WriteAllText( Path.Combine( data, "levels", "jungle", "added.sam" ), "added" );
			files.InvalidateLookups();
			Assert.AreEqual( "added", files.ReadAllText( "levels/jungle/added.sam" ) );
		}
		finally
		{
			LoadProfile.Enable( false );
			Directory.Delete( data, true );
		}
	}

	/// <summary>A one-member WAD (the layout of OnDemandReadTests' synthetic archive).</summary>
	private static void WriteWad( string path, string contents )
	{
		var filename = Encoding.ASCII.GetBytes( "file.txt\0" );
		var bytes = Encoding.ASCII.GetBytes( contents );
		using var writer = new BinaryWriter( File.Create( path ) );
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
		writer.Write( bytes.Length );
		writer.Write( 0 );
		writer.Write( bytes.Length );
		writer.Write( new byte[12] );
		writer.Write( filename );
		writer.Write( bytes );
	}
}
