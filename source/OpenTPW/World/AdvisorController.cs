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

/// <summary>
/// A scored advice message: descriptor fields plus the Advisor.sam key of its configured score. <paramref name="GameMode"/> is the
/// descriptor's game-mode selector (0: full simulation only, 1: Instant Action only, 2: always).
/// </summary>
internal readonly record struct AdvisorMessageDefinition( int MessageId, int FirstResponseId, int ResponseCount, byte Group, byte MaximumPendingDuplicates, string ScoreKey, byte GameMode = 2 );

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

	/// <summary>The Advisor.sam key of the score every help message carries.</summary>
	// [BIN:TPW-EXE:0x0059E8C0 help score] the score callback of the 82 help descriptors returns the balance field at +0x328, the Advisor.sam key HelpMessage.Score (1000 shipped)
	// [BIN:TPW-EXE:0x0072E124 advisor.sam schema] the schema's int fields laid out in order from +0x18 (GeneralAdvisor +0x18/+0x1C/+0x20, read at 0x59B6C3 as +0x20) put HelpMessage.Score at +0x328 and HelpMessage.ContextHelpScore at +0x32C once the 4-byte gap after the MessageGroups array is counted, which the accessors confirm: four Score getters at +0x31C..+0x328 (0x59E890..0x59E8C0, GeneralWonGoldenTicket, AwardGiven, AwardMoved, HelpMessage) and the money test 0x59E8E6 against +0x330 (WealthHigh.MoreMoneyThan)
	public const string HelpScoreKey = "HelpMessage.Score";
	/// <summary>The Advisor.sam key of the score F1 gives the replayed last tutorial advice (<see cref="AdvisorController.ReplayLastTutorial"/>).</summary>
	// [BIN:TPW-EXE:0x0059AB8E replay score] the only reader of the balance field +0x32C (0xFB386C), HelpMessage.ContextHelpScore (1000 shipped)
	public const string ContextHelpScoreKey = "HelpMessage.ContextHelpScore";

	private static IReadOnlyList<AdvisorMessageDefinition>? help;

	/// <summary>The help messages (<see cref="AdvisorHelpTable"/>) as definitions with the shared help score.</summary>
	public static IReadOnlyList<AdvisorMessageDefinition> HelpMessages => help ??= AdvisorHelpTable.Messages
		.Select( message => new AdvisorMessageDefinition( message.MessageId, message.FirstResponseId, message.ResponseCount, message.Group, message.MaximumPendingDuplicates, HelpScoreKey, message.GameMode ) )
		.ToArray();

	/// <summary>The bound game-event messages and the help messages.</summary>
	public static IEnumerable<AdvisorMessageDefinition> AllMessages => Messages.Concat( HelpMessages );

	/// <summary>Whether a message's game-mode selector admits the game type (2 = Instant Action, else the full simulation).</summary>
	// [BIN:TPW-EXE:0x0059C0A0 descriptor score] selector 2 always passes, 0 passes only while the game-mode global 0xFB3B7C is 0, 1 only while it is 2; any other state scores -1 and nothing is said
	public static bool GameModeAccepts( byte selector, int gameType ) => selector switch
	{
		0 => gameType == 0,
		1 => gameType == 2,
		_ => true,
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
	/// <summary><c>HelpMessage.ContextHelpScore</c>: the score F1 gives the replayed last tutorial advice (0 when the file lacks it, which leaves the replay silent).</summary>
	public int ContextHelpScore { get; init; }

	public static AdvisorBalance Load() => Parse( FileSystem.ReadAllBytes( Path ), Path );

	public static AdvisorBalance Parse( byte[] bytes, string source )
	{
		ArgumentNullException.ThrowIfNull( bytes );
		if ( bytes.Length > MaximumBytes )
			throw new InvalidDataException( $"{source} is {bytes.Length} bytes; advisor settings are limited to {MaximumBytes}." );
		var settings = new SamSettings( new[] { SamDocument.Parse( Encoding.Latin1.GetString( bytes ), source ) } );
		var groups = new Dictionary<int, (uint, bool, uint)>();
		foreach ( var group in AdvisorTables.AllMessages.Select( message => (int)message.Group ).Distinct() )
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
			// [BIN:TPW-EXE:0x0059BEA0 advisor balance] absence result (medium-high confidence): no code reads fields +0x18/+0x1C (MinTimeAnyMessage/MinTimeSameMessage) of the Advisor.sam object (static 0xFB3540, built by 0x59BEA0); +0x20 (MinScore) is read at 0x59B6C3. They are loaded and not applied
			MinimumTimeAnyMessage = settings.GetInt( "GeneralAdvisor.MinTimeAnyMessage" ),
			MinimumTimeSameMessage = settings.GetInt( "GeneralAdvisor.MinTimeSameMessage" ),
			Groups = groups,
			// The help score is optional: a settings file without it leaves the help silent instead of disabling the advisor.
			ContextHelpScore = settings.GetInt( AdvisorTables.ContextHelpScoreKey, 0, optional: true ),
			Scores = AdvisorTables.AllMessages.Select( message => message.ScoreKey ).Distinct( StringComparer.OrdinalIgnoreCase )
				.ToDictionary( key => key, key => key == AdvisorTables.HelpScoreKey ? settings.GetInt( key, 0, optional: true ) : settings.GetInt( key ), StringComparer.OrdinalIgnoreCase ),
		};
	}

	public IEnumerable<AdvisorMessageDescriptor> Descriptors() => AdvisorTables.AllMessages.Select( message =>
	{
		var group = Groups[message.Group];
		return new AdvisorMessageDescriptor( message.MessageId, message.FirstResponseId, message.ResponseCount, message.Group,
			group.RepeatGroups, group.SayOnlyOnce, group.DiscardAfterSlaps, message.MaximumPendingDuplicates );
	} );

	public int ScoreOf( int messageId ) => Scores[AdvisorTables.AllMessages.Single( message => message.MessageId == messageId ).ScoreKey];
}

