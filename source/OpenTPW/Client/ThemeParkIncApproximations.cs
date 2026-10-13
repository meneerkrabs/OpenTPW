namespace OpenTPW;

/// <summary>
/// The experimental Theme Park Inc slice's approximation register (docs/THEME-PARK-INC.md). Every entry names what the
/// slice does instead of the original Theme Park Inc; nothing in the slice has been compared with the original game.
/// IDs TPI-900..939 are this lane's reserved range.
/// </summary>
public static class ThemeParkIncApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("TPI-900", "TPI-HUD: the slice's interface is OpenTPW's own text UI (no Theme Park Inc interface behaviour is traced; its ui.wad panels are not Theme Park World's)"),
		("TPI-901", "TPI-FREE: building, paths and queues are free because the slice runs no economy; Theme Park Inc's own prices, wages and balance are untraced (its Standard.sam lacks 89 of the keys the Theme Park World economy reads)"),
		("TPI-902", "TPI-GUESTS: guests run the ported Theme Park World guest rules over Theme Park Inc's own guest settings; Theme Park Inc's guest behaviour is untraced"),
		("TPI-903", "TPI-GATES: the capability gates (no economy, staff, advisor, front end, park saves, sharing, missions, Global Master Save, COS) are OpenTPW's honest limits, not traced Theme Park Inc behaviour"),
		("TPI-904", "TPI-TOOLS: path and queue tools follow the traced Theme Park World tool flow (PATH-plan) with the speed pause of OpenTPW's own speed control"),
	};
}
