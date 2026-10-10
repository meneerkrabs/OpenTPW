namespace OpenTPW;

/// <summary>
/// Connects a <see cref="ParkEconomy"/> to a running level: creates it for an original level in the
/// chosen <see cref="ParkStart"/> (game mode, <c>Easy_</c> balance layer and shipped-save import are
/// separate decisions), advances it on the fixed simulation clock and writes HUD-free log lines for
/// days and months. Saving uses <see cref="ParkSaveFile"/>; original saves stay read-only.
/// </summary>
public sealed class ParkEconomyRuntime
{
	private ParkEconomyRuntime( ParkEconomy economy, OriginalEconomyImport? import )
	{
		Economy = economy;
		Import = import;
		Economy.EventRaised += LogEvent;
		Economy.EventRaised += ForwardEvent;
	}

	/// <summary>Events of the running economy, followed across <see cref="Load"/>.</summary>
	public event Action<ParkEvent>? EventRaised;

	public ParkEconomy Economy { get; private set; }
	public OriginalEconomyImport? Import { get; }
	/// <summary>How the park was started (mode, balance layer, shipped-save import).</summary>
	public ParkStart Start { get; private init; }
	/// <summary>Guest payments and statistics, once <see cref="AttachGuests"/> ran.</summary>
	public GuestEconomyBridge? Guests { get; private set; }

	/// <summary>
	/// Routes guest admission, shop/sideshow purchases and ride use through the economy, feeds guest
	/// statistics to the park rating, tickets and challenges, and opens the park (the original open
	/// state is not decoded from the save; OpenTPW opens imported parks).
	/// </summary>
	public void AttachGuests( GuestSimulation guests )
	{
		Guests = new GuestEconomyBridge( () => Economy, guests );
		guests.Payments = Guests;
		Economy.GuestStatistics = Guests;
		Economy.RideOperations = Guests;
		// [APPROX:ECON-031] imported parks are opened on load (open state not decoded) — evidence needed: park-open flag in the save
		Economy.OpenPark();
		Log.Trace( $"Park economy: guests pay the ${Economy.EntranceFee} entrance fee and shop/sideshow prices into the ledger." );
	}

	/// <summary>Registers a placed attraction as an economy object (not charged: prototype placements bypass research/purchase) and links its guest bridge.</summary>
	public ParkObjectState? LinkAttraction( IRideVisitorBridge attraction, int infoId )
	{
		if ( !Economy.Catalog.TryGet( infoId, out _ ) )
		{
			Log.Trace( $"Park economy: {attraction.Name} (Info.Id {infoId}) is not in the {Economy.Settings.Theme} catalogue; its visits are not booked." );
			return null;
		}
		// [EXT:developer-prototype] the developer prototype ride is registered uncharged (no original counterpart)
		var state = Economy.RegisterExisting( infoId );
		Guests?.Link( attraction.AttractionId, state.Id );
		return state;
	}

	public void UnlinkAttraction( IRideVisitorBridge attraction )
	{
		if ( Guests == null || !Guests.TryGetInstance( attraction.AttractionId, out var instance ) )
			return;
		Guests.Unlink( attraction.AttractionId );
		if ( Economy.TryGetObject( instance, out _ ) )
			Economy.Remove( instance );
	}
	/// <summary>Latest status line, e.g. for a debug overlay.</summary>
	public string Status => $"{Economy.Date}: balance ${Economy.Balance}, {Economy.Staff.Members.Count} staff, rating {Economy.ParkRating}, speed {Economy.Speed}";

