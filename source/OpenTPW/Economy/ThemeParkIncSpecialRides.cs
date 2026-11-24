namespace OpenTPW;

/// <summary>One staff slot of a special ride's record (12 bytes in memory): the staff type code it asks for, the ability (1..14) and the staff thing that fills it (0 = empty).</summary>
public readonly record struct ThemeParkIncSpecialRideStaffSlot( int StaffType, int Ability, ushort Thing )
{
	/// <summary>The staff type of a slot that asks for nothing (the builder's padding and the end of the used slots).</summary>
	public const int NoRequirement = 7;
}

/// <summary>
/// One record of the special-ride table (<c>0xAA0330 + 0xF4 × i</c>): the special ride (its economy instance; the original's <c>SpecialRideThingId</c>, <c>+0xF0</c>) and the 20 staff slots its
/// <c>SkillSet[n].Num</c> asks for. An instance of 0 is a record whose ride was activated (<c>0x624280</c>) or that never had one.
/// </summary>
public sealed record ThemeParkIncSpecialRideRecord( int Instance, ThemeParkIncSpecialRideStaffSlot[] Slots );

/// <summary>The special-ride table and the activated rides as a park save keeps them: the records in table order and the activated instances in ascending order.</summary>
public sealed record ThemeParkIncSpecialRidesSnapshot( ThemeParkIncSpecialRideRecord[] Records, int[] Activated );

/// <summary>
/// Theme Park Inc's special rides (docs/reverse/TPI-CHALLENGE-SYSTEM.md §11): the table of the rides a player must activate before guests may use them (<c>0xAA0330</c>, its count
/// <c>0xAA071C</c>), the staff a ride's activation needs and who fills each slot, and the rides' activated flag (the attraction's <c>mBeenActivated</c>, <c>+0x12C</c>). A special ride
/// (<c>Info.IsSpecial</c> 1) gets its record when it is placed; a trained member takes a slot that asks for his type and ability when his training ends and leaves it when he goes to train
/// again; the activation spends the ride's <c>UsageInfo.GoldenTicketCost</c> and empties the record, which makes the ride an ordinary one. Nothing here draws a random number.
/// </summary>
// [BIN:TPI-EXE:0x6244B0 special rides table] the World's tail holds mNumSpecialRides and per record SpecialRideThingId and 20 × (AllSpecialRidesThingId, AllSpecialRidesAbility, AllSpecialRidesType):
// in memory the slot is +0 type, +4 ability, +8 thing (the loader's stores at 0x6245A6, 0x6245F9 and 0x62463B)
public sealed class ThemeParkIncSpecialRides
{
	/// <summary>The slots of a record.</summary>
	public const int SlotCount = 20;

	/// <summary>The records the table holds before its own count word (<c>0xAA071C</c> lies inside the fifth record).</summary>
	public const int Capacity = 4;

	/// <summary>The <c>SkillSet[n].Num</c> entries of a type record (<c>+0x380 + 4 × n</c>), n = 0..13 for the abilities 1..14.</summary>
	public const int SkillSets = 14;

	private readonly List<ThemeParkIncSpecialRideRecord> records = new();
	private readonly SortedSet<int> activated = new();

	/// <summary>The records in table order.</summary>
	public IReadOnlyList<ThemeParkIncSpecialRideRecord> Records => records;

	/// <summary>The instances of the activated special rides (<c>+0x12C</c> set).</summary>
	public IReadOnlyCollection<int> Activated => activated;

	/// <summary>The staff type code that learns an ability (<c>0x62B640</c>): 1..6 the mechanic's (1), 7 and 8 the handyman's (0), 9 the guard's (3), 10..14 the gardener's (5), anything else 7.</summary>
	// [BIN:TPI-EXE:0x62B640 ability's staff type] abilities above 15 answer 7; else the byte table 0x62B688 (0, 1, 1, 1, 1, 1, 1, 2, 2, 3, 4, 4, 4, 4, 4, 0) picks of the targets 0x62B674: 7, 1, 0, 3, 5
	public static int StaffTypeOf( int ability ) => ability switch
	{
		>= 1 and <= 6 => 1,
		7 or 8 => 0,
		9 => 3,
		>= 10 and <= 14 => 5,
		_ => ThemeParkIncSpecialRideStaffSlot.NoRequirement
	};

