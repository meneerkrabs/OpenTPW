namespace OpenTPW;

/// <summary>What a guest gets from an attraction besides the ride itself.</summary>
[Flags]
public enum GuestNeeds
{
	None = 0,
	Hunger = 1,
	Thirst = 2,
	Toilet = 4
}

public enum RideVisitorKind
{
	Ride,
	Shop,
	Sideshow,
	Facility
}

/// <summary>Outcome of the joining checks at the back of a queue (state 10, <c>0xeccb0</c>).</summary>
public enum QueueJoinResult
{
	Joined,
	/// <summary>The attraction is closed (or the guest id is invalid).</summary>
	Closed,
	AlreadyQueued,
	/// <summary>count ≥ 4 × queue cells.</summary>
	NoRoom,
	/// <summary>|preferred − excitement| ≥ 45 with the ride below the guest's preference.</summary>
	NotExcitingEnough,
	/// <summary>|preferred − excitement| ≥ 45 with the ride above the guest's preference.</summary>
	TooExciting,
	/// <summary>count ≥ the data limit (100, or the QueueWaitTimeConstant formula).</summary>
	QueueTooLong
}

/// <summary>
/// One admission evaluation of a ride update (one per park turn). Admission calls the head whenever the gates hold
/// and it stands at position 0, so the progress signals are <see cref="HeadNotReady"/> (counted by
/// <see cref="RideVisitorBridge.HeadNotReadyStreak"/>) and the called guest's age (<see cref="RideVisitorBridge.CalledAgeTurns"/>).
/// </summary>
public readonly record struct AdmissionCheck( long Turn, bool ConditionsHold, int HeadGuest, bool HeadAtFront, int CalledGuest )
{
	/// <summary>The gates held and there is a head guest, but it does not yet stand in state 11 at position 0.</summary>
	public bool HeadNotReady => ConditionsHold && HeadGuest != 0 && !HeadAtFront;
}

/// <summary>
/// Queue rules of one object from its .sam layers (docs/reverse/QUEUE-plan.md §2): <c>Info.HasQueue</c>,
/// <c>Info.RunsContinuously</c>, the capacity-test bypass (<c>Bumper.WhichTrackType</c> 2 or 3) and the inputs of
/// the queue limit: <c>Upgrades[L].QueueWaitTimeConstant</c>, SPEED and <c>Upgrades[L].InitSpeed</c>, CAP and DUR.
/// The join excitement (<see cref="QueueJoinRules"/>) also divides DUR by the raw <c>Upgrades[L].InitDuration</c>,
/// which differs from DUR when the data lies outside <c>UsageInfo.MinDuration..MaxDuration</c>.
/// </summary>
/// <param name="InitDuration">The raw <c>Upgrades[L].InitDuration</c>; null reads as <paramref name="Duration"/> (the ratio is 1).</param>
public sealed record QueueParameters( bool HasQueue, bool RunsContinuously, bool SkipsCapacityTest, float QueueWaitTimeConstant, int Speed, int InitSpeed, int Capacity, int Duration, int? InitDuration = null )
{
	/// <summary>A queue-less object without data: the limit is the floor of 4.</summary>
	public static readonly QueueParameters Default = new( false, false, false, 0, 0, 0, 0, 0 );
}

/// <summary>
/// Receives visitor events from an attraction. Implemented by <see cref="GuestSimulation"/>.
/// Calls arrive from the fixed simulation tick in a deterministic order.
/// </summary>
public interface IRideVisitorHost
{
	/// <summary>The attraction called the front guest forward (ride <c>+104</c> = guest; the guest walks to the stand point).</summary>
	void OnVisitorOffered( IRideVisitorBridge ride, int guestId );
	/// <summary>The script took the guest (WALKON/BOUNCE, or it consumed VAR_LETMEON).</summary>
	void OnVisitorBoarded( IRideVisitorBridge ride, int guestId );
	/// <summary>The ride let go of a guest it held without the script's let-off (its script stopped or faulted, or a save's leftover rider): host teardown.</summary>
	void OnVisitorReleased( IRideVisitorBridge ride, int guestId );
	/// <summary>
	/// The ride update's dismiss (tp.exe <c>0x004E1410</c>) found the guest in VAR_LETMEOFF and runs its ExitRide (<c>0x005014E0</c>): true when the guest left (state 15), false when the exit is blocked and
	/// the guest stays on the ride. Hosts that do not model the exit walk treat it as <see cref="OnVisitorReleased"/>.
	/// </summary>
	bool OnVisitorExitRide( IRideVisitorBridge ride, int guestId )
	{
		OnVisitorReleased( ride, guestId );
		return true;
	}
	/// <summary>The guest left the queue without riding (ride closed or removed).</summary>
	void OnVisitorTurnedAway( IRideVisitorBridge ride, int guestId );
	/// <summary>
	/// The broken or closed ride dismissed its head guest (<c>0xee804</c>, tp.exe <c>0x4e0450</c> → <c>0x5012f0</c> from the ride update: ride states 1, 2 and 4, or state 0 with the open word 0): the guest loses
	/// happiness and wanders off. Hosts that do not model that treat it as <see cref="OnVisitorTurnedAway"/>.
	/// </summary>
	void OnVisitorDismissed( IRideVisitorBridge ride, int guestId ) => OnVisitorTurnedAway( ride, guestId );
	/// <summary>The park turn (<see cref="ParkCalendar.Turn"/>); attractions evaluate admission once per turn. −1: every step.</summary>
	long ParkTurn => -1;
	/// <summary>True when called guests walk to the stand point and present themselves (<see cref="RideVisitorBridge.PresentForBoarding"/>).</summary>
	bool WalksToBoard => false;
	/// <summary>The guest stands in the queue (state 11) at recorded position 0.</summary>
	bool IsStandingAtFront( IRideVisitorBridge ride, int guestId ) => true;
}

