using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW;

internal sealed partial class GameFlow
{
	/// <summary>
	/// The restart snapshot's file of a theme in the save folder, the place the original keeps restart.INTS: one per theme folder
	/// (the four private saves of a player hold one restart.INTS beside each theme's park saves, docs/TPWS-PAYLOAD.md).
	/// </summary>
	// the semantics are the original's; the file is OpenTPW's own park-resume format instead of INTS (no EXT label, by the user's decision)
	internal static string RestartSnapshotFileName( string theme ) => $"opentpw-{theme}-restart.json";

	/// <summary>Test seam: captures the restart snapshot instead of reading the live level.</summary>
	internal Func<Level, ParkResumeSnapshot>? RestartCaptureOverride { get; set; }

	/// <summary>Test seam: receives the snapshot a restart reloads instead of rebuilding the park.</summary>
	internal Action<ParkResumeSnapshot>? RestartResumeOverride { get; set; }

	private string RestartSnapshotPath( Level level ) => SaveFileSystem.GetAbsolutePath( RestartSnapshotFileName( level.LevelName ) );

	/// <summary>
	/// True for the parks that take a restart snapshot. The original's state 15 writes it for every game type except 1, the online
	/// visit (the word 0xFB3B7C, the same GameType the visit paths set through 0x550CA0); a sandbox, the generic viewer and
	/// the Theme Park Inc slice are not original parks.
	/// </summary>
	// [BIN:TPW-EXE:0x54f3e6 restart snapshot game type] state 15 compares the GameType word 0xFB3B7C with 1 (cmp ecx, ebx at 0x54f3ee, sete) and calls the snapshot writer 0x550B30 only when it differs, then moves to state 10 (0x54f3fc)
	internal static bool TakesRestartSnapshot( Level level ) => GameEditions.Current == GameEdition.ThemeParkWorld && !level.IsReadOnlyVisit &&
		!level.IsThemeParkIncSlice && level.OriginalPark != null && level.Guests != null && level.Park != null;

	/// <summary>
	/// Whether the pause menu offers Restart Park for this park: every park that takes a restart snapshot, Full Simulation included. The
	/// original's normal pause menu (0x48C150) lists the entry (text 10, command 3) for every game type except the online visit, whose
	/// menu is another branch without it (GameType 1), and nothing disables it by mode.
	/// </summary>
	// [BIN:TPW-EXE:0x48c150 pause menu restart entry] the builder adds the Restart Park line (text 10, command 3) in the branch taken whenever GameType 0xFB3B7C is not 1 (the visit's own menu has no such line) and the game-mode word +0x1da738 is not 4 (that menu has its own Restart line, command 0x17, whose confirm callback 0x48B620 sets the same request); no mode test guards it, and the command's confirm callback 0x48B5B0 stores request 1 (0x5508b0) like 0x48B620
	// the progress ledger of a Full Simulation park is player-wide in the original too (the ticket object 0xF7C58C is created while the park is set up (state 9) and freed at the park exit (state 12) and with the player slot, 0x5C7F40 and 0x5C8650, and the loader 0x414D40 and the teardown 0x5996D0 never reference it), so the restart states 13 and 14 leave it alone: the reload keeps it, as OpenTPW's ledger file survives a restart (RestartCurrentPark neither reads nor writes it)
	internal static bool RestartEligible( Level level ) => TakesRestartSnapshot( level ) && GameEditions.Capabilities.ParkSaves &&
		level.Park is { Start.Kind: ParkStartKind.InstantAction or ParkStartKind.FullSimulation or ParkStartKind.OriginalSaveReference } &&
		ObjectCatalog.Themes.Contains( level.LevelName, StringComparer.Ordinal );

