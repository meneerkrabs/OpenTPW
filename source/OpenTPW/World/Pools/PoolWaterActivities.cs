namespace OpenTPW;

/// <summary>The numbers of the water attractions (docs/POOLS.md, "Water attractions"). All of them are OpenTPW's own: the original game has no pools.</summary>
// [EXT:pools-attractions] OpenTPW's own numbers; the original game has no pools
public static class PoolAttractionSettings
{
	/// <summary>The most park turns a swimmer swims towards an attraction before it gives up and swims on (another swimmer is in the way, the water is a maze).</summary>
	public const int MaximumApproachTurns = 80;
	/// <summary>The rope swing: turns to climb onto the platform.</summary>
	public const int SwingClimbTurns = 10;
	/// <summary>The rope swing: turns of the swing out over the water (3.5 seconds).</summary>
	public const int SwingTurns = 14;
	/// <summary>The rope swing: turns of the splash after the landing.</summary>
	public const int SplashTurns = 6;
	/// <summary>The rope swing: the height of the platform the guest swings from, in engine units (a guest is 2.0 tall).</summary>
	public const float SwingPlatformHeight = 0.9f;
	/// <summary>The rope swing: how much further than the water-side cell's middle the guest lets go, in 16.16 cells.</summary>
	public const int SwingReach = Fixed16.One / 2;
	/// <summary>The happiness a rope swing adds when it ends (meter 0 to 100).</summary>
	public const float SwingHappiness = 6f;
	/// <summary>The jets: swimmers under one at a time.</summary>
	public const int JetCapacity = 3;
	/// <summary>The jets: the shortest stay under a jet, in turns (15 seconds), plus a draw below <see cref="JetExtraTurns"/>.</summary>
	public const int JetMinimumTurns = 60;
	public const int JetExtraTurns = 60;
	/// <summary>The jets: the happiness paid every <see cref="PoolWaves.PeriodTurns"/> turns under a jet, and when the stay ends.</summary>
	public const float JetHappinessPerPeriod = 0.5f;
	public const float JetHappiness = 3f;
	/// <summary>The jets' spray reaches this many cells from the water-side cell (Chebyshev): swimmers inside it, under a jet or not, are paid <see cref="SprayHappiness"/> every <see cref="SprayPeriodTurns"/> turns.</summary>
	public const int SprayRadius = 2;
	public const int SprayPeriodTurns = 12;
	public const float SprayHappiness = 0.3f;
	/// <summary>The spa: seats on its one tile.</summary>
	public const int SpaSeats = 8;
	/// <summary>The spa: how far from the middle of the tile the seats are, in 16.16 cells.</summary>
	public const int SpaSeatRadius = (int)(0.27 * Fixed16.One);
	/// <summary>The spa: the shortest sit, in turns (20 seconds), plus a draw below <see cref="SpaExtraTurns"/>.</summary>
	public const int SpaMinimumTurns = 80;
	public const int SpaExtraTurns = 120;
	/// <summary>The spa: the happiness paid every <see cref="PoolWaves.PeriodTurns"/> turns, and when the sit ends.</summary>
	public const float SpaHappinessPerPeriod = 0.4f;
	public const float SpaHappiness = 8f;
}

/// <summary>Helpers the water attractions share.</summary>
// [EXT:pools-attractions] OpenTPW's own water attractions; the original game has no pools
internal static class PoolActivityHelpers
{
	/// <summary>The swim region of the guest's changing room, or null when the room or its pool is gone.</summary>
	public static PoolSwimRegion? RegionOf( GuestSimulation simulation, Guest guest )
	{
		foreach ( var ride in simulation.Attractions )
			if ( ride.AttractionId == guest.AttractionId )
				return simulation.SwimRegionOf( ride );
		return null;
	}

	/// <summary>How many guests are doing this activity for this target now.</summary>
	public static int Occupancy( GuestSimulation simulation, string activityId, int targetId )
	{
		var count = 0;
		foreach ( var other in simulation.Guests )
			if ( other.State == GuestState.PoolActivity && other.PoolActivityTarget == targetId && other.PoolActivityId == activityId )
				count++;
		return count;
	}

