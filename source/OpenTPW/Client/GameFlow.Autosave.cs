using OpenTPW.FrontEnd;
using OpenTPW.Hud;

namespace OpenTPW;

internal sealed partial class GameFlow
{
	// [EXT:periodic-autosave] user-approved 2026-10-18; the original's per-theme autosave.TPWS is a checkpoint written when a park is left, not a timer (docs/PARK-SAVES.md, "Periodic autosave")
	private readonly PeriodicAutosave autosave = new();
	private WeakReference<Level>? autosaveLevel;
	/// <summary>Seam for tests: replaces the capture of the periodic autosave.</summary>
	internal Func<Level, ParkResumeSnapshot?>? AutosaveCaptureOverride { get; set; }
	internal PeriodicAutosave Autosave => autosave;

	/// <summary>
	/// Counts this frame's play time towards the periodic autosave and takes it when due. Runs after the park's update, so the capture sits between two
	/// simulation ticks; nothing here draws from a random stream or touches the simulation.
	/// </summary>
	private void UpdateAutosave( Level level, ParkHud hud )
	{
		if ( autosaveLevel == null || !autosaveLevel.TryGetTarget( out var known ) || !ReferenceEquals( known, level ) ) { autosaveLevel = new( level ); autosave.Reset(); }
		if ( !GameOptions.Current.Autosave || !AutomaticEligible( level ) ) return;
		RunAutosave( level, Time.Delta, parkBuild == null && hud.SimulationTimeScale > 0 );
	}

	internal AutosaveResult RunAutosave( Level level, double deltaSeconds, bool playing )
	{
		var files = SaveFileSystem;
		Action? validate = null;
		var result = autosave.Tick( deltaSeconds, playing, () => CaptureAutosave( level, out validate ),
			AutomaticThemeContinuation.Slot( files, level.LevelName, AutomaticSlotKind.Autosave ), Log.Warning, snapshot => { if ( validate != null ) validate(); else ParkResumeSaveFile.ValidateShape( snapshot ); } );
		if ( result == AutosaveResult.Saved )
			Log.Trace( $"Periodic autosave of the {level.LevelName} park taken: {autosave.LastBytes / 1024.0:0} KiB, {autosave.LastCaptureMilliseconds:0.0} ms capture on the game thread, {autosave.LastSerializeMilliseconds:0.0} ms checking and serializing over {autosave.LastSlices} frames (longest slice {autosave.LastLongestSliceMilliseconds:0.0} ms)." );
		return result;
	}

	private ParkResumeSnapshot? CaptureAutosave( Level level, out Action? validate )
	{
		validate = null;
		if ( AutosaveCaptureOverride != null ) return AutosaveCaptureOverride( level );
		// a staff member in hand or a hire being placed would be cancelled by the capture (the manual save does that at the pause menu); the autosave never changes what the player is doing
		if ( level.StaffPlacement is { IsActive: true } ) return null;
		// a response being spoken refuses the capture (AutomaticAdvisor.CaptureSnapshot); the next attempt comes soon
		var advisor = Advisor?.IsAttached == true ? Advisor : null;
		try { return level.CaptureResumeSnapshotUnchecked( advisor, level.LevelName, out validate, level.AutosaveAssets(), savedUtc: DateTimeOffset.UtcNow ); }
		catch ( InvalidDataException ) when ( advisor?.IsSpeechActive != true )
		{
			// the kept manifest may be stale (files changed under a running park): hash again once before calling the park uncapturable
			level.ForgetAutosaveAssets();
			return level.CaptureResumeSnapshotUnchecked( advisor, level.LevelName, out validate, level.AutosaveAssets(), savedUtc: DateTimeOffset.UtcNow );
		}
	}
}
