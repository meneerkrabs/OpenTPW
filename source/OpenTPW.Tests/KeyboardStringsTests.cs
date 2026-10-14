using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Veldrid;

namespace OpenTPW.Tests;

/// <summary>The shortcut letters come from each language's KEYBOARD.str (21 entries), as the original's key group initialiser copies them.</summary>
[TestClass]
[DoNotParallelize]
public class KeyboardStringsTests
{
	[TestCleanup]
	public void Cleanup() => Input.ResetKeyboardStrings();

	[TestMethod]
	public void TheTwentyOneEntriesNameTheOriginalShortcutsInOrder()
	{
		Assert.AreEqual( 21, Input.KeyboardStringButtons.Length );
		Assert.AreEqual( InputButton.BuyAttractions, Input.KeyboardStringButtons[0] );
		Assert.AreEqual( InputButton.Research, Input.KeyboardStringButtons[10] );
		Assert.AreEqual( InputButton.CamcorderMode, Input.KeyboardStringButtons[11] );
		Assert.AreEqual( InputButton.ToggleHelpBar, Input.KeyboardStringButtons[15] );
		Assert.AreEqual( InputButton.TurnBlueprintLeft, Input.KeyboardStringButtons[18], "string 18 is the comma row" );
		Assert.AreEqual( InputButton.TurnBlueprintRight, Input.KeyboardStringButtons[19], "string 19 is the full stop row" );
		Assert.AreEqual( InputButton.Pause, Input.KeyboardStringButtons[20] );
	}

	[TestMethod]
	public void LettersCommaAndFullStopMapToKeysAndNothingElseDoes()
	{
		Assert.AreEqual( Key.K, Input.KeyForCharacter( "K" ) );
		Assert.AreEqual( Key.K, Input.KeyForCharacter( "k" ) );
		Assert.AreEqual( Key.Comma, Input.KeyForCharacter( "," ) );
		Assert.AreEqual( Key.Period, Input.KeyForCharacter( "." ) );
		Assert.IsNull( Input.KeyForCharacter( "" ) );
		Assert.IsNull( Input.KeyForCharacter( "ÿ\u001d" ), "two-byte entries (Polish) keep the default" );
		Assert.IsNull( Input.KeyForCharacter( "5" ) );
	}

