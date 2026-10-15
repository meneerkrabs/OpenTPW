using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The start-up language choice shared by the desktop and the browser (<see cref="LanguageStartup"/>): the game folder
/// holds Data with English, and CD-style <c>&lt;Lang&gt;/data/Language/&lt;Lang&gt;</c> folders beside it.
/// </summary>
[TestClass]
[DoNotParallelize]
public class LanguageStartupTests
{
	private string root = "";
	private GameLanguage? previousLanguage;
	private string? previousOverlay;

	[TestInitialize]
	public void CreateRoot()
	{
		root = Path.Combine( Path.GetTempPath(), $"opentpw-language-startup-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( root );
		previousLanguage = GameLanguage.IsSelected ? GameLanguage.Current : null;
		previousOverlay = GameLanguage.AvailableOverlay;
	}

	[TestCleanup]
	public void DeleteRoot()
	{
		GameLanguage.Current = previousLanguage!;
		GameLanguage.AvailableOverlay = previousOverlay;
		Directory.Delete( root, true );
	}

	private void CreateLanguage( string dataDirectory, string folder, string characters = "abc" )
	{
		var directory = Path.Combine( root, dataDirectory, "Language", folder );
		Directory.CreateDirectory( directory );
		File.WriteAllBytes( Path.Combine( directory, "MBToUni.dat" ), StringTableTests.CreateCharacterTable( characters ) );
		File.WriteAllBytes( Path.Combine( directory, "UITEXT.str" ), StringTableTests.CreateStringTable( new byte[] { 1, 2, 3 } ) );
	}

	/// <summary>A game folder like the player's: Data with English, plus the CD's Dutch and Italian beside it.</summary>
	private string CreateGameFolder()
	{
		CreateLanguage( "Data", "English" );
		CreateLanguage( Path.Combine( "Dutch", "data" ), "Dutch", "xyz" );
		CreateLanguage( Path.Combine( "Italian", "data" ), "Italian", "zyx" );
		// A CD folder without its own text (the English CD's installer files) is not a language.
		Directory.CreateDirectory( Path.Combine( root, "English", "Meshes" ) );
		return Path.Combine( root, "Data" );
	}

	[TestMethod]
	public void TheFolderBesideDataOffersTheCdLanguages()
	{
		var data = CreateGameFolder();
		Assert.AreEqual( root, LanguageStartup.CdLanguageOverlay( data, null ) );
		var warnings = new List<string>();
		LanguageStartup.Apply( data, null, null, root, true, warnings.Add );
		Assert.AreEqual( 0, warnings.Count );
		Assert.AreEqual( root, GameLanguage.AvailableOverlay );
		Assert.AreEqual( "English", GameLanguage.Current.Name );
		CollectionAssert.AreEqual( new[] { "English", "Dutch", "Italian" }, GameLanguage.Choosable().ToArray() );
	}

	[TestMethod]
	public void AGameFolderWithoutOtherLanguagesHasNoOverlay()
	{
		CreateLanguage( "Data", "English" );
		Directory.CreateDirectory( Path.Combine( root, "English", "Meshes" ) );
		var data = Path.Combine( root, "Data" );
		Assert.IsNull( LanguageStartup.CdLanguageOverlay( data, null ) );
		LanguageStartup.Apply( data, null, null, null, true, _ => { } );
		Assert.IsNull( GameLanguage.AvailableOverlay );
		CollectionAssert.AreEqual( new[] { "English" }, GameLanguage.Choosable().ToArray() );
	}

	[TestMethod]
	public void ASavedLanguageIsChosenFromTheOverlay()
	{
		var data = CreateGameFolder();
		LanguageStartup.Apply( data, "dutch", null, root, true, _ => Assert.Fail( "no warning expected" ) );
		Assert.AreEqual( "Dutch", GameLanguage.Current.Name );
		Assert.IsNotNull( GameLanguage.Current.OverlayDataDirectory, "Dutch comes from the CD folder" );
		Assert.AreEqual( "xyz", GameLanguage.Current.LoadStrings( "UITEXT.str" )[0] );
		CollectionAssert.AreEqual( new[] { "English", "Dutch", "Italian" }, GameLanguage.Choosable().ToArray(), "the others stay on offer" );
	}

	private void CreateSpeech( string dataFolder, string language )
	{
		var directory = Path.Combine( root, dataFolder, "global", "speech" );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, "speechHD.SDT" ), language );
		File.WriteAllText( Path.Combine( directory, "lips.WAD" ), language );
		// The Welcome line comes from the level's bank, which each language ships too.
		var level = Path.Combine( root, dataFolder, "levels", "jungle", "Speech" );
		Directory.CreateDirectory( level );
		File.WriteAllText( Path.Combine( level, "speechHD.SDT" ), language );
	}

	/// <summary>
	/// The advisor asks <c>GameLanguage.Current.ResolveDataFile</c> at every line (nothing is cached), so a live switch changes the
	/// speech bank and lip marks together with the text; a language without speech files falls back to the installed ones.
	/// </summary>
	[TestMethod]
	public void TheAdvisorsSpeechFollowsALanguageSwitch()
	{
		var data = CreateGameFolder();
		CreateSpeech( "Data", "English" );
		CreateSpeech( Path.Combine( "Dutch", "data" ), "Dutch" );
		LanguageStartup.Apply( data, null, null, root, true, _ => { } );
		string Bank() => File.ReadAllText( GameLanguage.Current.ResolveDataFile( "global/Speech/speechHD.SDT" )! );
		string Lips() => File.ReadAllText( GameLanguage.Current.ResolveDataFile( "global/Speech/lips.wad" )! );
		Assert.AreEqual( "English", Bank() );
		var version = GameLanguage.Version;
		GameLanguage.Current = GameLanguage.ResolveSwitch( "Dutch" );
		Assert.AreEqual( version + 1, GameLanguage.Version );
		Assert.AreEqual( "Dutch", Bank() );
		Assert.AreEqual( "Dutch", Lips() );
		Assert.AreEqual( "Dutch", File.ReadAllText( GameLanguage.Current.ResolveDataFile( "levels/jungle/Speech/speechHD.SDT" )! ), "the level bank too" );
		GameLanguage.Current = GameLanguage.ResolveSwitch( "Italian" );
		Assert.AreEqual( "English", Bank(), "Italian has no speech files here: the installed ones" );
		GameLanguage.Current = GameLanguage.ResolveSwitch( "English" );
		Assert.AreEqual( "English", Lips() );
	}

	[TestMethod]
	public void AMissingSavedLanguageFallsBackToTheDefaultWithAWarning()
	{
		var data = CreateGameFolder();
		var warnings = new List<string>();
		LanguageStartup.Apply( data, "Swedish", null, root, true, warnings.Add );
		Assert.AreEqual( "English", GameLanguage.Current.Name );
		Assert.AreEqual( 1, warnings.Count );
		StringAssert.Contains( warnings[0], "Swedish" );
		StringAssert.Contains( warnings[0], "default language" );
		Assert.AreEqual( root, GameLanguage.AvailableOverlay, "the CD's languages are still offered" );
	}

	[TestMethod]
	public void AMissingLanguageStillFailsWhereFallbackIsNotWanted()
	{
		var data = CreateGameFolder();
		Assert.ThrowsException<DirectoryNotFoundException>( () => LanguageStartup.Apply( data, "Swedish", null, root, false, _ => { } ) );
	}

	[TestMethod]
	public void AMissingSeparateOverlayFallsBackToTheInstalledLanguage()
	{
		CreateLanguage( "Data", "English" );
		var warnings = new List<string>();
		LanguageStartup.Apply( Path.Combine( root, "Data" ), "German", Path.Combine( root, "gone" ), null, true, warnings.Add );
		Assert.AreEqual( "English", GameLanguage.Current.Name );
		Assert.IsNull( GameLanguage.AvailableOverlay, "a folder that is gone is not offered" );
		Assert.AreEqual( 1, warnings.Count );
	}
}
