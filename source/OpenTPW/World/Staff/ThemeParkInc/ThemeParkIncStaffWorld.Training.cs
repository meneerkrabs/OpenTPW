namespace OpenTPW;

/// <summary>The kind of staff room a thing's flag word marks (thing <c>+0x42</c>): a research room ("thinking space", 0x200) or a training room (0x400).</summary>
public enum ThemeParkIncRoomKind
{
	/// <summary><c>UsageInfo.ThinkingSpace</c> (type record <c>+0x1B4</c>), flag 0x200.</summary>
	Research,
	/// <summary><c>UsageInfo.TrainingRoom</c> (type record <c>+0x1B8</c>), flag 0x400.</summary>
	Training
}

/// <summary>A staff room: its thing and its entrance cell (thing word <c>+0x46</c>), the cell whose centre the staff walk to.</summary>
public readonly record struct ThemeParkIncRoom( ushort Thing, (int X, int Y) Entrance );

/// <summary>
/// A rest area as the rest-area finder reads it: its thing, the thing's cell (its bytes <c>+5</c> and <c>+7</c>), its entrance cell (<c>+0x46</c>) and its open word (<c>mCanLoad</c>, <c>+0x7C</c>).
/// </summary>
public readonly record struct ThemeParkIncRestArea( ushort Thing, (int X, int Y) Cell, (int X, int Y) Entrance, bool CanLoad );

/// <summary>The park's staff rooms as the staff read them: the first room of a kind, the rest areas, and a room's script variable 0, its occupancy.</summary>
public interface IThemeParkIncRooms
{
	/// <summary>The first thing of the attraction list (<c>park+0x1EAB88</c>, newest first) whose flag word marks <paramref name="kind"/>; null for none.</summary>
	ThemeParkIncRoom? First( ThemeParkIncRoomKind kind );

	/// <summary>Adds <paramref name="delta"/> to variable 0 of the room thing's script instance (<c>0x6B0740</c>, <c>0x6B0C90</c>, <c>0x6B0780</c>), when it has one.</summary>
	void AddOccupancy( ushort thing, int delta );

	/// <summary>
	/// Every thing of the attraction list (<c>park+0x1EAB88</c>, in its order) whose flag word has the rest-area bit (<c>+0x42</c> bit 2, <c>UsageInfo.ChillsYouOut</c>); none by default (a world without
	/// rest areas).
	/// </summary>
	IReadOnlyList<ThemeParkIncRestArea> RestAreas() => [];

	/// <summary>Variable 0 of the room thing's script instance (<c>0x6B0740</c>, <c>0x6B0C90</c>), its occupancy; 0 when it has none.</summary>
	int Occupancy( ushort thing ) => 0;

	/// <summary>A customer of the room thing (<c>0x5E5DA0</c>): its daily and monthly customer counts rise.</summary>
	void RecordCustomer( ushort thing ) { }
}

public sealed partial class ThemeParkIncStaffWorld
{
	/// <summary>The park's staff rooms; null in a world without them (tests without rooms).</summary>
	public IThemeParkIncRooms? Rooms { get; set; }

	/// <summary>The training notices since the HUD last took them (the advisor's lines 0x175 and 0x1C9 and the letters of kind 0xC and 0xD). Not saved; at most 32 are kept.</summary>
	public List<ThemeParkIncTrainingNotice> TrainingNotices { get; } = new();

	private void Notice( ThemeParkIncTrainingNotice notice )
	{
		TrainingNotices.Add( notice );
		if ( TrainingNotices.Count > 32 )
			TrainingNotices.RemoveRange( 0, TrainingNotices.Count - 32 );
	}

	/// <summary>The training room (<c>0x639AA0</c>): the first thing flagged 0x400, or null.</summary>
	// [BIN:TPI-EXE:0x639AA0 training room] the list park+0x1EAB88 (next +0x14) to the first thing whose word +0x42 has 0x400, set by the attraction builder from the type record's +0x1B8 (0x5DB79A),
	// UsageInfo.TrainingRoom; none writes 0. The functions 0x639AA0..0x639F40 do not read their this, so one room serves every trainee
	private ThemeParkIncRoom? TrainingRoom => Rooms?.First( ThemeParkIncRoomKind.Training );

