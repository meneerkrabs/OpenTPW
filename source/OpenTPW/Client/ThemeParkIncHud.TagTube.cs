using OpenTPW.UI.Original;

namespace OpenTPW;

public sealed partial class ThemeParkIncHud
{
	/// <summary>The tag tube window's globals that outlive one opening (the window object <c>0xA60188</c>).</summary>
	private readonly ThemeParkIncTagTubeSession tagTubeSession = new();

	/// <summary>The open tag tube window (null when it is closed).</summary>
	internal ThemeParkIncTagTubeWindow? TagTubeWindow { get; private set; }

	/// <summary>The tag tube window is open.</summary>
	public bool TagTubeOpen => Stack.Screens.Any( screen => screen.Name == ThemeParkIncTagTubeWindow.ScreenName );

	/// <summary>
	/// Opens the tag tube window ("Message Centre") over the HUD, as the message key and the HUD's mail panel do: the window the HUD has open (a letter, the mission window) closes first.
	/// False when it is open already, or the park has no tag tube or is lost.
	/// </summary>
	// [BIN:TPI-EXE:0x42EED0 message command] the key table's "message" command builds the window unless the game mode 0xC65B80 is 3; [BIN:TPI-EXE:0x54AED2 mail panel] the native HUD's mail
	// panel 0x45 builds it from its control 0x46
	// [BIN:TPI-EXE:0x551D1A tube open once] the builder does nothing while the window exists (+0x34 set); else it closes the HUD's window (0x5013C0, message 5 to the window registered at
	// 0xA568BC) and registers itself there (0x501CD0)
	// [BIN:TPI-EXE:0x50AFC0 loss window] a lost park's key tables are disabled (0x431230), so the key opens nothing then
	public bool OpenTagTube()
	{
		if ( Lost || TagTubeOpen || level.Park?.Economy is not { TpiTagTube: not null } economy )
			return false;
		if ( LetterOpen )
			closeOpenLetter?.Invoke();
		CloseMissionWindow();
		ThemeParkIncTagTubeWindow? window = null;
		var addressee = strings.Entry( ThemeParkIncText.NewPlayer );
		window = new ThemeParkIncTagTubeWindow( strings, economy, tagTubeSession, row => ThemeParkIncTagTubeLetters.Compose( row, TextsOrLanguage, strings, addressee ),
			() => RemoveTagTube( window ), RunLetterGoto );
		TagTubeWindow = window;
		Stack.Push( window.Build() );
		return true;
	}

	/// <summary>Closes the tag tube window as the HUD's message 5 does (the shown row is read first).</summary>
	private void CloseTagTube() => TagTubeWindow?.Close();

	private void RemoveTagTube( ThemeParkIncTagTubeWindow? only )
	{
		if ( only != null && !ReferenceEquals( TagTubeWindow, only ) )
			return;
		Stack.RemoveWhere( screen => screen.Name == ThemeParkIncTagTubeWindow.ScreenName );
		TagTubeWindow = null;
	}

	/// <summary>The message key (<see cref="UiKeys.Messages"/>) with no other window open; a text field keeps the key.</summary>
	private void UpdateTagTubeKey( UiInput input )
	{
		if ( !MenuOpen && !Input.TextEntryActive && input.Has( UiKeys.Messages ) )
			OpenTagTube();
	}

	/// <summary>The text HUD's button that opens the tag tube window, below the mission window's button, with the unread count the native panel shows.</summary>
	// [EXT:tpi-text-hud] the native HUD's mail panel 0x45 (0xA6011C) belongs to Theme Park Inc's in-game interface, which the slice replaces with the text HUD; this text button stands in for it:
	// it says the window's own title (UITEXT 861) and, while letters are unread, the panel's number (control 0x46, 0x54F25D: the counter's total)
	// [EXT:tpi-capability-gates] the panel's picture by the highest unread category (control 0x47: 0x9BB5E8, 0x9BB6B8, 0x9BB788) and its chime (sound 0x1E of the bank 0xAE0124 through
	// 0x652860 on every row filed in play) are not ported: Theme Park Inc's interface sounds and the HUD's art are not
	private void BuildTagTubeButton()
	{
		var button = ToolButton( "tagTube", TagTubeButtonText, "", () => OpenTagTube(), 5 );
		button.Help = null;
		button.Bounds = new UiRect( 2024 - 190, 376, 190, 110 );
		button.Anchor = UiAnchor.TopRight;
	}

	private string TagTubeButtonText()
	{
		var title = strings.Entry( ThemeParkIncTagTubeWindow.TitleText );
		return level.Park?.Economy.TpiTagTube is { Unread: > 0 and var unread } ? $"{title} {unread.ToString( System.Globalization.CultureInfo.InvariantCulture )}" : title;
	}
}
