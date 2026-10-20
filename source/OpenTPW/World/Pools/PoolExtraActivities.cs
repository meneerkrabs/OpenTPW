namespace OpenTPW;

/// <summary>The numbers of the pool extras' activities (docs/POOLS.md, "Extras"); all OpenTPW's own, none measured. Speeds are 16.16 cells per park turn.</summary>
// [EXT:pools-extras] OpenTPW's own numbers; the original game has no pools
public static class PoolExtraSettings
{
	/// <summary>Swimming to a ladder or the foot of a board: 0.12 cell a turn (0.48 cells a second).</summary>
	public const int SwimSpeed = 7864;
	/// <summary>Walking on the deck and the board: 0.10 cell a turn.</summary>
	public const int WalkSpeed = 6554;
	/// <summary>Climbing the steps of a board: 0.07 cell a turn (a ladder or a lounger's edge is climbed on the spot, <see cref="ClimbTurns"/>).</summary>
	public const int ClimbSpeed = 4588;
	/// <summary>Jumping in: 0.20 cell a turn.</summary>
	public const int JumpSpeed = 13107;
	/// <summary>The diver's fall from the end of the board: 0.22 cell a turn.</summary>
	public const int DiveSpeed = 14418;
	/// <summary>Where a swimmer meets a straight edge to climb out: 0.92 cell from the middle of the land cell, in the free water (0.42 cell from the rim) of the water cell beyond it.</summary>
	public const int EntryDistance = 60293;
	/// <summary>The rim of the pool: half a cell from the middle of the land cell.</summary>
	public const int RimDistance = Fixed16.One / 2;
	/// <summary>
	/// The foot of a ladder, where a swimmer starts and ends the climb: 0.78 cell from the middle of the land cell, exactly at the ladder's rails and rungs. Measured on the ladder model (content/pools/objects/*/ladder,
	/// tools/blender/opentpw_pool_extras.py <c>add_ladder</c>; 10 model units a cell, the model's Z is the cell's depth, 0 the back, 10 the cell's edge): the coping is the outer quarter of the pool cell
	/// (<c>PoolMeshOptions.RimWidth</c>), so the top of the wall, where the water starts, is 0.75 cell from the middle; the rails curve over the coping and go down along the wall at Z 12.6 to 13.0
	/// (0.78 cell, 0.1 unit off the wall) to 4.2 units below the deck, with five rungs 1.7 units apart (Y 3.4 down to -3.4, the water 0.6 below the deck). The offer still asks for free water at
	/// <see cref="EntryDistance"/> (swimmers keep 0.37 cell from the pool's edge).
	/// </summary>
	public const int LadderFootDistance = 51118;
	/// <summary>
	/// Where a ladder's climber stands after stepping over the rim: 0.25 cell from the middle of the land cell (Z 7.5), a little inside the cell, well behind the rails posts (Y 11.3, over the coping). The step over the coping from the foot is 0.53 cell, about five turns at <see cref="WalkSpeed"/>.
	/// </summary>
	public const int LadderDeckDistance = 16384;
	/// <summary>
	/// The turns of the vertical climb (8, 2 seconds). The guest stays at the foot; the drawn feet rise from the water to the deck, a distance of <c>PoolMeshOptions.WaterDrop</c> (0.12 engine units, 0.6 model
	/// units) on flat ground, i.e. about 0.015 engine units a turn (0.06 a second). The ladder model has five rungs 1.7 model units (0.34 engine units) apart.
	/// </summary>
	public const int ClimbTurns = 8;
	/// <summary>The end of the diving board: 1.6 cells from the middle of its cell.</summary>
	public const int BoardEnd = 104858;
	/// <summary>Where the diver lands: the middle of the second water cell, 2 cells from the middle of the board's cell.</summary>
	public const int BoardLanding = 2 * Fixed16.One;

	/// <summary>The most the swimmer's feet are raised on the board, in engine units (the board's top is 6.6 model units, 0.2 engine units each).</summary>
	public const float BoardLift = 1.32f;
	/// <summary>The top of the lounger's cushion above the deck, in engine units: 3.4 model units (tools/blender/opentpw_pool_extras.py, <c>add_lounger</c>: the seat cushion spans 2.6 to 3.4), 0.2 engine units each.</summary>
	public const float LoungerBedLift = 0.68f;