/// <summary>
/// The guest-facing side of one placed attraction. The guests slice drives queues through it; the
/// rides/objects slice supplies the cells and metadata from the object catalog (.sam <c>Info.Shape</c>
/// entrance '2' / exit 'S' cells, <c>UsageInfo.ExcitementLevel</c>, <c>Info.AttractionValue</c>, capacity).
/// <see cref="RideVisitorBridge"/> implements it on top of an RSE script.
/// </summary>
public interface IRideVisitorBridge
{
	/// <summary>Stable, unique, positive id per placed attraction (used for deterministic ordering).</summary>
	int AttractionId { get; }
	string Name { get; }
	RideVisitorKind Kind { get; }
	bool IsOpen { get; }
	/// <summary>
	/// A guest walking to the attraction may join its queue: the original's join (state 10, <c>0x004FFBC0</c>) reads neither the open word nor the
	/// ride state, so a closed or broken ride still takes joiners and sends them away from the queue head. Hosts without the distinction report <see cref="IsOpen"/>.
	/// </summary>
	bool AcceptsJoins => IsOpen;
	/// <summary>The coaster or track refuses guests (<c>0x00441970</c>, the track word): a queued guest leaves with thought 13 (state 11, <c>0x004FFFF0</c>).</summary>
	bool TrackRefusesGuests => false;
	int Capacity { get; }
	/// <summary>The capacity the attraction runs with now (a ride's settings change it); the throughput estimate of the smarter guests restarts when it changes. Hosts without a live value report <see cref="Capacity"/>.</summary>
	int LiveCapacity => Capacity;
	/// <summary>.sam <c>UsageInfo.ExcitementLevel</c> (0–100).</summary>
	int ExcitementLevel { get; }
	/// <summary>.sam <c>Info.AttractionValue</c>.</summary>
	int AttractionValue { get; }
	/// <summary>
	/// The excitement the original's <c>0xe02ec(ride, 0)</c> gives (the score, the join gate and the ride outcome read it): 0 when the type's level is 0,
	/// else the sideshow formula, a coaster's guest value, or the level scaled by the ride's speed and duration (<see cref="QueueJoinRules"/>).
	/// Hosts without a live value report <see cref="ExcitementLevel"/>.
	/// </summary>
	int LiveExcitement => ExcitementLevel;
	/// <summary>The type's <c>UsageInfo.ExcitementLevel</c> word (type <c>+316</c>); its low byte decides whether the score counts the excitement weight (<c>0xe02ec(ride, 1)</c>).</summary>
	int StaticExcitementLevel => ExcitementLevel;
	/// <summary>Type <c>UsageInfo.ThirstEffect</c> (<c>+324</c>): how much thirst a visit deducts; the score's thirst match reads it.</summary>
	int ThirstEffect => 0;
	/// <summary>Type <c>UsageInfo.HungerEffect</c> (<c>+328</c>).</summary>
	int HungerEffect => 0;
	/// <summary>Type <c>UsageInfo.VomitEffect</c> (<c>+332</c>): how much illness a visit adds.</summary>
	int VomitEffect => 0;
	/// <summary>Type <c>UsageInfo.InitCostOfGoods</c> (<c>+320</c>): the base of the price test's willingness to pay.</summary>
	int InitCostOfGoods => 0;
	/// <summary>Type <c>UsageInfo.RipOffOK</c> (<c>+0x16C</c>): the price test's per-type word, a premium in percent on the willingness to pay.</summary>
	int RipOffOk => 0;
	/// <summary>Type <c>UsageInfo.FatigueEffect</c> (<c>+0xE8</c>): what a completed use takes off the guest's meter <c>+0x1B8</c>.</summary>
	int FatigueEffect => 0;
	/// <summary>The live goods words: cost of goods <c>+388</c>, price <c>+400</c> and the losing chance behind the win byte <c>+396</c> (shops and sideshows).</summary>
	SideshowTerms Sideshow => default;
	/// <summary>True when <see cref="Sideshow"/> holds live terms; without them the price test falls back to the bridge's own price.</summary>
	bool HasSideshowTerms => false;
	/// <summary>Type <c>UsageInfo.HappinessEffect</c> (<c>+336</c>): how much happiness a visit adds.</summary>
	int HappinessEffect => 0;
	/// <summary>Type <c>UsageInfo.LitterEffect</c> (tp.exe type <c>+0x154</c>): how much a visit adds to the guest's litter meter.</summary>
	int LitterEffect => 0;
	/// <summary>Type <c>UsageInfo.GoldenTicketCost</c> (<c>+196</c>): above 0 raises the score by <c>1 + (cost + 1) / 10</c>.</summary>
	int GoldenTicketCost => 0;
	/// <summary>Type <c>+440</c>, <c>Upgrades[0].CostOfUpgrade</c>: above 3000 raises the score of a ride without a golden-ticket cost.</summary>
	int UpgradeCost => 0;
	/// <summary>Type <c>UsageInfo.ISIndoors</c> (<c>+276</c>): a shelter from the rain; its score is multiplied while drops fall.</summary>
	bool IsIndoors => false;
	/// <summary>The object type (<c>Info.Id</c>); a guest that just used one attraction scores every other of the same type 0.</summary>
	int TypeId => AttractionId;
	/// <summary>Days since the attraction was bought (<c>0xdc520</c>); a ride up to <c>DecisionVariable1</c> days old is new. <see cref="uint.MaxValue"/> when unknown.</summary>
	uint AgeDays => uint.MaxValue;
	/// <summary>Needs a visit satisfies (shops, toilets).</summary>
	GuestNeeds Satisfies { get; }
	/// <summary>Price per visit charged to the guest (0 for rides: TPW charges admission).</summary>
	int Price { get; }
	/// <summary>.sam <c>UsageInfo.SpecialIngredient</c> (type <c>+344</c>): 0 none, 1 Fat, 2 Salt, 3 Ice, 4 Sugar. Only Sugar changes a guest here: it raises the walk speed boost (<see cref="GuestSpeed.RaiseBoost"/>).</summary>
	int SpecialIngredient => 0;
	/// <summary>.sam <c>UsageInfo.AppearanceEffect</c> (tp.exe type <c>+0x15C</c>): 0 none, 1 a balloon, 2 a costume (the balloon and costume shops).</summary>
	int AppearanceEffect => 0;

