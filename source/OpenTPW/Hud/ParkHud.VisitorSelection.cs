using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	/// <summary>A real park press selects the clicked live visitor in the existing roster.</summary>
	internal bool SelectWorldVisitor(Guest guest)
	{
		RefreshVisitorRows();
		if (!guest.IsVisible || !visitorRows.Any(row => ReferenceEquals(row, guest)))
			return false;
		OpenVisitorWindow();
		SelectVisitor(VisitorRowIndex(guest.Id));
		GameAudio.PlayVisitorClick();
		toolPress = true; // Its release must not also select an object or finish a path click.
		return true;
	}

	/// <summary>Runs before automatic path activation; a miss leaves normal park tools alone.</summary>
	internal bool TrySelectWorldVisitor(UiInput input, NVector2 viewport, bool blocked)
	{
		if (blocked || !input.LeftPressed || Input.TextEntryActive || input.Text.Length != 0 || Stack.Screens.Count != 1 ||
			Camera.Mode is not ParkCameraMode || level.BuildEntry != null || level.IsPlacing || level.IsRemovingObjects ||
			level.CellTool.IsActive || level.IsCoasterToolActive || level.GuestRenderer == null)
			return false;
		var guest = level.PickVisitor(input.Mouse, viewport, Camera.ViewMatrix, Camera.ProjMatrix);
		return guest != null && SelectWorldVisitor(guest);
	}
}
