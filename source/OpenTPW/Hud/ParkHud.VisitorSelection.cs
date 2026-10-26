using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	/// <summary>A real park press opens the per-visitor window on the clicked live visitor.</summary>
	internal bool SelectWorldVisitor(Guest guest)
	{
		if (!guest.IsVisible || !OpenVisitorInfoWindow(guest))
			return false;
		// [BIN:TPW-EXE:0x00487c43 park visitor click] the park click handler 0x4879d0 (cursor mode 7) opens the visitor window (0x4b79b0, on the window object 0x7cc408) for the picked thing [0x7c2650], which the Ctrl+V locator then takes
		return true;
	}

	/// <summary>
	/// Whether a left press can pick a visitor: Shift alone with no locator panel out, or the visitor locator panel out with no modifier. In that state the park view picks visitors
	/// only; in every other state it never does, and a press on a visitor acts like one on the ground under it.
	/// </summary>
	// [BIN:TPW-EXE:0x487013 hover mode 7] the hover handler 0x486d90 builds the modifier mask (Shift 3, Ctrl 0xc, Alt 0x30; GetAsyncKeyState 0x10, 0x11, 0x12) and reads the locator mode 0x4b4480 ([0x7cc2f0], 0 none, 1 staff, 2 visitors):
	// only with (mode 2 and mask 0) or (mode 0 and mask 3) does a visitor under the cursor (a thing of type 1, the pick block's [0x7b05ac]) set cursor mode 7, which the click dispatcher 0x4879d0 turns into the visitor window 0x4b79b0;
	// in that state the handler returns without any object or cell mode, so nothing else is picked
	internal static bool IsVisitorPickState( LocatorMode locator, int modifiers ) =>
		(locator == LocatorMode.Visitor && modifiers == 0) || (locator == LocatorMode.None && modifiers == 3);

	/// <summary>Runs before automatic path activation; a miss leaves normal park tools alone.</summary>
	internal bool TrySelectWorldVisitor(UiInput input, NVector2 viewport, bool blocked)
	{
		if (blocked || !input.LeftPressed || Input.TextEntryActive || input.Text.Length != 0 || Stack.Screens.Count != 1 ||
			Camera.Mode is not ParkCameraMode || level.BuildEntry != null || level.IsPlacing || level.IsRemovingObjects ||
			level.CellTool.IsActive || level.IsCoasterToolActive || level.GuestRenderer == null || !IsVisitorPickState(Locator.Mode, Input.ModifierMaskDown))
			return false;
		var guest = level.PickVisitor(input.Mouse, viewport, Camera.ViewMatrix, Camera.ProjMatrix);
		return guest != null && SelectWorldVisitor(guest);
	}
}
