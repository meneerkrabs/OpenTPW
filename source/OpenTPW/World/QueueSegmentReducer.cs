namespace OpenTPW;

/// <summary>The original snapped endpoints of one still-active queue tool segment. Never a restore snapshot.</summary>
internal sealed record QueueSegmentReceipt( (int X, int Y) Start, (int X, int Y) End );

/// <summary>
/// tp.exe 0x5273DF's ordered queue passes over the supported plain surface domain. Secondary cells must be empty;
/// object overwrite, pools and track layers remain outside this reducer. No chain reconstruction occurs between passes.
/// </summary>
internal sealed class QueueSegmentReducer
{
	private readonly ParkCellMap cells;
	private readonly ushort owner;
	private readonly Func<int, int, bool> excluded;
	private readonly Func<bool> spend;
	private readonly Func<long, bool> previewMoney;
	private readonly Func<long> price;
	private readonly Action<ushort> refund;
	private readonly Action? changed;
	private readonly Action<IReadOnlySet<(int X, int Y)>>? removed;
	private readonly LinkEffects effects;
	private readonly Func<(int X, int Y)?>? entrance;
	private readonly Func<int, int, bool>? terminalAllowed;

	internal QueueSegmentReducer( ParkCellMap cells, ushort owner, Func<int, int, bool> excluded,
		Func<bool> spend, Func<long, bool> previewMoney, Func<long> price, Action<ushort> refund, Action? changed = null, LinkEffects effects = default, Action<IReadOnlySet<(int X, int Y)>>? removed = null, Func<(int X, int Y)?>? entrance = null, Func<int, int, bool>? terminalAllowed = null )
	{
		this.cells = cells; this.owner = owner; this.excluded = excluded; this.spend = spend;
		this.previewMoney = previewMoney; this.price = price; this.refund = refund; this.changed = changed; this.effects = effects; this.removed = removed; this.entrance = entrance; this.terminalAllowed = terminalAllowed;
	}

	internal bool Supports( (int X, int Y) start, (int X, int Y) end ) => SupportsDomain( start, end, true );
	internal bool SupportsAdmission( (int X, int Y) start, (int X, int Y) end ) => SupportsDomain( start, end, false );
	internal bool SupportsUndo( (int X, int Y) start, (int X, int Y) end ) => SupportsDomain( start, end, false, terminalException: false );
	private bool SupportsDomain( (int X, int Y) start, (int X, int Y) end, bool increments, (int X, int Y)? terminal = null, bool terminalException = true ) => owner is > 0 and <= 16384
		&& ParkPathBuilder.LayLine( start, ParkPathBuilder.SnapEnd( start, end ) ).All( p =>
			cells.InBounds( p.X, p.Y ) && cells.RawTypeAt( p.X, p.Y ) is 0 or 1 or 3
			&& (!increments || cells.RawTypeAt( p.X, p.Y ) != 3 || cells.PlacementCountAt( p.X, p.Y ) < short.MaxValue)
			&& cells.Tracks.Get( p.X, p.Y ).Equals( default(TrackCell) )
			&& cells.PoolBridges?.Reserves( p.X, p.Y ) != true
			// The host may admit its initial path only as the actual terminal cell; undo has no such exception.
			&& (!excluded( p.X, p.Y ) || (terminalException && p == (terminal ?? ParkPathBuilder.SnapEnd( start, end )) && terminalAllowed?.Invoke( p.X, p.Y ) == true)) );

