namespace OpenTPW;

/// <summary>
/// The three rules of phase 5 of the smarter guests (docs/SMARTER-GUESTS.md, "Phase 5: Planning needs"), as switches. The shipped extension (version 1.4.0) has all of them on; the switches exist so a measurement can run
/// "1.3.0 behaviour" and each rule alone from the same build (<c>--guest-rules</c>, <see cref="GuestSimulation.PlanningRulesForMeasurement"/>) and so tests can show what each one does. They are not a player setting: a park
/// records the extension's version as its algorithm identity, which means all three on.
/// </summary>
[Flags]
public enum SmarterPlanningRules
{
	/// <summary>None of the phase 5 rules: the guests of version 1.3.0.</summary>
	None = 0,
	/// <summary>Plan: a service whose need will cross its level before the guest is done with the ride it would take is scored as if the need had already grown.</summary>
	Planning = 1,
	/// <summary>Detour: a service on the guest's way (in the minor decision's window) is scored with the need one step higher.</summary>
	Detour = 2,
	/// <summary>Meal time: shortly after a meal, a ride that would make the guest sick scores lower.</summary>
	MealTime = 4,
	/// <summary>All three: version 1.4.0.</summary>
	All = Planning | Detour | MealTime
}
