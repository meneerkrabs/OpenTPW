namespace OpenTPW;

/// <summary>
/// The one place the park is heard from: the camera's eye in the eye camera modes, otherwise the midpoint of eye and target (tp.exe <c>0x54f8f6</c>),
/// and every voice given a range is turned down over its distance from it.
/// </summary>
public static class AudioListener
{
	/// <summary>The fork's proposed minimum distance: a voice this close is at full gain.</summary>
	// [LEAD:maexah/OpenTPW docs/exe/audio.md "The mixer's distance law"] the fork reports {2.0, range, 0.8} for QMixer; the locally available PC exe is packed, so this is not independently verified
	public const float MinimumDistance = 2f;

	/// <summary>Host distance gain: 1 inside the minimum, 0 past the range, otherwise ((range − d) / (range − 2))^0.8.</summary>
	// [APPROX:AUDIO-033] the minimum distance 2 is confirmed in both builds (PC 0x6C3B8A, Mac 0x1001858C: {2.0, max(range, 2.1 when range ≤ 2.0), rolloff}) and SetDistanceMapping(1.0) is sent (PC 0x51B6D3, Mac 0x100BA970); the gain curve itself is computed inside QMixer.dll / SoundSprocket, outside the analysable binaries: the exponent 0.8 (the fork's lead) and the missing 2.1 floor stand
	public static float RangeGain( float range, float distance )
	{
		if ( !float.IsFinite( range ) || range < 0 )
			throw new ArgumentOutOfRangeException( nameof( range ) );
		if ( float.IsNaN( distance ) || distance < 0 )
			throw new ArgumentOutOfRangeException( nameof( distance ) );
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

	/// <summary>Reinstates an already captured listener exactly when a scene transaction rolls back.</summary>
	internal static void RestoreCaptured( Vector3 position, Vector3 forward )
	{
		ValidatePosition( position, nameof( position ) );
		ValidatePosition( forward, nameof( forward ) );
		Position = position;
		Forward = forward;
	}

	public static void Update( Vector3 position, Vector3 forward )
	{
		ValidatePosition( position, nameof( position ) );
		ValidatePosition( forward, nameof( forward ) );
		Position = position;
		if ( forward.X != 0 || forward.Z != 0 )
		{
			var heading = MathF.Atan2( forward.X, forward.Z );
			Forward = new Vector3( MathF.Sin( heading ), 0, MathF.Cos( heading ) );
		}
	}

	/// <summary>The gain and pan of a source at <paramref name="position"/> with <paramref name="range"/>: the distance law over the three-axis distance, and −1..1 by the source's bearing around the forward heading.</summary>
	public static (float Gain, float Pan) Placement( Vector3 position, float range )
	{
		ValidatePosition( position, nameof( position ) );
		var offset = position - Position;
		var distance = offset.Length;
		var gain = RangeGain( range, distance );
		if ( gain <= 0 )
			return (0, 0);
		// The ground plane is the sound space's x/z; height only makes the source farther.
		var right = new Vector3( Forward.Z, 0, -Forward.X );
		var pan = range > 0 && distance > 0 ? Math.Clamp( (offset.X * right.X + offset.Z * right.Z) / distance, -1f, 1f ) : 0;
		return (gain, pan);
	}

	internal static void ValidatePosition( Vector3 position, string parameter )
	{
		if ( !float.IsFinite( position.X ) || !float.IsFinite( position.Y ) || !float.IsFinite( position.Z ) )
			throw new ArgumentOutOfRangeException( parameter );
	}
}
