namespace OpenTPW;

/// <summary>
/// One advice record (24 bytes, built by <c>0x71A930</c>): the locate word and dword the caption carries (<c>+0</c>, <c>+4</c>), the variant (<c>+8</c>, -1 lets the variant rule
/// choose), the message (<c>+0xC</c>), the score (<c>+0x10</c>), the valid byte (<c>+0x14</c>), the bypass byte (<c>+0x15</c>) and whether the score was given (<c>+0x16</c>,
/// the explicit path) rather than computed by the descriptor's callback.
/// </summary>
public readonly record struct ThemeParkIncAdviceRecord( ushort Locate, int LocateData, int Variant, int Message, int Score, bool Valid, bool Bypass, bool ExplicitScore );

/// <summary>An advice the advisor played: its message, the response it chose and the locate words the caption carries.</summary>
public readonly record struct ThemeParkIncAdvicePlay( int Message, int Response, ushort Locate, int LocateData );

/// <summary>
/// The advice context the class check reads (<c>0x71BDC0</c>): the mode word <c>0xC65B80</c>, the game object's bytes <c>+0x2E</c> and <c>+0x2F</c>, and the cutscene thing playing
/// (<c>0xA9D230</c>) with its mode (<c>+0x24</c>).
/// </summary>
public readonly record struct ThemeParkIncAdviceContext( int Mode, bool ObjectFlag2E, bool ObjectFlag2F, ushort AnimationThing, int AnimationVariant );

/// <summary>The advisor's saved state (docs/reverse/TPI-ADVISOR.md §3): the eight slots in queue order, the stored tutorial record, the playback window on the advisor clock and the inputs.</summary>
public sealed record ThemeParkIncAdvisorSnapshot( ThemeParkIncAdviceRecord[] Slots, ThemeParkIncAdviceRecord StoredTutorial, uint LastStart, uint LastWindow, int Playing, bool SkipPlayback,
	bool FromHelpButton, bool DelayedFromPanel, uint DelayStart, int DelayedMessage, bool TutorialOn, bool AdvisorPanelUp );

/// <summary>
/// Theme Park Inc's advisor thing (model 11, built by the new-world init at <c>0x64836F</c>): the queue of eight advice records, the acceptance of a new record, the replacement on a full
/// queue, the cancel, and the update that plays the best record when the last one's window has passed. Its play history is the player's (<see cref="ThemeParkIncCareer"/>'s
/// <c>mHistory</c>), so it carries over from park to park as the original's does. The condition scan (<c>0x71C0B0</c>) is not ported yet: <see cref="ConditionScan"/> is off.
/// </summary>
public sealed class ThemeParkIncAdvisor( ThemeParkIncAdvisorSettings settings, ThemeParkIncAdvisorTable table )
{
	public const int Slots = 8;
	/// <summary>The response id the lookups return for none (<c>0x3F6</c>).</summary>
	public const int NoResponse = 0x3F6;
	/// <summary>The message group whose records are mail (<c>0x1A1</c>–<c>0x1A3</c>): never replaced and played first.</summary>
	public const int MailGroup = 8;
	/// <summary>The message group of the tutorial advice, which the TutorialOn option refuses and the update keeps a copy of.</summary>
	public const int TutorialGroup = 1;
	/// <summary>The window after a speech before the next advice (<c>0x718584</c>, <c>+1000</c>).</summary>
	public const uint AfterSpeech = 1000;

	private readonly ThemeParkIncAdviceRecord[] slots = new ThemeParkIncAdviceRecord[Slots];
	private readonly List<ThemeParkIncAdvicePlay> played = new();