	/// <summary>A ladder: turns standing on the deck before jumping in, 5 to 10.</summary>
	public const int LadderStandTurns = 5, LadderStandExtra = 6;
	/// <summary>A board: turns at the end of the board, 3; turns of the splash, 4.</summary>
	public const int BoardPauseTurns = 3, SplashTurns = 4;
	/// <summary>A shower: 12 to 20 turns (3 to 5 seconds), the chance (percent) that a guest showers as its swim starts, and the distance (in cells, each way) a shower may lie from the room's pool-side cell.</summary>
	public const int ShowerTurns = 12, ShowerExtraTurns = 9, ShowerStartPercent = 60, ShowerReach = 4;
	/// <summary>
	/// The engine units a guest lying on a lounger is drawn pulled towards the camera (presentation only, <see cref="LoungerActivity.DepthPull"/>): the camera-facing quad stands at the seat's middle, and
	/// the lounger's own raised head end (up to 5.8 model units, 0.48 engine units above the cushion) and the front half of its seat, up to 0.68 engine units nearer the camera, hid the head and the
	/// near side of the body. The pull is along the view direction, so the screen position moves only by the perspective's small share (as the flume riders').
	/// </summary>
	public const float LoungerDepthPull = 0.75f;
	/// <summary>A lounger: 60 to 120 turns (15 to 30 seconds) lying, then 60 percent back into the water and the rest end the session.</summary>
	public const int LoungerTurns = 60, LoungerExtraTurns = 61, LoungerBackPercent = 60;

	/// <summary>Happiness points (meter 0 to 100) each activity adds when the guest is done.</summary>
	public const float LadderHappiness = 2f, BoardHappiness = 6f, ShowerHappiness = 2f, LoungerHappiness = 5f;
}

/// <summary>The shared geometry and motion of the pool extras' activities. A guest keeps its activity's step in the low four bits of <see cref="Guest.PoolActivityPhase"/> and the direction to the water in the rest.</summary>
// [EXT:pools-extras] OpenTPW's own activities; the original game has no pools
internal static class PoolExtraMotion
{
	public static int StepOf( Guest guest ) => guest.PoolActivityPhase & 15;

	public static (int X, int Y) WaterSide( Guest guest ) => PoolExtras.Directions[(guest.PoolActivityPhase >> 4) & 3];

	public static void SetStep( Guest guest, int step, int turns = 0 )
	{
		guest.PoolActivityPhase = (guest.PoolActivityPhase & ~15) | step;
		guest.PoolActivityTurns = turns;
	}

	/// <summary>The middle of the guest's land cell (the host puts it in the activity's point).</summary>
	public static (int X, int Y) Land( Guest guest ) => (guest.PoolActivityX, guest.PoolActivityY);

	/// <summary>A point <paramref name="distance"/> (16.16 cells) from <paramref name="from"/> towards <paramref name="side"/>.</summary>
	public static (int X, int Y) Along( (int X, int Y) from, (int X, int Y) side, int distance ) => (from.X + side.X * distance, from.Y + side.Y * distance);

	public static (int X, int Y) Entry( (int X, int Y) land, (int X, int Y) side ) => Along( land, side, PoolExtraSettings.EntryDistance );

	/// <summary>The foot of a ladder or the haul-out point at a lounger: in the water at the wall, <see cref="PoolExtraSettings.LadderFootDistance"/> from the land cell's middle.</summary>
	public static (int X, int Y) Foot( (int X, int Y) land, (int X, int Y) side ) => Along( land, side, PoolExtraSettings.LadderFootDistance );

	/// <summary>
	/// The vertical climb out of the water at the rim (step 1 of the ladder and the lounger): the guest stays at the foot facing the land and the turns count up to <see cref="PoolExtraSettings.ClimbTurns"/>;
	/// true when it is up.
	/// </summary>
	public static bool Climb( Guest guest )
	{
		var side = WaterSide( guest );
		Face( guest, (-side.X, -side.Y) );
		return ++guest.PoolActivityTurns >= PoolExtraSettings.ClimbTurns;
	}

	/// <summary>Moves the guest towards the point by <paramref name="speed"/>; true when it is there.</summary>
	public static bool StepToward( GuestSimulation simulation, Guest guest, (int X, int Y) target, int speed )
	{
		var navigation = guest.Navigation;
		var (dx, dy) = (target.X - navigation.X, target.Y - navigation.Y);
		var distance = Fixed16.Length( dx, dy );
		if ( distance <= speed )
		{
			simulation.MovePoolGuest( guest, target.X, target.Y );
			return true;
		}
		simulation.MovePoolGuest( guest, navigation.X + (int)((long)dx * speed / distance), navigation.Y + (int)((long)dy * speed / distance) );
		return false;
	}