	/// <summary>
	/// [EXT:umbrellas-extension] The score multipliers of an object of the umbrellas extension (a brolly shop): its weather and generation percentages, which the host score applies for these objects only; null for every other object, whose
	/// score is the game's alone (docs/UMBRELLAS.md).
	/// </summary>
	UmbrellaShopTerms? UmbrellaTerms => null;

	/// <summary>[EXT:umbrellas-extension] The attraction is a brolly shop: an object of the umbrellas extension whose use gives the guest an umbrella (<see cref="UmbrellaRules.ShopAppearanceEffect"/>).</summary>
	bool IsUmbrellaShop => UmbrellaTerms != null && AppearanceEffect == UmbrellaRules.ShopAppearanceEffect;

	/// <summary>
	/// [EXT:winter-extension] The score multipliers of an object of the winter extension (a winter kiosk or a glühwein stall): its weather and season percentages, which the host score applies for these objects only; null for every
	/// other object, whose score is the game's alone (docs/WINTER.md).
	/// </summary>
	WinterShopTerms? WinterTerms => null;

	/// <summary>[EXT:winter-extension] The attraction is a winter kiosk: an object of the winter extension whose use gives the guest a warm item (<see cref="WinterRules.KioskAppearanceEffect"/>).</summary>
	bool IsWinterKiosk => WinterTerms != null && AppearanceEffect == WinterRules.KioskAppearanceEffect;
	/// <summary>The amount of the special ingredient (ride <c>+404</c> byte, 0..255); the ride build sets 50.</summary>
	int IngredientAmount => GuestSpeed.DefaultIngredientAmount;
	/// <summary>The shop's quality of goods (ride <c>+0x18C</c> byte); the ride build sets 50 (<see cref="GuestAppearance.BuiltQualityOfGoods"/>).</summary>
	int QualityOfGoods => GuestAppearance.BuiltQualityOfGoods;
	/// <summary>Walkable path cell where guests join the queue.</summary>
	(int X, int Y) EntranceCell { get; }
	/// <summary>The entrance's outside cell (the original front cell), or null to use <see cref="EntranceCell"/>.</summary>
	(int X, int Y)? QueueFrontCell => null;
	/// <summary>Walkable path cell where released guests reappear.</summary>
	(int X, int Y) ExitCell { get; }
	/// <summary>.sam <c>UsageInfo.EntryCellStandPosX/Y</c>: where in its entrance cell a called guest stands (0..1 per axis).</summary>
	(float X, float Y) EntryCellStandPos => (0.5f, 0.5f);
	/// <summary>.sam <c>UsageInfo.ExitCellAppearPosX/Y</c> (type record +0xDC, +0xE0): where in its exit cell a guest the ride lets off appears (0..1 per axis, rotated like the stand point).</summary>
	(float X, float Y) ExitCellAppearPos => (0.5f, 0.5f);
	/// <summary>
	/// The object's own exit cell (the original's +0x38): the cell of the shape's exit symbol, or of its entrance for an object without an exit; null for a bridge without a placement, whose exit is the cell
	/// it takes its guests in (<c>GuestSimulation.StandCellOf</c>).
	/// </summary>
	(int X, int Y)? ExitAccessCell => null;
	/// <summary>
	/// The object's access cell (the original's thing <c>+0x36</c>): the cell of the shape's first entrance symbol, inside the footprint (a type 9 cell on a typed map), where the guests' route requests of the minor decision and the bin
	/// walk end and the route lengths of the minor decision start; null for a bridge without a placed object, whose guests use <see cref="EntranceCell"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x004DB33C access cell] the constructor 0x004DB090 stores the type record's first entrance cell (+0x494, the first type 9 cell of the shape scan 0x413410, (0, 0) without one) turned by the object's angle (0x4D9CC0) and added to the thing's own cell index (y × 128 + x + 1) into +0x36; no other instruction writes it (the exit cell +0x38 and +0x34 are written beside it)
	(int X, int Y)? AccessCell => null;
	/// <summary>The cell the object's thing stands on (the original's <c>+5</c>, <c>+7</c>), the anchor of its placement: the cell lists the minor decision's window reads hold the object there; null for a bridge without a placed object (<see cref="EntranceCell"/> stands in).</summary>
	(int X, int Y)? PositionCell => null;
	/// <summary>The object's rotation in degrees (0/90/180/270), which rotates <see cref="EntryCellStandPos"/>.</summary>
	int Rotation => 0;
	int QueueLength { get; }
	/// <summary>min(4 × queue cells, the data limit).</summary>
	int MaximumQueueLength { get; }
	/// <summary>The data limit: the QueueWaitTimeConstant formula with <c>Info.HasQueue</c>, else 100 (unsigned, like the original).</summary>
	uint QueueLimit { get; }
	bool HasQueue { get; }
	bool ProvidesRelief { get; }
	/// <summary>The ride script reports a breakdown (<c>VAR_BROKEN</c>, the original's ride state 1 or 4): its queue is dismissed head first (a joiner too, once it is the head).</summary>
	bool IsBroken { get; }
	/// <summary>The ride is in state 1 (broken down) until it is opened again; choices and the queue's move-up read this, not <c>VAR_BROKEN</c>.</summary>
	bool IsBrokenDown { get; }
	/// <summary>A queued guest's move-up holds its walk while the ride is in state 1 only (<c>0x004E0370</c>); hosts without the state 4 distinction report <see cref="IsBrokenDown"/>.</summary>
	bool HoldsQueuedGuests => IsBrokenDown;
	/// <summary>The original's ride-failure predicate <c>0xdfe34</c>: a relief object whose wear gauge is below 25. Queueing guests leave "feeling sick".</summary>
	bool ReliefWornOut { get; }
	/// <summary>The ride script's <c>VAR_RUNNING</c> is non-zero.</summary>
	bool IsRunning { get; }
	/// <summary>The ride script's <c>VAR_ONRIDE</c>.</summary>
	int RidersOnBoard { get; }
	/// <summary>The ride's operating speed.</summary>
	int Speed { get; }
	/// <summary>Queue cells from the front (index 0) to the back; empty for a ride whose entrance has no queue direction (a zero front, tp.exe <c>0x004DE040</c>).</summary>
	IReadOnlyList<(int X, int Y)> QueueCells { get; }
	/// <summary>The back-of-queue cell (<c>0x004DE110</c>): the last queue cell, or null when the queue is empty (the original's back cell of 0).</summary>
	/// <summary>The back cell was read from the entry cell's links (a typed world, <c>0x004DE040</c>), not made up by the host: it counts as the original's back cell whatever the cell holds.</summary>
	bool HasTypedBack => false;
	(int X, int Y)? BackCell => QueueCells.Count > 0 ? QueueCells[^1] : null;
	int QueueSizeInCells { get; }
	/// <summary>Walkable path cell where guests step onto the back of the queue; null when the queue is not connected.</summary>
	(int X, int Y)? JoinCell { get; }
	/// <summary>
	/// [EXT:pools-swim] The object is a pool complex's changing room (<c>Pools.ChangingRoom</c> in its .sam): a guest released from it goes swimming instead of walking on, when the
	/// pool beside it has water (<see cref="PoolAccessCandidates"/>).
	/// </summary>
	bool IsChangingRoom => false;
	/// <summary>The object's <c>UsageInfo.RequiresTeleport</c>: its guests beam in and out at its entrance and exit (tp.exe 0x501db0 states 15 and 16).</summary>
	bool RequiresTeleport => false;
	/// <summary>UsageInfo.RideHandlesSprite: retain the visitor body at admission, otherwise delete it.</summary>
	bool RideHandlesSprite => false;
	/// <summary>
	/// [EXT:pools-swim] The object has been stopped (demolished, or its script ended): the guests it releases then step out on its exit like those of any attraction, and no swim starts.
	/// </summary>
	bool IsStopped => false;
	/// <summary>[EXT:pools-swim] The cells beyond the changing room's back (opposite its entrance), in the order the first pool cell among them is taken as the pool-side cell; empty for any other object.</summary>
	IReadOnlyList<(int X, int Y)> PoolAccessCandidates => Array.Empty<(int X, int Y)>();
	/// <summary>Incremented when a recompute changed the queue cells.</summary>
	int QueueEditCount { get; }
	/// <summary>The guest called forward, 0 when none.</summary>
	int CalledGuest { get; }
	IRideVisitorHost? Host { get; set; }

