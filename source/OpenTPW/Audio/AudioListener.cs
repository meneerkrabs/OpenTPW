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

	// [BIN:TPW-EXE:0x6C3B8A distance mapping] the source's mapping is {2.0, max(range, 2.1 when range ≤ 2.0), rolloff} (0x6bc650's range against the constant 2.0 at 0x70a9c4, 0x40066666 = 2.1f when it is not above it), handed to QSWaveMixSetDistanceMapping (0x6D2930, 0x51B6D3 sends the global mapping 1.0)
	// [BIN:QMIXER-DLL:0x1800a0d0 distance gain] the channel parameter routine tests the channel's enable word (params +0x5c = channel +0x60, stored by the enable message 0x18007a11 from QSWaveMixEnableChannel 0x180014c0; tp.exe builds that word at 0x6d22d0 and always ORs 0x1000, 0x6d230e): with flag 0x1000 the gain is 1 up to the minimum distance (or for rolloff 0), 0 past the maximum and ((max - d) / (max - min)) to the power of the rolloff between, rolloff 1.0 being linear (0x1800a0bb-0x1800a172)
	/// <summary>The exponent of the distance law: 0.2, the rolloff of a sound system whose 3D comes from the QMixer layer.</summary>
	// [BIN:TPW-EXE:0x6b9bb0 rolloff] the float of the sound system object (0xfb1f24, built by 0x6b8ed0, vtable 0x70a5d8 +0x2c) is 0.2 (0x70a624) when the object's flag 0x40 (+0x28) is set and 0.8 (0x70a628) otherwise
	// [BIN:TPW-EXE:0x6b9006 rolloff flag] the flag is set when bit 0x100 of the creation descriptor survives the capability filter 0x6b7d70 (the descriptor is ORed with 0x300 at 0x6b8fe8); the filter clears it only when the driver object's slot +0x20 is set or its virtual +0x4c reports no native 3D (0x6b7d80-0x6b7d9d)
	// [BIN:TPW-EXE:0x5d1400 native 3D] the driver object is built by 0x6c4190: first the QMixer class (constructor 0x6d2d70, vtable 0x711b08), whose +0x4c is 'mov al, 1; ret' (0x5d1400), and only when that class fails to initialise the fallback class (0x6d1e30); with QMixer the software 3D layer 0x6b7750 (slot +0x20, made only when +0x4c reports false, 0x6b75c5) does not exist, so the flag is set and the rolloff is 0.2
	public const float Rolloff = 0.2f;

	/// <summary>Host distance gain: 1 inside the minimum, 0 past the far end (the range, at least <see cref="MinimumFarDistance"/>), otherwise ((far − d) / (far − 2))^<see cref="Rolloff"/>.</summary>
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
		return MathF.Pow( fraction, Rolloff );
	}

	/// <summary>The listener's place in the original's sound units (cell edges are 10; the thunder's bolt tops reach 300).</summary>
	public static Vector3 Position { get; private set; }

	/// <summary>The listener's ground heading, normalised; sources to its right pan right.</summary>
	public static Vector3 Forward { get; private set; } = new( 0, 0, 1 );

	/// <summary>The listener's front axis in sound space (x right, y up, z ahead), normalised: the camera's view direction, pitch included.</summary>
	public static Vector3 Front { get; private set; } = new( 0, 0, 1 );

	/// <summary>The listener's up axis in sound space, normalised.</summary>
	public static Vector3 Up { get; private set; } = new( 0, 1, 0 );

	/// <summary>Reinstates an already captured listener exactly when a scene transaction rolls back.</summary>
	internal static void RestoreCaptured( Vector3 position, Vector3 forward )
	{
		ValidatePosition( position, nameof( position ) );
		ValidatePosition( forward, nameof( forward ) );
		Position = position;
		Forward = forward;
		Front = forward;
		Up = new Vector3( 0, 1, 0 );
	}

	/// <summary>Reinstates the captured camera axes after <see cref="RestoreCaptured"/>.</summary>
	internal static void RestoreCapturedAxes( Vector3 front, Vector3 up )
	{
		ValidatePosition( front, nameof( front ) );
		ValidatePosition( up, nameof( up ) );
		Front = front;
		Up = up;
	}

	/// <summary>
	/// Moves the listener. <paramref name="forward"/> is the view direction in sound space; without <paramref name="up"/> only its ground heading
	/// matters (the listener stays upright), with it the listener takes the camera's full orientation as the original does.
	/// </summary>
	// [BIN:TPW-EXE:0x54f8f6 listener] the park loop hands QMixer the position and the camera matrix's rows 0x7909b0 (front) and 0x7909a0 (up) through 0x51c1d0, pitch included
	// [BIN:QMIXER-DLL:0x1800ce70 listener basis] both axes are normalised, right = up x front and up' = front x right; a source's offset is rotated into that basis (0x1800cf10: x = offset . right, y = offset . up', z = offset . front) before the azimuth is read
	public static void Update( Vector3 position, Vector3 forward, Vector3? up = null )
	{
		ValidatePosition( position, nameof( position ) );
		ValidatePosition( forward, nameof( forward ) );
		if ( up is { } upward )
			ValidatePosition( upward, nameof( up ) );
		Position = position;
		if ( forward.X != 0 || forward.Z != 0 )
		{
			var heading = MathF.Atan2( forward.X, forward.Z );
			Forward = new Vector3( MathF.Sin( heading ), 0, MathF.Cos( heading ) );
		}
		if ( up is { } axis && forward.Length > 0 && axis.Length > 0 )
		{
			Front = forward.Normal;
			Up = axis.Normal;
		}
		else
		{
			Front = Forward;
			Up = new Vector3( 0, 1, 0 );
		}
	}

	/// <summary>
	/// The DirectSound pan of an azimuth: 44.444 hundredths of a decibel per degree, mirrored behind the listener so a source at +-180 degrees is as
	/// centred as one straight ahead, rounded to an integer.
	/// </summary>
	// [BIN:QMIXER-DLL:0x18005730 pan law] the azimuth is wrapped into (-180, 180], a value above 90 becomes 180 - a and one below -90 becomes -180 - a, then the float 44.4444 (0x1802e2f8) multiplies it and fistp rounds to even
	public static int DirectSoundPan( float azimuthDegrees )
	{
		var azimuth = azimuthDegrees;
		while ( azimuth > 180 )
			azimuth -= 360;
		while ( azimuth < -180 )
			azimuth += 360;
		if ( azimuth > 90 )
			azimuth = 180 - azimuth;
		else if ( azimuth < -90 )
			azimuth = -180 - azimuth;
		return (int)MathF.Round( azimuth * 44.44444274902344f, MidpointRounding.ToEven );
	}

	/// <summary>The amplitude DirectSound leaves a channel after a volume of <paramref name="hundredthsOfDecibel"/> (at most 0).</summary>
	public static float DecibelGain( int hundredthsOfDecibel ) => hundredthsOfDecibel <= -10000 ? 0 : MathF.Pow( 10f, hundredthsOfDecibel / 2000f );

	/// <summary>
	/// The volume the original sets for a gain: 2000 log10(gain) in hundredths of a decibel rounded to the nearest integer, 0 for a gain of exactly 1,
	/// -10000 (silence) for a gain of 0 or less.
	/// </summary>
	// [BIN:QMIXER-DLL:0x1800ca40 gain to decibels] gain == 1 gives 0, gain <= 0 gives -10000, otherwise fistp(602.06 x log2(gain)) (602.06 = 2000 x log10(2), 0x1802e658)
	public static int GainHundredthsOfDecibel( float gain )
	{
		if ( gain == 1f )
			return 0;
		if ( gain <= 0f )
			return -10000;
		return (int)MathF.Round( (float)(602.0599913279623 * Math.Log2( gain )), MidpointRounding.ToEven );
	}

	/// <summary>The amplitude left and right of a pan: the louder side keeps the level, the other side is 10^(-|pan| / 2000) of it.</summary>
	// [BIN:QMIXER-DLL:0x18005830 volume and pan] a mono channel of the software stage gets SetVolume(dB of the gain, +0x3c) and SetPan(pan, +0x40); DirectSound's pan turns only the opposite channel down by |pan| hundredths of a decibel
	public static (float Left, float Right) PanGains( int pan )
	{
		var quieter = DecibelGain( -Math.Abs( pan ) );
		return pan > 0 ? (quieter, 1f) : pan < 0 ? (1f, quieter) : (1f, 1f);
	}

	/// <summary>The listener-relative azimuth in degrees of <paramref name="offset"/> (a source's place minus the listener's): atan2(x, z) in the listener's basis, 0 straight ahead, positive to the right.</summary>
	public static float Azimuth( Vector3 offset )
	{
		var right = Vector3.Cross( Up, Front );
		var x = Vector3.Dot( offset, right );
		var z = Vector3.Dot( offset, Front );
		return MathF.Atan2( x, z ) * (180f / MathF.PI);
	}

	/// <summary>The gain and the DirectSound pan (hundredths of a decibel, -4000..4000, positive to the right) of a source at <paramref name="position"/> with <paramref name="range"/>: the distance law over the three-axis distance and the azimuth's pan.</summary>
	public static (float Gain, int Pan) Placement( Vector3 position, float range )
	{
		ValidatePosition( position, nameof( position ) );
		var offset = position - Position;
		var distance = offset.Length;
		var gain = RangeGain( range, distance );
		if ( gain <= 0 )
			return (0, 0);
		return (gain, range > 0 ? DirectSoundPan( Azimuth( offset ) ) : 0);
	}

	internal static void ValidatePosition( Vector3 position, string parameter )
	{
		if ( !float.IsFinite( position.X ) || !float.IsFinite( position.Y ) || !float.IsFinite( position.Z ) )
			throw new ArgumentOutOfRangeException( parameter );
	}
}
