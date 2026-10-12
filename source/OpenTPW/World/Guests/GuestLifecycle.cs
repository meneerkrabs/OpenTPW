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

	/// <summary>
	/// The state-18 recall (the leaving handler's opening test, <c>0xedc98</c>): a leaving guest whose counter is above 0, happiness byte nonzero and park
	/// open turns back to state 6 (<c>0xef700</c>, selector 6), so a closed-park dismissal unwinds when the park reopens. The original also demands the
	/// guest's cell pass <c>0xe6c6c</c> (see <c>[APPROX:GUEST-012]</c> at the call site).
	/// </summary>
	// [BIN:STP-PPC:0x100EDC98 leaving handler head] read in the disassembly (not executed by the oracle): +444 above 0 (cmpwi, branch when GT clear), the happiness byte (fctiwz, clrlwi 24) nonzero, 0x1091ac zero, then the cell predicate 0xe6c6c nonzero
	public static bool ShouldRecall( float happiness, int exitLevel, bool parkClosed ) =>
		exitLevel > 0 && MeterMath.Byte( happiness ) != 0 && !parkClosed;
}
