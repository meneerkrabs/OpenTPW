namespace OpenTPW;

/// <summary>
/// Live, derived coaster cell reservations. The node token corresponds to secondary +4, not a kart/water track mesh.
/// It is rebuilt from the track after load, never serialized as a pointer. Ordinary Clear retains station nodes;
/// controller removal disposes this binding and removes its station reservations too.
/// </summary>
internal sealed class CoasterCellMarkers : IDisposable
{
	internal const byte MarkerType = ParkCellTypes.TrackConnection;
	/// <summary>
	/// The marker bit of a station head's marker cell in the cell's flag word (<see cref="ParkCellMap.FlagsAt"/>): the node creation's parameter 0x10 gives the node 0x400 and its marker
	/// cell this bit. It shares its value with a pool wave height bit (<see cref="ParkCellFlags.PoolWaveHeightMask"/>), which only pool cells read.
	/// </summary>
	// [BIN:TPW-EXE:0x4343B3,0x434401] creation parameter 0x10 maps node 0x400 and marker 0x100
	internal const ushort StationHeadMark = 0x100;
	private readonly ParkCellMap cells;
	private readonly CoasterTrack track;
	private readonly ushort owner;
	private readonly Dictionary<(int X, int Y), Lease> leases = new();
	private readonly HashSet<(int X, int Y)> displaced = new();
	private bool disposed;
	private readonly (int X, int Y)[] stationEnds;
	private sealed record Lease( int Generation, CoasterNode? Token, CoasterNode[] Nodes )
	{
		internal int Count => Nodes.Length;
	}

	internal CoasterCellMarkers( ParkCellMap cells, CoasterTrack track, ushort owner, IEnumerable<(int X, int Y)>? stationEnds = null )
	{
		this.cells = cells;
		this.track = track;
		this.owner = owner;
		this.stationEnds = stationEnds?.Distinct().ToArray() ?? Array.Empty<(int X, int Y)>();
		Synchronize();
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] A live park's binding taken over from its cells without writing, as a join's build makes it (docs/COOP.md, "Joining a running session"): the bind
	/// above synchronizes at once and would claim wanted cells the host's binding never claimed (one another coaster's marker held when the pylon was placed, released since). Each wanted
	/// cell that is a marker with this owner word is held; the cells the host's binding did not hold (<paramref name="unheld"/>) stay unheld, the displaced ones displaced. The next change of
	/// the track synchronizes, as on the host.
	/// </summary>
	internal CoasterCellMarkers( ParkCellMap cells, CoasterTrack track, ushort owner, IEnumerable<(int X, int Y)>? stationEnds, IEnumerable<(int X, int Y, bool Displaced)> unheld )
	{
		this.cells = cells;
		this.track = track;
		this.owner = owner;
		this.stationEnds = stationEnds?.Distinct().ToArray() ?? Array.Empty<(int X, int Y)>();
		var desired = Desired();
		var skipped = new HashSet<(int X, int Y)>();
		foreach ( var (x, y, isDisplaced) in unheld )
		{
			skipped.Add( (x, y) );
			if ( isDisplaced && desired.ContainsKey( (x, y) ) )
				displaced.Add( (x, y) );
		}
		foreach ( var (cell, nodes) in desired )
			if ( !skipped.Contains( cell ) && cells.RawTypeAt( cell.X, cell.Y ) == MarkerType && cells.OwnerAt( cell.X, cell.Y ) == owner )
				leases[cell] = new( cells.CoasterGenerationAt( cell.X, cell.Y ), nodes.FirstOrDefault(), nodes );
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] For a join snapshot, read only: the wanted cells this binding does not hold although the join's adoption would (markers with its owner word and no lease),
	/// and the displaced cells with the cells of leases it no longer owns (its next synchronization displaces them), in cell order.
	/// </summary>
	internal IEnumerable<(int X, int Y, bool Displaced)> UnheldCells()
	{
		if ( disposed )
			yield break;
		foreach ( var cell in Desired().Keys.Order() )
		{
			var held = leases.TryGetValue( cell, out var lease );
			if ( displaced.Contains( cell ) || held && !Owns( cell, lease! ) )
				yield return (cell.X, cell.Y, true);
			else if ( !held && cells.RawTypeAt( cell.X, cell.Y ) == MarkerType && cells.OwnerAt( cell.X, cell.Y ) == owner )
				yield return (cell.X, cell.Y, false);
		}
	}

