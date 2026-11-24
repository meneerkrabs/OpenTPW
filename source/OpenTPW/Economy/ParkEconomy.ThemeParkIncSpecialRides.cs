namespace OpenTPW;

public sealed partial class ParkEconomy
{
	/// <summary>
	/// Theme Park Inc's special rides (<see cref="ThemeParkIncSpecialRides"/>): the table of rides awaiting their activation, the staff slots each needs and the activated flags. Built with the Theme Park
	/// Inc staff (a trained member fills its slots); null in Theme Park World.
	/// </summary>
	public ThemeParkIncSpecialRides? TpiSpecialRides { get; private set; }

	/// <summary>
	/// Whether the object with this economy instance stands on land the park does not own (its thing square's flag 0x40, <c>0x67ECA0</c>); set by the level. Null (a headless economy, which has no map)
	/// answers false.
	/// </summary>
	internal Func<int, bool>? ThemeParkIncObjectOnUnownedLand { get; set; }

	private bool OnUnownedLand( int instance ) => ThemeParkIncObjectOnUnownedLand?.Invoke( instance ) ?? false;

	/// <summary>A special ride placed: its record is appended (<c>0x623D80</c>, the attraction placement <c>0x5DBD7B</c> for a type whose <c>Info.IsSpecial</c> is 1).</summary>
	private void AddThemeParkIncSpecialRide( ParkObjectState item, EconomyObjectInfo info )
	{
		if ( TpiSpecialRides != null && info.IsSpecial )
			TpiSpecialRides.Add( item.Id, info.SkillSet ?? Array.Empty<int>() );
	}

	/// <summary>Whether the object is a special ride still awaiting its activation (<c>0x6242F0</c> answers false): its click opens the activation, and it is not a ride guests may use.</summary>
	public bool IsThemeParkIncSpecialRidePending( int instance ) => TpiSpecialRides is { } rides && !rides.IsFree( instance );

	/// <summary>The golden tickets the activation of the object costs (its type's <c>UsageInfo.GoldenTicketCost</c>, type record <c>+0x174</c>).</summary>
	public int ThemeParkIncActivationCost( int instance ) =>
		TryGetObject( instance, out var item ) && Catalog.TryGet( item.InfoId, out var info ) ? info.GoldenTicketCost : 0;

	/// <summary>Whether the activation window's Activate Ride button is enabled for the object (<c>0x6243C0</c>): a pending special ride, enough golden tickets and every staff slot filled.</summary>
	public bool CanActivateThemeParkIncSpecialRide( int instance ) =>
		TpiSpecialRides is { } rides && TpiCareer is { } career && rides.CanActivate( instance, career.GoldenTickets, ThemeParkIncActivationCost( instance ) );

	/// <summary>Whether the career has discovered every ability the object's activation asks for (<c>0x624330</c>); the window's note (UITEXT 115) shows when it has not.</summary>
	public bool ThemeParkIncActivationAbilitiesDiscovered( int instance ) =>
		TpiSpecialRides is not { } rides || TpiCareer is not { } career || rides.AbilitiesDiscovered( instance, career.Abilities );

