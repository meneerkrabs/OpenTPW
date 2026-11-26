using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's x87 arithmetic at the game thread's control word 0x7F ([EXT:hardware-d3d-path], <see cref="ThemeParkIncX87"/>). The staff cases are the native routines' answers at that
/// word, printed by <c>tools/ppc-analysis/oracle_x86/tpi_staff_formula_oracle.py --golden</c>; the cases marked so give another answer at the 53-bit word 0x27F.
/// </summary>
[TestClass]
public class ThemeParkIncX87Tests
{
	[TestMethod]
	public void TheHelpersRoundTheExactResultOnce()
	{
		Assert.AreEqual( 0.2f, ThemeParkIncX87.Quotient( 20000, 100000 ) );
		Assert.AreEqual( 0.7f, ThemeParkIncX87.Quotient( 7, 10 ), "0.69999999 as a single" );
		Assert.AreEqual( -0.25f, ThemeParkIncX87.Quotient( 1, -4 ) );
		Assert.IsTrue( float.IsNaN( ThemeParkIncX87.Quotient( 0, 0 ) ) );
		// 2^31 - 1 times 1 + 2^-23 is 2^31 + 256 - 2^-23 exactly, 55 bits: the nearest single is 2^31 + 256
		Assert.AreEqual( 2147483904f, ThemeParkIncX87.Product( int.MaxValue, 1.00000012f ) );
		Assert.AreEqual( 100f, ThemeParkIncX87.Product( 3, 33.333332f ), "the midpoint 99.999996185 goes to the even 100.0" );
		Assert.AreEqual( 1f, ThemeParkIncX87.Product( 100, 0.01f ) );
		Assert.AreEqual( 16777216f, ThemeParkIncX87.Sum( 16777217, 0f ) );
		Assert.AreEqual( 16777218f, ThemeParkIncX87.Sum( 16777217, 0.25f ) );
		Assert.AreEqual( 0f, ThemeParkIncX87.Sum( -3, 3f ) );
		Assert.AreEqual( 0.8f, ThemeParkIncX87.Divide( 20f, 25 ) );
		Assert.AreEqual( 0, ThemeParkIncX87.Ftol( float.NaN ), "the integer indefinite's low dword" );
		Assert.AreEqual( 0, ThemeParkIncX87.Ftol( 1e19f ) );
		Assert.AreEqual( unchecked( (int)3000000000L ), ThemeParkIncX87.Ftol( 3e9f ), "the low dword of the 64-bit truncation" );
		Assert.AreEqual( -7, ThemeParkIncX87.Ftol( -7.9f ) );
	}

	[TestMethod]
	public void TheHelpersAgreeWithAnExactRationalReferenceOnRandomOperands()
	{
		// every path: small operands (the double fast paths), 29-bit and larger integers, 32-bit divisors (the 128-bit quotient) and 64-bit ones (BigInteger)
		var random = new Random( 20261122 );
		long Integer()
		{
			var bits = random.Next( 1, 64 );
			var value = (long)(random.NextInt64() & ((1L << bits) - 1)) | 1L << (bits - 1);
			return random.Next( 2 ) == 0 ? -value : value;
		}
		float Single()
		{
			// a random 24-bit significand between 2^-12 and 2^12, where no result leaves the normal singles
			var value = (float)Math.ScaleB( random.Next( 0x800000, 0x1000000 ), random.Next( -35, -11 ) );
			return random.Next( 2 ) == 0 ? -value : value;
		}
		for ( var index = 0; index < 20000; index++ )
		{
			var (a, b, single) = (Integer(), Integer(), random.Next( 3 ) == 0 ? (float)random.Next( 1, 1000 ) / 7f : Single());
			Assert.AreEqual( Reference( a, b ), ThemeParkIncX87.Quotient( a, b ), $"{a} / {b}" );
			var (significand, exponent) = Exact( single );
			Assert.AreEqual( Reference( a * significand * Pow2( exponent ), Pow2( -exponent ) ), ThemeParkIncX87.Product( a, single ), $"{a} x {single:R}" );
			Assert.AreEqual( Reference( a * Pow2( -exponent ) + significand * Pow2( exponent ), Pow2( -exponent ) ), ThemeParkIncX87.Sum( a, single ), $"{a} + {single:R}" );
			Assert.AreEqual( Reference( significand * Pow2( exponent ), b * Pow2( -exponent ) ), ThemeParkIncX87.Divide( single, b ), $"{single:R} / {b}" );
		}
		// the reference is sharp: this product is 2^40 + 2^16 (a midpoint) plus less than 2^-12, so the double rounds to the midpoint and the single to the even 2^40, while the exact value
		// rounds up to 2^40 + 2^17
		const long hard = 1098493290459;
		var (hardSignificand, hardExponent) = Exact( 1.0009270906448364f );
		Assert.AreEqual( 1099511758848f, Reference( hard * hardSignificand * Pow2( hardExponent ), Pow2( -hardExponent ) ) );
		Assert.AreEqual( 1099511758848f, ThemeParkIncX87.Product( hard, 1.0009270906448364f ) );
		Assert.AreEqual( 1099511627776f, (float)(hard * (double)1.0009270906448364f ), "a double in between rounds twice" );
	}

