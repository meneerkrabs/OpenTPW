using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>
/// The original's message system: pending messages stack as icons in the red tube left of the HUD panel; a left click on an icon opens that message in the
/// green panel that slides out of the HUD, a right click deletes it. Generic messages leave the tube when opened; tagged challenge progress and terminal rows remain when read or closed.
/// </summary>
public sealed partial class ParkHud
{
	/// <summary>Ten occupied slots, including hidden terminals awaiting final release; a full tube refuses new rows.</summary>
	// [BIN:TPW-EXE:0x48ec04,0x750610,0x48ec40] admission checks the configured count of ten and scans ten reusable records; there is no oldest eviction.
	// [BIN:TPW-EXE:0x4a1278,0x65f447] attaching a reused slot appends it after surviving rows, represented here by list order.
	// [EXT:message-tube-art] the red tube body is drawn as rectangles and the white icons as discs, the growth per message (77 units) read off the video, instead of the original's tube art
	// [BIN:TPW-EXE:0x48EE50 tube saved] the tube is saved with the park (ParkHud.MessageTubeSave.cs; the original's tube saver writes the occupied slots with their tag, text, lifetime and context words)
	// [BIN:TPW-EXE:0x48db94 offer removal] the tag window's removal (message 5, sent 500 ms after the removal 0x100002 hides it, 0x4a13e8) runs the action 0x1B first when the tag's second action is 0x1B (0x48db8b-0x48dbb0 -> 0x48e0f0 -> 0x48e269 -> the decline 0x4d2ec0), so an offer that is deleted, expires or is answered leaves a declined (or already answered) challenge
	// [BIN:TPW-EXE:0x48ede0 offer post] the post wrapper opens a tag at once only when the record's byte +0x24 is 1 (0x48edeb); the tag system's record builder 0x509bf0 clears that byte on every path, so an offer is added like any message (0x48ee09) and never opens by itself
	// [APPROX:UI-025] the original's own tag save record is not imported or written, and a generic message still leaves the tube when it is opened (the original keeps every tag until it is deleted, answered or expired)
	public const int MessageTubeCapacity = 10;

	/// <summary>The advisor help the open message panel carries (0x11C, 284).</summary>
	public const int MessagePanelHelp = 284;

	/// <summary>The <c>cat_ui</c> sound a new tube icon plays (0x1E).</summary>
	// [BIN:TPW-EXE:0x48edb4 message posted] the tube's add routine 0x48eb20 plays cat_ui 0x1E flat (0x51bfc0 with no place) when it creates a new icon (after bumping the icon count 0x7ca09c)
	public const uint MessagePostedEvent = 0x1E;

	/// <summary>The <c>cat_ui</c> sound a left click on a tube icon plays (0xC0).</summary>
	// [BIN:TPW-EXE:0x48e043 tube icon click] the icon's procedure plays cat_ui 0xC0 (the helper 0x485aa0, flat) on a button-0 click, before it opens the message (0x48d9c0) or closes the open one (0x4a25f0)
	public const uint MessageClickedEvent = 0xC0;

	// The tube sits left of the HUD panel above its top edge, in the BottomLeft-anchored authored coordinates of the main panel; read off the 720p frames (video 15:20 and 12:00).
	private static readonly UiRect TubeBase = Rect( 69, 850, 157, 985 );
	private const float TubeStep = 77f;
	// [BIN:TPW-EXE:0x7501a0 message panel] the panel (458,1101)-(1073,1442) is drawn from the tag models messagel (458..623), messagem (623..835) and messager (835..1073); its text area is (609,1118)-(1056,1422); mb_erase sits at (486,1325)-(589,1428) and two action buttons above it, (486,1218)-(589,1320) and (486,1110)-(589,1213), whose models are not fixed by the table
	private static readonly LayoutTable MessageTable = OriginalLayoutTables.MessagePanel;
	private static readonly UiRect MessagePanelRect = MessageTable.Root.Rect;
	private static readonly UiRect MessageTextRect = MessageTable.Control( 0x10D32 ).Inner!.Value;
	private static readonly string[] MessageActionIds = { "messageAction0", "messageAction1" };

