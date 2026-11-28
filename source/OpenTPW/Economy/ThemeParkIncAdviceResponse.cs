namespace OpenTPW;

/// <summary>
/// An advisor response the queue can play so far: a row of the response table <c>0xBEF1F0</c> (32 bytes: the id, the voice <c>+4</c>, the LIP <c>+8</c>, the animation <c>+0xC</c>,
/// the model and bank word <c>+0x10</c>, the accessory words <c>+0x14</c> and <c>+0x18</c>, the tag <c>+0x1C</c>). The table holds 993 responses; these are the rows of the messages posted today,
/// the four forecasts and the three notification letters.
/// </summary>
/// <param name="Voice">The voice's event in the global <c>cat_speech</c> catalogue (<c>+4</c>, played by <c>0x716230</c>).</param>
/// <param name="Lip">The LIP number (<c>+8</c>): <c>Speech\lips\sp_%04d.lip</c>.</param>
/// <param name="Animation">-1 for a generated sequence (<c>0x715AB0</c>), else the canned sequence's row plus 16 (<c>0x715BC0</c>).</param>
/// <param name="Model">The low word of <c>+0x10</c>: the advisor model that shows the clips (0 the global advisor, 1 the level's).</param>
/// <param name="LevelBank">The high word of <c>+0x10</c>: the voice and LIP come from the level's speech instead of the global one.</param>
/// <param name="Accessory">The word <c>+0x14</c>: 0x29A hides the head; else the accessory the head shows (<c>0x715CF0</c>).</param>
/// <param name="SecondAccessory">The word <c>+0x18</c>: a second accessory the head shows when it is not 0 (<c>0x715F9C</c>); 0 in every row OpenTPW plays.</param>
// [BIN:TPI-EXE:0x70F59E forecast response rows] [BIN:TPI-EXE:0x7104A0 notification response rows] rows of 0x715570 (tools/ppc-analysis/oracle_x86/tpi_advisor_forecast_oracle.py):
// {response, +4, +8, +0xC, +0x10, +0x14, +0x18, +0x1C}
public readonly record struct ThemeParkIncAdviceResponse( int Response, uint Voice, int Lip, int Animation, int Model, bool LevelBank, int Accessory, int Tag, int SecondAccessory = 0 )
{
	/// <summary>The tag of a response that broadcasts no caption (<c>0x97</c>).</summary>
	public const int NoTag = 0x97;

	/// <summary>The accessory word that hides the advisor's head while it speaks (<c>0x7163CC</c>).</summary>
	// [BIN:TPI-EXE:0x7163CC hidden head] the head is hidden when the animation word is set (0x5D0820), the accessory word is 0x29A, the presenter shows the speech (0x54D4D0) or the "game" key
	// group (0xA07A28 +6) is off
	public const int HiddenHead = 0x29A;

	/// <summary>The animation of the speech that shows the head without its parts (<c>0xBF7241</c>).</summary>
	public const int SuppressedHead = 0x11;

	private static readonly ThemeParkIncAdviceResponse[] Known =
	{
		new( 642, 763, 763, 25, 1, false, -1, 62 ), new( 645, 766, 766, 25, 1, false, -1, 65 ), new( 648, 769, 769, 25, 1, false, -1, 68 ),
		new( 649, 938, 769, 25, 1, false, -1, 71 ),
		new( 748, 868, 868, -1, 0, false, HiddenHead, NoTag ), new( 749, 869, 869, 25, 1, false, -1, 0x5E ), new( 750, 870, 870, 25, 1, false, -1, 0x5F ),
	};

	/// <summary>The rows OpenTPW plays.</summary>
	public static IReadOnlyList<ThemeParkIncAdviceResponse> All { get; } = Array.AsReadOnly( Known );

	/// <summary>The row of <paramref name="response"/> in the shipped response table (<see cref="ThemeParkIncAdviceResponseTable"/>), or null for none; the rows above when the table is missing.</summary>
	public static ThemeParkIncAdviceResponse? For( int response ) => ThemeParkIncAdviceResponseTable.Shipped.Responses.Count > 0
		? ThemeParkIncAdviceResponseTable.Shipped.Find( response )
		: Array.FindIndex( Known, row => row.Response == response ) is var index and >= 0 ? Known[index] : null;

	/// <summary>The forecast message the weather posts for a spell kind (1 heatwave, 2 rain, 3 storm, 4 snow), 0 for none.</summary>
	// [BIN:TPI-EXE:0x6420AF forecast dispatch] the newly rolled spell's kind picks 0x154, 0x157, 0x15B or 0x15A
	public static int ForecastMessage( int kind ) => kind switch { 1 => 0x154, 2 => 0x157, 3 => 0x15B, 4 => 0x15A, _ => 0 };

	/// <summary>Whether the response is one of the four forecasts.</summary>
	public bool Forecast => Response is 642 or 645 or 648 or 649;

	/// <summary>The speech group of the presenter's forced ranges (<c>0x54D3E0</c>: 0 to 5 for speeches <c>0x3E6</c>–<c>0x454</c>), -1 for any other voice.</summary>
	// [BIN:TPI-EXE:0x54D3E0 presenter speech group] 0x3E6-0x3FD: 0, 0x3FE-0x411: 1, 0x412-0x429: 3, 0x42A-0x438: 5, 0x439-0x449: 2, 0x44A-0x454: 4; else -1
	public static int PresenterGroup( uint voice ) => voice switch
	{
		>= 0x3E6 and <= 0x3FD => 0,
		>= 0x3FE and <= 0x411 => 1,
		>= 0x412 and <= 0x429 => 3,
		>= 0x42A and <= 0x438 => 5,
		>= 0x439 and <= 0x449 => 2,
		>= 0x44A and <= 0x454 => 4,
		_ => -1
	};
}

