namespace OpenTPW;

/// <summary>The mission controller's state word (<c>+0x14</c>, saved as <c>mCutsceneState</c>): what the camera that is flying shows, and what its end (message 0x29) does.</summary>
public enum ThemeParkIncMissionState : uint
{
	/// <summary>No camera of the controller's.</summary>
	Idle = 0,
	/// <summary>The researched obstacle is shown (message 0x23); the camera's end only resets the state.</summary>
	ObstacleResearched = 1,
	/// <summary>The player removes the obstacle (message 0x24); the camera's end opens it and completes the mission.</summary>
	ObstacleRemoved = 2,
	/// <summary>The completed mission's <c>Completion.ObstacleOpened</c> is shown; the camera's end opens it and completes the mission.</summary>
	ObstacleOpened = 3,
	/// <summary>The completed mission's <c>Completion.CutsceneObject</c> is shown; the camera's end deletes the saved thing and completes the mission.</summary>
	CutsceneObject = 4
}

/// <summary>A camera flight the controller asks the cutscene thing to play (<c>0x5D0400(thing, anim, mode)</c>).</summary>
public readonly record struct ThemeParkIncCameraRequest( ushort Thing, int Animation, int Mode );

/// <summary>The mission controller as a park save keeps it: the <c>OBJH</c> body's two fields.</summary>
public sealed record ThemeParkIncMissionControllerSnapshot( uint State, ushort CutsceneObject );

/// <summary>
/// The park and its world as Theme Park Inc's mission controller reads and changes them (docs/reverse/TPI-MISSIONS.md). The economy answers the money and the guests; the world answers the
/// attraction things (the obstacles), the land and the camera.
/// </summary>
public interface IThemeParkIncMissionHost
{
	/// <summary>The zone being viewed (<c>0xA06558 +0x14</c>): 0 arabian, 1 science, 2 water.</summary>
	int ViewedZone { get; }
	/// <summary>The bank balance (<c>0x5C0740</c>, bank <c>+0x14</c>).</summary>
	int BankBalance { get; }
	/// <summary>What the eight loans still owe (<c>0x5C1300</c>).</summary>
	int OutstandingLoans { get; }
	/// <summary>The guests in the park, every zone (<c>0x5B2060(4)</c>).</summary>
	int GuestsInPark { get; }
	/// <summary><c>ShareInfo.CompanySharesIssued</c> (<c>0xA02168</c>), which the shares statistic divides by.</summary>
	int CompanySharesIssued { get; }
	/// <summary>The current obstacle thing (<c>park+0x1EAB6E</c>), 0 for none.</summary>
	ushort CurrentObstacle { get; }
	/// <summary>Finds the current obstacle of <paramref name="type"/> (<c>0x6476F0</c>).</summary>
	void FindCurrentObstacle( int type );
	/// <summary>The first attraction thing of <paramref name="type"/> (<c>0x647850</c>), 0 for none.</summary>
	ushort FindAttraction( int type );
	/// <summary>Whether the object type <paramref name="type"/> opens automatically (its info <c>+0x138</c>, <c>Obstacle.OpenedAutomaticly</c>, is 1).</summary>
	bool OpensAutomatically( int type );
	/// <summary>
	/// Whether every slot of the staff HQ's obstacle table (<c>0x64E940() +0x14</c>, 20 × 12 bytes) is empty (kind 7) or holds a staff thing; null where the slots are not modelled.
	/// </summary>
	bool? ObstacleStaffSlotsFilled { get; }
	/// <summary>Opens the obstacle thing <paramref name="thing"/>: the camera target (<c>0x466050</c>), then <c>0x5E8BD0</c>.</summary>
	void OpenObstacle( ushort thing );
	/// <summary>Destroys the thing <paramref name="thing"/> (<c>0x637C40</c>).</summary>
	void DestroyThing( ushort thing );
	/// <summary>Loads <c>extents/extent.NNN</c> for <paramref name="index"/> with the land loader (<c>0x678B00</c>); its result is not looked at.</summary>
	void LoadExtent( int index );
	/// <summary>Asks the cutscene thing to play camera animation <paramref name="request"/> (<c>0x5D0400</c>); its end posts message 0x29 (<see cref="ThemeParkIncMissionController.CameraFinished"/>).</summary>
	void PlayCamera( ThemeParkIncCameraRequest request );
	/// <summary>The research lab's obstacle project (<c>0x61CBF0</c>): registered when the mission's obstacle needs research.</summary>
	void EnsureObstacleResearch();
	/// <summary>The "Objective Complete!" letter of mission <paramref name="completed"/> (<c>0x5CEAE0</c>).</summary>
	void PostObjectiveLetter( int completed );
	/// <summary>The first briefing after the zone arrival's flyover (<c>0x5CD6E0(0x5C, 5, 1)</c>).</summary>
	void PostIntroductionLetter();
	/// <summary>The controller's status broadcast (message 0x27).</summary>
	void StatusChanged();
	/// <summary>
	/// The mission status refresh (<c>0x54D660</c>) the message switch's end (<c>0x5F12DB</c>) and the daily evaluation (<c>0x5EFC7F</c>) call after message 0x27: its requirements-met advice; a host without
	/// an advisor ignores it.
	/// </summary>
	void StatusAdvice() { }
	/// <summary>Posts advice <paramref name="message"/> with its descriptor's score to the advisor thing (<c>0x71A930</c>, <c>0x718820</c>); a host without an advisor ignores it.</summary>
	bool PostAdvice( int message ) => false;
	/// <summary>Cancels advice <paramref name="message"/> on the advisor thing (<c>0x718990</c>); a host without an advisor ignores it.</summary>
	void CancelAdvice( int message ) { }
	/// <summary>Whether the player's history has advice <paramref name="message"/> played (<c>0x71B4F0</c> +8); false without an advisor.</summary>
	bool AdvicePlayed( int message ) => false;
}

