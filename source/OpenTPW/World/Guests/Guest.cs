namespace OpenTPW;

/// <summary>Guest states; the numeric values are the original <c>KIDSTATES.str</c> indices.</summary>
public enum GuestState : byte
{
	CrossingRoad = 0,
	CrossingRoadToPark = 1,
	GoingToTicketBooth = 2,
	/// <summary>
	/// The guest stands at the ticket booth after its walk with the park closed, or after paying while the gate holds it (original state 3, <c>0x004FF7F0</c>): a paid guest walks
	/// on to the entrance cell when it heads its cell's list; with the park closed or the gate shut it counts its wait down and goes home.
	/// </summary>
	WaitingAtBooth = 3,
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
	/// <summary>
	/// Swimming in the pool complex of a changing room (OpenTPW's own state, 22; not a KIDSTATES entry): a sub-cell position and heading in the water, drawn with the swim sprites, for a bounded
	/// session (<see cref="PoolSwimSettings"/>, docs/POOLS.md). The guest names the changing room as its attraction.
	/// </summary>
	// [EXT:pools-swim] OpenTPW's own guest state; the original game has no pools
	Swimming = 22,
	/// <summary>Swimming back to the changing room's pool-side cell at the end of the session (OpenTPW's own state, 23); still drawn in the water.</summary>
	// [EXT:pools-swim] OpenTPW's own guest state; the original game has no pools
	ClimbingOut = 23,
	/// <summary>Changing back inside the changing room, out of sight like a guest on a ride (OpenTPW's own state, 24); it then steps out on the changing room's path-side exit.</summary>
	// [EXT:pools-swim] OpenTPW's own guest state; the original game has no pools
	ChangingOut = 24,
	/// <summary>
	/// Doing a pool activity during the swim session (OpenTPW's own state, 25): a ladder, a diving board, a shower, a lounger, a slide or a water attraction (<see cref="IPoolActivity"/>, docs/POOLS.md,
	/// "Activities"). The guest still names its changing room as its attraction; the activity moves it and ends with it back in the water or out of the session.
	/// </summary>
	// [EXT:pools-activities] OpenTPW's own guest state; the original game has no pools
	PoolActivity = 25,
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
	/// <summary>Persistent body presentation. Ordinary walker rendering still uses DrawDistanceWalked.</summary>
	public VisitorSpriteState BodySprite { get; } = new();
	/// <summary>The live body's copied category at creation, independent of subsequent costume purchases.</summary>
	public bool BodySpriteInCostume { get; internal set; }
	/// <summary>True when old/imported history had to be reconstructed at load, or after an unsupported extension lifecycle.</summary>
	public bool BodySpriteHistoryReconstructed { get; internal set; }

