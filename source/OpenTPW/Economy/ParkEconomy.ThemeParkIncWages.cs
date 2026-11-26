namespace OpenTPW;

/// <summary>Theme Park Inc's wage demands, the wage negotiation, the dismissal and the month's strikes (docs/reverse/TPI-WAGES.md).</summary>
public sealed partial class ParkEconomy
{
	/// <summary>The advisor message the daily routine posts with a wage demand (<c>0x1C8</c>).</summary>
	public const int ThemeParkIncWageDemandAdvice = 0x1C8;

	/// <summary>
	/// The advisor messages the staff posted since the client last took them: the wage demand's <see cref="ThemeParkIncWageDemandAdvice"/>. Theme Park Inc's advisor queue takes only the forecasts and the letters so far, so the
	/// client notes them (<c>[EXT:tpi-capability-gates]</c>).
	/// </summary>
	internal List<int> TpiStaffAdvice { get; } = new();

	/// <summary>The strike messages (type 0x17) the staff HQ and the strikers posted since the client last took them; their only listener is the advisor (<c>0x7176E0</c>), which does not say them.</summary>
	internal List<ThemeParkIncStaff.StrikeMessage> TpiStrikeMessages { get; } = new();

	/// <summary>The staff's advisor messages since the last call, oldest first.</summary>
	internal IReadOnlyList<int> TakeThemeParkIncStaffAdvice()
	{
		var taken = TpiStaffAdvice.ToArray();
		TpiStaffAdvice.Clear();
		return taken;
	}

	/// <summary>The strike messages since the last call, oldest first.</summary>
	internal IReadOnlyList<ThemeParkIncStaff.StrikeMessage> TakeThemeParkIncStrikeMessages()
	{
		var taken = TpiStrikeMessages.ToArray();
		TpiStrikeMessages.Clear();
		return taken;
	}

	/// <summary>
	/// The day's wage demands, in the members' handle order: the member's older demand rows go, the advisor's message 0x1C8 is posted and the tag system files the demand's row (unread).
	/// </summary>
	// [BIN:TPI-EXE:0x628D4F demand filed] 0xAA07F4 = the day; 0x6375C0 (the handle), 0x64EA60 (the tag system) and 0x636C90(0x90, handle): the member's unflagged rows of tag 0x90 are flagged; the
	// advisor message 0x1C8 (0x71A930, 0x64EA00 -> 0x718820); the grievance and the age give the kind; new(0x2C), 0x5CEFB0(kind, member, +0x270) and 0x435DC0 posts it as the type-0x19 letter,
	// which the tag system files at once (0x436DD0 -> 0x637970 case 0xC -> 0x635D80); then +0x26C = +0x270 = 0 (ThemeParkIncStaff.EndDay)
	// [EXT:tpi-capability-gates] the message 0x1C8 is noted, not posted: the advisor's queue (ThemeParkIncAdvisor) takes the forecasts and the letters so far; the staff senders come with P5
	internal void FileThemeParkIncWageDemands( IEnumerable<ThemeParkIncStaff.WageDemand> demands )
	{
		foreach ( var demand in demands )
		{
			// a member always has a thing in the original (its handle keys the row); one without (a headless economy's) files no row
			if ( demand.Member.ThingId == 0 )
				continue;
			TpiTagTube?.DeleteTagged( ThemeParkIncTagLetter.WageDemandTag, (ushort)demand.Member.ThingId );
			TpiStaffAdvice.Add( ThemeParkIncWageDemandAdvice );
			FileThemeParkIncLetter( ThemeParkIncTagLetter.WageDemand( demand.Type, demand.Member.ThingId, demand.Shortfall, Turn ) );
		}
	}

	/// <summary>A training letter (the finish <c>0x62AB90</c>, the cancel <c>0x62E7A0</c>): the tag system files its row, unread.</summary>
	// [BIN:TPI-EXE:0x62AC4D training letter] new(0x2C), 0x5CEFB0(0xC, member, +0x218) and 0x435DC0 to the broadcaster; [BIN:TPI-EXE:0x62E8D6 suspended letter] 0x5CEFB0(0xD, member, the ability dropped)
	internal void FileThemeParkIncTrainingLetter( ThemeParkIncStaffMember member, bool finished, int ability ) =>
		FileThemeParkIncLetter( ThemeParkIncTagLetter.Training( finished ? ThemeParkIncTagLetter.TrainingFinished : ThemeParkIncTagLetter.TrainingSuspended, member.ThingId, ability, Turn ) );

