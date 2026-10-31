namespace OpenTPW;

/// <summary>
/// The sound side of the game pause (tp.exe's pause object 0x786B68: <c>0x4092A0</c> pauses, <c>0x409300</c> resumes, <c>0x409350</c> toggles). The clock side is
/// <c>ParkHud.ClockPaused</c>; this class mirrors the three things the pause does to the sound system and the words it keeps between a pause and its resume.
/// </summary>
/// <remarks>
/// A game pause (a menu, a window, the P key) pauses the advisor's speech handle and sets the sound entry's paused word, which lifts the listener 10000 units
/// (<see cref="ListenerHeight"/>) while it is set, so every positioned sound is out of the library's hearing and the sounds that follow the listener (the land's ambient
/// rectangles, the flat voices) are not. The application's deactivation pauses the whole mixer instead and touches neither. The resume undoes what the last pause did:
/// the pause object keeps one byte (+0x20) for it, so a resume after a deactivation pause resumes the mixer only, whatever paused first. Everything here is gated the way
/// the original's sound entry points are (they return at once while the sound system is not running, 0x802BC8 / 0x802BD4), which is the caller's business
/// (<see cref="GameAudio"/>, <see cref="ListenerPlace"/>).
/// </remarks>
internal static class GamePauseSound
{
	/// <summary>The height the paused listener takes in place of its own.</summary>
	// [BIN:TPW-EXE:0x700b28 paused listener height] the float 10000.0 that 0x51C1D0 loads in place of its second argument while the paused word 0x803AD2 is not 0 (0x51C1E9-0x51C1F9)
	internal const float ListenerHeight = 10000f;

	private static bool deactivation;
	private static bool advisorPaused;

	/// <summary>The sound entry's paused word (0x803AD2): non-zero from a game pause to its resume, or to the start of the next level.</summary>
	// [BIN:TPW-EXE:0x51c1c0 paused word] stores the 16-bit argument: 1 by the pause (0x4092E8, 0x4093BF), 0 by the resume (0x409339, 0x409391) and by the level start 0x415140 (0x415254, the call after the level's sound reset 0x5996D0 / 0x51FD80)
	internal static bool ListenerRaised { get; private set; }

	/// <summary>The last pause was the application's deactivation (the pause object's byte +0x20), which the resume answers with a mixer resume.</summary>
	internal static bool LastPauseWasDeactivation => deactivation;

	/// <summary>The advisor's speech was paused by a game pause and its resume is still due (0xF797C4).</summary>
	internal static bool AdvisorSpeechPaused => advisorPaused;

	/// <summary>The pause (<c>0x4092A0</c>, and the pausing half of <c>0x409350</c>): <paramref name="byDeactivation"/> is its second argument.</summary>
	// [BIN:TPW-EXE:0x4092a0 game pause] the second argument is stored at +0x20 (0x4092D3); non-zero calls the mixer pause 0x51BCF0 and returns, zero calls the advisor pause 0x598960 and sets the paused word through 0x51C1C0 (0x4092E1-0x4092E8)
	// [BIN:TPW-EXE:0x46b74c focus loss] the window procedure's deactivation in a park passes 1 for it; the owners (the pause menu 0x48C87E, the map 0x5F0B7F, the postcard 0x4A93D7, the message boxes 0x47F26C and the three screens 0x49EFBA, 0x49F29F, 0x4A3A6C) and the P key (0x409350) pass 0
	internal static void Pause( bool byDeactivation )
	{
		deactivation = byDeactivation;
		if ( byDeactivation )
		{
			AudioMixer.Current?.Pause();
			return;
		}
		PauseAdvisorSpeech();
		ListenerRaised = true;
	}

	/// <summary>The resume (<c>0x409300</c>, and the resuming half of <c>0x409350</c>): it undoes what the last pause did.</summary>
	// [BIN:TPW-EXE:0x409300 game resume] reads the byte +0x20: non-zero calls the mixer resume 0x51BD30 (0x40932B), zero the advisor resume 0x598990 and the paused word 0 (0x409332-0x409339); the byte stays as it was
	// [BIN:TPW-EXE:0x54e67b front end entry] the front end's first frame resumes the pause object (0x409300) before anything else, so a park left from a pause menu does not carry its pause into the lobby
	internal static void Resume()
	{
		if ( deactivation )
		{
			AudioMixer.Current?.Resume();
			return;
		}
		ResumeAdvisorSpeech();
		ListenerRaised = false;
	}

