namespace OpenTPW;

public partial class Level
{
	/// <summary>
	/// [EXT:online-coop-claims] The rides a player's track tool edits in a shared park (docs/COOP.md, "Claims"; C3c-1): a ride's attraction id and the seat that
	/// holds its claim (<see cref="ClaimRideCommand"/>, <see cref="ReleaseRideCommand"/>). Session state, as the grants are: in the session digest once there is one and in a join snapshot,
	/// cleared with the session; never in the world hash or a save. Null until the first claim.
	/// </summary>
	private SortedDictionary<int, int>? rideClaims;

	/// <summary>[EXT:online-coop-claims] Each claimed ride's attraction id and its holder's seat, in ride order (empty without a session or a claim).</summary>
	internal IReadOnlyDictionary<int, int> RideClaims => (IReadOnlyDictionary<int, int>?)rideClaims ?? NoRideClaims;

	/// <summary>The claims of a park without one.</summary>
	private static readonly IReadOnlyDictionary<int, int> NoRideClaims = new SortedDictionary<int, int>();

	/// <summary>[EXT:online-coop-claims] The seat that holds the claim of the ride with attraction id <paramref name="ride"/>, or null.</summary>
	internal int? ClaimOf( int ride ) => rideClaims != null && rideClaims.TryGetValue( ride, out var seat ) ? seat : null;

	/// <summary>[EXT:online-coop-claims] The attraction id of the ride whose claim <paramref name="seat"/> holds (a seat holds one at most), or null.</summary>
	internal int? ClaimedRideOf( int seat )
	{
		if ( rideClaims == null )
			return null;
		foreach ( var (ride, holder) in rideClaims )
		{
			if ( holder == seat )
				return ride;
		}
		return null;
	}

	/// <summary>[EXT:online-coop-claims] Whether another seat than <paramref name="player"/> holds the claim of the ride with attraction id <paramref name="ride"/>.</summary>
	internal bool ClaimedAgainst( int player, int ride ) => ClaimOf( ride ) is { } seat && seat != player;

	/// <summary>[EXT:online-coop-claims] Whether another seat than <paramref name="player"/> holds any claim: while none does, nothing is claimed against that player (<see cref="CoopBuildRights"/>).</summary>
	internal bool HasClaimsAgainst( int player ) => rideClaims != null && rideClaims.Values.Any( seat => seat != player );

	/// <summary>[EXT:online-coop-claims] Every claim goes: a claim is its session's, and the park's next session starts without one.</summary>
	internal void ClearRideClaims() => rideClaims = null;

	/// <summary>[EXT:online-coop-claims] Releases every claim <paramref name="seat"/> holds (a departed seat's, <see cref="ReleaseSeatCommand"/>). True when it held one.</summary>
	internal bool ReleaseClaimsOf( int seat )
	{
		if ( rideClaims == null )
			return false;
		var held = rideClaims.Where( pair => pair.Value == seat ).Select( pair => pair.Key ).ToArray();
		foreach ( var ride in held )
			rideClaims.Remove( ride );
		if ( rideClaims.Count == 0 )
			rideClaims = null;
		return held.Length > 0;
	}

	/// <summary>[EXT:online-coop-claims] The seats that hold a claim, in order.</summary>
	internal IEnumerable<int> ClaimingSeats() => rideClaims?.Values.Distinct().OrderBy( seat => seat ) ?? Enumerable.Empty<int>();

	/// <summary>[EXT:online-coop-claims] A claimed ride was removed (its sale, its demolition, the Move's pick-up): its claim goes with it, on every peer alike.</summary>
	private void DropClaimOf( OriginalObject item )
	{
		if ( rideClaims != null && rideClaims.Remove( item.Runtime.AttractionId ) && rideClaims.Count == 0 )
			rideClaims = null;
	}

