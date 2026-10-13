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
}
