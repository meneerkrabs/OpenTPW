namespace OpenTPW;

/// <summary>
/// [EXT:winter-snow-cover] The park's settled snow (docs/WINTER.md, <c>opentpw.winter-snow</c>): one scalar 0 to 1 that rises while it snows and melts afterwards, stepped once per park turn
/// from the weather's own state, so it freezes when the park is paused, runs with the park speed and is a pure function of the weather's turn sequence. Presentation only: it reads
/// <see cref="ParkWeather"/>, writes nothing back, draws from no random stream and is in no hash; a park save keeps it as an optional unhashed field (<see cref="ParkResumeSnapshot.SnowCover"/>)
/// so a resumed park keeps its snow.
/// </summary>
// [EXT:winter-snow-cover] OpenTPW's own presentation; tp.exe has no ground snow (docs/WINTER.md: its only snow strings are the two weather band keys and the flake texture of CRain)
internal sealed class SnowCover
{
	/// <summary>
	/// Cover gained per park turn at the full drop count (<c>CurrentDrops == MaxRaindrops</c>): full cover after about 1.5 game days (a day is 23.04 turns) of the heaviest snow, so a spell of
	/// the shipped hallow December (40 real seconds, 160 turns, drops around two thirds of the cap) covers the park completely.
	/// </summary>
	public const double Accumulate = 1.0 / 35.0;

	/// <summary>Cover lost per dry park turn: gone after about 3 game days, so the ground is bare again some 17 real seconds after a spell.</summary>
	public const double Melt = 1.0 / 70.0;

	/// <summary>How many times faster rain melts the cover than a dry sky does.</summary>
	public const double RainMeltFactor = 3.0;

	/// <summary>The drawn cover eases toward the turn value at this rate (1 / second), so the 4 Hz turn steps do not show as steps.</summary>
	public const double EaseRate = 3.0;

	/// <summary>The cover of the last park turn, 0 (bare) to 1 (white).</summary>
	public double Cover { get; private set; }

	/// <summary>The cover the renderer draws this frame: <see cref="Cover"/> eased per frame (<see cref="Ease"/>).</summary>
	public double Drawn { get; private set; }

	/// <summary>A cover that starts at <paramref name="cover"/> (a resumed park); the drawn value starts there too.</summary>
	public SnowCover( double cover = 0 )
	{
		Cover = Drawn = Math.Clamp( double.IsFinite( cover ) ? cover : 0, 0, 1 );
	}

	/// <summary>
	/// One park turn: while it snows with drops falling the cover rises by <see cref="Accumulate"/> times the share of the drop cap; otherwise it melts by <see cref="Melt"/> (three times faster
	/// while it rains). Clamped to 0..1.
	/// </summary>
	public void Step( bool snowing, bool raining, int drops, int maximumDrops )
	{
		if ( snowing && drops > 0 && maximumDrops > 0 )
			Cover += Accumulate * Math.Min( drops, maximumDrops ) / maximumDrops;
		else
			Cover -= Melt * ( raining ? RainMeltFactor : 1.0 );
		Cover = Math.Clamp( Cover, 0, 1 );
	}

	/// <summary>Steps from the park's weather (the weather's turn event).</summary>
	public void Step( ParkWeather weather ) => Step( weather.Snow, weather.Rain, weather.CurrentDrops, weather.MaxRaindrops );

	/// <summary>Moves the drawn cover toward <see cref="Cover"/> by <paramref name="seconds"/> of real time (0 or less: no move).</summary>
	public void Ease( double seconds )
	{
		if ( !( seconds > 0 ) )
			return;
		Drawn += ( Cover - Drawn ) * ( 1.0 - Math.Exp( -EaseRate * seconds ) );
		if ( Math.Abs( Cover - Drawn ) < 1e-4 )
			Drawn = Cover;
	}

	/// <summary>Sets the cover and the drawn value at once (<c>--debug-snow-cover</c>, a resume).</summary>
	public void Set( double cover )
	{
		Cover = Drawn = Math.Clamp( double.IsFinite( cover ) ? cover : 0, 0, 1 );
	}

	/// <summary>Whether a saved cover is one a park may hold: finite and above 0, up to 1 (<see cref="Level.CaptureResumeSnapshot"/> writes it only while it is above 0).</summary>
	public static bool IsValidSavedCover( double cover ) => double.IsFinite( cover ) && cover > 0 && cover <= 1;

	/// <summary>The surface factor of a path (a negative value reads as slush in test.shader): paths under snow turn wet and grey instead of white.</summary>
	// [EXT:winter-snow-paths] the user's decision (2026-10-18): paths under snow turn to slush so they stay readable
	public const float PathSurface = -0.35f;

	/// <summary>The surface factor of ground, roofs and other up-facing surfaces.</summary>
	public const float GroundSurface = 1f;
}
