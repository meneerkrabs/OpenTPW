using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// The runtime geometry of the HUD arm's drawer (procedure 0x4a14f0 of arm control 0x21): where a slide-out panel starts, where it ends and how far it moves per timer tick.
/// Every figure is integer arithmetic on the runtime rectangles of the HUD window and the arm, in the layout tables' units (the window system keeps those units; its 16-bit
/// rectangle words wrap exactly as the originals do).
/// </summary>
internal static class ArmDrawer
{
	/// <summary>The timer ticks of a slide: the count the slide-out writes to <c>[arm + 0x130]</c> (0x4a15a7).</summary>
	public const int Ticks = 10;

	/// <summary>The horizontal gap between a new panel and the arm's left edge (0x4a1631, <c>sub ebx, 0x32</c>).</summary>
	public const int Gap = 50;

	/// <summary>The extra travel added to the step numerator (0x4a16e5, <c>lea eax, [edx + ecx + 0x64]</c>).</summary>
	public const int Overshoot = 100;

	/// <summary>The right edge of the HUD window (child 0x1D of the HUD table 0x752940, (37,984)-(439,1507); the slide-out reads it through <c>[0x7cb2b8]</c>, written at 0x4a255c).</summary>
	// [BIN:TPW-EXE:0x752940 HUD window] the table's first record is window 0x1D with the rectangle words 0x25, 0x3D8, 0x1B7, 0x5E3 (left, top, right, bottom); the HUD builder 0x4a1d70 finds it as child 0x1D of the interface root [0x7cb2ac] (0x4a2305)
	public const int HudRight = 439;

	private static readonly UiRect ArmTable = OriginalLayoutTables.HudArm.Control( 0x21 ).Rect;
	private static readonly UiRect HandleTable = OriginalLayoutTables.HudArm.Control( 0x23 ).Rect;

	/// <summary>The arm's left edge (child 0x21 of the HUD window, table 0x752940).</summary>
	public static int ArmLeft => (int)ArmTable.X;

	/// <summary>The arm's top edge.</summary>
	public static int ArmTop => (int)ArmTable.Y;

	/// <summary>The arm's height; the arm's vertical extent never changes (only its right edge moves).</summary>
	public static int ArmHeight => (int)ArmTable.Height;

	/// <summary>
	/// The arm's right edge at rest. The HUD builder shortens the arm by (the arm's left - its handle's left): it reads the rectangles of the arm 0x21 and its handle 0x23 (child of 0x22)
	/// and adds the difference to the arm's right edge (0x4a23ab-0x4a23e2), and the resize moves the right-anchored children with it, so the handle then starts at the arm's left.
	/// </summary>
	// [BIN:TPW-EXE:0x4a23d0 arm home rectangle] arm.right += arm.left - handle.left: 1006 + (331 - 976) = 361; the slide-in step 0x4a1814-0x4a1828 divides (arm.left - the handle's left) by the tick count, so a slide in undoes a slide out exactly
	public static int ArmRestRight => (int)ArmTable.Right + (int)ArmTable.X - (int)HandleTable.X;

	/// <summary>cdq / sub / sar 1 of the drawer: halves a 16-bit height, rounding toward zero.</summary>
	private static int Half( int value ) => (value - (value >> 31)) >> 1;

	private static int Word( int value ) => (short)value;

	/// <summary>The panel's left edge when the slide starts: the arm's left less the panel's width less 50 (0x4a1631).</summary>
	public static int StartLeft( int panelWidth ) => Word( ArmLeft - panelWidth - Gap );

	/// <summary>The panel's top edge for the whole slide: centred on the arm (the arm's top + half its height - half the panel's, 0x4a163b-0x4a1647).</summary>
	public static int Top( int panelHeight ) => Word( ArmTop + Half( ArmHeight ) - Half( panelHeight ) );

	/// <summary>
	/// The panel's move per tick: (the HUD window's right - the arm's left + the panel's width + 100) / 10, truncated toward zero (0x4a16d7-0x4a16f3, <c>idiv [esi + 0x130]</c>).
	/// </summary>
	public static int TickStep( int panelWidth ) => Word( HudRight - ArmLeft + panelWidth + Overshoot ) / Ticks;

	/// <summary>The panel's left edge after <paramref name="tick"/> ticks (0..10): each timer message moves the arm's right edge, and the resize message moves the panel by the same amount (0x4a19e7, 0x4a1a0e).</summary>
	public static int Left( int panelWidth, int tick ) => Word( StartLeft( panelWidth ) + tick * TickStep( panelWidth ) );

	/// <summary>Where the slide ends: the start plus (the tick count times the truncated step), so up to 9 units short of the full travel.</summary>
	public static int EndLeft( int panelWidth ) => Left( panelWidth, Ticks );

	/// <summary>The arm's right edge after <paramref name="tick"/> ticks of a slide that carries a panel of <paramref name="panelWidth"/> out.</summary>
	public static int ArmRight( int panelWidth, int tick ) => Word( ArmRestRight + tick * TickStep( panelWidth ) );

	/// <summary>
	/// The shift of a panel whose table rectangle starts at (<paramref name="tableLeft"/>, <paramref name="tableTop"/>) from its table place after <paramref name="tick"/> ticks:
	/// the drawer moves the whole panel, children included, by the difference of its new and old rectangle (0x4a165d, <c>0x65cf5e</c> on a rectangle of the same size moves it).
	/// </summary>
	public static (int X, int Y) Shift( UiRect table, int tick )
	{
		var width = (int)table.Width;
		var height = (int)table.Height;
		return (Left( width, tick ) - (int)table.X, Top( height ) - (int)table.Y);
	}
}
