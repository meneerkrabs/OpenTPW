namespace OpenTPW;

/// <summary>
/// The one place the park is heard from: the camera's eye in the eye camera modes, otherwise the midpoint of eye and target (tp.exe <c>0x54f8f6</c>),
/// and every voice given a range is turned down over its distance from it.
/// </summary>
public static class AudioListener
{
	/// <summary>The original's minimum distance (tp.exe stores 2.0 at <c>0x6C3BB9</c>): a voice this close is at full gain.</summary>
	// [BIN:TPW-EXE:0x6C3B8A distance mapping] the minimum distance is the constant 2.0 (0x70a9c4) the mapping is built with; the law that uses it is read in QMixer.dll (below)
	public const float MinimumDistance = 2f;

	/// <summary>The far end the original hands its mixer for a range of 2 or less (tp.exe <c>0x6C3B8A</c> stores 2.1 there, so the far end is always beyond the minimum distance).</summary>
	public const float MinimumFarDistance = 2.1f;

	/// <summary>Host distance gain: 1 inside the minimum, 0 past the far end (the range, at least <see cref="MinimumFarDistance"/>), otherwise ((far − d) / (far − 2))^0.8.</summary>
	// [BIN:TPW-EXE:0x6C3B8A distance mapping] the source's mapping is {2.0, max(range, 2.1 when range ≤ 2.0), rolloff} (0x6bc650's range against the constant 2.0 at 0x70a9c4, 0x40066666 = 2.1f when it is not above it), handed to QSWaveMixSetDistanceMapping (0x6D2930, 0x51B6D3 sends the global mapping 1.0)
	// [BIN:QMIXER-DLL:0x1800a0d0 distance gain] the channel parameter routine tests the channel's enable word (params +0x5c = channel +0x60, stored by the enable message 0x18007a11 from QSWaveMixEnableChannel 0x180014c0; tp.exe builds that word at 0x6d22d0 and always ORs 0x1000, 0x6d230e): with flag 0x1000 the gain is 1 up to the minimum distance (or for rolloff 0), 0 past the maximum and ((max - d) / (max - min)) to the power of the rolloff between, rolloff 1.0 being linear (0x1800a0bb-0x1800a172)
	// [APPROX:AUDIO-033] the rolloff value is the float the sound system object (0xfb1f24) returns from its vtable +0x2c at tp.exe 0x6c3b8a-0x6c3bb2; that method is not identified, so the fork's exponent 0.8 stands
	public static float RangeGain( float range, float distance )
	{
		if ( !float.IsFinite( range ) || range < 0 )
			throw new ArgumentOutOfRangeException( nameof( range ) );
		if ( float.IsNaN( distance ) || distance < 0 )
			throw new ArgumentOutOfRangeException( nameof( distance ) );
		if ( range <= 0 || distance <= MinimumDistance )
			return 1;
		var far = range > MinimumDistance ? range : MinimumFarDistance;
		if ( distance >= far )
			return 0;
		var fraction = (far - distance) / (far - MinimumDistance);
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