/// <summary>
/// Theme Park Inc's mission controller (park thing class 21, stream tag <c>OBJH</c>; docs/reverse/TPI-MISSIONS.md): it starts and measures the mission's exit statistics once a game day, answers
/// the obstacle and promotion messages, tests the mission's requirements and runs the completion tick that spends the tickets, grows the land, opens a theme and advances the career.
/// </summary>
// [BIN:TPI-EXE:0x5EEDD0 controller constructor] the thing base (0x637100(1, 0x15)); +0x14 = 0, +0x18 = 0, +0x1A = 1, +0x1B = 0; subscribes to broadcasts 0x23, 0x24, 0x25, 0x26 and 0x29; then, unless
// the career's game is finished (+4), InitExitStatistics 0x5EEF60
public sealed class ThemeParkIncMissionController
{
	/// <summary>One game day in the park clock's 100 ns units (<c>0xC92A69C000</c>).</summary>
	public const long Day = ThemeParkIncChallenges.Day;

	private readonly ThemeParkIncCareer career;
	private readonly IThemeParkIncMissionHost host;

	/// <param name="startStatistics">False for a controller whose career brings its statuses from a save (a loaded controller is not constructed, 0x64D5B4).</param>
	public ThemeParkIncMissionController( ThemeParkIncCareer career, IThemeParkIncMissionHost host, long turn, bool startStatistics = true )
	{
		this.career = career ?? throw new ArgumentNullException( nameof( career ) );
		this.host = host ?? throw new ArgumentNullException( nameof( host ) );
		if ( startStatistics && !career.GameFinished )
			InitExitStatistics( turn );
	}

	/// <summary><c>+0x14</c>: the state word (<c>mCutsceneState</c>).</summary>
	public ThemeParkIncMissionState State { get; private set; }
	/// <summary><c>+0x18</c>: the thing the camera shows and message 0x29 acts on (<c>mCutsceneObject</c>).</summary>
	public ushort CutsceneObject { get; private set; }
	/// <summary><c>+0x1A</c>: register the obstacle research project on the next update.</summary>
	public bool RegisterObstacleResearch { get; private set; } = true;
	/// <summary><c>+0x1B</c>: the obstacle's research is complete (message 0x23), consumed by the next update.</summary>
	public bool ObstacleResearchComplete { get; private set; }

	/// <summary>The career this controller drives.</summary>
	public ThemeParkIncCareer Career => career;

