namespace OpenTPW;

/// <summary>
/// Which object types Theme Park Inc's catalogue tab lists (the list builder <c>0x57CA20</c>): a pure function of the type's settings, the career's mission number and whether the
/// mission's obstacle research is done. The career feeds the two values; until it runs the mission number is 0 and the research is not done.
/// </summary>
public static class ThemeParkIncCatalogue
{
	/// <summary>
	/// Whether the catalogue lists <paramref name="entry"/>: not a special object (<c>Info.IsSpecial</c>) or an add-on, a thinking space (<c>UsageInfo.ThinkingSpace</c> 1) only from
	/// mission 1, a training room (<c>UsageInfo.TrainingRoom</c>) from mission 2, or in mission 1 once the obstacle research is done.
	/// </summary>
	public static bool IsListed( ObjectCatalogEntry entry, int missionNumber, bool obstacleResearchDone )
	{
		ArgumentNullException.ThrowIfNull( entry );
		var settings = entry.Settings;
		return IsListed( settings.GetInt( "Info.IsSpecial" ), settings.GetInt( "AddOn.UpgradesId" ), settings.GetInt( "AddOn.UpgradesAnyRide" ),
			settings.GetInt( "UsageInfo.ThinkingSpace" ), settings.GetInt( "UsageInfo.TrainingRoom" ), missionNumber, obstacleResearchDone );
	}

	/// <summary><see cref="IsListed(ObjectCatalogEntry, int, bool)"/> on the type record's words.</summary>
	// [BIN:TPI-EXE:0x57CAB6 hidden types] a type is skipped when +0x5C (Info.IsSpecial: the Info key table 0x9A2D38, eight words before Info.OverwritePriority, which the validator reads at +0x7C, 0x67E464) is nonzero,
	// or +0x3D4 (AddOn.UpgradesId) or +0x3D0 (AddOn.UpgradesAnyRide) is nonzero (0x57CAE4..0x57CAFA)
	// [BIN:TPI-EXE:0x57CB00 thinking space] a type whose +0x1B4 (UsageInfo.ThinkingSpace: the UsageInfo key table ending at 0x5A51C8 of the file, 26 words before UsageInfo.AppearanceEffect at +0x21C) equals 1 is skipped while the mission number (0x5F03A0 +0xC) is below 1
	// [BIN:TPI-EXE:0x57CB2A training room] a type whose +0x1B8 (UsageInfo.TrainingRoom) is nonzero is listed when the mission number is above 1, or equal to 1 with the obstacle status of 0x5F01D0 equal to 2 (research done); otherwise skipped
	public static bool IsListed( int isSpecial, int upgradesId, int upgradesAnyRide, int thinkingSpace, int trainingRoom, int missionNumber, bool obstacleResearchDone )
	{
		if ( isSpecial != 0 || upgradesId != 0 || upgradesAnyRide != 0 )
			return false;
		if ( thinkingSpace == 1 && missionNumber < 1 )
			return false;
		if ( trainingRoom != 0 && missionNumber <= 1 && !(missionNumber == 1 && obstacleResearchDone) )
			return false;
		return true;
	}
}
