namespace OpenTPW;

public sealed partial class ParkEconomy
{
	/// <summary>Theme Park Inc's tag tube, the tag system thing's mail list; null in Theme Park World and in a park without a career.</summary>
	// [BIN:TPI-EXE:0x6483B7 tag system thing] the new-world init builds the tag system (0x638120(0x830), 0x635180) among the first things and keeps its id at park+0x1EAB52
	public ThemeParkIncTagTube? TpiTagTube { get; private set; }

	private void CreateThemeParkIncTagTube()
	{
		if ( TpiCareer != null )
			TpiTagTube = new ThemeParkIncTagTube();
	}

	/// <summary>
	/// Files a letter as the message centre's type-0x19 post does: the tag system builds its row at once (<c>0x436DD0</c> calls the receivers in place, <c>0x637970</c> case 0xC).
	/// </summary>
	// [BIN:TPI-EXE:0x637A1A tag system receiver] the receiver of class 0xC is the tag system, whose handler is the row builder 0x635D80
	private void FileThemeParkIncLetter( ThemeParkIncTagTubeRow row )
	{
		if ( TpiTagTube is not { } tube )
			return;
		tube.Insert( row, unchecked( (uint)Turn ), load: false );
		PostThemeParkIncUnreadAdvice( tube );
	}

	// [BIN:TPI-EXE:0x54F1A0 unread advice] the counter posts 0x198 (threshold 3) or 0x15D (a later threshold) inside the insert, at once: 0x71A930(message, locate 0, 0, variant -1, bypass 0, score
	// -1) -> 0x718820 on the advisor thing 0x64EA00 (descriptor scores 10000, group 0, limit 1)
	private void PostThemeParkIncUnreadAdvice( ThemeParkIncTagTube tube )
	{
		if ( tube.AdviceRequests.Count == 0 )
			return;
		var requests = tube.AdviceRequests.ToArray();
		tube.AdviceRequests.Clear();
		foreach ( var message in requests )
			PostThemeParkIncAdvice( message );
	}

	/// <summary>
	/// A notification letter (the bank's red-month letter, the challenge system's firing letter): the tag system files its row, read, and the client opens the letter window on it
	/// (<see cref="TpiPendingMail"/>).
	/// </summary>
	// [BIN:TPI-EXE:0x636209 notification letter] the kinds 6 to 8 that 0x5CD060 accepts build the letter window (0x526CD0) and set +0xE0C before the row is inserted (0x636142 -> 0x636880)
	// [BIN:TPI-EXE:0x6362AA letter advice] first the row builder posts the letter's advice (0x1A1, 0x1A2 or 0x1A3) to the advisor's queue with its descriptor's score (0x71A930 -> 0x718820): it is
	// spoken by the advisor's playback, which takes a mail record (group 8) before any other
	private void PostThemeParkIncMail( ThemeParkIncAdviceMail mail )
	{
		PostThemeParkIncAdvice( mail.Message );
		TpiPendingMail = mail;
		FileThemeParkIncLetter( ThemeParkIncTagLetter.Notification( mail, Turn ) );
	}

	/// <summary>
	/// An advice's caption: the tag system receives the playback's kind-0x18 broadcast and files a Message Centre row from the tag's record (<c>0x635D80</c>, branch 0x18), unread.
	/// </summary>
	// [BIN:TPI-EXE:0x635A63 tag subscriptions] the tag system's constructor subscribes to kinds 0x18, 0x19 and 0x1B; [BIN:TPI-EXE:0x6362CA advice row] a tag without a record (0x637000) files
	// nothing; the row is filed only when the body formatter (0x6365A0) succeeds with a text that is not empty (0x6363BC), which the plain body of every ported tag is
	internal void FileThemeParkIncAdviceRow( ThemeParkIncAdviceBroadcast broadcast )
	{
		if ( ThemeParkIncTagLetter.Advice( broadcast, Turn ) is { } row )
			FileThemeParkIncLetter( row );
	}

	/// <summary>The tag thing's update, once per world pass (<c>0x635AB0</c>), not in a lost park.</summary>
	// [BIN:TPI-EXE:0x63770E tag system update] the world pass's thing-class dispatch sends class 12 to 0x635AB0
	internal void UpdateThemeParkIncTagTube( long turn ) => TpiTagTube?.Update( unchecked( (uint)turn ) );

	/// <summary>The tube window's delete (<c>0x636A40</c>).</summary>
	internal void DeleteThemeParkIncTubeRow( ThemeParkIncTagTubeRow row ) => TpiTagTube?.Delete( row );

	/// <summary>The tube window's read (<c>0x553480</c>): false when the row was read.</summary>
	internal bool ReadThemeParkIncTubeRow( ThemeParkIncTagTubeRow row ) => TpiTagTube?.MarkRead( row ) ?? false;
}
