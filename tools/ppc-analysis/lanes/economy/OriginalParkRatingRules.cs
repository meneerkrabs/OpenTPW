using System;

namespace OpenTPW.Reverse.Economy;

/// <summary>
/// The inline cap-check count and the below-cap provider recount. Keeping them separate
/// avoids inventing traversal bindings or assuming the two observations always agree.
/// </summary>
public readonly record struct RatingCountPass( int CapCheck, int Recount )
{
	public static RatingCountPass Stable( int count ) => new( count, count );
}

/// <summary>Caller-supplied eligible count observations for code 0xc7b24, not production actor bindings.</summary>
public readonly record struct ParkRatingObservations( RatingCountPass Visitors, RatingCountPass Rides,
	RatingCountPass Shops, RatingCountPass Sideshows, RatingCountPass Features, RatingCountPass UpgradedRides,
	RatingCountPass Handymen, RatingCountPass Mechanics, RatingCountPass Entertainers, RatingCountPass Guards,
	RatingCountPass Researchers );

/// <summary>Convenience input for callers that explicitly assume the count observations are stable.</summary>
public readonly record struct StableParkCounts( int Visitors, int Rides, int Shops, int Sideshows,
	int Features, int UpgradedRides, int Handymen, int Mechanics, int Entertainers, int Guards, int Researchers );

public readonly record struct ParkRatingComponents( int Visitors, int Rides, int Shops, int Sideshows,
	int Features, int UpgradedRides, int Handymen, int Mechanics, int Entertainers, int Guards, int Researchers )
{
	/// <summary>Original addition order and word width; there is no final [0,100] normalization.</summary>
	public int Total => unchecked(Rides + Shops + Sideshows + Features + UpgradedRides + Handymen
		+ Mechanics + Entertainers + Guards + Researchers + Visitors);
}

/// <summary>
/// Standalone Mac rating operand model. Coefficients/caps are literal operands in 0xc7b24,
/// not a copied SAM table. Inputs are signed 32-bit count words supplied by the caller.
/// Negative or overflow-producing synthetic counts do not establish original reachability.
/// This helper supplies no object/staff/visitor traversal or Windows equivalence claim.
/// </summary>
public static class OriginalParkRatingRules
{
	public static ParkRatingComponents CalculateStableSnapshot( StableParkCounts counts ) => Calculate( new ParkRatingObservations(
		RatingCountPass.Stable( counts.Visitors ), RatingCountPass.Stable( counts.Rides ),
		RatingCountPass.Stable( counts.Shops ), RatingCountPass.Stable( counts.Sideshows ),
		RatingCountPass.Stable( counts.Features ), RatingCountPass.Stable( counts.UpgradedRides ),
		RatingCountPass.Stable( counts.Handymen ), RatingCountPass.Stable( counts.Mechanics ),
		RatingCountPass.Stable( counts.Entertainers ), RatingCountPass.Stable( counts.Guards ),
		RatingCountPass.Stable( counts.Researchers ) ) );

	public static ParkRatingComponents Calculate( ParkRatingObservations counts )
	{
		// 0xc7bec..0xc7c24: cap population before low-word multiply and signed /1000.
		var visitorCount = counts.Visitors.CapCheck < 1000 ? counts.Visitors.Recount : 1000;
		var visitors = unchecked(visitorCount * 20) / 1000;
		return new ParkRatingComponents( visitors,
			ScaledBelowCap( counts.Rides, 3, 2, 20 ),
			ScaledBelowCap( counts.Shops, 2, 1, 10 ),
			ScaledBelowCap( counts.Sideshows, 2, 1, 10 ),
			ScaledBelowCap( counts.Features, 1, 1, 10 ),
			ScaledBelowCap( counts.UpgradedRides, 1, 1, 10 ),
			ScaledBelowCap( counts.Handymen, 1, 1, 4 ),
			ScaledBelowCap( counts.Mechanics, 1, 1, 4 ),
			ScaledBelowCap( counts.Entertainers, 1, 1, 4 ),
			ScaledBelowCap( counts.Guards, 1, 1, 4 ),
			ScaledBelowCap( counts.Researchers, 1, 1, 4 ) );
	}

	private static int ScaledBelowCap( RatingCountPass count, int multiplier, int divisor, int cap )
	{
		// Native ride /2 uses srawi + addze, i.e. signed truncation toward zero.
		// The scaled inline result selects the branch; the recount is not capped again.
		var gate = unchecked(count.CapCheck * multiplier) / divisor;
		return gate < cap ? unchecked(count.Recount * multiplier) / divisor : cap;
	}
}

/// <summary>Host IEEE-f32 observations of code 0xf41dc, with the inspected conversion order.</summary>
public readonly record struct StaffSkillEstimate( float GradeAndFraction, float BeforeTruncation, uint Skill );

/// <summary>
/// Raw grade is a signed word and percentage is the lbz byte at staff +488. The native routine
/// applies no grade/percentage/100-point normalization. Original floating rounding state and
/// unusual-input reachability are unqualified; ordinary grade 0..5/byte examples are reviewed.
/// </summary>
public static class OriginalStaffSkill
{
	public static StaffSkillEstimate Calculate( int grade, byte percentage )
	{
		// fdiv is double; fsubs rounds the integer grade to single precision.
		var fraction = percentage / 100d;
		var singleGrade = (float)grade;
		// fadd then frsp, followed by fmuls with the single-precision literal 20.
		var combined = (float)(singleGrade + fraction);
		var score = 20f * combined;
		return new StaffSkillEstimate( combined, score, OriginalLoanRules.SaturatingUnsigned( score ) );
	}
}
