namespace OpenTPW;

/// <summary>
/// The "Objective Complete!" letter (<c>0x5CEAE0</c>, docs/reverse/TPI-MISSIONS.md §7): the mission controller posts it on the update after a mission advance, for the mission just
/// completed. Mr Maybury writes it to the player; its body is <c>OBJECTIVE_COMPLETE.str</c> of the completed mission and its go-to button leads to that mission's place.
/// </summary>
// [BIN:TPI-EXE:0x5CEAE0 objective letter] a 0x28-byte object, vtable 0x93DF80: +4 = 4, +0xC = 1 (the addressee is the player), +0x10 = 0, +0x14 = +0x1C = 0x97, +0x18 = 1, the sender row of key 1
// (0x9CD6E8, Mr Maybury; a missing row leaves none) at +8 and the completed index at +0x24
// [BIN:TPI-EXE:0x5CD060 letter acceptance] the tag system opens the letter window at once only for a letter whose +4 is 6, 7 or 8; this one (4) is filed as a row of the tag tube (0x636880) that the
// player opens; the slice has no tag tube, so the controller's letters wait in ParkEconomy.TpiObjectiveLetters
internal static class ThemeParkIncObjectiveLetter
{
	/// <summary><c>TAG_SYSTEM.str</c> entry 168, "Objective Complete!".</summary>
	// [BIN:TPI-EXE:0x5CEBB0 objective subject] TAG_SYSTEM 0xA8
	public const int SubjectEntry = 168;

	/// <summary>Go-to kinds of a letter record (<c>+0xE10</c>, dispatched by <c>0x5511B0</c>).</summary>
	public const int GotoThingOfType = 2, GotoChallenges = 0xB, GotoMissionWindow = 0xC, GotoZoneKind = 0x12;

	/// <summary>The go-to target by completed mission (<c>0x5CEC30</c>, jump table <c>0x5CED9C</c>; index 17 or more takes the mission window).</summary>
	// [BIN:TPI-EXE:0x5CEC30 objective go-to] record +0 = 0x8A, +4 = 0x97, +0xE04 = 0, +0xE14 = 0, then by +0x24: 0 (2, 0x6A7), 1 (0xC), 2 (0x12, 2), 3 (0x12, 1), 4 (0x12, 0), 5 (2, 0xE75), 6 (0xB),
	// 7 (0x12, 1), 8 (2, 0x6A6), 9 (0xB), 10 (0x12, 2), 11 (2, 0xA8F), 12 (2, 0xA8E), 13 (0xB), 14 (0x12, 0), 15 (2, 0xE77), 16 (0xB), above 16 (0xC); kinds 0xB and 0xC leave +0xE18 as it was
	// (the record's argument is not written; 0 here)
	public static (int Kind, int Argument) Goto( int completed ) => completed switch
	{
		0 => (GotoThingOfType, 0x6A7),
		1 => (GotoMissionWindow, 0),
		2 => (GotoZoneKind, 2),
		3 => (GotoZoneKind, 1),
		4 => (GotoZoneKind, 0),
		5 => (GotoThingOfType, 0xE75),
		6 => (GotoChallenges, 0),
		7 => (GotoZoneKind, 1),
		8 => (GotoThingOfType, 0x6A6),
		9 => (GotoChallenges, 0),
		10 => (GotoZoneKind, 2),
		11 => (GotoThingOfType, 0xA8F),
		12 => (GotoThingOfType, 0xA8E),
		13 => (GotoChallenges, 0),
		14 => (GotoZoneKind, 0),
		15 => (GotoThingOfType, 0xE77),
		16 => (GotoChallenges, 0),
		_ => (GotoMissionWindow, 0)
	};

	/// <summary>The letter of completed mission <paramref name="completed"/> (the counter less one when it was posted).</summary>
	// [BIN:TPI-EXE:0x636191 letter record] the tag system fills the record through the letter's virtual functions: the subject (+0, 0x5CEBB0), the sender (+4, 0x5CD0D0: TAG_SYSTEM[row +4]), the
	// addressee (+8, 0x5CD110: +0xC = 1 is the player's name), the body (+0xC, 0x5CEBF0: OBJECTIVE_COMPLETE[+0x24]), the signature (+0x14, 0x5CD1C0), the go-to (+0x10, 0x5CEC30), the picture
	// (0x5CD440: the row's +0xC) and +0x1C (0x5CD410: record +0xE0E = +0x10's word)
	internal static ThemeParkIncLetterWindow.Letter Compose( int completed, IThemeParkIncTexts texts, string addressee, string date )
	{
		var from = texts.Format( ThemeParkIncTable.TagSystem, ThemeParkIncMissionWindow.SenderName );
		// [BIN:TPI-EXE:0x5CD1C0 letter signature] a newline, the sender's name, a newline and the sender's title after the body
		var body = texts.Format( ThemeParkIncTable.ObjectiveComplete, completed ) + "\n" + from + "\n" + texts.Format( ThemeParkIncTable.TagSystem, ThemeParkIncMissionWindow.SenderTitle );
		var (kind, argument) = Goto( completed );
		// [BIN:TPI-EXE:0x5271ED letter picture] the mugshot shows frame (record +0xE02) - 1
		return new ThemeParkIncLetterWindow.Letter( texts.Format( ThemeParkIncTable.TagSystem, SubjectEntry ), from, addressee, date, body, ThemeParkIncMissionWindow.SenderMugshot - 1, kind, argument );
	}
}