	/// <summary>The start of a level clears the paused word (0x415140 -> 0x51C1C0(0)); the rest of the pause object is not touched.</summary>
	internal static void LevelStart() => ListenerRaised = false;

	/// <summary>The advisor's speech stopped: its handle is gone (0x5994E0 resumes a paused one first and clears the flag, 0x599523-0x59952B).</summary>
	// [BIN:TPW-EXE:0x5994e0 advisor stop] when the paused flag is set, the advisor option on and a handle held, the stop resumes the handle (0x51C0A0) and clears the flag before it releases the handle (0x51C2C0)
	internal static void AdvisorSpeechStopped()
	{
		if ( advisorPaused && GameOptions.Current.Advisor )
			advisorPaused = false;
	}

	/// <summary>The advisor pause <c>0x598960</c>: only with speech running, the advisor option on and a handle (0xF7966C, 0x78D90C, 0xF79680).</summary>
	// [BIN:TPW-EXE:0x598960 advisor pause] the three tests (0x598960-0x598979), then the pause-handle message 0x51C060 (record 0x700B9C: 0x6B5C50 -> 0x6B8A10 -> the channel's pause) and the flag 0xF797C4 = 1 (0x598984)
	private static void PauseAdvisorSpeech()
	{
		if ( !GameOptions.Current.Advisor || SpeechAudioPlayer.AdvisorHandle is not { } handle )
			return;
		handle.Pause();
		advisorPaused = true;
	}

	/// <summary>The advisor resume <c>0x598990</c>: the same three tests with the flag set, then the resume-handle message 0x51C0A0 (record 0x700BA0: 0x6B5C70 -> 0x6B8A70) and the flag back to 0.</summary>
	private static void ResumeAdvisorSpeech()
	{
		if ( !advisorPaused || !GameOptions.Current.Advisor || SpeechAudioPlayer.AdvisorHandle is not { } handle )
			return;
		handle.Resume();
		advisorPaused = false;
	}

	/// <summary>
	/// The listener's place as the sound entry hands it to the library: its own, or while the paused word is set the same place at <see cref="ListenerHeight"/>.
	/// <paramref name="soundRunning"/> is the original's gate: with the sound system off the entry point returns before anything is sent.
	/// </summary>
	// [BIN:TPW-EXE:0x51c1d0 listener] the second argument (the height) is replaced by the constant while the paused word is not 0; the place's other two floats, the front and the up vector go through unchanged (0x51C1E9-0x51C25D)
	internal static Vector3 ListenerPlace( Vector3 place, bool soundRunning ) =>
		soundRunning && ListenerRaised ? new Vector3( place.X, ListenerHeight, place.Z ) : place;

	private static bool? frontEndFocus;

	/// <summary>
	/// The front end's window focus, sampled once per frame: losing it pauses the whole mixer and getting it back restarts every channel, with nothing of the game pause. Automation without a
	/// window of its own (offscreen) is not a deactivation, as in a park (<c>ParkHud.ObserveApplicationFocus</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x46b70a front end focus] the window procedure's activation (0x46B6E0-0x46B70A) and deactivation (0x46B722-0x46B73C) answer [0xF82884] (non-zero only while the front end runs, 0x54E66C) with the mixer resume 0x51BD30 and the mixer pause 0x51BCF0 and leave the pause object alone; in a park the deactivation is the pause 0x4092A0(1, 1) instead (0x46B74C)
	// [EXT:offline-focus-pause] the host samples the focus once per frame, so a loss and a return between two samples are not seen
	internal static void ObserveFrontEndFocus( bool focused, bool offscreen )
	{
		if ( offscreen )
		{
			frontEndFocus = null;
			return;
		}
		if ( frontEndFocus == true && !focused )
			AudioMixer.Current?.Pause();
		else if ( frontEndFocus == false && focused )
			AudioMixer.Current?.Resume();
		frontEndFocus = focused;
	}

	/// <summary>Puts the pause object back to its start state (the tests; the process start).</summary>
	internal static void Reset()
	{
		deactivation = false;
		advisorPaused = false;
		ListenerRaised = false;
		frontEndFocus = null;
	}
}
