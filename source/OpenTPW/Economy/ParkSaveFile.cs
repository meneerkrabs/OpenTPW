using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>
/// OpenTPW's own versioned park save (JSON, <c>"Format": "opentpw-park"</c>). Original TPWS/TPWI
/// payloads are only partly understood, so they are never written. The file stores the full
/// simulation state — clock, RNG, ledger and history, objects, loans, staff and pool, research,
/// challenges, golden tickets and counters — but not the original settings: those are reloaded
/// from the game data for the saved theme/difficulty, so a save does not freeze balance values.
/// Writes are atomic (temporary file + move); loads are size-capped, reject unknown or missing
/// members and validate invariants.
/// </summary>
public static class ParkSaveFile
{
	public const string FormatName = "opentpw-park";
	/// <summary>Version 2: the clock follows the original's 248 ms turns and civil calendar, so version 1 tick counts and month indices no longer match.</summary>
	public const int CurrentVersion = 2;
	public const int MaximumFileSize = 16 * 1024 * 1024;

	private static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		MaxDepth = 16,
		Converters = { new JsonStringEnumConverter( allowIntegerValues: false ) }
	};

	public sealed class Data
	{
		public required string Format { get; init; }
		public required int Version { get; init; }
		public required string Theme { get; init; }
		public required bool Easy { get; init; }
		public required ParkGameMode Mode { get; init; }
		/// <summary><see cref="ParkEconomy.SeedResearcherStandIn"/> (absent in older saves: off).</summary>
		public bool SeedResearcherStandIn { get; init; }
		public required long Tick { get; init; }
		public required GameSpeed Speed { get; init; }
		public required ulong RandomState { get; init; }
		public required bool ParkOpen { get; init; }
		public required int EntranceFee { get; init; }
		public required bool Bankrupt { get; init; }
		public required int MonthsInRed { get; init; }
		public required long LitterScaled { get; init; }
		public required int TicketsSpent { get; init; }
		/// <summary>Info ids bought with golden tickets (absent in older saves: none).</summary>
		public List<int> TicketItems { get; init; } = new();
		public required int NextObjectId { get; init; }
		public required long DroppedAdmissions { get; init; }
		public required LedgerData Ledger { get; init; }
		public required List<ObjectData> Objects { get; init; }
		public required List<LoanAccount> Loans { get; init; }
		public required List<int> TakenLoanOffers { get; init; }
		public required List<long> MonthlyAdmissions { get; init; }
		public required StaffData Staff { get; init; }
		public required ResearchData Research { get; init; }
		public required ObjectivesData Objectives { get; init; }
		public required Dictionary<string, long> Counters { get; init; }
	}

	public sealed class LedgerData
	{
		public required long Balance { get; init; }
		public required long OpeningBalance { get; init; }
		public required long MonthIndex { get; init; }
		public required Dictionary<LedgerCategory, long> CurrentTotals { get; init; }
		public required List<MonthData> History { get; init; }
		/// <summary>The year's running profit (absent in saves written before it was kept: 0).</summary>
		public long ProfitThisYear { get; init; }
	}

	public sealed class MonthData
	{
		public required long MonthIndex { get; init; }
		public required Dictionary<LedgerCategory, long> Totals { get; init; }
		public required long OpeningBalance { get; init; }
		public required long ClosingBalance { get; init; }
		public required int ParkRating { get; init; }
		public required long ParkValue { get; init; }
	}

	public sealed class ObjectData
	{
		public required int Id { get; init; }
		public required int InfoId { get; init; }
		public required ParkObjectKind Kind { get; init; }
		public required long BuiltTick { get; init; }
		public required bool Imported { get; init; }
		public required int Level { get; init; }
		public required int PendingLevel { get; init; }
		public required long TotalSpent { get; init; }
		public required int Price { get; init; }
		public required int CostOfGoods { get; init; }
		public required int ChanceOfLosingPercent { get; init; }
		public required bool IsOpen { get; init; }
		public required int StateOfRepair { get; init; }
		public required bool IsBrokenDown { get; init; }
		public required int MechanicId { get; init; }
		public required long[] Statistics { get; init; }
	}

	public sealed class StaffData
	{
		public required List<StaffMemberData> Members { get; init; }
		public required List<StaffCandidate> Candidates { get; init; }
		public required List<long> TrainingBudgets { get; init; }
		public required int NextId { get; init; }
		public required long NextPoolUpdateTick { get; init; }
	}

	public sealed class StaffMemberData
	{
		public required int Id { get; init; }
		public required StaffType Type { get; init; }
		public required int NameIndex { get; init; }
		public required int Grade { get; init; }
		public required int TrainingPoints { get; init; }
		public required long HiredTick { get; init; }
		public required int Happiness { get; init; }
		public required StaffState State { get; init; }
		public required long BusyTicks { get; init; }
		public required int AssignedInstanceId { get; init; }
	}

	public sealed class ResearchData
	{
		public required List<int[]> Completed { get; init; }
		public required List<long[]> Progress { get; init; }
		public required List<int> Effort { get; init; }
	}

	public sealed class ObjectivesData
	{
		public required long NextOfferDay { get; init; }
		public required int NextListPosition { get; init; }
		public required ActiveChallenge? Current { get; init; }
		public required Dictionary<int, int> Declines { get; init; }
		public required List<int> Finished { get; init; }
		public required List<GoldenTicketKind> GoldenTickets { get; init; }
	}

	public static Data Capture( ParkEconomy park )
	{
		ArgumentNullException.ThrowIfNull( park );
		return new Data
		{
			Format = FormatName,
			Version = CurrentVersion,
			Theme = park.Settings.Theme,
			Easy = park.Settings.IsEasy,
			Mode = park.Mode,
			SeedResearcherStandIn = park.SeedResearcherStandIn,
			Tick = park.Tick,
			Speed = park.Speed,
			RandomState = park.Random.State,
			ParkOpen = park.IsParkOpen,
			EntranceFee = park.EntranceFee,
			Bankrupt = park.IsBankrupt,
			MonthsInRed = park.MonthsInRed,
			LitterScaled = park.LitterScaled,
			TicketsSpent = park.TicketsSpent,
			TicketItems = park.TicketItems.Order().ToList(),
			NextObjectId = park.NextObjectId,
			DroppedAdmissions = park.DroppedAdmissions,
			Ledger = new LedgerData
			{
				Balance = park.Ledger.Balance,
				OpeningBalance = park.Ledger.CurrentOpeningBalance,
				MonthIndex = park.Ledger.CurrentMonthIndex,
				CurrentTotals = new Dictionary<LedgerCategory, long>( park.Ledger.CurrentTotals ),
				ProfitThisYear = park.Ledger.ProfitThisYear,
				History = park.Ledger.History.Select( month => new MonthData
				{
					MonthIndex = month.MonthIndex,
					Totals = new Dictionary<LedgerCategory, long>( month.Totals ),
					OpeningBalance = month.OpeningBalance,
					ClosingBalance = month.ClosingBalance,
					ParkRating = month.ParkRating,
					ParkValue = month.ParkValue
				} ).ToList()
			},
			Objects = park.Objects.Select( item => new ObjectData
			{
				Id = item.Id,
				InfoId = item.InfoId,
				Kind = item.Kind,
				BuiltTick = item.BuiltTick,
				Imported = item.Imported,
				Level = item.Level,
				PendingLevel = item.PendingLevel,
				TotalSpent = item.TotalSpent,
				Price = item.Price,
				CostOfGoods = item.CostOfGoods,
				ChanceOfLosingPercent = item.ChanceOfLosingPercent,
				IsOpen = item.IsOpen,
				StateOfRepair = item.StateOfRepair,
				IsBrokenDown = item.IsBrokenDown,
				MechanicId = item.MechanicId,
				Statistics = new[] { item.CustomersThisMonth, item.CustomersLastMonth, item.TakingsThisMonth, item.TakingsLastMonth, item.CostsThisMonth, item.CostsLastMonth, item.WinnersThisMonth, item.WinnersLastMonth, item.TotalProfit }
			} ).ToList(),
			Loans = park.Loans.ToList(),
			TakenLoanOffers = park.TakenOffers.OrderBy( index => index ).ToList(),
			MonthlyAdmissions = park.MonthlyAdmissions.ToList(),
			Staff = new StaffData
			{
				Members = park.Staff.Members.Select( member => new StaffMemberData
				{
					Id = member.Id,
					Type = member.Type,
					NameIndex = member.NameIndex,
					Grade = member.Grade,
					TrainingPoints = member.TrainingPoints,
					HiredTick = member.HiredTick,
					Happiness = member.Happiness,
					State = member.State,
					BusyTicks = member.BusyTicks,
					AssignedInstanceId = member.AssignedInstanceId
				} ).ToList(),
				Candidates = park.Staff.Candidates.ToList(),
				TrainingBudgets = Enum.GetValues<StaffType>().Select( park.Staff.GetTrainingBudget ).ToList(),
				NextId = park.Staff.NextId,
				NextPoolUpdateTick = park.Staff.NextPoolUpdateTick
			},
			Research = new ResearchData
			{
				Completed = park.Research.Completed.OrderBy( key => key.InfoId ).ThenBy( key => key.Level ).Select( key => new[] { key.InfoId, key.Level } ).ToList(),
				Progress = park.Research.ProgressEntries.OrderBy( entry => entry.Key.InfoId ).ThenBy( entry => entry.Key.Level ).Select( entry => new[] { entry.Key.InfoId, entry.Key.Level, entry.Points } ).ToList(),
				Effort = park.Research.Effort.ToList()
			},
			Objectives = new ObjectivesData
			{
				NextOfferDay = park.Objectives.NextOfferDay,
				NextListPosition = park.Objectives.NextListPosition,
				Current = park.Objectives.Current,
				Declines = new Dictionary<int, int>( park.Objectives.Declines ),
				Finished = park.Objectives.Finished.OrderBy( index => index ).ToList(),
				GoldenTickets = park.Objectives.GoldenTickets.OrderBy( kind => kind ).ToList()
			},
			Counters = new Dictionary<string, long>( park.Counters.Values )
		};
	}

	public static string Serialize( ParkEconomy park ) => JsonSerializer.Serialize( Capture( park ), Options );

	/// <summary>Parses and validates a save; the result still needs <see cref="Restore"/> with matching settings.</summary>
	public static Data Deserialize( ReadOnlySpan<byte> json )
	{
		if ( json.Length > MaximumFileSize )
			throw new InvalidDataException( "Park save exceeds the size limit." );
		Data? data;
		try
		{
			data = JsonSerializer.Deserialize<Data>( json, Options );
		}
		catch ( JsonException exception )
		{
			throw new InvalidDataException( $"Park save is malformed: {exception.Message}", exception );
		}
		if ( data == null || data.Format != FormatName )
			throw new InvalidDataException( "Not an OpenTPW park save." );
		if ( data.Version != CurrentVersion )
			throw new InvalidDataException( $"Unsupported park save version {data.Version}." );
		Validate( data );
		return data;
	}

	private static void Validate( Data data )
	{
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"Park save is inconsistent: {message}." );
		}
		Require( data.Tick >= 0 && Enum.IsDefined( data.Speed ) && Enum.IsDefined( data.Mode ), "clock" );
		Require( data.EntranceFee >= 0 && data.MonthsInRed >= 0 && data.LitterScaled >= 0 && data.TicketsSpent >= 0, "park values" );
		Require( data.Ledger.MonthIndex == ParkCalendar.MonthIndex( data.Tick ), "ledger month matches the clock" );
		Require( data.Ledger.History.Count <= ParkLedger.MaximumHistoryMonths && data.MonthlyAdmissions.Count <= ParkLedger.MaximumHistoryMonths, "history length" );
		Require( data.Ledger.CurrentTotals.Values.All( value => value >= 0 ) && data.Ledger.History.All( month => month.Totals.Values.All( value => value >= 0 ) ), "ledger totals" );
		var ids = new HashSet<int>();
		foreach ( var item in data.Objects )
		{
			Require( item.Id > 0 && item.Id < data.NextObjectId && ids.Add( item.Id ), $"object id {item.Id}" );
			Require( item.Level is >= 0 and <= 2 && item.PendingLevel is >= 0 and <= 2 && item.StateOfRepair is >= 0 and <= 100 && item.Statistics.Length == 9, $"object {item.Id}" );
			Require( item.BuiltTick <= data.Tick && item.Price >= 0 && item.CostOfGoods >= 0 && item.ChanceOfLosingPercent is >= 0 and <= 100, $"object {item.Id} values" );
		}
		var staffIds = new HashSet<int>();
		foreach ( var member in data.Staff.Members )
		{
			Require( member.Id > 0 && member.Id < data.Staff.NextId && staffIds.Add( member.Id ), $"staff id {member.Id}" );
			Require( Enum.IsDefined( member.Type ) && member.Grade is >= 0 and < BalanceSettings.GradeCount && member.TrainingPoints is >= 0 and < ParkStaff.TrainingPointsPerGrade, $"staff {member.Id}" );
			Require( member.BusyTicks >= 0 && (member.AssignedInstanceId == 0 || ids.Contains( member.AssignedInstanceId )), $"staff {member.Id} job" );
		}
		foreach ( var candidate in data.Staff.Candidates )
			Require( candidate.Id > 0 && candidate.Id < data.Staff.NextId && staffIds.Add( candidate.Id ) && candidate.Grade is >= 0 and < BalanceSettings.GradeCount, $"candidate {candidate.Id}" );
		Require( data.Staff.TrainingBudgets.Count == BalanceSettings.StaffTypeCount && data.Staff.TrainingBudgets.All( value => value >= 0 ), "training budgets" );
		Require( data.Research.Effort.Count == 5 && data.Research.Completed.All( key => key.Length == 2 ) && data.Research.Progress.All( entry => entry.Length == 3 && entry[2] >= 0 ), "research" );
		foreach ( var loan in data.Loans )
			Require( loan.MonthsRemaining > 0 && loan.RemainingBalance >= 0 && data.TakenLoanOffers.Contains( loan.OfferIndex ), $"loan {loan.OfferIndex}" );
	}

	/// <summary>Rebuilds a park from saved data and the original settings/catalogue of the saved theme.</summary>
	public static ParkEconomy Restore( Data data, BalanceSettings settings, IEconomyObjectCatalog catalog )
	{
		ArgumentNullException.ThrowIfNull( data );
		if ( !string.Equals( settings.Theme, data.Theme, StringComparison.OrdinalIgnoreCase ) || settings.IsEasy != data.Easy )
			throw new InvalidDataException( $"Park save is for {data.Theme}{(data.Easy ? " (easy)" : "")}, not {settings.Theme}{(settings.IsEasy ? " (easy)" : "")}." );
		var park = new ParkEconomy( settings, catalog, data.Mode, 0 ) { SeedResearcherStandIn = data.SeedResearcherStandIn };
		park.Random.Restore( data.RandomState );
		park.Ledger.Restore( data.Ledger.Balance, data.Ledger.OpeningBalance, data.Ledger.MonthIndex, data.Ledger.CurrentTotals,
			data.Ledger.History.Select( month => new LedgerMonth( month.MonthIndex, month.Totals, month.OpeningBalance, month.ClosingBalance, month.ParkRating, month.ParkValue ) ) );
		park.Ledger.RestoreProfitThisYear( data.Ledger.ProfitThisYear );
		park.RestoreTicketItems( data.TicketItems );
		park.RestoreState( data.Tick, data.Speed, data.ParkOpen, data.EntranceFee, data.Bankrupt, data.MonthsInRed, data.LitterScaled, data.TicketsSpent, data.NextObjectId, data.DroppedAdmissions,
			data.Objects.Select( item => new ParkObjectState
			{
				Id = item.Id,
				InfoId = item.InfoId,
				Kind = item.Kind,
				BuiltTick = item.BuiltTick,
				Imported = item.Imported,
				Level = item.Level,
				PendingLevel = item.PendingLevel,
				TotalSpent = item.TotalSpent,
				Price = item.Price,
				CostOfGoods = item.CostOfGoods,
				ChanceOfLosingPercent = item.ChanceOfLosingPercent,
				IsOpen = item.IsOpen,
				StateOfRepair = item.StateOfRepair,
				IsBrokenDown = item.IsBrokenDown,
				MechanicId = item.MechanicId,
				CustomersThisMonth = item.Statistics[0],
				CustomersLastMonth = item.Statistics[1],
				TakingsThisMonth = item.Statistics[2],
				TakingsLastMonth = item.Statistics[3],
				CostsThisMonth = item.Statistics[4],
				CostsLastMonth = item.Statistics[5],
				WinnersThisMonth = item.Statistics[6],
				WinnersLastMonth = item.Statistics[7],
				TotalProfit = item.Statistics[8]
			} ),
			data.Loans, data.TakenLoanOffers, data.MonthlyAdmissions );
		park.Staff.Restore( data.Staff.Members.Select( member => new StaffMember
		{
			Id = member.Id,
			Type = member.Type,
			NameIndex = member.NameIndex,
			Grade = member.Grade,
			TrainingPoints = member.TrainingPoints,
			HiredTick = member.HiredTick,
			Happiness = member.Happiness,
			State = member.State,
			BusyTicks = member.BusyTicks,
			AssignedInstanceId = member.AssignedInstanceId
		} ), data.Staff.Candidates, data.Staff.TrainingBudgets, data.Staff.NextId, data.Staff.NextPoolUpdateTick );
		park.Research.Restore( data.Research.Completed.Select( key => (key[0], key[1]) ),
			data.Research.Progress.Select( entry => (((int)entry[0], (int)entry[1]), entry[2]) ), data.Research.Effort );
		park.Objectives.Restore( data.Objectives.NextOfferDay, data.Objectives.NextListPosition, data.Objectives.Current, data.Objectives.Declines, data.Objectives.Finished, data.Objectives.GoldenTickets );
		park.Counters.Restore( data.Counters );
		return park;
	}

	public static ParkEconomy Load( string path, Func<string, bool, (BalanceSettings Settings, IEconomyObjectCatalog Catalog)> loadTheme )
	{
		var data = Deserialize( ReadCapped( path ) );
		var (settings, catalog) = loadTheme( data.Theme, data.Easy );
		return Restore( data, settings, catalog );
	}

	/// <summary>Loads a save using the original data of its theme from the game file system.</summary>
	public static ParkEconomy Load( string path ) => Load( path, ( theme, easy ) => (BalanceSettings.Load( theme, easy ), EconomyObjectCatalog.Load( theme, easy )) );

	/// <summary>Writes atomically: a complete temporary file is moved over the destination.</summary>
	public static void Save( string path, ParkEconomy park )
	{
		var destination = GetPath( path );
		var bytes = System.Text.Encoding.UTF8.GetBytes( Serialize( park ) );
		var temporary = destination + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
		var ownsTemporary = false;
		try
		{
			using ( var stream = new FileStream( temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None ) )
			{
				ownsTemporary = true;
				stream.Write( bytes );
				stream.Flush( true );
			}
			File.Move( temporary, destination, true );
		}
		finally
		{
			if ( ownsTemporary )
				File.Delete( temporary );
		}
	}

	private static byte[] ReadCapped( string path )
	{
		using var stream = new FileStream( GetPath( path ), FileMode.Open, FileAccess.Read, FileShare.Read );
		if ( stream.Length > MaximumFileSize )
			throw new InvalidDataException( "Park save exceeds the size limit." );
		var bytes = new byte[stream.Length];
		stream.ReadExactly( bytes );
		return bytes;
	}

	private static string GetPath( string path )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		var extension = Path.GetExtension( path );
		if ( extension.Equals( ".tpws", StringComparison.OrdinalIgnoreCase ) || extension.Equals( ".tpwi", StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidDataException( "Original TPWS/TPWI files are never written or read as OpenTPW park saves." );
		return Path.GetFullPath( path );
	}
}