	/// <summary>
	/// The "Select Ability To Learn" panel's choice of <paramref name="ability"/> for <paramref name="member"/> (<c>0x580550</c>): his own ability refuses, another one needs the retraining confirmation
	/// first, else he chooses it and walks to the training room. The panel lists only abilities he may learn (<see cref="ThemeParkIncTraining.Learnable"/>) and nothing re-tests them here.
	/// </summary>
	// [BIN:TPI-EXE:0x580550 ability chosen] +0x218 == the choice: text 714; +0x218 != 0: the confirmation 715 whose yes (0x5806B0) runs the rest; then (0x580613) a member with a chosen ability updates
	// his training-room panel slot (presentation), +0x22C = the choice (0x62B920) and 0x628180(7), even from state 8; no capacity, eligibility or experience test
	public ThemeParkIncTrainingChoice ChooseAbility( ParkEconomy economy, ThemeParkIncStaffMember member, int ability, bool confirmed, long turn )
	{
		if ( member.Ability == ability )
			return ThemeParkIncTrainingChoice.AlreadyTrained;
		if ( member.Ability != 0 && !confirmed )
			return ThemeParkIncTrainingChoice.NeedsConfirmation;
		member.ChosenAbility = ability;
		SetState( economy, member, ThemeParkIncTraining.ToTrainingRoom, turn );
		return ThemeParkIncTrainingChoice.Started;
	}

	/// <summary>State 7 entered (<c>0x628492</c>): a walk to the centre of the training room's entrance cell, the walk's speed bonus and the walk template.</summary>
	// [BIN:TPI-EXE:0x628492 state 7 entry] room = 0x639AA0, c = its word +0x46; the staff route 0x62EB10 to ((c - 1) & 0x7F, (c - 1) >> 7) + 0.5 in 8.8; a refused route sets +0x1A0 = 1 and calls
	// 0x60B7F0(c); 0x60B3F0(1) (+0xD6 = 25); the thought 0x1A (0x638960, presentation); template 1 (0x5D1000 without a handle, else 0x5D0F90(1))
	// [BIN:TPI-EXE:0x60B7F0 cell steer] the route to a cell's centre, Theme Park World's 0x004FA530: the bytes ((c - 1) & 0x7F, 0x80) and ((c - 1) >> 7, 0x80); the stamp counter raised (0x5D81F0); the
	// stranded mark +0x1B0 tested only while nonzero (0x60BB50), which a staff member's never is (StaffRoute); +0x28, +0x2A = the words, +0x1B0 = 0, SetDest 0x63EE80 (the point << 8). So the fallback
	// searches the same point again with the walk mask 1, which lets the walk leave the paths; a walk it cannot set either stands stuck, and the state-7 tick costs happiness
	private void EnterToTrainingRoom( ParkEconomy economy, ThemeParkIncStaffMember member )
	{
		if ( TrainingRoom is not { } room )
			throw new InvalidOperationException( "A Theme Park Inc staff member cannot walk to a training room the park does not have (the original reads thing 0's entrance word)." );
		if ( !StaffRoute( economy, member, room.Entrance, GuestWander.CentreByte, GuestWander.CentreByte ) )
		{
			member.Navigation!.Mask = 1;
			Route( member, room.Entrance, GuestWander.CentreByte, GuestWander.CentreByte );
		}
		member.SpeedBonus = BonusTable[1];
		RequestTemplate( member, 1 );
	}

	/// <summary>State 8 entered (<c>0x62852A</c>): the member disappears into the room and starts the chosen ability, or, with none chosen, the cancel runs and he still stays in state 8.</summary>
	// [BIN:TPI-EXE:0x62852A state 8 entry] the thought stops (0x638D10, 0x638D50, presentation); 0x5D1050 deletes the sprite source; +0x1A0 = 0; ThemeParkIncTraining.Enter (+0x22C == 0 runs 0x62E7A0,
	// whose own setter then is overwritten by the state 8 stored at 0x6285CD)
	private void EnterTraining( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		member.Sprite.Delete();
		// +0x1A0 = 0 (0x62854D): the walk mask is back to the paths' for the walks after the training
		member.Navigation!.Mask = 0;
		if ( !ThemeParkIncTraining.Enter( member ) )
			CancelTraining( economy, member, turn );
	}

