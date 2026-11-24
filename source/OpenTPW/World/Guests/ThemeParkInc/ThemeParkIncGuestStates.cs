namespace OpenTPW;

/// <summary>
/// Theme Park Inc's guest states (the state word <c>+0x2C0</c>, 0..30) and the pure rules over them: the name table <c>0x9D069C</c>, the guard's catch test <c>0x614720</c>, the queue and ride predicate
/// <c>0x61AC40</c>, the animation mode of the setter's entry table <c>0x61AA54</c> and the projection onto Theme Park World's <see cref="GuestState"/> for the readers outside the simulation
/// (docs/reverse/TPI-GUESTS.md). Every number differs from Theme Park World's <c>KIDSTATES</c> from 8 on.
/// </summary>
public static class ThemeParkIncGuestStates
{
	public const int WalkingToCrossing = 0;
	public const int WaitingToCross = 1;
	public const int WalkingToBooth = 2;
	public const int WaitingForPark = 3;
	public const int PayingAtBooth = 4;
	public const int WalkingToEntrance = 5;
	public const int Idle = 6;
	public const int Wandering = 7;
	public const int SpotAnimation = 8;
	public const int WalkingToBin = 9;
	public const int WalkingToBinForPrank = 10;
	public const int WalkingToPlantForPrank = 11;
	public const int WalkingToInfodesk = 12;
	public const int WalkingToMajor = 13;
	public const int StandingInQueue = 14;
	public const int TakingQueueStep = 15;
	public const int BeingLoaded = 16;
	public const int WaitingToBeAdmitted = 17;
	public const int WaitingForInfodesk = 18;
	public const int BeingUnloaded = 19;
	public const int UnderObjectControl = 20;
	public const int UnderGuardControl = 21;
	public const int LeavingPark = 22;
	public const int Frozen = 23;
	public const int Whinging = 24;
	public const int SpreadingRumours = 25;
	public const int ListeningToRumours = 26;
	public const int WaitingForFirstAid = 27;
	public const int WaitingToCrossRoad = 28;
	public const int CrossingRoadToLeave = 29;
	public const int WaitingToBePickedUp = 30;
	/// <summary>The states the dispatch <c>0x619FF0</c> and the setter <c>0x61A540</c> know: a larger word returns at once (<c>ja 0x61A3A0</c>, <c>ja 0x61AA49</c>).</summary>
	public const int Count = 31;

	/// <summary>The debug names of the states (the table <c>0x9D069C</c>, 31 pointers).</summary>
	// [BIN:TPI-EXE:0x9D069C state names] the strings 0x9D0A58 (state 0) down to 0x9D0750 (state 30); entry 31 is the table's end (0)
	public static IReadOnlyList<string> Names { get; } =
	[
		"WALKING TO BS SIDE OF CROSSING", "WAITING TO CROSS ROAD INTO PARK", "WALKING TO TICKET BOOTHS", "WAITING FOR PARK TO OPEN", "PAYING AT BOOTH", "WALKING TO ENTRANCE", "IDLE",
		"WANDERING", "PLAYING SPOT ANIM", "WALKING TO LITTER_BIN", "WALKING TO LITTER BIN FOR PRANK", "WALKING TO PLANT FOR PRANK", "WALKING TO INFODESK", "WALKS TO MAJOR",
		"STANDING IN QUEUE", "TAKING QUEUE STEP", "BEING LOADED", "WAITING FOR SCRIPT TO ADMIT ME", "WAITING FOR INFODESK DECISION", "BEING UNLOADED", "UNDER OBJECT CONTROL",
		"UNDER GUARD CONTROL", "LEAVING PARK", "FROZEN", "WHINGING TO STAFF", "SPREADING VICIOUS RUMOURS", "LISTENING TO VICIOUS RUMOURS", "WAITING FOR FIRST AID",
		"WAITING TO CROSS ROAD", "CROSSING ROAD TO LEAVE", "WAITING TO BE PICKED UP"
	];

	/// <summary>
	/// The guard's catch test (<c>0x614720</c>): 0 for the arrival states 0..5, the queue, ride, infodesk, object and guard states 14..21 and 23..30; 1 for everything else (6..13 and 22, and any
	/// word outside 0..30).
	/// </summary>
	// [BIN:TPI-EXE:0x614720 catch test] eax = 1, then je to the 0 exit for the words 0, 1, 2, 3, 4, 5, 0xE, 0xF, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D
	// and jne past it for 0x1E; signed or unsigned does not matter, the compares are equalities
	public static bool IsCatchable( int state ) =>
		state is not ((>= 0 and <= 5) or (>= 0xE and <= 0x15) or (>= 0x17 and <= 0x1E));

