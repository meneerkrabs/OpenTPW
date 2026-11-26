using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// Theme Park Inc's person block (396 bytes): the part of a guest's or a staff member's thing body that the person serializer <c>0x609170</c> transfers, in its stream order (the first
/// member group of <see cref="ThemeParkIncBodyLayout"/>'s guest and staff bodies). The walk members are the native home of OpenTPW's walk state (docs/reverse/TPI-NAVIGATOR.md, NV5): the cell
/// bytes in <see cref="X"/>/<see cref="Y"/>, the accurate destination, the speed words, <see cref="Navigator"/> (the 177-byte navigator record, <see cref="SaveNavigation"/>), the turn's start
/// point, the stranded mark, the wander flag and the heading word. The other members (the thing chain links, the sprite and thought words) are carried as read; the park writer's later phases
/// fill them. <see cref="Write"/> is the save half's byte stream and <see cref="Read"/> the load half's.
/// </summary>
// [BIN:TPI-EXE:0x609170 person serializer] mode 1 (save, 0x6091D1..0x609756) and mode 0 (load, 0x60975B..0x609B30), versions 0..1: the thing base 0x637230 (mX, mY, mMapChild, mMapParent: 2 bytes each),
// the sprite 0x5D0CD0 (mSpriteScript, mNextAnim +0x20, mNextServiceInterval +0x24: 4 each), mAccurateDestX/Y +0x28/+0x2A (2 each), mAdjustorSpeed +0xD8 (2), mBaseSpeed +0xD4 (2), mCount +0x40 (1),
// mESPSprite +0x34 (4), mLastRecordedMapId +0xE4 (2), the navigator 0x63AD30 at +0xEC (177; 0x6093F9), mPreviousSpeed +0xDC (4), mPreviousX/Y +0x1A8/+0x1AC (4 each), mStrandedTime +0x1B0 (4),
// mPurposeSpeed +0xD6 (2), mSetDestSuccessfully +0xE8 (4), mSpriteAngle +0x2C (4), mSpriteID +0x30 (4: the load takes it modulo the sprite count of mESPSprite, 0x6902D0 at 0x609AB0), mSpriteUnderRideCtrl
// +0x38, mOnRideSoNoFloatingThingsAllowed +0x3C, mWhingeTimeout +0xE0 (4 each), then the thought object 0x638490 at +0x44 (mActionHistIndex 2, 32 × 4 mEventHistory, mLastThought, mThoughtScript,
// mTimeBubbleShown 4 each); then the re-route 0x609B55 (ThemeParkIncNativeWalk in OpenTPW). 396 bytes; tools/tpi-layouts/person.py runs both halves
public sealed record ThemeParkIncPersonRecord
{
	public const int Bytes = 396;
	/// <summary>The offset of the navigator record in the block.</summary>
	public const int NavigatorOffset = 35;
	public const int EventHistoryCount = 32;

	/// <summary><c>mX</c>, <c>mY</c> (person <c>+4</c>, <c>+6</c>): the position in 1/256 cells (the walk step's position &gt;&gt; 8); bytes <c>+5</c>/<c>+7</c> are the cell.</summary>
	public ushort X { get; init; }
	public ushort Y { get; init; }
	/// <summary><c>mMapChild</c>, <c>mMapParent</c> (<c>+8</c>, <c>+0xA</c>): the cell's thing chain links.</summary>
	public ushort MapChild { get; init; }
	public ushort MapParent { get; init; }
	public uint SpriteScript { get; init; }
	/// <summary><c>mNextAnim</c> (<c>+0x20</c>): the pending sprite template.</summary>
	public uint NextAnim { get; init; }
	/// <summary><c>mNextServiceInterval</c> (<c>+0x24</c>): the pending sprite interval.</summary>
	public uint NextServiceInterval { get; init; }
	/// <summary><c>mAccurateDestX</c>, <c>mAccurateDestY</c> (<c>+0x28</c>, <c>+0x2A</c>): the words of the last route request's point (cell &lt;&lt; 8 | sub-cell byte).</summary>
	public ushort AccurateDestX { get; init; }
	public ushort AccurateDestY { get; init; }
	/// <summary><c>mAdjustorSpeed</c> (<c>+0xD8</c>): the decaying boost.</summary>
	public ushort AdjustorSpeed { get; init; }
	/// <summary><c>mBaseSpeed</c> (<c>+0xD4</c>).</summary>
	public ushort BaseSpeed { get; init; }
	public byte Count { get; init; }
	public uint EspSprite { get; init; }
	/// <summary><c>mLastRecordedMapId</c> (<c>+0xE4</c>).</summary>
	public ushort LastRecordedMapId { get; init; }
	/// <summary>The navigator record (<c>+0xEC</c>, 177 bytes; <see cref="SaveNavigation"/>).</summary>
	public required byte[] Navigator { get; init; }
	/// <summary><c>mPreviousSpeed</c> (<c>+0xDC</c>): the smoothed speed, a single's bits.</summary>
	public uint PreviousSpeed { get; init; }
	/// <summary><c>mPreviousX</c>, <c>mPreviousY</c> (<c>+0x1A8</c>, <c>+0x1AC</c>): the walk position the speed step copied (16.16 cells).</summary>
	public int PreviousX { get; init; }
	public int PreviousY { get; init; }
	/// <summary><c>mStrandedTime</c> (<c>+0x1B0</c>): the stranded mark, an unsigned stamp-counter value.</summary>
	public int StrandedTime { get; init; }
	/// <summary><c>mPurposeSpeed</c> (<c>+0xD6</c>): the speed bonus.</summary>
	public ushort PurposeSpeed { get; init; }
	/// <summary><c>mSetDestSuccessfully</c> (<c>+0xE8</c>): the wander flag.</summary>
	public uint SetDestSuccessfully { get; init; }
	/// <summary><c>mSpriteAngle</c> (<c>+0x2C</c>): the heading word.</summary>
	public uint SpriteAngle { get; init; }
	public uint SpriteId { get; init; }
	public uint SpriteUnderRideCtrl { get; init; }
	public uint OnRideSoNoFloatingThingsAllowed { get; init; }
	/// <summary><c>mWhingeTimeout</c> (<c>+0xE0</c>).</summary>
	public uint WhingeTimeout { get; init; }
	public ushort ActionHistIndex { get; init; }
	/// <summary>The 32 words of <c>mEventHistory</c>; null writes zeros.</summary>
	public uint[]? EventHistory { get; init; }
	public uint LastThought { get; init; }
	public uint ThoughtScript { get; init; }
	public uint TimeBubbleShown { get; init; }

