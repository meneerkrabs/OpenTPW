using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>The saved form of one slide (<see cref="PoolSlidesSaveData"/>).</summary>
public sealed record PoolSlideSaveData( int TowerX, int TowerY, int Profile, int StartX, int StartY, int StartHeading, int[] Pieces, int TopHeight, int EndHeight );

/// <summary>[EXT:pools-slides] The shape check of the slides part of a save (<see cref="PoolSlideSaves"/>).</summary>
internal static class PoolSlideSaves
{
	/// <summary>
	/// What is wrong with the saved slides on their own, or null: a bounded count, slides in tower order, a bounded piece list of known pieces, known headings and profiles, heights in a sane range, and the
	/// <c>opentpw.pools</c> extension recorded (a slide's tower and pool are the extension's).
	/// </summary>
	public static string? ShapeProblem( PoolSlidesSaveData? slides, IReadOnlyList<ParkModRecord?>? mods )
	{
		if ( slides == null )
			return null;
		if ( slides.Slides == null || slides.Slides.Count == 0 || slides.Slides.Count > 256 )
			return "the slides part holds no slide or too many";
		if ( mods?.Any( mod => mod?.Id == OfficialExtensions.Pools.Id ) != true )
			return $"the save holds slides but does not record {OfficialExtensions.Pools.Id}";
		var previous = int.MinValue;
		foreach ( var slide in slides.Slides )
		{
			if ( slide == null || slide.Pieces == null )
				return "a saved slide is empty";
			if ( slide.TowerX is < 0 or > 4095 || slide.TowerY is < 0 or > 4095 )
				return $"a saved slide's tower ({slide.TowerX}, {slide.TowerY}) is outside the map";
			var id = PoolSlide.KeyOf( slide.TowerX, slide.TowerY );
			if ( id <= previous )
				return $"the saved slide at {slide.TowerX},{slide.TowerY} does not follow the one before it in tower order";
			previous = id;
			if ( slide.Pieces.Length is 0 or > PoolSlide.MaximumPieces || slide.Pieces.Any( piece => piece is < 0 or > 3 ) )
				return $"the saved slide at {slide.TowerX},{slide.TowerY} holds {slide.Pieces.Length} pieces, or one that does not exist";
			if ( slide.StartHeading is < 0 or > 3 || slide.Profile is < 0 or > 1 )
				return $"the saved slide at {slide.TowerX},{slide.TowerY} has a heading or a profile that does not exist";
			if ( slide.TopHeight is < -100_000 or > 100_000 || slide.EndHeight is < -100_000 or > 100_000 )
				return $"the saved slide at {slide.TowerX},{slide.TowerY} has heights outside the map's range";
		}
		return null;
	}
}

/// <summary>The park-resume part of the slides: every slide, in tower order. Absent from a save without slides, so such saves keep their bytes.</summary>
public sealed record PoolSlidesSaveData( IReadOnlyList<PoolSlideSaveData> Slides );

/// <summary>
/// The water slides of a park (docs/POOLS.md, "Water slides"): one slide per tower, built cell by cell from the tower to a splash piece over the pool, charged per piece. The world is the
/// headless builder the slide tool and the tests drive and the store the activity, the renderer and the saves read. It reads the park only through the site and the towers it is given.
/// </summary>
// [EXT:pools-slides] OpenTPW's own slides; the original game has no pools
public sealed class PoolSlideWorld
{
	private readonly SortedDictionary<int, PoolSlide> slides = new();
	private readonly Func<PoolSlideSite?> site;
	private readonly Func<int, PoolSlideTower?> towers;
	private readonly Func<ParkEconomy?> economy;

	public PoolSlideWorld( Func<PoolSlideSite?> site, Func<int, PoolSlideTower?> towers, Func<ParkEconomy?> economy )
	{
		this.site = site ?? throw new ArgumentNullException( nameof( site ) );
		this.towers = towers ?? throw new ArgumentNullException( nameof( towers ) );
		this.economy = economy ?? throw new ArgumentNullException( nameof( economy ) );
	}

