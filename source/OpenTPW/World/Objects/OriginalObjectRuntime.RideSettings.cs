namespace OpenTPW;

/// <summary>The ride's operating settings as its words hold them: SPEED (ride <c>+0x58</c>), CAPACITY (byte <c>+0x5d</c>) and DURATION (byte <c>+0x5c</c>).</summary>
public readonly record struct RideSettingWords( int Speed, int Capacity, int Duration );

public sealed partial class OriginalObjectRuntime
{
	// The ride words +0x58, +0x5d and +0x5c once a setter wrote them; null while they hold the catalogue tier's values (QueueParameters).
	private int? speedWord, capacityWord, durationWord;
	private QueueParameters? liveParameters;

	/// <summary>The queue parameters the runtime published: the catalogue tier's, with the ride words a setter wrote.</summary>
	private QueueParameters? PublishedParameters => liveParameters ?? catalogueParameters;

	/// <summary>TRACK INCOMPLETE of the status banner (<c>0x485f60</c>): a track ride whose track is not valid (<c>+0x2c</c>), or a coaster without a complete, closed circuit (<c>0x441910</c>).</summary>
	internal bool TrackIncomplete => Coaster != null ? !Coaster.IsOperational : Bump is { IsTrackValid: false };

	/// <summary>TRACK INVALID of the status banner (<c>0x485f60</c>): a coaster whose track crosses itself (controller <c>+0x140</c>, <c>0x441940</c>).</summary>
	internal bool TrackInvalid => Coaster is { IntersectionCount: > 0 };

	/// <summary>The ride's operating settings: the live SPEED, CAPACITY and DURATION words (<see cref="RideVisitorBridge.Parameters"/>).</summary>
	public RideSettingWords Settings => new( Visitors.Parameters.Speed, Visitors.Parameters.Capacity, Visitors.Parameters.Duration );

	/// <summary>Whether a setter changed a ride word away from the catalogue tier's value (the park-resume save then carries the words).</summary>
	internal bool HasSettingWords => speedWord != null || capacityWord != null || durationWord != null;

	/// <summary>
	/// The capacity setter's clamp (<c>0x4dd7f0</c>): the low byte, raised to <c>UsageInfo.MinCapacity</c>'s low byte when below it or lowered to
	/// <c>UsageInfo.MaxCapacity</c>'s when above it, only when the two sum above 0.
	/// </summary>
	// [BIN:TPW-EXE:0x4dd7f0 ride SetCapacity] byte b; when type +0x124 + type +0x128 > 0: b < MinCapacity → (byte)MinCapacity, else MaxCapacity < b → (byte)MaxCapacity (signed int compares); then script variable 2 (0x55a0b0), the log line "CAPACITY = %d" and ride +0x5d
	internal static int ClampCapacity( ObjectSettings settings, int capacity, int? raisedMaximum = null )
	{
		// [EXT:cheats-ride-vehicles] a ride whose vehicle limit the cheat raised takes a capacity up to its raised maximum, above the byte the original keeps
		if ( raisedMaximum is { } raised )
			return Math.Clamp( capacity, Math.Max( 0, settings.GetInt( "UsageInfo.MinCapacity" ) ), raised );
		var value = capacity & 0xFF;
		var min = settings.GetInt( "UsageInfo.MinCapacity" );
		var max = settings.GetInt( "UsageInfo.MaxCapacity" );
		if ( min + max <= 0 )
			return value;
		if ( value < min )
			return min & 0xFF;
		if ( max < value )
			return max & 0xFF;
		return value;
	}

	/// <summary>The duration setter's clamp (<c>0x4dd720</c>): the low byte clamped to <c>UsageInfo.MinDuration..MaxDuration</c> as bytes, with no sum test.</summary>
	// [BIN:TPW-EXE:0x4dd720 ride SetDuration] byte b: b < MinDuration (type +0x12c) → (byte)MinDuration, else MaxDuration (+0x130) < b → (byte)MaxDuration; then script variable 3, the log line "DUR = %d" and ride +0x5c
	internal static int ClampDuration( ObjectSettings settings, int duration )
	{
		var value = duration & 0xFF;
		var min = settings.GetInt( "UsageInfo.MinDuration" );
		var max = settings.GetInt( "UsageInfo.MaxDuration" );
		if ( value < min )
			return min & 0xFF;
		if ( max < value )
			return max & 0xFF;
		return value;
	}

	/// <summary>The ride's SetSpeed (<c>0x4dd6e0</c>): the script's speed bias (a signed 16-bit word) and the SPEED word.</summary>
	// [BIN:TPW-EXE:0x4dd6e0 ride SetSpeed] the script found by the ride's handle (+0x24, 0x55a070) gets its bias word +0xc0 (0x55a300), then the log line "SPEED = %d" and ride +0x58 = the speed
	public void SetSpeed( int speed )
	{
		if ( Script != null )
			Script.SpeedBias = (short)speed;
		speedWord = speed;
		PublishSettingWords();
	}