	public ThemeParkIncAdvisorSettings Settings => settings;
	public ThemeParkIncAdvisorTable Table => table;
	public IReadOnlyList<ThemeParkIncAdviceRecord> Queue => slots;
	/// <summary><c>+0xEC</c>: the last tutorial record posted (<c>mRequestedLastTutorial</c>), kept whether or not it was accepted.</summary>
	public ThemeParkIncAdviceRecord StoredTutorial { get; private set; }
	/// <summary><c>+0xE4</c>/<c>+0xE8</c>: the advisor clock at the last playback and the window it holds.</summary>
	public uint LastStart { get; private set; }
	public uint LastWindow { get; private set; }
	/// <summary><c>+0x108</c> (<c>mMessagePlaying</c>): the message played last.</summary>
	public int Playing { get; private set; } = ThemeParkIncAdvisorTable.NoMessage;
	/// <summary><c>+0x104</c>: the next due playback is skipped once.</summary>
	public bool SkipPlayback { get; internal set; }
	/// <summary><c>+0x10C</c> (<c>m_bFromHelpButton</c>): a record posted now bypasses the group, once-only and slap checks.</summary>
	public bool FromHelpButton { get; internal set; }
	/// <summary><c>+0x10D</c>, <c>+0x110</c>, <c>+0x114</c>: a window's delayed help message and the advisor clock it was asked at.</summary>
	public bool DelayedFromPanel { get; private set; }
	public uint DelayStart { get; private set; }
	public int DelayedMessage { get; private set; }
	/// <summary>The options' TutorialOn byte (<c>0xA10219</c>), a player input; on by default.</summary>
	public bool TutorialOn { get; set; } = true;
	/// <summary>The advisor panel's window (<c>0xA568BC</c>), a player input: while it is up only tutorial and mail records are chosen.</summary>
	public bool AdvisorPanelUp { get; set; }
	/// <summary>
	/// The help button's message (<c>0x9DDBEC</c>) and the response its playback chose (<c>0x9DDBE8</c>), which the speech reads (P2); statics of the executable, so a load resets
	/// them (D-5).
	/// </summary>
	public int HelpMessage { get; private set; } = ThemeParkIncAdvisorTable.NoMessage;
	public int HelpResponse { get; private set; } = NoResponse;

	/// <summary>
	/// The condition scan's switch (D-6): off until every row that can win is ported (P3). While it is off the update's intake (<c>0x718227</c>) takes nothing.
	/// </summary>
	// [EXT:tpi-capability-gates] the scan 0x71C0B0 evaluates one condition row per world tick and draws from the world generator for the pricing rows; the rows' callbacks are not ported, and a
	// partial scan would change which row wins and with it the draws, so the intake stays off
	public static bool ConditionScan => false;

	/// <summary>
	/// The advisor clock at <paramref name="turn"/>: the world ticks times the tick's real length at normal speed.
	/// </summary>
	// [EXT:tpi-advisor-tick-clock] (user decision D-1, 2026-11-25) the original times the playback window, the window help delay and the cancel's playing test on the park clock's sub-clock
	// (0x41B780, real milliseconds, frozen while paused); OpenTPW derives that clock from the world ticks, 248 ms each, so a pause freezes it and a replay, a load and a co-op peer see the
	// same windows. At a game speed other than normal the window spans the ticks of normal speed
	public static uint Clock( long turn ) => unchecked( (uint)(turn * ParkCalendar.TurnMilliseconds) );

	/// <summary>The score of <paramref name="message"/> in the context (<c>0x71B7E0</c>): -1 for no row, the no-message id, or a class the context does not admit.</summary>
	// [BIN:TPI-EXE:0x71B7E0 advice score] the first row of the message, the class check 0x71BDC0, then the row's callback with this = scorer + row+0x14 (0 in every row)
	public int Score( int message, ThemeParkIncAdviceContext context )
	{
		if ( table.Find( message ) is not { } row || !Admits( row.Class, context ) )
			return -1;
		// a callback that reads game state is the condition scan's (P3) or a sender's (P4, P5); only constant callbacks are reached by the messages posted so far
		return row.ConstantScore ?? throw new NotSupportedException( $"Theme Park Inc advisor message {message:X}: its score callback {row.Callback:X} is not ported." );
	}

