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
