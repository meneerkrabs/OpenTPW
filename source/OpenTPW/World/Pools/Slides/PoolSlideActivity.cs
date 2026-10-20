namespace OpenTPW;

/// <summary>The phases of a guest riding a slide (<see cref="Guest.PoolActivityPhase"/>).</summary>
public static class PoolSlidePhases
{
	/// <summary>Swimming to the foot of the tower's stairs.</summary>
	public const int Approach = 0;
	/// <summary>Climbing the stairs (<see cref="Guest.PoolActivityTurns"/> counts the turns).</summary>
	public const int Climb = 1;
	/// <summary>Waiting on the tower's platform in line (the turns waited).</summary>
	public const int Wait = 2;
	/// <summary>On the track (the turns since the launch: the position is <see cref="PoolSlide.PositionAtTurn"/>).</summary>
	public const int Ride = 3;
	/// <summary>Splashing down in the pool (the turns since the landing).</summary>
	public const int Splash = 4;
}

/// <summary>
/// [EXT:pools-slides] Riding a water slide (docs/POOLS.md, "Water slides"), an <see cref="IPoolActivity"/>. It is offered to a swimmer whose water holds both the foot of a slide tower's stairs and the slide's
/// splash cell. The guest swims to the foot, climbs the stairs, waits on the tower's platform in line (the slide's capacity), is launched when the track is clear, rides it by gravity, splashes down
/// at the end and swims on. Riding is free. The activity keeps no state of its own per guest: the phase, the turn counter and the slide (the target) are the guest's generic activity fields, and the
/// rider's place on the track is a function of its turn counter, so a save in the middle of a ride restores exactly. It keeps no random state of its own: the only draw it makes is the swimmers' own steering step on the way to the stairs, from the swimmer's own stream.
/// </summary>
// [EXT:pools-slides] OpenTPW's own water slides; the original game has no pools
public sealed class PoolSlideActivity : IPoolActivity
{
	/// <summary>The activity's id, saved with a guest that is riding.</summary>
	public const string ActivityId = "slide.ride";

	public string Id => ActivityId;

	public PoolActivityOffer? Offer( GuestSimulation simulation, Guest guest, PoolSwimRegion region )
	{
		// asked per swimmer per turn: no allocation, and the guests are scanned only for a slide that is usable in this water
		if ( simulation.PoolSlides is not { Count: > 0 } world )
			return null;
		PoolActivityOffer? best = null;
		var bestOccupied = int.MaxValue;
		var slides = world.Ordered;
		for ( var index = 0; index < slides.Count; index++ )
		{
			var slide = slides[index];
			if ( !world.IsUsable( slide ) || world.TowerOf( slide ) is not { } tower || tower.PoolSideCell( simulation.Grid.Cells ) is not { } foot )
				continue;
			var landing = slide.LandingCell!.Value;
			if ( !region.Contains( foot.X, foot.Y ) || !region.Contains( landing.X, landing.Y ) )
				continue;
			var occupied = Occupants( simulation, tower.ThingId );
			if ( occupied >= tower.Capacity || occupied >= bestOccupied )
				continue;
			(best, bestOccupied) = (new PoolActivityOffer( tower.ThingId, foot.X, foot.Y ), occupied);
		}
		return best;
	}

