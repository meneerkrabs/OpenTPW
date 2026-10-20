namespace OpenTPW.Online.Api;

// [EXT:ONLINE-040] OpenTPW server HTTP API (JSON, camelCase via StrictJson.Options). The original
// online service (EA "daphne" login/city/mail/chat/news/vote servers, Online.sam) is gone and its
// protocols are unknown; this API only mirrors the original feature set.

public static class ApiRoutes
{
	public const string Prefix = "/api/v1";
	public const string Server = Prefix + "/server";
	public const string Accounts = Prefix + "/accounts";
	public const string AccountConfirmation = Accounts + "/confirm";
	public const string AccountEmail = Accounts + "/email";
	public const string Recovery = Accounts + "/recover";
	public const string RecoveryConfirmation = Recovery + "/confirm";
	public const string Sessions = Prefix + "/sessions";
	public const string Parks = Prefix + "/parks";
	public const string Postcards = Prefix + "/postcards";
	public const string Inbox = Postcards + "/inbox";
	public const string Reports = Prefix + "/reports";
	public const string News = Prefix + "/news";
	/// <summary>The online world's cities (the globe's flags), public.</summary>
	public const string Cities = Prefix + "/cities";
	/// <summary>The online world's chat parks (the globe's five satellites), with their player counts; public.</summary>
	public const string ChatParks = Prefix + "/chat-parks";
	/// <summary>The chat parks' scenes: what each satellite's park details show and the park its visit enters; needs a login.</summary>
	public const string ChatParkScenes = ChatParks + "/scenes";
	/// <summary>The players of the online world: search by name, and each player's own park page; public.</summary>
	public const string Players = Prefix + "/players";
	/// <summary>The public charts of the original website's pages: top parks, top players, richest players.</summary>
	public const string ChartParks = Prefix + "/charts/parks";
	public const string ChartPlayers = Prefix + "/charts/players";
	public const string ChartRichest = Prefix + "/charts/richest";
	/// <summary>Operator only: asks the host to update the server (<c>DeployToken</c>).</summary>
	public const string Deploy = Prefix + "/admin/deploy";
	/// <summary>The public list of parks shown on the project website, and their pictures (<c>/{id}/thumbnail</c>).</summary>
	public const string WebsiteParks = Parks + "/website";
	public const string Chat = global::OpenTPW.Online.Chat.ChatProtocol.Path;
	public const string PackageMediaType = "application/vnd.opentpw.park";
	public const string PostcardMediaType = "application/vnd.opentpw.postcard";
}

public sealed record ServerInfo( string Name, string Version, int ProtocolVersion, string? Message, bool RegistrationOpen, bool WordFilterLoaded,
	int MaximumParkBytes, int MaximumPostcardBytes, int MaximumParksPerPlayer, int MaximumVotesPerDay );

/// <summary>A city of the online world: a flag on the globe at its latitude and longitude, with its number of parks.</summary>
public sealed record CityInfo( int Id, string Name, string Description, float Latitude, float Longitude, int Parks );

public sealed record CityList( IReadOnlyList<CityInfo> Cities );

/// <summary>
/// A chat park of the online world: a standing chat room drawn as one of the globe's five satellites, with the
/// number of players currently in it (the original's RoomInfo query; docs/reverse/ONLINE-FEATURES.md).
/// </summary>
public sealed record ChatParkInfo( int Id, string Name, int Players );

public sealed record ChatParkList( IReadOnlyList<ChatParkInfo> ChatParks );

/// <summary>
/// A chat park's scene (<see cref="ApiRoutes.ChatParkScenes"/>): its name and the description its satellite's park details show, and what its
/// visit enters — the published <see cref="Park"/> the server names for it, or else the starting park of the original <see cref="Level"/>, which the
/// game builds itself. A scene with neither has no park; the game enters its room in the overhead chat view.
/// </summary>
public sealed record ChatParkScene( int Id, string Name, string Description, string? Level, ParkSummary? Park );

public sealed record ChatParkSceneList( IReadOnlyList<ChatParkScene> Scenes );

/// <summary>The server's Game News and System News; empty text when the operator wrote none.</summary>
public sealed record NewsInfo( string Game, string System, DateTimeOffset? UpdatedUtc );

/// <summary>Name and password; <see cref="Code"/> is the authenticator code for accounts with two-step login (omitted when null, for older servers).</summary>
public sealed record Credentials( string Name, string Password,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] string? Code = null );

