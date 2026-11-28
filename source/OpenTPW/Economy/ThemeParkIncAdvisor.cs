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

/// <summary>A tag the advisor's playback broadcast (kind 0x18, <c>0x71AC35</c>) with the record's locate words, which the tag system files as a Message Centre row.</summary>
public readonly record struct ThemeParkIncAdviceBroadcast( int Tag, ushort Locate, int LocateData );

/// <summary>
/// The advisor's saved state (docs/reverse/TPI-ADVISOR.md §6): the eight slots in queue order, the stored tutorial record, the playback window, the inputs and the advisor clock; since the
/// presentation (P2) also the AdvisorOn option and the presentation random stream's state, which a save from before it lacks. The speech itself is not saved: a load starts it silent
/// (<see cref="ThemeParkIncAdvisorPresentation.Reset"/>).
/// </summary>
public sealed record ThemeParkIncAdvisorSnapshot( ThemeParkIncAdviceRecord[] Slots, ThemeParkIncAdviceRecord StoredTutorial, uint LastStart, uint LastWindow, int Playing, bool SkipPlayback,
	bool FromHelpButton, bool DelayedFromPanel, uint DelayStart, int DelayedMessage, bool TutorialOn, bool AdvisorPanelUp, long ClockMicroseconds, bool AdvisorOn = true,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] uint? PresentationRandom = null );

/// <summary>
/// Theme Park Inc's advisor thing (model 11, built by the new-world init at <c>0x64836F</c>): the queue of eight advice records, the acceptance of a new record, the replacement on a full
/// queue, the cancel, and the update that plays the best record when the last one's window has passed. Its play history is the player's (<see cref="ThemeParkIncCareer"/>'s
/// <c>mHistory</c>), so it carries over from park to park as the original's does. The condition scan (<c>0x71C0B0</c>) is not ported yet: <see cref="ConditionScan"/> is off.
/// </summary>
public sealed class ThemeParkIncAdvisor
{
	private readonly ThemeParkIncAdvisorSettings settings;
	private readonly ThemeParkIncAdvisorTable table;
	private readonly ThemeParkIncAdvisorPresentation presentation;
	private readonly uint presentationSeed;
	private readonly List<ThemeParkIncAdviceBroadcast> broadcasts = new();
	private readonly Func<int, uint> speak;
	private readonly IThemeParkIncAdvisorSpeechData speechData;
	private ThemeParkIncAdviceContext speakContext;
	private bool framePending;

	/// <summary>The plays kept for a client that has not taken them (<see cref="TakePlayed"/>); older ones are dropped.</summary>
	public const int PlayedLimit = 64;

	/// <param name="speech">The speech data the presentation reads; none: no voice, no LIP and no clips.</param>
	/// <param name="presentationSeed">The presentation stream's seed (D-4: from the session seed).</param>
	internal ThemeParkIncAdvisor( ThemeParkIncAdvisorSettings settings, ThemeParkIncAdvisorTable table, IThemeParkIncAdvisorSpeechData? speech = null, uint presentationSeed = 1 )
	{
		this.settings = settings;
		this.table = table;
		this.presentationSeed = presentationSeed;
		speechData = speech ?? NoSpeech.Instance;
		presentation = new ThemeParkIncAdvisorPresentation( speechData, presentationSeed );
		speak = response => Speak( response, speakContext );
	}

	private sealed class NoSpeech : IThemeParkIncAdvisorSpeechData
	{
		public static readonly NoSpeech Instance = new();
		public uint? VoiceLength( ThemeParkIncAdviceResponse response ) => null;
		public IReadOnlyList<uint>? LipMarks( ThemeParkIncAdviceResponse response ) => null;
		public AdvisorClipDurations? ClipDurations => null;
	}

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
	/// <summary>The options' AdvisorOn byte (<c>0xA10218</c>), a player input; on by default (the options' reset, GMS <c>MEHT</c>). Off, the speech refuses all but the help button's response.</summary>
	public bool AdvisorOn { get; set; } = true;
	/// <summary>The presentation: the speech, the per-frame advisor, the stop and their random stream.</summary>
	internal ThemeParkIncAdvisorPresentation Presentation => presentation;

