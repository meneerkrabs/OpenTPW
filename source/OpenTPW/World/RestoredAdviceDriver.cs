namespace OpenTPW;

/// <summary>
/// The restored advice extension's posting side (docs/reverse/ADVISOR-SCAN.md, "Restored advice"): the park events it answers, the arrival vehicle it watches, the poll of the state-driven rows and the help messages
/// it posts. It owns no simulation or saved state: the poll's place, the vehicle last seen and the follow-up count live in memory only, so the advisor's snapshot is the same with or without the extension. Every entry point is a
/// no-op while <see cref="RestoredAdvice.Enabled"/> is false, and every post goes through the advisor's own queue (<see cref="AdvisorController.AdviseRestored"/>, <see cref="AdvisorController.Advise"/>), which applies
/// the admission rules (tutorial option, once-only, slaps, duplicates, repeat interval) and the descriptor's game mode.
/// </summary>
// [EXT:restored-advice] OpenTPW's own posters; tp.exe posts none of these messages
internal sealed class RestoredAdviceDriver
{
	private static readonly int[] PollMessages = RestoredAdviceScan.PollRules.Keys.OrderBy( message => message ).ToArray();

	private readonly AdvisorController controller;
	private readonly Func<uint> gameTick;
	private readonly Func<int> gameType;
	private readonly Func<bool> tutorialEnabled;
	private readonly Func<int?>? playerKeys;
	// where the poll goes on (one rule per scan call, like the scan's cursor); not saved
	private int pollCursor;
	// the kind of the arrival vehicle last seen at work (null: none yet) and whether the park has been observed; not saved
	private int? lastVehicleKind;
	private bool vehicleObserved;
	// the golden tickets won and not yet told, and the player's keys as last seen; not saved
	private readonly List<GoldenTicketKind> pendingTickets = new();
	private int? lastKeys;
	// the follow-up challenges met in a row; not saved
	private int followups;

	/// <param name="controller">The advisor's controller the messages are posted to.</param>
	/// <param name="gameTick">The park turn the queue's history is stamped with.</param>
	/// <param name="gameType">The original game type (2 = Instant Action).</param>
	/// <param name="tutorialEnabled">The tutorial option.</param>
	/// <param name="playerKeys">The player's golden keys (<see cref="PlayerTicketLedger.Keys"/>, as the HUD reads them); null when no progress is kept, which leaves the key lines unsaid.</param>
	public RestoredAdviceDriver( AdvisorController controller, Func<uint> gameTick, Func<int> gameType, Func<bool> tutorialEnabled, Func<int?>? playerKeys = null )
	{
		this.playerKeys = playerKeys;
		this.controller = controller ?? throw new ArgumentNullException( nameof( controller ) );
		this.gameTick = gameTick ?? throw new ArgumentNullException( nameof( gameTick ) );
		this.gameType = gameType ?? throw new ArgumentNullException( nameof( gameType ) );
		this.tutorialEnabled = tutorialEnabled ?? throw new ArgumentNullException( nameof( tutorialEnabled ) );
	}

	/// <summary>A park is attached: the bookkeeping starts over, and message 283 is said when the pop-up help option is off.</summary>
	// [EXT:restored-advice-tutorial] 283 (pop-up help is off, the options screen turns it on): the tutorial group says it once
	public void Attached( bool popUpHelp )
	{
		Reset();
		if ( !popUpHelp )
			Help( RestoredAdvice.PopUpHelpOffHelp, "the pop-up help option being off" );
	}

	/// <summary>The park ended: the bookkeeping is dropped.</summary>
	public void Reset()
	{
		pollCursor = 0;
		lastVehicleKind = null;
		vehicleObserved = false;
		followups = 0;
		pendingTickets.Clear();
		lastKeys = null;
	}

	/// <summary>Posts a help message (the descriptor's help score), when the extension restores it and is on.</summary>
	public void Help( int message, string source )
	{
		if ( !RestoredAdvice.Enabled || !RestoredAdvice.IsRestored( message ) )
			return;
		var admission = controller.Advise( message, gameTick(), gameType(), tutorialEnabled() );
		Trace( message, source, admission );
	}

