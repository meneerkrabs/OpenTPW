namespace OpenTPW;

/// <summary>What the Theme Park Inc HUD tells of a challenge slot: an offer, a win or a loss.</summary>
public enum ThemeParkIncChallengeNoticeKind
{
	Offered,
	Won,
	Lost
}

/// <summary>A Theme Park Inc challenge notice: the slot and what happened to it.</summary>
public readonly record struct ThemeParkIncChallengeNotice( int Slot, ThemeParkIncChallengeNoticeKind Kind );

public sealed partial class ParkEconomy
{
	/// <summary>
	/// Theme Park Inc's challenge system (docs/reverse/TPI-CHALLENGE-SYSTEM.md); null in Theme Park World, where the challenges are refused by the rules, and where the settings hold
	/// no Theme Park Inc challenge keys or no <c>Challenges.sam</c>.
	/// </summary>
	public ThemeParkIncChallenges? TpiChallenges { get; private set; }

	/// <summary>
	/// The Theme Park Inc challenge notices since the HUD last took them (offers, wins and losses; the original's message 0x1B kinds 0, 2 and 3). Not saved: the HUD lists the slots
	/// from their states. A run without the HUD keeps the latest <see cref="MaxTpiChallengeNotices"/> only.
	/// </summary>
	internal List<ThemeParkIncChallengeNotice> TpiChallengeNotices { get; } = new();

	/// <summary>The notices a run without the HUD keeps.</summary>
	internal const int MaxTpiChallengeNotices = 32;

	private ThemeParkIncChallengeHost? tpiChallengeHost;

	private void CreateThemeParkIncChallenges()
	{
		if ( Rules.ThemeParkInc && Rules.Challenges && Settings.ThemeParkIncChallenges is { } challengeSettings )
		{
			TpiChallenges = new ThemeParkIncChallenges( challengeSettings );
			tpiChallengeHost = new ThemeParkIncChallengeHost( this );
		}
	}

	/// <summary>The challenge system's thing update: once per world update pass, not in a lost park (the caller's thing loop has stopped there).</summary>
	private void UpdateThemeParkIncChallenges( long turn ) => TpiChallenges!.Update( turn, tpiChallengeHost! );

	/// <summary>
	/// Whether the challenge mail's accept control is enabled for slot <paramref name="index"/>: the slot is in state 1 (offered, or lost) and the park is open.
	/// </summary>
	// [BIN:TPI-EXE:0x52C2B2 accept control] the mail view enables control 0xC1202 only when the slot's state is 1 (0x52C2B2) and the park is open (0x52C2D9 -> 0x5B1D30)
	public bool CanAcceptThemeParkIncChallenge( int index ) =>
		TpiChallenges is { } challenges && index >= 0 && index < ThemeParkIncChallenges.SlotCount
		&& challenges.Slots[index].Status == ThemeParkIncChallengeStatus.Offered && ThemeParkIncChallenges.IsPorted( challenges.Slots[index].Type ) && IsParkOpen;

	/// <summary>Accepts the offered challenge in slot <paramref name="index"/> (the mail's accept control, <c>0x52A657</c> -> <c>0x5C84C0</c>).</summary>
	public void AcceptThemeParkIncChallenge( int index )
	{
		if ( TpiChallenges is not { } challenges )
			throw new InvalidOperationException( "This park runs no Theme Park Inc challenges." );
		if ( !CanAcceptThemeParkIncChallenge( index ) )
			throw new InvalidOperationException( $"Theme Park Inc challenge {index} cannot be accepted now: it must be on offer and the park open." );
		challenges.Accept( index, Turn );
	}

	/// <summary>Offers a challenge slot now whatever its trigger (the slice smoke test's offer step); the slot offered, or -1.</summary>
	// [EXT:debug-hud] developer/smoke helper, not original behaviour
	internal int DebugOfferThemeParkIncChallenge( int index )
	{
		if ( TpiChallenges is not { } challenges || index < 0 || index >= ThemeParkIncChallenges.SlotCount )
			return -1;
		var offered = challenges.DebugOffer( index, Turn );
		if ( offered >= 0 )
			AddTpiChallengeNotice( new ThemeParkIncChallengeNotice( offered, ThemeParkIncChallengeNoticeKind.Offered ) );
		return offered;
	}

	private void AddTpiChallengeNotice( ThemeParkIncChallengeNotice notice )
	{
		if ( TpiChallengeNotices.Count >= MaxTpiChallengeNotices )
			TpiChallengeNotices.RemoveAt( 0 );
		TpiChallengeNotices.Add( notice );
	}

