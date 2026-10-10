namespace OpenTPW.Server;

/// <summary>
/// Operator settings (configuration section <c>OpenTPW</c>, environment <c>OpenTPW__Name</c>).
/// [EXT:ONLINE-050] All defaults are OpenTPW choices; the original service limits are unknown except
/// that limits existed (UITEXT 221 personal publish limit, CHAT_COMMANDS 131 daily vote limit).
/// </summary>
public sealed class ServerOptions
{
	public const string Section = "OpenTPW";

	public string ServerName { get; set; } = "OpenTPW community server";
	/// <summary>Accounts, parks, postcards and reports. Created when missing.</summary>
	public string DataDirectory { get; set; } = "data";
	/// <summary>
	/// The operator's own original language folder holding <c>swears.txt</c>/<c>alloweds.txt</c>, e.g.
	/// <c>/game/Data/Language/English</c>. Never shipped with the server; empty means no word filter.
	/// </summary>
	public string? FilterDirectory { get; set; }
	/// <summary>Optional message of the day, sent to chat on connect and shown in server info.</summary>
	public string? Message { get; set; }
	public bool AllowRegistration { get; set; } = true;
	/// <summary>
	/// The published browser build of the game (the <c>wwwroot</c> of OpenTPW.Web), served at the site
	/// root next to the API; empty serves the API only. It holds only OpenTPW code (docs/SERVER.md).
	/// </summary>
	public string? WebClientDirectory { get; set; }
	/// <summary>PBKDF2-SHA256 iterations for new password hashes.</summary>
	public int PasswordIterations { get; set; } = 600_000;
	public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays( 7 );
	public int MaximumSessionsPerPlayer { get; set; } = 8;
	public int MaximumParksPerPlayer { get; set; } = 5;
	public int MaximumVotesPerDay { get; set; } = 10;
	public int MaximumInboxPostcards { get; set; } = 200;
	public int MaximumBuddies { get; set; } = 50;
	public int MaximumAccounts { get; set; } = 100_000;
	/// <summary>Requests per minute per client address, all endpoints.</summary>
	public int RequestsPerMinute { get; set; } = 240;
	/// <summary>Logins and registrations per minute per client address.</summary>
	public int AuthenticationsPerMinute { get; set; } = 10;
	/// <summary>Park and postcard uploads per hour per client address.</summary>
	public int UploadsPerHour { get; set; } = 30;
	/// <summary>Chat: burst size and refill per second of each connection's message bucket.</summary>
	public int ChatBurst { get; set; } = 8;
	public int ChatMessagesPerSecond { get; set; } = 1;
	public int MaximumChatConnections { get; set; } = 1000;
	/// <summary>Moderation: players that cannot log in, players that cannot speak in chat, parks hidden from lists.</summary>
	public List<string> BannedPlayers { get; set; } = new();
	public List<string> MutedPlayers { get; set; } = new();
	public List<string> HiddenParks { get; set; } = new();
	/// <summary>
	/// Web pages (origins such as <c>https://opentpw.io</c>) allowed to read the public news and website park list from
	/// the browser; empty allows none. Only GET requests to those public routes are affected.
	/// </summary>
	public List<string> WebsiteOrigins { get; set; } = new();
	/// <summary>
	/// Secret for <c>POST /api/v1/admin/deploy</c>, which asks the host to update the server (docs/SERVER.md); empty
	/// disables the route.
	/// </summary>
	public string? DeployToken { get; set; }

	public void Validate()
	{
		if ( string.IsNullOrWhiteSpace( DataDirectory ) )
			throw new InvalidOperationException( "OpenTPW:DataDirectory is required." );
		if ( PasswordIterations < 1000 || MaximumParksPerPlayer < 0 || MaximumVotesPerDay < 0 || RequestsPerMinute < 1
			|| AuthenticationsPerMinute < 1 || UploadsPerHour < 1 || ChatBurst < 1 || ChatMessagesPerSecond < 1 || SessionLifetime <= TimeSpan.Zero )
			throw new InvalidOperationException( "OpenTPW server limits must be positive." );
	}
}