	/// <summary>
	/// InitExitStatistics (<c>0x5EEF60</c>): every exit statistic of the viewed zone (or of zone 4, any) that has not started takes its baseline and starts today; then the mission's tech level
	/// is applied and the status broadcast goes out.
	/// </summary>
	// [BIN:TPI-EXE:0x5EEF60 init exit statistics] for i = 0..4: status +0x20 (zone) == 0x42A340() (the viewed zone, 0xA06558 +0x14) or == 4, and state +0 == 0; then by type (jump table 0x5EF394):
	// 1 and 2 the bank balance 0x5C0740, 5 the guests 0x5B2060(4), 4 the analyser's 0x5B9880, 3/6/7/8 zero, any other type is not started; 0x74C620(i, 0x6089C0() the clock now, baseline);
	// then message 0x27 (0x5EF25E), the tech level (0x5EF30B) and [BIN:TPI-EXE:0x5EF367 requirements advice reset] the advisor history entry of 0x1E3 (0x64EA00 -> 0x71B4F0) +8 = 0: the
	// requirements-met advice 0x1E3 is unplayed again for the new mission, so its callback 0x726F50 scores it and its group's SayOnlyOnce lets it through
	// [EXT:tpi-capability-gates] type 4's baseline is 0x5B9880 (see Measure), which OpenTPW cannot read exactly: a type-4 statistic is left unstarted and never measured. No shipped mission uses type 4
	public void InitExitStatistics( long turn )
	{
		var zone = host.ViewedZone;
		for ( var index = 0; index < ThemeParkIncCareer.StatusCount; index++ )
		{
			var statusZone = career.StatusWord( index, 8 );
			if ( statusZone != zone && statusZone != 4 || career.StatusWord( index, 0 ) != 0 )
				continue;
			int baseline;
			switch ( career.StatusWord( index, 1 ) )
			{
				case 1 or 2: baseline = host.BankBalance; break;
				case 5: baseline = host.GuestsInPark; break;
				case 3 or 6 or 7 or 8: baseline = 0; break;
				default: continue;
			}
			career.StartStatus( index, ThemeParkIncChallenges.Now( turn ), baseline );
		}
		host.StatusChanged();
		career.RaiseTechLevel();
		career.SetAdviceHistory( RequirementsMetAdvice, career.AdviceHistory( RequirementsMetAdvice ) with { Played = false } );
	}

	/// <summary>The requirements-met advice of the missions after the first two (<c>0x1E3</c>, posted by the status refresh <c>0x54D660</c>), whose played byte a new mission clears.</summary>
	public const int RequirementsMetAdvice = 0x1E3;

	/// <summary>
	/// The value of exit statistic <paramref name="index"/> as the daily evaluation measures it (<c>0x5EFF00</c>), or null for a type OpenTPW cannot measure (4, last month's takings). Any other
	/// type (0 or above 8) measures 0.
	/// </summary>
	// [BIN:TPI-EXE:0x5EFF00 statistic value] type - 1 through the jump table 0x5F000C: 1 (cash - loans > 0) as 0/1 (setg), 2 cash - loans (0x5C0740 - 0x5C1300, 32 bits), 3 the sum over zones 0..2
	// of the zone record's +0xF0 (0x74F4B0, ChallengesDone), 4 the analyser's 0x5B9880, 5 and 8 the guests 0x5B2060(4), 6 the career's awards held 0x74C860, 7 the shares owned 0x6220B0 × 100 /
	// [0xA02168] (0 when none); other types 0
	// [BIN:TPI-EXE:0x5B9880 analyser takings] the sum of the newest closed-month entry (head +0x244, or count + head once wrapped) of the analyser's rings at +0x20148 (admission, 0x5C0E10), +0x20398 (shops),
	// +0x205E8 (sideshows), +0x20838 (rides) and +0x20A88 (facilities; 0x5E5DE0 by the type's +0x600): last month's takings, revenue, not profit; the first three are Theme Park World's gate, shop and
	// sideshow rows moved by +0x268; a ring with no entry adds 0x10(%esp), the pushed ecx, which is the analyser's own address
	// [EXT:tpi-capability-gates] type 4 (OpenTPW's ledger books ride and facility takings among the other income, without rows of their own, and an empty ring adds a heap address) is not
	// measurable: it is never stored or evaluated; no shipped mission uses it
	// [BIN:TPI-EXE:0x5EFFC3 awards statistic] type 6: 0x42A3B0 (the career) -> 0x74C860, the awards held (ThemeParkIncCareer.AwardsHeld, written by the award checker ThemeParkIncParkAwards)
	// [BIN:TPI-EXE:0x5EFF9F shares statistic] type 7: [0xAA0128] -> 0x6220B0 (mNumberOwned); 0 answers 0 (je 0x5F0004), else owned × 100 (32 bits) / [0xA02168] (cltd, idivl)
	public int? Measure( int index )
	{
		switch ( career.StatusWord( index, 1 ) )
		{
			case 1: return unchecked( host.BankBalance - host.OutstandingLoans ) > 0 ? 1 : 0;
			case 2: return unchecked( host.BankBalance - host.OutstandingLoans );
			case 3:
			{
				var won = 0;
				for ( var zone = 0; zone < 3; zone++ )
				{
					if ( career.ZoneRecord( zone ) is not { } record )
						throw new InvalidOperationException( $"Zone {zone} has no career record (the original's 0x74F4B0 would return none and fault)." );
					won = unchecked( won + record.ChallengesDone );
				}
				return won;
			}
			case 4: return null;
			case 5 or 8: return host.GuestsInPark;
			case 6: return career.AwardsHeld;
			case 7:
			{
				var owned = career.Shares.Owned;
				if ( owned == 0 )
					return 0;
				if ( host.CompanySharesIssued == 0 )
					throw new InvalidOperationException( "ShareInfo.CompanySharesIssued is 0: the original's shares statistic (0x5EFFBA) divides by it and faults." );
				return unchecked( owned * 100 ) / host.CompanySharesIssued;
			}
			default: return 0;
		}
	}

