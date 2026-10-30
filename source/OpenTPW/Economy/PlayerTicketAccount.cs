namespace OpenTPW;

/// <summary>The migration of a park's older ticket purchases into the player's account, shared by the progress owner and the Instant Action account.</summary>
internal static class PlayerTicketMigration
{
	/// <summary>
	/// The items a park bought with tickets before its purchases went through the player's account (an older park save) enter the account once, by identity: an item the account holds
	/// already is not charged again. One the account cannot afford is reported and stays the park's own record, which <see cref="ParkEconomy.OwnsTicketItem"/> still reads, so the park's
	/// next copy is paid in cash. True when the account changed.
	/// </summary>
	public static bool Reconcile( ParkEconomy economy, PlayerTicketLedger ledger, Action<string> report )
	{
		var changed = false;
		foreach ( var infoId in economy.TicketItems.Order().ToArray() )
		{
			if ( ledger.OwnsPurchasedItem( infoId ) || !economy.Catalog.TryGet( infoId, out var info ) || info.GoldenTicketCost <= 0 ) continue;
			switch ( ledger.TryPurchase( infoId, info.GoldenTicketCost ) )
			{
				case TicketPurchaseOutcome.Purchased: changed = true; break;
				case TicketPurchaseOutcome.InsufficientTickets:
					report( $"The park's earlier ticket purchase of item {infoId} ({info.GoldenTicketCost} tickets) exceeds the player's earned tickets and is not charged to the player's account; the park keeps it as its own." );
					break;
			}
		}
		return changed;
	}
}

/// <summary>
/// The player's ticket account for an Instant Action park: tp.exe's purchase check <c>0x4D4AD0</c> skips the account only in game type 1 (the online visit, <c>0xFB3B7C</c>), so Instant Action (type 2)
/// spends from it and the HUD counter (<c>0x4A10CD</c>) reads it in every game type. Instant Action earns no tickets here and keeps no other progress; the account is the player's file, read when
/// the park starts, and saved when a purchase changes it.
/// </summary>
internal sealed class InstantActionTicketAccount : IPlayerTicketWallet, IDisposable
{
	private readonly string path;
	private readonly ParkEconomyRuntime runtime;
	private readonly Func<bool> active;
	private readonly Action<string> report;
	private ParkEconomy observed;
	private string digest;
	private bool disposed;
	public PlayerTicketLedger Ledger { get; }
	public bool Dirty { get; private set; }

	public InstantActionTicketAccount( string path, PlayerTicketFile.ReadState state, ParkEconomyRuntime runtime, Func<bool> active, Action<string> report )
	{
		this.path = path; this.runtime = runtime; this.active = active; this.report = report;
		Ledger = state.Ledger; digest = state.Digest; observed = runtime.Economy;
		Attach();
	}

	private void Attach()
	{
		observed = runtime.Economy;
		if ( PlayerTicketMigration.Reconcile( observed, Ledger, report ) ) Dirty = true;
		observed.TicketWallet = this;
		TryFlush();
	}

	bool IPlayerTicketWallet.Active => !disposed && active() && ReferenceEquals( runtime.Economy, observed ) && runtime.Economy.Mode == ParkGameMode.InstantAction;
	int IPlayerTicketWallet.Available => Ledger.AvailableTickets;
	bool IPlayerTicketWallet.Owns( int infoId ) => Ledger.OwnsPurchasedItem( infoId );

	bool IPlayerTicketWallet.TrySpend( int infoId, int cost )
	{
		if ( Ledger.TryPurchase( infoId, cost ) != TicketPurchaseOutcome.Purchased ) return false;
		Dirty = true;
		TryFlush();
		return true;
	}

	/// <summary>Takes the account to a park the runtime replaced (a load), as the progress owner does.</summary>
	public void Pump()
	{
		if ( !disposed && !ReferenceEquals( observed, runtime.Economy ) ) Attach();
	}

	public bool TryFlush()
	{
		if ( !Dirty ) return true;
		try
		{
			digest = PlayerTicketFile.SaveIfUnchanged( path, Ledger.Capture(), digest );
			Dirty = false;
			return true;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			report( exception.Message );
			return false;
		}
	}

	public void Dispose()
	{
		if ( disposed ) return;
		TryFlush();
		disposed = true;
		if ( ReferenceEquals( runtime.Economy.TicketWallet, this ) ) runtime.Economy.TicketWallet = null;
	}
}