	/// <summary>Whether the context admits a descriptor class (<c>0x71BDC0</c>).</summary>
	// [BIN:TPI-EXE:0x71BDC0 class check] mode word 3 admits class 3 only; else the game object's +0x2E class 5, its +0x2F class 7; else a playing cutscene admits class 6, 5 or 4 by its mode
	// 0, 1 or 2 (0x5D07F0, 0x5D0790, 0x5D07C0) and nothing else; else class 2 always, class 0 in mode 0 and class 1 in mode 2
	public static bool Admits( int klass, ThemeParkIncAdviceContext context )
	{
		if ( context.Mode == 3 )
			return klass == 3;
		if ( context.ObjectFlag2E )
			return klass == 5;
		if ( context.ObjectFlag2F )
			return klass == 7;
		if ( context.AnimationThing != 0 )
			return context.AnimationVariant switch { 0 => klass == 6, 1 => klass == 5, 2 => klass == 4, _ => false };
		return klass switch { 2 => true, 0 => context.Mode == 0, 1 => context.Mode == 2, _ => false };
	}

	/// <summary>Builds a record with the descriptor's score (<c>0x71A930</c> with score -1) and offers it to the queue (<c>0x718820</c>).</summary>
	// [BIN:TPI-EXE:0x71A930 advice record] a score below 0 asks the descriptor (0x71B7E0) and clears +0x16; a given score sets it; +0x14 = 1
	public bool Post( int message, ThemeParkIncCareer career, long turn, ThemeParkIncAdviceContext context, ushort locate = 0, int locateData = 0, int variant = -1, bool bypass = false )
		=> Enqueue( new ThemeParkIncAdviceRecord( locate, locateData, variant, message, Score( message, context ), true, bypass, false ), career, turn );

	/// <summary>Builds a record with a given score of 0 or more (the explicit path, <c>+0x16</c> = 1) and offers it to the queue.</summary>
	public bool PostScored( int message, int score, ThemeParkIncCareer career, long turn, ushort locate = 0, int locateData = 0, int variant = -1, bool bypass = false )
		=> Enqueue( new ThemeParkIncAdviceRecord( locate, locateData, variant, message, score >= 0 ? score : throw new ArgumentOutOfRangeException( nameof( score ) ), true, bypass, true ),
			career, turn );

	/// <summary>
	/// Offers a record to the queue (<c>0x718820</c>): the first free slot takes it, else the first lowest-scoring slot that is not mail when its score is lower. True when it was
	/// accepted, also when a full queue kept its slots on an equal score.
	/// </summary>
	// [BIN:TPI-EXE:0x718820 advice enqueue] a tutorial-group record is copied to +0xEC first (0x71BCA0 = 1); the acceptance 0x718B40(id, +0x15); a free slot is the first whose +0x14 is 0;
	// else 0x718E90 (-1: refused) and a strict compare of the scores, an invalid slot or record counting -1; with +0x10C set the message becomes the help button's (0x9DDBEC)
	public bool Enqueue( ThemeParkIncAdviceRecord record, ThemeParkIncCareer career, long turn )
	{
		if ( (table.Find( record.Message )?.Group ?? 10) == TutorialGroup )
			StoredTutorial = record;
		if ( !Accepts( record.Message, record.Bypass, career, turn ) )
			return false;
		var free = Array.FindIndex( slots, slot => !slot.Valid );
		if ( free >= 0 )
			slots[free] = record;
		else
		{
			var index = ReplaceIndex();
			if ( index < 0 )
				return false;
			if ( (slots[index].Valid ? slots[index].Score : -1) < (record.Valid ? record.Score : -1) )
				slots[index] = record;
		}
		if ( FromHelpButton )
			HelpMessage = record.Message;
		return true;
	}