	/// <summary>Reads the speech data ahead of the first speech (the client calls it when the park starts).</summary>
	internal void PreloadSpeech() => speechData.Preload();
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

	/// <summary>The advisor clock in microseconds: the real time of the world ticks so far (<see cref="AdvanceClock"/>).</summary>
	public long ClockMicroseconds { get; private set; }

	/// <summary>The advisor clock in milliseconds, as the original's 32-bit clock reads.</summary>
	public uint Now => unchecked( (uint)(ClockMicroseconds / 1000) );

	/// <summary>
	/// The advisor clock before the last world tick moved it (<see cref="AdvanceClock"/>): the tick ran the presentation's frames from here to <see cref="ClockMicroseconds"/>. Not saved;
	/// a load or a new park starts it at the clock. Only the client reads it (<see cref="DisplayMilliseconds"/>).
	/// </summary>
	public long PreviousClockMicroseconds { get; private set; }

	/// <summary>
	/// The advisor clock a drawn frame shows, <paramref name="turnFraction"/> (0 to 1) of the way through the world tick that runs now: the tick moved the clock and ran the presentation's frames
	/// for its whole span at its start, so the client draws the advisor at the point of that span the park's fixed ticks have reached. Read-only: it changes nothing of the simulation.
	/// </summary>
	// [EXT:tpi-advisor-tick-clock] the original draws the frame its per-frame advisor (0x716ED0) has just run; OpenTPW runs the eight frames of a tick when the tick starts (AdvanceClock), so a
	// drawn frame shows the clips and the mouth at its own point of the tick's span, never ahead of the fixed clock
	public uint DisplayMilliseconds( double turnFraction )
	{
		var fraction = double.IsFinite( turnFraction ) ? Math.Clamp( turnFraction, 0, 1 ) : 0;
		var span = unchecked( ClockMicroseconds - PreviousClockMicroseconds );
		return Milliseconds( unchecked( PreviousClockMicroseconds + (long)(span * fraction) ) );
	}

	/// <summary>The real time one world tick takes at the park clock's <paramref name="rate"/>, in microseconds.</summary>
	public static long TickMicroseconds( double rate ) => (long)Math.Round( ParkCalendar.TurnMilliseconds * 1000.0 / rate, MidpointRounding.ToEven );

	/// <summary>Moves the advisor clock on by one world tick at the park clock's <paramref name="rate"/> (1 at normal speed).</summary>
	// [EXT:tpi-advisor-tick-clock] (user decision D-1, 2026-11-25) the original times the playback window and the cancel's playing test on the park clock's sub-clock (0x41B780 -> +0x50,
	// 0x41BD10 -> 0x41BB50 -> 0x41BA20: the raw millisecond deltas without the rate, minus the paused time), while the world ticks run on the rate-scaled main clock (0x6A5B4F -> 0x41B760 ->
	// 0x41BDE0 -> 0x41C150: delta × rate +0x20), one per eight 31 ms substeps (0x6A5B92, 0x6A5C18). So a tick spans 248 / rate real milliseconds: the advisor clock advances by that per world
	// tick, not with the frames, so a pause freezes it and a replay, a load and a co-op peer see the same windows. The original's frames (its three-ticks-a-frame cap and 2000 ms backlog cut,
	// 0x6A5B65) do not enter it
	public void AdvanceClock( double rate )
	{
		// the frame of the last substep of the previous tick, when no update ran it (EndTick)
		EndTick();
		var start = PreviousClockMicroseconds = ClockMicroseconds;
		var tick = TickMicroseconds( rate );
		// [EXT:tpi-advisor-tick-clock] the per-frame advisor (0x716ED0) runs once per 31 ms substep of the tick (0x6A5B92: one substep per frame at an ideal pacing), the eighth after the tick
		for ( var substep = 1; substep < Substeps; substep++ )
			presentation.Frame( Milliseconds( start + tick * substep / Substeps ) );
		ClockMicroseconds = unchecked( start + tick );
		framePending = true;
	}

	/// <summary>The substeps of one world tick (<c>0x6A5BF2</c>: the world ticks on every eighth).</summary>
	public const int Substeps = 8;

