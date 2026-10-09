using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SetupTests
{
	private string root = null!;

	[TestInitialize]
	public void CreateRoot()
	{
		root = Path.Combine( Path.GetTempPath(), "opentpw-setup-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
	}

	[TestCleanup]
	public void DeleteRoot() => Directory.Delete( root, true );

	/// <summary>The minimum an installation needs: Data/levels, Data/global and one language.</summary>
	private string CreateGame( string name, bool complete = true, string dataSpelling = "Data" )
	{
		var game = Path.Combine( root, name );
		var data = Path.Combine( game, dataSpelling );
		Directory.CreateDirectory( Path.Combine( data, "levels", "jungle" ) );
		Directory.CreateDirectory( Path.Combine( data, "global" ) );
		Directory.CreateDirectory( Path.Combine( data, "Movies" ) );
		var language = Path.Combine( data, "Language", "English" );
		Directory.CreateDirectory( language );
		if ( complete )
			foreach ( var file in new[] { "bankrupt.MD2", "congrats.MD2", "paused.MD2", "swears.txt" } )
				File.WriteAllText( Path.Combine( language, file ), "" );
		return game;
	}

	[TestMethod]
	public void CompleteInstallationIsUsableWithoutWarnings()
	{
		var report = GameInstallation.Inspect( CreateGame( "Theme Park World" ) );
		Assert.IsTrue( report.IsUsable, string.Join( " | ", report.Problems ) );
		CollectionAssert.AreEqual( new[] { "English" }, report.Languages.ToArray() );
		Assert.AreEqual( 0, report.Warnings.Count, string.Join( " | ", report.Warnings ) );
	}

	[TestMethod]
	public void ChoosingTheDataFolderUsesItsParent()
	{
		var game = CreateGame( "Theme Park World" );
		var report = GameInstallation.Inspect( Path.Combine( game, "Data" ) );
		Assert.IsTrue( report.IsUsable );
		Assert.AreEqual( Path.GetFullPath( game ), report.Path );
	}

	[TestMethod]
	public void MissingPartsAreProblemsAndOptionalPartsAreWarnings()
	{
		Assert.IsFalse( GameInstallation.Inspect( "" ).IsUsable );
		Assert.IsFalse( GameInstallation.Inspect( Path.Combine( root, "nowhere" ) ).IsUsable );

		var empty = Path.Combine( root, "empty" );
		Directory.CreateDirectory( empty );
		var noData = GameInstallation.Inspect( empty );
		Assert.IsFalse( noData.IsUsable );
		StringAssert.Contains( noData.Problems[0], "No Data folder" );

		var partial = CreateGame( "partial" );
		Directory.Delete( Path.Combine( partial, "Data", "global" ) );
		Assert.IsTrue( GameInstallation.Inspect( partial ).Problems.Any( problem => problem.Contains( "Data/global" ) ) );

		// Like the CD's own Data/Language: no banners or word filter, and no movies here.
		var cdLike = CreateGame( "cd", complete: false );
		Directory.Delete( Path.Combine( cdLike, "Data", "Movies" ) );
		var report = GameInstallation.Inspect( cdLike );
		Assert.IsTrue( report.IsUsable );
		Assert.AreEqual( 2, report.Warnings.Count, string.Join( " | ", report.Warnings ) );
	}

	[TestMethod]
	public void DataFolderSpellingDoesNotMatter()
	{
		Assert.IsTrue( GameInstallation.Inspect( CreateGame( "cd-mount", dataSpelling: "DATA" ) ).IsUsable );
	}

	[TestMethod]
	public void SetupSettingsRoundTripAndTolerateBrokenFiles()
	{
		var path = Path.Combine( root, "config", SetupSettings.FileName );
		Assert.AreEqual( new SetupSettings( null, null ), SetupSettings.Load( path ) );
		new SetupSettings( "/games/tpw", "/media/cd" ).Save( path );
		Assert.AreEqual( new SetupSettings( "/games/tpw", "/media/cd" ), SetupSettings.Load( path ) );
		StringAssert.Contains( File.ReadAllText( path ), "\"gamePath\"" );
		File.WriteAllText( path, "{ not json" );
		Assert.AreEqual( new SetupSettings( null, null ), SetupSettings.Load( path ) );
	}

	[TestMethod]
	public void ResolutionPrefersOverridesThenSavedThenLegacyThenDetected()
	{
		var saved = CreateGame( "saved" );
		var legacy = CreateGame( "legacy" );
		var detected = CreateGame( "detected" );
		var none = new SetupSettings( null, null );
		var detectCalls = 0;
		string[] Detect() { detectCalls++; return new[] { Path.Combine( root, "missing" ), detected }; }

		// Developer overrides win and are not inspected, so a wrong path still fails loudly later.
		Assert.AreEqual( (Path.GetFullPath( "/elsewhere" ), GamePathSource.CommandLine), GamePathResolution.Resolve( "/elsewhere", saved, new SetupSettings( saved, null ), legacy, Detect ) );
		Assert.AreEqual( (Path.GetFullPath( saved ), GamePathSource.Environment), GamePathResolution.Resolve( null, saved, none, legacy, Detect ) );
		Assert.AreEqual( (Path.GetFullPath( saved ), GamePathSource.Saved), GamePathResolution.Resolve( null, null, new SetupSettings( saved, null ), legacy, Detect ) );
		Assert.AreEqual( 0, detectCalls, "Detection only runs when nothing else applies." );

		// A saved folder that moved falls through to the next source.
		Assert.AreEqual( (Path.GetFullPath( legacy ), GamePathSource.Legacy), GamePathResolution.Resolve( null, null, new SetupSettings( Path.Combine( root, "moved" ), null ), legacy, Detect ) );
		Assert.AreEqual( (Path.GetFullPath( detected ), GamePathSource.Detected), GamePathResolution.Resolve( null, null, none, @"C:\Program Files (x86)\Bullfrog\Theme Park World", Detect ) );
		Assert.IsNull( GamePathResolution.Resolve( null, null, none, null, () => Array.Empty<string>() ) );
	}

	[TestMethod]
	public void ToolModesNeverOpenTheSetupWindow()
	{
		Assert.IsTrue( GamePathResolution.IsInteractive( Array.Empty<string>() ) );
		Assert.IsTrue( GamePathResolution.IsInteractive( new[] { "--sandbox" } ) );
		foreach ( var tool in new[] { "--smoke-test", "--validate-assets", "--inspect-model", "--headless", "--export-park", "--import-park" } )
			Assert.IsFalse( GamePathResolution.IsInteractive( new[] { tool } ), tool );
	}
}
