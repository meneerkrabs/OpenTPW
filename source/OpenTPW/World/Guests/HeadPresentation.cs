using System.Numerics;

namespace OpenTPW;

/// <summary>Where and how a rider's head sprite is drawn for one head node (see <see cref="HeadPresentation"/>).</summary>
/// <param name="Visible">False when the node hides its head (node flag 0x8000: frame -1).</param>
/// <param name="Anchor">The node's world translation, in the space of the matrix given.</param>
/// <param name="Frame">Sprite frame 8 * <see cref="Row"/> + <see cref="Column"/> of the head art (7 rows of 8).</param>
/// <param name="Roll">Sprite rotation in turns (radians / 2 pi), atan2(-m10, m11) after removing the selected frame basis from the camera-to-node matrix.</param>
/// <param name="Scale">Sprite scale property.</param>
public readonly record struct HeadPlacement( bool Visible, System.Numerics.Vector3 Anchor, int Row, int Column, float Roll, float Scale )
{
	public int Frame => Visible ? Row * HeadPresentation.Columns + Column : -1;
}

/// <summary>
/// GPU-free head presentation: anchor, frame, roll and scale of a rider's head from the world matrix of its head node
/// (MD2 axes, Y up, row vectors) and the camera world matrix in the same world space (right, up, front rows).
/// </summary>
// [BIN:STP-PPC:0x1019B6F8 head draw] hide when the node has flag 0x8000 (frame -1); with flag 0x400 matrix rows 0 and 2 are negated; the position is exactly the node's world translation (no offset); the camera is expressed in the node's frame (general inverse, times the camera matrix), normalised, and binned to the nearest of 7 elevation rows (30 degree steps from the local +Y pole) by 8 azimuths (45 degree steps); frame = 8 * row + column with the column forced to 0 at rows 0 and 6; roll = atan2(-m10, m11) of the remaining rotation / 2 pi; scale property 0.685 (default 1.0); no animation
// [BIN:TPW-EXE:0x0044B9FC head sprite registers] the PC routine sets the sprite's frame register 0xD to 8 x row + column, register 9 to the roll in turns (0x46F200 = atan2(-m10, m11), times 0x006FE2CC = 1 / 2 pi) and register 8, the scale, to 0x0074CED0 = 0.685; the script's flags 0x3000080 rotate the quad (0x00589388) and draw it as a camera-facing sprite at the node's position
// [BIN:TPW-EXE:0x0044B510 head placement] the frame, roll and scale registers come from OriginalHeadMath, which reproduces the routine's x87 arithmetic bit for bit (HeadPlacementGolden)
public static class HeadPresentation
{
	public const uint HiddenFlag = 0x8000;
	public const uint MirroredFlag = 0x400;
	public const int Rows = 7;
	public const int Columns = 8;
	public const float HeadScale = 0.685f;

	/// <summary>Converts the renderer's RH view basis and engine Z-up position to the native MD2 right/up/front camera basis.</summary>
	public static Matrix4x4 CameraFromEngineView( Matrix4x4 view, System.Numerics.Vector3 position ) => new(
		view.M11, view.M31, view.M21, 0,
		view.M12, view.M32, view.M22, 0,
		-view.M13, -view.M33, -view.M23, 0,
		position.X, position.Z, position.Y, 1 );

	private static float[] RowMajor( Matrix4x4 m ) => new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };

	/// <param name="nodeWorld">World matrix of the head node (row vectors, translation in the fourth row).</param>
	/// <param name="nodeFlags">The node's type flags (<see cref="ModelNode.Type"/>).</param>
	/// <param name="cameraWorld">Camera matrix in MD2 world space: right, up, front rows, translation in the fourth row.</param>
	public static HeadPlacement Compute( Matrix4x4 nodeWorld, uint nodeFlags, Matrix4x4 cameraWorld )
	{
		var anchor = nodeWorld.Translation;
		if ( (nodeFlags & HiddenFlag) != 0 )
			return new HeadPlacement( false, anchor, 0, 0, 0, HeadScale );
		if ( (nodeFlags & MirroredFlag) != 0 )
		{
			nodeWorld.M11 = -nodeWorld.M11; nodeWorld.M12 = -nodeWorld.M12; nodeWorld.M13 = -nodeWorld.M13;
			nodeWorld.M31 = -nodeWorld.M31; nodeWorld.M32 = -nodeWorld.M32; nodeWorld.M33 = -nodeWorld.M33;
		}
		// [EXT:original-undefined-reads] a camera offset that is not a finite direction ends the search at frame 0 (0x44B6D4) and the original takes the roll from stack words it never wrote; the host rolls by 0
		var row = 0;
		var column = 0;
		var roll = 0f;
		if ( OriginalHeadMath.TryFrame( RowMajor( nodeWorld ), RowMajor( cameraWorld ), out _, out var frameRow, out var frameColumn, out var frameRoll ) )
			(row, column, roll) = (frameRow, frameColumn, frameRoll);
		if ( !float.IsFinite( roll ) )
			roll = 0;
		return new HeadPlacement( true, anchor, row, column, roll, HeadScale );
	}
}
