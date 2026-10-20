using System.Text.Json;
using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Online.Packages;

namespace OpenTPW.Server;

/// <summary>
/// The chat parks of the online world: the standing chat rooms the globe draws as its five satellites
/// (docs/ONLINE.md, docs/reverse/ONLINE-FEATURES.md). Each satellite shows the number of players in its
/// room, and a click opens its park details, from which the player visits its park: a published park the
/// operator names for it, or else the starting park of an original level, which the game builds itself
/// (<see cref="Scenes"/>). The operator configures them with <c>chatparks.json</c> in the data folder (an
/// array of <see cref="ChatParkDefinition"/>); it is re-read when that file changes.
/// </summary>
// [BIN:STP-PPC:0x1b8808 chatellite connector] the original fetched a "Chat Satellite List" (UITEXT 252) from the
// service and wrote each room's "%d" player count onto one of the five satellites; the original room names were
// server-side and are lost, so the default names are OpenTPW's choice
// [BIN:TPW-EXE:0x5B7610 chat satellite list] each City connection asks for the parks of city 1 (GetParkInCityList(0, 1, lastCRC, 0) through 0x5BCB30),
// full park records (0x5BDE30) of which satellite i shows entry i; the counts come from a RoomInfo for each park's "%d" room on a connection of their own
// (0x4C4BB0, the answers matched to the satellites by park id in 0x4C4920)
public sealed class ChatParkDirectory
{
	/// <summary>The five satellites of the Mac layout table (data:0x523f0, controls 90120-90124).</summary>
	public const int Count = Online.Chat.ChatProtocol.SatelliteRoomCount;
	/// <summary>The original's chat park name length is unknown; OpenTPW bounds it at 32 characters, a park name's length.</summary>
	public const int MaximumNameLength = ParkPackage.MaximumParkNameLength;
	/// <summary>The levels a game builds a chat park's starting park from: the four themes' levels.</summary>
	public static readonly IReadOnlySet<string> Levels = new HashSet<string>( new[] { "jungle", "hallow", "fantasy", "space" }, StringComparer.Ordinal );

	/// <summary>One satellite's chat park.</summary>
	/// <param name="Level">
	/// The original level (jungle, hallow, fantasy or space) whose starting park the game builds for the satellite's visit while it names no published
	/// park; left out, the satellite's default level.
	/// </param>
	/// <param name="ParkId">A published park the satellite's visit enters, while it stays published.</param>
	/// <param name="Description">The text of the satellite's park details; left out, the published park's own description.</param>
	public sealed record ChatParkDefinition( int Id, string Name, string? Level = null, string? ParkId = null, string? Description = null );

	// [EXT:ONLINE-071] default chat park names, levels and descriptions; the original's list (the parks of city 1) is not recovered
	public static readonly IReadOnlyList<ChatParkDefinition> Defaults = new ChatParkDefinition[]
	{
		new( 1, "The Meeting Place", "jungle", Description: "The place to meet other players: walk around the park together and chat." ),
		new( 2, "The Fairground", "fantasy", Description: "Stroll past the rides and stalls with other players and chat." ),
		new( 3, "Coaster Corner", "hallow", Description: "Talk coasters with other players while you walk the park." ),
		new( 4, "The Lost Kingdom", "jungle", Description: "Explore the jungle with other players and chat." ),
		new( 5, "The Space Port", "space", Description: "Meet other players among the stars and chat." ),
	};

	private readonly string path;
	private readonly object gate = new();
	private DateTime stamp = DateTime.MinValue;
	private IReadOnlyList<ChatParkDefinition> current = Defaults;

	public ChatParkDirectory( string dataDirectory ) => path = Path.Combine( dataDirectory, "chatparks.json" );

	public IReadOnlyList<ChatParkDefinition> Read()
	{
		var next = File.Exists( path ) ? File.GetLastWriteTimeUtc( path ) : DateTime.MinValue;
		lock ( gate )
		{
			if ( next != stamp )
			{
				current = next == DateTime.MinValue ? Defaults : Load();
				stamp = next;
			}
			return current;
		}
	}

	/// <summary>The list with each park's live player count.</summary>
	public IReadOnlyList<ChatParkInfo> WithPlayers( Func<string, int> players ) =>
		Read().Select( park => new ChatParkInfo( park.Id, park.Name, players( Online.Chat.ChatProtocol.SatelliteRoom( park.Id ) ) ) ).ToList();

	/// <summary>
	/// The satellites' scenes: each one's name, the description its park details show, and what its visit enters — the published park it names while
	/// <paramref name="publishedPark"/> finds it (the summary of a published park by id, or null), else the starting park of its level.
	/// </summary>
	public IReadOnlyList<ChatParkScene> Scenes( Func<string, ParkSummary?> publishedPark ) =>
		Read().Select( park =>
		{
			var summary = park.ParkId is { } id ? publishedPark( id ) : null;
			return new ChatParkScene( park.Id, park.Name, park.Description ?? summary?.Description ?? "", summary?.Level ?? park.Level, summary );
		} ).ToList();

	public ChatParkDefinition? Find( int id ) => Read().FirstOrDefault( park => park.Id == id );

	private IReadOnlyList<ChatParkDefinition> Load()
	{
		try
		{
			var parks = JsonSerializer.Deserialize<List<ChatParkDefinition?>>( File.ReadAllText( path ), new JsonSerializerOptions( JsonSerializerDefaults.Web ) ) ?? new();
			var valid = parks.OfType<ChatParkDefinition>().Where( park => park.Id is >= 1 and <= Count && IsName( park.Name ) )
				.GroupBy( park => park.Id ).Select( group => Clean( group.First() ) ).ToList();
			return valid.Count > 0 ? valid : Defaults;
		}
		catch ( Exception exception ) when ( exception is IOException or JsonException )
		{
			return Defaults;
		}
	}

	private static bool IsName( string? name ) => !string.IsNullOrWhiteSpace( name ) && IsText( name, int.MaxValue, false );

	/// <summary>
	/// A valid entry with its name trimmed to <see cref="MaximumNameLength"/>; a malformed park id or description is left out, and a level that is
	/// missing or none of <see cref="Levels"/> is the satellite's default one.
	/// </summary>
	private static ChatParkDefinition Clean( ChatParkDefinition park )
	{
		var name = park.Name.Trim();
		var level = park.Level is { } named && Levels.Contains( named ) ? named : Defaults[park.Id - 1].Level;
		return new ChatParkDefinition( park.Id, Shorten( name, MaximumNameLength ), level,
			park.ParkId is { } id && ServerProgram.IsId( id ) ? id : null,
			park.Description is { } text && IsText( text, ParkPackage.MaximumDescriptionLength, true ) ? text : null );
	}

	/// <summary>The first <paramref name="length"/> characters of <paramref name="text"/>, without splitting a surrogate pair, and without trailing spaces.</summary>
	private static string Shorten( string text, int length )
	{
		if ( text.Length <= length )
			return text;
		var cut = char.IsHighSurrogate( text[length - 1] ) ? length - 1 : length;
		return text[..cut].TrimEnd();
	}

	/// <summary>Text a park package would carry (<see cref="OnlineText.RequireText"/>): the game builds the satellite's park from it.</summary>
	private static bool IsText( string text, int maximumLength, bool allowNewlines )
	{
		try
		{
			OnlineText.RequireText( text, maximumLength, allowNewlines, true, "Chat park text" );
			return true;
		}
		catch ( InvalidDataException )
		{
			return false;
		}
	}
}