	/// <summary>Whether a new record of <paramref name="message"/> is accepted (<c>0x718B40</c>).</summary>
	// [BIN:TPI-EXE:0x718B40 advice acceptance] in order: TutorialOn off refuses a tutorial-group message (0x71C030); a history entry whose time >> 2 is not 0 refuses while the world ticks >> 2
	// since then are below the group's interval for its play count (0x71BAC0, unsigned); unless the bypass byte or +0x10C: SayOnlyOnce with the played byte (0x71BB90), then the slaps
	// (0x71BD20: not group 10, a limit of 0 never, else slaps >= limit); the pending records of the message reach the row's limit byte (0x71BA40); unless +0x10C: variant rule 0 whose last
	// variant is the last response (0x71BC20, 0x71B9C0)
	public bool Accepts( int message, bool bypass, ThemeParkIncCareer career, long turn )
	{
		var row = table.Find( message );
		if ( !TutorialOn && row?.Group == TutorialGroup )
			return false;
		var history = career.AdviceHistory( message );
		if ( history.WorldTime >> 2 != 0 && unchecked( ((uint)turn >> 2) - (history.WorldTime >> 2) ) < Interval( row, history.Plays ) )
			return false;
		if ( !bypass && !FromHelpButton )
		{
			if ( row is { } once && settings.Groups[once.Group].SayOnlyOnce && history.Played )
				return false;
			if ( row is { Group: not 10 } slapped && settings.Groups[slapped.Group].DiscardAfterSlaps is var limit and not 0 && history.Slaps >= limit )
				return false;
		}
		if ( slots.Count( slot => slot.Valid && slot.Message == message ) >= (row?.Duplicates ?? 0) )
			return false;
		if ( !FromHelpButton && (row?.VariantRule ?? 5) == 0 && history.Variant == (row?.Count ?? 0) - 1 )
			return false;
		return true;
	}

	private uint Interval( ThemeParkIncAdvisorDescriptor? row, int plays ) => row is { } found ? settings.Groups[found.Group].RepeatInterval( plays ) : 0;

	// [BIN:TPI-EXE:0x718E90 replaced slot] over the valid slots with a row whose group is not 8: the first, then any with a strictly lower score; -1 when there is none
	private int ReplaceIndex()
	{
		var chosen = -1;
		for ( var index = 0; index < Slots; index++ )
		{
			if ( !slots[index].Valid || table.Find( slots[index].Message ) is not { } row || row.Group == MailGroup )
				continue;
			if ( chosen < 0 || slots[index].Score < slots[chosen].Score )
				chosen = index;
		}
		return chosen;
	}

	/// <summary>The slot the update plays next (<c>0x718F80</c>), or -1.</summary>
	// [BIN:TPI-EXE:0x718F80 advice selection] over the valid slots with a row (with the advisor panel up, 0x501440, only groups 1 and 8): a mail slot with a positive score is taken, the last
	// one counting; otherwise the first strictly higher score than the best so far, starting from 0
	public int Select()
	{
		var chosen = -1;
		var best = 0;
		var mail = false;
		for ( var index = 0; index < Slots; index++ )
		{
			var slot = slots[index];
			if ( !slot.Valid || table.Find( slot.Message ) is not { } row || AdvisorPanelUp && row.Group is not (TutorialGroup or MailGroup) )
				continue;
			if ( row.Group == MailGroup && slot.Score > 0 )
				(chosen, best, mail) = (index, slot.Score, true);
			if ( slot.Score > best && !mail )
				(chosen, best) = (index, slot.Score);
		}
		return chosen;
	}

	/// <summary>
	/// Removes every pending record of <paramref name="message"/> and stops it when it is the one playing (<c>0x718990</c>); a stopped message's play is undone and counted as a slap.
	/// True when something was removed or stopped; <paramref name="stopped"/> tells the speech to stop.
	/// </summary>
	// [BIN:TPI-EXE:0x718990 advice cancel] a stored tutorial record of the message is dropped (+0x100 = 0); every valid slot of the message is cleared; when the advisor clock is still inside
	// the window and +0x108 is the message: the speech stops (0x716950), the history's played byte is cleared, its slaps (+0xC) raised and the window (+0xE8) set to 0
	public bool Cancel( int message, ThemeParkIncCareer career, long turn, out bool stopped )
	{
		var cancelled = false;
		stopped = false;
		if ( StoredTutorial.Valid && table.Find( message )?.Group == TutorialGroup && StoredTutorial.Message == message )
			StoredTutorial = StoredTutorial with { Valid = false };
		for ( var index = 0; index < Slots; index++ )
		{
			if ( slots[index].Valid && slots[index].Message == message )
			{
				slots[index] = slots[index] with { Valid = false };
				cancelled = true;
			}
		}
		var playing = Clock( turn ) < unchecked( LastStart + LastWindow ) ? Playing : ThemeParkIncAdvisorTable.NoMessage;
		if ( playing != message )
			return cancelled;
		var history = career.AdviceHistory( message );
		career.SetAdviceHistory( message, history with { Played = false, Slaps = unchecked( history.Slaps + 1 ) } );
		LastWindow = 0;
		stopped = true;
		return true;
	}