	/// <summary>Posts a restored message with its score; <paramref name="variant"/> picks the response (the caption of a ticket's own criterion), -1 takes the next.</summary>
	public void Post( int message, string source, int variant = -1 )
	{
		if ( !RestoredAdvice.Enabled )
			return;
		Trace( message, source, controller.AdviseRestored( message, gameTick(), gameType(), tutorialEnabled(), variant ) );
	}

	private static void Trace( int message, string source, AdvisorQueueAdmission? admission ) =>
		Log.Trace( $"Advisor restored advice for {source}: message {message} {(admission is { } result ? $"{result.Eligibility}{(result.Stored ? $" in slot {result.Slot}" : result.Acknowledged ? ", queue full" : "")}" : "not posted (no score, descriptor or game mode)")}." );

	/// <summary>The park events the extension answers: tickets and awards, upgrades, challenges.</summary>
	public void OnParkEvent( ParkEvent item, ParkEconomy economy )
	{
		ArgumentNullException.ThrowIfNull( economy );
		if ( !RestoredAdvice.Enabled )
			return;
		switch ( item.Kind )
		{
			case ParkEventKind.GoldenTicketWon when Enum.IsDefined( (GoldenTicketKind)item.Amount ):
				// the player's keys are the ledger's, which updates as the event is raised: the ticket is told on the next frame (ResolveTickets)
				if ( RestoredAdvice.AwardMessage( (GoldenTicketKind)item.Amount ) is { } award )
					Post( award, $"the {(GoldenTicketKind)item.Amount} award" );
				pendingTickets.Add( (GoldenTicketKind)item.Amount );
				break;
			// [EXT:restored-advice-upgrades] the purchase is queued whether or not a mechanic is hired (ParkEconomy.TryBuyUpgrade), so 130 and 131 are the advice that goes with it
			case ParkEventKind.UpgradeBought when !economy.Staff.OfType( StaffType.Mechanic ).Any():
				Post( 130, "an upgrade bought without a mechanic" );
				break;
			case ParkEventKind.UpgradeBought when economy.RoleOnStrike( StaffType.Mechanic ):
				Post( 131, "an upgrade bought during the mechanics' strike" );
				break;
			case ParkEventKind.ItemResearched when item.InstanceId == 1 && item.Amount > 0 && !economy.Staff.OfType( StaffType.Mechanic ).Any():
				Post( 132, $"the upgrade {item.Detail} researched without a mechanic" );
				break;
			// [EXT:restored-advice-challenges] a met challenge with a follow-up is the double-or-nothing offer (the follow-up is current already when the event arrives, ParkObjectives.Update); the second in a row is 320
			case ParkEventKind.ChallengeCompleted when item.Amount == 0 && economy.Objectives.Current is { Accepted: false }:
				followups++;
				Post( followups == 1 ? 319 : 320, "a challenge met with a follow-up offered" );
				break;
			case ParkEventKind.ChallengeCompleted:
				followups = 0;
				break;
			// [EXT:restored-advice-tutorial] 344 (the first special challenge on offer): the tutorial group says it once
			case ParkEventKind.ChallengeOffered:
				Help( RestoredAdvice.FirstChallengeHelp, "a challenge being offered" );
				break;
		}
	}

