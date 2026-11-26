namespace OpenTPW;

/// <summary>One of the staff HQ's obstacle slots (<c>+0x14 + 12 × i</c>): the staff type code the mission asks for, the ability and the staff thing that fills it (0 = empty).</summary>
public readonly record struct ThemeParkIncHqSlot( int StaffType, int Ability, ushort Thing );

/// <summary>The staff HQ's obstacle slots and trained-staff counts as a park save keeps them: 20 slots of (staff type, ability, thing) and 15 counts.</summary>
public sealed record ThemeParkIncStaffHqSnapshot( int[] Slots, int[] TrainedStaff );

/// <summary>
/// Theme Park Inc's staff HQ thing (class 9, <c>park+0x1EAB4C</c>; docs/reverse/TPI-STAFF.md §12): the 20 obstacle slots the mission's <c>ObstacleStaff</c> requirements fill, the count of trained staff per
/// ability, and the slot work its messages do (0x21 a staff thing destroyed and 0x2A a member gone to train release, 0x2B a training finished assigns, 0x23 the obstacle researched and 0x24 the obstacle
/// removed rebuild). The HQ's strike records are <see cref="ThemeParkIncStaff"/>'s.
/// </summary>
// [BIN:TPI-EXE:0x632270 staff HQ constructor] the thing base (0x637100(1, 9)), subscriptions 0x0C, 0x0E, 0x2A, 0x2B, 0x21, 0x23, 0x24 (0x6322C8..0x632333), then the slot table 0x634600
public sealed class ThemeParkIncStaffHeadquarters
{
	/// <summary>The slots (<c>+0x14</c>, 20 × 12 bytes).</summary>
	public const int SlotCount = 20;

	/// <summary>The staff type of a slot no mission requirement fills (the table's padding).</summary>
	public const int NoRequirement = 7;

	private readonly ThemeParkIncHqSlot[] slots = new ThemeParkIncHqSlot[SlotCount];
	private readonly int[] trained = new int[ThemeParkIncCareer.AbilityCount];

	public ThemeParkIncStaffHeadquarters()
	{
		for ( var index = 0; index < SlotCount; index++ )
			slots[index] = new ThemeParkIncHqSlot( NoRequirement, 0, 0 );
	}

	public IReadOnlyList<ThemeParkIncHqSlot> Slots => slots;

	/// <summary><c>+0x104</c>: the trained staff per ability (<c>mTrainedStaff</c>).</summary>
	public IReadOnlyList<int> TrainedStaff => trained;

	/// <summary>The staff type code of a member's role (<c>0x62AF70</c>): handyman 0, mechanic 1, entertainer 2, guard 3, researcher 4, gardener 5, the role order itself.</summary>
	// [BIN:TPI-EXE:0x62AF70 staff type code] kind byte +2 minus 4 through the byte table 0x62AFD0 (0, 1, 2, 3, 4, then 6 for 9..0x13, 5 for 0x14) and the targets 0x62AFB4: kind 4 (mechanic) 1,
	// 5 (handyman) 0, 6 (entertainer) 2, 7 (guard) 3, 8 (researcher) 4, 0x14 (gardener) 5, any other 6 (r7-obstacles-hq §8.1 had the guard and the entertainer swapped)
	public static int StaffTypeCode( ThemeParkIncStaffRole role ) => (int)role;

	/// <summary>
	/// Whether every slot asks for nothing (staff type 7) or holds a staff thing: the slot part of the mission window's MetRequirements (<c>0x55A620</c>).
	/// </summary>
	// [BIN:TPI-EXE:0x55A6D5 slots filled] any of the 20 slots with +0 != 7 and word +8 == 0 fails ("some slots not filled", 0x9BEE74)
	public bool AllFilled => slots.All( slot => slot.StaffType == NoRequirement || slot.Thing != 0 );

	/// <summary>
	/// The slot table (<c>0x634600</c>): each of the mission's five <c>ObstacleStaff</c> records appends <c>Number</c> slots of its staff type and ability, empty; the rest up to 20 ask for nothing.
	/// </summary>
	// [BIN:TPI-EXE:0x634600 slot table] the career's mission (0x42A3B0 + 0x10B4): for the five records at +0x2E0 (StaffType), +0x2E4 (Ability), +0x2E8 (Number), stride 12, whatever the count dword
	// +0x31C, a Number above 0 (signed) appends that many {StaffType, Ability, 0}; then {7, 0, 0} up to 20. The appending has no bound: more than 20 required staff would write past the table, which
	// no shipped mission asks for (two at most)
	public void BuildSlots( ThemeParkIncMission mission )
	{
		var next = 0;
		for ( var record = 0; record < ThemeParkIncMission.ObstacleStaffCapacity; record++ )
		{
			var staff = mission.ObstacleStaff( record );
			for ( var count = 0; count < staff.Number; count++ )
			{
				if ( next >= SlotCount )
					throw new InvalidDataException( $"The mission's obstacle staff need more than the staff HQ's {SlotCount} slots (the original would write past its table)." );
				slots[next++] = new ThemeParkIncHqSlot( staff.StaffType, staff.Ability, 0 );
			}
		}
		for ( ; next < SlotCount; next++ )
			slots[next] = new ThemeParkIncHqSlot( NoRequirement, 0, 0 );
	}