	/// <summary>Whether the straight line from the guest to the point lies in free water all the way (sampled eight times).</summary>
	public static bool WaterLine( PoolSwimRegion region, Guest guest, (int X, int Y) target )
	{
		var (ax, ay) = (guest.Navigation.X, guest.Navigation.Y);
		for ( var i = 0; i <= 8; i++ )
			if ( !region.IsWater( ax + (int)((long)(target.X - ax) * i / 8), ay + (int)((long)(target.Y - ay) * i / 8) ) )
				return false;
		return true;
	}

	/// <summary>The sites of a kind, nearest to the guest first (ties by thing id), so a guest picks the extra in its own corner of the pool.</summary>
	public static IEnumerable<PoolExtraSite> Sites( GuestSimulation simulation, PoolExtraKind kind, Guest guest ) =>
		(simulation.PoolExtraSites?.Invoke() ?? Array.Empty<PoolExtraSite>()).Where( site => site.Kind == kind )
			.OrderBy( site => Fixed16.Length( site.Cells[0].X * Fixed16.One + Fixed16.One / 2 - guest.Navigation.X, site.Cells[0].Y * Fixed16.One + Fixed16.One / 2 - guest.Navigation.Y ) ).ThenBy( site => site.ThingId );

	public static PoolExtraSite? SiteOf( GuestSimulation simulation, int thingId ) => simulation.PoolExtraSites?.Invoke().FirstOrDefault( site => site.ThingId == thingId );

	/// <summary>Whether another guest does the activity at the target (and, when given, at the seat's cell): the host's exact occupancy, rebuilt only when a guest starts or ends an activity.</summary>
	public static bool Taken( GuestSimulation simulation, string id, int target, (int X, int Y)? seat = null ) => simulation.IsPoolActivityTaken( id, target, seat );

	public static void Face( Guest guest, (int X, int Y) side ) => (guest.HeadingX, guest.HeadingY) = (side.X, side.Y);

	public static int DirectionIndex( (int X, int Y) side ) => Array.IndexOf( PoolExtras.Directions, side );

	/// <summary>Cells away from a point (16.16) as a fraction of a distance, clamped to 0..1.</summary>
	public static float Fraction( Guest guest, (int X, int Y) point, int total ) => Math.Clamp( Fixed16.Length( point.X - guest.Navigation.X, point.Y - guest.Navigation.Y ) / (float)total, 0f, 1f );

	/// <summary>
	/// The drawn feet of a ladder or lounger climb: the water at the foot (0) and in the splash (6), rising to the deck with the turns of the climb on the spot (1), on the deck (2 to 4: the step over the rim, the
	/// stand or lie, the walk to the rim), falling to the water with the jump (5).
	/// </summary>
	public static float? EdgeFeet( Guest guest, PoolFeetHeights heights )
	{
		var land = Land( guest );
		switch ( StepOf( guest ) )
		{
			case 0 or 6:
				return heights.Water;
			case 1:
				return heights.Water + (heights.Deck - heights.Water) * Math.Clamp( guest.PoolActivityTurns / (float)PoolExtraSettings.ClimbTurns, 0f, 1f );
			case 5:
				return heights.Water + (heights.Deck - heights.Water) * Fraction( guest, Entry( land, WaterSide( guest ) ), PoolExtraSettings.EntryDistance - PoolExtraSettings.RimDistance );
			default:
				return heights.Deck;
		}
	}

	/// <summary>The common ending: the guest steps from the rim back into the water in the same place it climbed out.</summary>
	public static PoolActivityStep ReEnter( GuestSimulation simulation, Guest guest, int step, float happiness )
	{
		var land = Land( guest );
		var side = WaterSide( guest );
		switch ( step )
		{
			case 4:
				// at the rim: wait (up to LandingWaitTurns, counted in the turns of this step) while another body is in the water where the jump lands
				if ( StepToward( simulation, guest, Along( land, side, PoolExtraSettings.RimDistance ), PoolExtraSettings.WalkSpeed )
					&& (guest.PoolActivityTurns++ >= PoolAttractionSettings.LandingWaitTurns || !PoolActivityHelpers.LandingBusy( simulation, guest, Entry( land, side ) )) )
					SetStep( guest, 5 );
				break;
			case 5:
				if ( StepToward( simulation, guest, Entry( land, side ), PoolExtraSettings.JumpSpeed ) )
					SetStep( guest, 6, PoolExtraSettings.SplashTurns );
				break;
			default:
				if ( --guest.PoolActivityTurns <= 0 )
				{
					guest.Happiness = MeterMath.Add( guest.Happiness, happiness );
					return PoolActivityStep.BackInWater;
				}
				break;
		}
		return PoolActivityStep.Continue;
	}
}