	/// <summary>
	/// The controller's thing update (<c>0x5EF520</c>): the obstacle research project, the researched-obstacle camera, the objective letter and, every fourth world tick, the exit statistics' once-a-day
	/// measurement and test.
	/// </summary>
	// [BIN:TPI-EXE:0x63776F controller dispatch] the class-21 update is skipped when the mode word [0xC65B80] is 1 (an online game, which reads online_Standard.sam); OpenTPW's slice is the career
	// game, mode 0
	// [BIN:TPI-EXE:0x5EF520 controller update] returns when the career's +4 is set; +0x1A: the research lab's 0x61CBF0, then +0x1A = 0; +0x1B with no blocking interface state: 0x74C3C0, the panel
	// (missions 3/6/9/12: 0x1EB..0x1EE, else 0x172 or 0x19B), +0x14 = 1, 0x5D0400(park+0x1EAB6E, 1, 0), +0x1B = 0; the letter: career +0x20 == 0 and the counter != 0 posts 0x5CEAE0(counter - 1)
	// (0x5EF927) and sets +0x20 = 1; then, only when (park+0x1EAB40 & 3) == 0, the exit statistics (0x5EF9C8..0x5EFC88)
	// [BIN:TPI-EXE:0x5EF5CA interface gate] the researched-obstacle step waits while 0x42F2C0's objects report a modal state, 0x50DE30, 0x501440, 0x54FBB0 or 0x52FD90 answer nonzero, [0xA1465C] & 0x16
	// is set or the game state 0x674280 is 2 or 0xB: interface windows and game states OpenTPW's text HUD ([EXT:tpi-text-hud]) does not have, so nothing blocks it
	public void Update( long turn )
	{
		if ( career.GameFinished )
			return;
		if ( RegisterObstacleResearch )
		{
			host.EnsureObstacleResearch();
			RegisterObstacleResearch = false;
		}
		if ( ObstacleResearchComplete )
		{
			career.DiscoverObstacleAbilities();
			host.PostAdvice( ObstacleResearchedAdvice( career.MissionCounter, host.AdvicePlayed( 0x19B ) ) );
			State = ThemeParkIncMissionState.ObstacleResearched;
			host.PlayCamera( new ThemeParkIncCameraRequest( host.CurrentObstacle, 1, 0 ) );
			ObstacleResearchComplete = false;
		}
		if ( !career.ObjectiveLetterPosted && career.MissionCounter != 0 )
		{
			host.PostObjectiveLetter( unchecked( career.MissionCounter - 1 ) );
			career.ObjectiveLetterPosted = true;
		}
		if ( (turn & 3) == 0 )
			EvaluateExitStatistics( turn );
	}

	/// <summary>
	/// The advice the researched-obstacle step posts (<c>0x5EF6E2</c>): <c>0x1EB</c>..<c>0x1EE</c> after missions 3, 6, 9 and 12 (the counter), else <c>0x172</c> once <c>0x19B</c> has been played and
	/// <c>0x19B</c> before.
	/// </summary>
	// [BIN:TPI-EXE:0x5EF6E2 obstacle panel advice] the counter +0x105C - 3 through the byte table 0x5EFCBC (0, 4, 4, 1, 4, 4, 2, 4, 4, 3) and the jump table 0x5EFCA8: counters 3, 6, 9 and 12 post
	// 0x1EB, 0x1EC, 0x1ED and 0x1EE; any other (case 4, and a counter outside 3..12) reads the history's played byte of 0x19B (0x71B4F0 +8): set posts 0x172, else 0x19B; each with
	// 0x71A930(message, locate 0, 0, variant -1, bypass 0, score -1) -> 0x718820, after 0x74C3C0 and before +0x14 = 1 and the camera
	public static int ObstacleResearchedAdvice( int counter, bool played19B ) => counter switch
	{
		3 => 0x1EB,
		6 => 0x1EC,
		9 => 0x1ED,
		12 => 0x1EE,
		_ => played19B ? 0x172 : 0x19B
	};