	/// <summary>State 7's tick (<c>0x62A7C0</c>): arrived, the member enters the room (occupancy up, state 8, message 0x2A to the HQ); stuck, he loses happiness every tick.</summary>
	// [BIN:TPI-EXE:0x62A7C0 walk to training] the walk step 0x60B440: 0 -> 0x639CF0 (the room's script variable 0 += 1, answers 1, so the refused branch at 0x62A892 is dead), 0x628180(8), message 0x2A
	// (0x4363C0, the member's id) to the queue 0xA09728; 2 -> rand() % 50 picks the thought 0x1A (0 or 49) or 0x1E (25), then happiness -= HappyHitCosRoomInaccessible (0xA0225C), clamped; walking ->
	// nothing (no moving step)
	// the stuck branch's CRT rand() (0x870D38, seeded from the clock by srand(GetTickCount()) at 0x6A151B) only picks a thought bubble, which is not drawn (as the handyman's thoughts), so no draw is taken
	// (user decision 2026-11-21)
	private void UpdateToTrainingRoom( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		switch ( Walk( member ) )
		{
			case WalkResult.Arrived:
				if ( TrainingRoom is { } room )
					Rooms!.AddOccupancy( room.Thing, +1 );
				SetState( economy, member, ThemeParkIncTraining.Training, turn );
				economy.TpiStaff!.Hq.Release( economy.TpiStaff.Members, member );
				// [BIN:TPI-EXE:0x634771 special ride release] the HQ's release calls 0x6241B0 for a member with an ability before it looks at its own slots
				economy.ThemeParkIncReleaseSpecialRides( member );
				break;
			case WalkResult.Stuck:
				member.AddHappiness( -(float)settings.HappyHitRoomInaccessible );
				break;
		}
	}

	/// <summary>
	/// State 8's tick (<c>0x62AA50</c>): the training's progress; at its end the advisor's line, the finish (message 0x2B, the room, state 0), the reward and a look for work.
	/// </summary>
	// [BIN:TPI-EXE:0x62AA50 training tick] ThemeParkIncTraining.Tick; the end: the advisor text 0x175 with the new ability (0x4313D0, 0x71A930, 0x718820), 0x62AB90, ThemeParkIncTraining.Reward, the staff panel's
	// refresh 0x54D660 (presentation), then 0x62D810
	private void UpdateTraining( ParkEconomy economy, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, long turn )
	{
		if ( !ThemeParkIncTraining.Tick( member, settings ) )
			return;
		Notice( new ThemeParkIncTrainingNotice( member, true, member.Ability ) );
		FinishTraining( economy, member, turn );
		ThemeParkIncTraining.Reward( member, settings );
		LookForWork( economy, settings, member, turn );
	}

	/// <summary>The training's finish (<c>0x62AB90</c>): message 0x2B (the HQ counts and slots him), the letter, the room's occupancy down, the member shown again and set idle.</summary>
	// [BIN:TPI-EXE:0x62AB90 training finish] message 0x2B (0x436440, the id) to 0xA09728; message 0x19 with the letter 0x5CEFB0(0xC, member, ability); 0x639AA0's room script variable 0 -= 1 (0x62AD20);
	// 0x5D1000(3, +0x34, +0x30) makes the sprite source in template 3; 0x628180(0). No wage change
	private void FinishTraining( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		economy.TpiStaff!.Hq.TrainingFinished( member );
		economy.ThemeParkIncTrainingFinishedSpecialRides( member );
		if ( TrainingRoom is { } room )
			Rooms!.AddOccupancy( room.Thing, -1 );
		ShowAgain( member );
		SetState( economy, member, Idle, turn );
	}