	internal SegmentResult Preview( (int X, int Y) start, (int X, int Y) end )
	{
		end = ParkPathBuilder.SnapEnd( start, end );
		var line = ParkPathBuilder.LayLine( start, end );
		var built = new List<(int X, int Y)>();
		foreach ( var p in line )
		{
			if ( !SupportsDomain( p, p, true, end ) ) return Result( CellBuildResult.Occupied, p );
			if ( cells.FlagsAt( p.X, p.Y ).HasFlag( ParkCellFlags.Unowned ) ) return Result( CellBuildResult.NotOwned, p );
			var type = cells.RawTypeAt( p.X, p.Y );
			// Native validator returns 8 for a final path before testing its NoModify bit.
			if ( p == end && type == 1 ) break;
			if ( type == 1 && cells.FlagsAt( p.X, p.Y ).HasFlag( ParkCellFlags.NoModify ) ) return Result( CellBuildResult.NoModify, p );
			if ( type == 3 )
			{
				if ( cells.OwnerAt( p.X, p.Y ) != 0 && cells.OwnerAt( p.X, p.Y ) != owner ) return Result( CellBuildResult.Occupied, p );
				continue;
			}
			built.Add( p );
			if ( !previewMoney( built.Count * price() ) ) return Result( CellBuildResult.NotEnoughMoney, p );
		}
		return Result( CellBuildResult.Ok, null );
		SegmentResult Result( CellBuildResult failure, (int X, int Y)? at ) => new( end, built, built.Count * price(), failure, cells.RawTypeAt( end.X, end.Y ) is 1 or 3 )
		{ Start = start, Line = line, StoppedAt = at, LinkPointer = failure == CellBuildResult.Ok && cells.RawTypeAt( end.X, end.Y ) == 1 };
	}

	internal SegmentResult Commit( (int X, int Y) start, (int X, int Y) end )
	{
		end = ParkPathBuilder.SnapEnd( start, end );
		if ( !Supports( start, end ) ) throw new InvalidOperationException( "Queue segment is outside the plain secondary-empty domain." );
		var line = ParkPathBuilder.LayLine( start, end );
		var built = new List<(int X, int Y)>();
		var existing = new List<(int X, int Y)>();
		var endedOnExisting = cells.RawTypeAt( end.X, end.Y ) is 1 or 3;
		(int X, int Y)? stopped = null;
		long charged = 0;
		var finalEndFlag = false;
		var cleared = new HashSet<(int X, int Y)>();
		// Overwrite 87 has no work in this domain. Type pass 3 may stop; all later passes retain the full line.
		foreach ( var p in line )
		{
			var type = cells.RawTypeAt( p.X, p.Y );
			// 534589 resets the latch for each visited type cell; only the visited last old path/queue sets it.
			finalEndFlag = p == end && type is 1 or 3;
			if ( p == end && type == 1 ) break;
			if ( type == 3 )
			{
				cells.SetPlacementCount( p.X, p.Y, (short)(cells.PlacementCountAt( p.X, p.Y ) + 1) );
				existing.Add( p ); continue;
			}
			if ( type == 1 )
			{
				cleared.Add( p );
				PathLinkWriter.Unlink( cells, p.X, p.Y, cardinalOnly: false, effects );
				cells.SetFlags( p.X, p.Y, 0 );
				cells.SetType( p.X, p.Y, ParkCellType.Empty );
			}
			if ( !spend() ) { stopped = p; break; }
			cells.SetType( p.X, p.Y, ParkCellType.Queue );
			built.Add( p ); charged += price();
		}
		LinkFirstEntrance( start );
		for ( var i = 1; i < line.Count; i++ )
		{
			var p = line[i]; var previous = line[i - 1];
			PathLinkWriter.LinkQueueStep( cells, p.X, p.Y, p.X - previous.X, p.Y - previous.Y, 3,
				( x, y ) => cells.OwnerAt( x, y ) == 0 || cells.OwnerAt( x, y ) == owner );
		}
		// Direction82 uses LayLine's nonzero step even at the first cell (536175/177); no first-cell guard.
		var dx = Math.Sign( end.X - start.X ); var dy = Math.Sign( end.Y - start.Y );
		if ( dx == 0 && dy == 0 ) dy = 1; // LayLine's equal-axis branch chooses +Y for a singleton.
		var reverse = dx == 1 ? (byte)64 : dx == -1 ? (byte)4 : dy == 1 ? (byte)1 : (byte)16;
		foreach ( var p in line )
		{
			if ( cells.QueueLinkAt( p.X, p.Y ) == 0 )
				cells.ApplyQueuePassMetadata( p.X, p.Y, reverse, cells.OwnerAt( p.X, p.Y ) );
		}
		foreach ( var p in line ) cells.ApplyQueuePassMetadata( p.X, p.Y, cells.QueueLinkAt( p.X, p.Y ), owner );
		foreach ( var p in line ) effects.Refresh?.Invoke( p.X, p.Y );
		if ( cleared.Count > 0 ) removed?.Invoke( cleared );
		changed?.Invoke();
		return new SegmentResult( end, built, charged, stopped == null ? CellBuildResult.Ok : CellBuildResult.NotEnoughMoney, endedOnExisting )
		{ Start = start, Line = line, Existing = existing, StoppedAt = stopped, QueueFinalEndsTool = finalEndFlag, QueueReceipt = end == start || finalEndFlag ? null : new( start, end ) };
	}

