namespace OpenTPW;

/// <summary>
/// Imports the proven economy fields of an original level save into a <see cref="ParkEconomy"/>:
/// placed objects and fixed items (registered without charge, docs/TPWS-PAYLOAD.md), and verifies
/// the save's loan-offer and challenge tables against the balance settings. Both tables must match
/// the settings exactly — that is what proves their layout and that the save was made with the
/// easy-mode balance file — and they then confirm, rather than replace, the setting-driven state.
/// The candidate balance words before the loan table are reported but not imported.
/// </summary>
public sealed class OriginalEconomyImport
{
	private OriginalEconomyImport( SaveEconomyRecords records, int objects, int fixedItems, IReadOnlyList<string> evidence )
	{
		Records = records;
		ImportedObjects = objects;
		ImportedFixedItems = fixedItems;
		Evidence = evidence;
	}

	public SaveEconomyRecords Records { get; }
	public int ImportedObjects { get; }
	public int ImportedFixedItems { get; }
	public IReadOnlyList<string> Evidence { get; }

	/// <summary>Reads the original save of <paramref name="park"/>'s level from the game file system.</summary>
	public static OriginalEconomyImport Apply( ParkEconomy economy, OriginalPark park )
	{
		ArgumentNullException.ThrowIfNull( park );
		if ( park.Save == null )
			throw new InvalidOperationException( $"The {park.LevelName} level has no original save." );
		var savePath = FileSystem.GetFiles( $"/levels/{park.LevelName}" )
			.First( path => string.Equals( Path.GetFileName( path ), OriginalPark.SaveFileName, StringComparison.OrdinalIgnoreCase ) );
		using var stream = FileSystem.OpenRead( $"/levels/{park.LevelName}/{Path.GetFileName( savePath )}" );
		using var reader = new SaveReader( stream );
		var records = SaveEconomyRecords.Parse( reader.ReadFile() );
		return Apply( economy, records, park.Save.PlacedObjects.Select( item => item.Record.InfoId ), park.Save.FixedItems.Select( item => item.InfoId ) );
	}

	/// <summary>Cross-checks <paramref name="records"/> with the settings and registers the save's objects.</summary>
	public static OriginalEconomyImport Apply( ParkEconomy economy, SaveEconomyRecords records, IEnumerable<int> placedInfoIds, IEnumerable<int> fixedInfoIds )
	{
		ArgumentNullException.ThrowIfNull( economy );
		ArgumentNullException.ThrowIfNull( records );
		var settings = economy.Settings;
		var evidence = new List<string>();
		if ( records.Loans.Count != settings.Loans.Count )
			throw new InvalidDataException( $"The save has {records.Loans.Count} loan offers; the settings define {settings.Loans.Count}." );
		for ( var index = 0; index < records.Loans.Count; index++ )
		{
			var saved = records.Loans[index];
			var offer = settings.Loans[index];
			var expected = LoanMath.MonthlyRepayment( offer.Amount, offer.AprPercent, offer.Months );
			if ( saved.Amount != offer.Amount || saved.Months != offer.Months || saved.MonthlyRepayment != expected )
				throw new InvalidDataException( $"Save loan offer {index} ({saved.Amount}/{saved.Months} months/{saved.MonthlyRepayment} monthly) differs from LoanInfo[{offer.Index}] ({offer.Amount}/{offer.Months}/{expected})." );
		}
		evidence.Add( $"{records.Loans.Count} loan offers match LoanInfo amounts, terms and floor(amount/months) repayments ({(settings.Loans.All( offer => offer.AprPercent == 0 ) ? "0 % APR" : "settings APR")})." );
		var level = settings.ChallengesInThisLevel;
		if ( records.Challenges.Count != level.Count )
			throw new InvalidDataException( $"The save has {records.Challenges.Count} challenges; ChallengesInThisLevel lists {level.Count}." );
		for ( var index = 0; index < level.Count; index++ )
		{
			var saved = records.Challenges[index];
			if ( !settings.Challenges.TryGetValue( level[index], out var definition ) )
				throw new InvalidDataException( $"ChallengesInThisLevel[{index}] = {level[index]} is not defined." );
			if ( saved.Type != definition.Type || saved.TargetTime != definition.TargetTime || saved.TargetValue != definition.TargetValue || saved.TargetObject != definition.TargetObject
				|| saved.TargetObject2 != definition.TargetObject2 || saved.Prize != definition.Prize || saved.FollowupType != definition.FollowupType || saved.Independent != definition.Independent )
				throw new InvalidDataException( $"Save challenge {index} (type {saved.Type}, prize {saved.Prize}) differs from Challenges[{definition.Index}]." );
		}
		evidence.Add( $"{level.Count} challenge records equal Challenges[{string.Join( ", ", level )}] in ChallengesInThisLevel order." );
		if ( records.WordsBeforeLoans.Count == 8 )
			evidence.Add( $"Unreconciled words before the loan table: {string.Join( ", ", records.WordsBeforeLoans )} (not imported; balance stays BankAccountInfo.InitialCash = {settings.InitialCash})." );
		var objects = 0;
		foreach ( var infoId in placedInfoIds )
		{
			economy.RegisterExisting( infoId );
			objects++;
		}
		var fixedItems = 0;
		foreach ( var infoId in fixedInfoIds )
		{
			economy.RegisterExisting( infoId );
			fixedItems++;
		}
		evidence.Add( $"Registered {objects} placed objects and {fixedItems} fixed items without charge." );
		return new OriginalEconomyImport( records, objects, fixedItems, evidence );
	}
}
