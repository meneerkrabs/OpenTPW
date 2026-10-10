namespace OpenTPW;

/// <summary>
/// The one explicit seed of a park run (docs/DETERMINISM.md). Every simulation random stream is derived
/// from it, so the same seed and the same inputs give the same state in one process or in separate ones.
/// The original seeds its world generator from <c>time(NULL)</c> (docs/reverse/DET-plan.md §2.3), so no
/// seed value is "the original one".
/// </summary>
// [APPROX:DET-002] runs take an explicit world seed (default 0x5450574775657374) instead of the original's time(NULL)/timer seeds — evidence needed: none possible; explicit seeding is an OpenTPW replay policy
public readonly record struct WorldSeed( ulong Value )
{
	/// <summary>The seed every level used before world seeds existed (<c>Level.GuestSeed</c>, ASCII "TPWGuest").</summary>
	public const ulong DefaultValue = 0x5450_5747_7565_7374;

	// Stream keys: each stream seed is the world seed XOR a fixed key. The keys are chosen so that the default
	// world seed reproduces the seeds each system had before (guests DefaultValue, economy 1, object scripts
	// 1, 2, 3, …), which keeps every existing balance outcome and baseline unchanged.
	private const ulong EconomyKey = DefaultValue ^ 1;
	private const ulong ScriptKey = DefaultValue;
	private const ulong SoundKey = DefaultValue ^ 0x534F_554E_4453_4545;

	public static WorldSeed Default => new( DefaultValue );

	/// <summary>Seed of <see cref="GuestSimulation"/>'s <see cref="GuestRandom"/>.</summary>
	public ulong GuestStream => Value;

	/// <summary>Seed of <see cref="ParkEconomy.Random"/>.</summary>
	public ulong EconomyStream => Value ^ EconomyKey;

	/// <summary>Seed of a park's <see cref="RideScriptWorld"/> (scripts created without an explicit seed draw from it).</summary>
	public ulong ScriptStream => Value ^ ScriptKey;

	/// <summary>XORed into the placement ordinal to give each placed object's script seed (0 for the default seed).</summary>
	public int ObjectScriptKey => (int)(ScriptStream ^ (ScriptStream >> 32));

	/// <summary>Seed of the sound event chooser for this park (presentation only).</summary>
	public uint SoundStream => (uint)((Value ^ SoundKey) ^ ((Value ^ SoundKey) >> 32));

	public override string ToString() => Value.ToString( System.Globalization.CultureInfo.InvariantCulture );
}

/// <summary>
/// Every random stream state of a running park besides the economy's own (saved as <c>RandomState</c>),
/// persisted in OpenTPW's park save so a save → load continues the same streams.
/// </summary>
public sealed record WorldRandomState( ulong Seed, ulong GuestRandom, ulong ScriptRandom, uint SoundSeed );
