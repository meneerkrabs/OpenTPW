global using static OpenTPW.Common.GlobalNamespace;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class FileSystemTests
{
	[TestInitialize]
	public void Init()
	{
		Log = new();

		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to an installed game root or its data directory to run asset-dependent tests." );

		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "Challenges.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH does not contain the required game assets (Challenges.sam)." );

		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		FileSystem.RegisterArchiveHandler<SdtArchive>( ".sdt" );
	}

	private static void RequireFile( string path )
	{
		if ( !FileSystem.FileExists( path ) && !FileSystem.IsArchive( FileSystem.GetAbsolutePath( path ) ) )
			Assert.Inconclusive( $"Required game asset is missing: {path}" );
	}

	private static void RequireDirectory( string path )
	{
		if ( !FileSystem.DirectoryExists( path ) && !FileSystem.IsArchive( FileSystem.GetAbsolutePath( path ) ) )
			Assert.Inconclusive( $"Required game directory or archive is missing: {path}" );
	}

	[TestMethod]
	public void TestRead()
	{
		RequireFile( "Challenges.sam" );
		Assert.IsTrue( FileSystem.ReadAllText( "Challenges.sam" ).Length > 0 );
	}

	[TestMethod]
	public void TestReadArchive()
	{
		RequireFile( "levels/jungle/terrain/qickload.txt" );
		Assert.IsTrue( FileSystem.ReadAllText( "levels/jungle/terrain/qickload.txt" ).Length > 0 );
	}

	[TestMethod]
	public void EnumerateFiles()
	{
		RequireDirectory( "/levels" );
		var files = FileSystem.GetFiles( "/levels" );
		var directories = FileSystem.GetDirectories( "/levels" );

		foreach ( var directory in directories )
		{
			Console.WriteLine( $"{directory}/" );
		}

		foreach ( var file in files )
		{
			Console.WriteLine( $"{file}" );
		}

		Assert.IsTrue( files.Length > 0 );
		Assert.IsTrue( directories.Length > 0 );
	}

	[TestMethod]
	public void EnumerateFilesWADArchive()
	{
		RequireDirectory( "/fonts" );
		var files = FileSystem.GetFiles( "/fonts" );

		foreach ( var item in files )
		{
			Console.WriteLine( $"{item}" );
		}

		Assert.IsTrue( files.Length > 0 );
		Assert.IsTrue( files.Any( x => x.EndsWith( "TTF" ) ) );
	}

	[TestMethod]
	public void LoadFromArchiveDirectory()
	{
		RequireFile( "/levels/jungle/terrain/textures/jgr_bas1.wct" );
		var file = FileSystem.ReadAllBytes( "/levels/jungle/terrain/textures/jgr_bas1.wct" );

		Assert.IsTrue( file.Length > 0 );
	}

	[TestMethod]
	public void EnumerateFilesSDTArchive()
	{
		RequireDirectory( "/global/sound/AmbientHD" );
		var files = FileSystem.GetFiles( "/global/sound/AmbientHD" );

		foreach ( var item in files )
		{
			Console.WriteLine( $"{item}" );
		}

		Assert.IsTrue( files.Length > 0 );
		Assert.IsTrue( files.Any( x => x.EndsWith( "mp2" ) ) );
	}
}