	/// <summary>The ride's SetCapacity (<c>0x4dd7f0</c>): <see cref="ClampCapacity"/>, then script variable 2 and the CAPACITY word.</summary>
	public void SetCapacity( int capacity )
	{
		var value = ClampCapacity( Entry.Settings, capacity, VehiclesRaised ? MaximumCapacity : null );
		SetVariable( nameof( RideVariables.VAR_CAPACITY ), value );
		capacityWord = value;
		PublishSettingWords();
	}

	/// <summary>The ride's SetDuration (<c>0x4dd720</c>): <see cref="ClampDuration"/>, then script variable 3 and the DURATION word.</summary>
	public void SetDuration( int duration )
	{
		var value = ClampDuration( Entry.Settings, duration );
		SetVariable( nameof( RideVariables.VAR_DURATION ), value );
		durationWord = value;
		PublishSettingWords();
	}

	/// <summary>
	/// The ride window's apply (<c>0x4af440</c>, slot +0x3c of its vtable 0x6ffbe8), which the window runs when it closes (message 0x14,
	/// <c>0x48d24a</c>) and before it steps to the next or previous object (<c>0x48caf0</c>, <c>0x48cbe0</c>): the speed, the capacity, and the
	/// duration only when the type has a <c>Info.DurationUnit</c>.
	/// </summary>
	// [BIN:TPW-EXE:0x4af440 ride window apply] SetSpeed(window +0x2c), SetCapacity(byte window +0x30), and SetDuration(byte window +0x34) when the type record's +0x70 (Info.DurationUnit) is nonzero; the window's three values come from its sliders 0x3e30, 0x3e2d and 0x3e2f (0x4af600, message 0x800)
	public void ApplyRideWindow( RideSettingWords settings )
	{
		SetSpeed( settings.Speed );
		SetCapacity( settings.Capacity );
		if ( Entry.Settings.GetInt( "Info.DurationUnit" ) != 0 )
			SetDuration( settings.Duration );
	}

	/// <summary>
	/// Takes the operating words an original save holds (<c>mOperatingSpeed</c>, <c>mOperatingCapacity</c> and <c>mOperatingDuration</c>) as the ride's SPEED, CAPACITY and DURATION.
	/// A word of 0 is left alone (the constructor and the upgrade completion write a word only from an Init value above 0, so 0 is the unset word here, which keeps the tier's value), and a
	/// word equal to the live one changes nothing. Returns the words that differed and were taken.
	/// </summary>
	// [BIN:TPW-EXE:0x4dcc6c attraction loader operating words] the loader reads mOperatingCapacity into the byte +0x5d (0x4dcc6c), mOperatingDuration into the byte +0x5c (0x4dcc92) and mOperatingSpeed into the dword +0x58 (0x4dccb8), each straight through the reader 0x424c20 / 0x4fae10 with no clamp and no setter call, so the saved words are the live ride words; the script's bias and variables 2 and 3 come back with the saved script state, which OpenTPW rebuilds from the archives ([EXT:original-engine-state-dumps]), so they are written here from the words
	internal IReadOnlyList<string> RestoreOriginalSettingWords( uint speed, int capacity, int duration )
	{
		var taken = new List<string>();
		var live = Settings;
		var speedValue = unchecked((int)speed);
		if ( speedValue > 0 && speedValue != live.Speed )
		{
			SetSpeed( speedValue );
			taken.Add( $"speed {speedValue}" );
		}
		var capacityValue = capacity & 0xFF;
		if ( capacityValue > 0 && capacityValue != live.Capacity )
		{
			SetVariable( nameof( RideVariables.VAR_CAPACITY ), capacityValue );
			capacityWord = capacityValue;
			taken.Add( $"capacity {capacityValue}" );
		}
		var durationValue = duration & 0xFF;
		if ( durationValue > 0 && durationValue != live.Duration )
		{
			SetVariable( nameof( RideVariables.VAR_DURATION ), durationValue );
			durationWord = durationValue;
			taken.Add( $"duration {durationValue}" );
		}
		if ( taken.Count > 0 )
			PublishSettingWords();
		return taken;
	}

	/// <summary>Publishes the catalogue tier's queue parameters with the ride words a setter wrote.</summary>
	private void PublishSettingWords()
	{
		var basis = catalogueParameters ?? constructorParameters;
		liveParameters = !HasSettingWords ? null
			: basis with { Speed = speedWord ?? basis.Speed, Capacity = capacityWord ?? basis.Capacity, Duration = durationWord ?? basis.Duration };
		Visitors.Parameters = liveParameters ?? basis;
	}

	/// <summary>Restores the saved ride words onto the parameters only: the script's bias and variables come back with its own snapshot.</summary>
	private void RestoreSettingWords( int? speed, int? capacity, int? duration )
	{
		speedWord = speed;
		capacityWord = capacity;
		durationWord = duration;
		PublishSettingWords();
	}
}
