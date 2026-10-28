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

/// <summary>A scream the train holds a handle of: what the original's 0x5512D0 (move), 0x551290 (level) and 0x551300 (stop) do to the voice 0x551320 or 0x551130 made.</summary>
internal interface ICoasterScreamVoice
{
	void MoveTo( Vector3 place );
	void SetLevel( int level, int speed );
	void Stop();
}

/// <summary>The one-shot scream of a train (0x551320) as a voice: it only moves.</summary>
internal sealed class ShortScreamVoice : ICoasterScreamVoice
{
	private readonly AudioVoice voice;
	public ShortScreamVoice( AudioVoice voice ) => this.voice = voice;
	public void MoveTo( Vector3 place ) => voice.Position = place;
	public void SetLevel( int level, int speed ) { }
	public void Stop() { }
}

/// <summary>The held scream chain of a train (0x551130) as a voice.</summary>
internal sealed class HeldScreamVoice : ICoasterScreamVoice
{
	private readonly SoundChain chain;
	public HeldScreamVoice( SoundChain chain ) => this.chain = chain;
	public void MoveTo( Vector3 place ) => chain.MoveTo( place );
	public void SetLevel( int level, int speed ) => chain.Parameter = GameAudio.ScreamParameter( level, speed );
	public void Stop() => chain.Stop();
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
		/// <summary>Train +0x6C (the one-shot scream) and +0x70 (the held scream), and the train flag 0x100 the held scream sets.</summary>
		public ICoasterScreamVoice? Short, Held;
		public bool HeldFlag;
	}

	private readonly CoasterRide ride;
	private readonly Func<int, int?> eventVariable;
	private readonly Func<int, Vector3, ICoasterTrainVoice?> start;
	private readonly Dictionary<CoasterTrain, Record> records = new();
	private readonly Func<Vector3, ICoasterScreamVoice?>? startShortScream;
	private readonly Func<int, Vector3, ICoasterScreamVoice?>? startHeldScream;

	/// <param name="ride">The coaster whose trains sound.</param>
	/// <param name="eventVariable">The value of the ride's EventMap child variable at a position (<c>0x55A3E0</c>), null while the ride has no EventMap script.</param>
	/// <param name="start">Submits a level <c>cat_rides</c> event at a place and keeps its voice (<c>0x51EEB0</c> → <c>0x51BFC0</c>); null for a sound that is not made.</param>
	/// <param name="startShortScream">0x551320 with band 1 and level 100 at a place; null without a sound service.</param>
	/// <param name="startHeldScream">0x551130 with a band and level 75 at a place; null without a sound service.</param>
	public CoasterTrainSounds( CoasterRide ride, Func<int, int?> eventVariable, Func<int, Vector3, ICoasterTrainVoice?> start,
		Func<Vector3, ICoasterScreamVoice?>? startShortScream = null, Func<int, Vector3, ICoasterScreamVoice?>? startHeldScream = null )
	{
		this.startShortScream = startShortScream;
		this.startHeldScream = startHeldScream;
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
		EndLoops( train );
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
	public void EndLoops( CoasterTrain train )
	{
		if ( !records.Remove( train, out var record ) )
			return;
		record.VoiceA?.Stop();
		record.VoiceB?.Stop();
		record.Held?.Stop();
	}

	/// <summary>The ride is removed or stopped: every loop ends.</summary>
	public void Stop()
	{
		foreach ( var record in records.Values )
		{
			record.VoiceA?.Stop();
			record.VoiceB?.Stop();
			record.Held?.Stop();
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
			ride.StoreTrainSoundState( train.Index, (uint)state );
		}
		record?.VoiceB?.SetParameter( record.CodeOccupancy, value );
	}

	/// <summary>
	/// After the station step (<c>0x439570</c>): the first loop takes the train's speed in percent of the fastest speed setting and the second the rest to 100, and both move to the sound car
	/// (the camera car while the player rides the train), each coordinate rounded to a whole unit.
	/// </summary>
	// [BIN:TPW-EXE:0x439570 train sound position] ftol(train +0x78 / type +0x50 (fMaxSpeedAtMaxSetting) × 100, 0x6FDF38) goes to +0x64 as 0x51BC40(+0x64, +0x58, percent) and 100 − percent to +0x68; then 0x51C270 moves both to +0x3C (the ride camera's train, 0x42A4E0 against [[train +0x34] +0xC]) or +0x48, each coordinate through fistp (0x4395EC, 0x4395FE, 0x439610: round to nearest even, unlike the _ftol of the percent and of the setup) and, when the train has riders (+0x10, 0x439640), the scream pass 0x4393E0 follows with the same place
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
			var place = Nearest( IsRidden?.Invoke( train ) == true ? record.CameraCar : record.SoundCar );
			record.VoiceA?.MoveTo( place );
			record.VoiceB?.MoveTo( place );
			if ( train.Riders != 0 )
				ScreamTick( train, record, place );
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

	/// <summary>
	/// The scream pass (<c>0x4393E0</c>) of a train with riders, at the sample the reference car stands on (fistp of train <c>+0x74</c> into the sample table, 56 bytes each): sample flag 0x2 is the one-shot scream
	/// (the rating's <see cref="CoasterSampleFlags.TerrorScream"/>, handle <c>+0x6C</c>), flag 0x4 the held chain (<see cref="CoasterSampleFlags.NormalScream"/>, handle <c>+0x70</c>, with the train flag 0x100).
	/// </summary>
	// [BIN:TPW-EXE:0x4393E0 train scream] flag 0x2: with train flag 0x100 set a live +0x6C is moved by 0x5512D0 (not while [0x785A2C] is set, 0x43942F; a flag-less sample with a live handle moves it too, 0x43945C), a missing one is started by 0x551320(1, 100, 100, place) unless the train brakes (flag 0x10) or the ride is in mode 64 (ride +8 bit 0x40); without train flag 0x100 the start is also gated by [0x785A2C] == 0 (0x4394FE); flag 0x4 (not braking, not mode 64): a missing +0x70 is started by 0x551130(0, riders / 2 + 1, 75, 75, place) when [0x785A2C] == 0 (0x439544..0x439557) and train flag 0x100 is set; a live one is moved by 0x5512D0 and set to 75 / 75 by 0x551290 (0x43949F..0x4394AF); without the flag (or braking, or mode 64) a live +0x70 is stopped by 0x551300, cleared and the flag dropped (0x4394BE..0x4394DF)
	// [BIN:TPW-EXE:0x785A2C layout replay] every sound call of the scream pass and of the loop setup is gated by [0x785A2C] (the layout-replay flag 0x404140 sets); no writer of it is modelled here, so it counts as clear
	// [BIN:TPW-EXE:0x439670 scream teardown] the slot setup stops +0x70 (0x551300) and clears +0x6C and +0x70 without stopping the one-shot; the train flag 0x100 is kept apart here (presentation, not hashed) and starts clear with the slot's sounds
	private void ScreamTick( CoasterTrain train, Record record, Vector3 place )
	{
		var samples = ride.Samples;
		var index = (int)MathF.Round( train.Position, MidpointRounding.ToEven );
		var flags = (uint)index < (uint)samples.Count ? (uint)samples[index].Flags : 0u;
		var braking = train.BrakingNow;
		var broken = ride.IsBroken;
		var shortScream = (flags & 2) != 0;
		var heldScream = (flags & 4) != 0;
		var allowed = !braking && !broken;
		if ( record.HeldFlag )
		{
			if ( shortScream )
			{
				if ( record.Short != null )
					record.Short.MoveTo( place );
				else if ( allowed )
					record.Short = startShortScream?.Invoke( place );
			}
			else
				record.Short?.MoveTo( place );
			if ( heldScream && allowed )
			{
				record.Held?.MoveTo( place );
				record.Held?.SetLevel( 75, 75 );
				return;
			}
			record.Held?.Stop();
			record.Held = null;
			record.HeldFlag = false;
			return;
		}
		if ( shortScream && allowed && record.Short == null )
			record.Short = startShortScream?.Invoke( place );
		if ( heldScream && allowed )
		{
			if ( record.Held == null )
				record.Held = startHeldScream?.Invoke( train.Riders / 2 + 1, place );
			record.HeldFlag = true;
		}
	}

	private static Vector3 Nearest( Vector3 place ) => new( MathF.Round( place.X, MidpointRounding.ToEven ), MathF.Round( place.Y, MidpointRounding.ToEven ), MathF.Round( place.Z, MidpointRounding.ToEven ) );

	private static Vector3 Whole( Vector3 place ) => new( (int)place.X, (int)place.Y, (int)place.Z );
}
