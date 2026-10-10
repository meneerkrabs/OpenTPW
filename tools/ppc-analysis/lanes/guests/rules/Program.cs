using System;
using System.Collections.Generic;
using OpenTPW.Evidence;

internal static class Program
{
	private static int failures;
	private static int tests;

	public static int Main()
	{
		Test( "queue groups contain four positions", () =>
		{
			Equal( (0u, 3u), GuestOriginalRules.QueueCellGroup( 3 ) );
			Equal( (1u, 0u), GuestOriginalRules.QueueCellGroup( 4 ) );
			Equal( (1u, 3u), GuestOriginalRules.QueueCellGroup( 7 ) );
			Equal( (2u, 0u), GuestOriginalRules.QueueCellGroup( 8 ) );
		} );
		Test( "distance divisor 450 boundaries", () =>
		{
			Equal( 100u, GuestOriginalRules.DistanceMatch( 0, 0 ) );
			Equal( 2u, GuestOriginalRules.DistanceMatch( 21, 0 ) ); // squared 441
			Equal( 1u, GuestOriginalRules.DistanceMatch( 20, 7 ) ); // squared 449
			Equal( 0u, GuestOriginalRules.DistanceMatch( 15, 15 ) ); // squared 450
			Equal( 0u, GuestOriginalRules.DistanceMatch( 127, -127 ) );
		} );
		Test( "signed cell divisor enters unsigned division", () =>
		{
			Equal( 50u, GuestOriginalRules.DistanceMatch( 0, 0, 2 ) );
			Equal( 0u, GuestOriginalRules.DistanceMatch( 0, 0, -1 ) );
		} );
		Test( "near and far queue weight gates", () =>
		{
			Equal( (7u, 25u), GuestOriginalRules.QueueTerm( 2, 2, 3, 1, 7 ) );
			Equal( (0u, 0u), GuestOriginalRules.QueueTerm( 3, 0, 3, 1, 7 ) );
			Equal( (7u, 100u), GuestOriginalRules.QueueTerm( 0, 0, 0, 0, 7 ) );
			Equal( (7u, 0u), GuestOriginalRules.QueueTerm( 0, 0, 4, 1, 7 ) );
			Equal( (7u, 100u), GuestOriginalRules.QueueTerm( 0, 0, 4, uint.MaxValue, 7 ) ); // Raw queue-size word (+60) is unsigned.
		} );
		Test( "overfull queue match remains wrapped", () =>
		{
			Equal( 4294967271u, GuestOriginalRules.QueueTerm( 0, 0, 5, 1, 1 ).Match );
			Equal( 4294967271u, GuestOriginalRules.NormalizeBaseScore(
				new uint[] { 0, 1, 0, 0, 0, 0, 0 }, new uint[] { 0, 4294967271u, 0, 0, 0, 0, 0 } ) );
		} );
		Test( "negative queue numerator yields a large unsigned quotient", () =>
		{
			var queue = GuestOriginalRules.QueueTerm( 0, 0, 9, 1, 2 );
			Equal( 4294967171u, queue.Match ); // -125 in the register
			Equal( 1431655715u, GuestOriginalRules.NormalizeBaseScore(
				new uint[] { 1, queue.Weight, 0, 0, 0, 0, 0 }, new uint[] { 100, queue.Match, 0, 0, 0, 0, 0 } ) );
		} );
		Test( "weighted additions and products keep low 32 bits", () =>
		{
			Equal( 16u, GuestOriginalRules.NormalizeBaseScore(
				new uint[] { 1, 2, 0, 0, 0, 0, 0 }, new uint[] { 100, 4294967271u, 0, 0, 0, 0, 0 } ) );
			Equal( 0u, GuestOriginalRules.NormalizeBaseScore(
				new uint[] { 0, uint.MaxValue, 0, 0, 0, 0, 0 }, new uint[] { 0, 100, 0, 0, 0, 0, 0 } ) );
		} );
		Test( "excitement difference is capped at fifty", () =>
		{
			Equal( 100u, GuestOriginalRules.ExcitementMatch( 50, 50 ) );
			Equal( 40u, GuestOriginalRules.ExcitementMatch( 50, 80 ) );
			Equal( 0u, GuestOriginalRules.ExcitementMatch( 0, 255 ) );
		} );
		Test( "need lookup is effect-major and quantized", () =>
		{
			// Synthetic table: only two deliberately distinct entries; no original table is embedded.
			var table = new byte[121];
			table[11 * 5 + 5] = 17; // One separately witnessed original coefficient.
			table[11 * 3 + 7] = 91; // Synthetic sentinel distinguishes row/column order.
			Equal( 17u, GuestOriginalRules.HungerOrThirstMatch( 59, 59, table ) );
			Equal( 91u, GuestOriginalRules.HungerOrThirstMatch( 70, 30, table ) );
			Equal( 0u, GuestOriginalRules.HungerOrThirstMatch( 30, 70, table ) );
		} );
		Test( "toilet and illness lookup uses ceiling fifths only when enabled", () =>
		{
			var table = new byte[21];
			table[10] = 2;
			table[11] = 5;
			Equal( 2u, GuestOriginalRules.ToiletOrIllnessMatch( 50, true, table ) );
			Equal( 5u, GuestOriginalRules.ToiletOrIllnessMatch( 51, true, table ) );
			Equal( 0u, GuestOriginalRules.ToiletOrIllnessMatch( 255, false, ReadOnlySpan<byte>.Empty ) );
		} );
		Test( "seven matches normalize with integer truncation", () =>
		{
			Equal( 42u, GuestOriginalRules.NormalizeBaseScore(
				new uint[] { 1, 1, 1, 2, 2, 2, 2 }, new uint[] { 100, 50, 80, 17, 0, 2, 100 } ) );
		} );
		Test( "far queue weight is excluded from normalization", () =>
		{
			var queue = GuestOriginalRules.QueueTerm( 3, 0, 0, 1, 9 );
			Equal( 100u, GuestOriginalRules.NormalizeBaseScore(
				new uint[] { 1, queue.Weight, 0, 0, 0, 0, 0 }, new uint[] { 100, queue.Match, 0, 0, 0, 0, 0 } ) );
		} );
		Test( "used history uses first match before temporary history", () =>
		{
			var empty = new ushort[4];
			Equal( 20u, GuestOriginalRules.ApplyHistory( 100, 17, new ushort[] { 17, 17, 0, 0 }, empty ) );
			Equal( 25u, GuestOriginalRules.ApplyHistory( 100, 17, new ushort[] { 0, 17, 0, 0 }, empty ) );
			Equal( 33u, GuestOriginalRules.ApplyHistory( 100, 17, new ushort[] { 0, 0, 17, 0 }, empty ) );
			Equal( 50u, GuestOriginalRules.ApplyHistory( 100, 17, new ushort[] { 0, 0, 0, 17 }, empty ) );
			Equal( 10u, GuestOriginalRules.ApplyHistory( 100, 17, new ushort[] { 17, 0, 0, 0 }, new ushort[] { 0, 0, 0, 17 } ) );
		} );
		Test( "history reinterprets raw score as signed", () =>
		{
			Equal( 4294967291u, GuestOriginalRules.ApplyHistory( 4294967271u, 17, new ushort[] { 17, 0, 0, 0 }, new ushort[4] ) );
			Equal( 4294967290u, GuestOriginalRules.ApplyHistory( 4294967271u, 17, new ushort[] { 0, 17, 0, 0 }, new ushort[4] ) );
			Equal( 4294967288u, GuestOriginalRules.ApplyHistory( 4294967271u, 17, new ushort[] { 0, 0, 17, 0 }, new ushort[4] ) );
			Equal( 4294967284u, GuestOriginalRules.ApplyHistory( 4294967271u, 17, new ushort[] { 0, 0, 0, 17 }, new ushort[4] ) );
		} );
		Test( "all temporary matches accumulate their penalties", () =>
		{
			Equal( 5u, GuestOriginalRules.ApplyHistory( 600, 7, new ushort[4], new ushort[] { 7, 7, 7, 7 } ) );
		} );
		Test( "temporary match subsets follow native slot order", () =>
		{
			// Literal results of the native 5/4/3/2 fall-through paths, indexed by match mask.
			uint[] expected = { 600, 120, 150, 30, 200, 40, 50, 10, 300, 60, 75, 15, 100, 20, 25, 5 };
			for ( var mask = 0; mask < expected.Length; mask++ )
			{
				var temporary = new ushort[4];
				for ( var slot = 0; slot < temporary.Length; slot++ )
					temporary[slot] = (ushort)((mask & (1 << slot)) != 0 ? 7 : 9);
				Equal( expected[mask], GuestOriginalRules.ApplyHistory( 600, 7, new ushort[4], temporary ) );
			}
		} );
		Test( "used history stops before cumulative temporary history", () =>
		{
			Equal( 10u, GuestOriginalRules.ApplyHistory( 600, 7, new ushort[] { 9, 7, 7, 7 }, new ushort[] { 7, 9, 7, 9 } ) );
			Equal( 15u, GuestOriginalRules.ApplyHistory( 600, 7, new ushort[] { 7, 7, 7, 7 }, new ushort[] { 9, 7, 9, 7 } ) );
		} );
		Test( "temporary matches truncate each signed intermediate toward zero", () =>
		{
			int[] expected = { -25, -5, -6, -1, -8, -1, -2, 0, -12, -2, -3, 0, -4, 0, -1, 0 };
			for ( var mask = 0; mask < expected.Length; mask++ )
			{
				var temporary = new ushort[4];
				for ( var slot = 0; slot < temporary.Length; slot++ )
					temporary[slot] = (ushort)((mask & (1 << slot)) != 0 ? 7 : 9);
				Equal( unchecked((uint)expected[mask]), GuestOriginalRules.ApplyHistory( 4294967271u, 7, new ushort[4], temporary ) );
			}
		} );
		Test( "unsigned raw scores cross the signed history boundary", () =>
		{
			var all = new ushort[] { 7, 7, 7, 7 };
			Equal( 4277071599u, GuestOriginalRules.ApplyHistory( 2147483648u, 7, new ushort[4], all ) );
			Equal( 17895697u, GuestOriginalRules.ApplyHistory( 2147483647u, 7, new ushort[4], all ) );
			Equal( 0u, GuestOriginalRules.ApplyHistory( uint.MaxValue, 7, new ushort[4], all ) );
		} );
		Test( "shop subtracts amount and preserves fractional remainder", () =>
		{
			Equal( 5.5f, GuestOriginalRules.ApplyShopEffect( 25.5f, 20 ) );
			Equal( 0f, GuestOriginalRules.ApplyShopEffect( 3, 5 ) );
			Equal( 100f, GuestOriginalRules.ApplyShopEffect( 95, -10 ) );
		} );
		Test( "ride illness uses hunger bands and clamps only the need result", () =>
		{
			Equal( 35, GuestOriginalRules.RideIllnessIncrement( 70, 10, 0 ) );
			Equal( 7, GuestOriginalRules.RideIllnessIncrement( 79, 10, 80 ) );
			Equal( 0, GuestOriginalRules.RideIllnessIncrement( 70, 10, 81 ) );
			Equal( 100f, GuestOriginalRules.ApplyRideIllness( 90, 70, 10, 0 ) );
			Equal( 45.5f, GuestOriginalRules.ApplyRideIllness( 10.5f, 70, 10, 0 ) );
		} );
		Test( "unsupported input domains fail instead of silently clamping", () =>
		{
			Throws<ArgumentException>( () => GuestOriginalRules.DistanceMatch( 128, 0 ) );
			Throws<ArgumentException>( () => GuestOriginalRules.HungerOrThirstMatch( 101, 50, new byte[121] ) );
			Throws<ArgumentException>( () => GuestOriginalRules.HungerOrThirstMatch( 50, 50, new byte[120] ) );
			Throws<ArgumentException>( () => GuestOriginalRules.ToiletOrIllnessMatch( 50, true, new byte[20] ) );
			Throws<ArgumentException>( () => GuestOriginalRules.ApplyShopEffect( float.NaN, 0 ) );
			Throws<ArgumentException>( () => GuestOriginalRules.ApplyShopEffect( 150, 1 ) );
			Throws<ArgumentException>( () => GuestOriginalRules.RideIllnessIncrement( 70, 0, 0 ) );
			Throws<ArgumentException>( () => GuestOriginalRules.ApplyHistory( 100, 17, new ushort[3], new ushort[4] ) );
		} );
		Test( "zero and wrapped-zero denominators remain unsupported", () =>
		{
			Throws<ArgumentException>( () => GuestOriginalRules.QueueTerm( 0, 0, 0, 0x40000000u, 1 ) );
			Throws<ArgumentException>( () => GuestOriginalRules.NormalizeBaseScore( new uint[7], new uint[7] ) );
			Throws<ArgumentException>( () => GuestOriginalRules.NormalizeBaseScore(
				new uint[] { 1, uint.MaxValue, 0, 0, 0, 0, 0 }, new uint[7] ) );
		} );
		Console.WriteLine( $"GuestOriginalRules: {tests - failures}/{tests} cases passed; {failures} failed." );
		return failures == 0 ? 0 : 1;
	}

	private static void Test( string name, Action test )
	{
		tests++;
		try { test(); }
		catch ( Exception error )
		{
			failures++;
			Console.Error.WriteLine( $"FAIL {name}: {error.Message}" );
		}
	}

	private static void Equal<T>( T expected, T actual )
	{
		if ( !EqualityComparer<T>.Default.Equals( expected, actual ) )
			throw new InvalidOperationException( $"Expected {expected}, got {actual}." );
	}

	private static void Throws<T>( Action action ) where T : Exception
	{
		try { action(); }
		catch ( T ) { return; }
		throw new InvalidOperationException( $"Expected {typeof( T ).Name}." );
	}
}
