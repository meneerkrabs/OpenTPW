namespace OpenTPW;

/// <summary>
/// Approximation register of guest arrivals (docs/GUESTS.md, "Arrivals"). Each entry is tagged
/// <c>[APPROX:ARRIVAL-NNN]</c> at its code site; the arrival rule itself is traced from the Mac binary.
/// </summary>
public static class ArrivalApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("ARRIVAL-001", "STEP-OFF: one guest steps off per park turn from the turn after the group starts, at the arrival lanes; the original's bus first drives to its stop (its animation's own time)"),
		("ARRIVAL-002", "ONLINE CUT: the original cuts a group to 500 minus the guests when its GameType word (data:0x53D98) is 1 (online) and to 1500 otherwise (oracle F9); OpenTPW takes a non-sandbox park visit to be GameType 1 (an assumption), and a hosted online park may not run the routine at all. The factor is resolved: 0.8 while the weather thing's +48 word (mCurrentDrops, game +0x1DA724) is positive, which OpenTPW takes as ParkWeather.CurrentDrops above 0 (rain or snow drops falling); the drop ramp behind it is itself approximate (AUDIO-025..027, 029, 030)"),
		("ARRIVAL-003", "POINTS: the counts-test (0xDC834, tp.exe 0x4DD920) is applied except the coaster controller test and the kart track word (a water ride's track-valid flag is applied, docs/TRACKS.md), and kind 0/2 is read as ride/sideshow"),
		("ARRIVAL-004", "FLOOR: a group cut by the 1500 limit stops at 0; the original keeps 1500 minus guests, negative above the limit (oracle F9). Executed (oracle F11), a negative group ends at the next stop like 0: no guest, timer restart, active byte cleared. Only the stored group word and one arrival-vehicle step (slot 74, 'group == 0', effect untraced) differ"),
		("ARRIVAL-005", "FIRST PURCHASE: imported and fixed objects set a type's first-purchase turn to the current turn; the original restores park record +28 from its save"),
		("ARRIVAL-006", "BOOTH WAIT: the booth's expensive wait counts park turns of the fixed clock (248 ms each) rather than the guest updates of state 4, and its draws come from the guest stream (DET-014)"),
		("ARRIVAL-007", "PURSE: OpenTPW charges the entrance fee from the guest's purse and turns back a guest who cannot pay; the original's booth books the fee (0x004D0600) without reading the guest's cash"),
	};
}
