using System;
using System.Numerics;
using static OpenTPW.Reverse.Economy.OriginalLoanRulesTests;

namespace OpenTPW.Reverse.Economy;

internal static class OriginalParkRatingRulesTests
{
	public static void RunAll( Action<string, Action> run )
	{
		run( "rating zero and independent components", ZeroAndComponents );
		run( "visitor scaling cap precedes multiplication", Visitors );
		run( "ride multiply precedes signed truncation and cap", Rides );
		run( "shop sideshow feature upgrade and staff caps", OtherCaps );
		run( "rating cap gate precedes uncapped recount", SeparateCountPasses );
		run( "rating low-word overflow and final sum", LargeAndSignedCounts );
		run( "rating bounded BigInteger operation reference", RatingCrossCheck );
		run( "staff skill raw byte and grade behavior", StaffExamples );
		run( "staff skill double fraction then f32 rounding", StaffOperationOrder );
		run( "staff skill reviewed grade and byte domain", StaffDomain );
	}

	private static StableParkCounts Counts( int visitors = 0, int rides = 0, int shops = 0, int sideshows = 0,
		int features = 0, int upgraded = 0, int handymen = 0, int mechanics = 0, int entertainers = 0,
		int guards = 0, int researchers = 0 ) =>
		new( visitors, rides, shops, sideshows, features, upgraded, handymen, mechanics, entertainers, guards, researchers );

	private static ParkRatingComponents Score( StableParkCounts counts ) => OriginalParkRatingRules.CalculateStableSnapshot( counts );

	private static void ZeroAndComponents()
	{
		Equal( new ParkRatingComponents(), Score( Counts() ) );
		Equal( 1, Score( Counts( visitors: 50 ) ).Total );
		Equal( 1, Score( Counts( rides: 1 ) ).Total );
		Equal( 2, Score( Counts( shops: 1 ) ).Total );
		Equal( 2, Score( Counts( sideshows: 1 ) ).Total );
		Equal( 1, Score( Counts( features: 1 ) ).Total );
		Equal( 1, Score( Counts( upgraded: 1 ) ).Total );
		Equal( 1, Score( Counts( handymen: 1 ) ).Total );
		Equal( 1, Score( Counts( mechanics: 1 ) ).Total );
		Equal( 1, Score( Counts( entertainers: 1 ) ).Total );
		Equal( 1, Score( Counts( guards: 1 ) ).Total );
		Equal( 1, Score( Counts( researchers: 1 ) ).Total );
		Equal( 100, Score( Counts( 1000, 14, 5, 5, 10, 10, 4, 4, 4, 4, 4 ) ).Total );
	}

	private static void Visitors()
	{
		foreach ( var (count, expected) in new[] { (0, 0), (49, 0), (50, 1), (999, 19), (1000, 20), (1001, 20), (1000000, 20), (int.MaxValue, 20) } )
			Equal( expected, Score( Counts( visitors: count ) ).Visitors );
	}

	private static void Rides()
	{
		foreach ( var (count, expected) in new[] { (0, 0), (1, 1), (2, 3), (3, 4), (13, 19), (14, 20), (1000000, 20) } )
			Equal( expected, Score( Counts( rides: count ) ).Rides );
		Equal( -1, Score( Counts( rides: -1 ) ).Rides, "signed truncation, not arithmetic shift without carry correction" );
	}

	private static void OtherCaps()
	{
		foreach ( var count in new[] { 0, 1, 4, 5, 6, 1000000 } )
		{
			Equal( Math.Min( count * 2, 10 ), Score( Counts( shops: count ) ).Shops );
			Equal( Math.Min( count * 2, 10 ), Score( Counts( sideshows: count ) ).Sideshows );
		}
		foreach ( var count in new[] { 0, 1, 9, 10, 11, int.MaxValue } )
		{
			Equal( Math.Min( count, 10 ), Score( Counts( features: count ) ).Features );
			Equal( Math.Min( count, 10 ), Score( Counts( upgraded: count ) ).UpgradedRides );
		}
		foreach ( var count in new[] { 0, 1, 3, 4, 5, int.MaxValue } )
		{
			var result = Score( Counts( handymen: count, mechanics: count, entertainers: count, guards: count, researchers: count ) );
			var expected = Math.Min( count, 4 );
			Equal( expected, result.Handymen );
			Equal( expected, result.Mechanics );
			Equal( expected, result.Entertainers );
			Equal( expected, result.Guards );
			Equal( expected, result.Researchers );
		}
	}