	/// <summary>Runs the frame of the tick's last substep (<c>0x6A5C49</c>, after the world tick) once; the economy calls it after the world pass.</summary>
	public void EndTick()
	{
		if ( !framePending )
			return;
		framePending = false;
		presentation.Frame( Now );
	}

	private static uint Milliseconds( long microseconds ) => unchecked( (uint)(microseconds / 1000) );

	/// <summary>
	/// The score of <paramref name="message"/> in the context (<c>0x71B7E0</c>): -1 for no row, the no-message id, or a class the context does not admit; else its row's callback, which
	/// <paramref name="career"/>'s history feeds where the callback reads it.
	/// </summary>
	// [BIN:TPI-EXE:0x71B7E0 advice score] the first row of the message, the class check 0x71BDC0, then the row's callback with this = scorer + row+0x14 (0 in every row)
	public int Score( int message, ThemeParkIncAdviceContext context, ThemeParkIncCareer? career = null )
	{
		if ( table.Find( message ) is not { } row || !Admits( row.Class, context ) )
			return -1;
		if ( row.ConstantScore is { } constant )
			return constant;
		// [BIN:TPI-EXE:0x71FE90 field score] 194 callbacks return one word of the scorer (mov eax, [ecx + offset]; ret), an Advisor.sam key: HelpMessage.Score (+0x49C) for the window help
		if ( row.Field is { } field )
			return settings.Word( field );
		return Callback( row, career );
	}

	/// <summary>
	/// The park state the ported score callbacks read besides the scorer and the history (<see cref="IThemeParkIncAdviceWorld"/>); the economy sets it. Null: a callback that reads it throws.
	/// </summary>
	internal IThemeParkIncAdviceWorld? World { get; set; }

	// the callbacks that read state besides the scorer; a callback that reads game state is the condition scan's (P3) or a later sender's, which nothing posts yet
	private int Callback( ThemeParkIncAdvisorDescriptor row, ThemeParkIncCareer? career )
	{
		switch ( row.Callback )
		{
			// [BIN:TPI-EXE:0x727410 0x1FA score] (thunk 0x404BD3) the advisor thing's history byte of 0x1FA (0x71B520: +8, played): set 0, else 10000
			case 0x404BD3:
				return Played( 0x1FA ) ? 0 : 10_000;
			// [BIN:TPI-EXE:0x728580 0x21D score] (thunk 0x407CB6) the history entry of 0x21D (0x71B4F0) +8, the played byte: set 0, else 10000
			case 0x407CB6:
				return Played( 0x21D ) ? 0 : 10_000;
			// [BIN:TPI-EXE:0x729020 0x26D score] (thunk 0x40C7F7) the history entry of 0x26D (0x71B4F0) +8: neg, sbb, and 0xFFFFD8F0, add 0x2710: set 0, else 10000
			case 0x40C7F7:
				return Played( 0x26D ) ? 0 : 10_000;
			// [BIN:TPI-EXE:0x726FA0 0x1C7 score] (thunk 0x40CA77) the history byte of 0x1C7 (0x71B520) set: 0; else the mission's requirements met (0x55A620 on 0xA61810): the scorer word +0x7B8
			// (0x723250) shifted left by one, else 0
			case 0x40CA77:
				return Played( 0x1C7 ) ? 0 : RequirementsMet() ? unchecked( settings.Word( RequirementsScoreOffset ) << 1 ) : 0;
			// [BIN:TPI-EXE:0x726F50 0x1E3 score] (thunk 0x406195) the same with the history byte of 0x1E3
			case 0x406195:
				return Played( 0x1E3 ) ? 0 : RequirementsMet() ? unchecked( settings.Word( RequirementsScoreOffset ) << 1 ) : 0;
			default:
				throw new NotSupportedException( $"Theme Park Inc advisor message {row.Message:X}: its score callback {row.Callback:X} is not ported." );
		}

		bool Played( int message ) => (career ?? throw new InvalidOperationException( $"Theme Park Inc advisor message {row.Message:X}: its score reads the career's history." ) )
			.AdviceHistory( message ).Played;

		bool RequirementsMet() => (World ?? throw new InvalidOperationException( $"Theme Park Inc advisor message {row.Message:X}: its score reads the mission's requirements." ) )
			.MissionRequirementsMet;
	}

