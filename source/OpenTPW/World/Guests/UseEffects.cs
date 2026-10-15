namespace OpenTPW;

/// <summary>
/// What an attraction's own <c>UsageInfo</c> effects do to the guest's meters when it is used (the sale routine <c>0xeaaf8</c>, fragment <c>0xeabb8</c>,
/// docs/reverse/ORACLE.md F18): thirst and hunger fall by the type's <c>ThirstEffect</c> and <c>HungerEffect</c>, illness and happiness rise by
/// <c>VomitEffect</c> and <c>HappinessEffect</c>, each in single precision and clamped to 0..100. (The fifth effect, <c>LitterEffect</c>, fills the guest's litter
/// meter in the original; OpenTPW books it in the park's litter store, ECON-020.)
/// </summary>
public static class UseEffects
{
	/// <summary>The four meter updates, in the order the routine runs them (thirst, hunger, illness, happiness).</summary>
	// [BIN:STP-PPC:0x100EABC4 use effects] type +324 (thirst, subtracted), +328 (hunger, subtracted), +332 (vomit, added to illness), +336 (happiness, added); each a clamped single-precision step; executed by the PowerPC oracle (shop-sale-litter, 526 inputs)
	public static GuestMeters Apply( GuestMeters meters, int thirstEffect, int hungerEffect, int vomitEffect, int happinessEffect ) =>
		meters with
		{
			Thirst = MeterMath.Add( meters.Thirst, -(float)thirstEffect ),
			Hunger = MeterMath.Add( meters.Hunger, -(float)hungerEffect ),
			Illness = MeterMath.Add( meters.Illness, vomitEffect ),
			Happiness = MeterMath.Add( meters.Happiness, happinessEffect )
		};
}