	/// <summary>The site nearest to the guest (ties by lower object id).</summary>
	public static PoolAttractionSite? Nearest( IEnumerable<PoolAttractionSite> sites, Guest guest )
	{
		PoolAttractionSite? best = null;
		var bestDistance = long.MaxValue;
		foreach ( var site in sites )
		{
			var (cx, cy) = PoolSwimRegion.Centre( site.Access );
			var distance = Fixed16.Octile( guest.Navigation.X - cx, guest.Navigation.Y - cy );
			if ( distance < bestDistance || (distance == bestDistance && best != null && site.ObjectId < best.ObjectId) )
				(best, bestDistance) = (site, distance);
		}
		return best;
	}

	/// <summary>
	/// One swimming step of the guest towards a point (16.16) with the swimmer's own steering, ignoring the crowd (the guest is on its way to something that is its own). True once it is within the goal radius.
	/// Draws from <paramref name="random"/> only.
	/// </summary>
	public static bool Approach( GuestSimulation simulation, Guest guest, PoolSwimRegion region, (int X, int Y) goal, ref uint random )
	{
		var navigation = guest.Navigation;
		if ( Fixed16.Octile( goal.X - navigation.X, goal.Y - navigation.Y ) < PoolSwimSettings.GoalRadius )
			return true;
		var body = new PoolSwimBody { X = navigation.X, Y = navigation.Y, Heading = guest.SwimHeading, GoalX = goal.X, GoalY = goal.Y, HasGoal = true, Random = random };
		PoolSwimming.Step( region, ref body, ReadOnlySpan<(int X, int Y)>.Empty, guest.BaseSpeed * PoolSwimSettings.SpeedPerBaseSpeed, ignoreOthers: true );
		random = body.Random;
		guest.SwimHeading = body.Heading;
		simulation.MovePoolGuest( guest, body.X, body.Y );
		return Fixed16.Octile( goal.X - body.X, goal.Y - body.Y ) < PoolSwimSettings.GoalRadius;
	}

	public static void Gain( Guest guest, float amount ) => guest.Happiness = MeterMath.Add( guest.Happiness, amount );

	/// <summary>A point a fraction (0 to 64) of the way from a to b, 16.16.</summary>
	public static (int X, int Y) Lerp( (int X, int Y) a, (int X, int Y) b, int sixtyFourths ) =>
		(a.X + (int)((long)(b.X - a.X) * sixtyFourths / 64), a.Y + (int)((long)(b.Y - a.Y) * sixtyFourths / 64));
}

/// <summary>
/// The rope swing (<c>attraction.ropeswing</c>, docs/POOLS.md, "Rope swing"): a swimmer swims to the water side of a rope swing nobody is using, climbs onto its platform (sprite slot 3), swings out
/// over the water (slot 14), lets go and splashes in (slot 11), and is back in the water. One guest at a time. Phases: 0 swimming to the steps, 1 climbing, 2 swinging, 3 splashing; the guest's
/// <c>PoolActivityTurns</c> count the turns of the phase, and all else (the platform, the landing) is derived from the object, so a save round-trips mid-swing.
/// </summary>
// [EXT:pools-attractions] OpenTPW's own rope swing; the original game has no pools
public sealed class RopeSwingActivity : IPoolActivity
{
	public const string ActivityId = "attraction.ropeswing";
	private readonly PoolAttractionSites sites;

	public RopeSwingActivity( PoolAttractionSites sites ) => this.sites = sites ?? throw new ArgumentNullException( nameof( sites ) );

	public string Id => ActivityId;

	public PoolActivityOffer? Offer( GuestSimulation simulation, Guest guest, PoolSwimRegion region )
	{
		var free = sites.In( region, PoolAttractionKind.RopeSwing ).Where( site => PoolActivityHelpers.Occupancy( simulation, Id, site.ObjectId ) == 0 );
		return PoolActivityHelpers.Nearest( free, guest ) is { } chosen ? new PoolActivityOffer( chosen.ObjectId, chosen.Access.X, chosen.Access.Y ) : null;
	}

