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
	internal static void EnterGuestState( Guest guest, GuestState state )
	{
		// [BIN:TPW-EXE:0x00502305 state 18 entry] the leaving state's entry clears the target +0x1DC (0x0050231C) and the thought record, as a held guest's (state 17, 0x005022E0) does; the other states leave it, so it lives until the next major choice (0x004FCB21)
		if ( state == GuestState.LeavingPark )
		{
			guest.AttractionId = 0;
			guest.BinId = 0;
		}
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

	// [BIN:TPW-EXE:0x0050212B state 16 body] the entry tests the thing flag +0x32 bit 0x20 (set from UsageInfo.RideHandlesSprite, type +0x104, by the constructor at 0x004DB41C): clear deletes the body (0x004D4170 on +0xC), set keeps it, queues template 3 (stand, 0x004217F0) and sets +0x28 = 1
	private static void BoardBodySprite( Guest guest, IRideVisitorBridge ride )
	{
		if ( ride.RideHandlesSprite )
		{
			guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Stand );
			// [BIN:TPW-EXE:0x004FA087 held body] from here the guest update no longer places the body, so it stays where it was last drawn, even when a blocked ExitRide moves the guest itself (0x004FA930)
			(guest.KeptBodyX, guest.KeptBodyY) = (guest.DrawX, guest.DrawY);
		}
		else
			guest.BodySprite.DeletePlayer();
	}

	/// <summary>Where a kept body that stands in place is drawn (cells): its last placement, or the guest's own position when that is not known (a loaded park).</summary>
	public (float X, float Y) KeptBodyPosition( Guest guest ) =>
		float.IsFinite( guest.KeptBodyX ) && float.IsFinite( guest.KeptBodyY ) ? (guest.KeptBodyX, guest.KeptBodyY) : (guest.DrawX, guest.DrawY);

	/// <summary>
	/// A guest whose body the object keeps in view while it uses it (state 16): every object whose <c>UsageInfo.RideHandlesSprite</c> is 1 kept the body at the entry, whatever its kind (shop, sideshow,
	/// toilet or ride), and the body stays drawn until the state-15 entry places it again. Where the object's script walks or bounces it (WALKON, BOUNCE) the ride poses draw it
	/// (<see cref="FlatRideBounceRiders"/>, which also holds a body where its walk or bounce left it); otherwise it stands where it stood when the object took it, in the stand pose. A body the
	/// script's hidden walk actions deleted has no player and is not drawn. A changing room's guests stay out of view ([EXT:pools-swim]).
	/// </summary>
	// [BIN:TPW-EXE:0x0050212B state 16 body] the flag test has no object-kind test: any object whose type sets UsageInfo.RideHandlesSprite (+0x104, thing flag +0x32 bit 0x20 from the constructor 0x004DB41C) keeps the body and sets +0x28 = 1 (0x00502136)
	// [BIN:TPW-EXE:0x004FA087 held body] the guest update places its body at its own position (0x00475480) only while +0x28 (mSpriteUnderRideCtrl, saved by 0x004F8B10) is 0; the state-15 entry clears it (0x00501F79) and the removed object's message 10 (0x004FB3DF); no other guest code reads it, and the sprite pass (0x00542010) draws every sprite with a frame at its stored position whatever its owner's state
	public bool ShowsKeptBody( Guest guest ) =>
		guest.State == GuestState.Using && guest.BodySprite.Player != null && !guest.InSwimSession
		&& FindAttraction( guest.AttractionId ) is { RideHandlesSprite: true, IsChangingRoom: false };

	/// <summary>State 15 follows the use effects: a retained body keeps its copied appearance, while a new one uses the person's updated category.</summary>
	private void ReleaseBodySprite( Guest guest )
	{
		(guest.KeptBodyX, guest.KeptBodyY) = (float.NaN, float.NaN);
		if ( guest.InSwimSession )
			return; // Existing pools extension: no original lifecycle parity is claimed through swimming.
		if ( guest.BodySprite.Player == null )
			CreateBodySprite( guest, VisitorSpriteTemplate.Walk );
		else
			guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Walk );
	}

	private void RemovedRideBodySprite( Guest guest )
	{
		(guest.KeptBodyX, guest.KeptBodyY) = (float.NaN, float.NaN);
		if ( guest.BodySprite.Player == null )
			CreateBodySprite( guest, VisitorSpriteTemplate.Stand );
		else
			guest.BodySprite.QueueTemplate( VisitorSpriteTemplate.Walk );
	}

	/// <summary>The pose phase at the end of every fixed tick (Level.PrepareRideBodyPoses). It never advances the player or applies the guest's pending requests.</summary>
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
	// [APPROX:HEADS-007] an OpenTPW save older than the sprite state, or an original-save guest whose script record the bounded player cannot represent, has no shared visitor sprite history; reconstruct a fresh body in the host's load epoch, with default interval/no emitted frame
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

	/// <summary>
	/// A saved guest's body sprite: the person's sprite handle (+0x0C) names a saved record whose program position, wake time and interval become the player; a person with no handle has no sprite, and a record the bounded
	/// player cannot represent falls back to the rebuilt body with the reason noted.
	/// </summary>
	// [BIN:TPW-EXE:0x00475730 sprite loader] the saved record (program counter, entry, run state, due time against the saved game clock, period, output frame) is what the loader reads back for the person's handle (0x4D4140 binds the handle, 0x4D93B0 the person's pending words)
	private void ImportBodySprite( Guest guest, SaveGuest save, SavedSpriteScripts? sprites, List<string> notes )
	{
		if ( sprites == null )
		{
			ReconstructBodySprite( guest, BodySpriteClockMilliseconds );
			return;
		}
		if ( save.Person.SpriteScript == 0 )
		{
			guest.BodySprite.ResetForNewPerson();
			guest.BodySpriteHistoryReconstructed = false;
			notes.Add( $"ok guest {guest.Id} has no sprite" );
			return;
		}
		if ( sprites.Visitor( save.Person.SpriteScript, save.Person.NextAnim, save.Person.NextServiceInterval, BodySpriteClockMilliseconds, out var gap ) is { } snapshot )
		{
			guest.BodySprite.RestoreSnapshot( snapshot );
			guest.BodySpriteInCostume = guest.InCostume;
			guest.BodySpriteHistoryReconstructed = false;
			notes.Add( $"ok guest {guest.Id}" );
			return;
		}
		notes.Add( $"guest {guest.Id}: {gap}" );
		ReconstructBodySprite( guest, BodySpriteClockMilliseconds );
	}

	/// <summary>A saved guest's held balloon (+0x210 names its record), rebuilt fresh when the record is no held balloon program.</summary>
	private void ImportBalloon( Guest guest, SaveGuest save, SavedSpriteScripts? sprites, List<string> notes )
	{
		string? gap = null;
		if ( sprites?.Balloon( save.BalloonScript, guest.Id, BodySpriteClockMilliseconds, nextBalloonIdentity, out gap ) is { } balloon )
		{
			RemoveBalloonPlayer( guest );
			nextBalloonIdentity++;
			balloonPlayers.Add( balloon );
			notes.Add( $"ok balloon {guest.Id}" );
			return;
		}
		if ( sprites != null )
			notes.Add( $"balloon {guest.Id}: {gap}" );
		CreateBalloon( guest );
	}
}