	public bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random )
	{
		if ( simulation.PoolSlides?.FindByThing( offer.TargetId ) is not { } slide || !simulation.PoolSlides.IsUsable( slide ) )
			return false;
		guest.PoolActivityPhase = PoolSlidePhases.Approach;
		guest.PoolActivityTurns = 0;
		return true;
	}

	public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random )
	{
		var world = simulation.PoolSlides;
		var slide = world?.FindByThing( guest.PoolActivityTarget );
		var usable = slide != null && world!.IsUsable( slide );
		switch ( guest.PoolActivityPhase )
		{
			case PoolSlidePhases.Approach:
				return Approach( simulation, guest, slide, usable, ref random );
			case PoolSlidePhases.Climb:
				return usable ? Climb( simulation, guest, slide! ) : LeaveTower( simulation, guest, slide );
			case PoolSlidePhases.Wait:
				return usable ? Wait( simulation, guest, slide! ) : LeaveTower( simulation, guest, slide );
			case PoolSlidePhases.Ride:
				return slide is not { IsComplete: true } ? PoolActivityStep.EndSession : Ride( simulation, guest, slide );
			default:
				return slide is not { IsComplete: true } ? PoolActivityStep.EndSession : Splash( guest );
		}
	}

	public void Abort( GuestSimulation simulation, Guest guest )
	{
		// nothing is held outside the guest's own fields: the rider's place, the queue and the track are all derived from them
	}

	public int SpriteSlot( Guest guest ) => guest.PoolActivityPhase switch
	{
		PoolSlidePhases.Approach => PoolSwimSlots.Crawl,
		PoolSlidePhases.Climb => guest.PoolActivityTurns > ClimbStairsEnd ? PoolSwimSlots.Walk : PoolSwimSlots.LadderClimb,
		PoolSlidePhases.Wait => PoolSwimSlots.Stand,
		PoolSlidePhases.Ride => PoolSwimSlots.Sliding,
		_ => PoolSwimSlots.SplashDown
	};

	public float? FeetHeight( GuestSimulation simulation, Guest guest, PoolFeetHeights heights )
	{
		if ( simulation.PoolSlides?.FindByThing( guest.PoolActivityTarget ) is not { } slide )
			return null;
		var top = slide.TopHeight / (float)PoolSlide.HeightUnit;
		switch ( guest.PoolActivityPhase )
		{
			case PoolSlidePhases.Climb:
				// from the water surface to the ground at the foot of the stairs, up the flight (the stairs' height at each point of the line: the model's twelve steps are a straight rise to the platform), then on the deck
				var turns = Math.Min( guest.PoolActivityTurns, PoolSlideSettings.ClimbTurns );
				var ground = top - PoolSlideTowerModel.StairsRise;
				if ( turns <= ClimbFootEnd )
					return heights.Water + (ground - heights.Water) * turns / ClimbFootEnd;
				if ( turns <= ClimbStairsEnd )
					return ground + (top - ground) * (turns - ClimbFootEnd) / (ClimbStairsEnd - ClimbFootEnd);
				return top;
			case PoolSlidePhases.Wait:
				return top;
			case PoolSlidePhases.Ride:
				return slide.HeightAt( slide.PositionAtTurn( guest.PoolActivityTurns ) ) / (float)PoolSlide.HeightUnit + RiderLift;
			default:
				return null;
		}
	}

	/// <summary>How far above the track the rider is drawn, engine units: the sliding slot is bottom anchored, so its back rests on the track bed.</summary>
	private const float RiderLift = 0.0f;

	/// <summary>The climb's legs in turns: the foot of the stairs is reached after <see cref="ClimbFootEnd"/>, the top of the flight after <see cref="ClimbStairsEnd"/>, then one turn to the deck and one to the spot.</summary>
	private const int ClimbFootEnd = 2, ClimbStairsEnd = 10;

	private static PoolActivityStep Approach( GuestSimulation simulation, Guest guest, PoolSlide? slide, bool usable, ref uint random )
	{
		var region = simulation.SwimRegionOfGuest( guest );
		var tower = usable ? simulation.PoolSlides!.TowerOf( slide! ) : null;
		var foot = tower?.PoolSideCell( simulation.Grid.Cells );
		if ( region == null || foot == null || !region.Contains( foot.Value.X, foot.Value.Y ) || ++guest.PoolActivityTurns > PoolSlideSettings.ApproachLimitTurns )
			return PoolActivityStep.BackInWater;
		var (goalX, goalY) = PoolSwimRegion.Centre( foot.Value );
		var navigation = guest.Navigation;
		if ( Fixed16.Octile( goalX - navigation.X, goalY - navigation.Y ) < PoolSwimSettings.GoalRadius )
		{
			guest.PoolActivityPhase = PoolSlidePhases.Climb;
			guest.PoolActivityTurns = 0;
			(guest.PoolActivityX, guest.PoolActivityY) = (goalX, goalY);
			return PoolActivityStep.Continue;
		}
		// swim to the foot of the stairs with the swimmers' own steering, the crowd ignored near the stairs so nobody blocks them
		var body = new PoolSwimBody
		{
			X = navigation.X, Y = navigation.Y, Heading = guest.SwimHeading, GoalX = goalX, GoalY = goalY, HasGoal = true, Random = random
		};
		PoolSwimming.Step( region, ref body, ReadOnlySpan<(int X, int Y)>.Empty, guest.BaseSpeed * PoolSwimSettings.SpeedPerBaseSpeed, ignoreOthers: true );
		random = body.Random;
		guest.SwimHeading = body.Heading;
		simulation.MovePoolGuest( guest, body.X, body.Y );
		return PoolActivityStep.Continue;
	}

	private static PoolActivityStep Climb( GuestSimulation simulation, Guest guest, PoolSlide slide )
	{
		// the place in the line is taken from the counters as they were before this turn (see RankInLine)
		var (spotX, spotY) = SpotOf( simulation, guest, slide );
		var turns = ++guest.PoolActivityTurns;
		var (x, y) = ClimbPoint( simulation.PoolSlides!.TowerOf( slide ), guest, Math.Min( turns, PoolSlideSettings.ClimbTurns ), spotX, spotY );
		simulation.MovePoolGuest( guest, x, y );
		if ( turns >= PoolSlideSettings.ClimbTurns )
		{
			guest.PoolActivityPhase = PoolSlidePhases.Wait;
			guest.PoolActivityTurns = 0;
		}
		return PoolActivityStep.Continue;
	}

	/// <summary>
	/// Where the climber is after this many turns of the climb (16.16): from the cell it swam to, to the foot of the stairs at the tower's front edge, up the flight along its middle line to the top,
	/// onto the deck beside it and to its spot. The flight is where the tower's model has it, turned with the tower; the climber moves along it, so it is drawn facing the stairs.
	/// </summary>
	private static (int X, int Y) ClimbPoint( PoolSlideTower? tower, Guest guest, int turns, int spotX, int spotY )
	{
		var start = (X: guest.PoolActivityX, Y: guest.PoolActivityY);
		if ( tower == null )
			return (start.X + (int)((long)(spotX - start.X) * turns / PoolSlideSettings.ClimbTurns), start.Y + (int)((long)(spotY - start.Y) * turns / PoolSlideSettings.ClimbTurns));
		var foot = tower.ModelPoint( PoolSlideTowerModel.StairsX, PoolSlideTowerModel.StairsFootY );
		var top = tower.ModelPoint( PoolSlideTowerModel.StairsX, PoolSlideTowerModel.StairsTopY );
		var deck = tower.ModelPoint( PoolSlideTowerModel.StairsX + 10, PoolSlideTowerModel.DeckEntry );
		if ( turns <= ClimbFootEnd )
			return Lerp( start, foot, turns, ClimbFootEnd );
		if ( turns <= ClimbStairsEnd )
			return Lerp( foot, top, turns - ClimbFootEnd, ClimbStairsEnd - ClimbFootEnd );
		return turns == ClimbStairsEnd + 1 ? Lerp( top, deck, 1, 1 ) : (spotX, spotY);
	}

	private static (int X, int Y) Lerp( (int X, int Y) from, (int X, int Y) to, int step, int steps ) =>
		(from.X + (int)((long)(to.X - from.X) * step / steps), from.Y + (int)((long)(to.Y - from.Y) * step / steps));

	private static PoolActivityStep Wait( GuestSimulation simulation, Guest guest, PoolSlide slide )
	{
		// the rank and the spot are those of the start of this turn, whatever the order the guests are updated in
		var rank = RankInLine( simulation, guest );
		var (spotX, spotY) = SpotOf( simulation, guest, slide, rank );
		if ( ++guest.PoolActivityTurns > PoolSlideSettings.WaitLimitTurns )
			return LeaveTower( simulation, guest, slide );
		simulation.MovePoolGuest( guest, spotX, spotY );
		// the guest who has waited longest goes first, once the one before it is a cell and a half down the track
		if ( rank == 0 && IsStartClear( simulation, slide, guest.PoolActivityTarget ) && IsLandingClear( simulation, slide, guest ) )
		{
			guest.PoolActivityPhase = PoolSlidePhases.Ride;
			guest.PoolActivityTurns = 0;
			Place( simulation, guest, slide, 0 );
		}
		return PoolActivityStep.Continue;
	}

	private static PoolActivityStep Ride( GuestSimulation simulation, Guest guest, PoolSlide slide )
	{
		var turns = ++guest.PoolActivityTurns;
		var position = slide.PositionAtTurn( turns );
		if ( position < slide.Length )
		{
			var point = slide.PointAt( position );
			simulation.MovePoolGuest( guest, point.X * 64, point.Y * 64 );
			return PoolActivityStep.Continue;
		}
		// the end of the track: into the pool at the splash cell, a little off its centre by the guest's id so riders landing together do not stack
		var landing = slide.LandingPoint!.Value;
		simulation.PlacePoolGuest( guest, landing.X * 64 + (((guest.Id * 37) & 15) - 8) * 700, landing.Y * 64 + (((guest.Id * 59) & 15) - 8) * 700 );
		guest.PoolActivityPhase = PoolSlidePhases.Splash;
		guest.PoolActivityTurns = 0;
		return PoolActivityStep.Continue;
	}

	private static PoolActivityStep Splash( Guest guest )
	{
		if ( ++guest.PoolActivityTurns < PoolSlideSettings.SplashTurns )
			return PoolActivityStep.Continue;
		return PoolActivityStep.BackInWater;
	}

	/// <summary>The slide or its pool is gone with the guest on the stairs or the platform: it climbs down into the water at the foot of the stairs, or the session ends when there is none.</summary>
	private static PoolActivityStep LeaveTower( GuestSimulation simulation, Guest guest, PoolSlide? slide )
	{
		var foot = (guest.PoolActivityX >> 16, guest.PoolActivityY >> 16);
		if ( simulation.Grid.Cells.RawTypeAt( foot.Item1, foot.Item2 ) != ParkCellTypes.PoolWater )
			return PoolActivityStep.EndSession;
		simulation.PlacePoolGuest( guest, guest.PoolActivityX, guest.PoolActivityY );
		return PoolActivityStep.BackInWater;
	}

	private static void Place( GuestSimulation simulation, Guest guest, PoolSlide slide, int position )
	{
		var point = slide.PointAt( position );
		simulation.PlacePoolGuest( guest, point.X * 64, point.Y * 64 );
	}

	private static bool IsOn( Guest guest, int thingId, int lowest, int highest ) =>
		guest.State == GuestState.PoolActivity && guest.PoolActivityTarget == thingId && guest.PoolActivityPhase >= lowest && guest.PoolActivityPhase <= highest && guest.PoolActivityId == ActivityId;

	private static bool Any( GuestSimulation simulation, int thingId, int lowest, int highest )
	{
		var guests = simulation.Guests;
		for ( var index = 0; index < guests.Count; index++ )
			if ( IsOn( guests[index], thingId, lowest, highest ) )
				return true;
		return false;
	}

	/// <summary>How far a rider in the open flume is drawn towards the camera, engine units: a little more than the flume's depth (<see cref="PoolSlideMeshBuilder.FlumeRadius"/>), so the bed cannot cut the camera-facing quad.</summary>
	public const float FlumeDepthPull = 0.5f;

	/// <summary>The depth pull of the swimmers' renderer for this guest: <see cref="FlumeDepthPull"/> for a rider on a flume, 0 for everyone else. Presentation only.</summary>
	public static float DepthPull( GuestSimulation simulation, Guest guest ) =>
		guest.State == GuestState.PoolActivity && guest.PoolActivityPhase == PoolSlidePhases.Ride && guest.PoolActivityId == ActivityId
		&& simulation.PoolSlides?.FindByThing( guest.PoolActivityTarget ) is { Profile: PoolSlideProfile.Flume } ? FlumeDepthPull : 0f;

	/// <summary>
	/// Whether the guest is a rider inside a closed tube: it is not drawn there (as on a real enclosed slide) but on the platform before the launch and in the water after the splash. Presentation only.
	/// </summary>
	public static bool IsInsideTube( GuestSimulation simulation, Guest guest ) =>
		guest.State == GuestState.PoolActivity && guest.PoolActivityPhase == PoolSlidePhases.Ride && guest.PoolActivityId == ActivityId
		&& simulation.PoolSlides?.FindByThing( guest.PoolActivityTarget ) is { Profile: PoolSlideProfile.Tube };

	/// <summary>How many guests are on their way to, on the stairs of, or waiting in the tower with this thing id (the tower's capacity counts them all).</summary>
	public static int Occupants( GuestSimulation simulation, int thingId )
	{
		var count = 0;
		var guests = simulation.Guests;
		for ( var index = 0; index < guests.Count; index++ )
			if ( IsOn( guests[index], thingId, PoolSlidePhases.Approach, PoolSlidePhases.Wait ) )
				count++;
		return count;
	}

	/// <summary>Whether any guest is on the track or splashing down at the end: the slide's landing cell and its track are busy.</summary>
	public static bool HasRiders( GuestSimulation simulation, int thingId ) => Any( simulation, thingId, PoolSlidePhases.Ride, PoolSlidePhases.Splash );

	/// <summary>Whether any guest is on the stairs or the platform of the tower (the stairs' foot cell must stay water).</summary>
	public static bool HasClimbers( GuestSimulation simulation, int thingId ) => Any( simulation, thingId, PoolSlidePhases.Climb, PoolSlidePhases.Wait );

	/// <summary>
	/// The place of the guest in the line in the tower: how many guests on the stairs or the platform are ahead of it. Waiters go before climbers, the longest waiting (or the furthest up the stairs)
	/// first, ties by guest id; no list is built. The guests are updated one after the other within a turn, so a guest that has already been updated this turn (its <c>StepTick</c> is this tick, every update
	/// moves the guest) is read with its counters as they were before the turn: the line is the same whatever the order the guests are updated in.
	/// </summary>
	private static int RankInLine( GuestSimulation simulation, Guest guest )
	{
		var rank = 0;
		var guests = simulation.Guests;
		for ( var index = 0; index < guests.Count; index++ )
		{
			var other = guests[index];
			if ( other == guest || !IsOn( other, guest.PoolActivityTarget, PoolSlidePhases.Climb, PoolSlidePhases.Wait ) )
				continue;
			var (phase, turns) = (other.PoolActivityPhase, other.PoolActivityTurns);
			if ( other.StepTick == simulation.TickCount )
				(phase, turns) = phase == PoolSlidePhases.Wait && turns == 0 ? (PoolSlidePhases.Climb, PoolSlideSettings.ClimbTurns - 1) : (phase, turns - 1);
			if ( phase != guest.PoolActivityPhase ? phase > guest.PoolActivityPhase : turns != guest.PoolActivityTurns ? turns > guest.PoolActivityTurns : other.Id < guest.Id )
				rank++;
		}
		return rank;
	}

	private static (int X, int Y) SpotOf( GuestSimulation simulation, Guest guest, PoolSlide slide ) => SpotOf( simulation, guest, slide, RankInLine( simulation, guest ) );

	/// <summary>The guest's place on the platform (16.16): the deck's waiting spots of the model (<see cref="PoolSlideTowerModel.Spot"/>), one per rank in the line, turned with the tower.</summary>
	private static (int X, int Y) SpotOf( GuestSimulation simulation, Guest guest, PoolSlide slide, int rank )
	{
		if ( simulation.PoolSlides!.TowerOf( slide ) is not { Footprint.Count: > 0 } tower )
			return (guest.PoolActivityX, guest.PoolActivityY);
		var (x, y) = PoolSlideTowerModel.Spot( rank );
		return tower.ModelPoint( x, y );
	}

	/// <summary>The least distance from the landing point a swimmer must keep for a rider to be launched: 0.6 cell (16.16).</summary>
	private const long LandingRadius = 39322;

	/// <summary>
	/// Whether the water where the slide lands is free: no swimmer, climber out of the tower or rider on the end of the track is within <see cref="LandingRadius"/> of the landing point. The rider is
	/// not launched while it is not; the check reads positions only (no random draw) and the guests in list order, so it is the same whatever the order they are updated in.
	/// </summary>
	private static bool IsLandingClear( GuestSimulation simulation, PoolSlide slide, Guest rider )
	{
		if ( slide.LandingPoint is not { } landing )
			return true;
		var (landingX, landingY) = ((long)landing.X * 64, (long)landing.Y * 64);
		var guests = simulation.Guests;
		for ( var index = 0; index < guests.Count; index++ )
		{
			var other = guests[index];
			if ( other == rider || !(other.IsSwimming || other.InPoolActivity) || other.InPoolActivity && other.PoolActivityId == ActivityId && other.PoolActivityPhase is PoolSlidePhases.Climb or PoolSlidePhases.Wait )
				continue;
			var (dx, dy) = (other.Navigation.X - landingX, other.Navigation.Y - landingY);
			// a rider of this slide not yet updated this turn is read where its step of this turn takes it (its next point on the track, or the landing point), so the test does not depend on the update order
			if ( other.InPoolActivity && other.PoolActivityId == ActivityId && other.PoolActivityTarget == rider.PoolActivityTarget && other.PoolActivityPhase == PoolSlidePhases.Ride && other.StepTick != simulation.TickCount )
			{
				var position = slide.PositionAtTurn( other.PoolActivityTurns + 1 );
				if ( position >= slide.Length )
					return false;
				var point = slide.PointAt( position );
				(dx, dy) = ((long)point.X * 64 - landingX, (long)point.Y * 64 - landingY);
			}
			if ( dx * dx + dy * dy < LandingRadius * LandingRadius )
				return false;
		}
		return true;
	}

	/// <summary>Whether the start of the track is free: no rider is less than <see cref="PoolSlideSettings.Gap"/> down it.</summary>
	private static bool IsStartClear( GuestSimulation simulation, PoolSlide slide, int thingId )
	{
		var guests = simulation.Guests;
		for ( var index = 0; index < guests.Count; index++ )
			if ( IsOn( guests[index], thingId, PoolSlidePhases.Ride, PoolSlidePhases.Ride ) && slide.PositionAtTurn( guests[index].PoolActivityTurns ) < PoolSlideSettings.Gap )
				return false;
		return true;
	}
}

