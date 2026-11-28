namespace OpenTPW;

/// <summary>
/// The world side of Theme Park Inc's mission controller: the attraction things the obstacles are, the land and the camera (docs/reverse/TPI-MISSIONS.md). A level implements it; a headless economy
/// has none.
/// </summary>
public interface IThemeParkIncMissionWorld
{
	/// <summary>The current obstacle thing (<c>park+0x1EAB6E</c>, the World variable <c>CurrentObstacle</c>), 0 for none.</summary>
	ushort CurrentObstacle { get; }
	/// <summary>Finds the current obstacle of <paramref name="type"/> (<c>0x6476F0</c>).</summary>
	void FindCurrentObstacle( int type );
	/// <summary>The first attraction thing of <paramref name="type"/> (<c>0x647850</c>), 0 for none.</summary>
	ushort FindAttraction( int type );
	/// <summary>Whether the object type <paramref name="type"/>'s <c>Obstacle.OpenedAutomaticly</c> (info <c>+0x138</c>) is 1.</summary>
	bool OpensAutomatically( int type );
	/// <summary>Opens the obstacle thing <paramref name="thing"/> (<c>0x466050</c>, <c>0x5E8BD0</c>).</summary>
	void OpenObstacle( ushort thing );
	/// <summary>Destroys the thing <paramref name="thing"/> (<c>0x637C40</c>).</summary>
	void DestroyThing( ushort thing );
	/// <summary>Loads <c>extents/extent.NNN</c> (<c>0x678B00</c>).</summary>
	void LoadExtent( int index );
}

public sealed partial class ParkEconomy
{
	/// <summary>Theme Park Inc's mission controller (park thing class 21, <c>OBJH</c>); null in Theme Park World and in a park without a career.</summary>
	public ThemeParkIncMissionController? TpiMissions { get; private set; }

	/// <summary>The world the mission controller acts on (a level's obstacles, land and camera); null in a headless economy.</summary>
	internal IThemeParkIncMissionWorld? TpiMissionWorld { get; set; }

	/// <summary>The controller's status broadcasts (message 0x27) so far: the HUD redraws its mission lines when it changes. Not saved.</summary>
	internal int TpiMissionRevision { get; private set; }

	/// <summary>The latest <see cref="MaxTpiMissionNotices"/> camera flights the controller asked for (<see cref="TpiCutscene"/> plays them). Not saved.</summary>
	internal List<ThemeParkIncCameraRequest> TpiCameraRequests { get; } = new();

	/// <summary>The latest <see cref="MaxTpiMissionNotices"/> camera ends (message 0x29: the thing and the result). Not saved.</summary>
	internal List<ThemeParkIncCameraEnd> TpiCameraEnds { get; } = new();

	/// <summary>Theme Park Inc's cutscene thing (park thing class 22, <c>CUTS</c>): the camera flights; null wherever <see cref="TpiMissions"/> is.</summary>
	public ThemeParkIncCutscene? TpiCutscene { get; private set; }

	/// <summary>The world the cutscene's flights use (a level's things and ride view); null in a headless economy.</summary>
	internal IThemeParkIncCutsceneWorld? TpiCutsceneWorld
	{
		get => TpiCutscene?.World;
		set
		{
			if ( TpiCutscene != null )
				TpiCutscene.World = value;
		}
	}

	/// <summary>The camera requests and ends kept.</summary>
	internal const int MaxTpiMissionNotices = 32;

	private static void AddTpiMissionNotice<T>( List<T> list, T notice )
	{
		if ( list.Count >= MaxTpiMissionNotices )
			list.RemoveAt( 0 );
		list.Add( notice );
	}

