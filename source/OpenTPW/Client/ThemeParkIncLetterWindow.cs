using Veldrid;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// Theme Park Inc's letter window: the mail window its tag system opens on a notification letter (the bank's six-months-in-the-red
/// letter and the challenge system's firing letter). Dismissing it is what ends the game (docs/reverse/TPI-BANK-MAIL.md).
/// </summary>
internal static class ThemeParkIncLetterWindow
{
	/// <summary>The screen's name on the HUD stack.</summary>
	public const string ScreenName = "tpiLetter";

	// [BIN:TPI-EXE:0x526CD0 letter labels] UITEXT 855 "To:", 856 "From:", 857 "Subject:" and 858 "Date:" (0x402B0D -> 0x500F60)
	public const int ToLabel = 855, FromLabel = 856, SubjectLabel = 857, DateLabel = 858;

	/// <summary>[BIN:TPI-EXE:0x518C30 letter date] UITEXT 956, the template "{0x29}.{0x2A}.{0x2B}" the date field is filled through.</summary>
	public const int DateTemplate = 956;

	/// <summary>The letter's own texts, resolved by the caller from the language's tables.</summary>
	internal sealed record Letter( string Subject, string From, string To, string Date, string Body, int Picture );

	/// <summary>
	/// The texts of a notification letter: the subject, the sender row's name, the addressee, the date and the body with the sender's
	/// signature, from <c>TAG_SYSTEM.str</c> (<paramref name="tags"/>) and <c>UITEXT.str</c>.
	/// </summary>
	internal static Letter Compose( ThemeParkIncAdviceMail mail, StringFile tags, string addressee, string date )
	{
		string Tag( int entry ) => entry >= 0 && entry < tags.Entries.Length ? tags.Entries[entry] : "";
		// [BIN:TPI-EXE:0x5CD1C0 letter signature] after the body: a newline, the sender's name, a newline and the sender's title (the separator 0x9BEF94 is "\n")
		var body = Tag( mail.TextEntry ) + "\n" + Tag( ThemeParkIncAdviceMail.SenderNameEntry ) + "\n" + Tag( ThemeParkIncAdviceMail.SenderTitleEntry );
		return new Letter( Tag( ThemeParkIncAdviceMail.SubjectEntry ), Tag( ThemeParkIncAdviceMail.SenderNameEntry ), addressee, date, body,
			// [BIN:TPI-EXE:0x5271ED letter picture] the mugshot shows frame (record +0xE02) - 1, the sender row's picture word (0x5CD440)
			ThemeParkIncAdviceMail.SenderPicture - 1 );
	}