	/// <summary>The scorer word the requirements-met advice <c>0x1C7</c> and <c>0x1E3</c> score twice (<c>0x723250</c>: <c>mov eax, [ecx + 0x7B8]</c>).</summary>
	public const int RequirementsScoreOffset = 0x7B8;

	/// <summary>Whether OpenTPW runs the score callback of <paramref name="row"/>: a constant, a scorer word or one of the ported callbacks.</summary>
	public static bool Scores( ThemeParkIncAdvisorDescriptor row ) => row.ConstantScore != null || row.Field != null || row.Callback is 0x404BD3 or 0x407CB6 or 0x40C7F7 or 0x40CA77 or 0x406195;

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
		=> Enqueue( new ThemeParkIncAdviceRecord( locate, locateData, variant, message, Score( message, context, career ), true, bypass, false ), career, turn );

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
	public bool Cancel( int message, ThemeParkIncCareer career, out bool stopped )
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
		var playing = Now < unchecked( LastStart + LastWindow ) ? Playing : ThemeParkIncAdvisorTable.NoMessage;
		if ( playing != message )
			return cancelled;
		presentation.Stop();
		var history = career.AdviceHistory( message );
		career.SetAdviceHistory( message, history with { Played = false, Slaps = unchecked( history.Slaps + 1 ) } );
		LastWindow = 0;
		stopped = true;
		return true;
	}

	/// <summary>Asks for a window's help message after the panel delay (the fields <c>0x503AD0</c> sets), which the update posts once the delay has passed.</summary>
	// [BIN:TPI-EXE:0x503B05 window help delay] the delay's start is GetTickCount (import 0xC6900C), the wall clock, and the update tests it with GetTickCount too (0x7181D2), so it runs on while the
	// game is paused. [EXT:tpi-advisor-tick-clock] (D-2: window state is simulation input) OpenTPW measures it on the advisor clock, which freezes while the park is paused
	public void PostDelayed( int message )
	{
		(DelayedFromPanel, DelayStart, DelayedMessage) = (true, Now, message);
	}

	/// <summary>
	/// A window's help button (<c>0x503A40</c>): the delayed help is dropped and the message posted with its descriptor's score while <see cref="FromHelpButton"/> is set, so it bypasses the
	/// group, once-only and slap checks and becomes the help button's message. True when the queue took it.
	/// </summary>
	// [BIN:TPI-EXE:0x503A40 help button] advisor +0x10D = 0, +0x10C = 1, 0x71A930(message, locate 0, 0, variant -1, bypass 0, score -1) -> 0x718820, then +0x10C = 0
	public bool HelpButton( int message, ThemeParkIncCareer career, long turn, ThemeParkIncAdviceContext context )
	{
		DelayedFromPanel = false;
		FromHelpButton = true;
		try
		{
			return Post( message, career, turn, context );
		}
		finally
		{
			FromHelpButton = false;
		}
	}

	/// <summary>A window's or a panel's help that the advisor says at once (<c>0x5039F0</c>): the message with its descriptor's score.</summary>
	// [BIN:TPI-EXE:0x5039F0 panel help] 0x71A930(message, locate 0, 0, variant -1, bypass 0, score -1) -> 0x718820; the delayed help is left as it is
	public bool PanelHelp( int message, ThemeParkIncCareer career, long turn, ThemeParkIncAdviceContext context ) => Post( message, career, turn, context );

	/// <summary>A window's help cancelled when the window goes (<c>0x503B40</c>): the delayed help is dropped and the message cancelled (<see cref="Cancel"/>).</summary>
	// [BIN:TPI-EXE:0x503B40 help cancel] advisor +0x10D = 0, then 0x718990(message)
	public bool CancelHelp( int message, ThemeParkIncCareer career )
	{
		DelayedFromPanel = false;
		return Cancel( message, career, out _ );
	}

	/// <summary>
	/// Says the stored tutorial advice again (<c>0x718AB0</c>, the F1 key): a valid stored record takes <c>HelpMessage.ContextHelpScore</c> as its given score and its bypass byte and is offered
	/// to the queue. False with no stored record.
	/// </summary>
	// [BIN:TPI-EXE:0x718AB0 repeat tutorial] +0x100 (the stored record's valid byte) clear: false; else the scorer is built when it is not (0xC64DC7), the record +0xEC gets the score [0xC656C0]
	// (scorer +0x4A0, HelpMessage.ContextHelpScore) at +0x10, +0x16 = 1 and +0x101 (its bypass byte +0x15) = 1, and goes to 0x718820 (which copies a tutorial-group record to +0xEC again); true
	public bool RepeatTutorial( ThemeParkIncCareer career, long turn )
	{
		if ( !StoredTutorial.Valid )
			return false;
		StoredTutorial = StoredTutorial with { Score = settings.Word( ContextHelpScoreOffset ), ExplicitScore = true, Bypass = true };
		Enqueue( StoredTutorial, career, turn );
		return true;
	}

	/// <summary>The scorer offset of <c>HelpMessage.ContextHelpScore</c> (<c>0xC656C0</c>).</summary>
	public const int ContextHelpScoreOffset = 0x4A0;

	/// <summary>The tags the playback broadcast since the last call (kind 0x18), oldest first; the tag system files them.</summary>
	public IReadOnlyList<ThemeParkIncAdviceBroadcast> TakeBroadcasts()
	{
		var taken = broadcasts.ToArray();
		broadcasts.Clear();
		return taken;
	}

	/// <summary>
	/// The advisor thing's update (<c>0x718190</c>) with the presentation's speech (<c>0x716230</c>): its return is the playback's duration.
	/// </summary>
	public void Update( long turn, ThemeParkIncCareer career, ThemeParkIncAdviceContext context )
	{
		speakContext = context;
		Update( turn, career, context, speak );
	}

	// [BIN:TPI-EXE:0x7162A3 help response] the speech lets the help button's response (0x9DDBE8 = the response, not 0x3F6) through with AdvisorOn off; a speech that is not refused sets it to 0x3F6
	private uint Speak( int response, ThemeParkIncAdviceContext context )
	{
		var help = HelpResponse != NoResponse && HelpResponse == response;
		var duration = presentation.Speak( response, Now, context.AnimationThing != 0, AdvisorOn, help );
		if ( duration != 0 )
			HelpResponse = NoResponse;
		return duration;
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
	// [BIN:TPI-EXE:0x718190 advisor update] +0x10D: before UIPanelDelay (0xC65244, unsigned compare against GetTickCount, see PostDelayed) has passed the update goes to the playback; after it, +0x10D = 0, the message is posted with
	// its descriptor score and the update returns. Else the intake (0x718227) unless the animation word (0x5D0820) or the game object's +0x2F is set. The playback (0x71830E): the advisor clock
	// below start + window returns; +0x104 set is cleared and returns; the selected slot's score (an invalid slot -1) must exceed MinScoreForConsideration
	internal void Update( long turn, ThemeParkIncCareer career, ThemeParkIncAdviceContext context, Func<int, uint> speech )
	{
		var now = Now;
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
		if ( (slots[index].ExplicitScore ? PlayExplicit( index, variant, speech ) : PlayDefault( index, variant, context, career, speech )) is not { } duration )
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
	// its response (0x9DDBE8) else 0x3F6 is stored; the speech (0x716230) gives the duration; the group gate timer is reset (0x73D3F0, P3); a tag that is not 0x97 is broadcast (kind 0x18).
	// The locate words the second score writes (0x71AA9E-0x71AAB3) are not modelled: the constant callbacks posted so far write none, and the callbacks that do come with P3 and P4
	private uint? PlayDefault( int index, int variant, ThemeParkIncAdviceContext context, ThemeParkIncCareer career, Func<int, uint> speech )
	{
		var slot = slots[index];
		slots[index] = slot with { Valid = false };
		if ( Score( slot.Message, context, career ) < settings.MinimumScore )
			return null;
		var response = ResponseOf( slot.Message, variant );
		if ( response == NoResponse )
			return null;
		if ( HelpMessage != ThemeParkIncAdvisorTable.NoMessage && HelpMessage == slot.Message )
			(HelpResponse, HelpMessage) = (response, ThemeParkIncAdvisorTable.NoMessage);
		else
			HelpResponse = NoResponse;
		Played( new ThemeParkIncAdvicePlay( slot.Message, response, slot.Locate, slot.LocateData ) );
		var duration = speech( response );
		// [BIN:TPI-EXE:0x71ABC9 caption broadcast] after the speech and the gate timer, a tag (0x716D10: the response's +0x1C, 0x97 for none) that is not 0x97 goes out as kind 0x18 with the
		// record's locate word and dword (0x435D30); the tag system receives it at once (0x436DD0 -> 0x637970 -> 0x635D80)
		if ( ThemeParkIncAdviceResponse.For( response ) is { Tag: not ThemeParkIncAdviceResponse.NoTag } row )
			broadcasts.Add( new ThemeParkIncAdviceBroadcast( row.Tag, slot.Locate, slot.LocateData ) );
		return duration;
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
		Played( new ThemeParkIncAdvicePlay( slot.Message, response, slot.Locate, slot.LocateData ) );
		var duration = speech( response );
		// the explicit path broadcasts the tag without the 0x97 test; the tag system has no record of 0x97 (0x637000), so it files nothing for it
		if ( ThemeParkIncAdviceResponse.For( response ) is { } row )
			broadcasts.Add( new ThemeParkIncAdviceBroadcast( row.Tag, slot.Locate, slot.LocateData ) );
		return duration;
	}

	private void Played( ThemeParkIncAdvicePlay play )
	{
		if ( played.Count == PlayedLimit )
			played.RemoveAt( 0 );
		played.Add( play );
	}

	public ThemeParkIncAdvisorSnapshot Capture() => new( (ThemeParkIncAdviceRecord[])slots.Clone(), StoredTutorial, LastStart, LastWindow, Playing, SkipPlayback, FromHelpButton,
		DelayedFromPanel, DelayStart, DelayedMessage, TutorialOn, AdvisorPanelUp, ClockMicroseconds, AdvisorOn, presentation.RandomState );

	/// <summary>
	/// Whether a snapshot can come from this model: eight slots whose valid records name a message of the table, and every record that takes its descriptor's score (and a delayed message,
	/// which does) on a message whose score callback OpenTPW runs, so the update never meets a callback it cannot run.
	/// </summary>
	public static bool IsValid( ThemeParkIncAdvisorSnapshot? state, ThemeParkIncAdvisorTable table ) =>
		state is { Slots.Length: Slots, ClockMicroseconds: >= 0 }
		&& state.Slots.Append( state.StoredTutorial ).All( slot => !slot.Valid || table.Find( slot.Message ) is { } row && (slot.ExplicitScore || Scores( row )) )
		&& (state.Playing == ThemeParkIncAdvisorTable.NoMessage || table.Find( state.Playing ) != null)
		&& (!state.DelayedFromPanel || table.Find( state.DelayedMessage ) is { } delayed && Scores( delayed ));

	public void Restore( ThemeParkIncAdvisorSnapshot state )
	{
		if ( !IsValid( state, table ) )
			throw new InvalidDataException( "Invalid Theme Park Inc advisor snapshot." );
		state.Slots.CopyTo( slots, 0 );
		(StoredTutorial, LastStart, LastWindow, Playing, SkipPlayback, FromHelpButton) =
			(state.StoredTutorial, state.LastStart, state.LastWindow, state.Playing, state.SkipPlayback, state.FromHelpButton);
		(DelayedFromPanel, DelayStart, DelayedMessage, TutorialOn, AdvisorPanelUp, ClockMicroseconds) =
			(state.DelayedFromPanel, state.DelayStart, state.DelayedMessage, state.TutorialOn, state.AdvisorPanelUp, state.ClockMicroseconds);
		PreviousClockMicroseconds = ClockMicroseconds;
		AdvisorOn = state.AdvisorOn;
		// [BIN:TPI-EXE:0x6A36DE load init] a load runs the advisor init 0x715850 before the park file: the speech starts silent. The presentation stream continues from its saved state; a save from
		// before the presentation (P1, which drew nothing) has none and starts it fresh from the session seed
		presentation.Reset( state.PresentationRandom ?? presentationSeed );
		// the help button's words are statics of the executable, which a load in a new process starts from their initial values (D-5)
		(HelpMessage, HelpResponse) = (ThemeParkIncAdvisorTable.NoMessage, NoResponse);
		played.Clear();
		broadcasts.Clear();
		framePending = false;
	}
}
