namespace OpenTPW;

/// <summary>
/// A guest (thing model 1, 525 bytes): the person block, then the guest fields of tp.exe's guest serializer <c>0x004FB530</c> in its order. The
/// seven needs floats are written as their truncated low byte (the writer's <c>__ftol</c>, as for staff) and land at guest <c>+0x19C</c> (happiness),
/// <c>+0x1A8</c> (hunger), <c>+0x1B4</c> (litter), <c>+0x1A4</c> (thirst), <c>+0x1B8</c> (the unnamed meter), <c>+0x1AC</c> (toilet) and <c>+0x1B0</c>
/// (illness). Thing ids (<see cref="MajorDest"/>, <see cref="PreviousRides"/>, <see cref="QNext"/>, ...) name things of the same save.
/// Guest-object offsets (the order of the serializer's address operands): <c>+0x1A0</c> cash, <c>+0x1BC</c> exit level, <c>+0x1C0</c> prankery index, <c>+0x1C4</c> rides, <c>+0x1C8</c> shops,
/// <c>+0x1CC</c> sideshows, <c>+0x1D0</c> sideshows won, <c>+0x1D4</c> arrival date, <c>+0x1D8</c> arrival index, <c>+0x1DC</c> major destination, <c>+0x1DE</c> saved major destination,
/// <c>+0x1E0</c>/<c>+0x1E8</c> the ride histories, <c>+0x1F0</c> person type, <c>+0x1F1</c> queue position, <c>+0x1F4</c> queue move delay, <c>+0x1F8</c> been admitted, <c>+0x1FC</c> idle stamp,
/// <c>+0x200</c> opening wait, <c>+0x204</c> paid admission, <c>+0x208</c> spot animation stamp, <c>+0x210</c> balloon script, <c>+0x214</c> balloon life, <c>+0x218</c>/<c>+0x21C</c> last position,
/// <c>+0x220</c> state, <c>+0x224</c> saved state, <c>+0x228</c>/<c>+0x22A</c> queue next/previous. The guest constructor <c>0x4FAEC0</c> zeroes the links, admission flag, destinations and last position
/// and stores the constant 6 into both state and saved state. The guest word <c>+0x20C</c> (the happiness at queue join, a float the state-10 join stores and the
/// use satisfaction <c>0x4FDA1B</c> reads) is not saved: the thing-list loader zeroes it right after the constructor (<c>0x51861C</c>).
/// </summary>
public sealed record SaveGuest( int Id, SavePerson Person )
{
	public uint ArrivalDate { get; init; }
	public uint ArrivalIndex { get; init; }
	public uint BalloonScript { get; init; }
	/// <summary>
	/// <c>mBeenAdmitted</c> (+0x1F8): the called-forward flag. <c>OnAdmittance</c> (<c>0x5012B0</c>, from the ride's LetMeOn) sets it to 1 for the attraction named by
	/// <see cref="MajorDest"/>; the guest's queue update <c>0x4FFFF0</c> clears it when the guest notices it (entering state 13), and the dismissal <c>0x5012F0</c>
	/// clears it together with the queue links, the destination and the queue position. Only a state-11 head at queue position 0 carries it, typically for one turn.
	/// </summary>
	public uint BeenAdmitted { get; init; }
	public int Cash { get; init; }
	public int ExitLevel { get; init; }
	public float Happiness { get; init; }
	public float Hunger { get; init; }
	/// <summary><c>mLastPosX</c>/<c>Y</c> (+0x218/+0x21C): the balloon sprite's previous position; <c>0x4FE900</c> moves the balloon script (+0x210) from it to the new point and stores that point. 0 for a guest with no balloon.</summary>
	public uint LastPosX { get; init; }
	public uint LastPosY { get; init; }
	public float Litter { get; init; }
	public ushort MajorDest { get; init; }
	public uint NumRides { get; init; }
	public uint NumShops { get; init; }
	public uint NumSideshows { get; init; }
	public uint NumSideshowsWon { get; init; }
	public uint PaidAdmission { get; init; }
	public uint ParkOpeningWaitingTime { get; init; }
	public byte PersonType { get; init; }
	public byte PrankeryIndex { get; init; }
	/// <summary><c>mPreviousRides[i]</c> (guest <c>+0x1E0</c>), newest first.</summary>
	public IReadOnlyList<ushort> PreviousRides { get; init; } = Array.Empty<ushort>();
	/// <summary><c>mPreviousTemporaryRides[i]</c> (guest <c>+0x1E8</c>), newest first.</summary>
	public IReadOnlyList<ushort> PreviousTemporaryRides { get; init; } = Array.Empty<ushort>();
	/// <summary><c>mQNext</c> (+0x228) and <see cref="QPrev"/> (+0x22A): the thing ids of the next and previous guest of the attraction queue the guest stands in (a doubly linked list; 0 ends it and means "not queued").</summary>
	public ushort QNext { get; init; }
	public ushort QPrev { get; init; }
	/// <summary><c>mQueueMoveDelay</c> (+0x1F4): turns left before the guest moves up; <c>trunc(1.2 × position)</c> on the state-11 entry (<c>0x501E91</c>).</summary>
	public uint QueueMoveDelay { get; init; }
	/// <summary>
	/// <c>mQueuePos</c> (+0x1F1): the recorded queue position (0 = the front), written by <c>0x501160</c> before routing; it lags the list index while the move-up
	/// delay counts. In state 14 it holds the sideshow-win flag instead (<see cref="SideshowWon"/>): the state-14 entry (<c>0x501EF8</c> → <c>0x4E2670</c>) stores
	/// the outcome of its win draw here, which is why the queue re-evaluation <c>0x501390</c> exempts state 14.
	/// </summary>
	public byte QueuePos { get; init; }
	public int RemainingBalloonLife { get; init; }
	public ushort SavedMajorDest { get; init; }
	/// <summary><c>mSavedState</c> (+0x224): the state to resume after a spot animation. <c>0x4FC800</c> copies the current state here, stamps <see cref="TimeOfLastSpotAnim"/> with the world tick and enters state 8 (<c>0x501DB0</c>).</summary>
	public uint SavedState { get; init; }
	/// <summary><c>mState</c> (+0x220): 0 to 21, the states of the guest state setter <c>0x501DB0</c> (all 52 call sites pass at most 0x15; the setter stores the value at 0x501DBF before its <c>ja</c> at 0x501DC5, so it does not itself guard the range).</summary>
	public uint State { get; init; }
	public float Thirst { get; init; }
	public uint TimeOfLastSpotAnim { get; init; }
	public uint TimeStartedIdling { get; init; }
	public float Meter1B8 { get; init; }
	public float Toilet { get; init; }
	public float Illness { get; init; }

