namespace OpenTPW;

/// <summary>
/// Connects a <see cref="ParkEconomy"/> to a running level: creates it for an imported original
/// level (easy balance when the level ships <c>Easymode.TPWI</c>, whose loan table proves it was made
/// with <c>Easy_Standard.sam</c>), advances it on the fixed simulation clock and writes HUD-free log
/// lines for days and months. Saving uses <see cref="ParkSaveFile"/>; original saves stay read-only.
/// </summary>
public sealed class ParkEconomyRuntime
{
	private ParkEconomyRuntime( ParkEconomy economy, OriginalEconomyImport? import )
	{
		Economy = economy;
		Import = import;
		Economy.EventRaised += LogEvent;
	}

	public ParkEconomy Economy { get; private set; }
	public OriginalEconomyImport? Import { get; }
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

	public static ParkEconomyRuntime ForOriginalLevel( OriginalPark park )
	{
		ArgumentNullException.ThrowIfNull( park );
		var easy = park.Save != null && BalanceSettings.HasEasyLayer( park.LevelName );
		var economy = ParkEconomy.CreateForTheme( park.LevelName, easy );
		var import = park.Save != null ? OriginalEconomyImport.Apply( economy, park ) : null;
		Log.Trace( $"Park economy: {park.LevelName} {(easy ? "easy" : "standard")} balance from {string.Join( ", ", economy.Settings.Standard.Sources )}; "
			+ $"cash ${economy.Balance}, entrance fee ${economy.EntranceFee}, {economy.Catalog.Objects.Count} catalogue objects, {economy.Research.Items.Count} research items." );
		foreach ( var line in import?.Evidence ?? Array.Empty<string>() )
			Log.Trace( $"Park economy import: {line}" );
		return new ParkEconomyRuntime( economy, import );
	}

	/// <summary>Called once per 60 Hz fixed simulation tick.</summary>
	public void FixedTick() => Economy.AdvanceFixedTick();

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

	public void Save( string path ) => ParkSaveFile.Save( path, Economy );

	/// <summary>Replaces the running economy with a saved one of the same theme and difficulty.</summary>
	public void Load( string path )
	{
		var loaded = ParkSaveFile.Load( path, ( theme, easy ) =>
		{
			if ( !string.Equals( theme, Economy.Settings.Theme, StringComparison.OrdinalIgnoreCase ) || easy != Economy.Settings.IsEasy )
				throw new InvalidDataException( $"The park save is for {theme}, not the running {Economy.Settings.Theme} level." );
			return (Economy.Settings, Economy.Catalog);
		} );
		Economy.EventRaised -= LogEvent;
		Economy = loaded;
		Economy.EventRaised += LogEvent;
		if ( Guests != null )
			Economy.GuestStatistics = Guests;
	}
}
