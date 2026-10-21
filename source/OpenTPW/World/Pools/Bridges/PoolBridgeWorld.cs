using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>
/// A bridge check's answer. <see cref="Occupied"/> is the 256-bridge limit; the values after <see cref="NotBridge"/> name what refuses a placement so the tool can say it:
/// an outside end that is no ordinary path, an approach cell that is pool water (the pool is wider than three cells there, or the click was not on the first land cell),
/// a ride, track, queue, post, protected cell or scenery on a covered cell, and a cell another bridge already uses.
/// </summary>
public enum PoolBridgeResult { Ok, ExtensionOff, OutsideTerrain, NotOwned, Occupied, NotOverPool, TooSteep, Standing, NotEnoughMoney, NotBridge, EndNotPath, LandIsWater, Blocked, BridgeInTheWay }

/// <summary>[EXT:pools-bridges] An elevated route over unchanged water cells: two approach cells, three span cells, two approach cells.</summary>
public sealed record PoolBridge( int X, int Y, int Direction, int GroundMillimetres )
{
	public const int Length = 7;
	public (int X, int Y) Cell( int index )
	{
		var (dx, dy) = GuestPathGrid.Directions[Direction];
		return (X + dx * index, Y + dy * index);
	}
	[System.Text.Json.Serialization.JsonIgnore]
	public float Ground => GroundMillimetres / 1000f;
	public bool Contains( int x, int y ) => Enumerable.Range( 0, Length ).Any( i => Cell( i ) == (x, y) );
	public float HeightAt( float x, float y )
	{
		var (dx, dy) = GuestPathGrid.Directions[Direction];
		var along = (x - X - 0.5f) * dx + (y - Y - 0.5f) * dy + 0.5f;
		var rise = Math.Clamp( Math.Min( along, Length - along ), 0, 2 );
		return Ground + MathF.Ceiling( rise * 5 ) / 5;
	}
}

public sealed record PoolBridgesSaveData( IReadOnlyList<PoolBridge> Bridges );

/// <summary>Headless bridge placement, routing and persistence. The ordinary cell types and links are never replaced.</summary>
public sealed class PoolBridgeWorld
{
	public const int Price = 1200;
	private readonly ParkCellMap cells;
	private readonly Func<float, float, float> ground;
	private readonly Func<int, int, bool>? reserved, occupied;
	private readonly Func<ParkEconomy?> economy;
	private readonly Func<bool> enabled;
	private readonly SortedDictionary<int, PoolBridge> bridges = new();
	private readonly Dictionary<(int, int), PoolBridge> coverage = new();
	public int Version { get; private set; }
	public int Count => bridges.Count;
	public IEnumerable<PoolBridge> Bridges => bridges.Values;

	public PoolBridgeWorld( ParkCellMap cells, Func<float, float, float>? ground = null, Func<int, int, bool>? reserved = null,
		Func<int, int, bool>? occupied = null, Func<ParkEconomy?>? economy = null, Func<bool>? enabled = null )
	{
		this.cells = cells;
		this.ground = ground ?? (( _, _ ) => 0);
		this.reserved = reserved;
		this.occupied = occupied;
		this.economy = economy ?? (() => null);
		this.enabled = enabled ?? (() => true);
		cells.PoolBridges = this;
	}

	public PoolBridge? At( int x, int y ) => coverage.GetValueOrDefault( (x, y) );
	public bool Covers( int x, int y ) => coverage.ContainsKey( (x, y) );
	public bool Reserves( int x, int y ) => Covers( x, y ) || bridges.Values.Any( b => b.Cell( -1 ) == (x, y) || b.Cell( PoolBridge.Length ) == (x, y) );

	public bool Connects( int x, int y, int direction )
	{
		if ( Count == 0 || direction is < 0 or > 3 ) return false;
		var (dx, dy) = GuestPathGrid.Directions[direction];
		var target = (X: x + dx, Y: y + dy);
		var bridge = At( x, y ) ?? At( target.X, target.Y );
		if ( bridge == null || direction != bridge.Direction && direction != ((bridge.Direction + 2) & 3) ) return false;
		if ( Covers( x, y ) && Covers( target.X, target.Y ) ) return At( x, y ) == At( target.X, target.Y );
		var outside = Covers( x, y ) ? target : (X: x, Y: y);
		return (bridge.Cell( -1 ) == outside || bridge.Cell( PoolBridge.Length ) == outside) && cells.InBounds( outside.X, outside.Y )
			&& cells.TypeAt( outside.X, outside.Y ) == ParkCellType.Path;
	}

