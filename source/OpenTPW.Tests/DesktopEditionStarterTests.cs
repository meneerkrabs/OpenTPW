using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class DesktopEditionStarterTests
{
	[TestMethod]
	public void ExplicitExperimentalSelectionUsesOnlyAvailableKnownWorldsAndPreservesTpwPaths()
	{
		WithFolder( true, root =>
		{
			var report = GameInstallation.Inspect( root, GameEdition.ThemeParkInc );
			Assert.IsTrue( report.IsUsable );
			CollectionAssert.AreEqual( new[] { "science" }, report.Worlds.ToArray() );
			Assert.IsFalse( GameInstallation.Inspect( root, GameEdition.ThemeParkWorld ).IsUsable );
			var saved = new SetupSettings( "tpw", "cd", "bonus" );
			var selection = new DesktopSetupSelection( saved );
			selection.Select( GameEdition.ThemeParkInc, root );
			Assert.IsTrue( selection.AcceptInspection( selection.Revision, report ) );
			Assert.IsFalse( selection.CanStart );
			foreach ( var invalid in new[] { "arabian", "polar", "director", "../science" } ) { selection.World = invalid; Assert.IsFalse( selection.CanStart ); }
			selection.World = "science";
			Assert.IsTrue( selection.CanStart );
			CollectionAssert.AreEqual( new[] { "--load-original-level", "science" }, selection.LaunchArguments( new[] { "--setup" } ) );
			var remembered = selection.Remember( saved );
			Assert.AreEqual( "tpw", remembered.GamePath ); Assert.AreEqual( "cd", remembered.CdPath ); Assert.AreEqual( "bonus", remembered.BonusPath );
			Assert.AreEqual( root, remembered.ThemeParkIncPath );
			Assert.ThrowsException<ArgumentException>( () => selection.LaunchArguments( new[] { "--sandbox" } ) );
		} );
	}

	[TestMethod]
	public void MixedUnknownAndChangedFolderCannotStartFromStaleInspection()
	{
		WithFolder( true, root =>
		{
			var selection = new DesktopSetupSelection( new( null, null ) );
			selection.Select( GameEdition.ThemeParkInc, root ); var revision = selection.Revision;
			var report = GameInstallation.Inspect( root, GameEdition.ThemeParkInc );
			selection.Select( GameEdition.ThemeParkWorld, root );
			Assert.IsFalse( selection.AcceptInspection( revision, report ) ); Assert.IsFalse( selection.CanStart );
			Directory.CreateDirectory( Path.Combine( root, "Data/levels/jungle" ) );
			Assert.IsFalse( GameInstallation.Inspect( root, GameEdition.ThemeParkInc ).IsUsable );
			Assert.IsFalse( GameInstallation.Inspect( root, GameEdition.ThemeParkWorld ).IsUsable );
			Assert.IsFalse( GameInstallation.Inspect( root, (GameEdition)77 ).IsUsable );
			Directory.Delete( Path.Combine( root, "Data/levels/jungle" ) ); File.Delete( Path.Combine( root, "Data/Ver.txt" ) );
			Assert.IsFalse( GameInstallation.Inspect( root, GameEdition.ThemeParkInc ).IsUsable );
			Assert.IsTrue( GameInstallation.Inspect( root ).IsUsable, "Legacy structural inspector stays unchanged." );
			Assert.IsFalse( GameInstallation.Inspect( root, GameEdition.ThemeParkWorld ).IsUsable, "An ambiguous TPI theme cannot qualify as explicitly selected TPW." );
		} );
	}

	[TestMethod]
	public void OldSettingsMigrateToTpwAndExplicitTpiChoiceRoundTripsSeparately()
	{
		WithFolder( false, root =>
		{
			var path = Path.Combine( root, "setup.json" ); File.WriteAllText( path, "{\"gamePath\":\"old\",\"cdPath\":\"cd\"}" );
			var old = SetupSettings.Load( path ); Assert.AreEqual( GameEdition.ThemeParkWorld, old.SelectedEdition );
			Assert.IsNull( old.ThemeParkIncPath );
			var changed = old with { SelectedEdition = GameEdition.ThemeParkInc, ThemeParkIncPath = "tpi", ThemeParkIncWorld = "water" };
			changed.Save( path ); Assert.AreEqual( changed, SetupSettings.Load( path ) ); Assert.AreEqual( "old", SetupSettings.Load( path ).GamePath );
		} );
	}

	[TestMethod]
	public void StarterNeverInterceptsExplicitPathsToolsOrGameplayModes()
	{
		var saved = new SetupSettings( null, null );
		Assert.IsTrue( DesktopSetupSelection.ShouldShowStarter( Array.Empty<string>(), null, null, saved ) );
		Assert.IsFalse( DesktopSetupSelection.ShouldShowStarter( Array.Empty<string>(), "cli", null, saved ) );
		Assert.IsFalse( DesktopSetupSelection.ShouldShowStarter( Array.Empty<string>(), null, "env", saved ) );
		foreach ( var mode in new[] { "--headless", "--inspect-model", "--sandbox", "--front-end", "--load-original-level", "--no-autorun", "--advisor-say", "--advisor-response", "--visit-park", "--park-resume-file", "--capture-camera" } )
			Assert.IsFalse( DesktopSetupSelection.ShouldShowStarter( new[] { mode }, null, null, saved ) );
		Assert.IsFalse( DesktopSetupSelection.ShouldShowStarter( Array.Empty<string>(), null, null, saved with { ShowAutorun = false } ) );
		Assert.IsTrue( DesktopSetupSelection.ShouldShowStarter( new[] { "--setup" }, null, null, saved with { ShowAutorun = false } ) );
	}

	[TestMethod]
	public void ActualDesktopResolverStartsRememberedExperimentalWorldWithoutTouchingTpwSettings()
	{
		WithFolder( true, root =>
		{
			var previousConfig = Environment.GetEnvironmentVariable( "OPENTPW_CONFIG_DIR" );
			var previousOverride = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
			var previousPath = Settings.Default.GamePath;
			try
			{
				Environment.SetEnvironmentVariable( "OPENTPW_CONFIG_DIR", Path.Combine( root, "config", "OpenTPW" ) );
				Environment.SetEnvironmentVariable( "OPENTPW_GAME_PATH", null );
				var saved = new SetupSettings( "remembered-tpw", "tpw-cd", SelectedEdition: GameEdition.ThemeParkInc, ThemeParkIncPath: root, ThemeParkIncWorld: "science" );
				saved.Save( SetupSettings.GetDefaultPath() );
				var bytes = File.ReadAllBytes( SetupSettings.GetDefaultPath() );
				var method = typeof( Game ).GetMethod( "ResolveGameFolder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static )!;
				var args = (string[])method.Invoke( null, new object?[] { new[] { "--no-autorun" }, null } )!;
				CollectionAssert.AreEqual( new[] { "--no-autorun", "--load-original-level", "science" }, args );
				Assert.AreEqual( root, Settings.Default.GamePath );
				CollectionAssert.AreEqual( bytes, File.ReadAllBytes( SetupSettings.GetDefaultPath() ) );
				(saved with { ThemeParkIncWorld = "polar" }).Save( SetupSettings.GetDefaultPath() );
				var badBytes = File.ReadAllBytes( SetupSettings.GetDefaultPath() );
				Settings.Default.GamePath = "live-prior";
				var exception = Assert.ThrowsException<System.Reflection.TargetInvocationException>( () => method.Invoke( null, new object?[] { new[] { "--no-autorun" }, null } ) );
				Assert.IsInstanceOfType( exception.InnerException, typeof( DirectoryNotFoundException ) );
				Assert.AreEqual( "live-prior", Settings.Default.GamePath );
				CollectionAssert.AreEqual( badBytes, File.ReadAllBytes( SetupSettings.GetDefaultPath() ) );
			}
			finally
			{
				Settings.Default.GamePath = previousPath;
				Environment.SetEnvironmentVariable( "OPENTPW_CONFIG_DIR", previousConfig );
				Environment.SetEnvironmentVariable( "OPENTPW_GAME_PATH", previousOverride );
			}
		} );
	}

	[TestMethod]
	public void ActualStarterStartCancelAndMissingFolderRevalidateBeforeRemembering()
	{
		WithFolder( true, root =>
		{
			var previousConfig = Environment.GetEnvironmentVariable( "OPENTPW_CONFIG_DIR" );
			var previousPath = Settings.Default.GamePath;
			try
			{
				Environment.SetEnvironmentVariable( "OPENTPW_CONFIG_DIR", Path.Combine( root, "config", "OpenTPW" ) );
				var saved = new SetupSettings( "prior-tpw", "prior-cd", "prior-bonus" ); saved.Save( SetupSettings.GetDefaultPath() );
				var bytes = File.ReadAllBytes( SetupSettings.GetDefaultPath() );
				Settings.Default.GamePath = "live-prior";
				Assert.IsNull( Game.ResolveDesktopGameFolder( new[] { "--setup" }, null, ( _, _ ) => null ) );
				Assert.AreEqual( "live-prior", Settings.Default.GamePath ); CollectionAssert.AreEqual( bytes, File.ReadAllBytes( SetupSettings.GetDefaultPath() ) );
				Assert.ThrowsException<DirectoryNotFoundException>( () => Game.ResolveDesktopGameFolder( new[] { "--setup" }, null, ( _, _ ) => new( Path.Combine( root, "missing" ), null, GameEdition.ThemeParkInc, "science" ) ) );
				Assert.AreEqual( "live-prior", Settings.Default.GamePath ); CollectionAssert.AreEqual( bytes, File.ReadAllBytes( SetupSettings.GetDefaultPath() ) );
				Assert.ThrowsException<ArgumentException>( () => Game.ResolveDesktopGameFolder( new[] { "--setup", "--front-end" }, null, ( _, _ ) => new( root, null, GameEdition.ThemeParkInc, "science" ) ) );
				CollectionAssert.AreEqual( bytes, File.ReadAllBytes( SetupSettings.GetDefaultPath() ) );
				var args = Game.ResolveDesktopGameFolder( new[] { "--setup" }, null, ( _, _ ) => new( root, null, GameEdition.ThemeParkInc, "science" ) );
				CollectionAssert.AreEqual( new[] { "--load-original-level", "science" }, args );
				var remembered = SetupSettings.Load( SetupSettings.GetDefaultPath() );
				Assert.AreEqual( GameEdition.ThemeParkInc, remembered.SelectedEdition ); Assert.AreEqual( "science", remembered.ThemeParkIncWorld );
				Assert.AreEqual( "prior-tpw", remembered.GamePath ); Assert.AreEqual( "prior-cd", remembered.CdPath ); Assert.AreEqual( "prior-bonus", remembered.BonusPath );
			}
			finally { Settings.Default.GamePath = previousPath; Environment.SetEnvironmentVariable( "OPENTPW_CONFIG_DIR", previousConfig ); }
		} );
	}

	[TestMethod]
	public void EditionProbeProtocolRejectsUnknownValuesWithoutChangingLegacyProtocol()
	{
		var missing = GameInstallation.Inspect( "", GameEdition.ThemeParkInc );
		var selection = new DesktopSetupSelection( new( null, null ) ); selection.Select( GameEdition.ThemeParkInc, "" );
		Assert.IsTrue( selection.AcceptInspection( selection.Revision, missing ), "Unusable reports still reach the wizard to display their reason." );
		Assert.IsFalse( selection.CanStart ); Assert.IsTrue( selection.Report!.Problems.Count > 0 );
		Assert.AreEqual( 2, InstallationDiscovery.RunChild( new[] { InstallationDiscovery.InspectCommand, "missing", "77" } ) );
		Assert.AreEqual( 2, InstallationDiscovery.RunChild( new[] { InstallationDiscovery.InspectCommand, "missing", "ThemeParkInc", "extra" } ) );
		Assert.IsFalse( DesktopSetupSelection.CanUseRememberedEdition( new[] { "--sandbox" }, null, null ) );
		Assert.IsFalse( DesktopSetupSelection.CanUseRememberedEdition( new[] { "--headless" }, null, null ) );
	}

	private static void WithFolder( bool tpi, Action<string> action )
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-starter-" ).FullName;
		try
		{
			foreach ( var part in new[] { tpi ? "levels/science" : "levels/jungle", "global", "Language/English" } ) Directory.CreateDirectory( Path.Combine( root, "Data", part ) );
			if ( tpi ) File.WriteAllText( Path.Combine( root, "Data/Ver.txt" ), "synthetic" );
			action( root );
		}
		finally { Directory.Delete( root, true ); }
	}

	[TestMethod]
	public void ExistingStructuralThemeParkWorldInspectorRemainsUsable()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-starter-baseline-" ).FullName;
		try
		{
			foreach ( var part in new[] { "levels/jungle", "global", "Language/English" } ) Directory.CreateDirectory( Path.Combine( root, "Data", part ) );
			Assert.IsTrue( GameInstallation.Inspect( root ).IsUsable );
		}
		finally { Directory.Delete( root, true ); }
	}
}
