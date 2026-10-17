namespace OpenTPW;

/// <summary>
/// The Movie volume option as the PC player applies it: a percentage becomes a 0 to 1023 device volume, which sets the movie's
/// DirectSound buffer attenuation in hundredths of a decibel, so the option is a decibel slider and not a linear gain.
/// </summary>
/// <remarks>
/// [BIN:TPW-EXE:0x423dd0 movie volume apply] the sound-options apply routine passes percent · 1023 / 100 (C division) to the movie
/// volume setter 0x51b3c0, which keeps the low ten bits in the global 0x802ba8, or passes 0 when the Movie sound switch is off.
/// [BIN:TPW-EXE:0x51b010 movie open] a starting movie, and [BIN:TPW-EXE:0x51b2bc movie volume re-apply] a running one, call the
/// library's volume setter 0x66f520 with v · 10000 / 1023 − 10000, which forwards to the DirectSound buffer's SetVolume (vtable
/// +0x3C of the buffer at +0x5C of the movie). DirectSound reads that value as attenuation in 1/100 dB (0 full volume, −10000
/// silence), so 100 % is unattenuated, 75 % is about −25 dB and 50 % about −50 dB. The linear 0 to 1 gain this replaces came from
/// the Mac build's QuickTime volume (a linear 0 to 256), which the PC player does not use.
/// </remarks>
public static class MovieVolumeCurve
{
	/// <summary>The device volume range: 0 to 1023.</summary>
	public const int DeviceMaximum = 1023;

	/// <summary>DirectSound's lowest volume, 1/100 dB, which it plays as silence.</summary>
	public const int MinimumHundredthsDb = -10000;

	/// <summary>The device volume of a percentage (0 to 100) or 0 while the Movie sound switch is off.</summary>
	public static int DeviceVolume( int percent, bool on ) => on ? Math.Clamp( percent, 0, 100 ) * DeviceMaximum / 100 : 0;

	/// <summary>The DirectSound volume (attenuation in 1/100 dB, −10000 to 0) the movie buffer is set to.</summary>
	public static int HundredthsDb( int deviceVolume ) => (deviceVolume & DeviceMaximum) * 10000 / DeviceMaximum - 10000;

	/// <summary>The amplitude factor (0 to 1) of an attenuation in 1/100 dB; −10000 is silence.</summary>
	public static float Amplitude( int hundredthsDb ) =>
		hundredthsDb <= MinimumHundredthsDb ? 0f : MathF.Pow( 10f, Math.Min( hundredthsDb, 0 ) / 2000f );

	/// <summary>The amplitude factor the original plays a movie at for a Movie volume percentage and switch.</summary>
	public static float Gain( int percent, bool on ) => Amplitude( HundredthsDb( DeviceVolume( percent, on ) ) );
}
