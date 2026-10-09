using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>One 32-byte loan-offer record of an original save payload.</summary>
public readonly record struct SaveLoanRecord( int Offset, int Index, long Amount, int Months, int MonthlyRepayment, int Opaque16, int Opaque20, int Opaque28 );

/// <summary>One 45-byte challenge record of an original save payload (field order differs from <c>Challenges.sam</c>).</summary>
public readonly record struct SaveChallengeRecord( int Offset, int Type, int TargetTime, int TargetValue, int TargetObject, int TargetObject2, int Prize, int FollowupType, bool Independent );

/// <summary>
/// Economy records located in a decoded TPWI/TPWS payload by structure (docs/ECONOMY.md,
/// docs/TPWS-PAYLOAD.md). Only one fixture exists, so the locators are strict and the caller must
/// cross-check the records against the original settings before trusting them:
/// <list type="bullet">
/// <item>Loan offers: consecutive 32-byte records <c>i64 amount, i32 months, i32 monthly repayment,
/// i32 ?, i32 ?, i32 index, i32 ?</c> with indices 0, 1, 2, …</item>
/// <item>Challenges: consecutive 45-byte records <c>i32 type, time, value, object, object2, prize,
/// follow-up type</c>, 14 zero bytes, <c>u8 independent</c>, two zero bytes.</item>
/// <item>The 32 bytes before the loan table (observed: <c>25, 87987 (i64), 1, 87787 (i64),
/// −12013, 0</c>) are reported raw: they look like balance values but are not reconciled.</item>
/// </list>
/// </summary>
public sealed class SaveEconomyRecords
{
	public const int LoanRecordSize = 32;
	public const int ChallengeRecordSize = 45;
	public const int MinimumLoanRecords = 4;
	public const int MinimumChallengeRecords = 2;

	private SaveEconomyRecords( IReadOnlyList<SaveLoanRecord> loans, IReadOnlyList<SaveChallengeRecord> challenges, IReadOnlyList<int> wordsBeforeLoans )
	{
		Loans = loans;
		Challenges = challenges;
		WordsBeforeLoans = wordsBeforeLoans;
	}

	public IReadOnlyList<SaveLoanRecord> Loans { get; }
	public IReadOnlyList<SaveChallengeRecord> Challenges { get; }
	/// <summary>Eight little-endian i32 words immediately before the loan table (opaque).</summary>
	public IReadOnlyList<int> WordsBeforeLoans { get; }

	/// <summary>Locates the records; throws when either table is missing or ambiguous.</summary>
	public static SaveEconomyRecords Parse( ReadOnlySpan<byte> payload )
	{
		var loans = FindUnique( payload, LoanRecordSize, MinimumLoanRecords, IsLoanRecord, "loan offer" );
		var challenges = FindUnique( payload, ChallengeRecordSize, MinimumChallengeRecords, IsChallengeRecord, "challenge" );
		var loanRecords = new List<SaveLoanRecord>();
		for ( var index = 0; index < loans.Count; index++ )
			loanRecords.Add( ReadLoan( payload, loans.Offset + index * LoanRecordSize ) );
		var challengeRecords = new List<SaveChallengeRecord>();
		for ( var index = 0; index < challenges.Count; index++ )
			challengeRecords.Add( ReadChallenge( payload, challenges.Offset + index * ChallengeRecordSize ) );
		var words = new List<int>();
		for ( var index = 0; loans.Offset >= 32 && index < 8; index++ )
			words.Add( Int( payload, loans.Offset - 32 + index * 4 ) );
		return new SaveEconomyRecords( loanRecords, challengeRecords, words );
	}

	private delegate bool RecordTest( ReadOnlySpan<byte> payload, int offset, int index );

	private static (int Offset, int Count) FindUnique( ReadOnlySpan<byte> payload, int size, int minimum, RecordTest test, string name )
	{
		(int Offset, int Count)? found = null;
		for ( var offset = 0; offset + size * minimum <= payload.Length; offset++ )
		{
			if ( !test( payload, offset, 0 ) )
				continue;
			var count = 1;
			while ( offset + (count + 1) * size <= payload.Length && test( payload, offset + count * size, count ) )
				count++;
			if ( count < minimum )
				continue;
			if ( found != null )
				throw new InvalidDataException( $"The save payload has more than one {name} table (offsets {found.Value.Offset} and {offset})." );
			found = (offset, count);
			offset += count * size - 1;
		}
		return found ?? throw new InvalidDataException( $"The save payload has no {name} table." );
	}

	private static int Int( ReadOnlySpan<byte> payload, int offset ) => BinaryPrimitives.ReadInt32LittleEndian( payload.Slice( offset, 4 ) );

	private static bool IsLoanRecord( ReadOnlySpan<byte> payload, int offset, int index )
	{
		if ( offset + LoanRecordSize > payload.Length || Int( payload, offset + 24 ) != index )
			return false;
		var amount = BinaryPrimitives.ReadInt64LittleEndian( payload.Slice( offset, 8 ) );
		var months = Int( payload, offset + 8 );
		var monthly = Int( payload, offset + 12 );
		// [APPROX:ECON-045] loan/challenge record locators use plausibility bounds (one fixture) — evidence needed: a second TPWS/TPWI fixture
		return amount is > 0 and <= 100_000_000 && months is > 0 and <= 600 && monthly > 0
			&& (long)monthly * months >= amount - months && (long)monthly * months <= amount * 4;
	}

	private static bool IsChallengeRecord( ReadOnlySpan<byte> payload, int offset, int index )
	{
		if ( offset + ChallengeRecordSize > payload.Length )
			return false;
		var type = Int( payload, offset );
		var time = Int( payload, offset + 4 );
		var value = Int( payload, offset + 8 );
		var target = Int( payload, offset + 12 );
		var target2 = Int( payload, offset + 16 );
		var prize = Int( payload, offset + 20 );
		var followup = Int( payload, offset + 24 );
		if ( type is < 1 or > 63 || time is < 1 or > 3650 || value is < 0 or > 1_000_000 || target is < 0 or > 99_999 || target2 is < 0 or > 99_999
			|| prize is < 1 or > 10_000_000 || followup is < 0 or > 63 || payload[offset + 42] > 1 || payload[offset + 43] != 0 || payload[offset + 44] != 0 )
			return false;
		for ( var position = 28; position < 42; position++ )
		{
			if ( payload[offset + position] != 0 )
				return false;
		}
		return true;
	}

	private static SaveLoanRecord ReadLoan( ReadOnlySpan<byte> payload, int offset ) => new( offset, Int( payload, offset + 24 ),
		BinaryPrimitives.ReadInt64LittleEndian( payload.Slice( offset, 8 ) ), Int( payload, offset + 8 ), Int( payload, offset + 12 ),
		Int( payload, offset + 16 ), Int( payload, offset + 20 ), Int( payload, offset + 28 ) );

	private static SaveChallengeRecord ReadChallenge( ReadOnlySpan<byte> payload, int offset ) => new( offset, Int( payload, offset ), Int( payload, offset + 4 ),
		Int( payload, offset + 8 ), Int( payload, offset + 12 ), Int( payload, offset + 16 ), Int( payload, offset + 20 ), Int( payload, offset + 24 ), payload[offset + 42] != 0 );
}
