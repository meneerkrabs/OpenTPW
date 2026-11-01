namespace OpenTPW;

/// <summary>What the camcorder walk needs from the park: the ground, the map's size, the walk edge test and the rides' entrance cells.</summary>
internal interface ICamcorderWalkWorld
{
	/// <summary>The ground's height under a point in original units (<c>0x452560</c>, hole flag ignored); 0 off the map.</summary>
	float Height( float x, float z );

	/// <summary>The map's width and depth in original units (cells times the cell size, <c>0x42B6A8</c>: the heightfield's <c>+0x18</c> and <c>+0x10</c>, <c>+0x1C</c> and <c>+0x14</c>).</summary>
	float Width { get; }
	float Depth { get; }

	/// <summary>Whether the walk with mask 2 may not step from a cell to its neighbour (<c>0x4D8750</c>); direction 0 is -z, 1 +x, 2 +z and 3 -x.</summary>
	bool Blocked( int cellX, int cellZ, int direction );

	/// <summary>The ride whose entrance lies in a cell, if the cell is one (<c>0x42A340</c>: the first thing of type 3 of the cell's list whose type has CannotRide 0).</summary>
	object? RideAt( int cellX, int cellZ );
}

/// <summary>One frame's input of the camcorder walk, as <c>0x42B1C0</c> reads it.</summary>
internal struct CamcorderWalkInput
{
	/// <summary>The turn keys (bits 8 and 0x10 of <c>0x790AAC</c>: the Left arrow or numpad 9, the Right arrow or numpad 7).</summary>
	public bool TurnLeft, TurnRight;
	/// <summary>The walk keys (bits 0x20 and 0x40: the Up arrow or numpad 1, the Down arrow or numpad 3).</summary>
	public bool Forward, Backward;
	/// <summary>The right mouse button held (bit 4: walks forward).</summary>
	public bool Button;
	/// <summary>The Home key or numpad 5 (bit 0x100).</summary>
	public bool Home;
	/// <summary>The pointer's offset from the view's centre in half sizes (X right, Y up).</summary>
	public float PointerX, PointerY;
}

/// <summary>
/// The camcorder walk (camera mode 2, <c>gui_CameraFlags</c> 2): the frame step of the camera update <c>0x42B1C0</c> for that mode, in the original's units (a cell is ten of them) and in single
/// precision as the x87 runs them (the thread's control word is 24 bits). The eye stands at the ground plus 5 and looks along (sin yaw cos pitch, -sin pitch, cos yaw cos pitch).
/// </summary>
internal sealed class CamcorderWalk
{
	// [BIN:TPW-EXE:0x42B1C0 walk constants] 0.0012 radians per millisecond (0x6FDDA4, 0x6FDDA8), a walk key's speed 0.1 (0x6FDDAC), the pointer's dead zone of 0.4 either side (0x6FDDE4, 0x6FDDEC) with twice the
	// excess as the turn rate, the pitch's 0.2 and -0.4 limits and 1.2 factor (0x6FDDE0, 0x6FDDE4, 0x6FDDF0), the steering's 0.02 x the square root of the zoom value and half the sum with the last velocity (0x6FDDF8,
	// 0x6FDDFC), the standstill limit 0.0001 (0x74C988) and the damping 1 + 0.01 x the frame time (0x6FDDC8) outside the map
	public const float TurnPerMillisecond = 0.0012f, KeySpeed = 0.1f, DeadZone = 0.4f, EyeAboveGround = 5f;
	/// <summary>The cell edge's reach inside a cell: a step stopped by a wall stands 9.999 into it (0x74C9C4).</summary>
	public const float CellReach = 9.999f;
	/// <summary>The nudge across a cell edge a step reached exactly (0x74C9D0).</summary>
	public const float Nudge = 0.001f;
	/// <summary>The factor that breaks a tie between the two edges' arrival times (0x74C9C8).</summary>
	public const float TieFactor = 1.01f;
	/// <summary>The most steps one frame's walk takes: a frame's movement crosses a handful of cells, the original has no bound.</summary>
	private const int MaximumSteps = 4096;

