namespace OpenTPW;

/// <summary>
/// The person block shared by guests and staff (390 bytes; tp.exe person serializer <c>0x004F8B10</c>, docs/TPWS-PAYLOAD.md):
/// the base thing (<c>0x0050B090</c>), the sprite (<c>0x004D3EE0</c>: <c>mSpriteScript</c>, <c>mNextAnim</c>, <c>mNextServiceInterval</c>),
/// the person scalars, the 177-byte navigation state (<c>0x0050D150</c>, kept raw in <see cref="Navigation"/> and decoded by <see cref="NavigationState"/>) and the thought block
/// (<c>0x0050BAD0</c>). Positions (<see cref="SaveThingBase.X"/>, <see cref="AccurateDestX"/>) are in 1/256 cell; the loader re-plans the walk
/// to (<see cref="AccurateDestX"/>, <see cref="AccurateDestY"/>) once the person is read (<c>0x004FA5F0</c> at <c>0x004F9407</c>).
/// </summary>
public sealed record SavePerson(
	SaveThingBase Base, uint SpriteScript, uint NextAnim, uint NextServiceInterval,
	ushort AccurateDestX, ushort AccurateDestY, ushort AdjustorSpeed, ushort BaseSpeed, byte Count, uint EspSprite, ushort LastRecordedMapId,
	byte[] Navigation, uint PreviousSpeed, uint PreviousX, uint PreviousY, uint StrandedTime, ushort PurposeSpeed, uint SetDestSuccessfully,
	uint SpriteAngle, uint SpriteId, uint SpriteUnderRideCtrl, uint ActionHistIndex, IReadOnlyList<uint> EventHistory, uint LastThought,
	uint ThoughtScript, uint TimeBubbleShown )
{
	public const int NavigationBytes = 177;
	public const int EventHistoryEntries = 32;
	/// <summary>The highest event type any caller of <c>AddEvent</c> (<c>0x0050C100</c>) passes (the callers push 1 to 0x19).</summary>
	public const int MaximumEventType = 0x19;

	/// <summary>
	/// The event ring decoded: slot <c>i</c> of <see cref="EventHistory"/> is two words, the event type (low) and its value (high), as the thought object's
	/// constructor <c>0x0050BA80</c> clears them (word +4+4i and word +6+4i). <c>AddEvent(type, value)</c> (<c>0x0050C100</c>) writes slot <see cref="ActionHistIndex"/>,
	/// advances the head with <c>(head + 1) &amp; 0x1F</c> and skips the write when the previous slot (head − 1, wrapped) already holds the same pair. An empty slot is (0, 0).
	/// </summary>
	public IReadOnlyList<SaveThoughtEvent> ThoughtEvents => EventHistory.Select( word => new SaveThoughtEvent( (ushort)(word & 0xFFFF), (ushort)(word >> 16) ) ).ToArray();

	/// <summary>The recorded events, newest first: from the slot before the head back round the ring, without the empty slots.</summary>
	public IEnumerable<SaveThoughtEvent> RecentEvents()
	{
		var events = ThoughtEvents;
		for ( var back = 1; back <= EventHistoryEntries; back++ )
		{
			var slot = events[(int)((ActionHistIndex + EventHistoryEntries - back) % EventHistoryEntries)];
			if ( !slot.IsEmpty )
				yield return slot;
		}
	}

	/// <summary>The navigation bytes decoded (<see cref="SaveNavigation"/>).</summary>
	public SaveNavigation NavigationState => SaveNavigation.Read( Navigation );

	internal static SavePerson Read( ref SaveFieldReader reader )
	{
		var start = reader.Position;
		var thingBase = reader.Base();
		var (script, anim, service) = (reader.U32(), reader.U32(), reader.U32());
		var (destX, destY, adjustor, baseSpeed, count, esp, mapId) = (reader.U16(), reader.U16(), reader.U16(), reader.U16(), reader.U8(), reader.U32(), reader.U16());
		var navigation = reader.Bytes( NavigationBytes );
		var (previousSpeed, previousX, previousY, stranded) = (reader.U32(), reader.U32(), reader.U32(), reader.U32());
		var purpose = reader.U16();
		var (setDest, angle, spriteId, underRide) = (reader.U32(), reader.U32(), reader.U32(), reader.U32());
		var histIndex = reader.U32();
		var events = new uint[EventHistoryEntries];
		for ( var index = 0; index < events.Length; index++ )
			events[index] = reader.U32();
		if ( histIndex >= EventHistoryEntries )
			throw new InvalidDataException( $"Save {reader.What} has an event ring head {histIndex}; the ring of 0x50C100 wraps at {EventHistoryEntries}." );
		foreach ( var word in events )
			if ( (word & 0xFFFF) > MaximumEventType )
				throw new InvalidDataException( $"Save {reader.What} has an event of type {word & 0xFFFF}; AddEvent (0x50C100) is only called with 1 to {MaximumEventType}." );
		var person = new SavePerson( thingBase, script, anim, service, destX, destY, adjustor, baseSpeed, count, esp, mapId, navigation, previousSpeed,
			previousX, previousY, stranded, purpose, setDest, angle, spriteId, underRide, histIndex, events, reader.U32(), reader.U32(), reader.U32() );
		if ( reader.Position - start != SaveThingChain.PersonBytes )
			throw new InvalidOperationException( "The person reader does not cover the person block." );
		return person;
	}

	/// <summary>Decodes the 382 person bytes after the base thing (<see cref="SaveStaff.PersonBytes"/>).</summary>
	public static SavePerson Read( SaveThingBase thingBase, ReadOnlySpan<byte> afterBase, string what )
	{
		var bytes = new byte[SaveThingChain.PersonBytes];
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( bytes, thingBase.X );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 2 ), thingBase.Y );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 4 ), thingBase.MapChild );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 6 ), thingBase.MapParent );
		afterBase.CopyTo( bytes.AsSpan( SaveThingBase.Bytes ) );
		var reader = new SaveFieldReader( bytes, 0, what );
		return Read( ref reader );
	}
}

/// <summary>One slot of a person's event ring (<see cref="SavePerson.ThoughtEvents"/>): the type and value words <c>AddEvent</c> (<c>0x0050C100</c>) stores. Type 6 with the major destination logs a dismissal (<c>0x0050132C</c>).</summary>
public readonly record struct SaveThoughtEvent( ushort Type, ushort Value )
{
	public bool IsEmpty => Type == 0 && Value == 0;
}
