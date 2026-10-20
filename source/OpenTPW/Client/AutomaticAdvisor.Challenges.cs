namespace OpenTPW;

/// <summary>
/// The advice game code posts for golden tickets and challenges, and the record of arrival groups the scan's callbacks read (<c>0x004D4FA0</c>, <c>0x004D5270</c>, <c>0x004D5870</c>, <c>0x004D5B30</c>,
/// <c>0x004D2C50</c>, <c>0x004D1A00</c>, <c>0x004D1AE0</c>, <c>0x004CF3E0</c>). See docs/reverse/ADVISOR-SCAN.md, "Challenges and golden tickets".
/// </summary>
internal sealed partial class AutomaticAdvisor
{
	private readonly AdvisorArrivalRing arrivalRing = new();
	// a met challenge whose follow-up was offered says its win line (272) after the follow-up's offer line (270), as the original's win routine does: 0x4D2B60 offers, then 0x4D1A00 posts
	private bool wonLinePending;

	/// <summary>The sizes of the last arrival groups, the oldest first (at most <see cref="AdvisorArrivalRing.Capacity"/>).</summary>
	internal IReadOnlyList<int> ArrivalWaves => arrivalRing.Waves;

	/// <summary>
	/// The player's golden ticket ledger as the win advice reads it, given the park's own tickets: the sums the original's ledger answers (<c>0x5AF4A0</c>, <c>0x5AF530</c>, the keys and the parks' costs to enter).
	/// Null while none is attached, which leaves the park's own tickets standing for the player's.
	/// </summary>
	internal Func<IReadOnlyCollection<GoldenTicketKind>, AdvisorPlayerTickets?>? PlayerTickets { get; set; }

	private void ChallengesAttached( GuestSimulation? simulation )
	{
		arrivalRing.Clear();
		AssumeEstablishedArrivalRing();
		wonLinePending = false;
		if ( simulation != null )
			simulation.ArrivalGroupStarted += OnArrivalGroupStarted;
	}

	/// <summary>
	/// The stand-in for an unrecorded ring (<see cref="AdvisorArrivalRing.AssumeFilled"/>, ADVISOR-028): an older OpenTPW save has no record of the arrival group sizes, so a park with admissions takes the ring as full and the first-group
	/// explanation (281) does not fire once after loading an established park. Unknown sizes can also change hype/big-group advice. A park without admissions keeps its empty ring.
	/// Original TPW imports and manually attached TPI money runtimes read their source rings afterward (<see cref="SaveArrivalRing"/>, <see cref="SaveThemeParkIncArrivalRing"/>); public TPI gameplay has no advisor.
	/// </summary>
	private void AssumeEstablishedArrivalRing()
	{
		if ( CurrentEconomy?.Counters[ParkCounters.Admissions] > 0 )
			arrivalRing.AssumeFilled();
	}

	private void ChallengesDetached( GuestSimulation? simulation )
	{
		if ( simulation != null )
			simulation.ArrivalGroupStarted -= OnArrivalGroupStarted;
	}

	/// <summary>A group of arriving guests starts: its size joins the ring the scan's callbacks read.</summary>
	// [BIN:TPW-EXE:0x004CF42A arrival ring push] the arrival update pushes the group's size into the ring at park +0x216D8 as the group starts (the same call that logs "Bus about to arrive with %d people")
	internal void OnArrivalGroupStarted( int size ) => arrivalRing.Push( size );

	private void ChallengesOnParkEvent( ParkEvent item )
	{
		if ( CurrentEconomy is not { } economy )
			return;
		switch ( item.Kind )
		{
			case ParkEventKind.GoldenTicketWon when Enum.IsDefined( (GoldenTicketKind)item.Amount ):
				OnGoldenTicketWon( economy, (GoldenTicketKind)item.Amount );
				break;
			// [BIN:TPW-EXE:0x004D5270 global ticket moved] the moved outcome posts its own line (234 + type, in OnParkEvent) and then, like every outcome, runs 0x4D5B30
			case ParkEventKind.GlobalTicketMoved:
				AfterTicketAdvice( TicketsAfter( economy, (GoldenTicketKind)item.Amount ) );
				break;
			case ParkEventKind.ChallengeOffered:
				// [BIN:TPW-EXE:0x004D2C50 challenge offered] the offer posts 270 (0x10E), or 344 (0x158) when it is the first offer, the one that switched the challenge system on (the nonzero argument of 0x4D2A70)
				PostEventAdvice( economy.Objectives.LastOfferSwitchedOn ? AdvisorChallengeMessages.FirstOfferMessage : AdvisorChallengeMessages.OfferedMessage, $"challenge {item.InfoId} offered" );
				if ( wonLinePending )
				{
					wonLinePending = false;
					PostEventAdvice( AdvisorChallengeMessages.WonMessage, "a challenge won" );
				}
				break;
			case ParkEventKind.ChallengeCompleted:
				OnChallengeCompleted( economy, item.InfoId );
				break;
			// [BIN:TPW-EXE:0x004D1AE0 challenge lost] the loss routine counts the loss (+0x24) and posts 273 (0x111)
			case ParkEventKind.ChallengeFailed:
				PostEventAdvice( AdvisorChallengeMessages.LostMessage, $"challenge {item.InfoId} lost" );
				break;
		}
	}

