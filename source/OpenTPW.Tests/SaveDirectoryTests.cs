using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Where saves go now that OpenTPW runs without administrator rights: beside the game when writable, else the settings folder.</summary>
[TestClass]
[DoNotParallelize]
public class SaveDirectoryTests
{
	private string root = null!;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Setup()
	{
		previousLog = Log;
		Log = new Logger();
		root = Path.Combine( Path.GetTempPath(), "opentpw-save-dir-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( Path.Combine( root, "game" ) );
	}

	[TestCleanup]
	public void Cleanup()
	{
		var save = Path.Combine( root, "game", "save" );
		if ( !OperatingSystem.IsWindows() && Directory.Exists( save ) )
			File.SetUnixFileMode( save, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
		Directory.Delete( root, true );
		Log = previousLog;
	}

	[TestMethod]
	public void WritableGameFolderKeepsSavesBesideTheGameAndLeavesNoProbe()
	{
		var directory = Game.ResolveSaveDirectory( Path.Combine( root, "game" ), Path.Combine( root, "settings" ) );
		Assert.AreEqual( Path.Combine( root, "game", "save" ) + Path.DirectorySeparatorChar, directory );
		Assert.AreEqual( 0, Directory.GetFileSystemEntries( directory ).Length );
		Assert.IsFalse( Directory.Exists( Path.Combine( root, "settings" ) ) );
	}

	[TestMethod]
	public void ExistingButNonwritableSaveFolderFallsBackBeforeTheFirstSave()
	{
		if ( OperatingSystem.IsWindows() )
		{
			Assert.Inconclusive( "Unix directory permissions do not apply on Windows." );
			return;
		}
		var save = Path.Combine( root, "game", "save" );
		Directory.CreateDirectory( save );
		File.SetUnixFileMode( save, UnixFileMode.UserRead | UnixFileMode.UserExecute );
		try { using var stream = new FileStream( Path.Combine( save, "probe" ), FileMode.CreateNew ); Assert.Inconclusive( "The current identity can bypass nonwritable directory permissions." ); }
		catch ( UnauthorizedAccessException ) { }

		var directory = Game.ResolveSaveDirectory( Path.Combine( root, "game" ), Path.Combine( root, "settings" ) );
		Assert.AreEqual( Path.Combine( root, "settings", "save" ) + Path.DirectorySeparatorChar, directory );
		Assert.IsTrue( Directory.Exists( directory ) );
	}

	[TestMethod]
	public void FallbackCopiesEarlierSavesOnceWithoutReplacingFiles()
	{
		if ( OperatingSystem.IsWindows() )
		{
			Assert.Inconclusive( "Unix directory permissions do not apply on Windows." );
			return;
		}
		var save = Path.Combine( root, "game", "save" );
		Directory.CreateDirectory( Path.Combine( save, "autosave" ) );
		File.WriteAllText( Path.Combine( save, "park1.otpw" ), "old park" );
		File.WriteAllText( Path.Combine( save, "options.sam" ), "old options" );
		File.WriteAllText( Path.Combine( save, "autosave", "jungle.otpw" ), "old autosave" );
		var fallback = Path.Combine( root, "settings", "save" );
		Directory.CreateDirectory( fallback );
		File.WriteAllText( Path.Combine( fallback, "options.sam" ), "newer options" );
		File.SetUnixFileMode( save, UnixFileMode.UserRead | UnixFileMode.UserExecute );
		try { using var stream = new FileStream( Path.Combine( save, "probe" ), FileMode.CreateNew ); Assert.Inconclusive( "The current identity can bypass nonwritable directory permissions." ); }
		catch ( UnauthorizedAccessException ) { }

		var directory = Game.ResolveSaveDirectory( Path.Combine( root, "game" ), Path.Combine( root, "settings" ) );

		Assert.AreEqual( fallback + Path.DirectorySeparatorChar, directory );
		Assert.AreEqual( "old park", File.ReadAllText( Path.Combine( fallback, "park1.otpw" ) ) );
		Assert.AreEqual( "old autosave", File.ReadAllText( Path.Combine( fallback, "autosave", "jungle.otpw" ) ) );
		Assert.AreEqual( "newer options", File.ReadAllText( Path.Combine( fallback, "options.sam" ) ) );
		Assert.AreEqual( "old park", File.ReadAllText( Path.Combine( save, "park1.otpw" ) ) );

		File.WriteAllText( Path.Combine( fallback, "park1.otpw" ), "played on" );
		Game.ResolveSaveDirectory( Path.Combine( root, "game" ), Path.Combine( root, "settings" ) );
		Assert.AreEqual( "played on", File.ReadAllText( Path.Combine( fallback, "park1.otpw" ) ) );
	}

	[TestMethod]
	public void WindowsManifestDoesNotRequestAdministratorRights()
	{
		var manifest = XDocument.Load( Path.Combine( RepositoryRoot(), "source", "OpenTPW", "app.manifest" ) );
		var level = manifest.Descendants().Single( element => element.Name.LocalName == "requestedExecutionLevel" ).Attribute( "level" )?.Value;
		Assert.AreEqual( "asInvoker", level );
	}

	private static string RepositoryRoot()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
			if ( File.Exists( Path.Combine( directory.FullName, "source", "OpenTPW", "app.manifest" ) ) )
				return directory.FullName;
		Assert.Inconclusive( "Repository sources are not next to the test binaries." );
		return "";
	}
}
