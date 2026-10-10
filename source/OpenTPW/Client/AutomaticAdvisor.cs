using System.Diagnostics;

namespace OpenTPW;

/// <summary>
/// Connects the <see cref="AdvisorController"/> to a running level: raises the game events the
/// runtime has (level start: 10 then 0; park bankrupt/open/closed: 2/3/4), runs one controller
/// update per frame and says the chosen response through <see cref="Advisor.SayResponse"/>.
/// See docs/LIPS.md, "Automatic advice".
/// </summary>
internal sealed class AutomaticAdvisor : IDisposable
{
	// [APPROX:ADVISOR-017] Wall-clock milliseconds since the automatic advisor was created, sampled once per frame; the original's pause-aware unscaled advisor clock is not reconstructed — evidence needed: the advisor clock subobject's offset/freeze/compensation and the controller's update cadence
	private readonly Stopwatch clock = Stopwatch.StartNew();
	private Advisor? presentation;
	private bool presentationFailed;
	private Level? level;
	private ParkEconomyRuntime? park;

	private AutomaticAdvisor( AdvisorController controller ) => Controller = controller;

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

	private uint AdvisorClock => unchecked((uint)clock.ElapsedMilliseconds);
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
		// [APPROX:ADVISOR-022] The pending advice and the message history are neither saved nor loaded: loading a park save keeps whatever this session's controller holds (pending advice and history of earlier parks, empty in a fresh session) — evidence needed: whether the original park save writes the controller's pending records (record serializer 0x1000BC10) and history, and where in the save file
		// [BIN:STP-PPC:0x101C2108 main loop] CMsgEvent 10 (history reset of variant, played flag and slaps) followed by CMsgEvent 0 at 0x101C2174
		Raise( AdvisorGameEvent.ResetForEasyMode );
		Raise( AdvisorGameEvent.LevelStarted );
	}

	// [APPROX:ADVISOR-018] The advisor is only drawn while a response plays and only inside a level; leaving the level stops its speech — evidence needed: the advisor's entry/exit animation and idle visibility
	public void DetachLevel()
	{
		if ( park != null )
			park.EventRaised -= OnParkEvent;
		park = null;
		level = null;
		presentation?.Silence();
	}

	private void OnParkEvent( ParkEvent item )
	{
		// [APPROX:ADVISOR-020] The economy's bankruptcy (six months in the red) and park open/close transitions stand in for the original CMsgEvent 2/3/4 producers (0x100CC464, 0x10108FD4, 0x10109118) — evidence needed: the producers' financial threshold and transition preconditions
		switch ( item.Kind )
		{
			case ParkEventKind.Bankrupt:
				Raise( AdvisorGameEvent.Bankrupted );
				break;
			case ParkEventKind.ParkOpened:
				Raise( AdvisorGameEvent.ParkNowOpen );
				break;
			case ParkEventKind.ParkClosed:
				Raise( AdvisorGameEvent.ParkNowClosed );
				break;
		}
	}

	private void Raise( AdvisorGameEvent gameEvent )
	{
		foreach ( var (messageId, admission) in Controller.HandleGameEvent( gameEvent, GameTick, GameType, TutorialEnabled ) )
			Log.Trace( $"Advisor game event {(int)gameEvent} ({gameEvent}): advice {messageId} {admission.Eligibility}{(admission.Stored ? $" in slot {admission.Slot}" : admission.Acknowledged ? ", queue full" : "")}." );
	}

	/// <summary>One controller update; call once per frame while a level runs.</summary>
	public void Update()
	{
		if ( level == null )
			return;
		// [BIN:STP-PPC:0x10006BB4 advisor response player] options byte +0x34 clear: the player returns 0 (0x10006BC0) before saying anything
		// [BIN:STP-PPC:0x1000BBF0 controller playback wrapper] and the wrapper still succeeds, so the advice is picked, consumed and recorded silently with span 0
		// [BIN:STP-PPC:0x10129308 options serializer] the object at TOC −0x763C is the options object; its +0x34 is saved as "AdvisorOn" (0x1012653C)
		if ( !GameOptions.Current.Advisor )
		{
			presentation?.Silence();
			Controller.Update( () => AdvisorClock, () => GameTick, _ => 0u );
			return;
		}
		Controller.Update( () => AdvisorClock, () => GameTick, Play );
	}

	private uint? Play( int responseId )
	{
		if ( presentationFailed || level == null )
			return null;
		try
		{
			presentation ??= new Advisor();
			if ( !presentation.SayResponse( responseId, level.LevelName, GameLanguage.IsSelected ? GameLanguage.Current : null ) )
			{
				Log.Warning( $"Advisor response {responseId} is not in {AdvisorResponses.RelativePath}." );
				return null;
			}
			// [APPROX:ADVISOR-016] Returned span = speech length + 200 + 300 (the animation budget from audio length) + 1000; the original returns sequence + ending-clip duration + 1000 — evidence needed: decoded advisor sequence and ending-clip durations
			return checked((uint)presentation.Duration.TotalMilliseconds + 200 + 300 + 1000);
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

	public void Render()
	{
		if ( presentation is { IsSpeaking: true } )
			presentation.Render();
	}

	public void Dispose()
	{
		DetachLevel();
		presentation?.Dispose();
		presentation = null;
	}
}