	/// <summary>The member whose thing is <paramref name="thing"/> (a wage demand row's key, <c>0xAA18B0[+0xE14]</c>); null when no member holds it.</summary>
	internal ThemeParkIncStaffMember? ThemeParkIncMemberOfRow( ThemeParkIncTagTubeRow row ) =>
		row.Source is ThemeParkIncLetterSource.WageDemand or ThemeParkIncLetterSource.Training && row.Key != 0 ? TpiStaff?.Members.FirstOrDefault( member => member.ThingId == row.Key ) : null;

	/// <summary>
	/// The negotiation's end (the results dialog's close, <c>0x559860</c> message 0x14) with the decision <paramref name="result"/> (<see cref="ThemeParkIncWageNegotiation.Decide"/>): a yes
	/// (1 or 2) raises the salary by the offer; a refused offer (0) raises it by the demand when the player accepted the demand, and dismisses the member when he let them leave. The member's demand
	/// rows go with a raise.
	/// </summary>
	// [BIN:TPI-EXE:0x5598C0 negotiation result] +0x10 nonzero: 0x6375C0, 0x64EA60 and 0x636C90(0x90, handle) (the member's demand rows), then 0x62D160(0x62B620() + offer); zero: the panel 0xB28C's toggle
	// (0x85ACF1): 0xB28D sets the offer to the demand (+8 = +0xC), the rows and 0x62D160(salary + demand); 0xB28E runs 0x629040 with the member; another id nothing; every branch clears the member (+4)
	// and the window (+0, 0x502E20)
	internal void EndThemeParkIncNegotiation( ThemeParkIncStaffMember member, int result, int offer, int demand, ThemeParkIncWageNegotiation.Choice choice, Action<ThemeParkIncStaffMember>? destroyThing )
	{
		if ( TpiStaff is not { } staff || !staff.Members.Contains( member ) )
			return;
		var raise = offer;
		if ( result == ThemeParkIncWageNegotiation.Refused )
		{
			if ( choice == ThemeParkIncWageNegotiation.Choice.LetThemLeave )
			{
				DismissThemeParkIncMember( member, destroyThing );
				return;
			}
			if ( choice != ThemeParkIncWageNegotiation.Choice.AcceptDemand )
				return;
			raise = demand;
		}
		TpiTagTube?.DeleteTagged( ThemeParkIncTagLetter.WageDemandTag, (ushort)member.ThingId );
		// [BIN:TPI-EXE:0x62D160 salary setter] the new salary at least MinWage; the review 0x62CDA0 runs only when it changed
		member.SetSalary( staff.Settings, unchecked( member.Salary + raise ), World );
	}

	/// <summary>
	/// The dismissal (<c>0x629040</c>: the wage window's fire button and a refused negotiation's "Let them leave"): the role's strike ends when the member was its only striker, one salary is paid
	/// as severance, the member's rows leave the tag tube, and the member's thing is destroyed (<c>0x637C40</c>): the staff HQ, the research lab and the objects hear message 0x21, then the class's
	/// destroy releases his claims and his cell effect and he leaves the park.
	/// </summary>
	// [BIN:TPI-EXE:0x629040 dismissal] display state (0x62B010) 4: the strikers (display state 4) of the member's kind's list counted; exactly one: 0x634390(the role) (ThemeParkIncStaff.ClearStrike).
	// Then the salary +0x214 through the bank (0x64EBE0 -> 0x5C07B0, the debit) and into the park's wage total (+0x1FA58), 0x636B10(handle) (ThemeParkIncTagTube.DeleteKeyed), 0x60B700(0x13, 0, 0) and
	// 0x637C40 (the destroy)
	// the pick-up tool's reset (0xA434B4's type 6 or 10 swaps in a new 0x4D4A20 tool through 0x4D2480) and the sound 0x13 at the member's position (0x60B700 -> 0x65BD10) are presentation: the pick-up is
	// not ported and the sound bank 0x13 is not loaded by the slice
	internal void DismissThemeParkIncMember( ThemeParkIncStaffMember member, Action<ThemeParkIncStaffMember>? destroyThing )
	{
		if ( TpiStaff is not { } staff || !staff.Members.Contains( member ) )
			return;
		if ( ThemeParkIncStaffWorld.DisplayCategory( member ) == StrikeDisplayState
			&& staff.OfRole( member.Role ).Count( other => ThemeParkIncStaffWorld.DisplayCategory( other ) == StrikeDisplayState ) == 1 )
			staff.ClearStrike( member.Role );
		Post( LedgerCategory.StaffCosts, member.Salary );
		// a member always has a thing in the original (its handle keys the rows); one without (a headless economy's) keys no row
		if ( member.ThingId != 0 )
			TpiTagTube?.DeleteKeyed( (ushort)member.ThingId );
		DestroyThemeParkIncMember( staff, member, destroyThing );
	}

