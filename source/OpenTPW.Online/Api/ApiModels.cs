namespace OpenTPW.Online.Api;

// [EXT:ONLINE-040] OpenTPW server HTTP API (JSON, camelCase via StrictJson.Options). The original
// online service (EA "daphne" login/city/mail/chat/news/vote servers, Online.sam) is gone and its
// protocols are unknown; this API only mirrors the original feature set.

public static class ApiRoutes
{
	public const string Prefix = "/api/v1";
	public const string Server = Prefix + "/server";
	public const string Accounts = Prefix + "/accounts";
	public const string Sessions = Prefix + "/sessions";
	public const string Parks = Prefix + "/parks";
	public const string Postcards = Prefix + "/postcards";
	public const string Inbox = Postcards + "/inbox";
	public const string Reports = Prefix + "/reports";
	public const string News = Prefix + "/news";
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

/// <summary>The server's Game News and System News; empty text when the operator wrote none.</summary>
public sealed record NewsInfo( string Game, string System, DateTimeOffset? UpdatedUtc );

public sealed record Credentials( string Name, string Password );

public sealed record SessionToken( string Name, string Token, DateTimeOffset ExpiresUtc );

public sealed record ParkSummary( string Id, string Name, string Description, string Author, string Level, string Language,
	DateTimeOffset PublishedUtc, int Visits, int Votes, bool VisitedBefore, bool VotedFor, int PlayersAtPark, bool HasThumbnail, int Bytes );

public sealed record ParkList( IReadOnlyList<ParkSummary> Parks, int Total );

/// <summary>A park listed on the project website: only what its author chose to make public.</summary>
public sealed record WebsitePark( string Id, string Name, string Description, string Author, string Level, int Votes, int Visits, DateTimeOffset PublishedUtc, bool HasThumbnail );

public sealed record WebsiteParkList( IReadOnlyList<WebsitePark> Parks );

public sealed record CounterResult( int Visits, int Votes, int VotesLeftToday );

public sealed record PostcardSummary( string Id, string From, string Title, DateTimeOffset SentUtc, bool HasImage, string? ParkName );

public sealed record PostcardInbox( IReadOnlyList<PostcardSummary> Postcards );

public sealed record PostcardSent( string Id, IReadOnlyList<string> DeliveredTo );

public sealed record ReportRequest( string Kind, string Target, string Reason );

public static class ReportKinds
{
	public const string Park = "park";
	public const string Player = "player";
	public const string Postcard = "postcard";
	public const string Chat = "chat";

	public static bool IsKnown( string? kind ) => kind is Park or Player or Postcard or Chat;
}

public sealed record ApiError( string Error );
