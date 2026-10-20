using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class DesktopStarterIndependentReviewTests
{
	private string root = null!;
	private string? config, game, noSetup, noAutorun;
	private string oldPath = null!;
	private Logger oldLog = null!;
	private string Tpw => Path.Combine( root, "tpw" );
	private string Tpi => Path.Combine( root, "tpi" );
	[TestInitialize]
	public void Setup()
	{
		root = Directory.CreateTempSubdirectory( "opentpw-starter-review-" ).FullName;
		config = Environment.GetEnvironmentVariable( "OPENTPW_CONFIG_DIR" ); game = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" ); noSetup = Environment.GetEnvironmentVariable( "OPENTPW_NO_SETUP" ); noAutorun = Environment.GetEnvironmentVariable( "OPENTPW_NO_AUTORUN" ); oldPath = Settings.Default.GamePath; oldLog = Log; Log = new();
		Environment.SetEnvironmentVariable( "OPENTPW_CONFIG_DIR", Path.Combine( root, "config", "OpenTPW" ) ); Environment.SetEnvironmentVariable( "OPENTPW_GAME_PATH", null ); Environment.SetEnvironmentVariable( "OPENTPW_NO_SETUP", null ); Environment.SetEnvironmentVariable( "OPENTPW_NO_AUTORUN", null );
		foreach ( var folder in new[] { Tpw, Tpi } )
			foreach ( var part in new[] { "Data/global", "Data/Language/English", folder == Tpw ? "Data/levels/jungle" : "Data/levels/science" } ) Directory.CreateDirectory( Path.Combine( folder, part ) );
		File.WriteAllText( Path.Combine( Tpi, "Data/Ver.txt" ), "synthetic edition witness" );
	}
	[TestCleanup]
	public void Cleanup()
	{
		Settings.Default.GamePath = oldPath; Log = oldLog; Environment.SetEnvironmentVariable( "OPENTPW_CONFIG_DIR", config ); Environment.SetEnvironmentVariable( "OPENTPW_GAME_PATH", game ); Environment.SetEnvironmentVariable( "OPENTPW_NO_SETUP", noSetup ); Environment.SetEnvironmentVariable( "OPENTPW_NO_AUTORUN", noAutorun ); Directory.Delete( root, true );
	}
	[DataTestMethod, DataRow( "--m3-gate", true ), DataRow( "--m4-gate", true ), DataRow( "--m3-gate", false ), DataRow( "--m4-gate", false )]
	public void ActualHeadlessGateCannotOpenStarterOrAcquireRememberedTpiRoute( string flag, bool showAutorun )
	{
		new SetupSettings( Tpw, null, ShowAutorun: showAutorun, SelectedEdition: GameEdition.ThemeParkInc, ThemeParkIncPath: Tpi, ThemeParkIncWorld: "science" ).Save( SetupSettings.GetDefaultPath() );
		var before = File.ReadAllBytes( SetupSettings.GetDefaultPath() ); var wizard = 0;
		var result = Game.ResolveDesktopGameFolder( new[] { flag }, null, ( _, _ ) => { wizard++; return null; } );
		Assert.AreEqual( 0, wizard, "An existing headless command cannot be intercepted by a native starter." );
		CollectionAssert.AreEqual( new[] { flag }, result ); Assert.AreEqual( Tpw, Settings.Default.GamePath, "The existing explicit tool route must use TPW resolution, not a remembered experimental world." ); CollectionAssert.AreEqual( before, File.ReadAllBytes( SetupSettings.GetDefaultPath() ) );
	}
	[DataTestMethod, DataRow( "--m3-gate" ), DataRow( "--m4-gate" )]
	public void ExplicitHeadlessModesHaveNoRememberedEditionOrStarterPolicy( string flag )
	{
		Assert.IsFalse( DesktopSetupSelection.CanUseRememberedEdition( new[] { flag }, null, null ) );
		Assert.IsFalse( DesktopSetupSelection.ShouldShowStarter( new[] { flag } ) );
	}
	[TestMethod]
	public void OldConfigurationUsesTpwWithoutRewritingMigrationOrOtherFields()
	{
		var path = SetupSettings.GetDefaultPath(); Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		var old = $"{{\"gamePath\":\"{Tpw.Replace( "\\", "\\\\" )}\",\"cdPath\":null,\"showAutorun\":false}}"; File.WriteAllText( path, old );
		var bytes = File.ReadAllBytes( path ); var args = Game.ResolveDesktopGameFolder( new[] { "--no-autorun" }, null, ( _, _ ) => throw new AssertFailedException( "Migration must not show a native window." ) );
		CollectionAssert.AreEqual( new[] { "--no-autorun" }, args ); Assert.AreEqual( Tpw, Settings.Default.GamePath ); Assert.AreEqual( GameEdition.ThemeParkWorld, SetupSettings.Load( path ).SelectedEdition ); CollectionAssert.AreEqual( bytes, File.ReadAllBytes( path ) );
	}

	[DataTestMethod, DataRow( 77 ), DataRow( -1 )]
	public void UnknownNumericSavedEditionRefusesDirectStartWithoutOverwritingConfig( int value )
	{
		new SetupSettings( Tpw, null, ShowAutorun: false, SelectedEdition: (GameEdition)value, ThemeParkIncPath: Tpi, ThemeParkIncWorld: "science" ).Save( SetupSettings.GetDefaultPath() );
		var bytes = File.ReadAllBytes( SetupSettings.GetDefaultPath() ); Settings.Default.GamePath = "prior-memory";
		var shown = 0;
		Assert.IsNull( Game.ResolveDesktopGameFolder( new[] { "--no-autorun" }, null, ( _, _ ) => { shown++; return null; } ), "An unknown saved edition opens the wizard and never auto-starts." );
		Assert.AreEqual( 1, shown );
		Assert.AreEqual( "prior-memory", Settings.Default.GamePath ); CollectionAssert.AreEqual( bytes, File.ReadAllBytes( SetupSettings.GetDefaultPath() ) );
	}

	[TestMethod]
	public void MalformedEnumAndCanceledReplacementRemainUnknownWithoutRewritingSettings()
	{
		var path = SetupSettings.GetDefaultPath(); Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		File.WriteAllText( path, "{\"selectedEdition\":\"ThemeParkInc\",\"themeParkIncPath\":\"forged\"}" ); var bytes = File.ReadAllBytes( path );
		Settings.Default.GamePath = "prior-memory"; var shown = 0;
		Assert.IsNull( Game.ResolveDesktopGameFolder( Array.Empty<string>(), null, ( saved, _ ) => { shown++; Assert.AreEqual( GameEdition.ThemeParkWorld, saved.SelectedEdition ); Assert.IsNull( saved.ThemeParkIncPath ); return null; } ) );
		Assert.AreEqual( 1, shown ); Assert.AreEqual( "prior-memory", Settings.Default.GamePath ); CollectionAssert.AreEqual( bytes, File.ReadAllBytes( path ) );
	}

	[TestMethod]
	public void ActualWizardResultWorldRemovedAfterValidationCannotSaveOrReplacePath()
	{
		new SetupSettings( Tpw, "old-cd", "old-bonus" ).Save( SetupSettings.GetDefaultPath() ); var bytes = File.ReadAllBytes( SetupSettings.GetDefaultPath() ); Settings.Default.GamePath = "prior-memory";
		var asked = 0;
		Assert.IsNull( Game.ResolveDesktopGameFolder( new[] { "--setup" }, null, ( _, _ ) =>
		{
			if ( asked++ > 0 ) return null; // the wizard opens again after the failed re-inspection; the player closes it
			Assert.IsTrue( GameInstallation.Inspect( Tpi, GameEdition.ThemeParkInc ).IsUsable ); Directory.Delete( Path.Combine( Tpi, "Data/levels/science" ) ); return new( Tpi, null, GameEdition.ThemeParkInc, "science" );
		} ) );
		Assert.AreEqual( 2, asked );
		Assert.AreEqual( "prior-memory", Settings.Default.GamePath ); CollectionAssert.AreEqual( bytes, File.ReadAllBytes( SetupSettings.GetDefaultPath() ) );
	}

	[TestMethod]
	public void AllThreeWorldsOnlyWhenPresentAndCaseInsensitiveDroppedFileNormalize()
	{
		foreach ( var name in new[] { "arabian", "water", "polar", "director" } ) Directory.CreateDirectory( Path.Combine( Tpi, "Data/levels", name ) );
		Directory.Move( Path.Combine( Tpi, "Data/levels/science" ), Path.Combine( Tpi, "Data/levels/_case-temp" ) );
		Directory.Move( Path.Combine( Tpi, "Data/levels/_case-temp" ), Path.Combine( Tpi, "Data/levels/SCIENCE" ) );
		var beforeEdition = GameEditions.Current; var report = GameInstallation.Inspect( Path.Combine( Tpi, "Data/Ver.txt" ), GameEdition.ThemeParkInc );
		Assert.IsTrue( report.IsUsable ); Assert.AreEqual( Tpi, report.Path ); CollectionAssert.AreEqual( new[] { "arabian", "science", "water" }, report.Worlds.ToArray() ); Assert.AreEqual( beforeEdition, GameEditions.Current );
		var selection = new DesktopSetupSelection( new( Tpw, null ) ); selection.Select( GameEdition.ThemeParkInc, Tpi ); selection.AcceptInspection( selection.Revision, report );
		foreach ( var name in new[] { "ARABIAN", "SCIENCE", "WATER" } ) { selection.World = name; Assert.IsTrue( selection.CanStart ); CollectionAssert.AreEqual( new[] { "--load-original-level", name.ToLowerInvariant() }, selection.LaunchArguments( new[] { "--setup" } ) ); }
		selection.World = "polar"; Assert.IsFalse( selection.CanStart );
	}

	[TestMethod]
	public void HugeSyntheticSamDoesNotGetReadOrAllocatedByKnownIdentityInspection()
	{
		var path = Path.Combine( Tpi, "Data/levels/Standard.sam" ); using ( var file = File.Create( path ) ) file.SetLength( 16L * 1024 * 1024 );
		Assert.IsTrue( GameInstallation.Inspect( Tpi, GameEdition.ThemeParkInc ).IsUsable ); var before = GC.GetAllocatedBytesForCurrentThread();
		Assert.IsTrue( GameInstallation.Inspect( Tpi, GameEdition.ThemeParkInc ).IsUsable ); var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.IsTrue( allocated < 128 * 1024, $"Capped identity inspection allocated {allocated} bytes." ); Assert.IsFalse( GameEditions.HasKnownStandardSam( Path.Combine( Tpi, "Data" ) ) );
	}

	[DataTestMethod, DataRow( "--perf-run" ), DataRow( "--debug-weather" ), DataRow( "--capture-camera" )]
	public void AnOffscreenRunWithoutGameDataRefusesInsteadOfOpeningTheSetupWindow( string flag )
	{
		var offscreen = Window.OffscreenRequested;
		try
		{
			Window.OffscreenRequested = true;
			new SetupSettings( null, null, ShowAutorun: false ).Save( SetupSettings.GetDefaultPath() );
			Settings.Default.GamePath = ""; var wizard = 0;
			Assert.ThrowsException<DirectoryNotFoundException>( () => Game.ResolveDesktopGameFolder( new[] { flag, "1" }, null, ( _, _ ) => { wizard++; return null; },
				() => new( Array.Empty<InstallationReport>(), false, null ) ) );
			Assert.AreEqual( 0, wizard, "an offscreen run never shows the setup window" );
		}
		finally { Window.OffscreenRequested = offscreen; }
	}

	[DataTestMethod, DataRow( "--m3-gate" ), DataRow( "--m4-gate" )]
	public void MissingDataHeadlessGateRefusesAfterEmptyControlledDiscoveryBeforeWizard( string flag )
	{
		new SetupSettings( null, null, ShowAutorun: false, SelectedEdition: GameEdition.ThemeParkInc, ThemeParkIncPath: Tpi, ThemeParkIncWorld: "science" ).Save( SetupSettings.GetDefaultPath() );
		Settings.Default.GamePath = ""; var before = File.ReadAllBytes( SetupSettings.GetDefaultPath() ); var searches = 0; var wizard = 0;
		Assert.ThrowsException<DirectoryNotFoundException>( () => Game.ResolveDesktopGameFolder( new[] { flag }, null, ( _, _ ) => { wizard++; return null; },
			() => { searches++; return new( Array.Empty<InstallationReport>(), false, null ); } ) );
		Assert.AreEqual( 1, searches ); Assert.AreEqual( 0, wizard ); Assert.AreEqual( "", Settings.Default.GamePath ); CollectionAssert.AreEqual( before, File.ReadAllBytes( SetupSettings.GetDefaultPath() ) );
	}

	[DataTestMethod, DataRow( "--m3-gate" ), DataRow( "--m4-gate" )]
	public void ExplicitSetupAndHeadlessGateConflictRefusesBeforeDiscoveryAndWindow( string flag )
	{
		new SetupSettings( Tpw, null, SelectedEdition: GameEdition.ThemeParkInc, ThemeParkIncPath: Tpi, ThemeParkIncWorld: "science" ).Save( SetupSettings.GetDefaultPath() ); var before = File.ReadAllBytes( SetupSettings.GetDefaultPath() );
		Assert.IsFalse( DesktopSetupSelection.ShouldShowStarter( new[] { "--setup", flag } ) );
		Assert.ThrowsException<ArgumentException>( () => Game.ResolveDesktopGameFolder( new[] { "--setup", flag }, null,
			( _, _ ) => throw new AssertFailedException( "Conflicting explicit headless setup cannot open a window." ), () => throw new AssertFailedException( "Conflict must refuse before discovery." ) ) );
		CollectionAssert.AreEqual( before, File.ReadAllBytes( SetupSettings.GetDefaultPath() ) );
	}

}