	// [BIN:TPI-EXE:0x648633 controller thing] the new-world init builds the mission controller after the challenge system (0x6485A9) and stores its id at park+0x1EAB6C; its constructor reads the
	// career and the park (InitExitStatistics)
	private void CreateThemeParkIncMissions()
	{
		// [EXT:headless-economy] a headless economy over settings without the zones' global.sam (tests and tools) has no zone records: its controller starts no statistics
		if ( TpiCareer is not { } career )
			return;
		TpiMissions = new ThemeParkIncMissionController( career, new ThemeParkIncMissionHost( this ), Turn, startStatistics: !TpiHeadlessCareer );
		// [BIN:TPI-EXE:0x64860F cutscene thing] the factory builds the cutscene thing just before the mission controller; every camera end posts message 0x29 to it (0x5D0996)
		TpiCutscene = new ThemeParkIncCutscene( ThemeParkIncCameraFinished )
		{
			ArrivalEnded = () => TpiMissions?.IntroductionEnded(),
			ViewedZone = () => new ThemeParkIncMissionHost( this ).ViewedZone
		};
	}

	/// <summary>Message 0x29 from the cutscene thing: the mission controller's camera end.</summary>
	private void ThemeParkIncCameraFinished( ThemeParkIncCameraEnd end )
	{
		AddTpiMissionNotice( TpiCameraEnds, end );
		TpiMissions?.CameraFinished( Turn );
	}

	/// <summary>
	/// The zone arrival's flyover when a Theme Park Inc world is entered (<c>0x6A43D8</c>): the viewed zone's camera path flies while the park clock is on its first day. False when nothing flew.
	/// </summary>
	// [BIN:TPI-EXE:0x608C70 days since start] the clock's turn time (world +0x1EAB40 × rate / 4 × 10 000 000) from the clock's base, divided by the day 0xC92A69C000
	internal bool StartThemeParkIncArrival()
	{
		if ( TpiCutscene is not { } cutscene )
			return false;
		var days = (ThemeParkIncChallenges.Now( Turn ) - ThemeParkIncChallenges.Now( 0 )) / ThemeParkIncMissionController.Day;
		return cutscene.PlayArrival( new ThemeParkIncMissionHost( this ).ViewedZone, days, type => TpiMissionWorld?.FindAttraction( type ) ?? 0 );
	}

	/// <summary>
	/// Rebuilds the controller for <paramref name="career"/> without starting its statistics: the career attached after the park was built (Load A Game's player file, which the original reads
	/// before the park file, 0x7451E0) brings its own statuses, and a loaded controller is never constructed (0x64D5B4). The controller is the park's thing: its own fields stay.
	/// </summary>
	private void AttachThemeParkIncMissions( ThemeParkIncCareer career )
	{
		var attached = new ThemeParkIncMissionController( career, new ThemeParkIncMissionHost( this ), Turn, startStatistics: false );
		TpiTagTube ??= new ThemeParkIncTagTube();
		if ( TpiMissions is { } park )
			attached.TakeFields( park );
		TpiMissions = attached;
	}

	/// <summary>The controller's thing update, then the cutscene thing's: once per world update pass, before the challenge system's (they are the newer things), not in a lost park.</summary>
	// [BIN:TPI-EXE:0x6495C1 thing pass] newest first: the mission controller (0x648633), then the cutscene thing (0x64860F, update 0x5D02F0), then the challenge system
	private void UpdateThemeParkIncMissions( long turn )
	{
		TpiMissions!.Update( turn );
		TpiCutscene?.Update();
	}

	/// <summary>The mission's requirements are met (<c>0x55A620</c>): the mission window's complete button would be enabled.</summary>
	public bool ThemeParkIncMissionMet => TpiMissions is { } missions && TpiCareer?.CurrentZone != null && missions.MetRequirements();

	/// <summary>
	/// The mission window's complete button (Proceed): when the requirements are met, an obstacle mission sends message 0x24 with the current obstacle (the staff HQ rebuilds its slots, then the
	/// controller flies the camera to the obstacle, and the flight's end opens it and completes the mission), any other mission runs OnMissionComplete (its <c>Completion.ObstacleOpened</c> camera, or the
	/// tick at once). False, and nothing done, when the requirements are not met; the window closes either way.
	/// </summary>
	// [BIN:TPI-EXE:0x55BD79 complete button] 0x55A620 false: nothing; HasObstacle (0x5F0190): message 0x24 (0x436140, vtbl 0x93A3D0) with park+0x1EAB6E through the dispatcher 0xA09728, which calls its
	// listeners at once in ascending thing handle order (ThemeParkIncObstacleRemoved); else the controller's 0x5F0770; then the window is destroyed (0x85049F(.., 4, 0, 0)) and 0xA61810 = 0
	public bool PressThemeParkIncMissionComplete()
	{
		if ( !ThemeParkIncMissionMet )
			return false;
		if ( TpiCareer!.HasObstacle )
			ThemeParkIncObstacleRemoved( TpiMissionWorld?.CurrentObstacle ?? 0 );
		else
			TpiMissions!.OnMissionComplete( Turn );
		return true;
	}

