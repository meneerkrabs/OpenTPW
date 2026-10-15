namespace OpenTPW;

/// <summary>Frozen application-owned New request. No profile is published until candidate precommit.</summary>
internal sealed class PlayerProgressAdmission
{
	private readonly string path;
	private readonly string? expectedDigest;
	private readonly Func<int> cost;
	private readonly Func<bool> ownsRequest;
	private readonly Action flushPrevious;
	private readonly int? offeredCost;
	private bool published;
	public string Theme { get; }
	public bool Creating { get; }
	/// <summary>The keys a new progress history starts with.</summary>
	// [BIN:TPW-EXE:0x4a6a50 new player award] a new Full Simulation player's mExtraKeys (+0x20) is raised once from the constructor's 0 (0x5aef50) by 0x5afc30
	public const int StarterKeys = 1;
	public bool IsActive => ownsRequest();
	public PlayerTicketLedger Ledger { get; }

	public PlayerProgressAdmission( string path, string theme, PlayerTicketFile.ReadState? observed, int? offeredCost,
		Func<int> cost, Func<bool> ownsRequest, Action flushPrevious )
	{
		this.path = path; Theme = theme; this.offeredCost = offeredCost; this.cost = cost;
		this.ownsRequest = ownsRequest; this.flushPrevious = flushPrevious;
		expectedDigest = observed?.Digest; Creating = observed == null;
		Ledger = observed?.Ledger ?? PlayerTicketLedger.Restore( new PlayerTicketLedger().Capture() with { ExtraKeys = StarterKeys } );
	}

	public void Validate()
	{
		if ( !ownsRequest() ) throw new InvalidOperationException( "The park choice is no longer active." );
		if ( Ledger.Mode != ParkGameMode.FullSimulation ) throw new PlayerProgressModeException();
		if ( PlayerTicketFile.ReadIfPresent( path )?.Digest != expectedDigest ) throw new InvalidDataException( "The OpenTPW progress file changed. Reopen the park choice." );
		var currentCost = cost();
		if ( currentCost < 0 ) throw new InvalidDataException( "The theme entry cost must be nonnegative." );
		if ( offeredCost.HasValue && currentCost != offeredCost ) throw new InvalidDataException( "The theme entry cost changed. Reopen the park choice." );
		if ( currentCost > Ledger.Keys ) throw new PlayerProgressKeysException( currentCost, Ledger.Keys );
	}

	public void BeforeRetirement()
	{
		flushPrevious();
		if ( published ) throw new InvalidOperationException( "This park request has already committed." );
		Validate();
		if ( Creating ) PlayerTicketFile.SaveIfUnchanged( path, Ledger.Capture(), expectedDigest );
		published = true;
	}
}

internal sealed class PlayerProgressKeysException( int required, int available ) : InvalidOperationException( $"This theme needs {required} keys; OpenTPW progress has {available}." )
{
	public int Required { get; } = required;
	public int Available { get; } = available;
}
internal sealed class PlayerProgressModeException() : InvalidOperationException( "Instant Action progress is not reset or migrated." );
