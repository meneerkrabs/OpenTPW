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
	private void FileThemeParkIncLetter( ThemeParkIncTagTubeRow row ) => TpiTagTube?.Insert( row, unchecked( (uint)Turn ), load: false );

	/// <summary>
	/// A notification letter (the bank's red-month letter, the challenge system's firing letter): the tag system files its row, read, and the client opens the letter window on it
	/// (<see cref="TpiPendingMail"/>).
	/// </summary>
	// [BIN:TPI-EXE:0x636209 notification letter] the kinds 6 to 8 that 0x5CD060 accepts build the letter window (0x526CD0) and set +0xE0C before the row is inserted (0x636142 -> 0x636880)
	private void PostThemeParkIncMail( ThemeParkIncAdviceMail mail )
	{
		TpiPendingMail = mail;
		FileThemeParkIncLetter( ThemeParkIncTagLetter.Notification( mail, Turn ) );
	}

	/// <summary>The tag thing's update, once per world pass (<c>0x635AB0</c>), not in a lost park.</summary>
	// [BIN:TPI-EXE:0x63770E tag system update] the world pass's thing-class dispatch sends class 12 to 0x635AB0
	internal void UpdateThemeParkIncTagTube( long turn ) => TpiTagTube?.Update( unchecked( (uint)turn ) );

	/// <summary>The tube's unread-mail advisor messages since the last call, oldest first (<see cref="ThemeParkIncTagTube.FirstUnreadAdvice"/>, <see cref="ThemeParkIncTagTube.UnreadAdvice"/>).</summary>
	internal IReadOnlyList<int> TakeThemeParkIncTubeAdvice()
	{
		if ( TpiTagTube is not { AdviceRequests.Count: > 0 } tube )
			return Array.Empty<int>();
		var taken = tube.AdviceRequests.ToArray();
		tube.AdviceRequests.Clear();
		return taken;
	}

	/// <summary>The tube window's delete (<c>0x636A40</c>).</summary>
	internal void DeleteThemeParkIncTubeRow( ThemeParkIncTagTubeRow row ) => TpiTagTube?.Delete( row );

	/// <summary>The tube window's read (<c>0x553480</c>): false when the row was read.</summary>
	internal bool ReadThemeParkIncTubeRow( ThemeParkIncTagTubeRow row ) => TpiTagTube?.MarkRead( row ) ?? false;
}
