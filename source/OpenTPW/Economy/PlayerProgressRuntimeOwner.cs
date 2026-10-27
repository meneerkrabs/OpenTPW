namespace OpenTPW;

/// <summary>One application-owned active runtime subscription. Failed writes retain dirty earned identities.</summary>
internal sealed class PlayerProgressRuntimeOwner : IDisposable, IPlayerTicketWallet
{
	private readonly string path;
	private readonly string theme;
	private readonly ParkEconomyRuntime runtime;
	private readonly Func<bool> active;
	private readonly Func<bool>? shared;
	/// <summary>[EXT:online-coop-snapshot-join] The park was seen in a co-op session and is not reclaimed yet (<see cref="Reclaim"/>).</summary>
	private bool reclaimPending;
	/// <summary>[EXT:online-coop-snapshot-join] The global tickets the park won in the session, with its record at each win (the record the check compared).</summary>
	private readonly Dictionary<GoldenTicketKind, long> wonInSession = new();
	private readonly Action<string> report;
	private ParkEconomy observed;
	private string digest;
	private bool disposed;
	private readonly Func<GoldenTicketKind, long, GlobalTicketOutcome> authority;
	public PlayerTicketLedger Ledger { get; }
	public bool Dirty { get; private set; }
	public string? Error { get; private set; }
	internal string CommittedDigest => digest;
	// [EXT:online-coop-snapshot-join] a park in a co-op session is outside its player's progress until it is reclaimed: every peer decides its global tickets by the park's own records
	private bool Eligible => RecordsEligible && runtime.Economy.Settings.CanEarnTickets;
	// [EXT:online-coop-snapshot-join] a park in a co-op session neither reads nor writes its player's research records, so every peer's park takes the same finale decision
	private bool RecordsEligible => !disposed && active() && !reclaimPending && shared?.Invoke() != true && Ledger.Mode == ParkGameMode.FullSimulation &&
		runtime.Start.Kind == ParkStartKind.FullSimulation && runtime.Economy.Mode == ParkGameMode.FullSimulation;

	/// <param name="shared">[EXT:online-coop-snapshot-join] Whether the park is in a co-op session now; null for a park that never is.</param>
	public PlayerProgressRuntimeOwner( string path, string theme, PlayerTicketFile.ReadState state, ParkEconomyRuntime runtime, Func<bool> active, Action<string> report,
		Func<bool>? shared = null )
	{
		this.path = path; this.theme = theme; this.runtime = runtime; this.active = active; this.report = report; this.shared = shared;
		authority = Authority;
		Ledger = state.Ledger; digest = state.Digest; observed = runtime.Economy;
		Reconcile();
		TryFlush();
		runtime.EventRaised += OnEvent;
	}

	private void Reconcile()
	{
		if ( Eligible && Ledger.Reconcile( theme, runtime.Economy.Objectives.GoldenTickets ) != 0 ) Dirty = true;
		var economy = runtime.Economy;
		if ( !ReferenceEquals( observed, economy ) || economy.TicketWallet == null ) ReconcilePurchases( economy );
		observed = economy;
		economy.Objectives.GlobalTicketAuthority = authority;
		economy.TicketWallet = this;
	}

	private void ReconcilePurchases( ParkEconomy economy )
	{
		if ( RecordsEligible && PlayerTicketMigration.Reconcile( economy, Ledger, report ) ) Dirty = true;
	}

	// the park spends from the player's account while it is part of its player's progress (tp.exe 0x4D4AD0 -> 0x5AF740)
	bool IPlayerTicketWallet.Active => RecordsEligible && ReferenceEquals( runtime.Economy, observed );
	int IPlayerTicketWallet.Available => Ledger.AvailableTickets;
	bool IPlayerTicketWallet.Owns( int infoId ) => Ledger.OwnsPurchasedItem( infoId );

	bool IPlayerTicketWallet.TrySpend( int infoId, int cost )
	{
		if ( Ledger.TryPurchase( infoId, cost ) != TicketPurchaseOutcome.Purchased ) return false;
		Dirty = true;
		TryFlushRecords();
		return true;
	}

	/// <summary>The player-wide holder rule of the four global tickets (tp.exe 0x5AF940); a park outside this player's progress keeps its own set.</summary>
	private GlobalTicketOutcome Authority( GoldenTicketKind kind, long record )
	{
		if ( !Eligible ) return GlobalTicketOutcome.Unmanaged;
		var before = Ledger.HolderOf( kind );
		var outcome = Ledger.RecordGlobal( theme, kind, record );
		if ( outcome != GlobalTicketOutcome.None || before != Ledger.HolderOf( kind ) ) Dirty = true;
		return outcome;
	}

