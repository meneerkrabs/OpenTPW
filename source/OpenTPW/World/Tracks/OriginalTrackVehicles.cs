namespace OpenTPW;

/// <summary>
/// One vehicle chunk (chunk 5) of an original save's TrackRides block as the loader reads it: the 43 words of the 0xAC-byte record, the fallback object place, the key of the waypoint the vehicle aims at and the riders the
/// chunks 9 that follow hang on it, in the order the file lists them.
/// </summary>
internal sealed record SavedTrackVehicle( int[] Words, int FallbackX, int FallbackY, uint WaypointOwner, int WaypointX, int WaypointY )
{
	/// <summary>The riders (guest thing id, seat number) as the file lists them, the vehicle's list from its head: the loader pushes every one on the head again, so the vehicle holds them the other way round.</summary>
	public List<(int Guest, int Seat)> Seats { get; } = new();
}

/// <summary>One ride's vehicle and list chunks, in the file's order: the vehicles with their riders, the joining list (chunk 8) and the leaving list (chunk 7).</summary>
internal sealed class SavedTrackRide
{
	public SavedTrackRide( SaveTrackRideHeader header, int ordinal )
	{
		Header = header;
		Ordinal = ordinal;
	}

	public SaveTrackRideHeader Header { get; }
	/// <summary>The ride's place among the rides of the file: the order the loader made them in, which decides the vehicle pool slots and which rides' collision objects exist when a vehicle looks for its own.</summary>
	public int Ordinal { get; }
	public List<SavedTrackVehicle> Vehicles { get; } = new();
	/// <summary>The joining list's guests in file order (head first); only the ride's phase allows the loader to take them (<c>0x54AA80</c>).</summary>
	public List<int> Joining { get; } = new();
	public List<int> Leaving { get; } = new();
	/// <summary>Joining guests the loader drops because the ride's saved phase is 0, and nodes it has no room for.</summary>
	public int DroppedJoining { get; set; }
	public int DroppedNodes { get; set; }
	/// <summary>Why the chunks of this ride do not read like the saver's, or null.</summary>
	public string? Refusal { get; set; }

	/// <summary>Every guest a vehicle or list of the ride names.</summary>
	public IEnumerable<int> Guests => Vehicles.SelectMany( vehicle => vehicle.Seats.Select( seat => seat.Guest ) ).Concat( Joining ).Concat( Leaving );
}

/// <summary>
/// The vehicle, rider and list chunks (5, 7, 8 and 9) of an original save's TrackRides block, read in the loader's order (<c>0x543560</c>): a chunk names its ride by handle, and a ride that the file's ride chunk
/// (3) has not made yet takes nothing. Pure: nothing here touches a controller.
/// </summary>
// [BIN:TPW-EXE:0x543560 TrackRides loader] the loader makes a ride at chunk 3, takes a vehicle for each chunk 5 whose handle names a ride (the first free record of the pool of 256, 0x549B50) and hangs the riders of chunk 9 on the ride's last vehicle (ride +0xCC); chunk 7 puts a guest on the leaving list (+0xC8, 0x54ABD0) and chunk 8 on the joining list (+0xC4, 0x54AA80, refused for a ride in phase 0), both at the head
// [BIN:TPW-EXE:0x544330 peep list] the nodes of all three lists come from one free list of 1024 records (0x5000 bytes of 0x14), shared by every ride: chunks 7 and 8 log "Peep List full" and drop their guest when it is empty, and chunk 9 takes its node without looking
internal static class OriginalTrackVehicles
{
	/// <summary>The peep list's size, shared by every ride's riders, joining and leaving guests (0x544360 allocates 0x5000 bytes of 0x14-byte nodes).</summary>
	public const int PeepNodes = 0x5000 / 0x14;
	/// <summary>The vehicle pool of the bump module (0x544360 allocates 0xAC00 bytes of 0xAC-byte records).</summary>
	public const int VehiclePool = 0xAC00 / 0xAC;

