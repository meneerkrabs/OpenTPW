namespace OpenTPW;

/// <summary>
/// The park state of the <c>opentpw.smarter-guests</c> extension (docs/SMARTER-GUESTS.md, "Guest identity for traits" and "Persistence"): the park seed, the counter of birth ids and, per guest,
/// <see cref="Guest.BirthId"/>. It lives beside the guest simulation, never in the guest snapshot and never in <see cref="GuestSimulation.ComputeStateHash"/>; it is saved in its own
/// optional park-resume section (<see cref="SmarterGuestsSave"/>) and joins <see cref="WorldStateHash"/> only while the extension is on.
/// </summary>
// [EXT:smarter-guests] OpenTPW's own extension state; the original game has none
public sealed class SmarterGuestsState
{
	/// <summary>The first birth id: 0 means "no id yet".</summary>
	public const ulong FirstBirthId = 1;

	/// <summary>Mixed into the hash of a park-resume section's guests (the ASCII "SMGR").</summary>
	internal const long HashMarker = 0x534D4752L;

	public SmarterGuestsState( ulong parkSeed ) => ParkSeed = parkSeed;

	/// <summary>The seed the traits derive from: the simulation's seed when the extension started in the park, kept in the save so a resume continues the same personalities.</summary>
	public ulong ParkSeed { get; private set; }

	/// <summary>The next birth id to give; <see cref="ulong.MaxValue"/> means the counter is used up.</summary>
	public ulong NextBirthId { get; private set; } = FirstBirthId;

	/// <summary>
	/// The extension's first tick has run: guests that existed before it have their ids. In practice those are the guests of an imported original save (and of tests that restore a guest snapshot without
	/// the section); a park saved by this extension carries its ids, and a park of another build is refused at load. From then on every guest has one from its creation.
	/// </summary>
	public bool Bootstrapped { get; private set; }

	/// <summary>
	/// The runs of price-emptied decisions of the value-for-money rule (<see cref="PriceStalls"/>): empty unless the smarter rules run. The host writes and reads it; it is saved in the extension's section and
	/// joins the world hash only while it holds an entry, so a park that only has the guest identity (script mods) hashes as it did.
	/// </summary>
	public PriceStalls Stalls { get; } = new();

	/// <summary>
	/// The observed throughput of each attraction instance (<see cref="RideThroughputs"/>, phase 3): empty until the spreading rules run and an attraction has boarded a guest. The host writes and reads it; it is saved in
	/// the extension's section and joins the world hash only while it holds an entry, so a park that only has the guest identity (script mods) hashes as it did.
	/// </summary>
	public RideThroughputs Throughputs { get; } = new();

	/// <summary>
	/// The turn of each guest's latest meal (<see cref="MealTimes"/>, phase 5): empty until the planning rules run and a guest eats. The host writes and reads it; it is saved in the extension's section and joins
	/// the world hash only while it holds an entry, so a park that only has the guest identity (script mods) hashes as it did.
	/// </summary>
	public MealTimes Meals { get; } = new();

	/// <summary>
	/// The park turn of each guest's latest complaint about its path (phase 6; the same (birth id, turn) bookkeeping as <see cref="Meals"/>, so its <see cref="MealTime"/> entries read "complained on turn"): empty until
	/// the path rules run and a guest complains. The host writes and reads it; it is saved in the extension's section and joins the world hash only while it holds an entry.
	/// </summary>
	public MealTimes Complaints { get; } = new();

	/// <summary>Gives the next birth id. A counter at its limit is refused, never wrapped: ids are never reused, so a wrap would give two guests one personality.</summary>
	public ulong AllocateBirthId()
	{
		if ( NextBirthId == ulong.MaxValue )
			throw new OverflowException( "The smarter-guests birth id counter is used up; it is not wrapped." );
		return NextBirthId++;
	}

	/// <summary>
	/// The first tick of the extension: every guest without an id gets one, in the order of <paramref name="inThingListOrder"/> (the park's thing list, newest first, then any guest outside it by
	/// id), so the same park gives the same ids every time. A guest that has one keeps it. Does nothing once done.
	/// </summary>
	internal void Bootstrap( IEnumerable<Guest> inThingListOrder )
	{
		if ( Bootstrapped )
			return;
		foreach ( var guest in inThingListOrder )
			if ( guest.BirthId == 0 )
				guest.BirthId = AllocateBirthId();
		Bootstrapped = true;
	}