/// <summary>
/// [EXT:pools-extras] The ladder: a swimmer climbs out by it, stands on the deck a few seconds and jumps back in with a splash (docs/POOLS.md, "Extras"). One swimmer at a time.
/// Steps: 0 swim to the foot of the ladder (<see cref="PoolExtraSettings.LadderFootDistance"/>), 1 climb on the spot (the feet rise, the guest does not move), 2 step over the rim onto the deck, 3 stand,
/// 4 walk to the rim, 5 jump, 6 splash.
/// </summary>
public sealed class LadderActivity : IPoolActivity
{
	public string Id => "extra.ladder";

	public PoolActivityOffer? Offer( GuestSimulation simulation, Guest guest, PoolSwimRegion region )
	{
		foreach ( var site in PoolExtraMotion.Sites( simulation, PoolExtraKind.Ladder, guest ) )
		{
			var land = site.Cells[0];
			var side = PoolExtras.WaterDirection( site.Rotation );
			if ( region.Contains( land.X + side.X, land.Y + side.Y ) && !PoolExtraMotion.Taken( simulation, Id, site.ThingId )
				&& PoolExtraMotion.Entry( PoolSwimRegion.Centre( land ), side ) is var entry && region.IsWater( entry.X, entry.Y ) && PoolExtraMotion.WaterLine( region, guest, entry ) )
				return new PoolActivityOffer( site.ThingId, land.X, land.Y );
		}
		return null;
	}

	public bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random )
	{
		if ( PoolExtraMotion.SiteOf( simulation, offer.TargetId ) is not { } site )
			return false;
		guest.PoolActivityPhase = PoolExtraMotion.DirectionIndex( PoolExtras.WaterDirection( site.Rotation ) ) << 4;
		return true;
	}

	public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random )
	{
		var land = PoolExtraMotion.Land( guest );
		var side = PoolExtraMotion.WaterSide( guest );
		switch ( PoolExtraMotion.StepOf( guest ) )
		{
			case 0:
				if ( PoolExtraMotion.StepToward( simulation, guest, PoolExtraMotion.Foot( land, side ), PoolExtraSettings.SwimSpeed ) )
				{
					PoolExtraMotion.SetStep( guest, 1 );
					PoolExtraMotion.Face( guest, (-side.X, -side.Y) );
				}
				return PoolActivityStep.Continue;
			case 1:
				if ( PoolExtraMotion.Climb( guest ) )
					PoolExtraMotion.SetStep( guest, 2 );
				return PoolActivityStep.Continue;
			case 2:
				if ( PoolExtraMotion.StepToward( simulation, guest, PoolExtraMotion.Along( land, side, PoolExtraSettings.LadderDeckDistance ), PoolExtraSettings.WalkSpeed ) )
					PoolExtraMotion.SetStep( guest, 3, PoolExtraSettings.LadderStandTurns + (int)(PoolSwimming.Next( ref random ) % PoolExtraSettings.LadderStandExtra) );
				return PoolActivityStep.Continue;
			case 3:
				PoolExtraMotion.Face( guest, side );
				if ( --guest.PoolActivityTurns <= 0 )
					PoolExtraMotion.SetStep( guest, 4 );
				return PoolActivityStep.Continue;
			default:
				return PoolExtraMotion.ReEnter( simulation, guest, PoolExtraMotion.StepOf( guest ), PoolExtraSettings.LadderHappiness );
		}
	}

	public void Abort( GuestSimulation simulation, Guest guest ) { }

	/// <summary>A body in the water while swimming to the ladder (0), jumping in (5) and splashing (6); on the ladder (1) the guest hangs above the water, as on the board.</summary>
	public bool InWater( GuestSimulation simulation, Guest guest ) => PoolExtraMotion.StepOf( guest ) is 0 or 5 or 6;

	public int SpriteSlot( Guest guest ) => PoolExtraMotion.StepOf( guest ) switch
	{
		0 => PoolSwimSlots.Crawl, 1 => PoolSwimSlots.LadderClimb, 2 => PoolSwimSlots.Walk, 3 => PoolSwimSlots.Stand, 4 => PoolSwimSlots.Walk, 5 => PoolSwimSlots.Dive, _ => PoolSwimSlots.SplashDown
	};

	/// <summary>The feet rise from the water surface to the deck while climbing on the spot, stay on the deck, and fall back to the water with the jump.</summary>
	public float? FeetHeight( GuestSimulation simulation, Guest guest, PoolFeetHeights heights ) => PoolExtraMotion.EdgeFeet( guest, heights );
}

