using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>Nominal hire-box geometry only; the separate employee and visitor windows keep their own placement.</summary>
internal static class HirePortraitGeometry
{
	private const int Left = 513, Top = 202, Right = 723, Bottom = 415;
	// [BIN:TPW-EXE:0x75207e hire portrait control] opcode 3 supplies the inner rectangle; the picture model retains the outer rectangle.
	internal static readonly UiRect Inner = new( Left, Top, Right - Left, Bottom - Top );
	internal static readonly NVector2 FeetInCanvas = Feet( Left, Top, Right, Bottom );

	// [BIN:TPW-EXE:0x49bb64 hire scale;0x589d9c screen scale;0x5890e0 frame extents] primary 8 * 0.05 * (1536 / 2) / 128 = 2.4 nominal canvas units.
	// UiSpriteFigure applies each frame's 128/A and 128/B to both hotspot offsets and dimensions.
	internal const float CanvasUnitsPerPixel = 2.4f;

	/// <summary>Integer authored endpoints are shifted before mapping to the viewport; no clone helper's scale-dependent Y term.</summary>
	// [BIN:TPW-EXE:0x49ba49 hire hotspot] signed half-width and quarter-height shifts, then the authored-canvas conversion's +8 X.
	internal static NVector2 Feet( int left, int top, int right, int bottom ) =>
		new( left + ((right - left) >> 1) + 8, bottom - ((bottom - top) >> 2) );
}
