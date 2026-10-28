namespace OpenTPW;

/// <summary>A sound the train loop keeps a handle of: what the original's 0x51C270 (move), 0x51BC40 (parameter) and 0x51C2C0 (stop) do to a held voice.</summary>
internal interface ICoasterTrainVoice
{
	void MoveTo( Vector3 place );
	void SetParameter( int code, int value );
	void Stop();
}

/// <summary>A held sound as a train loop's voice.</summary>
internal sealed class HeldTrainVoice : ICoasterTrainVoice
{
	private readonly HeldSound sound;
	public HeldTrainVoice( HeldSound sound ) => this.sound = sound;
	public void MoveTo( Vector3 place ) => sound.MoveTo( place );
	public void SetParameter( int code, int value ) => sound.SetParameter( code, value );
	public void Stop() => sound.Stop();
}

/// <summary>
/// The loop sound of a coaster train (SAVE-923): two sounds of the ride's EventMap (variables 0 and 1) that follow the train's sound car, one crossfaded by the train's speed and the
/// other parameterised by what the train is doing (on a lift, braking, free); the parameter codes are EventMap variables 6, 7 and 8. Sounds are presentation: they are in no hash and no save.
/// </summary>
internal sealed class CoasterTrainSounds
{
	/// <summary>The EventMap variables read at the slot setup (<c>0x55A3E0</c> with 5, 6, 7 and 8, stored at train <c>+0x54</c>, <c>+0x58</c>, <c>+0x5C</c> and <c>+0x60</c>).</summary>
	private const int LoopEventA = 0, LoopEventB = 1, FirstCodeVariable = 5;

	/// <summary>The sound states of train <c>+0x38</c> and the parameter each sends (<c>0x439780</c>).</summary>
	public const int StateSlowLift = 2, StateFastLift = 4, StateBraking = 8, StateFree = 0x10;

	private sealed class Record
	{
		/// <summary>Train +0x54 (not read again after the setup), +0x58 (the speed crossfade), +0x5C (the state parameter) and +0x60 (the occupancy parameter).</summary>
		public int CodeUnused, CodeCrossfade, CodeState, CodeOccupancy;
		/// <summary>Train +0x64 and +0x68, the handles of the two loops.</summary>
		public ICoasterTrainVoice? VoiceA, VoiceB;
		/// <summary>Train +0x48 and +0x3C: the position of the sound car and of the camera car, as the tick's car loop left them.</summary>
		public Vector3 SoundCar, CameraCar;
	}

	private readonly CoasterRide ride;
	private readonly Func<int, int?> eventVariable;
	private readonly Func<int, Vector3, ICoasterTrainVoice?> start;
	private readonly Dictionary<CoasterTrain, Record> records = new();

	/// <param name="ride">The coaster whose trains sound.</param>
	/// <param name="eventVariable">The value of the ride's EventMap child variable at a position (<c>0x55A3E0</c>), null while the ride has no EventMap script.</param>
	/// <param name="start">Submits a level <c>cat_rides</c> event at a place and keeps its voice (<c>0x51EEB0</c> → <c>0x51BFC0</c>); null for a sound that is not made.</param>
	public CoasterTrainSounds( CoasterRide ride, Func<int, int?> eventVariable, Func<int, Vector3, ICoasterTrainVoice?> start )
	{
		this.ride = ride;
		this.eventVariable = eventVariable;
		this.start = start;
	}

	/// <summary>Whether the player rides the train: its sounds then lie at the camera car (<c>0x439570</c> asks the ride camera, <c>0x42A4E0</c>).</summary>
	public Func<CoasterTrain, bool>? IsRidden { get; set; }

	/// <summary>The trains that hold a loop (tests).</summary>
	internal int SoundingTrains => records.Count;

	private int Variable( int index ) => eventVariable( index ) ?? 0;

	/// <summary>
	/// The slot setup's sound part (<c>0x4392A0</c>): the old sounds end, the parameter codes are read, the two loops start at the train's sound car and the train takes state 2 with 100.
	/// A train whose slot already holds state 2 sends no state parameter to the new loop (<c>0x43978A</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x4392A0 train sound setup] 0x439670 first (stops +0x64, +0x68 and the scream handle +0x70 and clears +0x54..+0x70 but not +0x38), then 0x55A3E0(type, 5..8) into +0x54, +0x58, +0x5C and +0x60, then 0x51EEB0(type, 0, [0x803A3C], 0 and 1, x, y, z) (event = EventMap variable 0 and 1 of the level cat_rides, at the whole units of +0x48, +0x4C, +0x50) into +0x64 and +0x68, then 0x439780(train, 2, 100)
	public void SetUp( CoasterTrain train )
	{
		TearDown( train );
		var record = new Record
		{
			CodeUnused = Variable( FirstCodeVariable ),
			CodeCrossfade = Variable( FirstCodeVariable + 1 ),
			CodeState = Variable( FirstCodeVariable + 2 ),
			CodeOccupancy = Variable( FirstCodeVariable + 3 )
		};
		records[train] = record;
		var place = Whole( record.SoundCar );
		var first = Variable( LoopEventA );
		var second = Variable( LoopEventB );
		record.VoiceA = first != 0 ? start( first, place ) : null;
		record.VoiceB = second != 0 ? start( second, place ) : null;
		SetState( train, record, StateSlowLift, 100 );
	}

	/// <summary>The sounds of a train that leaves its slot end (<c>0x439670</c>, from <c>0x43BF20</c> and the rating pass 0x43CB88); the slot's state word stays.</summary>
	public void TearDown( CoasterTrain train )
	{
		if ( !records.Remove( train, out var record ) )
			return;
		record.VoiceA?.Stop();
		record.VoiceB?.Stop();
	}

