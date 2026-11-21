namespace OpenTPW;

public sealed partial class ParkEconomy
{
	/// <summary>
	/// Message 0x23, the mission's obstacle researched (the research lab's completion of a Park Expansion project, <c>0x61DD86</c>): the staff HQ rebuilds its slots and the mission controller shows
	/// the obstacle on its next update. The broadcaster reaches its listeners in ascending thing handle order, the HQ (built with the park) before the controller.
	/// </summary>
	// [BIN:TPI-EXE:0x6325B9 HQ rebuild] message 0x23 -> 0x634D20; [BIN:TPI-EXE:0x5F0F8D research complete] the controller's +0x1B = 1; [BIN:TPI-EXE:0x436E97 message order] ascending handles
	public void ThemeParkIncObstacleResearched()
	{
		if ( TpiStaff != null && TpiCareer != null )
			TpiStaff.Hq.Rebuild( TpiStaff.Members, TpiCareer );
		TpiMissions?.ObstacleResearched();
	}

	/// <summary>Message 0x24 with the obstacle thing <paramref name="thing"/>, the player removing the obstacle: the staff HQ rebuilds its slots and the mission controller starts its camera.</summary>
	// [BIN:TPI-EXE:0x6325B9 HQ rebuild] message 0x24 -> 0x634D20; [BIN:TPI-EXE:0x5F0F96 remove obstacle] the controller's state 2
	public void ThemeParkIncObstacleRemoved( ushort thing )
	{
		if ( TpiStaff != null && TpiCareer != null )
			TpiStaff.Hq.Rebuild( TpiStaff.Members, TpiCareer );
		TpiMissions?.RemoveObstacle( thing );
	}

	/// <summary>Message 0x21 for a staff member's thing destroyed: the staff HQ releases his ability count and slot.</summary>
	// [BIN:TPI-EXE:0x632520 staff destroyed] message 0x21 for a thing of kind 4, 5, 6, 8, 7 or 0x14 -> 0x634730
	internal void ThemeParkIncStaffDestroyed( ThemeParkIncStaffMember member )
	{
		if ( TpiStaff is { } staff )
			staff.Hq.Release( staff.Members, member );
	}
}
