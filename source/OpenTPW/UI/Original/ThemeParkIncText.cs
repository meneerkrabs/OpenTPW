namespace OpenTPW.UI.Original;

/// <summary>
/// The Theme Park Inc string-table ids the front end uses. Theme Park Inc's <c>UITEXT.str</c> has 994 records whose ids
/// differ from Theme Park World's (so the <see cref="UIStrings"/> enum does not apply); read them with
/// <see cref="UiStringTable.Entry(int)"/>. <c>UIHELPTEXT.str</c> (367 records) is read with <see cref="UiStringTable.Help(int)"/>
/// and <c>THEMENAMES.str</c> (four records) with <see cref="UiStringTable.Theme(int)"/>.
/// </summary>
public static class ThemeParkIncText
{
	// [BIN:TPI-EXE:0x56adb0 personnel file text] the Personnel File builder asks the text function 0x500f60 for UITEXT 0x13 (the title, control 0xc75c), 0x14 (a free player slot: the handler 0x56a8c3 and the three copies after it), 0x16 to 0x1a (the stock labels of controls 0xc746, 0xc74d, 0xc74f, 0xc751 and 0xc74a), 0x1d (the objective label 0xc744), 0x1e (Quit Game, toggle 0xc75b), 0x1f (Coaster Design Kit, toggle 0xc73a), 0x20 (the name field's prompt, field 0xc758), 0x21 (Delete Player 0xc755), 0x22 (Enter Game 0xc754) and 0x23 (Load A Game 0xc759); the slot handlers 0x569e12 to 0x56a1b6 use 0x1c for a zone that is not yet open
	/// <summary>"TPi Personnel File".</summary>
	public const int PersonnelFileTitle = 19;
	/// <summary>"New Player": the label of a free player slot.</summary>
	public const int NewPlayer = 20;
	/// <summary>"Money :" (the first stock label; not drawn in phase 1, the slice runs no economy).</summary>
	public const int Money = 21;
	/// <summary>"Golden Tickets :".</summary>
	public const int GoldenTickets = 22;
	/// <summary>"Shareholding % :".</summary>
	public const int ShareholdingPercent = 23;
	/// <summary>"Shares Owned :".</summary>
	public const int SharesOwned = 24;
	/// <summary>"Share Price :".</summary>
	public const int SharePrice = 25;
	/// <summary>"Portfolio Value :".</summary>
	public const int PortfolioValue = 26;
	/// <summary>"Zone not yet opened".</summary>
	public const int ZoneNotYetOpened = 28;
	/// <summary>"Current Objective : ".</summary>
	public const int CurrentObjective = 29;
	/// <summary>"Quit Game".</summary>
	public const int QuitGame = 30;
	/// <summary>"Coaster Design Kit".</summary>
	public const int CoasterDesignKit = 31;
	/// <summary>"Enter name here": the prompt of the name field.</summary>
	public const int EnterNameHere = 32;
	/// <summary>"Delete Player".</summary>
	public const int DeletePlayer = 33;
	/// <summary>"Enter Game".</summary>
	public const int EnterGame = 34;
	/// <summary>"Load A Game".</summary>
	public const int LoadGame = 35;
	// [BIN:TPI-EXE:0x568d86 quit text] the Quit Game handler (0x568d67, a click on control 0xc75b) fetches UITEXT 0x305 and passes it to the confirm dialog builder 0x4f47f0 with the quit action 0x568e60
	/// <summary>"QUIT GAME\n\nAre you sure you want to quit the game and return to Windows?": the quit confirmation.</summary>
	public const int QuitConfirmation = 773;

	// [BIN:TPI-EXE:0x568d40 personnel file help] the table's help ids (0x9c0fbc): 287 on Enter Game and Load A Game, 288 on Delete Player, 289 on Quit Game, 290 on Coaster Design Kit; the confirm table 0x9b18bc has 0 (Ok) and 3 (Cancel) on its two buttons
	public const int HelpOk = 0, HelpCancel = 3;

	/// <summary>The THEMENAMES.str record naming a zone, or -1 when the file has none for it.</summary>
	// [DATA:THEMENAMES.str entries 0 to 3] the four records read "Land of Invention", "Polar Zone", "Intro World" and "Arabian Nights" in the English table ("Land van Ontdekkingen", "Poolzone", "Intro World", "1001 Nacht" in Dutch): the science, water, intro-world and arabian zones; the exe's world name table (0x9d53b4) is in a different order (arabian, science, water, intro), so the pairing is read off the names
	public static int ThemeNameIndex( string world ) => world.ToLowerInvariant() switch
	{
		"science" => 0,
		"water" => 1,
		"arabian" => 3,
		_ => -1,
	};
}
