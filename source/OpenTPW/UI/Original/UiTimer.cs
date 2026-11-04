namespace OpenTPW.UI.Original;

/// <summary>
/// A window timer of the original's UI (the timer objects behind a window's timer slots, <c>0x65EF90</c> set, <c>0x65F024</c> enable, <c>0x65F077</c> disable): an interval, the
/// clock reading it counts from, and an enabled flag. The clock is the UI time source's millisecond counter (<c>0x65968E</c>: the UI start-up binds it to the game's own
/// counter, vtable <c>0x6FF178</c> -&gt; <c>0x5F5FA0</c>, a performance-counter reading), a 32-bit value whose differences wrap.
/// </summary>
// [BIN:TPW-EXE:0x65EF90 timer set] the slot (the timer id minus 0x80080) holds the timer object: a first set makes it (0x66220D, appended to the global timer list 0xFAA634), a later one only stores the interval (0x6620CC, +8); the object is made enabled-less
// [BIN:TPW-EXE:0x6620AC timer enable] enabling sets the flag (+0x10) and the origin (+4) to the clock; 0x662097 only clears the flag, the object and its origin stay
// [BIN:TPW-EXE:0x661FE5 timer tick] every pump (0x6622D2, unless the suspend flag 0xFAA638 is set) takes the clock, elapsed = clock - origin (a signed 32-bit difference); when elapsed >= interval the origin becomes the clock and, once per whole interval in elapsed, a timer message
// (0x10, the id and the count so far) is posted to the owner (0x658CCC) when the timer is enabled, the owner exists and the UI is active (0x77C488); the origin then moves back by the remainder, so no time is lost between ticks
// [BIN:TPW-EXE:0x662420 timer resume] a resume clears the suspend flag and makes every timer's origin the clock again (0x66213E), enabled or not
public sealed class UiTimer
{
	/// <summary>The most events one tick reports: every event after the first of a tick finds the owner's guards already settled, so counting further adds nothing (and an interval of 0 cannot loop).</summary>
	public const int MaximumEvents = 1;

	public int Interval { get; set; }
	public int Origin { get; private set; }
	public bool Enabled { get; private set; }

	/// <summary>Sets the interval without touching the origin or the enabled flag (<c>0x6620CC</c>).</summary>
	public void SetInterval( int milliseconds ) => Interval = milliseconds;

	/// <summary>Enables the timer counting from <paramref name="now"/> (<c>0x6620AC</c>).</summary>
	public void Enable( int now )
	{
		Enabled = true;
		Origin = now;
	}

	/// <summary>Stops the timer's events (<c>0x662097</c>); its origin stays and still advances in ticks.</summary>
	public void Disable() => Enabled = false;

	/// <summary>Makes <paramref name="now"/> the origin again, as the resume of the timer pump does for every timer (<c>0x66213E</c>).</summary>
	public void Reset( int now ) => Origin = now;

	/// <summary>
	/// One pump step at clock <paramref name="now"/> (a wrapping 32-bit millisecond count): the number of messages (at most <see cref="MaximumEvents"/>) the original would post to the owner; an elapsed time of
	/// at least one interval moves the origin on whether or not the timer is enabled.
	/// </summary>
	public int Tick( int now )
	{
		var elapsed = unchecked( now - Origin );
		if ( elapsed < Interval )
			return 0;
		if ( Interval <= 0 )
		{
			// the original's loop never ends for a timer whose interval is not positive: a zero interval is never made by a tag (a saved lifetime of 5000 ms or less puts the warning timer there; the saved lifetime is bounded
			// at load), and here it ticks once per pump
			Origin = now;
			return Enabled ? 1 : 0;
		}
		// the loop of 0x661FE5 subtracts the interval once per message; the remainder and the first message are all that matter, so it is one division
		var whole = elapsed / Interval;
		elapsed -= whole * Interval;
		Origin = unchecked( now - elapsed );
		return Enabled ? Math.Min( whole, MaximumEvents ) : 0;
	}
}
