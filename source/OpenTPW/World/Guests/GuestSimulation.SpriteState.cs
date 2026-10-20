namespace OpenTPW;

/// <summary>The guest's persistent body presentation, independent of ordinary walker rendering and of bounce-table membership.</summary>
public sealed partial class GuestSimulation
{
	/// <summary>The latest host sample, saved in the same fixed-clock epoch as body due timestamps. It is not the ride script's quantized clock.</summary>
	public int BodySpriteClockMilliseconds { get; private set; }

	/// <summary>The established fixed-60Hz host clock, deliberately distinct from the native scheduler's independently sampled timer.</summary>
	// [EXT:fixed-60hz-clock] every scheduler opportunity in a tick shares its end-time sample; batching ticks does not sample a render or wall clock
	internal static int BodySpriteSampleAtTick( long tick ) => unchecked( (int)(tick / 60 * 1000 + tick % 60 * 1000 / 60) );

	private bool bodySpriteHostRunning = true;
	internal void SetBodySpriteHostRunning( bool running ) => bodySpriteHostRunning = running;
	internal void SetBodySpriteClock( int milliseconds ) => BodySpriteClockMilliseconds = milliseconds;

	/// <summary>The host invokes this after even-substep tour work, before its pooled guest sweep. Hidden and lost-park players still run.</summary>
	internal void ScheduleBodySprites()
	{
		ScheduleBalloons();
		foreach ( var guest in guests )
			guest.BodySprite.Tick( BodySpriteClockMilliseconds );
	}

	internal void ClearBodySprites()
	{
		balloonPlayers.Clear();
		foreach ( var guest in guests )
			guest.BodySprite.ResetForNewPerson();
	}

	private void CreateBodySprite( Guest guest, VisitorSpriteTemplate template )
	{
		guest.BodySprite.CreatePlayer( template, BodySpriteClockMilliseconds );
		// The actual host bank variation remains GuestAppearance's existing id-based choice; this captures the category without drawing RNG again.
		guest.BodySpriteInCostume = guest.InCostume;
	}

	/// <summary>Called at verified semantic state entries only. Temporary placement writes and snapshot/import reconstruction never call this helper.</summary>
	// [BIN:TPW-EXE:0x00501DB0 guest state entry] ordinary states queue walk 1 or stand 3 directly, independently of current/pending template
	private static void EnterGuestState( Guest guest, GuestState state )
	{
		guest.State = state;
		if ( StateBodyTemplate( state ) is { } template )
			guest.BodySprite.QueueTemplate( template );
	}

	internal static VisitorSpriteTemplate? StateBodyTemplate( GuestState state ) => state switch
	{
		GuestState.CrossingRoad or GuestState.GoingToTicketBooth or GuestState.EnteringPark or GuestState.WalkingAround or
			GuestState.ThrowingAwayLitter or GuestState.GoingToRide or GuestState.MovingUpQueue or GuestState.Boarding or
			GuestState.LeavingPark or GuestState.WalkingToVehicle => VisitorSpriteTemplate.Walk,
		GuestState.CrossingRoadToPark or GuestState.WaitingAtBooth or GuestState.AtTicketBooth or GuestState.DoingNothing or
			GuestState.Queueing or GuestState.WaitingToBoard or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome => VisitorSpriteTemplate.Stand,
		_ => null
	};

	private static void ObserveBodyWalk( Guest guest, int beforeX, int beforeY, WalkResult result )
	{
		if ( result != WalkResult.Walking )
			return;
		var run = unchecked( (ushort)guest.ToiletBonus ) > 0;
		guest.BodySprite.RequestWalkTemplate( run ? VisitorSpriteTemplate.Run : VisitorSpriteTemplate.Walk );
		guest.BodySprite.QueueInterval( BodyIntervalForStep( beforeX, beforeY, guest.Navigation.X, guest.Navigation.Y, run ) );
	}

