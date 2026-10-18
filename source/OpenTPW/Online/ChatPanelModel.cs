using OpenTPW.Online.Chat;

namespace OpenTPW;

/// <summary>
/// What the original chat overlay (<see cref="UI.ChatPanel"/>) shows and does: the room's players, the buddies and the chat lines, and the
/// commands its buttons and name popups send. The online session is one; a visit reaches it through its presence.
/// </summary>
public interface IChatPanelSource
{
	/// <summary>The logged-in player's name: their own row opens no popup.</summary>
	string? PlayerName { get; }
	/// <summary>The chat connection is open: the entry, the buttons and the popups work.</summary>
	bool ChatOpen { get; }
	/// <summary>The players in the room, from its roster (a last "+N" entry stands for players the roster left out).</summary>
	IReadOnlyList<string> RoomPlayers { get; }
	/// <summary>The buddies of the last buddy-list answer and the changes since, each with whether it was online.</summary>
	IReadOnlyList<(string Name, bool Online)> Buddies { get; }
	/// <summary>The players this connection ignores, from the server's answers.</summary>
	IReadOnlyCollection<string> Ignored { get; }
	/// <summary>Whether the player's earmuffs are on, from the server's last answer.</summary>
	bool Earmuffs { get; }
	/// <summary>The room is a published park's and the server has not taken a vote for it from this session.</summary>
	bool CanVote { get; }
	/// <summary>The chat lines, oldest first.</summary>
	IReadOnlyList<string> ChatLines { get; }
	/// <summary>Says a typed line in the room (or runs the command it starts with).</summary>
	void SendChatLine( string line );
	/// <summary>Sends one chat command, whatever the language's command words.</summary>
	void SendCommand( ChatCommand command, string argument );
	/// <summary>Asks the server for the buddy list (its answers fill <see cref="Buddies"/>).</summary>
	void AskBuddies();
}

/// <summary>The flags of a name in the chat overlay's lists.</summary>
// [BIN:TPW-EXE:0x4EE5C0 row flags] a row's flags: bit 0 the name is a buddy (the buddy map at +0xE94), bit 1 that buddy is online (+0x1C), bit 2 the buddy is in this park (+0x1D), bit 3 the player ignores the name (the ignore map at +0xEA4)
[Flags]
public enum ChatPersonFlags
{
	None = 0,
	Buddy = 1,
	Online = 2,
	InPark = 4,
	Ignored = 8,
}

/// <summary>One row of the chat overlay's people list.</summary>
public readonly record struct ChatPerson( string Name, ChatPersonFlags Flags, bool Own );

/// <summary>The chat overlay's lists: its people rows, their colours, and the message rows.</summary>
public static class ChatPanelModel
{
	/// <summary>The message list keeps at most this many rows (wrapped lines), dropping the oldest.</summary>
	// [BIN:TPW-EXE:0x4BE970 message rows] a chat line is wrapped to the list's width and each piece added as a row; rows past the limit at 0x758F0C (20) are removed from the top, and the list shows its last row
	public const int MessageRows = 20;

	/// <summary>
	/// The rows of the people list: the room's players (the people tab, 0xC3126) or the buddies (the buddy tab, 0xC3127), each with its flags. A last
	/// "+N" entry of a cut-short roster is no player and is left out.
	/// </summary>
	// [BIN:TPW-EXE:0x4EE8D0 people tab] the people tab lists every person of the park (+0xE84) with its flags; [BIN:TPW-EXE:0x4EE9E0 buddy tab] the buddy tab every buddy (+0xE94)
	public static IReadOnlyList<ChatPerson> People( IChatPanelSource source, bool buddies )
	{
		var lookup = new Lookup( source );
		var names = buddies ? source.Buddies.Select( buddy => buddy.Name ) : source.RoomPlayers.Where( name => !name.StartsWith( "+", StringComparison.Ordinal ) );
		return names.Select( lookup.Person ).ToArray();
	}

	/// <summary>The row of <paramref name="name"/>.</summary>
	public static ChatPerson Person( IChatPanelSource source, string name ) => new Lookup( source ).Person( name );

	/// <summary>The room's players, the buddies and the ignored players as sets, made once for a list's rows.</summary>
	// [EXT:online-chat-person-flags] a buddy counts as online and in this park while the room's roster names them; the original kept both flags per buddy from the server's buddy messages (0x4EE5C0), OpenTPW's server answers the buddy list with online and offline only
	private sealed class Lookup( IChatPanelSource source )
	{
		private readonly HashSet<string> here = new( source.RoomPlayers, StringComparer.OrdinalIgnoreCase );
		private readonly HashSet<string> ignored = new( source.Ignored, StringComparer.OrdinalIgnoreCase );
		private readonly Dictionary<string, bool> buddies = BuddyMap( source.Buddies );
		private readonly string? own = source.PlayerName;

