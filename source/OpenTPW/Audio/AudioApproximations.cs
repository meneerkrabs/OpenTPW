namespace OpenTPW;

/// <summary>Every unproven sound rule (docs/AUDIO.md "Approximation register"); logged once when the sound service starts.</summary>
public static class AudioApproximations
{
	public static readonly (string Id, string Rule)[] All =
	{
		("AUDIO-001", "the second user volume the original ducks during speech is the effects channel"),
		("AUDIO-002", "banks resolve to <map folder's parent>/<name>HD.sdt, then to global/<name>HD.sdt"),
		("AUDIO-003", "pitch, delay and reverb of a sound are not applied; position is applied only by the positioned playback path"),
		("AUDIO-004", "the park view's 0xBD click plays for controls whose flag word (0x1017F618 reads control+0x44) has a bit set outside 24-26; no key is mapped to it"),
		("AUDIO-005", "a sentence's next segment is chosen when the current one starts"),
		("AUDIO-006", "guests within four cells of the camera are counted by cell rectangle; the original's area shape inside the radius (0x100C313C) is not traced"),
		("AUDIO-007", "the map/research drawer sounds (0x1C open, 0xC2 close) play when OpenTPW's map and research windows open and close; OpenTPW shows windows where the original slides a drawer"),
		("AUDIO-008", "a ride script's REPAIREFFECT 1 plays cat_kids event 75; the original picks among 75-90 by the object's repair-level field"),
		("AUDIO-009", "a rejected placement ('Cannot build') plays the buy window's error feedback 0x1D; only the buy window's and dialogs' error paths are traced"),
		("AUDIO-010", "the lobby sentences start with the level of the currently selected island and restart when the selection changes; when the original restarts them is not traced"),
		("AUDIO-011", "an RSE EVENT id missing from the level catalogue plays from the global category of the same name (incagod.RSE's EVENT 3 id 43 exists only in the global cat_rides); the sound library's own fallback rule is not traced"),
		("AUDIO-012", "every activated button plays the click 0x1F, front end included; the traced witness is the park view's control-message handler (0x10137FD0)"),
		("AUDIO-013", "de-emphasis is a first-order bilinear-transform shelf of the published 50/15 µs and J.17 curves at the stream's rate"),
		("AUDIO-014", "a failed frame CRC is counted while the frame still decodes; no shipped frame carries a CRC word"),
		("AUDIO-015", "only the level-driven rain voice is built; the weather thing's own rain voice (thing +0x54) is not, and whether the original's second voice is audible is not established"),
		("AUDIO-016", "a scream's speed is the default 50 (the Mac's ride VM word at +192, set at 0x100B3154/0x100B318C); OpenTPW keeps no per-script speed word, so every scream uses 50"),
		("AUDIO-018", "the constant-power pan of a positioned voice follows the source's bearing; the original's pan law (QSound's processing) is not read"),
		("AUDIO-019", "a positioned one-shot whose event has flag 0x8 loops its sample; flat one-shots do not loop; inaudible loops advance silently and resume when the listener returns"),
		("AUDIO-020", "a placed scape.omp effect that is a sentence plays its first sound once rather than as the original's placed sentence"),
		("AUDIO-021", "scape.omp places and the camera-midpoint listener share the park's sound units after undoing terrain centering; the listener midpoint is a fork lead and the Mac's placement units are not traced together"),
		("AUDIO-022", "a refused path-tool commit plays cat_ui 175; the Mac site is gated by the tool latch and its mapping to a refusal is inferred, not fully traced"),
		("AUDIO-023", "cat_ui 139's second source (a laid path cell linking to a neighbouring queue cell, 0x10082D6C) is not played; the queue tool's join and the backtrack (cat_ui 95) play"),
		("AUDIO-024", "a staff member's walking or idle voice draws once per park turn from a separate advancing host SplitMix64 stream seeded from the sound seed XOR STAF, not the Mac's shared world LCG; save/load continuity of this presentation stream is not implemented"),
		("AUDIO-025", "the December season row reads past the four-season table; this follows the fork's reading (Avg 4, Tolerance DaysOfWarning, Chance DaysBetweenChanges)"),
		("AUDIO-026", "a forecast applies on its day index DaysOfWarning after the roll, and the roll is taken on the first turn of its day; the Mac's day comparison units are not traced"),
		("AUDIO-027", "the first quality is rolled at park entry from the entry day's season and the drops start at 0; the Mac's initial roll is not traced"),
		("AUDIO-029", "thunder whose countdown computes to 0 plays at the strike; the Mac's countdown test beside 0x10103438 is not fully traced"),
		("AUDIO-030", "the weather's random draws come from the park's weather stream, not the Mac's world LCG; the seed source is not traced"),
		("AUDIO-032", "a ride's scream plays flat: the original places it at the ride's box middle, which OpenTPW's ride objects do not map to yet"),
		("AUDIO-031", "the forecast's warning is presented by an unverified OpenTPW approximation (AUDIO-905..907), and the drawn drops, snow and lightning follow the traced CRain with the approximations AUDIO-900..904; the original's forecast message handler is not traced"),
		("AUDIO-033", "the distance mapping uses minimum 2 and exponent 0.8 from the fork's QMixer lead; the packed local PC executable does not independently verify this law"),
		("AUDIO-034", "held-chain child timing, first-sound selection and child hard stop follow fork-only PC leads, not independently traced Mac routines"),
		("AUDIO-035", "crowd sample choices use an independent advancing host SplitMix64 stream seeded from the sound seed XOR CROW; the original source of crowd variation is unverified and this presentation stream is not persisted on save/load"),
		("AUDIO-036", "crowd parameter-controlled volume is evaluated when a PCM segment starts; the original propagation into already-playing sentence children is not established"),
		("AUDIO-900", "the rain and snow field follows the original CRain, but its parallax term is 0.1 x the camera's movement per frame with an assumed sign (the original's 0.1 and the 0.3 under an unidentified engine flag are not fully traced), and the blend is not traced: rain streaks blend additively, snow and bolts by alpha"),
		("AUDIO-901", "snow draws the +-1/128 jitter version only, as quads over their atlas quadrant; the original's flag-gated 128-point spline swirl (engine flag 0x115E94 bit 8) and its flake triangle mapping are not reproduced"),
		("AUDIO-902", "the bolt follows the traced CRain shape (roughness 0.6, stop test, branch odds, depth and lengths, ground at y = 0, 2000 ms life, up to 16), but the per-split displacement, the branch heading and the 500 ms colour pulse curve are OpenTPW's choices, and the ground point is the top's x and z where the original draws a uniform float"),
		("AUDIO-903", "the weather's visuals run on wall-clock frame time capped at 500 ms and freeze while the park is paused; the engine flag that zeroes the original's dt is not identified, so pause is an approximation of it"),
		("AUDIO-904", "the rain field lives within +-0.8 engine units of the eye, so the 0.1 near plane clips the nearest drops where the original's near clip is not traced, and the vertex buffer is sized once for 1024 drops and 16 bolts of 512 segments"),
		("AUDIO-905", "the forecast shows as one banner line at the top centre, drawn in the overlay pass with the original GAME8AA font above every screen (pause menus included); which surface the original warned on is unknown"),
		("AUDIO-906", "the forecast's kind classifies the pending quality by the converter's bands, the undecided snow/rain tie reads 'snow or rain' and the lightning band composes 'thunderstorms'; the wording is OpenTPW's, with no weather text in the shipped Language tables"),
		("AUDIO-907", "one message when a forecast appears and one when it applies, plus one transient current-weather line on attach shown for six seconds of simulation time; a park resumed mid-warning announces the pending forecast once; the original's cadence is not traced"),
		("AUDIO-908", "routed placed EVENT sounds sample the rendered placement origin at instruction time; native emitter anchor and update timing remain unqualified, and no scream box midpoint is inferred"),
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