/// <summary>
/// [EXT:pools-extras] The diving board: a swimmer swims to the tower, climbs the steps (slot 3), walks out on the board, pauses at its end, dives (slot 4) into the water two cells out with a splash (slot 11)
/// and is back in the water. The drawn height follows the climb and the fall (<see cref="IPoolActivity.FeetHeight"/>). One diver at a time. Steps: 0 swim to the foot, 1 climb, 2 walk out, 3 pause, 4 dive, 5 splash.
/// </summary>
public sealed class DivingBoardActivity : IPoolActivity
{
	public string Id => "extra.board";

	public PoolActivityOffer? Offer( GuestSimulation simulation, Guest guest, PoolSwimRegion region )
	{
		foreach ( var site in PoolExtraMotion.Sites( simulation, PoolExtraKind.DivingBoard, guest ) )
		{
			var land = site.Cells[0];
			var side = PoolExtras.WaterDirection( site.Rotation );
			var landing = PoolExtraMotion.Along( PoolSwimRegion.Centre( land ), side, PoolExtraSettings.BoardLanding );
			if ( region.Contains( land.X + side.X, land.Y + side.Y ) && !PoolExtraMotion.Taken( simulation, Id, site.ThingId )
				&& PoolExtraMotion.Entry( PoolSwimRegion.Centre( land ), side ) is var entry && region.IsWater( entry.X, entry.Y ) && region.IsWater( landing.X, landing.Y ) && PoolExtraMotion.WaterLine( region, guest, entry ) )
				return new PoolActivityOffer( site.ThingId, land.X, land.Y );
		}
		return null;
	}

