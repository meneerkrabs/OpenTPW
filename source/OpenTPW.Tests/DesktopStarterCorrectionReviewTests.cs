using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class DesktopStarterCorrectionReviewTests
{
	private string root = null!, tpw = null!, oldPath = null!;
	private string? oldConfig, oldGame, oldNoSetup, oldNoAutorun;
	private Logger oldLog = null!;
	[TestInitialize]
	public void Setup()
	{
		root = Directory.CreateTempSubdirectory( "opentpw-starter-correction-" ).FullName; tpw = Path.Combine( root, "tpw" );
		foreach ( var part in new[] { "Data/global", "Data/Language/English", "Data/levels/jungle" } ) Directory.CreateDirectory( Path.Combine( tpw, part ) );
		oldPath = Settings.Default.GamePath; oldLog = Log; Log = new(); oldConfig = Environment.GetEnvironmentVariable( "OPENTPW_CONFIG_DIR" ); oldGame = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" ); oldNoSetup = Environment.GetEnvironmentVariable( "OPENTPW_NO_SETUP" ); oldNoAutorun = Environment.GetEnvironmentVariable( "OPENTPW_NO_AUTORUN" );
		Environment.SetEnvironmentVariable( "OPENTPW_CONFIG_DIR", Path.Combine( root, "config", "OpenTPW" ) ); Environment.SetEnvironmentVariable( "OPENTPW_GAME_PATH", null ); Environment.SetEnvironmentVariable( "OPENTPW_NO_SETUP", null ); Environment.SetEnvironmentVariable( "OPENTPW_NO_AUTORUN", null );
	}
	[TestCleanup]
	public void Cleanup()
	{
		Settings.Default.GamePath = oldPath; Log = oldLog; Environment.SetEnvironmentVariable( "OPENTPW_CONFIG_DIR", oldConfig ); Environment.SetEnvironmentVariable( "OPENTPW_GAME_PATH", oldGame ); Environment.SetEnvironmentVariable( "OPENTPW_NO_SETUP", oldNoSetup ); Environment.SetEnvironmentVariable( "OPENTPW_NO_AUTORUN", oldNoAutorun ); Directory.Delete( root, true );
	}
	private byte[] Remember( GameEdition edition = GameEdition.ThemeParkInc )
	{
		new SetupSettings( tpw, null, ShowAutorun: true, SelectedEdition: edition, ThemeParkIncPath: Path.Combine( root, "unavailable-tpi" ), ThemeParkIncWorld: "../forged-world" ).Save( SetupSettings.GetDefaultPath() ); return File.ReadAllBytes( SetupSettings.GetDefaultPath() );
	}
	private static SetupWizard.Result? NoWizard( SetupSettings _, InstallationDiscoveryResult? __ ) => throw new AssertFailedException( "An explicit tool must not open the starter." );
	[DataTestMethod, DataRow( "--m3-gate" ), DataRow( "--m4-gate" )]
	public void ActualToolIgnoresInvalidRememberedEditionAndWorldWithoutRewritingThem( string flag )
	{
		var before = Remember( (GameEdition)77 ); var edition = GameEditions.Current; var args = new[] { flag, "--theme", "jungle" }; var result = Game.ResolveDesktopGameFolder( args, null, NoWizard, () => throw new AssertFailedException( "Known TPW folder needs no search." ) );
		CollectionAssert.AreEqual( args, result ); Assert.AreEqual( tpw, Settings.Default.GamePath ); Assert.AreEqual( edition, GameEditions.Current ); CollectionAssert.AreEqual( before, File.ReadAllBytes( SetupSettings.GetDefaultPath() ) );
	}
	[DataTestMethod, DataRow( "--m3-gate" ), DataRow( "--m4-gate" )]
	public void ActualToolKeepsCommandLineThenEnvironmentOverrideAheadOfRememberedChoice( string flag )
	{
		var before = Remember(); var cli = Path.Combine( root, "explicit-cli" ); var environment = Path.Combine( root, "explicit-env" ); Environment.SetEnvironmentVariable( "OPENTPW_GAME_PATH", environment ); var args = new[] { flag, "--no-autorun" };
		CollectionAssert.AreEqual( args, Game.ResolveDesktopGameFolder( args, cli, NoWizard ) ); Assert.AreEqual( cli, Settings.Default.GamePath );
		CollectionAssert.AreEqual( args, Game.ResolveDesktopGameFolder( args, null, NoWizard ) ); Assert.AreEqual( environment, Settings.Default.GamePath ); CollectionAssert.AreEqual( before, File.ReadAllBytes( SetupSettings.GetDefaultPath() ) );
	}
	[DataTestMethod, DataRow( "--m3-gate" ), DataRow( "--m4-gate" )]
	public void ConflictingSetupRefusesBeforeMalformedConfigurationOrDeveloperOverrideCanAct( string flag )
	{
		var path = SetupSettings.GetDefaultPath(); Directory.CreateDirectory( Path.GetDirectoryName( path )! ); File.WriteAllText( path, "{ malformed own configuration" ); var before = File.ReadAllBytes( path ); Settings.Default.GamePath = "prior-memory"; Environment.SetEnvironmentVariable( "OPENTPW_GAME_PATH", tpw );
		var error = Assert.ThrowsException<ArgumentException>( () => Game.ResolveDesktopGameFolder( new[] { flag, "--setup" }, tpw, NoWizard, () => throw new AssertFailedException( "Conflict must not discover." ) ) );
		StringAssert.Contains( error.Message, "--setup" ); Assert.AreEqual( "prior-memory", Settings.Default.GamePath ); CollectionAssert.AreEqual( before, File.ReadAllBytes( path ) );
	}
	[TestMethod]
	public void BothGateFlagsKeepExistingArgumentOrderAndToolResolution()
	{
		var before = Remember(); var args = new[] { "--m4-gate", "--m3-gate", "--no-autorun" }; CollectionAssert.AreEqual( args, Game.ResolveDesktopGameFolder( args, null, NoWizard ) ); Assert.AreEqual( tpw, Settings.Default.GamePath ); CollectionAssert.AreEqual( before, File.ReadAllBytes( SetupSettings.GetDefaultPath() ) );
	}
}
