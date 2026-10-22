using System.Linq;
using System.Reflection;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>A park click of the HUD's own path, queue and track line tool (<c>ParkHud.UpdateCellTool</c>), with keys held as a frame's input holds them.</summary>
internal static class HudToolClick
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

	/// <summary>Clicks <paramref name="cell"/> through <paramref name="hud"/>'s tool click while <paramref name="held"/> are down; the keyboard state is put back after it.</summary>
	internal static void Click( ParkHud hud, (int X, int Y) cell, params Veldrid.Key[] held )
	{
		var keyboard = Input.Keyboard;
		var down = Input.KeysDown;
		var last = Input.LastKeysDown;
		var text = Input.TextEntryActive;
		try
		{
			Input.Keyboard = new Input.KeyboardInfo( held.ToList() );
			Input.KeysDown = new();
			Input.LastKeysDown = new();
			Input.TextEntryActive = false;
			Input.UpdateButtons();
			typeof( ParkHud ).GetField( "cursorX", Private )!.SetValue( hud, cell.X );
			typeof( ParkHud ).GetField( "cursorY", Private )!.SetValue( hud, cell.Y );
			var press = new UiInput( new System.Numerics.Vector2( -1 ), true, true, false, false, UiKeys.None );
			typeof( ParkHud ).GetMethod( "UpdateCellTool", Private )!.Invoke( hud, new object[] { RideUpgradeIngressTests.Context(), press, true, false } );
		}
		finally
		{
			Input.Keyboard = keyboard;
			Input.UpdateButtons();
			Input.KeysDown = down;
			Input.LastKeysDown = last;
			Input.TextEntryActive = text;
		}
	}
}
