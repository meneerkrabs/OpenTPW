using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>
/// OpenTPW's own versioned park save (JSON, <c>"Format": "opentpw-park"</c>). Original TPWS/TPWI
/// payloads are only partly understood, so they are never written. The file stores the full
/// simulation state — clock, RNG, ledger and history, objects, loans, staff and pool, research,
/// challenges, golden tickets and counters, plus the world seed and the guest, script and sound random
/// streams — but not the original settings: those are reloaded
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
		/// <summary>
		/// Legacy: earlier version-2 saves recorded whether a stand-in researched for the Instant Action seed's then
		/// undecoded researcher. The seed's staff are imported now; the flag is accepted, ignored and written false.
		/// </summary>
		public bool SeedResearcherStandIn { get; init; }
		public required long Tick { get; init; }
		public required GameSpeed Speed { get; init; }
		public required ulong RandomState { get; init; }
		public required bool ParkOpen { get; init; }
		public required int EntranceFee { get; init; }
		public required bool Bankrupt { get; init; }
		public required int MonthsInRed { get; init; }
		/// <summary>The bank's withdrawals flag (absent in older saves: set).</summary>
		public bool? WithdrawalsEnabled { get; init; }
		/// <summary>The balance after the last debit (absent in older saves: the saved balance).</summary>
		public long? LastBalance { get; init; }
		/// <summary>The park turn the balance went below 0 (absent in older saves: counted back from <see cref="MonthsInRed"/>).</summary>
		public long? TurnEnteredRed { get; init; }
		/// <summary>
		/// Legacy: saves before per-cell litter kept a park-wide count of litter items in hundredths. It has no cells to land on, so it is
		/// accepted, ignored and written 0 (kept in the record: the determinism hashes cover the save's text); the litter is <see cref="Litter"/>.
		/// </summary>
		public long LitterScaled { get; init; }
		/// <summary>Every littered cell with its type and claim (absent in older saves: a clean park).</summary>
		public List<LitterData> Litter { get; init; } = new();
		public required int TicketsSpent { get; init; }
		/// <summary>Info ids bought with golden tickets (absent in older saves: none).</summary>
		public List<int> TicketItems { get; init; } = new();
		public required int NextObjectId { get; init; }
		public required long DroppedAdmissions { get; init; }
		public required LedgerData Ledger { get; init; }
		public required List<ObjectData> Objects { get; init; }
		public required List<LoanData> Loans { get; init; }
		public required List<int> TakenLoanOffers { get; init; }
		public required List<long> MonthlyAdmissions { get; init; }
		public required StaffData Staff { get; init; }
		public required ResearchData Research { get; init; }
		public required ObjectivesData Objectives { get; init; }
		public required Dictionary<string, long> Counters { get; init; }
		/// <summary>The mechanic HQ's cursor into the object list (<see cref="ParkEconomy.MechanicCursor"/>); absent in older saves and while 0.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public int MechanicCursor { get; init; }
		/// <summary>The park's perception statistics, the categories that hold a value; absent in older saves and while none does.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public List<PriceStatisticData>? PriceStatistics { get; init; }
		/// <summary>World seed and the guest, script and sound streams (absent in saves written before world seeds: those streams are not restored).</summary>
		// [EXT:legacy-economy-save] this legacy economy-only park save (Level.SavePark/LoadPark, used by DeterminismTests) persists the economy and every random stream, not guests, queues or object script state, and a load keeps the running guests and scripts; the player's save (GameFlow.SaveCurrentPark -> ParkResumeSaveFile) already stores the guests with their queue bookkeeping, the script world and each object runtime, the advisor, the coasters and the tracks
		public WorldData? World { get; init; }
	}

	/// <summary><see cref="WorldRandomState"/> as saved; the economy's own stream stays in <see cref="Data.RandomState"/>.</summary>
	public sealed class WorldData
	{
		public required ulong Seed { get; init; }
		public required ulong GuestRandom { get; init; }
		public required ulong ScriptRandom { get; init; }
		public required uint SoundSeed { get; init; }
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
		/// <summary>The pending upgrade is not paid yet (debited when it is installed); absent in saves that charged it at purchase.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public bool UpgradeCostDue { get; init; }
		public required long TotalSpent { get; init; }
		public required int Price { get; init; }
		public required int CostOfGoods { get; init; }
		public required int ChanceOfLosingPercent { get; init; }
		public required bool IsOpen { get; init; }
		public required int StateOfRepair { get; init; }
		/// <summary>Exact state of repair; absent in older saves, which then use <see cref="StateOfRepair"/>.</summary>
		public double? Repair { get; init; }
		/// <summary>Ride life gauge; absent in older saves, which then start at 100.</summary>
		public double? LifeGauge { get; init; }
		public required bool IsBrokenDown { get; init; }
		public required int MechanicId { get; init; }
		/// <summary>The turn the claim was taken; absent in older saves and while 0 (an older claim then lapses on the first look, as one taken at turn 0).</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public long ClaimTurn { get; init; }
		/// <summary>The player's call for a mechanic (<c>mRequestedService</c>); absent: none.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public bool MaintenanceRequested { get; init; }
		/// <summary>A fireworks object whose show has run (its <c>RegionFX</c> row is gone); absent: not yet.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public bool FireworksSpent { get; init; }
		/// <summary>Guests that have used the object; absent in older saves, which then use this and last month's customers.</summary>
		public long? TimesUsed { get; init; }
		public required long[] Statistics { get; init; }
		/// <summary>Guests that walked away from the price (mNumWalkAways); absent in older saves and when 0.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public long WalkAways { get; init; }
		/// <summary>Today's walk-aways; absent in older saves and when 0.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public long WalkAwaysToday { get; init; }
		/// <summary>
		/// The walk-aways of the last days, oldest first; absent in older saves and while every day held none (only sums read the ring, the advisor's
		/// per-ShopType test 0x004CAAE0, so days without a walk-away are dropped rather than saved).
		/// </summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public long[]? WalkAwayDays { get; init; }
	}

	/// <summary>One category of the park's perception statistics (<see cref="ParkPriceStatistics"/>).</summary>
	public sealed class PriceStatisticData
	{
		public required int Category { get; init; }
		public required int Head { get; init; }
		public required bool Full { get; init; }
		public required byte[] Values { get; init; }
	}

	/// <summary>
	/// A bought loan as saved. A loan is its instalment and the months left; the amount still owed is derived as
	/// M × the months left (the original keeps no declining balance, ORACLE F13). <see cref="RemainingBalance"/> is that
	/// derived amount, written for readers and otherwise ignored: saves written while loans accrued interest on a balance
	/// (also version 2) hold the old balance there, which is dropped because their months left already say how far the
	/// loan is repaid; their stored instalment stays the loan's instalment.
	/// </summary>
	public sealed class LoanData
	{
		public required int OfferIndex { get; init; }
		public required int LenderNameIndex { get; init; }
		public required long OriginalAmount { get; init; }
		public required int AprPercent { get; init; }
		public required int Months { get; init; }
		public required long MonthlyRepayment { get; init; }
		public required int MonthsRemaining { get; init; }
		public long? RemainingBalance { get; init; }

		public LoanAccount ToAccount() => new( OfferIndex, LenderNameIndex, OriginalAmount, AprPercent, Months, MonthlyRepayment, Months - MonthsRemaining );
	}

	public sealed class StaffData
	{
		public required List<StaffMemberData> Members { get; init; }
		public required List<StaffCandidate> Candidates { get; init; }
		public required List<long> TrainingBudgets { get; init; }
		public required int NextId { get; init; }
		public required long NextPoolUpdateTick { get; init; }
		/// <summary>The strike levels (ECON-014); absent in saves written before strikes and while no role has ever been considered.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public StrikeData? Strikes { get; init; }
	}

	/// <summary>The staff manager's five per-role records: strike level 0–4, the active flag and the park turn the role was last considered at.</summary>
	public sealed class StrikeData
	{
		public required List<int> Levels { get; init; }
		public required List<bool> Active { get; init; }
		public required List<uint> LastConsidered { get; init; }
	}

	public sealed class StaffMemberData
	{
		public required int Id { get; init; }
		public required StaffType Type { get; init; }
		public required int NameIndex { get; init; }
		public required int Grade { get; init; }
		public required int TrainingPoints { get; init; }
		public required long HiredTick { get; init; }
		public required float Happiness { get; init; }
		/// <summary>Energy, 0–100; saves written before staff vitals have none and read as 100.</summary>
		public float Energy { get; init; } = StaffVitals.Maximum;
		public required StaffState State { get; init; }
		/// <summary>Park turns until the job completes (ORACLE.md F19). Written by this version.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public long? BusyTurns { get; init; }
		/// <summary>The job time of earlier saves, in tenths of a park hour; read only when <see cref="BusyTurns"/> is absent and converted by <see cref="BusyTurnsOf"/>. Never written.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public long? BusyTicks { get; init; }
		/// <summary>New host mechanic work has not started; absent/false preserves legacy already-started jobs.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public bool AwaitingJobArrival { get; init; }
		public required int AssignedInstanceId { get; init; }
		/// <summary>The littered cell a handyman sweeps (absent: none).</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public int[]? LitterCell { get; init; }
		/// <summary>A handyman's, entertainer's or guard's state machine in a world (absent: none yet).</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public StaffWorldData? WorldState { get; init; }
		/// <summary>Legacy: saves written before the entertainers and guards walked named the handyman's record so; it is read into <see cref="WorldState"/>.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public StaffWorldData? Handyman { get => null; init => WorldState ??= value; }
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public float? CellX { get; init; }
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public float? CellY { get; init; }
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public double TravelSeconds { get; init; }
	}

	/// <summary>
	/// A world-driven member's state machine (docs/reverse/WANDER-plan.md §4): its state, stamps, toilet, patrol rectangle, speed and walk, a guard's
	/// target and the cell where an entertainer's or a guard's <c>RegionFX</c> row lies.
	/// </summary>
	public sealed class StaffWorldData
	{
		public required StaffJob Job { get; init; }
		public long IdleStamp { get; init; }
		public long JobStamp { get; init; }
		public int ToiletId { get; init; }
		/// <summary>The patrol rectangle: x0, y0, x1, y1.</summary>
		public required int[] PatrolArea { get; init; }
		public required int BaseSpeed { get; init; }
		public int SpeedBonus { get; init; }
		public float SmoothedSpeed { get; init; }
		/// <summary>The steering state, null before a world placed him.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public NavigationData? Navigation { get; init; }
		/// <summary>The guest a guard chases or escorts (absent: none).</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public int TargetGuestId { get; init; }
		/// <summary>The cell of the member's <c>RegionFX</c> row (absent: none).</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public int[]? EffectCell { get; init; }
		/// <summary>The ride a mechanic walks to or works on (absent: none).</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public int RideId { get; init; }
		/// <summary>The rest area the member walks to or rests in (absent: none).</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public int RestAreaId { get; init; }
		/// <summary>The points still to walk off the paths, as x, y, byte x, byte y quads (absent: none).</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public int[]? OffGridLeg { get; init; }
		/// <summary>The member's thought, its bubble and the bubble's turn (absent: none).</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public int Thought { get; init; }
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public int BubbleThought { get; init; }
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public long BubbleTurn { get; init; }

		internal static StaffWorldData Capture( StaffMember member ) => new()
		{
			Job = member.Job,
			IdleStamp = member.IdleStamp,
			JobStamp = member.JobStamp,
			ToiletId = member.ToiletId,
			PatrolArea = new[] { member.PatrolArea.X0, member.PatrolArea.Y0, member.PatrolArea.X1, member.PatrolArea.Y1 },
			BaseSpeed = member.BaseSpeed,
			SpeedBonus = member.SpeedBonus,
			SmoothedSpeed = member.SmoothedSpeed,
			Navigation = member.Navigation is { } navigation ? NavigationData.Capture( navigation ) : null,
			TargetGuestId = member.TargetGuestId,
			EffectCell = member.EffectCell is { } cell ? new[] { cell.X, cell.Y } : null,
			RestAreaId = member.RestAreaId,
			RideId = member.RideId,
			OffGridLeg = member.OffGridLeg.Count == 0 ? null : member.OffGridLeg.SelectMany( point => new[] { point.X, point.Y, point.ByteX, point.ByteY } ).ToArray(),
			Thought = member.Thought,
			BubbleThought = member.BubbleThought,
			BubbleTurn = member.BubbleTurn
		};

		internal void Apply( StaffMember member )
		{
			member.Job = Job;
			member.IdleStamp = IdleStamp;
			member.JobStamp = JobStamp;
			member.ToiletId = ToiletId;
			member.PatrolArea = (PatrolArea[0], PatrolArea[1], PatrolArea[2], PatrolArea[3]);
			member.BaseSpeed = BaseSpeed;
			member.SpeedBonus = SpeedBonus;
			member.SmoothedSpeed = SmoothedSpeed;
			member.Navigation = Navigation?.Restore();
			member.TargetGuestId = TargetGuestId;
			member.EffectCell = EffectCell is [var x, var y] ? (x, y) : null;
			member.RestAreaId = RestAreaId;
			member.RideId = RideId;
			member.OffGridLeg = OffGridLeg is { } leg ? leg.Chunk( 4 ).Select( point => (point[0], point[1], point[2], point[3]) ).ToList() : new();
			member.Thought = Thought is 0 or (>= 18 and <= 22) ? Thought : throw new InvalidDataException( $"Staff thought {Thought} is not a staff thought." );
			member.BubbleThought = BubbleThought is 0 or (>= 18 and <= 22) ? BubbleThought : throw new InvalidDataException( $"Staff bubble {BubbleThought} is not a staff thought." );
			member.BubbleTurn = BubbleTurn >= 0 ? BubbleTurn : throw new InvalidDataException( "A staff bubble's turn is negative." );
		}

		/// <summary>The states a role's state machine takes: the shared 0–7 and the role's own.</summary>
		internal static bool Fits( StaffType type, StaffJob job ) => Enum.IsDefined( job ) && (job <= StaffJob.PickedUp || type switch
		{
			StaffType.Handyman => job is >= StaffJob.ToLitter and <= StaffJob.Cleaning,
			StaffType.Entertainer => job == StaffJob.Performing,
			StaffType.Guard => job is StaffJob.Chasing or StaffJob.Escorting or StaffJob.ReturningFromEscort,
			StaffType.Researcher => job == StaffJob.Researching,
			StaffType.Mechanic => job is StaffJob.ToRide or StaffJob.Repairing,
			_ => false
		});
	}

	/// <summary>A <see cref="GuestNavigation"/> as saved: every field the next step reads; cell lists as x, y pairs.</summary>
	public sealed class NavigationData
	{
		public required int[] Position { get; init; }
		public required int[] Velocity { get; init; }
		public required int[] Target { get; init; }
		public required bool Arrived { get; init; }
		public required int Index { get; init; }
		public required int Count { get; init; }
		public required int[] Route { get; init; }
		public required int Cursor { get; init; }
		public required int WalkCrossings { get; init; }
		public required int[] AlsoOpen { get; init; }
		public required int History { get; init; }
		public required int PreviousRemaining { get; init; }
		public required bool OffGrid { get; init; }
		public required int Reroutes { get; init; }
		/// <summary>The fields of a walk of the original kind (the tp.exe search and steering step); null for a route walk and in older saves, which restore as route walks.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public OriginalWalkSnapshot? OriginalWalk { get; init; }
		/// <summary>All five waypoint slots of an original walk as x, y pairs (a failed set-up keeps the old ones); null for a route walk.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public int[]? Waypoints { get; init; }
		/// <summary>The walk mask (staff +0x188); 0, the default, is not written.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public int Mask { get; init; }

		private static int[] Pairs( IEnumerable<(int X, int Y)> cells ) => cells.SelectMany( cell => new[] { cell.X, cell.Y } ).ToArray();

		private static (int X, int Y)[] Cells( int[] pairs ) => Enumerable.Range( 0, pairs.Length / 2 ).Select( index => (pairs[2 * index], pairs[2 * index + 1]) ).ToArray();

		internal static NavigationData Capture( GuestNavigation navigation ) => new()
		{
			Position = new[] { navigation.X, navigation.Y },
			Velocity = new[] { navigation.VelocityX, navigation.VelocityY },
			Target = new[] { navigation.TargetX, navigation.TargetY },
			Arrived = navigation.Arrived,
			Index = navigation.Index,
			Count = navigation.Count,
			Route = Pairs( navigation.Route ),
			Cursor = navigation.Cursor,
			WalkCrossings = navigation.WalkCrossings,
			AlsoOpen = Pairs( navigation.AlsoOpen ),
			History = navigation.History,
			PreviousRemaining = navigation.PreviousRemaining,
			OffGrid = navigation.OffGrid,
			Reroutes = navigation.Reroutes,
			OriginalWalk = navigation.CaptureOriginal(),
			Waypoints = navigation.Original ? Pairs( navigation.Buffer ) : null,
			Mask = navigation.Mask
		};

		internal bool IsValid => Position is { Length: 2 } && Velocity is { Length: 2 } && Target is { Length: 2 } && Route is { } route && route.Length % 2 == 0
			&& AlsoOpen is { } open && open.Length % 2 == 0 && Count is >= 0 and <= GuestNavigation.BufferedWaypoints && Cursor >= 0
			&& Index >= 0 && (Index < Count || Count == 0) && Mask is >= 0 and <= 2
			&& (OriginalWalk is { } walk
				? Waypoints is { Length: 2 * GuestNavigation.BufferedWaypoints } waypoints && waypoints.All( value => value is >= 0 and < ParkLitter.MapSize )
					&& walk.PathCount is >= 0 and <= 0xFC && Count == Math.Min( walk.PathCount, GuestNavigation.BufferedWaypoints ) && !OffGrid
					&& walk.Stamp >= 0 && walk.Total >= 0 && walk.Ahead >= 0 && walk.Beyond >= 0 && walk.Progress <= Fixed16.One && Cursor <= route.Length / 2
				: Waypoints is null && Cursor <= route.Length / 2 - Count);

		internal GuestNavigation Restore()
		{
			var navigation = new GuestNavigation
			{
				X = Position[0], Y = Position[1], VelocityX = Velocity[0], VelocityY = Velocity[1], TargetX = Target[0], TargetY = Target[1],
				Arrived = Arrived, Index = Index, Count = Count, Route = Cells( Route ), Cursor = Cursor, WalkCrossings = WalkCrossings,
				AlsoOpen = Cells( AlsoOpen ), History = History, PreviousRemaining = PreviousRemaining, OffGrid = OffGrid, Reroutes = Reroutes
			};
			navigation.RestoreOriginal( OriginalWalk );
			navigation.Mask = Mask;
			if ( Waypoints is { } waypoints )
				Cells( waypoints ).AsSpan().CopyTo( navigation.Buffer );
			else
				for ( var index = 0; index < Count; index++ )
					navigation.Buffer[index] = navigation.Route[Cursor + index];
			return navigation;
		}
	}

	public sealed class LitterData
	{
		public required int X { get; init; }
		public required int Y { get; init; }
		/// <summary>The cell's litter (1..5 ordinary, 6 the sprite fallback, 7 vomit, 8 stinkbomb).</summary>
		public required int Type { get; init; }
		/// <summary>The handyman who claimed it (0: nobody).</summary>
		public int Collector { get; init; }
		public long ClaimTurn { get; init; }
	}

	public sealed class ResearchData
	{
		public required List<int[]> Completed { get; init; }
		public required List<long[]> Progress { get; init; }
		public required List<int> Effort { get; init; }
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public List<int[]>? Current { get; init; }
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public int? Workload { get; init; }
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

	/// <param name="world">The level's other random streams (<see cref="Level.CaptureRandomState"/>); null saves the economy alone.</param>
	public static Data Capture( ParkEconomy park, WorldRandomState? world = null )
	{
		ArgumentNullException.ThrowIfNull( park );
		foreach ( var member in park.Staff.Members.Where( member => member.AwaitingJobArrival ) )
			if ( !park.OwnsMechanicJob( member ) ) throw new InvalidDataException( $"Staff {member.Id} cannot capture its awaiting mechanic job." );
		return new Data
		{
			Format = FormatName,
			Version = CurrentVersion,
			Theme = park.Settings.Theme,
			Easy = park.Settings.IsEasy,
			Mode = park.Mode,
			SeedResearcherStandIn = false,
			Tick = park.Tick,
			Speed = park.Speed,
			RandomState = park.Random.State,
			ParkOpen = park.IsParkOpen,
			EntranceFee = park.EntranceFee,
			Bankrupt = park.IsBankrupt,
			MonthsInRed = park.MonthsInRed,
			WithdrawalsEnabled = park.WithdrawalsEnabled,
			LastBalance = park.LastBalance,
			TurnEnteredRed = park.TurnEnteredRed,
			Litter = park.Litter.Snapshot().Select( cell => new LitterData { X = cell.X, Y = cell.Y, Type = (int)cell.Type, Collector = cell.Collector, ClaimTurn = cell.ClaimTurn } ).ToList(),
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
				UpgradeCostDue = item.UpgradeCostDue,
				TotalSpent = item.TotalSpent,
				Price = item.Price,
				CostOfGoods = item.CostOfGoods,
				ChanceOfLosingPercent = item.ChanceOfLosingPercent,
				IsOpen = item.IsOpen,
				StateOfRepair = item.StateOfRepair,
				Repair = item.Repair,
				LifeGauge = item.LifeGauge,
				IsBrokenDown = item.IsBrokenDown,
				MechanicId = item.MechanicId,
				ClaimTurn = item.ClaimTurn,
				MaintenanceRequested = item.MaintenanceRequested,
				FireworksSpent = item.FireworksSpent,
				TimesUsed = item.TimesUsed,
				Statistics = new[] { item.CustomersThisMonth, item.CustomersLastMonth, item.TakingsThisMonth, item.TakingsLastMonth, item.CostsThisMonth, item.CostsLastMonth, item.WinnersThisMonth, item.WinnersLastMonth, item.TotalProfit },
				WalkAways = item.WalkAways,
				WalkAwaysToday = item.WalkAwaysToday,
				WalkAwayDays = item.WalkAwayDays.Any( day => day != 0 ) ? item.WalkAwayDays.ToArray() : null
			} ).ToList(),
			Loans = park.Loans.Select( loan => new LoanData
			{
				OfferIndex = loan.OfferIndex,
				LenderNameIndex = loan.LenderNameIndex,
				OriginalAmount = loan.OriginalAmount,
				AprPercent = loan.AprPercent,
				Months = loan.Months,
				MonthlyRepayment = loan.MonthlyRepayment,
				MonthsRemaining = loan.MonthsRemaining,
				RemainingBalance = loan.RemainingBalance
			} ).ToList(),
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
					Energy = member.Energy,
					State = member.State,
					BusyTurns = member.BusyTicks,
					AwaitingJobArrival = member.AwaitingJobArrival,
					AssignedInstanceId = member.AssignedInstanceId,
					LitterCell = member.LitterCell is { } litter ? new[] { litter.X, litter.Y } : null,
					WorldState = (member.Navigation != null || member.Job != StaffJob.Idle || member.EffectCell != null || member.HasPatrolArea
						|| member.Thought != 0 || member.BubbleThought != 0 || member.BubbleTurn != 0) ? StaffWorldData.Capture( member ) : null,
					CellX = member.CellX,
					CellY = member.CellY,
					TravelSeconds = member.TravelSeconds
				} ).ToList(),
				Candidates = park.Staff.Candidates.ToList(),
				TrainingBudgets = Enum.GetValues<StaffType>().Select( park.Staff.GetTrainingBudget ).ToList(),
				NextId = park.Staff.NextId,
				NextPoolUpdateTick = park.Staff.NextPoolUpdateTick,
				Strikes = CaptureStrikes( park.Staff.Strikes )
			},
			Research = new ResearchData
			{
				Completed = park.Research.Completed.OrderBy( key => key.InfoId ).ThenBy( key => key.Level ).Select( key => new[] { key.InfoId, key.Level } ).ToList(),
				Progress = park.Research.ProgressEntries.OrderBy( entry => entry.Key.InfoId ).ThenBy( entry => entry.Key.Level ).Select( entry => new[] { entry.Key.InfoId, entry.Key.Level, entry.Points } ).ToList(),
				Effort = park.Research.Effort.ToList(),
				Current = park.Research.CurrentEntries.Any() ? park.Research.CurrentEntries.Select( entry => new[] { (int)entry.Category, entry.InfoId, entry.Level } ).ToList() : null,
				Workload = park.Research.Workload == Math.Clamp( park.Settings.ResearchStartingWorkLoad, 0, 100 ) ? null : park.Research.Workload
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
			Counters = new Dictionary<string, long>( park.Counters.Values ),
			MechanicCursor = park.MechanicCursor,
			PriceStatistics = park.PriceStatistics.Capture() is { Count: > 0 } rings
				? rings.Select( ring => new PriceStatisticData { Category = ring.Category, Head = ring.Head, Full = ring.Full, Values = ring.Values } ).ToList() : null,
			World = world == null ? null : new WorldData { Seed = world.Seed, GuestRandom = world.GuestRandom, ScriptRandom = world.ScriptRandom, SoundSeed = world.SoundSeed }
		};
	}

	public static string Serialize( ParkEconomy park, WorldRandomState? world = null ) => JsonSerializer.Serialize( Capture( park, world ), Options );

	private static StrikeData? CaptureStrikes( StaffStrikeBoard board )
	{
		var roles = Enum.GetValues<StaffType>();
		if ( roles.All( role => board.Level( role ) == 0 && !board.IsActive( role ) && board.LastConsidered( role ) == 0 ) )
			return null;
		return new StrikeData
		{
			Levels = roles.Select( board.Level ).ToList(),
			Active = roles.Select( board.IsActive ).ToList(),
			LastConsidered = roles.Select( board.LastConsidered ).ToList()
		};
	}

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

	/// <summary>
	/// A staff member's job time in park turns. A save that has only the old field (tenths of a park hour, 360 park seconds each) is converted
	/// at 3750 park seconds per turn, rounded up, so a job in progress never ends early and never lasts about ten times too long.
	/// </summary>
	internal static long BusyTurnsOf( StaffMemberData member ) =>
		member.BusyTurns ?? ((Math.Clamp( member.BusyTicks!.Value, -1_000_000_000_000, 1_000_000_000_000 ) * 360 + ParkCalendar.SecondsPerTurn - 1) / ParkCalendar.SecondsPerTurn );

	/// <summary>Validates a parsed economy save in place. Internal: the world-save envelope validates its embedded economy through these same rules.</summary>
	internal static void Validate( Data data )
	{
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"Park save is inconsistent: {message}." );
		}
		Require( data.Tick >= 0 && Enum.IsDefined( data.Speed ) && Enum.IsDefined( data.Mode ), "clock" );
		Require( data.EntranceFee >= 0 && data.MonthsInRed >= 0 && data.LitterScaled >= 0 && data.TicketsSpent >= 0, "park values" );
		Require( data.TurnEnteredRed is not { } enteredRed || enteredRed >= 0 && enteredRed <= ParkCalendar.Turn( data.Tick ), "turn entered red" );
		var littered = new HashSet<(int, int)>();
		foreach ( var cell in data.Litter )
			Require( cell.X is >= 0 and < ParkLitter.MapSize && cell.Y is >= 0 and < ParkLitter.MapSize && cell.Type is >= 1 and <= (int)LitterType.Stinkbomb
				&& cell.Collector >= 0 && littered.Add( (cell.X, cell.Y) ), $"litter at ({cell.X}, {cell.Y})" );
		// A claim belongs to a handyman: in a world his own search takes it again (the original keeps a claim after a failed walk); without one he
		// must be sweeping it, or nothing would end the claim.
		foreach ( var cell in data.Litter.Where( cell => cell.Collector != 0 ) )
			Require( data.Staff.Members.Any( member => member.Id == cell.Collector && member.Type == StaffType.Handyman
				&& (member.WorldState != null || member.LitterCell is [var x, var y] && x == cell.X && y == cell.Y) ),
				$"litter at ({cell.X}, {cell.Y}) claimed by staff {cell.Collector}, who does not sweep it" );
		Require( data.Ledger.MonthIndex == ParkCalendar.MonthIndex( data.Tick ), "ledger month matches the clock" );
		Require( data.Ledger.History.Count <= ParkLedger.MaximumHistoryMonths && data.MonthlyAdmissions.Count <= ParkLedger.MaximumHistoryMonths, "history length" );
		Require( data.Ledger.CurrentTotals.Values.All( value => value >= 0 ) && data.Ledger.History.All( month => month.Totals.Values.All( value => value >= 0 ) ), "ledger totals" );
		var ids = new HashSet<int>();
		foreach ( var item in data.Objects )
		{
			Require( item.Id > 0 && item.Id < data.NextObjectId && ids.Add( item.Id ), $"object id {item.Id}" );
			Require( item.Level is >= 0 and <= 2 && item.PendingLevel is >= 0 and <= 2 && item.StateOfRepair is >= 0 and <= 100 && item.Statistics.Length == 9, $"object {item.Id}" );
			Require( !item.UpgradeCostDue || item.PendingLevel > item.Level, $"object {item.Id} upgrade cost" );
			Require( item.BuiltTick <= data.Tick && item.Price >= 0 && item.CostOfGoods >= 0 && item.ChanceOfLosingPercent is >= 0 and <= 100, $"object {item.Id} values" );
			Require( item.Repair is null || (double.IsFinite( item.Repair.Value ) && item.Repair.Value is >= 0 and <= 100 && (int)item.Repair.Value == item.StateOfRepair), $"object {item.Id} repair" );
			Require( item.LifeGauge is null || (double.IsFinite( item.LifeGauge.Value ) && item.LifeGauge.Value is >= 0 and <= 100), $"object {item.Id} life gauge" );
			Require( item.ClaimTurn >= 0 && item.ClaimTurn <= ParkCalendar.Turn( data.Tick ) && (item.ClaimTurn == 0 || item.MechanicId != 0), $"object {item.Id} claim turn" );
			Require( item.WalkAways >= 0 && item.WalkAwaysToday >= 0 && item.WalkAwaysToday <= item.WalkAways
				&& (item.WalkAwayDays is null || item.WalkAwayDays.Length <= ParkEconomy.WalkAwayHistoryDays && item.WalkAwayDays.All( day => day >= 0 )), $"object {item.Id} walk-aways" );
		}
		var staffIds = new HashSet<int>();
		foreach ( var member in data.Staff.Members )
		{
			Require( member.Id > 0 && member.Id < data.Staff.NextId && staffIds.Add( member.Id ), $"staff id {member.Id}" );
			Require( Enum.IsDefined( member.Type ) && member.Grade is >= 0 and < BalanceSettings.GradeCount && member.TrainingPoints is >= 0 and < ParkStaff.TrainingPointsPerGrade, $"staff {member.Id}" );
			Require( member.Happiness is >= 0f and <= StaffVitals.Maximum && member.Energy is >= 0f and <= StaffVitals.Maximum, $"staff {member.Id} vitals" );
			Require( (member.BusyTurns is null) != (member.BusyTicks is null) && (member.BusyTurns ?? member.BusyTicks!.Value) >= 0 && (member.AssignedInstanceId == 0 || ids.Contains( member.AssignedInstanceId )), $"staff {member.Id} job" );
			Require( member.WorldState is not { } world || (StaffWorldData.Fits( member.Type, world.Job ) && world.PatrolArea is { Length: 4 } area
				&& area[0] is >= 0 and < ParkLitter.MapSize && area[2] >= area[0] && area[2] < ParkLitter.MapSize && area[1] is >= 0 and < ParkLitter.MapSize
				&& area[3] >= area[1] && area[3] < ParkLitter.MapSize && world.BaseSpeed is >= 0 and <= 255 && world.SpeedBonus is >= 0 and <= 255
				&& float.IsFinite( world.SmoothedSpeed ) && (world.ToiletId == 0 || member.Type == StaffType.Handyman && ids.Contains( world.ToiletId ))
				&& (world.TargetGuestId == 0 || member.Type == StaffType.Guard && world.TargetGuestId > 0)
				&& (world.EffectCell is null || ParkEconomy.EffectRow( member.Type ) != null && world.EffectCell is [>= 0 and < ParkLitter.MapSize, >= 0 and < ParkLitter.MapSize])
				&& (world.RideId == 0 || member.Type == StaffType.Mechanic && ids.Contains( world.RideId ))
				&& (world.RestAreaId == 0 || ids.Contains( world.RestAreaId ) && world.Job is StaffJob.ToRestArea or StaffJob.Resting or StaffJob.Idle)
				&& (world.OffGridLeg is null || world.OffGridLeg.Length % 4 == 0 && world.OffGridLeg.Chunk( 4 ).All( point =>
					point[0] is >= 0 and < ParkLitter.MapSize && point[1] is >= 0 and < ParkLitter.MapSize && point[2] is >= 0 and < 256 && point[3] is >= 0 and < 256 ))
				&& (world.Navigation is null || world.Navigation.IsValid)), $"staff {member.Id} world state" );
			Require( member.LitterCell is null || (member.Type == StaffType.Handyman && member.LitterCell.Length == 2 && member.AssignedInstanceId == 0
				&& (member.WorldState is { Job: StaffJob.ToLitter or StaffJob.Sweeping } || member.WorldState is null && BusyTurnsOf( member ) > 0)
				&& data.Litter.Any( cell => cell.X == member.LitterCell[0] && cell.Y == member.LitterCell[1] && cell.Collector == member.Id )), $"staff {member.Id} litter job" );
			if ( member.AwaitingJobArrival )
			{
				var job = data.Objects.FirstOrDefault( item => item.Id == member.AssignedInstanceId );
				Require( member.Type == StaffType.Mechanic && BusyTurnsOf( member ) > 0 && Enum.IsDefined( member.State )
					&& job is { Kind: ParkObjectKind.Ride } && job.MechanicId == member.Id
					&& (job.IsBrokenDown ? RideBreakdownRules.GaugeByte( (float)(job.LifeGauge ?? 100) ) != 0 || job.MaintenanceRequested : job.PendingLevel > job.Level || job.StateOfRepair < ParkEconomy.WornStateOfRepair || job.MaintenanceRequested),
					$"staff {member.Id} awaiting arrival job ownership" );
			}

			Require( member.CellX.HasValue == member.CellY.HasValue && (!member.CellX.HasValue || (float.IsFinite( member.CellX.Value ) && float.IsFinite( member.CellY!.Value ))) && double.IsFinite( member.TravelSeconds ) && member.TravelSeconds >= 0 && member.TravelSeconds < StaffWorldSimulation.SecondsPerCell, $"staff {member.Id} position" );
		}
		foreach ( var candidate in data.Staff.Candidates )
			Require( candidate.Id > 0 && candidate.Id < data.Staff.NextId && staffIds.Add( candidate.Id ) && candidate.Grade is >= 0 and < BalanceSettings.GradeCount, $"candidate {candidate.Id}" );
		Require( data.Staff.TrainingBudgets.Count == BalanceSettings.StaffTypeCount && data.Staff.TrainingBudgets.All( value => value >= 0 ), "training budgets" );
		if ( data.Staff.Strikes is { } strikes )
			Require( strikes.Levels.Count == StaffStrikeBoard.Roles && strikes.Active.Count == StaffStrikeBoard.Roles && strikes.LastConsidered.Count == StaffStrikeBoard.Roles
				&& strikes.Levels.All( level => level is >= 0 and <= 4 ), "staff strikes" );
		Require( (data.Research.Workload == null || data.Research.Workload is >= 0 and <= 100) && (data.Research.Current == null || data.Research.Current.All( entry => entry.Length == 3 && Enum.IsDefined( (ResearchCategory)entry[0] ) )) && data.Research.Effort.Count == 5 && data.Research.Completed.All( key => key.Length == 2 ) && data.Research.Progress.All( entry => entry.Length == 3 && entry[2] >= 0 ), "research" );
		Require( data.MechanicCursor >= 0, "mechanic cursor" );
		Require( data.PriceStatistics is null || data.PriceStatistics.All( ring => ring?.Values is not null )
			&& ParkPriceStatistics.IsValid( data.PriceStatistics.Select( ring => new PriceStatisticRing( ring.Category, ring.Head, ring.Full, ring.Values ) ).ToList() ), "price statistics" );
		foreach ( var loan in data.Loans )
			Require( loan.MonthsRemaining > 0 && loan.MonthsRemaining <= loan.Months && loan.MonthlyRepayment >= 0 && (loan.RemainingBalance is null or >= 0) && data.TakenLoanOffers.Contains( loan.OfferIndex ), $"loan {loan.OfferIndex}" );
	}

	/// <summary>Rebuilds a park from saved data and the original settings/catalogue of the saved theme.</summary>
	public static ParkEconomy Restore( Data data, BalanceSettings settings, IEconomyObjectCatalog catalog )
	{
		ArgumentNullException.ThrowIfNull( data );
		if ( !string.Equals( settings.Theme, data.Theme, StringComparison.OrdinalIgnoreCase ) || settings.IsEasy != data.Easy )
			throw new InvalidDataException( $"Park save is for {data.Theme}{(data.Easy ? " (easy)" : "")}, not {settings.Theme}{(settings.IsEasy ? " (easy)" : "")}." );
		var park = new ParkEconomy( settings, catalog, data.Mode, 0 );
		park.Random.Restore( data.RandomState );
		park.Ledger.Restore( data.Ledger.Balance, data.Ledger.OpeningBalance, data.Ledger.MonthIndex, data.Ledger.CurrentTotals,
			data.Ledger.History.Select( month => new LedgerMonth( month.MonthIndex, month.Totals, month.OpeningBalance, month.ClosingBalance, month.ParkRating, month.ParkValue ) ) );
		park.Ledger.RestoreProfitThisYear( data.Ledger.ProfitThisYear );
		park.RestoreTicketItems( data.TicketItems );
		if ( data.LitterScaled > 0 && data.Litter.Count == 0 )
			Log.Trace( $"Park save: the legacy park-wide litter ({data.LitterScaled / 100.0:0.##} items) has no cells to land on; the park loads clean." );
		park.RestoreState( data.Tick, data.Speed, data.ParkOpen, data.EntranceFee, data.Bankrupt, data.MonthsInRed,
			data.Litter.Select( cell => new LitterCell( cell.X, cell.Y, (LitterType)cell.Type, cell.Collector, cell.ClaimTurn ) ), data.TicketsSpent, data.NextObjectId, data.DroppedAdmissions,
			data.Objects.Select( item => new ParkObjectState
			{
				Id = item.Id,
				InfoId = item.InfoId,
				Kind = item.Kind,
				BuiltTick = item.BuiltTick,
				Imported = item.Imported,
				Level = item.Level,
				PendingLevel = item.PendingLevel,
				UpgradeCostDue = item.UpgradeCostDue,
				TotalSpent = item.TotalSpent,
				Price = item.Price,
				CostOfGoods = item.CostOfGoods,
				ChanceOfLosingPercent = item.ChanceOfLosingPercent,
				IsOpen = item.IsOpen,
				Repair = item.Repair ?? item.StateOfRepair,
				LifeGauge = item.LifeGauge ?? 100,
				IsBrokenDown = item.IsBrokenDown,
				MechanicId = item.MechanicId,
				ClaimTurn = item.ClaimTurn,
				MaintenanceRequested = item.MaintenanceRequested,
				FireworksSpent = item.FireworksSpent,
				TimesUsed = item.TimesUsed ?? item.Statistics[0] + item.Statistics[1],
				CustomersThisMonth = item.Statistics[0],
				CustomersLastMonth = item.Statistics[1],
				TakingsThisMonth = item.Statistics[2],
				TakingsLastMonth = item.Statistics[3],
				CostsThisMonth = item.Statistics[4],
				CostsLastMonth = item.Statistics[5],
				WinnersThisMonth = item.Statistics[6],
				WinnersLastMonth = item.Statistics[7],
				TotalProfit = item.Statistics[8],
				WalkAways = item.WalkAways,
				WalkAwaysToday = item.WalkAwaysToday,
				WalkAwayDays = item.WalkAwayDays?.ToList() ?? new()
			} ),
			data.Loans.Select( loan => loan.ToAccount() ).ToList(), data.TakenLoanOffers, data.MonthlyAdmissions );
		// Older saves keep only the months in the red: the stamp is counted back so that the bank counts as many 30-day months
		park.RestoreBankWords( data.WithdrawalsEnabled ?? true, data.LastBalance ?? data.Ledger.Balance,
			data.TurnEnteredRed ?? LegacyTurnEnteredRed( ParkCalendar.Turn( data.Tick ), data.MonthsInRed, data.Ledger.Balance ) );
		park.Staff.Restore( data.Staff.Members.Select( member => new StaffMember
		{
			Id = member.Id,
			Type = member.Type,
			NameIndex = member.NameIndex,
			Grade = member.Grade,
			TrainingPoints = member.TrainingPoints,
			HiredTick = member.HiredTick,
			Happiness = member.Happiness,
			Energy = member.Energy,
			State = member.State,
			BusyTicks = BusyTurnsOf( member ),
			AwaitingJobArrival = member.AwaitingJobArrival,
			AssignedInstanceId = member.AssignedInstanceId,
			LitterCell = member.LitterCell is { Length: 2 } litter ? (litter[0], litter[1]) : null,
			CellX = member.CellX,
			CellY = member.CellY,
			TravelSeconds = member.TravelSeconds
		} ), data.Staff.Candidates, data.Staff.TrainingBudgets, data.Staff.NextId, data.Staff.NextPoolUpdateTick );
		foreach ( var saved in data.Staff.Members.Where( member => member.WorldState != null ) )
			saved.WorldState!.Apply( park.Staff.Members.Single( member => member.Id == saved.Id ) );
		park.ApplyStaffEffects();
		foreach ( var member in park.Staff.Members.Where( member => member.AwaitingJobArrival ) )
			if ( !park.OwnsMechanicJob( member ) ) throw new InvalidDataException( $"Staff {member.Id} cannot await its restored mechanic job." );

		if ( data.Staff.Strikes is { } strikes )
			park.Staff.Strikes.Restore( strikes.Levels, strikes.Active, strikes.LastConsidered );
		park.Research.Restore( data.Research.Completed.Select( key => (key[0], key[1]) ),
			data.Research.Progress.Select( entry => (((int)entry[0], (int)entry[1]), entry[2]) ), data.Research.Effort,
			data.Research.Current?.Select( entry => ((ResearchCategory)entry[0], entry[1], entry[2]) ) );
		if ( data.Research.Workload is int workload )
			park.Research.SetWorkload( workload );
		park.Objectives.Restore( data.Objectives.NextOfferDay, data.Objectives.NextListPosition, data.Objectives.Current, data.Objectives.Declines, data.Objectives.Finished, data.Objectives.GoldenTickets );
		park.Counters.Restore( data.Counters );
		park.MechanicCursor = data.MechanicCursor;
		if ( data.PriceStatistics is { } statistics )
			park.PriceStatistics.Restore( statistics.Select( ring => new PriceStatisticRing( ring.Category, ring.Head, ring.Full, ring.Values ) ).ToList() );
		return park;
	}

	public static ParkEconomy Load( string path, Func<string, bool, (BalanceSettings Settings, IEconomyObjectCatalog Catalog)> loadTheme ) =>
		LoadState( path, loadTheme ).Park;

	/// <summary>Loads the economy and the saved world streams (null for saves without them).</summary>
	public static (ParkEconomy Park, WorldRandomState? World) LoadState( string path, Func<string, bool, (BalanceSettings Settings, IEconomyObjectCatalog Catalog)> loadTheme )
	{
		var data = Deserialize( ReadCapped( path ) );
		var (settings, catalog) = loadTheme( data.Theme, data.Easy );
		return (Restore( data, settings, catalog ), ToWorldState( data ));
	}

	public static WorldRandomState? ToWorldState( Data data ) =>
		data.World is { } world ? new WorldRandomState( world.Seed, world.GuestRandom, world.ScriptRandom, world.SoundSeed ) : null;

	/// <summary>Loads a save using the original data of its theme from the game file system.</summary>
	public static ParkEconomy Load( string path ) => Load( path, ( theme, easy ) => (BalanceSettings.Load( theme, easy ), EconomyObjectCatalog.Load( theme, easy )) );

	/// <summary>Writes atomically: a complete temporary file is moved over the destination.</summary>
	public static void Save( string path, ParkEconomy park, WorldRandomState? world = null )
	{
		var destination = GetPath( path );
		var bytes = System.Text.Encoding.UTF8.GetBytes( Serialize( park, world ) );
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

	private static long LegacyTurnEnteredRed( long turn, int monthsInRed, long balance )
	{
		if ( balance >= 0 || monthsInRed <= 0 )
			return balance < 0 ? turn : 0;
		var turns = (monthsInRed * ParkEconomy.SecondsPerMonthInRed + ParkCalendar.SecondsPerTurn - 1) / ParkCalendar.SecondsPerTurn;
		return Math.Max( 0, turn - turns );
	}
}
