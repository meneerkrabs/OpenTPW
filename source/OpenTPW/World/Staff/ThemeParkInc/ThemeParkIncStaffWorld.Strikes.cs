namespace OpenTPW;

/// <summary>
/// Theme Park Inc's strikes in the park (docs/reverse/TPI-WAGES.md §3): a member of a role on strike walks to the strike area (state 4), stands there (state 5) until the strike is over and walks back
/// to the park's entrance; and the destroy of a dismissed member's thing. Every role runs the two states (the common handler 0x6287A0, which each role's table sends 4 and 5 to).
/// </summary>
public sealed partial class ThemeParkIncStaffWorld
{
	/// <summary>The strike states: walking to the strike area (4) and standing in it (5); both show "On strike" (display state 4).</summary>
	public const int ToStrike = 4, Striking = 5;

	/// <summary>The template the striking setter queues (<c>0x9B070C</c>: SET 0x10 = 0x1200, SLOT 5, FRAME 0, 1, 2, 3 and six times 0, JMP back: tp.exe's strike template 0x13, opcode for opcode).</summary>
	public const int StrikeTemplate = 0x1B;

	/// <summary>
	/// The strike branch of the gate (<c>0x62BBA0</c>): while the member's role is on strike and the park gate's status is open, he walks to a random point of the strike area (four world draws) and
	/// enters state 4. False when the role is not on strike or no route reaches the point.
	/// </summary>
	// [BIN:TPI-EXE:0x62BBAD strike join] 0x62AF70 (the role), 0x634210 on the HQ (the active word) and 0x64FA50 == 1; "Right! I'm fed up, I'm going on strike!" (0x9D2A7C); +0x1A0 = 1; four world draws
	// through 0x40A92F: x = StrikeAreaStartX + R % StrikeAreaSizeX (unsigned divl, the byte sum), the x fraction R & 0xFF, y = StartY + R % SizeY, the y fraction R & 0xFF; the staff route 0x62EB10 to them:
	// a route sets 4 (0x628180) and answers 1; none logs "Could not get to strike location" (0x9D2A48) and goes on to the rest branch 0x62BC88
	// [BIN:TPI-EXE:0x64FA50 gate status] the gate thing's (world +0x1EAB66) script variable 1 (VAR_STATUS, 1 open), -1 without a gate; [EXT:tpi-park-open] the Theme Park Inc park opens at the start and
	// runs no gate script (its gate commands are not traced), so its gate stands open: the status reads 1
	internal bool StrikeWalk( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		if ( economy.TpiStaff is not { } staff || !staff.OnStrike( member.Role ) || GateStatus != ParkGate.GateIsOpen )
			return false;
		var area = settings.StrikeArea;
		if ( area.Width == 0 || area.Height == 0 )
			throw new InvalidOperationException( "Theme Park Inc's strike area has no size (FixedItemInfo.StrikeAreaSizeX/Y): the original's strike walk divides by zero." );
		var navigation = member.Navigation!;
		navigation.Mask = 1;
		var x = (byte)(area.X + (int)(economy.World.NextWord() % (uint)area.Width));
		var byteX = (int)(economy.World.NextWord() & 0xFF);
		var y = (byte)(area.Y + (int)(economy.World.NextWord() % (uint)area.Height));
		var byteY = (int)(economy.World.NextWord() & 0xFF);
		if ( !StaffRoute( economy, member, (x, y), byteX, byteY ) )
			return false;
		SetState( economy, member, ToStrike, turn );
		return true;
	}

	/// <summary>The park gate's status as the strike branch reads it (<c>0x64FA50</c>): open (1), see <see cref="StrikeWalk"/>.</summary>
	private static int GateStatus => ParkGate.GateIsOpen;

	/// <summary>State 4's tick (<c>0x628855</c>): the walk step; arrived or stuck, the member stands in the strike area (5).</summary>
	// [BIN:TPI-EXE:0x628855 to the strike] 0x60B440 (the walk step: 0 arrived, 1 walking, 2 stuck), the thought 0x19 (0x638960, presentation); 0 or 2: +0x1A0 = 0 and 0x628180(5). No moving step (no 0x62B3E0)
	internal void UpdateToStrike( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		if ( Walk( member ) == WalkResult.Walking )
			return;
		member.Navigation!.Mask = 0;
		SetState( economy, member, Striking, turn );
	}

	/// <summary>
	/// State 5's tick (<c>0x62A4C0</c>): a world draw turns the member now and then; a closed park without guests ends the role's strike; once the strike is over the member walks back to the park's
	/// entrance (or idles when no route reaches it).
	/// </summary>
	// [BIN:TPI-EXE:0x62A4C0 striking] one world draw (0x40A92F): (R & 7) == 0 adds (R & 0xFF) - 0x80 to the sprite angle +0x2C, at most 0x7FF (unsigned: below 0 also gives 0x7FF), presentation only (the
	// heading stays the setter's, as Theme Park World's striker); the park closed (0x64FA30, world +0x1EAB44) and 0x5B2060(4) == 0 (no guest in the park) end the strike (0x6342B0(role, 0): message 5); then
	// with the role's active word 0 (0x634210): the staff route 0x62EB10 to the centre of the cell EntranceAPosX/Y (0xA025D0, 0xA025D4) with the thought 0x1D (presentation) and 0x628180(1), or
	// "Staff member couldn't find park entrance" (0x9D29A0) and 0x628180(0)
	internal void UpdateStriking( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		_ = economy.World.NextWord();
		var staff = economy.TpiStaff!;
		if ( !economy.IsParkOpen && economy.ThemeParkIncGuestsInPark == 0 && staff.EndStrike( member.Role, quiet: false ) is { } over )
			economy.TpiStrikeMessages.Add( over );
		if ( staff.OnStrike( member.Role ) )
			return;
		var entrance = staff.Settings.EntranceA;
		SetState( economy, member, StaffRoute( economy, member, entrance, GuestWander.CentreByte, GuestWander.CentreByte ) ? Walking : Idle, turn );
	}