	public bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random ) => sites.Find( offer.TargetId ) != null && PoolActivityHelpers.Occupancy( simulation, Id, offer.TargetId ) == 1;

	public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random )
	{
		if ( sites.Find( guest.PoolActivityTarget ) is not { } site || PoolActivityHelpers.RegionOf( simulation, guest ) is not { } region )
			return PoolActivityStep.BackInWater;
		var steps = PoolSwimRegion.Centre( site.Access );
		var platform = site.CentreFixed;
		guest.PoolActivityTurns++;
		switch ( guest.PoolActivityPhase )
		{
			case 0:
				if ( guest.PoolActivityTurns > PoolAttractionSettings.MaximumApproachTurns )
					return PoolActivityStep.BackInWater;
				if ( PoolActivityHelpers.Approach( simulation, guest, region, steps, ref random ) )
				{
					guest.PoolActivityPhase = 1;
					guest.PoolActivityTurns = 0;
					simulation.PlacePoolGuest( guest, platform.X, platform.Y );
				}
				return PoolActivityStep.Continue;
			case 1:
				if ( guest.PoolActivityTurns >= PoolAttractionSettings.SwingClimbTurns )
				{
					guest.PoolActivityPhase = 2;
					guest.PoolActivityTurns = 0;
				}
				return PoolActivityStep.Continue;
			case 2:
				// out along the line from the platform over the water-side cell, to a point a little beyond its middle when that is free water
				var landing = Landing( region, platform, steps );
				var (x, y) = PoolActivityHelpers.Lerp( platform, landing, guest.PoolActivityTurns * 64 / PoolAttractionSettings.SwingTurns );
				simulation.MovePoolGuest( guest, x, y );
				if ( guest.PoolActivityTurns >= PoolAttractionSettings.SwingTurns )
				{
					guest.PoolActivityPhase = 3;
					guest.PoolActivityTurns = 0;
				}
				return PoolActivityStep.Continue;
			default:
				if ( guest.PoolActivityTurns >= PoolAttractionSettings.SplashTurns )
				{
					PoolActivityHelpers.Gain( guest, PoolAttractionSettings.SwingHappiness );
					return PoolActivityStep.BackInWater;
				}
				return PoolActivityStep.Continue;
		}
	}

	/// <summary>Where the guest lets go: past the middle of the water-side cell, in the direction of the swing, when that is free water; else the middle of the cell.</summary>
	private static (int X, int Y) Landing( PoolSwimRegion region, (int X, int Y) platform, (int X, int Y) steps )
	{
		var (dx, dy) = (Math.Sign( steps.X - platform.X ), Math.Sign( steps.Y - platform.Y ));
		var beyond = (X: steps.X + dx * PoolAttractionSettings.SwingReach, Y: steps.Y + dy * PoolAttractionSettings.SwingReach);
		return region.IsWater( beyond.X, beyond.Y ) ? beyond : steps;
	}

	public int SpriteSlot( Guest guest ) => guest.PoolActivityPhase switch
	{
		0 => PoolSwimSlots.Crawl,
		1 => PoolSwimSlots.LadderClimb,
		2 => PoolSwimSlots.RopeSwing,
		_ => PoolSwimSlots.SplashDown
	};

	/// <summary>The guest stands on the platform while climbing (rising), swings down from it, and lands on the water.</summary>
	public float? FeetHeight( GuestSimulation simulation, Guest guest, float defaultHeight ) => defaultHeight + guest.PoolActivityPhase switch
	{
		1 => PoolAttractionSettings.SwingPlatformHeight * guest.PoolActivityTurns / PoolAttractionSettings.SwingClimbTurns,
		2 => PoolAttractionSettings.SwingPlatformHeight * (1f - (float)guest.PoolActivityTurns / PoolAttractionSettings.SwingTurns)
			+ 0.25f * MathF.Sin( MathF.PI * guest.PoolActivityTurns / PoolAttractionSettings.SwingTurns ),
		_ => 0f
	};

	public void Abort( GuestSimulation simulation, Guest guest )
	{
		// nothing is held: the platform and the rope belong to the object, which is not told who is on it
	}
}

