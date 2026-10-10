using System.Text;

namespace OpenTPW;

/// <summary>
/// <c>CMsgEvent</c> game-event IDs the advisor's event handler turns into advice. These are a
/// separate namespace from advice message IDs, response IDs and speech sample numbers.
/// </summary>
internal enum AdvisorGameEvent
{
	LevelStarted = 0,
	Bankrupted = 2,
	ParkNowOpen = 3,
	ParkNowClosed = 4,
	ResetForEasyMode = 10,
}

/// <summary>A scored advice message: descriptor fields plus the Advisor.sam key of its configured score.</summary>
internal readonly record struct AdvisorMessageDefinition( int MessageId, int FirstResponseId, int ResponseCount, byte Group, byte MaximumPendingDuplicates, string ScoreKey );

/// <summary>
/// The rows of the original 351-record descriptor table reached by the wired game events
/// (docs/reverse/PPC-advisor.md, "Actual CMsgEvent producers"). Only these rows are bound; every
/// other message is absent rather than guessed. Response IDs resolve through the shipped
/// <see cref="AdvisorResponses"/> table (responses 1, 274/275, 308–311 and 587).
/// </summary>
internal static class AdvisorTables
{
	// [BIN:STP-PPC:0x1000D468 descriptor accessors] rows of the 351-record descriptor table (data section 0x1F2B4): message, first response (+32, 0x1000D468), response count (+36, 0x1000D5EC), group (+8), pending-duplicate limit (+28 low byte, 0x1000D770); all use cyclic mode 2 and game-mode selector 2 (always eligible)
	public static readonly AdvisorMessageDefinition[] Messages =
	{
		new( 0, 1, 1, 0, 1, "Welcome.Score" ),
		new( 106, 274, 2, 0, 1, "Bankrupted.Score" ),
		new( 128, 308, 2, 0, 1, "ParkNowOpen.Score" ),
		new( 129, 310, 2, 0, 1, "ParkNowClosed.Score" ),
		new( 323, 587, 1, 1, 1, "PrebuiltPark.Score" ),
	};

	/// <summary>
	/// The advice a game event constructs, in construction order. <paramref name="gameType"/> is the
	/// original game type (2 = Instant Action).
	/// </summary>
	public static IReadOnlyList<int> AdviceFor( AdvisorGameEvent gameEvent, int gameType ) => gameEvent switch
	{
		// [BIN:STP-PPC:0x10009548 advisor event 0] advice 0 (Welcome); then advice 323 (PrebuiltPark) when the game type at TOC −30136 is 2 (0x100096F0–0x10009720; the player selector 0x1013781C writes 2 there for Instant Action)
		AdvisorGameEvent.LevelStarted => gameType == 2 ? new[] { 0, 323 } : new[] { 0 },
		// [BIN:STP-PPC:0x100098D4 advisor event 2] advice 106 (Bankrupted)
		AdvisorGameEvent.Bankrupted => new[] { 106 },
		// [BIN:STP-PPC:0x10009A88 advisor event 3] advice 128 (ParkNowOpen)
		AdvisorGameEvent.ParkNowOpen => new[] { 128 },
		// [BIN:STP-PPC:0x10009C3C advisor event 4] advice 129 (ParkNowClosed)
		AdvisorGameEvent.ParkNowClosed => new[] { 129 },
		_ => Array.Empty<int>(),
	};
}

/// <summary>
/// The scoring controls of <c>Advisor/Advisor.sam</c> used by the bound messages: the general
/// minimum score and intervals, the per-group repeat/once/slap controls and the configured scores.
/// </summary>
internal sealed class AdvisorBalance
{
	// [DATA:Advisor/Advisor.sam] loaded by the scoring data loader (STP-PPC 0x1000CFD4)
	public const string Path = "/Advisor/Advisor.sam";
	/// <summary>Upper bound for the settings file (the shipped one is about 20 KB).</summary>
	public const int MaximumBytes = 1 << 20;

	public required int MinimumScore { get; init; }
	public required int MinimumTimeAnyMessage { get; init; }
	public required int MinimumTimeSameMessage { get; init; }
	public required IReadOnlyDictionary<int, (uint RepeatGroups, bool SayOnlyOnce, uint DiscardAfterSlaps)> Groups { get; init; }
	public required IReadOnlyDictionary<string, int> Scores { get; init; }

	public static AdvisorBalance Load() => Parse( FileSystem.ReadAllBytes( Path ), Path );