	// [BIN:TPI-EXE:0x5EFA14 daily evaluation] for i = 0..4: type (+4) != 0, zone (+0x20) == the viewed zone or 4, state (+0) == 1; days = (now - start) / 0xC92A69C000 (0x7D8E30, __alldiv, the low
	// dword); value = 0x5EFF00; when days != +0x1C: 0x74C5B0(i, days, value); if days >= TargetTime (+0xC, signed): 0x74C670 -> state 2, else TargetTime != 0 -> state 3; then message 0x27
	// and 0x54D660
	private void EvaluateExitStatistics( long turn )
	{
		var zone = host.ViewedZone;
		for ( var index = 0; index < ThemeParkIncCareer.StatusCount; index++ )
		{
			var statusZone = career.StatusWord( index, 8 );
			if ( career.StatusWord( index, 1 ) == 0 || statusZone != zone && statusZone != 4 || career.StatusWord( index, 0 ) != 1 )
				continue;
			var days = unchecked( (int)((ThemeParkIncChallenges.Now( turn ) - career.StatusStart( index )) / Day) );
			if ( Measure( index ) is not { } value || days == career.StatusWord( index, 7 ) )
				continue;
			career.StoreStatus( index, days, value );
			var targetTime = career.StatusWord( index, 3 );
			if ( days >= targetTime )
			{
				if ( career.StatusReached( index ) )
					career.SetStatusState( index, 2 );
				else if ( targetTime != 0 )
					career.SetStatusState( index, 3 );
			}
			host.StatusChanged();
			host.StatusAdvice();
		}
	}

	/// <summary>
	/// The obstacle's state (<c>0x5F01D0</c>): 0 no obstacle (none, or a mission without research), 1 not researched, 2 researched, 3 opened automatically in the ending zone, 4 removed.
	/// </summary>
	// [BIN:TPI-EXE:0x5F01D0 obstacle state] Type 0 or NoResearch != 0: 0; else researched (+0x13E4) ? 2 : 1, then 3 when the current zone record's +0x64 (through 0x74F4B0) == EndingTheme and the
	// type's info +0x138 == 1, then 4 when +0x13E5 is set
	public int ObstacleState()
	{
		if ( !career.HasObstacle )
			return 0;
		var state = career.ObstacleResearched ? 2 : 1;
		if ( CurrentZoneNumber() == career.Mission.EndingTheme && host.OpensAutomatically( career.Mission.ObstacleType ) )
			state = 3;
		if ( career.ObstacleRemoved )
			state = 4;
		return state;
	}

	/// <summary>
	/// IsMissionComplete (<c>0x5F0600</c>): every used exit statistic achieved, the required challenges won in their zone, the required rank held, the obstacle removed (when the mission has one) and
	/// enough golden tickets.
	/// </summary>
	// [BIN:TPI-EXE:0x5F0600 mission complete] false once +4 is set; ok = every status with type != 0 in state 2; RequiredChallenges.Number != 0: ok &= Number <= 0x74F4B0(Theme) +0xF0 (signed jg);
	// Promotion.Number != 0: ok &= Number <= rank (+0x1060); HasObstacle: ok &= +0x13E5; finally ok &= tickets (+0x1074) >= Obstacle.GoldenTickets (signed jl)
	public bool IsMissionComplete()
	{
		if ( career.GameFinished )
			return false;
		var ok = true;
		for ( var index = 0; index < ThemeParkIncCareer.StatusCount; index++ )
			if ( career.StatusWord( index, 1 ) != 0 )
				ok = ok && career.StatusWord( index, 0 ) == 2;
		var mission = career.Mission;
		if ( mission.RequiredChallengesNumber != 0 )
		{
			if ( career.ZoneRecord( mission.RequiredChallengesTheme ) is not { } record )
				throw new InvalidOperationException( $"The required challenges' zone {mission.RequiredChallengesTheme} has no career record (the original's 0x74F4B0 would return none and fault)." );
			ok = ok && mission.RequiredChallengesNumber <= record.ChallengesDone;
		}
		if ( mission.PromotionNumber != 0 )
			ok = ok && mission.PromotionNumber <= career.Rank;
		if ( career.HasObstacle && !(ok && career.ObstacleRemoved) )
			return false;
		return ok && career.GoldenTickets >= mission.ObstacleGoldenTickets;
	}

