namespace OpenTPW;

/// <summary>
/// Theme Park Inc's ride wear, breakdown request and repair (docs/reverse/TPI-STAFF.md §11, "Mechanics and ride wear"): the wear part of the object update <c>0x5E4790</c>, the wear tick
/// <c>0x5E25B0</c> with the wear amount <c>0x5E21A0</c>, the maintenance request <c>0x5E3400</c> and the repair <c>0x5E2910</c>. They run in a park with Theme Park Inc's staff
/// (<see cref="TpiStaff"/>); Theme Park World's rules (<see cref="UpdateRide"/>) are untouched.
/// </summary>
public sealed partial class ParkEconomy
{
	/// <summary>
	/// One Theme Park Inc object update's wear part (<c>0x5E4790</c>): every 8th turn an open ride that is not broken down runs the wear tick and then the breakdown request; a relief object
	/// reports itself worn below a state of repair of 25 on every update.
	/// </summary>
	// [BIN:TPI-EXE:0x5E4803 object update] state 3 or 4 returns; the state not 1 (0x5E48AF), the open word +0x7C nonzero (0x5E48BB, cleared by every close: SetState 1, 2 and 4 at 0x5E51E8,
	// 0x5E52FC, the request 0x5E3498) and the turn & 7 zero (0x5E48CB) run the wear tick 0x5E25B0, then the breakdown test on the life (+0x58) and repair (+0x54) bytes (0x5E48DF..0x5E48F5);
	// the block before (0x5E4833..0x5E48A9: a closed object's sound 0x4CD640 once, latched in +0x12E) is presentation and draws nothing
	internal void UpdateThemeParkIncRide( ParkObjectState item, long turn )
	{
		// a type that is not choosable starts in state 3 (the constructor's SetState, as Theme Park World's 0x4DB4FB), which the update skips
		if ( !Catalog.TryGet( item.InfoId, out var info ) || !info.IsChoosable )
			return;
		var state = ObjectState( item );
		if ( state is RideObjectStates.NotAnAttraction or RideObjectStates.Condemned )
			return;
		if ( state != RideObjectStates.BrokenDown && item.IsOpen && (turn & 7) == 0 )
		{
			WearThemeParkInc( item, info, turn );
			var life = (float)item.LifeGauge;
			if ( RideBreakdownRules.GaugeByte( life ) == 0 || RideBreakdownRules.GaugeByte( (float)item.Repair ) == 0 )
			{
				// [BIN:TPI-EXE:0x5E48FB breakdown request] a relief object (+0x42 bit 1) goes to the worn test; VAR_BREAKSTAT (script variable 4) nonzero ends it; a life byte that is not 0 loses 5.0
				// (clamped to 0..100) unless the type's Info.IsSpecial is 1 (0x5E493B); then "Object %d: requested breakdown" and VAR_BREAKSTAT = 1 (0x5E499B). No random number is drawn
				if ( !info.ProvidesRelief && (item.ScriptHosted ? item.ScriptBreakStat : 0) == 0 )
				{
					if ( RideBreakdownRules.GaugeByte( life ) != 0 && !info.IsSpecial )
						item.LifeGauge = ClampThemeParkIncGauge( life - 5.0f );
					// [EXT:headless-economy-breakdown] (user-approved) as in UpdateRide: an object a script runs breaks down through its script (VAR_BREAKSTAT, the script's VAR_BROKEN and the
					// ride update's state 1 from 0x5E5960, Theme Park World's RideStateWord.BrokenStateFor rule), the headless economy without ride scripts at once
					if ( item.ScriptHosted )
						WriteScript( item, RideVariables.VAR_BREAKSTAT, 1 );
					else
					{
						item.IsBrokenDown = true;
						PublishRideState( item );
					}
					Raise( ParkEventKind.RideBrokeDown, 0, item.Id, item.InfoId );
				}
			}
		}
		// [BIN:TPI-EXE:0x5E3990 relief worn] a relief object (+0x42 bit 1) whose repair byte is below 25.0 (0x93E154) writes VAR_WORN = 1 (0x5E49C0), every update, as Theme Park World's 0x004E0D16
		// (the write is skipped while the script already holds 1: the same value, no event per turn)
		if ( info.ProvidesRelief && RideBreakdownRules.GaugeByte( (float)item.Repair ) < ReliefWornByte && item.ScriptWorn != 1 )
			WriteScript( item, RideVariables.VAR_WORN, 1 );
	}

