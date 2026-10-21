using OpenTPW.Online.Api;
using OpenTPW.UI.Original;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace OpenTPW.UI;

/// <summary>
/// The Online World screen over the 3D <see cref="GlobeScene"/>: arrow keys and the panel's arrow buttons turn the
/// globe, a click on the globe zooms in or out, the cursor at a screen edge or the right button held steers it, and
/// the city under the cursor is named at the top (docs/ONLINE.md).
/// </summary>
public sealed class OnlineGlobeScreen : UiScreen
{
	// [BIN:TPW-EXE:0x005DC860 globe edge scroll] the cursor within 1% of the 2048×1536 canvas edge steers the globe
	public const float EdgeMargin = 0.01f;

	private readonly Func<GlobeScene?> globe;
	private NVector2? firstMouse;
	private bool mouseMoved;

	public OnlineGlobeScreen( Func<GlobeScene?> globe, string name = "onlineWorld" ) : base( name )
	{
		this.globe = globe;
		HidesBelow = true;
	}

	/// <summary>The city under the cursor this frame.</summary>
	public CityInfo? Hovered { get; private set; }

	/// <summary>A click on a city's pin: enter its lobby.</summary>
	public Action<CityInfo>? CityClicked { get; set; }

	public override bool Update( UiContext context, UiInput input )
	{
		var canvas = context.Canvas;
		var overUi = input.Mouse.X >= 0 && HitTest( canvas, input.Mouse ) is not null;
		if ( input.Has( UiKeys.Left ) ) GlobeCameraMode.Turn( -1, 0 );
		if ( input.Has( UiKeys.Right ) ) GlobeCameraMode.Turn( 1, 0 );
		if ( input.Has( UiKeys.Up ) ) GlobeCameraMode.Turn( 0, 1 );
		if ( input.Has( UiKeys.Down ) ) GlobeCameraMode.Turn( 0, -1 );
		Hovered = null;
		if ( input.Mouse.X >= 0 && canvas.Width > 0 && canvas.Height > 0 )
		{
			var x = input.Mouse.X / canvas.Width;
			var y = input.Mouse.Y / canvas.Height;
			float Edge( float value ) => value < EdgeMargin ? -1 : value > 1 - EdgeMargin ? 1 : 0;
			// [EXT:online-globe-edge-gate] edge steering starts once the cursor has moved on this screen, so a cursor resting in a
			// corner when the screen opens does not spin the globe. Not reproduced: the original has no such gate: 0x005DC860 reads
			// the position of the online front end's last mouse move (0x005D5DD0, event 0x10003), which is the canvas centre (0x400,
			// 0x300, set by 0x005D5770) until the first move after the front end starts, so it steers from the first frame once the
			// mouse has moved anywhere in the online screens; OpenTPW keeps no move history across the online screens
			firstMouse ??= input.Mouse;
			mouseMoved |= NVector2.DistanceSquared( firstMouse.Value, input.Mouse ) > 4;
			// [BIN:TPW-EXE:0x005DC860 globe steering mode] the Scroll option (0x78D910, options control 0x1D4C7) picks the steering:
			// Pushscroll steers at the screen edges only, the other mode only by the right button held (0x005D5EE0(2))
			var push = GameOptions.Current.Scroll == ScrollMode.Pushscroll;
			var steerX = push && mouseMoved ? Edge( x ) : 0;
			var steerY = push && mouseMoved ? -Edge( y ) : 0;
			// [BIN:TPW-EXE:0x005DC860 globe right button] holding the right button steers by the cursor's offset from the screen centre
			if ( !push && Input.Mouse.Right && !overUi )
			{
				steerX += x * 2 - 1;
				steerY += 1 - y * 2;
			}
			if ( steerX != 0 || steerY != 0 )
				GlobeCameraMode.Steer( steerX, steerY, context.Delta );
			if ( !overUi && globe() is { } scene && Ray( input.Mouse, canvas ) is { } ray )
				Hovered = scene.Pick( ray.Origin, ray.Direction );
			// [BIN:TPW-EXE:0x005DD530 online globe input] a click (event 0x10005) on a hovered pin selects that city (DAT_00f82888) and switches to its lobby, whose top-10 request reads it (0x005E23F0); zooming is the zoom button (event 0x10006, param 2)
			if ( !input.PointerSuppressed && input.LeftReleased && !overUi && Hovered is { } city )
				CityClicked?.Invoke( city );
		}
		// Arrow keys turn the globe instead of moving the focus; the right button steers instead of going back.
		var forwarded = input with { Pressed = input.Pressed & ~(UiKeys.Left | UiKeys.Right | UiKeys.Up | UiKeys.Down), RightPressed = false };
		base.Update( context, forwarded );
		// The globe covers the whole screen: nothing below (a park) gets the input.
		return true;
	}

	internal override bool UpdatePointer( UiContext context, UiInput input, UiElement? recipient )
	{
		if ( IsRemoved ) return true;
		// Re-pick at this record's position, but never repeat the frame's camera steering or keyboard turns.
		var canvas = context.Canvas;
		Hovered = null;
		if ( recipient == null && input.Mouse.X >= 0 && canvas.Width > 0 && canvas.Height > 0
			&& globe() is { } scene && Ray( input.Mouse, canvas ) is { } ray )
			Hovered = scene.Pick( ray.Origin, ray.Direction );
		if ( input.LeftReleased && !input.NotClick && Hovered is { } city )
			CityClicked?.Invoke( city );
		// As in the frame path, the right button steers the globe rather than cancelling the screen.
		base.UpdatePointer( context, input with { RightPressed = false }, recipient );
		return true;
	}

	/// <summary>World-space ray through a canvas pixel, from the current camera.</summary>
	private static (NVector3 Origin, NVector3 Direction)? Ray( NVector2 mouse, UiCanvas canvas )
	{
		if ( !NMatrix4x4.Invert( Camera.ViewMatrix * Camera.ProjMatrix, out var inverse ) )
			return null;
		var ndcX = mouse.X / canvas.Width * 2 - 1;
		var ndcY = 1 - mouse.Y / canvas.Height * 2;
		NVector3 Unproject( float depth )
		{
			var point = NVector4.Transform( new NVector4( ndcX, ndcY, depth, 1 ), inverse );
			return new NVector3( point.X, point.Y, point.Z ) / point.W;
		}
		var near = Unproject( 0 );
		var far = Unproject( 1 );
		return (near, NVector3.Normalize( far - near ));
	}
}