	public bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random )
	{
		if ( PoolExtraMotion.SiteOf( simulation, offer.TargetId ) is not { } site )
			return false;
		guest.PoolActivityPhase = PoolExtraMotion.DirectionIndex( PoolExtras.WaterDirection( site.Rotation ) ) << 4;
		return true;
	}

	public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random )
	{
		var land = PoolExtraMotion.Land( guest );
		var side = PoolExtraMotion.WaterSide( guest );
		switch ( PoolExtraMotion.StepOf( guest ) )
		{
			case 0:
				if ( PoolExtraMotion.StepToward( simulation, guest, PoolExtraMotion.Entry( land, side ), PoolExtraSettings.SwimSpeed ) )
					PoolExtraMotion.SetStep( guest, 1 );
				break;
			case 1:
				if ( PoolExtraMotion.StepToward( simulation, guest, land, PoolExtraSettings.ClimbSpeed ) )
					PoolExtraMotion.SetStep( guest, 2 );
				break;
			case 2:
				if ( PoolExtraMotion.StepToward( simulation, guest, PoolExtraMotion.Along( land, side, PoolExtraSettings.BoardEnd ), PoolExtraSettings.WalkSpeed ) )
					PoolExtraMotion.SetStep( guest, 3, PoolExtraSettings.BoardPauseTurns + PoolAttractionSettings.LandingWaitTurns );
				break;
			case 3:
				// the pause is the turns above LandingWaitTurns; then the diver waits (up to LandingWaitTurns more) while another body is in the water at the landing
				PoolExtraMotion.Face( guest, side );
				if ( --guest.PoolActivityTurns <= PoolAttractionSettings.LandingWaitTurns
					&& (guest.PoolActivityTurns <= 0 || !PoolActivityHelpers.LandingBusy( simulation, guest, PoolExtraMotion.Along( land, side, PoolExtraSettings.BoardLanding ) )) )
					PoolExtraMotion.SetStep( guest, 4 );
				break;
			case 4:
				if ( PoolExtraMotion.StepToward( simulation, guest, PoolExtraMotion.Along( land, side, PoolExtraSettings.BoardLanding ), PoolExtraSettings.DiveSpeed ) )
					PoolExtraMotion.SetStep( guest, 5, PoolExtraSettings.SplashTurns );
				break;
			default:
				if ( --guest.PoolActivityTurns <= 0 )
				{
					guest.Happiness = MeterMath.Add( guest.Happiness, PoolExtraSettings.BoardHappiness );
					return PoolActivityStep.BackInWater;
				}
				break;
		}
		return PoolActivityStep.Continue;
	}

	public void Abort( GuestSimulation simulation, Guest guest ) { }

	/// <summary>A body in the water while swimming to the foot of the board (0), diving (4) and splashing (5); the board's guests are above the water.</summary>
	public bool InWater( GuestSimulation simulation, Guest guest ) => PoolExtraMotion.StepOf( guest ) is 0 or 4 or 5;

	public int SpriteSlot( Guest guest ) => PoolExtraMotion.StepOf( guest ) switch
	{
		0 => PoolSwimSlots.Crawl, 1 => PoolSwimSlots.LadderClimb, 2 => PoolSwimSlots.Walk, 3 => PoolSwimSlots.Stand, 4 => PoolSwimSlots.Dive, _ => PoolSwimSlots.SplashDown
	};

	/// <summary>The feet rise from the water to the board with the steps, stay at the board's height (the deck plus <see cref="PoolExtraSettings.BoardLift"/>) while walking out, and fall to the water with the dive.</summary>
	public float? FeetHeight( GuestSimulation simulation, Guest guest, PoolFeetHeights heights )
	{
		var top = heights.Deck + PoolExtraSettings.BoardLift;
		var land = PoolExtraMotion.Land( guest );
		switch ( PoolExtraMotion.StepOf( guest ) )
		{
			case 1:
				return heights.Water + (top - heights.Water) * (1f - PoolExtraMotion.Fraction( guest, land, PoolExtraSettings.EntryDistance ));
			case 2 or 3:
				return top;
			case 4:
				// the fall follows the dive's way from the end of the board to the landing (about two turns), not a turn count
				return heights.Water + (top - heights.Water) * PoolExtraMotion.Fraction( guest, PoolExtraMotion.Along( land, PoolExtraMotion.WaterSide( guest ), PoolExtraSettings.BoardLanding ), PoolExtraSettings.BoardLanding - PoolExtraSettings.BoardEnd );
			default:
				return null;
		}
	}
}

/// <summary>
/// [EXT:pools-extras] The shower: offered at the start of a swim (60 percent), a guest walks from the pool-side cell to a free shower within four cells, showers 3 to 5 seconds (slot 6) and walks back
/// into the water. One guest at a time. Steps: 0 walk to the shower, 1 shower, 2 walk back.
/// </summary>
public sealed class ShowerActivity : IPoolActivity
{
	public string Id => "extra.shower";

	public int StartOfferPercent => PoolExtraSettings.ShowerStartPercent;

	public PoolActivityOffer? Offer( GuestSimulation simulation, Guest guest, PoolSwimRegion region )
	{
		foreach ( var site in PoolExtraMotion.Sites( simulation, PoolExtraKind.Shower, guest ) )
		{
			var cell = site.Cells[0];
			if ( Math.Abs( cell.X - region.Access.X ) <= PoolExtraSettings.ShowerReach && Math.Abs( cell.Y - region.Access.Y ) <= PoolExtraSettings.ShowerReach && !PoolExtraMotion.Taken( simulation, Id, site.ThingId ) )
				return new PoolActivityOffer( site.ThingId, cell.X, cell.Y );
		}
		return null;
	}

	public bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random ) => true;

	public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random )
	{
		switch ( PoolExtraMotion.StepOf( guest ) )
		{
			case 0:
				if ( PoolExtraMotion.StepToward( simulation, guest, PoolExtraMotion.Land( guest ), PoolExtraSettings.WalkSpeed ) )
					PoolExtraMotion.SetStep( guest, 1, PoolExtraSettings.ShowerTurns + (int)(PoolSwimming.Next( ref random ) % PoolExtraSettings.ShowerExtraTurns) );
				break;
			case 1:
				if ( --guest.PoolActivityTurns <= 0 )
					PoolExtraMotion.SetStep( guest, 2 );
				break;
			default:
				if ( simulation.SwimRoomOf( guest ) is not { } room || simulation.SwimRegionOf( room ) is not { } region || PoolExtraMotion.StepToward( simulation, guest, PoolSwimRegion.Centre( region.Access ), PoolExtraSettings.WalkSpeed ) )
				{
					guest.Happiness = MeterMath.Add( guest.Happiness, PoolExtraSettings.ShowerHappiness );
					return PoolActivityStep.BackInWater;
				}
				break;
		}
		return PoolActivityStep.Continue;
	}

	public void Abort( GuestSimulation simulation, Guest guest ) { }

	public int SpriteSlot( Guest guest ) => PoolExtraMotion.StepOf( guest ) == 1 ? PoolSwimSlots.Shower : PoolSwimSlots.Walk;
}

