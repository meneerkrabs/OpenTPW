using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenTPW.Reverse.Economy;

internal static class OriginalLoanRulesTests
{
	private static int assertions;
	private static int failures;
	private static int groups;

	public static int Main()
	{
		Run( "saturating conversion boundaries", Saturation );
		Run( "host nonzero-APR examples", HostExamples );
		Run( "zero-APR unsigned interest underflow", ZeroAprUnderflow );
		Run( "fixed monthly installment and completion", MonthlyCompletion );
		Run( "withdrawals-disabled monthly bookkeeping", MonthlyWithoutWithdrawals );
		Run( "early payoff clears before profit adjustment", PayoffClearsBeforeProfit );
		Run( "withdrawals-disabled early payoff", PayoffWithoutWithdrawals );
		Run( "unsigned negative-balance affordability", UnsignedAffordability );
		Run( "inactive and unaffordable paths", RefusalPaths );
		Run( "32-bit payoff and profit wrap", WrapOperations );
		Run( "equality-only repayment termination", EqualityOnly );
		Run( "NaN, overflow and zero-term host results", FloatingEdges );
		Run( "undefined native division is explicit", UnqualifiedDivision );
		Run( "zero-term guarded transitions are conditional", ZeroTermGuards );
		Run( "bounded BigInteger operand cross-check", IntegerCrossCheck );
		Console.WriteLine( $"{groups - failures}/{groups} groups passed; {assertions} assertions; {failures} failures." );
		return failures == 0 ? 0 : 1;
	}

	private static LoanWords Loan( uint principal = 100000, uint apr = 20, uint months = 36, uint repaid = 0, bool bought = true ) =>
		new( false, principal, apr, months, OriginalLoanRules.EstimateRepayment( principal, apr, months ).Repayment, bought, repaid, 7 );

	private static void Run( string name, Action test )
	{
		groups++;
		try
		{
			test();
			Console.WriteLine( $"PASS {name}" );
		}
		catch ( Exception error )
		{
			failures++;
			Console.Error.WriteLine( $"FAIL {name}: {error.Message}" );
		}
	}

	private static void Equal<T>( T expected, T actual )
	{
		assertions++;
		if ( !EqualityComparer<T>.Default.Equals( expected, actual ) )
			throw new InvalidOperationException( $"expected {expected}, got {actual}" );
	}

	private static void Saturation()
	{
		foreach ( var value in new[] { -1d, -double.Epsilon, double.NegativeInfinity, -0d, 0d } )
			Equal( 0u, OriginalLoanRules.SaturatingUnsigned( value ) );
		Equal( 12u, OriginalLoanRules.SaturatingUnsigned( 12.999 ) );
		Equal( uint.MaxValue, OriginalLoanRules.SaturatingUnsigned( double.BitDecrement( 4294967296d ) ) );
		foreach ( var value in new[] { 4294967296d, double.MaxValue, double.PositiveInfinity, double.NaN } )
			Equal( uint.MaxValue, OriginalLoanRules.SaturatingUnsigned( value ) );
	}

	private static void HostExamples()
	{
		var offers = new (uint P, uint A, uint N, uint M)[]
		{
			(100000, 20, 36, 3651), (50000, 20, 36, 1825), (25000, 20, 36, 912),
			(10000, 20, 36, 365), (18000, 23, 24, 922), (30000, 22, 30, 1282),
			(80000, 18, 48, 2320), (65000, 21, 30, 2749)
		};
		foreach ( var (p, a, n, m) in offers )
			Equal( m, OriginalLoanRules.EstimateRepayment( p, a, n ).Repayment );
		Equal( 873u, OriginalLoanRules.InterestPerInstallment( Loan() ).Quotient!.Value );
	}

	private static void ZeroAprUnderflow()
	{
		var loan = Loan( apr: 0 );
		Equal( 2777u, loan.MonthlyRepayment );
		var interest = OriginalLoanRules.InterestPerInstallment( loan );
		Equal( 4294967268u, interest.Dividend );
		Equal( 119304646u, interest.Quotient!.Value );
		var result = OriginalLoanRules.ApplyMonthlyInstallment( new BankWords( 100000, 0, true ), loan );
		Equal( 97223, result.Bank.Balance );
		Equal( -119304646, result.Bank.ProfitThisYear );
		var cases = new (uint P, uint N, uint Q)[]
		{
			(100000, 36, 119304646), (50000, 36, 119304646), (25000, 36, 119304646),
			(10000, 36, 119304646), (18000, 24, 0), (30000, 30, 0),
			(80000, 48, 89478484), (65000, 30, 143165575)
		};
		foreach ( var (p, n, q) in cases )
			Equal( q, OriginalLoanRules.InterestPerInstallment( Loan( p, 0, n ) ).Quotient!.Value );
	}