		private static Dictionary<string, bool> BuddyMap( IReadOnlyList<(string Name, bool Online)> list )
		{
			var map = new Dictionary<string, bool>( StringComparer.OrdinalIgnoreCase );
			foreach ( var (name, online) in list )
				map.TryAdd( name, online );
			return map;
		}

		public ChatPerson Person( string name )
		{
			var flags = ChatPersonFlags.None;
			var inPark = here.Contains( name );
			if ( buddies.TryGetValue( name, out var online ) )
			{
				flags |= ChatPersonFlags.Buddy;
				if ( online || inPark )
					flags |= ChatPersonFlags.Online;
				if ( inPark )
					flags |= ChatPersonFlags.InPark;
			}
			if ( ignored.Contains( name ) )
				flags |= ChatPersonFlags.Ignored;
			return new ChatPerson( name, flags, own != null && own.Equals( name, StringComparison.OrdinalIgnoreCase ) );
		}
	}

	private static readonly Veldrid.RgbaByte NotBuddy = new( 0xFF, 0xFF, 0xFF, 0xFF );
	private static readonly Veldrid.RgbaByte BuddyOffline = new( 0xFE, 0x30, 0x30, 0xFF );
	private static readonly Veldrid.RgbaByte BuddyElsewhere = new( 0x51, 0x89, 0xEF, 0xFF );
	private static readonly Veldrid.RgbaByte BuddyHere = new( 0x00, 0xE2, 0x06, 0xFF );

	/// <summary>The colour of a name in the people list.</summary>
	// [BIN:TPW-EXE:0x4BE730 people row colour] the row's draw message (0x403) colours its text by the flags: no buddy white (0x758F30), a buddy offline red (0x758F3C), online elsewhere blue (0x758F34), in this park green (0x758F38)
	public static Veldrid.RgbaByte NameColor( ChatPersonFlags flags ) =>
		(flags & ChatPersonFlags.Buddy) == 0 ? NotBuddy
		: (flags & ChatPersonFlags.Online) == 0 ? BuddyOffline
		: (flags & ChatPersonFlags.InPark) == 0 ? BuddyElsewhere
		: BuddyHere;

	/// <summary>The UIHELPTEXT rows of a person's hover help: whether they are a buddy and where, whether the player ignores them, and that a right click opens the options (none for the player's own row).</summary>
	// [EXT:online-chat-help] the overlay's controls carry no help id in the table (0x758AE8); OpenTPW shows UIHELPTEXT 402-409, 418, 419, 422, 461 and 480 by their wording, and not 420 (the entry), which the help box would cover
	public static IEnumerable<int> PersonHelp( ChatPerson person )
	{
		if ( person.Own )
			yield break;
		if ( (person.Flags & ChatPersonFlags.Buddy) != 0 )
		{
			yield return 404;
			yield return (person.Flags & ChatPersonFlags.Online) == 0 ? 407 : (person.Flags & ChatPersonFlags.InPark) == 0 ? 409 : 408;
		}
		if ( (person.Flags & ChatPersonFlags.Ignored) != 0 )
			yield return 405;
		yield return 406;
	}

	/// <summary>
	/// The message list's rows: the chat lines cut into rows no wider than <paramref name="width"/> (by <paramref name="measure"/>), the last
	/// <see cref="MessageRows"/> of them.
	/// </summary>
	public static IReadOnlyList<string> MessageRowsOf( IReadOnlyList<string> lines, Func<string, float> measure, float width )
	{
		var rows = new List<string>();
		// only the last lines can fill the rows: each gives at least one
		for ( var index = Math.Max( 0, lines.Count - MessageRows ); index < lines.Count; index++ )
			Wrap( lines[index], measure, width, rows );
		return rows.Count > MessageRows ? rows.GetRange( rows.Count - MessageRows, MessageRows ) : rows;
	}

	/// <summary>
	/// Cuts <paramref name="line"/> into rows no wider than <paramref name="width"/>, each as many characters as fit (at least one). A surrogate pair counts as one
	/// character, so a row never ends between its halves (the original's text is single-byte).
	/// </summary>
	// [BIN:TPW-EXE:0x4BE970 message rows] the line is cut where the sum of its characters' widths (the font's vfunc +0xC) reaches the list column's width ((right − left) × screen width / 2048): characters, not words
	internal static void Wrap( string line, Func<string, float> measure, float width, List<string> rows )
	{
		var start = 0;
		while ( start < line.Length )
		{
			var end = After( line, start );
			while ( end < line.Length && measure( line[start..After( line, end )] ) <= width )
				end = After( line, end );
			rows.Add( line[start..end] );
			start = end;
		}
		if ( line.Length == 0 )
			rows.Add( line );
	}

	/// <summary>The index after the character at <paramref name="index"/>: past both halves of a surrogate pair.</summary>
	private static int After( string line, int index ) =>
		index + (char.IsHighSurrogate( line[index] ) && index + 1 < line.Length && char.IsLowSurrogate( line[index + 1] ) ? 2 : 1);
}
