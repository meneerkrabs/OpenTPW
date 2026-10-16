namespace OpenTPW;

/// <summary>
/// The weather renderer's own pseudo-random stream as tp.exe's <c>CRain</c> runs it on its state word (+0xA198): every draw is
/// <c>state = rotl4( rotl3( state x 0x13D573 ) + 0xE0423 )</c>, as in the inline copies at 0x57E4B0 and 0x57E810. A bolt
/// reseeds it with the bolt's own seed word every frame, so a bolt keeps its shape for its whole life. OpenTPW seeds a bolt
/// from its presentation stream (the original takes the time-stamp counter), so only the arithmetic is the original's.
/// </summary>
// [BIN:TPW-EXE:0x57E53C-0x57E54D CRain draw] mul by 0x13D573, rol 3, add 0xE0423, rol 4; the whole new state is the draw
internal struct CRainRandom
{
	private uint state;

	public CRainRandom( uint seed ) => state = seed;

	/// <summary>Advances the state and returns it: the whole word is the draw.</summary>
	public uint Next()
	{
		state = uint.RotateLeft( uint.RotateLeft( state * 0x13D573u, 3 ) + 0xE0423u, 4 );
		return state;
	}
}
