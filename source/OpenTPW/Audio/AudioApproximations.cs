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
		("AUDIO-017", "the lobby island one-shots (cat_locallobbysfx 3, 4, 5) are timed by a uniform 5-15 s gap; the original's timer distribution is not traced"),
		("AUDIO-018", "the constant-power pan of a positioned voice follows the source's bearing; the original's pan law (QSound's processing) is not read"),
		("AUDIO-019", "a positioned one-shot whose event has flag 0x8 loops its sample; flat one-shots do not loop; inaudible loops advance silently and resume when the listener returns"),
		("AUDIO-020", "a placed scape.omp effect that is a sentence plays its first sound once rather than as the original's placed sentence"),
		("AUDIO-021", "scape.omp places and the camera-midpoint listener share the park's sound units after undoing terrain centering; the listener midpoint is a fork lead and the Mac's placement units are not traced together"),
		("AUDIO-022", "a refused path-tool commit plays cat_ui 175; the Mac site is gated by the tool latch and its mapping to a refusal is inferred, not fully traced"),
		("AUDIO-023", "the path tool's link-end sound (cat_ui 139) is not played; its trigger is inferred and no undo sound is traced"),
		("AUDIO-024", "a staff member's walking or idle voice draws once per park turn from a separate advancing host SplitMix64 stream seeded from the sound seed XOR STAF, not the Mac's shared world LCG; save/load continuity of this presentation stream is not implemented"),
		("AUDIO-025", "the December season row reads past the four-season table; this follows the fork's reading (Avg 4, Tolerance DaysOfWarning, Chance DaysBetweenChanges)"),
		("AUDIO-026", "a forecast applies on its day index DaysOfWarning after the roll, and the roll is taken on the first turn of its day; the Mac's day comparison units are not traced"),
		("AUDIO-027", "the first quality is rolled at park entry from the entry day's season and the drops start at 0; the Mac's initial roll is not traced"),
		("AUDIO-029", "thunder whose countdown computes to 0 plays at the strike; the Mac's countdown test beside 0x10103438 is not fully traced"),
		("AUDIO-030", "the weather's random draws come from the park's weather stream, not the Mac's world LCG; the seed source is not traced"),
		("AUDIO-032", "a ride's scream plays flat: the original places it at the ride's box middle, which OpenTPW's ride objects do not map to yet"),
		("AUDIO-031","the forecast's warning is not shown, and the drawn rain drops, snow and lightning bolt are unverified procedural approximations (AUDIO-900..904); the original's weather renderer is not traced"),
		("AUDIO-033", "the distance mapping uses minimum 2 and exponent 0.8 from the fork's QMixer lead; the packed local PC executable does not independently verify this law"),
		("AUDIO-034", "held-chain child timing, first-sound selection and child hard stop follow fork-only PC leads, not independently traced Mac routines"),
		("AUDIO-035", "crowd sample choices use an independent advancing host SplitMix64 stream seeded from the sound seed XOR CROW; the original source of crowd variation is unverified and this presentation stream is not persisted on save/load"),
		("AUDIO-036", "crowd parameter-controlled volume is evaluated when a PCM segment starts; the original propagation into already-playing sentence children is not established"),
		("AUDIO-900", "the rain is drawn as one camera-following wrapping drop field with straight falling streaks, its count the simulated current drops; the original's drop volume, speed and streak shape (FUN_0057d300) are not traced"),
		("AUDIO-901", "snow reuses the rain's drop count but falls slower with a sinusoidal sway; the original's snow motion is not traced"),
		("AUDIO-902", "the bolt is a jittered polyline from the strike's top (height 300) to the heightfield, shown for a fixed flash time at the strike; the original's bolt shape and flash timing are not traced"),
		("AUDIO-903", "the weather's visuals advance with the frame delta scaled by the simulation speed, so a paused park freezes drops and bolts mid-flight; the original's pause behaviour for the weather renderer is not traced"),
		("AUDIO-904", "the visual field, its vertex buffer and the pending bolt queue are capped (600 drops, 4 flashes, 64 pending tops), so a level with a higher MaxRaindrops draws fewer drops than it simulates"),
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
