using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace OpenTPW.Tests;

/// <summary>The Theme Park Inc player file's folder rules and the loads' teardown, which need no game data.</summary>
[TestClass]
public class ThemeParkIncPlayerFolderTests
{
	private string root = "";

	[TestInitialize]
	public void Initialize() => root = Directory.CreateTempSubdirectory( "opentpw-tpi-folder-" ).FullName;

	[TestCleanup]
	public void Cleanup()
	{
		if ( root.Length > 0 )
			Directory.Delete( root, recursive: true );
	}

	[TestMethod]
	public void LoadingALayoutOrContinuingRetiresTheParkWithoutTheLeavesSave()
	{
		// both loads replace the park through the helper that holds incReplacingPark, so neither overwrites the Science slot or the player file
		const string relative = "source/OpenTPW/Client/GameFlow.ThemeParkIncLayout.cs";
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, relative ) ) )
			directory = directory.Parent;
		Assert.IsNotNull( directory, $"{relative} was not found above {AppContext.BaseDirectory}" );
		var source = File.ReadAllText( Path.Combine( directory.FullName, relative ) );
		Assert.AreEqual( 2, Regex.Matches( source, @"\bTearDownReplacingThemeParkIncPark\(\);" ).Count, "Continue and Load layout" );
		Assert.AreEqual( 1, Regex.Matches( source, @"\bTearDown\(" ).Count, "only the helper tears down" );
	}

	[TestMethod]
	public void TheReplacingTeardownHoldsTheFlagAndReleasesItEvenWhenItThrows()
	{
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		var held = false;
		flow.ReplacingThemeParkIncPark( () => held = flow.incReplacingPark );
		Assert.IsTrue( held, "the leave's save sees a load replacing the park" );
		Assert.IsFalse( flow.incReplacingPark );
		Assert.ThrowsException<InvalidOperationException>( () => flow.ReplacingThemeParkIncPark( () => throw new InvalidOperationException() ) );
		Assert.IsFalse( flow.incReplacingPark, "released after a failed teardown" );
	}

	[TestMethod]
	public void ASaveFolderReachedThroughASymbolicLinkIsStillInsideTheGameFolder()
	{
		// creating a symbolic link needs a privilege on Windows that a test run usually lacks
		if ( OperatingSystem.IsWindows() )
			Assert.Inconclusive( "Symbolic links are tested on macOS and Linux." );
		var game = Path.Combine( root, "Theme Park Inc" );
		Directory.CreateDirectory( Path.Combine( game, "save" ) );
		var linkedGame = Path.Combine( root, "linked game" );
		Directory.CreateSymbolicLink( linkedGame, game );
		var configuration = Path.Combine( root, "config" );
		var expected = Path.Combine( configuration, "save", GameFlow.ThemeParkIncPlayerFolder );
		Assert.AreEqual( expected, GameFlow.ThemeParkIncPlayerRoot( Path.Combine( linkedGame, "save" ), game, configuration ), "a save folder reached through a linked game folder" );
		Assert.AreEqual( expected, GameFlow.ThemeParkIncPlayerRoot( Path.Combine( game, "save" ), linkedGame, configuration ), "a game path that is itself a link" );
		var real = PathLinks.ResolveLinks( Path.Combine( linkedGame, "save", "new" ) );
		Assert.AreEqual( PathLinks.ResolveLinks( Path.Combine( game, "save", "new" ) ), real, "the link is resolved" );
		StringAssert.EndsWith( real, Path.Combine( "Theme Park Inc", "save", "new" ), "parts that do not exist yet are kept" );
	}
}
