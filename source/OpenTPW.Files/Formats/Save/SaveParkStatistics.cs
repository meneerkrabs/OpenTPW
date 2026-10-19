using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>A validated TPW or TPI word ring in physical slot order. Raw words retain scratch/inactive data; values expose only active samples.</summary>
public sealed class NativeStatisticsRing
{
	internal NativeStatisticsRing( int head, bool wrapped, int temp, int[] words, double[] values )
	{
		Head = head;
		Wrapped = wrapped;
		Temp = temp;
		RawValues = Array.AsReadOnly( (int[])words.Clone() );
		Values = Array.AsReadOnly( (double[])values.Clone() );
	}

	public int Head { get; }
	public bool Wrapped { get; }
	/// <summary>The serialized scratch word, not a historical sample.</summary>
	public int Temp { get; }
	public IReadOnlyList<int> RawValues { get; }
	/// <summary>Signed integers or reinterpreted finite binary32 happiness, with unread slots normalized to zero.</summary>
	public IReadOnlyList<double> Values { get; }
}

/// <summary>Five validated TPW or TPI monthly measurements and their host continuation inputs, decoded together before any runtime state changes.</summary>
public sealed class NativeParkStatistics
{
	internal NativeParkStatistics( NativeStatisticsRing people, NativeStatisticsRing arrivals, NativeStatisticsRing happiness,
		NativeStatisticsRing stay, NativeStatisticsRing rating, NativeStatisticsRing recentStays )
	{
		People = people;
		Arrivals = arrivals;
		Happiness = happiness;
		Stay = stay;
		Rating = rating;
		RecentStays = recentStays;
	}

	public NativeStatisticsRing People { get; }
	public NativeStatisticsRing Arrivals { get; }
	public NativeStatisticsRing Happiness { get; }
	public NativeStatisticsRing Stay { get; }
	public NativeStatisticsRing Rating { get; }
	public NativeStatisticsRing RecentStays { get; }
	public int ArrivalsThisMonth => Arrivals.Temp;
}

/// <summary>
/// The fixed-width Theme Park World analyser profile. Theme Park Inc has a different, variable-width serializer and is not handled here.
/// Offsets include the eight-byte thing base, but exclude the chain header. This reader does not implement native statistics export.
/// </summary>
public static class SaveParkStatistics
{
	public const int MonthlyCapacity = 144, MonthlyCount = 16, MonthlyBytes = 13 + MonthlyCapacity * 4;
	public const int MonthlyOffset = SaveRecentResearch.BodyOffset + SaveRecentResearch.Bytes + 6 * 5;
	public const int RecentStayCapacity = 50, RecentStaysOffset = MonthlyOffset + MonthlyCount * MonthlyBytes;
	public const int BodyBytes = RecentStaysOffset + (13 + RecentStayCapacity * 4) + (13 + 5 * 4) + 20 * (10 + 50) + 16 + 48 + 12;

	/// <summary>Strict host validation, stronger than the native loader: fixed capacities, bounded metadata and finite active happiness.</summary>
	// [BIN:TPW-EXE:0x4C5D70 analyser serializer] five grid words, 1024 61-byte cells, four 17-byte research rings and six 5-byte trackers precede sixteen 589-byte monthly records; 0x4CDF00 serializes head/capacity/wrapped/temp and every physical 32-bit word
	// [BIN:TPW-EXE:0x4A4750 graph dispatch] ordinals 12/13/14/9/15 are arrivals/happiness/stay/people/rating; only happiness uses the single reader 0x480570, the others use signed 0x480460
	public static NativeParkStatistics Read( ReadOnlySpan<byte> payload, SaveThing thing )
	{
		if ( thing.Model != SaveThingModel.ParkAnalyser || thing.BodyOffset < 0 || thing.BodyLength < BodyBytes
			|| thing.BodyOffset > payload.Length || thing.BodyLength > payload.Length - thing.BodyOffset )
			throw new InvalidDataException( "The referenced TPW park analyser does not contain the complete statistics body." );
		// A serialized capacity changes every subsequent offset. Validate the entire prefix, not just the selected graph records.
		SaveRecentResearch.Read( payload, thing );
		var body = payload.Slice( thing.BodyOffset, thing.BodyLength );
		for ( var index = 0; index < MonthlyCount; index++ )
			if ( BinaryPrimitives.ReadInt32LittleEndian( body.Slice( MonthlyOffset + index * MonthlyBytes + 4, 4 ) ) != MonthlyCapacity )
				throw new InvalidDataException( $"TPW analyser monthly ring {index} has an unsupported capacity." );
		var people = ReadRing( body, MonthlyOffset + 9 * MonthlyBytes, MonthlyCapacity, false, "people" );
		var arrivals = ReadRing( body, MonthlyOffset + 12 * MonthlyBytes, MonthlyCapacity, false, "arrivals" );
		var happiness = ReadRing( body, MonthlyOffset + 13 * MonthlyBytes, MonthlyCapacity, true, "happiness" );
		var stay = ReadRing( body, MonthlyOffset + 14 * MonthlyBytes, MonthlyCapacity, false, "stay" );
		var rating = ReadRing( body, MonthlyOffset + 15 * MonthlyBytes, MonthlyCapacity, false, "rating" );
		var recentStays = ReadRing( body, RecentStaysOffset, RecentStayCapacity, false, "recent stays" );
		// Host continuation policy: ParkStatisticsData requires a nonnegative live arrivals counter. Historical signed samples are not clamped.
		if ( arrivals.Temp < 0 )
			throw new InvalidDataException( "TPW analyser live arrivals must be nonnegative for host continuation." );
		return new NativeParkStatistics( people, arrivals, happiness, stay, rating, recentStays );
	}

	private static NativeStatisticsRing ReadRing( ReadOnlySpan<byte> body, int offset, int capacity, bool floating, string name )
	{
		var length = 13 + capacity * 4;
		if ( offset < 0 || offset > body.Length || length > body.Length - offset )
			throw new InvalidDataException( $"TPW analyser {name} ring exceeds its thing body." );
		var bytes = body.Slice( offset, length );
		var head = BinaryPrimitives.ReadInt32LittleEndian( bytes );
		var wrapped = bytes[8];
		if ( BinaryPrimitives.ReadInt32LittleEndian( bytes[4..] ) != capacity || head < -1 || head >= capacity
			|| wrapped > 1 || head == -1 && wrapped != 0 )
			throw new InvalidDataException( $"TPW analyser {name} ring has invalid capacity, head or wrapped flag." );
		var words = new int[capacity];
		var values = new double[capacity];
		for ( var slot = 0; slot < capacity; slot++ )
		{
			var word = BinaryPrimitives.ReadInt32LittleEndian( bytes[(13 + slot * 4)..] );
			words[slot] = word;
			if ( wrapped == 0 && slot > head )
				continue;
			// Widen separately: a conditional float/int expression would round signed integers through single precision first.
			double value = floating ? (double)BitConverter.Int32BitsToSingle( word ) : word;
			if ( !double.IsFinite( value ) )
				throw new InvalidDataException( $"TPW analyser {name} ring has a nonfinite active sample." );
			values[slot] = value;
		}
		return new NativeStatisticsRing( head, wrapped != 0, BinaryPrimitives.ReadInt32LittleEndian( bytes[9..] ), words, values );
	}
}
