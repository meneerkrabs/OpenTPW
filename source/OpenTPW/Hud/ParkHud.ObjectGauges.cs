using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>The object windows' excitement and reliability gauges, which follow the window's own slider values (<see cref="RideWindowGauges"/>).</summary>
public sealed partial class ParkHud
{
	/// <summary>The window's values while <paramref name="item"/> is the object the window shows, else null: the gauges then read the object's applied values.</summary>
	private ObjectWindowSettings? WindowValues( OriginalObject item ) =>
		InfoArmOpen && ReferenceEquals( item, settingsOwner ) ? pending : null;

	/// <summary>The ride window's excitement gauge (0x3e16) or the sideshow window's (0xa099), 0..1, null for an object without the row or without data.</summary>
	private float? ExcitementGauge( OriginalObject item, ObjectWindowSettings? window ) => ExcitementPercent( item, window ) is { } percent ? UiGauge.OfPercent( percent ) : null;

	/// <summary>The excitement byte of a ride or sideshow (<c>0x4e0560</c>) with the window's own values, or the object's applied ones without a window; null for another kind of object or without data.</summary>
	internal int? ExcitementPercent( OriginalObject item, ObjectWindowSettings? window )
	{
		if ( item.Entry.WhichUIType == (int)BuildCategory.Sideshows )
		{
			if ( window == null && LiveState( item ) is not { } state )
				return null;
			// [BIN:TPW-EXE:0x4b1a3c sideshow window refresh] the excitement gauge 0xa099 is 0x4e0560(window prize +0x28, window price +0x2c, window chance +0x24); the win byte is 100 less the chance of losing
			var values = window != null ? (window.Prize, window.Price, window.WinChance) : LiveState( item ) is { } live ? (live.CostOfGoods, live.Price, (100 - live.ChanceOfLosingPercent) & 0xFF) : default;
			return RideWindowGauges.SideshowExcitement( values.Item1, values.Item2, values.Item3 );
		}
		if ( item.Entry.WhichUIType != (int)BuildCategory.Rides )
			return null;
		// [BIN:TPW-EXE:0x4e0560 coaster branch] a type with track type 3 (+0x9c) has the guests' rating of its controller (0x43e0b0, 50 + ½ excitement), 0 without a controller
		if ( item.Entry.Settings.GetInt( "Bumper.WhichTrackType" ) == 3 )
			return (item.Runtime.Coaster?.GuestExcitement ?? 0) & 0xFF;
		var parameters = item.Runtime.Visitors.Parameters;
		var excitementLevel = item.Entry.Settings.GetInt( "UsageInfo.ExcitementLevel" );
		var baseLevel = item.Runtime.Visitors.ExcitementBase?.Invoke( excitementLevel ) ?? excitementLevel;
		var speed = window?.Ride.Speed ?? parameters.Speed;
		var duration = window?.Ride.Duration ?? parameters.Duration;
		return RideWindowGauges.RideExcitement( baseLevel, speed, parameters.InitSpeed, duration, parameters.InitDuration ?? parameters.Duration );
	}

	/// <summary>The ride window's reliability gauge (0x3e18), 0..1.</summary>
	private float? ReliabilityGauge( OriginalObject item, ObjectWindowSettings? window )
	{
		if ( item.Entry.WhichUIType != (int)BuildCategory.Rides )
			return null;
		var upgrade = item.Entry.Upgrades.FirstOrDefault( candidate => candidate.Level == RideLevel( item ) );
		if ( upgrade == null )
			return null;
		var parameters = item.Runtime.Visitors.Parameters;
		var settings = item.Entry.Settings;
		var speed = window?.Ride.Speed ?? parameters.Speed;
		var capacity = window?.Ride.Capacity ?? parameters.Capacity;
		// [EXT:cheats-ride-vehicles] a raised ride's capacity above the type's maximum reads as that maximum, as the economy's wear clamps its riders to it (ParkEconomy)
		var maxCapacity = settings.GetInt( "UsageInfo.MaxCapacity" );
		if ( item.Runtime.VehiclesRaised )
			capacity = Math.Min( capacity, maxCapacity );
		return UiGauge.OfPercent( RideWindowGauges.Reliability( settings.GetInt( "UsageInfo.MaxSpeed" ), maxCapacity,
			upgrade.GetInt( "RedLineSpeed" ), upgrade.GetInt( "RedLineCapacity" ), upgrade.GetInt( "WearRate" ), speed, capacity, item.Runtime.GetVariable( RideVariables.VAR_ONRIDE ) ) );
	}

