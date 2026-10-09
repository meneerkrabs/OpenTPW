using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The CD autorun launcher (docs/AUTORUN.md): layout, availability, focus, clicks and scaling on synthetic art; private CD data behind OPENTPW_GAME_PATH.</summary>
[TestClass]
public class AutorunTests
{
	private static AutorunBitmap Solid( int width, int height, byte value )
	{
		var rgba = new byte[width * height * 4];
		Array.Fill( rgba, value );
		return new AutorunBitmap( width, height, rgba );
	}

	private static AutorunView CreateView( string language, bool readme )
	{
		var rows = new Dictionary<string, int> { ["nvPlayY"] = 61, ["nvInstallY"] = 103, ["nvUninstallY"] = 145, ["nvReinstallY"] = 187, ["nvTechbuttonY"] = 229, ["nvReadmeY"] = 271, ["nvQuitY"] = 313 };
		var art = Enum.GetValues<AutorunButtonId>().ToDictionary( id => id, id => Solid( 224, 40, (byte)(100 + (int)id) ) );
		return new AutorunView( new AutorunAssets( "/cd/Autorun", language, Solid( 640, 480, 10 ), art, rows ), readme );
	}

	[TestMethod]
	public void EnglishHasSevenRowsAndShortLanguagesSix()
	{
		var english = CreateView( "English", true );
		CollectionAssert.AreEqual( new[] { 61, 103, 145, 187, 229, 271, 313 }, english.Buttons.Select( button => button.Y ).ToArray() );
		var dutch = CreateView( "Dutch", true );
		CollectionAssert.AreEqual( new[] { 61, 103, 145, 187, 229, 271 }, dutch.Buttons.Select( button => button.Y ).ToArray() );
		Assert.AreEqual( AutorunButtonId.Readme, dutch.Buttons[4].Id );
		Assert.AreEqual( AutorunButtonId.Exit, dutch.Buttons[5].Id );
		Assert.IsFalse( AutorunAssets.UsesShortLayout( "German" ) );
		Assert.IsTrue( AutorunAssets.UsesShortLayout( "Swedish" ) );
	}

	[TestMethod]
	public void OnlyAvailableButtonsAreDrawnAndClickable()
	{
		var view = CreateView( "English", readme: false );
		var frame = view.Compose();
		Assert.AreEqual( (byte)100, frame[(61 * 640 + 5) * 4] ); // Play drawn from its bitmap
		Assert.AreEqual( (byte)10, frame[(103 * 640 + 5) * 4] ); // Install: the backdrop's dim label shows
		Assert.AreEqual( (byte)10, frame[(271 * 640 + 5) * 4] ); // Read-me without a file is unavailable
		Assert.AreEqual( (byte)106, frame[(313 * 640 + 5) * 4] ); // Exit
		Assert.IsNull( view.HitTest( 5, 110 ) );
		Assert.AreEqual( AutorunAction.None, view.Handle( new Vector2( 5, 110 ), false, true, UiKeys.None ) );
		Assert.AreEqual( AutorunAction.Play, view.Handle( new Vector2( 5, 70 ), false, true, UiKeys.None ) );
		Assert.AreEqual( AutorunAction.None, view.Handle( new Vector2( 100, 330 ), true, false, UiKeys.None ) );
		Assert.AreEqual( AutorunAction.Exit, view.Handle( new Vector2( 100, 330 ), false, true, UiKeys.None ) );
		// Pressed on one button, released on another: no click.
		Assert.AreEqual( AutorunAction.None, view.Handle( new Vector2( 5, 70 ), true, false, UiKeys.None ) );
		Assert.AreEqual( AutorunAction.None, view.Handle( new Vector2( 5, 330 ), false, true, UiKeys.None ) );
	}

