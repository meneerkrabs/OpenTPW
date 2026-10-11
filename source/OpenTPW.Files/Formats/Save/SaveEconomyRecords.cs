namespace OpenTPW;

/// <summary>
/// Economy things of a save's World thing list (docs/ECONOMY.md, docs/reverse/SAVE-plan.md): the bank account
/// (model 16, named by the World variable <c>mBankAccount</c>) with its eight loan offers, and the used challenge
/// slots of the challenge system (model 19).
/// </summary>
public sealed class SaveEconomyRecords
{
	private SaveEconomyRecords( SaveBank bank, SaveChallengeSystem challenges )
	{
		BankAccount = bank;
		ChallengeSystem = challenges;
	}

	public SaveBank BankAccount { get; }
	public SaveChallengeSystem ChallengeSystem { get; }
	public SaveBankRecord Bank => BankAccount.Account;
	public IReadOnlyList<SaveLoanRecord> Loans => BankAccount.Loans;
	public IReadOnlyList<SaveChallengeRecord> Challenges => ChallengeSystem.Challenges;

	/// <summary>Reads the bank named by <c>mBankAccount</c> and the one challenge system; throws when either is missing.</summary>
	public static SaveEconomyRecords Parse( ReadOnlySpan<byte> payload, SaveWorld world )
	{
		ArgumentNullException.ThrowIfNull( world );
		var bankId = world.Variables.BankAccount;
		if ( bankId == 0 || world.Find( bankId ) is not { } bank )
			throw new InvalidDataException( "The save World names no bank account." );
		return new SaveEconomyRecords( SaveBank.Read( payload, bank ), SaveChallengeSystem.Read( payload, world.Single( SaveThingModel.ChallengeSystem ) ) );
	}
}
