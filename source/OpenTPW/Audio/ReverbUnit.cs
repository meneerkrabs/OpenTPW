namespace OpenTPW;

/// <summary>
/// The parameters of one <c>SndReverb.map</c> record in the units the original hands to its audio device: millibels for the levels, seconds for the delays and
/// the decay, an EAX 2.0 listener property set in tp.exe's word order (<see cref="FromRecord"/>).
/// </summary>
// [BIN:TPW-EXE:0x6C46B0 reverb command] the thirteen record words reach the device in the order lRoom (word 3), lRoomHF (4), flRoomRolloffFactor (11), flDecayTime (5), flDecayHFRatio (6), lReflections (7), flReflectionsDelay (8), lReverb (9), flReverbDelay (10), dwEnvironment (0), flEnvironmentSize (1), flEnvironmentDiffusion (2), flAirAbsorptionHF (12) and the flags 9 (an immediate): EAX 2.0's EAXLISTENERPROPERTIES
public readonly record struct ReverbSettings( int Environment, float Size, float Diffusion, int Room, int RoomHF, float DecayTime, float DecayHFRatio, int Reflections, float ReflectionsDelay, int Reverb, float ReverbDelay, float RoomRolloff, float AirAbsorptionHF )
{
	/// <summary>The settings of a 13-word record; null for a record of another length.</summary>
	public static ReverbSettings? FromRecord( IReadOnlyList<uint>? record )
	{
		if ( record is not { Count: SndReverbMap.RecordWords } )
			return null;
		float Single( int word ) => BitConverter.UInt32BitsToSingle( record[word] );
		return new ReverbSettings( (int)record[0], Single( 1 ), Single( 2 ), (int)record[3], (int)record[4], Single( 5 ), Single( 6 ), (int)record[7], Single( 8 ), (int)record[9], Single( 10 ), Single( 11 ), Single( 12 ) );
	}
}

/// <summary>
/// A stand-in for the listener reverb of the original sound service: a Freeverb-style network (eight damped combs and four allpasses per side, a mono send) whose
/// decay time, high-frequency decay, diffusion, size, levels and delays follow a <see cref="ReverbSettings"/> record. The original has no software reverb to port:
/// tp.exe sends the record to the audio device as an EAX 2.0 listener property set, or to a second backend whose QMixer.dll exports forward it to a COM object and
/// contain no delay lines, combs or reverb constants. The network and its constants are OpenTPW's own by design (the audio-reverb extension); the mapping from the record is the usual one
/// (comb feedback 10^(-3 D / RT60), a one-pole damping from the high-frequency decay ratio, levels from millibels).
/// </summary>
// [EXT:audio-reverb] the network, its constants, the size mapping and the voices it covers are OpenTPW's own stand-in by design (user decision 2026-10-19: "nagalm mag EXT"): the original's reverb lives in the sound device or driver behind the EAX 2.0 listener property set (tp.exe 0x6C46B0) or the COM object behind QMixer.dll (+0x10C, made at 0x1800B530), which holds no DSP, so there is nothing to port or capture statically
public sealed class ReverbUnit
{
	// Freeverb's lengths at 44,100 Hz (Jezar, public domain): they are scaled to the mixer rate and by the environment size
	private static readonly int[] CombLengths = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
	private static readonly int[] AllpassLengths = { 556, 441, 341, 225 };
	private const int StereoSpread = 23;
	private const float ReferenceRate = 44100f;
	/// <summary>The reverb return before the record's levels: the late tail at 0 mB sits this far below the dry sound.</summary>
	private const float WetScale = 0.25f;
	/// <summary>The early reflections' level before the record's.</summary>
	private const float ReflectionScale = 0.2f;
	/// <summary>Reflections nearer than this (seconds) are not separate echoes and add nothing.</summary>
	private const float MinimumReflectionDelay = 0.002f;
	private const float MinimumSettingsMillibels = -10000f;