	[TestMethod]
	public void DraggingOntoAButtonDoesNotClick()
	{
		var view = CreateView( "English", readme: true );
		// Press on the backdrop or an unavailable button, release on Play or Exit: nothing.
		Assert.AreEqual( AutorunAction.None, view.Handle( new Vector2( 400, 70 ), true, false, UiKeys.None ) );
		Assert.AreEqual( AutorunAction.None, view.Handle( new Vector2( 5, 70 ), false, true, UiKeys.None ) );
		Assert.AreEqual( AutorunAction.None, view.Handle( new Vector2( 5, 110 ), true, false, UiKeys.None ) );
		Assert.AreEqual( AutorunAction.None, view.Handle( new Vector2( 5, 330 ), false, true, UiKeys.None ) );
		// A normal press and release still works.
		view.Handle( new Vector2( 5, 70 ), true, false, UiKeys.None );
		Assert.AreEqual( AutorunAction.Play, view.Handle( new Vector2( 5, 70 ), false, true, UiKeys.None ) );
	}

	[TestMethod]
	public void TabMovesFocusAndAcceptActivates()
	{
		var view = CreateView( "English", readme: true );
		Assert.IsNull( view.Focus );
		view.Handle( null, false, false, UiKeys.Tab );
		Assert.AreEqual( AutorunButtonId.Play, view.Focus!.Id );
		view.Handle( null, false, false, UiKeys.Tab );
		Assert.AreEqual( AutorunButtonId.Readme, view.Focus!.Id );
		view.Handle( null, false, false, UiKeys.Up );
		Assert.AreEqual( AutorunButtonId.Play, view.Focus!.Id );
		Assert.AreEqual( AutorunAction.Play, view.Handle( null, false, false, UiKeys.Accept ) );
		var plain = CreateView( "English", true ).Compose().ToArray();
		var focused = view.Compose();
		Assert.IsTrue( view.Compose().Zip( plain ).Any( pair => pair.First != pair.Second ), "the focus rectangle changes pixels" );
		Assert.AreNotEqual( plain[(63 * 640 + 3) * 4], focused[(63 * 640 + 3) * 4] );
	}

	[TestMethod]
	public void FitUsesTheLargestIntegerScaleCentred()
	{
		Assert.AreEqual( (0, 0, 640, 480), AutorunView.Fit( 640, 480 ) );
		Assert.AreEqual( (320, 0, 1920, 1440), AutorunView.Fit( 2560, 1440 ) );
		Assert.AreEqual( (0, 30, 640, 480), AutorunView.Fit( 640, 540 ) );
		Assert.AreEqual( (0, 0, 320, 240), AutorunView.Fit( 320, 240 ) );
	}

	[TestMethod]
	public void SettingAndSwitchesDisableTheLauncher()
	{
		Assert.IsTrue( AutorunLauncher.IsDisabled( ["--no-autorun"], new SetupSettings( null, null ) ) );
		Assert.IsTrue( AutorunLauncher.IsDisabled( [], new SetupSettings( null, null, null, false ) ) );
		Assert.IsFalse( AutorunLauncher.IsDisabled( [], new SetupSettings( null, null ) ) );
	}

	[TestMethod]
	public void LoadsTheOriginalCdLauncher()
	{
		var game = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		var folder = AutorunAssets.FindFolder( [game] );
		if ( folder == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to a game or CD folder with an Autorun folder to run the original autorun tests." );
		var assets = AutorunAssets.Load( folder!, "English", out var problem );
		Assert.IsNotNull( assets, problem );
		Assert.AreEqual( 61, assets!.Rows["nvPlayY"] );
		Assert.AreEqual( 313, assets.Rows["nvQuitY"] );
		var view = new AutorunView( assets, assets.FindReadme() != null );
		Assert.AreEqual( 7, view.Buttons.Count );
		Assert.IsTrue( view.Buttons.All( button => button.Width == 224 && button.Height == 40 ) );
		Assert.AreEqual( 640 * 480 * 4, view.Compose().Length );
	}
}