	private List<string>? pendingMessages;
	private List<GameMessageAction[]>? pendingActions;
	private List<ChallengeTubeRow?>? pendingChallengeRows;
	private ChallengeTubeRow? openedChallengeRow;
	internal readonly record struct ChallengeMessageIdentity( int Tag, short ContextShort, int ContextInt );
	private string? openedMessage;
	private GameMessageAction[] openedActions = Array.Empty<GameMessageAction>();
	private string? closingMessage;
	private GameMessageAction[] closingActions = Array.Empty<GameMessageAction>();
	private ArmSlide? messageSlide;

	/// <summary>The message panel's slide in the HUD arm (the arm drawer 0x4a14f0, shared with the camera panel's).</summary>
	internal ArmSlide MessageSlide => messageSlide ??= new();

	/// <summary>The occupied tube slots, oldest first (the lowest icon), including hidden terminals awaiting final release.</summary>
	public IReadOnlyList<string> PendingMessages => pendingMessages ?? (IReadOnlyList<string>)Array.Empty<string>();
	/// <summary>The message the slide-out panel shows, null when it is closed.</summary>
	public string? OpenedMessage => openedMessage;
	/// <summary>How many of the drawer's ten timer ticks the panel has slid out of the HUD, 0 (hidden) to <see cref="ArmSlide.Steps"/> (fully out).</summary>
	public int MessagePanelStep => MessageSlide.Step;

	/// <summary>The actions of the opened message, in the order of the panel's two action slots.</summary>
	public IReadOnlyList<GameMessageAction> OpenedActions => openedActions;

	/// <summary>
	/// Adds a game message to the tube. It stays until it is opened or deleted. A message may carry up to two actions, drawn as the panel's action buttons with
	/// the art of their kind. Returns false for blank text or a full tube, without changing rows, sound or help.
	/// </summary>
	public bool PostGameMessage( string text, params GameMessageAction[] actions )
	{
		if ( string.IsNullOrWhiteSpace( text ) || PendingMessages.Count >= MessageTubeCapacity )
			return false;
		pendingMessages ??= new List<string>();
		pendingActions ??= new List<GameMessageAction[]>();
		pendingChallengeRows ??= new List<ChallengeTubeRow?>();
		pendingMessages.Add( text );
		pendingActions.Add( actions.Take( MessageActionIds.Length ).ToArray() );
		pendingChallengeRows.Add( null );
		GameAudio.PlayUi( MessagePostedEvent );
		// [EXT:restored-advice-tutorial] 322 (a message icon sits on the control panel antenna): the first message of the tube; the advisor posts it only while the extension is on, once (the tutorial group)
		if ( pendingMessages.Count == 1 )
			host?.RestoredHelp?.Invoke( RestoredAdvice.MessageIconHelp );
		return true;
	}

