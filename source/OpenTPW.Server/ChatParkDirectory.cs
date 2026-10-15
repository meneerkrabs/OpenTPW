using System.Text.Json;
using OpenTPW.Online.Api;

namespace OpenTPW.Server;

/// <summary>
/// The chat parks of the online world: the standing chat rooms the globe draws as its five satellites
/// (docs/ONLINE.md, docs/reverse/ONLINE-FEATURES.md). Each satellite shows the number of players in its
/// room and a click enters it. The operator may rename them with <c>chatparks.json</c> in the data
/// folder (an array of <see cref="ChatParkDefinition"/>); it is re-read when that file changes.
/// </summary>
// [BIN:STP-PPC:0x1b8808 chatellite connector] the original fetched a "Chat Satellite List" (UITEXT 252) from the
// service and wrote each room's "%d" player count onto one of the five satellites; the original room names were
// server-side and are lost, so the default names are OpenTPW's choice
public sealed class ChatParkDirectory
{
	/// <summary>The five satellites of the Mac layout table (data:0x523f0, controls 90120-90124).</summary>
	public const int Count = Online.Chat.ChatProtocol.SatelliteRoomCount;
	/// <summary>The original's chat park name length is unknown; OpenTPW bounds it at 32 characters.</summary>
	public const int MaximumNameLength = 32;

	public sealed record ChatParkDefinition( int Id, string Name );

	// [EXT:ONLINE-071] default chat park names; the original's list is not recovered
	public static readonly IReadOnlyList<ChatParkDefinition> Defaults = new ChatParkDefinition[]
	{
		new( 1, "The Meeting Place" ), new( 2, "The Fairground" ), new( 3, "Coaster Corner" ),
		new( 4, "The Lost Kingdom" ), new( 5, "The Space Port" ),
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

	public ChatParkDefinition? Find( int id ) => Read().FirstOrDefault( park => park.Id == id );

	private IReadOnlyList<ChatParkDefinition> Load()
	{
		try
		{
			var parks = JsonSerializer.Deserialize<List<ChatParkDefinition>>( File.ReadAllText( path ), new JsonSerializerOptions( JsonSerializerDefaults.Web ) ) ?? new();
			var valid = parks.Where( park => park.Id is >= 1 and <= Count && !string.IsNullOrWhiteSpace( park.Name ) )
				.GroupBy( park => park.Id ).Select( group => group.First() )
				.Select( park => park with { Name = park.Name.Trim()[..Math.Min( park.Name.Trim().Length, MaximumNameLength )] } ).ToList();
			return valid.Count > 0 ? valid : Defaults;
		}
		catch ( Exception exception ) when ( exception is IOException or JsonException )
		{
			return Defaults;
		}
	}
}