	/// <summary>
	/// Captures the restart snapshot of a park that has just started or just been loaded: the original takes it in state 15, right after the
	/// level start 0x407E00 (which loads the quick save when 0x788160 is set) and never after a restart's own reload.
	/// </summary>
	// [BIN:TPW-EXE:0x54f3f7 restart snapshot] state 15 calls 0x550B30 once per park start or load: it builds <save>\restart.INTS from the folder global 0x878F18, the name "restart" (0x878A68) and the extension ".INTS" (0x877F38), and saves the park through 0x5AC630 with the save extension 0xF7ADF8 (writer 0x5ac780, the loader's twin)
	// [EXT:restart-snapshot-per-start] OpenTPW retakes the snapshot at every park start and load, so Restart Park returns to the park being played (user-approved 2026-10-17); the original opens restart.INTS for reading first (0x550c0f, mode 0x80000000) and saves only when that fails (0x550c14), and nothing deletes it between parks, so it keeps the first park of a theme folder as the restart point forever
	// [APPROX:SAVE-012] the snapshot is OpenTPW's park-resume file (opentpw-<theme>-restart.json), not the original's <save>\restart.INTS written through 0x550B30 and loaded by 0x414D40; an original-format writer exists (SaveContainerWriter, OriginalSavePayload, OriginalParkExport) but only exports the economy, the research lab and the object controls' researched bytes onto a template save, so a park with placed objects, paths, staff or guests cannot be snapshotted in the original's format yet
	/// <param name="overFrames">Serialize the detached copy on later updates; the live capture still finishes before this call returns.</param>
	internal void TakeRestartSnapshot( Level level, IReadOnlyDictionary<string, string>? verifiedAssets = null, bool overFrames = false )
	{
		// the sound values the park started with go with its snapshot (none for a resumed OpenTPW park, whose save has no Sound block)
		restartSound = TakesRestartSnapshot( level ) ? level.OriginalSaveSound : null;
		if ( !TakesRestartSnapshot( level ) )
			return;
		FlushRestartSnapshot();
		var started = System.Diagnostics.Stopwatch.GetTimestamp();
		try
		{
			using ( LoadProfile.Measure( "restart-snapshot" ) )
			{
				// [EXT:periodic-autosave] the manifest hashed for this snapshot is the one the periodic autosave reuses
				using ( LoadProfile.Measure( "restart-assets" ) )
					if ( RestartCaptureOverride == null ) verifiedAssets = level.AutosaveAssets( verifiedAssets );
				ParkResumeSnapshot snapshot;
				using ( LoadProfile.Measure( "restart-capture" ) )
					snapshot = RestartCaptureOverride != null ? RestartCaptureOverride( level ) :
						level.CaptureResumeSnapshot( Advisor?.IsAttached == true ? Advisor : null, level.LevelName, verifiedAssets );
				restartWrite = new RestartSnapshotWrite( RestartSnapshotPath( level ), snapshot );
				if ( !overFrames ) FlushRestartSnapshot();
			}
			Log.Trace( $"Restart snapshot of the {level.LevelName} park captured in {System.Diagnostics.Stopwatch.GetElapsedTime( started ).TotalMilliseconds:0} ms." );
		}
		catch ( Exception error ) when ( error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
			or NotSupportedException or ArgumentException )
		{
			// a park that cannot be captured must not leave an earlier park's snapshot behind to be restarted into
			Log.Warning( $"Restart snapshot not written ({error.Message}); Restart Park can start a new Instant Action park; other modes report that the snapshot is unavailable." );
			try { File.Delete( RestartSnapshotPath( level ) ); }
			catch ( Exception deleteError ) when ( deleteError is IOException or UnauthorizedAccessException ) { }
		}
	}

	private RestartSnapshotWrite? restartWrite;

