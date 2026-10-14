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
	// [APPROX:ADVISOR-017] the clock starts at 0 for a new park and an original-save import (the original's load sets its +0x50 offset to the saved value, 0x402E80 -> 0x403160), is sampled once per frame and stands still while the pause menu is open (the original stands still whenever 0x4092A0 pauses the timer: eight callers at 0x46B74C, 0x47F26C, 0x48C87E, 0x49EFBA, 0x49F29F, 0x4A3A6C, 0x4A93D7 and 0x5F0B7F, screens unidentified) — evidence needed: the saved clock field in the import and the UI owners of the eight callers
	private readonly PausableClock clock;
	// Tests substitute a controllable speech state for the presentation (see the internal constructor).
	private readonly Func<bool>? speechActive;
	private Advisor? presentation;
	private bool presentationFailed;
	private Level? level;
	private ParkEconomyRuntime? park;

	private AutomaticAdvisor( AdvisorController controller ) : this( controller, StopwatchSource() ) { }

	private static Func<long> StopwatchSource()
	{
		var watch = Stopwatch.StartNew();
		return () => watch.ElapsedMilliseconds;
	}

	/// <param name="milliseconds">The running clock the pause snapshots resume (tests supply a controllable one).</param>
	/// <param name="speechActive">The speech state (tests substitute a controllable one for the presentation); null reads the presentation.</param>
	internal AutomaticAdvisor( AdvisorController controller, Func<long> milliseconds, Func<bool>? speechActive = null )
	{
		Controller = controller;
		clock = new PausableClock( milliseconds );
		this.speechActive = speechActive;
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
			Log.Trace( $"Advisor scoring: {AdvisorBalance.Path}, minimum score {balance.MinimumScore}, {AdvisorTables.Messages.Length} bound messages ({string.Join( ", ", AdvisorTables.Messages.Select( message => $"{message.MessageId}: {message.ScoreKey} {balance.ScoreOf( message.MessageId )}" ) )})." );
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
			park.EventRaised += OnParkEvent;
		// A park started from an original save takes its saved controller first; the main loop's events follow.
		if ( started.Park?.Import is { } import && started.OriginalPark?.Save?.Advisor is { } saved )
		{
			var report = Controller.ImportOriginal( saved );
			import.Report( report );
			Log.Trace( report );
		}
		// [BIN:STP-PPC:0x101C2108 main loop] CMsgEvent 10 (history reset of variant, played flag and slaps) followed by CMsgEvent 0 at 0x101C2174
		Raise( AdvisorGameEvent.ResetForEasyMode );
		Raise( AdvisorGameEvent.LevelStarted );
	}

	// [BIN:TPW-EXE:0x005996D0 stop path] "Kill advisor" (0x429D60) hides the model; the stop path has six callers, level exit among them
	public void DetachLevel()
	{
		if ( park != null )
			park.EventRaised -= OnParkEvent;
		park = null;
		level = null;
		presentation?.Kill();
	}

	private void OnParkEvent( ParkEvent item )
	{
		// [BIN:TPW-EXE:0x004D0370 bankruptcy event] at the bank's month-end message (type 0xC via 0x4D02D0): balance < 0, previous balance (+0x11C) < 0 and __alldiv(0x4F88B0(world+0x2A0, turn entered red +0x120), 0x1792F8648000 = 2.592e13) >= 6 (0x4D053B–0x4D054F) post event 2 every month while that holds, through 0x40F630(2); the turn entered red is set when the balance first goes below zero (0x4D041A–0x4D0432); it does not close the park
		// [APPROX:ADVISOR-020] The economy's bankruptcy (six months in the red) stands in for the original's event 2 producer, which posts it every month the red-time condition holds — evidence needed: 0x4F88B0's term and an economy field for the turn entered red (ParkEconomy belongs to the economy work)
		if ( item.Kind == ParkEventKind.Bankrupt )
			Raise( AdvisorGameEvent.Bankrupted );
	}

	/// <summary>The open/close button or its shortcut was pressed: game event 3 (open) or 4 (close), whatever the park's state.</summary>
	// [BIN:TPW-EXE:0x00519EF0 open/close events] the door routine posts event 3 (0x51A033) or 4 (0x51A1C6) on every press of the open/close button (control 0x4F3B1) and of the Ctrl-O/Ctrl-C shortcuts (0x40C410), also when the park is already in that state; it is not the economy's transition
	public void OnParkDoorPressed( bool open )
	{
		if ( level == null )
			return;
		Raise( open ? AdvisorGameEvent.ParkNowOpen : AdvisorGameEvent.ParkNowClosed );
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
			Controller.Update( () => AdvisorClock, () => GameTick, _ => 0u );
			return;
		}
		presentation?.Update();
		Controller.Update( () => AdvisorClock, () => GameTick, Play );
	}

	private uint? Play( int responseId )
	{
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
