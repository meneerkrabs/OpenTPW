namespace OpenTPW;

public partial class Level
{
	public FreeWalkCameraMode? FreeWalk { get; private set; }
	private CameraMode? cameraBeforeFreeWalk;

	/// <summary>[EXT:online-coop-snapshot-join] The park camera the free walk was entered from while it lasts, else null: a resync keeps the player's view from it.</summary>
	internal ParkCameraMode? ParkCameraWhileWalking => FreeWalk != null ? cameraBeforeFreeWalk as ParkCameraMode : null;

	/// <summary>Starts the camcorder walk at an engine ground point, looking where the park camera looked (the camcorder tool's click, <see cref="UseCamcorderTool"/>, and the captures).</summary>
	public bool EnterFreeWalk( Vector3 position )
	{
		if ( GameEditions.Current != GameEdition.ThemeParkWorld || FreeWalk != null || RideView != null
			|| Camera.Mode is not ParkCameraMode park || OriginalPark?.Heightfield is not { } field
			|| !FreeWalkCameraMode.TryGround( field, position.X, position.Y, out var ground ) ) return false;
		cameraBeforeFreeWalk = Camera.Mode;
		FreeWalk = new FreeWalkCameraMode( field, ground, Camera.Mode.Rotation.Forward, WalkEdgeBlocked, RideAtWalkerCell, park.Height );
		Camera.Mode = FreeWalk;
		return true;
	}

	/// <summary>Whether a walker with mask 2 may not step from a cell to its neighbour (<c>0x4D8750</c>): none in a park without cells.</summary>
	private bool WalkEdgeBlocked( int x, int y, int direction ) => Guests?.Grid.Cells is { } cells && WalkEdges.Blocked( cells, x, y, direction, 2 );

	/// <summary>
	/// The ride whose entrance lies in the cell a walk step reached (<c>0x42A340</c>): the cell is clamped to the map first (<c>0x42CD40</c>).
	/// </summary>
	private OriginalObject? RideAtWalkerCell( int x, int y ) =>
		Guests?.Grid.Cells is { } cells ? EntranceRide( Objects, cells, Math.Clamp( x, 0, cells.Width - 1 ), Math.Clamp( y, 0, cells.Height - 1 ) ) : null;

	public void ExitFreeWalk()
	{
		if ( FreeWalk == null ) return;
		if ( RideView != null ) ExitRideView();
		if ( ReferenceEquals( Camera.Mode, FreeWalk ) )
		{
			Camera.Mode = cameraBeforeFreeWalk ?? new ParkCameraMode();
			// [BIN:TPW-EXE:0x42AE70 leaving the walk] the park's target and yaw are put back as they were saved at the click (0x790AD0..0x790AD8, 0x790A9C); the zoom value is the one global the walk shares with the park camera
			if ( Camera.Mode is ParkCameraMode park )
			{
				park.AdoptZoom( FreeWalk.Zoom );
				if ( !EntityConstructionScope.IsStaging ) park.UpdateAudioListener();
			}
		}
		FreeWalk = null;
		cameraBeforeFreeWalk = null;
	}

	/// <summary>What the camcorder tool's click did.</summary>
	internal enum CamcorderClick
	{
		/// <summary>Nothing under the cursor to act on, or the click is refused: the tool stays.</summary>
		Refused,
		/// <summary>The click went on a ride or on the ground: the tool is spent whether or not the ride's camera opened or the walk began.</summary>
		Used
	}

	/// <summary>
	/// The camcorder tool's click (<c>0x46D0D0</c>) at the pointer: a ride under the cursor (the cell's owner thing whose type is a ride, Info.WhichUIType 0) is boarded as the ride window's button does
	/// (<c>0x4E15B0</c>); on the ground the cell must be owned (or an entrance-area walkway) and hold no park fence post, and the walk begins there when the cell's type is empty ground, a path, a queue, an
	/// entrance or a fixed walkway (<c>0x42AE70</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x46D0D0 camcorder click] the hover cell's owner thing (0x527E80) whose type's +0x4AC is 0 is ridden (0x4E15B0 with 0); else the cell is refused (the pointer 8 stays, the tool stays) unless its flag 0x40 is clear or its flag
	// 0x80 is set on a type 30 cell, and unless its structure record is a fence post (0x53AD60: type 0x19); then 0x42AE70 starts the walk, which does nothing for a cell of another type, and the tool is replaced either way
	// [BIN:TPW-EXE:0x42AE70 walk cell types] the cell of the hover point (0x7A1C70, 0x7A1C78 over 10, clamped to the map) must have type 30 (0x5363A0), 1 (0x536310), 3 or 9 (0x536320) or 0 (0x536390)
	internal CamcorderClick UseCamcorderTool( System.Numerics.Vector2 mouse, System.Numerics.Vector2 viewport )
	{
		if ( GameEditions.Current != GameEdition.ThemeParkWorld || FreeWalk != null || RideView != null || Camera.Mode is not ParkCameraMode
			|| OriginalPark?.Heightfield is not { } field || Guests?.Grid.Cells is not { } cells || !TryGetGroundHit( mouse, viewport, out var hit )
			|| !OriginalParkPlacement.TryGetCell( field, hit.X, hit.Y, out var x, out var y ) )
			return CamcorderClick.Refused;
		// a ride on the cell is boarded
		if ( Objects.FindAt( x, y ) is { IsDeleted: false } item && IsRideKind( item ) )
		{
			EnterRideView( item );
			return CamcorderClick.Used;
		}
		var flags = cells.FlagsAt( x, y );
		var type = cells.PrimaryTypeAt( x, y );
		if ( (flags & ParkCellFlags.Unowned) != 0 && !((flags & ParkCellFlags.EntranceArea) != 0 && type == ParkCellTypes.FixedWalkway) )
			return CamcorderClick.Refused;
		if ( cells.Tracks.Get( x, y ) is { IsEmpty: false, Type: ParkCellTypes.Hoarding } )
			return CamcorderClick.Refused;
		if ( type is 0 or ParkCellTypes.FixedWalkway or 1 or 3 or ParkCellTypes.Entrance )
			EnterFreeWalk( hit );
		return CamcorderClick.Used;
	}

	/// <summary>Whether an object is a ride (the type record's Info.WhichUIType, +0x4AC, is 0).</summary>
	internal static bool IsRideKind( OriginalObject item ) => item.Entry.WhichUIType == 0;

	/// <summary>
	/// Boards the ride the walk reached (<c>0x42C5B1</c>: <c>0x4E15B0</c> with the force argument 1 once the walker's cell is a ride's entrance); the walk stays under the ride and resumes when it ends.
	/// </summary>
	internal bool BoardWalkRide()
	{
		if ( FreeWalk?.TakePendingRide() is not { } ride || RideView != null )
			return false;
		return EnterRideView( ride );
	}
}
