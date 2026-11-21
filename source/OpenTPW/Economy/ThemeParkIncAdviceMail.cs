namespace OpenTPW;

/// <summary>
/// One mail the Theme Park Inc tag system builds from a bank notification, as the advice it carries: the
/// message id, its response, the response's speech event and the tag's <c>TAG_SYSTEM.str</c> entry.
/// </summary>
// [BIN:TPI-EXE:0x635D80 mail record] the tag system turns the bank's type-25 notification into a tag entry carrying advice
// message 0x1A2 (text 0x1A2 in the handler) whose response 0x2ED plays speech event 0x365 and whose tag 0x5E holds entry 166;
// the six-month record is the only one the bank posts (docs/reverse/TPI-BANK-MAIL.md)
internal readonly record struct ThemeParkIncAdviceMail( int Message, int Response, uint SpeechEvent, int TextEntry, int Category, int LossReason )
{
	/// <summary>The bank's six-months-in-the-red mail (message <c>0x1A2</c>, response 749, speech 869, tag 0x5E).</summary>
	// [BIN:TPI-EXE:0x63623E letter category] the tag system writes the record's category 0x5E for notification id 8; the letter window's
	// procedure turns a dismissed 0x5E letter into 0x649E60 with reason 0 (0x5269C1)
	public static readonly ThemeParkIncAdviceMail RedSixMonths = new( 0x1A2, 749, 869, 166, 0x5E, 0 );

	/// <summary>The challenge system's lost-challenge mail (message <c>0x1A3</c>, response 750, speech 870, tag 0x5F).</summary>
	// [BIN:TPI-EXE:0x636266 letter category] category 0x5F for notification id 7, dismissed into 0x649E60 with reason 1 (0x5269CA)
	public static readonly ThemeParkIncAdviceMail ChallengeLost = new( 0x1A3, 750, 870, 167, 0x5F, 1 );

	/// <summary>[BIN:TPI-EXE:0x5CEF20 letter subject] <c>TAG_SYSTEM.str</c> entry 165, "Game Over - You Lose!", for every notification letter.</summary>
	public const int SubjectEntry = 165;

	/// <summary>
	/// [BIN:TPI-EXE:0x5CD0D0 letter sender] the sender row the notification constructor picks (row key 1 of the table at 0x9CD6D8): its
	/// two <c>TAG_SYSTEM.str</c> entries, the name (173) and the title (174), and its picture word 1.
	/// </summary>
	public const int SenderNameEntry = 173, SenderTitleEntry = 174, SenderPicture = 1;
}