	[TestMethod]
	public void AnOverrideReplacesTheShortcutKeyAndKeepsTheChordModifier()
	{
		Assert.AreEqual( Key.B, Input.ChordKey( InputButton.BuyAttractions ) );
		Assert.AreEqual( Key.H, Input.ChordKey( InputButton.ToggleHelpBar ) );
		var entries = Enumerable.Range( 0, 21 ).Select( _ => "?" ).ToArray();
		entries[0] = "K";
		entries[15] = "Q";
		Input.ApplyKeyboardStrings( entries );
		Assert.AreEqual( Key.K, Input.ChordKey( InputButton.BuyAttractions ) );
		Assert.AreEqual( Key.Q, Input.ChordKey( InputButton.ToggleHelpBar ) );
		Assert.AreEqual( Key.H, Input.ChordKey( InputButton.HireStaff ), "an unknown entry keeps the default" );
		var previous = Input.Keyboard;
		try
		{
			Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.K } );
			Input.UpdateButtons();
			Assert.IsTrue( Input.Down( InputButton.BuyAttractions ), "the new letter presses the shortcut" );
			Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.B } );
			Input.UpdateButtons();
			Assert.IsFalse( Input.Down( InputButton.BuyAttractions ), "the old letter no longer does" );
			Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.ControlLeft, Key.Q } );
			Input.UpdateButtons();
			Assert.IsTrue( Input.Down( InputButton.ToggleHelpBar ), "Ctrl stays the help chord's modifier" );
		}
		finally
		{
			Input.Keyboard = previous;
			Input.UpdateButtons();
		}
		Input.ResetKeyboardStrings();
		Assert.AreEqual( Key.B, Input.ChordKey( InputButton.BuyAttractions ), "reset restores the defaults" );
		Assert.AreEqual( Key.H, Input.ChordKey( InputButton.ToggleHelpBar ) );
	}

	[TestMethod]
	public void ALetterThatIsACameraPanKeyKeepsTheDefault()
	{
		var entries = Enumerable.Range( 0, 21 ).Select( _ => "?" ).ToArray();
		entries[1] = "A";
		entries[4] = "W";
		entries[14] = "A";
		Input.ApplyKeyboardStrings( entries );
		Assert.AreEqual( Key.H, Input.ChordKey( InputButton.HireStaff ), "the host camera pans with A (the Dutch hire letter), so a press cannot also open the window" );
		Assert.AreEqual( Key.S, Input.ChordKey( InputButton.AllStaff ), "W is a pan key as well" );
		Assert.AreEqual( Key.A, Input.ChordKey( InputButton.SendEmailPostcard ), "a chord with a modifier takes the letter" );
	}

	[TestMethod]
	public void TheCommaAndFullStopEntriesRebindTheTurnKeys()
	{
		var entries = Enumerable.Range( 0, 21 ).Select( _ => "?" ).ToArray();
		entries[18] = ".";
		entries[19] = ",";
		Input.ApplyKeyboardStrings( entries );
		Assert.AreEqual( Key.Period, Input.ChordKey( InputButton.TurnBlueprintLeft ) );
		Assert.AreEqual( Key.Comma, Input.ChordKey( InputButton.TurnBlueprintRight ) );
	}

	[DataTestMethod]
	[DataRow( "English", 'B', 'H', 'R' )]
	[DataRow( "Dutch", 'K', 'A', 'O' )]
	public void TheShippedFilesGiveTheDocumentedLetters( string name, char buy, char hire, char research )
	{
		var language = LanguageTests.OriginalLanguagePublic( name );
		var entries = language.LoadStrings( "KEYBOARD.str" ).Entries;
		Assert.AreEqual( 21, entries.Length );
		Assert.AreEqual( buy.ToString(), entries[0], "buy" );
		Assert.AreEqual( hire.ToString(), entries[1], "hire" );
		Assert.AreEqual( research.ToString(), entries[10], "research" );
		var all = string.Concat( entries );
		Assert.AreEqual( name == "Dutch" ? "KAIVWBFLTMOCWBAHSO,.P" : "BHIASVFLTGRCSVPHCO,.P", all, $"{name} KEYBOARD.str letters in file order" );
	}

	private static IEnumerable<InputButton> DownButtons() => Enum.GetValues<InputButton>().Where( Input.Down );

	private static void AssertNoPlainButtonFiresUnderCtrl( string label )
	{
		var previous = Input.Keyboard;
		try
		{
			foreach ( var chord in Enum.GetValues<InputButton>().Where( button => Input.ChordKey( button ) != default && IsCtrlChord( button ) ) )
			{
				Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.ControlLeft, Input.ChordKey( chord ) } );
				Input.UpdateButtons();
				var fired = DownButtons().Where( button => button != chord && button != InputButton.Clone ).ToArray();
				CollectionAssert.AreEqual( Array.Empty<InputButton>(), fired, $"{label}: Ctrl+{Input.ChordKey( chord )} ({chord}) also fires {string.Join( ",", fired )}" );
			}
		}
		finally
		{
			Input.Keyboard = previous;
			Input.UpdateButtons();
		}
	}

	private static bool IsCtrlChord( InputButton button ) => button is InputButton.OpenPark or InputButton.ClosePark or InputButton.ToggleHelpBar
		or InputButton.StaffLocator or InputButton.VisitorLocator or InputButton.SendEmailPostcard;

	[TestMethod]
	public void TheDutchLettersDoNotMakeACtrlChordFireAPlainShortcut()
	{
		var dutch = "KAIVWBFLTMOCWBAHSO,.P".Select( c => c.ToString() ).ToArray();
		Input.ApplyKeyboardStrings( dutch );
		AssertNoPlainButtonFiresUnderCtrl( "Dutch" );
	}

	[DataTestMethod]
	[DataRow( "English" )]
	[DataRow( "Dutch" )]
	public void NoShippedLanguageLetsAPlainShortcutFireUnderACtrlChord( string name )
	{
		var language = LanguageTests.OriginalLanguagePublic( name );
		Input.ApplyKeyboardStrings( language.LoadStrings( "KEYBOARD.str" ).Entries );
		AssertNoPlainButtonFiresUnderCtrl( name );
	}

	[TestMethod]
	public void TheBrowserKeepsTheDefaultForCtrlChordsItReserves()
	{
		var entries = Enumerable.Range( 0, 21 ).Select( _ => "?" ).ToArray();
		entries[12] = "W";
		entries[13] = "T";
		entries[14] = "N";
		Input.ApplyKeyboardStrings( entries, browser: true );
		Assert.AreEqual( Key.S, Input.ChordKey( InputButton.StaffLocator ), "Ctrl+W would close the tab" );
		Assert.AreEqual( Key.V, Input.ChordKey( InputButton.VisitorLocator ), "Ctrl+T would open a tab" );
		Assert.AreEqual( Key.P, Input.ChordKey( InputButton.SendEmailPostcard ), "Ctrl+N would open a window" );
		Input.ApplyKeyboardStrings( entries, browser: false );
		Assert.AreEqual( Key.W, Input.ChordKey( InputButton.StaffLocator ), "the native build takes the letter" );
	}
}
