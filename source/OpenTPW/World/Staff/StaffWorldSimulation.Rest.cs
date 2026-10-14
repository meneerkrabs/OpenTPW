namespace OpenTPW;

/// <summary>A rest area for the staff (an object with <c>UsageInfo.ChillsYouOut</c>, the Staff Room): its economy id, its cell and the path cell in front of it.</summary>
public readonly record struct RestSite( int Id, (int X, int Y) Cell, (int X, int Y) Access );

/// <summary>
/// The staff states every role shares (tp.exe <c>0x00506A40</c> gate, <c>0x00505FE0</c>, <c>0x005061D0</c>, <c>0x005056E0</c>, <c>0x00506300</c>;
/// docs/reverse/WANDER-plan.md §4): a tired member walks to the nearest rest area and rests there until its energy is full; while its role
/// strikes, it walks out through the gate to the strike area and stands there until the strike ends, then walks back in.
/// </summary>
public sealed partial class StaffWorldSimulation
{
	/// <summary>The park's rest areas; null: none.</summary>
	public Func<IReadOnlyList<RestSite>>? RestAreas { get; set; }
	/// <summary>Whether the park gates stand open (their script's <c>VAR_STATUS</c> is 1); null: no gates, so nobody walks out to strike.</summary>
	public Func<bool>? GatesOpen { get; set; }
	/// <summary>Called after each staff turn with every rest area's id and the members resting in it (its script's <c>VAR_STAFFIN</c>).</summary>
	public Action<int, int>? RestingStaff { get; set; }

	/// <summary>
	/// The strike and rest gate every search opens with (tp.exe <c>0x00506A40</c>): while the role strikes and the gates stand open, the member walks
	/// to a random point of the strike area; a member above the rest level takes its base speed from its energy; a tired one walks to the nearest rest
	/// area, or without one loses happiness one turn in sixteen. True when it now walks to a strike or a rest.
	/// </summary>
	// [BIN:TPW-EXE:0x00506A40 strike and rest gate] the role's strike flag (0x00509990) and the park gates' variable 1 (0x0051A290 through game +0x1DA732, mParkGates: VAR_STATUS) equal to 1: +0x188 = 1, four world draws pick the point (draw % StrikeAreaSizeX + StartX, a draw's low byte, draw % SizeY + StartY, a byte; 0x007855EC..0x007855F8), a route (0x004FA5F0): state 4; no route: "Could not get to strike location" and on; the energy byte (+0x1FC) above RestLevel (0x00785324): the base speed from 0x0075C7F8 (byte / 20, 140 from 100), then the happiness byte (+0x1F8) at most 10 sends message 0x13, above 97 draws once (message 0x12 at (draw & 15) == 0); else message 0x14 and the rest finder 0x00506910: a route to its access cell (+0x36, 0x004FA530) stores it (+0x208) and sets state 2; else a draw and at (draw & 15) == 0 happiness −= HappyHitCosNoRestArea (0x00785328), clamped
	private bool StrikeOrRest( ParkEconomy economy, StaffMember member, long turn )
	{
		var settings = economy.Settings;
		if ( economy.RoleOnStrike( member.Type ) && GatesOpen?.Invoke() == true )
		{
			var area = settings.StrikeArea;
			var x = (int)(Draw( economy ) % (uint)area.Width) + area.X;
			var byteX = (int)(Draw( economy ) & 0xFF);
			var y = (int)(Draw( economy ) % (uint)area.Height) + area.Y;
			var byteY = (int)(Draw( economy ) & 0xFF);
			if ( RouteToStrike( member, (x, y, byteX, byteY) ) )
			{
				SetJob( economy, member, StaffJob.ToStrike, turn );
				return true;
			}
		}
		var energy = StaffVitals.Byte( member.Energy );
		if ( energy > settings.RestLevel )
		{
			member.BaseSpeed = energy < 100 ? GuestSpeed.BaseSpeeds[energy / 20] : GuestSpeed.BaseSpeeds[^1];
			if ( StaffVitals.Byte( member.Happiness ) > 97 )
				Draw( economy ); // the "happy" message's one-in-sixteen draw
			return false;
		}
		if ( FindRestArea( member ) is { } rest && Route( member, rest.Access, GuestWander.CentreByte, GuestWander.CentreByte ) )
		{
			member.RestAreaId = rest.Id;
			SetJob( economy, member, StaffJob.ToRestArea, turn );
			return true;
		}
		if ( (Draw( economy ) & 15) == 0 )
			member.Happiness = Math.Clamp( member.Happiness - settings.HappyHitCosNoRestArea, 0f, StaffVitals.Maximum );
		return false;
	}

	/// <summary>
	/// The rest finder (tp.exe <c>0x00506910</c>): the objects in list order with flag <c>+0x32</c> bit 1 (<c>UsageInfo.ChillsYouOut</c>), the smallest
	/// squared distance between their cell and the member's, the first on a tie; null when there is none.
	/// </summary>
	// [BIN:TPW-EXE:0x00506910 rest finder] the object list (game +0x1DA746, next at +0xC), flag +0x32 & 2, dx² + dy² of the cells unsigned and strictly smaller replaces, from 0xFFFFFFFF; no route test (the gate routes to the winner)
	private RestSite? FindRestArea( StaffMember member )
	{
		if ( RestAreas?.Invoke() is not { Count: > 0 } sites )
			return null;
		var (x, y) = Cell( member );
		RestSite? best = null;
		var bestDistance = uint.MaxValue;
		foreach ( var site in sites )
		{
			var dx = site.Cell.X - x;
			var dy = site.Cell.Y - y;
			var distance = (uint)(dx * dx + dy * dy);
			if ( distance < bestDistance )
				(best, bestDistance) = (site, distance);
		}
		return best;
	}

