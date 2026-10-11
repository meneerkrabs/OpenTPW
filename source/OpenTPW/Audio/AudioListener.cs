namespace OpenTPW;

/// <summary>
/// The one place the park is heard from. The original updates the listener per frame from the camera
/// (a park through the midpoint of the camera's eye and the point it looks at, a lobby at a fixed spot),
/// and every voice given a range is turned down over its distance from it.
/// </summary>
public static class AudioListener
{
	/// <summary>The mapping's minimum distance: a voice this close is at full gain (the original's 2.0, written by the release exe's voice channel set-up beside the voice's own range).</summary>
	// [BIN:TPW-EXE:QMixer distance mapping] the release tp.exe's channel set-up writes {2.0, the voice's range, 0.8} to QMixer's SetDistanceMapping (maexah/OpenTPW docs/exe/audio.md, "The mixer's distance law", measured live in the original to four places)
	public const float MinimumDistance = 2f;

	/// <summary>The distance gain of a voice of <paramref name="range"/> at <paramref name="distance"/> over all three axes: 1 at or inside the minimum, 0 at or past the range, between them the linear arm of the original's mapping, ((range − d) / (range − 2))^0.8.</summary>
	// [BIN:TPW-EXE:QMixer distance law] QMixer.dll applies the mapping as ((max − d) / (max − min))^scale over the three-axis distance with min 2.0 and scale 0.8; every channel the game enables carries the flag that selects this arm
	public static float RangeGain( float range, float distance )
	{
		if ( range <= 0 || distance <= MinimumDistance )
			return 1;
		if ( distance >= range )
			return 0;
		var fraction = (range - distance) / (range - MinimumDistance);
		return MathF.Pow( fraction, 0.8f );
	}

	/// <summary>The listener's place in the original's sound units (cell edges are 10; the thunder's bolt tops reach 300).</summary>
	public static Vector3 Position { get; private set; }

	/// <summary>The listener's ground heading, normalised; sources to its right pan right.</summary>
	public static Vector3 Forward { get; private set; } = new( 0, 0, 1 );

	public static void Update( Vector3 position, Vector3 forward )
	{
		Position = position;
		if ( forward.LengthSquared > 0 )
			Forward = forward.Normal;
	}

	/// <summary>The gain and pan of a source at <paramref name="position"/> with <paramref name="range"/>: the distance law over the three-axis distance, and −1..1 by the source's bearing around the forward heading.</summary>
	public static (float Gain, float Pan) Placement( Vector3 position, float range )
	{
		var offset = position - Position;
		var distance = offset.Length;
		var gain = RangeGain( range, distance );
		if ( gain <= 0 )
			return (0, 0);
		// The ground plane is the sound space's x/z; height only makes the source farther.
		var right = new Vector3( Forward.Z, 0, -Forward.X );
		var pan = range > 0 ? Math.Clamp( (offset.X * right.X + offset.Z * right.Z) / distance, -1f, 1f ) : 0;
		return (gain, pan);
	}
}
