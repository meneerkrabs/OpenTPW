namespace OpenTPW;

/// <summary>Every unproven sound rule (docs/AUDIO.md "Approximation register"); logged once when the sound service starts.</summary>
public static class AudioApproximations
{
	public static readonly (string Id, string Rule)[] All =
	{
		("AUDIO-001", "the second user volume the original ducks during speech is the effects channel"),
		("AUDIO-002", "banks resolve to <map folder's parent>/<name>HD.sdt, then to global/<name>HD.sdt"),
		("AUDIO-003", "pitch, delay, 3D position and reverb of a sound are not applied"),
		("AUDIO-004", "the park view's 0xBD click modifier is not mapped to a key"),
		("AUDIO-005", "a sentence's next segment is chosen when the current one starts"),
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
