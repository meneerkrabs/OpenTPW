namespace OpenTPW;

internal sealed partial class GameFlow
{
	private string IncLayoutPath(string theme) => SaveFileSystem.GetAbsolutePath($"opentpw-{theme}.tpilayout");

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
