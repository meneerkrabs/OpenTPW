namespace OpenTPW;

/// <summary>
/// The one explicit seed of a park run (docs/DETERMINISM.md). Every simulation random stream is derived
/// from it, so the same seed and the same inputs give the same state in one process or in separate ones.
/// The original seeds its world generator from <c>time(NULL)</c> (docs/reverse/DET-plan.md §2.3), so no
/// seed value is "the original one".
/// </summary>
// [EXT:explicit-world-seed] runs take an explicit world seed (default 0x5450574775657374) instead of the original's time(NULL)/timer seeds; no original seed is "the" seed, so this is OpenTPW's replay policy (DET-002 reclassified)
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
	private const ulong WeatherKey = DefaultValue ^ 0x5745_4154_4845_5221;

	public static WorldSeed Default => new( DefaultValue );

	/// <summary>Seed of a standalone <see cref="GuestSimulation"/>'s world generator (a level shares the park's one, <see cref="GeneratorState"/>).</summary>
	public ulong GuestStream => Value;

	/// <summary>Seed of <see cref="ParkEconomy.World"/>, the park's world generator.</summary>
	public ulong EconomyStream => Value ^ EconomyKey;

	/// <summary>
	/// The state the park's one world generator (tp.exe 0x00516330) starts from (<see cref="WorldRng.StateFromSeed"/> of <see cref="EconomyStream"/>, the seed the economy that
	/// holds it is built with), so that a level with and without an economy start the same.
	/// </summary>
	public uint GeneratorState => WorldRng.StateFromSeed( EconomyStream );

	/// <summary>Seed of a park's <see cref="RideScriptWorld"/> (scripts created without an explicit seed draw from it).</summary>
	public ulong ScriptStream => Value ^ ScriptKey;

	/// <summary>XORed into the placement ordinal to give each placed object's script seed (0 for the default seed).</summary>
	public int ObjectScriptKey => (int)(ScriptStream ^ (ScriptStream >> 32));

	/// <summary>Seed of the park's weather draws (kept apart from the economy's stream so the weather adds no economy draws).</summary>
	public ulong WeatherStream => Value ^ WeatherKey;

	/// <summary>Seed of the sound event chooser for this park (presentation only).</summary>
	public uint SoundStream => (uint)((Value ^ SoundKey) ^ ((Value ^ SoundKey) >> 32));

	public override string ToString() => Value.ToString( System.Globalization.CultureInfo.InvariantCulture );
}

/// <summary>
/// Every random state of a running park: the one world generator (tp.exe 0x00516330, the original's <c>mRandomSeed</c>) that guests, staff, scripts and the economy draw from,
/// and the sound chooser's seed, persisted in OpenTPW's park save so a save → load continues the same streams.
/// </summary>
public sealed record WorldRandomState( ulong Seed, uint Generator, uint SoundSeed );
