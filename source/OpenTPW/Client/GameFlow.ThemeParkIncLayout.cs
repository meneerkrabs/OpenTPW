namespace OpenTPW;

internal sealed partial class GameFlow
{
	private string IncLayoutPath(string theme) => SaveFileSystem.GetAbsolutePath($"opentpw-{theme}.tpilayout");
	internal static string IncScienceSavePath() => SaveFileSystem.GetAbsolutePath( "opentpw-tpi-science-world.json" );

	internal void SaveThemeParkIncScience()
	{
		var level = RequireThemeParkIncScience();
		var snapshot = level.CaptureResumeSnapshot( null, "Science", savedUtc: DateTimeOffset.UtcNow ) with
		{
			TpiHudSpeed = IncHud?.Speed ?? GameSpeed.Normal
		};
		ParkResumeSaveFile.Save( IncScienceSavePath(), snapshot );
	}

	internal void LoadThemeParkIncScience()
	{
		RequireThemeParkIncScience();
		ContinueThemeParkIncScience();
	}

	private void ContinueThemeParkIncScience()
	{
		var snapshot = ParkResumeSaveFile.Load( IncScienceSavePath() );
		if ( snapshot.Economy.Edition != GameEdition.ThemeParkInc || snapshot.Theme != "science" )
			throw new InvalidDataException( "The save is not for a Theme Park Inc Science park." );
		var settings = BalanceSettings.LoadThemeParkInc( snapshot.Theme );
		var catalog = ObjectCatalog.Load( snapshot.Theme );
		var economyCatalog = EconomyObjectCatalog.Load( snapshot.Theme, easy: false );
		var assets = ParkResumeAssetManifest.Capture( catalog, settings );
		ParkResumeSaveFile.ValidateSnapshot( snapshot, catalog, settings, economyCatalog, assets );
		var economy = ParkSaveFile.Restore( snapshot.Economy, settings, economyCatalog );
		using var scene = new EntityConstructionScope();
		var candidate = new Level( snapshot, economy, OnlineFolders ) { ShowDeveloperPanels = false };
		var candidateHud = CreateThemeParkIncHud( candidate, snapshot.TpiHudSpeed!.Value );
		candidate.SimulationTimeScale = candidateHud.TimeScale;
		scene.Detach();
		TearDown( applyPendingLanguage: false );
		scene.Activate();
		Level = ViewerLevel = candidate;
		IncHud = candidateHud;
		StartKind = snapshot.StartKind;
		AttachGuestSounds( candidate );
		try
		{
			GameAudio.EnterPark( snapshot.Theme, snapshot.SoundSeed );
			GameAudio.Events?.Reseed( snapshot.SoundSeed );
			candidate.ResumeHeldScreams();
		}
		catch ( Exception error ) when ( error is IOException or InvalidDataException or InvalidOperationException or NotSupportedException )
		{
			Log.Warning( $"Science park resumed; audio could not start: {error.Message}" );
		}
	}

	private Level RequireThemeParkIncScience() => Level is { IsThemeParkIncSlice: true, LevelName: "science", IsReadOnlyVisit: false, CoopRole: CoopRole.None } level
		? level : throw new NotSupportedException( "Only a solo Theme Park Inc Science park can be continued." );

	internal void SaveThemeParkIncLayout()
	{
		var level = Level ?? throw new InvalidOperationException("There is no experimental TPI world to checkpoint.");
		var checkpoint = level.CaptureThemeParkIncLayout(DateTimeOffset.UtcNow);
		ThemeParkIncLayoutCheckpoint.Save(IncLayoutPath(level.LevelName), checkpoint);
	}

	internal void LoadThemeParkIncLayout()
	{
		var live = Level ?? throw new InvalidOperationException("There is no experimental TPI world to replace.");
		if (!live.IsThemeParkIncSlice || live.IsReadOnlyVisit)
			throw new NotSupportedException("Only the writable experimental TPI slice can load a layout checkpoint.");
		var checkpoint = ThemeParkIncLayoutCheckpoint.Load(IncLayoutPath(live.LevelName));
		if (checkpoint.Theme != live.LevelName)
			throw new InvalidDataException("The layout checkpoint belongs to another theme.");
		// Catalog, manifest and terrain checks occur during candidate construction, before the old world is retired.
		// [EXT:online-coop-snapshot-join] the player goes elsewhere: a join's build holds the one construction scene
		LeaveJoinBuild();
		using var scene = new EntityConstructionScope();
		var candidate = new Level(checkpoint, OnlineFolders) { ShowDeveloperPanels = false };
		var candidateHud = CreateThemeParkIncHud(candidate);
		scene.Detach();
		TearDown(applyPendingLanguage: false);
		scene.Activate();
		Level = candidate;
		ViewerLevel = candidate;
		IncHud = candidateHud;
		StartKind = ParkStartKind.FullSimulation;
		try { GameAudio.EnterPark(checkpoint.Theme, candidate.Seed.SoundStream); }
		catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or NotSupportedException)
		{
			Log.Warning($"Layout loaded as a new experimental session; audio could not start: {error.Message}");
		}
		// The HUD that asked is retired: the new world's HUD tells the player.
		candidateHud.PostMessage("Layout loaded as a new experimental session.");
	}
}