/// <summary>
/// The jets (<c>attraction.jets</c>, docs/POOLS.md, "Splash cannon and water jets"): a swimmer swims to the water side of a jet that has room (<see cref="PoolAttractionSettings.JetCapacity"/>),
/// stays under its spray for 15 to 30 seconds (treading water, slot 10, or sitting, slot 12, drawn at the start) and swims on, happier. Phases: 0 swimming to the jet, 1 treading, 2 sitting;
/// <c>PoolActivityTurns</c> count the turns of the approach, then the turns left under the jet.
/// </summary>
// [EXT:pools-attractions] OpenTPW's own jets; the original game has no pools
public sealed class JetsActivity : IPoolActivity
{
	public const string ActivityId = "attraction.jets";
	private readonly PoolAttractionSites sites;

	public JetsActivity( PoolAttractionSites sites ) => this.sites = sites ?? throw new ArgumentNullException( nameof( sites ) );

	public string Id => ActivityId;

	public PoolActivityOffer? Offer( GuestSimulation simulation, Guest guest, PoolSwimRegion region )
	{
		var room = sites.In( region, PoolAttractionKind.Jets ).Where( site => PoolActivityHelpers.Occupancy( simulation, Id, site.ObjectId ) < PoolAttractionSettings.JetCapacity );
		return PoolActivityHelpers.Nearest( room, guest ) is { } chosen ? new PoolActivityOffer( chosen.ObjectId, chosen.Access.X, chosen.Access.Y ) : null;
	}

	public bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random ) =>
		sites.Find( offer.TargetId ) != null && PoolActivityHelpers.Occupancy( simulation, Id, offer.TargetId ) <= PoolAttractionSettings.JetCapacity;

	public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random )
	{
		if ( sites.Find( guest.PoolActivityTarget ) is not { } site || PoolActivityHelpers.RegionOf( simulation, guest ) is not { } region )
			return PoolActivityStep.BackInWater;
		if ( guest.PoolActivityPhase == 0 )
		{
			guest.PoolActivityTurns++;
			if ( guest.PoolActivityTurns > PoolAttractionSettings.MaximumApproachTurns )
				return PoolActivityStep.BackInWater;
			if ( PoolActivityHelpers.Approach( simulation, guest, region, Spot( region, site, guest ), ref random ) )
			{
				// two draws of the swimmer's own stream: how long, and treading or sitting
				guest.PoolActivityTurns = PoolAttractionSettings.JetMinimumTurns + (int)(PoolSwimming.Next( ref random ) % PoolAttractionSettings.JetExtraTurns);
				guest.PoolActivityPhase = (PoolSwimming.Next( ref random ) & 1) == 0 ? 1 : 2;
			}
			return PoolActivityStep.Continue;
		}
		if ( --guest.PoolActivityTurns % PoolWaves.PeriodTurns == 0 )
			PoolActivityHelpers.Gain( guest, PoolAttractionSettings.JetHappinessPerPeriod );
		if ( guest.PoolActivityTurns > 0 )
			return PoolActivityStep.Continue;
		PoolActivityHelpers.Gain( guest, PoolAttractionSettings.JetHappiness );
		return PoolActivityStep.BackInWater;
	}

	/// <summary>Where a guest stands under the jet: the middle of the water-side cell, shifted a little by the guest's id so up to three do not stand on one point; the middle when that is not free water.</summary>
	private static (int X, int Y) Spot( PoolSwimRegion region, PoolAttractionSite site, Guest guest )
	{
		var (cx, cy) = PoolSwimRegion.Centre( site.Access );
		var (dx, dy) = PoolSwimming.Direction( (guest.Id % PoolAttractionSettings.JetCapacity) * 21 );
		var spot = (X: cx + (dx >> 2), Y: cy + (dy >> 2));
		return region.IsWater( spot.X, spot.Y ) ? spot : (cx, cy);
	}

	public int SpriteSlot( Guest guest ) => guest.PoolActivityPhase == 2 ? PoolSwimSlots.SittingInWater : guest.PoolActivityPhase == 1 ? PoolSwimSlots.TreadWater : PoolSwimSlots.Crawl;

	public void Abort( GuestSimulation simulation, Guest guest )
	{
	}
}