	/// <summary>The position in original units (0x7908F0 and 0x7908F8).</summary>
	public float X, Z;
	/// <summary>The yaw in radians (0x790A38), which the walk turns directly (the smoothed yaw 0x7909E0 follows it at once).</summary>
	public float Yaw;
	/// <summary>The velocity (0x790A88, 0x790A8C, 0x790A90).</summary>
	public float VelocityX, VelocityY, VelocityZ;
	/// <summary>The look pitch of the pointer (0x790ADC, positive looks down) and the turn rate it asks (0x790AE0).</summary>
	public float Pitch, TurnRate;
	/// <summary>The eye's height (0x790AA4).</summary>
	public float EyeY;
	/// <summary>The ride the last step's walk entered (the entrance cell it ended in), or null.</summary>
	public object? EnteredRide { get; private set; }

	/// <summary>The frame time the camera update works with: the milliseconds elapsed since the last frame plus one, whole, and 100 when that is above 200 (a stalled frame).</summary>
	// [BIN:TPW-EXE:0x42B2A0 frame time] dt = the millisecond tick now (0x402D80) + 1 less the last frame's tick ([0x7909E4]); above 200 it becomes 100.0 (0x42B2D4), and 0 or less 1.0 (0x42B2ED)
	public static float FrameMilliseconds( float elapsedMilliseconds )
	{
		var whole = float.IsFinite( elapsedMilliseconds ) ? MathF.Floor( Math.Max( elapsedMilliseconds, 0f ) ) : 0f;
		var dt = whole + 1f;
		return dt > 200f ? 100f : dt;
	}

	/// <summary>The Home pose (0x42A990 and the Home key: the park's entrance, 475 and 175, yaw 0).</summary>
	public void Home()
	{
		X = 475f;
		Z = 175f;
		Yaw = 0f;
	}

	/// <summary>The look direction in original axes (x, y up, z): the camera matrix's third row <c>0x46FB50</c> builds from the pitch and the yaw.</summary>
	public (float X, float Y, float Z) Forward
	{
		get
		{
			var yaw = (double)Yaw;
			var pitch = (double)Pitch;
			return ((float)(Math.Sin( yaw ) * Math.Cos( pitch )), (float)-Math.Sin( pitch ), (float)(Math.Cos( yaw ) * Math.Cos( pitch )));
		}
	}

	/// <summary>The turn rate and look pitch the pointer asks for: both from its offset, a dead zone of 0.4 across and 0.2 up (the ride view's rules have 0.2 across).</summary>
	// [BIN:TPW-EXE:0x42B885 walk look] with no ride bit in 0x3C: x below -0.4 gives 2 x (x + 0.4), above 0.4 2 x (x - 0.4), else 0 (0x790AE0); y below -0.4 gives -y - 0.4, above 0.2 (0.2 - y) x 1.2, else 0 (0x790ADC)
	public static (float TurnRate, float Pitch) Look( float x, float y )
	{
		float turn = x < -DeadZone ? Twice( x - -DeadZone ) : x <= DeadZone ? 0f : Twice( x - DeadZone );
		float pitch = y < -DeadZone ? -y - DeadZone : y <= 0.2f ? 0f : (0.2f - y) * 1.2f;
		return (turn, pitch);
	}

	private static float Twice( float value ) => value + value;

