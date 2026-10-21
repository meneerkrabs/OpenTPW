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
		Economy.Objectives.PresentationPosted += ForwardChallengePresentation;
	}

	/// <summary>Events of the running economy, followed across <see cref="Load"/>.</summary>
	public event Action<ParkEvent>? EventRaised;

	/// <summary>Immutable challenge presentation messages, followed across economy replacement without changing gameplay events.</summary>
	public event Action<ChallengePresentation>? ChallengePresentationPosted;

	public ParkEconomy Economy { get; private set; }
	public OriginalEconomyImport? Import { get; }
	/// <summary>How the park was started (mode, balance layer, shipped-save import).</summary>
	public ParkStart Start { get; private init; }
	/// <summary>The park opens when its guests attach, instead of waiting for the player's gate command (<see cref="ForThemeParkIncLevel"/>).</summary>
	public bool StartsOpen { get; private init; }
	/// <summary>Guest payments and statistics, once <see cref="AttachGuests"/> ran.</summary>
	public GuestEconomyBridge? Guests { get; private set; }

	/// <summary>
	/// Routes guest admission, shop/sideshow purchases and ride use through the economy, feeds guest
	/// statistics to the park rating, tickets and challenges, and opens an imported park unless its save's
	/// <c>mParkClosed</c> says it is closed; a park started without a save stays closed until the player opens it.
	/// </summary>
	public void AttachGuests( GuestSimulation guests ) => AttachGuests( guests, openParkOnAttach: true );

	internal void AttachGuests( GuestSimulation guests, bool openParkOnAttach )
	{
		Guests = new GuestEconomyBridge( () => Economy, guests );
		guests.Payments = Guests;
		// the guests draw from the park's one world generator, the economy's (tp.exe 0x00516330)
		guests.AttachWorld( Economy.World );
		// The arrival rule reads the park's turn, attraction points and bankruptcy (world mode 4).
		guests.ArrivalClock = () => Economy.Turn;
		guests.ParkPoints = () => Economy.ParkPoints();
		guests.ArrivalsHalted = () => Economy.IsBankrupt;
		// [EXT:cheats-arrival] an arrival cheat waiting for the next free turn holds the natural groups and the pick-ups (docs/CHEATS.md)
		guests.ArrivalHeldForCheat = () => Economy.Cheats.ArrivalPending;
		Economy.GuestStatistics = Guests;
		Economy.RideOperations = Guests;
		AttachLitter( guests );
		// [BIN:TPW-EXE:0x51562d world constructor] a new world writes mParkClosed (+0x1da710) = 1; only the gate command 0x519ef0 (the entrance-price window's button
		// 0x498c60, the keys 0x40c3f0 / 0x40c410, the online park creation 0x516700) opens it, and a park loaded from a file takes the flag from it (reader 0x5179c0)
		// [EXT:tpi-park-open] the Theme Park Inc slice has no gate control and no entrance-price window, so its park opens at once; its own initial state is not traced
		if ( StartsOpen )
		{
			Economy.OpenPark();
			Log.Trace( "Park economy: the Theme Park Inc park opens at the start." );
		}
		else if ( Import is { ParkClosed: true } )
			Log.Trace( "Park economy: the imported park is closed, as saved." );
		else if ( Import == null )
			Log.Trace( "Park economy: a new park starts closed until the player opens it." );
		else if ( openParkOnAttach )
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
	/// <param name="playerSave">The park starts from a player's own save (<see cref="Level"/>'s <c>playerSave</c>): Full Simulation rules, and the balance layer the save was made with (the standard one, else the easy one when the save's loan table matches only that).</param>
	public static ParkEconomyRuntime ForOriginalLevel( OriginalPark park, ParkStartKind kind = ParkStartKind.OriginalSaveReference, WorldSeed? seed = null, bool playerSave = false ) =>
		ForOriginalLevelSteps( park, kind, seed, playerSave ).Last()!;

	/// <summary>
	/// <see cref="ForOriginalLevel"/> in steps, for a park built over frames (docs/BROWSER-PERFORMANCE.md, "Build steps"): null after the
	/// settings and after each part of the catalogues (<see cref="ParkEconomy.CreateForThemeSteps"/>), then the economy, imported. A player's save is one step.
	/// </summary>
	internal static IEnumerable<ParkEconomyRuntime?> ForOriginalLevelSteps( OriginalPark park, ParkStartKind kind = ParkStartKind.OriginalSaveReference, WorldSeed? seed = null, bool playerSave = false )
	{
		ArgumentNullException.ThrowIfNull( park );
		if ( playerSave )
		{
			yield return ForPlayerSave( park, seed );
			yield break;
		}
		if ( park.Save != null && !ParkStart.ReadsShippedSave( kind ) )
			throw new ArgumentException( $"A {kind} start does not use the shipped save; load the level without it.", nameof( park ) );
		EconomyApproximations.LogOnce();
		var start = ParkStart.Resolve( kind, park.Save != null, BalanceSettings.HasEasyLayer( park.LevelName ) );
		var easy = start.EasyBalance;
		ParkEconomy? createdEconomy = null;
		foreach ( var created in ParkEconomy.CreateForThemeSteps( park.LevelName, easy, start.Mode, (seed ?? WorldSeed.Default).EconomyStream ) )
		{
			if ( created == null )
				yield return null;
			else
				createdEconomy = created;
		}
		var economy = createdEconomy!;
		yield return null;
		var import = start.ImportShippedSave ? OriginalEconomyImport.Apply( economy, park, shippedSeed: true ) : null;
		Log.Trace( $"Park economy: {kind} start, {start.Mode} rules; {park.LevelName} {(easy ? "easy" : "standard")} balance from {string.Join( ", ", economy.Settings.Standard.Sources )}; "
			+ $"cash ${economy.Balance}, entrance fee ${economy.EntranceFee}, {economy.Catalog.Objects.Count} catalogue objects, {economy.Research.Items.Count} research items." );
		foreach ( var line in import?.Evidence ?? Array.Empty<string>() )
			Log.Trace( $"Park economy import: {line}" );
		yield return new ParkEconomyRuntime( economy, import ) { Start = start };
	}

	private static ParkEconomyRuntime ForPlayerSave( OriginalPark park, WorldSeed? seed )
	{
		if ( park.Save == null )
			throw new ArgumentException( "A player-save start needs the save.", nameof( park ) );
		EconomyApproximations.LogOnce();
		InvalidDataException? standardMismatch = null;
		foreach ( var easy in BalanceSettings.HasEasyLayer( park.LevelName ) ? new[] { false, true } : new[] { false } )
		{
			var economy = ParkEconomy.CreateForTheme( park.LevelName, easy, ParkGameMode.FullSimulation, (seed ?? WorldSeed.Default).EconomyStream );
			OriginalEconomyImport import;
			try
			{
				import = OriginalEconomyImport.Apply( economy, park );
			}
			catch ( InvalidDataException exception ) when ( !easy && BalanceSettings.HasEasyLayer( park.LevelName ) )
			{
				standardMismatch = exception;
				continue;
			}
			Log.Trace( $"Park economy: player save, Full Simulation rules; {park.LevelName} {(easy ? "easy" : "standard")} balance{(standardMismatch == null ? "" : $" (the standard one did not match: {standardMismatch.Message})")}; cash ${economy.Balance}, entrance fee ${economy.EntranceFee}, {economy.Research.Items.Count} research items." );
			foreach ( var line in import.Evidence )
				Log.Trace( $"Park economy import: {line}" );
			return new ParkEconomyRuntime( economy, import ) { Start = new ParkStart( ParkStartKind.OriginalSaveReference, ParkGameMode.FullSimulation, easy, ImportShippedSave: true ) };
		}
		throw new InvalidDataException( "The save matches neither balance layer." );
	}

	/// <summary>
	/// Creates the money economy of a Theme Park Inc <paramref name="park"/> (<see cref="EconomyRules.ThemeParkIncMoney"/>): Theme Park Inc's own settings and
	/// object catalogue, no native economy/clock import, no staff, research or challenges. Saved analyser history is retained across this fresh host start;
	/// its partial arrivals and recent stays contribute to the first new host month, not the remainder of the saved native month. Objects register uncharged.
	/// </summary>
	public static ParkEconomyRuntime ForThemeParkIncLevel( OriginalPark park, WorldSeed? seed = null )
	{
		ArgumentNullException.ThrowIfNull( park );
		EconomyApproximations.LogOnce();
		var settings = BalanceSettings.LoadThemeParkInc( park.LevelName );
		var catalog = EconomyObjectCatalog.Load( park.LevelName, easy: false );
		var economy = new ParkEconomy( settings, catalog, ParkGameMode.FullSimulation, (seed ?? WorldSeed.Default).EconomyStream, EconomyRules.ThemeParkIncMoney );
		// [EXT:tpi-history-fresh-start] retain the saved numeric history and live inputs while this money-only path keeps its fresh clock/calendar and finance; the first host close crosses that boundary
		// [EXT:tpi-history-five-series] the traced native TPI graph (0x5630B0) selects arrivals/people/rating; the shared host five-series state also retains separately proven happiness/stay measurements; this path adds no graph to the text-only TPI HUD
		if ( park.Save?.Statistics is { } statistics )
			economy.StatisticsHistory.RestoreOriginal( statistics );
		Log.Trace( $"Park economy: Theme Park Inc money rules; {park.LevelName} balance from {string.Join( ", ", settings.Standard.Sources )}; "
			+ $"cash ${economy.Balance}, entrance fee ${economy.EntranceFee}, {economy.Catalog.Objects.Count} catalogue objects, no staff, research or challenges." );
		return new ParkEconomyRuntime( economy, import: null )
		{
			Start = new ParkStart( ParkStartKind.FullSimulation, ParkGameMode.FullSimulation, EasyBalance: false, ImportShippedSave: false ),
			StartsOpen = true
		};
	}

	/// <summary>Wraps a separately validated and restored economy without importing or opening a shipped park.</summary>
	internal static ParkEconomyRuntime ForResume( ParkEconomy economy, ParkStartKind originalStart )
	{
		ArgumentNullException.ThrowIfNull( economy );
		if ( !Enum.IsDefined( originalStart ) )
			throw new InvalidDataException( "The saved park start is unknown." );
		var expectedMode = originalStart == ParkStartKind.InstantAction ? ParkGameMode.InstantAction : ParkGameMode.FullSimulation;
		if ( economy.Mode != expectedMode )
			throw new InvalidDataException( "The saved park start and economy mode differ." );
		return new ParkEconomyRuntime( economy, import: null )
		{
			Start = new ParkStart( originalStart, economy.Mode, economy.Settings.IsEasy, ImportShippedSave: false )
		};
	}

	/// <summary>Called once per 60 Hz fixed simulation tick.</summary>
	public void FixedTick() => Economy.AdvanceFixedTick();

	private void ForwardEvent( ParkEvent item ) => EventRaised?.Invoke( item );

	private void ForwardChallengePresentation( ChallengePresentation message ) => ChallengePresentationPosted?.Invoke( message );

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
			case ParkEventKind.RunningCostsPaid:
			case ParkEventKind.Bankrupt:
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
		Economy.Objectives.PresentationPosted -= ForwardChallengePresentation;
		Economy = loaded;
		Economy.EventRaised += LogEvent;
		Economy.EventRaised += ForwardEvent;
		Economy.Objectives.PresentationPosted += ForwardChallengePresentation;
		if ( Guests != null )
		{
			Economy.GuestStatistics = Guests;
			Economy.RideOperations = Guests;
			AttachLitter( Guests.Guests );
		}
		return world;
	}

	/// <summary>The litter lies on the guests' cell map (for the litter share); an online visit drops none (the original's game type 1).</summary>
	private void AttachLitter( GuestSimulation guests )
	{
		Economy.Litter.Cells = guests.Grid.Cells;
		// [EXT:tpi-no-maintenance-wear] a park without staff drops no litter either: no handyman would ever sweep it
		Economy.Litter.Disabled = guests.OnlineVisit || !Economy.Rules.Staff;
		Economy.OnlineVisit = guests.OnlineVisit;
	}
}
