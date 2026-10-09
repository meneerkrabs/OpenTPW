using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
public class SaveEconomyRecordTests
{
	private const int LoanOffset = 128;
	private const int ChallengeOffset = 512;

	private static void Int( byte[] payload, int offset, int value ) =>
		BinaryPrimitives.WriteInt32LittleEndian( payload.AsSpan( offset, 4 ), value );

	private static byte[] Payload( int apr = 20 )
	{
		var payload = Enumerable.Repeat( (byte)0xCD, 1024 ).ToArray();
		var bank = new[] { 25, 87987, 0, 1, 87787, 0, -12013 };
		for ( var index = 0; index < bank.Length; index++ )
			Int( payload, LoanOffset - 28 + index * 4, bank[index] );
		var lenders = new[] { 7, 3, 11, 0 };
		for ( var index = 0; index < 4; index++ )
		{
			var offset = LoanOffset + index * SaveEconomyRecords.LoanRecordSize;
			var amount = 10000 * (index + 1);
			var loan = new[] { index == 0 ? 1 : 0, amount, apr, 36, amount / 36 + (apr == 0 ? 0 : 100), index == 2 ? 1 : 0, index == 2 ? 5 : 0, lenders[index] };
			for ( var word = 0; word < loan.Length; word++ )
				Int( payload, offset + word * 4, loan[word] );
		}
		for ( var index = 0; index < 2; index++ )
		{
			var offset = ChallengeOffset + index * SaveEconomyRecords.ChallengeRecordSize;
			Array.Clear( payload, offset, SaveEconomyRecords.ChallengeRecordSize );
			Int( payload, offset, 3 + index );
			Int( payload, offset + 4, 60 );
			Int( payload, offset + 8, 30 );
			Int( payload, offset + 20, 5000 );
			payload[offset + 42] = 1;
		}
		return payload;
	}

	[TestMethod]
	public void PositiveAprAndPermutedLendersDoNotBecomeAnInt64AmountOrRecordIndex()
	{
		var records = SaveEconomyRecords.Parse( Payload() );
		Assert.AreEqual( 4, records.Loans.Count );
		Assert.AreEqual( LoanOffset, records.Loans[0].Offset );
		CollectionAssert.AreEqual( new long[] { 10000, 20000, 30000, 40000 }, records.Loans.Select( loan => (long)loan.Amount ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3 }, records.Loans.Select( loan => loan.Index ).ToArray() );
		Assert.AreEqual( new SaveBankRecord( LoanOffset - 28, 25, 87987, 0, true, 87787, 0, -12013 ), records.Bank );
		Assert.AreEqual( new SaveLoanRecord( LoanOffset + 64, 2, false, 30000, 20, 36, 933, true, 5, 11 ), records.Loans[2] );
		CollectionAssert.AreEqual( new[] { 7, 3, 11, 0 }, records.Loans.Select( loan => loan.LenderNameIndex ).ToArray() );
	}

	[DataTestMethod]
	[DataRow( 2 )]
	[DataRow( -1 )]
	public void NonBooleanBankWithdrawalFlagIsRejected( int invalid )
	{
		var payload = Payload();
		Int( payload, LoanOffset - 28 + 12, invalid );
		var error = Assert.ThrowsException<InvalidDataException>( () => SaveEconomyRecords.Parse( payload ) );
		StringAssert.Contains( error.Message, "non-boolean withdrawals-enabled flag" );
	}

	[TestMethod]
	public void EachTruncatedBankPrefixIsRejectedBeforeReadingFields()
	{
		var full = Payload();
		for ( var length = 0; length < SaveEconomyRecords.BankRecordSize; length++ )
		{
			var payload = Enumerable.Repeat( (byte)0xCD, 1024 ).ToArray();
			Array.Copy( full, LoanOffset - length, payload, 0, length + 4 * SaveEconomyRecords.LoanRecordSize );
			Array.Copy( full, ChallengeOffset, payload, ChallengeOffset, 2 * SaveEconomyRecords.ChallengeRecordSize );
			var error = Assert.ThrowsException<InvalidDataException>( () => SaveEconomyRecords.Parse( payload ), $"bank prefix length {length}" );
			StringAssert.Contains( error.Message, "truncated bank prefix" );
		}
	}