	private static void MonthlyCompletion()
	{
		var loan = Loan();
		var bank = new BankWords( 200000, 0, true );
		for ( var month = 1; month <= 36; month++ )
		{
			var result = OriginalLoanRules.ApplyMonthlyInstallment( bank, loan );
			Equal( 3651u, result.QuotedAmount );
			Equal( 3651u, result.CashDebited );
			Equal( TransitionQualification.ReviewedIntegerOperations, result.Qualification );
			bank = result.Bank;
			loan = result.Loan;
			Equal( month < 36, loan.Bought );
		}
		Equal( 68564, bank.Balance );
		Equal( -31428, bank.ProfitThisYear );
		Equal( 0u, loan.MonthsRepaid );
		Equal( 100000u, loan.Principal );
		loan = Loan( apr: 0 );
		bank = new BankWords( 100000, 0, true );
		for ( var month = 0; month < 36; month++ )
		{
			var result = OriginalLoanRules.ApplyMonthlyInstallment( bank, loan );
			bank = result.Bank;
			loan = result.Loan;
		}
		Equal( 28, bank.Balance );
		Equal( 40, bank.ProfitThisYear );
		Equal( false, loan.Bought );
	}

	private static void MonthlyWithoutWithdrawals()
	{
		var bank = new BankWords( 100000, 0, false );
		var result = OriginalLoanRules.ApplyMonthlyInstallment( bank, Loan() );
		Equal( 100000, result.Bank.Balance );
		Equal( 2778, result.Bank.ProfitThisYear );
		Equal( 0u, result.CashDebited );
		Equal( 1u, result.Loan.MonthsRepaid );
		var zeroApr = OriginalLoanRules.ApplyMonthlyInstallment( bank, Loan( apr: 0 ) );
		Equal( 100000, zeroApr.Bank.Balance );
		Equal( -119301869, zeroApr.Bank.ProfitThisYear );
		Equal( 0u, zeroApr.CashDebited );
	}

	private static void PayoffClearsBeforeProfit()
	{
		var loan = Loan( repaid: 5 );
		var result = OriginalLoanRules.TryEarlyPayoff( new BankWords( 200000, 0, true ), loan );
		Equal( 113181u, result.QuotedAmount );
		Equal( 86819, result.Bank.Balance );
		Equal( -31428, result.Bank.ProfitThisYear );
		Equal( 0u, result.Loan.MonthsRepaid );
		Equal( false, result.Loan.Bought );
		Equal( loan.Available, result.Loan.Available );
		Equal( loan.LenderNameIndex, result.Loan.LenderNameIndex );
	}

	private static void PayoffWithoutWithdrawals()
	{
		var result = OriginalLoanRules.TryEarlyPayoff( new BankWords( 200000, 0, false ), Loan( repaid: 5 ) );
		Equal( LoanOperationOutcome.Applied, result.Outcome );
		Equal( 200000, result.Bank.Balance );
		Equal( 81753, result.Bank.ProfitThisYear );
		Equal( 0u, result.CashDebited );
		Equal( false, result.Loan.Bought );
		var noMoney = OriginalLoanRules.TryEarlyPayoff( new BankWords( 0, 0, false ), Loan() );
		Equal( LoanOperationOutcome.Unaffordable, noMoney.Outcome );
		Equal( true, noMoney.Loan.Bought );
	}

	private static void UnsignedAffordability()
	{
		Equal( true, OriginalLoanRules.CanAffordPayoff( -1, 131436 ) );
		Equal( true, OriginalLoanRules.CanAffordPayoff( -1, uint.MaxValue ) );
		Equal( false, OriginalLoanRules.CanAffordPayoff( -2, uint.MaxValue ) );
		var result = OriginalLoanRules.TryEarlyPayoff( new BankWords( -1, 0, true ), Loan() );
		Equal( LoanOperationOutcome.Applied, result.Outcome );
		Equal( -131437, result.Bank.Balance );
	}

	private static void RefusalPaths()
	{
		var bank = new BankWords( 1, 7, true );
		var loan = Loan();
		var refused = OriginalLoanRules.TryEarlyPayoff( bank, loan );
		Equal( LoanOperationOutcome.Unaffordable, refused.Outcome );
		Equal( bank, refused.Bank );
		Equal( loan, refused.Loan );
		loan = loan with { Bought = false };
		foreach ( var result in new[] { OriginalLoanRules.TryEarlyPayoff( bank, loan ), OriginalLoanRules.ApplyMonthlyInstallment( bank, loan ) } )
		{
			Equal( LoanOperationOutcome.NoBoughtLoan, result.Outcome );
			Equal( bank, result.Bank );
			Equal( loan, result.Loan );
		}
	}