	/// <summary>A gauge after an x87 sum, clamped to 0..100 (the clamps of <c>0x5E26AC</c>, <c>0x5E270D</c> and <c>0x5E4943</c>).</summary>
	// [BIN:TPI-EXE:0x5E26B4 gauge clamp] fcoms 100.0 with C0|C3 clear stores 100.0, fcoms 0.0 with C0 set stores 0 (an unordered NaN sets C0 too), else fstps
	// [EXT:hardware-d3d-path] the world update runs on the game thread after the Direct3D device set the x87 to 24-bit precision (SetCooperativeLevel without FPUPRESERVE, 0x6BF33F/0x6BF5A8): every
	// x87 step of the wear rounds to a single, as single-precision arithmetic does (tools/ppc-analysis/oracle_x86/tpi_ride_wear_oracle.py runs the routines at that control word)
	internal static float ClampThemeParkIncGauge( float sum ) => sum > 100f ? 100f : !(sum >= 0f) ? 0f : sum;

	/// <summary>
	/// The wear tick (<c>0x5E25B0</c>) of a ride whose level wears: while the ride runs (its script's VAR_RUNNING, or <c>Info.RunsContinuously</c>), every 64th turn the state of repair loses the
	/// wear and the life gauge 0.02 × the wear (none for a special ride); below <c>RideBreakDown.WarnPlayer</c> the ride is worn (VAR_WORN).
	/// </summary>
	// [BIN:TPI-EXE:0x5E25DA wear tick] Upgrades[level].WearRate (+0x2A4 + 72 × the level byte +0x64) zero (unsigned) returns; script variable 9 (VAR_RUNNING) above 0 or +0x43 bit 1
	// (Info.RunsContinuously, 0x5DB782); the turn & 0x3F zero (0x5E2614); variable 5 (VAR_ONRIDE) 0 gives the wear 0, else 0x5E21A0(the script's speed +0xC0 (0x6B0C30), variables 2 and 3 as floats, 0)
	// [BIN:TPI-EXE:0x5E26A6 wear gauges] +0x54 = clamp(+0x54 - wear); unless the type's +0x5C (Info.IsSpecial) is 1, +0x58 = clamp(+0x58 - 0.02f × wear) (0x5E26FB..0x5E273C); then the repair
	// byte below the type's +0x3CC (RideBreakDown.WarnPlayer) writes VAR_WORN = 1 (0x5E2761), and below +0x3C8 (RideBreakDown.Imminent) with the byte plus the wear not below it posts advice 0x40
	// [EXT:tpi-capability-gates] the advice 0x40 (0x71A930 -> 0x718820) is not posted: Theme Park Inc's advisor queue carries the forecasts only (the advisor's other messages); it draws nothing
	private void WearThemeParkInc( ParkObjectState item, EconomyObjectInfo info, long turn )
	{
		if ( info.Upgrades.Count == 0 )
			return;
		var level = info.Upgrades[Math.Min( item.Level, info.Upgrades.Count - 1 )];
		if ( level.WearRate == 0 )
			return;
		// an object no script runs reads 0 from every script variable (0x6B0C90 without a handle)
		if ( !RideOperations.TryGet( item.Id, out var operation ) )
			operation = default;
		if ( !operation.Running && !info.RunsContinuously )
			return;
		if ( (turn & 0x3F) != 0 )
			return;
		var wear = operation.Riders == 0 ? 0f : ThemeParkIncWearAmount( info, level, operation );
		item.Repair = ClampThemeParkIncGauge( -wear + (float)item.Repair );
		if ( !info.IsSpecial )
			item.LifeGauge = ClampThemeParkIncGauge( -(0.02f * wear) + (float)item.LifeGauge );
		var repair = RideBreakdownRules.GaugeByte( (float)item.Repair );
		if ( repair < info.BreakdownWarnPlayer )
			WriteScript( item, RideVariables.VAR_WORN, 1 );
	}

