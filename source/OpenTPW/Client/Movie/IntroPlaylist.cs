namespace OpenTPW;

/// <summary>
/// The movies the original shows at start-up, in order: the Bullfrog logo, then one trailer chosen by the day of
/// the month. The trailer table is the eight remaining movies in alphabetical order, indexed by the day of the
/// month modulo 8 ([BIN:TPW-EXE:0x54e070 daily start-up movie], docs/TGQ-MOVIES.md). These are the only two movie triggers of the
/// PC build: its movie start 0x51b010 has exactly these two callers, 0x54df20 (the logo) and 0x54e070 (the trailer), both called from
/// the start-up states 5 and 7 of the main state machine 0x54e360.
/// </summary>
public static class IntroPlaylist
{
	/// <summary>Automation captures count world frames; any nonempty environment override bypasses intros.</summary>
	internal static bool ShouldPlay( bool frontEndRun, bool smoke, IReadOnlyCollection<string> args, string? noIntroEnvironment ) =>
		frontEndRun && !smoke && !args.Contains( "--no-intro" ) && !args.Contains( "--capture-world" ) && string.IsNullOrEmpty( noIntroEnvironment );

	/// <summary>The movie played first at every start-up.</summary>
	public const string Logo = "bf";

	/// <summary>
	/// Trailers by <c>day of month % 8</c> (the original's table order: bub, buc, grav, jug, mir, plan, roc, roll).
	/// </summary>
	// [BIN:TPW-EXE:0x54e0ce movie table] localtime() tm_mday (0x67d8e0, +0xC), made non-negative, & 7, indexes the jump table 0x54e288: 0 bub (0x7650a0), 1 buc (0x76508c), 2 grav (0x765074), 3 jug (0x765060), 4 mir (0x76504c), 5 plan (0x765034), 6 roc (0x765020), 7 roll (0x765008); the logo is bf (0x764ff4)
	public static IReadOnlyList<string> Trailers { get; } = ["bub", "buc", "grav", "jug", "mir", "plan", "roc", "roll"];

	public static string TrailerForDay( int dayOfMonth )
	{
		if ( dayOfMonth is < 1 or > 31 )
			throw new ArgumentOutOfRangeException( nameof( dayOfMonth ), "A day of the month is 1 to 31." );
		return Trailers[dayOfMonth % Trailers.Count];
	}

	/// <summary>The start-up sequence for the given local date.</summary>
	public static IReadOnlyList<string> For( DateTime localDate ) => [Logo, TrailerForDay( localDate.Day )];
}

/// <summary>
/// Skip decision of the start-up movies. Before each movie (the Bullfrog logo, then the trailer) the original waits
/// until Esc, Space and every mouse button (left, right and middle) are released
/// ([BIN:TPW-EXE:0x54e360 states 5-8]); an input held while a movie plays stops it. An input still held after one
/// movie was skipped therefore delays the next movie instead of skipping it, and input held at launch is ignored
/// until released, so launching the game with a button down does not drop the intro.
/// </summary>
public sealed class IntroSkipGate
{
	private bool armed;

	/// <summary>True once the skip inputs were seen released since the last <see cref="Rearm"/>; a movie may start only then.</summary>
	public bool Armed => armed;

	/// <summary>Called when a movie ends: the next one waits for the skip inputs to be released.</summary>
	public void Rearm() => armed = false;

	/// <summary>True when the movie that is about to start, or is playing, must end now.</summary>
	public bool Poll( bool skipInputDown )
	{
		if ( !armed )
		{
			armed = !skipInputDown;
			return false;
		}
		return skipInputDown;
	}
}