	/// <summary>Original integer displacement arithmetic; existing host movement and ToiletBonus decisions are observed without changing them.</summary>
	// [BIN:TPW-EXE:0x004FA3F8 guest walk interval] int32 squared displacement, sqrt times the float speed constant, rounded to float before doubling unless +0xC2 > 0 unsigned
	internal static int BodyIntervalForStep( int beforeX, int beforeY, int afterX, int afterY, bool run )
	{
		var dx = unchecked( afterX - beforeX );
		var dy = unchecked( afterY - beforeY );
		var speed = (float)(Math.Sqrt( unchecked( dx * dx + dy * dy ) ) * SpritePlayer.SpeedPerUnit);
		if ( !run )
			speed += speed;
		if ( !float.IsFinite( speed ) || speed <= 0 )
			return 0;
		// 0x4D41D6 divides on x87 and calls ftol directly: do not round the quotient to float before truncation.
		var interval = (int)((double)SpritePlayer.IntervalNumerator / speed);
		return Math.Clamp( interval, 0, SpritePlayer.LongestInterval );
	}

	private static void BoardBodySprite( Guest guest, IRideVisitorBridge ride )
	{
		if ( ride.RideHandlesSprite )
			guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Stand );
		else
			guest.BodySprite.DeletePlayer();
	}

	/// <summary>State 15 follows the use effects: a retained body keeps its copied appearance, while a new one uses the person's updated category.</summary>
	private void ReleaseBodySprite( Guest guest )
	{
		if ( guest.InSwimSession )
			return; // Existing pools extension: no original lifecycle parity is claimed through swimming.
		if ( guest.BodySprite.Player == null )
			CreateBodySprite( guest, VisitorSpriteTemplate.Walk );
		else
			guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Walk );
	}

	private void RemovedRideBodySprite( Guest guest )
	{
		if ( guest.BodySprite.Player == null )
			CreateBodySprite( guest, VisitorSpriteTemplate.Stand );
		else
			guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Walk );
	}

	/// <summary>The once-per-host-update pose phase, after all fixed ticks. It never advances the player or applies the guest's pending requests.</summary>
	internal VisitorSpriteFrame? RequestRideBody( int guestId, int attractionId, VisitorSpriteTemplate template )
	{
		if ( Find( guestId ) is not { } guest || !RideHeadRenderer.CanDraw( guest, attractionId ) )
			return null;
		if ( guest.BodySprite.Player == null )
			CreateBodySprite( guest, template );
		else
			guest.BodySprite.RequestBody( template, BodySpriteClockMilliseconds );
		return guest.BodySprite.Player!.Output;
	}

	/// <summary>Only the decoded explicit hidden WALK actions delete a body; a missing pose or unknown action does not.</summary>
	internal void HideRideBody( int guestId, int attractionId )
	{
		if ( Find( guestId ) is { } guest && RideHeadRenderer.CanDraw( guest, attractionId ) )
			guest.BodySprite.DeletePlayer();
	}

	/// <summary>Old and original saves have no body history. Their explicitly reconstructed player starts at load, never at a bounce entry's start.</summary>
	// [APPROX:HEADS-007] an older or imported save has no shared visitor sprite history; reconstruct a fresh body in the host's load epoch, with default interval/no emitted frame
	private void ReconstructBodySprite( Guest guest, int nowMilliseconds )
	{
		guest.BodySprite.ResetForNewPerson();
		guest.BodySpriteHistoryReconstructed = true;
		if ( guest.State is GuestState.HeldByGuard or GuestState.Gone || guest.InSwimSession ||
			guest.State == GuestState.Using && FindAttraction( guest.AttractionId )?.RideHandlesSprite != true )
			return;
		var template = guest.Activity != GuestActivity.None ? (VisitorSpriteTemplate)guest.Activity :
			guest.State == GuestState.ExitingRide ? VisitorSpriteTemplate.Walk : StateBodyTemplate( guest.State ) ?? VisitorSpriteTemplate.Stand;
		guest.BodySprite.CreatePlayer( template, nowMilliseconds );
		guest.BodySpriteInCostume = guest.InCostume;
	}
}