	private readonly int rate;
	private readonly object gate = new();
	private ReverbSettings? settings;
	private Side left = new(), right = new();
	private float[] preDelay = Array.Empty<float>();
	private int preDelayPosition;
	private float[] early = Array.Empty<float>();
	private int earlyPosition;
	private (int Samples, float Gain, bool OnLeft)[] earlyTaps = Array.Empty<(int, float, bool)>();
	private float damping;
	private float allpassGain;
	private float wet;
	private float inputGain;
	private bool pending;
	private float sizeScale = -1;

	public ReverbUnit( int sampleRate ) => rate = sampleRate;

	/// <summary>One output side: eight damped combs in parallel, then four allpasses in series.</summary>
	private sealed class Side
	{
		public float[][] Combs = Array.Empty<float[]>();
		public int[] CombPositions = Array.Empty<int>();
		public float[] CombFilters = Array.Empty<float>();
		public float[] CombFeedback = Array.Empty<float>();
		public float[][] Allpasses = Array.Empty<float[]>();
		public int[] AllpassPositions = Array.Empty<int>();

		public void Build( int spread, float factor )
		{
			var count = CombLengths.Length;
			Combs = new float[count][];
			CombPositions = new int[count];
			CombFilters = new float[count];
			CombFeedback = new float[count];
			for ( var index = 0; index < count; index++ )
				Combs[index] = new float[Math.Max( 8, (int)((CombLengths[index] + spread) * factor) )];
			Allpasses = new float[AllpassLengths.Length][];
			AllpassPositions = new int[AllpassLengths.Length];
			for ( var index = 0; index < Allpasses.Length; index++ )
				Allpasses[index] = new float[Math.Max( 4, (int)((AllpassLengths[index] + spread) * factor) )];
		}

		public void SetFeedback( int rate, float decay )
		{
			for ( var index = 0; index < Combs.Length; index++ )
			{
				var seconds = Combs[index].Length / (float)rate;
				CombFeedback[index] = MathF.Min( 0.985f, MathF.Pow( 10f, -3f * seconds / decay ) );
			}
		}

		public float Run( float input, float damping, float allpassGain )
		{
			var sum = 0f;
			for ( var index = 0; index < Combs.Length; index++ )
			{
				var buffer = Combs[index];
				var position = CombPositions[index];
				var read = buffer[position];
				var filtered = read * (1 - damping) + CombFilters[index] * damping;
				CombFilters[index] = filtered;
				buffer[position] = input + filtered * CombFeedback[index];
				if ( ++position == buffer.Length )
					position = 0;
				CombPositions[index] = position;
				sum += read;
			}
			for ( var index = 0; index < Allpasses.Length; index++ )
			{
				var buffer = Allpasses[index];
				var position = AllpassPositions[index];
				var stored = buffer[position];
				buffer[position] = sum + stored * allpassGain;
				if ( ++position == buffer.Length )
					position = 0;
				AllpassPositions[index] = position;
				sum = stored - sum;
			}
			return sum;
		}
	}

	/// <summary>Whether a preset is set (the mixer skips the send altogether otherwise).</summary>
	public bool Active
	{
		get { lock ( gate ) return settings != null; }
	}

	/// <summary>The held settings, or null.</summary>
	public ReverbSettings? Settings
	{
		get { lock ( gate ) return settings; }
	}

	/// <summary>
	/// Sets the preset (null: no reverb). A change of the effective environment size (the scale after clamping) rebuilds the delay lines and drops the tail;
	/// a raw Size change that clamps to the same effective scale keeps it. Otherwise the tail is kept unless a delay length changes, which replaces that delay line.
	/// </summary>
	public void Set( ReverbSettings? value )
	{
		lock ( gate )
		{
			settings = value;
			pending = value != null;
		}
	}