	/// <summary>
	/// The Activate Ride button (<c>0x586D40</c>: a press of control <c>0x4E24</c> runs <c>0x5E8230</c> on the window's ride and closes the window): the ride's golden-ticket cost is spent, the ride is
	/// marked activated and opened at some point, and its record is emptied, so guests may use it and a share challenge that targets it may be offered.
	/// </summary>
	// [BIN:TPI-EXE:0x586D40 activate button] message 0x100 from control 0x4E24 calls 0x5E8230 on [0xA5960C] (the window's ride, set by 0x5863E0), closes the window (0x5106C0) and plays 0x1A6; the
	// window enables the control only while 0x6243C0 answers true (0x587219..0x587257), so a press never reaches the activation otherwise
	// [BIN:TPI-EXE:0x5E8230 activation] 0x74C8C0(type +0x174, UsageInfo.GoldenTicketCost) on the career, its answer dropped; then the camera fly-over (0x647930, 0x673230, 0x6742F0, 0x6746F0, 0x6662D0,
	// 0x660D80 "Zoom: %f", 0x46A550/0x4660F0) and, for a type whose +0x150 (Bumper.BumperType) is nonzero, the track tool's start (0x674750..0x5E87FE); +0x12D = 1, +0x12C = 1 (0x5E8910); the sound
	// 0x4AA970; 0x624280 empties the ride's record. No random number is drawn
	// [EXT:tpi-capability-gates] the fly-over and the track tool's start are not ported: the camera's flight and the track editor of a special track ride (the Science water ride 1160 and changing
	// hut 1110 have a nonzero BumperType) are Theme Park Inc's tool flow, which the text HUD does not run; the flags, the tickets and the record are
	public void ActivateThemeParkIncSpecialRide( int instance )
	{
		if ( TpiSpecialRides is not { } rides || TpiCareer is not { } career )
			throw new InvalidOperationException( "This park has no Theme Park Inc special rides." );
		if ( !CanActivateThemeParkIncSpecialRide( instance ) )
			throw new InvalidOperationException( $"Object {instance} cannot be activated now: it must be a special ride awaiting its activation with every staff slot filled and its golden tickets in hand." );
		career.TrySpendGoldenTickets( ThemeParkIncActivationCost( instance ) );
		rides.Activate( instance );
	}

	/// <summary>Message 0x2B's special-ride half (<c>0x623FB0</c>): the member whose training finished takes a slot of a special ride that asks for him.</summary>
	// [BIN:TPI-EXE:0x6325AA training finished] after the HQ's 0x634B00 the handler calls 0x623FB0 with the member's id; 0x6346C0, the other caller, has no caller of its own (its thunk 0x4019E7 is never called)
	// [BIN:TPI-EXE:0x6240C4 activation advice] a slot taken whose ride 0x6243C0 now answers true posts the advisor's 0x21F (a type whose object record +0x10 is 0x488) or 0x1E7 (0x71A930 -> 0x718820)
	// [EXT:tpi-capability-gates] the advice is not posted: Theme Park Inc's advisor queue carries the forecasts only (the advisor's other messages)
	internal void ThemeParkIncTrainingFinishedSpecialRides( ThemeParkIncStaffMember member ) => TpiSpecialRides?.Assign( member, OnUnownedLand );

	/// <summary>The staff HQ's release's special-ride half (<c>0x6241B0</c>, from <c>0x634730</c> for a member with an ability): he leaves the special-ride slot he fills.</summary>
	internal void ThemeParkIncReleaseSpecialRides( ThemeParkIncStaffMember member )
	{
		if ( member.Ability != 0 )
			TpiSpecialRides?.Release( member, OnUnownedLand );
	}

	/// <summary>
	/// The challenge events of a Theme Park Inc guest admitted to a ride (the guest state 20's entry <c>0x61A77E</c>, "UNDER OBJECT CONTROL"): types 17, 52 and 58 each count one for the ride's type, and
	/// type 57 counts one in its first value.
	/// </summary>
	// [BIN:TPI-EXE:0x61A867 admission events] after its side effects the state-20 entry posts 0x5C8940(0x11, 1, ride +0x16, 0) (0x61A867), (0x34, 1, type, 0) (0x61A87F) and (0x3A, 1, type, 0) (0x61A897); then a
	// type record whose +0xCC (UsageInfo.IsPhotoBooth) is nonzero posts (0x39, 1, the type of the booth's host thing +0x38, 1) (0x61A8BA pushl $1), any other (0x39, 1, type, 0) (0x61A902 pushl %ebx); no
	// random number is drawn in 0x61A77E..0x61A8FF
	// [EXT:tpi-twp-guest-rules] the events ride on the boarding of Theme Park World's guest (GuestEconomyBridge.TryPayVisit's Theme Park Inc branch, RecordRideUse), which stands in for the state-20
	// entry while Theme Park Inc's guests are Theme Park World's; the guests' state machine (guest plan P3, 0x61A77E) takes this call over, and no second sender is added. The photo booth's branch is
	// not reached: a booth is an add-on, which OpenTPW cannot place, so every admission takes the other branch
	private void PostThemeParkIncAdmissionEvents( ParkObjectState item )
	{
		if ( TpiChallenges is not { } challenges )
			return;
		var type = unchecked( (ushort)item.InfoId );
		challenges.AddEvent( 17, 1, type, false );
		challenges.AddEvent( 52, 1, type, false );
		challenges.AddEvent( 58, 1, type, false );
		challenges.AddEvent( 57, 1, type, false );
	}

