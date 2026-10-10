using System;

namespace OpenTPW.Reverse.Economy;

/// <summary>Unsigned loan words from the inspected Mac routines, without save framing.</summary>
public readonly record struct LoanWords( bool Available, uint Principal, uint AprPercent, uint Months,
	uint MonthlyRepayment, bool Bought, uint MonthsRepaid, uint LenderNameIndex );

/// <summary>The bank fields touched by the per-loan operations; balances preserve signed display bits.</summary>
public readonly record struct BankWords( int Balance, int ProfitThisYear, bool WithdrawalsEnabled );

/// <summary>A host-libm estimate, not a claim of bit-identical original PowerPC pow results.</summary>
public readonly record struct HostRepaymentEstimate( double BeforeConversion, uint Repayment );

/// <summary>No quotient is supplied for divisor zero: native DIVWU zero behavior is unqualified.</summary>
public readonly record struct UnsignedDivisionResult( uint Dividend, uint Divisor, uint? Quotient )
{
	public bool IsQualified => Quotient.HasValue;
}

public enum LoanOperationOutcome
{
	Applied,
	NoBoughtLoan,
	Unaffordable
}

public enum TransitionQualification
{
	ReviewedIntegerOperations,
	AssumesZeroTermDiagnosticReturns
}

/// <summary>
/// A per-loan arithmetic transition. A null interest division means that it was not reached
/// or was skipped by the original zero-term guard, not that its quotient was zero.
/// </summary>
public readonly record struct LoanTransition( BankWords Bank, LoanWords Loan, LoanOperationOutcome Outcome,
	uint QuotedAmount, uint CashDebited, UnsignedDivisionResult? InterestDivision, TransitionQualification Qualification );

/// <summary>
/// Standalone interpretation of reviewed Mac loan arithmetic (PPC-economy.md / PPC-review.md).
/// This is not wired into gameplay and does not execute original instructions. It omits batch
/// flushing, calendar/year events, other bank statistics, red-entry timestamps, capacity updates,
/// and diagnostic handling.
/// Reachability, host/original libm equivalence and Windows behavior require separate evidence.
/// </summary>
public static class OriginalLoanRules
{
	private const double UnsignedLimit = 4294967296d;

	/// <summary>Code 0x1c3fbc: negative values map to zero; NaN/+infinity/overflow map to uint.MaxValue.</summary>
	public static uint SaturatingUnsigned( double value )
	{
		if ( value < 0d )
			return 0;
		if ( !(value < UnsignedLimit) )
			return uint.MaxValue;
		return (uint)Math.Truncate( value );
	}

	/// <summary>
	/// Constructor 0xcb7a8. Maintains the inspected operation order, including floating division
	/// by zero. .NET Math.Pow supplies the host estimate; it is not the original math library.
	/// </summary>
	public static HostRepaymentEstimate EstimateRepayment( uint principal, uint aprPercent, uint months )
	{
		var exponent = (months / 12d) * 0.5d;
		var factor = Math.Pow( 1d + aprPercent / 100d, exponent );
		var product = principal * factor;
		var beforeConversion = product / months;
		return new HostRepaymentEstimate( beforeConversion, SaturatingUnsigned( beforeConversion ) );
	}

	/// <summary>Pure operand model: divisor zero deliberately has no qualified quotient.</summary>
	public static UnsignedDivisionResult DivideUnsigned( uint dividend, uint divisor ) =>
		new( dividend, divisor, divisor == 0 ? null : dividend / divisor );

	/// <summary>0xcc378..0xcc384: low 32-bit product/subtraction before unsigned division.</summary>
	public static UnsignedDivisionResult InterestPerInstallment( LoanWords loan )
	{
		var dividend = unchecked(loan.MonthlyRepayment * loan.Months - loan.Principal);
		return DivideUnsigned( dividend, loan.Months );
	}

