namespace OpenTPW;

// Theme Park Inc's own guest state machine (docs/reverse/TPI-GUESTS.md, phase P1 of the port): the setter 0x61A540 with its entry table, the dispatch 0x619FF0 (table 0x61A3A8),
// the pre-update 0x619B50 with the weather rule 0x611050, the constructor's tail and the arrival and leaving states (idle, the wander and the walk to a bin, phase P2, are in
// GuestSimulation.ThemeParkIncIdle.cs). It runs on the shared Guest records, inside the shared
// sweep, for the guests that carry a Theme Park Inc record (Guest.Tpi); every other guest keeps the Theme Park World update untouched. The walk step, the route search, the
// wander and the speed stay Theme Park World's ([EXT:tpi-twp-guest-rules]), as for the staff.
public sealed partial class GuestSimulation
{
	/// <summary>
	/// Whether the next park a level builds runs Theme Park Inc's native guest states (the hidden <c>--tpi-native-guests</c> option); off by default. A level reads it once, when it builds its
	/// guest simulation (<see cref="ThemeParkIncNativeGuests"/>).
	/// </summary>
	public static bool ThemeParkIncNativeGuestsRequested { get; set; }

	/// <summary>
	/// Whether the next Theme Park Inc park a level builds walks its guests and staff with Theme Park Inc's own navigator rules (the hidden <c>--tpi-native-walk</c> option,
	/// <c>ThemeParkIncNativeWalk</c> of docs/reverse/TPI-NAVIGATOR.md); off by default, so the Theme Park Inc determinism pins move once, when the port's last phase turns it on. A level reads it once,
	/// when it builds its cell map (<see cref="ParkCellMap.EdgeRules"/>, <see cref="WalkEdges.RulesForNewPark"/>); a save records it and a restore refuses the other setting
	/// (<see cref="GuestSimulationSnapshot.ThemeParkIncNativeWalk"/>). So far it selects Theme Park Inc's edge test.
	/// </summary>
	public static bool ThemeParkIncNativeWalkRequested { get; set; }

	/// <summary>
	/// Whether the Theme Park Inc guests this simulation builds run Theme Park Inc's own state machine (<see cref="Guest.Tpi"/>, docs/reverse/TPI-GUESTS.md) instead of Theme Park World's
	/// update under <c>[EXT:tpi-twp-guest-rules]</c>. Off by default; it takes effect only with Theme Park Inc's guest settings (<see cref="GuestSettings.TpiGuestStart"/> and
	/// <see cref="GuestSettings.TpiNative"/>). Set before the first guest is built.
	/// </summary>
	public bool ThemeParkIncNativeGuests { get; set; }

	/// <summary>Whether this simulation runs Theme Park Inc's native guest states: the switch is on and the park has Theme Park Inc's guest settings (<see cref="NativeTpi"/>).</summary>
	public bool RunsThemeParkIncNativeGuests => NativeTpi != null;

	/// <summary>The native settings while the switch is on and the park has them; null otherwise (no guest gets a Theme Park Inc record).</summary>
	private ThemeParkIncGuestSettings? NativeTpi => ThemeParkIncNativeGuests && Settings.TpiGuestStart != null ? Settings.TpiNative : null;

	/// <summary>Theme Park Inc's vehicle names, by kind (<c>0x9D53EC</c> "bus", <c>0x9D53F8</c> "heli", <c>0x9D5404</c> "mono"; guest-states-p0.md §3).</summary>
	public static readonly IReadOnlyList<string> ThemeParkIncVehicleNames = ["bus", "heli", "mono"];

	/// <summary>A creation stamp, the generation and the type's words for a new native guest: the constructor's fields the shared record does not hold.</summary>
	// [BIN:TPI-EXE:0x60C589 creation stamp] +0x1FC = the world turn; [BIN:TPI-EXE:0x60C67D generation] +0x2A4, +0x2A8 and +0x2AC from the type's row (0xA01C30, 0xA01C24, 0xA01C2C)
	private ThemeParkIncGuestRecord NewThemeParkIncRecord( ThemeParkIncGuestProfile profile ) => new()
	{
		ArrivalTurn = ParkTurn, Generation = profile.Generation, PreferredExcitement = profile.PreferredExcitement, BoredomThreshold = profile.BoredomThreshold
	};

	/// <summary>The park turn as Theme Park Inc's world turn word <c>+0x1EAB40</c> (unsigned 32 bits).</summary>
	private uint TpiTurn => unchecked( (uint)ParkTurn );

	// ---- The constructor's tail ----------------------------------------------------------------------------------------------------

