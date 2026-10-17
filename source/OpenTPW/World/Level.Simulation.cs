namespace OpenTPW;

public partial class Level
{
	/// <summary>The frame and restart smoke harness advance the same fixed-step simulation.</summary>
	internal void AdvanceSimulation( double elapsedSeconds ) => simulationClock.Advance( elapsedSeconds, deltaTime =>
	{
		SyncObjectEconomy();
		// [BIN:TPW-EXE:0x00516380 world update] a lost park (world state 4, ParkEconomy.IsBankrupt) runs no thing updates: the guests and the staff walks stop, the ride scripts (the RSSE tick) go on
		var lost = Park?.Economy.IsBankrupt == true;
		if ( !lost )
			Guests?.Tick( deltaTime );
		PlacedRide?.Simulate( deltaTime );
		Objects.Simulate( deltaTime );
		Park?.FixedTick();
		// [EXT:tpi-viewer] Theme Park Inc's weather is not traced (its states and keys differ from the Theme Park World weather this class ports), so a Theme Park Inc park has none
		if ( Park != null && Guests != null && !Park.Economy.Rules.ThemeParkInc )
		{
			Weather ??= new ParkWeather( Park.Economy.Settings.Standard, Seed.WeatherStream, Guests.Grid.Cells.Width, Guests.Grid.Cells.Height );
			Weather.AdvanceTo( Park.Economy.Tick );
		}
		// The deferred gate-close retry follows the thing sweep (Mac 0x10536c).
		Gate?.Tick();
		if ( !lost )
			StaffWorld?.Tick( deltaTime );
		UpdateTrackRendering( deltaTime );
		UpdateTourRendering( deltaTime );
		// [BIN:TPW-EXE:0x00516641 turn's end] the park turn ends by freeing the things that asked for their destruction, last request first (0x00516310, 0x00516200)
		Things.DestroyQueued();
	} );
}
