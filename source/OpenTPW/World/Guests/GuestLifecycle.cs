namespace OpenTPW;

/// <summary>
/// When a guest heads home (the state-6 handler <c>0xebbd0</c>, docs/reverse/ORACLE.md F26, docs/GUESTS.md "Decision"): the truncated happiness byte is 0 (that is, happiness below 1),
/// the exit counter is exactly 0, or the park is closed (the world variable <c>mParkClosed</c>, game <c>+0x1da710</c>, read by <c>0x1091ac</c>). The guest then loses
/// <c>PeepInfo.BigHappinessChange</c> (<c>0xeb6f4</c>, selector 2) and goes to state 18. The test runs every park turn a guest is in state 6, not only when it decides.
/// </summary>
public static class GuestLifecycle
{
	/// <summary>
	/// The leaving test. The counter compares for equality, not "at or below": it passes 0 once, four park turns long, and a guest that is in another state
	/// (queueing, riding) at that moment keeps a negative counter and never leaves by it (the state-18 handler, <c>0xedc98</c>, only recalls a guest whose counter is above 0).
	/// </summary>
	// [BIN:STP-PPC:0x100EBDFC leaving test] executed by the PowerPC oracle (guest-exit-test, 513 inputs; C# agrees): the happiness byte (fctiwz, clrlwi 24) is 0, or +444 compares equal to 0 (cmpwi, beq), or 0x1091ac is nonzero
	public static bool ShouldLeave( float happiness, int exitLevel, bool parkClosed ) =>
		MeterMath.Byte( happiness ) == 0 || exitLevel == 0 || parkClosed;
}
