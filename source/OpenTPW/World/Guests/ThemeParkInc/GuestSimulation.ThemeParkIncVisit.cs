namespace OpenTPW;

// Theme Park Inc's native guest states, phase P3b (docs/reverse/TPI-GUESTS.md): the visit of an attraction. Being loaded (state 16, 0x617F80), waiting for the script to admit the guest (17, 0x61A21D,
// its entry roll 0x5E7A00), under the object's control (20, 0x61A292, its entry 0x61A77E; the ride's force 0x6182A0), the exit (0x619980) with the use routine 0x6118D0, its payment step 0x6126D0
// and its outcome 0x612740, and being unloaded (19, 0x618380, its entry 0x61A69D). The ride's side is the bridge's (RideVisitorBridge: AdmitPerson 0x5E4420 is PresentForBoarding, the ride's
// dismiss of VAR_LETMEOFF calls the exit through IRideVisitorHost.OnVisitorExitRide).
public sealed partial class GuestSimulation
{
	/// <summary>The balloon sprite's colours (the slot count <c>+0x21E</c> of the bank <c>0x690370(0xE)</c> draws from).</summary>
	// [DATA:esprites.wad/Generic/Balloons/Spr_bl.esp] the four colour slots, as Theme Park World's SPR_BL (GuestSpriteAtlas.LoadBalloons); the litter table 0x9D0614 holds one type per colour (9, 0xC, 0xB, 0xA)
	public const int ThemeParkIncBalloonColours = 4;

	/// <summary>The info type word (<c>Info.RideTypeStringIndex</c>, type <c>+0xC0</c>) whose use reads its result back from the script's VAR_PARAM (<c>0x61196C</c>).</summary>
	public const int ThemeParkIncScriptedResultType = 0x17;

	/// <summary>The loaded costume banks' gender bytes (sprite category 6, <c>0x9D7000</c>'s "costumes"): the bank a costume shop draws (<c>0x6902F0(6)</c>), and the mood animation's gender for a costumed guest.</summary>
	internal bool[]? TpiCostumeBankFemale { get; set; }

	/// <summary>The words a native guest's use reads from the attraction thing: the economy's live words, else the bridge's (a rig without an economy).</summary>
	private ThemeParkIncThingWords TpiThingWords( RideVisitorBridge ride ) => Payments?.ThemeParkIncThing( ride ) ?? new ThemeParkIncThingWords(
		ride.HasSideshowTerms ? ride.Sideshow.PricePerUse : ride.Price, ride.HasSideshowTerms ? ride.Sideshow.CostOfGoods : ride.InitCostOfGoods, ride.QualityOfGoods,
		(100 - ride.Sideshow.ChanceOfLosingPercent) & 0xFF, ride.IngredientAmount );