	/// <summary>Moves with every change of any slide; renderers rebuild when it changes.</summary>
	public int Version { get; private set; }
	/// <summary>The money the last change charged (a refund is negative).</summary>
	public long LastCharged { get; private set; }
	/// <summary>The slides in tower order (the order of their ids).</summary>
	public IEnumerable<PoolSlide> Slides => slides.Values;

	private PoolSlide[] ordered = Array.Empty<PoolSlide>();
	private int orderedVersion = -1;

	/// <summary>The slides in tower order as an array that is rebuilt only when the slides change, for callers that ask every turn and must not allocate.</summary>
	public IReadOnlyList<PoolSlide> Ordered
	{
		get
		{
			if ( orderedVersion != Version )
			{
				ordered = slides.Values.ToArray();
				orderedVersion = Version;
			}
			return ordered;
		}
	}

	/// <summary>The slide of the tower with this thing id (the target the guests on it hold), or null.</summary>
	public PoolSlide? FindByThing( int thingId )
	{
		if ( thingId == 0 )
			return null;
		var list = Ordered;
		for ( var index = 0; index < list.Count; index++ )
			if ( TowerOf( list[index] ) is { } tower && tower.ThingId == thingId )
				return list[index];
		return null;
	}

	/// <summary>Invalidates what the world has remembered about its slides (a tower was placed or removed): the next question is answered afresh.</summary>
	public void Touch() => Version++;
	public int Count => slides.Count;

	public PoolSlide? Find( int id ) => slides.TryGetValue( id, out var slide ) ? slide : null;

	/// <summary>The slide whose track covers the cell, or null.</summary>
	public PoolSlide? SlideAt( int x, int y ) => slides.Values.FirstOrDefault( slide => slide.Covers( x, y ) );

	/// <summary>Whether a slide's track covers the cell on land: the host keeps the cell for the pylon (the water cells under the track stay pool water).</summary>
	public bool ReservesLand( int x, int y, ParkCellMap cells ) => cells.RawTypeAt( x, y ) != ParkCellTypes.PoolWater && SlideAt( x, y ) != null;

	private readonly Dictionary<int, (int Version, int PoolWrites, bool Usable)> usable = new();

	/// <summary>
	/// Whether the slide can be used now: complete and still valid for its tower and the pool (the pool at its end may have been changed since it was built). False without a tower. The answer is
	/// kept until the slides or the pool cells change, so asking every turn is cheap.
	/// </summary>
	public bool IsUsable( PoolSlide slide )
	{
		if ( !slide.IsComplete || site() is not { } place )
			return false;
		var writes = place.Cells.PoolWrites;
		if ( usable.TryGetValue( slide.Id, out var known ) && known.Version == Version && known.PoolWrites == writes )
			return known.Usable;
		var valid = PoolSlideRules.Validate( slide, TowerAt( slide.TowerX, slide.TowerY ), place ) == PoolSlideResult.Ok;
		usable[slide.Id] = (Version, writes, valid);
		return valid;
	}

	/// <summary>
	/// Whether the pool cell must stay water because a slide needs it: the splash cell of a complete slide, and the pool-side cells of the tower of any slide (the foot of its stairs may be any of them, as a
	/// changing room's access cells are protected). The pool tool refuses to erase them, so an ordinary edit cannot take away what a built slide stands on.
	/// </summary>
	public bool NeedsWater( int x, int y )
	{
		var list = Ordered;
		for ( var index = 0; index < list.Count; index++ )
		{
			if ( list[index].IsComplete && list[index].LandingCell == (x, y) )
				return true;
			if ( TowerOf( list[index] ) is { } tower && tower.PoolSide.Contains( (x, y) ) )
				return true;
		}
		return false;
	}

	/// <summary>The tower of a slide, as the world was given it; null when it is gone.</summary>
	public PoolSlideTower? TowerOf( PoolSlide slide ) => TowerAt( slide.TowerX, slide.TowerY );