	/// <summary>
	/// The wear of one tick (<c>0x5E21A0</c>, called with its rider flag 0): a speed term and a rider term, each 0.9 × value / maximum plus 0.1, or halfway to 1.1 at or above the level's red
	/// line, times half the level's <c>WearRate</c>. Every step rounds to a single (the game thread's 24-bit x87, <see cref="ClampThemeParkIncGauge"/>).
	/// </summary>
	// [BIN:TPI-EXE:0x5E21A0 wear amount] riders = variable 5 clamped to 0..UsageInfo.MaxCapacity (+0x1E4, signed); s = single(speed / MaxSpeed (+0x1F4) × 0.9f), then single(s + 0.1f), or
	// single((s + 1.1f) × 0.5f) when Upgrades[level].RedLineSpeed (+0x2A0) <= speed; c = single(riders / MaxCapacity × 0.9f) (flag 0: fidivl), then single(c + 0.1f), or single((c + 1.1f) × 0.5f)
	// when RedLineCapacity (+0x290) <= riders × 0.1f; the result (c + s) × WearRate (+0x2A4) × 0.5f stays on the stack and the caller stores it as a single
	public static float ThemeParkIncWearAmount( EconomyObjectInfo info, UpgradeLevelInfo level, RideOperation operation )
	{
		var riders = operation.Riders < 0 ? 0 : operation.Riders > info.MaxCapacity ? info.MaxCapacity : operation.Riders;
		var speed = (float)operation.Speed;
		var s = speed / info.MaxSpeed * 0.9f;
		s = level.RedLineSpeed <= speed ? (s + 1.1f) * 0.5f : s + 0.1f;
		var c = riders / (float)info.MaxCapacity * 0.9f;
		c = level.RedLineCapacity <= riders * 0.1f ? (c + 1.1f) * 0.5f : c + 0.1f;
		return (c + s) * level.WearRate * 0.5f;
	}

	/// <summary>Whether the object's state of repair byte is below its type's <c>RideBreakDown.Imminent</c> line (<c>0x5E28A0</c>): a mechanic's search takes it and requests its maintenance.</summary>
	// [BIN:TPI-EXE:0x5E28A0 repair line] (u8)trunc(+0x54) as a float compared with the type's float +0x3C8: true when below
	internal bool BelowThemeParkIncRepairLine( ParkObjectState item ) =>
		Catalog.TryGet( item.InfoId, out var info ) && RideBreakdownRules.GaugeByte( (float)item.Repair ) < info.BreakdownImminent;

	/// <summary>
	/// A mechanic's search requests the object's maintenance (<c>0x5E3400(1)</c>): nothing when it is requested already; else the request is set and, unless the object is a bin or a plant,
	/// the object closes (the host closes it on <see cref="ParkEventKind.MaintenanceRequested"/>, as Theme Park World's request does).
	/// </summary>
	// [BIN:TPI-EXE:0x5E3400 maintenance request] +0x78 nonzero returns; "Object %d: wants maintenance", +0x78 = 1; +0x42 bit 0x40 (UsageInfo.HoldsLitter) or bit 0x1000 (UsageInfo.RequiresWatering)
	// returns; else "Closing...", the open word +0x7C = 0, the person being loaded +0x80 = 0, script variable 6 (VAR_RIDECLOSED) = 1 and the close model call 0x4AA970(.., 1), as Theme Park
	// World's 0x004DFE30
	internal void RequestThemeParkIncMaintenance( ParkObjectState item )
	{
		if ( item.MaintenanceRequested )
			return;
		item.MaintenanceRequested = true;
		if ( Catalog.TryGet( item.InfoId, out var info ) && (info.HoldsLitter || info.RequiresWatering) )
			return;
		Raise( ParkEventKind.MaintenanceRequested, 0, item.Id, item.InfoId );
	}

