namespace OpenTPW;

/// <summary>Application-owned earned identity; player-wide tickets have no theme.</summary>
internal sealed record EarnedTicketIdentity( string? Theme, GoldenTicketKind Kind );
/// <summary>The park that holds one global ticket and the record it won it with (the original's held byte and record, tp.exe 0x5AF940).</summary>
internal sealed record GlobalTicketHolder( GoldenTicketKind Kind, string Theme, long Record );
internal sealed record KnownTicketPurchase( int InfoId, int Cost );
internal enum TicketPurchaseOutcome { Purchased, AlreadyOwned, InsufficientTickets }

/// <param name="Cheated">[EXT:cheats-ticket-provenance] The earned identities a cheat awarded (a subset of <paramref name="Earned"/>); null when none was.</param>
internal sealed record PlayerTicketSnapshot( ParkGameMode Mode, int ExtraKeys, int SpentTickets, IReadOnlyList<EarnedTicketIdentity> Earned, IReadOnlyList<GlobalTicketHolder>? Holders = null,
	IReadOnlyList<EarnedTicketIdentity>? Cheated = null, IReadOnlyList<KnownTicketPurchase>? KnownPurchases = null );

/// <summary>
/// Own player-wide history, separate from native profiles and the legacy count-only PlayerProgress.
/// A new application ledger has zero earned tickets and zero extra keys. No lobby award or gate is implied.
/// </summary>
internal sealed class PlayerTicketLedger
{
	public const int MaximumIdentities = 4 * 6 + 4 + 2;
	private static readonly string[] Themes = { "jungle", "hallow", "fantasy", "space" };
	private readonly HashSet<EarnedTicketIdentity> earned = new();
	private readonly Dictionary<GoldenTicketKind, GlobalTicketHolder> holders = new();
	private readonly HashSet<EarnedTicketIdentity> cheated = new();
	private readonly Dictionary<int, KnownTicketPurchase> purchases = new();
	public ParkGameMode Mode { get; }
	public int ExtraKeys { get; }
	public int SpentTickets { get; private set; }
	public int EarnedTickets => earned.Count;
	public int AvailableTickets => EarnedTickets - SpentTickets;
	public int Keys => ExtraKeys + EarnedTickets / PlayerProgress.TicketsPerKey;

	public PlayerTicketLedger( ParkGameMode mode = ParkGameMode.FullSimulation )
	{
		if ( !Enum.IsDefined( mode ) ) throw new InvalidDataException( "Invalid player mode." );
		Mode = mode;
	}

	private PlayerTicketLedger( PlayerTicketSnapshot snapshot )
	{
		Mode = snapshot.Mode;
		ExtraKeys = snapshot.ExtraKeys;
		SpentTickets = snapshot.SpentTickets;
		earned.UnionWith( snapshot.Earned );
		cheated.UnionWith( snapshot.Cheated ?? Array.Empty<EarnedTicketIdentity>() );
		foreach ( var purchase in snapshot.KnownPurchases ?? Array.Empty<KnownTicketPurchase>() )
			purchases.Add( purchase.InfoId, purchase );
		foreach ( var holder in snapshot.Holders ?? Array.Empty<GlobalTicketHolder>() )
			holders[holder.Kind] = holder;
	}

	/// <summary>
	/// [EXT:cheats-ticket-provenance] The earned identities a cheat awarded (docs/CHEATS.md). They count toward the keys and the theme unlocks like any other, as the original's ledger counts them
	/// (its cheats write the same bytes as a win); the record only lets a later online profile leave them out.
	/// </summary>
	public IReadOnlyCollection<EarnedTicketIdentity> Cheated => cheated;

	/// <summary>Pure own-ledger foundation; live park purchases do not call this API yet. Info.Id retains the existing host identity semantics.</summary>
	public TicketPurchaseOutcome TryPurchase( int infoId, int cost )
	{
		if ( infoId < 0 || cost <= 0 ) throw new InvalidDataException( "Invalid ticket purchase." );
		if ( purchases.ContainsKey( infoId ) ) return TicketPurchaseOutcome.AlreadyOwned;
		if ( cost > AvailableTickets ) return TicketPurchaseOutcome.InsufficientTickets;
		purchases.Add( infoId, new( infoId, cost ) );
		SpentTickets += cost; // AvailableTickets bounds the sum by the earned identity count.
		return TicketPurchaseOutcome.Purchased;
	}

	public bool OwnsPurchasedItem( int infoId ) => purchases.ContainsKey( infoId );

	/// <summary>Whether a cheat awarded this park's or the player's ticket of <paramref name="kind"/>.</summary>
	public bool IsCheated( string theme, GoldenTicketKind kind ) => cheated.Contains( Identity( theme, kind ) );

