namespace OpenTPW;

/// <summary>
/// The flag word of the sound library's voice (placeholder +0x30) as the library builds it from the event record, and the questions the rest of the sound code asks of it.
/// </summary>
// [BIN:TPW-EXE:0x6bbe90 voice flags from the event] the word starts at 0; the event flag 0x8 makes 6 (voice flag 4: loops, 2: no expiry), the event flag 0x4 ORs voice flag 2, the event word's 0x200 ORs voice flag 8 (audible at any distance, flat placement), 0x20 ORs 0x200, the byte at +0x11 bit 0x40 (the event word's 0x4000) ORs 0x20000, and a range scale above 0 (0x70a834) stores itself at +0x40 and ORs 0x80
// [BIN:TPW-EXE:0x6bc484 fixed-pitch flag] the only reader of voice flag 0x10000 (the fixed-pitch short at +0x4A); a scan of every write to a voice's flag word in the library (0x6b6247-0x6beaa1: 0x6bbe40, 0x6bbe5d-0x6bbef3, 0x6bc7c0, 0x6bc7fc, 0x6bc9ee, 0x6bca60, 0x6bca79, 0x6bcae0, 0x6bcb0a, 0x6bcc77, 0x6bd5c2, 0x6bd83a, 0x6bd8e3, 0x6bd8ff, 0x6bdab6, 0x6bdb32, 0x6bdf8a, 0x6be044, 0x6be060, 0x6be567, 0x6be5cc, 0x6be891, 0x6bea75, 0x6beaa1, the pool node 0x6b6a29) finds byte-wise or constant ORs of the bits 0x1 and 0x2 and 0x4 and 0x8 and 0x10 and 0x20 and 0x40 and 0x80, the high bytes 0x1 and 0x4 and 0x8 and 0x10 and 0x20 and 0x40 and 0x80 and the clears of them, the constants 6, 0 and 0x20000, and no write that can set bit 16: no immediate 0x10000 or 0xFFFEFFFF exists in the library outside 0x6bc484 and the mixer handle's own flag word (0x6c51d3, 0x6d20d8)
internal static class SoundVoiceFlags
{
	/// <summary>Voice flag 2: the voice is exempt from the expiry test of 0x6bcb60.</summary>
	internal const uint NoExpiry = 0x2;
	/// <summary>Voice flag 4: the sample loops (0x6c5110 with 9999, 0x6bc628).</summary>
	internal const uint Loops = 0x4;
	/// <summary>Voice flag 8: audible at any distance, and placed flat by the sample start (0x6bc515).</summary>
	internal const uint AnyDistance = 0x8;
	/// <summary>Voice flag 0x10000: plays the short at +0x4A as its pitch step (0x6bc484); nothing sets it.</summary>
	internal const uint FixedPitch = 0x10000;

	/// <summary>The voice flags the library derives from the event word.</summary>
	internal static uint FromEvent( uint eventFlags, bool rangeScaled = false )
	{
		uint voice = 0;
		if ( (eventFlags & 0x8) != 0 )
			voice = NoExpiry | Loops;
		if ( (eventFlags & 0x4) != 0 )
			voice |= NoExpiry;
		if ( (eventFlags & 0x200) != 0 )
			voice |= AnyDistance;
		if ( (eventFlags & 0x20) != 0 )
			voice |= 0x200;
		if ( (eventFlags & 0x4000) != 0 )
			voice |= 0x20000;
		if ( rangeScaled )
			voice |= 0x80;
		return voice;
	}

	/// <summary>Whether the event's voice is audible at any distance and placed flat.</summary>
	internal static bool IsAnyDistance( ushort eventFlags ) => (FromEvent( eventFlags ) & AnyDistance) != 0;

	/// <summary>Whether the event's voice loops its sample.</summary>
	internal static bool Loop( ushort eventFlags ) => (FromEvent( eventFlags ) & Loops) != 0;

	/// <summary>Whether the event's voice is exempt from the expiry test.</summary>
	internal static bool IsExpiryExempt( ushort eventFlags ) => (FromEvent( eventFlags ) & NoExpiry) != 0;
}