	/// <summary>The water surface over a pool cell, engine units, or null.</summary>
	public float? WaterLevelAt( int x, int y ) => site()?.WaterLevel( x, y );

	public PoolSlideTower? TowerAt( int x, int y ) => towers( PoolSlide.KeyOf( x, y ) );

	/// <summary>
	/// Adds the piece for a cell: the first cell of a slide must be one of the tower's start cells, every later cell is the one beside the end of the track, not behind it. The previous piece
	/// becomes a straight or a turn towards it; a pool cell ends the slide with a splash piece. Charges the piece. Returns <see cref="PoolSlideResult.Ok"/> or the reason.
	/// </summary>
	public PoolSlideResult AddCell( int towerX, int towerY, int x, int y )
	{
		var check = Plan( towerX, towerY, x, y, out var candidate );
		if ( check != PoolSlideResult.Ok || candidate == null )
			return check;
		var park = economy();
		if ( park != null && !park.CanSpendCell( CellPurchase.SlidePiece ) )
			return PoolSlideResult.NotEnoughMoney;
		var before = park?.Balance ?? 0;
		park?.TrySpendCell( CellPurchase.SlidePiece );
		LastCharged = before - (park?.Balance ?? 0);
		slides[candidate.Id] = candidate;
		Version++;
		return PoolSlideResult.Ok;
	}

	/// <summary>What <see cref="AddCell"/> would answer for the cell, the money included, without writing anything.</summary>
	public PoolSlideResult Check( int towerX, int towerY, int x, int y )
	{
		var plan = Plan( towerX, towerY, x, y, out _ );
		if ( plan == PoolSlideResult.Ok && economy() is { } park && !park.CanPreviewLine( park.CellCost( CellPurchase.SlidePiece ) ) )
			return PoolSlideResult.NotEnoughMoney;
		return plan;
	}

	/// <summary>Whether <see cref="AddCell"/> would accept the cell, with the slide it would make (the money not asked).</summary>
	public PoolSlideResult Plan( int towerX, int towerY, int x, int y, out PoolSlide? candidate )
	{
		candidate = null;
		if ( !FeatureGates.Pools )
			return PoolSlideResult.ExtensionOff;
		if ( TowerAt( towerX, towerY ) is not { } tower || site() is not { } place )
			return PoolSlideResult.NoTower;
		if ( tower.PoolSideCell( place.Cells ) == null )
			return PoolSlideResult.NoPoolSide;
		if ( !place.Cells.InBounds( x, y ) )
			return PoolSlideResult.OutsideLand;
		var isPool = place.Cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater;
		var pieces = new List<PoolSlidePiece>();
		int startX, startY, startHeading;
		var existing = Find( tower.Id );
		if ( existing == null )
		{
			var option = tower.StartOptions().Cast<(int X, int Y, int Heading)?>().FirstOrDefault( o => o!.Value.X == x && o.Value.Y == y );
			if ( option == null )
				return PoolSlideResult.BadStart;
			(startX, startY, startHeading) = (option.Value.X, option.Value.Y, option.Value.Heading);
			pieces.Add( isPool ? PoolSlidePiece.Splash : PoolSlidePiece.Straight );
		}
		else
		{
			if ( existing.IsComplete )
				return PoolSlideResult.NoSplash;
			var last = existing.Cells[^1];
			var turn = TurnTo( existing.HeadingOf( existing.Pieces.Count - 1 ), x - last.X, y - last.Y );
			if ( turn == null )
				return PoolSlideResult.NotNext;
			pieces.AddRange( existing.Pieces );
			pieces[^1] = turn.Value;
			pieces.Add( isPool ? PoolSlidePiece.Splash : PoolSlidePiece.Straight );
			(startX, startY, startHeading) = (existing.StartX, existing.StartY, existing.StartHeading);
		}
		var top = PoolSlideRules.TopHeightOf( tower, place );
		var end = isPool ? PoolSlideRules.EndHeightOf( x, y, place ) ?? top : top;
		var made = new PoolSlide( tower.X, tower.Y, tower.Profile, startX, startY, startHeading, pieces, top, end );
		var result = PoolSlideRules.Validate( made, tower, place, requireComplete: false );
		if ( result != PoolSlideResult.Ok )
			return result;
		if ( TouchesAnotherSlide( made ) )
			return PoolSlideResult.Occupied;
		candidate = made;
		return PoolSlideResult.Ok;
	}