	/// <summary>
	/// MetRequirements (<c>0x55A620</c>), which enables the mission window's complete button: in the ending zone (or <c>EndingTheme</c> 3); an obstacle mission needs the obstacle researched, the staff
	/// slots filled and enough golden tickets; any other mission IsMissionComplete.
	/// </summary>
	// [BIN:TPI-EXE:0x55A620 met requirements] EndingTheme != the current zone record's +0x64 and != 3: false ("not in correct theme"); HasObstacle (0x5F0190): obstacle state (0x5F01D0) != 2: false;
	// any of the 20 staff HQ slots (0x64E940 +0x14, stride 12) with +0 != 7 and word +8 == 0: false ("some slots not filled"); tickets (0x5F05E0) < Obstacle.GoldenTickets: false; else true without
	// 0x5F0600; not an obstacle mission: 0x5F0600
	// the slots are the staff HQ's (ThemeParkIncStaffHeadquarters.AllFilled); a park without the HQ (no Theme Park Inc staff, IThemeParkIncMissionHost.ObstacleStaffSlotsFilled null) never meets an obstacle mission
	public bool MetRequirements()
	{
		var ending = career.Mission.EndingTheme;
		if ( ending != CurrentZoneNumber() && ending != 3 )
			return false;
		if ( !career.HasObstacle )
			return IsMissionComplete();
		if ( ObstacleState() != 2 || host.ObstacleStaffSlotsFilled != true )
			return false;
		return career.GoldenTickets >= career.Mission.ObstacleGoldenTickets;
	}

	/// <summary>
	/// OnMissionComplete (<c>0x5F0770</c>), the mission window's complete button for a mission without an obstacle: nothing outside the ending zone; else the camera flies to the
	/// <c>Completion.ObstacleOpened</c> thing (state 3) or the <c>Completion.CutsceneObject</c> thing (state 4) and its end completes the mission, or, with neither, the mission completes at once.
	/// False when the zone is wrong.
	/// </summary>
	// [BIN:TPI-EXE:0x5F0770 on mission complete] the current zone record's +0x64 != EndingTheme and EndingTheme != 3: logs "Mission is complete but we're in the wrong theme!!!" and returns; the words
	// ObstacleOpened (+0x10D4) and CutsceneObject (+0x10D0) are read as u16; ObstacleOpened != 0: 0x647850 finds the first attraction of the type; found: +0x18 = it, +0x14 = 3, 0x5D0400(it, 2, 0), and
	// with counter 0 the advice 0x26C; not found: the tick 0x5F0940; else CutsceneObject != 0 and found: +0x14 = 4 (+0x18 is not set), 0x5D0400(it, 2, 0); else the tick
	public bool OnMissionComplete( long turn )
	{
		var ending = career.Mission.EndingTheme;
		if ( CurrentZoneNumber() != ending && ending != 3 )
		{
			Log?.Trace( "Mission is complete but we're in the wrong theme!!!" );
			return false;
		}
		var opened = unchecked( (ushort)career.Mission.ObstacleOpened );
		var cutscene = unchecked( (ushort)career.Mission.CutsceneObject );
		if ( opened != 0 )
		{
			var thing = host.FindAttraction( opened );
			if ( thing != 0 )
			{
				CutsceneObject = thing;
				State = ThemeParkIncMissionState.ObstacleOpened;
				host.PlayCamera( new ThemeParkIncCameraRequest( thing, 2, 0 ) );
				// [BIN:TPI-EXE:0x5F0827 obstacle opened advice] with the career's counter +0x105C at 0: 0x71A930(0x26C, locate 0, 0, variant -1, bypass 0, score -1) -> 0x718820, after the flight
				// started (its class 6 is admitted only by a flight of mode 0, so the mode-2 flight playing now scores it -1)
				if ( career.MissionCounter == 0 )
					host.PostAdvice( 0x26C );
				return true;
			}
		}
		else if ( cutscene != 0 && host.FindAttraction( cutscene ) is var shown and not 0 )
		{
			State = ThemeParkIncMissionState.CutsceneObject;
			host.PlayCamera( new ThemeParkIncCameraRequest( shown, 2, 0 ) );
			return true;
		}
		CompleteMission( turn );
		return true;
	}

