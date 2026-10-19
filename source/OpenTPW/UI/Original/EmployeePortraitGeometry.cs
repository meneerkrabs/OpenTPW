using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>Employee-only nominal geometry; hire and visitor portraits keep their separate hotspots.</summary>
internal static class EmployeePortraitGeometry
{
	// [BIN:TPW-EXE:0x4b537a employee scale;0x589d9c screen scale;0x5890e0 frame extents] primary 8 * 0.05 * (1536 / 2) / 128 = 2.4 nominal canvas units.
	// UiSpriteFigure applies the frame's 128/A and 128/B to both hotspot offsets and dimensions.
	internal const float CanvasUnitsPerPixel = 2.4f;

	/// <summary>The employee control's effective inner rectangle, with integer endpoint arithmetic before viewport mapping.</summary>
	internal static NVector2 Feet( UiRect inner ) => Feet( (int)inner.X, (int)inner.Y, (int)inner.Right, (int)inner.Bottom );

	// [BIN:TPW-EXE:0x486530 clone hotspot] signed half-width and quarter-height shifts; scale 8 gives trunc(8 * -0.25) = -2 Y.
	// Unlike the hire creator's no-offset Y and visitor scale 12's -3 Y, this placement is employee-specific.
	internal static NVector2 Feet( int left, int top, int right, int bottom ) =>
		new( left + ((right - left) >> 1) + 8, bottom - ((bottom - top) >> 2) - 2 );
}