	/// <summary>Whether any cell of the slide is also a cell of another tower's slide (two slides never share a pylon).</summary>
	private bool TouchesAnotherSlide( PoolSlide slide ) => slides.Values.Any( other => other.Id != slide.Id && slide.Cells.Any( cell => other.Covers( cell.X, cell.Y ) ) );

	/// <summary>The piece that turns a track heading <paramref name="heading"/> towards a step (<paramref name="dx"/>, <paramref name="dy"/>), or null for a step behind it or not to a neighbour.</summary>
	private static PoolSlidePiece? TurnTo( int heading, int dx, int dy )
	{
		for ( var direction = 0; direction < 4; direction++ )
		{
			if ( PoolSlide.Direction( direction ) != (dx, dy) )
				continue;
			return ((direction - heading) & 3) switch { 0 => PoolSlidePiece.Straight, 1 => PoolSlidePiece.Right, 3 => PoolSlidePiece.Left, _ => null };
		}
		return null;
	}

	/// <summary>Takes back the last piece of a slide (the whole slide when it has one) and refunds it. False when there is no slide.</summary>
	public bool RemoveLastPiece( int towerX, int towerY )
	{
		if ( Find( PoolSlide.KeyOf( towerX, towerY ) ) is not { } slide )
			return false;
		if ( slide.Pieces.Count <= 1 )
			slides.Remove( slide.Id );
		else
		{
			var pieces = slide.Pieces.Take( slide.Pieces.Count - 1 ).ToList();
			pieces[^1] = PoolSlidePiece.Straight;
			slides[slide.Id] = slide.With( pieces, slide.TopHeight, slide.TopHeight );
		}
		Refund( 1 );
		Version++;
		return true;
	}

	/// <summary>Removes the slide of a tower that was demolished and refunds its pieces. False when the tower had none.</summary>
	public bool Remove( int towerX, int towerY )
	{
		if ( !slides.Remove( PoolSlide.KeyOf( towerX, towerY ), out var slide ) )
			return false;
		Refund( slide.Pieces.Count );
		Version++;
		return true;
	}

	private void Refund( int pieces )
	{
		var park = economy();
		var before = park?.Balance ?? 0;
		for ( var index = 0; index < pieces; index++ )
			park?.RefundSlidePiece();
		LastCharged = before - (park?.Balance ?? 0);
	}

	/// <summary>Drops every slide without a refund (a level is being rebuilt).</summary>
	public void Clear()
	{
		if ( slides.Count == 0 )
			return;
		slides.Clear();
		Version++;
	}

	/// <summary>The saved form of the slides, or null when there are none.</summary>
	public PoolSlidesSaveData? CaptureSnapshot() => slides.Count == 0 ? null :
		new PoolSlidesSaveData( slides.Values.Select( slide => new PoolSlideSaveData( slide.TowerX, slide.TowerY, (int)slide.Profile, slide.StartX, slide.StartY, slide.StartHeading,
			slide.Pieces.Select( piece => (int)piece ).ToArray(), slide.TopHeight, slide.EndHeight ) ).ToArray() );

