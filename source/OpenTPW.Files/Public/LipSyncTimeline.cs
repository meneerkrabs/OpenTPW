namespace OpenTPW;

/// <summary>
/// Talking/silent timeline for an advisor speech clip, derived from <see cref="LipSyncFile"/>
/// marks (see docs/LIPS.md). Marks are microseconds from the start of the paired speech
/// sample. The advisor is talking from 0 until the first mark; every mark toggles the
/// state, so with the odd mark count found in every original file the last mark closes the
/// mouth. This reading is inferred from the decoded speech audio (talking intervals are
/// ≈31 dB louder than silent ones; the fit peaks at exactly 1 µs per unit), not from the
/// original runtime, and it does not select a particular mouth shape.
/// </summary>
public sealed class LipSyncTimeline
{
	public IReadOnlyList<uint> Marks { get; }

	/// <summary>Talking intervals as [start, end) in microseconds; empty intervals are omitted.</summary>
	public IReadOnlyList<(long StartMicroseconds, long EndMicroseconds)> TalkingIntervals { get; }

	public long EndMicroseconds => Marks.Count == 0 ? 0 : Marks[^1];

	public LipSyncTimeline( IReadOnlyList<uint> marks )
	{
		ArgumentNullException.ThrowIfNull( marks );
		for ( var index = 1; index < marks.Count; index++ )
			if ( marks[index] <= marks[index - 1] )
				throw new ArgumentException( "Lip-sync marks must be strictly increasing.", nameof( marks ) );
		Marks = marks.ToArray();
		var intervals = new List<(long, long)>();
		long start = 0;
		for ( var index = 0; index < marks.Count; index++ )
		{
			if ( index % 2 == 0 )
			{
				if ( marks[index] > start )
					intervals.Add( (start, marks[index]) );
			}
			else
				start = marks[index];
		}
		if ( marks.Count % 2 == 0 && marks.Count > 0 )
			intervals.Add( (start, long.MaxValue) );
		TalkingIntervals = intervals;
	}

	public LipSyncTimeline( LipSyncFile file ) : this( file.Marks ) { }

	/// <summary>
	/// Whether the advisor is talking at <paramref name="microseconds"/> into the clip:
	/// true when an even number of marks lie at or before that position.
	/// </summary>
	public bool IsTalking( long microseconds )
	{
		// [APPROX:ADVISOR-013] µs unit and "talking from 0, toggle per mark" inferred from decoded audio (31 dB talking/silent contrast, fit peaks at 1 µs/unit) — evidence needed: original runtime LIP consumer (binary or trace)
		if ( microseconds < 0 || Marks.Count == 0 )
			return false;
		var low = 0;
		var high = Marks.Count;
		while ( low < high )
		{
			var middle = (low + high) / 2;
			if ( Marks[middle] <= microseconds )
				low = middle + 1;
			else
				high = middle;
		}
		return low % 2 == 0;
	}

	public bool IsTalking( TimeSpan position ) => IsTalking( position.Ticks / 10 );
}