	/// <summary>
	/// Tells the golden tickets won since the last frame, by the keys the player has now against the keys seen before (the player-wide ledger the HUD shows, <see cref="PlayerTicketLedger.Keys"/>; the ledger updates as the win is
	/// raised, so this runs a frame later). A ticket that earned no key says 99; the key that opens Wonderland (3) or Space Zone (5) says 165 or 164; any other key says the ticket's criterion line (218, 223 or 228, or the twin once the first
	/// has been said, with the response that carries the ticket's caption). Without a ledger the keys are unknown and every ticket says 99, which claims no key.
	/// </summary>
	// [EXT:restored-advice-tickets] OpenTPW chooses the lines; tp.exe has no caller of 0x59B590 for 99, 164, 165, 218, 219, 223, 224, 228, 229 or 230 to 233 to learn them from
	public void ResolveTickets()
	{
		if ( !RestoredAdvice.Enabled )
		{
			pendingTickets.Clear();
			return;
		}
		var keys = playerKeys?.Invoke();
		if ( pendingTickets.Count == 0 )
		{
			lastKeys = keys;
			return;
		}
		var gained = keys is { } now && lastKeys is { } before ? Math.Max( 0, now - before ) : 0;
		var tickets = pendingTickets.ToArray();
		pendingTickets.Clear();
		lastKeys = keys;
		foreach ( var kind in tickets )
		{
			if ( gained <= 0 || keys is not { } total )
			{
				Post( 99, $"the {kind} ticket (no key)" );
				continue;
			}
			gained--;
			if ( RestoredAdvice.UnlockMessage( total ) is { } unlock )
			{
				Post( unlock, $"the {kind} ticket (key {total})" );
				continue;
			}
			if ( RestoredAdvice.TicketMessage( kind ) is var (message, twin, variant) )
				Post( controller.Queue.GetHistory( message ).HasBeenPlayed ? twin : message, $"the {kind} ticket (key {total})", variant );
		}
	}

	/// <summary>
	/// An observation of the arrival vehicle at work (<see cref="GuestSimulation.ArrivalVehicle"/>, -1 for none; the simulation puts it back to none after every group). A vehicle of another kind than the one seen last says its line
	/// (137 bus, 138 seaplane, 139 Ferry), and each is told once per park (<see cref="RestoredAdviceEntry.RepeatGroups"/>); the vehicle already at work when the park is first seen is only taken as the start.
	/// </summary>
	// [EXT:restored-advice-arrivals] OpenTPW's own trigger: a vehicle kind that differs from the last one seen is a change of how guests arrive (GuestSimulation.DispatchArrivalVehicle)
	public void ObserveVehicle( int vehicle )
	{
		if ( !RestoredAdvice.Enabled )
			return;
		var first = !vehicleObserved;
		vehicleObserved = true;
		if ( vehicle < 0 )
			return;
		var previous = lastVehicleKind;
		lastVehicleKind = vehicle;
		if ( first || previous == vehicle )
			return;
		var message = vehicle switch { ArrivalVehicleKind.Bus => 137, ArrivalVehicleKind.Seaplane => 138, ArrivalVehicleKind.Ferry => 139, _ => 0 };
		if ( message != 0 )
			Post( message, $"the {ArrivalVehicleKind.Names[vehicle]} arriving" );
	}

	/// <summary>
	/// <paramref name="steps"/> poll steps (one per scan call that ran; at most one pass over the rules): each evaluates the next state-driven rule the scan skips, in message order, and posts it when it scores above
	/// <c>MinScoreForConsideration</c> and the queue would admit it (a rule that holds for long does not repeat itself in the log or the queue).
	/// </summary>
	public void Poll( IRestoredAdviceState state, int steps = 1 )
	{
		ArgumentNullException.ThrowIfNull( state );
		if ( !RestoredAdvice.Enabled || PollMessages.Length == 0 )
			return;
		for ( var step = 0; step < Math.Min( steps, PollMessages.Length ); step++ )
		{
			var message = PollMessages[pollCursor++ % PollMessages.Length];
			var score = RestoredAdviceScan.PollRules[message]( new AdvisorScanContext( controller.Balance, null ) { Restored = state } );
			if ( score > controller.Queue.MinimumScore && controller.Queue.HasDescriptor( message ) && RestoredAdvice.RepeatAllows( controller.Queue, message, gameTick() )
				&& controller.Queue.Eligibility( new AdvisorPendingAdvice( message, score ), gameTick(), tutorialEnabled() ) == AdvisorEligibility.Eligible )
				Post( message, "the poll" );
		}
	}

	/// <summary>
	/// A screen of the online extension opened (<see cref="UI.OnlineHost.Advise"/>): its help message.
	/// </summary>
	// [EXT:restored-advice-online] the original's online screens posted these through 0x486B00 in the lobby; OpenTPW's online screens open over a park too (Go Online in the pause menu)
	public void OnlineScreen( int message )
	{
		if ( RestoredAdvice.Enabled && RestoredAdvice.Find( message ) is { Topic: "online" } )
			Help( message, "an online screen" );
	}
}
