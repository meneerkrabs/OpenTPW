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
