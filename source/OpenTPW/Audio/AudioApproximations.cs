namespace OpenTPW;

/// <summary>Every unproven sound rule (docs/AUDIO.md "Approximation register"); logged once when the sound service starts.</summary>
public static class AudioApproximations
{
	public static readonly (string Id, string Rule)[] All =
	{
		("AUDIO-003", "the pitch is applied (a step drawn min + r % (max - min) per playback and played at ratio 1 or 2^(sign * (|step| + 1) / 96), tp.exe 0x6bc170 / 0x6d2820, confirmed by 141 playbacks of the original's video); a parameter-bound pitch slot, a placeholder's fixed-pitch flag (0x10000, which replaces the draw with the short at +0x4A, 0x6bc410), the delay of non-chain sounds (u16 + r % range, 0x6bc343) and reverb (SndReverb.map) are not applied, chain delays are milliseconds (video, 2026-10-15); position is applied only by the positioned playback path"),
		("AUDIO-004", "0xBD plays for a control whose construction style (+0x48, tp.exe 0x65bcad arg 2) has bit 0x10 (0x4857cb); which original controls are created with it is not traced, so every OpenTPW control plays 0x1F"),
		("AUDIO-008", "the repair family comes from the owner object's word +0x14 (band 1 -> 75, 2-3 -> 79, 4-7 -> 83, >= 8 -> 87, 0 silent; tp.exe 0x55635f / 0x551320); OpenTPW uses band 1 (event 75) and plays it at the object as the original does, but the field's meaning is not traced"),
		("AUDIO-009", "a rejected placement ('Cannot build') plays the buy window's error feedback 0x1D; the traced 0x1D callers are buy and hire money and limit checks (tp.exe 0x4879d0, 0x4ac590, 0x4af500) and no placement-tool site was found"),
		("AUDIO-018", "the constant-power pan of a positioned voice follows the source's bearing; the original's pan law (QSound's processing, inside QMixer) is not read"),
		("AUDIO-022", "a click on a refused path or queue ghost plays cat_ui 175 at the clicked cell, as tp.exe's click handler 0x52496e does when the refusal latch 0x816d48 is set (0x524a80..0x524aae); its other two gates are not modelled: no line drag running (0x785a2c == 0) and a mouse move since the last hover (0x8186d8)"),
		("AUDIO-023", "cat_ui 139's second source (a laid path cell linking to a neighbouring queue cell, 0x10082D6C) is not played; the queue tool's join and the backtrack (cat_ui 95) play"),
		("AUDIO-025", "December's average is the storage of the unnamed type-0 .sam schema record after Seasons (tp.exe schema 0x741d80), taken as 4; the season is the FILETIME date's month / 3 (0x4f8770: wMonth 1-12, so December is row 4) and that word (0x7854f4, +0x4B4) lies in .bss with no code reference, so only the generic .sam loader can write it (0 if nothing, which would make December's average 0); its tolerance (DaysOfWarning) and chance (DaysBetweenChanges) are confirmed (0x7854f8/0x7854fc)"),
		("AUDIO-033", "the minimum distance 2 is confirmed in both builds; the gain curve lives in QMixer/SoundSprocket, so the exponent 0.8 (the fork's lead) and the missing 2.1 maximum floor stand"),
		("AUDIO-034", "the held chain's child timing, first-sound choice and hard stop are not traced in the PC library (sentence vtables 0x70a7e0 / 0x70a8d0); the fork-only PC leads stand"),
		("AUDIO-036", "the original re-evaluates the playing sample's volume and pitch when a parameter in slots 1-3 changes (tp.exe 0x6bbb80 -> 0x6bbbe0); OpenTPW evaluates crowd gain when a segment starts, and the crowd parameter's slot is not read"),
		("AUDIO-037", "the sound chooser is seeded from the clock when the sound service starts, as the original (QueryPerformanceCounter, tp.exe 0x72e09c), but a park reseeds it from the world seed at entry and saves it with the park so headless runs and tests start from a known state (with sound on, the draws follow wall-clock events, so it is not a replay guarantee and is left out of the resume smoke's hash); the original's chooser is unsaved"),
		("AUDIO-038", "the voice pool of 12 (tp.exe 0x6b62b0) ranks the held chains (screams) with the running sentences, placed sounds and one-shots of the event system by priority and ground distance (0x6bb9f0); a voice enters only while its rank exceeds the pool's lowest (0x6b6bc0 compares strictly, threshold +0x48), so among equal ranks the voice met first in the manager's voice list (+0x24, next +0x34) keeps its slot, and the list's insertion order is not read; a voice out of range without the any-distance flag (event flag 0x200, voice flag 8 at 0x6bca44) has its sample stopped by mixer message 3 (0x6bca70 -> 0x6b8d60 -> 0x6bb7c0) and is not ticked: OpenTPW stops one-shots and sentence segments, and a chain starts no new child, but a chain's children already playing play out and looping voices are not stopped (nothing read restarts a stopped loop); how a stopped sentence resumes is not read and voices the mixer plays outside the event system do not compete"),
		("AUDIO-900", "the rain and snow field follows the original CRain, but its parallax term is 0.1 x the camera's movement per frame, equivalent to the original's wrapped absolute camera position x 0.1 only up to the sign with which the offset is added (the 0.3 factor under camera modes 0x790ab0 & 0x16 is not reproduced), and the blend is not traced: rain streaks blend additively, snow and bolts by alpha"),
		("AUDIO-901", "snow draws the +-1/128 jitter version only, as quads over their atlas quadrant; the original's flag-gated 128-point spline swirl (engine flag 0x115E94 bit 8) and its flake triangle mapping are not reproduced"),
		("AUDIO-902", "the bolt follows the traced CRain shape (roughness 0.6, stop test, branch odds, depth and lengths, ground at y = 0, 2000 ms life, up to 16), but the per-split displacement and the branch heading are OpenTPW's where tp.exe moves the midpoint along a perpendicular to the segment (0x57e4b0: built from one of three axes picked by rand % 3) by |segment| x roughness x r2 x r1^2 with a random sign (r = (rand % 0x1fff) / 8192) and starts a branch when (rand & 0x1ff) < 9 below the depth limit +0xa1a0, heading 2.5 x (0x200 + rand & 0x3ff) / (96 x depth) along the segment plus (0x200 + rand & 0x3ff) / (96 x depth) x the bolt's direction, halved when it points back (0x57e810); the 500 ms colour pulse curve is OpenTPW's, and the ground point is the top's x and z where the original draws a uniform float"),
		("AUDIO-903", "the weather's visuals run on wall-clock frame time capped at 500 ms and freeze while the park is paused; tp.exe's CRain update (0x57ff60) also zeroes dt while engine flag 0x8bd508 bit 2 is set (0x57ffb1, 0x57ffc9), and nothing writes that bit alone (only whole-word stores at 0x564342, 0x577326, 0x577446 and the constant 0x3000141 at 0x583555), so what sets it is not identified"),
		("AUDIO-904", "the rain field lives within +-0.8 engine units of the eye, so the camera's 0.1 near plane (0.5 original units) clips the nearest drops where the original's near plane is 0.1 original units (0x54e86f -> 0x578be0 near 0.1, far 1000, fov 90; the park camera 0x578ca0 keeps the near plane) and its polygon clipper (clip codes 0x561bc0, clipping 0x562a70 in 0x565bb0) cuts the streaks there, drawing only drops in front of the eye (0x57f1f0); the vertex buffer is sized once for 1024 drops and 16 bolts of 512 segments"),
		("AUDIO-908", "an EVENT sound's node position and the footprint middle are sampled once at instruction time as floats (the original passes ints) and the footprint middle's height is the object's base height (the original reads the model's +0x44 field, not traced)"),
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