	// [BIN:TPW-EXE:0x00505FE0 walk to rest] the walk step: arrived, the rest area's script variable 0 (its thing +0x24, 0x0055A070 / 0x0055A0B0) + 1 and state 3; held (2): "couldn't get to his rest area", happiness −= HappyHitCosNoRestArea (clamped) and state 0
	// [BIN:TPW-EXE:0x005061D0 resting] energy +0x1FC += RecuperationRate[grade] (0x00785334 + 16 × grade) and happiness +0x1F8 += HappinessRecuperationRate[grade] (0x00785338), each clamped to 0..100; at an energy byte of 100 the rest ends (0x00506D10: the variable − 1, +0x208 = 0, state 0) and the type's search runs
	// [BIN:TPW-EXE:0x005056E0 strike walk] state 4: the walk step and message 0x15; arrived or held: +0x188 = 0 and state 5
	// [BIN:TPW-EXE:0x00506300 striking] a world draw turns the member's heading at (draw & 7) == 0 (presentation); the park closed and empty ends the role's strike (0x004C9130, 0x005099A0; the economy's turn here); with the role's strike flag clear a route to entrance A (FixedItemInfo.EntranceAPos, 0x007855BC / 0x007855C0): state 1, else "couldn't find park entrance" and state 0
	private void UpdateRestOrStrike( ParkEconomy economy, StaffMember member, long turn, int maxSpeed, int maxForce )
	{
		var settings = economy.Settings;
		switch ( member.Job )
		{
			case StaffJob.ToRestArea:
				switch ( Walk( member, maxSpeed, maxForce ) )
				{
					case WalkResult.Arrived when member.RestAreaId != 0 && economy.IsRestArea( member.RestAreaId ):
						SetJob( economy, member, StaffJob.Resting, turn );
						return;
					case WalkResult.Walking:
						return;
				}
				member.Happiness = Math.Clamp( member.Happiness - settings.HappyHitCosNoRestArea, 0f, StaffVitals.Maximum );
				member.RestAreaId = 0;
				SetJob( economy, member, StaffJob.Idle, turn );
				return;
			case StaffJob.Resting:
				if ( !StaffVitals.Rest( member, settings.RecuperationRate[member.Grade], settings.HappinessRecuperationRate[member.Grade] ) )
					return;
				member.RestAreaId = 0;
				SetJob( economy, member, StaffJob.Idle, turn );
				SearchByType( economy, member, turn );
				return;
			case StaffJob.ToStrike:
				if ( Walk( member, maxSpeed, maxForce ) == WalkResult.Walking )
					return;
				SetJob( economy, member, StaffJob.Striking, turn );
				return;
			case StaffJob.Striking:
				Draw( economy ); // the heading's turn
				if ( economy.RoleOnStrike( member.Type ) )
					return;
				SetJob( economy, member, RouteFromStrike( member ) ? StaffJob.Patrol : StaffJob.Idle, turn );
				return;
		}
	}

	/// <summary>The members resting in each rest area (its script's <c>VAR_STAFFIN</c>, which the original counts up on arrival and down at the rest's end).</summary>
	private void CountRestingStaff( ParkEconomy economy )
	{
		if ( RestingStaff == null || RestAreas?.Invoke() is not { Count: > 0 } sites )
			return;
		foreach ( var site in sites )
			RestingStaff( site.Id, economy.Staff.Members.Count( member => member.Job == StaffJob.Resting && member.RestAreaId == site.Id ) );
	}

	/// <summary>
	/// The walk to a strike point outside the park: along the paths to the park entrance of lane A, then off the paths past its ticket booth to its
	/// park-side crossing and along the road to the point.
	/// </summary>
	// Approximation (ECON-022, registered at UpdateStaff): OpenTPW's path grid ends at the park entrance, so a striker leaves along arrival lane A (entrance, ticket booth, park-side crossing) to the strike point and comes back the same way, where the original routes over its map's own cells
	private bool RouteToStrike( StaffMember member, (int X, int Y, int ByteX, int ByteY) point )
	{
		if ( Guests?.Lane( 0 ) is not { Length: >= 5 } lane || !Route( member, lane[^1], GuestWander.CentreByte, GuestWander.CentreByte ) )
			return false;
		member.OffGridLeg = [Centred( lane[3] ), Centred( lane[2] ), point];
		return true;
	}

	/// <summary>The walk back from the strike point to the park entrance of lane A (tp.exe <c>0x00506300</c> routes to entrance A).</summary>
	private bool RouteFromStrike( StaffMember member )
	{
		var navigation = member.Navigation!;
		if ( !navigation.OffGrid )
			return Guests?.Lane( 0 ) is { Length: >= 5 } inside && Route( member, inside[^1], GuestWander.CentreByte, GuestWander.CentreByte );
		if ( Guests?.Lane( 0 ) is not { Length: >= 5 } lane )
			return false;
		member.OffGridLeg = [Centred( lane[2] ), Centred( lane[3] ), Centred( lane[^1] )];
		NextLeg( member );
		return true;
	}

	private static (int X, int Y, int ByteX, int ByteY) Centred( (int X, int Y) cell ) => (cell.X, cell.Y, GuestWander.CentreByte, GuestWander.CentreByte);

	/// <summary>Starts the next off-grid stretch of the member's walk (off the path grid: no walls, every position accepted).</summary>
	private static void NextLeg( StaffMember member )
	{
		var navigation = member.Navigation!;
		var (x, y, byteX, byteY) = member.OffGridLeg[0];
		member.OffGridLeg.RemoveAt( 0 );
		navigation.OffGrid = true;
		var (px, py) = GuestNavigation.BytePoint( (x, y), byteX, byteY );
		navigation.SetDestination( px, py, [(x, y)] );
	}
}