	/// <summary>The park as the mission window reads it; null without a mission controller.</summary>
	internal IThemeParkIncMissionWindowWorld? TpiMissionWindowWorld( string playerName ) => TpiMissions == null ? null : new ThemeParkIncMissionWindowWorld( this, playerName );

	private sealed class ThemeParkIncMissionWindowWorld( ParkEconomy economy, string playerName ) : IThemeParkIncMissionWindowWorld
	{
		public ThemeParkIncMissionController Missions => economy.TpiMissions!;

		public long Turn => economy.Turn;

		public ushort CurrentObstacle => economy.TpiMissionWorld?.CurrentObstacle ?? 0;

		// [BIN:TPI-EXE:0x61EB30 obstacle project] the research manager's first project of kind 5 whose progress is below its effort (stride 0x24 from +0x8C, count +0x24E8), or -1;
		// [BIN:TPI-EXE:0x61E5E0 project progress] -1 gives 0, else progress × 100 / effort (100 at an effort of 0.1 or less), at most 100; a park without a lab has no project, so 0
		public int ObstacleResearchPercent => economy.TpiResearch is { } research ? research.ProgressPercent( research.ObstacleProject() ) : 0;

		// [BIN:TPI-EXE:0x55C8B0 researchers] the researcher list head word park+0x1EAB84 (TPI-STAFF), set while a researcher is employed
		public bool ResearchersHired => economy.TpiStaff?.OfRole( ThemeParkIncStaffRole.Researcher ).Any() ?? false;

		// [BIN:TPI-EXE:0x64E940 staff HQ] the HQ's twenty slots (ThemeParkIncStaffHeadquarters); a park without Theme Park Inc staff has no HQ
		public bool? ObstacleStaffSlotsFilled => economy.TpiStaff?.Hq.AllFilled;

		public string PlayerName => playerName;

		public bool PressComplete() => economy.PressThemeParkIncMissionComplete();
	}

	private sealed class ThemeParkIncMissionHost( ParkEconomy economy ) : IThemeParkIncMissionHost
	{
		// [BIN:TPI-EXE:0x42A340 viewed zone] the world's zone object 0xA06558 holds the zone being viewed at +0x14 (set by 0x42A360 on a zone change): the economy's zone, as an index of the name
		// table 0x9D53B4 (arabian 0, science 1, water 2)
		public int ViewedZone
		{
			get
			{
				var index = ThemeParkIncCareer.ZoneNames.ToList().FindIndex( name => string.Equals( name, economy.Settings.Theme, StringComparison.OrdinalIgnoreCase ) );
				return index < 0 ? 4 : index;
			}
		}

		// [BIN:TPI-EXE:0x5C0740 bank balance] returns the bank's +0x14, the balance as a 32-bit word
		public int BankBalance => unchecked( (int)economy.Balance );

		// [BIN:TPI-EXE:0x5C1300 loans owed] the sum over the eight loan records (+0x28 stride 0x20) of (term +0x28 - repaid +0x34) × instalment +0x2C for a record whose bought word +0x30 is
		// set, 32-bit arithmetic: the advisor's loan slots of the same bank (AdvisorLoanSlot.Owed)
		public int OutstandingLoans
		{
			get
			{
				var owed = 0;
				foreach ( var slot in economy.LoanSlots() )
					owed = unchecked( owed + slot.Owed );
				return owed;
			}
		}