	/// <summary>0xcccd0..0xcccd8: low 32 bits of M * (term - months repaid).</summary>
	public static uint RawPayoffAmount( LoanWords loan ) =>
		unchecked(loan.MonthlyRepayment * (loan.Months - loan.MonthsRepaid));

	/// <summary>0xcccdc: affordability compares balance bits as unsigned, including negative displays.</summary>
	public static bool CanAffordPayoff( int balance, uint payoff ) => unchecked((uint)balance) >= payoff;

	/// <summary>
	/// Per-loan part of 0xcc21c. The original outer month handler flushes batch balance first.
	/// Completion is equality-only. Zero term bypasses division, but continuation past the original
	/// diagnostic call is explicitly conditional; this helper does not implement that diagnostic.
	/// </summary>
	public static LoanTransition ApplyMonthlyInstallment( BankWords bank, LoanWords loan )
	{
		if ( !loan.Bought )
			return Unchanged( bank, loan, LoanOperationOutcome.NoBoughtLoan, 0 );
		var payment = loan.MonthlyRepayment;
		var debit = bank.WithdrawalsEnabled ? payment : 0;
		bank = Debit( bank, debit );
		loan = loan with { MonthsRepaid = unchecked(loan.MonthsRepaid + 1) };
		UnsignedDivisionResult? interest = null;
		if ( loan.Months != 0 )
		{
			interest = InterestPerInstallment( loan );
			bank = AddProfit( bank, unchecked(payment - interest.Value.Quotient!.Value) );
		}
		if ( loan.MonthsRepaid == loan.Months )
			loan = loan with { Bought = false, MonthsRepaid = 0 };
		return new LoanTransition( bank, loan, LoanOperationOutcome.Applied, payment, debit, interest, Qualification( loan ) );
	}

	/// <summary>
	/// 0xccc78. The loan is cleared before the profit adjustment reads months repaid again.
	/// Withdrawals-disabled still requires the unsigned affordability test, then clears the loan.
	/// </summary>
	public static LoanTransition TryEarlyPayoff( BankWords bank, LoanWords loan )
	{
		if ( !loan.Bought )
			return Unchanged( bank, loan, LoanOperationOutcome.NoBoughtLoan, 0 );
		var payoff = RawPayoffAmount( loan );
		if ( !CanAffordPayoff( bank.Balance, payoff ) )
			return Unchanged( bank, loan, LoanOperationOutcome.Unaffordable, payoff );
		var debit = bank.WithdrawalsEnabled ? payoff : 0;
		bank = Debit( bank, debit );
		loan = loan with { Bought = false, MonthsRepaid = 0 };
		UnsignedDivisionResult? interest = null;
		if ( loan.Months != 0 )
		{
			interest = InterestPerInstallment( loan );
			var interestTotal = unchecked(interest.Value.Quotient!.Value * (loan.Months - loan.MonthsRepaid));
			bank = AddProfit( bank, unchecked(payoff - interestTotal) );
		}
		return new LoanTransition( bank, loan, LoanOperationOutcome.Applied, payoff, debit, interest, Qualification( loan ) );
	}

	private static LoanTransition Unchanged( BankWords bank, LoanWords loan, LoanOperationOutcome outcome, uint quote ) =>
		new( bank, loan, outcome, quote, 0, null, TransitionQualification.ReviewedIntegerOperations );

	private static TransitionQualification Qualification( LoanWords loan ) => loan.Months == 0
		? TransitionQualification.AssumesZeroTermDiagnosticReturns
		: TransitionQualification.ReviewedIntegerOperations;

	private static BankWords Debit( BankWords bank, uint amount ) => bank with
	{
		Balance = unchecked((int)((uint)bank.Balance - amount)),
		ProfitThisYear = unchecked((int)((uint)bank.ProfitThisYear - amount))
	};

	private static BankWords AddProfit( BankWords bank, uint amount ) =>
		bank with { ProfitThisYear = unchecked((int)((uint)bank.ProfitThisYear + amount)) };
}