	/// <summary>
	/// [EXT:cheats-ticket-provenance] Records that a cheat awarded the ticket: a local ticket is earned now if it was not and then marked as a cheat's; a global one was earned through
	/// <see cref="RecordGlobal"/> already, and is marked when that award earned it (<paramref name="globalAwarded"/>), not when it moved from another park. A ticket the player had earned before keeps
	/// its honest provenance. True when anything changed.
	/// </summary>
	public bool RecordCheated( string theme, GoldenTicketKind kind, bool globalAwarded )
	{
		var identity = Identity( theme, kind );
		if ( IsGlobal( kind ) )
			return globalAwarded && earned.Contains( identity ) && cheated.Add( identity );
		var added = RecordEarned( theme, kind );
		if ( added )
			cheated.Add( identity );
		return added;
	}

	/// <summary>The park that holds a global ticket, null for an unknown holder (an older file) or a ticket not earned.</summary>
	public GlobalTicketHolder? HolderOf( GoldenTicketKind kind ) => holders.GetValueOrDefault( kind );

	/// <summary>Six local kinds are theme-scoped; four globals and two secrets are player-wide.</summary>
	public bool RecordEarned( string theme, GoldenTicketKind kind )
	{
		var identity = Identity( theme, kind );
		if ( !earned.Add( identity ) )
			return false;
		if ( IsGlobal( kind ) )
			holders[kind] = new( kind, CanonicalTheme( theme ), 0 );
		return true;
	}

	/// <summary>
	/// A global ticket whose condition holds in a park with this record (tp.exe 0x5AF940): the first win earns it for the park; later, a park that does not hold it takes it from the holder
	/// when its record is above the holder's (the old park loses the ticket's held byte only, the count is player-wide and unchanged). A holder unknown from an older file is adopted by the
	/// first park that reports; an equal or lower record changes nothing.
	/// </summary>
	public GlobalTicketOutcome RecordGlobal( string theme, GoldenTicketKind kind, long record )
	{
		if ( !IsGlobal( kind ) ) throw new InvalidDataException( "Not a global ticket." );
		var canonical = CanonicalTheme( theme );
		if ( record < 0 ) throw new InvalidDataException( "Invalid ticket record." );
		if ( earned.Add( new EarnedTicketIdentity( null, kind ) ) )
		{
			holders[kind] = new( kind, canonical, record );
			return GlobalTicketOutcome.Awarded;
		}
		if ( !holders.TryGetValue( kind, out var holder ) )
		{
			holders[kind] = new( kind, canonical, record );
			return GlobalTicketOutcome.None;
		}
		if ( holder.Theme == canonical )
		{
			// A holder adopted without a record (an older file, record 0: a real record is above its threshold) learns the park's record once; the original's holder keeps its award-time record.
			if ( holder.Record == 0 && record > 0 )
				holders[kind] = holder with { Record = record };
			return GlobalTicketOutcome.None;
		}
		if ( holder.Record >= record )
			return GlobalTicketOutcome.None;
		holders[kind] = new( kind, canonical, record );
		return GlobalTicketOutcome.Moved;
	}

	/// <summary>Reconciles known earned park identities only; validates the entire batch before mutation.</summary>
	public int Reconcile( string theme, IEnumerable<GoldenTicketKind> kinds )
	{
		ArgumentNullException.ThrowIfNull( kinds );
		var canonical = CanonicalTheme( theme );
		var identities = kinds.Select( kind => Identity( canonical, kind ) ).ToArray();
		var changes = 0;
		foreach ( var identity in identities )
		{
			var added = earned.Add( identity );
			if ( IsGlobal( identity.Kind ) && !holders.ContainsKey( identity.Kind ) )
				holders[identity.Kind] = new( identity.Kind, canonical, 0 );
			else if ( !added )
				continue;
			changes++;
		}
		return changes;
	}

	public PlayerTicketSnapshot Capture() => new( Mode, ExtraKeys, SpentTickets,
		Array.AsReadOnly( earned.OrderBy( item => item.Kind ).ThenBy( item => item.Theme, StringComparer.Ordinal ).ToArray() ),
		holders.Count == 0 ? null : Array.AsReadOnly( holders.Values.OrderBy( item => item.Kind ).ToArray() ),
		cheated.Count == 0 ? null : Array.AsReadOnly( cheated.OrderBy( item => item.Kind ).ThenBy( item => item.Theme, StringComparer.Ordinal ).ToArray() ),
		purchases.Count == 0 ? null : Array.AsReadOnly( purchases.Values.OrderBy( item => item.InfoId ).ToArray() ) );

