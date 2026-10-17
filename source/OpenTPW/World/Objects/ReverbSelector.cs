namespace OpenTPW;

/// <summary>
/// The per-frame choice of the sound service's reverb preset (tp.exe <c>0x55A470</c>, called from the main loop <c>0x54E360</c>): while the player rides a ride
/// the preset is the SETREVERB level of that ride's root script, everywhere else (and for a ride whose script never set a level) it is the default 0.
/// </summary>
public static class ReverbSelector
{
	/// <summary>The preset when no ridden ride sets one (the constant at <c>0x78578C</c>).</summary>
	public const int DefaultPreset = 0;

	/// <summary>
	/// The preset to hold for the ride being ridden (null outside the ride camera): the ride's first root script with a non-zero level wins; a negative level leaves the held preset.
	/// </summary>
	// [BIN:TPW-EXE:0x55A470 reverb reader] the object is the ride camera's (0x42A4E0: camera byte 0x790AB0 bit 4 and 0x790988 set, 0x790A44); the script list (0x8791B0) is walked for a script whose owner (+0xC8) is that object, with no parent (+0x10) and a non-zero +0xD8; found: a level of 0 or more goes to 0x51C8D0, a negative one does nothing; not found, or no ridden object: 0x51C8D0(0x78578C)
	public static int? PresetFor( OriginalObject? ridden ) => PresetFor( ridden?.Runtime?.Script );

	/// <summary><see cref="PresetFor(OriginalObject?)"/> for the root script of the ridden ride (null: no ridden ride, or one without a script).</summary>
	public static int? PresetFor( RideVM? rootScript )
	{
		if ( rootScript is not { Parent: null, ReverbLevel: not 0 } script )
			return DefaultPreset;
		return script.ReverbLevel >= 0 ? script.ReverbLevel : null;
	}

	/// <summary>Applies the choice for this frame to the sound service.</summary>
	public static void Update( OriginalObject? ridden )
	{
		if ( PresetFor( ridden ) is int preset )
			GameAudio.SelectReverb( preset );
	}
}
