namespace OpenTPW;

internal readonly record struct ThemeParkIncForecastLine( int Kind, int Message, int Response, uint SpeechEvent, int Tag, int TextEntry )
{
	// [BIN:TPI-EXE:0x6420AF forecast dispatch] [BIN:TPI-EXE:0x729530 descriptor initialization]
	// [BIN:TPI-EXE:0x70F59E response initialization] [BIN:TPI-EXE:0x6365A0 plain caption]
	internal static ThemeParkIncForecastLine? For( int kind ) => kind switch
	{
		1 => new( 1, 0x154, 642, 763, 62, 127 ),
		2 => new( 2, 0x157, 645, 766, 65, 130 ),
		3 => new( 3, 0x15B, 649, 938, 71, 156 ),
		4 => new( 4, 0x15A, 648, 769, 68, 133 ),
		_ => null,
	};
}

/// <summary>One queued forecast: the spell kind and the score the native record holds (10,000, or -1 when its eligibility failed).</summary>
public readonly record struct ThemeParkIncForecastSlotSnapshot( int Kind, int Score );

/// <summary>One kind's last playback: the packed world word after its two-bit shift (the value the repeat check compares) and how often it has played.</summary>
public readonly record struct ThemeParkIncForecastHistorySnapshot( uint WorldTime, int Plays );

/// <summary>The forecast queue's saved state: all eight slots in queue order and the four kinds' history.</summary>
public sealed record ThemeParkIncForecastSnapshot(
	ThemeParkIncForecastSlotSnapshot[] Slots,
	ThemeParkIncForecastHistorySnapshot[] History,
	uint LastStart, uint LastWindow );

internal readonly record struct ThemeParkIncForecastTiming( int MinimumScore, uint FirstInterval, uint SecondInterval, uint ThirdInterval, uint LaterInterval )
{
	internal uint RepeatInterval( int playCount ) => playCount switch
	{
		0 => FirstInterval,
		1 => SecondInterval,
		2 => ThirdInterval,
		_ => LaterInterval,
	};
}

/// <summary>The original advisor's queue restricted to TPI's four default-score forecasts. The caller
/// supplies the eligibility predicates and both native clock domains; this class does not derive one from the other.</summary>
internal sealed class ThemeParkIncForecastQueue( ThemeParkIncForecastTiming timing )
{
	private const int ForecastScore = 10_000;
	private const uint AfterSpeechMilliseconds = 1_000;
	private readonly (ThemeParkIncForecastLine Line, int Score)?[] slots = new (ThemeParkIncForecastLine, int)?[8];
	private readonly (uint WorldTime, int Plays)[] history = new (uint, int)[4];
	private uint lastStart;
	private uint lastWindow;

	public ThemeParkIncForecastTiming Timing => timing;

	/// <summary>Occupied slots, including ineligible (-1) records, in queue order.</summary>
	internal int Count => slots.Count( slot => slot != null );

	internal ThemeParkIncForecastSnapshot Capture() => new(
		slots.Select( slot => slot is { } item ? new ThemeParkIncForecastSlotSnapshot( item.Line.Kind, item.Score ) : default ).ToArray(),
		history.Select( ( item, index ) => new ThemeParkIncForecastHistorySnapshot( item.WorldTime, item.Plays ) ).ToArray(),
		lastStart, lastWindow );

	/// <summary>Valid only rejects a present snapshot that cannot come from this model; a save without forecast advice is fine.</summary>
	internal static bool IsValid( ThemeParkIncForecastSnapshot? state ) =>
		state == null
		|| state is { Slots.Length: 8, History.Length: 4 }
		&& state.Slots.All( slot => slot is { Kind: 0, Score: 0 }
			|| (ThemeParkIncForecastLine.For( slot.Kind ) != null && slot.Score is 10_000 or -1) )
		&& state.History.All( item => item.Plays >= 0 );

	internal void Restore( ThemeParkIncForecastSnapshot? state )
	{
		if ( !IsValid( state ) )
			throw new InvalidDataException( "Invalid Theme Park Inc forecast snapshot." );
		if ( state == null )
			return;
		for ( var index = 0; index < slots.Length; index++ )
			slots[index] = state.Slots[index].Kind == 0 ? null : (ThemeParkIncForecastLine.For( state.Slots[index].Kind )!.Value, state.Slots[index].Score);
		for ( var index = 0; index < history.Length; index++ )
			history[index] = (state.History[index].WorldTime, state.History[index].Plays);
		// the window is measured on the scene clock, which starts again at 0 in every level and is not saved: a window carried over would silence the advisor
		// until the new clock passed the old one, so a restored queue starts with no voice window (LastStart and LastWindow stay in the snapshot for older files)
		(lastStart, lastWindow) = (0u, 0u);
	}

	// [BIN:TPI-EXE:0x718820 forecast enqueue] worldTime is the original packed world word shifted right by two.
	// Descriptor limit 255 cannot be reached by the eight slots. An ineligible score (-1) can still
	// occupy a slot. Full queues replace only a strictly lower score; a tie still reports success.
	internal bool Post( int kind, uint worldTime, bool eligible )
	{
		if ( ThemeParkIncForecastLine.For( kind ) is not { } line )
			return false;
		var previous = history[kind - 1];
		if ( previous.WorldTime != 0 && unchecked(worldTime - previous.WorldTime) < timing.RepeatInterval( previous.Plays ) )
			return false;
		var score = eligible ? ForecastScore : -1;
		var lowest = 0;
		for ( var index = 0; index < slots.Length; index++ )
		{
			if ( slots[index] is not { } item )
			{
				slots[index] = (line, score);
				return true;
			}
			if ( item.Score < slots[lowest]!.Value.Score )
				lowest = index;
		}
		if ( score > slots[lowest]!.Value.Score )
			slots[lowest] = (line, score);
		return true;
	}

	// [BIN:TPI-EXE:0x718190 forecast playback] The first maximum-score slot wins, the floor is strict,
	// and the default-score wrapper consumes the slot before recalculating its eligibility. The one-response
	// forecasts always resolve variant zero. Present returns the duration of the speech submission, not a voice handle.
	internal ThemeParkIncForecastLine? Tick( uint now, uint worldTime, bool eligible, Func<ThemeParkIncForecastLine, uint> present )
	{
		if ( now < unchecked(lastStart + lastWindow) || ForecastScore <= timing.MinimumScore )
			return null;
		for ( var index = 0; index < slots.Length; index++ )
		{
			if ( slots[index] is not { Score: ForecastScore, Line: var line } )
				continue;
			slots[index] = null;
			if ( !eligible )
				return null;
			var duration = present( line );
			lastStart = now;
			lastWindow = unchecked(duration + AfterSpeechMilliseconds);
			ref var previous = ref history[line.Kind - 1];
			previous = (worldTime, unchecked(previous.Plays + 1));
			return line;
		}
		return null;
	}
}