	public byte LinksAt( int x, int y )
	{
		if ( Count == 0 ) return 0;
		byte links = 0;
		for ( var d = 0; d < 4; d++ ) if ( Connects( x, y, d ) ) links |= GuestPathGrid.LinkValue( d );
		return links;
	}

	public PoolBridgeResult Check( int x, int y, int direction ) => Check( x, y, direction, restoring: false, out _ );
	/// <summary>The check, with the cell that refuses the placement (null when it passes or when no single cell is to blame: the slope, the money, the limit).</summary>
	public PoolBridgeResult Check( int x, int y, int direction, out (int X, int Y)? blocker ) => Check( x, y, direction, restoring: false, out blocker );
	private PoolBridgeResult Check( int x, int y, int direction, bool restoring ) => Check( x, y, direction, restoring, out _ );
	private PoolBridgeResult Check( int x, int y, int direction, bool restoring, out (int X, int Y)? blocker )
	{
		blocker = null;
		if ( !enabled() ) return PoolBridgeResult.ExtensionOff;
		if ( Count >= 256 ) return PoolBridgeResult.Occupied;
		if ( direction is < 0 or > 3 ) return PoolBridgeResult.OutsideTerrain;
		var candidate = new PoolBridge( x, y, direction, 0 );
		float minimum = float.MaxValue, maximum = float.MinValue;
		for ( var i = -1; i <= PoolBridge.Length; i++ )
		{
			var (cx, cy) = candidate.Cell( i );
			if ( !cells.InBounds( cx, cy ) ) { blocker = null; return PoolBridgeResult.OutsideTerrain; }
			blocker = (cx, cy);
			if ( cells.FlagsAt( cx, cy ).HasFlag( ParkCellFlags.Unowned ) ) return PoolBridgeResult.NotOwned;
			if ( i is -1 or PoolBridge.Length )
			{
				if ( Covers( cx, cy ) ) return PoolBridgeResult.BridgeInTheWay;
				if ( cells.TypeAt( cx, cy ) != ParkCellType.Path ) return PoolBridgeResult.EndNotPath;
				continue;
			}
			if ( Reserves( cx, cy ) ) return PoolBridgeResult.BridgeInTheWay;
			if ( cells.Tracks.Get( cx, cy ) is { Type: not 0 } || cells.Land?.IsPost( cx, cy ) == true || reserved?.Invoke( cx, cy ) == true ) return PoolBridgeResult.Blocked;
			if ( (cells.FlagsAt( cx, cy ) & (ParkCellFlags.NoModify | ParkCellFlags.EntranceArea | ParkCellFlags.MapBit04 | ParkCellFlags.AddOn)) != 0 ) return PoolBridgeResult.Blocked;
			if ( i is >= 2 and <= 4 )
			{
				if ( cells.TypeAt( cx, cy ) != ParkCellType.PoolWater ) return PoolBridgeResult.NotOverPool;
			}
			else if ( cells.TypeAt( cx, cy ) == ParkCellType.PoolWater ) return PoolBridgeResult.LandIsWater;
			else if ( cells.TypeAt( cx, cy ) is not (ParkCellType.Empty or ParkCellType.Path) ) return PoolBridgeResult.Blocked;
			if ( !restoring && occupied?.Invoke( cx, cy ) == true ) return PoolBridgeResult.Standing;
			blocker = null;
			for ( var ox = 0; ox <= 1; ox++ ) for ( var oy = 0; oy <= 1; oy++ )
			{
				var h = ground( cx + ox, cy + oy );
				if ( !float.IsFinite( h ) ) return PoolBridgeResult.TooSteep;
				minimum = Math.Min( minimum, h ); maximum = Math.Max( maximum, h );
			}
		}
		blocker = null;
		// The fixed bridge model needs flat approaches: a tilted bank would leave a step at either end.
		if ( maximum - minimum > 0.01f ) return PoolBridgeResult.TooSteep;
		if ( !restoring && economy() is { } park && park.Balance < Price ) return PoolBridgeResult.NotEnoughMoney;
		return PoolBridgeResult.Ok;
	}