/// <summary>
/// [EXT:pools-extras] The sun lounger (two seats): a swimmer climbs out beside it, lies on a free seat 15 to 30 seconds (slot 5) and then either jumps back into the water (60 percent) or ends its session
/// (it is put at the steps and climbs out). Steps: 0 swim to the foot at the edge (as the ladder's), 1 climb on the spot, 2 step over the rim onto the seat, 3 lie, 4 walk to the rim, 5 jump, 6 splash.
/// </summary>
public sealed class LoungerActivity : IPoolActivity
{
	/// <summary>The activity's id.</summary>
	public const string ActivityId = "extra.lounger";

	public string Id => ActivityId;

	/// <summary>The side of the seat the swimmer comes in from: the first, from the lounger's own facing, with water of the swimmer's pool beside the seat and a free line to it.</summary>
	private static int? SideOf( ParkCellMap cells, PoolSwimRegion region, (int X, int Y) seat, int first ) =>
		PoolExtras.WaterSides( cells, seat, first ).Where( index => region.Contains( seat.X + PoolExtras.Directions[index].X, seat.Y + PoolExtras.Directions[index].Y )
			&& PoolExtraMotion.Entry( PoolSwimRegion.Centre( seat ), PoolExtras.Directions[index] ) is var entry && region.IsWater( entry.X, entry.Y ) ).Select( index => (int?)index ).FirstOrDefault();

	public PoolActivityOffer? Offer( GuestSimulation simulation, Guest guest, PoolSwimRegion region )
	{
		foreach ( var site in PoolExtraMotion.Sites( simulation, PoolExtraKind.Lounger, guest ) )
		{
			foreach ( var seat in site.Cells )
			{
				if ( PoolExtraMotion.Taken( simulation, Id, site.ThingId, seat ) || SideOf( simulation.Grid.Cells, region, seat, PoolExtraMotion.DirectionIndex( PoolExtras.WaterDirection( site.Rotation ) ) ) is not { } index )
					continue;
				var entry = PoolExtraMotion.Entry( PoolSwimRegion.Centre( seat ), PoolExtras.Directions[index] );
				if ( PoolExtraMotion.WaterLine( region, guest, entry ) )
					return new PoolActivityOffer( site.ThingId, seat.X, seat.Y );
			}
		}
		return null;
	}