	[TestMethod]
	public void TheArrivalKeepsTheLowDwordOfAGroupOfTwoToTheThirtyTwoOrMore()
	{
		// [BIN:TPI-EXE:0x005BCD60 arrival group] _ftol (0x86FAB0) keeps the low dword of the 64-bit truncation: (1717987308 + 20) x 2.5 is 2^32 + 1024, so the group is 1024 (the native routine at 0x7F,
		// x86_targets.TPI_ARRIVAL_TARGET); a saturated or indefinite conversion would give MinPeople
		var settings = GuestTests.CreateSettings();
		settings.ArrivalNewParkBonus = 20;
		settings.ArrivalPointsPerVisitor = 1;
		settings.ArrivalMinPeople = 1;
		settings.ArrivalWeatherPercentages = new[] { 90, 80, 75, 250 };
		var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 1 ) { ParkPoints = () => 1717987308, ArrivalWeatherState = () => 1 };
		Assert.AreEqual( 1024, simulation.ArrivalGroupSize() );
	}

	/// <summary>A finite nonzero single as its exact <c>significand × 2^exponent</c>.</summary>
	private static (System.Numerics.BigInteger Significand, int Exponent) Exact( float value )
	{
		var bits = BitConverter.SingleToInt32Bits( value );
		var field = (bits >> 23) & 0xFF;
		var significand = (long)(bits & 0x7FFFFF) | (field == 0 ? 0 : 0x800000L);
		return (bits < 0 ? -significand : significand, field == 0 ? -149 : field - 150);
	}

	/// <summary>2^exponent as an integer, 1 for a negative exponent (the other side of the fraction carries it).</summary>
	private static System.Numerics.BigInteger Pow2( int exponent ) => exponent > 0 ? System.Numerics.BigInteger.One << exponent : System.Numerics.BigInteger.One;

	/// <summary>The single nearest the exact <paramref name="numerator"/> / <paramref name="denominator"/>, ties to even, found among the neighbours of an approximation.</summary>
	private static float Reference( System.Numerics.BigInteger numerator, System.Numerics.BigInteger denominator )
	{
		if ( denominator.Sign < 0 )
			(numerator, denominator) = (-numerator, -denominator);
		var guess = (float)((double)numerator / (double)denominator);
		var best = guess;
		var bestDistance = Distance( guess );
		foreach ( var candidate in new[] { MathF.BitDecrement( MathF.BitDecrement( guess ) ), MathF.BitDecrement( guess ), MathF.BitIncrement( guess ), MathF.BitIncrement( MathF.BitIncrement( guess ) ) } )
		{
			var distance = Distance( candidate );
			var order = distance.Numerator * bestDistance.Denominator - bestDistance.Numerator * distance.Denominator;
			if ( order.Sign < 0 || order.IsZero && (BitConverter.SingleToInt32Bits( candidate ) & 1) == 0 )
				(best, bestDistance) = (candidate, distance);
		}
		return best;

		(System.Numerics.BigInteger Numerator, System.Numerics.BigInteger Denominator) Distance( float candidate )
		{
			var (significand, exponent) = candidate == 0f ? (System.Numerics.BigInteger.Zero, 0) : Exact( candidate );
			var candidateNumerator = significand * Pow2( exponent );
			var candidateDenominator = Pow2( -exponent );
			var difference = candidateNumerator * denominator - numerator * candidateDenominator;
			return (System.Numerics.BigInteger.Abs( difference ), candidateDenominator * denominator);
		}
	}

	[TestMethod]
	[DataRow( 100000, 34000, 33, -33, DisplayName = "34000 of 100000: 33.0 (53 bits: 32.99999999999999, 32)" )]
	[DataRow( 50000, 17000, 33, -33, DisplayName = "17000 of 50000 (53 bits: 32)" )]
	[DataRow( 100000, 82297, 8, -8 )]
	[DataRow( 100000, 0, 50, -50 )]
	public void TheSearchPeriodAndTheIdleTermAreTheNativeOnes( int maxExperience, int experience, int period, int term )
	{
		// [BIN:TPI-EXE:0x5D5000 handyman pre-update] [BIN:TPI-EXE:0x62CA10 idle end]
		Assert.AreEqual( (period, term), (ThemeParkIncStaffWorld.SearchPeriod( experience, maxExperience ), ThemeParkIncStaffWorld.IdleTerm( experience, maxExperience )) );
	}

	[TestMethod]
	[DataRow( 50000, 49999, 0.05000000074505806, 70.9017333984375, 0.04999799653887749, 70.9017333984375, DisplayName = "53 bits differ" )]
	[DataRow( 100000, 99999, 0.05000000074505806, 0.05000000074505806, 0.04999899864196777, 0.049999501556158066, DisplayName = "53 bits differ (2)" )]
	[DataRow( 50000, 10574, 0.0, 0.05000000074505806, 0.0, 0.01057400181889534, DisplayName = "53 bits differ (3)" )]
	[DataRow( 100000, 76951, 64.51603698730469, 100.0, 64.49298858642578, 99.98847198486328 )]
	public void TheStepIsTheNativeOne( int maxExperience, int experience, double energy, double happiness, double energyAfter, double happinessAfter )
	{
		// [BIN:TPI-EXE:0x62B3E0 staff step]
		var member = Member( experience, (float)energy, (float)happiness, 100f );
		member.Step( Settings( maxExperience ) );
		Assert.AreEqual( ((float)energyAfter, (float)happinessAfter), (member.Energy, member.Happiness) );
	}

	[TestMethod]
	[DataRow( 50000, 1531, 0.0, 100.0, 100.0, new[] { 0.003507493296638131, 0.75, 1.0, 3.0 }, -15, 2, 0.029008368030190468, 100.0, 100.0, DisplayName = "53 bits differ" )]
	[DataRow( 100000, 99999, 0.05000000074505806, 100.0, 100.0, new[] { 0.20000000298023224, 2.5512168407440186, 0.6504303216934204, 5.919777870178223 }, 10, 20, 1.369989037513733, 100.0, 100.0,
		DisplayName = "53 bits differ (2)" )]
	[DataRow( 35524, 35523, 0.05000000074505806, 88.57746124267578, 100.0, new[] { 0.20000000298023224, 1.3728656768798828, 2.643338680267334, 5.509371280670166 }, 0, 20, 1.249971866607666,
		94.08683013916016, 100.0, DisplayName = "53 bits differ (3)" )]
	[DataRow( 100000, 19876, 0.05000000074505806, 44.64984893798828, 0.05000000074505806, new[] { 0.5649723410606384, 1.8179771900177002, 1.0, 1.5911099910736084 }, 26, 0, 1.0123026371002197,
		46.24095916748047, 1.6411099433898926, DisplayName = "53 bits differ (4)" )]
	[DataRow( 52052, 52051, 24.084213256835938, 0.0, 20.063703536987305, new[] { 0.20000000298023224, 0.8601991534233093, 2.2350449562072754, 5.4465651512146 }, 14, 20, 24.944412231445312,
		5.4465651512146, 25.510269165039062 )]
	public void TheRestIsTheNativeOne( int maxExperience, int experience, double energy, double happiness, double motivation, double[] rates, int recovery, int happyRecovery,
		double energyAfter, double happinessAfter, double motivationAfter )
	{
		// [BIN:TPI-EXE:0x62A1C0 rest]
		var member = Member( experience, (float)energy, (float)happiness, (float)motivation );
		member.Rest( Settings( maxExperience, rates, recovery: recovery, happyRecovery: happyRecovery ) );
		Assert.AreEqual( ((float)energyAfter, (float)happinessAfter, (float)motivationAfter), (member.Energy, member.Happiness, member.Motivation) );
	}

	[TestMethod]
	[DataRow( new[] { 7, 0, 0, 3, 0, 0, 0 }, 10, 0, 20007, DisplayName = "7 of 10 days: 10 x 0.69999999 is 7.0 at 24 bits (53 bits: 20006)" )]
	[DataRow( new[] { 7, 0, 0, 3, 0, 0, 0 }, 10, 20, 20021, DisplayName = "and the modifier's sum (53 bits differ)" )]
	[DataRow( new[] { 3, 0, 4, 0, 0, 4, 9 }, 20, 0, 20007, DisplayName = "53 bits differ" )]
	[DataRow( new[] { 700, 100, 100, 100, 0, 0, 0 }, 3, 30, 20023, DisplayName = "53 bits differ (2)" )]
	public void TheExperienceGainIsTheNativeOne( int[] counters, int perMonth, int modifier, int experience )
	{
		// [BIN:TPI-EXE:0x629650 experience gain] the oracle's counters in the routine's order: working, looking for work, resting, idle, training, researching, on strike
		var member = Member( 20000, 100f, 100f, 100f );
		member.RestoreDayCounters( new uint[] { (uint)counters[3], (uint)counters[1], (uint)counters[0], (uint)counters[2], (uint)counters[6], 0, 0, (uint)counters[5], (uint)counters[4] } );
		member.EndDay( Settings( 74057, experiencePerMonth: perMonth, experienceGain: modifier ) );
		Assert.AreEqual( experience, member.Experience );
	}

	[TestMethod]
	public void TheWageReviewIsTheNativeOne()
	{
		// [BIN:TPI-EXE:0x62CDA0 wage review] review kind 0x4 max 50000 exp 20789 pay 80 top 166 unhappy 5 happy 2 lowest 20 row (gain 0, loss 7, initial wage 48) h 63.335739 m 19.9
		// -> (53.335739, 0.0, shortfall 28)
		var member = new ThemeParkIncStaffMember( 1, ThemeParkIncStaffRole.Mechanic, 0, 20789, 80, 0, 0 ) { Happiness = 63.33573913574219f, Motivation = 19.899999618530273f };
		member.ReviewWage( Settings( 50000, maxWage: 166, motivationLoss: 7, initialWage: 48 ), new WorldRng( 1 ) );
		Assert.AreEqual( (53.33573913574219f, 0f, 28), (member.Happiness, member.Motivation, member.WageShortfall) );
		// review kind 0x8 max 100000 exp 0 pay 369 top 429 unhappy 0 happy 2 lowest 55 row (12, 7, -15) h 0.0 m 19.9 -> (26.0, 45.900001525878906, 0, 0)
		member = new ThemeParkIncStaffMember( 2, ThemeParkIncStaffRole.Researcher, 0, 0, 369, 0, 0 ) { Happiness = 0f, Motivation = 19.899999618530273f };
		member.ReviewWage( Settings( 100000, maxWage: 429, unhappy: 0, lowest: 55, motivationGain: 12, motivationLoss: 7, initialWage: -15 ), new WorldRng( 1 ) );
		Assert.AreEqual( (26f, 45.900001525878906f, 0, 0), (member.Happiness, member.Motivation, member.WageShortfall, member.WageDemandDay) );
	}

	private static ThemeParkIncStaffMember Member( int experience, float energy, float happiness, float motivation ) =>
		new( 1, ThemeParkIncStaffRole.Handyman, 0, experience, 80, 0, 0 ) { Energy = energy, Happiness = happiness, Motivation = motivation };

	/// <summary>Staff settings with every key the reader needs: one agency (row 0) and the same words for every role.</summary>
	private static ThemeParkIncStaffSettings Settings( int maxExperience, double[]? rates = null, int recovery = 0, int happyRecovery = 0, int experiencePerMonth = 3, int experienceGain = 0,
		int maxWage = 80, int unhappy = 5, int happy = 2, int lowest = 20, int motivationGain = 0, int motivationLoss = 0, int initialWage = 0 )
	{
		rates ??= new[] { 0.20000000298023224, 0.75, 1.0, 3.0 };
		var keys = new Dictionary<string, string>();
		void Set( string key, object value ) => keys[key] = Convert.ToString( value, CultureInfo.InvariantCulture )!;
		foreach ( var key in new[] { "StaffPoolInfo.TimeBetweenStaffUpdates", "StaffPoolInfo.MaxNumberOfStaffPerUpdate", "StaffPoolInfo.StaffTimeoutTime", "StaffPoolInfo.AvgWageDifference",
			"AllStaffConstants.RestLevel", "AllStaffConstants.MinWage", "AllStaffConstants.MotivationFactor", "AllStaffConstants.StaffSlackingEnergy", "AllStaffConstants.MinDistanceForSlacking",
			"AllStaffConstants.StaffRoomCapacity", "AllStaffConstants.MaxSpeedDifference", "AllStaffConstants.TrainingExtraExperience", "AllStaffConstants.MinExperienceForTraining",
			"AllStaffConstants.SkillIncrement", "AllStaffConstants.ProgessUpperLimit", "AllStaffConstants.HappyHitCosNoRestArea", "AllStaffConstants.HappyHitCosRoomInaccessible",
			"AllStaffConstants.PercentageForBadWageDecision", "AllStaffConstants.MaxWage", "AllStaffConstants.WageStep", "Strike.MinMonthsForStrike", "Strike.MinStaffOfTypeForStrike", "Strike.EnergyPercentToStrike", "Strike.HappyPercentToStrike",
			"Strike.MotivationPercentToStrike", "Researcher.MinResearchAbility", "Researcher.MaxResearchAbility" } )
			Set( key, 1 );
		Set( "AllStaffConstants.MaxExperience", maxExperience );
		Set( "AllStaffConstants.UnHappyWageVal", unhappy );
		Set( "AllStaffConstants.HappyWageVal", happy );
		Set( "AllStaffConstants.LowestWageHappiness", lowest );
		foreach ( var (name, value) in new[] { "MinRecuperationRate", "MaxRecuperationRate", "MinHappinessRecuperationRate", "MaxHappinessRecuperationRate" }.Zip( rates ) )
			Set( $"AllStaffConstants.{name}", value.ToString( "R", CultureInfo.InvariantCulture ) );
		var agency = new Dictionary<string, int>
		{
			["RequiredPromotionLevel"] = 0, ["MotivationGainModifier"] = motivationGain, ["MotivationLossModifier"] = motivationLoss, ["RecuperationModifier"] = recovery,
			["HappinessRecuperationModifier"] = happyRecovery, ["InitialWageModifier"] = initialWage, ["InitialExperienceModifier"] = 0, ["ExperienceGainModifier"] = experienceGain,
			["TrainingExperienceGainModifier"] = 0
		};
		foreach ( var (field, value) in agency )
			Set( $"StaffAgencies[0].{field}", value );
		foreach ( var section in new[] { "Handyman", "Mechanic", "Entertainer", "Guard", "Researcher", "Gardener" } )
		{
			Set( $"{section}.MaxWage", maxWage );
			foreach ( var field in new[] { "MinWorkDuration", "MaxWorkDuration", "MinDetectionRange", "MaxDetectionRange", "MinActiveRange", "MaxActiveRange" } )
				Set( $"{section}.{field}", 1 );
			Set( $"{section}ConstsPerGrade[0].ExperiencePerMonth", experiencePerMonth );
		}
		foreach ( var pool in new[] { "Handymen", "Mechanics", "Entertainers", "Guards", "Researchers", "Gardeners" } )
			foreach ( var key in new[] { $"Max{pool}", $"Min{pool}InPool", $"Max{pool}InPark", $"BeginningNumberOf{pool}", $"AvgExperienceOf{pool}" } )
				Set( $"StaffPoolInfo.{key}", 1 );
		var text = new StringBuilder();
		foreach ( var (key, value) in keys )
			text.Append( key ).Append( "\t\t\t" ).Append( value ).Append( '\n' );
		return new ThemeParkIncStaffSettings( new SamSettings( new[] { SamDocument.Parse( text.ToString(), "staff.sam" ) } ) );
	}
}
