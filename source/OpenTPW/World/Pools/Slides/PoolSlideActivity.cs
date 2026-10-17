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
/// rider's place on the track is a function of its turn counter, so a save in the middle of a ride restores exactly. It draws nothing from the swimmer's stream.
/// </summary>
// [EXT:pools-slides] OpenTPW's own water slides; the original game has no pools
public sealed class PoolSlideActivity : IPoolActivity
{
	/// <summary>The activity's id, saved with a guest that is riding.</summary>
	public const string ActivityId = "slide.ride";

	public string Id => ActivityId;

	/// <summary>How far from a platform spot the riders waiting stand from each other and the centre (cells).</summary>
	private const int SpotOffset = 29491; // 0.45 cell in 16.16

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
				return slide == null ? PoolActivityStep.EndSession : Ride( simulation, guest, slide );
			default:
				return Splash( simulation, guest, slide );
		}
	}

	public void Abort( GuestSimulation simulation, Guest guest )
	{
		// nothing is held outside the guest's own fields: the rider's place, the queue and the track are all derived from them
	}

	public int SpriteSlot( Guest guest ) => guest.PoolActivityPhase switch
	{
		PoolSlidePhases.Approach => PoolSwimSlots.Crawl,
		PoolSlidePhases.Climb => PoolSwimSlots.LadderClimb,
		PoolSlidePhases.Wait => PoolSwimSlots.Stand,
		PoolSlidePhases.Ride => PoolSwimSlots.Sliding,
		_ => PoolSwimSlots.SplashDown
	};

	public float? FeetHeight( GuestSimulation simulation, Guest guest, float defaultHeight )
	{
		if ( simulation.PoolSlides?.FindByThing( guest.PoolActivityTarget ) is not { } slide )
			return null;
		var top = slide.TopHeight / (float)PoolSlide.HeightUnit;
		switch ( guest.PoolActivityPhase )
		{
			case PoolSlidePhases.Climb:
			{
				// from the water at the foot of the stairs to the platform
				var foot = simulation.PoolSlides.WaterLevelAt( guest.PoolActivityX >> 16, guest.PoolActivityY >> 16 ) ?? defaultHeight;
				return foot + (top - foot) * Math.Min( guest.PoolActivityTurns, PoolSlideSettings.ClimbTurns ) / PoolSlideSettings.ClimbTurns;
			}
			case PoolSlidePhases.Wait:
				return top;
			case PoolSlidePhases.Ride:
				return slide.HeightAt( slide.PositionAtTurn( guest.PoolActivityTurns ) ) / (float)PoolSlide.HeightUnit + RiderLift;
			default:
				return null;
		}
	}

	/// <summary>How far above the track the sprite's feet are drawn, engine units: the rider lies in the flume.</summary>
	private const float RiderLift = 0.15f;

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
		var turns = ++guest.PoolActivityTurns;
		var (spotX, spotY) = SpotOf( simulation, guest, slide );
		// a straight line from the foot of the stairs (where the climb began: the activity's point) to the platform
		var x = guest.PoolActivityX + (int)((long)(spotX - guest.PoolActivityX) * Math.Min( turns, PoolSlideSettings.ClimbTurns ) / PoolSlideSettings.ClimbTurns);
		var y = guest.PoolActivityY + (int)((long)(spotY - guest.PoolActivityY) * Math.Min( turns, PoolSlideSettings.ClimbTurns ) / PoolSlideSettings.ClimbTurns);
		simulation.MovePoolGuest( guest, x, y );
		if ( turns >= PoolSlideSettings.ClimbTurns )
		{
			guest.PoolActivityPhase = PoolSlidePhases.Wait;
			guest.PoolActivityTurns = 0;
		}
		return PoolActivityStep.Continue;
	}

	private static PoolActivityStep Wait( GuestSimulation simulation, Guest guest, PoolSlide slide )
	{
		guest.PoolActivityTurns++;
		if ( guest.PoolActivityTurns > PoolSlideSettings.WaitLimitTurns )
			return LeaveTower( simulation, guest, slide );
		var (spotX, spotY) = SpotOf( simulation, guest, slide );
		simulation.MovePoolGuest( guest, spotX, spotY );
		// the guest who has waited longest goes first, once the one before it is a cell and a half down the track
		if ( RankInLine( simulation, guest ) == 0 && IsStartClear( simulation, slide, guest.PoolActivityTarget ) )
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

	private static PoolActivityStep Splash( GuestSimulation simulation, Guest guest, PoolSlide? slide )
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
	/// first, ties by guest id; no list is built.
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
			if ( other.PoolActivityPhase != guest.PoolActivityPhase ? other.PoolActivityPhase > guest.PoolActivityPhase
				: other.PoolActivityTurns != guest.PoolActivityTurns ? other.PoolActivityTurns > guest.PoolActivityTurns : other.Id < guest.Id )
				rank++;
		}
		return rank;
	}

	/// <summary>The guest's place on the platform (16.16): the spots lie round the tower's centre, one per rank in the line.</summary>
	private static (int X, int Y) SpotOf( GuestSimulation simulation, Guest guest, PoolSlide slide )
	{
		var rank = RankInLine( simulation, guest );
		var tower = simulation.PoolSlides!.TowerOf( slide );
		long sumX = 0, sumY = 0;
		var count = 0;
		if ( tower != null )
			foreach ( var (x, y) in tower.Footprint )
			{
				var centre = PoolSwimRegion.Centre( (x, y) );
				sumX += centre.X;
				sumY += centre.Y;
				count++;
			}
		if ( count == 0 )
			return (guest.PoolActivityX, guest.PoolActivityY);
		var ring = rank / 4;
		var (dx, dy) = ((rank & 1) == 0 ? -1 : 1, (rank & 2) == 0 ? -1 : 1);
		var spread = SpotOffset - ring * (SpotOffset / 4);
		return ((int)(sumX / count) + dx * spread, (int)(sumY / count) + dy * spread);
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
