using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Roundtrip of every sprite bank (<c>.FPC</c>, <c>.TPC</c>) and animation table (<c>.ESP</c>) in <c>esprites.wad</c>:
/// writer(reader(x)) equals x byte for byte. Inconclusive without OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SpriteBankWriterCorpusTests
{
	private BaseFileSystem? previousFileSystem;
	private Logger? previousLog;
	private bool initialized;

	[TestCleanup]
	public void Cleanup()
	{
		if ( !initialized )
			return;
		FileSystem = previousFileSystem!;
		Log = previousLog!;
	}

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		previousLog = Log;
		initialized = true;
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the sprite bank roundtrip corpus." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "esprites.wad" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks esprites.wad." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	private static IEnumerable<string> Files( string directory )
	{
		foreach ( var file in FileSystem.GetFiles( directory ) )
			yield return file.StartsWith( '/' ) ? file : $"{directory}/{Path.GetFileName( file )}";
		foreach ( var child in FileSystem.GetDirectories( directory ) )
			foreach ( var nested in Files( child.StartsWith( '/' ) ? child : $"{directory}/{Path.GetFileName( child )}" ) )
				yield return nested;
	}

	private static byte[] ReadAll( string path )
	{
		using var stream = FileSystem.OpenRead( path );
		using var memory = new MemoryStream();
		stream.CopyTo( memory );
		return memory.ToArray();
	}

	[TestMethod]
	public void EverySpriteBankAndEspRewritesIdentically()
	{
		var banks = 0;
		var tables = 0;
		var failures = new List<string>();
		foreach ( var path in Files( "/esprites" ) )
		{
			var extension = Path.GetExtension( path ).ToUpperInvariant();
			if ( extension is not (".FPC" or ".TPC" or ".ESP") )
				continue;
			var original = ReadAll( path );
			byte[] rewritten;
			if ( extension == ".ESP" )
			{
				tables++;
				rewritten = SpriteAnimationWriter.Write( new SpriteAnimationFile( new MemoryStream( original ) ) );
			}
			else
			{
				banks++;
				rewritten = SpriteBankWriter.Write( new SpriteBankFile( new MemoryStream( original ) ) );
			}
			if ( !original.AsSpan().SequenceEqual( rewritten ) )
				failures.Add( $"{path} ({original.Length} -> {rewritten.Length})" );
		}
		Assert.IsTrue( banks == 75 && tables == 46, $"{banks} banks, {tables} tables" );
		Assert.AreEqual( 0, failures.Count, $"{failures.Count} of {banks + tables} differ: {string.Join( "; ", failures.Take( 8 ) )}" );
	}
}