	/// <summary>The ride is removed or stopped: every loop ends.</summary>
	public void Stop()
	{
		foreach ( var record in records.Values )
		{
			record.VoiceA?.Stop();
			record.VoiceB?.Stop();
		}
		records.Clear();
	}

	/// <summary>
	/// The end of the motion tick (<c>0x43D5A6..0x43D7D8</c>): per train the positions of its sound car and camera car, then the state the train is in and the parameter value it sends.
	/// </summary>
	// [BIN:TPW-EXE:0x43D7D8 train sound state] a braking train (flag 0x10) is state 8 with 50 × (round(x⁴ of the tick's last braking hop) + 1) (0x43D76A..0x43D781); else with a car on a winch (the count of car flag 0x2, 0x43D73D) and a power factor other than 0 (0x43D78A) it is state 2 when speed / speed cap is at most 0.3 (0x6FE078) and state 4 above, with 100 × cars on winch / the train type's car count (+0x2F4); anything else is state 0x10 with 100 (0x43D7D3)
	public void StateTick()
	{
		var definition = ride.Definition;
		foreach ( var train in ride.Trains )
		{
			records.TryGetValue( train, out var record );
			if ( record != null && (record.VoiceA != null || record.VoiceB != null) )
			{
				record.SoundCar = CarPosition( train, definition.ReferenceCar );
				record.CameraCar = CarPosition( train, 0 );
			}
			int state, value;
			if ( train.BrakingNow )
			{
				state = StateBraking;
				value = 50 * ((int)MathF.Round( ride.BrakeFactor, MidpointRounding.ToEven ) + 1);
			}
			else
			{
				var lifts = 0;
				foreach ( var on in train.OnWinch )
					lifts += on ? 1 : 0;
				if ( lifts != 0 && ride.PowerFactor != 0 )
				{
					// fcomp against 0.3: C0 or C3 (at most, or unordered) is the slow state
					state = !(train.Speed / ride.SpeedCap > 0.3f) ? StateSlowLift : StateFastLift;
					value = (int)((uint)(lifts * 100) / (uint)Math.Max( 1, definition.CarCount ));
				}
				else
				{
					state = StateFree;
					value = 100;
				}
			}
			SetState( train, record, state, value );
		}
	}

	/// <summary>
	/// The sound routine (<c>0x439780</c>): a state that differs from the slot's sends its parameter to the second loop and is stored; every call then sends the occupancy value, an equal state included.
	/// </summary>
	// [BIN:TPW-EXE:0x439780 train sound state] a state other than train +0x38 maps 1, 2, 4, 8, 0x10 to 0xF, 0x1E, 0x46, 0x5A, 0x32 (the byte table 0x43982C, the jump table 0x439814; every other state sends nothing), calls 0x51BC40(+0x68, +0x5C, parameter) and stores the state; the call 0x51BC40(+0x68, +0x60, value) follows in every case (0x4397EE..0x43980C), so an equal state does not return early
	private void SetState( CoasterTrain train, Record? record, int state, int value )
	{
		if ( (uint)state != ride.TrainSoundState( train.Index ) )
		{
			var parameter = state switch
			{
				1 => 0xF,
				2 => 0x1E,
				4 => 0x46,
				8 => 0x5A,
				0x10 => 0x32,
				_ => 0
			};
			if ( parameter != 0 )
				record?.VoiceB?.SetParameter( record.CodeState, parameter );
			ride.SetTrainSoundState( train.Index, (uint)state );
		}
		record?.VoiceB?.SetParameter( record.CodeOccupancy, value );
	}

	/// <summary>
	/// After the station step (<c>0x439570</c>): the first loop takes the train's speed in percent of the fastest speed setting and the second the rest to 100, and both move to the sound car
	/// (the camera car while the player rides the train), each coordinate cut to whole units.
	/// </summary>
	// [BIN:TPW-EXE:0x439570 train sound position] ftol(train +0x78 / type +0x50 (fMaxSpeedAtMaxSetting) × 100, 0x6FDF38) goes to +0x64 as 0x51BC40(+0x64, +0x58, percent) and 100 − percent to +0x68; then 0x51C270 moves both to +0x3C (the ride camera's train, 0x42A4E0 against [[train +0x34] +0xC]) or +0x48 through ftol
	public void PositionTick()
	{
		var definition = ride.Definition;
		foreach ( var train in ride.Trains )
		{
			if ( !records.TryGetValue( train, out var record ) )
				continue;
			var percent = (int)(train.Speed / definition.MaxSpeedAtMaxSetting * 100f);
			record.VoiceA?.SetParameter( record.CodeCrossfade, percent );
			record.VoiceB?.SetParameter( record.CodeCrossfade, 100 - percent );
			var place = Whole( IsRidden?.Invoke( train ) == true ? record.CameraCar : record.SoundCar );
			record.VoiceA?.MoveTo( place );
			record.VoiceB?.MoveTo( place );
		}
	}

	/// <summary>The car's point on the track with the car type's height offset turned by the car's frame (<c>0x43C0A0</c>, <c>0x43D8F0</c>), in the sound's axes.</summary>
	private Vector3 CarPosition( CoasterTrain train, int car )
	{
		var definition = ride.Definition;
		var pose = ride.CarPose( ride.CarCoordinate( train, car ) );
		var types = definition.CarTypes.Types;
		var typeIndex = definition.CarTypeIndex( car );
		var yOffset = typeIndex >= 0 && typeIndex < types.Count ? types[typeIndex].YOffset : 0f;
		var position = CoasterCarRig.Matrix( pose, yOffset ).Translation;
		return new Vector3( position.X, position.Y, position.Z );
	}

	private static Vector3 Whole( Vector3 place ) => new( (int)place.X, (int)place.Y, (int)place.Z );
}
