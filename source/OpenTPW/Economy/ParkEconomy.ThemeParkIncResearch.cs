namespace OpenTPW;

public sealed partial class ParkEconomy
{
	/// <summary>
	/// Theme Park Inc's research lab and object controls; null in Theme Park World and in a park whose lab no park file brought (a new zone's lab fill, 0x61C9E0 with the CRT rand() % 2 + 1 of
	/// 0x61FD50, is not ported: it waits for the user's decision at the zone package).
	/// </summary>
	public ThemeParkIncResearch? TpiResearch { get; private set; }

	/// <summary>The research group (<c>Research.Group</c>, info <c>+0x250</c>) of an object type, for the obstacle's project; the level answers it, 0 without one.</summary>
	internal Func<int, byte>? TpiResearchGroupOf { get; set; }

	/// <summary>Takes a Theme Park Inc park file's research lab and object controls (the prebuilt Science park's).</summary>
	// [BIN:TPI-EXE:0x64D21D lab load] the park file's lab thing (case 14) and the control table are loaded, not constructed: no fill runs
	internal void SeedThemeParkIncResearch( ThemeParkIncResearchLab lab, ThemeParkIncObjectControls? controls )
	{
		if ( !Rules.ThemeParkInc || TpiStaff == null || ThemeParkIncResearch.FromSettings( Settings.Standard ) is not { } research )
			return;
		research.Import( lab, controls );
		AttachThemeParkIncResearch( research );
	}

	private void AttachThemeParkIncResearch( ThemeParkIncResearch research )
	{
		research.Member = id => TpiStaff?.Members.FirstOrDefault( member => member.ThingId == id );
		TpiResearch = research;
	}

	/// <summary>The member whose thing id is <paramref name="thing"/>, or null.</summary>
	internal ThemeParkIncStaffMember? TpiMemberOfThing( ushort thing ) => thing == 0 ? null : TpiStaff?.Members.FirstOrDefault( member => member.ThingId == thing );

	/// <summary>The research lab's thing update (<c>0x61E980</c>), in the world pass at the lab thing's place.</summary>
	internal void UpdateThemeParkIncResearch( long turn )
	{
		if ( TpiResearch is not { } research || TpiStaff is not { } staff )
			return;
		// [BIN:TPI-EXE:0x5C07B0 bank debit] each advance debits its points' cash through the bank, adding it to the analyser's money out (+0x1F808), also a debit of 0
		research.Tick( turn, TpiMemberOfThing, staff.Settings, cash => Post( LedgerCategory.OtherCosts, cash ), OnThemeParkIncResearched );
	}

	/// <summary>
	/// Message 0x11, an object researched (<c>0x61DC20</c>): the career is told, a Park Expansion project posts message 0x23 (the mission's obstacle researched), and the next upgrade level is appended.
	/// </summary>
	// [BIN:TPI-EXE:0x61DCF9 researched message] 0x43C700(type) (dropped); the career 0x74D740(type); category (+0xC) 5: message 0x23 with the current obstacle (0x4360C0(park+0x1EAB6E), 0x61DD86) to the queue;
	// then the level project 0x61FE70
	private void OnThemeParkIncResearched( int type, int category )
	{
		TpiCareer?.InformResearched( type );
		if ( category == 5 )
			ThemeParkIncObstacleResearched();
		TpiResearch!.AppendNextLevel( type );
	}

	/// <summary>The obstacle's Park Expansion project (<c>0x61CBF0</c>): the controller's registration and every message 0x27.</summary>
	internal void RegisterThemeParkIncObstacleResearch()
	{
		if ( TpiResearch is not { } research || TpiCareer is not { } career || TpiMissions is not { } missions || career.CurrentZone == null )
			return;
		var mission = career.Mission;
		research.RegisterObstacle( career.HasObstacle, mission.ObstacleType, TpiMissionWorld?.CurrentObstacle ?? 0, missions.ObstacleState(), ThemeParkIncViewedZone, mission.ObstacleResearchEffort,
			TpiResearchGroupOf?.Invoke( mission.ObstacleType ) ?? 0 );
	}

	/// <summary>The zone being viewed (<c>0xA06558 +0x14</c>): 0 arabian, 1 science, 2 water, 4 another.</summary>
	private int ThemeParkIncViewedZone
	{
		get
		{
			var index = ThemeParkIncCareer.ZoneNames.ToList().FindIndex( name => string.Equals( name, Settings.Theme, StringComparison.OrdinalIgnoreCase ) );
			return index < 0 ? 4 : index;
		}
	}

	/// <summary>The research for a save; null without a lab.</summary>
	internal ThemeParkIncResearchSnapshot? CaptureThemeParkIncResearch() => TpiResearch?.Capture();

	/// <summary>Restores a saved lab (a save without one leaves the park without research).</summary>
	internal void RestoreThemeParkIncResearch( ThemeParkIncResearchSnapshot? state )
	{
		if ( state == null )
		{
			TpiResearch = null;
			return;
		}
		if ( ThemeParkIncResearch.FromSettings( Settings.Standard ) is not { } research )
			throw new InvalidDataException( "The save holds a Theme Park Inc research lab, but the park's settings have no research keys." );
		research.Restore( state );
		AttachThemeParkIncResearch( research );
	}
}