	/// <summary>
	/// A mechanic's repair ends (<c>0x5E2910</c>, a ride not being upgraded): the state of repair is full, VAR_WORN and VAR_BREAKSTAT clear, the claim and the request clear, and the ride
	/// opens (the host opens it on <see cref="ParkEventKind.RideRepaired"/> and runs the repair's closing tail).
	/// </summary>
	// [BIN:TPI-EXE:0x5E2910 repair fully] "Object %d: repairing fully"; +0x54 = 100.0, VAR_WORN (script variable 8) = 0, the claim word +0x72 = 0 and the request +0x78 = 0 (0x5E293D..0x5E2959);
	// state 2 takes the upgrade branch, unreachable here (see ThemeParkIncStaffWorld.FindRide); else 0x5E2C02 reads variables 7 and 4 (unused), writes VAR_BREAKSTAT = 0 and the fix model call,
	// then the open 0x5E1DC0 (the open word +0x7C = 1, VAR_RIDECLOSED = 0, SetState(0)) and the closing tail (Level.OpenAfterThemeParkIncRepair)
	// the claim turn +0x74 is not written, and nothing reads it while the claim word is 0 (0x5E37F0 returns at once), so it is cleared with the word
	internal void FinishThemeParkIncRepair( ParkObjectState item )
	{
		if ( ObjectState( item ) == RideObjectStates.Upgrading )
			throw new InvalidOperationException( "A Theme Park Inc upgrade needs a mechanic trained in Ride Upgrading, which is not ported." );
		item.Repair = RideRepair.FullGauge;
		WriteScript( item, RideVariables.VAR_WORN, 0 );
		item.MechanicId = 0;
		item.ClaimTurn = 0;
		item.MaintenanceRequested = false;
		WriteScript( item, RideVariables.VAR_BREAKSTAT, 0 );
		item.IsBrokenDown = false;
		Raise( ParkEventKind.RideRepaired, 0, item.Id, item.InfoId );
	}

	/// <summary>
	/// The end of a broken ride's repair timer (<c>0x5DA7E7</c>): VAR_BREAKSTAT clears, and the fix animation has ended once the script no longer reports VAR_BROKEN (an object no script runs
	/// reads 0).
	/// </summary>
	// [BIN:TPI-EXE:0x5E3BB0 fix animation] writes script variable 4 (VAR_BREAKSTAT) = 0; 0x5E3BE0 reads variable 7 (VAR_BROKEN), whose nonzero keeps the mechanic at work (the level mirrors it into
	// ScriptBroken each tick)
	internal bool ThemeParkIncFixAnimationEnded( ParkObjectState item )
	{
		WriteScript( item, RideVariables.VAR_BREAKSTAT, 0 );
		return !(item.ScriptHosted && item.ScriptBroken);
	}

	/// <summary>A mechanic claims the object (<c>0x5E3780</c>): the claim word takes his thing id and the claim turn the current turn.</summary>
	// [BIN:TPI-EXE:0x5E3780 claim] +0x72 = the mechanic's thing id (0x6375C0), +0x74 = the world turn (+0x1EAB40)
	internal void ClaimThemeParkInc( ParkObjectState item, int thingId, long turn )
	{
		item.MechanicId = thingId;
		item.ClaimTurn = turn;
	}

	/// <summary>The life gauge a newly built Theme Park Inc object starts with: its type's <c>Info.WorkingLife</c>, or 100 without one.</summary>
	// [BIN:TPI-EXE:0x5DB53A object life] the constructor stores 100.0 into +0x54 and +0x60, and +0x58 = the type's +0x8C (Info.WorkingLife) as a float, or 100.0 when it is 0
	internal static double ThemeParkIncStartingLife( EconomyObjectInfo info ) => info.WorkingLife == 0 ? 100.0 : (float)info.WorkingLife;
}
