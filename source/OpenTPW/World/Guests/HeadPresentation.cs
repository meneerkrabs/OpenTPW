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
// [APPROX:HEADS-004] the azimuth origin and sense of column 0 (local +X, towards +Z) and the reference of the roll (the node matrix in world axes, not the camera's) are not traced; how scale 1.0 maps to world units and the depth handling are untraced
public static class HeadPresentation
{
	public const uint HiddenFlag = 0x8000;
	public const uint MirroredFlag = 0x400;
	public const int Rows = 7;
	public const int Columns = 8;
	public const float HeadScale = 0.685f;
	private const float RowStep = MathF.PI / 6;
	private const float ColumnStep = MathF.PI / 4;

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
				var elevation = MathF.Acos( Math.Clamp( local.Y, -1f, 1f ) );
				row = Math.Clamp( (int)MathF.Round( elevation / RowStep, MidpointRounding.AwayFromZero ), 0, Rows - 1 );
				if ( row is not (0 or Rows - 1) )
				{
					var azimuth = MathF.Atan2( local.Z, local.X );
					column = ((int)MathF.Round( azimuth / ColumnStep, MidpointRounding.AwayFromZero ) % Columns + Columns) % Columns;
				}
			}
		}
		// A uniform positive scale of the matrix does not change the angle.
		var roll = MathF.Atan2( -nodeWorld.M21, nodeWorld.M22 ) / (2 * MathF.PI);
		if ( !float.IsFinite( roll ) )
			roll = 0;
		return new HeadPlacement( true, anchor, row, column, roll, HeadScale );
	}
}
