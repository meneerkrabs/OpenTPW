using Veldrid;
using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

public partial class ParkHud
{
	public bool CamcorderArmed { get; private set; }
	private bool camcorderKeyDown = Input.IsFrameKeyDown( Input.ChordKey( InputButton.CamcorderMode ) ) || Input.Down( InputButton.CamcorderMode );
	private bool camcorderMouseDown = Input.Mouse.Left;

	private bool CancelCamcorder()
	{
		if ( !CamcorderArmed && level.FreeWalk == null ) return false;
		CamcorderArmed = false;
		level.ExitFreeWalk();
		return true;
	}

	private bool UpdateCamcorder( UiInput input, System.Numerics.Vector2 viewport, bool blocked )
	{
		var down = Input.IsFrameKeyDown( Input.ChordKey( InputButton.CamcorderMode ) ) || Input.Down( InputButton.CamcorderMode );
		var pressed = down && !camcorderKeyDown;
		camcorderKeyDown = down;
		var wasMouseDown = camcorderMouseDown;
		camcorderMouseDown = input.LeftDown;
		var owned = CamcorderArmed || level.FreeWalk != null;
		var control = Input.IsFrameKeyDown( Key.ControlLeft ) || Input.IsFrameKeyDown( Key.ControlRight ) || Input.Down( InputButton.ClosePark );
		var tools = level.BuildEntry != null || level.IsPlacing || level.IsRemovingObjects || level.CellTool.IsActive || level.IsCoasterToolActive;
		if ( pressed && !control && !blocked && !tools && !IsRiding && GameEditions.Current == GameEdition.ThemeParkWorld )
		{
			if ( owned ) CancelCamcorder();
			else if ( Camera.Mode is ParkCameraMode && level.OriginalPark != null )
			{
				CamcorderArmed = true;
				PostMessage( strings.Extra( OpenTpwText.CamcorderArmed ) );
			}
		}
		owned |= CamcorderArmed || level.FreeWalk != null;
		if ( CamcorderArmed && !control && !blocked && !tools && !IsRiding && !wasMouseDown && input.LeftPressed )
		{
			if ( level.TryEnterFreeWalk( input.Mouse, viewport ) ) CamcorderArmed = false;
		}
		if ( owned && (input.LeftDown || input.LeftPressed) ) toolPress = true;
		return owned;
	}
}