	public static AdvisorBalance Parse( byte[] bytes, string source )
	{
		ArgumentNullException.ThrowIfNull( bytes );
		if ( bytes.Length > MaximumBytes )
			throw new InvalidDataException( $"{source} is {bytes.Length} bytes; advisor settings are limited to {MaximumBytes}." );
		var settings = new SamSettings( new[] { SamDocument.Parse( Encoding.Latin1.GetString( bytes ), source ) } );
		var groups = new Dictionary<int, (uint, bool, uint)>();
		foreach ( var group in AdvisorTables.Messages.Select( message => (int)message.Group ).Distinct() )
		{
			// [BIN:STP-PPC:0x10016F4C advisor balance] MessageGroups[g] fields at +36/+40/+44 + 12g
			var prefix = $"MessageGroups[{group}].";
			groups[group] = (checked((uint)settings.GetInt( prefix + "MinTimeSameMessage" )), settings.GetInt( prefix + "SayOnlyOnce" ) != 0,
				checked((uint)settings.GetInt( prefix + "DiscardAfterSlaps" )));
		}
		return new AdvisorBalance
		{
			// [BIN:STP-PPC:0x10016F4C advisor balance] GeneralAdvisor.MinTimeAnyMessage +24, MinTimeSameMessage +28, MinScoreForConsideration +32
			MinimumScore = settings.GetInt( "GeneralAdvisor.MinScoreForConsideration" ),
			// [APPROX:ADVISOR-019] Loaded but not applied: the eligibility path compares the group repeat interval, and no consumer of the general intervals (+24/+28) is traced — evidence needed: the reads of balance fields +24 and +28
			MinimumTimeAnyMessage = settings.GetInt( "GeneralAdvisor.MinTimeAnyMessage" ),
			MinimumTimeSameMessage = settings.GetInt( "GeneralAdvisor.MinTimeSameMessage" ),
			Groups = groups,
			Scores = AdvisorTables.Messages.ToDictionary( message => message.ScoreKey, message => settings.GetInt( message.ScoreKey ), StringComparer.OrdinalIgnoreCase ),
		};
	}

	public IEnumerable<AdvisorMessageDescriptor> Descriptors() => AdvisorTables.Messages.Select( message =>
	{
		var group = Groups[message.Group];
		return new AdvisorMessageDescriptor( message.MessageId, message.FirstResponseId, message.ResponseCount, message.Group,
			group.RepeatGroups, group.SayOnlyOnce, group.DiscardAfterSlaps, message.MaximumPendingDuplicates );
	} );

	public int ScoreOf( int messageId ) => Scores[AdvisorTables.Messages.Single( message => message.MessageId == messageId ).ScoreKey];
}

/// <summary>
/// The advisor controller for the wired game events: the event handler builds pending advice with
/// its configured score, and <see cref="Update"/> picks, consumes and completes one response at a
/// time through <see cref="AdvisorScoreQueue"/>. Playback is a callback taking the response ID and
/// returning the action span (null when the response could not be played, e.g. absent from the table).
/// </summary>
internal sealed class AdvisorController
{
	public AdvisorController( AdvisorBalance balance )
	{
		Balance = balance ?? throw new ArgumentNullException( nameof( balance ) );
		Queue = new AdvisorScoreQueue( balance.Descriptors(), new Dictionary<int, AdvisorMessageHistory>(), balance.MinimumScore );
	}

	public AdvisorBalance Balance { get; }
	public AdvisorScoreQueue Queue { get; }

	/// <summary>Handles one <c>CMsgEvent</c>; returns the admissions of the advice it constructed.</summary>
	public IReadOnlyList<(int MessageId, AdvisorQueueAdmission Admission)> HandleGameEvent( AdvisorGameEvent gameEvent, uint liveGameTick, int gameType, bool tutorialEnabled )
	{
		if ( gameEvent == AdvisorGameEvent.ResetForEasyMode )
		{
			Queue.ClearHistory();
			return Array.Empty<(int, AdvisorQueueAdmission)>();
		}
		var result = new List<(int, AdvisorQueueAdmission)>();
		foreach ( var messageId in AdvisorTables.AdviceFor( gameEvent, gameType ) )
			result.Add( (messageId, Queue.Enqueue( new AdvisorPendingAdvice( messageId, Balance.ScoreOf( messageId ) ), liveGameTick, tutorialEnabled )) );
		return result;
	}

	/// <summary>
	/// One controller update: when not busy, selects the best pending advice, consumes it, plays its
	/// response through <paramref name="play"/> and records the outcome. Returns the attempted selection.
	/// </summary>
	public AdvisorResponseSelection? Update( Func<uint> advisorClock, Func<uint> liveGameTick, Func<int, uint?> play )
	{
		ArgumentNullException.ThrowIfNull( advisorClock );
		ArgumentNullException.ThrowIfNull( liveGameTick );
		ArgumentNullException.ThrowIfNull( play );
		if ( Queue.SelectNext( advisorClock() ) is not { } selection )
			return null;
		Queue.BeginPlaybackAttempt( selection );
		uint? span = null;
		try
		{
			// Configured-field scores recompute to the same value; the wrapper's check is inclusive.
			if ( Queue.RevalidatedPlaybackScoreAccepts( Balance.ScoreOf( selection.Advice.MessageId ) ) )
				span = play( selection.ResponseId );
		}
		finally
		{
			Queue.CompletePlaybackAttempt( selection, span.HasValue, advisorClock(), liveGameTick(), span ?? 0 );
		}
		return selection;
	}
}