	/// <summary>
	/// The completion tick (<c>0x5F0940</c>): the career's side (tickets, theme, end of game or advance) with the world's: the new extent with the counter before the advance, then after an advance the
	/// next obstacle thing and the new mission's exit statistics. The outcome is the career's.
	/// </summary>
	// [BIN:TPI-EXE:0x5F0940 complete mission] returns at once when +4 is set; 0x74C8C0(Obstacle.GoldenTickets) (result ignored); LoadNewHoardings == 1: "%sextent.%03d" (0x9CF714) with the data root
	// (0x771420) and the counter +0x105C before the advance, through the land loader 0x678B00; ThemeOpened == 1: 0x74C490(ThemeNumber); EndOfGame == 1: the end object 0x5CD6E0(0x5D, 6, 1) is
	// posted, +4 = 1, "Game is finished!"; else 0x74C1B0, the park's 0x6476F0, 0x5EEF60, +0x20 = 0 and message 0x2C (0x4364C0); then the refresh 0x4A5E30, 0x648C90 (presentation)
	// [EXT:tpi-capability-gates] the end-of-game object 0x5CD6E0(0x5D, 6, 1) (the ending sequence) and message 0x2C, which nothing in OpenTPW receives, are not posted
	public ThemeParkIncCompletion CompleteMission( long turn )
	{
		if ( career.GameFinished )
			return ThemeParkIncCompletion.None;
		// the extent is loaded between the ticket spend and the theme; neither of those reads the land, so the career's side runs first and the world's land after it, with the counter it reports
		var completion = career.CompleteMission();
		if ( completion.Extent >= 0 )
			host.LoadExtent( completion.Extent );
		if ( completion.Advanced )
		{
			host.FindCurrentObstacle( career.Mission.ObstacleType );
			InitExitStatistics( turn );
		}
		return completion;
	}

	/// <summary>
	/// The end of the zone arrival's flyover (<c>0x5F1760</c>, called by the cutscene's mode-2 end): on mission 0, unless the career's <c>+0x20</c> byte is set, the first briefing goes out and the byte is set.
	/// </summary>
	// [BIN:TPI-EXE:0x5F1760 arrival briefing] career +0x20 == 1: nothing; counter (+0x105C) != 0: nothing; else a 0x24-byte message object 0x5CD6E0(0x5C, 5, 1) is posted and +0x20 = 1 (0x5F18DA, 0x5F18F7)
	public void IntroductionEnded()
	{
		if ( career.ObjectiveLetterPosted || career.MissionCounter != 0 )
			return;
		host.PostIntroductionLetter();
		career.ObjectiveLetterPosted = true;
	}

	/// <summary>Message 0x23: the obstacle's research is complete; the next update shows it.</summary>
	// [BIN:TPI-EXE:0x5F0F8D research complete] case 0x23: +0x1B = 1
	public void ObstacleResearched()
	{
		ObstacleResearchComplete = true;
		host.StatusChanged();
		host.StatusAdvice();
	}

	/// <summary>Message 0x24 with the obstacle thing <paramref name="thing"/>: the player removes it; the camera flies to it, and its end opens it and completes the mission.</summary>
	// [BIN:TPI-EXE:0x5F0F96 remove obstacle] logs "Obstacle Removed Message Received"; the advice 0x173 when the history's played byte of 0x19C (0x71B4F0 +8) is set (0x71A930(0x173, locate 0, 0,
	// variant -1, bypass 0, score -1) -> 0x718820); +0x14 = 2, +0x18 = the message's thing (word +8), 0x5D0400(thing, 2, 0); 0x74C430 (career +0x13E5 = 1); with the counter +0x105C at 1 the
	// advice 0x21D the same way, after the flight started (its class 6 is admitted only by a flight of mode 0, so the mode-2 flight scores it -1)
	public void RemoveObstacle( ushort thing )
	{
		Log?.Trace( "Obstacle Removed Message Received" );
		if ( host.AdvicePlayed( 0x19C ) )
			host.PostAdvice( 0x173 );
		State = ThemeParkIncMissionState.ObstacleRemoved;
		CutsceneObject = thing;
		host.PlayCamera( new ThemeParkIncCameraRequest( thing, 2, 0 ) );
		career.MarkObstacleRemoved();
		if ( career.MissionCounter == 1 )
			host.PostAdvice( 0x21D );
		host.StatusChanged();
		host.StatusAdvice();
	}

