using OpenTPW.Hud;
using OpenTPW.Mods;

namespace OpenTPW;

internal sealed partial class GameFlow
{
	internal void SaveCurrentPark()
	{
		var level = Level ?? throw new InvalidOperationException( "There is no park to save." );
		// [EXT:online-coop-lockstep] a co-op guest's park is the host's: the pause menu offers no Save for it, and nothing writes it
		if ( level.IsCoopGuestPark )
			throw new InvalidOperationException( "A shared park is saved by its host." );
		if ( level.OriginalPark == null )
		{
			level.SaveSandbox();
			return;
		}
		// [BIN:TPW-EXE:0x48b6a0 pause menu commands] the Save command (command 2, case 0x48B7AD of the handler 0x48B6A0) closes the menu and opens the save screen (0x49F280) with no speech test, and the speech stop 0x51C0E0 has only the callers 0x5994E0 and 0x5996D0 (neither on a save), so a response being spoken goes on and is not part of the file
		var snapshot = level.CaptureResumeSnapshot( Advisor?.IsAttached == true ? Advisor : null, level.LevelName,
			savedUtc: DateTimeOffset.UtcNow, advisorMaySpeak: true );
		ParkResumeSaveFile.Save( SaveFileSystem.GetAbsolutePath( $"opentpw-{level.LevelName}-world.json" ), snapshot );
		Log.Trace( $"Saved {level.LevelName} park: {snapshot.Layout.Objects.Count} objects, {snapshot.Guests.Guests.Length} guests." );
	}

	/// <summary>Preflights and builds an isolated candidate before retiring the live park or lobby.</summary>
	/// <param name="restart">The reload of the restart snapshot itself (Restart Park), which keeps that snapshot instead of taking a new one.</param>
	/// <param name="extensionSwitch">[EXT:save-extension-switch] The extension switch the player confirmed for this load (<see cref="ResumeSwitched"/>); null loads strictly, as every replay and check does.</param>
	internal Level ResumePark( ParkResumeSnapshot snapshot, bool restart = false, ParkExtensionSwitch? extensionSwitch = null )
	{
		// a Theme Park Inc run refuses before a switch or a waiting extension choice is applied
		if ( GameEditions.IsThemeParkInc ) throw new NotSupportedException( "Theme Park Inc park saves are not supported." );
		Level Resume() => extensionSwitch == null ? ResumeStrict( snapshot, restart ) : ResumeSwitched( snapshot, restart, extensionSwitch );
		// [EXT:extensions-apply-at-load] extension choices made in the park being left apply to the park this load builds (a restart reloads the same park and keeps its set); a failed load keeps them waiting
		if ( restart || pendingExtensions is not { } next )
			return Resume();
		var before = OpenTpwExtensions.Active;
		var live = Level;
		ApplyPendingExtensions();
		try { return Resume(); }
		catch ( Exception error )
		{
			// only while the park that was running is still the live one: a park that replaced it runs with the extensions it was built with
			if ( ReferenceEquals( Level, live ) )
			{
				SwitchBack( before, error );
				pendingExtensions = next;
			}
			throw;
		}
	}

	private Level ResumeStrict( ParkResumeSnapshot snapshot, bool restart )
	{
		RequireProgressSaved();
		if ( GameEditions.IsThemeParkInc ) throw new NotSupportedException( "Theme Park Inc park saves are not supported." );
		var settings = BalanceSettings.Load( snapshot.Theme, snapshot.Economy.Easy );
		var catalog = ObjectCatalog.Load( snapshot.Theme, snapshot.Economy.Easy );
		var economyCatalog = EconomyObjectCatalog.Load( snapshot.Theme, snapshot.Economy.Easy );
		var assets = ParkResumeAssetManifest.Capture( catalog, settings );
		ParkResumeSaveFile.ValidateSnapshot( snapshot, catalog, settings, economyCatalog, assets );
		if ( snapshot.Gate == null || snapshot.Staff == null )
			throw new InvalidDataException( "The saved writable park is missing its gate or staff-world state." );
		var economy = ParkSaveFile.Restore( snapshot.Economy, settings, economyCatalog );
		AutomaticAdvisor? candidateAdvisor = null;
		try
		{
			if ( snapshot.Advisor != null )
			{
				if ( !AutomaticAdvisorEnabled ) throw new InvalidOperationException( "Enable the advisor to resume this park's saved advice." );
				candidateAdvisor = AutomaticAdvisor.TryCreate()
					?? throw new InvalidDataException( "The saved park's advisor settings are unavailable." );
				candidateAdvisor.ValidateSnapshot( snapshot.Advisor );
			}
			AbandonParkBuild();
			// [EXT:online-coop-snapshot-join] the player goes elsewhere: a join's build holds the one construction scene
			LeaveJoinBuild();
			using var scene = new EntityConstructionScope();
			var candidate = new Level( snapshot, economy, OnlineFolders ) { ShowDeveloperPanels = false };
			var candidateHud = CreateParkHud( candidate, EconomyParkStatus.ForLevel( candidate, PlayerKeys ) );
			if ( candidateAdvisor != null )
			{
				candidateAdvisor.AttachLevel( candidate );
				candidateAdvisor.CaptionSink = candidateHud.PostAdvisorCaption;
				candidateAdvisor.PlayerTickets = PlayerTickets;
				candidateAdvisor.RestoreSnapshot( snapshot.Advisor! );
			}
			RequireProgressSaved();
			scene.Detach();
			var retiredAdvisor = Advisor;
			TearDown( applyPendingLanguage: false );
			scene.Activate();
			Level = candidate;
			Hud = candidateHud;
			AttachGuestSounds( candidate );
			StartKind = snapshot.StartKind;
			Advisor = candidateAdvisor;
			candidateAdvisor = null;
			advisorUnavailable = false;
			retiredAdvisor?.Dispose();
			try
			{
				GameAudio.EnterPark( snapshot.Theme, snapshot.SoundSeed );
				// [BIN:TPW-EXE:0x550B30 restart save] Restart Park plays at the sound values the park started with again (GameFlow.restartSound)
				if ( restart && restartSound is { } startSound )
					Log.Trace( GameAudio.ApplySavedSound( startSound, GameOptions.Current ) );
				GameAudio.Events?.Reseed( snapshot.SoundSeed );
				// the restored scream handles had no chain in the candidate: they scream again now that the park's audio runs
				candidate.ResumeHeldScreams();
			}
			catch ( Exception error ) when ( error is IOException or InvalidDataException or InvalidOperationException or NotSupportedException )
			{
				Log.Warning( $"Park resumed; audio could not start: {error.Message}" );
			}
			AttachProgress( candidate );
			// a load takes the restart snapshot like the original's state 15; a restart's own reload does not
			if ( !restart ) TakeRestartSnapshot( candidate, assets, overFrames: true ); // the manifest was hashed for the validation above; hashing it again is most of the snapshot's cost
			Log.Trace( $"Resumed {snapshot.Theme} park: {candidate.Objects.Objects.Count} objects, {candidate.Guests!.Guests.Count} guests." );
			return candidate;
		}
		finally { candidateAdvisor?.Dispose(); }
	}
}