	/// <summary>The member's sprite source made again in the stand template (<c>0x5D1000(3, +0x34, +0x30)</c>).</summary>
	private void ShowAgain( ThemeParkIncStaffMember member )
	{
		member.Template = IdleTemplate;
		member.Sprite.Delete();
		member.Sprite.Create( SpriteTypeOf( member ), VariantOf( member, SpriteBanks ), ProgramOf( IdleTemplate ), SpriteClockMilliseconds, SpriteBanks );
	}

	/// <summary>
	/// The training's cancel (<c>0x62E7A0</c>): nothing for a member learning nothing (also one still walking to the room); else the ability in training is dropped, the room's occupancy falls, a member in
	/// the room is shown again, the advisor's line and the letter go out, and he is set idle and looks for work.
	/// </summary>
	// [BIN:TPI-EXE:0x62E7A0 training cancel] +0x220 == 0: "Not learning..." and return; ab = +0x220, +0x220 = 0 (+0x224 kept); a training room: its script variable 0 -= 1; state 8: 0x5D1000(3, ...); the
	// advisor text 0x1C9 (0x5AEF20, 0x64EA00, 0x718820); message 0x19 with the letter 0x5CEFB0(0xD, member, ab); 0x628180(0); 0x62D810
	internal void CancelTraining( ParkEconomy economy, ThemeParkIncStaffMember member, long turn )
	{
		if ( member.LearningAbility == 0 )
			return;
		var ability = member.LearningAbility;
		member.LearningAbility = 0;
		if ( TrainingRoom is { } room )
			Rooms!.AddOccupancy( room.Thing, -1 );
		if ( member.State == ThemeParkIncTraining.Training )
			ShowAgain( member );
		Notice( new ThemeParkIncTrainingNotice( member, false, ability ) );
		SetState( economy, member, Idle, turn );
		LookForWork( economy, economy.TpiStaff!.Settings, member, turn );
	}

	/// <summary>
	/// A staff room's thing destroyed (<c>0x637C40</c>): message 0x21 first (a research room's makes the lab clear both teams, <c>0x61DB90</c>), then the class's destroy broadcasts message 0xA
	/// (<c>0x5DE780</c>), which cancels the trainees of a training room or sends the researchers out of a research room. Both reach their listeners at once, at the destroy.
	/// </summary>
	// [BIN:TPI-EXE:0x637CB9 message 0x21] the destroy's own message (vtable 0x93A3B8, type 0x21 at 0x436070) before the kind switch 0x637CD5; [BIN:TPI-EXE:0x5DE829 message 0xA] the attraction's destroy
	// (vtable 0x93A304, type 0xA at 0x435840); [BIN:TPI-EXE:0x61DC20 lab message 0x21] a destroyed kind-3 thing with +0x43 & 2 runs 0x61DB90
	public void RoomDestroyed( ParkEconomy economy, ThemeParkIncRoomKind kind, ushort thing, long turn )
	{
		if ( kind == ThemeParkIncRoomKind.Research )
		{
			economy.TpiResearch?.ResearchRoomRemoved();
			ResearchRoomRemoved( economy, thing, turn );
		}
		else
			TrainingRoomRemoved( economy, turn );
	}

	/// <summary>
	/// A training room's thing removed (the staff message 0xA, <c>0x626638</c>): every member walking to a training room or training is cancelled (the walk's cancel does nothing, as the original's).
	/// </summary>
	// [BIN:TPI-EXE:0x626638 room removed] the staff message 0xA: a thing that is not the member's rest area (+0x248), of kind 3 with +0x43 & 4 (a training room), and a member in state 7 or 8 runs 0x62E7A0
	public void TrainingRoomRemoved( ParkEconomy economy, long turn )
	{
		if ( economy.TpiStaff is not { } staff )
			return;
		// [BIN:TPI-EXE:0x436E97 message order] the staff hear it in ascending thing handle order
		foreach ( var member in staff.Members.Where( member => member.Placed && member.State is ThemeParkIncTraining.ToTrainingRoom or ThemeParkIncTraining.Training ).OrderBy( member => member.ThingId ).ToList() )
			CancelTraining( economy, member, turn );
	}
}