	/// <summary>The average happiness byte of the guests on the cells within <paramref name="radius"/> of cell (<paramref name="x"/>, <paramref name="y"/>), 0 without guests.</summary>
	// [BIN:TPW-EXE:0x4cd880 cell id and edges] the argument is the cell id (y << 7) + x + 1 (decremented at 0x4cd88e); the high edges x + radius and y + radius are clamped to 0x7f by an unsigned test (0x4cd8a7 ja, 0x4cd8c1 jbe), the low edges are not clamped (the loops start at x - radius and y - radius, signed)
	// [BIN:TPW-EXE:0x4cd8fd low edge wrap] each visited cell is looked up by the 16-bit id ((y << 7) + x + 1) & 0xFFFF (0x4cd8f8-0x4cd901) in the cell table at [0x8023a0] (68-byte records, 0x4cd906-0x4cd912): a column x = -1 reads id y * 128, which is cell (127, y - 1) (and the guest whose own id that is, such as one placed at x = -1, is on that list), so a guest at the map's east edge counts for an object in column 0; an id of 0 (x = -1 on row 0) or from 0x4001 up (a row y = -1 gives 0xFF80 and up) lies outside the table and this port counts nothing there
	// [BIN:TPW-EXE:0x4cd880 low edge unreachable] only the two window refreshes call it (0x4b05f9, 0x4b1b03), with the object's own cell: an object stands on owned land, and the land's west and north fence lines (x = xmin, y = ymin, ParkLand.Price) are never owned, so column 0 and row 0 hold no object and the out-of-table reads cannot happen; the column wrap above is kept for the function itself
	// [BIN:TPW-EXE:0x4cd880 local happiness] the cells x - radius..min(0x7f, x + radius) by y - radius..min(0x7f, y + radius) are walked through their thing lists; every thing of type 1 (a guest) adds the low byte of its float +0x19c (the happiness, ftol) to a sum and 1 to a count, and the result is sum / count (integer division), 0 when nothing was found
	internal static int LocalHappiness( IEnumerable<(int X, int Y, float Happiness)> guests, int x, int y, int radius = 1 )
	{
		var highX = (uint)(x + radius) > 0x7f ? 0x7f : x + radius;
		var highY = (uint)(y + radius) > 0x7f ? 0x7f : y + radius;
		// the cell ids the walk reads: 1..16384 are the map's cells, row-major with 128 columns; a guest is on the list of its own id
		var ids = new HashSet<int>();
		for ( var cellX = x - radius; cellX <= highX; cellX++ )
			for ( var cellY = y - radius; cellY <= highY; cellY++ )
			{
				var id = ((cellY << 7) + cellX + 1) & 0xFFFF;
				if ( id is >= 1 and <= 128 * 128 )
					ids.Add( id );
			}
		int count = 0, sum = 0;
		foreach ( var (guestX, guestY, happiness) in guests )
		{
			if ( !ids.Contains( ((guestY << 7) + guestX + 1) & 0xFFFF ) )
				continue;
			count++;
			sum += RideBreakdownRules.GaugeByte( happiness );
		}
		return count == 0 ? 0 : sum / count;
	}

	/// <summary>The shop and sideshow windows' local happiness gauge: the guests around the object's cell.</summary>
	private float? LocalHappinessGauge( OriginalObject item )
	{
		if ( level.IsReadOnlyVisit || level.Guests is not { } guests )
			return null;
		// [BIN:TPW-EXE:0x4cd880] count actual cell-list members, without the separate IsOnParkCell predicate or a state filter.
		// [EXT:guest-cell-lists] retain the simulation's membership and synchronization policy, including its swim exclusion.
		// The existing placement anchor matches the inspected shipped objects (zero MapOffsetX/Y); custom nonzero offsets are not resolved here.
		return UiGauge.OfPercent( LocalHappiness( guests.CellListHappiness(), item.Placement.X, item.Placement.Y ) );
	}
}
