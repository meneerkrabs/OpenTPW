namespace OpenTPW.Online.Chat;

/// <summary>
/// The original chat commands, in <c>CHAT_COMMANDS.str</c> order. [DATA:Language/*/CHAT_COMMANDS.str:0-42]
/// holds the localized command words (German "sage", "erzähle", ...) and [DATA:CHAT_COMMANDS.str:43-85]
/// the same commands in English in every language, so both spellings are accepted.
/// </summary>
public enum ChatCommand
{
	Say, Tell, Reply, Last, Shout, Emote, ETell, EShout, WShout, UShout, Hearing, Help, Earmuffs, Ignore, Mark,
	Friend, Buddy, Who, Page, Locate, Afk, Goto, Vote, Filter, Cheer, Think, Hungry, Thirsty, HungryThirsty,
	NeedToGo, ThumbsUp, ThumbsDown, Messy, VeryHappy, Happy, Ok, Unhappy, Bored, Angry, Sick, Scared,
	QueueTooLong, Confused,
}

/// <summary>
/// Responses as indices into <c>CHAT_COMMANDS.str</c> ([DATA:Language/English/CHAT_COMMANDS.str:86-175]).
/// The server sends the index plus arguments; each client shows its own language's string.
/// Several entries are a prefix followed by a player name ("You told ", "There is no player called ").
/// </summary>
public enum ChatNotice
{
	LastToldTo = 86,
	LastToldBy = 87,
	YouTold = 90,
	YouEmotedTo = 91,
	NoSuchPlayer = 94,
	CannotShoutWithEarmuffs = 95,
	EarmuffsOn = 96,
	EarmuffsOff = 97,
	YouIgnore = 98,
	YouNoLongerIgnore = 99,
	WhoToIgnore = 101,
	// [APPROX:ONLINE-003] 102/110 are both "Could not add "; assigned to ignore/buddy by their position next to
	// the ignore (98-105) and buddy (106-113) blocks — evidence needed: the original response code table.
	CouldNotAddIgnore = 102,
	YouBlackmark = 103,
	Muted = 104,
	WhoToBlackmark = 105,
	YouMakeBuddy = 106,
	CouldNotAddBuddy = 110,
	// [APPROX:ONLINE-004] 111-113 are all "Your buddy " (English suffix strings are empty); online/offline/removed
	// meanings are assumed — evidence needed: localized variants or the original response code table.
	YourBuddyOnline = 111,
	YourBuddyOffline = 112,
	YourBuddyRemoved = 113,
	NeedName = 114,
	YouPage = 115,
	HearingRangeSet = 117,
	Commands = 120,
	AfkOn = 121,
	AfkOff = 122,
	AfkAnnounceOn = 123,
	AfkAnnounceOff = 124,
	YouGoTo = 126,
	LeaveParkToGoTo = 127,
	VotedForPark = 128,
	VoteFailed = 129,
	VoteAuthorisationFailed = 130,
	VoteLimitReached = 131,
	FilterOn = 132,
	FilterOff = 133,
	YouCheer = 134,
	/// <summary>First of the "You appear hungry" … "You appear confused" responses (Hungry … Confused).</summary>
	YouAppearHungry = 135,
	NoSuchCommand = 154,
	CannotBuddySelf = 155,
	CannotGotoSelf = 156,
	GotoFailed = 157,
	WelcomeThemeParkWorld = 158,
	WelcomeSimThemePark = 159,
	SystemMessage = 160,
	BadParameter = 162,
	NameAlreadyOnline = 165,
	NoPark = 167,
	ParkFull = 168,
	ThePark = 169,
	AuthorisationFailed = 174,
	BlackmarkedOut = 175,
}

public static class ChatCommands
{
	/// <summary>[DATA:Language/English/CHAT_COMMANDS.str:43-85] Canonical (English) command words.</summary>
	public static readonly IReadOnlyList<string> CanonicalNames = new[]
	{
		"say", "tell", "reply", "last", "shout", "emote", "etell", "eshout", "wshout", "ushout", "hearing", "help",
		"earmuffs", "ignore", "mark", "friend", "buddy", "who", "page", "locate", "afk", "goto", "vote", "filter",
		"cheer", "think", "hungry", "thirsty", "hungry thirsty", "need to go", "thumbs up", "thumbs down", "messy",
		"very happy", "happy", "ok", "unhappy", "bored", "angry", "sick", "scared", "queue too long", "confused",
	};

	public const int CommandCount = 43;
	public const int LocalizedNamesOffset = 0;
	public const int CanonicalNamesOffset = 43;
	// [EXT:ONLINE-030] OpenTPW chat line limit; the original limit is unknown.
	public const int MaximumLineLength = 256;

