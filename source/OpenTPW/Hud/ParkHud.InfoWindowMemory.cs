namespace OpenTPW.Hud;

/// <summary>The four windows the HUD's info button remembers (the original's kinds 3 to 6).</summary>
internal enum InfoWindowKind
{
	ParkInfo = 3,
	AllStaff = 4,
	Attractions = 5,
	Visitors = 6,
}

public sealed partial class ParkHud
{
	/// <summary>The last of the four info windows opened, which the HUD's info button opens again; the HUD starts it at the park information.</summary>
	// [BIN:TPW-EXE:0x004a0810 last window memory] the builders of the park information (0x4a52a0, at 0x4a52cc), All Staff (0x496620, 0x49663e), attractions (0x495aa0, 0x495ab9) and visitors windows (0x493530, 0x493539) call
	// 0x4a0810 with 2 and their kind 3, 4, 5 or 6, which stores it in 0x7cb2a0; the HUD builder starts it at 3 (0x4a1dbe) and the info button (case 2 of 0x4a0940, 0x4a098a) opens the window of the stored kind: 0x4a52a0, 0x496620 with -1, 0x495aa0 with -1, 0x493530
	private InfoWindowKind lastInfoWindow = InfoWindowKind.ParkInfo;

	/// <summary>The info window the HUD's info button opens next.</summary>
	internal InfoWindowKind LastInfoWindow => lastInfoWindow;

	private void RememberInfoWindow( InfoWindowKind kind ) => lastInfoWindow = kind;

	/// <summary>The HUD's info button: the info window that was open last (the park information until another one was).</summary>
	public void OpenLastInfoWindow()
	{
		switch ( lastInfoWindow )
		{
			case InfoWindowKind.AllStaff:
				OpenAllStaffWindow();
				break;
			case InfoWindowKind.Attractions:
				OpenAttractionsWindow();
				break;
			case InfoWindowKind.Visitors:
				OpenVisitorWindow();
				break;
			default:
				OpenParkInfoWindow();
				break;
		}
	}
}
