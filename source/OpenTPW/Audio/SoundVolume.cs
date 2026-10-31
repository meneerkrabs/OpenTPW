namespace OpenTPW;

/// <summary>
/// The sound library's volume chain between a sound's own volume percent and the level QMixer is given: the effective integer volume of the voice's enabled groups,
/// then the driver's table of 128 entries.
/// </summary>
internal static class SoundVolume
{
	/// <summary>The largest index the driver's volume call accepts.</summary>
	internal const int MaximumIndex = 127;

	/// <summary>The driver's table entry for an index in 0..127.</summary>
	// [BIN:TPW-EXE:0x6D2980,0x782E40 volume table] the driver's SetVolume (virtual slot) refuses an index below 0 or above 127 (nothing is sent) and sends the 16-bit entry of the table 0x782E40: 258 x index, and 32767 for the last entry (0x6D2980 reads it as a word)
	internal static int Entry( int index ) => index == MaximumIndex ? 32767 : 258 * index;

	/// <summary>The volume QMixer ends up with for an index: its message case 2 (0x18007A1C) multiplies the table entry by 1/32767 (a percent of 100 gives 0.7874); null when the driver sends nothing.</summary>
	// [BIN:QMIXER-DLL:0x180045C0 case 2 at 0x18007A1C library volume] the volume message stores entry / 32767 (as a float) in the channel parameters (+0x4C), and the channel's volume product +0x4C x +0xB8 (distance gain) x +0xB4 (cone) = +0xBC is recomputed
	internal static float? Library( int index ) => (uint)index > MaximumIndex ? null : Entry( index ) * (1f / 32767f);

	/// <summary>
	/// The effective integer volume of a voice: its percent times the float of every group its mask names (bit 0 master, 1 music, 2 speech, 3 effects), from the start value 1.0.
	/// The product is formed as floats (the main thread's 24-bit arithmetic) and rounded to the nearest even integer.
	/// </summary>
	// [BIN:TPW-EXE:0x6BB860 effective volume] start 1.0 (0x70A7DC); every set bit of the voice's group mask multiplies in that group's float (+0x48..+0x54 of the service, each the service's percent x 0.01f stored by 0x6B98D0); fistp of the result is the index, so 100 with nothing else is 100 and 100 x 0.5 is 50
	internal static int Index( int percent, uint groupMask, ReadOnlySpan<float> groups )
	{
		var product = 1f;
		for ( var group = 0; group < groups.Length && group < 4; group++ )
			if ( (groupMask & (1u << group)) != 0 )
			{
				var value = groups[group];
				product *= float.IsFinite( value ) ? value : 0;
			}
		return (int)MathF.Round( percent * product, MidpointRounding.ToEven );
	}
}
