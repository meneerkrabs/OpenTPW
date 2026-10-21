namespace OpenTPW;

/// <summary>One application-owned active runtime subscription. Failed writes retain dirty earned identities.</summary>
internal sealed class PlayerProgressRuntimeOwner : IDisposable
{
	private readonly string path;
	private readonly string theme;
	private readonly ParkEconomyRuntime runtime;
	private readonly Func<bool> active;
	private readonly Func<bool>? shared;
	/// <summary>[EXT:online-coop-snapshot-join] The park was seen in a co-op session and is not reclaimed yet (<see cref="Reclaim"/>).</summary>
	private bool reclaimPending;
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
	private bool Eligible => !disposed && active() && !reclaimPending && shared?.Invoke() != true && Ledger.Mode == ParkGameMode.FullSimulation &&
		runtime.Start.Kind == ParkStartKind.FullSimulation && runtime.Economy.Mode == ParkGameMode.FullSimulation && runtime.Economy.Settings.CanEarnTickets;

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
		observed = runtime.Economy;
		runtime.Economy.Objectives.GlobalTicketAuthority = authority;
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
		if ( Eligible && !ReferenceEquals( observed, runtime.Economy ) ) { Reconcile(); TryFlush(); }
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] A park back from a co-op session returns to this player's progress (docs/COOP.md, "Golden tickets in a session"): while shared it decided its global
	/// tickets by its own records, as every peer's park did, so each global ticket it holds goes through the player-wide rule now, with the park's record; one the rule keeps with another park
	/// is taken from this park, as the rule would have decided at the win. Its local tickets reconcile as ever.
	/// </summary>
	private void Reclaim()
	{
		if ( !Eligible ) return;
		var objectives = runtime.Economy.Objectives;
		foreach ( var kind in objectives.GoldenTickets.Where( PlayerTicketLedger.IsGlobal ).ToArray() )
		{
			var before = Ledger.HolderOf( kind );
			if ( Ledger.RecordGlobal( theme, kind, runtime.Economy.GlobalTicketRecord( kind ) ) != GlobalTicketOutcome.None || before != Ledger.HolderOf( kind ) ) Dirty = true;
			if ( !Ledger.HeldBy( theme, kind ) ) objectives.TakeGoldenTicket( kind );
		}
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
	}
}

internal sealed class PlayerProgressSaveException( string message ) : IOException( message );