	/// <summary>
	/// The constructor's tail (<c>0x60C9F5..0x60CB7F</c>) for a native guest placed at its cell: on a park cell the visitor number and state 6; otherwise two world draws whose results are
	/// dropped, the walk to a ticket booth with the <c>^ 1</c> retry and state 2 with the arrival beam.
	/// </summary>
	// [BIN:TPI-EXE:0x60C9F5 in-park branch] 0x60BE00 (cell types 1, 3, 9, 10 and 0) true: +0x200 = 0x650560() (the park's visitor count, raised by one) and setter(6), no draw and no beam
	// [BIN:TPI-EXE:0x60CA29 entrance branch] R10 & 1 picks a crossing cell (0x5D7B10) whose result is never read (its slot is overwritten at 0x60CAF7); R11 (0x60CA44) is overwritten by the id at 0x60CA51
	// [BIN:TPI-EXE:0x60CA49 booth pick] i = id % NumTicketBooths (unsigned); for k below the count (signed): 0x60B7F0(booth i) and stop on success, else i ^= 1; then +0x228 = 0, setter(2),
	// 0x60AE00 (the attachments, presentation) and 0x60B700(0x13, 0, 0) (the arrival beam, particle 19)
	// [BIN:TPI-EXE:0x60CAD9 tail] +0x22C = 0 and the eight history words +0x20A..+0x219 zeroed (a new record already holds them so); the creation message 0x435FC0 is a debug post
	private void ThemeParkIncConstructorTail( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncGuestSettings settings )
	{
		tpi.SpriteIndex = unchecked( (int)guest.KidSetDraw + 4 * tpi.Generation );
		var (x, y) = guest.Navigation.Cell;
		// [EXT:untyped-harness-world] a harness world does not type its cells: an empty cell with no walkable cell there is no park cell, as for the Theme Park World arrival (SpawnArrival)
		var onParkCell = OnParkCellTypeAt( x, y ) && (!UntypedHarnessWorld || Grid.Cells.RawTypeAt( x, y ) != 0 || Grid.IsWalkable( x, y ));
		if ( onParkCell )
		{
			guest.VisitorNumber = ++VisitorCount;
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
			return;
		}
		random.NextWord();
		random.NextWord();
		var count = settings.NumTicketBooths;
		var booth = count > 0 ? (int)((uint)(ushort)guest.Id % (uint)count) : 0;
		for ( var tried = 0; tried < count; tried++ )
		{
			if ( RouteToTpiCellWord( guest, settings.BoothCell( booth ) ) )
				break;
			booth ^= 1;
		}
		tpi.Wait = 0;
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WalkingToBooth );
		Beamed?.Invoke( guest );
	}

	// ---- The setter -----------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// The state setter <c>0x61A540</c>: the word is stored first, then the entry of the table <c>0x61AA54</c> runs (the animation mode, a draw for state 3's wait, the booth's heading, the
	/// spot animation's stamp, the leaving and first-aid counters, the stay of a paid guest at the pick-up). The projection (<see cref="ThemeParkIncGuestStates.Projection"/>) follows the word.
	/// </summary>
	// [BIN:TPI-EXE:0x61A540 setter] +0x2C0 = state (0x61A550) before the bound test; a word above 0x1E returns with no entry (0x61AA49)
	internal void SetThemeParkIncState( Guest guest, ThemeParkIncGuestRecord tpi, int state )
	{
		tpi.State = state;
		EnterGuestState( guest, ThemeParkIncGuestStates.Projection( state ) );
		if ( (uint)state >= ThemeParkIncGuestStates.Count )
			return;
		if ( ThemeParkIncGuestStates.EntryMode( state ) is { } mode )
			tpi.Mode = mode;
		switch ( state )
		{
			case ThemeParkIncGuestStates.WaitingForPark:
				// [BIN:TPI-EXE:0x61A5D2 state 3 entry] mode 3, then +0x228 = R % 150 + 200 (unsigned div)
				tpi.Wait = (int)(random.NextWord() % 150) + 200;
				break;
			case ThemeParkIncGuestStates.PayingAtBooth:
				EnterPayingAtBooth( guest, tpi );
				break;
			case ThemeParkIncGuestStates.SpotAnimation:
				// [BIN:TPI-EXE:0x61A9AC state 8 entry] +0x230 = the world turn; no mode
				tpi.SpotTurn = TpiTurn;
				break;
			case ThemeParkIncGuestStates.StandingInQueue:
				// [BIN:TPI-EXE:0x61A606 state 14 entry] mode 3, +0x224 = the turn, +0x21C = _ftol(byte +0x21B × 1.2f) (the float 0x93E6F8 at 24-bit x87 precision, single-rounded, then truncated)
				tpi.Word224 = unchecked( (int)TpiTurn );
				tpi.Word21C = (int)(tpi.Byte21B * 1.2f);
				break;
			case ThemeParkIncGuestStates.TakingQueueStep:
				// [BIN:TPI-EXE:0x61A64D state 15 entry] 0x60B3F0(0) (the speed term 0), then mode 1
				guest.ToiletBonus = 0;
				break;
			case ThemeParkIncGuestStates.LeavingPark:
				// [BIN:TPI-EXE:0x61A952 state 22 entry] +0x204 = 0 (a word), mode 1
				tpi.Target = 0;
				break;
			case ThemeParkIncGuestStates.WaitingForFirstAid:
				// [BIN:TPI-EXE:0x61AA35 state 27 entry] mode 3, +0x2B4 = 100
				tpi.FirstAidWait = 100;
				break;
			case ThemeParkIncGuestStates.WaitingToBePickedUp:
				// [BIN:TPI-EXE:0x61A96F state 30 entry] a paid guest's stay, (turn − +0x1FC) >> 2 (0x6118A0) times 4, goes into the analyser's stay ring as days (0x64EAC0 -> 0x5AF080, the clock 0x608A90
				// over a day of 0xC92A69C000); then mode 3
				if ( tpi.Paid )
					Payments?.StayEnded( ParkStatisticsHistory.StayDays( (long)(unchecked( TpiTurn - (uint)tpi.ArrivalTurn ) >> 2 << 2) ) );
				break;
			case ThemeParkIncGuestStates.Wandering:
				// [BIN:TPI-EXE:0x61A9C7 state 7 entry] +0xE8 nonzero: 0x60B8E0(+0x28, +0x2A), the walk to the target words again (its result unread); then mode 1
				if ( tpi.Wandered )
					RouteToThemeParkIncTarget( guest );
				break;
			case ThemeParkIncGuestStates.WaitingToBeAdmitted or ThemeParkIncGuestStates.BeingUnloaded
				or ThemeParkIncGuestStates.UnderObjectControl or ThemeParkIncGuestStates.UnderGuardControl or ThemeParkIncGuestStates.ListeningToRumours:
				// [EXT:tpi-capability-gates] the entries of 17 (0x61A66A, the ride's roll 0x5E7A00), 19 and 20 (0x61A69D, 0x61A77E, the ride), 21 (0x61A906, the guard) and 26 (0x61A9F6, 0x60B6E0) read
				// objects the later phases port; no state of phases P1 and P2 enters them
				throw new NotSupportedException( $"Theme Park Inc guest state {state} ({ThemeParkIncGuestStates.Names[state]}) is not ported yet (docs/reverse/TPI-GUESTS.md)." );
		}
	}

	/// <summary>State 4's entry (<c>0x61A563</c>): at the cell of a ticket booth the guest takes the booth's angle as its heading and stands (mode 3); elsewhere nothing.</summary>
	// [BIN:TPI-EXE:0x61A563 state 4 entry] for i below NumTicketBooths (signed): the guest's cell word against 0x5D79B0(i); a match sets +0x2C = [0xA02618 + 12i] and mode 3; no match changes nothing
	private void EnterPayingAtBooth( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		var settings = NativeTpi!;
		var here = TpiCellWord( guest );
		for ( var booth = 0; booth < settings.NumTicketBooths; booth++ )
		{
			if ( settings.BoothCell( booth ) != here )
				continue;
			tpi.Heading = booth < settings.TicketBooths.Count ? settings.TicketBooths[booth].Angle : 0;
			tpi.Mode = 3;
			return;
		}
	}

	// ---- The guest's update --------------------------------------------------------------------------------------------------------

	/// <summary>
	/// One native guest in the thing sweep (the kind switch <c>0x6375E0</c>, kind 1): the pre-update <c>0x619B50</c> and then the dispatch <c>0x619FF0</c>.
	/// </summary>
	// [BIN:TPI-EXE:0x6375FA guest tick] 0x6375FC calls 0x619B50, 0x637603 calls 0x619FF0, every turn for every guest
	private void UpdateThemeParkIncGuest( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		// [BIN:TPI-EXE:0x60BC90 speed average] (+0xD4 + +0xD6 + +0xD8) / [0x9CFB2C] (100) + 3 × +0xDC, times 0.25, the caps 0x63EF90 and the boost decay (× 99 / 100, 0x60BD0A): Theme Park World's 0x004FA870 (GuestSpeed.Update)
		UpdateSpeed( guest );
		(guest.StepFromX, guest.StepFromY, guest.StepFromDistance, guest.StepTick) = (guest.X, guest.Y, guest.DistanceWalked, TickCount);
		if ( bodySpriteHostRunning )
			guest.BodySprite.ApplyPending();
		ThemeParkIncPreUpdate( guest, tpi );
		ThemeParkIncDispatch( guest, tpi );
	}

	/// <summary>
	/// The pre-update <c>0x619B50</c>. On the guest's phase (<c>(id &amp; 3) == (turn &amp; 3)</c>): the weather rule, the exit counter, the cell's environment words, the growth every 16 turns, the full
	/// needs, the toilet's happiness and speed term, the thought draw, the balloon countdown and the thought object; then, for every guest, the queue words outside the queue states and the
	/// temporary history every 20 turns.
	/// </summary>
	// [BIN:TPI-EXE:0x619B50 pre-update] 0x60BC90 first; the phase test at 0x619B89 (jne 0x619EC3) skips everything up to the thought object update 0x6388F0 at 0x619EBE
	private void ThemeParkIncPreUpdate( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		var settings = NativeTpi!;
		var turn = TpiTurn;
		if ( NeedsUpdate.IsPhase( turn, guest.Id ) )
		{
			ThemeParkIncWeather( guest, tpi, settings, turn );
			// [BIN:TPI-EXE:0x619BA3 exit counter] +0x1E0 -= 1 (a dword)
			guest.ExitLevel = unchecked( guest.ExitLevel - 1 );
			// [BIN:TPI-EXE:0x619BC7 cell words] 0x5D7420(cell word) is the region grid's record (0x1C1104 + 10 × (word − 1)): the signed words +0, +2 and +4 are added to happiness, illness and hunger, each clamped
			var (cx, cy) = guest.Navigation.Cell;
			var cell = Litter?.Environment.At( cx, cy ) ?? default;
			guest.Happiness = MeterMath.Add( guest.Happiness, cell.Happiness );
			guest.Nausea = MeterMath.Add( guest.Nausea, cell.Illness );
			guest.Hunger = MeterMath.Add( guest.Hunger, cell.Hunger );
			// [BIN:TPI-EXE:0x619CA0 growth] a turn with its low four bits clear: toilet +1, hunger +2, thirst +2 (floats 0x93A034, 0x93AD1C), each clamped
			if ( (turn & 0xF) == 0 )
			{
				guest.Toilet = MeterMath.Add( guest.Toilet, 1f );
				guest.Hunger = MeterMath.Add( guest.Hunger, 2f );
				guest.Thirst = MeterMath.Add( guest.Thirst, 2f );
			}
			// [BIN:TPI-EXE:0x619D6D full needs] illness, hunger and thirst whose truncated byte is 100 each cost 1 happiness through 0x60D0D0 (the clamped adder)
			if ( MeterMath.Byte( guest.Nausea ) == 100 )
				guest.Happiness = MeterMath.Add( guest.Happiness, -1f );
			if ( MeterMath.Byte( guest.Hunger ) == 100 )
				guest.Happiness = MeterMath.Add( guest.Happiness, -1f );
			if ( MeterMath.Byte( guest.Thirst ) == 100 )
				guest.Happiness = MeterMath.Add( guest.Happiness, -1f );
			// [BIN:TPI-EXE:0x619DBE toilet] a toilet byte above 80 costs 1 happiness and makes the speed term index 1; at 100 it costs 1 more, at 95 the thought object takes thought 4 (0x638960(4, 0));
			// a byte at most 80 makes the index 0; 0x60B3F0 then stores the word table 0x9CFB20[index] (0 or 25), 0 while an umbrella is open or pending
			var toilet = MeterMath.Byte( guest.Toilet );
			var term = 0;
			if ( toilet > 80 )
			{
				guest.Happiness = MeterMath.Add( guest.Happiness, -1f );
				if ( MeterMath.Byte( guest.Toilet ) == 100 )
					guest.Happiness = MeterMath.Add( guest.Happiness, -1f );
				else if ( MeterMath.Byte( guest.Toilet ) == 95 )
					// [EXT:tpi-twp-guest-rules] the thought object 0x638960 is drawn by Theme Park World's bubble rules (Think: tp.exe 0x0050BE80); Theme Park Inc's own bubble timing is not ported
					Think( guest, GuestThought.NeedToilet );
				term = 1;
			}
			guest.ToiletBonus = UmbrellaRules.SpeedTerm( guest.UmbrellaState, SpeedTermWord( term ) );
			// [BIN:TPI-EXE:0x619E63 thought draw] outside states 0x14 and 0x15 and on a park cell (0x60BE00): one world draw, R % 10 == 0 runs the pick 0x60FC60; then the balloon countdown 0x619E9E:
			// +0x298 above 0 (unsigned) loses 1 and at 0 the balloon goes (0x613D00(0)). Either test failing jumps to 0x619EBB, past the countdown: a guest on an object, held or outside the park keeps its balloon's time
			if ( tpi.State is not (ThemeParkIncGuestStates.UnderObjectControl or ThemeParkIncGuestStates.UnderGuardControl) && OnParkCellTypeAt( cx, cy ) )
			{
				if ( random.NextWord() % 10 == 0 )
					ThemeParkIncThoughtPick( guest, settings );
				if ( tpi.BalloonCountdown > 0 && --tpi.BalloonCountdown == 0 )
					ReleaseThemeParkIncBalloon( guest, tpi, settings );
			}
			// [BIN:TPI-EXE:0x6388F0 thought object] a bubble older than its stamp + 12 goes (thought 21 holds 120 turns; nothing of phase P1 shows 21)
			if ( guest.BubbleThought != GuestThought.None && (ulong)(guest.BubbleTurn + GuestThoughts.BubbleTurns) < turn )
				guest.BubbleThought = GuestThought.None;
		}
		// [BIN:TPI-EXE:0x619EC3 queue words] the state (the saved state during a spot animation) outside 14..17 clears the words +0x2CC and +0x2CE, on every turn
		if ( !ThemeParkIncGuestStates.InQueueOrLoading( tpi.State, tpi.Saved ) )
		{
			tpi.QueueLink = 0;
			tpi.QueueSlot = 0;
		}
		// [BIN:TPI-EXE:0x611D60 history] a turn that is a multiple of 20 shifts a 0 into the four words +0x212..+0x218 (0x611D20)
		if ( turn % NeedsUpdate.HistoryPeriod == 0 )
			ShiftThemeParkIncHistory( guest.TemporaryHistory );
	}

	/// <summary>
	/// The history shift <c>0x611D20</c>, bug-compatible: the dword copy of the first two words to the second and third runs before the last word is read, so the fourth slot takes the old second
	/// word again and the old third word is lost (<c>[a, b, c, d]</c> becomes <c>[0, a, b, b]</c>). Theme Park World's shift (<see cref="NeedsUpdate.DecayHistory"/>) keeps <c>c</c>.
	/// </summary>
	// [BIN:TPI-EXE:0x611D20 history shift] [+0x214] = dword [+0x212] (the words a, b); word [+0x218] = word [+0x216], which the dword store has just made b; word [+0x212] = the argument 0
	internal static void ShiftThemeParkIncHistory( int[] history )
	{
		history[2] = history[1];
		history[1] = history[0];
		history[3] = history[2];
		history[0] = 0;
	}

	/// <summary>The speed term word of an index (<c>0x9CFB20</c>: 0, 25, 50).</summary>
	// [BIN:TPI-EXE:0x9CFB20 speed term words] the halfwords 0x0000, 0x0019, 0x0032
	private static int SpeedTermWord( int index ) => index switch { 1 => GuestSpeed.ToiletBonus, 2 => BusWaitingSpeedTerm, _ => 0 };

	/// <summary>
	/// The thought pick <c>0x60FC60</c>: the first need over its <c>ThoughtBubble*</c> threshold, the mess, then happiness, else none; thought 4 is voiced for one thing id in sixteen.
	/// </summary>
	// [BIN:TPI-EXE:0x60FC60 thought pick] hunger and thirst bytes both above 90: 3; else toilet above NeedTheLoo (0xA01B74): 4; hunger above Hungry (0xA01B6C): 1; thirst above Thirsty (0xA01B70): 2;
	// illness above Sick (0xA01B78): 14; the block's littered share above Mess (the float 0xA01B7C): 7; happiness above Happy (0xA01B80): 9; below Unhappy (0xA01B84): 11; else nothing (each byte
	// against the integer through fild, an exact compare); the tail voices 4 (0x85, the table 0x60FE74) when the id's low nibble is 0, then 0x638960(thought, 0)
	private void ThemeParkIncThoughtPick( Guest guest, ThemeParkIncGuestSettings settings )
	{
		var hunger = MeterMath.Byte( guest.Hunger );
		var thirst = MeterMath.Byte( guest.Thirst );
		GuestThought? thought;
		if ( hunger > 90 && thirst > 90 )
			thought = GuestThought.HungryAndThirsty;
		else if ( MeterMath.Byte( guest.Toilet ) > settings.ThoughtBubbleNeedTheLoo )
			thought = GuestThought.NeedToilet;
		else if ( hunger > settings.ThoughtBubbleHungry )
			thought = GuestThought.Hungry;
		else if ( thirst > settings.ThoughtBubbleThirsty )
			thought = GuestThought.Thirsty;
		else if ( MeterMath.Byte( guest.Nausea ) > settings.ThoughtBubbleSick )
			thought = GuestThought.FeelingSick;
		// [EXT:tpi-twp-guest-rules] the block's littered and path cells (0x64EAC0 -> 0x5AF880, 0x5B1690(4) over 0x5B15A0(4), the same census as tp.exe's 0x004C8BA0 over 0x004C8AE0) are read
		// through Theme Park World's analyser blocks (MessyShare); Theme Park Inc's block size (the analyser's +0x24) is not read
		else if ( MessyShare( guest ) > settings.ThoughtBubbleMess )
			thought = GuestThought.TooMessy;
		else
		{
			var happiness = MeterMath.Byte( guest.Happiness );
			thought = happiness > settings.ThoughtBubbleHappy ? GuestThought.Happy : happiness < settings.ThoughtBubbleUnhappy ? GuestThought.Unhappy : null;
		}
		if ( thought is not { } picked )
			return;
		if ( picked == GuestThought.NeedToilet && VoiceGate( guest ) )
			Voiced?.Invoke( guest, ToiletThoughtVoiceEvent );
		// [EXT:tpi-twp-guest-rules] the thought object 0x638960 is drawn by Theme Park World's bubble rules (Think: tp.exe 0x0050BE80)
		Think( guest, picked );
	}

	/// <summary>
	/// The balloon goes (<c>0x613D00</c> with 0): a held balloon object is released, the guest loses <c>MediumHappinessChange</c> (a byte) and the countdown is cleared, held or not.
	/// </summary>
	// [BIN:TPI-EXE:0x613D00 balloon release] +0x294 nonzero: 0x4E49A0 (the float-away) and +0x294 = 0; with the argument nonzero (not here) the litter 0x5D8C80 of [0x9D0614 + 4 × +0x2A0] drops;
	// then happiness += -byte [0xA01A7C] (clamped) and +0x298 = 0, whatever was held
	private static void ReleaseThemeParkIncBalloon( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncGuestSettings settings )
	{
		tpi.HoldsBalloon = false;
		guest.Happiness = MeterMath.Add( guest.Happiness, -(float)(settings.MediumHappinessChange & 0xFF) );
		tpi.BalloonCountdown = 0;
	}

	// ---- The weather rule ------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// The weather rule <c>0x611050</c>, the first step of the guest's phase update: on a day the guest's age makes a multiple of <c>WeatherModifierFrequency</c>, the generation's deltas of the
	/// weather (heat wave, rain, storm, snow) go to happiness, hunger and thirst, and the umbrella opens or closes; an open umbrella softens a happiness loss.
	/// </summary>
	// [BIN:TPI-EXE:0x611050 weather rule] age = ((turn − +0x1FC) >> 2) << 2 (unsigned), the clock's 100 ns over a day (0xC92A69C000), the quotient's remainder by [0xA01B60] (signed idiv) nonzero
	// returns at once; else +0x2E8 = the day, the weather word +0x70 of 0x64EB80 picks the column (table 0x611324, 1..4) of the rows 0xA01B88 (happiness), 0xA01BF0 (hunger) and 0xA01BBC (thirst)
	// at 16 × +0x2A4; weather 1 and any other take the fair step, 2..4 the bad step (table 0x611334); the deltas are added clamped (+0x1BC, +0x1CC, +0x1C8); three debug lines
	private void ThemeParkIncWeather( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncGuestSettings settings, uint turn )
	{
		var age = (long)(unchecked( turn - (uint)tpi.ArrivalTurn ) >> 2 << 2);
		if ( !UmbrellaRules.DayGateOpen( age, settings.WeatherModifierFrequency ) )
			return;
		tpi.WeatherDay = unchecked( (int)RainMoodRules.DayOf( age ) );
		var weather = ArrivalWeatherState?.Invoke() ?? UmbrellaRules.WeatherNone;
		var row = tpi.Generation;
		var known = weather is >= UmbrellaRules.WeatherHeatWave and <= UmbrellaRules.WeatherSnow && (uint)row < (uint)settings.HappinessWeather.Count;
		var happiness = known ? settings.HappinessWeather[row].Of( weather ) : 0f;
		var hunger = known ? settings.HungerWeather[row].Of( weather ) : 0f;
		var thirst = known ? settings.ThirstWeather[row].Of( weather ) : 0f;
		if ( UmbrellaRules.IsBadWeather( weather ) )
		{
			switch ( guest.UmbrellaState )
			{
				case UmbrellaRules.StateClosing:
					// [BIN:TPI-EXE:0x6111E3 cancel close] +0x238 = 4
					guest.UmbrellaState = UmbrellaRules.StateOpen;
					break;
				case UmbrellaRules.StateClosed:
					ScheduleThemeParkIncUmbrella( guest, tpi, settings, turn, UmbrellaRules.StateOpening );
					break;
				case UmbrellaRules.StateOpening:
					CarryOutThemeParkIncUmbrella( guest, tpi, settings, turn );
					break;
				case UmbrellaRules.StateOpen when happiness < 0f:
					// [BIN:TPI-EXE:0x6111BE umbrella modifier] state 4 with a negative happiness delta: += fild [0xA01B5C], stored as a single
					happiness = (float)(happiness + settings.UmbrellaModifier);
					break;
			}
		}
		else
		{
			switch ( guest.UmbrellaState )
			{
				case UmbrellaRules.StateClosing:
					CarryOutThemeParkIncUmbrella( guest, tpi, settings, turn );
					break;
				case UmbrellaRules.StateOpening:
					// [BIN:TPI-EXE:0x6111FA cancel open] +0x238 = 2
					guest.UmbrellaState = UmbrellaRules.StateClosed;
					break;
				case UmbrellaRules.StateOpen:
					ScheduleThemeParkIncUmbrella( guest, tpi, settings, turn, UmbrellaRules.StateClosing );
					break;
			}
		}
		guest.Happiness = MeterMath.Add( guest.Happiness, happiness );
		guest.Hunger = MeterMath.Add( guest.Hunger, hunger );
		guest.Thirst = MeterMath.Add( guest.Thirst, thirst );
	}

	/// <summary>
	/// An umbrella change is scheduled (<c>0x613EA0</c>): not inside an attraction and not on a track cell; the delay is a draw modulo <c>SlowestWeatherChangeResponse</c>, stretched by a held
	/// balloon, and the change falls due that many guest updates later.
	/// </summary>
	// [BIN:TPI-EXE:0x613F18 reaction delay] rand() % [0xA01B64] (signed idiv; rand is 0..0x7FFF), then the balloon stretch, +0x23C = delay + (turn >> 2), +0x238 = the next state
	// [EXT:tpi-staff-crt-rand] user-approved 2026-11-22 (widened from the staff's two sites to the guests' 0x613F18, 0x615318, 0x615328 and 0x61795E): the CRT rand() (0x870D38, seeded from the clock by
	// srand(GetTickCount()) at 0x6A151B) is drawn from the park's world generator, rand() % N as world.Next(N)
	private void ScheduleThemeParkIncUmbrella( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncGuestSettings settings, uint turn, int next )
	{
		if ( IsInsideAttraction( guest ) || StandsOnTrackCell( guest ) )
			return;
		var delay = random.Next( settings.SlowestWeatherChangeResponse );
		delay = UmbrellaRules.StretchDelay( delay, tpi.HoldsBalloon, unchecked( (int)tpi.BalloonCountdown ), settings.DiscardBalloonPctModifier );
		guest.UmbrellaDueTurn = UmbrellaRules.DueTurn( turn, delay );
		guest.UmbrellaState = next;
	}

	/// <summary>
	/// A due umbrella change is carried out (<c>0x614010</c>): a pending close closes; otherwise a held balloon goes and the umbrella opens, its speed term index 0.
	/// </summary>
	// [BIN:TPI-EXE:0x614010 carry out] not inside, not on a track cell, (turn >> 2) not below +0x23C; state 1 with the sprite +0x240: state 2 and the sprite destroyed; otherwise +0x294 set: the
	// float-away and the balloon's litter, then the happiness step; +0x298 above 0 alone: the step; then a nonzero state without a sprite: 0x60B3F0(0), the sprite made, state 4
	private void CarryOutThemeParkIncUmbrella( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncGuestSettings settings, uint turn )
	{
		if ( IsInsideAttraction( guest ) || StandsOnTrackCell( guest ) || !UmbrellaRules.IsDue( turn, guest.UmbrellaDueTurn ) )
			return;
		if ( guest.UmbrellaState == UmbrellaRules.StateClosing )
		{
			guest.UmbrellaState = UmbrellaRules.StateClosed;
			return;
		}
		if ( tpi.HoldsBalloon || tpi.BalloonCountdown > 0 )
		{
			// [EXT:tpi-capability-gates] the balloon's litter (0x5D8C80 of [0x9D0614 + 4 × +0x2A0]: the types 9, 0xC, 0xB, 0xA by the balloon's colour) is not dropped: ParkLitter holds the types 1..8, and no
			// native guest holds a balloon before phase P3
			tpi.HoldsBalloon = false;
			guest.Happiness = MeterMath.Add( guest.Happiness, -(float)(settings.MediumHappinessChange & 0xFF) );
			tpi.BalloonCountdown = 0;
		}
		if ( guest.UmbrellaState != UmbrellaRules.StateNone && !UmbrellaRules.HasSprite( guest.UmbrellaState ) )
		{
			guest.ToiletBonus = 0;
			guest.UmbrellaState = UmbrellaRules.StateOpen;
		}
	}

	// ---- The dispatch ----------------------------------------------------------------------------------------------------------------

	/// <summary>The dispatch <c>0x619FF0</c>: the handler of the state word (table <c>0x61A3A8</c>); a word above 30 does nothing.</summary>
	// [BIN:TPI-EXE:0x619FF0 dispatch] ja 0x61A3A0 for a word above 0x1E, else jmp [0x61A3A8 + 4 × state]
	private void ThemeParkIncDispatch( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		switch ( tpi.State )
		{
			case ThemeParkIncGuestStates.WalkingToCrossing:
				UpdateTpiWalkingToCrossing( guest, tpi );
				break;
			case ThemeParkIncGuestStates.WaitingToCross:
				UpdateTpiWaitingToCross( guest, tpi );
				break;
			case ThemeParkIncGuestStates.WalkingToBooth:
				UpdateTpiWalkingToBooth( guest, tpi );
				break;
			case ThemeParkIncGuestStates.WaitingForPark:
				UpdateTpiWaitingForPark( guest, tpi );
				break;
			case ThemeParkIncGuestStates.PayingAtBooth:
				UpdateTpiPayingAtBooth( guest, tpi );
				break;
			case ThemeParkIncGuestStates.WalkingToEntrance:
				UpdateTpiWalkingToEntrance( guest, tpi );
				break;
			case ThemeParkIncGuestStates.Idle:
				UpdateTpiIdle( guest, tpi );
				break;
			case ThemeParkIncGuestStates.Wandering:
				UpdateTpiWandering( guest, tpi );
				break;
			case ThemeParkIncGuestStates.SpotAnimation:
				// [BIN:TPI-EXE:0x61A382 state 8] the turn past +0x230 + 10 (unsigned) restores the saved state (0x60FC40 = setter(+0x2C4))
				if ( TpiTurn > unchecked( (uint)tpi.SpotTurn + 10 ) )
					SetThemeParkIncState( guest, tpi, tpi.Saved );
				break;
			case ThemeParkIncGuestStates.WalkingToBin:
				UpdateTpiWalkingToBin( guest, tpi );
				break;
			case ThemeParkIncGuestStates.LeavingPark:
				UpdateTpiLeavingPark( guest, tpi );
				break;
			case ThemeParkIncGuestStates.Frozen:
				// [BIN:TPI-EXE:0x61A307 state 23] setter(6): a frozen guest is idle on its next update; the saved state is not restored
				SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
				break;
			case ThemeParkIncGuestStates.WaitingForFirstAid:
				// [BIN:TPI-EXE:0x614830 state 27] old = +0x2B4, +0x2B4 = old − 1, old below 0 (signed) sets 6: 101 waits, the 102nd update leaves
				if ( tpi.FirstAidWait-- < 0 )
					SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
				break;
			case ThemeParkIncGuestStates.WaitingToCrossRoad or ThemeParkIncGuestStates.CrossingRoadToLeave:
				// [BIN:TPI-EXE:0x61A3A0 states 28 and 29] the table sends both to the return; no code writes either state (unreachable; guest-states-d.md §2)
				break;
			case ThemeParkIncGuestStates.WaitingToBePickedUp:
				UpdateTpiWaitingToBePickedUp( guest, tpi );
				break;
			case >= 0 and < ThemeParkIncGuestStates.Count:
				// [EXT:tpi-capability-gates] states 10..21 and 24..26 are the later phases' (P3..P6); nothing of phases P1 and P2 enters them
				throw new NotSupportedException( $"Theme Park Inc guest state {tpi.State} ({ThemeParkIncGuestStates.Names[tpi.State]}) is not ported yet (docs/reverse/TPI-GUESTS.md)." );
		}
	}

	/// <summary>The walk step <c>0x60B440</c>: Theme Park World's (<c>[EXT:tpi-twp-guest-rules]</c>, tp.exe <c>0x004FA2A0</c>): 0 arrived, 1 walking, 2 stuck.</summary>
	private WalkResult TpiWalk( Guest guest ) => MoveAlongStep( guest );

	/// <summary>The cell word of the guest's own cell (the thing's bytes <c>+5</c>, <c>+7</c>).</summary>
	private static ushort TpiCellWord( Guest guest )
	{
		var (x, y) = guest.Navigation.Cell;
		return ThemeParkIncGuestSettings.CellWord( x, y );
	}

	/// <summary>The route request <c>0x60B7F0</c> to a cell word's centre: Theme Park World's <c>0x004FA530</c> (<see cref="RequestRoute"/>, with the stranded gate).</summary>
	// [BIN:TPI-EXE:0x60B7F0 route to a cell] x word ((word − 1) & 0x7F) << 8 | 0x80, y word ((word − 1) >> 7) << 8 | 0x80, then the common body of 0x60B8E0: byte for byte tp.exe's 0x004FA530 (guest-states-p0.md §5)
	private bool RouteToTpiCellWord( Guest guest, ushort word ) => RequestRoute( guest, ThemeParkIncGuestSettings.CellOf( word ), null );

	/// <summary>
	/// The route request <c>0x60B8E0</c> to a free sub-cell point: the stranded gate of <see cref="RequestRoute"/>, then the walk to (<paramref name="cell"/>, the two bytes).
	/// </summary>
	// [BIN:TPI-EXE:0x60B8E0 route to a point] the counter (0x5D81F0) below +0x1B0 clears the mark; a mark still held (0x60BB50) returns 0 and leaves the walk; else +0x28/+0x2A = the words, the mark
	// cleared and SetDest 0x63EE80 with the words << 8: tp.exe's 0x004FA5F0
	private bool RouteToTpiPoint( Guest guest, (int X, int Y) cell, int byteX, int byteY )
	{
		if ( guest.StrandedMark != 0 && Grid.Cells.StampCounter + 1 < guest.StrandedMark )
			guest.StrandedMark = 0;
		if ( guest.StrandedMark != 0 && StrandedHolds( guest, (guest.CellX, guest.CellY) ) )
			return false;
		guest.StrandedMark = 0;
		var (x, y) = GuestNavigation.BytePoint( cell, byteX, byteY );
		var found = guest.Navigation.Navigate( Grid.Cells, x, y );
		guest.HasWaypoint = true;
		return found;
	}

	/// <summary>
	/// The walk to the bus stop (<c>0x615D50</c>): two world draws for a point anywhere in the BusStopA cell and the route request there; the callers retry once with the walk mask 1
	/// (<see cref="WalkToBusStopWithRetry"/>).
	/// </summary>
	// [BIN:TPI-EXE:0x615D50 walk to the bus stop] c0 = 0x5D79F0(0) (BusStopA); c0 + 1, c0 − 0x100 and c0 − 0xFF are computed and unused; R1 (0x615DF5) and R2 (0x615E02) low bytes are the x and y bytes;
	// 0x60B8E0 with ((c0 − 1) & 0x7F) << 8 | R1b and ((c0 − 1) >> 7) << 8 | R2b; returns its result as a byte
	private bool WalkToBusStop( Guest guest, ThemeParkIncGuestSettings settings )
	{
		var byteX = (int)(random.NextWord() & 0xFF);
		var byteY = (int)(random.NextWord() & 0xFF);
		return RouteToTpiPoint( guest, ThemeParkIncGuestSettings.CellOf( ThemeParkIncGuestSettings.CellWord( settings.BusStopA.X, settings.BusStopA.Y ) ), byteX, byteY );
	}

	/// <summary>Every caller's pattern around <see cref="WalkToBusStop"/>: a refused first request sets the walk mask <c>+0x1A0</c> to 1 (empty cells allowed) and asks again, two more draws.</summary>
	// [BIN:TPI-EXE:0x614C58 walk mask retry] call; on 0: +0x1A0 = 1; call again (also 0x615C92, 0x615EC7, 0x615F44, 0x6186CD); the second result is not read
	private void WalkToBusStopWithRetry( Guest guest, ThemeParkIncGuestSettings settings )
	{
		if ( WalkToBusStop( guest, settings ) )
			return;
		guest.Navigation.Mask = 1;
		WalkToBusStop( guest, settings );
	}

	/// <summary>The happiness changers <c>0x6143A0</c> (gain) and <c>0x614460</c> (loss): kind 0, 1 or 2 takes the small, medium or big change as a byte; any other kind 0.</summary>
	// [BIN:TPI-EXE:0x614460 happiness loss] byte [0xA01A78 + 4 × kind] for kinds 0..2 (0 otherwise), fild, fchs, fadds +0x1BC, clamped; 0x6143A0 the same without fchs
	private static float HappinessChange( ThemeParkIncGuestSettings settings, int kind ) => kind switch
	{
		0 => settings.SmallHappinessChange & 0xFF,
		1 => settings.MediumHappinessChange & 0xFF,
		2 => settings.BigHappinessChange & 0xFF,
		_ => 0
	};

	// ---- The arrival states -------------------------------------------------------------------------------------------------------

	/// <summary>State 0 (<c>0x61A05C</c>, unreachable: no code writes 0): the walk; arrived, the heading 0x400 and state 1.</summary>
	// [BIN:TPI-EXE:0x61A05C state 0] 0x60B440 nonzero returns; else +0x2C = 0x400, setter(1)
	private void UpdateTpiWalkingToCrossing( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		if ( TpiWalk( guest ) != WalkResult.Arrived )
			return;
		tpi.Heading = 0x400;
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WaitingToCross );
	}

	/// <summary>
	/// State 1 (<c>0x61A082</c>, unreachable: its writers are state 0 and the dead <c>0x6156E0</c>): once the bus lets guests cross (<see cref="TpiCrossingAllowed"/>), a booth from a draw, a point in
	/// its cell from two more, the route there and state 2.
	/// </summary>
	// [BIN:TPI-EXE:0x61A082 state 1] 0x6501D0 zero returns; R1 % [0xA025B4] (unsigned) picks the booth 0x5D79B0, R2 and R3 low bytes the point, 0x60B8E0 (result unread), setter(2)
	private void UpdateTpiWaitingToCross( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		if ( !TpiCrossingAllowed() )
			return;
		var settings = NativeTpi!;
		var draw = random.NextWord();
		var booth = settings.NumTicketBooths != 0 ? (int)(draw % unchecked( (uint)settings.NumTicketBooths )) : 0;
		var byteX = (int)(random.NextWord() & 0xFF);
		var byteY = (int)(random.NextWord() & 0xFF);
		RouteToTpiPoint( guest, ThemeParkIncGuestSettings.CellOf( settings.BoothCell( booth ) ), byteX, byteY );
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WalkingToBooth );
	}

	/// <summary>
	/// Whether the bus lets a guest cross (<c>0x6501D0</c>): yes with no vehicle or one that is not the bus; with the bus no at status 3, yes while the group being dropped off holds 10 or more,
	/// else no at status 1 or 2.
	/// </summary>
	// [BIN:TPI-EXE:0x6501D0 crossing gate] no vehicle at work or not the bus slot: 1; the bus's status (a 6 releases it): 3 returns 0; the controller's group (world +0x458, the arrival object +0xC) at
	// least 10 returns 1; then status 1 returns 0, status 2 returns 0, anything else 1 (each read releases a 6 and then returns 1)
	private bool TpiCrossingAllowed()
	{
		if ( ArrivalVehicles == null || arrivalVehicle != ArrivalVehicleKind.Bus )
			return true;
		if ( ReadArrivalVehicle() == 3 )
			return false;
		if ( pendingArrivals >= 10 )
			return true;
		if ( ReadArrivalVehicle() == 1 )
			return false;
		return ReadArrivalVehicle() != 2;
	}

	/// <summary>
	/// State 2 (<c>0x615A80</c>): the speed term (50 while the bus waits at status 3 with no heli or mono at work, else 25 for one thing id in four), the walk; arrived, state 4 with the park open
	/// and 3 with it closed.
	/// </summary>
	// [BIN:TPI-EXE:0x615A80 state 2] 0x650530 zero and 0x650130 == 3: "The bus is coming! RUUUUUUUUUUN!!!!" and index 2; else index 1 when the id's low two bits are 0, else 0; 0x60B3F0(index);
	// 0x60B440 nonzero returns; else 0x64FA30 (the park closed) sets 3, open sets 4
	private void UpdateTpiWalkingToBooth( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		var index = !NonBusVehicleAtWork() && ReadArrivalVehicle() == BusWaitingStatus ? 2 : (guest.Id & 3) == 0 ? 1 : 0;
		guest.ToiletBonus = UmbrellaRules.SpeedTerm( guest.UmbrellaState, SpeedTermWord( index ) );
		if ( TpiWalk( guest ) != WalkResult.Arrived )
			return;
		SetThemeParkIncState( guest, tpi, Payments?.ParkClosed == true ? ThemeParkIncGuestStates.WaitingForPark : ThemeParkIncGuestStates.PayingAtBooth );
	}

	/// <summary>The gate's status as <c>0x64FA50</c> reads it: 1 open; the level's gate (<see cref="EntranceGateOpen"/>, its script variable 1) or open without one.</summary>
	// [BIN:TPI-EXE:0x64FA50 gate status] −1 without a gate thing (world +0x1EAB66), else the gate's VAR_STATUS (Gates.RSE: 1 open, 0 closed)
	private bool TpiGateOpen => EntranceGateOpen?.Invoke() != false;

	/// <summary>
	/// State 3 (<c>0x615B40</c>): with the park closed or the gate shut the wait counts down and at 0 the guest heads for the bus stop (state 22); with both open a paid guest at the head of its
	/// cell's list walks to an entrance (state 5) and an unpaid one goes back to its booth (state 2).
	/// </summary>
	// [BIN:TPI-EXE:0x615B40 state 3] 0x64FA30 nonzero or 0x64FA50 != 1 go to 0x615C86: +0x228 nonzero is decremented; zero walks to the bus stop (0x615D50 with the mask retry), setter(0x16) and
	// +0x1E0 = 0. Open: +0x22C set and the cell's chain head (the cell record's word −0x20) this guest: R & 1 picks the entrance (0x5D7A70), 0x60B7F0 there, setter(5); +0x22C set and not the head:
	// nothing; +0x22C clear: the booth scan 0x615BEB, then +0x228 = 0 and setter(2)
	private void UpdateTpiWaitingForPark( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		var settings = NativeTpi!;
		if ( Payments?.ParkClosed == true || !TpiGateOpen )
		{
			if ( tpi.Wait != 0 )
			{
				tpi.Wait--;
				return;
			}
			WalkToBusStopWithRetry( guest, settings );
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.LeavingPark );
			guest.ExitLevel = 0;
			return;
		}
		if ( tpi.Paid )
		{
			if ( ChainHead( guest.Navigation.Cell ).Guest != guest )
				return;
			var entrance = settings.EntranceCell( (int)(random.NextWord() & 1) );
			RouteToTpiCellWord( guest, ThemeParkIncGuestSettings.CellWord( entrance.X, entrance.Y ) );
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WalkingToEntrance );
			return;
		}
		RescanBooths( guest, settings );
		tpi.Wait = 0;
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WalkingToBooth );
	}

	/// <summary>
	/// State 3's booth scan (<c>0x615BEB</c>, bug-compatible): starting at booth <c>id % N</c> it compares the guest's cell with the booth's and, on a match, asks for the walk to that booth's
	/// centre; a mismatch flips bit 0 of the index, so only the start booth and its <c>^ 1</c> partner are ever compared.
	/// </summary>
	// [BIN:TPI-EXE:0x615BEB booth scan] ebx = id % [0xA025B4] (unsigned); for k below the count (signed): 0x5D79B0(ebx) against the cell word, a match: 0x60B7F0(that cell) and leave; else ebx ^= 1
	private void RescanBooths( Guest guest, ThemeParkIncGuestSettings settings )
	{
		var count = settings.NumTicketBooths;
		if ( count <= 0 )
			return;
		var booth = (int)((uint)(ushort)guest.Id % (uint)count);
		var here = TpiCellWord( guest );
		for ( var tried = 0; tried < count; tried++ )
		{
			if ( settings.BoothCell( booth ) == here )
			{
				RouteToTpiCellWord( guest, here );
				return;
			}
			booth ^= 1;
		}
	}

	/// <summary>
	/// State 4 (<c>0x615E90</c>): after the wait the booth's verdict (<see cref="BoothVerdict"/> over Theme Park Inc's settings): far too expensive, the guest heads for the bus stop; expensive, it waits
	/// (50 + R % 50), less happy, and leaves at a happiness byte of 0; cheap, it is happier; cheap and fair pay the fee (state 3).
	/// </summary>
	// [BIN:TPI-EXE:0x615E90 state 4] +0x228 nonzero is decremented; else 0x6157D0's verdict through the table 0x615FEC: 0 the bus stop walk, thought 6, setter(0x16), +0x1E0 = 0; 1 +0x228 = R % 50 + 50,
	// thought 6, 0x614460(1), a happiness byte nonzero returns, else as 0 without the thought; 2 0x6143A0(1), then 3: the fee 0x5C0E10 (the bank's takings), +0x22C = 1, +0x1C4 += the fee (park +0x120), setter(3)
	private void UpdateTpiPayingAtBooth( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		if ( tpi.Wait != 0 )
		{
			tpi.Wait--;
			return;
		}
		var settings = NativeTpi!;
		var verdict = ThemeParkIncBoothVerdict( settings );
		switch ( verdict )
		{
			case BoothVerdictKind.FarTooExpensive:
				WalkToBusStopWithRetry( guest, settings );
				Think( guest, GuestThought.Dissatisfied );
				break;
			case BoothVerdictKind.Expensive:
				tpi.Wait = (int)(random.NextWord() % 50) + 50;
				Think( guest, GuestThought.Dissatisfied );
				guest.Happiness = MeterMath.Add( guest.Happiness, -HappinessChange( settings, 1 ) );
				if ( MeterMath.Byte( guest.Happiness ) != 0 )
					return;
				WalkToBusStopWithRetry( guest, settings );
				break;
			default:
				if ( verdict == BoothVerdictKind.Cheap )
					guest.Happiness = MeterMath.Add( guest.Happiness, HappinessChange( settings, 1 ) );
				// [BIN:TPI-EXE:0x615FA7 fee] 0x5C0E10 books the park's fee (+0x120 of the park record) with no refusal (IGuestPayments.PayThemeParkIncAdmission); +0x1C4 adds the fee booked
				var fee = Payments?.PayThemeParkIncAdmission() ?? AdmissionFee;
				Admissions++;
				Observer?.Admitted( guest, fee );
				if ( fee > 0 )
				{
					Observer?.Spent( guest, fee, 0 );
					MoneySpent?.Invoke( guest, fee, 0 );
				}
				tpi.Paid = true;
				tpi.FeesPaid = unchecked( tpi.FeesPaid + fee );
				SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WaitingForPark );
				return;
		}
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.LeavingPark );
		guest.ExitLevel = 0;
	}

	/// <summary>
	/// The booth's verdict <c>0x6157D0</c>: Theme Park World's formula (<see cref="BoothVerdict"/>) over Theme Park Inc's words, with one world draw; every verdict but the early fair one records its
	/// opinion byte in the park's perception ring 0.
	/// </summary>
	// [BIN:TPI-EXE:0x6157D0 booth verdict] GP = S (0x5B05D0, the park's excitement worked out longhand) / (N + R % (N / 2 + 1)) + MinimumEntryFee (unsigned, N = [0xA01A90]), AF = park +0x120 (the fee);
	// GP and AF both at most the fee floor return 3 with no opinion; else AF against GP × Average (0xA01A9C), GP × Expensive (0xA01AA0) and GP × Cheap (0xA01A98) in x87 at 24 bits (fildll of the
	// words, exact below 2^24), recording the opinion bytes 0xA01AEC, 0xA01AF0, 0xA01AF4, 0xA01AF8 (0x5AEDE0) as Theme Park World's 0x004FF5B0 does
	// [BIN:TPI-EXE:0x5B05D0 park excitement] the sum over the object list (world +0x1EAB88) of the kinds 0 and 2 that pass the counts test 0x5DFC90 and have copies, with the age bracket from the
	// first purchase: the same sum as tp.exe's 0x004C8240, which the economy ports (ParkPoints, also read by Theme Park Inc's arrival group 0x5BCD60)
	private BoothVerdictKind ThemeParkIncBoothVerdict( ThemeParkIncGuestSettings settings )
	{
		var points = (uint)Math.Max( 0, ParkPoints?.Invoke() ?? 0 );
		var fee = (uint)Math.Max( 0, AdmissionFee );
		var draw = random.NextWord();
		var divisor = settings.ExcitementToCostDivisor;
		var value = unchecked( points / (draw % ((divisor >> 1) + 1) + divisor) + settings.MinimumEntryFee );
		var minimum = settings.MinimumEntryFee;
		if ( value <= minimum && fee <= minimum )
			return BoothVerdictKind.Fair;
		// fildll loads both words exactly; each fmuls rounds once to the 24-bit significand; fcompp compares the exact fee with that product
		double b = fee;
		BoothVerdictKind verdict;
		int opinion;
		if ( ThemeParkIncX87.Product( value, settings.AveragePriceMultiplier ) <= b )
			(verdict, opinion) = b >= ThemeParkIncX87.Product( value, settings.ExpensivePriceMultiplier ) ? (BoothVerdictKind.FarTooExpensive, settings.OpinionForMuchTooHigh) : (BoothVerdictKind.Expensive, settings.OpinionForTooHigh);
		else
			(verdict, opinion) = b <= ThemeParkIncX87.Product( value, settings.CheapPriceMultiplier ) ? (BoothVerdictKind.Cheap, settings.OpinionForTooLow) : (BoothVerdictKind.Fair, settings.OpinionForAboutRight);
		Payments?.PriceTested( BoothVerdict.OpinionCategory, opinion & 0xFF );
		return verdict;
	}

	/// <summary>
	/// State 5 (<c>0x616790</c>): with the park closed or the gate shut the guest looks for a booth (bug-compatible) and waits (state 3); otherwise the walk, and on arrival the park's visitor
	/// number and state 6.
	/// </summary>
	// [BIN:TPI-EXE:0x616790 state 5] 0x64FA30 nonzero or 0x64FA50 != 1: the block 0x6168D6, then setter(3); else 0x60B440 nonzero returns; arrived: +0x200 = 0x650560(), the first hour's tips, setter(6)
	// [EXT:tpi-capability-gates] the tutorial's two tips (advice 0x207 and 0x208 at the census New.ADVISORSTUTWELLDONE1NUMVIS and ADVISORSTUTVISITORINFONUMVIS within the first FirstHour.Minutes,
	// 0x650890) are not posted: Theme Park Inc's advisor plays only the weather forecasts (the advisor's other messages); they draw nothing
	private void UpdateTpiWalkingToEntrance( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		var settings = NativeTpi!;
		if ( Payments?.ParkClosed == true || !TpiGateOpen )
		{
			ReturnToBooth( guest, settings );
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WaitingForPark );
			return;
		}
		if ( TpiWalk( guest ) != WalkResult.Arrived )
			return;
		guest.VisitorNumber = ++VisitorCount;
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
	}

	/// <summary>
	/// State 5's closed block (<c>0x6168D6</c>, bug-compatible): for each booth k it compares the guest's cell with booth k's, and on a match asks for the walk to booth <c>i</c> (starting at
	/// <c>id % N</c>), stopping on success and flipping bit 0 of <c>i</c> on failure; a guest on no booth's cell asks for nothing.
	/// </summary>
	// [BIN:TPI-EXE:0x6168D6 closed block] i = id % [0xA025B4] (unsigned); for k below the count (signed): the cell word against 0x5D79B0(k); a match: 0x60B7F0(0x5D79B0(i)), nonzero leaves, else i ^= 1
	private void ReturnToBooth( Guest guest, ThemeParkIncGuestSettings settings )
	{
		var count = settings.NumTicketBooths;
		if ( count <= 0 )
			return;
		var booth = (int)((uint)(ushort)guest.Id % (uint)count);
		var here = TpiCellWord( guest );
		for ( var k = 0; k < count; k++ )
		{
			if ( settings.BoothCell( k ) != here )
				continue;
			if ( RouteToTpiCellWord( guest, settings.BoothCell( booth ) ) )
				return;
			booth ^= 1;
		}
	}

	// ---- Leaving -------------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// State 22 (<c>0x618620</c>): a guest who may stay after all (exit counter above 0, happiness byte nonzero, park open, on a park cell) goes back to state 6 ("Make up your mind!"); otherwise the
	/// walk to the bus stop: arrived, it faces the road (0x7FF for the heli or mono, else 0x400) and waits to be picked up (state 30); stuck, it asks for the bus stop again and stays.
	/// </summary>
	// [BIN:TPI-EXE:0x618620 state 22] +0x1E0 > 0 (signed), the happiness byte nonzero, 0x64FA30 zero and 0x60BE00: "Make up your mind!", +0x1A0 = 0, setter(6); else 0x60B440: 0 sets +0x2C (0x7FF when
	// 0x650530, else 0x400) and setter(0x1E); 2 logs "Got stuck walking around outside park" and walks to the bus stop with the mask retry; 1 nothing
	private void UpdateTpiLeavingPark( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		var (x, y) = guest.Navigation.Cell;
		if ( guest.ExitLevel > 0 && MeterMath.Byte( guest.Happiness ) != 0 && Payments?.ParkClosed != true && OnParkCellTypeAt( x, y ) )
		{
			guest.Navigation.Mask = 0;
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
			Observer?.Recalled( guest );
			return;
		}
		switch ( TpiWalk( guest ) )
		{
			case WalkResult.Arrived:
				tpi.Heading = NonBusVehicleAtWork() ? 0x7FF : 0x400;
				SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WaitingToBePickedUp );
				break;
			case WalkResult.Stuck:
				WalkToBusStopWithRetry( guest, NativeTpi! );
				break;
		}
	}

	/// <summary>
	/// State 30 (<c>0x618790</c>): at the vehicle's status 4 (boarding) the guest leaves the park (destroyed, with the beam); at status 1, 2 or 3 a guest off both stop cells (BusStopA and its +X
	/// neighbour) picks one with a draw and a point in it with two more and walks there again (state 22).
	/// </summary>
	// [BIN:TPI-EXE:0x618790 state 30] 0x650130 == 4: +0x1E4 nonzero marks the tag system's records (0x64EA60 -> 0x636B10, the tag tube's refresh), 0x60B700(0x13, 0, 0) and the destroy 0x637C40; 1, 2 or 3:
	// c0 = BusStopA, c1 = c0 + 1; the guest's cell on either returns; else R1 & 1 picks c1 or c0, R2 and R3 low bytes the point, 0x60B8E0 (unread), setter(0x16); any other status nothing
	// [EXT:tpi-capability-gates] the tag tube's mark of a prankster's records (0x636B10) is not made: the tag system reads prank reports, which phase P4 ports
	private void UpdateTpiWaitingToBePickedUp( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		var status = ReadArrivalVehicle();
		if ( status == 4 )
		{
			Beamed?.Invoke( guest );
			guest.State = GuestState.Gone;
			return;
		}
		if ( status is not (1 or 2 or 3) )
			return;
		var settings = NativeTpi!;
		var stop = ThemeParkIncGuestSettings.CellWord( settings.BusStopA.X, settings.BusStopA.Y );
		var next = unchecked( (ushort)(stop + 1) );
		var here = TpiCellWord( guest );
		if ( here == stop || here == next )
			return;
		var chosen = (random.NextWord() & 1) == 0 ? stop : next;
		var byteX = (int)(random.NextWord() & 0xFF);
		var byteY = (int)(random.NextWord() & 0xFF);
		RouteToTpiPoint( guest, ThemeParkIncGuestSettings.CellOf( chosen ), byteX, byteY );
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.LeavingPark );
	}
}