	/// <summary>The load half: the members of a 396-byte block in stream order.</summary>
	public static ThemeParkIncPersonRecord Read( ReadOnlySpan<byte> bytes )
	{
		if ( bytes.Length != Bytes )
			throw new InvalidDataException( $"A Theme Park Inc person block is {Bytes} bytes, not {bytes.Length}." );
		var position = 0;
		var data = bytes.ToArray();
		ushort U16()
		{
			var value = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( position ) );
			position += 2;
			return value;
		}
		uint U32()
		{
			var value = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( position ) );
			position += 4;
			return value;
		}
		var (x, y, child, parent) = (U16(), U16(), U16(), U16());
		var (script, anim, interval) = (U32(), U32(), U32());
		var (destX, destY, adjustor, baseSpeed) = (U16(), U16(), U16(), U16());
		var count = data[position++];
		var esp = U32();
		var mapId = U16();
		var navigator = data.AsSpan( position, SaveNavigation.Bytes ).ToArray();
		position += SaveNavigation.Bytes;
		var (previousSpeed, previousX, previousY, stranded) = (U32(), (int)U32(), (int)U32(), (int)U32());
		var purpose = U16();
		var (setDest, angle, spriteId, underRide, onRide, whinge) = (U32(), U32(), U32(), U32(), U32(), U32());
		var histIndex = U16();
		var history = new uint[EventHistoryCount];
		for ( var index = 0; index < history.Length; index++ )
			history[index] = U32();
		var (lastThought, thoughtScript, bubble) = (U32(), U32(), U32());
		if ( position != Bytes )
			throw new InvalidOperationException( "The person block reader does not cover the block." );
		return new ThemeParkIncPersonRecord
		{
			X = x, Y = y, MapChild = child, MapParent = parent, SpriteScript = script, NextAnim = anim, NextServiceInterval = interval, AccurateDestX = destX, AccurateDestY = destY,
			AdjustorSpeed = adjustor, BaseSpeed = baseSpeed, Count = count, EspSprite = esp, LastRecordedMapId = mapId, Navigator = navigator, PreviousSpeed = previousSpeed, PreviousX = previousX,
			PreviousY = previousY, StrandedTime = stranded, PurposeSpeed = purpose, SetDestSuccessfully = setDest, SpriteAngle = angle, SpriteId = spriteId, SpriteUnderRideCtrl = underRide,
			OnRideSoNoFloatingThingsAllowed = onRide, WhingeTimeout = whinge, ActionHistIndex = histIndex, EventHistory = history, LastThought = lastThought, ThoughtScript = thoughtScript,
			TimeBubbleShown = bubble
		};
	}

	/// <summary>The save half: the members in stream order, 396 bytes.</summary>
	public byte[] Write()
	{
		if ( Navigator is not { Length: SaveNavigation.Bytes } )
			throw new InvalidOperationException( $"A person block's navigator record is {SaveNavigation.Bytes} bytes." );
		if ( EventHistory is { } events && events.Length != EventHistoryCount )
			throw new InvalidOperationException( $"A person block's event history holds {EventHistoryCount} words." );
		var data = new byte[Bytes];
		var position = 0;
		void U16( ushort value )
		{
			BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( position ), value );
			position += 2;
		}
		void U32( uint value )
		{
			BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( position ), value );
			position += 4;
		}
		U16( X );
		U16( Y );
		U16( MapChild );
		U16( MapParent );
		U32( SpriteScript );
		U32( NextAnim );
		U32( NextServiceInterval );
		U16( AccurateDestX );
		U16( AccurateDestY );
		U16( AdjustorSpeed );
		U16( BaseSpeed );
		data[position++] = Count;
		U32( EspSprite );
		U16( LastRecordedMapId );
		Navigator.CopyTo( data.AsSpan( position ) );
		position += SaveNavigation.Bytes;
		U32( PreviousSpeed );
		U32( unchecked( (uint)PreviousX ) );
		U32( unchecked( (uint)PreviousY ) );
		U32( unchecked( (uint)StrandedTime ) );
		U16( PurposeSpeed );
		U32( SetDestSuccessfully );
		U32( SpriteAngle );
		U32( SpriteId );
		U32( SpriteUnderRideCtrl );
		U32( OnRideSoNoFloatingThingsAllowed );
		U32( WhingeTimeout );
		U16( ActionHistIndex );
		for ( var index = 0; index < EventHistoryCount; index++ )
			U32( EventHistory?[index] ?? 0 );
		U32( LastThought );
		U32( ThoughtScript );
		U32( TimeBubbleShown );
		if ( position != Bytes )
			throw new InvalidOperationException( "The person block writer does not cover the block." );
		return data;
	}
}
