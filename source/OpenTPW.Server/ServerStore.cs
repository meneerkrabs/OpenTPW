using System.Text.Json;
using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Online.Packages;

namespace OpenTPW.Server;

public sealed class AccountRecord
{
	public string Name { get; set; } = "";
	public string Key { get; set; } = "";
	public string Salt { get; set; } = "";
	public string Hash { get; set; } = "";
	public int Iterations { get; set; }
	public DateTimeOffset CreatedUtc { get; set; }
	public List<string> Buddies { get; set; } = new();
	public string VoteDay { get; set; } = "";
	public int VotesToday { get; set; }
	/// <summary>Authenticator secret (base32, RFC 6238) of an account with two-step login; null for older accounts.</summary>
	public string? TotpSecret { get; set; }
	/// <summary>The operator reset two-step login: the next login with the right password sets up a new secret.</summary>
	public bool TotpEnroll { get; set; }
	/// <summary>When an operator reset was applied; reset requests written before this (or before the account) are stale.</summary>
	public DateTimeOffset? TotpResetUtc { get; set; }
	/// <summary>The last reset request applied (file name, time and size), so a copy left behind is never applied twice.</summary>
	public string? TotpResetRequest { get; set; }
	/// <summary>Confirmed e-mail address for account recovery (one account per address); null for older accounts.</summary>
	public string? Email { get; set; }
}

public sealed class ParkRecord
{
	public string Id { get; set; } = "";
	public string OwnerKey { get; set; } = "";
	public string Author { get; set; } = "";
	public string Name { get; set; } = "";
	public string Description { get; set; } = "";
	public string Level { get; set; } = "";
	public string Language { get; set; } = "";
	public Guid PackageId { get; set; }
	public DateTimeOffset PublishedUtc { get; set; }
	public int Bytes { get; set; }
	public bool HasThumbnail { get; set; }
	/// <summary>The author chose to list this park on the project website (opt-in when publishing).</summary>
	public bool ShowOnWebsite { get; set; }
	/// <summary>City of the online world the park is published in (<see cref="CityDirectory"/>); 0 for parks from before cities.</summary>
	public int CityId { get; set; }
	public int Visits { get; set; }
	public int Votes { get; set; }
	/// <summary>
	/// The park's best place in the votes chart (1 is the top; 0 before it ever placed). The original's "My Theme
	/// Park" page showed each park's current rank and highest rank (docs/reverse/ONLINE-SOURCES.md S1).
	/// </summary>
	public int HighestRank { get; set; }
	public List<string> Visitors { get; set; } = new();
	/// <summary>Player key → UTC day (yyyy-MM-dd) of that player's last vote for this park.</summary>
	public Dictionary<string, string> LastVoteDay { get; set; } = new();
	/// <summary>Player key → UTC day (yyyy-MM-dd) of that player's last counted visit to this park.</summary>
	public Dictionary<string, string> LastVisitDay { get; set; } = new();
}

/// <summary>A login session: the SHA-256 hash of its token (never the token), the account and the expiry.</summary>
public sealed record SessionRecord( string TokenHash, string Key, DateTimeOffset Expires );

public sealed class PostcardRecord
{
	public string Id { get; set; } = "";
	public string RecipientKey { get; set; } = "";
	public string From { get; set; } = "";
	public string Title { get; set; } = "";
	public DateTimeOffset SentUtc { get; set; }
	public bool HasImage { get; set; }
	public string? ParkName { get; set; }
}

public sealed record ReportRecord( DateTimeOffset Utc, string Reporter, string Kind, string Target, string Reason );

public sealed class StoreException : Exception
{
	public StoreException( int status, string message, string? reason = null ) : base( message ) => (Status, Reason) = (status, reason);
	public int Status { get; }
	/// <summary>An <see cref="Online.Api.ApiErrorReasons"/> value, or null.</summary>
	public string? Reason { get; }
}

/// <summary>A login result: a session, or a two-step enrollment the player has to confirm first.</summary>
public sealed record LoginOutcome( SessionToken? Session, TotpEnrollment? Enrollment );

/// <summary>
/// [EXT:ONLINE-052] File storage: JSON indexes (accounts, parks, postcards) rewritten atomically after
/// each change, blobs as files named by random ids, reports appended as JSON lines. One lock guards
/// all state; meant for community-sized servers. Sessions are kept as token hashes in sessions.json, so a restart
/// (a deploy) does not log players out.
/// </summary>
public sealed partial class ServerStore
{
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
	private static readonly JsonSerializerOptions LineOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
	private readonly object gate = new();
	private readonly ServerOptions options;
	private readonly Func<DateTimeOffset> clock;
	private readonly Dictionary<string, AccountRecord> accounts;
	private readonly Dictionary<string, ParkRecord> parks;
	private readonly Dictionary<string, PostcardRecord> postcards;
	private readonly Dictionary<string, (string Key, DateTimeOffset Expires)> sessions = new();
	/// <summary>Enrollments waiting for their first code, by account key; new accounts are not stored before that.</summary>
	private readonly Dictionary<string, PendingEnrollment> enrollments = new();
	/// <summary>Last accepted code step per account, kept in <c>totp-steps.json</c> so a used code stays used after a restart.</summary>
	private readonly Dictionary<string, long> lastCodeSteps;
	/// <summary>Recent wrong login codes per account (memory only).</summary>
	private readonly Dictionary<string, List<DateTimeOffset>> codeFailures = new();

	/// <summary>
	/// A setup waiting for its first code. <see cref="TicketHash"/> binds the confirmation to whoever started it;
	/// <see cref="Failures"/> counts wrong codes for this setup only. A reset setup also holds the account's salt, so it
	/// can only finish the same account (a deleted and re-registered name has a new salt).
	/// </summary>
	private sealed record PendingEnrollment( string Name, string Key, string Salt, string Hash, int Iterations, byte[] Secret, string TicketHash,
		DateTimeOffset Expires, bool NewAccount )
	{
		public List<DateTimeOffset> Failures { get; } = new();
		/// <summary>The address a code was sent to, the code's hash and its wrong attempts.</summary>
		public string? Email { get; set; }
		public string? EmailCodeHash { get; set; }
		public int EmailFailures { get; set; }
	}

	/// <summary>A recovery code waiting for its use (memory only), by account key.</summary>
	private sealed class RecoveryCode
	{
		public required string CodeHash { get; init; }
		public required DateTimeOffset Expires { get; init; }
		public int Failures { get; set; }
	}

