using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	private const string VisitChatScreen = "visitChat";
	/// <summary>The original chat overlay's message list and text entry (Mac UI table data:0x50b2c: 799019 at (543,1150)-(2005,1426), 799020 at (561,1441)-(1979,1486)).</summary>
	private static readonly UiRect VisitChatMessages = new( 543, 1150, 1462, 276 );
	private static readonly UiRect VisitChatEntry = new( 561, 1441, 1418, 45 );
	/// <summary>Lines of the park's room shown over the park.</summary>
	public const int VisitChatLines = 6;
	/// <summary>How long the room's lines stay over the park after the last one came, seconds (the chat line keeps them while it is open).</summary>
	public const float VisitChatSeconds = 10f;
	private static readonly Veldrid.RgbaByte VisitChatPlate = new( 16, 24, 48, 170 );
	private int visitChatCount = -1;
	private string? visitChatLast;
	private float visitChatAge = float.PositiveInfinity;

	/// <summary>
	/// The visit with other players whose chat line Enter opens now, or null: only the HUD is open, the pointer rests on no control (a control would take
	/// Enter itself), no text is typed, no modifier is held (Alt+Enter is the window's) and no ride view is up.
	/// </summary>
	private IVisitPresence? VisitChatLineWanted( UiContext context, UiInput input ) =>
		input.Has( UiKeys.Accept ) && !input.Has( UiKeys.Space ) && Stack.Screens.Count == 1 && !IsRiding && !level.HidesInterface && !Input.TextEntryActive
		&& Input.ModifierMaskDown == 0 && hud.HitTest( context.Canvas, input.Mouse ) == null && host?.Presence?.Invoke() is { CanChat: true } presence ? presence : null;

	/// <summary>
	/// The chat line of a visit with other players: a text entry at the original chat overlay's entry place; Enter says the line in the park's room and
	/// closes it, Escape closes it unsent.
	/// </summary>
	// [EXT:online-visit-chat-line] the original's in-park chat overlay (Mac table data:0x50b2c) with its people list and vote button is not reproduced in the visit;
	// Enter opens OpenTPW's single line at the overlay's entry (799020), and the room's last lines show at its message list (799019)
	private void OpenVisitChatLine( IVisitPresence presence )
	{
		var screen = new UiScreen( VisitChatScreen );
		var line = screen.Add( new UiTextField { Id = "line", MaximumLength = Online.Chat.ChatCommands.MaximumLineLength, Model = "f_text1",
			Bounds = VisitChatEntry, Anchor = UiAnchor.Bottom } );
		line.Submitted = () =>
		{
			if ( line.Text.Trim().Length > 0 )
				presence.Say( line.Text );
			CloseVisitChatLine();
		};
		screen.Back = CloseVisitChatLine;
		Stack.Push( screen );
		screen.Focus( line );
	}

	private void CloseVisitChatLine()
	{
		if ( Stack.Top?.Name == VisitChatScreen )
			Stack.Pop();
	}

	/// <summary>
	/// The park room's last lines at the original chat overlay's message list: while the chat line is open, and for <see cref="VisitChatSeconds"/> after a
	/// line came. The visit draws them here instead of the chat overlay's corner box, which the game flow hides in a visit with other players.
	/// </summary>
	private void DrawVisitChat( UiContext context )
	{
		if ( IsRiding || level.HidesInterface || host?.Presence?.Invoke() is not { } presence )
			return;
		var lines = presence.ChatLines;
		if ( lines.Count != visitChatCount || (lines.Count > 0 && !ReferenceEquals( lines[^1], visitChatLast )) )
		{
			// a line came (the list is capped, so its last entry tells a new one apart once it is full)
			if ( visitChatCount >= 0 && lines.Count > 0 )
				visitChatAge = 0f;
			visitChatCount = lines.Count;
			visitChatLast = lines.Count > 0 ? lines[^1] : null;
		}
		visitChatAge += context.Delta;
		var open = Stack.Screens.Any( screen => screen.Name == VisitChatScreen );
		if ( lines.Count == 0 || (!open && visitChatAge > VisitChatSeconds) )
			return;
		var area = context.Canvas.Map( VisitChatMessages, UiAnchor.Bottom );
		var text = string.Join( "\n", lines.Skip( Math.Max( 0, lines.Count - VisitChatLines ) ) );
		var (width, height) = context.Measure( context.Fonts.Small, text, (int)area.Width );
		var pad = 4 * context.Canvas.TextScale;
		var plate = new UiRect( area.X, area.Bottom - height - pad * 2, area.Width, height + pad * 2 );
		context.Batch.AddRectangle( plate, VisitChatPlate );
		context.DrawText( context.Fonts.Small, text, new UiRect( plate.X + pad, plate.Y + pad, plate.Width - pad * 2, height ), UiColors.Text, UiAlign.Left, wrap: true, shadow: false );
	}

	private static readonly Veldrid.RgbaByte VisitNamePlate = new( 16, 24, 48, 170 );
	/// <summary>The player's own name: the original islands' yellow, as the chat park view marks the player's own marker.</summary>
	private static readonly Veldrid.RgbaByte VisitOwnName = new( 252, 232, 7, 255 );

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