	/// <summary>Copies a validated own snapshot. Spending never removes earned identities or keys.</summary>
	public static PlayerTicketLedger Restore( PlayerTicketSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		if ( !Enum.IsDefined( snapshot.Mode ) || snapshot.ExtraKeys < 0 || snapshot.ExtraKeys > int.MaxValue - MaximumIdentities / PlayerProgress.TicketsPerKey
			|| snapshot.Earned == null || snapshot.Earned.Count > MaximumIdentities || snapshot.SpentTickets < 0 || snapshot.SpentTickets > snapshot.Earned.Count )
			throw new InvalidDataException( "Invalid own player progress." );
		var unique = new HashSet<EarnedTicketIdentity>();
		foreach ( var item in snapshot.Earned )
		{
			if ( item == null || !Enum.IsDefined( item.Kind ) ) throw new InvalidDataException( "Invalid earned ticket." );
			var local = IsLocal( item.Kind );
			if ( local ? item.Theme == null || CanonicalTheme( item.Theme ) != item.Theme : item.Theme != null )
				throw new InvalidDataException( "Ticket theme does not match its scope." );
			if ( !unique.Add( item ) ) throw new InvalidDataException( "Duplicate earned identity." );
		}
		var restoredHolders = new Dictionary<GoldenTicketKind, GlobalTicketHolder>();
		foreach ( var holder in snapshot.Holders ?? Array.Empty<GlobalTicketHolder>() )
		{
			if ( holder == null || !Enum.IsDefined( holder.Kind ) || !IsGlobal( holder.Kind ) || holder.Theme == null || holder.Record < 0 )
				throw new InvalidDataException( "Invalid global ticket holder." );
			if ( CanonicalTheme( holder.Theme ) != holder.Theme || !unique.Contains( new EarnedTicketIdentity( null, holder.Kind ) ) || !restoredHolders.TryAdd( holder.Kind, holder ) )
				throw new InvalidDataException( "Invalid global ticket holder." );
		}
		var restoredCheated = new HashSet<EarnedTicketIdentity>();
		foreach ( var item in snapshot.Cheated ?? Array.Empty<EarnedTicketIdentity>() )
			if ( item == null || !unique.Contains( item ) || !restoredCheated.Add( item ) )
				throw new InvalidDataException( "Invalid cheated ticket." );
		if ( snapshot.KnownPurchases?.Count > MaximumIdentities ) throw new InvalidDataException( "Too many known ticket purchases." );
		var restoredPurchases = new Dictionary<int, KnownTicketPurchase>();
		var knownCost = 0;
		foreach ( var purchase in snapshot.KnownPurchases ?? Array.Empty<KnownTicketPurchase>() )
		{
			if ( purchase == null || purchase.InfoId < 0 || purchase.Cost <= 0 || purchase.Cost > snapshot.SpentTickets - knownCost
				|| !restoredPurchases.TryAdd( purchase.InfoId, purchase ) )
				throw new InvalidDataException( "Invalid known ticket purchase." );
			knownCost += purchase.Cost;
		}
		// A legacy scalar may include unknown purchases. Never manufacture their identities.
		return new PlayerTicketLedger( snapshot with { Earned = unique.ToArray(), Holders = restoredHolders.Values.ToArray(), Cheated = restoredCheated.ToArray(), KnownPurchases = restoredPurchases.Values.ToArray() } );
	}

	/// <summary>
	/// The player's tickets as the advisor's win advice counts them: the ledger's earned identities and the park's own tickets, each once, so the count does not depend on whether the ledger has recorded the
	/// win that raised the advice yet. Nothing is recorded.
	/// </summary>
	internal AdvisorPlayerTickets AdvisorView( string theme, IEnumerable<GoldenTicketKind> parkTickets, IReadOnlyList<int> themeKeyCosts )
	{
		var all = new HashSet<EarnedTicketIdentity>( earned );
		foreach ( var kind in parkTickets )
			all.Add( Identity( theme, kind ) );
		var secret = all.Count( item => AdvisorPlayerTickets.IsSecret( item.Kind ) );
		return new AdvisorPlayerTickets( all.Count, all.Count - secret, ExtraKeys, themeKeyCosts );
	}

	internal static bool IsGlobal( GoldenTicketKind kind ) => kind is >= GoldenTicketKind.CoasterHeight and <= GoldenTicketKind.BigPark;

	/// <summary>Whether the park of <paramref name="theme"/> holds global ticket <paramref name="kind"/> by this player's rule.</summary>
	internal bool HeldBy( string theme, GoldenTicketKind kind ) => holders.TryGetValue( kind, out var holder ) && holder.Theme == CanonicalTheme( theme );

	private static bool IsLocal( GoldenTicketKind kind ) => kind is GoldenTicketKind.Visitors or GoldenTicketKind.PeopleInPark
		or GoldenTicketKind.Happiness or GoldenTicketKind.AllResearchedAndBuilt or GoldenTicketKind.ProfitYear or GoldenTicketKind.RecentVisitors;

	private static EarnedTicketIdentity Identity( string theme, GoldenTicketKind kind )
	{
		var canonical = CanonicalTheme( theme );
		if ( !Enum.IsDefined( kind ) ) throw new InvalidDataException( "Invalid earned ticket kind." );
		return new( IsLocal( kind ) ? canonical : null, kind );
	}

	private static string CanonicalTheme( string theme ) => Themes.FirstOrDefault( known => string.Equals( known, theme, StringComparison.OrdinalIgnoreCase ) )
		?? throw new InvalidDataException( "Unknown ticket theme." );
}