	// 53522D..53538C: first80 links only the selected ride's actual entry cell, not an arbitrary neighbour.
	private void LinkFirstEntrance( (int X, int Y) start )
	{
		if ( entrance?.Invoke() is not { } entry || !cells.InBounds( entry.X, entry.Y ) ) return;
		for ( var direction = 0; direction < 4; direction++ )
		{
			var step = GuestPathGrid.Directions[direction];
			if ( (start.X + step.DX, start.Y + step.DY) != entry ) continue;
			var toward = GuestPathGrid.LinkValue( direction );
			var back = PathLinkWriter.Opposite( toward );
			if ( cells.QueueLinkAt( entry.X, entry.Y ) != back ) return;
			cells.SetLinks( start.X, start.Y, (byte)(cells.LinksAt( start.X, start.Y ) | toward) );
			cells.SetLinks( entry.X, entry.Y, (byte)(cells.LinksAt( entry.X, entry.Y ) | back) );
			return;
		}
	}

	internal void Undo( QueueSegmentReceipt receipt )
	{
		if ( receipt.Start == ParkPathBuilder.SnapEnd( receipt.Start, receipt.End ) || !SupportsUndo( receipt.Start, receipt.End ) ) throw new InvalidOperationException( "Queue undo leaves the supported plain domain." );
		// 52FE50: the prior corner currently being a path suppresses the second removal click altogether.
		if ( cells.RawTypeAt( receipt.Start.X, receipt.Start.Y ) == 1 ) return;
		var line = ParkPathBuilder.LayLine( receipt.End, receipt.Start );
		var cleared = new HashSet<(int X, int Y)>();
		foreach ( var p in line )
		{
			var type = cells.RawTypeAt( p.X, p.Y );
			if ( type == 0 ) continue; // Empty residue survives; mode32 continues rather than stopping.
			if ( cells.FlagsAt( p.X, p.Y ).HasFlag( ParkCellFlags.NoModify ) && (type == 3 || cells.LinksAt( p.X, p.Y ) != 0) ) continue;
			var mask = cells.LinksAt( p.X, p.Y );
			var bend = type == 3 && TrackHeading.Count( mask ) == 2 && (mask & 0x55) is not (0x11 or 0x44);
			var count = bend ? -1 : cells.PlacementCountAt( p.X, p.Y ) - 1;
			if ( count >= 0 ) { cells.SetPlacementCount( p.X, p.Y, (short)count ); continue; }
			cleared.Add( p );
			if ( type == 3 ) refund( cells.OwnerAt( p.X, p.Y ) );
			PathLinkWriter.Unlink( cells, p.X, p.Y, cardinalOnly: type == 3, effects );
			cells.SetFlags( p.X, p.Y, 0 );
			cells.SetType( p.X, p.Y, ParkCellType.Empty );
		}
		if ( cleared.Count > 0 ) removed?.Invoke( cleared );
		changed?.Invoke();
	}
}
