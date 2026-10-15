namespace OpenTPW;

/// <summary>Application-owned earned identity; player-wide tickets have no theme.</summary>
internal sealed record EarnedTicketIdentity( string? Theme, GoldenTicketKind Kind );
internal sealed record PlayerTicketSnapshot( ParkGameMode Mode, int ExtraKeys, int SpentTickets, IReadOnlyList<EarnedTicketIdentity> Earned );

/// <summary>
/// Own player-wide history, separate from native profiles and the legacy count-only PlayerProgress.
/// A new application ledger has zero earned tickets and zero extra keys. No lobby award or gate is implied.
/// </summary>
internal sealed class PlayerTicketLedger
{
	public const int MaximumIdentities = 4 * 6 + 4 + 2;
	private static readonly string[] Themes = { "jungle", "hallow", "fantasy", "space" };
	private readonly HashSet<EarnedTicketIdentity> earned = new();
	public ParkGameMode Mode { get; }
	public int ExtraKeys { get; }
	public int SpentTickets { get; }
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
	}

	/// <summary>Six local kinds are theme-scoped; four globals and two secrets are player-wide.</summary>
	public bool RecordEarned( string theme, GoldenTicketKind kind ) => earned.Add( Identity( theme, kind ) );

	/// <summary>Reconciles known earned park identities only; validates the entire batch before mutation.</summary>
	public int Reconcile( string theme, IEnumerable<GoldenTicketKind> kinds )
	{
		ArgumentNullException.ThrowIfNull( kinds );
		var canonical = CanonicalTheme( theme );
		var identities = kinds.Select( kind => Identity( canonical, kind ) ).ToArray();
		var before = earned.Count;
		earned.UnionWith( identities );
		return earned.Count - before;
	}

	public PlayerTicketSnapshot Capture() => new( Mode, ExtraKeys, SpentTickets,
		Array.AsReadOnly( earned.OrderBy( item => item.Kind ).ThenBy( item => item.Theme, StringComparer.Ordinal ).ToArray() ) );

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
		return new PlayerTicketLedger( snapshot with { Earned = unique.ToArray() } );
	}

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
