namespace OpenTPW;

/// <summary>
/// [EXT:umbrellas-tuning] A measurement aid for the browser performance harness (tools/web-perf scenario <c>umbrellaCost</c>, docs/BROWSER-PERFORMANCE.md): fills the park with extra guests, applies a weather quality and gives every guest
/// an open umbrella (or none), so the frame cost of many umbrellas can be measured. The harness calls it through <c>Program.PerfUmbrellaCrowd</c> (scenario <c>umbrellaCost</c>), which the page reaches only on a loopback address with <c>?perf=1</c>, and the native Debug flag <c>--debug-umbrellas-crowd</c> calls it too; nothing else in the game
/// calls it. Like the other smoke hooks it writes the guests' fields directly: the park it leaves is not a real run and is never saved.
/// </summary>
public static class UmbrellaPerf
{
	/// <summary>The guests in the park, the guests with an umbrella and whether it rains after <see cref="Stage"/>.</summary>
	public readonly record struct Result( int Guests, int Spawned, int Umbrellas, bool Rain );

	/// <summary>
	/// Adds <paramref name="crowd"/> guests on the park's walkable path cells (one each, then round again), applies the weather quality, then opens an umbrella for every guest
	/// (colour from its id) when <paramref name="umbrellas"/> is set, or closes them all (state 0) when it is not.
	/// </summary>
	public static Result Stage( GuestSimulation guests, ParkWeather? weather, int crowd, int quality, bool umbrellas )
	{
		// the park's walkable path cells in row order, filled round and round until the crowd is there
		var cells = guests.Grid.Cells;
		var paths = new List<(int X, int Y)>();
		for ( var y = 0; y < cells.Height; y++ )
			for ( var x = 0; x < cells.Width; x++ )
				if ( guests.Grid.IsWalkable( x, y ) )
					paths.Add( (x, y) );
		var spawned = 0;
		while ( paths.Count > 0 && spawned < crowd )
		{
			var (x, y) = paths[spawned % paths.Count];
			guests.SpawnInPark( x, y );
			spawned++;
		}
		weather?.ApplyQuality( quality );
		var open = 0;
		foreach ( var guest in guests.Guests )
		{
			guest.UmbrellaState = umbrellas ? UmbrellaRules.StateOpen : UmbrellaRules.StateNone;
			guest.UmbrellaType = umbrellas ? guest.Id % UmbrellaRules.Colours : 0;
			guest.UmbrellaDueTurn = 0;
			if ( umbrellas )
				open++;
		}
		return new Result( guests.Guests.Count, spawned, open, weather?.Rain == true );
	}
}