	/// <summary>
	/// The window over its layout table. <paramref name="dismiss"/> runs once, when the player closes the letter with its close, okay or
	/// go-to button; help does nothing of its own.
	/// </summary>
	internal static UiScreen Create( Letter letter, UiStringTable strings, Action dismiss )
	{
		var table = ThemeParkIncLayoutTables.LetterWindow;
		var screen = new UiScreen( ScreenName );
		var dismissed = false;
		void Dismiss()
		{
			if ( dismissed )
				return;
			dismissed = true;
			dismiss();
		}
		UiModelImage Image( string id, uint control, Func<int>? frame = null ) => screen.Add( new UiModelImage
		{
			Id = id,
			Model = table.Control( control ).Model!,
			Frame = frame ?? (() => 0),
			Bounds = table.Control( control ).Rect,
			Anchor = UiAnchor.Center
		} );
		// [BIN:TPI-EXE:0x9B4854 letter table] the !window 0xA7F8 (46,44)-(2002,1492) carries every control
		Image( "window", 0xA7F8 );
		// [BIN:TPI-EXE:0x526DA9 letter title] the title box 0xA806 is the title control (0x405187 -> 0x501D50, as the Personnel File's) with the record's subject (+0xC04)
		screen.Add( new UiLabel
		{
			Id = "title",
			Text = () => letter.Subject,
			Font = fonts => fonts.Title,
			Shadow = true,
			ShadowAlpha = UiBatch.TpiTitleCopyAlpha,
			Color = UiColors.Title,
			Fit = true,
			Bounds = table.Control( 0xA806 ).Rect,
			Anchor = UiAnchor.Center
		} );
		// [BIN:TPI-EXE:0x526DF9 letter fields] each label gets the colour (0, 0, 0x96, 0xFF) and each value (0, 0, 0, 0xFF) through 0x84A6BB with flags 3, all in font slot 5 (0x401997(5))
		// [EXT:ui-font-tier-by-scale] which shipped font serves font slot 5 and slot 7 is OpenTPW's choice: the label and small roles
		var labelColour = new RgbaByte( 0, 0, 0x96, 0xFF );
		void Field( string id, uint labelControl, int label, uint valueControl, Func<string> value )
		{
			screen.Add( new UiLabel { Id = id + "Label", Text = () => strings.Entry( label ), Font = fonts => fonts.Label, Color = labelColour, Bounds = table.Control( labelControl ).Rect, Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = id, Text = value, Font = fonts => fonts.Label, Color = UiColors.Ink, Fit = true, Bounds = table.Control( valueControl ).Rect, Anchor = UiAnchor.Center } );
		}
		Field( "subject", 0xA7F9, SubjectLabel, 0xA7FA, () => letter.Subject );
		Field( "date", 0xA803, DateLabel, 0xA802, () => letter.Date );
		Field( "from", 0xA7FB, FromLabel, 0xA7FC, () => letter.From );
		Field( "to", 0xA7FF, ToLabel, 0xA800, () => letter.To );
		// [BIN:TPI-EXE:0x5271D2 letter mugshot] the sender's picture: the mugshot 0xA7FE in the !frame 0xA7FD
		Image( "mugshotFrame", 0xA7FD );
		Image( "mugshot", 0xA7FE, () => letter.Picture );
		// [BIN:TPI-EXE:0x52720F letter body] the !frame 0xA801 holds the body (record +0x8) in font slot 7 with 0x84A577(1, 0, 3), inside the frame's inner rectangle
		var frame = table.Control( 0xA801 );
		Image( "bodyFrame", 0xA801 );
		screen.Add( new UiLabel
		{
			Id = "body",
			Text = () => letter.Body,
			Font = fonts => fonts.Small,
			Color = BodyColour,
			Wrap = true,
			Bounds = frame.Inner ?? frame.Rect,
			Anchor = UiAnchor.Center
		} );
		// [BIN:TPI-EXE:0x526960 letter buttons] a click on any control but help destroys the window and runs the letter's action; go-to (0xA808) is enabled only
		// when the record's +0xE10 is set, which 0x5CD3A0 clears for every notification letter (0x526D6E)
		Button( "close", 0xA804, Dismiss, enabled: true );
		var okay = Button( "ok", 0xA805, Dismiss, enabled: true );
		Button( "help", 0xA807, null, enabled: true );
		Button( "goto", 0xA808, Dismiss, enabled: false );
		screen.Focus( okay );
		return screen;

		UiButton Button( string id, uint control, Action? clicked, bool enabled )
		{
			var node = table.Control( control );
			return screen.Add( new UiButton
			{
				Id = id,
				Model = node.Model,
				Help = node.Help >= 0 ? strings.Help( node.Help ) : null,
				Clicked = clicked,
				Enabled = enabled,
				Bounds = node.Rect,
				Anchor = UiAnchor.Center
			} );
		}
	}

	/// <summary>The body's colour: the builder makes no colour call on it (0x527272), so it keeps the text object's own.</summary>
	// [BIN:TPI-EXE:0x5123A3 text default colour] Theme Park Inc's text class constructor 0x512330 sets the colour bytes +0x4C/+0x4D/+0x4E
	// (red, green, blue; the 0x40000 colour message copies a colour there at 0x512C61) to 0xFF; it writes no alpha byte, so the drawn alpha is not read here
	private static readonly RgbaByte BodyColour = new( 0xFF, 0xFF, 0xFF, 0xFF );
}
