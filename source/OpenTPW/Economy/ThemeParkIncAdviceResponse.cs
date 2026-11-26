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

	/// <summary>The row of <paramref name="response"/>, or null for a response OpenTPW cannot play yet.</summary>
	public static ThemeParkIncAdviceResponse? For( int response ) => Array.FindIndex( Known, row => row.Response == response ) is var index and >= 0 ? Known[index] : null;

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
	// {t, 0x97, 0xA6/0xA7, 0, 0, 0, 0, 0xC0, 0xC1, 0}
	private static readonly ThemeParkIncAdviceTag[] Known =
	{
		new( 62, 0x97, 127, 0, 0, 0, 0xC0, 0xF7, 0 ), new( 65, 0x97, 130, 0, 0, 0, 0xC0, 0xF7, 0 ), new( 68, 0x97, 133, 0, 0, 0, 0xC0, 0xF7, 0 ),
		new( 71, 0x97, 156, 0, 0, 0, 0xC0, 0xF7, 0 ), new( 0x5E, 0x97, 0xA6, 0, 0, 0, 0xC0, 0xC1, 0 ), new( 0x5F, 0x97, 0xA7, 0, 0, 0, 0xC0, 0xC1, 0 ),
	};

	/// <summary>The sender row of the advice rows: key 8 of <c>0x9CD6D8</c>, whose name and title are both <c>0xC0</c> and whose picture word is 8.</summary>
	// [BIN:TPI-EXE:0x5CD460 sender picture] the first sender row whose name (+4) or title (+8) is the entry: {8, 0xC0, 0xC0, 8}
	public const int SenderPicture = 8;

	/// <summary>The record of <paramref name="tag"/>, or null for a tag no ported advice broadcasts.</summary>
	public static ThemeParkIncAdviceTag? For( int tag ) => Array.FindIndex( Known, row => row.Tag == tag ) is var index and >= 0 ? Known[index] : null;
}