	/// <summary>
	/// A special ride placed (<c>0x623D80</c>): a record is appended and built (<c>0x623DC0</c>): for each <c>SkillSet[n].Num</c> above 0 that many slots asking for ability n + 1 and its staff
	/// type, the rest up to 20 asking for nothing.
	/// </summary>
	// [BIN:TPI-EXE:0x5DBD7B special ride record] the attraction placement appends a record when the type's +0x5C (Info.IsSpecial) is exactly 1
	// [BIN:TPI-EXE:0x623DC0 record builder] +0xF0 = the thing id (0 ends here); for n = 0..13 the dword +0x380 + 4n of the type record (SkillSet[n].Num), when above 0 (signed jle), appends that many
	// {0x62B640(n + 1), n + 1, 0}; then {7, 0, 0} while fewer than 20 (0x623E6E jge: more than 20 appended slots would write on, which no shipped ride asks for)
	public void Add( int instance, IReadOnlyList<int> skillSet )
	{
		ArgumentNullException.ThrowIfNull( skillSet );
		if ( records.Count >= Capacity )
			throw new InvalidOperationException( $"A Theme Park Inc park holds at most {Capacity} special rides (the original's table would overwrite its own count)." );
		var slots = new List<ThemeParkIncSpecialRideStaffSlot>();
		for ( var set = 0; set < Math.Min( SkillSets, skillSet.Count ); set++ )
		{
			for ( var count = 0; count < skillSet[set]; count++ )
				slots.Add( new ThemeParkIncSpecialRideStaffSlot( StaffTypeOf( set + 1 ), set + 1, 0 ) );
		}
		if ( slots.Count > SlotCount )
			throw new InvalidDataException( $"Special ride {instance} asks for {slots.Count} staff, more than the {SlotCount} slots of its record (the original would write past it)." );
		while ( slots.Count < SlotCount )
			slots.Add( new ThemeParkIncSpecialRideStaffSlot( ThemeParkIncSpecialRideStaffSlot.NoRequirement, 0, 0 ) );
		records.Add( new ThemeParkIncSpecialRideRecord( instance, slots.ToArray() ) );
	}

	/// <summary>The first record of <paramref name="instance"/>, null for none (<c>0x623EE0</c>).</summary>
	// [BIN:TPI-EXE:0x623EE0 find record] the records 0..count-1 by their word +0xF0; the first equal one
	public ThemeParkIncSpecialRideRecord? Find( int instance ) => instance == 0 ? null : records.FirstOrDefault( record => record.Instance == instance );

	/// <summary>Whether a ride is free to use: no record names it (<c>0x6242F0</c>). An unactivated special ride is not.</summary>
	// [BIN:TPI-EXE:0x6242F0 not pending] false when a record's +0xF0 is the thing id, else true; the object click (0x5036DC) opens the activation window instead of the ride window when it is false
	public bool IsFree( int instance ) => Find( instance ) == null;

	/// <summary>Whether a special ride has been activated (<c>0x5E8210</c>: <c>+0x12C</c>).</summary>
	public bool IsActivated( int instance ) => activated.Contains( instance );

	/// <summary>
	/// The used slots of a record: the slots before the first that asks for nothing (the activation's and the window's walks stop there).
	/// </summary>
	private static IEnumerable<ThemeParkIncSpecialRideStaffSlot> UsedSlots( ThemeParkIncSpecialRideRecord record ) =>
		record.Slots.TakeWhile( slot => slot.StaffType != ThemeParkIncSpecialRideStaffSlot.NoRequirement );

