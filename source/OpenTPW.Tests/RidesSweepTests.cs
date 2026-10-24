using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Rides slice rules read from tp.exe in the rides and tours sweep (inconclusive without OPENTPW_GAME_PATH).</summary>
[TestClass]
public class RidesSweepTests
{
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void OpenData()
	{
		previousFileSystem = FileSystem;
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original object archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previousFileSystem!;

	/// <summary>
	/// RIDES-015: the constructor opens a built object and then closes one whose type has a queue (0x004DB712: "Object %d Closing", open word 0, VAR_RIDECLOSED = 1), so the
	/// built ride with a queue starts closed and its script variable says so, while a type without a queue starts open.
	/// </summary>
	[TestMethod]
	public void ABuiltRideWithAQueueStartsClosed()
	{
		var withQueue = 0;
		var without = 0;
		foreach ( var theme in new[] { "jungle", "hallow", "space", "fantasy" } )
		{
			foreach ( var entry in ObjectCatalog.Load( theme ).Buildable.Where( item => item.ScriptPath != null && !item.IsFixedItem ) )
			{
				var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld(), 1, open: Level.BuiltObjectStartsOpen( entry ) );
				Assert.AreEqual( !entry.HasQueue, runtime.IsOpen, $"{theme}/{entry.ArchiveName}: open exactly without a queue" );
				Assert.AreEqual( entry.HasQueue ? 1 : 0, runtime.GetVariable( RideVariables.VAR_RIDECLOSED ), $"{theme}/{entry.ArchiveName}: VAR_RIDECLOSED" );
				runtime.Stop();
				if ( entry.HasQueue )
					withQueue++;
				else
					without++;
			}
		}
		Assert.IsTrue( withQueue > 20 && without > 0, $"both kinds exist ({withQueue} with a queue, {without} without)" );
	}
}
