using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>Options > Game files > Edition: the desktop-only switch between Theme Park World and the experimental Theme Park Inc.</summary>
[TestClass]
public class GameFilesEditionTests
{
	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) )
	{
		Canvas = new UiCanvas( 1024, 768 )
	};

	private static void Pump( UiScreen screen ) => screen.Updating!( Context() );

	private static Task<InstallationDiscoveryResult> Report( string path, GameEdition edition, params string[] worlds ) =>
		Task.FromResult( new InstallationDiscoveryResult( new[] { new InstallationReport( path, path, new[] { "English" }, worlds.Length == 0 && edition == GameEdition.ThemeParkInc ? new[] { "no worlds" } : Array.Empty<string>(), Array.Empty<string>() )
		{ SelectedEdition = edition, Worlds = worlds } }, false, null ) );

	[TestMethod]
	public void EditionSwitchPersistsToSetupJsonAndTakesEffectNextStart()
	{
		var directory = Directory.CreateTempSubdirectory( "opentpw-edition-" ).FullName;
		try
		{
			var path = Path.Combine( directory, "setup.json" );
			new SetupSettings( "/tpw", "/cd", "/bonus" ).Save( path );
			var stack = new UiScreenStack();
			var strings = OriginalUiTests.FakeStrings();
			var game = GameFilesScreen.Create( stack, strings, path, ( p, _ ) => Report( p, GameEdition.ThemeParkWorld ),
				( p, edition, _ ) => Report( p, edition, edition == GameEdition.ThemeParkInc ? new[] { "arabian", "science" } : Array.Empty<string>() ) );
			stack.Push( game );
			((UiButton)game.Find( "edition" )!).Clicked!();
			var screen = stack.Top!;
			Assert.AreEqual( "edition", screen.Name );

			// Pick a TPI folder by typed path: inspected as Theme Park Inc, first world chosen, edition switched.
			((UiButton)screen.Find( "tpiFolderType" )!).Clicked!();
			var field = (UiTextField)stack.Top!.Find( "path" )!;
			field.Text = "/tpi";
			field.Submitted!();
			Pump( screen ); Pump( screen );
			var saved = SetupSettings.Load( path );
			Assert.AreEqual( GameEdition.ThemeParkInc, saved.SelectedEdition );
			Assert.AreEqual( "/tpi", saved.ThemeParkIncPath );
			Assert.AreEqual( "arabian", saved.ThemeParkIncWorld );
			Assert.AreEqual( "/tpw", saved.GamePath, "The Theme Park World folder stays untouched." );
			Assert.AreEqual( "/cd", saved.CdPath );
			Assert.AreEqual( strings.Extra( OpenTpwText.EditionRestart ), ((UiLabel)screen.Find( "status" )!).Text!() );

			// The world button cycles the available worlds.
			((UiButton)screen.Find( "tpiWorld" )!).Clicked!();
			Pump( screen ); Pump( screen );
			Assert.AreEqual( "science", SetupSettings.Load( path ).ThemeParkIncWorld );

			// Back to Theme Park World through its inspection.
			((UiButton)screen.Find( "editionTpw" )!).Clicked!();
			Pump( screen ); Pump( screen );
			saved = SetupSettings.Load( path );
			Assert.AreEqual( GameEdition.ThemeParkWorld, saved.SelectedEdition );
			Assert.AreEqual( "/tpi", saved.ThemeParkIncPath, "The remembered Theme Park Inc folder is kept for switching back." );

			// Going back to the Game files screen must not write a stale edition when it stores something else.
			stack.Pop();
			Assert.AreEqual( game, stack.Top );
			stack.Clear();
		}
		finally { Directory.Delete( directory, true ); }
	}

	[TestMethod]
	public void UnusableExperimentalFolderIsRefusedWithoutChangingTheSavedEdition()
	{
		var directory = Directory.CreateTempSubdirectory( "opentpw-edition-" ).FullName;
		try
		{
			var path = Path.Combine( directory, "setup.json" );
			new SetupSettings( "/tpw", null ).Save( path );
			var before = File.ReadAllBytes( path );
			var stack = new UiScreenStack();
			var screen = GameFilesScreen.CreateEdition( stack, OriginalUiTests.FakeStrings(), path, ( p, edition, _ ) => Report( p, edition ), () => { } );
			stack.Push( screen );
			((UiButton)screen.Find( "tpiFolderType" )!).Clicked!();
			var field = (UiTextField)stack.Top!.Find( "path" )!;
			field.Text = "/tpw";
			field.Submitted!();
			Pump( screen ); Pump( screen );
			CollectionAssert.AreEqual( before, File.ReadAllBytes( path ) );
			stack.Clear();
		}
		finally { Directory.Delete( directory, true ); }
	}
}