	/// <summary>
	/// Whether the activation's button is enabled (<c>0x6243C0</c>): the ride has a record, the career holds its golden-ticket cost and every used slot of the record is filled.
	/// </summary>
	// [BIN:TPI-EXE:0x6243C0 can activate] the debug toggle 0xAA0720 (the debug menu's 0x537879, 0 in play) answers true; no record false; the career's tickets (0x5F05E0, +0x1074) below the type's
	// +0x174 (UsageInfo.GoldenTicketCost, signed jl) false; then the slots up to the first of type 7: true when every one has its word +8 set
	public bool CanActivate( int instance, int tickets, int cost ) =>
		Find( instance ) is { } record && tickets >= cost && UsedSlots( record ).All( slot => slot.Thing != 0 );

	/// <summary>Whether the career has discovered every ability a ride's record asks for (<c>0x624330</c>); a ride without a record answers true.</summary>
	// [BIN:TPI-EXE:0x624330 abilities known] the records whose +0xF0 is the thing id: each slot up to the first of type 7 asks 0x5F0370(+4), the career's +0x1064[ability]; the first unknown answers false;
	// a type-7 slot answers true at once. The window shows UITEXT 115 in control 0x4E2B when it is false (0x58673C)
	public bool AbilitiesDiscovered( int instance, IReadOnlyList<byte> discovered )
	{
		if ( Find( instance ) is not { } record )
			return true;
		foreach ( var slot in UsedSlots( record ) )
		{
			if ( slot.Ability < 0 || slot.Ability >= discovered.Count || discovered[slot.Ability] == 0 )
				return false;
		}
		return true;
	}

	/// <summary>
	/// Message 0x2B's second half (<c>0x623FB0</c>, after the staff HQ's assign): a member whose training finished takes the first empty slot, in table order, whose staff type is his and whose
	/// ability is his trained one, of a special ride that stands on owned land. The ride whose slot he took, or null; whether it can now be activated is the caller's (the advice <c>0x21F</c>/<c>0x1E7</c>).
	/// </summary>
	// [BIN:TPI-EXE:0x623FB0 assign] for each record with +0xF0 != 0 whose thing's square (bytes +5, +7) lacks flag 0x40 (0x67ECA0, unowned land): each slot with word +8 == 0, +0 == 0x62AF70(member) (the
	// staff type code) and 0x62B7D0(+4) (the member's +0x218 is the ability) takes his id and the walk ends (0x6240A6); a member already in another slot is not looked for
	public int? Assign( ThemeParkIncStaffMember member, Func<int, bool> onUnownedLand )
	{
		ArgumentNullException.ThrowIfNull( member );
		ArgumentNullException.ThrowIfNull( onUnownedLand );
		var type = ThemeParkIncStaffHeadquarters.StaffTypeCode( member.Role );
		foreach ( var record in records )
		{
			if ( record.Instance == 0 || onUnownedLand( record.Instance ) )
				continue;
			for ( var index = 0; index < SlotCount; index++ )
			{
				var slot = record.Slots[index];
				if ( slot.Thing == 0 && slot.StaffType == type && slot.Ability == member.Ability )
				{
					record.Slots[index] = slot with { Thing = (ushort)member.ThingId };
					return record.Instance;
				}
			}
		}
		return null;
	}

	/// <summary>
	/// The staff HQ's release (<c>0x634730</c>, a member with an ability leaving for training or the park): the first slot, in table order, that holds him in the record of a special ride on owned land is
	/// emptied. True when one was.
	/// </summary>
	// [BIN:TPI-EXE:0x6241B0 unassign] for each record with +0xF0 != 0 whose thing's square lacks flag 0x40: the first of the 20 slots whose word +8 is the id is cleared and the walk ends; the type and the
	// ability of the slot are not compared
	public bool Release( ThemeParkIncStaffMember member, Func<int, bool> onUnownedLand )
	{
		ArgumentNullException.ThrowIfNull( member );
		ArgumentNullException.ThrowIfNull( onUnownedLand );
		var thing = (ushort)member.ThingId;
		foreach ( var record in records )
		{
			if ( record.Instance == 0 || onUnownedLand( record.Instance ) )
				continue;
			for ( var index = 0; index < SlotCount; index++ )
			{
				if ( record.Slots[index].Thing == thing )
				{
					record.Slots[index] = record.Slots[index] with { Thing = 0 };
					return true;
				}
			}
		}
		return false;
	}