/// <summary>
/// The spa pool (<c>attraction.spa</c>, docs/POOLS.md, "Spa pool"): a swimmer swims to the water side of a spa with a free seat (eight on its one tile), steps out onto the seat, sits in it (slot 12) for
/// 20 to 50 seconds and steps back into the pool, happier. The seat is the guest's <c>PoolActivityX/Y</c>, taken when the activity starts. Phases: 0 swimming to the spa, 1 sitting, 2 stepping back.
/// </summary>
// [EXT:pools-attractions] OpenTPW's own spa pool; the original game has no pools
public sealed class SpaActivity : IPoolActivity
{
	public const string ActivityId = "attraction.spa";
	private readonly PoolAttractionSites sites;

	public SpaActivity( PoolAttractionSites sites ) => this.sites = sites ?? throw new ArgumentNullException( nameof( sites ) );

	public string Id => ActivityId;

	/// <summary>The seat positions of a spa whose tile has the middle (16.16): eight points on a circle, seat 0 to the east.</summary>
	public static (int X, int Y) Seat( (int X, int Y) middle, int index )
	{
		var (dx, dy) = PoolSwimming.Direction( index * (PoolSwimSettings.Headings / PoolAttractionSettings.SpaSeats) );
		return (middle.X + (int)((long)dx * PoolAttractionSettings.SpaSeatRadius >> 16), middle.Y + (int)((long)dy * PoolAttractionSettings.SpaSeatRadius >> 16));
	}

	public PoolActivityOffer? Offer( GuestSimulation simulation, Guest guest, PoolSwimRegion region )
	{
		var room = sites.In( region, PoolAttractionKind.Spa ).Where( site => PoolActivityHelpers.Occupancy( simulation, Id, site.ObjectId ) < PoolAttractionSettings.SpaSeats );
		return PoolActivityHelpers.Nearest( room, guest ) is { } chosen ? new PoolActivityOffer( chosen.ObjectId, chosen.Access.X, chosen.Access.Y ) : null;
	}

