namespace OpenTPW;

/// <summary>
/// The row the tag system builds from each ported letter (<c>0x635D80</c>, branch 0x19): the letter's virtual functions fill the record (the row fill at slot 4 writes the tag, the kind word,
/// the expiry and the go-to; slot 7 the unread category) and the builder dates it.
/// </summary>
public static class ThemeParkIncTagLetter
{
	/// <summary>The tag of the zone arrival's briefing (<c>0x5CD6E0(0x5C, 5, 1)</c>).</summary>
	public const int IntroductionTag = 0x5C;

	/// <summary>The notification ids the bank (8) and the challenge system (7) post (<c>0x5CEE50</c>).</summary>
	public const int RedSixMonthsId = 8, ChallengeLostId = 7;

	/// <summary>The row fills' tag for the objective and notification letters before the kind's own write (<c>0x5CD3A0</c>, <c>0x5CEC30</c>).</summary>
	public const int PlainTag = 0x8A;

	/// <summary>The null tag (<c>0x97</c>): the kind word every ported row fill writes.</summary>
	public const int NullTag = 0x97;

	/// <summary>The sender row of the objective letter and the briefing: key 1 of <c>0x9CD6D8</c> (Mr Maybury), whose picture word is 1.</summary>
	public const int MayburyPicture = 1;

	/// <summary>The packed date of <paramref name="turn"/> (<c>0x518BC0</c>): <c>year &lt;&lt; 16 | (month - 1) &lt;&lt; 8 | day</c>.</summary>
	// [BIN:TPI-EXE:0x635E05 row date] 0x6089C0 on the clock (world +0x428) gives the year (0xAA0ADC), the month (0xAA0AD8) and the day (0xAA0AD4); the month is decremented in place and
	// 0x518BC0(day, month, year) returns (year << 8 + month) << 8 + day into +0xE24
	public static int PackedDate( long turn )
	{
		var date = ParkCalendar.DateAtTurn( turn );
		return (date.Year << 16) | ((date.Month - 1) << 8) | date.Day;
	}

	/// <summary>The row of the "Objective Complete!" letter of completed mission <paramref name="completed"/>, unread.</summary>
	// [BIN:TPI-EXE:0x5CEC30 objective row fill] +0 = 0x8A, +4 = 0x97, +0xE04 = 0, then by the letter's +0x24 the go-to kind +0xE10 and argument +0xE18 (kinds 0xB and 0xC leave +0xE18 at the
	// builder's 0); +0xE14 stays 0; the letter's +0x10 is 0 (0x5CEAE0), so the category (0x5CD410) is 0; the picture (0x5CD440) is the sender row key 1's word, 1
	public static ThemeParkIncTagTubeRow Objective( int completed, long turn )
	{
		var (kind, argument) = ThemeParkIncObjectiveLetterGoto.Of( completed );
		return new ThemeParkIncTagTubeRow
		{
			Tag = PlainTag,
			Kind = NullTag,
			Source = ThemeParkIncLetterSource.Objective,
			Argument = completed,
			Picture = MayburyPicture,
			GotoKind = kind,
			GotoArgument = argument,
			Date = PackedDate( turn )
		};
	}

	/// <summary>The row of the zone arrival's first briefing, the tag letter of tag 0x5C, unread.</summary>
	// [BIN:TPI-EXE:0x5CD840 tag row fill] +0 = the letter's tag (+0x1C, 0x5C), +4 = the tag record's +4 (0x97), +0xE04 = its +0x14 word (0), +0xE10 = its +0xC (0), +0xE14 = 0; the tag record
	// 0x9D3480 + 0x5C * 40 = {0x5C, 0x97, body 0xA2, 0, 0, 0, 0, sender 0xAE, subject 0xA1, 0}
	// [BIN:TPI-EXE:0x5CCFF0 tag sender] the builder looks the record's +0x1C (0xAE) up among the sender rows' name and title entries: row key 1's title, so the sender row is key 1 and the picture 1
	public static ThemeParkIncTagTubeRow Introduction( long turn ) => new()
	{
		Tag = IntroductionTag,
		Kind = NullTag,
		Source = ThemeParkIncLetterSource.Introduction,
		Argument = IntroductionTag,
		Picture = MayburyPicture,
		Date = PackedDate( turn )
	};

	/// <summary>The row of a notification letter (the bank's id 8, the challenge system's id 7): category 2, filed read because the letter window opens on it at once.</summary>
	// [BIN:TPI-EXE:0x5CD3A0 notification row fill] +0 = 0x8A, +4 = 0x97, +0xE04 = 0, +0xE10 = 0, +0xE14 = 0, +0xE18 = 0; the letter's +0x10 is 2 (0x5CEE93), the category; the picture is the
	// sender row key 1's word; [BIN:TPI-EXE:0x63621C open at once] 0x5CD060 accepts kinds 6 to 8: the letter window (0x526CD0), +0xE0C = 1, then id 8 writes +0 = 0x5E and id 7 +0 = 0x5F
	internal static ThemeParkIncTagTubeRow Notification( ThemeParkIncAdviceMail mail, long turn ) => new()
	{
		Tag = mail.Category,
		Kind = NullTag,
		Source = ThemeParkIncLetterSource.Notification,
		Argument = mail.Category == ThemeParkIncAdviceMail.RedSixMonths.Category ? RedSixMonthsId : ChallengeLostId,
		Picture = ThemeParkIncAdviceMail.SenderPicture,
		Read = true,
		Category = 2,
		Date = PackedDate( turn )
	};

