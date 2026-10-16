namespace OpenTPW;

/// <summary>A staff member's two vitals as the strike test reads them (energy at thing +504, happiness at +500).</summary>
public readonly record struct StaffVital( float Energy, float Happiness );

/// <summary>
/// Staff vitals (ECON-014, docs/reverse/ORACLE.md F22). Every member has an energy and a happiness, both singles in 0..100 that
/// start at 100. Each park turn a walking member loses energy and happiness, a working one loses more, a resting one regains
/// them; a member whose energy has fallen to <c>RestLevel</c> looks for a rest area and loses happiness when there is none.
/// The arithmetic is the original's (the oracle targets <c>staff-vitals</c> and <c>staff-rest-seek</c> execute the routines).
/// </summary>
public static class StaffVitals
{
	/// <summary>Both vitals start at, and are clamped to, 100; the lower clamp is 0.</summary>
	// [BIN:STP-PPC:0x100F29FC staff constructor] energy (+504) and happiness (+500) are both set to the single at TOC −0x29C8, 100.0 (the clamp in every step is 100.0 and TOC −0x29C4, 0.0)
	public const float Maximum = 100f;

	/// <summary>The truncated low byte the original compares: <c>fctiwz</c> (saturating, NaN is 0x80000000) then the low 8 bits.</summary>
	public static int Byte( float value ) => Truncate( value ) & 0xFF;

	private static int Truncate( float value ) =>
		float.IsNaN( value ) ? int.MinValue : value >= 2147483648f ? int.MaxValue : value <= -2147483648f ? int.MinValue : (int)value;

	private static float Clamp( float value ) => value > Maximum ? Maximum : value < 0f ? 0f : value;

	/// <summary>One park turn of a moving member (0x100F44C0): energy −= 0.012 (6 − grade), happiness −= 0.005 (6 − grade), clamped.</summary>
	// [BIN:STP-PPC:0x100F44C0 moving step] the products 0.012 × (6 − grade) and 0.005 × (6 − grade) are binary64 (data constants 0x5610, 0x5608) rounded to single before the single subtraction; the result is clamped to 0..100
	public static void Walk( StaffMember member )
	{
		var factor = 6 - member.Grade;
		member.Energy = Clamp( member.Energy - (float)(0.012 * factor) );
		member.Happiness = Clamp( member.Happiness - (float)(0.005 * factor) );
	}

	/// <summary>One park turn of a working member (0x100F459C): energy −= 0.025f × (6 − grade), happiness −= 0.01 × (6 − grade), clamped.</summary>
	// [BIN:STP-PPC:0x100F459C working step] the energy product is a single product with the float 0.025 (data 0x5600), the happiness product binary64 with 0.01 (data 0x55F8) rounded to single
	public static void Work( StaffMember member )
	{
		var factor = 6 - member.Grade;
		member.Energy = Clamp( member.Energy - 0.025f * (float)factor );
		member.Happiness = Clamp( member.Happiness - (float)(0.01 * factor) );
	}

	/// <summary>
	/// One park turn of a resting member (0x100F3D8C): energy and happiness gain their grade's recuperation rates, clamped. True when the
	/// truncated energy byte has reached 100: the rest ends and the member goes back to its role's next action.
	/// </summary>
	// [BIN:STP-PPC:0x100F3D8C rest step] rates are the settings floats at +756 (energy) and +760 (happiness) + 16 × grade; the rest ends when the energy's truncated low byte equals 100 (0x100F3E2C..0x100F3E44), which frees the rest area (0x100F4D00)
	public static bool Rest( StaffMember member, float energyRate, float happinessRate )
	{
		member.Energy = Clamp( energyRate + member.Energy );
		member.Happiness = Clamp( happinessRate + member.Happiness );
		return Byte( member.Energy ) == 100;
	}

	/// <summary>0x100F448C: the truncated energy byte is below <paramref name="restLevel"/> (a signed compare).</summary>
	public static bool IsBelowRestLevel( float energy, int restLevel ) => Byte( energy ) < restLevel;

	/// <summary>
	/// The decision of 0x100F4900 for a member that is not striking: with the truncated energy byte at most <paramref name="restLevel"/> it
	/// looks for a rest area and, when one is reachable, sets off (true: it will rest). Without one it loses
	/// <paramref name="noRestAreaHappinessHit"/> happiness (clamped) when a world draw's low four bits are 0, one turn in sixteen.
	/// </summary>
	// [BIN:STP-PPC:0x100F4A60 rest search] byte > RestLevel (signed) is the comfortable branch (speed tiers and thoughts only); otherwise 0x100F4C18 looks for a rest area (state 2, result 1) and, when none is found, a world draw with (draw & 15) == 0 lowers the happiness (0x100F4AD0..0x100F4B54)
	// [EXT:headless-economy] this decision serves the headless economy only: a level's staff world has the thoughts (tired, unhappy, happy) and the walking-speed tiers of the comfortable branch (StaffWorldSimulation.Rest), and the walk to the rest area takes time there
	public static bool SeekRest( StaffMember member, int restLevel, int noRestAreaHappinessHit, bool restAreaFound, Func<uint> worldDraw )
	{
		if ( Byte( member.Energy ) > restLevel )
			return false;
		if ( restAreaFound )
			return true;
		if ( (worldDraw() & 15) == 0 )
			member.Happiness = Clamp( member.Happiness - (float)noRestAreaHappinessHit );
		return false;
	}
}
