namespace OpenTPW;

/// <summary>
/// Theme Park Inc's monthly analyser measurements, adapted to the host's fixed-capacity statistics state. The bounded named-field
/// walk follows saved counts, never TPW offsets. This imports relative history, not a native saved-clock or economy continuation.
/// </summary>
public static class SaveThemeParkIncStatistics
{
	public const int MonthlyCapacity = 144, RecentStayCapacity = 50;

	/// <summary>Decodes the entire selected bundle before exposing it; a zero world reference means no history.</summary>
	// [BIN:TPI-EXE:0x64EAC0 analyser lookup] the world names the model-13 analyser; an unrelated PARK record is not a substitute for a zero reference
	// [BIN:TPI-EXE:0x5ACE30 analyser serializer] twenty-one variable-count word rings follow four short rings and six totals; serializer order, not runtime memory stride, determines their named paths
	// [BIN:TPI-EXE:0x5AF220 monthly producer] ordinals 14/17/18/19/20 are people/bus arrivals/float happiness/recent-stay mean/rating; admission counter mVisitorsThisMonth instead feeds ordinal 15
	// [BIN:TPI-EXE:0x5AF080 recent stays] departing paid guests append whole park days after saved-calendar-rate conversion; 0x5BC4A0 sums the newest at most 50 signed entries for the monthly truncated mean
	// [BIN:TPI-EXE:0x5630B0 graph dispatch] the traced native toggles select only arrivals/people/rating, each through the integer reader 0x4F6990; happiness/stay are separately proven stored measurements
	public static NativeParkStatistics? Read( ThemeParkIncSaveWorld world )
	{
		ArgumentNullException.ThrowIfNull( world );
		var analyserId = world.Variables.ParkAnalyser;
		if ( analyserId == 0 )
			return null;
		if ( world.Find( analyserId ) is not { Model: ThemeParkIncThingModel.ParkAnalyser } thing )
			throw new InvalidDataException( "The referenced Theme Park Inc park analyser is missing or has the wrong model." );
		var body = new ThemeParkIncBody( thing.Model, world.BodyOf( thing ) );
		var people = ReadRing( body, "mDayRings[14]", MonthlyCapacity, false, "people" );
		var arrivals = ReadRing( body, "mDayRings[17]", MonthlyCapacity, false, "arrivals" );
		var happiness = ReadRing( body, "mDayRings[18]", MonthlyCapacity, true, "happiness" );
		var stay = ReadRing( body, "mDayRings[19]", MonthlyCapacity, false, "stay" );
		var rating = ReadRing( body, "mDayRings[20]", MonthlyCapacity, false, "rating" );
		var recentStays = ReadRing( body, "ring50", RecentStayCapacity, false, "recent stays" );
		// [EXT:tpi-history-host-profile] host saves require a nonnegative live arrivals counter; signed historical samples remain unclamped
		if ( arrivals.Temp < 0 )
			throw new InvalidDataException( "Theme Park Inc analyser live arrivals must be nonnegative for host continuation." );
		return new NativeParkStatistics( people, arrivals, happiness, stay, rating, recentStays );
	}

	private static NativeStatisticsRing ReadRing( ThemeParkIncBody body, string path, int requiredCapacity, bool floating, string name )
	{
		// The layout already bounds each count by its native maximum and the whole record by this thing's exact body range.
		var capacity = body.U32( path + ".mNumEntries" );
		var head = unchecked( (int)body.U32( path + ".mCurrentEntry" ) );
		// Do not use ThemeParkIncHistoryRing.WrappedAround: its Flag reader has already collapsed any nonzero byte to true.
		var wrapped = body.U8( path + ".mWrappedAround" );
		if ( head < -1 || head >= (long)capacity || wrapped > 1 || head == -1 && wrapped != 0 )
			throw new InvalidDataException( $"Theme Park Inc analyser {name} ring has an invalid head or wrapped flag." );
		// [EXT:tpi-history-host-profile] native smaller capacities are readable, but padding/replaying them would change head, retention and wrap semantics in the fixed host representation
		if ( capacity != requiredCapacity )
			throw new InvalidDataException( $"Theme Park Inc analyser {name} capacity {capacity} is structurally supported but unsupported by the fixed host history profile (requires {requiredCapacity})." );
		var words = new int[requiredCapacity];
		var values = new double[requiredCapacity];
		for ( var slot = 0; slot < requiredCapacity; slot++ )
		{
			var word = unchecked( (int)body.U32( $"{path}.mData[{slot}]" ) );
			words[slot] = word;
			if ( wrapped == 0 && slot > head )
				continue;
			// Widen separately so integer samples never round through single precision.
			double value = floating ? (double)BitConverter.Int32BitsToSingle( word ) : word;
			if ( !double.IsFinite( value ) )
				throw new InvalidDataException( $"Theme Park Inc analyser {name} ring has a nonfinite active sample." );
			values[slot] = value;
		}
		return new NativeStatisticsRing( head, wrapped != 0, unchecked( (int)body.U32( path + ".mTemp" ) ), words, values );
	}
}