	// ---- State 16 ----------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// State 16 (<c>0x617F80</c>), being loaded: the walk to the stand point; arrived (or stuck, which falls through), the price test, the weather reaction armed in the queue and the admission
	/// (AdmitPerson <c>0x5E4420</c>): admitted, state 17; refused, back to the guest's place (FindQueueDestination) or out of the queue.
	/// </summary>
	// [BIN:TPI-EXE:0x617F80 state 16] 0x60B440: 2 logs "Got stuck in middle of very short queue step" and goes on as 0 (0x617F9F), any other nonzero returns (0x6181F4); edi = the slot of +0x204;
	// 0x6120C0 nonzero: "too expensive", thought 6, 0x614460(1), 0x5E46A0, 0x5E0100, 0x619700(0), 0x5E5D70, setter(6)
	internal void UpdateTpiBeingLoaded( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		if ( TpiWalk( guest ) == WalkResult.Walking )
			return;
		if ( TpiAttraction( tpi.Target ) is not { } view )
		{
			// [EXT:original-undefined-reads] as in states 13 and 14: the removal message 0x60D120 clears the target before the thing goes; a target no hosted attraction holds is read as none
			ClearTpiQueueMirror( guest );
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
			return;
		}
		var ride = view.Ride;
		var settings = NativeTpi!;
		if ( TooExpensiveInTheme( guest, ride, tpi.PreferredExcitement & 0xFF ) )
		{
			Think( guest, GuestThought.Dissatisfied );
			guest.Happiness = MeterMath.Add( guest.Happiness, -HappinessChange( settings, 1 ) );
			NoteQueueEnd( guest, ride.AttractionId, QueueEndReason.Price );
			// [BIN:TPI-EXE:0x5E46A0 admitted guest let go] "person %d left after I'd admitted him": the loading slot +0x80 = 0 and VAR_LETMEON naming him = 0; with the list's 0x5E0100 that is the
			// bridge's LeaveQueue (its pending visitor withdrawn)
			TpiLeaveQueue( guest, tpi, ride );
			return;
		}
		// [BIN:TPI-EXE:0x618079 armed weather reaction] +0x28C == 1: a balloon (+0x294 nonzero, or +0x298 above 0) or a reaction due within 0.66 × SlowestWeatherChangeResponse quarter turns
		// (the unsigned 64-bit +0x290 − (turn >> 2) against fild [0xA01B64] × 0.66f at 24 bits, not below) leaves quietly (0x618151: 0x5E46A0, 0x5E0100, 0x619700(1), 0x5E5D70, setter(6)); else
		// +0x28C and +0x290 are cleared
		if ( tpi.Byte28C == 1 )
		{
			var remaining = (ulong)unchecked( (uint)(tpi.Word290 - (int)(TpiTurn >> 2)) );
			var limit = ThemeParkIncX87.Product( settings.SlowestWeatherChangeResponse, 0.66f );
			if ( tpi.HoldsBalloon || tpi.BalloonCountdown > 0 || !((double)limit < remaining) )
			{
				NoteQueueEnd( guest, ride.AttractionId, QueueEndReason.Price );
				TpiLeaveQueue( guest, tpi, ride, quietly: true );
				return;
			}
			tpi.Byte28C = 0;
			tpi.Word290 = 0;
		}
		// [BIN:TPI-EXE:0x6180FD admission] 0x5E4420 (AdmitPerson: the ride broken (+0xA0 1 or 4) or closed (+0x7C 0) refuses; the loading slot +0x80 cleared; VAR_LETMEON busy refuses, else it takes the
		// id) nonzero: "been AdmitPerson'd", setter(0x11)
		if ( ride.PresentForBoarding( guest.Id ) )
		{
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WaitingToBeAdmitted );
			return;
		}
		// [BIN:TPI-EXE:0x6181B1 rejoin] 0x619530 (state 15) or "Couldn't rejoin FOQ even!": 0x5E0100, 0x619700(0), setter(6)
		if ( TpiFindQueueDestination( guest, tpi, ride ) )
			return;
		NoteQueueEnd( guest, ride.AttractionId, QueueEndReason.NoPlace );
		TpiRemoveFromQueue( ride, guest );
		TpiDismissFromQueue( guest, tpi, quietly: false );
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
	}

	// ---- State 17 ----------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// State 17's entry (<c>0x61A66A</c>): the ride's chance roll <c>0x5E7A00</c>, one world draw: the result (the thing's win byte <c>+0x90</c> at least the draw modulo 100) goes into the script's
	/// VAR_PARAM and into <c>+0x21B</c>, which held the queue place until now.
	/// </summary>
	// [BIN:TPI-EXE:0x5E7A00 chance roll] the type lookup 0x43C700 (its result unread); R = 0x6493E0 (0x5E7A2D) % 100 (unsigned div); cmp byte +0x90, R%100 with sbb/inc: 1 when the byte is not below,
	// else 0; 0x6B0780(VAR 11, the result); returns the result, which 0x61A68D stores in the byte +0x21B
	private void EnterTpiWaitingToBeAdmitted( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		var draw = random.NextWord() % 100;
		// 16 sets 17 only after the target's admission (AdmitPerson), so its slot is filled
		if ( TpiAttraction( tpi.Target ) is not { } view )
			return;
		var won = (uint)(TpiThingWords( view.Ride ).WinByte & 0xFF) >= draw ? 1 : 0;
		view.Ride.Param = won;
		tpi.Byte21B = won;
	}

	/// <summary>
	/// State 17 (<c>0x61A21D</c>), waiting for the script to admit the guest: once the script has taken VAR_LETMEON (<c>0x5E4630</c>: it no longer names the queue's head), the guest leaves the list and the
	/// ride controls it (state 20).
	/// </summary>
	// [BIN:TPI-EXE:0x61A21D state 17] 0x4F9420(+0x204); 0x5E4630 (VAR 0 against the head +0x4C) zero: nothing (0x61A3A0); else 0x5E0100(id) and setter(0x14)
	internal void UpdateTpiWaitingToBeAdmitted( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		if ( TpiAttraction( tpi.Target ) is not { } view )
		{
			// [EXT:original-undefined-reads] the ride of a state-17 guest goes only through the removal message, which clears the target first; a target no hosted attraction holds is read as none
			ClearTpiQueueMirror( guest );
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
			return;
		}
		ForceTpiOntoRide( guest, tpi, view.Ride );
	}

	/// <summary>The script took the guest (<c>0x5E4630</c>): out of the list, state 20 (<c>0x6182A0</c>, also the ride's head step's force). False when the script has not taken it yet.</summary>
	// [BIN:TPI-EXE:0x6182A0 forced onto the ride] 0x5E4630 nonzero: 0x5E0100(id) and setter(0x14); 0x5E4630 is 0x6B0C90(VAR 0) != the head word +0x4C
	private bool ForceTpiOntoRide( Guest guest, ThemeParkIncGuestRecord tpi, RideVisitorBridge ride )
	{
		if ( ride.LetMeOn == ride.QueueHead )
			return false;
		TpiRemoveFromQueue( ride, guest );
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.UnderObjectControl );
		return true;
	}

	/// <summary>The ride's head step (<c>0x5E3CC0</c>) finds a native head in state 17 whose admission the script has taken: it forces it onto the ride rather than dismissing it.</summary>
	// [BIN:TPI-EXE:0x5E3CE9 forcing] VAR 0 not the head and the head's +0x2C0 == 0x11: "script admitted person %d but he doesn't know yet, forcing him onto ride", 0x6182A0
	bool IRideVisitorHost.ForcesAdmittedHead( IRideVisitorBridge ride, int headGuestId ) =>
		ride is RideVisitorBridge bridge && Find( headGuestId ) is { Tpi: { State: ThemeParkIncGuestStates.WaitingToBeAdmitted } tpi } guest
			&& bridge.LetMeOn != headGuestId && ForceTpiOntoRide( guest, tpi, bridge );

	/// <summary>A native guest keeps its list place when the script takes it: its own state 17 unlinks it.</summary>
	bool IRideVisitorHost.KeepsBoardedGuestQueued( int guestId ) => Find( guestId ) is { Tpi: not null };

	// ---- State 20 ----------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// State 20's entry (<c>0x61A77E</c>), the guest under the object's control: a teleporting ride's beam, the thought gone, a held balloon put away (an umbrella shop takes it), the umbrella closed, the
	/// body kept by a ride that draws it or taken down, and the challenge events of the admission.
	/// </summary>
	// [BIN:TPI-EXE:0x61A77E state 20 entry] type +0x1C0 (UsageInfo.RequiresTeleport): 0x60B700(0x13, 0, 0), the arrival beam's particle; 0x638D10 (the thought object cleared); +0x294 or +0x298: type
	// +0x21C 3 (an umbrella shop) 0x613D00(1), else 0x4E3E90(+0x294) and +0x294 = 0 (the countdown kept); +0x238 nonzero: 0x6142E0 (a drawn umbrella closes, state 2); +0x3C = 1; the thing's +0x42 bit
	// 0x20 (UsageInfo.RideHandlesSprite): +0x38 = 1 and mode 3, else 0x60AC00(0) (the sprite taken down, presentation); then the challenge events 0x11, 0x34, 0x3A and 0x39 (0x61A867..0x61A8F3)
	private void EnterTpiUnderObjectControl( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		if ( TpiAttraction( tpi.Target ) is not { } view )
			return;
		var ride = view.Ride;
		var use = view.Use;
		if ( use.RequiresTeleport )
			Beamed?.Invoke( guest );
		guest.BubbleThought = GuestThought.None;
		if ( tpi.HoldsBalloon || tpi.BalloonCountdown > 0 )
		{
			if ( view.Terms.AppearanceEffect == 3 )
				ReleaseThemeParkIncBalloon( guest, tpi, NativeTpi!, litter: true );
			else
				tpi.HoldsBalloon = false;
		}
		// [BIN:TPI-EXE:0x6142E0 umbrella put away] +0x240 (the sprite, held in states 4 and 1) destroyed: +0x238 = 2
		if ( guest.UmbrellaState != UmbrellaRules.StateNone && UmbrellaRules.HasSprite( guest.UmbrellaState ) )
			guest.UmbrellaState = UmbrellaRules.StateClosed;
		if ( use.RideHandlesSprite )
			tpi.Mode = 3;
		BoardBodySprite( guest, ride );
		guest.HasWaypoint = false;
		// [BIN:TPI-EXE:0x61A8A3 photo booth events] a type whose +0xCC (Info.IsPhotoBooth) is set posts 0x39 with the type of its host thing (+0x38, 0x5E4C90): OpenTPW places no add-on, so every admission
		// takes the other branch (IGuestPayments.ThemeParkIncRideAdmitted)
		Payments?.ThemeParkIncRideAdmitted( ride );
		if ( guest.QueueJoinTurn >= 0 )
		{
			guest.LastQueueWaitTurns = ParkTurn - guest.QueueJoinTurn;
			QueueWaitCompleted?.Invoke( guest, ride, guest.LastQueueWaitTurns );
		}
		Observer?.QueueBoarded( guest, ride, guest.QueueJoinTurn >= 0 ? ParkTurn - guest.QueueJoinTurn : -1 );
		ClearQueueState( guest );
		guest.AttractionId = ride.AttractionId;
	}

	/// <summary>State 20 (<c>0x61A292</c>): the guest is the ride's; its handler only looks the ride up.</summary>
	// [BIN:TPI-EXE:0x61A292 state 20] 0x4F9420(+0x204), its result unread
	internal static void UpdateTpiUnderObjectControl()
	{
	}

	// ---- The exit ----------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// The exit <c>0x619980</c>, from the ride's dismiss of VAR_LETMEOFF: the guest is put at the exit's appear point and asked to walk to the cell beyond it; a queue cell there or no route is a blocked
	/// exit (false, the ride closes); else the use routine and state 19. A guest with no target leaves at once (true) and is not moved.
	/// </summary>
	// [BIN:TPI-EXE:0x619980 exit] +0x204 0 returns 1; "ExitRide, leaving ride %d"; 0x5E1440(1) (the exit's appear point) and 0x60BD80 (the teleport); the exit cell +0x48's link (0x65CC40, reversed by
	// 0x5D8150 when it is the entrance +0x46) and its neighbour (0x5D92E0); a neighbour of type 3 or 9 (0x67F5D0) or no route (0x60B7F0): "SetDest on ride exit failed", 0x5E1620 (the close) and 0;
	// else 0x6118D0 (the use routine), setter(0x13) and 1. tp.exe's 0x005014E0 (ExitRide)
	private bool TpiExitRide( Guest guest, ThemeParkIncGuestRecord tpi, RideVisitorBridge ride )
	{
		if ( tpi.Target == 0 )
			return true;
		if ( !WalkOffRide( guest, ride ) )
			return false;
		if ( TpiAttraction( tpi.Target ) is { } view )
			ThemeParkIncUseRoutine( guest, tpi, view );
		ReleaseBodySprite( guest );
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.BeingUnloaded );
		return true;
	}

	/// <summary>
	/// OpenTPW's teardown lets go of a rider without the script's let-off (its script halted or faulted): a guest under the ride's control leaves by the exit, and where the exit is blocked it is set down
	/// beyond the exit and idle; one still waiting in state 17 is idle too. A demolished object's riders are the removal message's (<c>0x60D120</c>), which has already made them idle.
	/// </summary>
	private void ReleaseThemeParkIncRider( Guest guest, ThemeParkIncGuestRecord tpi, IRideVisitorBridge ride )
	{
		if ( ride is not RideVisitorBridge bridge || bridge.IsStopped && tpi.State != ThemeParkIncGuestStates.UnderObjectControl
			|| tpi.State is not (ThemeParkIncGuestStates.UnderObjectControl or ThemeParkIncGuestStates.WaitingToBeAdmitted) )
			return;
		if ( tpi.State == ThemeParkIncGuestStates.UnderObjectControl && !bridge.IsStopped && TpiExitRide( guest, tpi, bridge ) )
			return;
		if ( tpi.State == ThemeParkIncGuestStates.WaitingToBeAdmitted )
			TpiRemoveFromQueue( bridge, guest );
		ReturnToPath( guest, ride.ExitCell );
		RemovedRideBodySprite( guest );
		tpi.Target = 0;
		ClearTpiQueueMirror( guest );
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
	}

	// ---- The use routine ---------------------------------------------------------------------------------------------------------

	/// <summary>
	/// The use routine <c>0x6118D0</c>: the used history, the visit count by kind, the payment step, the customer, the fatigue, a scripted result read back, then the outcome (a successful use) or the lost
	/// use's happiness step.
	/// </summary>
	// [BIN:TPI-EXE:0x6118D0 use routine] 0x6118EC..0x611906 the history push; type +0x600 (Info.WhichUIType) 0, 1, 2 raise +0x1EC, +0x1F0, +0x1F4; 0x6126D0; 0x5E5DA0; 0x60D0D0(+0x1DC, −type +0x198
	// FatigueEffect); type +0xC0 (Info.RideTypeStringIndex) 0x17: +0x21B = the byte of VAR 11; +0x21B nonzero: 0x612740, then the record 3 × (byte(+0x1BC) − byte(+0x234)) + 50 by kind (a shop: 0xB..0xE by
	// +0x218, then 0x10..0x12 by +0x21C; a sideshow 0x14) through 0x5AEDE0, 0x5E61D0, and a sideshow's thought 5; zero: 0x611B0B "lost", −MediumHappinessChange, +0x2E4 = 0, +0x2DC −= 35, a sideshow's thought 6
	internal void ThemeParkIncUseRoutine( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncAttractionView view )
	{
		var settings = NativeTpi!;
		var ride = view.Ride;
		var use = view.Use;
		PushThemeParkIncHistory( guest.UsedHistory, view.ThingId );
		if ( (uint)use.WhichUIType <= 2 )
			tpi.Words1EC[use.WhichUIType]++;
		ThemeParkIncPayment( guest, tpi, ride );
		Payments?.ThemeParkIncCustomer( ride );
		tpi.Word1DC = BitConverter.SingleToInt32Bits( MeterMath.Add( BitConverter.Int32BitsToSingle( tpi.Word1DC ), -(float)use.FatigueEffect ) );
		if ( use.RideTypeStringIndex == ThemeParkIncScriptedResultType )
			tpi.Byte21B = ride.Param & 0xFF;
		guest.LastAttractionId = ride.AttractionId;
		guest.SecondsSinceRide = 0;
		if ( tpi.Byte21B == 0 )
		{
			guest.Happiness = MeterMath.Add( guest.Happiness, -HappinessChange( settings, 1 ) );
			tpi.UseSucceeded = false;
			SetTpiWord( tpi, 3, MeterMath.Add( TpiWord( tpi, 3 ), -35f ) );
			if ( use.WhichUIType == 2 )
				Think( guest, GuestThought.Dissatisfied );
			return;
		}
		ThemeParkIncOutcome( guest, tpi, view, settings );
		var change = 3 * (MeterMath.Byte( guest.Happiness ) - MeterMath.Byte( BitConverter.Int32BitsToSingle( tpi.Word234 ) ));
		var value = (change + 50) & 0xFF;
		if ( use.WhichUIType == 1 )
		{
			if ( use.SpecialIngredient is >= 1 and <= 4 )
				Payments?.PriceTested( 0xA + use.SpecialIngredient, value );
			if ( view.Terms.AppearanceEffect is >= 1 and <= 3 )
				Payments?.PriceTested( 0xF + view.Terms.AppearanceEffect, value );
		}
		else if ( use.WhichUIType == 2 )
			Payments?.PriceTested( 0x14, value );
		// [EXT:tpi-capability-gates] the history object's ring +0x220 (0x5E61D0) is not kept: OpenTPW holds only the three price rings and the walk-away count of Theme Park Inc's attraction history
		// (SAVE-940), and nothing reads it; no draw
		if ( use.WhichUIType == 2 )
			Think( guest, GuestThought.Pleased );
	}

	/// <summary>The payment step <c>0x6126D0</c>: a recorded fee above 0 is booked (the bank and the object's takings), the till sounds, the purse falls and the fees paid rise.</summary>
	// [BIN:TPI-EXE:0x6126D0 payment step] +0x1B4 above 0 (unsigned): 0x5E5DE0(fee), 0x60BE90(0xD0) (the till), +0x1C0 −= fee, +0x1C4 += fee; the fee word is not cleared
	private void ThemeParkIncPayment( Guest guest, ThemeParkIncGuestRecord tpi, RideVisitorBridge ride )
	{
		var fee = tpi.Fee;
		if ( fee == 0 )
			return;
		Payments?.ThemeParkIncTakings( ride, fee );
		Voiced?.Invoke( guest, TillVoiceEvent );
		guest.Money -= fee;
		tpi.FeesPaid = unchecked( tpi.FeesPaid + fee );
		Observer?.Spent( guest, fee, ride.AttractionId );
		MoneySpent?.Invoke( guest, fee, ride.AttractionId );
	}

	/// <summary>The float meter word <c>+0x2D0 + 4 × <paramref name="index"/></c> (<see cref="ThemeParkIncGuestRecord.Words2D0"/>).</summary>
	private static float TpiWord( ThemeParkIncGuestRecord tpi, int index ) => BitConverter.Int32BitsToSingle( tpi.Words2D0[index] );

	private static void SetTpiWord( ThemeParkIncGuestRecord tpi, int index, float value ) => tpi.Words2D0[index] = BitConverter.SingleToInt32Bits( value );

	// ---- The outcome -------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// The outcome <c>0x612740</c> of a successful use: the prize or the goods booked, the excitement match, the type's effects, a shop's own terms, the small-portion draws, the special ingredient, the
	/// appearance (a balloon, a costume, an umbrella), a relief object's visit, a won play's gain, the photo booth and the mood: its rings' thought and spot animation.
	/// </summary>
	// [BIN:TPI-EXE:0x612740 outcome] in this order: +0x2E4 = 1; a sideshow (+0x600 2): 0x5E60E0(+0x88), +0x1C0 += +0x88, +0x2DC += 50; a shop (1): 0x5E60E0(0x5E63E0); the match 0x611DA0 m, happiness
	// += m and += type +0x20C, the mood (a single at [esp+0x14]) = +0x20C + m; illness += type +0x208; the rest below
	private void ThemeParkIncOutcome( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncAttractionView view, ThemeParkIncGuestSettings settings )
	{
		var ride = view.Ride;
		var use = view.Use;
		var words = TpiThingWords( ride );
		tpi.UseSucceeded = true;
		if ( use.WhichUIType == 2 )
		{
			Payments?.ThemeParkIncCostBooked( ride, words.CostOfGoods );
			guest.Money += words.CostOfGoods;
			SetTpiWord( tpi, 3, MeterMath.Add( TpiWord( tpi, 3 ), 50f ) );
		}
		else if ( use.WhichUIType == 1 )
			Payments?.ThemeParkIncCostBooked( ride, Payments.ThemeParkIncGoodsCost( ride ) );
		var match = ThemeParkIncMatchStep( guest, tpi, ride, settings );
		guest.Happiness = MeterMath.Add( guest.Happiness, match );
		guest.Happiness = MeterMath.Add( guest.Happiness, use.HappinessEffect );
		var mood = (float)((float)use.HappinessEffect + (float)match);
		guest.Nausea = MeterMath.Add( guest.Nausea, use.VomitEffect );
		if ( use.WhichUIType == 1 )
		{
			// [BIN:TPI-EXE:0x6128E8 shop terms] thirst −= type +0x200, hunger −= +0x204 (0x60D0D0); the larger plays 0x83 (thirst) or 0x84 (hunger); illness, happiness and the mood += the thing's third byte
			// +0x9C; litter += type +0x210 and += +0x9C; toilet += type +0x214
			guest.Thirst = MeterMath.Add( guest.Thirst, -(float)use.ThirstEffect );
			guest.Hunger = MeterMath.Add( guest.Hunger, -(float)use.HungerEffect );
			if ( use.ThirstEffect != use.HungerEffect )
				Voiced?.Invoke( guest, use.ThirstEffect > use.HungerEffect ? DrinkVoiceEvent : MealVoiceEvent );
			const int third = ThemeParkIncGoodsCost.BuiltThirdByte;
			guest.Nausea = MeterMath.Add( guest.Nausea, third );
			guest.Happiness = MeterMath.Add( guest.Happiness, third );
			mood = (float)(third + mood);
			guest.Litter = MeterMath.Add( guest.Litter, use.LitterEffect );
			guest.Litter = MeterMath.Add( guest.Litter, third );
			guest.Toilet = MeterMath.Add( guest.Toilet, use.ToiletEffect );
		}
		var amount = words.Amount & 0xFF;
		// [BIN:TPI-EXE:0x612AE0 small portion] type +0x204 nonzero: R (0x612AEC), (R & 7) + +0x204 + the amount below 30 (unsigned): −SmallHappinessChange to happiness and the mood, the ring +0x550, +0x2D0 −= 30;
		// else +0x2D0 += 35; then the same for +0x200 with R (0x612BCF) and +0x2D4
		if ( use.HungerEffect != 0 )
			mood = ThemeParkIncSmallPortion( guest, tpi, settings, use.HungerEffect, amount, 0, mood );
		if ( use.ThirstEffect != 0 )
			mood = ThemeParkIncSmallPortion( guest, tpi, settings, use.ThirstEffect, amount, 1, mood );
		// [BIN:TPI-EXE:0x612C9D scaled happiness] happiness and the mood += (type +0x20C × the amount) / 100 (signed, truncated)
		var scaled = use.HappinessEffect * amount / 100;
		guest.Happiness = MeterMath.Add( guest.Happiness, scaled );
		mood = (float)(scaled + mood);
		// [BIN:TPI-EXE:0x612D3B special ingredient] the switch on type +0x218 (the table 0x6136EC): 1 toilet += ((FatEffect +0x22C) + 1) × amount / 100; 2 thirst += ((SaltEffect +0x228) + 1) × amount / 100;
		// 3 thirst += (IceEffect +0x230 + ThirstEffect +0x200) × amount / 100; 4 the speed boost word +0xD8 += ((SugarEffect +0x234) + 1) × amount / 100
		switch ( use.SpecialIngredient )
		{
			case FatIngredient:
				guest.Toilet = MeterMath.Add( guest.Toilet, (use.FatEffect + 1) * amount / 100 );
				break;
			case SaltIngredient:
				guest.Thirst = MeterMath.Add( guest.Thirst, (use.SaltEffect + 1) * amount / 100 );
				break;
			case IceIngredient:
				guest.Thirst = MeterMath.Add( guest.Thirst, (use.IceEffect + use.ThirstEffect) * amount / 100 );
				break;
			case SugarIngredient:
				guest.Boost = unchecked( (ushort)(guest.Boost + (use.SugarEffect + 1) * amount / 100) );
				break;
		}
		ThemeParkIncAppearance( guest, tpi, view, settings, words );
		if ( ride.ProvidesRelief )
			mood = ThemeParkIncRelief( guest, tpi, ride, mood );
		if ( use.WhichUIType == 2 )
		{
			// [BIN:TPI-EXE:0x613398 won play] +0x1F8 += 1; g = _ftol(ln(+0x88 / +0x94) / ln(2.0) × MediumHappinessChange) (fidiv, fyl2x, fdivp, fmulp at 24 bits); happiness and the mood += g
			tpi.Words1EC[3]++;
			var gain = ThemeParkIncPlayGain( words.CostOfGoods, words.Price, settings.MediumHappinessChange & 0xFF );
			guest.Happiness = MeterMath.Add( guest.Happiness, gain );
			mood = (float)(gain + mood);
		}
		// [BIN:TPI-EXE:0x613448 photo booth] the thing's photo booth word +0x3A (0x5E8EE0) nonzero scores the booth: (100 − the squared cell distance to its back cell, at most 100) × DistanceWeightForPhoto
		// + SatisfactionWeightForPhoto × the mood above PhotoThreshold sets +0x208 to it; 0 sets +0x208 = 0. OpenTPW places no add-on, so the word is 0 for every attraction
		tpi.Word208 = 0;
		ThemeParkIncMood( guest, tpi, settings, mood );
	}

	/// <summary>
	/// The excitement match <c>0x611DA0</c>: a rating of 0 gives 0; else the bands of the difference between the guest's preferred excitement and the rating give PerfectRide, GoodRide or OKRide (and raise
	/// <c>+0x2E0</c> by it), and the illness rises by <c>((100 − hunger) / 20) × (rating / RideVomitDivisor)</c>, a rise that also lowers <c>+0x2E0</c>. Returns the gain.
	/// </summary>
	// [BIN:TPI-EXE:0x611DA0 match step] 0x5E41D0(ride, 0) (the rating byte) 0 returns 0; d = |byte +0x2A8 − rating| (cdq/xor/sub); d < PerfectExcitementMatch: +0x2E0 += PerfectRide, g = PerfectRide;
	// d < GoodExcitementMatch: GoodRide; d < OKExcitementMatch: OKRide, and +0x2A8 below the rating raises the declined ring +0x4C8; else 0; then b0 = byte(illness), illness += (100 − byte(hunger)) / 20
	// (the magic 0x66666667 with sar 3, signed) × (rating / [0xA01A84]) (idiv), b1 = byte(illness), b1 above b0 lowers +0x2E0 by b1 − b0
	private int ThemeParkIncMatchStep( Guest guest, ThemeParkIncGuestRecord tpi, RideVisitorBridge ride, ThemeParkIncGuestSettings settings )
	{
		var rating = ride.LiveExcitement & 0xFF;
		if ( rating == 0 )
			return 0;
		var preferred = tpi.PreferredExcitement & 0xFF;
		var difference = Math.Abs( preferred - rating );
		var gain = 0;
		if ( difference < settings.PerfectExcitementMatch )
			gain = settings.PerfectRide;
		else if ( difference < settings.GoodExcitementMatch )
			gain = settings.GoodRide;
		else if ( difference < settings.OkExcitementMatch )
			gain = settings.OkRide;
		if ( difference < settings.OkExcitementMatch )
			SetTpiWord( tpi, 4, MeterMath.Add( TpiWord( tpi, 4 ), gain ) );
		if ( difference >= settings.GoodExcitementMatch && difference < settings.OkExcitementMatch && preferred < rating )
			Payments?.PriceVerdict( ride, false, false, true );
		var before = MeterMath.Byte( guest.Nausea );
		guest.Nausea = MeterMath.Add( guest.Nausea, (100 - MeterMath.Byte( guest.Hunger )) / 20 * (rating / settings.RideVomitDivisor) );
		var after = MeterMath.Byte( guest.Nausea );
		if ( after > before )
			SetTpiWord( tpi, 4, MeterMath.Add( TpiWord( tpi, 4 ), -(float)(after - before) ) );
		return gain;
	}

	/// <summary>One small-portion test of the outcome: one world draw; a small portion costs the small happiness step (and the mood) and lowers the word, a full one raises it.</summary>
	private float ThemeParkIncSmallPortion( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncGuestSettings settings, int effect, int amount, int word, float mood )
	{
		var draw = random.NextWord();
		if ( unchecked( (uint)((int)(draw & 7) + effect + amount) ) < 30 )
		{
			var small = settings.SmallHappinessChange & 0xFF;
			guest.Happiness = MeterMath.Add( guest.Happiness, -(float)small );
			mood = (float)(mood - small);
			// [EXT:tpi-capability-gates] the history object's ring +0x550 is not kept (SAVE-940), as for state 13's refusal of a long queue
			SetTpiWord( tpi, word, MeterMath.Add( TpiWord( tpi, word ), -30f ) );
		}
		else
			SetTpiWord( tpi, word, MeterMath.Add( TpiWord( tpi, word ), 35f ) );
		return mood;
	}

	/// <summary>
	/// The appearance step of the outcome (type <c>+0x21C</c>): a balloon shop gives a balloon (a colour draw, the countdown from the quality byte) and judges the quality; a costume shop puts a costume
	/// on (or takes it off); an umbrella shop gives an umbrella. Each judges the quality byte against <c>ComplainAboutThisQuality</c> plus a draw.
	/// </summary>
	// [BIN:TPI-EXE:0x612E89 appearance] 1 (0x6130B2): +0x2A0 = 0x690370(0xE, 0) (a draw), +0x294 = the model, +0x298 = clamp(byte +0x8C × [0x9D0610] / 100, [0x9D060C], [0x9D0610]); the analyser's +0x18;
	// R % 10 + [0xA01B44] above the quality byte (signed): the declined ring and +0x2D8 += 33, else +0x2D8 −= 33. 2 (0x612F4E): +0x34 6 already: "taking costume off", +0x34 = 0, the reseed
	// 0x649430(id), +0x30 = 0x6902F0(0); else +0x34 = 6, +0x30 = 0x6902F0(6), the analyser's +0x1C, a draw unread, R % 10 + [0xA01B44] above the quality (unsigned): the declined ring; +0x2D8 = 33.
	// 3 (0x612EA9): +0x238 = 3, +0x244 = 0x690370(0x13, 0), no sprite +0x240: 0x60B3F0(0), the sprite, +0x238 = 4; the analyser's +0x20; R % 10 as for the costume; +0x2D8 = 33
	// [EXT:tpi-capability-gates] the analyser's balloon, costume and umbrella counters (+0x18, +0x1C, +0x20 of 0x64EAC0) are not kept: no ported reader; they draw nothing
	private void ThemeParkIncAppearance( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncAttractionView view, ThemeParkIncGuestSettings settings, ThemeParkIncThingWords words )
	{
		var quality = words.Quality & 0xFF;
		switch ( view.Terms.AppearanceEffect )
		{
			case 1:
			{
				tpi.BalloonColour = (int)((random.NextWord() >> 2) % ThemeParkIncBalloonColours);
				tpi.HoldsBalloon = true;
				var countdown = (uint)quality * ThemeParkIncBalloonLife / 100;
				tpi.BalloonCountdown = countdown < ThemeParkIncBalloonFloor ? ThemeParkIncBalloonFloor : countdown > ThemeParkIncBalloonLife ? ThemeParkIncBalloonLife : countdown;
				var threshold = (int)(random.NextWord() % 10) + settings.ComplainAboutThisQuality;
				if ( quality >= threshold )
					SetTpiWord( tpi, 2, MeterMath.Add( TpiWord( tpi, 2 ), -33f ) );
				else
				{
					Payments?.PriceVerdict( view.Ride, false, false, true );
					SetTpiWord( tpi, 2, MeterMath.Add( TpiWord( tpi, 2 ), 33f ) );
				}
				break;
			}
			case 2:
				if ( tpi.SpriteCategory == ThemeParkIncGuestSnapshot.ThemeParkIncCostumeCategory )
				{
					tpi.SpriteCategory = 0;
					random.Reseed( (ushort)guest.Id );
					tpi.SpriteVariant = ThemeParkIncSpriteVariant( 0, random.NextWord() );
					guest.InCostume = false;
					break;
				}
				tpi.SpriteCategory = ThemeParkIncGuestSnapshot.ThemeParkIncCostumeCategory;
				tpi.SpriteVariant = ThemeParkIncSpriteVariant( ThemeParkIncGuestSnapshot.ThemeParkIncCostumeCategory, random.NextWord() );
				guest.InCostume = true;
				random.NextWord();
				JudgeThemeParkIncQuality( tpi, view, settings, quality );
				break;
			case 3:
				guest.UmbrellaState = UmbrellaRules.StateOpening;
				guest.UmbrellaType = UmbrellaRules.Colour( random.NextWord() );
				// the sprite +0x240 went at state 20's entry (0x6142E0), so the umbrella opens at once
				guest.ToiletBonus = 0;
				guest.UmbrellaState = UmbrellaRules.StateOpen;
				JudgeThemeParkIncQuality( tpi, view, settings, quality );
				break;
		}
	}

	/// <summary>The costume and umbrella shops' quality judgement: a draw, the declined ring when the quality byte is below <c>ComplainAboutThisQuality</c> plus the draw modulo 10, and <c>+0x2D8</c> = 33.</summary>
	private void JudgeThemeParkIncQuality( ThemeParkIncGuestRecord tpi, ThemeParkIncAttractionView view, ThemeParkIncGuestSettings settings, int quality )
	{
		var threshold = unchecked( (uint)((int)(random.NextWord() % 10) + settings.ComplainAboutThisQuality) );
		if ( (uint)quality < threshold )
			Payments?.PriceVerdict( view.Ride, false, false, true );
		SetTpiWord( tpi, 2, 33f );
	}

	/// <summary>The sprite variant draw <c>0x6902F0(category)</c>: the draw shifted right by 2, modulo the category's loaded bank count.</summary>
	// [BIN:TPI-EXE:0x6902F0 sprite variant] (R >> 2) % [0x9D7008 + 12 × category] (unsigned div): the loaded count. A rig without loaded banks has no count and reads variant 0; every level loads
	// its generations' and costumes' banks (all three worlds ship a costume set)
	private int ThemeParkIncSpriteVariant( int category, uint draw )
	{
		var count = category == ThemeParkIncGuestSnapshot.ThemeParkIncCostumeCategory ? TpiCostumeBankFemale?.Length ?? 0
			: TpiSpriteBankCounts is { } counts && (uint)category < (uint)counts.Length ? counts[category] : 0;
		return count > 0 ? (int)((draw >> 2) % (uint)count) : 0;
	}

	/// <summary>The balloon's full life (<c>0x9D0610</c>) and the floor of a new balloon's countdown (<c>0x9D060C</c>).</summary>
	// [BIN:TPI-EXE:0x9D060C balloon countdown bounds] the dwords 25 and 255
	private const uint ThemeParkIncBalloonLife = 255, ThemeParkIncBalloonFloor = 25;

	/// <summary>
	/// A relief object's visit (the thing flag <c>+0x42</c> bit 1): an illness byte above 90 is cured at a happiness cost of 50, a worn toilet costs 20, else the toilet's byte halved is the gain; the toilet
	/// meter goes to 0 and the speed term to 25. Returns the mood with the change.
	/// </summary>
	// [BIN:TPI-EXE:0x612FD7 relief] the thing's +0x3C (a primed toilet) nonzero: the prank's victim (0x5E78E0(2000), the prank message kind 2 with the prankster's id 0x613AF0, −BigHappinessChange,
	// illness 95, the particle 0x26); else 0x5E78E0(byte toilet) (the wear); +0x29C nonzero: the thing's +0x3C = the id, +0x29C = 0, happiness += 1, thought 0x15; then byte(illness) above 90:
	// illness 0 and −50; else 0x5E3990 (worn): −20; else byte(toilet) >> 1; happiness and the mood += it, +0x1D0 = 0, 0x60B3F0(1)
	// [EXT:tpi-capability-gates] the toilet prank (state 6's case 1 sets +0x29C) is phase P4's: the priming of the thing's +0x3C (and its +1 happiness and thought 0x15) is not made, so no toilet is ever
	// primed and the victim's branch is not reached; +0x29C stays set. Neither branch draws
	// [EXT:tpi-no-maintenance-wear] the toilet's wear 0x5E78E0 (+0x54 by the toilet byte and its RegionFX rows 1 and 6) is not made, as for Theme Park Inc's toilets in the default path
	private float ThemeParkIncRelief( Guest guest, ThemeParkIncGuestRecord tpi, RideVisitorBridge ride, float mood )
	{
		int change;
		if ( MeterMath.Byte( guest.Nausea ) > 90 )
		{
			guest.Nausea = 0;
			change = -50;
		}
		else if ( ride.ReliefWornOut )
			change = -20;
		else
			change = MeterMath.Byte( guest.Toilet ) >> 1;
		guest.Happiness = MeterMath.Add( guest.Happiness, change );
		mood = (float)(change + mood);
		guest.Toilet = 0;
		guest.ToiletBonus = UmbrellaRules.SpeedTerm( guest.UmbrellaState, GuestSpeed.ToiletBonus );
		return mood;
	}

	/// <summary>A won play's gain: <c>_ftol(ln(prize / price) / ln 2 × medium)</c> with each x87 step rounded to a single; a result that is not finite gives 0, the low dword of the integer indefinite.</summary>
	// [BIN:TPI-EXE:0x6133B7 won play gain] fild +0x88, fidiv +0x94, fldln2/fyl2x (ln), fldln2 with the double 2.0 (0x93B310)/fyl2x (ln 2), fdivp, fild the byte, fmulp, _ftol: at the control word 0x7F each
	// result rounds to 24 bits; a price of 0 gives an infinity (or a NaN), whose _ftol is the low dword 0 of the integer indefinite
	private const double Ln2 = 0.69314718055994530942;

	internal static int ThemeParkIncPlayGain( int prize, int price, int medium )
	{
		var quotient = (float)((double)prize / price);
		// ln x as log₂ x × ln 2 through the deterministic DetMath.Log2 (docs/DETERMINISM.md), each rounded to a single as the x87 at 24 bits rounds fyl2x's result
		var logarithm = (float)(DetMath.Log2( quotient ) * Ln2);
		var ln2 = (float)Ln2;
		var product = (float)((float)(logarithm / ln2) * (float)medium);
		return ThemeParkIncX87.Ftol( product );
	}

	/// <summary>
	/// The mood's verdict at the end of the outcome: above <c>HappyDiffForVGood</c> thought 5 and the mood animation (umbrella down); below <c>HappyDiffForVPoor</c> thought 6 and animation 10 (umbrella
	/// down); above <c>HappyDiffForGood</c> thought 5; below <c>HappyDiffForPoor</c> thought 6 and animation 10 (umbrella down).
	/// </summary>
	// [BIN:TPI-EXE:0x61356E mood bands] fild VGood, fcomp the mood: below it (C0) the ring +0x2A8, the animation (umbrella +0x238 0 or 2: generation 0 → 5, 1 → 6 female or 7, 2 → 8 or 9, the bank's
	// gender 0x68E7B0), thought 5, 0x60FB30(animation) when one was picked; else VPoor above the mood (neither C0 nor C3): the ring +0x440, thought 6 and 0x60FB30(10) with the umbrella down; else
	// Good below the mood: the ring +0x330 and thought 5; else Poor above the mood: the ring +0x3B8, thought 6 and 0x60FB30(10) with the umbrella down
	// [EXT:tpi-capability-gates] the four mood rings +0x2A8, +0x440, +0x330 and +0x3B8 of the history object are not kept (SAVE-940); no draw
	private void ThemeParkIncMood( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncGuestSettings settings, float mood )
	{
		var umbrellaDown = guest.UmbrellaState is UmbrellaRules.StateNone or UmbrellaRules.StateClosed;
		int? animation = null;
		if ( (double)settings.HappyDiffForVGood < mood )
		{
			if ( umbrellaDown && tpi.Generation is >= 0 and <= 2 )
				animation = ThemeParkIncMoodAnimation( tpi.Generation, ThemeParkIncSpriteIsFemale( guest ) );
			Think( guest, GuestThought.Pleased );
		}
		else if ( (double)settings.HappyDiffForVPoor > mood )
		{
			if ( umbrellaDown )
				animation = ThemeParkIncAnimationTen;
			Think( guest, GuestThought.Dissatisfied );
		}
		else if ( (double)settings.HappyDiffForGood < mood )
		{
			Think( guest, GuestThought.Pleased );
			return;
		}
		else if ( (double)settings.HappyDiffForPoor > mood )
		{
			Think( guest, GuestThought.Dissatisfied );
			if ( umbrellaDown )
				animation = ThemeParkIncAnimationTen;
		}
		if ( animation is { } picked )
			StartThemeParkIncSpotAnimation( guest, tpi, picked );
	}

	/// <summary>The spot animation 10 (the unhappy shrug), which state 19 also plays for a lost use.</summary>
	public const int ThemeParkIncAnimationTen = 10;

	// ---- State 19 ----------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// State 19's entry (<c>0x61A69D</c>): the body back (a ride that drew it keeps it, walking), a teleporting ride's beam, and a balloon kept through the ride is given back unless a balloon shop has just
	/// given one.
	/// </summary>
	// [BIN:TPI-EXE:0x61A69D state 19 entry] the thing's +0x42 bit 0x20 clear: 0x5D1050 (the sprite destroyed); the sprite +0x14 0: 0x5D1000 (made again, the mode kept), else mode 1; +0x38 = +0x3C = 0;
	// type +0x1C0: 0x60AE00(0) and 0x60B700(0x13, 0, 0); +0x294 or +0x298 and type +0x21C not 1: +0x294 = 0x4E4810("CO@", 0xE, 0, +0x2A0) (the balloon again)
	private void EnterTpiBeingUnloaded( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		if ( TpiAttraction( tpi.Target ) is not { } view )
			return;
		if ( view.Use.RideHandlesSprite )
			tpi.Mode = 1;
		if ( view.Use.RequiresTeleport )
		{
			// [BIN:TPI-EXE:0x61A70B placement] 0x60AE00(0) comes first: besides the drawing it raises the stamp counter (0x5D81F0 at 0x60AFFD) and clears the stranded mark +0x1B0 when the
			// raised value is below it (0x60B002). The counter here counts the edits only (ParkCellMap.StampCounter), and a mark set under it is never above the next value, so the test
			// cannot clear it; the raise and the test join here with Theme Park Inc's navigator and its counter of the simulation's calls (NV3, TPI-NAVIGATOR.md)
			Beamed?.Invoke( guest );
		}
		if ( (tpi.HoldsBalloon || tpi.BalloonCountdown > 0) && view.Terms.AppearanceEffect != 1 )
			tpi.HoldsBalloon = true;
	}

	/// <summary>
	/// State 19 (<c>0x618380</c>), being unloaded: the walk off; stuck, idle with no target; arrived, a photo booth (<c>+0x208</c>) or the old major destination (<c>+0x206</c>) may take the guest back to a
	/// queue (state 13), else it is idle, with the mood animation after a successful use or animation 10 after a lost one (umbrella down).
	/// </summary>
	// [BIN:TPI-EXE:0x618380 state 19] 0x60B440: 2 sets +0x204 = 0 and 6; 1 returns; 0: +0x208 nonzero: +0x204 = it (+0x208 kept), a thing of class 3 (byte +2): 0x5E0520 nonzero and 0x60B7F0: "Lets go get a
	// photo", 13; another class: "Deleted photo booth while getting off ride!!!", +0x204 = 0. Then +0x206 nonzero: +0x204 = it, +0x206 = 0, a class-3 thing: an umbrella shop (+0x21C 3) with +0x238
	// nonzero, or a balloon shop (1) with +0x294 or +0x298: "trapped the little ****", +0x204 = 0; else 0x5E0520 and 0x60B7F0: "Left minor destination, found old major one again!", 13; else
	// "Deleted major dest", +0x204 = 0; another class keeps +0x204. Then "successfully left ride", setter(6) and with +0x238 0 or 2: +0x2E4 set 0x60FB30(0x60CDD0()), else 0x60FB30(10)
	internal void UpdateTpiBeingUnloaded( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		var walk = TpiWalk( guest );
		if ( walk == WalkResult.Walking )
			return;
		if ( walk == WalkResult.Stuck )
		{
			tpi.Target = 0;
			guest.AttractionId = 0;
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
			return;
		}
		if ( tpi.Word208 != 0 )
		{
			tpi.Target = tpi.Word208;
			if ( TpiAttraction( tpi.Target ) is { } booth )
			{
				var back = TpiBackWord( booth.Ride );
				if ( back != 0 && RouteToTpiCellWord( guest, back ) )
				{
					SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WalkingToMajor );
					return;
				}
			}
			else
				tpi.Target = 0;
		}
		if ( tpi.Word206 != 0 )
		{
			tpi.Target = tpi.Word206;
			tpi.Word206 = 0;
			if ( TpiAttraction( tpi.Target ) is { } major )
			{
				var effect = major.Terms.AppearanceEffect;
				if ( effect == 3 && guest.UmbrellaState != UmbrellaRules.StateNone || effect == 1 && (tpi.HoldsBalloon || tpi.BalloonCountdown > 0) )
					tpi.Target = 0;
				else
				{
					var back = TpiBackWord( major.Ride );
					if ( back != 0 && RouteToTpiCellWord( guest, back ) )
					{
						SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WalkingToMajor );
						return;
					}
					tpi.Target = 0;
				}
			}
		}
		guest.AttractionId = 0;
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
		if ( guest.UmbrellaState is not (UmbrellaRules.StateNone or UmbrellaRules.StateClosed) )
			return;
		StartThemeParkIncSpotAnimation( guest, tpi, tpi.UseSucceeded ? ThemeParkIncMoodAnimation( tpi.Generation, ThemeParkIncSpriteIsFemale( guest ) ) : ThemeParkIncAnimationTen );
	}

	// ---- Restore of a phase P3a save ---------------------------------------------------------------------------------------------

	/// <summary>
	/// A phase P3a save's guest of the bridged boarding (state word 16 with one of Theme Park World's four boarding states): the native state its boarding stands for, or null when the save holds none.
	/// Boarding (the walk to the stand point) stays 16; WaitingToBoard becomes 17 without its entry roll; Using becomes 20; ExitingRide becomes 19. Nothing is drawn.
	/// </summary>
	internal static int? ThemeParkIncStateOfBridgedBoarding( GuestState state ) => state switch
	{
		GuestState.Boarding => ThemeParkIncGuestStates.BeingLoaded,
		GuestState.WaitingToBoard => ThemeParkIncGuestStates.WaitingToBeAdmitted,
		GuestState.Using => ThemeParkIncGuestStates.UnderObjectControl,
		GuestState.ExitingRide => ThemeParkIncGuestStates.BeingUnloaded,
		_ => null
	};
}
