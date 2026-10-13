using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StbImageSharp;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class FshPngExportReviewTests
{
	[TestMethod]
	public void ActualCliKeepsGameFileSystemsRendererShaderAndProfileUntouched()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-fsh-review-" ).FullName;
		var log = Log; var files = FileSystem; var saves = SaveFileSystem; var cache = CacheFileSystem;
		var renderer = global::Global.Render; var hotReload = Shader.HotReload;
		try
		{
			FileSystem = SaveFileSystem = CacheFileSystem = new BaseFileSystem( root );
			Shader.HotReload = false;
			LoadProfile.Enable( true );
			using ( LoadProfile.Measure( "sentinel" ) ) { }
			var input = Path.Combine( root, "input.FSH" );
			var output = Path.Combine( root, "selected.PNG" );
			File.WriteAllBytes( input, FshFileTests.CreateExportFixture( true, 0x2A ) );
			var sentinel = FileSystem;
			Game.Run( new[] { "--shader-hot-reload", "--game-path", Path.Combine( root, "missing" ), "--export-fsh-png", input, "01", output } );
			var image = ImageResult.FromMemory( File.ReadAllBytes( output ), ColorComponents.RedGreenBlueAlpha );
			Assert.AreEqual( (2, 3), (image.Width, image.Height) );
			Assert.AreSame( sentinel, FileSystem ); Assert.AreSame( sentinel, SaveFileSystem ); Assert.AreSame( sentinel, CacheFileSystem );
			Assert.AreSame( renderer, global::Global.Render ); Assert.IsFalse( Shader.HotReload );
			Assert.AreEqual( 1, LoadProfile.Snapshot()["sentinel"].Count );
			Assert.IsFalse( Directory.Exists( Path.Combine( root, "missing" ) ) );
		}
		finally
		{
			Log = log; FileSystem = files; SaveFileSystem = saves; CacheFileSystem = cache;
			Shader.HotReload = hotReload; LoadProfile.Enable( false ); Directory.Delete( root, true );
		}
	}

	[TestMethod]
	public void EveryTruncatedInputPrefixRefusesBeforeCreatingOutput()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-fsh-prefix-" ).FullName;
		try
		{
			var bytes = FshFileTests.CreateExportFixture( true, 0x2D );
			var input = Path.Combine( root, "input.fsh" );
			var parent = Path.Combine( root, "absent" );
			for ( var length = 0; length < bytes.Length; length++ )
			{
				File.WriteAllBytes( input, bytes.AsSpan( 0, length ).ToArray() );
				Assert.ThrowsException<InvalidDataException>( () => FshPngExport.Run( input, 1, Path.Combine( parent, "selected.png" ) ), $"prefix {length}" );
				Assert.IsFalse( Directory.Exists( parent ) );
				Assert.AreEqual( length, File.ReadAllBytes( input ).Length );
			}
		}
		finally { Directory.Delete( root, true ); }
	}

	[DataTestMethod]
	[DataRow( "+0" )]
	[DataRow( "-0" )]
	[DataRow( " 0" )]
	[DataRow( "0x0" )]
	[DataRow( "1,000" )]
	[DataRow( "2147483648" )]
	public void CliIndexIsAnExplicitInvariantDecimalBeforeReadingInput( string index )
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-fsh-index-" ).FullName;
		var log = Log;
		try
		{
			Assert.ThrowsException<ArgumentException>( () => Game.Run( new[] { "--export-fsh-png", Path.Combine( root, "missing.fsh" ), index, Path.Combine( root, "out.png" ) } ) );
			Assert.AreEqual( 0, Directory.GetFileSystemEntries( root ).Length );
		}
		finally { Log = log; Directory.Delete( root, true ); }
	}
}