	private static void WrapOperations()
	{
		var loan = Loan() with { MonthlyRepayment = uint.MaxValue, Months = 2, MonthsRepaid = 0, Principal = 0 };
		Equal( 4294967294u, OriginalLoanRules.RawPayoffAmount( loan ) );
		Equal( 4294967294u, OriginalLoanRules.InterestPerInstallment( loan ).Dividend );
		Equal( 2147483647u, OriginalLoanRules.InterestPerInstallment( loan ).Quotient!.Value );
		loan = loan with { MonthsRepaid = 3 };
		Equal( 1u, OriginalLoanRules.RawPayoffAmount( loan ) );
		loan = loan with { Principal = 0, Months = 1, MonthsRepaid = 0 };
		var monthly = OriginalLoanRules.ApplyMonthlyInstallment( new BankWords( 0, int.MaxValue, true ), loan );
		Equal( 1, monthly.Bank.Balance );
		Equal( int.MinValue, monthly.Bank.ProfitThisYear );
	}

	private static void EqualityOnly()
	{
		var loan = Loan( months: 2, repaid: 3 );
		var beyond = OriginalLoanRules.ApplyMonthlyInstallment( new BankWords( 1000000, 0, true ), loan );
		Equal( 4u, beyond.Loan.MonthsRepaid );
		Equal( true, beyond.Loan.Bought );
		loan = loan with { MonthsRepaid = uint.MaxValue };
		var wrap = OriginalLoanRules.ApplyMonthlyInstallment( new BankWords( 1000000, 0, true ), loan );
		Equal( 0u, wrap.Loan.MonthsRepaid );
		Equal( true, wrap.Loan.Bought );
	}

	private static void FloatingEdges()
	{
		var zero = OriginalLoanRules.EstimateRepayment( 100000, 20, 0 );
		Equal( true, double.IsPositiveInfinity( zero.BeforeConversion ) );
		Equal( uint.MaxValue, zero.Repayment );
		var nan = OriginalLoanRules.EstimateRepayment( 0, 0, 0 );
		Equal( true, double.IsNaN( nan.BeforeConversion ) );
		Equal( uint.MaxValue, nan.Repayment );
		var overflow = OriginalLoanRules.EstimateRepayment( uint.MaxValue, uint.MaxValue, uint.MaxValue );
		Equal( true, double.IsPositiveInfinity( overflow.BeforeConversion ) );
		Equal( uint.MaxValue, overflow.Repayment );
		Equal( 0u, OriginalLoanRules.EstimateRepayment( 0, 0, 36 ).Repayment );
	}

	private static void UnqualifiedDivision()
	{
		var division = OriginalLoanRules.DivideUnsigned( 17, 0 );
		Equal( false, division.IsQualified );
		Equal<uint?>( null, division.Quotient );
		Equal( 17u, division.Dividend );
		Equal( 0u, division.Divisor );
		var interest = OriginalLoanRules.InterestPerInstallment( Loan( months: 0 ) );
		Equal( false, interest.IsQualified );
		Equal<uint?>( null, interest.Quotient );
	}

	private static void ZeroTermGuards()
	{
		var loan = Loan( months: 0 );
		var bank = new BankWords( 100, 0, true );
		var monthly = OriginalLoanRules.ApplyMonthlyInstallment( bank, loan );
		Equal( TransitionQualification.AssumesZeroTermDiagnosticReturns, monthly.Qualification );
		Equal<UnsignedDivisionResult?>( null, monthly.InterestDivision );
		Equal( 101, monthly.Bank.Balance );
		Equal( 1, monthly.Bank.ProfitThisYear );
		Equal( 1u, monthly.Loan.MonthsRepaid );
		Equal( true, monthly.Loan.Bought );
		var payoff = OriginalLoanRules.TryEarlyPayoff( bank, loan );
		Equal( 0u, payoff.QuotedAmount );
		Equal( TransitionQualification.AssumesZeroTermDiagnosticReturns, payoff.Qualification );
		Equal<UnsignedDivisionResult?>( null, payoff.InterestDivision );
		Equal( false, payoff.Loan.Bought );
		var wrap = OriginalLoanRules.ApplyMonthlyInstallment( bank, loan with { MonthsRepaid = uint.MaxValue } );
		Equal( false, wrap.Loan.Bought );
	}

	private static uint Word( BigInteger value ) => (uint)(value & uint.MaxValue);

	private static void IntegerCrossCheck()
	{
		var values = new[] { 0u, 1u, 17u, 100000u, 2147483648u, uint.MaxValue };
		foreach ( var principal in values )
		foreach ( var monthly in values )
		foreach ( var months in values )
		foreach ( var repaid in values )
		{
			var loan = new LoanWords( false, principal, 20, months, monthly, true, repaid, 7 );
			var expectedPayoff = Word( (BigInteger)monthly * (months - (BigInteger)repaid) );
			Equal( expectedPayoff, OriginalLoanRules.RawPayoffAmount( loan ) );
			var dividend = Word( (BigInteger)monthly * months - principal );
			var interest = OriginalLoanRules.InterestPerInstallment( loan );
			Equal( dividend, interest.Dividend );
			Equal( months != 0, interest.IsQualified );
			Equal<uint?>( months == 0 ? null : dividend / months, interest.Quotient );
		}
	}
}