/// <summary>
/// The advisor controller for the wired game events: the event handler builds pending advice with
/// its configured score, and <see cref="Update"/> picks, consumes and completes one response at a
/// time through <see cref="AdvisorScoreQueue"/>. Playback is a callback taking the response ID and
/// returning the action span (null when the response could not be played, e.g. absent from the table;
/// the attempt still succeeds, with span 0, as in the original wrapper).
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
	/// A window or tool asks for its help (the original's 0x486B00): the message becomes pending advice with the help score, subject to
	/// the queue's admission rules (tutorial option, once-only, slaps, duplicates, repeat interval) and to the descriptor's game-mode
	/// selector. Returns null for a message the controller has no descriptor for, or one the game mode excludes.
	/// </summary>
	public AdvisorQueueAdmission? Advise( int messageId, uint liveGameTick, int gameType, bool tutorialEnabled )
	{
		var definition = AdvisorTables.AllMessages.FirstOrDefault( message => message.MessageId == messageId );
		if ( definition.ScoreKey == null || !AdvisorTables.GameModeAccepts( definition.GameMode, gameType ) )
			return null;
		return Queue.Enqueue( new AdvisorPendingAdvice( messageId, Balance.ScoreOf( messageId ) ), liveGameTick, tutorialEnabled );
	}

	/// <summary>
	/// F1 in a HUD window (the default window procedure 0x488BA0, key 0x70 without modifiers): the last tutorial advice the queue kept is
	/// offered again at <c>HelpMessage.ContextHelpScore</c>, bypassing its once-only and slap checks. Null when none is kept.
	/// </summary>
	// [BIN:TPW-EXE:0x00488C02 F1 replay] message 0x1000B with key 0x70 and no modifier byte calls the controller getter 0x5194D0 and 0x59AB50
	public AdvisorQueueAdmission? ReplayLastTutorial( uint liveGameTick, bool tutorialEnabled ) =>
		Queue.ReplayLastTutorial( Balance.ContextHelpScore, liveGameTick, tutorialEnabled );

	/// <summary>The window or tool no longer needs its help (the original's 0x486B40): see <see cref="AdvisorScoreQueue.Withdraw"/>.</summary>
	public AdvisorWithdrawal Withdraw( int messageId, uint advisorClock ) => Queue.Withdraw( messageId, advisorClock );

	/// <summary>
	/// The interface mode changed: the help of the tools and modes is withdrawn (<see cref="AdvisorUiHelp.ModeHelpMessages"/>); true when
	/// one of them was being spoken.
	/// </summary>
	public bool WithdrawModeHelp( uint advisorClock )
	{
		var cancelled = false;
		foreach ( var messageId in AdvisorUiHelp.ModeHelpMessages )
			cancelled |= Queue.Withdraw( messageId, advisorClock ).SpeechCancelled;
		return cancelled;
	}

	/// <summary>
	/// The controller's own-format state for a park save: its queue's pending records, history and action
	/// reservation (an <see cref="ImportOriginal"/> import takes this path too).
	/// </summary>
	internal sealed record AdvisorControllerSnapshot( AdvisorQueueSnapshot Queue );

	/// <summary>The controller's state for a park save; fails explicitly while a playback attempt is outstanding.</summary>
	public AdvisorControllerSnapshot CaptureSnapshot() => new( Queue.CaptureSnapshot() );

	/// <summary>Checks a snapshot against this controller's bound messages without touching it.</summary>
	public void ValidateSnapshot( AdvisorControllerSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		Queue.ValidateSnapshot( snapshot.Queue );
	}

	/// <summary>
	/// Continues this controller (fresh, with the same balance) from a validated snapshot. Restore after the
	/// level-start game events of <see cref="AutomaticAdvisor.AttachLevel"/> so the saved pending advice and
	/// history replace the freshly raised welcome advice.
	/// </summary>
	public void RestoreSnapshot( AdvisorControllerSnapshot snapshot )
	{
		ValidateSnapshot( snapshot );
		Queue.RestoreSnapshot( snapshot.Queue );
	}

	/// <summary>
	/// Takes the pending records and per-message history of an original park save's advisor controller (model 11).
	/// Free (invalid) records stay free; records and non-initial history of messages without a bound descriptor are
	/// dropped and counted. Returns a report line for the log.
	/// </summary>
	// [BIN:STP-PPC:0x100082E0 advisor controller serializer] eight 21-byte pending records (0x1000BC10) from controller +20, then 351 16-byte history entries (tick, variant, played, slaps) from +224, indexed by message ID
	public string ImportOriginal( SaveAdvisorController saved )
	{
		ArgumentNullException.ThrowIfNull( saved );
		var pending = new AdvisorPendingAdvice?[AdvisorScoreQueue.Capacity];
		var imported = 0;
		var dropped = new List<uint>();
		var fields = new List<string>();
		for ( var index = 0; index < pending.Length; index++ )
		{
			var record = saved.Pending[index];
			if ( !record.Valid )
				continue;
			if ( record.MessageId > int.MaxValue || !Queue.HasDescriptor( (int)record.MessageId ) )
			{
				dropped.Add( record.MessageId );
				continue;
			}
			pending[index] = new AdvisorPendingAdvice( (int)record.MessageId, record.Score, record.ResponseNo, record.OverrideOnlyOnce != 0 );
			fields.Add( $"message {record.MessageId} thing {record.ThingId} secondary {record.Secondary} score overridden {record.ScoreOverridden}" );
			imported++;
		}
		var history = new Dictionary<int, AdvisorMessageHistory>();
		var droppedHistory = 0;
		for ( var messageId = 0; messageId < saved.History.Count; messageId++ )
		{
			var entry = saved.History[messageId];
			if ( !Queue.HasDescriptor( messageId ) )
			{
				if ( !entry.IsInitial )
					droppedHistory++;
				continue;
			}
			history[messageId] = new AdvisorMessageHistory( entry.SavedGameTick, entry.PreviousVariant, entry.HasBeenPlayed, entry.SlapCount );
		}
		Queue.Restore( pending, history );
		// [BIN:TPW-EXE:0x0059BA30 record serializer] the controller's save and load pass the record at +0x16D0 through it (0x59A1C6, 0x59A49D), so the save's current record is the last tutorial advice
		var current = saved.Current;
		var lastTutorial = current.Valid && current.MessageId <= int.MaxValue && Queue.HasDescriptor( (int)current.MessageId ) && Queue.IsTutorial( (int)current.MessageId )
			? new AdvisorPendingAdvice( (int)current.MessageId, current.Score, current.ResponseNo, current.OverrideOnlyOnce != 0 ) : null;
		Queue.RestoreLastTutorial( lastTutorial );
		// [APPROX:ADVISOR-022] An original save's pending advice and history of messages without a bound descriptor (only 0, 106, 128, 129 and 323 are bound) are dropped on import; OpenTPW's own park save stores the controller (ParkResumeSaveFile, AutomaticAdvisorSnapshot) — evidence needed: the remaining descriptor rows (table 0x76E300)
		return $"[APPROX:ADVISOR-022] Advisor controller imported: {imported} pending advice, history of {history.Count} bound messages; dropped {dropped.Count} pending advice of unbound messages ({string.Join( ", ", dropped )}) and {droppedHistory} unbound history entries; last tutorial advice {(lastTutorial != null ? $"message {lastTutorial.MessageId} kept" : current.Valid ? $"message {current.MessageId} dropped (unbound or not a tutorial)" : "none")}; the imported records' thing, secondary and score-overridden fields are not kept ({string.Join( "; ", fields )}); the controller's other values are reported under SAVE-010.";
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
			span = play( selection.ResponseId );
		}
		finally
		{
			// [BIN:STP-PPC:0x1000BBF0 controller playback wrapper] the wrapper (sole caller 0x100089FC) fails only for an invalid record or the missing-descriptor response 614, which the bound rows cannot produce; the player's result (0x10006B7C, stored at 0x1000BB64) is kept only as the span, so an unplayed response still records the history and reserves span + 1000 with span 0
			Queue.CompletePlaybackAttempt( selection, playbackSucceeded: true, advisorClock(), liveGameTick(), span ?? 0 );
		}
		return selection;
	}
}