	/// <summary>
	/// Restores the saved slides over an empty world. Each is checked for its structure against its tower and the site (<see cref="PoolSlideRules.ValidateStructure"/>); the first that fails throws, so a
	/// save cannot bring in a slide the rules would not have built. What depends on the pool is not checked: a slide whose pool was changed legally comes back unusable, and with the water it comes back to use.
	/// </summary>
	public void RestoreSnapshot( PoolSlidesSaveData? saved )
	{
		slides.Clear();
		Version++;
		if ( saved == null )
			return;
		var place = site() ?? throw new InvalidDataException( "The saved slides need a park." );
		var previous = int.MinValue;
		foreach ( var item in saved.Slides )
		{
			if ( item.Pieces == null || item.Pieces.Length == 0 || item.Pieces.Length > PoolSlide.MaximumPieces || item.Pieces.Any( piece => piece is < 0 or > 3 ) || item.Profile is < 0 or > 1 || item.StartHeading is < 0 or > 3 )
				throw new InvalidDataException( $"The saved slide at {item.TowerX},{item.TowerY} is malformed." );
			var slide = new PoolSlide( item.TowerX, item.TowerY, (PoolSlideProfile)item.Profile, item.StartX, item.StartY, item.StartHeading, item.Pieces.Select( piece => (PoolSlidePiece)piece ), item.TopHeight, item.EndHeight );
			if ( slide.Id <= previous )
				throw new InvalidDataException( "The saved slides are not in tower order." );
			previous = slide.Id;
			var tower = TowerAt( item.TowerX, item.TowerY );
			// the structure only: whether the slide is usable now depends on its pool, which the pool tool may have changed since (docs/POOLS.md, "Saves and determinism")
			var result = PoolSlideRules.ValidateStructure( slide, tower, place );
			if ( result != PoolSlideResult.Ok || TouchesAnotherSlide( slide ) )
				throw new InvalidDataException( $"The saved slide at {item.TowerX},{item.TowerY} is not valid: {(result != PoolSlideResult.Ok ? result : PoolSlideResult.Occupied)}." );
			slides[slide.Id] = slide;
		}
		Version++;
	}

	/// <summary>
	/// Checks the guests a save holds against the restored slides: a guest doing <see cref="PoolSlideActivity.ActivityId"/> is in a phase that exists, on a slide whose tower has this thing id, and on a complete
	/// slide when it is riding or splashing. Throws <see cref="InvalidDataException"/> otherwise, so a forged save cannot send a guest down a track that is not there.
	/// </summary>
	public void ValidateGuests( IEnumerable<Guest> guests )
	{
		foreach ( var guest in guests )
		{
			if ( guest.State != GuestState.PoolActivity || guest.PoolActivityId != PoolSlideActivity.ActivityId )
				continue;
			if ( guest.PoolActivityPhase is < PoolSlidePhases.Approach or > PoolSlidePhases.Splash )
				throw new InvalidDataException( $"Guest {guest.Id} is on a slide in phase {guest.PoolActivityPhase}, which does not exist." );
			var slide = FindByThing( guest.PoolActivityTarget );
			if ( slide == null )
				throw new InvalidDataException( $"Guest {guest.Id} is on the slide of thing {guest.PoolActivityTarget}, which the park does not hold." );
			if ( guest.PoolActivityPhase >= PoolSlidePhases.Ride && !slide.IsComplete )
				throw new InvalidDataException( $"Guest {guest.Id} rides a slide that is not finished." );
		}
	}

	/// <summary>Adds the slides to the canonical hash: for each slide in tower order its key, profile, start, pieces and heights. Nothing for a park without slides.</summary>
	public void AddCanonicalState( StateHasher hash )
	{
		if ( slides.Count == 0 )
			return;
		hash.Add( slides.Count );
		foreach ( var slide in slides.Values )
		{
			hash.Add( slide.Id );
			hash.Add( (int)slide.Profile );
			hash.Add( slide.StartX );
			hash.Add( slide.StartY );
			hash.Add( slide.StartHeading );
			hash.Add( slide.TopHeight );
			hash.Add( slide.EndHeight );
			hash.Add( slide.Pieces.Count );
			foreach ( var piece in slide.Pieces )
				hash.Add( (int)piece );
		}
	}
}
