using System.Diagnostics;
using System.IO;

namespace OpenTPW;

/// <summary>
/// Connects the <see cref="AdvisorController"/> to a running level: raises the game events the
/// runtime has (level start: 10 then 0; park bankrupt/open/closed: 2/3/4), runs one controller
/// update per frame and says the chosen response through <see cref="Advisor.SayResponse"/>.
/// See docs/LIPS.md, "Automatic advice".
/// </summary>
internal sealed class AutomaticAdvisor : IDisposable
{
	// [BIN:TPW-EXE:0x00402D63 clock offset] the clock's offset word starts at 0 and is rewritten only by the savegame load (0x415140 → 0x402E80 → 0x403160: the saved advisor clock minus the live clock); pause is 0x4092A0 and resume 0x409300
	// [APPROX:ADVISOR-017] the clock starts at 0 for a new park, carries the saved word on an original-save import (AttachLevel), is sampled once per frame and stands still while the pause menu is open; the original stands still whenever 0x4092A0 pauses the timer: 0x46B74C in the main window procedure 0x46B600 (WM_ACTIVATEAPP deactivation, unless 0xF82884), 0x47F26C in the message box builder 0x47F020, 0x49EFB0 (load window), 0x49F280 (save window), 0x48C87E (the Escape menu 0x48C830, opened by VK_ESCAPE at 0x4A2933 and 0x4BE537: OpenTPW's pause menu) and 0x4A3A6C (the Game Options dialog 0x4A3A30, title 0x1D4D4, opened from that menu's items 0x48B977 and 0x48BF28) are identified, and 0x4092A0 acts only while the pause object's enable word (0x786BA4) is 1 and clears it — evidence needed: the screens of the callers 0x4A93D7 (0x4A9380, the level exit path: it sets 0x7CC150 to 3) and 0x5F0B7F (0x5F0B40, a modal with controls 0x990-0x995)
	private readonly PausableClock clock;
	// Tests substitute a controllable speech state for the presentation (see the internal constructor).
	private readonly Func<bool>? speechActive;
	// The slap's one-of-three choice: a local presentation stub (the original's CRT rand()), never a world or guest stream.
	private readonly Func<int> slapDraw;
	private readonly Action<uint> playSlap;
	private Advisor? presentation;
	private bool presentationFailed;
	private Level? level;
	private ParkEconomyRuntime? park;
	private GuestSimulation? guests;
	private StaffWorldSimulation? staffWorld;

	private AutomaticAdvisor( AdvisorController controller ) : this( controller, StopwatchSource() ) { }

	private static Func<long> StopwatchSource()
	{
		var watch = Stopwatch.StartNew();
		return () => watch.ElapsedMilliseconds;
	}

	/// <param name="milliseconds">The running clock the pause snapshots resume (tests supply a controllable one).</param>
	/// <param name="speechActive">The speech state (tests substitute a controllable one for the presentation); null reads the presentation.</param>
	/// <param name="slapDraw">The draw that picks the cut-off slap (tests supply a fixed one); null uses a local unseeded generator.</param>
	/// <param name="playSlap">Plays the slap's sound id (tests record it); null plays it through <see cref="GameAudio.PlaySpeechEffect"/>.</param>
	internal AutomaticAdvisor( AdvisorController controller, Func<long> milliseconds, Func<bool>? speechActive = null, Func<int>? slapDraw = null, Action<uint>? playSlap = null )
	{
		Controller = controller;
		clock = new PausableClock( milliseconds );
		this.speechActive = speechActive;
		var random = slapDraw == null ? new Random() : null;
		this.slapDraw = slapDraw ?? (() => random!.Next( 0x8000 ));
		this.playSlap = playSlap ?? (sound => GameAudio.PlaySpeechEffect( sound ));
	}

	public AdvisorController Controller { get; }
	/// <summary>The presentation, once a response has been said.</summary>
	public Advisor? Presentation => presentation;