	/// <summary>
	/// The challenge events of a Theme Park Inc attraction's takings (<c>0x5E5DE0</c>, the income booking of a paid use): a shop posts type 12 with the amount and its sale count, a
	/// sideshow type 13 with the amount.
	/// </summary>
	// [BIN:TPI-EXE:0x5E5DE0 takings events] after the bank credit and the object's takings, a type record whose category +0x600 is 1 (a shop) posts 0x5C8940(12, n, 0, 0) and then by
	// UsageInfo.ShopType (+0x224, jump table 0x5E6010): 1 type 5; 3 type 8; 5 type 7; 6 type 6; 2 or 4 by UsageInfo.SpecialIngredient (+0x218, table 0x5E6028): 1 type 2, 2 type 1, 3 type 3
	// then 45 with n, 4 type 4 then 45 with n (each sale count + 1, object 0); category 2 (a sideshow) posts (13, n, 0, 0); the category is the attraction kind (shop 1, sideshow 2)
	private void PostThemeParkIncTakingsEvents( ParkObjectState item, int amount )
	{
		if ( TpiChallenges is not { } challenges || !Catalog.TryGet( item.InfoId, out var info ) )
			return;
		if ( item.Kind == ParkObjectKind.Sideshow )
		{
			challenges.AddEvent( 13, amount, 0, false );
			return;
		}
		if ( item.Kind != ParkObjectKind.Shop )
			return;
		challenges.AddEvent( 12, amount, 0, false );
		switch ( info.ShopType )
		{
			case 1: challenges.AddEvent( 5, 1, 0, false ); break;
			case 3: challenges.AddEvent( 8, 1, 0, false ); break;
			case 5: challenges.AddEvent( 7, 1, 0, false ); break;
			case 6: challenges.AddEvent( 6, 1, 0, false ); break;
			case 2 or 4:
				switch ( info.SpecialIngredient )
				{
					case 1: challenges.AddEvent( 2, 1, 0, false ); break;
					case 2: challenges.AddEvent( 1, 1, 0, false ); break;
					case 3 or 4:
						challenges.AddEvent( info.SpecialIngredient, 1, 0, false );
						challenges.AddEvent( 45, amount, 0, false );
						break;
				}
				break;
		}
	}

	/// <summary>The challenge event of a Theme Park Inc attraction's cost booking (<c>0x5E60E0</c>): a shop posts type 12 with the cost negated, a sideshow type 13.</summary>
	// [BIN:TPI-EXE:0x5E60E0 cost events] after the object's cost rings (+0x88, +0x938) a category-1 record posts 0x5C8940(12, -n, 0, 0) and a category-2 record (13, -n, 0, 0)
	private void PostThemeParkIncCostEvent( ParkObjectState item, long amount )
	{
		if ( TpiChallenges is not { } challenges )
			return;
		var negated = unchecked( -(int)amount );
		if ( item.Kind == ParkObjectKind.Shop )
			challenges.AddEvent( 12, negated, 0, false );
		else if ( item.Kind == ParkObjectKind.Sideshow )
			challenges.AddEvent( 13, negated, 0, false );
	}

	/// <summary>The park as the challenge system reads it, and the outcomes it books there.</summary>
	private sealed class ThemeParkIncChallengeHost( ParkEconomy economy ) : IThemeParkIncChallengeHost
	{
		public bool ParkOpen => economy.IsParkOpen;

		// [BIN:TPI-EXE:0x5C7967 mission gate] the switch and the availability read the career's +0x105C (ThemeParkIncCareer.MissionCounter)
		public int MissionCounter => economy.TpiCareer!.MissionCounter;

		// [BIN:TPI-EXE:0x5C4857 firing line step] the first loss of a slot lowers the career's first word by one
		public void LowerFiringLine() => economy.TpiCareer!.LowerFiringLine();

		// [BIN:TPI-EXE:0x5B9380 fat shop] walks every thing; a kind-3 thing (an attraction) whose type record has +0x218 == 1 answers true
		public bool HasFatShop() => economy.objects.Values.Any( item => economy.Catalog.TryGet( item.InfoId, out var info ) && info.SpecialIngredient == 1 );

		// [BIN:TPI-EXE:0x5B32F0 most experience] walks every thing; one of the kind keeps its +0x210 when above the best so far (signed jle), which starts at 0. The staff kinds are
		// 4 mechanic, 5 handyman, 6 entertainer, 7 guard, 8 researcher and 0x14 gardener (the thing dispatch 0x637860, docs/reverse/TPI-STAFF.md)
		public int MaxExperience( int kind )
		{
			var best = 0;
			if ( RoleOfKind( kind ) is not { } role || economy.TpiStaff is not { } staff )
				return best;
			foreach ( var member in staff.OfRole( role ) )
			{
				if ( member.Experience > best )
					best = member.Experience;
			}
			return best;
		}

		// [BIN:TPI-EXE:0x5B2F40 staff count] counts the things of kinds 4, 5, 6, 8, 7 and 0x14: the placed staff members
		public int StaffCount() => economy.TpiStaff?.Members.Count ?? 0;