	/// <summary>
	/// The activation's table and flag steps (<c>0x5E8230</c>): the ride's <c>mHasBeenOpenedAtSomePoint</c> and <c>mBeenActivated</c> are set and every record that names it is emptied
	/// (<c>0x624280</c>), so the ride is free to use. The golden tickets are the caller's.
	/// </summary>
	// [BIN:TPI-EXE:0x5E8910 activated flags] +0x12D = 1 and +0x12C = 1; [BIN:TPI-EXE:0x624280 record emptied] every record whose +0xF0 is the thing id is rebuilt with id 0 (0x623DC0(0) stores the 0
	// and returns before the slots), so its slots keep their members and the count keeps the record
	public void Activate( int instance )
	{
		activated.Add( instance );
		for ( var index = 0; index < records.Count; index++ )
		{
			if ( records[index].Instance == instance )
				records[index] = records[index] with { Instance = 0 };
		}
	}

	internal ThemeParkIncSpecialRidesSnapshot Capture() =>
		new( records.Select( record => record with { Slots = (ThemeParkIncSpecialRideStaffSlot[])record.Slots.Clone() } ).ToArray(), activated.ToArray() );

	/// <summary>
	/// Whether a saved table can be restored: at most four records of 20 slots, the named rides distinct, the activated rides distinct and positive. A record may name a ride sold since (the original
	/// keeps the record of a destroyed thing; OpenTPW's instance ids are never handed out again).
	/// </summary>
	internal static bool IsValid( ThemeParkIncSpecialRidesSnapshot? state ) =>
		state == null || state.Records is { Length: <= Capacity } saved && state.Activated != null
		&& saved.All( record => record != null && record.Instance >= 0 && record.Slots is { Length: SlotCount } )
		&& state.Activated.All( instance => instance > 0 ) && state.Activated.Distinct().Count() == state.Activated.Length
		&& saved.Where( record => record.Instance != 0 ).Select( record => record.Instance ).Distinct().Count() == saved.Count( record => record.Instance != 0 );

	internal void Restore( ThemeParkIncSpecialRidesSnapshot state )
	{
		if ( !IsValid( state ) )
			throw new InvalidDataException( "Invalid Theme Park Inc special rides snapshot." );
		records.Clear();
		records.AddRange( state.Records.Select( record => record with { Slots = (ThemeParkIncSpecialRideStaffSlot[])record.Slots.Clone() } ) );
		activated.Clear();
		activated.UnionWith( state.Activated );
	}

	/// <summary>
	/// Takes a Theme Park Inc park file's table and flags (the prebuilt Science park): each saved record, its ride's saved thing mapped to its economy instance (a record of thing 0 stays empty), its slots
	/// as saved; and every imported attraction whose <c>mBeenActivated</c> is set.
	/// </summary>
	// [BIN:TPI-EXE:0x624701 special rides load] the loader reads SpecialRideThingId, rebuilds the record from its type (0x623DC0) and then overwrites all 20 slots with the saved ones, so the saved slots stand
	internal void Import( IReadOnlyList<(ushort Thing, IReadOnlyList<ThemeParkIncSpecialRideStaffSlot> Slots)> saved, IReadOnlyCollection<int> activatedInstances, IReadOnlyDictionary<int, int> instances )
	{
		if ( saved.Count > Capacity )
			throw new InvalidDataException( $"The park file holds {saved.Count} special rides; the table holds {Capacity}." );
		records.Clear();
		foreach ( var (thing, slots) in saved )
		{
			if ( slots.Count != SlotCount )
				throw new InvalidDataException( $"A saved special ride record holds {slots.Count} slots, not {SlotCount}." );
			var instance = 0;
			if ( thing != 0 && !instances.TryGetValue( thing, out instance ) )
				throw new InvalidDataException( $"The park file's special ride thing {thing} is no imported object." );
			records.Add( new ThemeParkIncSpecialRideRecord( instance, slots.ToArray() ) );
		}
		activated.Clear();
		activated.UnionWith( activatedInstances );
	}
}