	/// <summary>
	/// The Sound block of the original save the restart snapshot's park started from, applied again when Restart Park reloads the snapshot: the original's restart.INTS
	/// carries the sound-service values the park started with and its reload runs the Sound block loader again.
	/// </summary>
	// [BIN:TPW-EXE:0x550B30 restart save] state 15 writes <save>\restart.INTS through the payload writer 0x4164C0 (Sound block 0x51C350: the shadows at 0x803AA0 as the park started); the restart (state 14) reloads it through 0x414D40, whose Sound block loader 0x51C400 copies them back
	private SaveSoundData? restartSound;
	internal bool RestartSnapshotPending => restartWrite != null;

	/// <summary>One serialization slice per frame, after a capture on an earlier frame.</summary>
	internal void PumpRestartSnapshot( TimeSpan? budget = null )
	{
		if ( restartWrite == null ) return;
		try
		{
			if ( restartWrite.Run( budget ?? TimeSpan.FromMilliseconds( 2 ) ) ) restartWrite = null;
		}
		catch ( Exception error ) when ( error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
			or NotSupportedException or ArgumentException or System.Text.Json.JsonException )
		{
			restartWrite = null;
			// Retain the prior file and pending marker for diagnosis; Load must not use that prior park as the restart point.
			Log.Warning( $"Restart snapshot not committed ({error.Message}); Restart Park can start a new Instant Action park; other modes report that the snapshot is unavailable." );
		}
	}

	/// <summary>Restart, park replacement and orderly shutdown finish the detached job before changing its owner or save root.</summary>
	internal void FlushRestartSnapshot()
	{
		while ( restartWrite != null ) PumpRestartSnapshot( TimeSpan.MaxValue );
	}

	/// <summary>
	/// The pause menu's Restart Park (after its confirmation): the request code 1 of the dialog callback 0x48B620 (stored by 0x5508B0)
	/// takes state 10 to 13 and 14 (13 only advances; 14 does the work), which tears the park down (0x5996D0(1)) and reloads the restart snapshot (0x5AC5F0 through the
	/// loader 0x414D40). The reload returns to state 10 itself, so it takes no new snapshot.
	/// </summary>
	// [BIN:TPW-EXE:0x54fe23 restart request] request 1 sets state 13 (0x54fe23), which only advances to state 14; state 14 (the code at 0x55035a) runs 0x5996D0(1), loads <save>\restart through 0x5AC5F0 (name 0x878A68) and returns to state 10 (0x550371) with the request cleared (0x55037b); no state-15 write follows
	internal void RestartCurrentPark( Level level )
	{
		if ( BuildingPark || ProgressCaptured() || !ReferenceEquals( Level, level ) || !TakesRestartSnapshot( level ) )
			return;
		ParkResumeSnapshot? snapshot = null;
		try
		{
			FlushRestartSnapshot();
			snapshot = RestartSnapshotWrite.Load( RestartSnapshotPath( level ) );
			if ( !string.Equals( snapshot.Theme, level.LevelName, StringComparison.Ordinal ) )
				throw new InvalidDataException( "The restart snapshot belongs to another theme." );
			// a snapshot taken with the advisor attached cannot resume once the player has switched the advisor off
			if ( snapshot.Advisor != null && !AutomaticAdvisorEnabled )
				snapshot = snapshot with { Advisor = null };
			if ( RestartResumeOverride != null ) RestartResumeOverride( snapshot );
			else ResumePark( snapshot, restart: true );
		}
		catch ( Exception error ) when ( error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException
			or ArgumentException or InvalidOperationException or NotSupportedException )
		{
			Log.Warning( $"Restart snapshot cannot be loaded: {error.Message}" );
			// without a snapshot an Instant Action park still restarts from the level, as before
			if ( snapshot == null && level.Park is { Start.Kind: ParkStartKind.InstantAction } )
			{
				RequestParkStart( level.LevelName, GameMode.InstantAction );
				return;
			}
			var stack = Hud?.Stack ?? Menu?.Stack;
			stack?.Push( UiDialogs.Message( "loadFailed", () => error.Message,
				(() => Strings.Extra( OpenTpwText.Back ), () => stack.Pop()) ) );
		}
	}
}