	private static void SeparateCountPasses()
	{
		var inputs = new ParkRatingObservations() with { Rides = new RatingCountPass( 1, 100 ) };
		Equal( 150, OriginalParkRatingRules.Calculate( inputs ).Rides );
		Equal( 150, OriginalParkRatingRules.Calculate( inputs ).Total, "no invented final percentage normalization" );
		inputs = inputs with { Rides = new RatingCountPass( 14, 0 ) };
		Equal( 20, OriginalParkRatingRules.Calculate( inputs ).Rides );
		inputs = new ParkRatingObservations() with
		{
			Visitors = new RatingCountPass( 999, 1100 ), Shops = new RatingCountPass( 1, 100 ),
			Features = new RatingCountPass( 1, 100 ), Handymen = new RatingCountPass( 1, 100 )
		};
		var result = OriginalParkRatingRules.Calculate( inputs );
		Equal( 22, result.Visitors );
		Equal( 200, result.Shops );
		Equal( 100, result.Features );
		Equal( 100, result.Handymen );
		inputs = new ParkRatingObservations() with
		{
			Visitors = new RatingCountPass( 1000, int.MinValue ), Shops = new RatingCountPass( 5, int.MinValue ),
			Features = new RatingCountPass( 10, int.MinValue ), Handymen = new RatingCountPass( 4, int.MinValue )
		};
		result = OriginalParkRatingRules.Calculate( inputs );
		Equal( 20, result.Visitors );
		Equal( 10, result.Shops );
		Equal( 10, result.Features );
		Equal( 4, result.Handymen );
	}

	private static void LargeAndSignedCounts()
	{
		Equal( 0, Score( Counts( visitors: -49 ) ).Visitors );
		Equal( -1, Score( Counts( visitors: -50 ) ).Visitors );
		Equal( -1073741823, Score( Counts( rides: 715827883 ) ).Rides );
		Equal( int.MinValue, Score( Counts( shops: 1073741824 ) ).Shops );
		Equal( 1073741825, Score( Counts( rides: 715827883, shops: 1073741824 ) ).Total );
		Equal( 20, Score( Counts( rides: int.MaxValue ) ).Rides );
	}

	private static int SignedWord( BigInteger value )
	{
		var bits = (uint)(value & uint.MaxValue);
		return unchecked((int)bits);
	}

	private static int ReferenceScale( RatingCountPass count, int multiplier, int divisor, int cap )
	{
		var gate = SignedWord( (BigInteger)count.CapCheck * multiplier ) / divisor;
		return gate < cap ? SignedWord( (BigInteger)count.Recount * multiplier ) / divisor : cap;
	}

	private static void RatingCrossCheck()
	{
		var values = new[] { int.MinValue, -50, -1, 0, 1, 3, 4, 5, 10, 13, 14, 49, 50, 999, 1000, 1000000, 715827883, 1073741824, int.MaxValue };
		var random = new Random( 74291 );
		RatingCountPass Next() => new( values[random.Next( values.Length )], values[random.Next( values.Length )] );
		for ( var sample = 0; sample < 2048; sample++ )
		{
			var inputs = new ParkRatingObservations( Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next(), Next() );
			var actual = OriginalParkRatingRules.Calculate( inputs );
			var visitorCount = inputs.Visitors.CapCheck < 1000 ? inputs.Visitors.Recount : 1000;
			var expected = new ParkRatingComponents( SignedWord( (BigInteger)visitorCount * 20 ) / 1000,
				ReferenceScale( inputs.Rides, 3, 2, 20 ), ReferenceScale( inputs.Shops, 2, 1, 10 ),
				ReferenceScale( inputs.Sideshows, 2, 1, 10 ), ReferenceScale( inputs.Features, 1, 1, 10 ),
				ReferenceScale( inputs.UpgradedRides, 1, 1, 10 ), ReferenceScale( inputs.Handymen, 1, 1, 4 ),
				ReferenceScale( inputs.Mechanics, 1, 1, 4 ), ReferenceScale( inputs.Entertainers, 1, 1, 4 ),
				ReferenceScale( inputs.Guards, 1, 1, 4 ), ReferenceScale( inputs.Researchers, 1, 1, 4 ) );
			Equal( expected, actual );
			var sum = (BigInteger)expected.Rides + expected.Shops + expected.Sideshows + expected.Features
				+ expected.UpgradedRides + expected.Handymen + expected.Mechanics + expected.Entertainers
				+ expected.Guards + expected.Researchers + expected.Visitors;
			Equal( SignedWord( sum ), actual.Total );
		}
	}

	private static void StaffExamples()
	{
		foreach ( var (grade, percentage, expected) in new (int, byte, uint)[]
		{
			(0, 0, 0), (0, 99, 19), (4, 0, 80), (4, 99, 99), (0, 100, 20),
			(0, 255, 51), (4, 100, 100), (4, 255, 131), (5, 0, 100),
			(int.MinValue, 255, 0), (int.MaxValue, 255, uint.MaxValue)
		} )
			Equal( expected, OriginalStaffSkill.Calculate( grade, percentage ).Skill );
	}

	private static void StaffOperationOrder()
	{
		var originalOrder = OriginalStaffSkill.Calculate( -1, 105 );
		var allSingle = 20f * (-1f + 105 / 100f);
		Equal( 1u, originalOrder.Skill );
		Equal( 0u, OriginalLoanRules.SaturatingUnsigned( allSingle ) );
		Equal( 1f, originalOrder.BeforeTruncation );
	}

	private static void StaffDomain()
	{
		for ( var grade = 0; grade <= 5; grade++ )
		for ( var percentage = 0; percentage <= 255; percentage++ )
			Equal( (uint)((100 * grade + percentage) / 5), OriginalStaffSkill.Calculate( grade, (byte)percentage ).Skill );
	}
}