	/// <summary>
	/// Creates the economy of <paramref name="park"/>. The park must have been loaded with
	/// <see cref="ParkStart.ReadsShippedSave"/> for <paramref name="kind"/>, so its save is present exactly
	/// when the start imports it.
	/// </summary>
	/// <param name="seed">The park's world seed; the economy stream is <see cref="WorldSeed.EconomyStream"/> (1 for the default seed).</param>
	public static ParkEconomyRuntime ForOriginalLevel( OriginalPark park, ParkStartKind kind = ParkStartKind.OriginalSaveReference, WorldSeed? seed = null )
	{
		ArgumentNullException.ThrowIfNull( park );
		if ( park.Save != null && !ParkStart.ReadsShippedSave( kind ) )
			throw new ArgumentException( $"A {kind} start does not use the shipped save; load the level without it.", nameof( park ) );
		EconomyApproximations.LogOnce();
		var start = ParkStart.Resolve( kind, park.Save != null, BalanceSettings.HasEasyLayer( park.LevelName ) );
		var easy = start.EasyBalance;
		var economy = ParkEconomy.CreateForTheme( park.LevelName, easy, start.Mode, (seed ?? WorldSeed.Default).EconomyStream );
		var import = start.ImportShippedSave ? OriginalEconomyImport.Apply( economy, park ) : null;
		economy.SeedResearcherStandIn = import != null && start.Mode == ParkGameMode.InstantAction;
		Log.Trace( $"Park economy: {kind} start, {start.Mode} rules; {park.LevelName} {(easy ? "easy" : "standard")} balance from {string.Join( ", ", economy.Settings.Standard.Sources )}; "
			+ $"cash ${economy.Balance}, entrance fee ${economy.EntranceFee}, {economy.Catalog.Objects.Count} catalogue objects, {economy.Research.Items.Count} research items." );
		foreach ( var line in import?.Evidence ?? Array.Empty<string>() )
			Log.Trace( $"Park economy import: {line}" );
		return new ParkEconomyRuntime( economy, import ) { Start = start };
	}

	/// <summary>Called once per 60 Hz fixed simulation tick.</summary>
	public void FixedTick() => Economy.AdvanceFixedTick();

	private void ForwardEvent( ParkEvent item ) => EventRaised?.Invoke( item );

	private void LogEvent( ParkEvent item )
	{
		switch ( item.Kind )
		{
			case ParkEventKind.DayEnded:
				Log.Trace( $"Park clock: {ParkCalendar.ToDate( item.Tick )}; balance ${item.Amount}." );
				break;
			case ParkEventKind.MonthEnded:
			case ParkEventKind.YearEnded:
			case ParkEventKind.WagesPaid:
			case ParkEventKind.Bankrupt:
			case ParkEventKind.BankruptcyWarning:
			case ParkEventKind.ItemResearched:
			case ParkEventKind.ChallengeOffered:
			case ParkEventKind.ChallengeCompleted:
			case ParkEventKind.ChallengeFailed:
			case ParkEventKind.GoldenTicketWon:
				Log.Trace( $"Park {item.Kind} at {item.Date}: {item.Amount} {item.Detail}".TrimEnd() );
				break;
		}
	}

	/// <param name="world">The level's other random streams; null saves the economy alone.</param>
	public void Save( string path, WorldRandomState? world = null ) => ParkSaveFile.Save( path, Economy, world );

	/// <summary>
	/// Replaces the running economy with a saved one of the same theme, difficulty and game mode. The
	/// mode is part of the save and is never defaulted; a save of the other mode is refused so the rules
	/// cannot change under the running start.
	/// </summary>
	/// <returns>The saved world streams for the caller to restore, or null when the save has none.</returns>
	public WorldRandomState? Load( string path )
	{
		var (loaded, world) = ParkSaveFile.LoadState( path, ( theme, easy ) =>
		{
			if ( !string.Equals( theme, Economy.Settings.Theme, StringComparison.OrdinalIgnoreCase ) || easy != Economy.Settings.IsEasy )
				throw new InvalidDataException( $"The park save is for {theme}, not the running {Economy.Settings.Theme} level." );
			return (Economy.Settings, Economy.Catalog);
		} );
		if ( loaded.Mode != Economy.Mode )
			throw new InvalidDataException( $"The park save is a {loaded.Mode} park, not the running {Economy.Mode} park." );
		Economy.EventRaised -= LogEvent;
		Economy.EventRaised -= ForwardEvent;
		Economy = loaded;
		Economy.EventRaised += LogEvent;
		Economy.EventRaised += ForwardEvent;
		if ( Guests != null )
		{
			Economy.GuestStatistics = Guests;
			Economy.RideOperations = Guests;
		}
		return world;
	}
}
