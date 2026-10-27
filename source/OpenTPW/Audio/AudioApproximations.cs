namespace OpenTPW;

/// <summary>Every unproven sound rule (docs/AUDIO.md "Approximation register"); logged once when the sound service starts.</summary>
public static class AudioApproximations
{
	public static readonly (string Id, string Rule)[] All =
	{
		("AUDIO-003", "the pitch is applied (a step drawn min + r % (max - min) per playback and played at ratio 1 or 2^(sign * (|step| + 1) / 96), tp.exe 0x6bc170 / 0x6d2820, confirmed by 141 playbacks of the original's video); a parameter-bound pitch slot is applied to every playback through its placeholder table (tp.exe 0x6bbf60 / 0x6bc040 / 0x6bc170); the fixed-pitch flag 0x10000 (0x6bc484) is unreachable (nothing sets it, the events' flags are 16-bit); the delay of every sample start is drawn after the volume and the pitch (min + r % (max - min), 0x6bc2d0, stored at +0x3c by 0x6bc4fa) and is a chain's wait between children, or for any other voice the time it keeps its place in the pool after its sample's nominal end plus 250 ms (0x6bc01b, 0x6bcb60, 0x6b62b0; events flagged 0x4 or 0x8 are exempt); reverb is OpenTPW's own stand-in by design, EXT:audio-reverb (SndReverb.map is loaded at 0x6c5d20; tp.exe imports none of QMixer's reverb calls); chain delays are milliseconds (video, 2026-10-15); position is applied only by the positioned playback path"),
		("AUDIO-018", "a positioned mono voice follows the software channel stage of QMixer.dll, which tp.exe selects for every voice (the request flag 1 is ORed in unconditionally at 0x6bc61b, so 0x6d23ab hands QSWaveMixConfigureChannel the mode 0x1000000, whose buffers have no DirectSound 3D object): the source's offset is rotated into the listener's basis (front and up from the camera matrix rows 0x7909b0 and 0x7909a0, right = up x front, 0x1800ce70, 0x1800cf10), the azimuth atan2(x, z) in degrees (0x1800cb10) becomes a DirectSound pan of 44.444 hundredths of a dB per degree mirrored behind the listener (0x18005730), the gain a volume of 2000 log10(gain) hundredths of a dB (0x1800ca40), and 0x18005830 sets both for a mono wave only (0x18005982 tests the format table 0x1803c6b4); the opposite channel is 10^(-|pan|/2000) of the level; the axis mapping of the camera rows into the listener (the swap flag of the tp.exe adapters 0x6d2aec and 0x6d2c68, byte +8 of the driver) and how QMixer places the flat voices that tp.exe positions at (0,1,0) (0x6bc51d, SetPosition 0x6d2640 against SetSourcePosition) are not read, so flat voices keep both channels at the voice's gain"),
		("AUDIO-038", "the voice pool of 12 (tp.exe 0x6b62b0) ranks the held chains (screams) with the running sentences, placed sounds and one-shots of the event system by priority and ground distance (0x6bb9f0), a voice entering only while its rank exceeds the pool's lowest (0x6b6bc0) and among equal ranks the newer voice keeping its slot (0x6b6a80 links a new voice at the head of the voice list); a voice out of range without the any-distance flag (event flag 0x200, voice flag 8 at 0x6bca44) has its sample stopped by mixer message 3 (0x6bca70 -> 0x6b8d60 -> 0x6bb7c0) and its handle at +0x24 left in place, and is not ticked: OpenTPW stops one-shots and sentence segments, and a chain starts no new child, but a chain's children already playing play out and looping voices are not stopped (nothing read restarts a stopped loop); how a stopped sentence resumes is not read and voices the mixer plays outside the event system, including the children a chain makes, do not compete"),
		("AUDIO-040", "FADEOBJ now uses configured quality > threshold, service-owned integer-decay tails, shared 100/250 ms polls, subtype-specific stop/continuation and terminal cleanup. AUDIO-040 retains managed float32/nearest-even group quantization because the original caller-thread x87 control word is unresolved, plus the host monotonic startup phase and serialized poll/admission order; these are explicit compatibility policies, not observed original timing. Missing host SoundQuality uses loaded SAM DEFAULTQUALITY (supplied 50; FADETOGGLE 30), not a recovered saved user preference. AUDIO-038 admission/restart and existing general sentence approximations remain separate."),
	};

	private static bool logged;

	public static void LogOnce()
	{
		if ( logged )
			return;
		logged = true;
		foreach ( var (id, rule) in All )
			Log.Trace( $"[APPROX:{id}] {rule}" );
	}
}