	/// <summary>
	/// [EXT:online-coop-claims] Binds this machine's claim to its track tool (C3c-1); the HUD calls this after taking its requests' outcomes each frame, so a claim is bound to its ride by the
	/// park's state, never by a tool's event:
	/// <list type="bullet">
	/// <item>a claim of a ride the track tool is not on goes: the tool left the ride (Escape, a right click, another tool, the queue button, a valid circuit, the queue tool that follows it),
	/// a tool start was dropped, or a resync's park has the claim in it; the edit tool's turn into the line tool, within one call, keeps it. The host lets it go at once; a guest asks the host
	/// at most once a second (by <paramref name="now"/>, kept by the session across a resync), and only while no claim, open or release request of its own is open in the session's requests (from this park or one a resync
	/// replaced): the host applies them in order, so a release sent before an open claim's outcome would come before the claim;</item>
	/// <item>the host's track tool on a ride whose claim it does not hold (a tool that was on when the session started) claims it, and ends, with the claim's note, when another player holds
	/// it;</item>
	/// <item>a guest's track tool on a ride whose claim its seat no longer holds (the host let it go: the guest was away for 30 seconds, or sent nothing on the ride for 5 minutes) ends, with
	/// a note, once no line or Backspace of it waits (whose outcome ends it and says so) and no claim or open request of its own is open (C3c-1b).</item>
	/// </list>
	/// True when it released, claimed, ended or sent something.
	/// </summary>
	internal bool SettleClaim( double now )
	{
		if ( CoopSession == null )
			return false;
		var tool = TrackToolStation?.Runtime.AttractionId;
		var requests = CoopSession.GuestCommands;
		var entering = requests != null && requests.Open.Any( request => request.Command is ClaimRideCommand or OpenTrackLineToolCommand );
		if ( tool is { } on && ClaimOf( on ) != Commands.Player )
		{
			if ( Commands.SendsRequests )
			{
				if ( CellTool.IsWaiting || entering )
					return false;
				ExitTrackTool();
				// a claim another player took, building taken back (its record let go of the claim), or a claim the host let go
				NoteSessionRefusal( ClaimOf( on ) != null ? CoopRefusal.Claimed : !HasBuildGrant( Commands.Player ) ? CoopRefusal.NotGranted : CoopRefusal.ClaimLost );
				return true;
			}
			// the claim moves from any other ride the host held
			var claim = Commands.Issue( new ClaimRideCommand( on ) );
			if ( claim.Accepted )
				return true;
			ExitTrackTool();
			if ( claim.Reason == ClaimedReason )
				NoteSessionRefusal( CoopRefusal.Claimed );
			tool = null;
		}
		if ( ClaimedRideOf( Commands.Player ) is not { } ride || ride == tool )
			return false;
		if ( !Commands.SendsRequests )
			return Commands.Issue( new ReleaseRideCommand( ride ) ).Accepted;
		if ( requests == null || entering || requests.Open.Any( request => request.Command is ReleaseRideCommand )
			|| (CoopSession.LastStrayClaimRelease is { } last && now - last < 1) )
			return false;
		CoopSession.LastStrayClaimRelease = now;
		return Commands.Issue( new ReleaseRideCommand( ride ) ).IsPending;
	}

	/// <summary>
	/// [EXT:online-coop-claims] In a shared park the host's track tool claims its ride before it starts (C3c-1): the claim applies at once, and another player's refuses the tool, which the HUD
	/// says (<see cref="CoopRefusal.Claimed"/>). True when the tool may start: no session, or the claim taken.
	/// </summary>
	private bool ClaimForTrackTool( OriginalObject station )
	{
		if ( CoopSession == null )
			return true;
		var claim = Commands.Issue( new ClaimRideCommand( station.Runtime.AttractionId ) );
		if ( claim.Accepted )
			return true;
		// as the frame check says it (SettleClaim): another player's claim, and no note for a refusal of another reason
		if ( claim.Reason == ClaimedReason )
			NoteSessionRefusal( CoopRefusal.Claimed );
		return false;
	}

	/// <summary>[EXT:online-coop-claims] The reason a claim refuses another player's edit, which the HUD says in its own words.</summary>
	internal const string ClaimedReason = "Another player is editing this ride.";

	/// <summary>
	/// [EXT:online-coop-claims] Runs a <see cref="ClaimRideCommand"/>: <paramref name="player"/> takes the claim of the track ride with attraction id <paramref name="ride"/> when no other seat
	/// holds it, and lets go of its claim of any other ride in the same command (the claim moves with the player's track tool, so a seat holds one at most and its own other claim never
	/// refuses it). A refusal changes no claim.
	/// </summary>
	internal ParkCommandResult ExecuteClaimRide( int player, int ride )
	{
		if ( ResolveCommandObject( ride ) is not { IsDeleted: false } item || !trackStations.ContainsKey( item ) )
			return ParkCommandResult.Rejected( $"Attraction {ride} is no track ride." );
		if ( ClaimedAgainst( player, ride ) )
			return ParkCommandResult.Rejected( ClaimedReason );
		if ( ClaimedRideOf( player ) is { } other && other != ride )
			rideClaims!.Remove( other );
		(rideClaims ??= new SortedDictionary<int, int>())[ride] = player;
		return ParkCommandResult.Ok();
	}

	/// <summary>
	/// [EXT:online-coop-claims] Runs a <see cref="ReleaseRideCommand"/>: the claim of the ride with attraction id <paramref name="ride"/> goes when <paramref name="player"/> holds it, or when the host
	/// releases it (a disconnected seat's, an idle one's).
	/// </summary>
	internal ParkCommandResult ExecuteReleaseRide( int player, int ride )
	{
		if ( ClaimOf( ride ) is not { } seat || (seat != player && player != ParkCommands.LocalPlayer) )
			return ParkCommandResult.Rejected( $"Player {player} holds no claim of attraction {ride}." );
		rideClaims!.Remove( ride );
		if ( rideClaims.Count == 0 )
			rideClaims = null;
		return ParkCommandResult.Ok( seat );
	}
}