	[TestMethod]
	public void ZeroAprStillAllowsLendersToDifferFromRecordOrder()
	{
		var records = SaveEconomyRecords.Parse( Payload( apr: 0 ) );
		Assert.AreEqual( LoanOffset, records.Loans[0].Offset );
		Assert.AreEqual( 4, records.Loans.Count );
	}

	[DataTestMethod]
	[DataRow( 0, 2 )]
	[DataRow( 0, -1 )]
	[DataRow( 20, 2 )]
	[DataRow( 20, -1 )]
	public void NonBooleanLoanFlagsRejectTheCandidate( int fieldOffset, int invalid )
	{
		var payload = Payload( apr: 0 );
		for ( var index = 0; index < 4; index++ )
			Int( payload, LoanOffset + index * SaveEconomyRecords.LoanRecordSize + 28, index );
		Int( payload, LoanOffset + fieldOffset, invalid );
		var error = Assert.ThrowsException<InvalidDataException>( () => SaveEconomyRecords.Parse( payload ) );
		StringAssert.Contains( error.Message, "no loan offer table" );
	}

	[TestMethod]
	public void TwoSeparateLoanTablesAreAmbiguous()
	{
		var payload = Payload();
		Array.Copy( payload, LoanOffset - 28, payload, 700, 28 + 4 * SaveEconomyRecords.LoanRecordSize );
		var error = Assert.ThrowsException<InvalidDataException>( () => SaveEconomyRecords.Parse( payload ) );
		StringAssert.Contains( error.Message, "more than one loan offer table" );
	}

	[TestMethod]
	public void FewerThanFourCompleteLoanRecordsAreRejectedAtEveryTruncatedLength()
	{
		var full = Payload();
		var loans = full.AsSpan( LoanOffset, 4 * SaveEconomyRecords.LoanRecordSize ).ToArray();
		for ( var length = 0; length < loans.Length; length++ )
		{
			var payload = Enumerable.Repeat( (byte)0xCD, 128 + length ).ToArray();
			Array.Copy( full, ChallengeOffset, payload, 0, 2 * SaveEconomyRecords.ChallengeRecordSize );
			Array.Copy( loans, 0, payload, 128, length );
			Assert.ThrowsException<InvalidDataException>( () => SaveEconomyRecords.Parse( payload ), $"truncated loan bytes {length}" );
		}
	}

	private static (BalanceSettings Settings, byte[] Payload) MatchingSettingsPayload( int apr = 0 )
	{
		var settings = EconomyTestData.Settings( apr: apr, extra: $"LoanInfo[3].LoanAmount 10000\nLoanInfo[3].APRInPercent {apr}\nLoanInfo[3].RepaymentPeriodInMonths 24\nLoanInfo[3].Lendername 7\n" );
		var payload = Payload( apr );
		var positiveRepayments = new[] { 3000, 1500, 800, 458 };
		for ( var index = 0; index < settings.Loans.Count; index++ )
		{
			var offer = settings.Loans[index];
			var offset = LoanOffset + index * SaveEconomyRecords.LoanRecordSize;
			Int( payload, offset + 4, checked((int)offer.Amount) );
			Int( payload, offset + 12, offer.Months );
			Int( payload, offset + 16, apr == 0 ? checked((int)(offer.Amount / offer.Months)) : positiveRepayments[index] );
			Int( payload, offset + 28, offer.LenderNameIndex );
		}
		for ( var index = 0; index < settings.ChallengesInThisLevel.Count; index++ )
		{
			var definition = settings.Challenges[settings.ChallengesInThisLevel[index]];
			var offset = ChallengeOffset + index * SaveEconomyRecords.ChallengeRecordSize;
			Array.Clear( payload, offset, SaveEconomyRecords.ChallengeRecordSize );
			var words = new[] { definition.Type, definition.TargetTime, definition.TargetValue, definition.TargetObject, definition.TargetObject2, checked((int)definition.Prize), definition.FollowupType };
			for ( var word = 0; word < words.Length; word++ )
				Int( payload, offset + word * 4, words[word] );
			payload[offset + 42] = (byte)(definition.Independent ? 1 : 0);
		}
		return (settings, payload);
	}