	/// <summary>
	/// The class's destroy of a dismissed member's thing (the kinds' destroys <c>0x5D4A10</c>, <c>0x5D9D60</c>, <c>0x5D1850</c>, <c>0x5D3390</c>, <c>0x61B520</c>, <c>0x5D2430</c>), after the destroy's message
	/// 0x21: he leaves his role's list, a handyman's litter claim, a mechanic's ride claim and a gardener's plant claim go (the claimant then idles), an entertainer's or a guard's cell effect comes off, and a
	/// researcher's leaving updates the lab's active state. The person's destroy (the cell chain and the sprite) and the thing's are the level's.
	/// </summary>
	// [BIN:TPI-EXE:0x5D4A10 handyman destroy] 0x64EE40 (the unlink), +0x5D8 set: 0x5D9010 on the record (+0x34 = 0, +0x38 = 0) and +0x5D8 = 0; +0x5DA and +0x5DC (the loo and the bin, never set here:
	// [EXT:tpi-no-maintenance-wear]) 0x5E37B0; then 0x609130 (the person: 0x637FE0, the sprite source deleted 0x5D1050, 0x638460)
	// [BIN:TPI-EXE:0x5D9D60 mechanic destroy] 0x64EF70, +0x5D8 set: 0x5E37B0 on the ride (its claimant +0x72 nonzero gets 0x628180(0), then +0x72 = 0 and +0x74 = 0); 0x609130. [BIN:TPI-EXE:0x5D2430 gardener
	// destroy] the same with 0x64F560 and the plant
	// [BIN:TPI-EXE:0x5D1850 entertainer destroy] 0x64F0A0, radius = 0x62C900(0, MaxExperience, +0x210, MinActiveRange, MaxActiveRange), 0x5D7530(row 0, 0x609E20's stored word +0xE4, radius); 0x609130.
	// [BIN:TPI-EXE:0x5D3390 guard destroy] 0x64F1D0 and row 3 with the guard's range; a guard in state 0x1C with a held guest (+0x5D2) releases him (0x614650): the chase is not ported, so no guard holds one
	// [BIN:TPI-EXE:0x61B520 researcher destroy] 0x64F300, 0x609130, then with the lab 0x61E4E0 (the lab's active word from the researchers' list)
	internal static void DestroyMember( ParkEconomy economy, ThemeParkIncStaff staff, ThemeParkIncStaffMember member, long turn )
	{
		staff.Remove( member );
		switch ( member.Role )
		{
			case ThemeParkIncStaffRole.Handyman when member.LitterCell != 0:
				var (x, y) = CellOf( member.LitterCell );
				economy.Litter.Claim( x, y, 0, 0 );
				member.LitterCell = 0;
				break;
			case ThemeParkIncStaffRole.Mechanic when member.RideId != 0:
				ReleaseClaim( economy, staff, member.RideId, turn );
				break;
			case ThemeParkIncStaffRole.Gardener when member.PlantId != 0:
				ReleaseClaim( economy, staff, member.PlantId, turn );
				break;
			case ThemeParkIncStaffRole.Entertainer or ThemeParkIncStaffRole.Guard when member.Placed:
				// 0x609E20 hands the stored word (+0xE4); the radius is the one his experience gives now, so a radius that grew since the effect went down leaves its difference
				var radius = EffectRadiusOf( staff.Settings, member );
				var row = EffectRowOf( member.Role );
				var (cellX, cellY) = CellOf( member.EffectCell );
				economy.Litter.Environment.ApplyRadius( row, cellX, cellY, add: false, radius );
				economy.Litter.Environment.KeepRadiusResidue( row, cellX, cellY, member.EffectRadius, radius );
				break;
			case ThemeParkIncStaffRole.Researcher:
				economy.TpiResearch?.UpdateLabActive( staff.Members.Any( other => other.Role == ThemeParkIncStaffRole.Researcher && other.Placed ) );
				break;
		}
		member.Sprite.Delete();
	}

	/// <summary>The claim release <c>0x5E37B0</c> on an object: a claimant goes idle by the common setter (<c>0x628180(0)</c>), then the claim word and its turn clear.</summary>
	private static void ReleaseClaim( ParkEconomy economy, ThemeParkIncStaff staff, int instanceId, long turn )
	{
		if ( !economy.TryGetObject( instanceId, out var item ) )
			return;
		if ( item.MechanicId != 0 )
			ClaimantReleased( staff, item.MechanicId, turn );
		item.MechanicId = 0;
		item.ClaimTurn = 0;
	}
}
