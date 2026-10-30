namespace OpenTPW;

/// <summary>The vehicles, riders and lists of an original save's ride chunks, as the loader <c>0x543560</c> leaves them (TRACK-032).</summary>
public sealed partial class BumpController
{
	private int? modelBase;
	private int? modelCount;

	/// <summary>+0x14: the first vehicle model variant. The constructor copies the template's word and the ride chunk of a save overwrites it (<c>0x543638</c>).</summary>
	public int ModelBase => modelBase ?? Template.ModelBase;

	/// <summary>+0x18: the number of vehicle model variants (the same word's fate as <see cref="ModelBase"/>); a new vehicle's variant is its count modulo this plus the base (0x549DB0).</summary>
	public int ModelCount => modelCount ?? Template.ModelCount;

	/// <summary>Whether the ride holds the template's model words (a save holds them only when they differ).</summary>
	internal bool HasTemplateModelWords => ModelBase == Template.ModelBase && ModelCount == Template.ModelCount;

	internal void RestoreModelWords( int? savedBase, int? savedCount )
	{
		if ( savedBase is not { } first || savedCount is not { } count )
			return;
		if ( !TryRestoreModelWords( first, count ) )
			throw new InvalidDataException( $"A saved controller has the model words {first} and {count}, which are no range of vehicle models." );
	}

	/// <summary>
	/// Takes the model words of a ride chunk (<c>0x543638</c> stores them raw at +0x14 and +0x18). A count of 0 would divide by zero at the next launch (0x549DB0) and a range past the 256 variants names no model,
	/// so such words are refused (false) and the template's stay.
	/// </summary>
	internal bool TryRestoreModelWords( int first, int count )
	{
		if ( first < 0 || count < 1 || first + count > 256 )
			return false;
		modelBase = first == Template.ModelBase && count == Template.ModelCount ? null : first;
		modelCount = modelBase == null ? null : count;
		return true;
	}

	/// <summary>What the vehicle chunks of a ride made of its controller: how many vehicles, riders, joining and leaving guests it holds now.</summary>
	internal sealed record OriginalVehicleImport( int Vehicles, int Riders, int Joining, int Leaving );

	/// <summary>
	/// Puts the vehicles, riders and lists of a ride's chunks on a controller the way the loader leaves them, or returns the reason it cannot (nothing is then changed). The phase is the raw word of the ride chunk
	/// (a kart or bumper ride keeps racing or over); the model words are the chunk's; every vehicle takes the first free record of the module's pool in file order, finds the collision object it stands in (the first of the
	/// module's pool that holds its place, among the objects of the rides the loader has made by then, else the one that held it when it was saved) and aims at the buoy the key of its chunk names; the riders of a
	/// vehicle and the two lists come back in the order opposite to the file's (every node is pushed on the head again).
	/// </summary>
	// [BIN:TPW-EXE:0x543B9F vehicle load] a chunk 5 whose handle names the ride takes the first free record of the pool (0x549B50, flags 1, the aimed buoy and chase words -1), reads the 0xAC bytes of the record raw (the length of a short chunk is zero-filled), links it to the ride (+0x9C, ride +0xCC, ride +0x5C count), zeroes its rider list (+0x30) and the word +0x94 and a NaN height (+0xA0), finds its collision object (+0x98) from its place (+0x34, +0x38, 0x5493F0 over the objects of the pool that are not keep-out circles) or else from the fallback place the chunk adds, binds its model instances again, makes it the ride's first vehicle for flag 0x400000, resolves the waypoint key against the ride's chain (+0xA8: first node with the saved corner; owner 0 gives -1, no match 0) and zeroes the buoy countdown (+0x8C) of a chasing car (flag 4)
	// [BIN:TPW-EXE:0x543F24 rider load] chunk 9 pushes a node {guest, ride, head dummy of the saved number (0x44B220 on the vehicle's mesh), number} on the head of the ride's last vehicle's rider list and counts it in the ride (+0x60); it does not hang the head (no 0x44B410, no draw of the world generator): the vehicle load and the rider load draw nothing
	// [BIN:TPW-EXE:0x543560 TrackRides loader] the phase (+0x50) is read raw by chunk 3 and nothing restarts the ride: only the ride's own code ends a race or resets a ride without vehicles (0x54AE50)
	internal string? ImportOriginalVehicles( SavedTrackRide ride, Func<CollisionObject, bool> visible, out OriginalVehicleImport? result )
	{
		ArgumentNullException.ThrowIfNull( ride );
		ArgumentNullException.ThrowIfNull( visible );
		result = null;
		var header = ride.Header;
		var ownHandle = header.Identity.Value;
		var phase = unchecked((int)header.Field50);
		var first = unchecked((int)header.Field14);
		var count = unchecked((int)header.Field18);
		if ( ride.Refusal != null )
			return ride.Refusal;
		if ( phase is < PhaseClosed or > PhaseOver )
			return $"its phase {phase} is none of the ride's phases 0 to 3";
		if ( first < 0 || count < 1 || first + count > 256 )
			return $"its model words {first} and {count} are no range of vehicle models (a count of 0 divides by zero at the next launch, 0x549DB0)";
		if ( vehicles.Count != 0 || Pending.Count != 0 || Leaving.Count != 0 )
			return "the ride already holds vehicles or guests";
		if ( ride.Vehicles.Count > OriginalTrackVehicles.VehiclePool - module.LiveVehicles.Count() )
			return $"the vehicle pool of {OriginalTrackVehicles.VehiclePool} has no room for {ride.Vehicles.Count} more vehicles";
		var built = new List<BumpVehicle>();
		foreach ( var saved in ride.Vehicles )
		{
			var vehicle = BuildOriginalVehicle( saved, ownHandle, first, count, visible, out var refusal );
			if ( vehicle == null )
				return $"vehicle {built.Count}: {refusal}";
			built.Add( vehicle );
		}
		var seen = new HashSet<int>();
		var guests = ride.Guests.ToList();
		if ( guests.Any( guest => guest <= 0 || !seen.Add( guest ) ) )
			return "a guest is named twice, or is no guest, among its riders and lists";
		var engines = built.Count( vehicle => vehicle.Engine );
		if ( Template.IsBumper && engines > 0 && (engines != 1 || phase != PhaseRunning || built.Any( vehicle => vehicle.Engine && (vehicle.Flags & (BumpVehicle.First | BumpVehicle.Steerable)) != (BumpVehicle.First | BumpVehicle.Steerable) )) )
			return "a bumper car holds an engine voice that no running ride's first carrying car owns";
		if ( built.Any( vehicle => vehicle.Smoke && IsWater ) )
			return "a boat holds a smoke emitter, which only karts and bumper cars have";

		modelBase = first == Template.ModelBase && count == Template.ModelCount ? null : first;
		modelCount = modelBase == null ? null : count;
		foreach ( var vehicle in built )
		{
			vehicle.Slot = module.AllocateVehicle( vehicle );
			InsertSorted( vehicle );
			VehicleCount++;
			RiderCount += vehicle.Riders.Count;
		}
		// every node is pushed on the head of its list again, so each list comes back the other way round
		Leaving.AddRange( ride.Leaving.AsEnumerable().Reverse() );
		Pending.AddRange( ride.Joining.AsEnumerable().Reverse() );
		Phase = phase;
		result = new OriginalVehicleImport( built.Count, built.Sum( vehicle => vehicle.Riders.Count ), Pending.Count, Leaving.Count );
		return null;
	}