	public static string CanonicalName( ChatCommand command ) => CanonicalNames[(int)command];

	public static bool TryParseCanonical( string name, out ChatCommand command )
	{
		var index = CanonicalNames.ToList().IndexOf( name );
		command = (ChatCommand)Math.Max( index, 0 );
		return index >= 0;
	}

	/// <summary>Commands whose first argument is a player name.</summary>
	public static bool TakesPlayerName( ChatCommand command ) => command is ChatCommand.Tell or ChatCommand.ETell or ChatCommand.Ignore
		or ChatCommand.Mark or ChatCommand.Friend or ChatCommand.Buddy or ChatCommand.Page or ChatCommand.Locate or ChatCommand.Goto;

	/// <summary>Mood emotes (cheer … confused) that the original shows as "You cheer"/"You appear …".</summary>
	public static bool IsMoodEmote( ChatCommand command ) => command >= ChatCommand.Cheer;

	/// <summary>
	/// "You cheer" for Cheer, nothing for Think, "You appear hungry" … "You appear confused" for the rest
	/// ([DATA:CHAT_COMMANDS.str:134-151] — 18 responses for the 19 mood commands; Think has none).
	/// </summary>
	public static ChatNotice? MoodNotice( ChatCommand command ) => command switch
	{
		ChatCommand.Cheer => ChatNotice.YouCheer,
		>= ChatCommand.Hungry => (ChatNotice)((int)ChatNotice.YouAppearHungry + (command - ChatCommand.Hungry)),
		_ => null,
	};

	/// <summary>Splits "name rest" or "\"two words\" rest" into a player name and the remaining text.</summary>
	public static (string? Name, string Remainder) SplitName( string argument )
	{
		var text = argument.TrimStart();
		if ( text.Length == 0 )
			return (null, "");
		if ( text[0] == '"' )
		{
			var close = text.IndexOf( '"', 1 );
			if ( close < 0 )
				return (null, text);
			return (text[1..close], text[(close + 1)..].TrimStart());
		}
		var space = text.IndexOf( ' ' );
		return space < 0 ? (text, "") : (text[..space], text[(space + 1)..].TrimStart());
	}
}

/// <summary>
/// Maps typed chat lines to commands with one language's command words (localized entries 0-42 plus
/// the English entries 43-85 of that language's <c>CHAT_COMMANDS.str</c>).
/// </summary>
public sealed class ChatCommandTable
{
	private readonly List<(string Word, ChatCommand Command)> words = new();

	public ChatCommandTable( IReadOnlyList<string>? chatCommandsStrings = null )
	{
		for ( var index = 0; index < ChatCommands.CommandCount; index++ )
		{
			Add( ChatCommands.CanonicalNames[index], (ChatCommand)index );
			if ( chatCommandsStrings != null && chatCommandsStrings.Count >= ChatCommands.CanonicalNamesOffset + ChatCommands.CommandCount )
			{
				Add( chatCommandsStrings[ChatCommands.LocalizedNamesOffset + index], (ChatCommand)index );
				Add( chatCommandsStrings[ChatCommands.CanonicalNamesOffset + index], (ChatCommand)index );
			}
		}
		// Longest words first so "hungry thirsty" wins over "hungry".
		words.Sort( ( a, b ) => b.Word.Length.CompareTo( a.Word.Length ) );
	}

	private void Add( string word, ChatCommand command )
	{
		word = word.Trim().ToLowerInvariant();
		if ( word.Length > 0 && !words.Any( entry => entry.Word == word ) )
			words.Add( (word, command) );
	}

	public string LocalizedName( ChatCommand command, IReadOnlyList<string>? chatCommandsStrings ) =>
		chatCommandsStrings != null && chatCommandsStrings.Count > (int)command && chatCommandsStrings[(int)command].Length > 0
			? chatCommandsStrings[(int)command]
			: ChatCommands.CanonicalName( command );

	/// <summary>
	/// [APPROX:ONLINE-002] A line starting with '/' is a command, anything else is "say" — evidence
	/// needed: the original chat input syntax is not documented in the data.
	/// Returns false for an unknown command word.
	/// </summary>
	public bool TryParse( string line, out ChatCommand command, out string argument )
	{
		command = ChatCommand.Say;
		argument = line;
		if ( !line.StartsWith( '/' ) )
			return true;
		var body = line[1..];
		var lower = body.ToLowerInvariant();
		foreach ( var (word, value) in words )
		{
			if ( lower.StartsWith( word, StringComparison.Ordinal ) && (lower.Length == word.Length || lower[word.Length] == ' ') )
			{
				command = value;
				argument = body[word.Length..].Trim();
				return true;
			}
		}
		argument = body;
		return false;
	}
}
