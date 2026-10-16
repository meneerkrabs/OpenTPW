namespace OpenTPW;

/// <summary>
/// Explicit state of a <see cref="ScriptRandom"/>: the 56-word table, both cursors and the draw counter.
/// Word 0 is never written (the seeding and sampling loops use 1..55); every other word is in
/// [0, <see cref="TableWordLimit"/>). <see cref="Inext"/>/<see cref="Inextp"/> are in [0, 55] (0 only before
/// the first draw, as the constructors leave it). DTOs own their arrays; capture copies.
/// </summary>
public sealed record ScriptRandomState
{
	public const int TableWords = 56;
	/// <summary>Exclusive upper bound of every table word: <see cref="int.MaxValue"/> − 1, as the sampler keeps it.</summary>
	public const int TableWordLimit = int.MaxValue;

	public required int[] Table { get; init; }
	public required int Inext { get; init; }
	public required int Inextp { get; init; }

	/// <summary>Deep copy.</summary>
	public ScriptRandomState DeepCopy() => new() { Table = (int[])Table.Clone(), Inext = Inext, Inextp = Inextp };

	/// <summary>Pure structural validation; throws <see cref="InvalidDataException"/> on any violation.</summary>
	public void Validate()
	{
		if ( Table == null )
			throw new InvalidDataException( "RNG state carries no table." );
		if ( Table.Length != TableWords )
			throw new InvalidDataException( $"RNG state has {Table.Length} table words, expected {TableWords}." );
		for ( var index = 0; index < Table.Length; ++index )
			if ( Table[index] < 0 || Table[index] >= TableWordLimit )
				throw new InvalidDataException( $"RNG state word {index} is {Table[index]}, outside [0, {TableWordLimit})." );
		if ( Table[0] != 0 )
			throw new InvalidDataException( "RNG state word 0 is not zero (the generator never writes it)." );
		if ( Inext is < 0 or > 55 || Inextp is < 0 or > 55 )
			throw new InvalidDataException( $"RNG state cursors ({Inext}, {Inextp}) are outside [0, 55]." );
	}
}

/// <summary>
/// The seeded .NET <c>System.Random</c> algorithm (the "compat" subtractive generator of
/// <c>Random(int)</c>: Knuth's lagged Fibonacci from Numerical Recipes in C, 2nd ed., with .NET's
/// seeding and <see cref="Sample"/> arithmetic), reimplemented so its full state can be captured and
/// restored — <c>System.Random</c>'s cannot. Draw sequences are exactly those of a seeded
/// <c>System.Random</c> on the same runtime; ScriptRandomTests proves the equivalence across seeds and
/// bounds, and every change to this class must keep that test passing. The park-resume slice stores
/// this state explicitly; a seed plus a replay count is not a state snapshot.
/// </summary>
// Only a script created with an explicit seed (RideVMOptions.Seed: tests, standalone scripts, older saves) owns one of these; a park's scripts draw from the world generator (WorldRng).
public sealed class ScriptRandom
{
	private const int MBIG = int.MaxValue;
	private const int MSEED = 161803398;

	private readonly int[] seedArray = new int[ScriptRandomState.TableWords];
	private int inext;
	private int inextp;

	/// <param name="seed">Any <see cref="int"/>; <see cref="int.MinValue"/> maps to <see cref="int.MaxValue"/> for the seeding subtraction, as in .NET.</param>
	public ScriptRandom( int seed )
	{
		// .NET's initialization, operation for operation.
		var subtraction = seed == int.MinValue ? int.MaxValue : Math.Abs( seed );
		var mj = MSEED - subtraction;
		seedArray[55] = mj;
		var mk = 1;
		for ( var i = 1; i < 55; i++ )
		{
			var ii = 21 * i % 55;
			seedArray[ii] = mk;
			mk = mj - mk;
			if ( mk < 0 )
				mk += MBIG;
			mj = seedArray[ii];
		}
		for ( var k = 1; k < 5; k++ )
			for ( var i = 1; i < 56; i++ )
			{
				seedArray[i] -= seedArray[1 + (i + 30) % 55];
				if ( seedArray[i] < 0 )
					seedArray[i] += MBIG;
			}
		inext = 0;
		inextp = 21;
	}

	/// <summary>Full explicit state (a deep copy; mutating it does not affect this generator).</summary>
	public ScriptRandomState CaptureState() => new() { Table = (int[])seedArray.Clone(), Inext = inext, Inextp = inextp };

	/// <summary>Replaces the state; validates <paramref name="state"/> first and throws without changing anything on a violation.</summary>
	public void RestoreState( ScriptRandomState state )
	{
		ArgumentNullException.ThrowIfNull( state );
		state.Validate();
		Array.Copy( state.Table, seedArray, seedArray.Length );
		inext = state.Inext;
		inextp = state.Inextp;
	}

	private int InternalSample()
	{
		int retVal;
		var locINext = inext;
		var locINextp = inextp;

		if ( ++locINext >= 56 )
			locINext = 1;
		if ( ++locINextp >= 56 )
			locINextp = 1;

		retVal = seedArray[locINext] - seedArray[locINextp];

		if ( retVal == MBIG )
			retVal--;
		if ( retVal < 0 )
			retVal += MBIG;

		seedArray[locINext] = retVal;

		inext = locINext;
		inextp = locINextp;

		return retVal;
	}

	/// <summary>As <see cref="Random.Sample"/>: one draw scaled into [0, 1) with .NET's exact arithmetic.</summary>
	private double Sample() => InternalSample() * (1.0 / MBIG);

	/// <summary>.NET's large-range sample (range &gt; <see cref="int.MaxValue"/>): a second draw decides the sign.</summary>
	private double GetSampleForLargeRange()
	{
		var result = InternalSample();
		if ( InternalSample() % 2 == 0 )
			result = -result;
		var d = (double)result;
		d += int.MaxValue - 1;
		d /= 2u * (uint)int.MaxValue - 1;
		return d;
	}

	/// <summary>Same result and bounds checks as <see cref="Random.Next()"/>.</summary>
	public int Next() => InternalSample();

	/// <summary>Same result and bounds checks as <see cref="Random.Next(int)"/>.</summary>
	public int Next( int maxValue )
	{
		if ( maxValue < 0 )
			throw new ArgumentOutOfRangeException( nameof( maxValue ), "maxValue must be non-negative." );
		return (int)(Sample() * maxValue);
	}

	/// <summary>Same result and bounds checks as <see cref="Random.Next(int,int)"/>.</summary>
	public int Next( int minValue, int maxValue )
	{
		if ( minValue > maxValue )
			throw new ArgumentOutOfRangeException( nameof( minValue ), "minValue cannot be greater than maxValue." );
		long range = (long)maxValue - minValue;
		if ( range <= int.MaxValue )
			return (int)(Sample() * range) + minValue;

		// Now the range is 4+ billion possible values, so use the large-range sample.
		return (int)((long)(GetSampleForLargeRange() * range) + minValue);
	}
}
