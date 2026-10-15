using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>
/// The ride view's screen: the camcorder viewfinder with the eject button, the hidden HUD, the crosshair pointer and the TV-static transition on entry and exit
/// (UI-093/094/095).
/// </summary>
public sealed partial class ParkHud
{
	/// <summary>How long the full-screen static plays on entry and on exit, in seconds.</summary>
	// [DATA:video 77:09-77:24, 7:52, 8:36] the static lasts 0.30 s on entering and leaving the ride (19 and 18 video frames of 60 per second) and 0.35 s (21 frames) in the camcorder walk; the noise changes every rendered frame (about 10 different images)
	// [APPROX:UI-094] 0.33 s, the middle of the measured 0.30-0.35 s; the original's timer for it is not traced
	public const float RideStaticSeconds = 0.33f;

	/// <summary>A right click leaves the ride view only after it has been open this long, in seconds.</summary>
	// [BIN:TPW-EXE:0x48a7bd right-click guard] the right-button message (0x10006, button 1) leaves only when RMBCancelOn (0x78d911) is set and the clock (0x65968e) is more than 0x7d0 = 2000 ms past the stamp the screen took on message 0x15 (0x48a789); Escape without modifiers (0x1b, 0x48a8a7) leaves too
	internal const float RideRightClickDelaySeconds = 2f;

	// The viewfinder and the eject button at the rectangles of the ride-view screen's control table.
	// [BIN:TPW-EXE:0x74fa98 ride-view screen table] control 1: the viewfinder model at (170,170)-(1878,1366); control 2: the eject button model at (1916,1404)-(2018,1506), help 468 "Left-click to leave camcorder mode (C)"; the screen is created at 0x48a1d0/0x48a4d4 with the message procedure 0x488a00 and the crosshair pointer 0x15 (0x65f11d)
	private static readonly UiRect ViewfinderBounds = new( 170, 170, 1708, 1196 );
	private static readonly UiRect EjectBounds = new( 1916, 1404, 102, 102 );

	private UiScreen? rideScreen;
	private float rideStaticLeft;

	/// <summary>True while the ride view's screen (viewfinder and eject button) is open.</summary>
	public bool RideScreenOpen => rideScreen != null;

	/// <summary>True while the TV-static transition covers the screen.</summary>
	public bool RideStaticActive => rideStaticLeft > 0;

	/// <summary>The seconds of static left.</summary>
	public float RideStaticLeft => rideStaticLeft;

	/// <summary>Opens the ride screen when the ride view starts and closes it when it ends, each with the static transition; runs at both ends of a frame.</summary>
	private void SyncRideScreen( float delta )
	{
		if ( rideStaticLeft > 0 )
			rideStaticLeft = Math.Max( 0, rideStaticLeft - Math.Max( 0, delta ) );
		var riding = IsRiding;
		if ( riding && rideScreen == null )
		{
			rideScreen = BuildRideScreen();
			Stack.Push( rideScreen );
			rideStaticLeft = RideStaticSeconds;
		}
		else if ( !riding && rideScreen != null )
		{
			var screen = rideScreen;
			rideScreen = null;
			Stack.RemoveWhere( candidate => ReferenceEquals( candidate, screen ) );
			rideStaticLeft = RideStaticSeconds;
		}
	}

	private UiScreen BuildRideScreen()
	{
		var screen = new UiScreen( "rideView" ) { Modal = false, HidesBelow = true };
		screen.Add( new UiModelImage { Id = "viewfinder", Model = "viewfinder", Bounds = ViewfinderBounds, Anchor = UiAnchor.Center } );
		screen.Add( new UiButton
		{
			Id = "eject",
			Model = "b_eject",
			Help = strings.Help( 468 ),
			Clicked = StopRide,
			Bounds = EjectBounds,
			Anchor = UiAnchor.BottomRight
		} );
		screen.Back = StopRide;
		screen.DrawOverlay = DrawRideStatic;
		return screen;
	}