	/// <summary>
	/// Whether this build runs <paramref name="state"/>'s handler and setter entry (phases P1 and P2): the arrival and leaving states, idle, the wander, the spot animation, the walk to a litter bin,
	/// frozen, first aid and the unreachable 28 and 29. The later phases' states (10..21, 24..26) are not ported; a restore refuses a guest in one.
	/// </summary>
	public static bool RunsInThisBuild( int state ) => state is (>= WalkingToCrossing and <= WalkingToBin) or LeavingPark or Frozen
		or (>= WaitingForFirstAid and <= WaitingToBePickedUp);

	/// <summary>The queue and ride predicate (<c>0x61AC40</c>): the state, or the saved state while a spot animation (8) runs, is 14..17.</summary>
	// [BIN:TPI-EXE:0x61AC40 queue predicate] eax = +0x2C0, +0x2C4 when it is 8; 1 for 0xE <= eax <= 0x11 (signed jl/jg), else 0
	public static bool InQueueOrLoading( int state, int saved )
	{
		var effective = state == SpotAnimation ? saved : state;
		return effective is >= StandingInQueue and <= WaitingToBeAdmitted;
	}

	/// <summary>
	/// The animation mode the setter's entry writes for <paramref name="state"/> through <c>0x5D0F90</c> (1 walking, 3 standing), or null when the entry writes none or writes it only under a
	/// condition (state 4 at a booth, the photo booth's 19, the object's 20, the guard's 21, the rumour's 26).
	/// </summary>
	// [BIN:TPI-EXE:0x61AA54 setter entries] 0x61A9E2 (mode 1) for 0, 2, 5, 9, 13, 16 and 29; 0x61A998 (mode 3) for 1, 6 and 28; mode 3 also in 3 (0x61A5D2), 14 (0x61A606), 17 (0x61A66A), 27 (0x61AA35)
	// and 30 (0x61A96F); mode 1 also in 7 (0x61A9C7), 15 (0x61A64D) and 22 (0x61A952); 0x61AA49 (nothing) for 10, 11, 12, 18, 23, 24 and 25; 8 (0x61A9AC) stamps the turn only
	public static int? EntryMode( int state ) => state switch
	{
		WalkingToCrossing or WalkingToBooth or WalkingToEntrance or Wandering or WalkingToBin or WalkingToMajor or TakingQueueStep or BeingLoaded or LeavingPark or CrossingRoadToLeave => 1,
		WaitingToCross or WaitingForPark or Idle or StandingInQueue or WaitingToBeAdmitted or WaitingForFirstAid or WaitingToCrossRoad or WaitingToBePickedUp => 3,
		_ => null
	};

	/// <summary>
	/// The <see cref="GuestState"/> the readers outside the simulation see for a Theme Park Inc state (the HUD, the renderer, the gate count, the statistics, the resume checks): the Theme Park World
	/// state of the same meaning, by name and role, never by number.
	/// </summary>
	public static GuestState Projection( int state ) => state switch
	{
		WalkingToCrossing => GuestState.CrossingRoad,
		WaitingToCross => GuestState.CrossingRoadToPark,
		WalkingToBooth => GuestState.GoingToTicketBooth,
		WaitingForPark => GuestState.WaitingAtBooth,
		PayingAtBooth => GuestState.AtTicketBooth,
		WalkingToEntrance => GuestState.EnteringPark,
		Wandering or WalkingToBinForPrank or WalkingToPlantForPrank or WalkingToInfodesk => GuestState.WalkingAround,
		WalkingToBin => GuestState.ThrowingAwayLitter,
		WalkingToMajor => GuestState.GoingToRide,
		StandingInQueue => GuestState.Queueing,
		TakingQueueStep => GuestState.MovingUpQueue,
		BeingLoaded => GuestState.Boarding,
		WaitingToBeAdmitted => GuestState.WaitingToBoard,
		BeingUnloaded => GuestState.ExitingRide,
		UnderObjectControl => GuestState.Using,
		UnderGuardControl => GuestState.HeldByGuard,
		LeavingPark => GuestState.LeavingPark,
		WaitingToCrossRoad or CrossingRoadToLeave => GuestState.CrossingRoadHome,
		WaitingToBePickedUp => GuestState.WaitingToGoHome,
		// 6, 8 (a spot animation), 18 (the infodesk's decision), 23 (frozen), 24..26 (whinging and rumours) and 27 (first aid) stand in the park
		_ => GuestState.DoingNothing
	};
}
