using Veldrid;

namespace OpenTPW;

public static partial class Input
{
	/// <summary>
	/// The shortcut each KEYBOARD.str entry names, in file order (21 entries per language): the letter replaces the character of that shortcut.
	/// The window shortcuts B, H, I, A, S, V, F, L, T, G, R come first, then the camcorder, the staff and visitor locators, the postcard,
	/// Ctrl+H (help), Ctrl+C and Ctrl+O, the comma and full stop turn keys, and the pause key.
	/// </summary>
	// [BIN:TPW-EXE:0x40cb80 key group initialiser, 0x40caa0 helper] the 21 strings of table 0x3fc are copied into the char byte (+2) of the shortcut rows 1..11 (strings 0..10), the camcorder row 16 (string 11), the staff locator, visitor locator and postcard rows (12..14), the game rows Ctrl+C and Ctrl+O (16, 17) and ',' and '.' (18, 19), and the system rows pause (20) and toghelp (15); Space (map) and Esc are never overridden. For example Dutch buy is K, hire is A and research is O
	internal static readonly InputButton[] KeyboardStringButtons =
	{
		InputButton.BuyAttractions, InputButton.HireStaff, InputButton.ParkInfo, InputButton.AllAttractions, InputButton.AllStaff, InputButton.AllVisitors,
		InputButton.FinancialInfo, InputButton.Loans, InputButton.StaffBudgets, InputButton.EntryPrice, InputButton.Research,
		InputButton.CamcorderMode, InputButton.StaffLocator, InputButton.VisitorLocator, InputButton.SendEmailPostcard, InputButton.ToggleHelpBar,
		InputButton.ClosePark, InputButton.OpenPark, InputButton.TurnBlueprintLeft, InputButton.TurnBlueprintRight, InputButton.Pause,
	};

	private static readonly Dictionary<InputButton, Key> defaultChordKeys = new();
	private static int appliedLanguageVersion;

	/// <summary>The key that completes the chord of <paramref name="button"/> (the letter after Ctrl for Ctrl+H); the shortcut's own key for a single-key binding.</summary>
	internal static Key ChordKey( InputButton button ) => Bindings[button][^1];

	/// <summary>The key a KEYBOARD.str character stands for: A-Z (either case), the comma and the full stop; null for anything else.</summary>
	internal static Key? KeyForCharacter( string entry )
	{
		if ( entry.Length != 1 )
			return null;
		var character = char.ToUpperInvariant( entry[0] );
		return character switch
		{
			>= 'A' and <= 'Z' => Key.A + (character - 'A'),
			',' => Key.Comma,
			'.' => Key.Period,
			_ => null,
		};
	}

	/// <summary>
	/// Rebinds the shortcuts to the letters of one language's KEYBOARD.str. An entry that is missing or no single known key keeps the shortcut's
	/// default (the Polish file stores two-byte characters, which this does not decode). The chord's modifier keys stay.
	/// </summary>
	internal static void ApplyKeyboardStrings( IReadOnlyList<string> entries, bool? browser = null )
	{
		var inBrowser = browser ?? OperatingSystem.IsBrowser();
		ResetKeyboardStrings();
		for ( var index = 0; index < KeyboardStringButtons.Length && index < entries.Count; index++ )
		{
			var button = KeyboardStringButtons[index];
			if ( KeyForCharacter( entries[index] ) is not { } key || !Bindings.TryGetValue( button, out var chord ) )
				continue;
			// The host camera pans with W, A, S and D, so a plain shortcut cannot take one of them (the Dutch hire letter A): it keeps its default.
			if ( chord.Length == 1 && key is Key.W or Key.A or Key.S or Key.D )
				continue;
			// [EXT:browser-reserved-chords] the browser owns Ctrl+W, Ctrl+T and Ctrl+N (close tab, new tab, new window; the Dutch staff locator is Ctrl+W), so in the browser build a chord keeps its default letter.
			if ( inBrowser && chord.Length > 1 && key is Key.W or Key.T or Key.N )
				continue;
			defaultChordKeys.TryAdd( button, chord[^1] );
			var keys = (Key[])chord.Clone();
			keys[^1] = key;
			Bindings[button] = keys;
		}
	}

	/// <summary>Restores the default (English) shortcut keys.</summary>
	internal static void ResetKeyboardStrings()
	{
		foreach ( var (button, key) in defaultChordKeys )
		{
			var keys = (Key[])Bindings[button].Clone();
			keys[^1] = key;
			Bindings[button] = keys;
		}
		defaultChordKeys.Clear();
	}

	/// <summary>Re-reads KEYBOARD.str when the language changed since the last frame (a language chosen in the options applies without a restart).</summary>
	private static void RefreshKeyboardStrings()
	{
		if ( GameLanguage.Version == appliedLanguageVersion )
			return;
		appliedLanguageVersion = GameLanguage.Version;
		try
		{
			ApplyKeyboardStrings( GameLanguage.Current.LoadStrings( "KEYBOARD.str" ).Entries );
		}
		catch ( Exception )
		{
			// A language without the file (or no resolvable language yet: the frame's input must never throw) keeps the default letters.
			ResetKeyboardStrings();
		}
	}
}