	/// <summary>Message 0x25: a promotion to <paramref name="rank"/>.</summary>
	// [BIN:TPI-EXE:0x5F10F7 promotion] logs "Promotion Message Received"; 0x74C450(the message's rank, dword +8); then 0x71A930(0x1A4, locate 0, 0, variant -1, bypass 0, score -1) -> 0x718820
	/// <returns>False when the promotion's firing line word is one OpenTPW cannot read (<see cref="ThemeParkIncCareer.Promote"/>).</returns>
	public bool Promotion( int rank )
	{
		Log?.Trace( "Promotion Message Received" );
		var read = career.Promote( rank );
		host.PostAdvice( 0x1A4 );
		host.StatusChanged();
		host.StatusAdvice();
		return read;
	}

	/// <summary>Message 0x26: a challenge was won; the controller has no case for it and only broadcasts its status.</summary>
	// [BIN:TPI-EXE:0x5F0F86 message switch] 0x26 falls to the default of the jump table 0x5F12F4: only the closing 0x27 broadcast (0x5F126B)
	public void ChallengeWon()
	{
		host.StatusChanged();
		host.StatusAdvice();
	}

	/// <summary>
	/// Message 0x29, posted at the end of every camera flight: in state 2 or 3 the shown obstacle opens and the mission completes, in state 4 the saved thing is destroyed and the mission completes;
	/// then the state and the thing clear.
	/// </summary>
	// [BIN:TPI-EXE:0x5F118D camera finished] the message's own thing is not read; +0x14 == 2 or 3: 0x466050(things[+0x18]), things[+0x18]->0x5E8BD0(), 0x5F0940; == 4: 0x637C40(things[+0x18]), 0x5F0940;
	// then +0x14 = 0, +0x18 = 0 and the advice 0x172, 0x19B, 0x1E3, 0x20F and 0x210 is cancelled in that order (0x718990); the 0x27 broadcast
	public void CameraFinished( long turn )
	{
		switch ( State )
		{
			case ThemeParkIncMissionState.ObstacleRemoved or ThemeParkIncMissionState.ObstacleOpened:
				host.OpenObstacle( CutsceneObject );
				CompleteMission( turn );
				break;
			case ThemeParkIncMissionState.CutsceneObject:
				host.DestroyThing( CutsceneObject );
				CompleteMission( turn );
				break;
		}
		State = ThemeParkIncMissionState.Idle;
		CutsceneObject = 0;
		foreach ( var message in FlightCancels )
			host.CancelAdvice( message );
		host.StatusChanged();
		host.StatusAdvice();
	}

	/// <summary>The advice the end of a camera flight cancels (<c>0x5F11F8</c>..<c>0x5F1266</c>), in that order.</summary>
	public static readonly IReadOnlyList<int> FlightCancels = new[] { 0x172, 0x19B, 0x1E3, 0x20F, 0x210 };

	// [BIN:TPI-EXE:0x5F077E current zone] the career's current record (+0x4C) +0x64 goes through 0x74F4B0 and that record's +0x64 is compared
	private int CurrentZoneNumber()
	{
		if ( career.CurrentZone is not { } current || current.ParkNumber is < 0 or >= 4 || career.ZoneRecord( current.ParkNumber ) is not { } record )
			throw new InvalidOperationException( "The mission controller needs the career's current zone record (the original's 0x74F4B0 would return none and fault)." );
		return record.ParkNumber;
	}

	/// <summary>Takes the thing's fields of <paramref name="other"/> (the same park thing, now with another career).</summary>
	internal void TakeFields( ThemeParkIncMissionController other )
	{
		State = other.State;
		CutsceneObject = other.CutsceneObject;
		RegisterObstacleResearch = other.RegisterObstacleResearch;
		ObstacleResearchComplete = other.ObstacleResearchComplete;
	}

	internal ThemeParkIncMissionControllerSnapshot Capture() => new( (uint)State, CutsceneObject );

	internal static bool IsValid( ThemeParkIncMissionControllerSnapshot? state ) => state == null || state.State <= (uint)ThemeParkIncMissionState.CutsceneObject;

	/// <summary>
	/// Restores the saved fields (<c>0x5F03C0</c>): the state and the thing; the load then sets <c>+0x1B = 0</c> and <c>+0x1A = 1</c>, so a pending "research complete" is lost and the next update
	/// registers the research project again.
	/// </summary>
	// [BIN:TPI-EXE:0x5F03C0 controller load] versions 0 and 1 read mCutsceneObject (u16, +0x18) and mCutsceneState (u32, +0x14) after the thing base; after a read +0x1B = 0 and +0x1A = 1
	internal void Restore( ThemeParkIncMissionControllerSnapshot state )
	{
		State = (ThemeParkIncMissionState)state.State;
		CutsceneObject = state.CutsceneObject;
		ObstacleResearchComplete = false;
		RegisterObstacleResearch = true;
	}
}
