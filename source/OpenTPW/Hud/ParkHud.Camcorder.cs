using Veldrid;
using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

public partial class ParkHud
{
	public bool CamcorderArmed { get; private set; }
	private bool camcorderKeyDown = CamcorderKeyHeld();
	private bool camcorderMouseDown = Input.Mouse.Left;

	private static bool CamcorderKeyHeld() => Input.IsFrameKeyDown( Input.ChordKey( InputButton.CamcorderMode ) ) || Input.Down( InputButton.CamcorderMode );

	/// <summary>
	/// [EXT:park-entry-preparation] Takes the camcorder's held key and button from the current frame's input, as the constructor did: a HUD a park
	/// build made a few frames before entering (GameFlow.PrepareFirstFrame) reads them again in the entering frame, so a key or button held then
	/// is not taken for a new press, exactly as for a HUD made in that frame. The rest of what the constructor reads is the park's own state, which
	/// does not change before the park is entered.
	/// </summary>
	internal void RefreshEntryInput()
	{
		camcorderKeyDown = CamcorderKeyHeld();
		camcorderMouseDown = Input.Mouse.Left;
	}

	private bool CancelCamcorder()
	{
		if ( !CamcorderArmed && level.FreeWalk == null ) return false;
		CamcorderArmed = false;
		level.ExitFreeWalk();
		return true;
	}

	/// <summary>Arms the camcorder as the C key does (the camera panel's camcorder button); the next click in the park starts the walk.</summary>
	internal void ArmCamcorder()
	{
		var tools = level.BuildEntry != null || level.IsPlacing || level.IsRemovingObjects || level.CellTool.IsActive || level.IsCoasterToolActive;
		if ( CamcorderArmed || level.FreeWalk != null || tools || IsRiding || GameEditions.Current != GameEdition.ThemeParkWorld
			|| Camera.Mode is not ParkCameraMode || level.OriginalPark == null )
			return;
		CamcorderArmed = true;
		PostMessage( strings.Extra( OpenTpwText.CamcorderArmed ) );
		// [EXT:restored-advice-tutorial] 306 (the camcorder mode explained): the advisor posts it only while the extension is on, once (the tutorial group)
		host?.RestoredHelp?.Invoke( RestoredAdvice.CamcorderHelp );
	}

	private bool UpdateCamcorder( UiInput input, System.Numerics.Vector2 viewport, bool blocked )
	{
		var down = Input.IsFrameKeyDown( Input.ChordKey( InputButton.CamcorderMode ) ) || Input.Down( InputButton.CamcorderMode );
		var pressed = down && !camcorderKeyDown;
		camcorderKeyDown = down;
		var wasMouseDown = camcorderMouseDown;
		camcorderMouseDown = input.LeftDown;
		var owned = CamcorderArmed || level.FreeWalk != null;
		// [BIN:TPW-EXE:0x40C900 context key match] a key matches its context entry only with the entry's exact modifier mask: the camcorder's C has none, so Alt+C (the cheats window's key) is no camcorder press
		var control = Input.IsFrameKeyDown( Key.ControlLeft ) || Input.IsFrameKeyDown( Key.ControlRight ) || Input.Down( InputButton.ClosePark ) || (Input.ModifierMaskDown & 0x30) != 0;
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
