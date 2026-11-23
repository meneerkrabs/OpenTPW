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

	public static ThemeParkIncLetterWindow.Letter Compose( ThemeParkIncTagTubeRow row, IThemeParkIncTexts texts, UiStringTable strings, string addressee, Func<ThemeParkIncTagTubeRow, string>? memberName = null )
	{
		var date = ThemeParkIncLetterWindow.FormatDate( strings, row.Date );
		return row.Source switch
		{
			ThemeParkIncLetterSource.Objective => ThemeParkIncObjectiveLetter.Compose( row.Argument, texts, addressee, date ),
			ThemeParkIncLetterSource.Introduction => Introduction( texts, addressee, date, row.Picture ),
			ThemeParkIncLetterSource.Notification => Notification( ThemeParkIncTagLetter.NotificationMail( row.Argument ), texts, addressee, date ),
			ThemeParkIncLetterSource.WageDemand => WageDemand( row, texts, addressee, date, memberName?.Invoke( row ) ?? "" ),
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

	/// <summary>
	/// A wage demand (<c>0x5CEFB0</c>, vtable <c>0x93DFD0</c>): subject TAG_SYSTEM 0xFB, the sender row key 5's name, the player, the body 0xFC (kind 0xA) or 0xFD (0xB) with the member's name and the demand,
	/// then the sender's signature.
	/// </summary>
	// [BIN:TPI-EXE:0x5CF050 wage demand subject] kinds 0xA and 0xB: TAG_SYSTEM 0xFB; [BIN:TPI-EXE:0x5CF0F1 wage demand body] 0xFC (0xA) or 0xFD (0xB) with the member's name (0x62B600, parameter 8) and the
	// demand (+0x28, parameter 9); the sender (0x5CD0D0) is the row's +4 (0xB7), the addressee (0x5CD110, +0xC = 1) the player, the signature (0x5CD1C0) "\n", 0xB7, "\n", 0xB8
	// a row keeps the member's thing, not his name: the name is the member's now (a dismissed member's rows are deleted with him, 0x636B10)
	private static ThemeParkIncLetterWindow.Letter WageDemand( ThemeParkIncTagTubeRow row, IThemeParkIncTexts texts, string addressee, string date, string name )
	{
		string Tag( int entry, IReadOnlyDictionary<int, string>? values = null ) => texts.Format( ThemeParkIncTable.TagSystem, entry, values );
		var bodyEntry = row.Argument == ThemeParkIncTagLetter.WageDemandWithStrike ? 0xFC : 0xFD;
		var body = Tag( bodyEntry, new Dictionary<int, string> { [8] = name, [9] = row.GotoArgument.ToString( System.Globalization.CultureInfo.InvariantCulture ) } )
			+ "\n" + Tag( ThemeParkIncTagLetter.WageDemandSenderName ) + "\n" + Tag( ThemeParkIncTagLetter.WageDemandSenderTitle );
		return new ThemeParkIncLetterWindow.Letter( Tag( 0xFB ), Tag( ThemeParkIncTagLetter.WageDemandSenderName ), addressee, date, body, row.Picture - 1, row.GotoKind, row.GotoArgument );
	}
}