	/// <summary>The guests in the park as the striker's test counts them (<c>0x5B2060(4)</c>: the census, ThemeParkIncMissionController's GuestsInPark).</summary>
	internal int ThemeParkIncGuestsInPark => guestStatistics.GuestCensus;

	/// <summary>The display state of a striker (states 4 and 5, <c>0x62B010</c>).</summary>
	private const int StrikeDisplayState = 4;

	/// <summary>
	/// The destroy of a member's thing (<c>0x637C40</c>): message 0x21 to its listeners in thing order (the staff HQ releases his slot, the research lab his team place or consultancy, an object he
	/// claims its claim), then the class's destroy (the role list's unlink, the claims and the cell effect, a researcher's lab state) and the person's (his cell chain entry and sprite, the thing queued for
	/// destruction).
	/// </summary>
	// [BIN:TPI-EXE:0x637CB9 message 0x21] 0x436040 builds the type-0x21 message with the handle (vtable 0x93A3B8) and 0x436DD0 delivers it at once to the subscribers in handle order: the staff HQ
	// (thing 1, 0x632520 -> 0x634730 for kinds 4, 5, 6, 8, 7, 0x14), the research lab (thing 6, 0x61DAD0) and every attraction (0x5DEF4E: the claim word +0x72 that names the thing goes: "staff member
	// assigned to service me no longer exists", the claimant's 0x628180(0), +0x72 = 0, +0x74 = 0); a guard hears it for his chase (0x5D3480), which is not ported; then the kind's destroy (0x637CD5)
	private void DestroyThemeParkIncMember( ThemeParkIncStaff staff, ThemeParkIncStaffMember member, Action<ThemeParkIncStaffMember>? destroyThing )
	{
		staff.Hq.Release( staff.Members, member );
		// [BIN:TPI-EXE:0x634772 special ride release] the HQ's release 0x634730 calls 0x6241B0 (thunk 0x4082BF) for a member with an ability, so the dismissed member leaves his special-ride slot
		ThemeParkIncReleaseSpecialRides( member );
		TpiResearch?.StaffRemoved( member, staff.Members );
		foreach ( var item in objects.Values.OrderBy( item => item.Id ) )
		{
			if ( item.MechanicId != member.ThingId || member.ThingId == 0 )
				continue;
			item.MechanicId = 0;
			item.ClaimTurn = 0;
		}
		ThemeParkIncStaffWorld.DestroyMember( this, staff, member, Turn );
		destroyThing?.Invoke( member );
	}

	/// <summary>
	/// The staff HQ's month message (<c>0x632C20</c>): every role's strike is considered (a strike ends, a grievance escalates); the messages go to the advisor.
	/// </summary>
	// [BIN:TPI-EXE:0x632C20 strike month] the staff HQ is thing 1 of the prebuilt park, so it hears the month message (0x0C) before the analyser (5), the bank (9) and the members (their reviews change
	// the meters the grievance reads); [EXT:tpi-capability-gates] the messages (type 0x17, 0x435CA0 posted by 0x436DD0) reach only the advisor's listener 0x7176E0, whose speech is not ported: the client
	// notes them
	internal void ConsiderThemeParkIncStrikes()
	{
		if ( TpiStaff is not { } staff )
			return;
		TpiStrikeMessages.AddRange( staff.ConsiderStrikes( Turn ) );
	}
}
