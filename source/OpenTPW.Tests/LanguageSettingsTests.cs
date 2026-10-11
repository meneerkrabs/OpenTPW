using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class LanguageSettingsTests
{
	[TestMethod]
	public void TheChoiceIsKeptBesideTheOtherSettings()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-language-{Guid.NewGuid():N}" );
		try
		{
			var path = Path.Combine( directory, LanguageSettings.FileName );
			var diagnostics = new List<string>();
			Assert.AreEqual( new LanguageSettings(), LanguageSettings.Load( path, diagnostics ), "no file is no choice" );
			new LanguageSettings( "Dutch", "/cd" ).Save( path );
			Assert.AreEqual( new LanguageSettings( "Dutch", "/cd" ), LanguageSettings.Load( path, diagnostics ) );
			File.WriteAllText( path, "{ \"Language\": \"  \", }" );
			Assert.AreEqual( new LanguageSettings(), LanguageSettings.Load( path, diagnostics ), "blank values are no choice" );
			File.WriteAllText( path, "not json" );
			Assert.AreEqual( new LanguageSettings(), LanguageSettings.Load( path, diagnostics ) );
			Assert.AreEqual( 1, diagnostics.Count );
			StringAssert.EndsWith( LanguageSettings.GetDefaultPath(), Path.Combine( "OpenTPW", LanguageSettings.FileName ) );
		}
		finally
		{
			if ( Directory.Exists( directory ) )
				Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public void CommandLineThenEnvironmentThenFileThenOldSetting()
	{
		Assert.AreEqual( "German", LanguageSettings.Choose( "German", "French", "Dutch", "Danish" ) );
		Assert.AreEqual( "French", LanguageSettings.Choose( null, "French", "Dutch", "Danish" ) );
		Assert.AreEqual( "Dutch", LanguageSettings.Choose( null, " ", "Dutch", "Danish" ) );
		Assert.AreEqual( "Danish", LanguageSettings.Choose( null, null, "", "Danish" ) );
		Assert.IsNull( LanguageSettings.Choose( null, null, null, "" ) );
	}
}

[TestClass]
[DoNotParallelize]
public class LanguageSwitchTests
{
	/// <summary>Switching the current language bumps <see cref="GameLanguage.Version"/> and reloads the UITEXT cache (needs two installed languages).</summary>
	[TestMethod]
	public void SelectingAnotherLanguageReloadsTheStrings()
	{
		var data = OriginalParkImportTests.OriginalDataPath();
		var overlay = System.IO.Path.GetDirectoryName( data );
		var languages = GameLanguage.Readable( data, overlay );
		if ( languages.Count < 2 )
			Assert.Inconclusive( "Needs a game folder with a second language (a CD-copied folder)." );
		var previous = GameLanguage.IsSelected ? GameLanguage.Current : null;
		var previousOverlay = GameLanguage.AvailableOverlay;
		try
		{
			GameLanguage.AvailableOverlay = overlay;
			GameLanguage.Current = GameLanguage.Resolve( data, languages[0], overlay );
			var first = Localization.Get( UIStrings.GameOptions );
			var version = GameLanguage.Version;
			var selected = GameLanguage.Select( languages[1] );
			Assert.AreEqual( languages[1], selected.Name );
			Assert.AreEqual( version + 1, GameLanguage.Version );
			Assert.AreEqual( OpenTPW.UI.Original.UiStringTable.Load( selected )[UIStrings.GameOptions], Localization.Get( UIStrings.GameOptions ), "Localization follows the switch" );
			Assert.AreNotEqual( first, Localization.Get( UIStrings.GameOptions ), $"{languages[0]} and {languages[1]} name Game Options differently" );
		}
		finally
		{
			GameLanguage.AvailableOverlay = previousOverlay;
			GameLanguage.Current = previous ?? GameLanguage.Resolve( data, null, null );
		}
	}
}