	private void OnEvent( ParkEvent item )
	{
		// [EXT:online-coop-snapshot-join] a shared park's global wins are noted, outside the simulation, for the hand-back; the event comes in the check's own tick
		if ( reclaimPending || shared?.Invoke() == true )
		{
			reclaimPending = true;
			if ( item.Kind == ParkEventKind.GoldenTicketWon && item.Amount is >= 0 and <= int.MaxValue && PlayerTicketLedger.IsGlobal( (GoldenTicketKind)item.Amount ) )
				wonInSession[(GoldenTicketKind)item.Amount] = runtime.Economy.GlobalTicketRecord( (GoldenTicketKind)item.Amount );
			return;
		}
		// [EXT:cheats-ticket-provenance] a ticket cheat (silent: no GoldenTicketWon) earns the ticket like a win and records that a cheat awarded it; a global one was earned through the authority already
		if ( Eligible && item.Kind == ParkEventKind.CheatApplied && item.InfoId == (int)CheatKind.Ticket && item.Amount is >= 0 and <= int.MaxValue
			&& Enum.IsDefined( (GoldenTicketKind)item.Amount ) && runtime.Economy.Objectives.GoldenTickets.Contains( (GoldenTicketKind)item.Amount )
			&& item.Detail is nameof( GlobalTicketOutcome.Awarded ) or nameof( GlobalTicketOutcome.Moved ) )
		{
			if ( Ledger.RecordCheated( theme, (GoldenTicketKind)item.Amount, item.Detail == nameof( GlobalTicketOutcome.Awarded ) ) ) Dirty = true;
			if ( Dirty ) TryFlush();
			return;
		}
		if ( !Eligible || item.Kind is not (ParkEventKind.GoldenTicketWon or ParkEventKind.GlobalTicketMoved) || item.Amount < 0 || item.Amount > int.MaxValue ) return;
		var kind = (GoldenTicketKind)item.Amount;
		if ( !Enum.IsDefined( kind ) || !runtime.Economy.Objectives.GoldenTickets.Contains( kind ) ) return;
		if ( Ledger.RecordEarned( theme, kind ) ) Dirty = true;
		if ( Dirty ) TryFlush();
	}

	/// <summary>Records that this park's research is complete in the player's record list and saves it (tp.exe <c>0x5B0CA0</c>, which no ticket setting gates).</summary>
	public void RecordResearchCompleted()
	{
		if ( !RecordsEligible ) return;
		if ( Ledger.RecordResearchCompleted( theme ) ) Dirty = true;
		if ( Dirty ) TryFlushRecords();
	}

	/// <summary>Whether every park's research is complete in the player's record list once this park's is (tp.exe <c>0x5B0C40</c>); false while the park is outside the player's progress.</summary>
	public bool ResearchCompletedEverywhere => RecordsEligible && Ledger.ResearchCompletedEverywhereWith( theme );

	/// <summary>Saves the records even in a theme whose tickets cannot be earned (where <see cref="TryFlush"/> leaves the file alone).</summary>
	private void TryFlushRecords()
	{
		if ( Eligible ) { TryFlush(); return; }
		try
		{
			digest = PlayerTicketFile.SaveIfUnchanged( path, Ledger.Capture(), digest );
			Dirty = false; Error = null;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Error = exception.Message; report( Error );
		}
	}

	public void Pump()
	{
		if ( shared?.Invoke() == true )
		{
			reclaimPending = true;
			return;
		}
		if ( reclaimPending )
		{
			reclaimPending = false;
			Reclaim();
			return;
		}
		// the park a load replaced takes the player's account again: the wallet needs the records the park may keep (RecordsEligible), as the constructor does, not the earning of tickets
		if ( RecordsEligible && !ReferenceEquals( observed, runtime.Economy ) ) { Reconcile(); TryFlushRecords(); }
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] A park back from a co-op session returns to this player's progress (docs/COOP.md, "Golden tickets in a session"): while shared it decided its global
	/// tickets by its own records, as every peer's park did, so each global ticket it won in the session goes through the player-wide rule now, with its record at the win; one the rule keeps
	/// with another park is taken from this park, as the rule would have decided at the win. The tickets it held before the session, and its local tickets, reconcile as ever.
	/// </summary>
	private void Reclaim()
	{
		if ( !Eligible ) { wonInSession.Clear(); return; }
		var objectives = runtime.Economy.Objectives;
		foreach ( var (kind, record) in wonInSession.OrderBy( pair => pair.Key ) )
		{
			if ( !objectives.GoldenTickets.Contains( kind ) ) continue;
			var before = Ledger.HolderOf( kind );
			if ( Ledger.RecordGlobal( theme, kind, record ) != GlobalTicketOutcome.None || before != Ledger.HolderOf( kind ) ) Dirty = true;
			if ( !Ledger.HeldBy( theme, kind ) ) objectives.TakeGoldenTicket( kind );
		}
		wonInSession.Clear();
		// what the park bought with its own tickets in the session enters the player's account now
		ReconcilePurchases( runtime.Economy );
		TryFlush();
	}

	public bool TryFlush( bool requireUnchangedWhenClean = false )
	{
		if ( !Eligible ) return true;
		Reconcile();
		try
		{
			if ( !Dirty )
			{
				if ( requireUnchangedWhenClean && PlayerTicketFile.ReadIfPresent( path )?.Digest != digest )
					throw new InvalidDataException( "The OpenTPW progress file changed. Reopen the park choice." );
				return true;
			}
			digest = PlayerTicketFile.SaveIfUnchanged( path, Ledger.Capture(), digest );
			Dirty = false; Error = null;
			return true;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Error = exception.Message; report( Error );
			return false;
		}
	}

	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		runtime.EventRaised -= OnEvent;
		if ( runtime.Economy.Objectives.GlobalTicketAuthority == authority ) runtime.Economy.Objectives.GlobalTicketAuthority = null;
		if ( ReferenceEquals( runtime.Economy.TicketWallet, this ) ) runtime.Economy.TicketWallet = null;
	}
}

internal sealed class PlayerProgressSaveException( string message ) : IOException( message );