	/// <summary>The cells the pylons want, each with its nodes lowest in the stack first, and the station's end cells.</summary>
	private Dictionary<(int X, int Y), CoasterNode[]> Desired()
	{
		var desired = track.Nodes.Where( node => !node.IsPylonless ).GroupBy( node => (node.X, node.Y) ).ToDictionary( group => group.Key, group => group.OrderBy( node => node.StackIndex ).ToArray() );
		foreach ( var end in stationEnds ) desired.TryAdd( end, Array.Empty<CoasterNode>() );
		return desired;
	}

	/// <summary>The single-cell tool preview: first and last are both set by 0x53628C. Geometric validity is a separate test.</summary>
	// [BIN:TPW-EXE:0x5240D5,0x5242F1,0x53578E,0x535AFB] place15 accepts equal type21 even with another owner; move16 accepts equal type22, but a type21 requires its owner.
	// The host has no sticky multi-cell refusal globals. Each builder request is one independent cell; this models that zero-prior-failure context, not all native tool state.
	internal static bool AllowsPreview( ParkCellMap cells, int x, int y, ushort owner, bool moving )
	{
		if ( !cells.InBounds( x, y ) ) return false;
		var type = cells.RawTypeAt( x, y );
		var flags = cells.FlagsAt( x, y );
		// The marker100 success precedes unowned and secondary tests. Do not move it below them.
		if ( !moving && type == MarkerType && ((ushort)flags & StationHeadMark) != 0 && cells.OwnerAt( x, y ) == owner ) return true;
		if ( !AllowsOrdinaryGround( cells, x, y ) ) return false;
		if ( type == (moving ? 22 : MarkerType) ) return true;
		return type switch
		{
			0 => true,
			(byte)ParkCellType.Path => (flags & ParkCellFlags.NoModify) == 0,
			MarkerType => cells.OwnerAt( x, y ) == owner,
			_ => false
		};
	}

	/// <summary>Secondary-track and land checks after the early marker100 case. Also used before station mode4 validation.</summary>
	internal static bool AllowsOrdinaryGround( ParkCellMap cells, int x, int y )
	{
		if ( !cells.InBounds( x, y ) || (cells.FlagsAt( x, y ) & ParkCellFlags.Unowned) != 0 || cells.PoolBridges?.Reserves( x, y ) == true ) return false;
		var secondary = cells.Tracks.Get( x, y );
		if ( ParkCellTypes.IsTrackExtension( secondary.Type ) )
		{
			if ( secondary.Parent < 0 || secondary.Parent >= cells.Width * cells.Height ) return false;
			var parent = cells.Tracks.CellOf( secondary.Parent );
			secondary = cells.Tracks.Get( parent.X, parent.Y );
		}
		return !TrackCellTypes.IsWalkable( secondary.Type );
	}

	/// <summary>[EXT:coaster-marker-guard] Claim only safe occupancy, even when the preview accepts a foreign marker. Invalid foreign pylons remain in geometry but never own its lease.</summary>
	internal static bool Allows( ParkCellMap cells, int x, int y, ushort owner ) => AllowsOrdinaryGround( cells, x, y ) && (cells.RawTypeAt( x, y ) switch
	{
		0 or 22 => true,
		(byte)ParkCellType.Path => (cells.FlagsAt( x, y ) & ParkCellFlags.NoModify) == 0,
		MarkerType => cells.OwnerAt( x, y ) == owner,
		_ => false
	});

	internal bool AllowsPreview( int x, int y, bool moving ) => !disposed && AllowsPreview( cells, x, y, owner, moving );
	private bool Allows( int x, int y ) => !disposed && Allows( cells, x, y, owner ) && !displaced.Contains( (x, y) );

	internal CoasterNode? TokenAt( int x, int y ) => leases.TryGetValue( (x, y), out var lease ) && Owns( (x, y), lease ) ? lease.Token : null;
	internal int CountAt( int x, int y ) => leases.TryGetValue( (x, y), out var lease ) && Owns( (x, y), lease ) ? lease.Count : 0;