/// <summary>
/// The authenticator secret for a new or reset account (RFC 6238, base32) and its <c>otpauth://</c> URI. Nothing is
/// stored for a new account until <see cref="TotpConfirmation"/> arrives with a valid code before <see cref="ExpiresUtc"/>.
/// <see cref="Ticket"/> proves the confirmation comes from whoever registered (or logged in with the password).
/// </summary>
public sealed record TotpEnrollment( string Name, string Ticket, string Secret, string Uri, DateTimeOffset ExpiresUtc,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] bool? EmailRequired = null );

/// <summary>Finishes an enrollment with its ticket and the first code from the authenticator app.</summary>
public sealed record TotpConfirmation( string Name, string Ticket, string Code,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] string? EmailCode = null );

/// <summary>Sends a code to the address a new account will use for recovery.</summary>
public sealed record EnrollmentEmail( string Name, string Ticket, string Email, [property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] string? Language = null );

/// <summary>Asks for a recovery code for a player name or e-mail address; the answer is the same whether it exists or not.</summary>
public sealed record RecoveryRequest( string NameOrEmail, [property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] string? Language = null );

/// <summary>A new password with the recovery code; <see cref="ResetTwoStep"/> also sets up two-step login again.</summary>
public sealed record RecoveryConfirmation( string NameOrEmail, string Code, string Password, bool ResetTwoStep, [property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] string? Language = null );

public sealed record SessionToken( string Name, string Token, DateTimeOffset ExpiresUtc );

public sealed record ParkSummary( string Id, string Name, string Description, string Author, string Level, string Language,
	DateTimeOffset PublishedUtc, int Visits, int Votes, bool VisitedBefore, bool VotedFor, int PlayersAtPark, bool HasThumbnail, int Bytes, int CityId = 0 );

public sealed record ParkList( IReadOnlyList<ParkSummary> Parks, int Total );

/// <summary>One park of a player's own page: the original website's "My Theme Park" columns (ONLINE-SOURCES.md S1).</summary>
public sealed record PlayerPark( string Name, int Visits, int Votes, DateTimeOffset PublishedUtc, int Rank, int HighestRank );

/// <summary>
/// A player's published parks with their chart places and the player's Platinum Tickets (S1/S2). Without a login
/// only parks the author chose to show on the website are listed.
/// </summary>
public sealed record PlayerProfile( string Name, int PlatinumTickets, IReadOnlyList<PlayerPark> Parks );

/// <summary>The public top-parks chart (S1: rank, park name, player), of the parks their authors show on the website.</summary>
public sealed record ChartParkEntry( int Rank, string Name, string Player );

public sealed record ChartParkList( IReadOnlyList<ChartParkEntry> Parks );

/// <summary>The public player charts: Top 10 players (by votes) and Richest players (Platinum Tickets, S1).</summary>
public sealed record ChartPlayerEntry( int Rank, string Player, int Score );

public sealed record ChartPlayerList( IReadOnlyList<ChartPlayerEntry> Players );

/// <summary>Public player search by name (S1); OpenTPW accounts have one player name, so first/last names do not exist.</summary>
public sealed record PlayerSearchResult( IReadOnlyList<string> Names, bool More );

/// <summary>A park listed on the project website: only what its author chose to make public.</summary>
public sealed record WebsitePark( string Id, string Name, string Description, string Author, string Level, int Votes, int Visits, DateTimeOffset PublishedUtc, bool HasThumbnail );

public sealed record WebsiteParkList( IReadOnlyList<WebsitePark> Parks );

public sealed record CounterResult( int Visits, int Votes, int VotesLeftToday );

public sealed record PostcardSummary( string Id, string From, string Title, DateTimeOffset SentUtc, bool HasImage, string? ParkName );

public sealed record PostcardInbox( IReadOnlyList<PostcardSummary> Postcards );

public sealed record PostcardSent( string Id, IReadOnlyList<string> DeliveredTo,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] string? Error = null );

public sealed record ReportRequest( string Kind, string Target, string Reason );

public static class ReportKinds
{
	public const string Park = "park";
	public const string Player = "player";
	public const string Postcard = "postcard";
	public const string Chat = "chat";

	public static bool IsKnown( string? kind ) => kind is Park or Player or Postcard or Chat;
}

public sealed record ApiError( string Error,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] string? Reason = null );

/// <summary>Machine-readable <see cref="ApiError.Reason"/> values.</summary>
public static class ApiErrorReasons
{
	/// <summary>The account uses two-step login and the request had no code.</summary>
	public const string TotpRequired = "totp-required";
	/// <summary>The code was wrong, expired or already used.</summary>
	public const string TotpInvalid = "totp-invalid";
	/// <summary>The server needs a confirmed e-mail address for a new account.</summary>
	public const string EmailRequired = "email-required";
	/// <summary>The code from the e-mail was wrong or no longer valid.</summary>
	public const string EmailCodeInvalid = "email-code-invalid";
}
