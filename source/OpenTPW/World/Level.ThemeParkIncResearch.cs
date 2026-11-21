namespace OpenTPW;

public partial class Level
{
	/// <summary>
	/// A park built from a Theme Park Inc save (the prebuilt Science park) takes its staff HQ's obstacle slots and trained-staff counts; a continuation restores its own later, and a new park's HQ built
	/// its table from the mission (ParkEconomy).
	/// </summary>
	// [BIN:TPI-EXE:0x64CD3E staff HQ load] a park file's HQ thing is loaded, not constructed: its slots are the saved ones (the prebuilt's twenty {7, 0, 0}), not the current mission's table
	private void SeedThemeParkIncHqAndLab()
	{
		if ( !IsThemeParkIncSlice || resumeState != null || OriginalPark?.Save?.ThemeParkIncWorld is not { } world || Park?.Economy is not { } economy )
			return;
		if ( world.StaffHq is { } hq && economy.TpiStaff is { } staff )
			staff.Hq.Import( hq.StaffSlots, hq.TrainedStaff );
	}
}
