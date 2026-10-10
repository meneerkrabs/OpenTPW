namespace OpenTPW;

/// <summary>Guest states; the numeric values are the original <c>KIDSTATES.str</c> indices.</summary>
public enum GuestState : byte
{
	CrossingRoad = 0,
	CrossingRoadToPark = 1,
	GoingToTicketBooth = 2,
	AtTicketBooth = 4,
	EnteringPark = 5,
	DoingNothing = 6,
	WalkingAround = 7,
	GoingToRide = 10,
	/// <summary>Standing in a queue (original state 11).</summary>
	Queueing = 11,
	/// <summary>Walking to a queue position (original state 12; KIDSTATES gives it the same text as 11).</summary>
	MovingUpQueue = 12,
	/// <summary>Called forward: walking to the stand point and offering itself to the script (original state 13).</summary>
	Boarding = 13,
	/// <summary>Waiting until the script takes the guest from VAR_LETMEON (original state 14).</summary>
	WaitingToBoard = 14,
	ExitingRide = 15,
	Using = 16,
	LeavingPark = 18,
	CrossingRoadHome = 19,
	WaitingToGoHome = 21,
	/// <summary>Not a KIDSTATES entry: removed from the simulation at the end of the tick.</summary>
	Gone = 255
}

/// <summary>Guest thoughts; the numeric values are the original <c>THOUGHTS.str</c> indices.</summary>
public enum GuestThought : byte
{
	None = 0,
	Hungry = 1,
	Thirsty = 2,
	HungryAndThirsty = 3,
	NeedToilet = 4,
	Pleased = 5,
	Dissatisfied = 6,
	TooMessy = 7,
	SuperHappy = 8,
	Happy = 9,
	Okay = 10,
	Unhappy = 11,
	Bored = 12,
	Angry = 13,
	FeelingSick = 14,
	Scared = 15,
	QueueTooLong = 16,
	Confused = 17
}

/// <summary>One park visitor. Positions are in game cells (cell (x, y) spans [x, x+1) × [y, y+1)).</summary>
public sealed class Guest
{
	internal Guest( int id, int type )
	{
		Id = id;
		Type = type;
	}

	/// <summary>Positive id; this is the value ride scripts see in VAR_LETMEON/VAR_LETMEOFF.</summary>
	public int Id { get; }
	/// <summary>Index into the .sam PeepTypes and the kid sprite set.</summary>
	public int Type { get; }
	public GuestState State { get; internal set; }
	public float X { get; internal set; }
	public float Y { get; internal set; }
	/// <summary>Unit direction of the last movement (game cells).</summary>
	public float HeadingX { get; internal set; } = 0;
	public float HeadingY { get; internal set; } = 1;
	public bool IsMoving { get; internal set; }
	/// <summary>Cells walked in total; drives the walk-cycle frame.</summary>
	public float DistanceWalked { get; internal set; }
	public bool IsVisible => State is not (GuestState.Using or GuestState.Gone);

	public float Hunger { get; internal set; }
	public float Thirst { get; internal set; }
	public float Toilet { get; internal set; }
	public float Energy { get; internal set; } = 100;
	public float Nausea { get; internal set; }
	public float Happiness { get; internal set; }
	public int Money { get; internal set; }
	/// <summary>Seconds left before the guest heads home (.sam PeepInfo.ExitLevel ± ExitLevelVar).</summary>
	public float ExitLevel { get; internal set; }
	public float SecondsSinceRide { get; internal set; }
	public int RidesTaken { get; internal set; }
	public GuestThought Thought { get; internal set; }
	/// <summary>Attraction the guest is heading to, queueing for or riding (0 = none).</summary>
	public int AttractionId { get; internal set; }
	public int LastAttractionId { get; internal set; }

	// Queue (docs/reverse/QUEUE-plan.md §3.2; original guest offsets in brackets)
	/// <summary>True while the guest is in an attraction's queue list (walking up, standing, called or waiting to be taken).</summary>
	public bool IsInQueue => State is GuestState.MovingUpQueue or GuestState.Queueing or GuestState.Boarding or GuestState.WaitingToBoard;
	/// <summary>Recorded queue position, 0 = front [+497]; −1 outside a queue.</summary>
	public int QueuePosition { get; internal set; } = -1;
	/// <summary>Park turn at which the guest joined its current queue, or −1.</summary>
	public long QueueJoinTurn { get; internal set; } = -1;
	/// <summary>Park turns from joining to being taken by the script, for the last completed queue (−1: none yet).</summary>
	public long LastQueueWaitTurns { get; internal set; } = -1;
	/// <summary>Remaining move-up delay in park turns [+500].</summary>
	public int QueueMoveDelay { get; internal set; }
	/// <summary>Called forward by the attraction [+504].</summary>
	public bool QueueCalled { get; internal set; }
	/// <summary>Park turn of the last entry into state 11 [+508].</summary>
	public long QueueStandingSinceTurn { get; internal set; }
	/// <summary>Park turn of the last idle interlude [+520].</summary>
	public long InterludeTurn { get; internal set; }
	/// <summary>In an idle interlude (original state 8, entered from state 11 and restored to it).</summary>
	public bool InQueueInterlude { get; internal set; }
	/// <summary>Happiness when the guest joined [+524].</summary>
	public float QueueJoinHappiness { get; internal set; }

	// Navigation
	internal int CellX;
	internal int CellY;
	internal int PreviousCellX = -1;
	internal int PreviousCellY = -1;
	internal bool OnGrid;
	internal float OffsetX;
	internal float OffsetY;
	internal float WaypointX;
	internal float WaypointY;
	internal bool HasWaypoint;
	internal (int X, int Y)[]? Lane;
	internal int LaneIndex;
	internal bool LaneReversed;
	internal float StateTimer;
	internal float DecisionTimer;
	internal int QueueCellIndex;      // index of the queue cell the guest is on (cell count = still on the join cell)
	internal int QueueTargetIndex;    // queue cell of the destination slot
	internal float QueueTargetX;
	internal float QueueTargetY;
	internal long LastQueueUpdateTurn = -1;

	public (int X, int Y) Cell => (CellX, CellY);
}
