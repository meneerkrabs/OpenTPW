using System.Numerics;

namespace OpenTPW;

/// <summary>
/// The pose a tour car is drawn at, in the original's draw units: the position in model units (the module's 300 per unit), the heading and roll in 4096ths of a turn
/// and the pitch in 1024ths. Presentation state only: it is not saved and not hashed, and nothing in the simulation reads it.
/// </summary>
internal struct TourDrawPose
{
	public float X;
	public float Y;
	public float Z;
	public int Heading;
	public int Roll;
	public int Pitch;
}

/// <summary>The per-frame pose of a tour car and the rotation it is drawn with (the original's draw pass 0x0055CE60).</summary>
internal static class TourPose
{
	/// <summary>The most a draw fraction may reach (0x0055CE60 clamps it to 2).</summary>
	public const float MaximumFraction = 2f;

	private const float UnitsPerModelUnit = 0.0033333334f;

	/// <summary>The draw fraction: the time since the last tick over the tick's 62 ms (0x0054FA38; the divisor is the constant 0x00700F90).</summary>
	public static float Fraction( TourController controller ) => (float)Math.Min( controller.TickRemainder / TourController.TickSeconds, MaximumFraction );

	/// <summary>A car's first draw: it snaps to its current pose (the original's first-draw word, car +0xC8, set when the car is created).</summary>
	// [BIN:TPW-EXE:0x0055CEA6 first draw] the word at car +0xC8 is non-zero: the target is the current position / 300 and the current heading, pitch and roll, drawn as they are; the word clears
	public static TourDrawPose Snap( TourCar car ) => new()
	{
		X = car.X * UnitsPerModelUnit,
		Y = car.Y * UnitsPerModelUnit,
		Z = car.Z * UnitsPerModelUnit,
		Heading = car.Heading,
		Roll = car.Roll,
		Pitch = car.Pitch
	};

	/// <summary>
	/// The pose one frame later: the previous-to-current pose interpolated by <paramref name="fraction"/>, then eased into the drawn pose, a quarter of the way for the
	/// position, heading and roll and an eighth for the pitch (angles by the shortest way round).
	/// </summary>
	// [BIN:TPW-EXE:0x0055CF10 smoothing] target = previous + (current − previous) × f for the position (÷ 300) and for the angles (the difference masked to 12 bits, 0xFFF taken off when it is 0x800 or more); drawn = (target + 3 × drawn) / 4 for position, heading and roll, (target + 7 × drawn) / 8 for pitch, the angles truncated and masked to 12 bits. The drawn position stays a float (car +0x34..+0x3C)
	public static TourDrawPose Smooth( TourCar car, float fraction, in TourDrawPose drawn )
	{
		fraction = Math.Clamp( fraction, 0f, MaximumFraction );
		float Position( int current, int previous, float eased )
		{
			var target = (float)(((double)previous + (double)(current - previous) * fraction) * UnitsPerModelUnit);
			return (float)((target - eased * -3f) * 0.25f);
		}
		return new TourDrawPose
		{
			X = Position( car.X, car.PreviousX, drawn.X ),
			Y = Position( car.Y, car.PreviousY, drawn.Y ),
			Z = Position( car.Z, car.PreviousZ, drawn.Z ),
			Heading = Angle( car.Heading, car.PreviousHeading, fraction, drawn.Heading, 4 ),
			Roll = Angle( car.Roll, car.PreviousRoll, fraction, drawn.Roll, 4 ),
			Pitch = Angle( car.Pitch, car.PreviousPitch, fraction, drawn.Pitch, 8 )
		};
	}

	private static int Angle( int current, int previous, float fraction, int drawn, int weight )
	{
		var delta = (current - previous) & 0xFFF;
		if ( delta >= 0x800 )
			delta -= 0xFFF;
		var target = (int)((double)delta * fraction + previous) & 0xFFF;
		var eased = drawn & 0xFFF;
		var difference = target - eased;
		if ( difference > 0x800 )
			target -= 0x1000;
		else if ( difference < -0x800 )
			target += 0x1000;
		return ((target + (weight - 1) * eased) / weight) & 0xFFF;
	}

	/// <summary>
	/// The car's rotation in the original's model frame (Y up, row vectors): roll about Z, then pitch about X, then yaw about Y. The roll is negated and the pitch is negated
	/// and counted in 1024ths of a turn (four times the angle units of the heading), the yaw is a quarter turn less the heading, which points the model's +Z axis along the
	/// car's velocity (cos heading, sin heading).
	/// </summary>
	// [BIN:TPW-EXE:0x0055D2A5 draw angles] the helper 0x0046F6F0 reads its three angles with mode 0xE ("ZXY", string at 0x0074DA7C): −roll × 2π/4096 about Z (0x0046F5C0), −pitch × 2π/1024 about X (0x0046F350), π + (3072 − heading) × π/2048 ≡ π/2 − heading × 2π/4096 about Y (0x0046F650); each helper post-multiplies the instance matrix (0x0046F180 resets it, the translation is row 3 at +0x40..+0x48), so a model vertex v maps to v·Rz·Rx·Ry
	public static Matrix4x4 Rotation( in TourDrawPose pose )
	{
		var roll = 3.1415927f * pose.Roll * -0.00048828125f;
		var pitch = 3.1415927f * pose.Pitch * -0.001953125f;
		var yaw = (1f - (3072f - pose.Heading) * -0.00048828125f) * 3.1415927f;
		return AboutZ( roll ) * AboutX( pitch ) * AboutY( yaw );
	}

	// [BIN:TPW-EXE:0x0046F6F0 rotation builder] each axis step takes its cosine and sine from the engine's sine table (OriginalSineTable) and turns the matrix rows by it (0x0046F5C0 about Z, 0x0046F350 about X, 0x0046F650 about Y): the same rotation as the System.Numerics one with the table's sine and cosine
	private static Matrix4x4 AboutZ( float angle )
	{
		float cos = OriginalSineTable.Cos( angle ), sin = OriginalSineTable.Sin( angle );
		return new Matrix4x4( cos, sin, 0, 0, -sin, cos, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 );
	}

	private static Matrix4x4 AboutX( float angle )
	{
		float cos = OriginalSineTable.Cos( angle ), sin = OriginalSineTable.Sin( angle );
		return new Matrix4x4( 1, 0, 0, 0, 0, cos, sin, 0, 0, -sin, cos, 0, 0, 0, 0, 1 );
	}

	private static Matrix4x4 AboutY( float angle )
	{
		float cos = OriginalSineTable.Cos( angle ), sin = OriginalSineTable.Sin( angle );
		return new Matrix4x4( cos, 0, -sin, 0, 0, 1, 0, 0, sin, 0, cos, 0, 0, 0, 0, 1 );
	}

	/// <summary>
	/// The head node a rider hangs from: rider <paramref name="rider"/> (0-based) of a car with <paramref name="headNodes"/> head dummies takes node
	/// (random + rider + 1) mod headNodes, counting from 0. The car's own random word is the shuffle: no draw is made, so it is the same on every run and every machine.
	/// </summary>
	// [BIN:TPW-EXE:0x0055D830 TOUR 3] the head node of the boarded rider is ((car +0xD8 + the new rider count) mod car +0xDC) + 1 through 0x0044B220 (class 0x80); +0xDC counts the car mesh's consecutively numbered head nodes (0x0055AA2D); 0x0055D610 and 0x0055D720 take the head off with the same node
	public static int HeadNode( int random, int rider, int headNodes ) => headNodes <= 0 ? -1 : (int)(((long)random + rider + 1) % headNodes);
}
