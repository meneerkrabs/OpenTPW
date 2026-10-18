namespace OpenTPW;

/// <summary>
/// The rules of phases 5 and 6 of the smarter guests (docs/SMARTER-GUESTS.md) as switches. The shipped extension (version 1.5.0) has all of them on; the switches exist so a measurement can run an earlier
/// version's behaviour and each rule alone from the same build (<c>--guest-rules</c>) and so tests can show what each one does. They are not a player setting: a park records the extension's version as its
/// algorithm identity, which means all of them on. The rules of phases 2 to 4 have no switch: they are the base every set runs.
/// </summary>
[Flags]
public enum SmarterRuleSet
{
	/// <summary>None of the switchable rules: the guests of version 1.3.0.</summary>
	None = 0,
	/// <summary>Plan: a service whose need will cross its level before the guest is done with the ride it would take is scored as if the need had already grown (phase 5).</summary>
	Planning = 1,
	/// <summary>Detour: a service on the guest's way (in the minor decision's window) is scored with the need one step higher (phase 5).</summary>
	Detour = 2,
	/// <summary>Meal time: shortly after a meal, a ride that would make the guest sick scores lower (phase 5).</summary>
	MealTime = 4,
	/// <summary>Litter: a wander goal on or beside litter is swapped for a cleaner cell of the walk, and a guest standing in litter complains (phase 6).</summary>
	Litter = 8,
	/// <summary>Crowding: a wander goal in a crowd is swapped for a quieter cell of the walk, and a guest standing in a crowd complains (phase 6).</summary>
	Crowding = 16,
	/// <summary>Litter-aware routes: a route that crosses litter is walked round it when a few cells more avoid it (phase 6b).</summary>
	Routes = 32,
	/// <summary>Bins: a guest with a full litter meter looks further for a bin before it drops the litter (phase 6b).</summary>
	Bins = 64,
	/// <summary>The three rules of phase 5: version 1.4.0.</summary>
	Phase5 = Planning | Detour | MealTime,
	/// <summary>The two rules of phase 6.</summary>
	Paths = Litter | Crowding,
	/// <summary>All five: version 1.5.0.</summary>
	All = Phase5 | Paths
}