	/// <summary>The kart and boat fields of a vehicle record's words (the offsets are the record's bytes, 4 per word).</summary>
	private BumpVehicle? BuildOriginalVehicle( SavedTrackVehicle saved, uint ownHandle, int modelFirst, int modelLength, Func<CollisionObject, bool> visible, out string? refusal )
	{
		refusal = null;
		var w = saved.Words;
		if ( w.Length != 43 )
		{
			refusal = $"its record holds {w.Length} words, not 43";
			return null;
		}
		bool Small( int value, int limit ) => value >= -limit && value <= limit;
		var flags = w[0];
		// the saver writes the live vehicles of the pool only (flag 1)
		if ( (flags & BumpVehicle.Live) == 0 )
		{
			refusal = "its record is not live (flag 1)";
			return null;
		}
		var vehicle = new BumpVehicle
		{
			Flags = flags, Variant = w[1], Animation = w[4], EffectRequest = w[5], EffectLatch = w[6], X = w[13], Y = w[14], VX = w[15], VY = w[16], VX0 = w[17], VY0 = w[18], Speed = w[19],
			Heading = w[20], Radius = w[25], AccelBonus = w[26], TargetX = w[27], TargetY = w[28], JitterX = w[29], JitterY = w[30], ChaseTarget = w[32], Laps = w[34],
			BuoyCountdown = (flags & BumpVehicle.Chasing) != 0 ? 0 : w[35], Age = w[36], WallHits = w[41], WallWindow = w[42], Controller = this,
			// +0x2C is -1 while no emitter was made; +0x20 is the voice's handle: a boat holds one all its life and a bumper car the first carrying car does, while a kart's voice is the effect player's (OpenTPW raises it as events, not as a held voice)
			Smoke = w[11] != -1, Engine = IsWater || Template.IsBumper && w[8] != 0
		};
		// +0xA0 is a float: the loader clears a NaN
		var height = BitConverter.Int32BitsToSingle( w[40] );
		vehicle.Height = float.IsNaN( height ) ? 0f : height;
		if ( IsWater )
		{
			// the boat's wall spin (+0x58) and roll (+0x5C); +0x54 is only ever advanced by the roll and never read
			(vehicle.Spin, vehicle.Roll) = (w[22], w[23]);
		}
		else
		{
			vehicle.VisualHeading = w[21];
			vehicle.YawRate = w[23];
			if ( Template.IsKart )
				vehicle.YawDelta = w[24];
		}
		if ( vehicle.Variant < modelFirst || vehicle.Variant >= modelFirst + modelLength )
		{
			refusal = $"its model variant {vehicle.Variant} is outside the ride's models {modelFirst} to {modelFirst + modelLength - 1}";
			return null;
		}
		if ( vehicle.Heading is < 0 or >= BumpMath.Turn || !IsWater && vehicle.VisualHeading is < 0 or >= BumpMath.Turn )
		{
			refusal = $"its heading {vehicle.Heading} or drawn heading {vehicle.VisualHeading} is outside a turn";
			return null;
		}
		if ( vehicle.EffectRequest is < 0 or > 5 || vehicle.EffectLatch is < 0 or > 5 || vehicle.Animation is not (0 or 5 or 12) )
		{
			refusal = "its effect words or animation are none the effect player and the model's clips know";
			return null;
		}
		if ( !Small( vehicle.VX, 0x10000 ) || !Small( vehicle.VY, 0x10000 ) || !Small( vehicle.VX0, 0x10000 ) || !Small( vehicle.VY0, 0x10000 ) || !Small( vehicle.Speed, 0x100000 )
			|| !Small( vehicle.Spin, 0x100000 ) || !Small( vehicle.Roll, 0x100000 ) || !Small( vehicle.AccelBonus, 0x100000 ) || !Small( vehicle.JitterX, 0x10000 ) || !Small( vehicle.JitterY, 0x10000 )
			|| vehicle.Radius is < 0 or > 0x10000 || !Small( vehicle.Laps, 0x10000 ) || vehicle.Age < 0 || vehicle.WallHits < -1 || vehicle.WallWindow < 0 || !Small( vehicle.BuoyCountdown, 0x10000 )
			|| !Small( vehicle.YawRate, 0x10000 ) || !Small( vehicle.YawDelta, 0x10000 ) || Math.Abs( vehicle.Height ) >= 1e6f )
		{
			refusal = "a word of its record is out of the range a vehicle holds";
			return null;
		}
		// +0x80 is an index into the pool, taken as saved (the loader does not move it with the record)
		if ( vehicle.ChaseTarget is < -1 or >= BumpController.VehiclePool )
		{
			refusal = $"its chase target {vehicle.ChaseTarget} is no slot of the pool";
			return null;
		}
		// the collision object: from the place of the vehicle, else from the place the chunk adds
		var home = FirstContaining( vehicle.X, vehicle.Y, visible ) ?? FirstContaining( saved.FallbackX, saved.FallbackY, visible );
		if ( home == null )
		{
			refusal = "it stands in no collision object (the module tick of the original reads its object unchecked, 0x546C80)";
			return null;
		}
		SetObject( vehicle, home );
		// the aimed waypoint: the key's owner 0 means none; else the first node of the ride's chain with the saved corner (a chain of this ride only)
		if ( saved.WaypointOwner == 0 )
			vehicle.TargetBuoy = -1;
		else if ( saved.WaypointOwner != ownHandle )
		{
			refusal = $"it aims at a waypoint of ride {saved.WaypointOwner:X8}, not of its own ride";
			return null;
		}
		else
		{
			var index = buoys.FindIndex( buoy => buoy.X == saved.WaypointX && buoy.Y == saved.WaypointY );
			if ( index < 0 )
			{
				refusal = $"the waypoint it aims at ({saved.WaypointX}, {saved.WaypointY}) is not on the walked circuit's chain";
				return null;
			}
			vehicle.TargetBuoy = buoys[index].Index;
		}
		// the riders: the file lists the vehicle's list from its head, the loader pushes each on the head
		var riders = saved.Seats.AsEnumerable().Reverse().ToList();
		if ( riders.Any( rider => rider.Seat is < 1 or > 255 ) || riders.Select( rider => rider.Seat ).Distinct().Count() != riders.Count )
		{
			refusal = "a rider has a seat number outside 1 to 255, or two riders share one";
			return null;
		}
		vehicle.Riders.AddRange( riders.Select( rider => rider.Guest ) );
		vehicle.SeatNumbers = riders.Select( rider => rider.Seat ).ToArray();
		return vehicle;
	}

	// [BIN:TPW-EXE:0x543B3C object search] the object of a loaded vehicle is the first of the module's pool that is used, not a keep-out circle and holds the place; the pool then holds the objects of the rides made so far
	private CollisionObject? FirstContaining( int x, int y, Func<CollisionObject, bool> visible )
	{
		foreach ( var item in module.Objects )
		{
			if ( (item.Flags & CollisionObject.Used) != 0 && !item.IsKeepOut && visible( item ) && Contains( x, y, item ) )
				return item;
		}
		return null;
	}
}
