namespace OpenTPW;

public partial class Level
{
	/// <summary>The frame and restart smoke harness advance the same fixed-step simulation.</summary>
	internal void AdvanceSimulation( double elapsedSeconds ) => simulationClock.Advance( elapsedSeconds, deltaTime =>
	{
		SyncObjectEconomy();
		Guests?.Tick( deltaTime );
		PlacedRide?.Simulate( deltaTime );
		Objects.Simulate( deltaTime );
		Park?.FixedTick();
		if ( Park != null && Guests != null )
		{
			Weather ??= new ParkWeather( Park.Economy.Settings.Standard, Seed.WeatherStream, Guests.Grid.Cells.Width, Guests.Grid.Cells.Height );
			Weather.AdvanceTo( Park.Economy.Tick );
		}
		// The deferred gate-close retry follows the thing sweep (Mac 0x10536c).
		Gate?.Tick();
		StaffWorld?.Tick( deltaTime );
		UpdateTrackRendering( deltaTime );
		UpdateTourRendering( deltaTime );
	} );
}
