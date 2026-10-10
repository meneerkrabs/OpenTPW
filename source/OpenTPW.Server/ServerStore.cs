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
	public int Visits { get; set; }
	public int Votes { get; set; }
	public List<string> Visitors { get; set; } = new();
	/// <summary>Player key → UTC day (yyyy-MM-dd) of that player's last vote for this park.</summary>
	public Dictionary<string, string> LastVoteDay { get; set; } = new();
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
	public StoreException( int status, string message ) : base( message ) => Status = status;
	public int Status { get; }
}

/// <summary>
/// [EXT:ONLINE-052] File storage: JSON indexes (accounts, parks, postcards) rewritten atomically after
/// each change, blobs as files named by random ids, reports appended as JSON lines. One lock guards
/// all state; meant for community-sized servers. Sessions are kept as token hashes in sessions.json, so a restart
/// (a deploy) does not log players out.
/// </summary>
public sealed class ServerStore
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
	}

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
		if ( !options.AllowRegistration )
			throw new StoreException( 403, "Registration is closed on this server." );
		if ( !OnlineText.IsValidPlayerName( name ) )
			throw new StoreException( 400, $"Names must be {OnlineText.MinimumNameLength}-{OnlineText.MaximumNameLength} letters, digits, spaces, '_', '-' or '.'." );
		if ( !PasswordHasher.IsAcceptable( password ) )
			throw new StoreException( 400, $"Passwords must be {PasswordHasher.MinimumLength}-{PasswordHasher.MaximumLength} characters." );
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

	private void CheckRegistration( string key )
	{
		if ( accounts.ContainsKey( key ) )
			throw new StoreException( 409, "There is already a player of that name." );
		if ( accounts.Count >= options.MaximumAccounts )
			throw new StoreException( 503, "This server has reached its account limit." );
	}

	public SessionToken Login( string name, string password )
	{
		var account = FindAccount( name ?? "" );
		// Hash anyway for unknown names so timing does not reveal which names exist.
		var valid = PasswordHasher.Verify( password ?? "", account?.Salt ?? "AAAAAAAAAAAAAAAAAAAAAA==", account?.Hash ?? "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", account?.Iterations ?? options.PasswordIterations );
		if ( account == null || !valid )
			throw new StoreException( 401, "Invalid username or password." );
		if ( IsBanned( account.Name ) )
			throw new StoreException( 403, "This player is banned from this server." );
		var (token, tokenHash) = PasswordHasher.NewToken();
		var expires = clock() + options.SessionLifetime;
		lock ( gate )
		{
			foreach ( var expired in sessions.Where( pair => pair.Value.Expires <= clock() ).Select( pair => pair.Key ).ToList() )
				sessions.Remove( expired );
			var own = sessions.Where( pair => pair.Value.Key == account.Key ).OrderBy( pair => pair.Value.Expires ).ToList();
			foreach ( var old in own.Take( Math.Max( 0, own.Count - options.MaximumSessionsPerPlayer + 1 ) ) )
				sessions.Remove( old.Key );
			sessions[tokenHash] = (account.Key, expires);
			SaveSessions();
		}
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

	public ParkRecord Publish( AccountRecord owner, ParkPackage package, byte[] packageBytes, bool showOnWebsite = false )
	{
		lock ( gate )
		{
			if ( parks.Values.Count( park => park.OwnerKey == owner.Key ) >= options.MaximumParksPerPlayer )
				throw new StoreException( 409, "You cannot publish any more parks; un-publish one first." );
			var park = new ParkRecord
			{
				Id = NewId(), OwnerKey = owner.Key, Author = owner.Name, Name = package.Manifest.Park.Name,
				Description = package.Manifest.Park.Description, Level = package.Manifest.Park.Level, Language = package.Manifest.Game.Language,
				PackageId = package.Manifest.PackageId, PublishedUtc = clock(), Bytes = packageBytes.Length, HasThumbnail = package.Thumbnail != null,
				ShowOnWebsite = showOnWebsite,
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

	public IReadOnlyList<ParkRecord> ListParks( string? search, string? author, string sort )
	{
		lock ( gate )
		{
			IEnumerable<ParkRecord> query = parks.Values.Where( park => !options.HiddenParks.Contains( park.Id ) );
			if ( !string.IsNullOrWhiteSpace( search ) )
				query = query.Where( park => park.Name.Contains( search, StringComparison.OrdinalIgnoreCase ) );
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
			Save( "parks.json", parks.Values.ToList() );
			File.Delete( ParkFile( id, ParkPackage.FileExtension ) );
			File.Delete( ParkFile( id, ".png" ) );
		}
	}

	public CounterResult Visit( AccountRecord visitor, string id )
	{
		lock ( gate )
		{
			var park = FindPark( id ) ?? throw new StoreException( 404, "Park ID is invalid." );
			if ( !park.Visitors.Contains( visitor.Key ) )
			{
				park.Visitors.Add( visitor.Key );
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
				viewer != null && park.Visitors.Contains( viewer.Key ), viewer != null && park.LastVoteDay.ContainsKey( viewer.Key ), playersAtPark, park.HasThumbnail, park.Bytes );
		}
	}

	// ---- postcards ----

	public (string Id, IReadOnlyList<string> Delivered) SendPostcard( AccountRecord sender, Postcard card, byte[] cardBytes )
	{
		lock ( gate )
		{
			var recipients = card.Manifest.To.Select( name => accounts.GetValueOrDefault( OnlineText.NormalizeName( name ) ) ).OfType<AccountRecord>().Distinct().ToList();
			if ( recipients.Count == 0 )
				throw new StoreException( 404, "Email recipient unknown." );
			var delivered = new List<string>();
			foreach ( var recipient in recipients )
			{
				if ( postcards.Values.Count( item => item.RecipientKey == recipient.Key ) >= options.MaximumInboxPostcards )
					continue;
				var record = new PostcardRecord
				{
					Id = NewId(), RecipientKey = recipient.Key, From = sender.Name, Title = card.Manifest.Title, SentUtc = clock(),
					HasImage = card.Image != null, ParkName = card.Manifest.Park?.ParkName,
				};
				AtomicFile.Write( PostcardFile( record.Id ), cardBytes );
				postcards.Add( record.Id, record );
				delivered.Add( recipient.Name );
			}
			if ( delivered.Count == 0 )
				throw new StoreException( 409, "The recipients' inboxes are full." );
			Save( "postcards.json", postcards.Values.ToList() );
			return (card.Id.ToString( "N" ), delivered);
		}
	}

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