	/// <summary>Leaves the ride view when its exit key or a right click asks for it (Escape and the eject button are the ride screen's own).</summary>
	private void UpdateRideExits( UiInput input, bool shortcutsAllowed, bool mouseCaptured )
	{
		if ( !IsRiding )
			return;
		var control = Input.IsFrameKeyDown( Key.ControlLeft ) || Input.IsFrameKeyDown( Key.ControlRight );
		// The C key is the camcorder's shortcut (key table action 0x10, tp.exe 0x488a00); E also leaves, as it enters ([EXT:ride-view-key]).
		if ( shortcutsAllowed && ((!control && Input.Pressed( InputButton.CamcorderMode )) || Input.Pressed( InputButton.RideView )) )
		{
			StopRide();
			return;
		}
		if ( shortcutsAllowed && !mouseCaptured && input.RightPressed && GameOptions.Current.RmbCancel && RideRightClickReady( Time.Now - level.RideViewOpenedAt ) )
			StopRide();
	}

	/// <summary>Whether a right click may leave: more than two seconds since the ride view opened.</summary>
	internal static bool RideRightClickReady( float secondsOpen ) => secondsOpen > RideRightClickDelaySeconds;

	/// <summary>The static drawn over a screen: full-screen grey noise that changes every frame it is shown.</summary>
	private void DrawRideStatic( UiContext context )
	{
		if ( rideStaticLeft <= 0 )
			return;
		DrawStatic( context, (int)(context.Time * 30f) );
	}

	/// <summary>Full-screen TV static: a grid of grey cells, each from a hash of its place and the frame number, with a few brighter scan bands.</summary>
	// [EXT:ride-static] the original's static image is not in the data files or the executable's strings (no static or noise texture or movie was found); the noise is generated here, about 7 px cells at 720p, a new picture each frame like the video's
	internal static void DrawStatic( UiContext context, int frame )
	{
		var canvas = context.Canvas;
		var cell = Math.Max( 2f, canvas.Height / 100f );
		var columns = (int)MathF.Ceiling( canvas.Width / cell );
		var rows = (int)MathF.Ceiling( canvas.Height / cell );
		var state = unchecked((uint)(frame * 2654435761u + 0x9E3779B9u));
		for ( var row = 0; row < rows; row++ )
		{
			state = unchecked(state * 1664525u + 1013904223u);
			var band = (state >> 24) < 18 ? 40 : 0;
			for ( var column = 0; column < columns; column++ )
			{
				state ^= state << 13;
				state ^= state >> 17;
				state ^= state << 5;
				var grey = (byte)Math.Min( 255, 30 + (state >> 24) * 200 / 255 + band );
				context.Batch.AddRectangle( new UiRect( column * cell, row * cell, cell + 0.5f, cell + 0.5f ), new RgbaByte( grey, grey, grey, 255 ) );
			}
		}
	}

	/// <summary>Draws the crosshair pointer of the ride view: a small yellow plus about 16 px at 720p.</summary>
	// [DATA:video 77:09-77:24] the pointer inside the camcorder is a small yellow "+" crosshair (about 16 px at 720p), not the c_cam art
	// [EXT:ride-crosshair] the original's crosshair pointer (c_crosshair.ani, art CCro.tga) is not in the data files, so the plus is drawn from rectangles
	private static void DrawCrosshair( UiContext context, NVector2 at )
	{
		var scale = context.Canvas.Height / 720f;
		var half = 8f * scale;
		var thickness = Math.Max( 2f, 2f * scale );
		var yellow = new RgbaByte( 255, 232, 0, 255 );
		var shade = new RgbaByte( 0, 0, 0, 255 );
		var outline = Math.Max( 1f, scale );
		context.Batch.AddRectangle( new UiRect( at.X - half - outline, at.Y - thickness / 2 - outline, half * 2 + outline * 2, thickness + outline * 2 ), shade );
		context.Batch.AddRectangle( new UiRect( at.X - thickness / 2 - outline, at.Y - half - outline, thickness + outline * 2, half * 2 + outline * 2 ), shade );
		context.Batch.AddRectangle( new UiRect( at.X - half, at.Y - thickness / 2, half * 2, thickness ), yellow );
		context.Batch.AddRectangle( new UiRect( at.X - thickness / 2, at.Y - half, thickness, half * 2 ), yellow );
	}
}