	/// <summary>Replaces the state with a saved one (<see cref="SmarterGuestsSave.Restore"/>, after its validation).</summary>
	internal void Restore( ulong parkSeed, ulong nextBirthId, bool bootstrapped )
	{
		ParkSeed = parkSeed;
		NextBirthId = nextBirthId;
		Bootstrapped = bootstrapped;
		// a restore from a section loads the runs afterwards (SmarterGuestsSave.Restore); any other restore starts without any
		Stalls.Clear();
		Throughputs.Clear();
		Meals.Clear();
		Complaints.Clear();
	}

	/// <summary>
	/// A stateless trait of a guest: a well-mixed 64-bit hash of the park seed, the guest's birth id and the trait's index. It depends on nothing else, so a guest's personality never changes
	/// during its visit, in a resumed park or on another machine, and drawing it takes no random number.
	/// </summary>
	// [EXT:smarter-guests] the mix is SplitMix64's finalizer, applied once per input so each of the three words reaches every output bit
	public static ulong Trait( ulong parkSeed, ulong birthId, int traitIndex )
	{
		var state = Mix( parkSeed + 0x9E3779B97F4A7C15UL );
		state = Mix( state ^ Mix( birthId + 0xD1B54A32D192ED03UL ) );
		return Mix( state ^ Mix( (ulong)(uint)traitIndex + 0x8CB92BA72F3D8DD7UL ) );
	}

	/// <summary><see cref="Trait(ulong, ulong, int)"/> with this park's seed.</summary>
	public ulong Trait( ulong birthId, int traitIndex ) => Trait( ParkSeed, birthId, traitIndex );

	internal static ulong Mix( ulong value )
	{
		value += 0x9E3779B97F4A7C15UL;
		value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
		value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
		return value ^ (value >> 31);
	}

	/// <summary>Mixed into the hash before the price stalls (the ASCII "PSTL").</summary>
	internal const long StallsHashMarker = 0x5053544CL;

	/// <summary>The extension's part of <see cref="WorldStateHash"/>: the seed, the counter, the first-tick flag, the guests' ids in id order and, while there are any, the runs of price-emptied decisions in birth-id order, the throughput estimates in attraction-id order the meal times and the complaint times in birth-id order.</summary>
	internal void AddCanonicalState( StateHasher hash, IReadOnlyList<Guest> guests )
	{
		hash.Add( HashMarker );
		hash.Add( ParkSeed );
		hash.Add( NextBirthId );
		hash.Add( Bootstrapped );
		var withIds = guests.Where( guest => guest.BirthId != 0 ).OrderBy( guest => guest.Id ).ToArray();
		hash.Add( withIds.Length );
		foreach ( var guest in withIds )
		{
			hash.Add( guest.Id );
			hash.Add( guest.BirthId );
		}
		if ( Stalls.Count != 0 )
		{
			hash.Add( StallsHashMarker );
			hash.Add( Stalls.Count );
			foreach ( var entry in Stalls.Entries )
			{
				hash.Add( entry.BirthId );
				hash.Add( entry.Count );
			}
		}
		if ( Throughputs.Count != 0 )
		{
			hash.Add( ThroughputHashMarker );
			hash.Add( Throughputs.Count );
			foreach ( var entry in Throughputs.Entries )
			{
				hash.Add( entry.AttractionId );
				hash.Add( entry.Capacity );
				hash.Add( entry.Speed );
				hash.Add( entry.Interval256 );
				hash.Add( entry.Samples );
				hash.Add( entry.LastBoardTurn );
				hash.Add( entry.ClockTurn );
				hash.Add( entry.Demand );
			}
		}
		if ( Meals.Count != 0 )
		{
			hash.Add( MealsHashMarker );
			hash.Add( Meals.Count );
			foreach ( var entry in Meals.Entries )
			{
				hash.Add( entry.BirthId );
				hash.Add( entry.Turn );
			}
		}
		if ( Complaints.Count == 0 )
			return;
		hash.Add( ComplaintsHashMarker );
		hash.Add( Complaints.Count );
		foreach ( var entry in Complaints.Entries )
		{
			hash.Add( entry.BirthId );
			hash.Add( entry.Turn );
		}
	}

	/// <summary>Mixed into the hash before the complaint times (the ASCII "CMPL").</summary>
	internal const long ComplaintsHashMarker = 0x434D504CL;

	/// <summary>Mixed into the hash before the meal times (the ASCII "MEAL").</summary>
	internal const long MealsHashMarker = 0x4D45414CL;

	/// <summary>Mixed into the hash before the throughput estimates (the ASCII "THRP").</summary>
	internal const long ThroughputHashMarker = 0x54485250L;
}