	private readonly Dictionary<string, RecoveryCode> recoveryCodes = new();
	/// <summary>
	/// Recent account e-mails per player name and per address, for <see cref="MaximumEmailsPerHour"/>. Kept apart from the
	/// setups, so registering again does not reset the count, and per address, so nobody can flood someone else's inbox.
	/// </summary>
	private readonly Dictionary<string, List<DateTimeOffset>> emailSends = new();

	public static readonly TimeSpan EmailCodeLifetime = TimeSpan.FromMinutes( 15 );
	public const int MaximumEmailsPerHour = 3;
	public const int MaximumEmailCodeFailures = 5;
	public const int MaximumEmailLength = 254;

	public const int MaximumPendingEnrollments = 1000;
	public static readonly TimeSpan EnrollmentLifetime = TimeSpan.FromMinutes( 10 );
	/// <summary>Wrong codes per account within <see cref="CodeFailureWindow"/> before codes are refused for the rest of it.</summary>
	public const int MaximumCodeFailures = 5;
	public static readonly TimeSpan CodeFailureWindow = TimeSpan.FromMinutes( 15 );
	/// <summary>
	/// Operator folder in the data directory: each file holds one player name whose two-step login is reset at their next
	/// login with the right password. The server deletes the file when it applies the reset.
	/// </summary>
	public const string TotpResetDirectory = "totp-reset";
	private const int MaximumResetFileBytes = 1024;