	/// <summary>Whether a staff thing fills any slot (<c>0x55E3B0</c>).</summary>
	// [BIN:TPI-EXE:0x55E3B0 thing in table] the 20 words at +8, stride 12, compared with the thing id
	private bool InTable( ushort thing ) => slots.Any( slot => slot.Thing == thing );

	/// <summary>
	/// Assign (<c>0x634B00</c>): a member in no slot takes the first empty slot whose ability is his trained ability; the slot's staff type is not compared. True when he took one.
	/// </summary>
	// [BIN:TPI-EXE:0x634B00 assign] the member's slot index (0x62B9E0, +0x228) must be -1; the first slot (of 20) with +4 == the member's ability (0x62B8E0, +0x218) and word +8 == 0 takes the id and the
	// member's index becomes that slot (0x62BA00); 0x62AF70 is called there and its answer dropped (0x634B50), so the staff type is not compared
	public bool Assign( ThemeParkIncStaffMember member )
	{
		if ( member.HqSlot != -1 )
			return false;
		for ( var index = 0; index < SlotCount; index++ )
		{
			if ( slots[index].Ability == member.Ability && slots[index].Thing == 0 )
			{
				slots[index] = slots[index] with { Thing = (ushort)member.ThingId };
				member.HqSlot = index;
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// The first member of <paramref name="role"/> in the members' list order (newest first, as each role's list) with <paramref name="ability"/> who fills no slot (the finders <c>0x6348C0</c>, <c>0x634980</c>, <c>0x634A40</c>); null for none.
	/// </summary>
	// [BIN:TPI-EXE:0x6348C0 gardener finder] the list from park+0x1EAB7E by +0x5D0 (mechanics 0x634980 from +0x1EAB80, handymen 0x634A40 from +0x1EAB7C): the first whose +0x218 == the ability and, with a
	// table, whose id 0x55E3B0 does not find in it; none answers 0
	private ThemeParkIncStaffMember? Find( IReadOnlyList<ThemeParkIncStaffMember> members, ThemeParkIncStaffRole role, int ability ) =>
		members.FirstOrDefault( member => member.Role == role && member.Ability == ability && !InTable( (ushort)member.ThingId ) );

	/// <summary>Whether the release and the rebuild clear a member's slot: a gardener, a handyman or a mechanic.</summary>
	private static bool HasSlotFinder( ThemeParkIncStaffRole role ) => role is ThemeParkIncStaffRole.Gardener or ThemeParkIncStaffRole.Handyman or ThemeParkIncStaffRole.Mechanic;

	/// <summary>Clears a gardener's, a handyman's or a mechanic's slot (the one his index names) and his index.</summary>
	// [BIN:TPI-EXE:0x634807 clear member slot] the member's index in 0..19 and his kind 0x14, 5 or 4: word +8 of the slot at his index = 0 and his index = -1 (0x62BA00(-1)); any other member keeps both
	private void ClearSlotOf( ThemeParkIncStaffMember member )
	{
		if ( member.HqSlot is < 0 or >= SlotCount || !HasSlotFinder( member.Role ) )
			return;
		slots[member.HqSlot] = slots[member.HqSlot] with { Thing = 0 };
		member.HqSlot = -1;
	}

	/// <summary>
	/// Release (<c>0x634730</c>, messages 0x21 for a staff thing and 0x2A): the member's ability count falls; a member with an ability who fills a slot leaves it, and a gardener, handyman or mechanic
	/// of his role with the same ability who fills none takes it.
	/// </summary>
	// [BIN:TPI-EXE:0x634730 release] count[+0x218]-- whatever the ability; ability 0 returns; 0x6241B0(id) (the special rides' staff slots, ParkEconomy.ThemeParkIncReleaseSpecialRides); then the id's slot: none returns;
	// a replacement by the member's kind (0x14 the gardener finder, 5 the handyman's, 4 the mechanic's) with his ability, the table given, so neither he nor another slotted member is picked; then his
	// own slot is cleared (0x634807) and a replacement found is assigned (0x634B00)
	public void Release( IReadOnlyList<ThemeParkIncStaffMember> members, ThemeParkIncStaffMember member )
	{
		CountAbility( member.Ability, -1 );
		if ( member.Ability == 0 )
			return;
		if ( !slots.Any( slot => slot.Thing == (ushort)member.ThingId ) )
			return;
		var replacement = HasSlotFinder( member.Role ) ? Find( members, member.Role, member.Ability ) : null;
		ClearSlotOf( member );
		if ( replacement != null )
			Assign( replacement );
	}

	/// <summary>Message 0x2B, a member's training finished: his new ability's count rises and he takes a slot that asks for it.</summary>
	// [BIN:TPI-EXE:0x63256F training finished] count[+0x218]++, 0x634B00(id), then 0x623FB0(id), the special rides' staff slots (ParkEconomy.ThemeParkIncTrainingFinishedSpecialRides)
	public void TrainingFinished( ThemeParkIncStaffMember member )
	{
		CountAbility( member.Ability, +1 );
		Assign( member );
	}

	// [BIN:TPI-EXE:0x63475A ability count] the dword +0x104 + 4 × ability, the ability unchecked: abilities 0..14 are the only values training writes
	private void CountAbility( int ability, int delta )
	{
		if ( ability is < 0 or >= ThemeParkIncCareer.AbilityCount )
			throw new InvalidOperationException( $"Staff ability {ability} is outside the HQ's {ThemeParkIncCareer.AbilityCount} trained-staff counts." );
		trained[ability] = unchecked( trained[ability] + delta );
	}

	/// <summary>
	/// Rebuild (<c>0x634D20</c>, messages 0x23 and 0x24): every slotted gardener, handyman and mechanic leaves his slot, the table is built again from the mission and, for an obstacle mission, each
	/// required member is found by the staff type's finder (0 handyman, 1 mechanic, 5 gardener) and assigned.
	/// </summary>
	// [BIN:TPI-EXE:0x634D20 rebuild] for each slot with a thing: 0x634807's clear; then 0x634600; then when HasObstacle (0x5F0190) for the five ObstacleStaff records and Number times each: StaffType 0 the
	// handyman finder, 1 the mechanic finder, 5 the gardener finder, with the Ability and the table; another type keeps the last answer (the word starts 0 from the slot loop's counter); a nonzero answer
	// is assigned (0x634B00, which refuses a member already slotted)
	public void Rebuild( IReadOnlyList<ThemeParkIncStaffMember> members, ThemeParkIncCareer career )
	{
		foreach ( var slot in slots.ToArray() )
		{
			if ( slot.Thing != 0 && members.FirstOrDefault( member => member.ThingId == slot.Thing ) is { } member )
				ClearSlotOf( member );
		}
		BuildSlots( career.Mission );
		if ( !career.HasObstacle )
			return;
		ThemeParkIncStaffMember? found = null;
		for ( var record = 0; record < ThemeParkIncMission.ObstacleStaffCapacity; record++ )
		{
			var required = career.Mission.ObstacleStaff( record );
			for ( var count = 0; count < required.Number; count++ )
			{
				found = required.StaffType switch
				{
					0 => Find( members, ThemeParkIncStaffRole.Handyman, required.Ability ),
					1 => Find( members, ThemeParkIncStaffRole.Mechanic, required.Ability ),
					5 => Find( members, ThemeParkIncStaffRole.Gardener, required.Ability ),
					_ => found
				};
				if ( found != null )
					Assign( found );
			}
		}
	}

	internal ThemeParkIncStaffHqSnapshot Capture() => new( slots.SelectMany( slot => new[] { slot.StaffType, slot.Ability, (int)slot.Thing } ).ToArray(), (int[])trained.Clone() );

	internal static bool IsValid( ThemeParkIncStaffHqSnapshot? state ) => state == null || state.Slots is { Length: SlotCount * 3 } saved && state.TrainedStaff is { Length: ThemeParkIncCareer.AbilityCount }
		&& Enumerable.Range( 0, SlotCount ).All( index => saved[3 * index + 2] is >= 0 and <= ushort.MaxValue );

	internal void Restore( ThemeParkIncStaffHqSnapshot state )
	{
		if ( !IsValid( state ) )
			throw new InvalidDataException( "Invalid Theme Park Inc staff HQ snapshot." );
		for ( var index = 0; index < SlotCount; index++ )
			slots[index] = new ThemeParkIncHqSlot( state.Slots[3 * index], state.Slots[3 * index + 1], (ushort)state.Slots[3 * index + 2] );
		state.TrainedStaff.CopyTo( trained, 0 );
	}

	/// <summary>Takes the slots and counts of a Theme Park Inc park file's staff HQ (<c>mStaffSlots</c>, <c>mTrainedStaff</c>).</summary>
	// [BIN:TPI-EXE:0x6326B0 staff HQ load] the 20 slots of 12 bytes (u32 type, u32 ability, u16 thing, two bytes of padding) and the 15 trained-staff dwords
	internal void Import( IReadOnlyList<byte[]> savedSlots, IReadOnlyList<uint> savedTrained )
	{
		if ( savedSlots.Count != SlotCount || savedTrained.Count != ThemeParkIncCareer.AbilityCount || savedSlots.Any( slot => slot.Length != 12 ) )
			throw new InvalidDataException( "The park file's staff HQ has no 20 slots and 15 trained-staff counts." );
		for ( var index = 0; index < SlotCount; index++ )
		{
			var slot = savedSlots[index];
			slots[index] = new ThemeParkIncHqSlot( BitConverter.ToInt32( slot, 0 ), BitConverter.ToInt32( slot, 4 ), BitConverter.ToUInt16( slot, 8 ) );
		}
		for ( var ability = 0; ability < ThemeParkIncCareer.AbilityCount; ability++ )
			trained[ability] = unchecked( (int)savedTrained[ability] );
	}
}