	/// <summary>One frame of <paramref name="frameMilliseconds"/> (the original's frame time, 1 to 200) with the park's zoom value <paramref name="zoom"/> (the speed's square root).</summary>
	/// <returns>The ride the walk entered, null when it ended in no entrance cell.</returns>
	public object? Step( float frameMilliseconds, in CamcorderWalkInput input, float zoom, ICamcorderWalkWorld world )
	{
		EnteredRide = null;
		var dt = frameMilliseconds;
		var forward = 0f;
		// [BIN:TPW-EXE:0x42B412 walk keys] the Left key lowers the yaw by 0.0012 x the frame time and the Right key raises it; the Down key asks -0.1 and the Up key 0.1 (the two cancel)
		if ( input.TurnLeft )
			Yaw = Yaw - dt * TurnPerMillisecond;
		if ( input.TurnRight )
			Yaw = Yaw - dt * -TurnPerMillisecond;
		if ( input.Backward )
			forward = -KeySpeed;
		if ( input.Forward )
			forward = forward - -KeySpeed;
		if ( input.Home )
			Home();
		(TurnRate, Pitch) = Look( input.PointerX, input.PointerY );
		// the right button held walks forward too
		if ( input.Button )
			forward = forward - -KeySpeed;
		// [BIN:TPW-EXE:0x42B6A8 map edges] bit 1: x below 0, bit 4: z below 0, bit 2: x beyond the map's width, bit 8: z beyond its depth (read before the frame's move)
		var edges = 0;
		if ( X < 0f )
			edges = 1;
		if ( Z < 0f )
			edges |= 4;
		if ( !(X <= world.Width) )
			edges |= 2;
		if ( !(Z <= world.Depth) )
			edges |= 8;
		// [BIN:TPW-EXE:0x42BAB3 walk turn] the pointer's turn rate adds 0.0012 x the rate x the frame time to the yaw; the walk's direction is (sin yaw, cos yaw) in x and z
		var turn = dt * TurnRate;
		Yaw = Yaw - turn * -TurnPerMillisecond;
		var sine = Math.Sin( (double)Yaw );
		var cosine = Math.Cos( (double)Yaw );
		var wantX = (float)(sine * forward);
		var wantZ = (float)cosine * forward;
		// [BIN:TPW-EXE:0x42BC2A walk velocity] the new velocity is half the sum of 0.02 x the square root of the zoom value times the wanted velocity and the last one
		var oldX = VelocityX;
		var oldZ = VelocityZ;
		VelocityY = 0f;
		var steering = (float)Math.Sqrt( (double)zoom ) * 0.02f;
		VelocityX = (steering * wantX + oldX) * 0.5f;
		VelocityZ = (steering * wantZ + oldZ) * 0.5f;
		var damping = 1f - dt * -0.01f;
		// [BIN:TPW-EXE:0x42BC9C map edge damping] beyond the map a velocity that carries further out is the last one divided by 1 + 0.01 x the frame time
		if ( (edges & 1) != 0 && VelocityX < 0f )
			VelocityX = oldX / damping;
		if ( (edges & 4) != 0 && VelocityZ < 0f )
			VelocityZ = oldZ / damping;
		if ( (edges & 2) != 0 && VelocityX > 0f )
			VelocityX = oldX / damping;
		if ( (edges & 8) != 0 && VelocityZ > 0f )
			VelocityZ = oldZ / damping;
		if ( MathF.Abs( VelocityX ) < 0.0001f && MathF.Abs( VelocityY ) < 0.0001f && MathF.Abs( VelocityZ ) < 0.0001f )
			VelocityX = VelocityY = VelocityZ = 0f;
		Move( dt, world );
		EyeY = world.Height( X, Z ) - -EyeAboveGround;
		return EnteredRide;
	}