	public ServerStore( ServerOptions options, Func<DateTimeOffset>? clock = null )
	{
		this.options = options;
		this.clock = clock ?? (() => DateTimeOffset.UtcNow);
		Root = Path.GetFullPath( options.DataDirectory );
		Directory.CreateDirectory( Path.Combine( Root, "parks" ) );
		Directory.CreateDirectory( Path.Combine( Root, "postcards" ) );
		accounts = Load<List<AccountRecord>>( "accounts.json" ).ToDictionary( item => item.Key );
		parks = Load<List<ParkRecord>>( "parks.json" ).ToDictionary( item => item.Id );
		postcards = Load<List<PostcardRecord>>( "postcards.json" ).ToDictionary( item => item.Id );
		foreach ( var session in Load<List<SessionRecord>>( "sessions.json" ).Where( session => session.Expires > this.clock() && accounts.ContainsKey( session.Key ) ) )
			sessions[session.TokenHash] = (session.Key, session.Expires);
		postcardDeliveries = Load<Dictionary<string, PostcardDeliveryRecord>>( "postcard-deliveries.json" );
		postcardAttempts = Load<List<PostcardAttempt>>( "postcard-attempts.json" );
		lastCodeSteps = Load<Dictionary<string, long>>( "totp-steps.json" ).Where( pair => accounts.ContainsKey( pair.Key ) ).ToDictionary( pair => pair.Key, pair => pair.Value );
		// Created by the server, which owns it and can delete the operator's request files in it (docs/SERVER.md).
		Directory.CreateDirectory( Path.Combine( Root, TotpResetDirectory ) );
		// The data holds password hashes and authenticator secrets: owner only (the operator works as root).
		if ( !OperatingSystem.IsWindows() )
		{
			try { File.SetUnixFileMode( Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute ); }
			catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException ) { }
			DataDirectoryIsPrivate = (File.GetUnixFileMode( Root ) & (UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute)) == 0;
		}
	}

	/// <summary>
	/// True when only the server's user can list the data directory; false when it could not be restricted, and on Windows,
	/// where its access list is not checked. Logged at start.
	/// </summary>
	public bool DataDirectoryIsPrivate { get; }

	/// <summary>Call inside the lock after any change to <see cref="sessions"/>.</summary>
	private void SaveSessions() => Save( "sessions.json", sessions.Select( pair => new SessionRecord( pair.Key, pair.Value.Key, pair.Value.Expires ) ).ToList() );

	public string Root { get; }

	private T Load<T>( string name ) where T : new()
	{
		var file = Path.Combine( Root, name );
		if ( !File.Exists( file ) )
			return new T();
		using var stream = File.OpenRead( file );
		return JsonSerializer.Deserialize<T>( stream, JsonOptions ) ?? new T();
	}

	private void Save<T>( string name, T value ) => AtomicFile.Write( Path.Combine( Root, name ), JsonSerializer.SerializeToUtf8Bytes( value, JsonOptions ) );

	private string Today => clock().UtcDateTime.ToString( "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture );

	private static string NewId() => Guid.NewGuid().ToString( "N" );

	// ---- accounts and sessions ----

	public AccountRecord? FindAccount( string name )
	{
		lock ( gate )
			return accounts.GetValueOrDefault( OnlineText.NormalizeName( name ) );
	}

	public bool IsBanned( string name ) => options.BannedPlayers.Any( banned => OnlineText.NormalizeName( banned ) == OnlineText.NormalizeName( name ) );

	public bool IsMuted( string name ) => options.MutedPlayers.Any( muted => OnlineText.NormalizeName( muted ) == OnlineText.NormalizeName( name ) );

	public AccountRecord Register( string name, string password )
	{
		ValidateRegistration( name, password );
		var key = OnlineText.NormalizeName( name );
		// Cheap checks first: a taken name or a full server must not cost a password hash.
		lock ( gate )
			CheckRegistration( key );
		var (salt, hash) = PasswordHasher.Hash( password, options.PasswordIterations );
		lock ( gate )
		{
			CheckRegistration( key );
			var account = new AccountRecord { Name = name, Key = key, Salt = salt, Hash = hash, Iterations = options.PasswordIterations, CreatedUtc = clock() };
			accounts.Add( key, account );
			Save( "accounts.json", accounts.Values.ToList() );
			return account;
		}
	}

	private void ValidateRegistration( string name, string password )
	{
		if ( !options.AllowRegistration )
			throw new StoreException( 403, "Registration is closed on this server." );
		if ( !OnlineText.IsValidPlayerName( name ) )
			throw new StoreException( 400, $"Names must be {OnlineText.MinimumNameLength}-{OnlineText.MaximumNameLength} letters, digits, spaces, '_', '-' or '.'." );
		if ( !PasswordHasher.IsAcceptable( password ) )
			throw new StoreException( 400, $"Passwords must be {PasswordHasher.MinimumLength}-{PasswordHasher.MaximumLength} characters." );
	}

	/// <summary>Call inside the lock: the name is free (stored accounts only) and the server has room.</summary>
	private void CheckRegistration( string key )
	{
		if ( accounts.ContainsKey( key ) )
			throw new StoreException( 409, "There is already a player of that name." );
		if ( accounts.Count >= options.MaximumAccounts )
			throw new StoreException( 503, "This server has reached its account limit." );
	}

	public LoginOutcome Login( string name, string password, string? code = null )
	{
		AccountRecord? account;
		string? salt, hash;
		lock ( gate )
		{
			account = accounts.GetValueOrDefault( OnlineText.NormalizeName( name ?? "" ) );
			(salt, hash) = (account?.Salt, account?.Hash);
		}
		// Hash anyway for unknown names so timing does not reveal which names exist.
		var valid = PasswordHasher.Verify( password ?? "", salt ?? "AAAAAAAAAAAAAAAAAAAAAA==", hash ?? "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", account?.Iterations ?? options.PasswordIterations );
		if ( account == null || !valid )
			throw new StoreException( 401, "Invalid username or password." );
		if ( IsBanned( account.Name ) )
			throw new StoreException( 403, "This player is banned from this server." );
		lock ( gate )
		{
			// The password was checked outside the lock: a recovery or deletion in between makes it stale.
			if ( !ReferenceEquals( accounts.GetValueOrDefault( account.Key ), account ) || account.Salt != salt || account.Hash != hash )
				throw new StoreException( 401, "Invalid username or password." );
			ApplyTotpReset( account );
			if ( account.TotpEnroll )
				return new LoginOutcome( null, Enroll( account.Name, account.Key, account.Salt, account.Hash, account.Iterations, newAccount: false ) );
			if ( account.TotpSecret != null )
			{
				var failures = codeFailures.TryGetValue( account.Key, out var list ) ? list : codeFailures[account.Key] = new List<DateTimeOffset>();
				VerifyCode( account.Key, Totp.FromBase32( account.TotpSecret ), code, failures );
			}
			return new LoginOutcome( CreateSession( account ), null );
		}
	}

	/// <summary>
	/// Starts a registration with two-step login: checks name and password, then keeps the account in memory with a new
	/// authenticator secret until <see cref="ConfirmEnrollment"/> receives a code from it.
	/// </summary>
	public TotpEnrollment BeginRegistration( string name, string password )
	{
		ValidateRegistration( name, password );
		var key = OnlineText.NormalizeName( name );
		lock ( gate )
			CheckRegistration( key );
		PendingEnrollment? waiting;
		lock ( gate )
		{
			PruneEnrollments();
			waiting = enrollments.GetValueOrDefault( key );
		}
		// A waiting setup is replaced only by the same password (the player asking again), not by a stranger.
		if ( waiting != null && !PasswordHasher.Verify( password, waiting.Salt, waiting.Hash, waiting.Iterations ) )
			throw new StoreException( 409, "This name is being registered right now; try again in a few minutes." );
		var (salt, hash) = PasswordHasher.Hash( password, options.PasswordIterations );
		lock ( gate )
		{
			CheckRegistration( key );
			if ( enrollments.TryGetValue( key, out var current ) && !ReferenceEquals( current, waiting ) )
				throw new StoreException( 409, "This name is being registered right now; try again in a few minutes." );
			return Enroll( name, key, salt, hash, options.PasswordIterations, newAccount: true );
		}
	}

	/// <summary>
	/// Finishes a registration or a reset enrollment with its ticket and the first authenticator code; returns a session.
	/// A wrong ticket counts as no setup at all, so only whoever started the setup can confirm it or use up its attempts.
	/// </summary>
	public SessionToken ConfirmEnrollment( string name, string ticket, string code, string? emailCode = null )
	{
		var key = OnlineText.NormalizeName( name ?? "" );
		var ticketHash = PasswordHasher.HashToken( ticket ?? "" );
		lock ( gate )
		{
			PruneEnrollments();
			if ( !enrollments.TryGetValue( key, out var pending )
				|| !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals( System.Text.Encoding.ASCII.GetBytes( pending.TicketHash ), System.Text.Encoding.ASCII.GetBytes( ticketHash ) ) )
				throw new StoreException( 400, "No two-step setup is waiting for this name; register or log in again." );
			// A new account on a server that requires e-mail needs the code sent to its address first.
			if ( pending.NewAccount && (options.RequireEmail || pending.Email != null) )
				VerifyEmailCode( pending, emailCode );
			VerifyCode( key, pending.Secret, code, pending.Failures );
			AccountRecord account;
			if ( pending.NewAccount )
			{
				CheckRegistration( key );
				if ( pending.Email != null && FindByEmail( pending.Email ) != null )
					throw new StoreException( 409, "This e-mail address already belongs to an account." );
				account = new AccountRecord { Name = pending.Name, Key = key, Salt = pending.Salt, Hash = pending.Hash, Iterations = pending.Iterations, CreatedUtc = clock(), Email = pending.Email };
				accounts.Add( key, account );
			}
			else if ( !accounts.TryGetValue( key, out account! ) || account.Salt != pending.Salt || !account.TotpEnroll )
			{
				enrollments.Remove( key );
				throw new StoreException( 400, "This account changed; log in again." );
			}
			account.TotpSecret = Totp.ToBase32( pending.Secret );
			account.TotpEnroll = false;
			enrollments.Remove( key );
			Save( "accounts.json", accounts.Values.ToList() );
			return CreateSession( account );
		}
	}

	/// <summary>
	/// Stores <paramref name="email"/> on a waiting setup and returns the e-mail with its new code (the caller sends it
	/// outside the lock). Needs the setup's ticket; at most <see cref="MaximumEmailsPerHour"/> codes per setup.
	/// </summary>
	public EmailMessage PrepareEnrollmentEmail( string name, string ticket, string email, string? language )
	{
		var key = OnlineText.NormalizeName( name ?? "" );
		var address = NormalizeEmail( email ) ?? throw new StoreException( 400, "Enter a valid e-mail address." );
		var ticketHash = PasswordHasher.HashToken( ticket ?? "" );
		lock ( gate )
		{
			PruneEnrollments();
			if ( !enrollments.TryGetValue( key, out var pending ) || !pending.NewAccount
				|| !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals( System.Text.Encoding.ASCII.GetBytes( pending.TicketHash ), System.Text.Encoding.ASCII.GetBytes( ticketHash ) ) )
				throw new StoreException( 400, "No two-step setup is waiting for this name; register or log in again." );
			if ( FindByEmail( address ) != null )
				throw new StoreException( 409, "This e-mail address already belongs to an account." );
			var now = clock();
			if ( !CountEmail( now, key, address ) )
				throw new StoreException( 429, "Too many e-mails; try again later." );
			var code = NewEmailCode();
			// The setup lives at least as long as the code the e-mail promises.
			if ( pending.Expires < now + EmailCodeLifetime )
				enrollments[key] = pending = pending with { Expires = now + EmailCodeLifetime };
			pending.Email = address;
			pending.EmailCodeHash = PasswordHasher.HashToken( code );
			pending.EmailFailures = 0;
			return EmailTexts.Compose( EmailKind.VerifyAddress, language, address, code, pending.Name, options.EmailReplyTo ?? "" );
		}
	}

	/// <summary>Call inside the lock.</summary>
	private static void VerifyEmailCode( PendingEnrollment pending, string? emailCode )
	{
		if ( pending.Email == null || pending.EmailCodeHash == null )
			throw new StoreException( 400, "Enter your e-mail address and the code sent to it.", ApiErrorReasons.EmailRequired );
		if ( pending.EmailFailures >= MaximumEmailCodeFailures )
			throw new StoreException( 429, "Too many wrong codes; send a new code.", ApiErrorReasons.EmailCodeInvalid );
		if ( string.IsNullOrWhiteSpace( emailCode ) || !SameHash( pending.EmailCodeHash, PasswordHasher.HashToken( emailCode.Trim() ) ) )
		{
			pending.EmailFailures++;
			throw new StoreException( 401, "The code from the e-mail is wrong.", ApiErrorReasons.EmailCodeInvalid );
		}
	}

	/// <summary>
	/// Starts a recovery for the account named <paramref name="nameOrEmail"/> (a player name or its e-mail address). Returns
	/// the e-mail to send, or null when there is nothing to send (no such account, no address, too many e-mails); the
	/// caller answers the same either way, so the answer does not tell which names or addresses exist.
	/// </summary>
	public EmailMessage? PrepareRecovery( string nameOrEmail, string? language )
	{
		lock ( gate )
		{
			var account = FindByNameOrEmail( nameOrEmail );
			if ( account?.Email == null || IsBanned( account.Name ) )
				return null;
			var now = clock();
			if ( !CountEmail( now, account.Key, account.Email ) )
				return null;
			var code = NewEmailCode();
			recoveryCodes[account.Key] = new RecoveryCode { CodeHash = PasswordHasher.HashToken( code ), Expires = now + EmailCodeLifetime };
			return EmailTexts.Compose( EmailKind.Recovery, language, account.Email, code, account.Name, options.EmailReplyTo ?? "" );
		}
	}

	/// <summary>
	/// Finishes a recovery: a fresh code from the e-mail sets a new password, and with <paramref name="resetTwoStep"/> the next
	/// login sets up two-step login again. Every session ends. Returns the account (whose chat the caller disconnects) and
	/// the notice e-mail to send.
	/// </summary>
	public (AccountRecord Account, EmailMessage Notice) ConfirmRecovery( string nameOrEmail, string code, string newPassword, bool resetTwoStep, string? language )
	{
		if ( !PasswordHasher.IsAcceptable( newPassword ) )
			throw new StoreException( 400, $"Passwords must be {PasswordHasher.MinimumLength}-{PasswordHasher.MaximumLength} characters." );
		var (salt, hash) = PasswordHasher.Hash( newPassword, options.PasswordIterations );
		lock ( gate )
		{
			var account = FindByNameOrEmail( nameOrEmail );
			if ( account == null || !recoveryCodes.TryGetValue( account.Key, out var recovery ) || recovery.Expires <= clock() )
				throw new StoreException( 400, "This code is wrong or no longer valid; ask for a new one.", ApiErrorReasons.EmailCodeInvalid );
			if ( string.IsNullOrWhiteSpace( code ) || !SameHash( recovery.CodeHash, PasswordHasher.HashToken( code.Trim() ) ) )
			{
				// A few wrong guesses use the code up.
				if ( ++recovery.Failures >= MaximumEmailCodeFailures )
					recoveryCodes.Remove( account.Key );
				throw new StoreException( 400, "This code is wrong or no longer valid; ask for a new one.", ApiErrorReasons.EmailCodeInvalid );
			}
			recoveryCodes.Remove( account.Key );
			account.Salt = salt;
			account.Hash = hash;
			account.Iterations = options.PasswordIterations;
			if ( resetTwoStep && account.TotpSecret != null )
			{
				account.TotpSecret = null;
				account.TotpEnroll = true;
				lastCodeSteps.Remove( account.Key );
				Save( "totp-steps.json", lastCodeSteps );
			}
			codeFailures.Remove( account.Key );
			enrollments.Remove( account.Key );
			foreach ( var session in sessions.Where( pair => pair.Value.Key == account.Key ).Select( pair => pair.Key ).ToList() )
				sessions.Remove( session );
			SaveSessions();
			Save( "accounts.json", accounts.Values.ToList() );
			var kind = resetTwoStep && account.TotpEnroll ? EmailKind.PasswordAndTwoStepChanged : EmailKind.PasswordChanged;
			return (account, EmailTexts.Compose( kind, language, account.Email!, account.Name, account.Name, options.EmailReplyTo ?? "" ));
		}
	}

	/// <summary>
	/// Call inside the lock. Counts one e-mail for the player name <paramref name="key"/> and for <paramref name="address"/>;
	/// false (and nothing counted) when either already had <see cref="MaximumEmailsPerHour"/> in the last hour.
	/// </summary>
	private bool CountEmail( DateTimeOffset now, string key, string address )
	{
		foreach ( var stale in emailSends.Where( pair => pair.Value.All( time => time <= now - TimeSpan.FromHours( 1 ) ) ).Select( pair => pair.Key ).ToList() )
			emailSends.Remove( stale );
		var lists = new[] { "name:" + key, "address:" + address }
			.Select( entry => emailSends.TryGetValue( entry, out var list ) ? list : emailSends[entry] = new List<DateTimeOffset>() ).ToList();
		foreach ( var list in lists )
			list.RemoveAll( time => time <= now - TimeSpan.FromHours( 1 ) );
		if ( lists.Any( list => list.Count >= MaximumEmailsPerHour ) )
			return false;
		foreach ( var list in lists )
			list.Add( now );
		return true;
	}

	/// <summary>Call inside the lock.</summary>
	private AccountRecord? FindByNameOrEmail( string? nameOrEmail )
	{
		var text = (nameOrEmail ?? "").Trim();
		if ( text.Contains( '@' ) )
			return NormalizeEmail( text ) is { } address ? FindByEmail( address ) : null;
		return accounts.GetValueOrDefault( OnlineText.NormalizeName( text ) );
	}

	/// <summary>Call inside the lock.</summary>
	private AccountRecord? FindByEmail( string address ) =>
		accounts.Values.FirstOrDefault( account => string.Equals( account.Email, address, StringComparison.Ordinal ) );

	/// <summary>A trimmed, lower-case address with one '@', a dot in the domain and no spaces or control characters; else null.</summary>
	public static string? NormalizeEmail( string? email )
	{
		var text = (email ?? "").Trim().ToLowerInvariant();
		var at = text.IndexOf( '@' );
		if ( text.Length is 0 or > MaximumEmailLength || at <= 0 || at != text.LastIndexOf( '@' ) || at == text.Length - 1
			|| !text[(at + 1)..].Contains( '.' ) || text.EndsWith( '.' ) || text.Any( c => char.IsWhiteSpace( c ) || char.IsControl( c ) || c is '<' or '>' or ',' or ';' or '"' ) )
			return null;
		return text;
	}

	private static string NewEmailCode() => System.Security.Cryptography.RandomNumberGenerator.GetInt32( 0, 100_000_000 ).ToString( "D8", System.Globalization.CultureInfo.InvariantCulture );

	private static bool SameHash( string a, string b ) =>
		System.Security.Cryptography.CryptographicOperations.FixedTimeEquals( System.Text.Encoding.ASCII.GetBytes( a ), System.Text.Encoding.ASCII.GetBytes( b ) );

	/// <summary>Call inside the lock.</summary>
	private TotpEnrollment Enroll( string name, string key, string salt, string hash, int iterations, bool newAccount )
	{
		PruneEnrollments();
		if ( enrollments.Count >= MaximumPendingEnrollments && !enrollments.ContainsKey( key ) )
			enrollments.Remove( enrollments.MinBy( pair => pair.Value.Expires ).Key );
		var secret = Totp.NewSecret();
		var (ticket, ticketHash) = PasswordHasher.NewToken();
		var expires = clock() + EnrollmentLifetime;
		// A newer setup for the same name replaces the older one.
		enrollments[key] = new PendingEnrollment( name, key, salt, hash, iterations, secret, ticketHash, expires, newAccount );
		var base32 = Totp.ToBase32( secret );
		return new TotpEnrollment( name, ticket, base32, Totp.Uri( options.ServerName, name, base32 ), expires, newAccount && options.RequireEmail ? true : null );
	}

	private void PruneEnrollments()
	{
		foreach ( var expired in enrollments.Where( pair => pair.Value.Expires <= clock() ).Select( pair => pair.Key ).ToList() )
			enrollments.Remove( expired );
	}

	/// <summary>
	/// Call inside the lock. Throws unless <paramref name="code"/> is a fresh code of <paramref name="secret"/>; wrong codes
	/// go into <paramref name="failures"/> (per account for logins, per setup for enrollments).
	/// </summary>
	private void VerifyCode( string key, byte[] secret, string? code, List<DateTimeOffset> failures )
	{
		if ( Totp.Normalize( code ) == null )
			throw new StoreException( 401, "Enter the code from your authenticator app.", ApiErrorReasons.TotpRequired );
		var now = clock();
		failures.RemoveAll( time => time <= now - CodeFailureWindow );
		foreach ( var stale in codeFailures.Where( pair => pair.Value != failures && pair.Value.All( time => time <= now - CodeFailureWindow ) ).Select( pair => pair.Key ).ToList() )
			codeFailures.Remove( stale );
		if ( failures.Count >= MaximumCodeFailures )
			throw new StoreException( 429, "Too many wrong codes; try again later.", ApiErrorReasons.TotpInvalid );
		var step = Totp.Verify( secret, code, now, lastCodeSteps.GetValueOrDefault( key, long.MinValue ) );
		if ( step == null )
		{
			failures.Add( now );
			throw new StoreException( 401, "The code is wrong or was already used.", ApiErrorReasons.TotpInvalid );
		}
		lastCodeSteps[key] = step.Value;
		Save( "totp-steps.json", lastCodeSteps );
		failures.Clear();
		codeFailures.Remove( key );
	}

	/// <summary>
	/// Call inside the lock. Applies an operator request file in <see cref="TotpResetDirectory"/> naming this account.
	/// Only a request written after the account was made and after its last reset counts, so a request left from a deleted
	/// account of the same name, or one that could not be deleted, is never applied. The reset is saved before the request
	/// is deleted: a crash in between leaves a stale request, not a lost one.
	/// </summary>
	private void ApplyTotpReset( AccountRecord account )
	{
		if ( account.TotpSecret == null )
			return;
		string[] files;
		try { files = Directory.GetFiles( Path.Combine( Root, TotpResetDirectory ) ); }
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException ) { return; }
		var notBefore = account.TotpResetUtc is { } last && last > account.CreatedUtc ? last : account.CreatedUtc;
		foreach ( var file in files )
		{
			string request;
			try
			{
				var info = new FileInfo( file );
				var written = new DateTimeOffset( info.LastWriteTimeUtc, TimeSpan.Zero );
				request = $"{info.Name}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
				if ( info.Length > MaximumResetFileBytes || written <= notBefore || request == account.TotpResetRequest
					|| OnlineText.NormalizeName( File.ReadAllText( file ).Trim() ) != account.Key )
					continue;
			}
			catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
			{
				continue;
			}
			account.TotpSecret = null;
			account.TotpEnroll = true;
			account.TotpResetUtc = clock();
			account.TotpResetRequest = request;
			lastCodeSteps.Remove( account.Key );
			Save( "accounts.json", accounts.Values.ToList() );
			Save( "totp-steps.json", lastCodeSteps );
			try { File.Delete( file ); }
			catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException ) { }
			return;
		}
	}

	/// <summary>Call inside the lock.</summary>
	private SessionToken CreateSession( AccountRecord account )
	{
		var (token, tokenHash) = PasswordHasher.NewToken();
		var expires = clock() + options.SessionLifetime;
		foreach ( var expired in sessions.Where( pair => pair.Value.Expires <= clock() ).Select( pair => pair.Key ).ToList() )
			sessions.Remove( expired );
		var own = sessions.Where( pair => pair.Value.Key == account.Key ).OrderBy( pair => pair.Value.Expires ).ToList();
		foreach ( var old in own.Take( Math.Max( 0, own.Count - options.MaximumSessionsPerPlayer + 1 ) ) )
			sessions.Remove( old.Key );
		sessions[tokenHash] = (account.Key, expires);
		SaveSessions();
		return new SessionToken( account.Name, token, expires );
	}

	public AccountRecord? Authenticate( string? token )
	{
		if ( string.IsNullOrEmpty( token ) || token.Length > 128 )
			return null;
		var tokenHash = PasswordHasher.HashToken( token );
		lock ( gate )
		{
			if ( !sessions.TryGetValue( tokenHash, out var session ) )
				return null;
			if ( session.Expires <= clock() || !accounts.TryGetValue( session.Key, out var account ) || IsBanned( account.Name ) )
			{
				sessions.Remove( tokenHash );
				SaveSessions();
				return null;
			}
			return account;
		}
	}

	public void Logout( string token )
	{
		lock ( gate )
		{
			if ( sessions.Remove( PasswordHasher.HashToken( token ) ) )
				SaveSessions();
		}
	}

	/// <summary>
	/// [EXT:ONLINE-055] Deletes a player and what the server holds about them (docs/SERVER.md): the
	/// account, its sessions, its published parks, the postcards in its inbox, and its traces in other
	/// players' buddy lists and in park visitor and vote lists. Reports they made name them as a
	/// deleted player; reports about them are kept for moderation. Postcards they already sent stay
	/// with their recipients. The name becomes free again.
	/// </summary>
	public void DeleteAccount( AccountRecord account, string password )
	{
		if ( !PasswordHasher.Verify( password ?? "", account.Salt, account.Hash, account.Iterations ) )
			throw new StoreException( 401, "Invalid username or password." );
		lock ( gate )
		{
			if ( !accounts.Remove( account.Key ) )
				throw new StoreException( 404, "There is no such player." );
			enrollments.Remove( account.Key );
			recoveryCodes.Remove( account.Key );
			if ( lastCodeSteps.Remove( account.Key ) )
				Save( "totp-steps.json", lastCodeSteps );
			codeFailures.Remove( account.Key );
			foreach ( var session in sessions.Where( pair => pair.Value.Key == account.Key ).Select( pair => pair.Key ).ToList() )
				sessions.Remove( session );
			SaveSessions();
			foreach ( var other in accounts.Values )
				other.Buddies.Remove( account.Key );
			foreach ( var park in parks.Values.Where( park => park.OwnerKey == account.Key ).ToList() )
			{
				parks.Remove( park.Id );
				File.Delete( ParkFile( park.Id, ParkPackage.FileExtension ) );
				File.Delete( ParkFile( park.Id, ".png" ) );
			}
			foreach ( var park in parks.Values )
			{
				park.Visitors.Remove( account.Key );
				park.LastVoteDay.Remove( account.Key );
			}
			foreach ( var card in postcards.Values.Where( card => card.RecipientKey == account.Key ).ToList() )
			{
				postcards.Remove( card.Id );
				File.Delete( PostcardFile( card.Id ) );
			}
			Save( "accounts.json", accounts.Values.ToList() );
			Save( "parks.json", parks.Values.ToList() );
			Save( "postcards.json", postcards.Values.ToList() );
			AnonymizeReporter( account.Name );
		}
	}

	public const string DeletedPlayer = "(deleted player)";

	private void AnonymizeReporter( string name )
	{
		var file = Path.Combine( Root, "reports.jsonl" );
		if ( !File.Exists( file ) )
			return;
		var key = OnlineText.NormalizeName( name );
		var lines = File.ReadAllLines( file ).Select( line =>
		{
			var report = JsonSerializer.Deserialize<ReportRecord>( line, LineOptions );
			return report != null && OnlineText.NormalizeName( report.Reporter ) == key
				? JsonSerializer.Serialize( report with { Reporter = DeletedPlayer }, LineOptions )
				: line;
		} );
		AtomicFile.Write( file, System.Text.Encoding.UTF8.GetBytes( string.Join( "\n", lines ) + "\n" ) );
	}

	/// <summary>Adds or removes a buddy; returns true when added.</summary>
	public bool ToggleBuddy( AccountRecord account, AccountRecord buddy )
	{
		lock ( gate )
		{
			bool added;
			if ( account.Buddies.Remove( buddy.Key ) )
				added = false;
			else
			{
				if ( account.Buddies.Count >= options.MaximumBuddies )
					throw new StoreException( 400, "Buddy list is full." );
				account.Buddies.Add( buddy.Key );
				added = true;
			}
			Save( "accounts.json", accounts.Values.ToList() );
			return added;
		}
	}

	public IReadOnlyList<string> BuddyNames( AccountRecord account )
	{
		lock ( gate )
			return account.Buddies.Select( key => accounts.GetValueOrDefault( key )?.Name ).OfType<string>().ToList();
	}

	// ---- parks ----

	public ParkRecord Publish( AccountRecord owner, ParkPackage package, byte[] packageBytes, bool showOnWebsite = false, int cityId = 0 )
	{
		lock ( gate )
		{
			if ( parks.Values.Count( park => park.OwnerKey == owner.Key ) >= options.MaximumParksPerPlayer )
				// [DATA:Language/English/UITEXT.str:221] the original's limit message (the limit itself: FAQ, S2)
				throw new StoreException( 409, "You cannot publish any more parks, you have reached your personal limit.\nPlease un-publish one or more parks before publishing your new park." );
			var park = new ParkRecord
			{
				Id = NewId(), OwnerKey = owner.Key, Author = owner.Name, Name = package.Manifest.Park.Name,
				Description = package.Manifest.Park.Description, Level = package.Manifest.Park.Level, Language = package.Manifest.Game.Language,
				PackageId = package.Manifest.PackageId, PublishedUtc = clock(), Bytes = packageBytes.Length, HasThumbnail = package.Thumbnail != null,
				ShowOnWebsite = showOnWebsite, CityId = cityId,
			};
			AtomicFile.Write( ParkFile( park.Id, ParkPackage.FileExtension ), packageBytes );
			if ( package.Thumbnail != null )
				AtomicFile.Write( ParkFile( park.Id, ".png" ), package.Thumbnail );
			parks.Add( park.Id, park );
			Save( "parks.json", parks.Values.ToList() );
			return park;
		}
	}

	public ParkRecord? FindPark( string id )
	{
		lock ( gate )
			return parks.TryGetValue( id, out var park ) && !options.HiddenParks.Contains( id ) ? park : null;
	}

	public IReadOnlyList<ParkRecord> ListParks( string? search, string? author, string sort, int? cityId = null )
	{
		lock ( gate )
		{
			IEnumerable<ParkRecord> query = parks.Values.Where( park => !options.HiddenParks.Contains( park.Id ) );
			if ( !string.IsNullOrWhiteSpace( search ) )
				query = query.Where( park => park.Name.Contains( search, StringComparison.OrdinalIgnoreCase ) );
			if ( cityId is { } city )
				query = query.Where( park => park.CityId == city );
			if ( !string.IsNullOrWhiteSpace( author ) )
				query = query.Where( park => OnlineText.NormalizeName( park.Author ) == OnlineText.NormalizeName( author ) );
			query = sort switch
			{
				"visits" => query.OrderByDescending( park => park.Visits ).ThenByDescending( park => park.PublishedUtc ),
				"votes" => query.OrderByDescending( park => park.Votes ).ThenByDescending( park => park.PublishedUtc ),
				"name" => query.OrderBy( park => park.Name, StringComparer.OrdinalIgnoreCase ),
				_ => query.OrderByDescending( park => park.PublishedUtc ),
			};
			return query.ToList();
		}
	}

	/// <summary>
	/// The most-voted parks whose authors chose to show them on the website: votes, then visits, then the earlier
	/// published first.
	/// </summary>
	public IReadOnlyList<ParkRecord> WebsiteParks( int count )
	{
		lock ( gate )
			return parks.Values.Where( park => park.ShowOnWebsite && !options.HiddenParks.Contains( park.Id ) )
				.OrderByDescending( park => park.Votes ).ThenByDescending( park => park.Visits ).ThenBy( park => park.PublishedUtc ).ThenBy( park => park.Id, StringComparer.Ordinal )
				.Take( count ).ToList();
	}

	/// <summary>Published, not hidden parks per city id.</summary>
	public IReadOnlyDictionary<int, int> ParksPerCity()
	{
		lock ( gate )
			return parks.Values.Where( park => !options.HiddenParks.Contains( park.Id ) ).GroupBy( park => park.CityId ).ToDictionary( group => group.Key, group => group.Count() );
	}

	public string ParkFile( string id, string extension ) => Path.Combine( Root, "parks", id + extension );

	public void Unpublish( AccountRecord owner, string id )
	{
		lock ( gate )
		{
			if ( !parks.TryGetValue( id, out var park ) )
				throw new StoreException( 404, "Park ID is invalid." );
			if ( park.OwnerKey != owner.Key )
				throw new StoreException( 403, "Only the creator can un-publish a park." );
			parks.Remove( id );
			RefreshRanks();
			Save( "parks.json", parks.Values.ToList() );
			File.Delete( ParkFile( id, ParkPackage.FileExtension ) );
			File.Delete( ParkFile( id, ".png" ) );
		}
	}

	/// <summary>
	/// One visit report: counts one visit for the park. The client sends exactly one report per visit (it
	/// downloads the park and enters it), like the original client, which sent its MarkParkVisited message once
	/// per park entry — every visit is what earned the owner Platinum Tickets, voted or not
	/// (docs/reverse/ONLINE-VISIT.md). <see cref="ParkRecord.Visitors"/> stays the distinct-visitor list the
	/// park summaries mark "visited before" with.
	/// </summary>
	// [EXT:ONLINE-075] the visit counter counts accepted visit reports; a ticket award hooked here sees one call per visit.
	// [EXT:online-visit-count-guard] every visit earned the owner Platinum Tickets (FAQ, S2); OpenTPW's guard: an owner's
	// visits to their own park never count, and one visitor counts at most once per UTC day per park, so tickets cannot be
	// farmed by re-entering a park. The original service's guard against repeated or self visits is not recovered.
	public CounterResult Visit( AccountRecord visitor, string id )
	{
		lock ( gate )
		{
			var park = FindPark( id ) ?? throw new StoreException( 404, "Park ID is invalid." );
			if ( park.OwnerKey != visitor.Key && !(park.LastVisitDay.TryGetValue( visitor.Key, out var day ) && day == Today) )
			{
				if ( !park.Visitors.Contains( visitor.Key ) )
					park.Visitors.Add( visitor.Key );
				park.LastVisitDay[visitor.Key] = Today;
				park.Visits++;
				Save( "parks.json", parks.Values.ToList() );
			}
			return new CounterResult( park.Visits, park.Votes, VotesLeft( visitor ) );
		}
	}

	private int VotesLeft( AccountRecord account ) => account.VoteDay == Today ? Math.Max( 0, options.MaximumVotesPerDay - account.VotesToday ) : options.MaximumVotesPerDay;

	public CounterResult Vote( AccountRecord voter, string id )
	{
		lock ( gate )
		{
			var park = FindPark( id ) ?? throw new StoreException( 404, "Park ID is invalid." );
			if ( park.OwnerKey == voter.Key )
				throw new StoreException( 403, "You cannot vote for your own park." );
			if ( VotesLeft( voter ) == 0 )
				throw new StoreException( 429, "You have exceeded your maximum number of votes for today." );
			if ( park.LastVoteDay.TryGetValue( voter.Key, out var day ) && day == Today )
				throw new StoreException( 409, "You already voted for this park today." );
			if ( voter.VoteDay != Today )
			{
				voter.VoteDay = Today;
				voter.VotesToday = 0;
			}
			voter.VotesToday++;
			park.LastVoteDay[voter.Key] = Today;
			park.Votes++;
			RefreshRanks();
			Save( "parks.json", parks.Values.ToList() );
			Save( "accounts.json", accounts.Values.ToList() );
			return new CounterResult( park.Visits, park.Votes, VotesLeft( voter ) );
		}
	}

	public ParkSummary Summarize( ParkRecord park, AccountRecord? viewer, int playersAtPark )
	{
		lock ( gate )
		{
			return new ParkSummary( park.Id, park.Name, park.Description, park.Author, park.Level, park.Language, park.PublishedUtc, park.Visits, park.Votes,
				viewer != null && park.Visitors.Contains( viewer.Key ), viewer != null && park.LastVoteDay.ContainsKey( viewer.Key ), playersAtPark, park.HasThumbnail, park.Bytes, park.CityId );
		}
	}

	// ---- the original website's lists (docs/reverse/ONLINE-SOURCES.md S1) ----

	/// <summary>The chart order of the original's top-parks list: by votes (S2), then visits, then the earlier published.</summary>
	private static IEnumerable<ParkRecord> ChartOrder( IEnumerable<ParkRecord> parks ) =>
		parks.OrderByDescending( park => park.Votes ).ThenByDescending( park => park.Visits ).ThenBy( park => park.PublishedUtc ).ThenBy( park => park.Id, StringComparer.Ordinal );

	/// <summary>
	/// Call inside the lock after a vote or an un-publish — the changes that move the votes chart: recomputes every
	/// published park's place in it and remembers each park's best place ("highest rank", S1). A park that has just
	/// been passed does not lose its recorded best place, and a park's current place is always computed fresh for its
	/// page, so a publish needs no refresh.
	/// </summary>
	private void RefreshRanks()
	{
		var rank = 0;
		foreach ( var park in ChartOrder( parks.Values.Where( park => !options.HiddenParks.Contains( park.Id ) ) ) )
		{
			rank++;
			if ( park.HighestRank == 0 || rank < park.HighestRank )
				park.HighestRank = rank;
		}
	}

	/// <summary>A player's Platinum Tickets: one per recorded visit of their published parks (manual p. 62: a visit earns the owner Platinum Tickets, voting or not).</summary>
	public int PlatinumTickets( string ownerKey )
	{
		lock ( gate )
			return parks.Values.Where( park => park.OwnerKey == ownerKey ).Sum( park => park.Visits );
	}

	/// <summary>
	/// One player's published parks with their current and highest chart place — the original website's per-player
	/// "My Theme Park" page (S1: park name, visitors, votes, publish date, rank, highest rank). Without
	/// <paramref name="viewer"/> (a public request) only the parks their author chose to show on the website are
	/// listed, like the server's other public lists.
	/// </summary>
	public (string Name, int PlatinumTickets, IReadOnlyList<(ParkRecord Park, int Rank)> Parks) PlayerParks( string name, AccountRecord? viewer )
	{
		lock ( gate )
		{
			var key = OnlineText.NormalizeName( name );
			var ranks = ChartOrder( parks.Values.Where( park => !options.HiddenParks.Contains( park.Id ) ) )
				.Select( ( park, index ) => (park, Rank: index + 1) )
				.ToDictionary( entry => entry.park.Id, entry => entry.Rank );
			var own = this.parks.Values
				.Where( park => park.OwnerKey == key && !options.HiddenParks.Contains( park.Id ) && (viewer != null || park.ShowOnWebsite) )
				.OrderBy( park => park.PublishedUtc ).Select( park => (park, ranks.GetValueOrDefault( park.Id )) ).ToList();
			var author = own.Count > 0 ? own[0].park.Author : key;
			// A public page counts the Platinum Tickets of the parks it can list, so the total hides the rest.
			var platinum = viewer == null ? own.Sum( entry => entry.park.Visits ) : PlatinumTickets( key );
			return (author, platinum, own);
		}
	}

	/// <summary>The top parks of the public chart (S1: rank, park name, player): only parks their authors show on the website.</summary>
	public IReadOnlyList<(int Rank, string Name, string Player)> TopParks( int count )
	{
		lock ( gate )
			return ChartOrder( parks.Values.Where( park => park.ShowOnWebsite && !options.HiddenParks.Contains( park.Id ) ) )
				.Take( count ).Select( ( park, index ) => (index + 1, park.Name, park.Author) ).ToList();
	}

	/// <summary>
	/// The public player charts (S1: Top 10 players by their parks' votes; Richest players by Platinum Tickets). A
	/// player appears with only the parks they show on the website, so the chart is public without exposing anyone who
	/// did not opt in.
	/// </summary>
	public IReadOnlyList<(string Name, int Score)> PlayerChart( int count, bool byPlatinum )
	{
		lock ( gate )
		{
			var players = parks.Values.Where( park => park.ShowOnWebsite && !options.HiddenParks.Contains( park.Id ) )
				.GroupBy( park => park.Author, StringComparer.OrdinalIgnoreCase )
				.Select( group => (Name: group.Key, Score: byPlatinum ? group.Sum( park => park.Visits ) : group.Sum( park => park.Votes )) )
				.OrderByDescending( entry => entry.Score ).ThenBy( entry => entry.Name, StringComparer.OrdinalIgnoreCase )
				.Take( count ).ToList();
			return players;
		}
	}

	/// <summary>
	/// Player search by name (S1: the website searched by user name, first name or last name; OpenTPW accounts have
	/// one player name, so first and last names do not exist). At most 50 names; <c>more</c> reports that the search
	/// was too broad, as the original asked for more detail.
	/// </summary>
	// Without a login the search finds only players who show a park on the website, like the charts, so the public
	// endpoint cannot list every account name; a logged-in player searches all players.
	public (IReadOnlyList<string> Names, bool More) SearchPlayers( string search, AccountRecord? viewer )
	{
		const int maximum = 50;
		lock ( gate )
		{
			var shown = viewer != null ? null : parks.Values.Where( park => park.ShowOnWebsite && !options.HiddenParks.Contains( park.Id ) )
				.Select( park => park.OwnerKey ).ToHashSet();
			var names = accounts.Values
				.Where( account => (shown == null || shown.Contains( account.Key )) && account.Name.Contains( search, StringComparison.OrdinalIgnoreCase ) )
				.OrderBy( account => account.Name, StringComparer.OrdinalIgnoreCase ).Select( account => account.Name ).ToList();
			return (names.Take( maximum ).ToList(), names.Count > maximum);
		}
	}

	// ---- postcards ----

	public IReadOnlyList<PostcardSummary> Inbox( AccountRecord account )
	{
		lock ( gate )
		{
			return postcards.Values.Where( item => item.RecipientKey == account.Key ).OrderByDescending( item => item.SentUtc )
				.Select( item => new PostcardSummary( item.Id, item.From, item.Title, item.SentUtc, item.HasImage, item.ParkName ) ).ToList();
		}
	}

	public string OwnPostcardFile( AccountRecord account, string id )
	{
		lock ( gate )
		{
			if ( !postcards.TryGetValue( id, out var record ) || record.RecipientKey != account.Key )
				throw new StoreException( 404, "Postcard not found." );
			return PostcardFile( id );
		}
	}

	public void DeletePostcard( AccountRecord account, string id )
	{
		lock ( gate )
		{
			var file = OwnPostcardFile( account, id );
			postcards.Remove( id );
			Save( "postcards.json", postcards.Values.ToList() );
			File.Delete( file );
		}
	}

	private string PostcardFile( string id ) => Path.Combine( Root, "postcards", id + Postcard.FileExtension );

	// ---- moderation ----

	public void Report( AccountRecord reporter, string kind, string target, string reason )
	{
		var line = JsonSerializer.Serialize( new ReportRecord( clock(), reporter.Name, kind, target, reason ), LineOptions );
		lock ( gate )
			File.AppendAllText( Path.Combine( Root, "reports.jsonl" ), line + "\n" );
	}
}