	/// <summary>
	/// The first economy object of <paramref name="type"/> in the attraction list (<c>0x647850</c>): the world's first attraction thing of the type, as the level finds it, and that thing's economy
	/// instance; 0 when the type has no thing, -1 when its thing has no economy record.
	/// </summary>
	// [EXT:headless-economy] a headless economy has no attraction things: its lowest instance of the type stands in for the first thing
	internal int ThemeParkIncFirstObjectOfType( ushort type )
	{
		if ( TpiMissionWorld is { } world )
		{
			var thing = world.FindAttraction( type );
			if ( thing == 0 )
				return 0;
			foreach ( var item in objects.Values )
				if ( item.InfoId == type && ThingOf( item.Id ) == thing )
					return item.Id;
			return -1;
		}
		return objects.Values.Where( item => item.InfoId == type ).Select( item => item.Id ).DefaultIfEmpty( 0 ).Min();
	}

	/// <summary>A challenge's target as the trigger and the value poll read it.</summary>
	internal ThemeParkIncChallengeTarget ThemeParkIncChallengeTargetOf( ushort type )
	{
		var instance = ThemeParkIncFirstObjectOfType( type );
		if ( instance == 0 )
			return ThemeParkIncChallengeTarget.None;
		var special = Catalog.TryGet( type, out var info ) && info.IsSpecial;
		if ( instance < 0 || !TryGetObject( instance, out var item ) )
			return new ThemeParkIncChallengeTarget( true, special, false, 100 );
		return new ThemeParkIncChallengeTarget( true, special, TpiSpecialRides?.IsActivated( instance ) ?? false, RideBreakdownRules.GaugeByte( (float)item.Repair ) );
	}

	/// <summary>The lowest truncated state-of-repair byte of the park's rides (<c>0x5B7D30</c>), 100 with none.</summary>
	// [BIN:TPI-EXE:0x5B7D30 worst repair] walks the thing list (0x649060, 0x6490A0) from 100: a class-3 thing whose type record's +0x600 (Info.WhichUIType) is 0 gives (u8)__ftol(+0x54); the first one
	// is taken whatever its byte, a later one only when lower (signed jge); its id goes to the caller's mThingInFocus. The walk's order changes only the thing in focus, which nothing here keeps. The
	// attraction things OpenTPW keeps no economy record for (the obstacles, the camera path) keep the constructor's 100.0 (0x5DB52F) and are never worn, so they change no answer
	internal int ThemeParkIncLeastRideRepair()
	{
		var least = 100;
		var first = true;
		foreach ( var item in objects.Values )
		{
			if ( !Catalog.TryGet( item.InfoId, out var info ) || info.RatingType != 0 )
				continue;
			var repair = RideBreakdownRules.GaugeByte( (float)item.Repair );
			if ( first || repair < least )
				least = repair;
			first = false;
		}
		return least;
	}

	/// <summary>
	/// Restores the special rides of a park save; a save without them refused before this (<see cref="ParkSaveFile"/>).
	/// </summary>
	internal void RestoreThemeParkIncSpecialRides( ThemeParkIncSpecialRidesSnapshot? state )
	{
		if ( state == null )
			return;
		if ( TpiSpecialRides == null )
			throw new InvalidDataException( "Park save has Theme Park Inc special rides, but the park has no Theme Park Inc staff." );
		TpiSpecialRides.Restore( state );
	}
}