	public const int GuestBytes = 135;
	public const int HistorySlots = 4;
	public const float MaximumNeed = 255f;
	/// <summary>The highest state the guest state setter <c>0x501DB0</c> is called with (all 52 call sites pass at most 0x15; the setter does not guard it, it stores before its <c>cmp eax, 0x15; ja</c>). Verified against this tp.exe build only. One refused guest aborts the whole park import.</summary>
	public const uint MaximumState = 21;

	/// <summary>The called-forward flag (<see cref="BeenAdmitted"/>) as a boolean.</summary>
	public bool Admitted => BeenAdmitted != 0;

	/// <summary>The guest state whose <see cref="QueuePos"/> is the sideshow-win flag rather than a position (<c>0x501EF8</c>).</summary>
	public const uint SideshowOutcomeState = 14;

	/// <summary>In state 14, the sideshow outcome the state's entry drew (<see cref="QueuePos"/> nonzero); null in any other state, where the byte is a position.</summary>
	public bool? SideshowWon => State == SideshowOutcomeState ? QueuePos != 0 : null;

	/// <summary>True when the guest stands in an attraction queue chain (a link or a queue position is set).</summary>
	public bool InQueueChain => QNext != 0 || QPrev != 0 || QueuePos != 0;

	public static SaveGuest Read( ReadOnlySpan<byte> payload, SaveThing thing )
	{
		if ( thing.Model != SaveThingModel.Guest )
			throw new ArgumentException( $"Thing {thing.Id} is a {thing.Model}, not a guest.", nameof( thing ) );
		var reader = new SaveFieldReader( payload[..thing.End], thing.BodyOffset, $"guest thing {thing.Id}" );
		var person = SavePerson.Read( ref reader );
		var guest = new SaveGuest( thing.Id, person )
		{
			ArrivalDate = reader.U32(), ArrivalIndex = reader.U32(), BalloonScript = reader.U32(), BeenAdmitted = reader.U32(), Cash = reader.I32(),
			ExitLevel = reader.I32(), Happiness = reader.F32(), Hunger = reader.F32(), LastPosX = reader.U32(), LastPosY = reader.U32(), Litter = reader.F32(),
			MajorDest = reader.U16(), NumRides = reader.U32(), NumShops = reader.U32(), NumSideshows = reader.U32(), NumSideshowsWon = reader.U32(),
			PaidAdmission = reader.U32(), ParkOpeningWaitingTime = reader.U32(), PersonType = reader.U8(), PrankeryIndex = reader.U8(),
		};
		var previous = new ushort[HistorySlots];
		var temporary = new ushort[HistorySlots];
		// The serializer interleaves the two histories: mPreviousRides[i] (+0x1E0 + 2i), then mPreviousTemporaryRides[i] (+0x1E8 + 2i).
		for ( var index = 0; index < HistorySlots; index++ )
			(previous[index], temporary[index]) = (reader.U16(), reader.U16());
		guest = guest with
		{
			PreviousRides = previous, PreviousTemporaryRides = temporary, QNext = reader.U16(), QPrev = reader.U16(), QueueMoveDelay = reader.U32(),
			QueuePos = reader.U8(), RemainingBalloonLife = reader.I32(), SavedMajorDest = reader.U16(), SavedState = reader.U32(), State = reader.U32(),
			Thirst = reader.F32(), TimeOfLastSpotAnim = reader.U32(), TimeStartedIdling = reader.U32(), Meter1B8 = reader.F32(), Toilet = reader.F32(),
			Illness = reader.F32(),
		};
		if ( reader.Position != thing.End )
			throw new InvalidDataException( $"Save guest thing {thing.Id} body is {thing.BodyLength} bytes; the guest serializer reads {reader.Position - thing.BodyOffset}." );
		if ( guest.State > MaximumState || guest.SavedState > MaximumState )
			throw new InvalidDataException( $"Save guest thing {thing.Id} has state {guest.State} and saved state {guest.SavedState}; every caller of the state setter 0x501DB0 passes 0 to {MaximumState}." );
		if ( guest.BeenAdmitted > 1 )
			throw new InvalidDataException( $"Save guest thing {thing.Id} has an admission flag of {guest.BeenAdmitted}; the admission flag is only written as 1 (0x5012B0, 0x5012DF) or 0 (0x4FAF6F, 0x4FB181, 0x50007A, 0x501372)." );
		foreach ( var need in new[] { guest.Happiness, guest.Hunger, guest.Litter, guest.Thirst, guest.Meter1B8, guest.Toilet, guest.Illness } )
			if ( !float.IsFinite( need ) || need < 0f || need > MaximumNeed )
				throw new InvalidDataException( $"Save guest thing {thing.Id} has a need {need} outside 0–{MaximumNeed}." );
		return guest;
	}
}
