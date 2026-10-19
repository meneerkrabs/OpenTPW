namespace OpenTPW;

public partial class Level
{
	/// <summary>Runtime-only CPU ownership, independent of renderer/asset visibility and absent from gameplay saves and hashes.</summary>
	public StaffSpritePresentation? StaffSprites { get; private set; }

	private void InitializeStaffSprites()
	{
		StaffPlacement?.Reconcile();
		StaffWorld?.Synchronize();
		StaffSprites?.Reconstruct( Park?.Economy.Tick ?? simulationClock.TickCount );
	}

	private void ReplaceStaffSpriteSession()
	{
		if ( StaffSprites is not { } previous || Park == null ) return;
		var catalog = previous.Catalog;
		previous.Dispose();
		StaffSprites = new StaffSpritePresentation( catalog, () => Park.Economy.Staff.Members, Park.Economy.Tick, ready: false );
		StaffSprites.AttachRoster( Park.Economy.Staff );
		InitializeStaffSprites();
	}

	/// <summary>
	/// One outer-frame phase, including pause/zero elapsed: eligible UI partial copy, then source heading/pose publication.
	/// Headless harnesses call this explicitly after their simulation batch. Draw/repaint never calls it.
	/// </summary>
	// [APPROX:UI-092] Full host UI callbacks still precede Level.Update; open roots under rename remain eligible here. Native full-event/modal parity is unproven.
	public void PresentStaffSprites( Action? partialRefresh = null, float elapsedSeconds = 0 ) => StaffSprites?.Present( partialRefresh, elapsedSeconds );
}
