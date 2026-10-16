namespace OpenTPW;

/// <summary>
/// The original's one world generator (tp.exe <c>0x00516330</c>, the u32 at world +0x1DA708): every random decision of the simulation
/// (guests, staff, the economy's staff pool, script RAND, the bump rides) takes its draw from this one stream.
/// </summary>
/// <remarks>
/// A draw computes <c>s = s * 1664525 + 1013904223</c>, rotates the result right by 13 and stores the rotated value as the new state; it returns the
/// absolute value (a state of <c>0x80000000</c> stays negative as an int and is returned as is). The reseed <c>0x00516370</c> stores its argument as
/// the state. The projections (modulo, shifts, masks) stay with each caller, in <see cref="WorldRng.NextWord"/>'s unsigned word.
/// </remarks>
// [BIN:TPW-EXE:0x00516330 world generator] ecx + 0x1DA708 = s; imul eax, 0x19660D; add 0x3C6EF35F; store; ror 13; store the rotated value as the state; neg when negative (0x00516355..0x0051635F)
// [BIN:TPW-EXE:0x00516370 world reseed] stores its one argument as the state (mov [ecx + 0x1DA708], eax)
// [BIN:TPW-EXE:0x0051A34B world generator copy] the same step is inlined at 0x0051674B, 0x0051680B, 0x00516879 and 0x0051A34B
public sealed class WorldRng
{
	/// <summary>The multiplier of the generator's step (1664525).</summary>
	public const uint Multiplier = 0x0019660D;
	/// <summary>The increment of the generator's step (1013904223).</summary>
	public const uint Increment = 0x3C6EF35F;

	public WorldRng( uint state ) => State = state;

	/// <summary>The generator's whole state (saved as the original's <c>mRandomSeed</c>).</summary>
	public uint State { get; private set; }

	/// <summary>The state a run starts from, derived from a seed: one SplitMix64 step folded to 32 bits (the original seeds from <c>time(NULL)</c>; the explicit world seed is OpenTPW's replay policy, see <see cref="WorldSeed"/>).</summary>
	public static uint StateFromSeed( ulong seed )
	{
		var z = seed + 0x9E3779B97F4A7C15UL;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		z ^= z >> 31;
		return (uint)(z ^ (z >> 32));
	}

	/// <summary>One draw (0x00516330): the absolute value of the new state, as the word of the original's int result.</summary>
	public uint NextWord()
	{
		var step = unchecked( State * Multiplier + Increment );
		State = (step >> 13) | (step << 19);
		var signed = (int)State;
		return signed < 0 ? unchecked( (uint)-signed ) : State;
	}

	/// <summary>A draw reduced with an unsigned remainder, the form of the original's <c>xor edx, edx; div</c> sites. A bound of 0 is the original's division by zero (undefined); here it gives 0.</summary>
	public uint Next( uint exclusiveMaximum ) => exclusiveMaximum == 0 ? 0 : NextWord() % exclusiveMaximum;

	/// <summary>A draw reduced with an unsigned remainder to an int bound; bounds at or below 0 give 0.</summary>
	public int Next( int exclusiveMaximum ) => exclusiveMaximum <= 0 ? 0 : (int)(NextWord() % (uint)exclusiveMaximum);

	/// <summary>The reseed (0x00516370): the zero-extended id becomes the state, so the draws that follow depend on the id alone.</summary>
	public void Reseed( uint state ) => State = state;

	/// <summary>Continues the generator from a saved state.</summary>
	public void Restore( uint state ) => State = state;
}