	/// <summary>Asks for a window's help message after the panel delay (the fields <c>0x503AD0</c> sets), which the update posts once the delay has passed.</summary>
	public void PostDelayed( int message, long turn )
	{
		(DelayedFromPanel, DelayStart, DelayedMessage) = (true, Clock( turn ), message);
	}

	/// <summary>The advice played since the last call, oldest first.</summary>
	public IReadOnlyList<ThemeParkIncAdvicePlay> TakePlayed()
	{
		var taken = played.ToArray();
		played.Clear();
		return taken;
	}

	/// <summary>
	/// The advisor thing's update (<c>0x718190</c>), once per world tick: a due delayed message is posted (and nothing else runs), else the intake, then the playback when the window has
	/// passed. <paramref name="speech"/> is the speech's duration for a response, 0 when it has none.
	/// </summary>
	// [BIN:TPI-EXE:0x718190 advisor update] +0x10D: before UIPanelDelay (0xC65244, unsigned compare) has passed the update goes to the playback; after it, +0x10D = 0, the message is posted with
	// its descriptor score and the update returns. Else the intake (0x718227) unless the animation word (0x5D0820) or the game object's +0x2F is set. The playback (0x71830E): the advisor clock
	// below start + window returns; +0x104 set is cleared and returns; the selected slot's score (an invalid slot -1) must exceed MinScoreForConsideration
	public void Update( long turn, ThemeParkIncCareer career, ThemeParkIncAdviceContext context, Func<int, uint> speech )
	{
		var now = Clock( turn );
		if ( DelayedFromPanel )
		{
			if ( unchecked( now - DelayStart ) >= unchecked( (uint)settings.PanelDelay ) )
			{
				DelayedFromPanel = false;
				Post( DelayedMessage, career, turn, context );
				return;
			}
		}
		else if ( context.AnimationThing == 0 && !context.ObjectFlag2F && ConditionScan )
			throw new NotSupportedException( "The Theme Park Inc condition scan is not ported." );
		if ( now < unchecked( LastStart + LastWindow ) )
			return;
		if ( SkipPlayback )
		{
			SkipPlayback = false;
			return;
		}
		var index = Select();
		if ( index < 0 || (slots[index].Valid ? slots[index].Score : -1) <= settings.MinimumScore )
			return;
		var message = slots[index].Message;
		var row = table.Find( message )!.Value;
		var history = career.AdviceHistory( message );
		var variant = slots[index].Variant >= 0 ? slots[index].Variant : NextVariant( row, history, message );
		if ( (slots[index].ExplicitScore ? PlayExplicit( index, variant, speech ) : PlayDefault( index, variant, context, speech )) is not { } duration )
			return;
		LastStart = now;
		LastWindow = unchecked( duration + AfterSpeech );
		career.SetAdviceHistory( message, history with { WorldTime = unchecked( (uint)turn ), Variant = variant, Played = true, Plays = unchecked( history.Plays + 1 ) } );
		Playing = message;
	}

	// [BIN:TPI-EXE:0x7183B1 variant rule] a slot without a variant (+8 < 0) asks the rule (0x71BC20) with the history's last variant: 0 the next until the last, then the message id; 1 the next,
	// staying at the last; 2 the next; 3 a world-generator draw modulo the count (0x71847D, unreachable: every row has rule 2); another rule the message id; then a result below 0 or at or above
	// the count is 0
	private static int NextVariant( ThemeParkIncAdvisorDescriptor row, ThemeParkIncAdviceHistory history, int message )
	{
		var last = history.Variant;
		var variant = row.VariantRule switch
		{
			0 => last == row.Count - 1 ? message : last + 1,
			1 => last < row.Count - 1 ? last + 1 : last,
			2 => last + 1,
			3 => throw new NotSupportedException( "The world-generator variant rule 3 is in no Theme Park Inc descriptor row." ),
			_ => message,
		};
		return variant < 0 || variant >= row.Count ? 0 : variant;
	}