	// [BIN:TPW-EXE:0x42BDC6 walk move] the frame's move (velocity x frame time, a part under 0.0001 dropped) is taken cell edge by cell edge: the time to the next edge on each axis (the fraction of the cell left over the move's
	// share of a cell, 2 when the axis stands still), the nearer edge first (z when they tie, after the x move is shortened by 1.01); an edge the walk test 0x4D8750 (mask 2) refuses stops that axis at the cell's near
	// edge (cell x 10) or 9.999 into it; the cell then reached is tested for a ride's entrance (0x42A340), which takes the walk into that ride (0x4E15B0)
	private void Move( float dt, ICamcorderWalkWorld world )
	{
		var moveX = dt * VelocityX;
		var moveY = dt * VelocityY;
		var moveZ = dt * VelocityZ;
		_ = moveY;
		if ( moveX >= -0.0001f && moveX <= 0.0001f )
			moveX = 0f;
		if ( moveZ >= -0.0001f && moveZ <= 0.0001f )
			moveZ = 0f;
		if ( moveX == 0f && moveZ == 0f )
			return;
		var again = true;
		for ( var steps = 0; again && steps < MaximumSteps; steps++ )
		{
			var cellX = (int)(X * 0.1f);
			var cellZ = (int)(Z * 0.1f);
			var cellMoveX = moveX * 0.1f;
			var cellMoveZ = moveZ * 0.1f;
			var newX = 0f;
			var newZ = 0f;
			var timeX = EdgeTime( X, moveX, cellMoveX );
			var timeZ = EdgeTime( Z, moveZ, cellMoveZ );
			if ( timeX == timeZ && moveX != 0f && moveZ != 0f )
			{
				moveX = moveX / TieFactor;
				timeX = timeX * TieFactor;
			}
			var newCellX = 0;
			var newCellZ = 0;
			if ( timeX < timeZ )
			{
				if ( timeX < 1f )
				{
					var direction = moveX < 0f ? 3 : 1;
					if ( world.Blocked( cellX, cellZ, direction ) )
					{
						newX = moveX < 0f ? cellX * 10f : CellReach - cellX * -10f;
						moveX = 0f;
						newCellX = (int)(newX * 0.1f);
					}
					else
					{
						var share = timeX * moveX;
						newX = X + share;
						moveX = moveX - share;
						newCellX = (int)(newX * 0.1f);
						if ( cellX == newCellX )
						{
							newX = moveX < 0f ? newX - Nudge : newX + Nudge;
							newCellX = (int)(newX * 0.1f);
						}
					}
					var shareZ = timeX * moveZ;
					newZ = Z + shareZ;
					moveZ = moveZ - shareZ;
					newCellZ = (int)(newZ * 0.1f);
					if ( cellZ != newCellZ )
					{
						newZ = moveZ < 0f ? cellZ * 10f : CellReach - cellZ * -10f;
						newCellZ = (int)(newZ * 0.1f);
					}
					again = moveX != 0f || moveZ != 0f;
				}
				else
					again = FullMove( cellX, cellZ, moveX, moveZ, out newX, out newZ );
			}
			else if ( timeZ < 1f )
			{
				var direction = moveZ < 0f ? 0 : 2;
				if ( world.Blocked( cellX, cellZ, direction ) )
				{
					newZ = moveZ < 0f ? cellZ * 10f : CellReach - cellZ * -10f;
					moveZ = 0f;
					newCellZ = (int)(newZ * 0.1f);
				}
				else
				{
					var share = timeZ * moveZ;
					newZ = Z + share;
					moveZ = moveZ - share;
					newCellZ = (int)(newZ * 0.1f);
					if ( cellZ == newCellZ )
					{
						newZ = moveZ < 0f ? newZ - Nudge : newZ + Nudge;
						newCellZ = (int)(newZ * 0.1f);
					}
				}
				var shareX = timeZ * moveX;
				newX = X + shareX;
				moveX = moveX - shareX;
				newCellX = (int)(newX * 0.1f);
				if ( cellX != newCellX )
				{
					newX = moveX < 0f ? cellX * 10f : CellReach - cellX * -10f;
					newCellX = (int)(newX * 0.1f);
				}
				again = moveX != 0f || moveZ != 0f;
			}
			else
				again = FullMove( cellX, cellZ, moveX, moveZ, out newX, out newZ );
			var reachedX = Math.Clamp( (int)(newX * 0.1f), 0, int.MaxValue );
			var reachedZ = Math.Clamp( (int)(newZ * 0.1f), 0, int.MaxValue );
			reachedX = Math.Min( reachedX, MapCells - 1 );
			reachedZ = Math.Min( reachedZ, MapCells - 1 );
			if ( world.RideAt( reachedX, reachedZ ) is { } ride )
			{
				EnteredRide = ride;
				again = false;
			}
			X = newX;
			Z = newZ;
		}
	}

	/// <summary>The side of the map the walk clamps the cell it tests for an entrance to (0x7A0314 and 0x7A0374: 128).</summary>
	public const int MapCells = 128;

	/// <summary>The time, as a fraction of the move, until the next cell edge on an axis: the cell's fraction left (to the near edge going down, to the far going up) over the move in cells; 2 when it stands still.</summary>
	private static float EdgeTime( float position, float move, float cellMove )
	{
		if ( move == 0f )
			return 2f;
		var cells = position * 0.1f;
		var fraction = (float)((double)cells - Math.Truncate( (double)cells ));
		return MathF.Abs( (move < 0f ? fraction : 1f - fraction) / cellMove );
	}

	/// <summary>The whole move in one go (no edge ahead within it): the new position, an axis that left its cell stopped at that cell's edge.</summary>
	private bool FullMove( int cellX, int cellZ, float moveX, float moveZ, out float newX, out float newZ )
	{
		newX = moveX + X;
		newZ = moveZ + Z;
		var reachedX = (int)(newX * 0.1f);
		var reachedZ = (int)(newZ * 0.1f);
		if ( cellX != reachedX )
			newX = moveX < 0f ? cellX * 10f : CellReach - cellX * -10f;
		if ( cellZ != reachedZ )
			newZ = moveZ < 0f ? cellZ * 10f : CellReach - cellZ * -10f;
		return false;
	}
}