	/// <summary>The rides of the file in the order the loader made them, with their vehicles and lists; <see cref="Problem"/> is set when the file does not lay its chunks out the way the saver does.</summary>
	internal sealed record Result( IReadOnlyList<SavedTrackRide> Rides, int OrphanChunks, string? Problem );

	public static Result Read( SaveTrackRideBlock? block )
	{
		var rides = new Dictionary<uint, SavedTrackRide>();
		var order = new List<SavedTrackRide>();
		var closed = new HashSet<uint>();
		uint? current = null;
		var orphans = 0;
		var nodes = 0;
		string? problem = null;
		foreach ( var chunk in block?.Chunks ?? Array.Empty<SaveTrackRideChunk>() )
		{
			// the saver writes one ride after the other: its ride chunk, its sections, its vehicles with their riders, its lists and its reference-waypoint request
			var handle = chunk.Ride?.Identity.Value ?? chunk.Tile?.Identity.Value ?? chunk.RawRecord?.Identity.Value ?? chunk.ReferenceRequest?.Identity.Value
				?? chunk.Leaving?.Ride.Value ?? chunk.Joining?.Ride.Value ?? chunk.Seat?.Ride.Value;
			if ( handle is not { } named )
				continue;
			if ( current != named )
			{
				if ( current is { } left )
					closed.Add( left );
				current = named;
				if ( closed.Contains( named ) )
					problem ??= $"the chunks of ride {named:X8} are not in one run, as the saver writes them";
			}
			if ( chunk.Ride is { } header )
			{
				if ( rides.ContainsKey( named ) )
				{
					problem ??= $"ride {named:X8} has two ride chunks";
					continue;
				}
				var ride = new SavedTrackRide( header, order.Count );
				rides.Add( named, ride );
				order.Add( ride );
				continue;
			}
			if ( chunk.Tile != null || chunk.ReferenceRequest != null )
				continue;
			if ( !rides.TryGetValue( named, out var owner ) )
			{
				// a handle no ride answers to: the loader reads nothing of it (the comparison of the handle with the ride's own fails)
				orphans++;
				continue;
			}
			if ( chunk.RawRecord is { } record )
			{
				var bytes = record.Bytes;
				var words = new int[bytes.Length / 4];
				for ( var index = 0; index < words.Length; index++ )
					words[index] = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian( bytes[(index * 4)..] );
				owner.Vehicles.Add( new SavedTrackVehicle( words, record.FallbackTileCoordinate0, record.FallbackTileCoordinate1, record.Waypoint.Owner.Value, record.Waypoint.Coordinate0, record.Waypoint.Coordinate1 ) );
			}
			else if ( chunk.Seat is { } seat )
			{
				if ( owner.Vehicles.Count == 0 )
				{
					owner.Refusal ??= $"a rider chunk (9) of ride {named:X8} comes before any vehicle of it: the loader hangs it on no vehicle";
					continue;
				}
				if ( nodes >= PeepNodes )
				{
					owner.Refusal ??= $"the peep list of {PeepNodes} nodes is full at a rider chunk (9): the loader takes its node without looking";
					continue;
				}
				nodes++;
				owner.Vehicles[^1].Seats.Add( (unchecked((int)seat.Guest), unchecked((int)seat.Seat)) );
			}
			else if ( chunk.Leaving is { } leaving )
			{
				if ( nodes >= PeepNodes )
					owner.DroppedNodes++;
				else
				{
					nodes++;
					owner.Leaving.Add( unchecked((int)leaving.Guest) );
				}
			}
			else if ( chunk.Joining is { } joining )
			{
				// [BIN:TPW-EXE:0x54AA80 peep join] the join answers 0 without taking a node while the ride's phase (+0x50, which chunk 3 has set) is 0
				if ( owner.Header.Field50 == 0 )
					owner.DroppedJoining++;
				else if ( nodes >= PeepNodes )
					owner.DroppedNodes++;
				else
				{
					nodes++;
					owner.Joining.Add( unchecked((int)joining.Guest) );
				}
			}
		}
		return new Result( order, orphans, problem );
	}
}
