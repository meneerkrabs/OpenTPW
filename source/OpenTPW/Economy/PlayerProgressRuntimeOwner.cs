namespace OpenTPW;

/// <summary>One application-owned active runtime subscription. Failed writes retain dirty earned identities.</summary>
internal sealed class PlayerProgressRuntimeOwner : IDisposable
{
	private readonly string path;
	private readonly string theme;
	private readonly ParkEconomyRuntime runtime;
	private readonly Func<bool> active;
	private readonly Action<string> report;
	private ParkEconomy observed;
	private string digest;
	private bool disposed;
	public PlayerTicketLedger Ledger { get; }
	public bool Dirty { get; private set; }
	public string? Error { get; private set; }
	private bool Eligible => !disposed && active() && Ledger.Mode == ParkGameMode.FullSimulation &&
		runtime.Start.Kind == ParkStartKind.FullSimulation && runtime.Economy.Mode == ParkGameMode.FullSimulation && runtime.Economy.Settings.CanEarnTickets;

	public PlayerProgressRuntimeOwner( string path, string theme, PlayerTicketFile.ReadState state, ParkEconomyRuntime runtime, Func<bool> active, Action<string> report )
	{
		this.path = path; this.theme = theme; this.runtime = runtime; this.active = active; this.report = report;
		Ledger = state.Ledger; digest = state.Digest; observed = runtime.Economy;
		Reconcile();
		TryFlush();
		runtime.EventRaised += OnEvent;
	}

	private void Reconcile()
	{
		if ( Eligible && Ledger.Reconcile( theme, runtime.Economy.Objectives.GoldenTickets ) != 0 ) Dirty = true;
		observed = runtime.Economy;
	}

	private void OnEvent( ParkEvent item )
	{
		if ( !Eligible || item.Kind != ParkEventKind.GoldenTicketWon || item.Amount < 0 || item.Amount > int.MaxValue ) return;
		var kind = (GoldenTicketKind)item.Amount;
		if ( !Enum.IsDefined( kind ) || !runtime.Economy.Objectives.GoldenTickets.Contains( kind ) ) return;
		if ( Ledger.RecordEarned( theme, kind ) ) { Dirty = true; TryFlush(); }
	}

	public void Pump()
	{
		if ( Eligible && !ReferenceEquals( observed, runtime.Economy ) ) { Reconcile(); TryFlush(); }
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
	}
}

internal sealed class PlayerProgressSaveException( string message ) : IOException( message );
