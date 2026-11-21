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
	/// <summary>The cutscene thing's camera flight (<c>0x5D0400</c>).</summary>
	void PlayCamera( ThemeParkIncCameraRequest request );
}

public sealed partial class ParkEconomy
{
	/// <summary>Theme Park Inc's mission controller (park thing class 21, <c>OBJH</c>); null in Theme Park World and in a park without a career.</summary>
	public ThemeParkIncMissionController? TpiMissions { get; private set; }

	/// <summary>The world the mission controller acts on (a level's obstacles, land and camera); null in a headless economy.</summary>
	internal IThemeParkIncMissionWorld? TpiMissionWorld { get; set; }

	/// <summary>The controller's status broadcasts (message 0x27) so far: the HUD redraws its mission lines when it changes. Not saved.</summary>
	internal int TpiMissionRevision { get; private set; }

	/// <summary>
	/// The latest <see cref="MaxTpiMissionNotices"/> "Objective Complete!" letters the controller has posted (the index of the completed mission, <c>OBJECTIVE_COMPLETE.str</c>); their window is
	/// not built, so nothing takes them. Not saved: the career's <c>+0x20</c> byte records that the letter went out.
	/// </summary>
	internal List<int> TpiObjectiveLetters { get; } = new();

	/// <summary>The latest <see cref="MaxTpiMissionNotices"/> camera flights the controller asked for (the cutscene thing is not ported: nothing plays them). Not saved.</summary>
	internal List<ThemeParkIncCameraRequest> TpiCameraRequests { get; } = new();

	/// <summary>The letters and camera requests kept.</summary>
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
		if ( TpiCareer is { } career )
			TpiMissions = new ThemeParkIncMissionController( career, new ThemeParkIncMissionHost( this ), Turn, startStatistics: !TpiHeadlessCareer );
	}

	/// <summary>
	/// Rebuilds the controller for <paramref name="career"/> without starting its statistics: the career attached after the park was built (Load A Game's player file, which the original reads
	/// before the park file, 0x7451E0) brings its own statuses, and a loaded controller is never constructed (0x64D5B4). The controller is the park's thing: its own fields stay.
	/// </summary>
	private void AttachThemeParkIncMissions( ThemeParkIncCareer career )
	{
		var attached = new ThemeParkIncMissionController( career, new ThemeParkIncMissionHost( this ), Turn, startStatistics: false );
		if ( TpiMissions is { } park )
			attached.TakeFields( park );
		TpiMissions = attached;
	}

	/// <summary>The controller's thing update: once per world update pass, before the challenge system's (it is the newer thing), not in a lost park.</summary>
	private void UpdateThemeParkIncMissions( long turn ) => TpiMissions!.Update( turn );

	/// <summary>The mission's requirements are met (<c>0x55A620</c>): the mission window's complete button would be enabled.</summary>
	public bool ThemeParkIncMissionMet => TpiMissions is { } missions && TpiCareer?.CurrentZone != null && missions.MetRequirements();

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

		public void PlayCamera( ThemeParkIncCameraRequest request )
		{
			AddTpiMissionNotice( economy.TpiCameraRequests, request );
			economy.TpiMissionWorld?.PlayCamera( request );
		}

		// [BIN:TPI-EXE:0x61CBF0 obstacle research] the research lab adds a kind-5 project for the mission's obstacle (HasObstacle, a current obstacle thing, the zone's type range, obstacle state 1,
		// no such project yet): ThemeParkIncResearch.RegisterObstacle
		public void EnsureObstacleResearch() => economy.RegisterThemeParkIncObstacleResearch();

		public void PostObjectiveLetter( int completed ) => AddTpiMissionNotice( economy.TpiObjectiveLetters, completed );

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
