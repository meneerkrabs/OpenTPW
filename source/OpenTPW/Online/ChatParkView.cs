using OpenTPW.UI.Original;

namespace OpenTPW.UI;

/// <summary>
/// A chat room seen from above: every player in the room is a marker at the position the server relayed, and a
/// click walks the player's own marker there (manual p. 68: "Left click to walk to destination"). A satellite's
/// chat park is visited in three dimensions like any park (OnlineScreens.EnterChatPark); this view shows a room
/// entered without a park to visit: on an older server, or a published park's chat from its details.
/// </summary>
// [EXT:online-chat-park-view] a flat overhead view with a marker per player, walked by clicking at one speed, shows a room without a visited park
// (the original's chat rooms were always parks: a satellite's park was visited, tp.exe 0x4C0880; the double-click teleport is not separated)
public sealed class UiChatParkView : UiElement, IUiDragTarget
{
	private readonly OnlineSession session;
	private (float X, float Y)? target;
	private (float X, float Y) lastSent;
	private float sinceSent;
	/// <summary>How fast the player's marker walks across the park, in park widths per second.</summary>
	public const float WalkSpeed = 0.35f;
	/// <summary>Shortest gap between two positions sent to the server, in seconds.</summary>
	public const float SendInterval = 0.12f;

	public UiChatParkView( OnlineSession session ) => this.session = session;

	public override bool Focusable => true;

	/// <summary>The click that walks: the place in the park under the pointer becomes the walk target.</summary>
	void IUiDragTarget.DragTo( UiCanvas canvas, System.Numerics.Vector2 point )
	{
		var rect = ScreenRect( canvas );
		if ( !Enabled || rect.Width <= 0 || rect.Height <= 0 || !session.IsLoggedIn )
			return;
		var x = Math.Clamp( (point.X - rect.X) / rect.Width, 0f, 1f );
		var y = Math.Clamp( (point.Y - rect.Y) / rect.Height, 0f, 1f );
		target = (x, y);
	}

	/// <summary>Walks the player's marker toward the click and sends the new position; call every frame.</summary>
	public void Walk( float delta )
	{
		if ( session.Settings.PlayerName is not { } name || !session.IsLoggedIn )
			return;
		var position = session.RoomPositions.TryGetValue( name, out var place ) ? place : ((float X, float Y))(0.5f, 0.5f);
		if ( target is { } goal )
		{
			var distance = MathF.Sqrt( MathF.Pow( goal.X - position.X, 2 ) + MathF.Pow( goal.Y - position.Y, 2 ) );
			var step = WalkSpeed * delta;
			if ( distance <= step )
				position = goal;
			else
				position = (position.X + (goal.X - position.X) / distance * step, position.Y + (goal.Y - position.Y) / distance * step);
			target = distance <= step ? null : goal;
		}
		sinceSent += delta;
		var moved = MathF.Abs( position.X - lastSent.X ) + MathF.Abs( position.Y - lastSent.Y );
		// The first position announces the player's place in the park to everyone; afterwards only real movement goes out.
		if ( sinceSent >= SendInterval && (moved > 0.001f || !session.RoomPositions.ContainsKey( name )) )
		{
			sinceSent = 0;
			lastSent = position;
			session.MoveTo( position.X, position.Y );
		}
	}

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		var rect = ScreenRect( context.Canvas );
		// The park ground: the muted green of the original parks, with a lighter border.
		context.Batch.AddRectangle( rect, new Veldrid.RgbaByte( 46, 84, 46, 255 ) );
		var edge = MathF.Max( 2f, rect.Height / 200f );
		context.Batch.AddRectangle( new UiRect( rect.X, rect.Y, rect.Width, edge ), new Veldrid.RgbaByte( 96, 140, 96, 255 ) );
		context.Batch.AddRectangle( new UiRect( rect.X, rect.Bottom - edge, rect.Width, edge ), new Veldrid.RgbaByte( 96, 140, 96, 255 ) );
		context.Batch.AddRectangle( new UiRect( rect.X, rect.Y, edge, rect.Height ), new Veldrid.RgbaByte( 96, 140, 96, 255 ) );
		context.Batch.AddRectangle( new UiRect( rect.Right - edge, rect.Y, edge, rect.Height ), new Veldrid.RgbaByte( 96, 140, 96, 255 ) );
		var player = session.Settings.PlayerName;
		if ( session.RoomPositions.Count == 0 && session.RoomPlayers.Count <= 1 )
		{
			// Nobody has moved yet: the room's title and how to walk.
			context.DrawText( context.Fonts.Menu, OnlineStrings.Ui( OnlineStrings.PlayersAtThePark, "Players at the park" ),
				new UiRect( rect.X, rect.Y + rect.Height * 0.4f, rect.Width, rect.Height * 0.1f ), UiColors.Text, UiAlign.Center );
			// [BIN:TPW-EXE:0x4BD933 chat mode hover] the original's chat park says UIHELPTEXT 401, "Left-click to walk to this point"
			context.DrawText( context.Fonts.Small, context.Strings.Help( 401 ),
				new UiRect( rect.X, rect.Y + rect.Height * 0.52f, rect.Width, rect.Height * 0.1f ), UiColors.Text, UiAlign.Center );
		}
		var size = MathF.Max( 10f, rect.Width / 70f );
		foreach ( var (name, (x, y)) in session.RoomPositions )
		{
			var own = player != null && name.Equals( player, StringComparison.OrdinalIgnoreCase );
			var marker = new UiRect( rect.X + x * rect.Width - size / 2, rect.Y + y * rect.Height - size / 2, size, size );
			// The player's own marker is the original islands' yellow; the others are white.
			context.Batch.AddRectangle( marker, own ? new Veldrid.RgbaByte( 252, 232, 7, 255 ) : Veldrid.RgbaByte.White );
			context.DrawText( context.Fonts.Small, name, new UiRect( marker.X - size * 2, marker.Bottom + edge, size * 5, size ),
				own ? UiColors.Highlight : UiColors.Text, UiAlign.Center );
		}
	}
}
