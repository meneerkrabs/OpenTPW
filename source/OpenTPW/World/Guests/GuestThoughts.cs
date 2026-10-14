namespace OpenTPW;

/// <summary>The staff's own thought ids (tp.exe <c>0x0050BE80</c> callers in <c>0x00506A40</c>, <c>0x005056E0</c> and <c>0x00506F30</c>); the guests' THOUGHTS table ends at 17.</summary>
public static class StaffThoughts
{
	/// <summary>Happiness byte above 97, one gate pass in sixteen (0x00506CF4).</summary>
	public const int VeryHappy = 18;
	/// <summary>Happiness byte at most 10 (0x00506CCD).</summary>
	public const int Unhappy = 19;
	/// <summary>Energy at most RestLevel: off to rest (0x00506B50).</summary>
	public const int Tired = 20;
	/// <summary>Walking out to strike, state 4 (0x00505709).</summary>
	public const int Strike = 21;
	/// <summary>No reachable cell of the patrol area in 30 tries (0x0050701F).</summary>
	public const int NoDestination = 22;
}

/// <summary>
/// The guests' thoughts as tp.exe sets them: the pick of the needs phase (<c>0x004FC8A0</c>) and the thought object's bubble (<c>0x0050BE80</c>), which
/// shows a thought for 12 park turns and lets a new bubble in only after a gap that depends on the new thought.
/// </summary>
public static class GuestThoughts
{
	/// <summary>The pick runs on one needs phase in this many (a world draw modulo 10).</summary>
	public const uint PickOdds = 10;
	/// <summary>A bubble goes once the turn is past its stamp plus this (<c>0x0050BE40</c>).</summary>
	public const int BubbleTurns = 12;
	/// <summary>A new bubble needs the turn to reach the last stamp plus this times the new thought's priority (<c>0x0050BE80</c>).</summary>
	public const int CooldownTurns = 20;
	/// <summary>The share of littered path cells in the guest's 4 × 4 block above which it thinks "too messy" (<c>0x00700738</c>).</summary>
	public const float MessyShare = 0.2f;

	/// <summary>
	/// The draw that decides whether the needs phase picks a thought: a SplitMix64 hash of the guest and the park turn, so the pick needs no saved
	/// stream and leaves the guests' own stream as it was.
	/// </summary>
	// Approximation (GUEST-006, registered at GuestSimulation.UpdateNeedsTurn): the original takes a draw of its shared world generator (0x00516330); OpenTPW's streams differ from it anyway (DET-014)
	public static uint PickDraw( int guestId, uint turn )
	{
		var z = ((ulong)(uint)guestId << 32 | turn) + 0x9E3779B97F4A7C15UL;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		return (uint)((z ^ (z >> 31)) >> 33);
	}

	/// <summary>
	/// The thought the needs phase picks from the guest's meters (each taken as a byte) and the littered share of its block, or null to keep the
	/// thought it has.
	/// </summary>
	// [BIN:TPW-EXE:0x004FC8A0 thought pick] hunger (+0x1A8) and thirst (+0x1A4) bytes both above 90: 3; else a byte above 99.0 (0x00700734) of hunger 1, thirst 2, toilet (+0x1AC) 4, illness (+0x1B0) 14; else the analyser block's littered cells (0x004C8BA0) over its path, queue and type-9 cells (0x004C8AE0) above 0.2: 7; else happiness (+0x19C) above 99: 9, below 10.0 (0x0070073C): 11; else no call
	public static GuestThought? Pick( float hunger, float thirst, float toilet, float illness, float happiness, float messyShare )
	{
		var hungerByte = MeterMath.Byte( hunger );
		var thirstByte = MeterMath.Byte( thirst );
		if ( hungerByte > 90 && thirstByte > 90 )
			return GuestThought.HungryAndThirsty;
		if ( hungerByte > 99 )
			return GuestThought.Hungry;
		if ( thirstByte > 99 )
			return GuestThought.Thirsty;
		if ( MeterMath.Byte( toilet ) > 99 )
			return GuestThought.NeedToilet;
		if ( MeterMath.Byte( illness ) > 99 )
			return GuestThought.FeelingSick;
		// a block without path cells divides by zero: litter over 0 is infinite (messy), 0 over 0 is unordered (not messy)
		if ( messyShare > MessyShare )
			return GuestThought.TooMessy;
		var happinessByte = MeterMath.Byte( happiness );
		if ( happinessByte > 99 )
			return GuestThought.Happy;
		return happinessByte < 10 ? GuestThought.Unhappy : null;
	}

	/// <summary>The priority of a thought's bubble (<c>0x0050BE80</c>): its cooldown is this times <see cref="CooldownTurns"/>.</summary>
	// [BIN:TPW-EXE:0x0050BE80 thought bubble] ids 1-4, 7, 14: 1; 5, 6, 12, 13, 15, 16, 17 and the staff's 20-22: 0; 8-11 and 18: 3; 19: 2; any other id prints "Not a known thought!" and shows nothing
	public static int Priority( int thought ) => thought switch
	{
		1 or 2 or 3 or 4 or 7 or 14 => 1,
		8 or 9 or 10 or 11 or 18 => 3,
		19 => 2,
		_ => 0
	};

	/// <summary>Whether the thought object takes a new bubble for <paramref name="thought"/> at <paramref name="turn"/>, given the last bubble's stamp.</summary>
	public static bool TakesBubble( int thought, long turn, long stamp ) => thought is >= 1 and <= 22 && (ulong)turn >= (ulong)(stamp + CooldownTurns * Priority( thought ));
}
