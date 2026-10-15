using System.Buffers.Binary;

namespace OpenTPW.Evidence.Clock;

/// <summary>Two saved adjusted clock words in native SSEM order. These are not raw hardware samples.</summary>
public readonly record struct SavedClockEpochs( uint ScaledMilliseconds, uint UnscaledMilliseconds )
{
	/// <summary>Exactly8 bytes is a tool contract; native partial-read mutations are not modelled.</summary>
	public static SavedClockEpochs ReadLittleEndian( ReadOnlySpan<byte> words )
	{
		if ( words.Length != 8 )
			throw new ArgumentException( "A complete pair of four-byte clock words is required.", nameof( words ) );
		return new( BinaryPrimitives.ReadUInt32LittleEndian( words ), BinaryPrimitives.ReadUInt32LittleEndian( words[4..] ) );
	}
}

/// <summary>
/// Caller supplies current base getters EXCLUDING outer epoch offsets: scaled0x10ed54 and unscaled0x117c00.
/// Scaled base already includes its forced/normal selection and transition offset+56; both retain pause semantics.
/// </summary>
public readonly record struct ClockBaseWords( uint SelectedScaledMilliseconds, uint PauseAwareUnscaledMilliseconds );

/// <summary>Outer offsets+60 and embedded+24. These additions wrap, even when an underlying conversion saturates.</summary>
public readonly record struct ClockEpochOffsets( uint ScaledOffset, uint UnscaledOffset );

public readonly record struct SharedClockWords( uint ScaledMilliseconds, uint UnscaledMilliseconds );

/// <summary>
/// Identified native epoch arithmetic only. No source accumulator/scale/pause/forced state, counters, or VM deadlines are reset.
/// Caller owns complete-load validation, actual getter samples, callback state and all untraced lifecycle effects.
/// </summary>
public static class SharedClockEpochs
{
	/// <summary>0x11a5bc/0x11a45c: saved adjusted word minus current base word, modulo2^32.</summary>
	public static ClockEpochOffsets Align( SavedClockEpochs saved, ClockBaseWords bases ) => new(
		unchecked(saved.ScaledMilliseconds - bases.SelectedScaledMilliseconds),
		unchecked(saved.UnscaledMilliseconds - bases.PauseAwareUnscaledMilliseconds) );

	/// <summary>0x11a588/0x11a428: current base word plus outer epoch offset, modulo2^32.</summary>
	public static SharedClockWords Read( ClockEpochOffsets offsets, ClockBaseWords bases ) => new(
		unchecked(bases.SelectedScaledMilliseconds + offsets.ScaledOffset),
		unchecked(bases.PauseAwareUnscaledMilliseconds + offsets.UnscaledOffset) );

	/// <summary>0x11b3cc..0x11b3d8. This incoming r6 selector is separate from GameType and callback/world state.</summary>
	public static bool AppliesPostLoadAlignment( int numericLoadSelector ) => numericLoadSelector != 1;

	/// <summary>Only models whether this particular hook rewrites offsets; it does not model selector1's complete lifecycle.</summary>
	public static ClockEpochOffsets ApplyPostLoadHook( ClockEpochOffsets existing, SavedClockEpochs saved,
		ClockBaseWords bases, int numericLoadSelector ) => AppliesPostLoadAlignment( numericLoadSelector )
		? Align( saved, bases ) : existing;
}