		// [BIN:TPI-EXE:0x5B2060 guests in park] walks every thing; a thing of class 1 (a guest) whose cell (bytes +5/+7) passes 0x60BE00 (cell type 1, 3, 9, 10 or 0: 0x67F5B0, 0x67F5D0,
		// 0x67F600, 0x67F620, 0x67F6D0) counts; with argument 4 every zone counts: Theme Park World's census (ParkGate.CountCensus)
		public int GuestsInPark => economy.guestStatistics.GuestCensus;

		// [BIN:TPI-EXE:0xA02168 shares issued] the Standard object's ShareInfo.CompanySharesIssued
		public int CompanySharesIssued => economy.Settings.ThemeParkIncShares.CompanySharesIssued;

		public ushort CurrentObstacle => economy.TpiMissionWorld?.CurrentObstacle ?? 0;

		// [EXT:headless-economy] a headless economy has no world: no attraction things, no land and no camera
		public void FindCurrentObstacle( int type ) => economy.TpiMissionWorld?.FindCurrentObstacle( type );

		public ushort FindAttraction( int type ) => economy.TpiMissionWorld?.FindAttraction( type ) ?? 0;

		public bool OpensAutomatically( int type ) => economy.TpiMissionWorld?.OpensAutomatically( type ) ?? false;

		// [BIN:TPI-EXE:0x64E940 staff HQ] the HQ thing park+0x1EAB4C, whose 20 slots ThemeParkIncStaffHeadquarters keeps; a park without Theme Park Inc staff has no HQ
		public bool? ObstacleStaffSlotsFilled => economy.TpiStaff?.Hq.AllFilled;

		public void OpenObstacle( ushort thing ) => World( "open an obstacle" ).OpenObstacle( thing );

		public void DestroyThing( ushort thing ) => World( "destroy a thing" ).DestroyThing( thing );

		public void LoadExtent( int index ) => economy.TpiMissionWorld?.LoadExtent( index );

		// [BIN:TPI-EXE:0x64EA00 advisor thing] the controller's advice goes to the world's advisor thing (0x408EE5 -> 0x4077F2)
		public bool PostAdvice( int message ) => economy.PostThemeParkIncAdvice( message );

		public void CancelAdvice( int message ) => economy.CancelThemeParkIncAdvice( message );

		public bool AdvicePlayed( int message ) => economy.ThemeParkIncAdvicePlayed( message );

		public void PlayCamera( ThemeParkIncCameraRequest request )
		{
			AddTpiMissionNotice( economy.TpiCameraRequests, request );
			economy.TpiCutscene?.Play( request.Thing, request.Animation, request.Mode );
		}

		// [BIN:TPI-EXE:0x5F18DA arrival briefing post] the tag letter 0x5CD6E0(0x5C, 5, 1) in a type-0x19 wrapper (0x435DC0) through the message centre: the tag system files its row
		public void PostIntroductionLetter() => economy.FileThemeParkIncLetter( ThemeParkIncTagLetter.Introduction( economy.Turn ) );

		// [BIN:TPI-EXE:0x61CBF0 obstacle research] the research lab adds a kind-5 project for the mission's obstacle (HasObstacle, a current obstacle thing, the zone's type range, obstacle state 1,
		// no such project yet): ThemeParkIncResearch.RegisterObstacle
		public void EnsureObstacleResearch() => economy.RegisterThemeParkIncObstacleResearch();

		// [BIN:TPI-EXE:0x5EF991 objective letter post] 0x5CEAE0(career +0x105C - 1) in a type-0x19 wrapper (0x435DC0) through the message centre: the tag system files its row (kind 4 opens no window)
		public void PostObjectiveLetter( int completed ) => economy.FileThemeParkIncLetter( ThemeParkIncTagLetter.Objective( completed, economy.Turn ) );

		// [BIN:TPI-EXE:0x61DC20 lab message 0x27] the research lab hears the controller's status broadcast and runs its obstacle registration 0x61CBF0 again
		public void StatusChanged()
		{
			economy.TpiMissionRevision++;
			economy.RegisterThemeParkIncObstacleResearch();
		}

		private IThemeParkIncMissionWorld World( string action ) =>
			economy.TpiMissionWorld ?? throw new InvalidOperationException( $"The mission controller cannot {action} in a park without a world." );
	}
}