	[TestMethod]
	public void PositiveAprRepaymentDifferentFromAnnuityKeepsExactMetadataAndReportsUncertainty()
	{
		var (settings, payload) = MatchingSettingsPayload( apr: 10 );
		var records = SaveEconomyRecords.Parse( payload );
		Assert.AreEqual( 458, records.Loans[3].MonthlyRepayment, "fixed test input, independent of LoanMath" );
		Assert.AreNotEqual( (long)records.Loans[3].MonthlyRepayment, LoanMath.MonthlyRepayment( 10000, 10, 24 ) );
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var imported = OriginalEconomyImport.Apply( economy, records, Array.Empty<int>(), Array.Empty<int>() );
		Assert.AreEqual( 4, imported.Records.Loans.Count );
		Assert.IsTrue( imported.Evidence.Any( line => line.Contains( "loan offer 3" ) && line.Contains( "458" ) && line.Contains( "[APPROX:ECON-006]" ) && line.Contains( "unverified" ) ) );
		Assert.IsFalse( imported.Evidence.Any( line => line.Contains( "and repayments" ) ), "unknown positive-APR repayments must not be reported as matched" );
		Assert.AreEqual( settings.InitialCash, economy.Balance );
		Assert.IsFalse( economy.Loans.Any() );
	}

	[TestMethod]
	public void PositiveAprRepaymentMatchingAnnuityStillHasUnverifiedProvenance()
	{
		var (settings, payload) = MatchingSettingsPayload( apr: 10 );
		Int( payload, LoanOffset + 3 * SaveEconomyRecords.LoanRecordSize + 16, 461 );
		var records = SaveEconomyRecords.Parse( payload );
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var imported = OriginalEconomyImport.Apply( economy, records, Array.Empty<int>(), Array.Empty<int>() );
		Assert.IsTrue( imported.Evidence.Any( line => line.Contains( "loan offer 3" ) && line.Contains( "461" ) && line.Contains( "unverified" ) ) );
		Assert.IsFalse( imported.Evidence.Any( line => line.Contains( "zero-APR monthly repayments" ) ) );
	}

	[TestMethod]
	public void ZeroAprMonthlyRepaymentMismatchStillRejects()
	{
		var (settings, payload) = MatchingSettingsPayload();
		Int( payload, LoanOffset + 16, 2778 );
		var records = SaveEconomyRecords.Parse( payload );
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var error = Assert.ThrowsException<InvalidDataException>( () => OriginalEconomyImport.Apply( economy, records, Array.Empty<int>(), Array.Empty<int>() ) );
		StringAssert.Contains( error.Message, "zero-APR monthly repayment" );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 10 )]
	public void OriginalSettingsCrossCheckRejectsAprLenderTermAndAmountMismatch( int apr )
	{
		var (settings, payload) = MatchingSettingsPayload( apr );
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var matched = OriginalEconomyImport.Apply( economy, SaveEconomyRecords.Parse( payload ), Array.Empty<int>(), Array.Empty<int>() );
		Assert.AreEqual( 4, matched.Records.Loans.Count );
		Assert.AreEqual( settings.InitialCash, economy.Balance );
		Assert.IsFalse( economy.Loans.Any(), "decoded bought flags do not restore loans in this parser correction" );
		foreach ( var (field, value) in new[] { (8, apr + 1), (28, 99), (12, 37), (4, 100001) } )
		{
			var changed = payload.ToArray();
			Int( changed, LoanOffset + field, value );
			var records = SaveEconomyRecords.Parse( changed );
			var error = Assert.ThrowsException<InvalidDataException>( () => OriginalEconomyImport.Apply( economy, records, Array.Empty<int>(), Array.Empty<int>() ) );
			StringAssert.Contains( error.Message, "Save loan offer 0" );
		}
	}
}