	/// <summary>Simulation position in cells (<see cref="Navigation"/>, 16.16), updated once per park turn.</summary>
	public float X => Navigation.X / (float)Fixed16.One;
	public float Y => Navigation.Y / (float)Fixed16.One;
	/// <summary>Presentation position: interpolated between the last two steering steps (not simulation state).</summary>
	public float DrawX { get; internal set; }
	public float DrawY { get; internal set; }
	/// <summary>
	/// Where a body an object kept (state 16) was last placed by the guest update: the drawn position when the object took the guest. Presentation only, not saved or hashed; NaN outside such a use
	/// (a loaded park draws the kept body at the guest's own position).
	/// </summary>
	public float KeptBodyX { get; internal set; } = float.NaN;
	public float KeptBodyY { get; internal set; } = float.NaN;
	/// <summary>Unit direction of the last movement (game cells).</summary>
	public float HeadingX { get; internal set; } = 0;
	public float HeadingY { get; internal set; } = 1;
	public bool IsMoving { get; internal set; }
	/// <summary>Cells walked in total; drives the walk-cycle frame.</summary>
	public float DistanceWalked { get; internal set; }
	/// <summary>Presentation counterpart of <see cref="DistanceWalked"/>, interpolated like <see cref="DrawX"/>.</summary>
	public float DrawDistanceWalked { get; internal set; }
	public bool IsVisible => State is not (GuestState.Using or GuestState.HeldByGuard or GuestState.ChangingOut or GuestState.Gone);
	/// <summary>[EXT:pools-swim] In the water: swimming, or on the way back to the changing room's pool-side cell.</summary>
	public bool IsSwimming => State is GuestState.Swimming or GuestState.ClimbingOut;
	/// <summary>[EXT:pools-swim] Part of a swim session (the water or the changing back): such a guest names a changing room as its attraction and has no path cell.</summary>
	public bool InSwimSession => State is GuestState.Swimming or GuestState.ClimbingOut or GuestState.ChangingOut or GuestState.PoolActivity;
	/// <summary>[EXT:pools-activities] Doing a pool activity (state 25), drawn with the swim sprites.</summary>
	public bool InPoolActivity => State == GuestState.PoolActivity;
	// [EXT:pools-activities] the generic fields of a pool activity (docs/POOLS.md, "Activities"); all zero (the id null) outside state 25
	/// <summary>The <see cref="IPoolActivity.Id"/> of the activity the guest is doing.</summary>
	public string? PoolActivityId { get; internal set; }
	/// <summary>The thing id of the object that provides the activity (<see cref="PoolActivityOffer.TargetId"/>; 0 for none, such as a river).</summary>
	public int PoolActivityTarget { get; internal set; }
	/// <summary>The activity's own phase number (0 at the start).</summary>
	public int PoolActivityPhase { get; internal set; }
	/// <summary>Park turns left or spent in the current phase, as the activity counts them.</summary>
	public int PoolActivityTurns { get; internal set; }
	/// <summary>A point of the activity in 16.16 cells (the host sets the centre of the offered cell; the activity may move it anywhere inside the map).</summary>
	public int PoolActivityX { get; internal set; }
	public int PoolActivityY { get; internal set; }
	// [EXT:pools-swim] the swim session (docs/POOLS.md); all zero outside it
	/// <summary>The swimmer's heading, 0..63 (see <see cref="PoolSwimming.Direction"/>).</summary>
	public int SwimHeading { get; internal set; }
	/// <summary>Park turns left of the swim (state 22) or of the changing back (state 24).</summary>
	public int SwimTurns { get; internal set; }
	/// <summary>The stroke: 0 front crawl, 1 breaststroke, 2 treading water (sprite slots 8, 9, 10).</summary>
	public int SwimStroke { get; internal set; }
	/// <summary>The swimmer's goal point in 16.16 cells; <see cref="SwimHasGoal"/> tells whether it has one.</summary>
	public int SwimGoalX { get; internal set; }
	public int SwimGoalY { get; internal set; }
	public bool SwimHasGoal { get; internal set; }
	/// <summary>The swimmer's own random stream (xorshift32), seeded by one draw of the world generator when the swim starts.</summary>
	public uint SwimRandom { get; internal set; }

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
	/// <summary>The litter bin a guest in state 9 walks to and keeps as its target after the walk (guest <c>+0x1dc</c>, the ride word's other use) until the next major choice, a bin search of another bin, the leaving state or a hold; 0 otherwise.</summary>
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
	/// <summary>
	/// [EXT:umbrellas-extension] The guest's umbrella state (the umbrellas extension, docs/UMBRELLAS.md; Theme Park Inc's guest word <c>+0x238</c>): 0 none, 1 open and about to close, 2 owned and closed, 3 closed and
	/// about to open, 4 open (<see cref="UmbrellaRules"/>). Only a purchase at a brolly shop writes anything but 0 into it, and nothing writes 0 again.
	/// </summary>
	// [BIN:TPI-EXE:0x60d260 guest field names] mUmbrellaState +0x238, mUmbrellaType +0x244 (the colour), mUmbrellaScript +0x240 (the sprite object, rebuilt from the state on load here)
	public int UmbrellaState { get; internal set; }
	/// <summary>[EXT:umbrellas-extension] The umbrella's colour, the sprite slot 0 to 2 (<see cref="UmbrellaRules.Colours"/>); 0 while there is no umbrella.</summary>
	public int UmbrellaType { get; internal set; }
	/// <summary>
	/// [EXT:umbrellas-save-due] The park turn a scheduled change (state 1 or 3) is due, a multiple of four; 0 in every other state. Theme Park Inc's <c>+0x23C</c> is not saved; the extension saves it so a resumed park
	/// continues the same run.
	/// </summary>
	public long UmbrellaDueTurn { get; internal set; }
	/// <summary>
	/// [EXT:rain-mood-once-per-day] The game day plus one on which the rain mood rule (<c>opentpw.rain-mood</c>) last changed this guest's meters; 0 while it never has. Theme Park Inc stores its day at <c>+0x2E8</c> and never reads it,
	/// so the extension keeps its own to apply the rule once per qualifying day.
	/// </summary>
	public long RainMoodDay { get; internal set; }
	/// <summary>[EXT:winter-cold] The game day plus one of the cold rule's last application to the guest (as <see cref="RainMoodDay"/>); written only once it has applied, so a save without <c>opentpw.winter-cold</c> has no such field.</summary>
	public long ColdMoodDay { get; internal set; }
	/// <summary>
	/// [EXT:winter-items] The warm items the guest wears (the winter extension, docs/WINTER.md): bit 1 a hat, bit 2 earmuffs, bit 4 mittens (<see cref="WinterRules"/>); at most one head item. Only a purchase at a winter kiosk writes anything
	/// but 0 into it, and nothing takes an item off: the guest keeps it until it leaves, whatever the weather. The colours are a function of the guest id and the item, so they are not kept.
	/// </summary>
	public int WarmItems { get; internal set; }
	/// <summary>[EXT:winter-items] The guest's head item: <see cref="WinterRules.Hat"/>, <see cref="WinterRules.Earmuffs"/> or 0.</summary>
	public int HeadItem => WarmItems & WinterRules.HeadItems;
	/// <summary>[EXT:winter-items] Whether the guest wears mittens.</summary>
	public bool HasMittens => (WarmItems & WinterRules.Mittens) != 0;
	/// <summary>
	/// [EXT:winter-gluehwein] The park calendar day (<see cref="ParkCalendar.DayIndex"/>) the guest's warmth from a glühwein lasts until, exclusive: the guest counts as warmed while the park's day index is below it; 0 while it never was.
	/// Phase W3 only carries the field (saved, validated, hashed when set); the cold rule of phase W5 (<c>opentpw.winter-cold</c>) writes and reads it, and nothing in W3 does.
	/// </summary>
	public long WarmUntilDay { get; internal set; }
	/// <summary>[EXT:winter-save] Whether the guest carries any state of the winter extension (a warm item, or warmth from a glühwein): the park then uses the extension, whatever its shops (<see cref="ParkResumeMods.WinterInUse"/>).</summary>
	public bool HoldsWinter => WarmItems != 0 || WarmUntilDay != 0;
	/// <summary>
	/// [EXT:rain-mood-imported-age] The park turn an imported original guest without a known time in the park was loaded at, while the rain mood extension is on; its age for the rain mood's day gate counts from it. Null for every
	/// other guest (they have their time in the park) and in every park without the extension.
	/// </summary>
	public long? RainMoodAgeStartTurn { get; internal set; }
	/// <summary>Whether the guest wears a costume (guest <c>+0x24</c> == 2): a costume shop puts one on, and the next visit to one returns it.</summary>
	public bool InCostume { get; internal set; }
	public int Money { get; internal set; }
	/// <summary>
	/// The exit counter (guest <c>+444</c>, saved as <c>mExitLevel</c>): <c>PeepInfo.ExitLevel</c> plus a draw within ±<c>ExitLevelVar</c> at creation, one less on every
	/// needs phase of the guest (every fourth park turn, 0.992 s: the .sam's "seconds"), in every state. It is a signed word and runs negative after 0.
	/// </summary>
	public int ExitLevel { get; internal set; }
	/// <summary>Simulated seconds since creation, including the arrival and departure walk; null means the prior duration is unknown.
	/// Retained for stay statistics and weather gates; new visitor displays use the independent integer-turn age.</summary>
	public double? TimeInParkSeconds { get; internal set; }
	/// <summary>Display-only age in unsigned 32-bit park turns, or null for a legacy seconds-based duration. Never used by gameplay.</summary>
	public long? DisplayTimeInParkTurns { get; internal set; }
	/// <summary>
	/// The guest's sprite bank <c>+0x20</c> as the original stores it: 0x00541F70's (word >> 2) modulo the kid sets loaded when the guest was made (0x004FB1A7, 0x004FB1B5), the raw draw only where that count is
	/// not known (a headless run, <see cref="GuestSimulation.KidSetCount"/>). The renderers read it modulo the sets loaded now (<see cref="GuestAppearance.KidSet(Guest, int)"/>), the second reduction of a load (0x004F93AA);
	/// the simulation never reads it, so it is saved but not hashed. A guest of an original save keeps the saved bank number.
	/// </summary>
	public uint KidSetDraw
	{
		get => kidSetDraw ?? GuestAppearance.RawKidDraw( Id );
		internal set => kidSetDraw = value;
	}
	private uint? kidSetDraw;
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
	public int AttractionId
	{
		get => attractionId;
		// +0x1DC holds one id: naming an attraction replaces a bin the guest last used (the bin search 0x00500DC0, written at 0x004FEDEF by its caller, is the only other writer of a bin there)
		internal set
		{
			attractionId = value;
			if ( value != 0 )
				BinId = 0;
		}
	}
	private int attractionId;
	/// <summary>
	/// The saved major destination (guest <c>+0x1DE</c>; tp.exe's serializer names it <c>mSavedMajorDest</c> and the target <c>+0x1DC</c>, <see cref="AttractionId"/>, <c>mMajorDest</c>; the save keeps both, <c>0x004FD92A</c>): the attraction a guest had chosen when its minor decision (<c>0x004FD570</c>) sent it to another one on the way; the exit walk of the visit
	/// that follows (state 15, <c>0x00500923</c>) takes it up again as the target. 0 = none; cleared only by that and by the removal of the attraction it names.
	/// </summary>
	public int SavedMajorAttractionId { get; internal set; }
	/// <summary>The byte at +0x2C that state 10 counts while the guest walks to a ride with the park open; every twelfth count the minor decision runs (tp.exe 0x004FFEF2).</summary>
	public int RideWalkCount { get; internal set; }
	/// <summary>Park turns an expensive verdict still holds the guest at the booth (+0x200, tp.exe 0x004FF9D0): one is counted per turn, the verdict runs again as the last reaches 0. State 3 counts the same word down.</summary>
	public int BoothWait { get; internal set; }
	/// <summary>Whether the guest has paid its admission (guest <c>+0x204</c>, set by the booth's paying verdict <c>0x004FFAD5</c>, 0 at creation): a state-3 guest with it set walks on to the entrance, one without goes back to the booth.</summary>
	public bool Paid { get; internal set; }
	/// <summary>The visitor number (guest <c>+0x1D8</c>): the park's visitor count at the guest's arrival at the entrance (<c>0x004FFB20</c>, <c>0x0051AAF0</c>), 0 until then ("New Visitor").</summary>
	public int VisitorNumber { get; internal set; }
	/// <summary>
	/// The smarter-guests extension's birth id (docs/SMARTER-GUESTS.md): given at creation, before any booth decision, so traits can depend on it; 0 while the guest has none (the extension is
	/// off, or the guest predates it and its first tick has not run). Never saved in the guest snapshot (<see cref="SmarterGuestsSave"/> keeps it) and never in the guest state hash.
	/// </summary>
	// [EXT:smarter-guests] OpenTPW's own identity for the extension; the original guest has only its thing id
	public ulong BirthId { get; internal set; }
	/// <summary>The stranded mark (tp.exe <c>+0x198</c>): the cell map's stamp counter raised by one when the wander found no way on, 0 for none; the guest does not search again until a cell edit near it stamps a block above it.</summary>
	public int StrandedMark { get; internal set; }
	public int LastAttractionId { get; internal set; }
	/// <summary>The attractions the guest used last, newest first (guest <c>+480..+486</c>); the score divides by 5, 4, 3 or 2 for the first match. 0 = empty slot.</summary>
	public int[] UsedHistory { get; } = new int[HistorySlots];
	/// <summary>The attractions the guest recently turned away from (queue too long, too boring or too exciting), newest first (guest <c>+488..+494</c>); a 0 is shifted in every 20 park turns. 0 = empty slot.</summary>
	public int[] TemporaryHistory { get; } = new int[HistorySlots];
	private IReadOnlyList<int>? usedHistoryView, temporaryHistoryView;
	/// <summary>The used history as the decision layer sees it: a read-only wrapper of <see cref="UsedHistory"/> (the array is never replaced), made once.</summary>
	internal IReadOnlyList<int> UsedHistoryView => usedHistoryView ??= Array.AsReadOnly( UsedHistory );
	/// <summary>The temporary history as the decision layer sees it: a read-only wrapper of <see cref="TemporaryHistory"/>, made once.</summary>
	internal IReadOnlyList<int> TemporaryHistoryView => temporaryHistoryView ??= Array.AsReadOnly( TemporaryHistory );
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
	internal long CellSerial;         // the number the guest took when it entered the cell list it stands in: a cell's list is ordered by it, newest at the head (saved)
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
