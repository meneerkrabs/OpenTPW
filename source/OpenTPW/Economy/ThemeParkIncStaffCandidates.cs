namespace OpenTPW;

/// <summary>
/// A Theme Park Inc hiring-pool candidate (the 0x20-byte record of the pool's 32 slots): its role, name, expected wage, staff agency, the raw sprite draw, its
/// experience, the turn it was made on and its timeout (it leaves the pool once more than 4 × the timeout turns have passed).
/// </summary>
// [BIN:TPI-EXE:0x6302C0 candidate record] +0 role, +4 name index, +8 wage, +0xC agency, +0x10 sprite byte, +0x11 valid, +0x12 being placed, +0x14 stamp, +0x18 timeout, +0x1C experience
public sealed record ThemeParkIncStaffCandidate( int Id, ThemeParkIncStaffRole Role, int NameIndex, int Wage, int Agency, uint SpriteDraw, int Experience, long Stamp, int Timeout );

/// <summary>Theme Park Inc's candidate generation (docs/reverse/TPI-STAFF.md, section 3).</summary>
public static class ThemeParkIncStaffCandidates
{
	/// <summary>The size of each role's name table the candidate draws from (R mod 35).</summary>
	public const int NameTableSize = 35;

	/// <summary>The most name draws a candidate makes before it keeps a name already in use.</summary>
	public const int MaximumNameDraws = 15;

	/// <summary>The sprite category a role's candidate draws its look from (the argument of 0x6902F0).</summary>
	// [BIN:TPI-EXE:0x630308 candidate sprite category] handyman 9, mechanic 0xA, entertainer 8, guard 0xB, researcher 0xC, gardener 0x12 (0x630418, 0x6304A0, 0x630528, 0x6305B0, 0x630390)
	public static int SpriteCategory( ThemeParkIncStaffRole role ) => role switch
	{
		ThemeParkIncStaffRole.Handyman => 9,
		ThemeParkIncStaffRole.Mechanic => 0xA,
		ThemeParkIncStaffRole.Entertainer => 8,
		ThemeParkIncStaffRole.Guard => 0xB,
		ThemeParkIncStaffRole.Researcher => 0xC,
		_ => 0x12
	};

	/// <summary>
	/// One new candidate of <paramref name="role"/>: the agency, the sprite draw, the experience, the wage, the name and the timeout, in the executable's order.
	/// </summary>
	// [BIN:TPI-EXE:0x6302C0 candidate] the agency is rand() % (the agencies the career's rank reaches, 0x6302CB..0x6302F8); the sprite takes a world draw (0x6902F0: (R >> 2) mod the
	// category's loaded bank count); experience = AvgExperienceOf<Role> + (R % AvgWageDifference, unsigned) - AvgWageDifference / 2 (toward zero), plus exp × the agency's
	// InitialExperienceModifier / 100 (signed, toward zero) when both are nonzero, capped at MaxExperience (0x630318..0x630375); the wage is ExpectedWage(exp, <Role>.MaxWage)
	// plus the 32-bit product wage × InitialWageModifier divided by 100 unsigned when both are nonzero (0x630642..0x63066B); the name is R % 35 drawn again while
	// it is taken, at most 15 draws (0x6300F0); the timeout is StaffTimeoutTime + R % (StaffTimeoutTime >> 1), unsigned (0x630690..0x6306AC)
	// [EXT:tpi-staff-crt-rand] user-approved 2026-11-21: the agency's rand() (0x870D38, seeded from the clock by srand(GetTickCount()) at 0x6A151B) is drawn from the park's world generator
	public static ThemeParkIncStaffCandidate Generate( ThemeParkIncStaffSettings settings, ThemeParkIncStaffRole role, WorldRng world, int promotionLevel, int id, long turn,
		Func<ThemeParkIncStaffRole, int, bool> nameTaken )
	{
		var roleSettings = settings.Roles[role];
		var agencyCount = settings.AvailableAgencies( promotionLevel );
		var agency = (int)world.Next( (uint)agencyCount );
		var spriteDraw = world.NextWord();
		var spread = settings.AverageWageDifference;
		var experience = unchecked( roleSettings.AverageExperience + (int)(world.NextWord() % (uint)spread) - spread / 2 );
		var row = agency < settings.Agencies.Count ? settings.Agencies[agency] : null;
		if ( row is { InitialExperienceModifier: not 0 } && experience != 0 )
			experience = unchecked( experience + row.InitialExperienceModifier * experience / 100 );
		if ( experience > settings.MaxExperience )
			experience = settings.MaxExperience;
		var wage = settings.ExpectedWage( experience, roleSettings.MaxWage );
		if ( row is { InitialWageModifier: not 0 } && wage != 0 )
			wage = unchecked( (int)((uint)(wage * row.InitialWageModifier) / 100u) + wage );
		int name;
		var draws = 0;
		do
		{
			name = (int)world.Next( (uint)NameTableSize );
			draws++;
		}
		while ( nameTaken( role, name ) && draws < MaximumNameDraws );
		var timeout = settings.StaffTimeoutTime + (int)(world.NextWord() % (uint)(settings.StaffTimeoutTime >> 1));
		return new ThemeParkIncStaffCandidate( id, role, name, wage, agency, spriteDraw, experience, turn, timeout );
	}
}
