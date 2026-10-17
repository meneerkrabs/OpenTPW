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
	/// <summary>A landing waits at the top (the rope swing's platform, the diving board's end, the rim of a ladder) while another body is in the water within this distance of the landing point (0.7 cell, 16.16).</summary>
	public const int LandingZoneRadius = (int)(0.7 * Fixed16.One);
	/// <summary>The most park turns a landing waits for its zone to be free (40 turns, 10 seconds) before it goes anyway, so a zone nobody leaves cannot hold a guest for ever.</summary>
	public const int LandingWaitTurns = 40;
	/// <summary>The jets: where a guest stands under one, from the middle of the water-side cell (0.42 cell, 16.16): three spots a third of a circle apart are 0.73 cell from each other.</summary>
	public const int JetSpotRadius = (int)(0.42 * Fixed16.One);
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
	/// <summary>
	/// The spa: seats on its one tile. One, in the middle of the tub: the swimwear sprite of a seated guest (slot 12) is up to 0.729 engine units (0.36 cell) wide each side of its hotspot below the tub's rim,
	/// the basin's inner wall is 0.74 engine units (0.37 cell) from the middle at its flat sides (<c>tools/blender/opentpw_pool_attractions.py</c>, <c>SPA_INNER</c>; <c>PoolsSpaGeometryTests</c>), so any seat off
	/// the middle puts the guest's shoulders through the wall (eight seats on a 0.27 cell circle did).
	/// </summary>
	public const int SpaSeats = 1;
	/// <summary>The spa: how far from the middle of the tile the seats are, in 16.16 cells (0: the one seat is the middle).</summary>
	public const int SpaSeatRadius = 0;
	/// <summary>The spa: the water surface of the tub above the deck, in engine units: 5.2 model units (<c>SPA_WATER</c>), 0.2 engine units each. A seated guest's torso (the sprite's hotspot) is at the water line.</summary>
	public const float SpaWaterHeight = 1.04f;
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
	public static PoolSwimRegion? RegionOf( GuestSimulation simulation, Guest guest ) =>
		simulation.SwimRoomOf( guest ) is { } ride ? simulation.SwimRegionOf( ride ) : null;

	private sealed class OccupancyCache
	{
		public long Tick = -1;
		public readonly Dictionary<(string Activity, int Target), int> Counts = new();
	}

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GuestSimulation, OccupancyCache> occupancies = new();

	private static OccupancyCache OccupancyOf( GuestSimulation simulation )
	{
		var occupancy = occupancies.GetOrCreateValue( simulation );
		// rebuilt once per tick; within a tick the activities keep it exact themselves (Started, Left)
		if ( occupancy.Tick != simulation.TickCount )
		{
			occupancy.Tick = simulation.TickCount;
			occupancy.Counts.Clear();
			foreach ( var other in simulation.Guests )
				if ( other.State == GuestState.PoolActivity && other.PoolActivityId is { } id )
					occupancy.Counts[(id, other.PoolActivityTarget)] = occupancy.Counts.GetValueOrDefault( (id, other.PoolActivityTarget) ) + 1;
		}
		return occupancy;
	}

	/// <summary>How many guests are doing this activity for this target now (counted once per tick, not per swimmer and offer).</summary>
	public static int Occupancy( GuestSimulation simulation, string activityId, int targetId ) =>
		OccupancyOf( simulation ).Counts.GetValueOrDefault( (activityId, targetId) );

	/// <summary>
	/// A guest is starting the activity for the target (the host has set its state): the exact number of others doing it, by one scan (starts are rare, offers are not), and the tick's count set to that plus the
	/// guest so the swimmers after it this tick see it.
	/// </summary>
	public static int Starting( GuestSimulation simulation, Guest guest, string activityId, int targetId )
	{
		var others = 0;
		foreach ( var other in simulation.Guests )
			if ( other != guest && other.State == GuestState.PoolActivity && other.PoolActivityTarget == targetId && other.PoolActivityId == activityId )
				others++;
		OccupancyOf( simulation ).Counts[(activityId, targetId)] = others + 1;
		return others;
	}

	/// <summary>A guest left the activity this tick (back in the water, session over, aborted).</summary>
	public static void Left( GuestSimulation simulation, string activityId, int targetId )
	{
		var occupancy = OccupancyOf( simulation );
		if ( occupancy.Counts.TryGetValue( (activityId, targetId), out var count ) )
			occupancy.Counts[(activityId, targetId)] = Math.Max( 0, count - 1 );
	}

	/// <summary>
	/// One swimming step of the guest towards a point (16.16) with the swimmer's own steering, keeping its distance from the other bodies in the water until it is within <see cref="PoolSwimSettings.CrowdedRadius"/> of the goal (so a crowd at
	/// the target cannot block it). True once it is within the goal radius.
	/// Draws from <paramref name="random"/> only.
	/// </summary>
	public static bool Approach( GuestSimulation simulation, Guest guest, PoolSwimRegion region, (int X, int Y) goal, ref uint random )
	{
		var navigation = guest.Navigation;
		if ( Fixed16.Octile( goal.X - navigation.X, goal.Y - navigation.Y ) < PoolSwimSettings.GoalRadius )
			return true;
		var body = new PoolSwimBody { X = navigation.X, Y = navigation.Y, Heading = guest.SwimHeading, GoalX = goal.X, GoalY = goal.Y, HasGoal = true, Random = random };
		var near = Fixed16.Octile( goal.X - navigation.X, goal.Y - navigation.Y ) < PoolSwimSettings.CrowdedRadius;
		PoolSwimming.Step( region, ref body, near ? ReadOnlySpan<(int X, int Y)>.Empty : simulation.WaterBodyPoints( region, guest, navigation.X, navigation.Y ), guest.BaseSpeed * PoolSwimSettings.SpeedPerBaseSpeed, ignoreOthers: near );
		random = body.Random;
		guest.SwimHeading = body.Heading;
		simulation.MovePoolGuest( guest, body.X, body.Y );
		return Fixed16.Octile( goal.X - body.X, goal.Y - body.Y ) < PoolSwimSettings.GoalRadius;
	}

	/// <summary>Whether another body is in the water within <see cref="PoolAttractionSettings.LandingZoneRadius"/> of a landing point (16.16): a landing waits while it is.</summary>
	public static bool LandingBusy( GuestSimulation simulation, Guest guest, (int X, int Y) point ) =>
		RegionOf( simulation, guest ) is { } region && simulation.IsWaterBodyNear( region, guest, point.X, point.Y, PoolAttractionSettings.LandingZoneRadius );

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
		var chosen = sites.NearestIn( region, PoolAttractionKind.RopeSwing, guest.Navigation.X, guest.Navigation.Y, site => PoolActivityHelpers.Occupancy( simulation, Id, site.ObjectId ) == 0 );
		return chosen != null ? new PoolActivityOffer( chosen.ObjectId, chosen.Access.X, chosen.Access.Y ) : null;
	}

	public bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random ) =>
		sites.Find( offer.TargetId ) != null && PoolActivityHelpers.Starting( simulation, guest, Id, offer.TargetId ) == 0;

	public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random )
	{
		var target = guest.PoolActivityTarget;
		var step = UpdateCore( simulation, guest, ref random );
		if ( step != PoolActivityStep.Continue )
			PoolActivityHelpers.Left( simulation, Id, target );
		return step;
	}

	private PoolActivityStep UpdateCore( GuestSimulation simulation, Guest guest, ref uint random )
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
				// at the top: wait (up to LandingWaitTurns) while a body is in the water at the landing point
				if ( guest.PoolActivityTurns >= PoolAttractionSettings.SwingClimbTurns
					&& (guest.PoolActivityTurns >= PoolAttractionSettings.SwingClimbTurns + PoolAttractionSettings.LandingWaitTurns || !PoolActivityHelpers.LandingBusy( simulation, guest, Landing( region, platform, steps ) )) )
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

	/// <summary>A body in the water while swimming to the steps (phase 0) and after the landing (phase 3); on the platform and in the air it is not.</summary>
	public bool InWater( GuestSimulation simulation, Guest guest ) => guest.PoolActivityPhase is 0 or 3;

	/// <summary>The guest stands on the platform while climbing (rising), swings down from it, and lands on the water.</summary>
	public float? FeetHeight( GuestSimulation simulation, Guest guest, PoolFeetHeights heights )
	{
		var platform = heights.Deck + PoolAttractionSettings.SwingPlatformHeight;
		return guest.PoolActivityPhase switch
		{
			// up the ladder from the deck to the platform
			1 => heights.Deck + PoolAttractionSettings.SwingPlatformHeight * Math.Min( guest.PoolActivityTurns, PoolAttractionSettings.SwingClimbTurns ) / PoolAttractionSettings.SwingClimbTurns,
			// down from the platform to the water in an arc, then the splash at the water's surface
			2 => platform + (heights.Water - platform) * guest.PoolActivityTurns / PoolAttractionSettings.SwingTurns + 0.25f * MathF.Sin( MathF.PI * guest.PoolActivityTurns / PoolAttractionSettings.SwingTurns ),
			3 => heights.Water,
			_ => null
		};
	}

	public void Abort( GuestSimulation simulation, Guest guest ) => PoolActivityHelpers.Left( simulation, Id, guest.PoolActivityTarget );
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
		var chosen = sites.NearestIn( region, PoolAttractionKind.Jets, guest.Navigation.X, guest.Navigation.Y, site => PoolActivityHelpers.Occupancy( simulation, Id, site.ObjectId ) < PoolAttractionSettings.JetCapacity );
		return chosen != null ? new PoolActivityOffer( chosen.ObjectId, chosen.Access.X, chosen.Access.Y ) : null;
	}

	/// <summary>
	/// The guest takes the first spot under the jet that is free water and at least <see cref="PoolSwimSettings.MinimumDistance"/> from the spots of the guests already at this jet, and keeps it in its <see cref="Guest.PoolActivityX"/>,
	/// <see cref="Guest.PoolActivityY"/> for the whole activity; with no such spot the jet is full and the guest swims on.
	/// </summary>
	public bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random )
	{
		if ( sites.Find( offer.TargetId ) is not { } site || PoolActivityHelpers.RegionOf( simulation, guest ) is not { } region || PoolActivityHelpers.Starting( simulation, guest, Id, offer.TargetId ) >= PoolAttractionSettings.JetCapacity )
			return false;
		foreach ( var spot in Spots( site ) )
		{
			if ( !region.IsWater( spot.X, spot.Y ) )
				continue;
			var free = true;
			foreach ( var other in simulation.Guests )
				if ( other != guest && other.State == GuestState.PoolActivity && other.PoolActivityTarget == offer.TargetId && other.PoolActivityId == Id
					&& Fixed16.Length( other.PoolActivityX - spot.X, other.PoolActivityY - spot.Y ) < PoolSwimSettings.MinimumDistance )
				{
					free = false;
					break;
				}
			if ( !free )
				continue;
			(guest.PoolActivityX, guest.PoolActivityY) = spot;
			return true;
		}
		// no free spot: undo the count of this start
		PoolActivityHelpers.Left( simulation, Id, offer.TargetId );
		return false;
	}

	/// <summary>
	/// The candidate spots under a jet, in the order they are taken: three a third of a circle apart (<see cref="PoolAttractionSettings.JetSpotRadius"/> from the middle of the water-side cell), three between those,
	/// then two wider rings of sixteen (the spray reaches a cell and more), then the middle. A spot beside the rim is not free water, so the next one is taken.
	/// </summary>
	private static IEnumerable<(int X, int Y)> Spots( PoolAttractionSite site )
	{
		var (cx, cy) = PoolSwimRegion.Centre( site.Access );
		foreach ( var heading in new[] { 0, 21, 43, 11, 32, 54 } )
		{
			var (dx, dy) = PoolSwimming.Direction( heading );
			yield return (cx + Fixed16.Mul( dx, PoolAttractionSettings.JetSpotRadius ), cy + Fixed16.Mul( dy, PoolAttractionSettings.JetSpotRadius ));
		}
		foreach ( var radius in new[] { PoolAttractionSettings.JetSpotRadius * 2, PoolAttractionSettings.JetSpotRadius * 3 } )
			for ( var heading = 0; heading < PoolSwimSettings.Headings; heading += 4 )
			{
				var (dx, dy) = PoolSwimming.Direction( heading );
				yield return (cx + Fixed16.Mul( dx, radius ), cy + Fixed16.Mul( dy, radius ));
			}
		yield return (cx, cy);
	}

	/// <summary>A body in the water at every phase: swimming to the jet and standing under it.</summary>
	public bool InWater( GuestSimulation simulation, Guest guest ) => true;

	public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random )
	{
		var target = guest.PoolActivityTarget;
		var step = UpdateCore( simulation, guest, ref random );
		if ( step != PoolActivityStep.Continue )
			PoolActivityHelpers.Left( simulation, Id, target );
		return step;
	}

	private PoolActivityStep UpdateCore( GuestSimulation simulation, Guest guest, ref uint random )
	{
		if ( sites.Find( guest.PoolActivityTarget ) is not { } site || PoolActivityHelpers.RegionOf( simulation, guest ) is not { } region )
			return PoolActivityStep.BackInWater;
		if ( guest.PoolActivityPhase == 0 )
		{
			guest.PoolActivityTurns++;
			if ( guest.PoolActivityTurns > PoolAttractionSettings.MaximumApproachTurns )
				return PoolActivityStep.BackInWater;
			if ( PoolActivityHelpers.Approach( simulation, guest, region, (guest.PoolActivityX, guest.PoolActivityY), ref random ) )
			{
				// it steps onto its spot, so the guests under the jet stand a minimum distance apart
				simulation.MovePoolGuest( guest, guest.PoolActivityX, guest.PoolActivityY );
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

	public int SpriteSlot( Guest guest ) => guest.PoolActivityPhase == 2 ? PoolSwimSlots.SittingInWater : guest.PoolActivityPhase == 1 ? PoolSwimSlots.TreadWater : PoolSwimSlots.Crawl;

	public void Abort( GuestSimulation simulation, Guest guest ) => PoolActivityHelpers.Left( simulation, Id, guest.PoolActivityTarget );
}

/// <summary>
/// The spa pool (<c>attraction.spa</c>, docs/POOLS.md, "Spa pool"): a swimmer swims to the water side of a spa with a free seat (one, in the middle of its tub), steps out onto the seat, sits in it (slot 12) for
/// 20 to 50 seconds and steps back into the pool, happier. The seat is the guest's <c>PoolActivityX/Y</c>, taken when the activity starts. Phases: 0 swimming to the spa, 1 sitting, 2 stepping back.
/// </summary>
// [EXT:pools-attractions] OpenTPW's own spa pool; the original game has no pools
public sealed class SpaActivity : IPoolActivity
{
	public const string ActivityId = "attraction.spa";
	private readonly PoolAttractionSites sites;

	public SpaActivity( PoolAttractionSites sites ) => this.sites = sites ?? throw new ArgumentNullException( nameof( sites ) );

	public string Id => ActivityId;

	/// <summary>The seat positions of a spa whose tile has the middle (16.16): <see cref="PoolAttractionSettings.SpaSeats"/> points on a circle of <see cref="PoolAttractionSettings.SpaSeatRadius"/> (with the one seat: the middle), seat 0 to the east.</summary>
	public static (int X, int Y) Seat( (int X, int Y) middle, int index )
	{
		var (dx, dy) = PoolSwimming.Direction( index * (PoolSwimSettings.Headings / PoolAttractionSettings.SpaSeats) );
		return (middle.X + (int)((long)dx * PoolAttractionSettings.SpaSeatRadius >> 16), middle.Y + (int)((long)dy * PoolAttractionSettings.SpaSeatRadius >> 16));
	}

	public PoolActivityOffer? Offer( GuestSimulation simulation, Guest guest, PoolSwimRegion region )
	{
		var chosen = sites.NearestIn( region, PoolAttractionKind.Spa, guest.Navigation.X, guest.Navigation.Y, site => PoolActivityHelpers.Occupancy( simulation, Id, site.ObjectId ) < PoolAttractionSettings.SpaSeats );
		return chosen != null ? new PoolActivityOffer( chosen.ObjectId, chosen.Access.X, chosen.Access.Y ) : null;
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
			PoolActivityHelpers.Starting( simulation, guest, Id, offer.TargetId );
			return true;
		}
		return false;
	}

	public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random )
	{
		var target = guest.PoolActivityTarget;
		var step = UpdateCore( simulation, guest, ref random );
		if ( step != PoolActivityStep.Continue )
			PoolActivityHelpers.Left( simulation, Id, target );
		return step;
	}

	private PoolActivityStep UpdateCore( GuestSimulation simulation, Guest guest, ref uint random )
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
				PoolActivityHelpers.Gain( guest, PoolAttractionSettings.SpaHappiness );
				simulation.PlacePoolGuest( guest, steps.X, steps.Y );
				return PoolActivityStep.BackInWater;
		}
	}

	/// <summary>A body in the water only while swimming to the spa (phase 0); seated, the guest is in the tub.</summary>
	public bool InWater( GuestSimulation simulation, Guest guest ) => guest.PoolActivityPhase == 0;

	public int SpriteSlot( Guest guest ) => guest.PoolActivityPhase == 1 ? PoolSwimSlots.SittingInWater : PoolSwimSlots.Crawl;

	/// <summary>A seated guest sits in the water of the tub: the torso (the sprite's hotspot, slot 12) at the water surface, <see cref="PoolAttractionSettings.SpaWaterHeight"/> above the deck.</summary>
	public float? FeetHeight( GuestSimulation simulation, Guest guest, PoolFeetHeights heights ) => guest.PoolActivityPhase == 1 ? heights.Deck + PoolAttractionSettings.SpaWaterHeight : null;

	public void Abort( GuestSimulation simulation, Guest guest ) => PoolActivityHelpers.Left( simulation, Id, guest.PoolActivityTarget );
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
		var target = guest.PoolActivityTarget;
		var step = UpdateCore( simulation, guest, ref random );
		if ( step != PoolActivityStep.Continue )
			PoolActivityHelpers.Left( simulation, Id, target );
		return step;
	}

	private PoolActivityStep UpdateCore( GuestSimulation simulation, Guest guest, ref uint random )
	{
		if ( PoolActivityHelpers.RegionOf( simulation, guest ) is not { } region )
			return PoolActivityStep.BackInWater;
		var navigation = guest.Navigation;
		int x = navigation.X, y = navigation.Y;
		// a ring broken under the floater lets it go, unpaid; only a float that ran its turns is paid
		if ( !PoolRiverMap.Of( simulation.Grid.Cells ).Float( ref x, ref y, PoolRiverSettings.FlowSpeed ) )
			return PoolActivityStep.BackInWater;
		if ( --guest.PoolActivityTurns <= 0 )
		{
			PoolActivityHelpers.Gain( guest, PoolRiverSettings.HappinessGain );
			return PoolActivityStep.BackInWater;
		}
		// a floater keeps its gap along the lane: with another body in the water ahead of it within FloatGap (a floater, or a swimmer in the lane) it only drifts, and it stands when that one is closer than FloatMinimumGap
		var (fromX, fromY) = (navigation.X, navigation.Y);
		var blocker = Blocker( simulation, guest, region, fromX, fromY, x - fromX, y - fromY );
		if ( blocker < PoolRiverSettings.FloatGap )
		{
			x = fromX;
			y = fromY;
			if ( blocker >= PoolRiverSettings.FloatMinimumGap )
				PoolRiverMap.Of( simulation.Grid.Cells ).Float( ref x, ref y, PoolRiverSettings.DriftSpeed );
			else
				return PoolActivityStep.Continue;
		}
		// the float never leaves free water: a step that would (a lane the rim narrows) is not taken this turn
		if ( region.IsWater( x, y ) )
			simulation.MovePoolGuest( guest, x, y );
		return PoolActivityStep.Continue;
	}

	/// <summary>The distance (16.16) to the nearest other body in the water ahead of the guest (a floater or a swimmer in the lane) within <see cref="PoolRiverSettings.FloatGap"/> (ahead: on the side of the step the guest is about to take), or <see cref="int.MaxValue"/> when there is none.</summary>
	private static int Blocker( GuestSimulation simulation, Guest guest, PoolSwimRegion region, int x, int y, int stepX, int stepY )
	{
		var nearest = int.MaxValue;
		var bodies = simulation.WaterBodiesNear( region, guest, x, y, PoolRiverSettings.FloatGap );
		foreach ( var other in bodies )
		{
			long offsetX = other.Navigation.X - x, offsetY = other.Navigation.Y - y;
			if ( offsetX * stepX + offsetY * stepY <= 0 )
				continue;
			var distance = Fixed16.Length( (int)offsetX, (int)offsetY );
			if ( distance < nearest )
				nearest = distance;
		}
		return nearest;
	}

	/// <summary>A floater is always a body in the water.</summary>
	public bool InWater( GuestSimulation simulation, Guest guest ) => true;

	public int SpriteSlot( Guest guest ) => PoolSwimSlots.FloatingInRing;

	public void Abort( GuestSimulation simulation, Guest guest ) => PoolActivityHelpers.Left( simulation, Id, guest.PoolActivityTarget );
}