	public bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random )
	{
		if ( PoolExtraMotion.SiteOf( simulation, offer.TargetId ) is not { } site || simulation.SwimRoomOf( guest ) is not { } room || simulation.SwimRegionOf( room ) is not { } region
			|| SideOf( simulation.Grid.Cells, region, (offer.X, offer.Y), PoolExtraMotion.DirectionIndex( PoolExtras.WaterDirection( site.Rotation ) ) ) is not { } index )
			return false;
		guest.PoolActivityPhase = index << 4;
		return true;
	}

	public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random )
	{
		var land = PoolExtraMotion.Land( guest );
		var step = PoolExtraMotion.StepOf( guest );
		switch ( step )
		{
			case 0:
				if ( PoolExtraMotion.StepToward( simulation, guest, PoolExtraMotion.Foot( land, PoolExtraMotion.WaterSide( guest ) ), PoolExtraSettings.SwimSpeed ) )
				{
					PoolExtraMotion.SetStep( guest, 1 );
					PoolExtraMotion.Face( guest, (-PoolExtraMotion.WaterSide( guest ).X, -PoolExtraMotion.WaterSide( guest ).Y) );
				}
				return PoolActivityStep.Continue;
			case 1:
				if ( PoolExtraMotion.Climb( guest ) )
					PoolExtraMotion.SetStep( guest, 2 );
				return PoolActivityStep.Continue;
			case 2:
				if ( PoolExtraMotion.StepToward( simulation, guest, land, PoolExtraSettings.WalkSpeed ) )
				{
					PoolExtraMotion.SetStep( guest, 3, PoolExtraSettings.LoungerTurns + (int)(PoolSwimming.Next( ref random ) % PoolExtraSettings.LoungerExtraTurns) );
					// lie down at once along the seat, so the first lying turn is not drawn with the heading of the step onto it
					PoolExtraMotion.Face( guest, LieDirection( simulation, guest ) );
				}
				return PoolActivityStep.Continue;
			case 3:
				PoolExtraMotion.Face( guest, LieDirection( simulation, guest ) );
				if ( --guest.PoolActivityTurns > 0 )
					return PoolActivityStep.Continue;
				if ( PoolSwimming.Next( ref random ) % 100 < PoolExtraSettings.LoungerBackPercent )
				{
					PoolExtraMotion.SetStep( guest, 4 );
					return PoolActivityStep.Continue;
				}
				guest.Happiness = MeterMath.Add( guest.Happiness, PoolExtraSettings.LoungerHappiness );
				return PoolActivityStep.EndSession;
			default:
				return PoolExtraMotion.ReEnter( simulation, guest, step, PoolExtraSettings.LoungerHappiness );
		}
	}

	/// <summary>
	/// The way a lying guest faces: along the seat, the feet towards the lounger's foot end and the head on its raised head end. That is the lounger's own back, <see cref="PoolExtras.WaterDirection"/> of its
	/// rotation (tools/blender/opentpw_pool_extras.py, <c>add_lounger</c>: the head end is the front of the cell, the cushion runs to the back), and the lying frames have the feet towards the heading
	/// (<see cref="PoolSwimSlots.Lying"/>). It is not the side the guest climbed out on: a lounger may stand with its side or its head end to the water (any cell with water beside it), and a guest who came
	/// in from there lay across the seat. The seat's own side only while the lounger is gone (the activity is then aborted).
	/// </summary>
	public static (int X, int Y) LieDirection( GuestSimulation simulation, Guest guest ) =>
		PoolExtraMotion.SiteOf( simulation, guest.PoolActivityTarget ) is { } site ? PoolExtras.WaterDirection( site.Rotation ) : PoolExtraMotion.WaterSide( guest );

	/// <summary>The depth pull of the swimmers' renderer for this guest: <see cref="PoolExtraSettings.LoungerDepthPull"/> while it lies on a lounger (step 3), 0 for everyone else. Presentation only.</summary>
	public static float DepthPull( GuestSimulation simulation, Guest guest ) =>
		guest.State == GuestState.PoolActivity && guest.PoolActivityId == ActivityId && PoolExtraMotion.StepOf( guest ) == 3 ? PoolExtraSettings.LoungerDepthPull : 0f;

	public void Abort( GuestSimulation simulation, Guest guest ) { }

	/// <summary>A body in the water while swimming to the lounger (0), jumping in (5) and splashing (6).</summary>
	public bool InWater( GuestSimulation simulation, Guest guest ) => PoolExtraMotion.StepOf( guest ) is 0 or 5 or 6;

	public int SpriteSlot( Guest guest ) => PoolExtraMotion.StepOf( guest ) switch
	{
		0 => PoolSwimSlots.Crawl, 1 => PoolSwimSlots.LadderClimb, 2 => PoolSwimSlots.Walk, 3 => PoolSwimSlots.Lying, 4 => PoolSwimSlots.Walk, 5 => PoolSwimSlots.Dive, _ => PoolSwimSlots.SplashDown
	};

	/// <summary>
	/// As the ladder's: from the water up to the deck on the spot and back down with the jump, but the lie (step 3) is on the cushion, <see cref="PoolExtraSettings.LoungerBedLift"/> above the deck, where the
	/// bottom-anchored lying frames rest; walking onto the seat (2) and off it (4) the feet rise and fall with the distance to the seat's middle over the cushion's half cell.
	/// </summary>
	public float? FeetHeight( GuestSimulation simulation, Guest guest, PoolFeetHeights heights )
		=> PoolExtraMotion.StepOf( guest ) switch
		{
			3 => heights.Deck + PoolExtraSettings.LoungerBedLift,
			2 or 4 => heights.Deck + PoolExtraSettings.LoungerBedLift * (1f - PoolExtraMotion.Fraction( guest, PoolExtraMotion.Land( guest ), PoolExtraSettings.RimDistance )),
			_ => PoolExtraMotion.EdgeFeet( guest, heights )
		};
}
