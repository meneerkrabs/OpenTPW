namespace OpenTPW;

public partial class Level
{
	private ThemeParkIncLayoutCheckpoint? incLayout;
	private ParkCellMap? incLayoutCells;

	internal Level(ThemeParkIncLayoutCheckpoint checkpoint, OnlineFolders? onlineFolders = null)
		: this(checkpoint.Theme, true, null, onlineFolders, ParkStartKind.FullSimulation, new WorldSeed(checkpoint.Seed), null, null, overFrames: false, checkpoint: checkpoint) { }

	internal ThemeParkIncLayoutCheckpoint CaptureThemeParkIncLayout(DateTimeOffset savedUtc)
	{
		// the checkpoint holds the layout only: cash, the calendar and the loans start fresh in the new session, like guests and script progress
		if (!IsThemeParkIncSlice || IsReadOnlyVisit || Guests == null)
			throw new NotSupportedException("Only the writable experimental TPI slice can capture a layout checkpoint.");
		if (PlacedRide != null || Objects.Objects.Any(item => item.Runtime.Coaster != null))
			throw new NotSupportedException("Prototype and coaster layouts cannot be saved as a TPI layout checkpoint.");
		var checkpoint = new ThemeParkIncLayoutCheckpoint
		{
			Format = ThemeParkIncLayoutCheckpoint.FormatName, Version = ThemeParkIncLayoutCheckpoint.CurrentVersion,
			RestartPolicy = ThemeParkIncLayoutCheckpoint.FreshSession, Theme = LevelName, Seed = Seed.Value, SavedUtc = savedUtc,
			Layout = ParkLayoutSnapshot.CaptureSnapshot(Guests.Grid.Cells, Objects),
			AssetHashes = ParkResumeAssetManifest.Capture(Objects.Catalog, LevelName, false)
		};
		checkpoint.Validate(Objects.Catalog, checkpoint.AssetHashes, Guests.Grid.Cells.Width, Guests.Grid.Cells.Height);
		return checkpoint;
	}
}
