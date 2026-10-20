using OpenTPW.UI;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	/// <summary>The name of the chat mode's screen: the original chat overlay in place of the HUD, in a visit with other players.</summary>
	public const string ChatModeScreenName = "chatMode";
	private ChatPanel.ChatScreen? chatModeScreen;
	private ChatPanel? chatPanel;
	/// <summary>Whether the player keeps the chat mode (a visit starts in it; F4 switches to the HUD and back).</summary>
	private bool chatModeWanted = true;
	private bool chatModeKeyDown;

	/// <summary>
	/// The visit shows the original chat overlay in place of the HUD: a visit of a published park with other players starts in it, as the original's
	/// visit switched to chat mode on entering the park (manual p. 65), and F4 switches between it and the HUD.
	/// </summary>
	// [BIN:TPW-EXE:0x4862A0 chat mode] in chat mode the HUD (0x7CB2AC) does not exist: the overlay (0x7CC8BC) or a bare full-screen panel (0x7CC8C0, 0x65DD7B) stands over the park, and F4 (key 0x73 in the entry's handler 0x4BE3A0, flag 0x7CC8B4) switches between them
	// [EXT:online-visit-chat-mode] F4's other side is OpenTPW's read-only HUD of the visit (its windows, the camera and the ride view), where the original's was the bare park; Enter over the park returns to the chat mode with the entry focused
	public bool ChatMode => chatModeScreen != null && Stack.Screens.Count > 0 && ReferenceEquals( Stack.Screens[0], chatModeScreen );

	/// <summary>The chat overlay of the visit, once it had other players.</summary>
	internal ChatPanel? VisitChatPanel => chatPanel;
	/// <summary>The chat overlay's entry takes the keys.</summary>
	internal bool ChatEntryFocused => ChatMode && chatPanel != null && ReferenceEquals( chatModeScreen!.Focused, chatPanel.Entry );

	/// <summary>The other players of a read-only visit, while there are any.</summary>
	private IVisitPresence? VisitPresence => level.IsReadOnlyVisit ? host?.Presence?.Invoke() : null;

	/// <summary>
	/// The ride a click in the chat mode rides (tp.exe 0x4BD7F4): a placed object that is no track add-on (AddOn.UpgradesId 0), of the rides' or the features'
	/// interface type (Info.WhichUIType 0 or 3), whose ride view opens (<see cref="RideCameraMode.CanRide"/>); null for anything else, where the click walks.
	/// </summary>
	// [BIN:TPW-EXE:0x4BD7F4 chat mode ride] over a thing of type 3 whose definition has AddOn.UpgradesId (+0x284) 0 and Info.WhichUIType (+0x4ac) 0, or 3 with the thing's choosable bit (+0x32 bit 0x10), the chat mode panel shows help 580 with the ride's name, and a click runs the ride window's Ride it! (0x4E15B0)
	// [EXT:online-chat-ride-click] the ride view opens only for a ride with a camera seat or a running coaster (RideCameraMode.CanRide), and a feature's choosable bit is not tracked: a feature rides when its view opens
	internal static OriginalObject? ChatModeRide( OriginalObject? thing )
	{
		if ( thing is not { IsDeleted: false } || thing.Entry.Settings.GetInt( "AddOn.UpgradesId" ) != 0 )
			return null;
		// -1 is a missing key (GetInt's own fallback is 0, which is a ride): only the rides' and the features' interface types ride
		var type = thing.Entry.Settings.GetInt( "Info.WhichUIType", -1 );
		return type is 0 or 3 && RideCameraMode.CanRide( thing ) ? thing : null;
	}

	/// <summary>F4: switches between the chat mode and the HUD (with nothing open over them).</summary>
	internal void ToggleChatMode()
	{
		chatModeWanted = !chatModeWanted;
		SyncChatMode();
	}

	/// <summary>F4 while nothing but the chat mode or the HUD is open; Alt+F4 is the window's.</summary>
	private void HandleChatModeKey()
	{
		var down = Input.IsFrameKeyDown( Key.F4 );
		if ( down && !chatModeKeyDown && (Input.ModifierMaskDown & 0x30) == 0 && VisitPresence != null && Stack.Screens.Count == 1 )
			ToggleChatMode();
		chatModeKeyDown = down;
	}

	/// <summary>
	/// Puts the chat mode or the HUD at the bottom of the stack, with nothing open over it: the chat mode once the visit is in its park's room (and on
	/// through a reconnection) while the player keeps it and no ride view is up; the HUD otherwise, and when the server refused the park's room.
	/// </summary>
	private void SyncChatMode()
	{
		if ( Stack.Screens.Count != 1 )
			return;
		var presence = VisitPresence;
		var wanted = presence != null && chatModeWanted && !IsRiding && !presence.Refused && (ChatMode || presence.CanChat);
		if ( wanted == ChatMode )
			return;
		if ( wanted )
		{
			// the HUD's windows, panels and modes go with it
			SelectObject( null );
			CloseArmPanel();
			CloseCameraPanel();
			CancelCamcorder();
			chatModeScreen ??= CreateChatModeScreen();
			Stack.Replace( chatModeScreen );
		}
		else
		{
			chatPanel?.ClosePopup();
			Stack.Replace( hud );
		}
	}

	/// <summary>
	/// Whether Enter now goes to the chat overlay's entry: a visit with other players, nothing but the base screen open, no text field taking the keys
	/// (the entry itself says its line), the pointer on no control (a control takes Enter itself), no modifier held (Alt+Enter is the window's) and no
	/// ride view.
	/// </summary>
	private bool ChatEntryWanted( UiContext context, UiInput input ) =>
		input.Has( UiKeys.Accept ) && !input.Has( UiKeys.Space ) && Stack.Screens.Count == 1 && !IsRiding && !level.HidesInterface && !Input.TextEntryActive
		&& Stack.Screens[0].Focused is not UiTextField { Visible: true, Enabled: true }
		&& Input.ModifierMaskDown == 0 && Stack.Screens[0].HitTest( context.Canvas, input.Mouse ) == null && VisitPresence is { CanChat: true };

	/// <summary>Enter over the park: the chat mode, with the entry taking the keys.</summary>
	private void FocusChatEntry()
	{
		chatModeWanted = true;
		SyncChatMode();
		if ( ChatMode && chatPanel != null )
			chatModeScreen!.Focus( chatPanel.Entry );
	}

	/// <summary>
	/// The chat mode's screen: the original chat overlay (<see cref="ChatPanel"/>) over the park, which takes the pointer only on its controls; Escape opens
	/// the pause menu.
	/// </summary>
	private ChatPanel.ChatScreen CreateChatModeScreen()
	{
		chatPanel ??= new ChatPanel( strings, Stack, () => VisitPresence?.Chat )
		{
			Postcard = host?.OpenPostcard is { } open ? () => open( Stack ) : null,
			GoTo = name => VisitPresence?.GoTo( name ),
		};
		var screen = new ChatPanel.ChatScreen( ChatModeScreenName, chatPanel ) { Modal = false, Back = OpenPauseMenu };
		chatPanel.AddTo( screen );
		return screen;
	}

	private static readonly RgbaByte VisitNamePlate = new( 16, 24, 48, 170 );
	/// <summary>The player's own name: the original islands' yellow, as the chat park view marks the player's own marker.</summary>
	private static readonly RgbaByte VisitOwnName = new( 252, 232, 7, 255 );

	/// <summary>
	/// The players' names over their figures in a visit (<see cref="IVisitPresence.Names"/>), on a dark plate, the player's own in yellow. Drawn before the
	/// HUD's screens, so its windows and menus cover them; not in the ride view.
	/// </summary>
	// [EXT:online-avatar-labels] the original's online persons carry no name the decoded code draws (the chat overlay's people list names them)
	private void DrawVisitNames( UiContext context )
	{
		if ( IsRiding || level.HidesInterface || host?.Presence?.Invoke() is not { } presence )
			return;
		var view = Camera.ViewMatrix;
		var up = new System.Numerics.Vector3( view.M12, view.M22, view.M32 );
		var size = new Point2( (int)context.Canvas.Width, (int)context.Canvas.Height );
		var pad = 2 * context.Canvas.TextScale;
		foreach ( var name in presence.Names )
		{
			if ( !ScreenMapping.TryProjectToPixel( name.Feet + up * ParkPresence.NameHeight, view, Camera.ProjMatrix, size, out var pixel )
				|| pixel.X < 0 || pixel.Y < 0 || pixel.X > size.X || pixel.Y > size.Y )
				continue;
			var (width, height) = context.Measure( context.Fonts.Small, name.Name );
			var plate = new UiRect( pixel.X - width / 2f - pad, pixel.Y - height - pad * 2, width + pad * 2, height + pad * 2 );
			context.Batch.AddRectangle( plate, VisitNamePlate );
			context.DrawText( context.Fonts.Small, name.Name, new UiRect( plate.X + pad, plate.Y + pad, width, height ), name.Own ? VisitOwnName : UiColors.Text, UiAlign.Left, shadow: false );
		}
	}
}
