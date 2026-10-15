using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>
/// The original's message system: pending messages stack as icons in the red tube left of the HUD panel; a left click on an icon opens that message in the
/// green panel that slides out of the HUD, a right click deletes it. A message stays in the tube until it is opened or deleted.
/// </summary>
public sealed partial class ParkHud
{
	/// <summary>How many messages the tube holds; the oldest goes when a sixth arrives.</summary>
	// [DATA:video 15:20, 51:40, 76:03] up to about five icons are stacked in the tube at once
	// [APPROX:UI-025] pending tube messages are not saved with the park; the capacity of five is the most the video shows, not a traced limit; the original's message queue and its overflow rule are not traced
	public const int MessageTubeCapacity = 5;

	// The tube sits left of the HUD panel above its top edge, in the BottomLeft-anchored authored coordinates of the main panel; read off the 720p frames (video 15:20 and 12:00).
	private static readonly UiRect TubeBase = Rect( 69, 850, 157, 985 );
	private const float TubeStep = 77f;
	private static readonly UiRect MessagePanelRect = Rect( 405, 1090, 1100, 1445 );
	private static readonly UiRect MessageTextRect = Rect( 640, 1118, 1085, 1425 );

	private static readonly string[] PanelButtonIds = { "messageDelete", "messageClose" };
	private List<string>? pendingMessages;
	private string? openedMessage;
	private float messageSlide;

	/// <summary>The messages waiting in the tube, oldest first (the lowest icon).</summary>
	public IReadOnlyList<string> PendingMessages => pendingMessages ?? (IReadOnlyList<string>)Array.Empty<string>();
	/// <summary>The message the slide-out panel shows, null when it is closed.</summary>
	public string? OpenedMessage => openedMessage;
	/// <summary>How far the panel has slid out of the HUD, 0 (hidden) to 1 (fully out).</summary>
	public float MessagePanelSlide => messageSlide;

	/// <summary>Adds a game message to the tube. It stays until it is opened or deleted.</summary>
	public void PostGameMessage( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
			return;
		pendingMessages ??= new List<string>();
		pendingMessages.Add( text );
		while ( pendingMessages.Count > MessageTubeCapacity )
			pendingMessages.RemoveAt( 0 );
	}

	/// <summary>A left click on an icon: the message leaves the tube and the panel shows it.</summary>
	public bool OpenPendingMessage( int index )
	{
		if ( pendingMessages == null || index < 0 || index >= pendingMessages.Count )
			return false;
		// the panel comes out where the info arm is, so opening a message puts the arm away: the opened message is always visible, and one read there is not lost
		if ( InfoArmOpen )
			SetInfoArm( false );
		openedMessage = pendingMessages[index];
		pendingMessages.RemoveAt( index );
		return true;
	}

	/// <summary>A right click on an icon: the message is deleted without being read.</summary>
	public bool DeletePendingMessage( int index )
	{
		if ( pendingMessages == null || index < 0 || index >= pendingMessages.Count )
			return false;
		pendingMessages.RemoveAt( index );
		return true;
	}

	/// <summary>Closes the panel (the red cross, or the bomb that deletes the opened message).</summary>
	public void CloseMessagePanel() => openedMessage = null;

	/// <summary>The tube's whole rectangle for <paramref name="count"/> messages: the base tube grown by one step per icon.</summary>
	internal static UiRect TubeRect( int count ) => new( TubeBase.X, TubeBase.Y - TubeStep * count, TubeBase.Width, TubeBase.Height + TubeStep * count );

	/// <summary>The rectangle of icon <paramref name="index"/> (0 = the lowest, oldest message) in the authored BottomLeft canvas.</summary>
	internal static UiRect TubeIcon( int index ) => UiRect.FromCenter( new NVector2( TubeBase.Center.X, TubeBase.Bottom - 55 - TubeStep * index ), 62, 62 );

	private void UpdateMessageTube( float delta )
	{
		var target = openedMessage != null && !InfoArmOpen ? 1f : 0f;
		messageSlide = target > messageSlide ? Math.Min( target, messageSlide + delta * 4f ) : Math.Max( target, messageSlide - delta * 4f );
		var fullyOut = messageSlide >= 1f;
		foreach ( var id in PanelButtonIds )
			if ( hud.Find( id ) is { } element )
				element.Visible = fullyOut;
		// an empty tube is hidden, so its rectangle swallows no park clicks
		if ( hud.Find( "messageTube" ) is { } tube )
		{
			tube.Visible = PendingMessages.Count > 0;
			tube.Bounds = TubeRect( PendingMessages.Count );
		}
	}

	/// <summary>The panel slides out of the HUD to the right, drawn under the main panel so it appears from behind it.</summary>
	private void DrawMessagePanel( UiContext context )
	{
		if ( openedMessage == null || messageSlide <= 0f )
			return;
		var canvas = context.Canvas;
		var offset = -(1f - messageSlide) * MessagePanelRect.Width;
		UiRect Slide( UiRect rect ) => canvas.Map( new UiRect( rect.X + offset, rect.Y, rect.Width, rect.Height ), UiAnchor.BottomLeft );
		// the green panel (panel model: the arm's art), then the black text box with the message
		context.DrawModel( "panel", 0, Slide( MessagePanelRect ) );
		context.Batch.AddRectangle( Slide( MessageTextRect ), new RgbaByte( 0, 0, 0, 235 ) );
		if ( messageSlide >= 1f )
			context.DrawText( context.Fonts.Small, openedMessage, Slide( MessageTextRect ).Inflate( -6 * canvas.TextScale ), UiColors.Text, UiAlign.Left, wrap: true );
	}

	/// <summary>The tube's icons: one list row per message; left opens, right deletes.</summary>
	private sealed class MessageTubeList( ParkHud owner ) : UiElement, IUiRowList
	{
		public override bool Focusable => Visible && Enabled && owner.PendingMessages.Count > 0;
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
				if ( canvas.Map( TubeIcon( index ), UiAnchor.BottomLeft ).Inflate( 4 * canvas.Scale ).Contains( point ) )
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
				var icon = canvas.Map( TubeIcon( index ), UiAnchor.BottomLeft );
				var hovered = index == HoveredRow;
				UiDisc.Draw( context.Batch, icon.Center, icon.Width / 2, hovered ? new RgbaByte( 255, 240, 120, 255 ) : new RgbaByte( 250, 250, 250, 255 ) );
				UiDisc.Draw( context.Batch, icon.Center + new NVector2( 0, icon.Height * 0.1f ), icon.Width * 0.18f, new RgbaByte( 60, 60, 70, 255 ) );
			}
		}
	}
}