	/// <summary>The wage demand's kinds (<c>0x5CEFB0</c>'s first argument): 0xA when its role has a grievance in a park younger than MinMonthsForStrike months (the letter threatens a strike), else 0xB.</summary>
	public const int WageDemandWithStrike = 0xA, WageDemandWithoutStrike = 0xB;

	/// <summary>The wage demand row's tag (<c>0x90</c>), the tag the daily routine and the negotiation delete the member's older rows by (<c>0x636C90(0x90, handle)</c>).</summary>
	public const int WageDemandTag = 0x90;

	/// <summary>The wage demand row's go-to kind (<c>0xE</c>): the go-to opens the wage demand window (<c>0x5511B0</c> → <c>0x59ABB0</c>).</summary>
	public const int WageDemandGoto = 0xE;

	/// <summary>The wage demand's sender row: key 5 of <c>0x9CD6D8</c> (Miss Moody, Human Resources Director), whose picture word is 5.</summary>
	// [BIN:TPI-EXE:0x5CEFDC wage demand sender] the constructor looks for the row whose key is 5 (0x9CD728 first, then the table from 0x9CD6D8 in 16-byte steps) and keeps it at +8: {5, 0xB7, 0xB8, 5}
	public const int WageDemandSenderName = 0xB7, WageDemandSenderTitle = 0xB8, WageDemandPicture = 5;

	/// <summary>
	/// The row of a wage demand of <paramref name="kind"/> (0xA or 0xB) by the member whose thing is <paramref name="thing"/> for <paramref name="demand"/> a month: tag 0x90, the go-to 0xE with the
	/// demand, the member's thing as the key, category 2 and the sender's picture 5, unread and never expiring.
	/// </summary>
	// [BIN:TPI-EXE:0x5CF3F0 wage demand row fill] +0xE14 = the member's handle (0x6375C0 on +0x24); kinds 0xA and 0xB: +0xE10 = 0xE, +0 = 0x90, +4 = 0x97, the letter's +0x10 = 2, +0xE18 = the demand (+0x28);
	// +0xE04 stays the builder's 0 (0x635DB2), so the row never expires; the category is the letter's +0x10 (0x5CD410), the picture the sender row's word +0xC (0x5CD440); 0x5CD060 refuses kinds 0xA and
	// 0xB, so no letter window opens and the row is filed unread (0x636142)
	public static ThemeParkIncTagTubeRow WageDemand( int kind, int thing, int demand, long turn ) => new()
	{
		Tag = WageDemandTag,
		Kind = NullTag,
		Source = ThemeParkIncLetterSource.WageDemand,
		Argument = kind,
		Picture = WageDemandPicture,
		Category = 2,
		GotoKind = WageDemandGoto,
		Key = thing,
		GotoArgument = demand,
		Date = PackedDate( turn )
	};

	/// <summary>The advice mail of a notification row's id.</summary>
	internal static ThemeParkIncAdviceMail NotificationMail( int id ) => id == RedSixMonthsId ? ThemeParkIncAdviceMail.RedSixMonths : ThemeParkIncAdviceMail.ChallengeLost;
}

/// <summary>The objective letter's go-to target by completed mission (<c>0x5CEC30</c>, jump table <c>0x5CED9C</c>).</summary>
public static class ThemeParkIncObjectiveLetterGoto
{
	/// <summary>Go-to kinds of a letter record (<c>+0xE10</c>, dispatched by <c>0x5511B0</c>).</summary>
	public const int ThingOfType = 2, Challenges = 0xB, MissionWindow = 0xC, ZoneKind = 0x12;

	// [BIN:TPI-EXE:0x5CEC30 objective go-to] by +0x24: 0 (2, 0x6A7), 1 (0xC), 2 (0x12, 2), 3 (0x12, 1), 4 (0x12, 0), 5 (2, 0xE75), 6 (0xB), 7 (0x12, 1), 8 (2, 0x6A6), 9 (0xB), 10 (0x12, 2),
	// 11 (2, 0xA8F), 12 (2, 0xA8E), 13 (0xB), 14 (0x12, 0), 15 (2, 0xE77), 16 (0xB), above 16 (0xC); kinds 0xB and 0xC leave +0xE18 as it was (0 here)
	public static (int Kind, int Argument) Of( int completed ) => completed switch
	{
		0 => (ThingOfType, 0x6A7),
		1 => (MissionWindow, 0),
		2 => (ZoneKind, 2),
		3 => (ZoneKind, 1),
		4 => (ZoneKind, 0),
		5 => (ThingOfType, 0xE75),
		6 => (Challenges, 0),
		7 => (ZoneKind, 1),
		8 => (ThingOfType, 0x6A6),
		9 => (Challenges, 0),
		10 => (ZoneKind, 2),
		11 => (ThingOfType, 0xA8F),
		12 => (ThingOfType, 0xA8E),
		13 => (Challenges, 0),
		14 => (ZoneKind, 0),
		15 => (ThingOfType, 0xE77),
		16 => (Challenges, 0),
		_ => (MissionWindow, 0)
	};
}