	private int ResponseOf( int message, int variant )
	{
		var row = table.Find( message );
		return row is not { } found ? NoResponse : variant >= found.Count ? found.Response : found.Response + variant;
	}

	// [BIN:TPI-EXE:0x71A9F0 default-score playback] the slot is consumed (+0x14 = 0), the descriptor scores again (0x71B870, which also writes the locate words) and a score below the minimum
	// fails; nonzero locate words replace the record's; the response is the base plus a variant below the count, else the base; 0x3F6 fails with no duration; the help button's message records
	// its response (0x9DDBE8) else 0x3F6 is stored; the speech (0x716230) gives the duration; the group gate timer is reset (0x73D3F0, P3); a tag that is not 0x97 is broadcast (kind 0x18)
	private uint? PlayDefault( int index, int variant, ThemeParkIncAdviceContext context, Func<int, uint> speech )
	{
		var slot = slots[index];
		slots[index] = slot with { Valid = false };
		if ( Score( slot.Message, context ) < settings.MinimumScore )
			return null;
		var response = ResponseOf( slot.Message, variant );
		if ( response == NoResponse )
			return null;
		if ( HelpMessage != ThemeParkIncAdvisorTable.NoMessage && HelpMessage == slot.Message )
			(HelpResponse, HelpMessage) = (response, ThemeParkIncAdvisorTable.NoMessage);
		else
			HelpResponse = NoResponse;
		played.Add( new ThemeParkIncAdvicePlay( slot.Message, response, slot.Locate, slot.LocateData ) );
		return speech( response );
	}

	// [BIN:TPI-EXE:0x71AD10 explicit-score playback] the slot is consumed, the response chosen as above (0x3F6 fails), the speech gives the duration and the tag is broadcast; no second score
	// and no gate timer
	private uint? PlayExplicit( int index, int variant, Func<int, uint> speech )
	{
		var slot = slots[index];
		slots[index] = slot with { Valid = false };
		var response = ResponseOf( slot.Message, variant );
		if ( response == NoResponse )
			return null;
		played.Add( new ThemeParkIncAdvicePlay( slot.Message, response, slot.Locate, slot.LocateData ) );
		return speech( response );
	}

	public ThemeParkIncAdvisorSnapshot Capture() => new( (ThemeParkIncAdviceRecord[])slots.Clone(), StoredTutorial, LastStart, LastWindow, Playing, SkipPlayback, FromHelpButton,
		DelayedFromPanel, DelayStart, DelayedMessage, TutorialOn, AdvisorPanelUp );

	/// <summary>Whether a snapshot can come from this model: eight slots whose valid records name a message of the table.</summary>
	public static bool IsValid( ThemeParkIncAdvisorSnapshot? state, ThemeParkIncAdvisorTable table ) =>
		state is { Slots.Length: Slots }
		&& state.Slots.All( slot => !slot.Valid || table.Find( slot.Message ) != null )
		&& (state.Playing == ThemeParkIncAdvisorTable.NoMessage || table.Find( state.Playing ) != null)
		&& (!state.DelayedFromPanel || table.Find( state.DelayedMessage ) != null);

	public void Restore( ThemeParkIncAdvisorSnapshot state )
	{
		if ( !IsValid( state, table ) )
			throw new InvalidDataException( "Invalid Theme Park Inc advisor snapshot." );
		state.Slots.CopyTo( slots, 0 );
		(StoredTutorial, LastStart, LastWindow, Playing, SkipPlayback, FromHelpButton) =
			(state.StoredTutorial, state.LastStart, state.LastWindow, state.Playing, state.SkipPlayback, state.FromHelpButton);
		(DelayedFromPanel, DelayStart, DelayedMessage, TutorialOn, AdvisorPanelUp) =
			(state.DelayedFromPanel, state.DelayStart, state.DelayedMessage, state.TutorialOn, state.AdvisorPanelUp);
		// the help button's words are statics of the executable, which a load in a new process starts from their initial values (D-5)
		(HelpMessage, HelpResponse) = (ThemeParkIncAdvisorTable.NoMessage, NoResponse);
		played.Clear();
	}
}
