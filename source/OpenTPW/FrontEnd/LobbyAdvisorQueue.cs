namespace OpenTPW.FrontEnd;

/// <summary>
/// The lobby's speech queue: the response ids waiting to be spoken and the idle line the advisor repeats after a long silence. The lobby steps it once per frame, after the
/// islands component has (re)set the idle line; <c>say</c> is the response player and refuses while a response plays or the speech is off.
/// </summary>
// [BIN:TPW-EXE:0x5D5F70 queue setup] the task's setup (slot +4 of vtable 0x702DA0) clears the idle word (+0x18), the idle delay (+0x1C) and the time of the last speech (+0x20); the object is made at 0x5E3ECF and registered at 0x5E3EDE
// [BIN:TPW-EXE:0x5E2A30 list push] a pushed id goes in at the head of the list (+0xC) and the step takes it from the tail (+0x10): first in, first out
internal sealed class LobbyAdvisorQueue
{
	/// <summary>The silence after which the idle line is said: 0x15F90 milliseconds.</summary>
	// [BIN:TPW-EXE:0x5E1854 idle delay] the islands component's update passes 0x15F90 (90 s) with the idle line
	public const uint IdleDelayMilliseconds = 0x15F90;

	/// <summary>The idle line for an even draw (0x18A, 394) and for an odd one (0x18B, 395).</summary>
	public const int IdleLineEven = 0x18A, IdleLineOdd = 0x18B;

	private readonly Queue<int> waiting = new();
	private bool idleOn;
	private uint idleDelay;
	private uint lastSpeech;
	private int idleLine;
	// [EXT:original-undefined-reads] the word +0x28 ("the advisor spoke since the last step") is allocated with the object and no constructor or setup writes it (0x5E3EBA-0x5E3EF4, 0x5D5F70): OpenTPW starts it cleared
	private bool spoke;

	/// <summary>How many ids wait.</summary>
	public int Count => waiting.Count;

	/// <summary>Whether the idle line is on.</summary>
	public bool IdleOn => idleOn;

	/// <summary>The idle line's id.</summary>
	public int IdleLine => idleLine;

	/// <summary>Queues <paramref name="id"/>; with <paramref name="flush"/> the waiting ids go first.</summary>
	// [BIN:TPW-EXE:0x5D6110 push] a nonzero second argument empties the list from its head (0x5D6131-0x5D616E) before the id is added (0x5D6172-0x5D6186)
	public void Push( int id, bool flush )
	{
		if ( flush )
			waiting.Clear();
		waiting.Enqueue( id );
	}

	/// <summary>Turns the idle line on: <paramref name="id"/> is said again once the advisor has been silent for <paramref name="delayMilliseconds"/>.</summary>
	// [BIN:TPW-EXE:0x5D61A0 idle line] sets the idle word to 1, the delay (+0x1C) and the id (+0x24)
	public void SetIdle( int id, uint delayMilliseconds )
	{
		idleOn = true;
		idleDelay = delayMilliseconds;
		idleLine = id;
	}

	/// <summary>
	/// The queue's frame (0x5D5F80). <paramref name="now"/> is the timer in milliseconds (0x5F5FA0), <paramref name="busy"/> whether a response is active (0xF7966C) and
	/// <paramref name="say"/> the response player (0x599050).
	/// </summary>
	// [BIN:TPW-EXE:0x5D5F80 queue step] a response that plays only marks that the advisor spoke (0x5D6047); else a waiting id is said (0x5D5FCF) and marks it; else, with the idle line on, the mark moves the last speech time to now, and a silence longer than the delay (an unsigned compare against the last speech plus the delay, 0x5D6014) pushes the idle line and takes the time (0x5D601B-0x5D6037)
	public void Step( uint now, bool busy, Action<int> say )
	{
		if ( busy )
		{
			spoke = true;
			return;
		}
		if ( waiting.Count > 0 )
		{
			say( waiting.Dequeue() );
			spoke = true;
			return;
		}
		if ( idleOn )
		{
			if ( spoke )
			{
				lastSpeech = now;
				spoke = false;
				return;
			}
			if ( unchecked(now > idleDelay + lastSpeech) )
			{
				lastSpeech = now;
				waiting.Enqueue( idleLine );
			}
		}
		spoke = false;
	}

	/// <summary>The queue's teardown (0x5D6060): a response that plays is cut off, then the waiting ids are dropped.</summary>
	// [BIN:TPW-EXE:0x5D6060 queue teardown] with a response active (0xF7966C) it calls the cut-off path 0x5994E0, then empties the list from its head
	public void Teardown( bool busy, Action cutOff )
	{
		if ( busy )
			cutOff();
		waiting.Clear();
	}
}
