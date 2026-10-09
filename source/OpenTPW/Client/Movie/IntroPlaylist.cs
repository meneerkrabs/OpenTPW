namespace OpenTPW;

/// <summary>
/// The movies the original shows at start-up, in order: the Bullfrog logo, then one trailer chosen by the day of
/// the month. The trailer table is the eight remaining movies in alphabetical order, indexed by the day of the
/// month modulo 8 ([BIN:STP-PPC:0x101C0F40 daily start-up movie], docs/TGQ-MOVIES.md).
/// </summary>
public static class IntroPlaylist
{
	/// <summary>The movie played first at every start-up.</summary>
	public const string Logo = "bf";

	/// <summary>Trailers by <c>day of month % 8</c> (the original's table order: bub, buc, grav, jug, mir, plan, roc, roll).</summary>
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
/// Skip decision of the start-up movies. The original samples the skip inputs (Esc, Space, mouse button) before
/// starting each movie and while it plays, so an input still held after skipping one movie also skips the next.
/// Input already held when the sequence begins is ignored until released [APPROX:UI-035], so launching the game
/// with a button down does not drop the intro.
/// </summary>
public sealed class IntroSkipGate
{
	private bool armed;

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