	/// <summary>
	/// Refreshes every matching progress row, including the selected panel, without changing its order, actions or posted sound.
	/// New progress refuses a full tube; its first insertion follows the existing new-message sound/help path.
	/// </summary>
	internal bool PostChallengeProgress( int tag, string text, short contextShort = 0, int contextInt = 0 )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
			return false;
		text = text.Length <= 255 ? text : text[..255];
		var identity = new ChallengeMessageIdentity( tag, contextShort, contextInt );
		var refreshed = false;
		for ( var index = 0; index < (pendingMessages?.Count ?? 0); index++ )
			if ( pendingChallengeRows![index] is { Visible: true } row && row.Identity == identity )
			{
				pendingMessages![index] = text;
				refreshed = true;
			}
		if ( refreshed )
		{
			if ( openedChallengeRow?.Identity == identity )
				openedMessage = text;
			return true;
		}
		if ( !PostGameMessage( text ) )
			return false;
		pendingChallengeRows![^1] = new ChallengeTubeRow( identity );
		return true;
	}

	/// <summary>Applies only the incoming record's exact replacement identity, never a guessed completed-definition identity.</summary>
	internal void RemoveChallengeProgress( int tag, short contextShort = 0, int contextInt = 0 )
	{
		var identity = new ChallengeMessageIdentity( tag, contextShort, contextInt );
		for ( var index = (pendingMessages?.Count ?? 0) - 1; index >= 0; index-- )
			if ( pendingChallengeRows![index] is { Visible: true } row && row.Identity == identity )
				RemovePendingMessageAt( index );
	}

	private void RemovePendingMessageAt( int index )
	{
		if ( pendingChallengeRows![index] is { } row && ReferenceEquals( openedChallengeRow, row ) )
			CloseMessagePanel();
		if ( pendingChallengeRows[index] is { IsOffer: true } )
			DeclineRemovedOffer();
		pendingMessages!.RemoveAt( index );
		pendingActions!.RemoveAt( index );
		pendingChallengeRows.RemoveAt( index );
	}

	/// <summary>A left click selects the message. A challenge row remains; reading a terminal cancels its timers permanently.</summary>
	public bool OpenPendingMessage( int index )
	{
		if ( !IsPendingMessageVisible( index ) )
			return false;
		GameAudio.PlayUi( MessageClickedEvent );
		var row = pendingChallengeRows![index];
		if ( row != null && ReferenceEquals( openedChallengeRow, row ) )
		{
			CloseMessagePanel();
			return true;
		}
		// the panel comes out where the info arm is, so opening a message puts the arm away: the opened message is always visible, and one read there is not lost
		if ( InfoArmOpen )
			SetInfoArm( false );
		// the message panel and the camera panel share one slot: opening a message puts the camera panel away
		CloseCameraPanel();
		openedMessage = pendingMessages![index];
		openedActions = pendingActions![index];
		openedChallengeRow = row;
		row?.Read();
		if ( row == null )
			RemovePendingMessageAt( index );
		// [BIN:TPW-EXE:0x48D9C0 message selected] the tube item's left click (0x48E03B: sound 0xC0, then 0x48D9C0 unless it is the open one) stores the item as the panel's message and posts help 0x11C (284) through 0x486B00
		host?.HelpPosted?.Invoke( MessagePanelHelp );
		return true;
	}

	/// <summary>A right click on an icon: the message is deleted without being read.</summary>
	public bool DeletePendingMessage( int index )
	{
		if ( !IsPendingMessageVisible( index ) )
			return false;
		if ( pendingChallengeRows![index] is { Lifetime: > 0 } row )
			BeginChallengeTerminalRemoval( row, closePanel: true );
		else
			RemovePendingMessageAt( index );
		return true;
	}

	/// <summary>The bomb deletes the selected instance; closing alone keeps it in the tube.</summary>
	public void DeleteOpenedMessage()
	{
		if ( openedChallengeRow is { } row && pendingChallengeRows?.IndexOf( row ) is int index && index >= 0 )
			DeletePendingMessage( index );
		CloseMessagePanel();
	}

	/// <summary>Closes the panel, retaining the selected challenge row without restarting its timers.</summary>
	public void CloseMessagePanel()
	{
		// [BIN:TPW-EXE:0x48E4BF message panel close] the panel's close message 0x1001D8 (sent when the arm drawer puts it away, 0x4A25F0 with 0) withdraws help 0x11C through 0x486B40 (0x48E4D3) and clears the selected item
		if ( openedMessage != null )
		{
			host?.HelpWithdrawn?.Invoke( MessagePanelHelp );
			// the panel keeps its look while the drawer slides it back in (the close message only clears the selected item)
			closingMessage = openedMessage;
			closingActions = openedActions;
		}
		openedMessage = null;
		openedChallengeRow = null;
		openedActions = Array.Empty<GameMessageAction>();
	}

	/// <summary>The tube's whole rectangle for <paramref name="count"/> messages: the base tube grown by one step per icon.</summary>
	internal static UiRect TubeRect( int count ) => new( TubeBase.X, TubeBase.Y - TubeStep * count, TubeBase.Width, TubeBase.Height + TubeStep * count );

	/// <summary>The rectangle of icon <paramref name="index"/> (0 = the lowest, oldest message) in the authored BottomLeft canvas.</summary>
	internal static UiRect TubeIcon( int index ) => UiRect.FromCenter( new NVector2( TubeBase.Center.X, TubeBase.Bottom - 55 - TubeStep * index ), 62, 62 );

	private void UpdateMessageTube( float delta )
	{
		KeepChallengeOfferPosted( level?.Park?.Economy );
		// [BIN:TPW-EXE:0x48dab3 message panel open] the tube puts the panel into the arm drawer (0x4a2590, so it slides out as the camera panel does: ten 20 ms ticks) and takes it out again with 0x4a25f0
		if ( openedMessage != null && !InfoArmOpen )
			MessageSlide.Open();
		else
			MessageSlide.Close();
		MessageSlide.Update( delta );
		if ( !MessageSlide.Out && MessageSlide.Step == 0 )
			closingMessage = null;
		var (shiftX, shiftY) = MessagePanelShift( MessageSlide.Step );
		var shown = openedMessage ?? closingMessage;
		var interactive = openedMessage != null && !InfoArmOpen;
		// [BIN:TPW-EXE:0x4a1725 arm retract] the arm's retract button (child 0x24) is shown by the slide out (0x4a171a-0x4a1734) and hidden again by the start of the slide in (0x4a1859-0x4a186c), whichever panel the arm carries
		if ( hud.Find( "messageClose" ) is { } retract )
			retract.Visible = MessageSlide.Out || CameraSlide.Out;
		if ( hud.Find( "messageDelete" ) is { } erase )
		{
			var table = MessageTable.Model( "mb_erase" ).Rect;
			erase.Bounds = table with { X = table.X + shiftX, Y = table.Y + shiftY };
			erase.Visible = shown != null && ArmChildShown( erase.Bounds );
			erase.Enabled = interactive;
		}
		for ( var slot = 0; slot < MessageActionIds.Length; slot++ )
		{
			if ( hud.Find( MessageActionIds[slot] ) is not UiButton button )
				continue;
			var table = MessageTable.Control( slot == 0 ? 0x10D2Fu : 0x10D30u ).Rect;
			button.Bounds = table with { X = table.X + shiftX, Y = table.Y + shiftY };
			var actions = openedMessage != null ? openedActions : closingActions;
			button.Visible = shown != null && ArmChildShown( button.Bounds ) && slot < actions.Length;
			button.Enabled = interactive;
			if ( button.Visible )
				button.Model = actions[slot].Kind.Model();
		}
		// an empty tube is hidden, so its rectangle swallows no park clicks
		if ( hud.Find( "messageTube" ) is { } tube )
		{
			tube.Visible = PendingMessages.Count > 0;
			tube.Bounds = TubeRect( PendingMessages.Count );
		}
	}

	/// <summary>The message panel's shift from its table place after <paramref name="step"/> drawer ticks: it ends at left 486, top 1112 (the panel is 615 wide and 341 high).</summary>
	// [BIN:TPW-EXE:0x4a1631 message panel place] the arm drawer places every panel it carries the same way (ArmDrawer): the message panel (458,1101)-(1073,1442) starts at 331 - 615 - 50 = -334, steps by (439 - 331 + 615 + 100) / 10 = 82 and ends at 486, centred on the arm at top 1069 + 213 - 170 = 1112; a Dutch gameplay video shows the panel's left edge at 486 and its top at 1112
	internal static (int X, int Y) MessagePanelShift( int step ) => ArmDrawer.Shift( MessagePanelRect, step );

	/// <summary>The panel slides out of the HUD to the right, drawn under the main panel so it appears from behind it.</summary>
	private void DrawMessagePanel( UiContext context )
	{
		var shown = openedMessage ?? closingMessage;
		if ( shown == null || MessageSlide.Step <= 0 )
			return;
		var canvas = context.Canvas;
		var (shiftX, shiftY) = MessagePanelShift( MessageSlide.Step );
		UiRect Slide( UiRect rect ) => canvas.Map( new UiRect( rect.X + shiftX, rect.Y + shiftY, rect.Width, rect.Height ), UiAnchor.BottomLeft );
		// the arm's own art under the panel (the green bar, its pane and the blue handle at its right end), as for the camera panel
		var (bar, pane, handle) = ArmPieces( (int)MessagePanelRect.Width, MessageSlide.Step );
		context.DrawModel( "panel", 0, canvas.Map( bar, UiAnchor.BottomLeft ) );
		context.DrawModel( ArmPaneModel, 0, canvas.Map( pane, UiAnchor.BottomLeft ) );
		context.DrawModel( ArmHandleModel, 0, canvas.Map( handle, UiAnchor.BottomLeft ) );
		// the tag models side by side: the left part with the buttons, the middle and the right part with the black text box; each child shows once its right edge passes the arm's left edge (0x4a1440)
		foreach ( var part in MessageTable.Root.Children.Where( node => node.Model?.StartsWith( "message", StringComparison.Ordinal ) == true ) )
			if ( ArmChildShown( part.Rect with { X = part.Rect.X + shiftX } ) )
				context.DrawModel( part.Model!, 0, Slide( part.Rect ) );
		if ( ArmChildShown( MessageTable.Control( 0x10D32 ).Rect with { X = MessageTable.Control( 0x10D32 ).Rect.X + shiftX } ) )
			context.DrawText( context.Fonts.Small, shown, Slide( MessageTextRect ).Inflate( -6 * canvas.TextScale ), UiColors.Text, UiAlign.Left, wrap: true );
	}

	/// <summary>The tube's icons: one list row per message; left opens, right deletes.</summary>
	private sealed class MessageTubeList( ParkHud owner ) : UiElement, IUiRowList
	{
		public override bool Focusable => Visible && Enabled && owner.pendingChallengeRows?.Any( row => row?.Visible != false ) == true;
		public int Selected => -1;
		public bool ArrowsSelect => false;
		public int HoveredRow { get; private set; } = -1;
		public void Hover( int index ) => HoveredRow = index;
		public bool HoverSelects => true;
		public void Select( int index ) { }
		public void Scroll( int rows ) { }

		public int RowAt( UiCanvas canvas, NVector2 point )
		{
			for ( var index = 0; index < owner.PendingMessages.Count; index++ )
				if ( owner.IsPendingMessageVisible( index ) && canvas.Map( TubeIcon( index ), UiAnchor.BottomLeft ).Inflate( 4 * canvas.Scale ).Contains( point ) )
					return index;
			return -1;
		}

		// [DATA:video 15:20 hover help] "Bericht - klik links om te openen, klik rechts om te verwijderen": a left click opens the message, a right click deletes it
		public bool RowClicked( int index, bool left ) => left ? owner.OpenPendingMessage( index ) : owner.DeletePendingMessage( index );

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var count = owner.PendingMessages.Count;
			if ( count == 0 )
				return;
			var canvas = context.Canvas;
			// The tube grows upwards by one step per message over its base (the base is part of the HUD art).
			var bodyTop = TubeBase.Y - TubeStep * count;
			var body = canvas.Map( new UiRect( TubeBase.X + 14, bodyTop, TubeBase.Width - 28, TubeStep * count + TubeBase.Height - 12 ), UiAnchor.BottomLeft );
			context.Batch.AddRectangle( body.Inflate( 2 ), new RgbaByte( 90, 10, 10, 255 ) );
			context.Batch.AddRectangle( body, new RgbaByte( 215, 30, 30, 255 ) );
			context.Batch.AddRectangle( new UiRect( body.X + body.Width * 0.18f, body.Y, body.Width * 0.18f, body.Height ), new RgbaByte( 255, 120, 110, 255 ) );
			for ( var index = 0; index < count; index++ )
			{
				if ( !owner.IsPendingMessageVisible( index ) )
					continue;
				var icon = canvas.Map( TubeIcon( index ), UiAnchor.BottomLeft );
				var hovered = index == HoveredRow;
				UiDisc.Draw( context.Batch, icon.Center, icon.Width / 2, hovered ? new RgbaByte( 255, 240, 120, 255 ) : new RgbaByte( 250, 250, 250, 255 ) );
				UiDisc.Draw( context.Batch, icon.Center + new NVector2( 0, icon.Height * 0.1f ), icon.Width * 0.18f, new RgbaByte( 60, 60, 70, 255 ) );
			}
		}
	}
}

/// <summary>What a message's action button does; each kind has its own art among the ui.wad mb_ models.</summary>
// [DATA:ui.wad mb_locate, mb_callmech, mb_resrch, mb_buy, mb_accept, mb_decline] the six models the panel's action buttons can take (the table gives those buttons no fixed model)
public enum GameMessageKind { Locate, CallMechanic, Research, Buy, Accept, Decline }

/// <summary>One action of a tube message: the kind picks the button's art, <paramref name="Clicked"/> runs when it is pressed.</summary>
public readonly record struct GameMessageAction( GameMessageKind Kind, Action Clicked );

public static class GameMessageKinds
{
	/// <summary>The ui.wad model of the action button of this kind.</summary>
	public static string Model( this GameMessageKind kind ) => kind switch
	{
		GameMessageKind.Locate => "mb_locate",
		GameMessageKind.CallMechanic => "mb_callmech",
		GameMessageKind.Research => "mb_resrch",
		GameMessageKind.Buy => "mb_buy",
		GameMessageKind.Accept => "mb_accept",
		GameMessageKind.Decline => "mb_decline",
		_ => throw new ArgumentOutOfRangeException( nameof( kind ) )
	};
}
