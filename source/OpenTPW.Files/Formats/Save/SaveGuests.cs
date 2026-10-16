namespace OpenTPW;

/// <summary>
/// A guest (thing model 1, 525 bytes): the person block, then the guest fields of tp.exe's guest serializer <c>0x004FB530</c> in its order. The
/// seven needs floats are written as their truncated low byte (the writer's <c>__ftol</c>, as for staff) and land at guest <c>+0x19C</c> (happiness),
/// <c>+0x1A8</c> (hunger), <c>+0x1B4</c> (litter), <c>+0x1A4</c> (thirst), <c>+0x1B8</c> (the unnamed meter), <c>+0x1AC</c> (toilet) and <c>+0x1B0</c>
/// (illness). Thing ids (<see cref="MajorDest"/>, <see cref="PreviousRides"/>, <see cref="QNext"/>, ...) name things of the same save.
/// </summary>
public sealed record SaveGuest( int Id, SavePerson Person )
{
	public uint ArrivalDate { get; init; }
	public uint ArrivalIndex { get; init; }
	public uint BalloonScript { get; init; }
	public uint BeenAdmitted { get; init; }
	public int Cash { get; init; }
	public int ExitLevel { get; init; }
	public float Happiness { get; init; }
	public float Hunger { get; init; }
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
	public ushort QNext { get; init; }
	public ushort QPrev { get; init; }
	public uint QueueMoveDelay { get; init; }
	public byte QueuePos { get; init; }
	public int RemainingBalloonLife { get; init; }
	public ushort SavedMajorDest { get; init; }
	public uint SavedState { get; init; }
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
		foreach ( var need in new[] { guest.Happiness, guest.Hunger, guest.Litter, guest.Thirst, guest.Meter1B8, guest.Toilet, guest.Illness } )
			if ( !float.IsFinite( need ) || need < 0f || need > MaximumNeed )
				throw new InvalidDataException( $"Save guest thing {thing.Id} has a need {need} outside 0–{MaximumNeed}." );
		return guest;
	}
}