	public PoolBridgeResult TryAdd( int x, int y, int direction )
	{
		var result = Check( x, y, direction );
		if ( result != PoolBridgeResult.Ok ) return result;
		if ( economy() is { } park && park.TrySpendPoolBridge() != ParkEconomy.PurchaseResult.Ok ) return PoolBridgeResult.NotEnoughMoney;
		Insert( new PoolBridge( x, y, direction, (int)MathF.Round( ground( x, y ) * 1000 ) ) );
		cells.Touch();
		return PoolBridgeResult.Ok;
	}
	private void Insert( PoolBridge bridge )
	{
		bridges.Add( bridge.Y * cells.Width + bridge.X, bridge );
		for ( var i = 0; i < PoolBridge.Length; i++ ) coverage.Add( bridge.Cell( i ), bridge );
		Version++;
	}
	public PoolBridgeResult CheckRemove( int x, int y )
	{
		if ( !enabled() ) return PoolBridgeResult.ExtensionOff;
		if ( At( x, y ) is not { } bridge ) return PoolBridgeResult.NotBridge;
		return Enumerable.Range( 0, PoolBridge.Length ).Any( i => { var c = bridge.Cell( i ); return occupied?.Invoke( c.X, c.Y ) == true; } ) ? PoolBridgeResult.Standing : PoolBridgeResult.Ok;
	}
	public PoolBridgeResult RemoveAt( int x, int y )
	{
		var result = CheckRemove( x, y );
		if ( result != PoolBridgeResult.Ok ) return result;
		var bridge = At( x, y )!;
		bridges.Remove( bridge.Y * cells.Width + bridge.X );
		for ( var i = 0; i < PoolBridge.Length; i++ ) coverage.Remove( bridge.Cell( i ) );
		economy()?.RefundPoolBridge();
		Version++; cells.Touch();
		return PoolBridgeResult.Ok;
	}
	public PoolBridgesSaveData? CaptureSnapshot() => Count == 0 ? null : new( bridges.Values.ToArray() );
	public void RestoreSnapshot( PoolBridgesSaveData? data )
	{
		bridges.Clear(); coverage.Clear(); Version++;
		if ( data == null ) return;
		foreach ( var bridge in data.Bridges )
		{
			if ( Check( bridge.X, bridge.Y, bridge.Direction, restoring: true ) != PoolBridgeResult.Ok || Math.Abs( bridge.Ground - ground( bridge.X, bridge.Y ) ) > .001f )
				throw new InvalidDataException( "The saved pool bridge does not fit its water, paths or terrain." );
			Insert( bridge );
		}
	}
	public void AddCanonicalState( StateHasher hash )
	{
		if ( Count == 0 ) return;
		hash.Add( "opentpw.pool-bridges" ); hash.Add( Count );
		foreach ( var b in bridges.Values ) { hash.Add( b.X ); hash.Add( b.Y ); hash.Add( b.Direction ); hash.Add( b.GroundMillimetres ); }
	}
	internal static string? ShapeProblem( PoolBridgesSaveData? data, IReadOnlyList<ParkModRecord?>? mods )
	{
		if ( data == null ) return null;
		if ( data.Bridges == null || data.Bridges.Count is < 1 or > 256 ) return "the pool bridges list is empty or too large";
		if ( mods?.Any( m => m?.Id == OfficialExtensions.Pools.Id ) != true ) return "pool bridges require the pools extension record";
		var used = new HashSet<(int, int)>();
		foreach ( var b in data.Bridges )
		{
			if ( b == null || b.X is < 0 or >= 1024 || b.Y is < 0 or >= 1024 || b.Direction is < 0 or > 3 || b.GroundMillimetres is < -100000 or > 100000 ) return "a pool bridge record is invalid";
			for ( var i = 0; i < PoolBridge.Length; i++ ) { var c = b.Cell( i ); if ( c.X is < 0 or >= 1024 || c.Y is < 0 or >= 1024 || !used.Add( c ) ) return "pool bridge footprints overlap or leave the map"; }
		}
		return null;
	}
}