	private bool Owns( (int X, int Y) cell, Lease lease ) => cells.RawTypeAt( cell.X, cell.Y ) == MarkerType
		&& cells.OwnerAt( cell.X, cell.Y ) == owner && cells.CoasterGenerationAt( cell.X, cell.Y ) == lease.Generation;

	internal void Synchronize()
	{
		if ( disposed ) return;
		var desired = Desired();
		foreach ( var (cell, lease) in leases.ToArray() )
		{
			if ( !Owns( cell, lease ) )
			{
				// [EXT:coaster-marker-guard] Native clear has no owner check. A host replacement invalidates the old generation, even if it reuses the same owner word.
				leases.Remove( cell );
				displaced.Add( cell );
			}
			else if ( !desired.ContainsKey( cell ) )
				Release( cell, lease );
		}
		displaced.RemoveWhere( cell => !desired.ContainsKey( cell ) );
		foreach ( var (cell, nodes) in desired )
		{
			if ( !Allows( cell.X, cell.Y ) ) continue;
			var existing = leases.GetValueOrDefault( cell );
			var restored = cells.RawTypeAt( cell.X, cell.Y ) == MarkerType;
			if ( existing == null )
			{
				if ( cells.RawTypeAt( cell.X, cell.Y ) != MarkerType )
				{
					if ( cells.RawTypeAt( cell.X, cell.Y ) == (byte)ParkCellType.Path )
						PathLinkWriter.Unlink( cells, cell.X, cell.Y, cardinalOnly: false );
					cells.SetType( cell.X, cell.Y, ParkCellType.Empty );
					cells.SetFlags( cell.X, cell.Y, ParkCellFlags.None );
					cells.SetType( cell.X, cell.Y, MarkerType );
				}
				if ( cells.OwnerAt( cell.X, cell.Y ) != owner ) cells.SetOwner( cell.X, cell.Y, owner );
				cells.ClaimCoasterGeneration( cell.X, cell.Y );
			}
			// [BIN:TPW-EXE:0x444EC3,0x444EE7] only the last node clears a cell; otherwise publish the surviving first node.
			CoasterNode? token;
			if ( existing == null || nodes.Length < existing.Count )
				token = nodes.FirstOrDefault();
			else if ( nodes.Length > existing.Count )
				// [BIN:TPW-EXE:0x434412] construction publishes the newly registered node, not necessarily the bottom survivor.
				token = nodes.LastOrDefault( node => !existing.Nodes.Any( old => ReferenceEquals( old, node ) ) ) ?? nodes.LastOrDefault();
			else if ( nodes.Any( node => ReferenceEquals( node, existing.Token ) ) )
				token = existing.Token;
			else
			{
				// [EXT:coaster-marker-token] immutable host record replacement preserves the token's stack slot; a native model pointer has no equivalent serialized identity here.
				var slot = Array.FindIndex( existing.Nodes, node => ReferenceEquals( node, existing.Token ) );
				token = nodes.Length == 0 ? null : nodes[Math.Clamp( slot, 0, nodes.Length - 1 )];
			}
			// [BIN:TPW-EXE:0x4343B3,0x434401] creation parameter0x10 maps node0x400 and marker0x100. Stack republish passes zero (0x444EC6).
			// An unchanged saved marker needs only its derived token; leave its flags/stamps intact.
			if ( !restored || existing != null && existing.Count != nodes.Length )
			{
				var set = existing == null && token != null && (token.Flags & CoasterNodeFlags.StationHead) != 0;
				var flags = (ParkCellFlags)((ushort)cells.FlagsAt( cell.X, cell.Y ) & ~StationHeadMark | (set ? StationHeadMark : 0));
				if ( flags != cells.FlagsAt( cell.X, cell.Y ) ) cells.SetFlags( cell.X, cell.Y, flags );
			}
			leases[cell] = new( cells.CoasterGenerationAt( cell.X, cell.Y ), token, nodes );
		}
	}

	private void Release( (int X, int Y) cell, Lease lease )
	{
		if ( Owns( cell, lease ) )
		{
			cells.SetType( cell.X, cell.Y, ParkCellType.Empty );
			cells.SetFlags( cell.X, cell.Y, ParkCellFlags.None );
		}
		leases.Remove( cell );
	}

	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		foreach ( var (cell, lease) in leases.ToArray() ) Release( cell, lease );
		displaced.Clear();
	}
}
