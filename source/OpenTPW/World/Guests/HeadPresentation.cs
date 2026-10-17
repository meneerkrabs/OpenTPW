using System.Numerics;

namespace OpenTPW;

/// <summary>Where and how a rider's head sprite is drawn for one head node (see <see cref="HeadPresentation"/>).</summary>
/// <param name="Visible">False when the node hides its head (node flag 0x8000: frame -1).</param>
/// <param name="Anchor">The node's world translation, in the space of the matrix given.</param>
/// <param name="Frame">Sprite frame 8 * <see cref="Row"/> + <see cref="Column"/> of the head art (7 rows of 8).</param>
/// <param name="Roll">Sprite rotation in turns (radians / 2 pi), atan2(-m10, m11) of the node matrix.</param>
/// <param name="Scale">Sprite scale property.</param>
public readonly record struct HeadPlacement( bool Visible, System.Numerics.Vector3 Anchor, int Row, int Column, float Roll, float Scale )
{
	public int Frame => Visible ? Row * HeadPresentation.Columns + Column : -1;
}

/// <summary>
/// GPU-free head presentation: anchor, frame, roll and scale of a rider's head from the world matrix of its head node
/// (MD2 axes, Y up, row vectors) and the camera position in the same world space.
/// </summary>
// [BIN:STP-PPC:0x1019B6F8 head draw] hide when the node has flag 0x8000 (frame -1); with flag 0x400 matrix rows 0 and 2 are negated; the position is exactly the node's world translation (no offset); the camera is expressed in the node's frame (general inverse, times the camera matrix), normalised, and binned to the nearest of 7 elevation rows (30 degree steps from the local +Y pole) by 8 azimuths (45 degree steps); frame = 8 * row + column with the column forced to 0 at rows 0 and 6; roll = atan2(-m10, m11) of the remaining rotation / 2 pi; scale property 0.685 (default 1.0); no animation
// [BIN:TPW-EXE:0x0044B9FC head sprite registers] the PC routine sets the sprite's frame register 0xD to 8 x row + column, register 9 to the roll in turns (0x46F200 = atan2(-m10, m11), times 0x006FE2CC = 1 / 2 pi) and register 8, the scale, to 0x0074CED0 = 0.685; the script's flags 0x3000080 rotate the quad (0x00589388) and draw it as a camera-facing sprite at the node's position
// [APPROX:HEADS-004] OpenTPW takes the roll from the node matrix in world axes; the PC routine builds the chosen frame's own reference basis from its row and column (0x44B6EF..0x44B96C) and multiplies it in before 0x46F200, which is not compared instruction by instruction
public static class HeadPresentation
{
	public const uint HiddenFlag = 0x8000;
	public const uint MirroredFlag = 0x400;
	public const int Rows = 7;
	public const int Columns = 8;
	public const float HeadScale = 0.685f;
	private const float RowStep = MathF.PI / 6;
	private const float ColumnStep = MathF.PI / 4;

	/// <summary>The unit direction of head art frame (row, column) in the node's frame (Y up): row r is r * 30 degrees from +Y towards +Z, column c turns it by c * 45 degrees about Y from +Z towards -X.</summary>
	// [BIN:TPW-EXE:0x0044B510 head frame table] the draw routine (called per head node) starts at (0, 1, 0) and steps row by row with a 30 degree rotation of (y, z) (cos 30, sin 30 from the sine table at 2 * pi / 3 and pi / 6) and column by column with a 45 degree rotation of (x, z) (x' = 0.7071 * (x - z), z' = 0.7071 * (x + z), 8 steps), so direction (r, c) = (-sin(30 r) sin(45 c), cos(30 r), sin(30 r) cos(45 c)): column 0 faces local +Z and the azimuth atan2(z, x) grows by 45 degrees per column from 90 degrees
	public static System.Numerics.Vector3 FrameDirection( int row, int column )
	{
		var elevation = row * RowStep;
		var azimuth = column * ColumnStep;
		return new System.Numerics.Vector3( -MathF.Sin( elevation ) * MathF.Sin( azimuth ), MathF.Cos( elevation ), MathF.Sin( elevation ) * MathF.Cos( azimuth ) );
	}

	/// <summary>The frame whose direction has the largest dot product with the camera direction in the node's frame (the first one on a tie; the first and last rows always use column 0).</summary>
	// [BIN:TPW-EXE:0x0044B7B2 nearest frame] rows 0..6 by columns 0..7 are tried in that order and a frame replaces the best one only when its dot product is greater (the best starts at 0); rows 0 and 6 then take column 0 (0x44B81C)
	public static (int Row, int Column) NearestDirection( System.Numerics.Vector3 local )
	{
		var best = 0f;
		int bestRow = 0, bestColumn = 0;
		for ( var row = 0; row < Rows; row++ )
		{
			for ( var column = 0; column < Columns; column++ )
			{
				var dot = System.Numerics.Vector3.Dot( local, FrameDirection( row, column ) );
				if ( dot > best )
				{
					best = dot;
					bestRow = row;
					bestColumn = column;
				}
			}
		}
		return (bestRow, bestRow is 0 or Rows - 1 ? 0 : bestColumn);
	}

	/// <param name="nodeWorld">World matrix of the head node (row vectors, translation in the fourth row).</param>
	/// <param name="nodeFlags">The node's type flags (<see cref="ModelNode.Type"/>).</param>
	/// <param name="cameraPosition">Camera position in the same space as <paramref name="nodeWorld"/>.</param>
	public static HeadPlacement Compute( Matrix4x4 nodeWorld, uint nodeFlags, System.Numerics.Vector3 cameraPosition )
	{
		var anchor = nodeWorld.Translation;
		if ( (nodeFlags & HiddenFlag) != 0 )
			return new HeadPlacement( false, anchor, 0, 0, 0, HeadScale );
		if ( (nodeFlags & MirroredFlag) != 0 )
		{
			nodeWorld.M11 = -nodeWorld.M11; nodeWorld.M12 = -nodeWorld.M12; nodeWorld.M13 = -nodeWorld.M13;
			nodeWorld.M31 = -nodeWorld.M31; nodeWorld.M32 = -nodeWorld.M32; nodeWorld.M33 = -nodeWorld.M33;
		}
		var row = 0;
		var column = 0;
		if ( Matrix4x4.Invert( nodeWorld, out var inverse ) )
		{
			var local = System.Numerics.Vector3.Transform( cameraPosition, inverse );
			var length = local.Length();
			if ( float.IsFinite( length ) && length > 0 )
			{
				local /= length;
				(row, column) = NearestDirection( local );
			}
		}
		// A uniform positive scale of the matrix does not change the angle.
		var roll = MathF.Atan2( -nodeWorld.M21, nodeWorld.M22 ) / (2 * MathF.PI);
		if ( !float.IsFinite( roll ) )
			roll = 0;
		return new HeadPlacement( true, anchor, row, column, roll, HeadScale );
	}
}