	/// <summary>
	/// Adds the reverb of <paramref name="send"/> (interleaved stereo, the positioned effects' mix) to <paramref name="output"/> (the same layout) for
	/// <paramref name="frames"/> frames.
	/// </summary>
	public void Process( ReadOnlySpan<float> send, Span<float> output, int frames )
	{
		lock ( gate )
		{
			if ( settings is not { } current )
				return;
			if ( pending )
			{
				Configure( current );
				pending = false;
			}
			for ( var frame = 0; frame < frames; frame++ )
			{
				var input = (send[frame * 2] + send[frame * 2 + 1]) * 0.5f;
				// the tail's pre-delay (flReverbDelay)
				var delayed = input;
				if ( preDelay.Length > 0 )
				{
					delayed = preDelay[preDelayPosition];
					preDelay[preDelayPosition] = input;
					if ( ++preDelayPosition == preDelay.Length )
						preDelayPosition = 0;
				}
				var fed = delayed * inputGain;
				var outLeft = left.Run( fed, damping, allpassGain ) * wet;
				var outRight = right.Run( fed, damping, allpassGain ) * wet;
				if ( early.Length > 0 )
				{
					early[earlyPosition] = input;
					foreach ( var (samples, gain, onLeft) in earlyTaps )
					{
						var tap = early[(earlyPosition - samples + early.Length) % early.Length] * gain;
						if ( onLeft )
							outLeft += tap;
						else
							outRight += tap;
					}
					if ( ++earlyPosition == early.Length )
						earlyPosition = 0;
				}
				output[frame * 2] += outLeft;
				output[frame * 2 + 1] += outRight;
			}
		}
	}

	private void Configure( ReverbSettings value )
	{
		// [EXT:audio-reverb] the size scale is OpenTPW's choice: EAX's environment size (metres) stretches the room's delays, here the comb and allpass lengths by sqrt(size / 7.5), within a factor of two
		var scale = Math.Clamp( MathF.Sqrt( Math.Clamp( value.Size, 1.5f, 100f ) / 7.5f ), 0.5f, 1.5f );
		var rebuild = left.Combs.Length == 0 || scale != sizeScale;
		if ( rebuild )
		{
			sizeScale = scale;
			var factor = rate / ReferenceRate * scale;
			left.Build( 0, factor );
			right.Build( StereoSpread, factor );
		}
		// the comb feedback gives the late tail the decay time: 60 dB down after flDecayTime seconds (10^(-3 D / T) per pass of D seconds)
		var decay = Math.Clamp( value.DecayTime, 0.1f, 20f );
		left.SetFeedback( rate, decay );
		right.SetFeedback( rate, decay );
		// the high frequencies decay as fast as flDecayHFRatio times the mid-band time: a one-pole low-pass of DC gain 1 and Nyquist gain ratio in each loop
		var ratio = Math.Clamp( value.DecayHFRatio, 0.1f, 2f );
		damping = ratio >= 1 ? 0 : (1 - ratio) / (1 + ratio);
		allpassGain = Math.Clamp( 0.35f + 0.3f * Math.Clamp( value.Diffusion, 0f, 1f ), 0.35f, 0.65f );
		var millibels = Math.Max( MinimumSettingsMillibels, (float)value.Room + value.Reverb );
		wet = WetScale * MathF.Pow( 10f, millibels / 2000f );
		inputGain = 0.5f / CombLengths.Length;
		var preSamples = (int)MathF.Round( Math.Clamp( value.ReverbDelay, 0f, 0.1f ) * rate );
		if ( rebuild || preDelay.Length != preSamples )
		{
			preDelay = new float[preSamples];
			preDelayPosition = 0;
		}
		if ( value.ReflectionsDelay >= MinimumReflectionDelay && value.Reflections > MinimumSettingsMillibels )
		{
			var baseDelay = Math.Clamp( value.ReflectionsDelay, 0f, 0.3f );
			var level = ReflectionScale * MathF.Pow( 10f, (value.Room + value.Reflections) / 2000f );
			earlyTaps = new[]
			{
				((int)MathF.Max( 1, baseDelay * rate ), level, true),
				((int)MathF.Max( 2, baseDelay * 1.35f * rate ), level * 0.7f, false),
				((int)MathF.Max( 3, baseDelay * 1.8f * rate ), level * 0.5f, true),
			};
			var length = earlyTaps.Max( tap => tap.Samples ) + 1;
			if ( rebuild || early.Length != length )
			{
				early = new float[length];
				earlyPosition = 0;
			}
		}
		else
		{
			earlyTaps = Array.Empty<(int, float, bool)>();
			early = Array.Empty<float>();
		}
	}
}