	/// <summary>
	/// A challenge was won. The win routine (<c>0x4D2FB0</c>) pays the prize or offers the follow-up (<c>0x4D2B60</c>, which posts 270), then marks the slot now current as won (<c>0x4D1A00</c>), which posts 272 when
	/// that slot has no follow-up type: the won challenge itself, or the follow-up that was offered in its place.
	/// </summary>
	// [BIN:TPW-EXE:0x004D1A00 challenge won mark] sets the slot's completed byte (+0x2C) and, when its follow-up type (+0x18) is 0, posts 0x110 (272); the slot is the one the current index (+0x3CC) names after the follow-up offer has moved it
	private void OnChallengeCompleted( ParkEconomy economy, int index )
	{
		var followupOffered = economy.Objectives.Current is { Accepted: false };
		var result = followupOffered ? economy.Objectives.CurrentDefinition : economy.Settings.Challenges.GetValueOrDefault( index );
		if ( result is not { FollowupType: 0 } )
			return;
		if ( followupOffered )
			wonLinePending = true;
		else
			PostEventAdvice( AdvisorChallengeMessages.WonMessage, $"challenge {index} won" );
	}

	/// <summary>The player's tickets once the park's own are counted, as the original's sums are when the advice is posted.</summary>
	private AdvisorPlayerTickets TicketsAfter( ParkEconomy economy, GoldenTicketKind kind )
	{
		var held = economy.Objectives.GoldenTickets;
		// the check's later wins are in the park's set already, where the original has not counted them yet: they leave the park's set before the ledger adds it in, so a later win the ledger holds already (a replayed theme) still counts
		var batch = economy.Objectives.LastWonTickets;
		var position = batch.ToList().IndexOf( kind );
		var counted = position < 0 ? held : held.Except( batch.Skip( position + 1 ) ).ToList();
		return PlayerTickets?.Invoke( counted ) ?? AdvisorPlayerTickets.ForPark( counted );
	}

	/// <summary>A golden ticket was won: the line of its kind for the outcome of the key rule, with the ticket as the response, then the first-ticket lines.</summary>
	// [BIN:TPW-EXE:0x004D4FA0 ticket won] the outcome 1, 2 or 3 of 0x5AF810 (0x5AF940, 0x5AFB00) picks the message of the three, the ticket's type is the response variant; every win then runs 0x4D5B30
	private void OnGoldenTicketWon( ParkEconomy economy, GoldenTicketKind kind )
	{
		var tickets = TicketsAfter( economy, kind );
		if ( AdvisorTicketAdvice.For( kind, AdvisorTicketAdvice.Outcome( tickets ) ) is var (message, variant) )
			PostEventAdvice( message, $"the {kind} ticket won", variant );
		AfterTicketAdvice( tickets );
	}

	/// <summary>The lines every ticket event ends with: with exactly one ticket in all, the player's first, the advice on it and the clues for the rest.</summary>
	// [BIN:TPW-EXE:0x004D5B30 first ticket] the sum of all tickets, the secret ones too (0x5AF530), is 1: 0x137 (311) and then 0x13B (315) are queued
	private void AfterTicketAdvice( AdvisorPlayerTickets tickets )
	{
		if ( tickets.Earned != 1 )
			return;
		PostEventAdvice( AdvisorTicketAdvice.FirstTicketMessage, "the player's first golden ticket" );
		PostEventAdvice( AdvisorTicketAdvice.CluesMessage, "the clues for the remaining golden tickets" );
	}
}
