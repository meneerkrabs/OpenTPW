using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// The texts of a tag tube row, composed from the same string tables the original's letter functions fill the record from when it is filed (<c>0x636191</c>): the subject (<c>+0xC04</c>),
/// the sender (<c>+0x808</c>), the addressee (<c>+0xA06</c>), the body with the signature (<c>+0x8</c>) and the date (<c>+0xE24</c>).
/// </summary>
// a row keeps the letter it was filed from rather than its texts: the texts are composed in the language that is selected when the row is shown, where the original kept the language of the
// moment it was filed
internal static class ThemeParkIncTagTubeLetters
{
	/// <summary>The briefing's tag record (<c>0x9D3480 + 0x5C × 40</c>): the body (+8), the sender (+0x1C) and the subject (+0x20) entries of <c>TAG_SYSTEM.str</c>.</summary>
	// [BIN:TPI-EXE:0x9D42E0 briefing tag record] {0x5C, 0x97, 0xA2, 0, 0, 0, 0, 0xAE, 0xA1, 0}: body 162, sender 174, subject 161 ("Welcome!")
	public const int IntroductionBody = 0xA2, IntroductionSender = 0xAE, IntroductionSubject = 0xA1;

	public static ThemeParkIncLetterWindow.Letter Compose( ThemeParkIncTagTubeRow row, IThemeParkIncTexts texts, UiStringTable strings, string addressee )
	{
		var date = ThemeParkIncLetterWindow.FormatDate( strings, row.Date );
		return row.Source switch
		{
			ThemeParkIncLetterSource.Objective => ThemeParkIncObjectiveLetter.Compose( row.Argument, texts, addressee, date ),
			ThemeParkIncLetterSource.Introduction => Introduction( texts, addressee, date, row.Picture ),
			ThemeParkIncLetterSource.Notification => Notification( ThemeParkIncTagLetter.NotificationMail( row.Argument ), texts, addressee, date ),
			_ => throw new InvalidOperationException( $"A tag tube row has the unknown letter source {row.Source}." )
		};
	}

	/// <summary>The briefing (<c>0x5CD6E0(0x5C, 5, 1)</c>, vtable <c>0x93DF08</c>): the tag record's texts, the player as addressee and the sender row key 1's signature.</summary>
	// [BIN:TPI-EXE:0x5CD780 tag letter texts] subject TAG_SYSTEM[record +0x20] (0x5CD780), sender TAG_SYSTEM[record +0x1C] (0x5CD7C0), addressee 0x5CD110 (+0xC = 1: the player's name), body
	// TAG_SYSTEM[record +8] (0x5CD800), then the signature 0x5CD1C0 (+0x18 = 1): "\n", TAG_SYSTEM[sender +4] (173), "\n", TAG_SYSTEM[sender +8] (174), the sender row key 1 that 0x5CCFF0 found
	private static ThemeParkIncLetterWindow.Letter Introduction( IThemeParkIncTexts texts, string addressee, string date, int picture )
	{
		string Tag( int entry ) => texts.Format( ThemeParkIncTable.TagSystem, entry );
		var body = Tag( IntroductionBody ) + "\n" + Tag( ThemeParkIncMissionWindow.SenderName ) + "\n" + Tag( ThemeParkIncMissionWindow.SenderTitle );
		return new ThemeParkIncLetterWindow.Letter( Tag( IntroductionSubject ), Tag( IntroductionSender ), addressee, date, body, picture - 1 );
	}

	/// <summary>A notification letter (<c>0x5CEE50</c>, vtable <c>0x93DFA8</c>), as <see cref="ThemeParkIncLetterWindow.Compose"/> composes it for the letter window.</summary>
	// [BIN:TPI-EXE:0x5CEF20 notification texts] subject TAG_SYSTEM 165, sender TAG_SYSTEM[sender +4] (173), the player, the body (0x5CEF60) and the signature 0x5CD1C0
	private static ThemeParkIncLetterWindow.Letter Notification( ThemeParkIncAdviceMail mail, IThemeParkIncTexts texts, string addressee, string date )
	{
		string Tag( int entry ) => texts.Format( ThemeParkIncTable.TagSystem, entry );
		var body = Tag( mail.TextEntry ) + "\n" + Tag( ThemeParkIncAdviceMail.SenderNameEntry ) + "\n" + Tag( ThemeParkIncAdviceMail.SenderTitleEntry );
		return new ThemeParkIncLetterWindow.Letter( Tag( ThemeParkIncAdviceMail.SubjectEntry ), Tag( ThemeParkIncAdviceMail.SenderNameEntry ), addressee, date, body,
			ThemeParkIncAdviceMail.SenderPicture - 1 );
	}
}