/// <summary>
/// A tag record of the tag system (<c>0x9D3480</c>, 40 bytes per tag): the kind word <c>+4</c>, the body entry <c>+8</c>, the go-to kind <c>+0xC</c>, the expiry <c>+0x14</c>, the body
/// formatter <c>+0x18</c>, the sender <c>+0x1C</c> and subject <c>+0x20</c> entries of <c>TAG_SYSTEM.str</c> and the unread category <c>+0x24</c>. These are the tags the played advice
/// broadcasts so far.
/// </summary>
// [BIN:TPI-EXE:0x637000 tag record] the record of tag t is 0x9D3480 + 40·t when its +0 is t (a tag of 0x96 or more, and 0x97, have none)
public readonly record struct ThemeParkIncAdviceTag( int Tag, int Kind, int Body, int GotoKind, int ExpiryUnits, int Formatter, int Sender, int Subject, int Category )
{
	// [BIN:TPI-EXE:0x9D3E30 forecast tag records] tags 62, 65, 68, 71: {t, 0x97, 0x7F/0x82/0x85/0x9C, 0, 0, 0, 0, 0xC0, 0xF7, 0}; [BIN:TPI-EXE:0x9D4330 letter tag records] tags 0x5E, 0x5F:
	// {t, 0x97, 0xA6/0xA7, 0, 0, 0, 0, 0xC0, 0xC1, 0}; [BIN:TPI-EXE:0x9D4380 obstacle panel tag records] tags 0x60..0x63 (responses 823..826, the researched-obstacle advice 0x1EB..0x1EE):
	// {t, t, 0x9D..0xA0, 0, 0, 0, 0, 0xC0, 0xFA, 0}: their kind word is the tag itself, so a new row supersedes the older row of the same tag
	// [BIN:TPI-EXE:0x9D3480 P5 tag records] the captions of the P5 senders: tags 0..5 (the strikes' warnings 0x22..0x27) {t, t, t, 0, 0, 0, 0, 0xC0, 0xC5 + t, 0}; tags 6, 8, 9 (the ride advice 0x40,
	// 0x41, 0x42) {t, 6, 0x12/0x13/0x14, 0x13, 0, 0, 2, 0xC0, 0xD7/0xD8/0xD8, 2}; tags 14..19 (the research advice 0x59, 0x58, 0x5A, 0x5B, 0x5C, 0x11B) {t, t, 0x1A/0x19/0x1B/0x1C/0x1D/0x71,
	// 6/6/7/8/9/0xA, 0, 0, 3, 0xC0, 0xDB/0xDA/0xDC/0xDD/0xDE/0xED, 0}; tags 32..43 (the strikes' 0x1D1..0x1D6 and 0x1DD..0x1E2) {t, t, t - 26, 0, 0, 0, 0, 0xC0, 0xCB + t - 32, 0}; tag 105 (the
	// condemned ride 0xDB) {0x69, 0x69, 0x68, 1, 0, 0, 2, 0xC0, 0xE9, 2}. Word +0x10 is 0 in every one
	private static readonly ThemeParkIncAdviceTag[] Known =
	{
		new( 62, 0x97, 127, 0, 0, 0, 0xC0, 0xF7, 0 ), new( 65, 0x97, 130, 0, 0, 0, 0xC0, 0xF7, 0 ), new( 68, 0x97, 133, 0, 0, 0, 0xC0, 0xF7, 0 ),
		new( 71, 0x97, 156, 0, 0, 0, 0xC0, 0xF7, 0 ), new( 0x5E, 0x97, 0xA6, 0, 0, 0, 0xC0, 0xC1, 0 ), new( 0x5F, 0x97, 0xA7, 0, 0, 0, 0xC0, 0xC1, 0 ),
		new( 0x60, 0x60, 0x9D, 0, 0, 0, 0xC0, 0xFA, 0 ), new( 0x61, 0x61, 0x9E, 0, 0, 0, 0xC0, 0xFA, 0 ), new( 0x62, 0x62, 0x9F, 0, 0, 0, 0xC0, 0xFA, 0 ),
		new( 0x63, 0x63, 0xA0, 0, 0, 0, 0xC0, 0xFA, 0 ),
		new( 0, 0, 0, 0, 0, 0, 0xC0, 0xC5, 0 ), new( 1, 1, 1, 0, 0, 0, 0xC0, 0xC6, 0 ), new( 2, 2, 2, 0, 0, 0, 0xC0, 0xC7, 0 ), new( 3, 3, 3, 0, 0, 0, 0xC0, 0xC8, 0 ),
		new( 4, 4, 4, 0, 0, 0, 0xC0, 0xC9, 0 ), new( 5, 5, 5, 0, 0, 0, 0xC0, 0xCA, 0 ),
		new( 6, 6, 0x12, 0x13, 0, 2, 0xC0, 0xD7, 2 ), new( 8, 6, 0x13, 0x13, 0, 2, 0xC0, 0xD8, 2 ), new( 9, 6, 0x14, 0x13, 0, 2, 0xC0, 0xD8, 2 ),
		new( 0xE, 0xE, 0x1A, 6, 0, 3, 0xC0, 0xDB, 0 ), new( 0xF, 0xF, 0x19, 6, 0, 3, 0xC0, 0xDA, 0 ), new( 0x10, 0x10, 0x1B, 7, 0, 3, 0xC0, 0xDC, 0 ),
		new( 0x11, 0x11, 0x1C, 8, 0, 3, 0xC0, 0xDD, 0 ), new( 0x12, 0x12, 0x1D, 9, 0, 3, 0xC0, 0xDE, 0 ), new( 0x13, 0x13, 0x71, 0xA, 0, 3, 0xC0, 0xED, 0 ),
		new( 0x20, 0x20, 6, 0, 0, 0, 0xC0, 0xCB, 0 ), new( 0x21, 0x21, 7, 0, 0, 0, 0xC0, 0xCC, 0 ), new( 0x22, 0x22, 8, 0, 0, 0, 0xC0, 0xCD, 0 ),
		new( 0x23, 0x23, 9, 0, 0, 0, 0xC0, 0xCE, 0 ), new( 0x24, 0x24, 0xA, 0, 0, 0, 0xC0, 0xCF, 0 ), new( 0x25, 0x25, 0xB, 0, 0, 0, 0xC0, 0xD0, 0 ),
		new( 0x26, 0x26, 0xC, 0, 0, 0, 0xC0, 0xD1, 0 ), new( 0x27, 0x27, 0xD, 0, 0, 0, 0xC0, 0xD2, 0 ), new( 0x28, 0x28, 0xE, 0, 0, 0, 0xC0, 0xD3, 0 ),
		new( 0x29, 0x29, 0xF, 0, 0, 0, 0xC0, 0xD4, 0 ), new( 0x2A, 0x2A, 0x10, 0, 0, 0, 0xC0, 0xD5, 0 ), new( 0x2B, 0x2B, 0x11, 0, 0, 0, 0xC0, 0xD6, 0 ),
		new( 0x69, 0x69, 0x68, 1, 0, 2, 0xC0, 0xE9, 2 ),
	};

	/// <summary>The body formatters (<c>0x6365A0</c>, word <c>+0x18</c>): 0 the plain body, 2 the body with the name of the locate word's thing, 3 the body with the name of the locate dword's type.</summary>
	public const int PlainBody = 0, ThingNameBody = 2, TypeNameBody = 3;

	/// <summary>
	/// Whether the body formatter files a row for a caption with these locate words (<c>0x6365A0</c> answering true): the plain body always, the thing's name with a thing, the type's name with a
	/// type; any other formatter never.
	/// </summary>
	// [BIN:TPI-EXE:0x6365A0 body formatter] by +0x18 (jump table 0x6366C4): 0 formats TAG_SYSTEM[+8] (0x6365C6); 2 needs the locate word (its second argument) nonzero and formats with the thing's name
	// (0x5E7C20 on the thing table's entry, a string argument 'MRAV' at index 0; 0x6365EC); 3 needs the low word of the locate dword (its third) nonzero and formats with the type's name (0x43C810,
	// 0x63665F); 1 and 4..9 answer false. The row is then filed when the text is not empty (0x6363BC), which every body entry's template makes it
	public bool Files( ushort locate, int locateData ) => Formatter switch
	{
		PlainBody => true,
		ThingNameBody => locate != 0,
		TypeNameBody => unchecked( (ushort)locateData ) != 0,
		_ => false
	};

	/// <summary>The sender row of the advice rows: key 8 of <c>0x9CD6D8</c>, whose name and title are both <c>0xC0</c> and whose picture word is 8.</summary>
	// [BIN:TPI-EXE:0x5CD460 sender picture] the first sender row whose name (+4) or title (+8) is the entry: {8, 0xC0, 0xC0, 8}
	public const int SenderPicture = 8;

	/// <summary>The record of <paramref name="tag"/>, or null for a tag no ported advice broadcasts.</summary>
	public static ThemeParkIncAdviceTag? For( int tag ) => Array.FindIndex( Known, row => row.Tag == tag ) is var index and >= 0 ? Known[index] : null;
}
