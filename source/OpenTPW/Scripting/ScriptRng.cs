using System.Text;

namespace OpenTPW.Scripting;

/// <summary>
/// <c>rng(n)</c> is a stateless hash of the call identity (docs/SCRIPT-MODS.md): park seed, mod id, hook, guest birth id, park turn and the
/// draw ordinal within the call. No generator state exists anywhere, so replays, saves and the other platforms see the same values.
/// </summary>
public static class ScriptRng
{
	private const ulong Fnv64Offset = 14695981039346656037UL;
	private const ulong Fnv64Prime = 1099511628211UL;

	/// <summary>Draws a value in [0, <paramref name="n"/>) for one call identity and ordinal (the unsigned remainder of the 64-bit hash; the bias is below 2^-40 for n up to 2^24).</summary>
	public static long Draw( long parkSeed, string modId, string hook, long guestBirthId, long parkTurn, long ordinal, long n )
	{
		if ( n < 1 )
			throw new ArgumentOutOfRangeException( nameof( n ) );
		return (long)(Hash( parkSeed, HashText( modId ), HashText( hook ), guestBirthId, parkTurn, ordinal ) % (ulong)n);
	}

	internal static long DrawHashed( in RngIdentity id, long ordinal, long n ) =>
		(long)(Hash( id.ParkSeed, id.ModHash, id.HookHash, id.GuestBirthId, id.ParkTurn, ordinal ) % (ulong)n);

	internal static ulong HashText( string text )
	{
		var hash = Fnv64Offset;
		foreach ( var b in Encoding.UTF8.GetBytes( text ) )
			hash = (hash ^ b) * Fnv64Prime;
		return (hash ^ (ulong)text.Length) * Fnv64Prime;
	}

	internal static ulong Hash( long parkSeed, ulong modHash, ulong hookHash, long guestBirthId, long parkTurn, long ordinal )
	{
		var h = Mix( 0x7470775363726970UL, (ulong)parkSeed );
		h = Mix( h, modHash );
		h = Mix( h, hookHash );
		h = Mix( h, (ulong)guestBirthId );
		h = Mix( h, (ulong)parkTurn );
		return Mix( h, (ulong)ordinal );
	}

	/// <summary>SplitMix64 finalizer over the running hash and the next field.</summary>
	private static ulong Mix( ulong h, ulong value )
	{
		var z = h + 0x9E3779B97F4A7C15UL + value * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		return z ^ (z >> 31);
	}
}
