using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Packages;

namespace OpenTPW.Tests;

[TestClass]
public class OnlineCommandTests
{
	[TestMethod]
	public void HeadlessSandboxExportUsesOnlyOfflineSharingFiles()
	{
		var root = Path.Combine( Path.GetTempPath(), "opentpw-command-" + Guid.NewGuid().ToString( "N" ) );
		var game = Path.Combine( root, "game" );
		Directory.CreateDirectory( game );
		var previous = OpenTPW.Common.GlobalNamespace.FileSystem;
		OpenTPW.Common.GlobalNamespace.FileSystem = new BaseFileSystem( game );
		try
		{
			var output = Path.Combine( root, "sandbox.tpwpark" );
			var folders = new OnlineFolders( Path.Combine( root, "online" ) );
			Assert.IsTrue( OnlineCommands.Run( new[] { "--export-park", output }, game, folders, _ => { } ) );
			var payload = MinimalParkPayload.FromSnapshot( ParkPackage.Load( output ).ToSnapshot() );
			Assert.AreEqual( MinimalParkPayload.SourceSandbox, payload.Source );
			Assert.IsNull( payload.Economy );
			Assert.AreEqual( 0, Directory.EnumerateFileSystemEntries( game ).Count() );
			Assert.IsFalse( Directory.Exists( folders.Root ) );
			Assert.ThrowsException<ArgumentException>( () => OnlineCommands.Run( new[] { "--export-park" }, game, folders, _ => { } ) );
			Assert.ThrowsException<ArgumentException>( () => OnlineCommands.Run( new[] { "--export-park", output, "--visit-park", output }, game, folders, _ => { } ) );
			Assert.ThrowsException<ArgumentException>( () => OnlineCommands.Run( new[] { "--export-park", output, "--import-park", output }, game, folders, _ => { } ) );
		}
		finally { OpenTPW.Common.GlobalNamespace.FileSystem = previous; Directory.Delete( root, true ); }
	}

	[TestMethod]
	public void OutputCannotWriteInsideTheInstallationOrThroughDirectoryLinks()
	{
		var root = Path.Combine( Path.GetTempPath(), "opentpw-command-path-" + Guid.NewGuid().ToString( "N" ) );
		var game = Path.Combine( root, "game" );
		Directory.CreateDirectory( game );
		try
		{
			Assert.ThrowsException<ArgumentException>( () => OnlineCommands.ValidateOutputPath( Path.Combine( game, "Data", "park.tpwpark" ), game ) );
			Assert.AreEqual( Path.Combine( root, "game-other", "park.tpwpark" ), OnlineCommands.ValidateOutputPath( Path.Combine( root, "game-other", "park.tpwpark" ), game ) );
			if ( !OperatingSystem.IsWindows() )
			{
				var link = Path.Combine( root, "linked-game" );
				Directory.CreateSymbolicLink( link, game );
				Assert.ThrowsException<ArgumentException>( () => OnlineCommands.ValidateOutputPath( Path.Combine( link, "park.tpwpark" ), game ) );
			}
		}
		finally { Directory.Delete( root, true ); }
	}

	[TestMethod]
	public void MalformedImportCreatesNoDownloadedFile()
	{
		var root = Path.Combine( Path.GetTempPath(), "opentpw-command-bad-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		try
		{
			var source = Path.Combine( root, "invalid.tpwpark" );
			File.WriteAllText( source, "invalid package" );
			var folders = new OnlineFolders( Path.Combine( root, "online" ) );
			Assert.ThrowsException<InvalidDataException>( () => OnlineCommands.Import( source, Path.Combine( root, "game" ), folders ) );
			Assert.IsFalse( Directory.Exists( folders.Root ) );
		}
		finally { Directory.Delete( root, true ); }
	}

	[TestMethod]
	public void OriginalAssetCommandsExportValidateAndImportWithoutSaveWrites()
	{
		var data = OriginalParkImportTests.OriginalDataPath();
		var root = Path.Combine( Path.GetTempPath(), "opentpw-command-original-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		var previous = OpenTPW.Common.GlobalNamespace.FileSystem;
		var fileSystem = new BaseFileSystem( data );
		fileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		OpenTPW.Common.GlobalNamespace.FileSystem = fileSystem;
		try
		{
			var output = Path.Combine( root, "original.tpwpark" );
			var folders = new OnlineFolders( Path.Combine( root, "online" ) );
			Assert.IsTrue( OnlineCommands.Run( new[] { "--export-park", output, "--load-original-level", "jungle" }, data, folders, _ => { } ) );
			var imported = OnlineCommands.Import( output, data, folders );
			Assert.IsTrue( imported.Visit.MatchesOriginalImport );
			Assert.IsTrue( File.Exists( imported.Path ) );
			StringAssert.StartsWith( imported.Path, Path.Combine( folders.Root, "visited" ) + Path.DirectorySeparatorChar );
			Assert.IsNull( imported.Visit.Payload.Economy );
			Assert.IsFalse( File.Exists( folders.SettingsFile ) );
			Assert.ThrowsException<ArgumentException>( () => OnlineCommands.Import( output, data, new OnlineFolders( data ) ) );
		}
		finally { OpenTPW.Common.GlobalNamespace.FileSystem = previous; Directory.Delete( root, true ); }
	}
}
