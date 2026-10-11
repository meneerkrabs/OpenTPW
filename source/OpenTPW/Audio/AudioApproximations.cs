namespace OpenTPW;

/// <summary>Every unproven sound rule (docs/AUDIO.md "Approximation register"); logged once when the sound service starts.</summary>
public static class AudioApproximations
{
	public static readonly (string Id, string Rule)[] All =
	{
		("AUDIO-001", "the second user volume the original ducks during speech is the effects channel"),
		("AUDIO-002", "banks resolve to <map folder's parent>/<name>HD.sdt, then to global/<name>HD.sdt"),
		("AUDIO-003", "pitch, delay, 3D position and reverb of a sound are not applied"),
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
		("AUDIO-016", "a scream's speed is the loader's default 50; the RSE loader's speed field beside RideVM's timing is not traced"),
		("AUDIO-017", "the lobby island one-shots (cat_locallobbysfx 3, 4, 5) are timed by a uniform 5-15 s gap; the original's timer distribution is not traced"),
		("AUDIO-018", "the constant-power pan of a positioned voice follows the source's bearing; the original's pan law (QSound's processing) is not read"),
		("AUDIO-019", "a positioned one-shot whose event has flag 0x8 loops its sample; a flat one-shot with 0x8 is not looped"),
		("AUDIO-020", "a placed scape.omp effect that is a sentence plays its first sound once rather than as the original's placed sentence"),
		("AUDIO-021", "scape.omp places and the listener share the park's sound units; the Mac's placement units are not traced in one place"),
		("AUDIO-022", "a refused path-tool commit plays cat_ui 175; the Mac site is gated by the tool latch and its mapping to a refusal is inferred, not fully traced"),
		("AUDIO-023", "the path tool's link-end sound (cat_ui 139) is not played; its trigger is inferred and no undo sound is traced"),
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
