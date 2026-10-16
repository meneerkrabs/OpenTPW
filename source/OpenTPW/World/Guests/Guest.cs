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
	/// <summary>Walking to a litter bin with a full litter meter (original state 9).</summary>
	ThrowingAwayLitter = 9,
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
	/// <summary>Caught by a guard: carried, out of sight, until the guard sets it down (original state 17, set by 0x004FEB10).</summary>
	HeldByGuard = 17,
	LeavingPark = 18,
	/// <summary>
	/// At the gate's crossing, waiting for the arrival vehicle to let a leaving guest walk to its stop (original state 19); a guest that walks OpenTPW's lane home is in
	/// this state too, with <see cref="Guest.Lane"/> set.
	/// </summary>
	CrossingRoadHome = 19,
	/// <summary>Walking to the arrival vehicle's stop cells over the cell map (original state 20).</summary>
	WalkingToVehicle = 20,
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
	/// <summary>Simulation position in cells (<see cref="Navigation"/>, 16.16), updated once per park turn.</summary>
	public float X => Navigation.X / (float)Fixed16.One;
	public float Y => Navigation.Y / (float)Fixed16.One;
	/// <summary>Presentation position: interpolated between the last two steering steps (not simulation state).</summary>
	public float DrawX { get; internal set; }
	public float DrawY { get; internal set; }
	/// <summary>Unit direction of the last movement (game cells).</summary>
	public float HeadingX { get; internal set; } = 0;
	public float HeadingY { get; internal set; } = 1;
	public bool IsMoving { get; internal set; }
	/// <summary>Cells walked in total; drives the walk-cycle frame.</summary>
	public float DistanceWalked { get; internal set; }
	/// <summary>Presentation counterpart of <see cref="DistanceWalked"/>, interpolated like <see cref="DrawX"/>.</summary>
	public float DrawDistanceWalked { get; internal set; }
	public bool IsVisible => State is not (GuestState.Using or GuestState.HeldByGuard or GuestState.Gone);

	public float Hunger { get; internal set; }
	public float Thirst { get; internal set; }
	public float Toilet { get; internal set; }
	/// <summary>
	/// The original's unnamed meter at guest <c>+0x1B8</c> (0..100): 0 at creation (tp.exe <c>0x004FAEC0</c>), lowered by the type's
	/// <c>UsageInfo.FatigueEffect</c> when a use completes (<c>0x004FD970</c>) and read by nothing but the save, so with the shipped
	/// FatigueEffect of 5 it stays 0.
	/// </summary>
	// [BIN:TPW-EXE:0x004FAF1F guest meter start] the constructor stores 0 (ebx) at +0x1B8 (also at 0x004FB0EF; the load factory at 0x00518611)
	public float Energy { get; internal set; }
	public float Nausea { get; internal set; }
	public float Happiness { get; internal set; }
	/// <summary>The litter meter (guest <c>+0x1b4</c>, 0..100): each use adds the type's <c>LitterEffect</c>; at a byte of 90 the guest bins or drops its litter.</summary>
	public float Litter { get; internal set; }
	/// <summary>The litter bin a guest in state 9 walks to (guest <c>+0x1dc</c>, the ride word's other use); 0 otherwise.</summary>
	public int BinId { get; internal set; }
	/// <summary>The prankery flag (guest <c>+0x1c0</c>, <c>mPrankeryIndex</c>): 0, or 100 (stinkbombs), 101 (litter) or 102 (balloons) for a prankster.</summary>
	public int PrankFlag { get; internal set; }
	/// <summary>
	/// The way out a guard's set-down drew for the guest (0: arrival lane A, 1: lane B; tp.exe <c>0x004FEB50</c> takes bit 0 of a world draw), which its
	/// walk out of the park takes; null for every other guest, which leaves by the nearer reachable lane.
	/// </summary>
	public int? ExitSide { get; internal set; }
	/// <summary>
	/// The balloon's remaining life (guest <c>+0x214</c>, <c>mRemainingBalloonLife</c>): set by a balloon shop, one less on every needs phase while the guest is in
	/// the park and neither on a ride nor held; the balloon goes at 0. It is not cleared when the balloon bursts, so a burst balloon comes back after the next ride.
	/// </summary>
	public int BalloonLife { get; internal set; }
	/// <summary>Whether the guest holds a balloon (guest <c>+0x210</c>, the balloon's model <c>mBalloonScript</c>): put away on a ride, back after it while it has life.</summary>
	public bool HasBalloon { get; internal set; }
	/// <summary>Whether the guest wears a costume (guest <c>+0x24</c> == 2): a costume shop puts one on, and the next visit to one returns it.</summary>
	public bool InCostume { get; internal set; }
	public int Money { get; internal set; }
	/// <summary>
	/// The exit counter (guest <c>+444</c>, saved as <c>mExitLevel</c>): <c>PeepInfo.ExitLevel</c> plus a draw within ±<c>ExitLevelVar</c> at creation, one less on every
	/// needs phase of the guest (every fourth park turn, 0.992 s: the .sam's "seconds"), in every state. It is a signed word and runs negative after 0.
	/// </summary>
	public int ExitLevel { get; internal set; }
	/// <summary>Display-only simulated seconds since admission; null means the prior duration is unknown.
	/// Seeded in-park guests start at zero. Outside travel and the queue before admission do not count.</summary>
	public double? TimeInParkSeconds { get; internal set; }
	/// <summary>Own-format/display duration bound; no original timing limit is inferred.</summary>
	public static double MaximumTimeInParkSeconds => TimeSpan.MaxValue.TotalSeconds;
	public float SecondsSinceRide { get; internal set; }
	/// <summary>The rides ridden (guest <c>+0x1C4</c>): only an attraction of kind <c>Info.WhichUIType</c> 0 counts (the use routine 0x4FD970 sorts a finished visit by the type's <c>+0x4AC</c>).</summary>
	public int RidesTaken { get; internal set; }
	/// <summary>The shops visited (guest <c>+0x1C8</c>, kind 1), shown by the visitor info window (<c>0x4B72A0</c>, control 0x372F+).</summary>
	public int ShopsVisited { get; internal set; }
	/// <summary>The sideshows played (guest <c>+0x1CC</c>, kind 2), won or lost.</summary>
	public int SideshowsVisited { get; internal set; }
	/// <summary>The sideshows won (guest <c>+0x1D0</c>), counted where the use routine's won-sideshow tail runs (<c>0x4FE816</c>).</summary>
	public int SideshowsWon { get; internal set; }
	public GuestThought Thought { get; internal set; }
	/// <summary>
	/// The thought the guest's bubble shows (the thought object's script, guest <c>+0x30 +0x84</c>), <see cref="GuestThought.None"/> without a bubble; presentation
	/// state the simulation times (<see cref="GuestThoughts"/>).
	/// </summary>
	public GuestThought BubbleThought { get; internal set; }
	/// <summary>The park turn the last bubble appeared (guest <c>+0x30 +0x8C</c>): it goes 12 turns later and gates the next one.</summary>
	public long BubbleTurn { get; internal set; }
	/// <summary>Whether the sideshow the guest is playing was won (guest <c>+0x1F1</c>, set when the play starts); null outside a sideshow or without its result.</summary>
	public bool? SideshowWon { get; internal set; }
	/// <summary>Attraction the guest is heading to, queueing for or riding (0 = none).</summary>
	public int AttractionId { get; internal set; }
	/// <summary>The byte at +0x2C that state 10 counts while the guest walks to a ride with the park open; every twelfth count the minor decision runs (tp.exe 0x004FFEF2).</summary>
	public int RideWalkCount { get; internal set; }
	/// <summary>Park turns an expensive verdict still holds the guest at the booth (+0x200, tp.exe 0x004FF9D0): one is counted per turn, the verdict runs again as the last reaches 0.</summary>
	public int BoothWait { get; internal set; }
	public int LastAttractionId { get; internal set; }
	/// <summary>The attractions the guest used last, newest first (guest <c>+480..+486</c>); the score divides by 5, 4, 3 or 2 for the first match. 0 = empty slot.</summary>
	public int[] UsedHistory { get; } = new int[HistorySlots];
	/// <summary>The attractions the guest recently turned away from (queue too long, too boring or too exciting), newest first (guest <c>+488..+494</c>); a 0 is shifted in every 20 park turns. 0 = empty slot.</summary>
	public int[] TemporaryHistory { get; } = new int[HistorySlots];
	/// <summary>Slots of each history array (four).</summary>
	public const int HistorySlots = 4;

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
	/// <summary>Park turn of the last entry into state 11 [+508]. The same word is the decision stamp of state 6 (tp.exe <c>+0x1FC</c>):
	/// a major decision waits until 30 turns after it, and a failed decision or wander sets it to the turn.</summary>
	public long QueueStandingSinceTurn { get; internal set; }
	/// <summary>Park turn of the last idle interlude or activity [+520] (tp.exe <c>+0x208</c>, set when state 8 starts).</summary>
	public long InterludeTurn { get; internal set; }
	/// <summary>In an idle interlude (original state 8, entered from state 11 and restored to it).</summary>
	public bool InQueueInterlude { get; internal set; }
	/// <summary>The activity of a guest in state 6 that is in the original's state 8 (ten park turns from <see cref="InterludeTurn"/>, then back to state 6); <see cref="GuestActivity.None"/> otherwise.</summary>
	public GuestActivity Activity { get; internal set; }
	/// <summary>Happiness when the guest joined [+524].</summary>
	public float QueueJoinHappiness { get; internal set; }

	// Walk speed (docs/reverse/WALK-plan.md §4; original guest offsets in brackets)
	/// <summary>Base speed [+192]: 60, 80, 100, 120 or 140.</summary>
	public int BaseSpeed { get; internal set; }
	/// <summary>Toilet speed term [+194]: 25 while toilet &gt; 80, else 0.</summary>
	public int ToiletBonus { get; internal set; }
	/// <summary>Decaying speed boost [+196].</summary>
	public ushort Boost { get; internal set; }
	/// <summary>Smoothed speed input s [+200], 0 at creation.</summary>
	public float SmoothedSpeed { get; internal set; }
	/// <summary>Max speed [nav +28] in 16.16 cells per park turn, from <see cref="GuestSpeed.Caps"/>.</summary>
	public int MaxSpeed { get; internal set; } = GuestSpeed.Floor;
	/// <summary>Max steering force [nav +24].</summary>
	public int MaxForce { get; internal set; } = GuestSpeed.Floor;
	/// <summary>The steering state [+212]; its position is <see cref="X"/>/<see cref="Y"/>.</summary>
	public GuestNavigation Navigation { get; } = new();

	// Navigation
	internal float StepFromX;         // position before the last steering step (presentation)
	internal float StepFromY;
	internal float StepFromDistance;
	internal long StepTick = -1;      // tick of the last steering step (presentation)
	internal (int X, int Y)? OccupiedCell; // the cell list separation finds the guest in (derived from Navigation, not hashed)
	internal int CellX;
	internal int CellY;
	internal int PreviousCellX = -1;
	internal int PreviousCellY = -1;
	internal bool OnGrid;
	internal float OffsetX;
	internal float OffsetY;
	internal bool HasWaypoint;        // walking to a destination (cleared on arrival)
	internal (int X, int Y)[]? Lane;
	internal int LaneIndex;
	internal bool LaneReversed;
	internal float StateTimer;
	internal int QueueCellIndex;      // index of the queue cell the guest is on (cell count = still on the join cell)
	internal int QueueTargetIndex;    // queue cell of the destination slot
	internal float QueueTargetX;
	internal float QueueTargetY;
	internal long LastQueueUpdateTurn = -1;

	public (int X, int Y) Cell => (CellX, CellY);
}