	/// <summary>Loads <c>Advisor/Advisor.sam</c>; null (logged) when it is missing or unreadable.</summary>
	public static AutomaticAdvisor? TryCreate()
	{
		try
		{
			var balance = AdvisorBalance.Load();
			Log.Trace( $"Advisor scoring: {AdvisorBalance.Path}, minimum score {balance.MinimumScore}, {AdvisorTables.AllMessages.Count()} bound messages ({string.Join( ", ", AdvisorTables.Messages.Select( message => $"{message.MessageId}: {message.ScoreKey} {balance.ScoreOf( message.MessageId )}" ) ) + $", and the help messages at {balance.Scores.GetValueOrDefault( AdvisorTables.HelpScoreKey )} (F1 replay {balance.ContextHelpScore})"})." );
			return new AutomaticAdvisor( new AdvisorController( balance ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or KeyNotFoundException or UnauthorizedAccessException or OverflowException )
		{
			Log.Warning( $"Automatic advisor disabled: {exception.Message}" );
			return null;
		}
	}

	/// <summary>A level is attached (between <see cref="AttachLevel"/> and <see cref="DetachLevel"/>).</summary>
	public bool IsAttached => level != null;

	private uint AdvisorClock => clock.Milliseconds;
	// [BIN:STP-PPC:0x10121098 history setter] history saves the world's mGameTick (+0x1DA70C), which the park economy counts as park turns
	private uint GameTick => level?.Park is { } running ? unchecked((uint)running.Economy.Turn) : 0;
	// [BIN:STP-PPC:0x1013781C player selection] game type 2 = Instant Action, 0 otherwise (offline)
	private int GameType => level?.Park?.Economy.Mode == ParkGameMode.InstantAction ? 2 : 0;
	// [BIN:STP-PPC:0x10009038 eligibility check] byte +0x35 of the object at TOC −0x763C, which the options serializer (0x10129308 → 0x1012653C) writes as "TutorialOn" and the defaults (0x10125B7C) set to 1
	private static bool TutorialEnabled => GameOptions.Current.Tutorial;

	/// <summary>A level started: game event 10 then 0, as the main loop raises them.</summary>
	public void AttachLevel( Level started )
	{
		DetachLevel();
		level = started;
		park = started.Park;
		if ( park != null )
		{
			park.EventRaised += OnParkEvent;
			park.Economy.Objectives.TimeShort += OnChallengeTimeShort;
		}
		guests = started.Guests;
		if ( guests != null )
			guests.Pranked += OnPrank;
		staffWorld = started.StaffWorld;
		if ( staffWorld != null )
			staffWorld.MechanicOnTheWay += OnMechanicOnTheWay;
		// A park started from an original save takes its saved controller first; the main loop's events follow.
		if ( started.Park?.Import is { } import && started.OriginalPark?.Save?.Advisor is { } saved )
		{
			var report = Controller.ImportOriginal( saved );
			import.Report( report );
			Log.Trace( report );
		}
		// [BIN:TPW-EXE:0x00415270 clock load] behind the MESS marker check (0x41555A) the load reads the clock block (0x402E30): the scaled clock at +0x4C and the saved advisor clock, whose load sets the clock's offset so it reads the saved value from then on (0x402E80 -> 0x403160)
		if ( started.Park?.Import is not null && started.OriginalPark?.Save?.SavedAdvisorClockMilliseconds is { } savedClock )
			clock.RestoreMilliseconds( savedClock );
		// [BIN:STP-PPC:0x101C2108 main loop] CMsgEvent 10 (history reset of variant, played flag and slaps) followed by CMsgEvent 0 at 0x101C2174
		Raise( AdvisorGameEvent.ResetForEasyMode );
		Raise( AdvisorGameEvent.LevelStarted );
	}

	// [BIN:TPW-EXE:0x005996D0 stop path] "Kill advisor" (0x429D60) hides the model; the stop path has six callers, level exit among them
	public void DetachLevel()
	{
		if ( park != null )
		{
			park.EventRaised -= OnParkEvent;
			park.Economy.Objectives.TimeShort -= OnChallengeTimeShort;
		}
		if ( guests != null )
			guests.Pranked -= OnPrank;
		guests = null;
		if ( staffWorld != null )
			staffWorld.MechanicOnTheWay -= OnMechanicOnTheWay;
		staffWorld = null;
		// Help still pending or speaking belongs to the windows of this park, which close without reporting.
		if ( level != null )
			foreach ( var message in AdvisorUiHelp.AllWindowMessages )
				Controller.Withdraw( message, AdvisorClock );
		park = null;
		level = null;
		CaptionSink = null;
		presentation?.Kill();
	}

	private void OnParkEvent( ParkEvent item )
	{
		// [BIN:TPW-EXE:0x004D0370 bankruptcy event] at the bank's month-end message (type 0xC via 0x4D02D0): balance < 0, previous balance (+0x11C) < 0 and __alldiv(0x4F88B0(world+0x2A0, turn entered red +0x120), 0x1792F8648000 = 2.592e13) >= 6 (0x4D053B–0x4D054F) post event 2 every month while that holds, through 0x40F630(2); the turn entered red is set when the balance first goes below zero (0x4D041A–0x4D0432); it does not close the park
		// [BIN:TPW-EXE:0x004F88B0 park time] the term is (park turn − stamp) as an unsigned 32-bit count × the calendar rate (+0x1C of world+0x2A0, 15000) / 4 × 10^7, the park time in 100 ns units; 0x004D04D9–0x004D04F2 take 0 months unless the balance (+0xC) and mLastBalance (+0x11C) are both below 0 (ParkEconomy.MonthsSinceEnteringRed, raised as ParkEventKind.BankruptcyMessage)
		if ( item.Kind == ParkEventKind.BankruptcyMessage )
			Raise( AdvisorGameEvent.Bankrupted );
		// [BIN:TPW-EXE:0x00509031 staff messages] the staff manager's month routine sends the staff info message (type 0x17, kind and role) that 0x59AFC0 turns into the advice 33-57; the kind and role are those of the strike message, whose text the advisor's caption then posts
		if ( item.Kind == ParkEventKind.StaffStrikeMessage )
			PostEventAdvice( AdvisorTables.StaffMessage( new StrikeMessage( (StaffType)item.InfoId, (StrikeMessageKind)item.Amount ) ), "a staff strike message" );
		// the research completion message 0x11 (0x504630 sends it as the project reaches its target) reaches the lab and the advisor; a free item that followed one sends none
		if ( item.Kind == ParkEventKind.ItemResearched && item.InstanceId == 1 && park != null )
		{
			var category = item.Amount > 0 ? ResearchCategory.Upgrade : park.Economy.Catalog.TryGet( item.InfoId, out var info ) ? info.ResearchCategory : ResearchCategory.Feature;
			PostEventAdvice( ResearchDoneMessage( category ), $"research of {item.Detail} ({category})" );
		}
		if ( item.Kind == ParkEventKind.ResearchGroupOpened && NewResearchGroupMessage( (ResearchCategory)item.Amount ) is { } groupAdvice )
			PostEventAdvice( groupAdvice, $"a new {(ResearchCategory)item.Amount} research group" );
		// [BIN:TPW-EXE:0x00503E8E all research done] 0x167 (167) is posted in Instant Action (the game-type word 0xFB3B7C is 2) without further test; in the full simulation only when the player's ledger has the flag +0xB9 of every park (0x5B0C40), which OpenTPW's ledger lacks
		if ( item.Kind == ParkEventKind.ResearchAllDone && GameType == 2 )
			PostEventAdvice( 167, "all research done" );
	}

	/// <summary>The advice of each research category's completion (0x59B060, message type 0x11: ride 93, add-on 94, shop 95, sideshow 96, feature 97).</summary>
	// [BIN:TPW-EXE:0x0059B0E6 research done] the category in the message's +0xC picks the advice: 0 queues 0x5D, 1 0x5F, 2 0x60, 3 0x61 and 4 0x5E; the project's category is the object's own, or 4 for an upgrade level
	internal static int ResearchDoneMessage( ResearchCategory category ) => category switch
	{
		ResearchCategory.Ride => 93,
		ResearchCategory.Shop => 95,
		ResearchCategory.Sideshow => 96,
		ResearchCategory.Feature => 97,
		_ => 94,
	};

	/// <summary>The advice of a new research group (0x503B80: ride 133, shop 134, sideshow 135, feature 136; none for the upgrades).</summary>
	// [BIN:TPW-EXE:0x00503C6E group advice] the switch on the category posts 0x85, 0x86, 0x87 or 0x88 through 0x59B590 and 0x59A940
	internal static int? NewResearchGroupMessage( ResearchCategory category ) => category switch
	{
		ResearchCategory.Ride => 133,
		ResearchCategory.Shop => 134,
		ResearchCategory.Sideshow => 135,
		ResearchCategory.Feature => 136,
		_ => null,
	};

	/// <summary>Advice posted by game code (the original builds the record through 0x59B590 with the descriptor's score and queues it through 0x59A940).</summary>
	internal void PostEventAdvice( int message, string source )
	{
		if ( level == null )
			return;
		var admission = Controller.Advise( message, GameTick, GameType, TutorialEnabled );
		Log.Trace( $"Advisor advice for {source}: message {message} {(admission is { } result ? $"{result.Eligibility}{(result.Stored ? $" in slot {result.Slot}" : result.Acknowledged ? ", queue full" : "")}" : "not posted (game mode)")}." );
	}

	/// <summary>A mechanic set off for a broken-down ride: advice 70 reaches the advisor's queue at once.</summary>
	// [BIN:TPW-EXE:0x004DA6C5 mechanic on the way] the mechanic update builds the advice record (0x0059B590) and queues it (0x0059A940) as the mechanic claims the ride; the record's arguments (his id, the ride's Info.Id) are not kept, as for every advice OpenTPW queues (GUEST-007)
	internal void OnMechanicOnTheWay( int rideId, int mechanicId )
	{
		if ( level == null )
			return;
		var admission = Controller.Advise( AdvisorTables.MechanicOnTheWayMessage, GameTick, GameType, TutorialEnabled );
		if ( admission is { Stored: true } )
			Log.Trace( $"Advisor mechanic message for ride {rideId} (mechanic {mechanicId}): advice {AdvisorTables.MechanicOnTheWayMessage} {admission.Value.Eligibility} in slot {admission.Value.Slot}." );
	}

	/// <summary>
	/// A running challenge's time is short (the warning of 0x4D21FD): the challenge message reaches the advisor at once, which queues advice 271 with the
	/// challenge score. The challenge system posts it on every checked turn while it holds; the queue admits one pending copy and the repeat interval holds the rest back.
	/// </summary>
	// [BIN:TPW-EXE:0x0059B060 challenge message receiver] the advisor queues the advice id in +0xC of a category-25 message (0x0059B540, 0x0059B590, 0x0059A940)
	internal void OnChallengeTimeShort( int challengeIndex )
	{
		if ( level == null )
			return;
		var admission = Controller.Advise( AdvisorTables.ChallengeTimeShortMessage, GameTick, GameType, TutorialEnabled );
		if ( admission is { Stored: true } )
			Log.Trace( $"Advisor challenge warning for challenge {challengeIndex}: advice {AdvisorTables.ChallengeTimeShortMessage} {admission.Value.Eligibility} in slot {admission.Value.Slot}." );
	}

	/// <summary>
	/// A prankster pranked: the prank message reaches the advisor at once (the bus delivers it as it is sent), which queues one of the three prank
	/// lines by the prankster's id (<see cref="AdvisorTables.PrankAdvice"/>).
	/// </summary>
	// Approximation (GUEST-007, registered at GuestSimulation.Prank): the prank line is chosen by the prankster's thing id modulo 3 (the guest's id), and the advice record's prankster id and cell words (+0x0, +0x4, 0x0059B590) are not kept, as for every advice OpenTPW queues — evidence needed: what reads those two words of a queued record
	internal void OnPrank( Guest guest, GuestDecision.Prank prank )
	{
		if ( level == null )
			return;
		var message = AdvisorTables.PrankAdvice( guest.Id );
		var admission = Controller.Advise( message, GameTick, GameType, TutorialEnabled );
		Log.Trace( $"Advisor prank message from guest {guest.Id} ({prank}): advice {message} {(admission is { } result ? $"{result.Eligibility}{(result.Stored ? $" in slot {result.Slot}" : result.Acknowledged ? ", queue full" : "")}" : "not posted (game mode)")}." );
	}

	/// <summary>The open/close button or its shortcut was pressed: game event 3 (open) or 4 (close), whatever the park's state.</summary>
	// [BIN:TPW-EXE:0x00519EF0 open/close events] the door routine posts event 3 (0x51A033) or 4 (0x51A1C6) on every press of the open/close button (control 0x4F3B1) and of the Ctrl-O/Ctrl-C shortcuts (0x40C410), also when the park is already in that state; it is not the economy's transition
	public void OnParkDoorPressed( bool open )
	{
		if ( level == null )
			return;
		Raise( open ? AdvisorGameEvent.ParkNowOpen : AdvisorGameEvent.ParkNowClosed );
	}

	/// <summary>
	/// A HUD window opened: the interface mode changed (the help of the tools is withdrawn) and the window's help message, if it has
	/// one (<see cref="AdvisorUiHelp.Windows"/>), is posted with the help score, which beats the other pending advice. It waits for a
	/// speech that is running.
	/// </summary>
	// [BIN:TPW-EXE:0x00486B00 advise] the window builders post their message (the title-string identified windows of AdvisorUiHelp) through 0x486B00 as they open, which builds the record (0x59B590, score from the descriptor's callback) and queues it (0x59A940)
	public void OnWindowOpened( string window )
	{
		if ( level == null )
			return;
		// the object window builders (0x4AF980, 0x4B0E30, 0x4B2430) only build the window and post its help; whether the window creator 0x48CEA0 changes the interface mode is not read, so the tools' help stays
		if ( !window.StartsWith( ObjectInfoHelp.Prefix, StringComparison.Ordinal ) )
			OnModeChanged();
		if ( AdvisorUiHelp.MessageFor( window ) is { } message )
			PostHelp( message, $"window {window}" );
	}

	/// <summary>Posts help <paramref name="message"/> as a window builder does through 0x486B00, without an interface mode change.</summary>
	public void PostHelp( int message, string source = "the HUD" )
	{
		if ( level == null )
			return;
		var admission = Controller.Advise( message, GameTick, GameType, TutorialEnabled );
		Log.Trace( $"Advisor help for {source}: message {message} {(admission is { } result ? $"{result.Eligibility}{(result.Stored ? $" in slot {result.Slot}" : result.Acknowledged ? ", queue full" : "")}" : "not posted (game mode)")}." );
	}

	/// <summary>
	/// A HUD window closed: its help message is withdrawn. Pending help is dropped; help being spoken is cut off at once (the advisor
	/// vanishes within a frame, the message counts as not played and gains a slap).
	/// </summary>
	// [BIN:TPW-EXE:0x00486B40 unadvise] the window close handlers withdraw their message through 0x486B40 → 0x59AA70
	public void OnWindowClosed( string window )
	{
		foreach ( var message in AdvisorUiHelp.WithdrawnWith( window ) )
			WithdrawHelp( message, $"window {window}" );
	}

	/// <summary>Withdraws help <paramref name="message"/> as a close handler does through 0x486B40.</summary>
	public void WithdrawHelp( int message, string source = "the HUD" )
	{
		if ( level == null )
			return;
		var withdrawal = Controller.Withdraw( message, AdvisorClock );
		if ( withdrawal.SpeechCancelled )
			CutOffSpeech();
		Log.Trace( $"Advisor help for {source} withdrawn: message {message}, {withdrawal.PendingRemoved} pending dropped{(withdrawal.SpeechCancelled ? ", speech cut off" : "")}." );
	}

	/// <summary>The interface mode changed (a window or tool took over): the help of the tools and modes is withdrawn.</summary>
	// [BIN:TPW-EXE:0x0052F200 interface mode change] a changed mode withdraws the ten tool and mode help messages (AdvisorUiHelp.ModeHelpMessages)
	public void OnModeChanged()
	{
		if ( level != null && Controller.WithdrawModeHelp( AdvisorClock ) )
			CutOffSpeech();
	}

	/// <summary>The first of the three slap sounds of the global <c>cat_speech</c> category a cut-off plays.</summary>
	internal const uint FirstSlapSound = 0x27F;

	/// <summary>The slap a cut-off plays for a draw: <c>cat_speech</c> 0x27F, 0x280 or 0x281 for the draw modulo 3.</summary>
	// [BIN:TPW-EXE:0x005995A4 slap choice] rand() (0x67B5C0, the CRT generator) modulo 3 (idiv by 3) picks 0x27F, 0x280 or 0x281, each submitted flat to the global cat_speech bank [0x803A34] (0x51EC42) through 0x51BFC0 with handle 0 and position 0, 0, 0; the handle it returns is not kept
	internal static uint SlapSound( int draw ) => FirstSlapSound + (uint)(Math.Abs( draw % 3 ));

	/// <summary>
	/// A withdrawn message was being spoken: the advisor stops and vanishes (the original's slap path 0x5994E0 → "Kill advisor" 0x429D60),
	/// and while its speech sound was still playing a slap sounds (<see cref="SlapSound"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x005994E0 slap path] with the advisor active (0xF7966C) and a speech handle (0xF79680) it stops the speech (0x51C0E0), kills the advisor (0x429D60), releases the handle (0x51C2C0) and then plays the slap; without a handle it takes the branch 0x599620, which does the same without the slap. The update 0x599902 re-validates the handle each frame (0x51C270) and zeroes it once the speech sound has ended, so a cut-off after the speech ended (within the 1000 ms the reservation adds) is silent
	// OpenTPW plays the slap on the speech channel, as it plays the advisor's other cat_speech sounds
	internal void CutOffSpeech()
	{
		var speaking = IsSpeechActive;
		presentation?.Kill();
		if ( speaking )
			playSlap( SlapSound( slapDraw() ) );
	}

	/// <summary>
	/// F1 in a HUD window whose procedure passes keys to the default window procedure: the last tutorial advice is offered again
	/// (<see cref="AdvisorController.ReplayLastTutorial"/>).
	/// </summary>
	public void OnHelpKeyPressed()
	{
		if ( level == null )
			return;
		var admission = Controller.ReplayLastTutorial( GameTick, TutorialEnabled );
		Log.Trace( admission is { } result
			? $"Advisor F1: last tutorial advice {Controller.Queue.LastTutorial?.MessageId} {result.Eligibility}{(result.Stored ? $" in slot {result.Slot}" : "")}."
			: "Advisor F1: no tutorial advice to repeat." );
	}


	private void Raise( AdvisorGameEvent gameEvent )
	{
		foreach ( var (messageId, admission) in Controller.HandleGameEvent( gameEvent, GameTick, GameType, TutorialEnabled ) )
			Log.Trace( $"Advisor game event {(int)gameEvent} ({gameEvent}): advice {messageId} {admission.Eligibility}{(admission.Stored ? $" in slot {admission.Slot}" : admission.Acknowledged ? ", queue full" : "")}." );
	}

	/// <summary>One controller update; call once per frame while a level runs.</summary>
	public void Update( bool gamePaused = false )
	{
		clock.SetPaused( gamePaused );
		if ( level == null )
			return;
		// [BIN:STP-PPC:0x10006BB4 advisor response player] options byte +0x34 clear: the player returns 0 (0x10006BC0) before saying anything
		// [BIN:STP-PPC:0x1000BBF0 controller playback wrapper] and the wrapper still succeeds, so the advice is picked, consumed and recorded silently with span 0
		// [BIN:STP-PPC:0x10129308 options serializer] the object at TOC −0x763C is the options object; its +0x34 is saved as "AdvisorOn" (0x1012653C)
		if ( !GameOptions.Current.Advisor )
		{
			presentation?.Kill();
			Controller.Update( () => AdvisorClock, () => GameTick, responseId =>
			{
				PostCaption( responseId );
				return 0u;
			} );
			return;
		}
		presentation?.Update();
		Controller.Update( () => AdvisorClock, () => GameTick, Play );
	}

	/// <summary>
	/// Where the caption of a chosen response goes: the entry of <c>TAG_SYSTEM.str</c> the response's tag shows (the park HUD posts it to the message tube); null posts none.
	/// </summary>
	internal Action<int>? CaptionSink { get; set; }

	/// <summary>
	/// A response is chosen to be said: its caption goes to the message tube, whether or not the advisor is heard (the speech switched off, the clip missing).
	/// </summary>
	// [BIN:TPW-EXE:0x0059B620 advisor caption] the player of the next response (0x0059A550) asks the speech (0x00599050, 0 when the advisor is off), then posts a category-24 message with the response's tag (field +0x1C of its row, 0x005997B0; 0x17F without a caption) to the tag system (0x00509BF0), which shows the tag's text in the message tube
	internal void PostCaption( int responseId )
	{
		if ( AdvisorCaptionTable.For( responseId ) is { } caption )
			CaptionSink?.Invoke( caption.TextEntry );
	}

	private uint? Play( int responseId )
	{
		PostCaption( responseId );
		if ( presentationFailed || level == null )
			return null;
		try
		{
			presentation ??= new Advisor( () => AdvisorClock );
			if ( !presentation.SayResponse( responseId, level.LevelName, GameLanguage.IsSelected ? GameLanguage.Current : null ) )
			{
				Log.Warning( $"Advisor response {responseId} is not in {AdvisorResponses.RelativePath}." );
				return null;
			}
			// [BIN:TPW-EXE:0x005994AC response player return] the span is the sequence sum + 1000 + the length of clip 15 (a generated sequence already ends with clip 15, so it counts twice); the clip length is trailer word 2 × 1000 / 30, unsigned (0x474070), from the level advisor if loaded, else the global one (0x5989C0)
			// [BIN:TPW-EXE:0x00598BF0 fixed-sequence builder] a fixed-sequence response (60 of 610) returns its row's clip sum from table 0x76DC18 + 1000 + the length of clip 15 (a row that sums to 0 uses the generator)
			return presentation.ResponseSpanMilliseconds;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or InvalidOperationException or NotSupportedException or OverflowException )
		{
			Log.Warning( $"Advisor response {responseId} could not be played: {exception.Message}" );
			return null;
		}
		catch ( Exception exception ) when ( presentation == null )
		{
			presentationFailed = true;
			Log.Warning( $"Automatic advisor presentation unavailable: {exception.Message}" );
			return null;
		}
	}

	/// <summary>The advisor image rendered this frame, or null while the advisor is silent.</summary>
	public Veldrid.Texture? Image { get; private set; }

	/// <summary>
	/// Whether a response is being spoken right now. Active speech cannot be captured in this scope — the
	/// presentation (<see cref="Advisor"/>) is render/playback state without a snapshot seam — so a park
	/// save is refused while speech is active (<see cref="CaptureSnapshot"/> fails explicitly) instead of
	/// silently dropping the speech.
	/// </summary>
	public bool IsSpeechActive => speechActive?.Invoke() ?? presentation is { IsSpeaking: true };

	/// <summary>
	/// The automatic advisor's own-format state for a park save: the controller's queue (pending advice,
	/// history, action reservation) and the advisor clock's milliseconds the reservations are measured on.
	/// <see cref="SpeechWasActive"/> is always false from <see cref="CaptureSnapshot"/> (active speech is
	/// refused, not flagged): the flag exists so a persisted snapshot that claims active speech — which
	/// cannot resume — is rejected by validation instead of silently restoring a silent advisor. The
	/// process-local presentation-failure flag and the pause state are re-established, not saved.
	/// </summary>
	internal sealed record AutomaticAdvisorSnapshot( AdvisorController.AdvisorControllerSnapshot Controller, uint AdvisorClockMilliseconds, bool SpeechWasActive );

	/// <summary>
	/// The advisor's state for a park save; fails explicitly while a playback attempt is outstanding or a
	/// response is being spoken (the speech itself has no snapshot seam, so it cannot continue after a load).
	/// </summary>
	public AutomaticAdvisorSnapshot CaptureSnapshot()
	{
		if ( IsSpeechActive )
			throw new InvalidOperationException( "Cannot capture the automatic advisor while a response is being spoken: the presentation's speech has no snapshot seam, so it cannot continue after a load." );
		return new AutomaticAdvisorSnapshot( Controller.CaptureSnapshot(), AdvisorClock, SpeechWasActive: false );
	}

	/// <summary>
	/// Checks a snapshot without touching this advisor: a snapshot that claims active speech is rejected
	/// before the queue is validated (the controller validates its queue).
	/// </summary>
	public void ValidateSnapshot( AutomaticAdvisorSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		if ( snapshot.SpeechWasActive )
			throw new InvalidDataException( "Automatic advisor snapshot is inconsistent: it claims active speech, which cannot be restored." );
		Controller.ValidateSnapshot( snapshot.Controller );
	}

	/// <summary>
	/// Continues this advisor (freshly created and attached to the restored level) from a validated
	/// snapshot: the queue replaces the level-start advice and the advisor clock is rebased to its saved
	/// milliseconds exactly — also while the destination is paused under a load screen — so a speech's
	/// action reservation keeps holding until it would have ended. Active speech at capture time was
	/// refused; nothing of it resumes. Restore after <see cref="AttachLevel"/>, before the next
	/// <see cref="Update"/>.
	/// </summary>
	public void RestoreSnapshot( AutomaticAdvisorSnapshot snapshot )
	{
		ValidateSnapshot( snapshot );
		Controller.RestoreSnapshot( snapshot.Controller );
		// The rebase, not an offset on the source: the destination's own elapsed time (construction, pause
		// screens) must not shift the saved reservations.
		clock.RestoreMilliseconds( snapshot.AdvisorClockMilliseconds );
	}

	/// <summary>World pass: renders the shown advisor into its own image (see <see cref="Draw"/>).</summary>
	// [BIN:TPW-EXE:0x004735D0 animation sampler] the model is drawn until the last clip of the response's sequence ends (Advisor.DrawnSpanMilliseconds), where the sampler hides it; only a later response or Kill changes that
	public void Render() =>
		Image = presentation is { IsDrawn: true } ? presentation.RenderImage( Screen.PixelSize ) : null;

	/// <summary>
	/// Draws the advisor image at its <see cref="Advisor.BoxRectangle"/> after the park HUD: the original draws the
	/// advisor without the park camera, over the scene, in the bottom-right of the screen.
	/// </summary>
	public void Draw( UI.Original.UiContext context )
	{
		if ( Image == null )
			return;
		var (x, y, width, height) = Advisor.BoxRectangle( new Point2( context.Canvas.Width, context.Canvas.Height ) );
		context.Batch.AddQuad( UI.Original.UiTexture.Of( Image ), new UI.Original.UiRect( x, y, width, height ), System.Numerics.Vector2.Zero, System.Numerics.Vector2.One, Veldrid.RgbaByte.White );
	}

	public void Dispose()
	{
		DetachLevel();
		presentation?.Dispose();
		presentation = null;
	}
}