	bool TryJoinQueue( int guestId );
	/// <summary>
	/// Joins the back of the queue. <paramref name="preferredExcitement"/> is the guest's preferred excitement for the
	/// excitement gate (<see cref="QueueJoinRules"/>); null skips the gate.
	/// </summary>
	QueueJoinResult JoinQueue( int guestId, int? preferredExcitement = null );
	/// <summary>
	/// The joining checks of <see cref="JoinQueue"/> that belong to the attraction: open, not listed, room. <see cref="QueueJoinResult.Joined"/> means they hold and nothing was changed; the excitement gate
	/// and the queue limit are the guests' decision layer's (<see cref="IGuestDecisions.DecideAdmission"/>), and <see cref="AppendToQueue"/> then joins the guest.
	/// </summary>
	QueueJoinResult CheckJoinRoom( int guestId );
	/// <summary>Appends a guest <see cref="CheckJoinRoom"/> let through at the tail of the queue.</summary>
	void AppendToQueue( int guestId );
	/// <summary><c>0xe9a84</c>: the difference between the live excitement and a guest's preferred one, the join gate's input.</summary>
	int JoinDifference( int preferredExcitement );
	/// <summary>0-based position in the queue, or −1.</summary>
	int GetQueuePosition( int guestId );
	bool LeaveQueue( int guestId );
	/// <summary>Recomputes the queue cells from the grid; true when they changed.</summary>
	bool RecomputeQueue( GuestPathGrid grid );
	/// <summary><see cref="RecomputeQueue"/> for a recompute that is no re-evaluation (0x004DE1F0): a cached back (+0x3A) stays. Defaults to <see cref="RecomputeQueue"/> for a bridge without such a cache.</summary>
	bool RecomputeQueueKeepingBack( GuestPathGrid grid ) => RecomputeQueue( grid );
	/// <summary>Direction from queue cell <paramref name="index"/> toward its predecessor (toward the ride entrance for the front cell).</summary>
	int DirectionTowardsFront( int index );
	/// <summary>The called guest reached the stand point; true once its id is in VAR_LETMEON.</summary>
	bool PresentForBoarding( int guestId );
}