	public bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random )
	{
		if ( sites.Find( offer.TargetId ) is not { } site )
			return false;
		// the lowest seat nobody has taken (the other guests' seats are their PoolActivityX/Y)
		var middle = site.CentreFixed;
		for ( var index = 0; index < PoolAttractionSettings.SpaSeats; index++ )
		{
			var seat = Seat( middle, index );
			if ( simulation.Guests.Any( other => other != guest && other.State == GuestState.PoolActivity && other.PoolActivityId == Id && other.PoolActivityTarget == offer.TargetId
				&& (other.PoolActivityX, other.PoolActivityY) == seat ) )
				continue;
			(guest.PoolActivityX, guest.PoolActivityY) = seat;
			return true;
		}
		return false;
	}

	public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random )
	{
		if ( sites.Find( guest.PoolActivityTarget ) is not { } site || PoolActivityHelpers.RegionOf( simulation, guest ) is not { } region )
			return PoolActivityStep.BackInWater;
		var steps = PoolSwimRegion.Centre( site.Access );
		switch ( guest.PoolActivityPhase )
		{
			case 0:
				guest.PoolActivityTurns++;
				if ( guest.PoolActivityTurns > PoolAttractionSettings.MaximumApproachTurns )
					return PoolActivityStep.BackInWater;
				if ( PoolActivityHelpers.Approach( simulation, guest, region, steps, ref random ) )
				{
					guest.PoolActivityPhase = 1;
					guest.PoolActivityTurns = PoolAttractionSettings.SpaMinimumTurns + (int)(PoolSwimming.Next( ref random ) % PoolAttractionSettings.SpaExtraTurns);
					simulation.PlacePoolGuest( guest, guest.PoolActivityX, guest.PoolActivityY );
				}
				return PoolActivityStep.Continue;
			case 1:
				if ( --guest.PoolActivityTurns % PoolWaves.PeriodTurns == 0 )
					PoolActivityHelpers.Gain( guest, PoolAttractionSettings.SpaHappinessPerPeriod );
				if ( guest.PoolActivityTurns <= 0 )
				{
					guest.PoolActivityPhase = 2;
					guest.PoolActivityTurns = 0;
				}
				return PoolActivityStep.Continue;
			default:
				PoolActivityHelpers.Gain( guest, guest.PoolActivityTurns == 0 ? PoolAttractionSettings.SpaHappiness : 0f );
				guest.PoolActivityTurns++;
				simulation.PlacePoolGuest( guest, steps.X, steps.Y );
				return PoolActivityStep.BackInWater;
		}
	}

	public int SpriteSlot( Guest guest ) => guest.PoolActivityPhase == 1 ? PoolSwimSlots.SittingInWater : PoolSwimSlots.Crawl;

	/// <summary>A seated guest sits in the water of the tub: a little above the ground of the tile.</summary>
	public float? FeetHeight( GuestSimulation simulation, Guest guest, float defaultHeight ) => guest.PoolActivityPhase == 1 ? defaultHeight + 0.35f : null;

	public void Abort( GuestSimulation simulation, Guest guest )
	{
	}
}

/// <summary>
/// The lazy river (<c>attraction.river</c>, docs/POOLS.md, "Lazy river"): a swimmer in a river cell (<see cref="PoolRiverMap"/>) may let the current take it: it floats in a ring (slot 13) at the current's
/// speed, round the corners, for 30 to 60 seconds and swims on, happier. Not an object: the river is the pool cells painted with the river option. The guest's position is the only state besides the turns left.
/// </summary>
// [EXT:pools-river] OpenTPW's own lazy river; the original game has no pools
public sealed class RiverActivity : IPoolActivity
{
	public const string ActivityId = "attraction.river";

	public string Id => ActivityId;

	public PoolActivityOffer? Offer( GuestSimulation simulation, Guest guest, PoolSwimRegion region )
	{
		if ( !region.Contains( guest.CellX, guest.CellY ) || !PoolRiverMap.Of( simulation.Grid.Cells ).IsRiver( guest.CellX, guest.CellY ) )
			return null;
		return new PoolActivityOffer( 0, guest.CellX, guest.CellY );
	}

	public bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random )
	{
		guest.PoolActivityTurns = PoolRiverSettings.MinimumTurns + (int)(PoolSwimming.Next( ref random ) % PoolRiverSettings.ExtraTurns);
		return true;
	}

	public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random )
	{
		if ( PoolActivityHelpers.RegionOf( simulation, guest ) is not { } region )
			return PoolActivityStep.BackInWater;
		var navigation = guest.Navigation;
		int x = navigation.X, y = navigation.Y;
		if ( --guest.PoolActivityTurns <= 0 || !PoolRiverMap.Of( simulation.Grid.Cells ).Float( ref x, ref y, PoolRiverSettings.FlowSpeed ) || !region.IsWater( x, y ) )
		{
			PoolActivityHelpers.Gain( guest, PoolRiverSettings.HappinessGain );
			return PoolActivityStep.BackInWater;
		}
		simulation.MovePoolGuest( guest, x, y );
		return PoolActivityStep.Continue;
	}

	public int SpriteSlot( Guest guest ) => PoolSwimSlots.FloatingInRing;

	public void Abort( GuestSimulation simulation, Guest guest )
	{
	}
}