		// [BIN:TPI-EXE:0x5AFED0 least happy] starts at 100; each staff thing's happiness (+0x230) truncated by __ftol (0x86FAB0) and masked to a byte replaces it when lower (signed jge)
		public int MinStaffHappiness()
		{
			var least = 100;
			foreach ( var member in economy.TpiStaff?.Members ?? (IReadOnlyList<ThemeParkIncStaffMember>)Array.Empty<ThemeParkIncStaffMember>() )
			{
				var happiness = (int)member.Happiness & 0xFF;
				if ( happiness < least )
					least = happiness;
			}
			return least;
		}

		// [BIN:TPI-EXE:0x634240 strike test] any of the six roles' active words (+0x160, +0x19C, +0x16C, +0x178, +0x184, +0x190) set
		public bool AnyRoleOnStrike() => economy.TpiStaff?.Strikes.Any( strike => strike.Active ) ?? false;

		// [BIN:TPI-EXE:0x5C4285 cash prize] a CashPrize above 0 (signed) is credited by the bank (0x5C0760: the balance, the income history and the bank's +0x12C), as a paid fare is;
		// [BIN:TPI-EXE:0x5C42C8 ticket prize] a GTPrize above 0 is added to the career's golden tickets (+0x1074, 0x74C890)
		// [BIN:TPI-EXE:0x5C43AF promotion] a PromoGiven (+0x58) above 0 (unsigned) posts message 0x25 with it (0x5C4405), which the mission controller's handler (0x5F0F50) turns into
		// 0x74C450(rank): the rank and the firing line of FiringLineForRank; the promotion letter 0x5CF4D0 and the controller's panel 0x1A4 are presentation
		// [BIN:TPI-EXE:0x5C4514 challenge count] every slot of the system empty (type 0) or won (state 4) sets the current zone record's +0xF4 (0x5C45A9); either way its +0xF0 rises by one
		// (0x5C4594, 0x5C45FC), the record being 0x74F4B0(current zone +0x64)
		// [EXT:tpi-capability-gates] the share prize and the share price effect (0x621CE0, 0x621D30; Theme Park Inc's share system is not ported) are left off; the won mail (message 0x1B kind 2)
		// is the HUD's notice
		public void Won( int index, ThemeParkIncChallengeSlot slot )
		{
			var career = economy.TpiCareer!;
			if ( slot.CashPrize > 0 )
				economy.Post( LedgerCategory.OtherIncome, slot.CashPrize );
			if ( slot.GTPrize > 0 )
				career.AddGoldenTickets( slot.GTPrize );
			economy.AddTpiChallengeNotice( new ThemeParkIncChallengeNotice( index, ThemeParkIncChallengeNoticeKind.Won ) );
			if ( slot.PromoGiven != 0 )
				career.Promote( slot.PromoGiven );
			// [EXT:headless-economy] a headless economy (no Theme Park Inc files) has no current zone record to count in; a game's park always has one (EnterThemeParkIncZone)
			if ( !economy.TpiHeadlessCareer )
				career.CountChallengeWon( economy.TpiChallenges!.Slots.All( other => other.Type == 0 || other.Status == ThemeParkIncChallengeStatus.Won ) );
		}

		// [BIN:TPI-EXE:0x5C48B6 firing line post] after the count the lost branch posts the lost mail (message 0x1B kind 3, 0x5C49A3) while the career's firing line is above 0 (signed jg),
		// else notification id 7, which the tag system turns into advice 0x1A3 and the firing letter (TAG_SYSTEM.str entry 167, ThemeParkIncAdviceMail.ChallengeLost), on the loss that
		// exhausts the line and on every loss after it
		// [EXT:tpi-capability-gates] the first loss's share price fall (0x621D30 with -SharePriceEffectIfLost) is left off: Theme Park Inc's share system is not ported
		public void Lost( int index, ThemeParkIncChallengeSlot slot, bool firstLoss )
		{
			if ( economy.TpiCareer!.FiringLine > 0 )
				economy.AddTpiChallengeNotice( new ThemeParkIncChallengeNotice( index, ThemeParkIncChallengeNoticeKind.Lost ) );
			else
				economy.TpiPendingMail = ThemeParkIncAdviceMail.ChallengeLost;
		}

		public void Offered( int index ) => economy.AddTpiChallengeNotice( new ThemeParkIncChallengeNotice( index, ThemeParkIncChallengeNoticeKind.Offered ) );

		private static ThemeParkIncStaffRole? RoleOfKind( int kind ) => kind switch
		{
			4 => ThemeParkIncStaffRole.Mechanic,
			5 => ThemeParkIncStaffRole.Handyman,
			6 => ThemeParkIncStaffRole.Entertainer,
			7 => ThemeParkIncStaffRole.Guard,
			8 => ThemeParkIncStaffRole.Researcher,
			0x14 => ThemeParkIncStaffRole.Gardener,
			_ => null
		};
	}
}
