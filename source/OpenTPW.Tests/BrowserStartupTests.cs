using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class BrowserStartupTests
{
	private string root = null!;
	private string GameRoot => Path.Combine( root, "game" );
	private string Data => Path.Combine( GameRoot, "Data" );

	[TestInitialize]
	public void Initialize() => root = Path.Combine( Path.GetTempPath(), "opentpw-browser-startup-" + Guid.NewGuid() );

	[TestCleanup]
	public void Cleanup()
	{
		if ( Directory.Exists( root ) )
			Directory.Delete( root, true );
	}

	private void Installation( string theme, bool tpi )
	{
		Directory.CreateDirectory( Path.Combine( Data, "levels", theme ) );
		File.WriteAllText( Path.Combine( Data, "levels", theme, "terrain.wad" ), "synthetic marker, not decoder data" );
		if ( tpi )
			File.WriteAllText( Path.Combine( Data, "Ver.txt" ), "synthetic structure control" );
	}

	[DataTestMethod]
	[DataRow( "arabian" )]
	[DataRow( "science" )]
	[DataRow( "water" )]
	public void ExplicitThemeUsesExistingEditionEvidenceAndKeepsUnsupportedCapabilitiesOff( string theme )
	{
		Installation( theme, true );
		var result = BrowserStartup.Validate( Data, theme );
		Assert.AreEqual( GameEdition.ThemeParkInc, result.Edition );
		Assert.AreEqual( GameEditionEvidence.Structure, result.Evidence );
		var capabilities = EditionCapabilities.ForThemeParkIncSlice();
		Assert.IsTrue( capabilities.Building && capabilities.Guests && capabilities.ExperimentalHud );
		Assert.IsFalse( capabilities.Economy || capabilities.Staff || capabilities.Advisor || capabilities.FrontEnd || capabilities.ParkSaves || capabilities.Sharing );
	}

	[TestMethod]
	public void TpiCannotFallThroughToTheTpwFrontEnd()
	{
		Installation( "water", true );
		Assert.ThrowsException<ArgumentException>( () => BrowserStartup.Validate( Data, "" ) );
		Assert.ThrowsException<DirectoryNotFoundException>( () => BrowserStartup.Validate( Data, "science" ) );
	}

	[TestMethod]
	public void TpwCannotEnterAnExperimentalThemeAndTheLobbyStillWorks()
	{
		Installation( "jungle", false );
		Assert.AreEqual( GameEdition.ThemeParkWorld, BrowserStartup.Validate( Data, "" ).Edition );
		Assert.ThrowsException<ArgumentException>( () => BrowserStartup.Validate( Data, "water" ) );
		Assert.ThrowsException<ArgumentException>( () => BrowserStartup.Validate( Data, "../water" ) );
		Assert.ThrowsException<ArgumentException>( () => BrowserStartup.Validate( Data, "jungle" ) );
	}

	[TestMethod]
	public void AThemeNameWithoutExistingEditionEvidenceIsRefused()
	{
		Installation( "water", false );
		Assert.ThrowsException<ArgumentException>( () => BrowserStartup.Validate( Data, "water" ) );
	}

	[TestMethod]
	public void FreshImportRemovesOnlyStagingAndSupportsCrossEditionRetries()
	{
		Directory.CreateDirectory( Path.Combine( root, "config" ) );
		Directory.CreateDirectory( Path.Combine( root, "save" ) );
		File.WriteAllText( Path.Combine( root, "config", "language.json" ), "retained language" );
		File.WriteAllText( Path.Combine( root, "save", "layout.json" ), "retained checkpoint" );
		Installation( "jungle", false );
		BrowserStartup.BeginImport( GameRoot, false );
		Assert.IsFalse( Directory.Exists( GameRoot ) );
		Installation( "water", true );
		Assert.AreEqual( GameEdition.ThemeParkInc, BrowserStartup.Validate( Data, "water" ).Edition );
		BrowserStartup.BeginImport( GameRoot, false );
		Installation( "jungle", false );
		Assert.AreEqual( GameEdition.ThemeParkWorld, BrowserStartup.Validate( Data, "" ).Edition );
		Assert.AreEqual( "retained language", File.ReadAllText( Path.Combine( root, "config", "language.json" ) ) );
		Assert.AreEqual( "retained checkpoint", File.ReadAllText( Path.Combine( root, "save", "layout.json" ) ) );
	}

	[TestMethod]
	public void ARendererOrWorldRefusesResetWithoutDeletingItsFiles()
	{
		Installation( "water", true );
		Assert.ThrowsException<InvalidOperationException>( () => BrowserStartup.BeginImport( GameRoot, true ) );
		Assert.IsTrue( File.Exists( Path.Combine( Data, "levels", "water", "terrain.wad" ) ) );
		Assert.AreEqual( GameEdition.ThemeParkInc, BrowserStartup.Validate( Data, "water" ).Edition );
	}
	[TestMethod]
	public void DeferringTheThemeWouldEraseNativeStructuralEditionEvidence()
	{
		Installation( "water", true );
		Assert.ThrowsException<ArgumentException>( () => BrowserStartup.Validate( Data, "" ) );
		Directory.Move( Path.Combine( Data, "levels", "water" ), Path.Combine( root, "deferred-water" ) );
		Assert.AreEqual( GameEdition.ThemeParkWorld, BrowserStartup.Validate( Data, "" ).Edition,
			"The native detector sees only the staged directories, so source-list preflight must preserve or refuse the omitted evidence." );
	}

	[DataTestMethod]
	[DataRow( "jungle" )]
	[DataRow( "fantasy" )]
	[DataRow( "hallow" )]
	[DataRow( "space" )]
	public void FullAndStagedMixedSourcesKeepTheSameNativeClassification( string worldTheme )
	{
		Installation( "water", true ); Installation( worldTheme, false );
		File.WriteAllText( Path.Combine( Data, "levels", "Standard.sam" ), "edited synthetic balance bytes" );
		var full = BrowserStartup.Validate( Data, "" );
		Assert.ThrowsException<ArgumentException>( () => BrowserStartup.Validate( Data, "water" ) );
		var balance = File.ReadAllBytes( Path.Combine( Data, "levels", "Standard.sam" ) );
		var version = File.ReadAllBytes( Path.Combine( Data, "Ver.txt" ) );
		var directories = BrowserStartup.BeginImport( GameRoot, false, new[] { "Data/levels/water", "Data/levels/" + worldTheme } );
		Installation( "water", true ); File.WriteAllBytes( Path.Combine( Data, "levels", "Standard.sam" ), balance );
		Assert.AreEqual( GameEdition.ThemeParkInc, GameEditions.Detect( Data ).Edition, "Without source witnesses the selected subset inverted the native classification." );
		BrowserStartup.PreserveThemeDirectories( Data, directories );
		Assert.AreEqual( full, BrowserStartup.Validate( Data, "" ) );
		Assert.ThrowsException<ArgumentException>( () => BrowserStartup.Validate( Data, "water" ) );
		Assert.IsFalse( File.Exists( Path.Combine( Data, "levels", worldTheme, "terrain.wad" ) ), "Metadata must not fabricate or load payload." );
		CollectionAssert.AreEqual( balance, File.ReadAllBytes( Path.Combine( Data, "levels", "Standard.sam" ) ) );
		CollectionAssert.AreEqual( version, File.ReadAllBytes( Path.Combine( Data, "Ver.txt" ) ) );
		Assert.IsFalse( GameEditions.HasKnownStandardSam( Data ) );
	}

	[TestMethod]
	public void WitnessesUseCopiedDirectoryCasingAndDoNotCreateAnotherDataRoot()
	{
		Installation( "water", true );
		var actualLevels = Path.Combine( Data, "levels" );
		BrowserStartup.PreserveThemeDirectories( Data, new[] { "DATA\\LEVELS\\WATER", "data//Levels//science", "Data/LEVELS/Science" } );
		Assert.AreEqual( 1, Directory.GetDirectories( GameRoot ).Length );
		Assert.AreEqual( 1, Directory.GetDirectories( Data ).Length );
		Assert.AreEqual( 2, Directory.GetDirectories( actualLevels ).Length );
		Assert.AreEqual( GameEdition.ThemeParkInc, BrowserStartup.Validate( Data, "water" ).Edition );
		Assert.AreEqual( 1, Directory.GetFiles( actualLevels, "*", SearchOption.AllDirectories ).Length );
	}

	[DataTestMethod]
	[DataRow( "../Data/levels/water" )]
	[DataRow( "OtherData/levels/water" )]
	[DataRow( "Data/levels/waterfall" )]
	[DataRow( "Data/levels/water/asset.wad" )]
	[DataRow( "Data/levels/../water" )]
	public void InvalidWitnessBatchRefusesBeforeRemovingStaging( string invalid )
	{
		Installation( "water", true );
		Assert.ThrowsException<ArgumentException>( () => BrowserStartup.BeginImport( GameRoot, false, new[] { "Data/levels/science", invalid } ) );
		Assert.IsTrue( File.Exists( Path.Combine( Data, "levels", "water", "terrain.wad" ) ) );
		Assert.IsFalse( Directory.Exists( Path.Combine( Data, "levels", "science" ) ) );
	}

	[TestMethod]
	public void BrowserExportCarriesWitnessesBeforeNativeEditionAndRendererDecisions()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, "source", "OpenTPW.Web", "Program.cs" ) ) ) directory = directory.Parent;
		Assert.IsNotNull( directory );
		var program = File.ReadAllText( Path.Combine( directory!.FullName, "source", "OpenTPW.Web", "Program.cs" ) );
		program = System.Text.RegularExpressions.Regex.Replace( program, @"(?m)^[\t ]*//.*$", "" );
		StringAssert.Contains( program, "sourceThemeDirectories = BrowserStartup.BeginImport( GameRoot, renderer != null || flow != null, themeDirectories );" );
		var preserve = program.IndexOf( "BrowserStartup.PreserveThemeDirectories( dataDirectory, sourceThemeDirectories );", StringComparison.Ordinal );
		var validate = program.IndexOf( "BrowserStartup.Validate( dataDirectory, theme )", StringComparison.Ordinal );
		var renderer = program.IndexOf( "renderer = new Renderer", StringComparison.Ordinal );
		Assert.IsTrue( preserve >= 0 && preserve < validate && validate < renderer );
		StringAssert.Contains( program, "flow.StartLevel( theme, original: true, developerPanels: false );" );
		var detector = File.ReadAllText( Path.Combine( directory.FullName, "source", "OpenTPW", "Client", "Setup", "GameEdition.cs" ) );
		var detect = detector.IndexOf( "public static GameEditionReport Detect", StringComparison.Ordinal );
		Assert.IsTrue( detect >= 0 );
		var hash = detector.IndexOf( "HasKnownStandardSam( dataDirectory )", detect, StringComparison.Ordinal );
		var structure = detector.IndexOf( "ThemeParkInc.Any", detect, StringComparison.Ordinal );
		Assert.IsTrue( hash >= 0 && structure > hash, "The real-byte identity branch must retain precedence over directory structure." );
	}

}
