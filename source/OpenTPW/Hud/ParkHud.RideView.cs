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
	/// <summary>The nominal static countdown on an observed eye-camera edge, in seconds.</summary>
	// [BIN:TPW-EXE:0x54faf7 static countdown] the Boolean (gui_CameraFlags & 0x16) edge resets to 300 ms; 0x54fb20 tests positivity before 0x54fb46 subtracts elapsed wall time
	public const float RideStaticSeconds = 0.3f;

	// [BIN:TPW-EXE:0x488aa1 right click] the ride view's procedure 0x488a00 (built at 0x48a1f5 and 0x48a4d4) leaves on the right-button message (button 1) when RMBCancelOn (0x78d911) is set and gui_CameraFlags ([0x790ab0]) & 0x16 is non-zero (0x488ab7), which holds while the eye camera runs; it reads no clock, so a right click leaves at once
	// ([0x48a740] is the finale's and the loss window's procedure, which has the 2000 ms guard, not this one)

	// The viewfinder and the eject button at the rectangles of the ride-view screen's control table.
	// [BIN:TPW-EXE:0x74fa98 ride-view screen table] control 1: the viewfinder model at (170,170)-(1878,1366); control 2: the eject button model at (1916,1404)-(2018,1506), help 468 "Left-click to leave camcorder mode (C)"; the screen is created at 0x48a1d0/0x48a4d4 with the message procedure 0x488a00 and the crosshair pointer 0x15 (0x65f11d)
	private static readonly UiRect ViewfinderBounds = new( 170, 170, 1708, 1196 );
	private static readonly UiRect EjectBounds = new( 1916, 1404, 102, 102 );

	private UiScreen? rideScreen;
	private EyeModeStaticTransition rideStatic;
	/// <summary>Presentation-only monotonic milliseconds; an override lets asset-free tests drive the real HUD update.</summary>
	internal Func<double>? RideStaticClock { get; set; }

	// [BIN:TPW-EXE:0x42a6b8 ride mode] mode 4 enters the native seat camera; 0x42af24 selects terrain camcorder mode 2.
	// [APPROX:UI-094] FreeWalk is the host terrain-inspection substitute for native camcorder mode 2. Only this level's current camera counts;
	// an armed camcorder, a suspended walk beneath a ride, and retired camera owners do not create extra Boolean edges.
	// [BIN:TPW-EXE:0x42ab20 camera mode bits] the mask 0x16 of 0x790ab0 names the walk (2), the ride (4) and 0x10, and no writer sets the low byte's bits 8, 0x10 or 0x20: the setter 0x42ab20 ors in only 2 or 4 (0x42ac4c, 0x42abb5), the camera reset 0x42a9be and the load 0x42d04c clear the word, and 0x42b1c0 rewrites it from itself; the saved word (0x42ce1b, 0x42cf61) is cleared after loading
	private bool EyeCameraActive => (level.RideView != null && ReferenceEquals( Camera.Mode, level.RideView ))
		|| (level.FreeWalk != null && ReferenceEquals( Camera.Mode, level.FreeWalk ));

	private void PrepareRideStatic( UiContext context ) => rideStatic.Prepare( EyeCameraActive,
		RideStaticClock?.Invoke() ?? System.Diagnostics.Stopwatch.GetTimestamp() * (1000.0 / System.Diagnostics.Stopwatch.Frequency),
		(int)(context.Time * 30f) );

	/// <summary>True while the ride view's screen (viewfinder and eject button) is open.</summary>
	public bool RideScreenOpen => rideScreen != null;

	/// <summary>Whether the prepared frame shows static, including its last positive-budget frame.</summary>
	public bool RideStaticActive => rideStatic.ShowStatic;

	/// <summary>The budget after preparing this frame, in seconds; it can be nonpositive while this frame still shows static.</summary>
	public float RideStaticLeft => (float)(rideStatic.RemainingMilliseconds / 1000.0);

	/// <summary>Synchronizes only the viewfinder/eject screen at both ends of input handling; timer observation is separate.</summary>
	private void SyncRideScreen()
	{
		var riding = IsRiding;
		if ( riding && rideScreen == null )
		{
			rideScreen = BuildRideScreen();
			Stack.Push( rideScreen );
		}
		else if ( !riding && rideScreen != null )
		{
			var screen = rideScreen;
			rideScreen = null;
			Stack.RemoveWhere( candidate => ReferenceEquals( candidate, screen ) );
			RideCameraMode.PointerOverride = null;
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
		// Alt+C is no C press either ([BIN:TPW-EXE:0x40C900] exact modifier match): it is the cheats window's key
		var control = Input.IsFrameKeyDown( Key.ControlLeft ) || Input.IsFrameKeyDown( Key.ControlRight ) || (Input.ModifierMaskDown & 0x30) != 0;
		// The C key is the camcorder's shortcut (key table action 0x10, tp.exe 0x488a00); E also leaves, as it enters ([EXT:ride-view-key]).
		if ( shortcutsAllowed && ((!control && Input.Pressed( InputButton.CamcorderMode )) || Input.Pressed( InputButton.RideView )) )
		{
			StopRide();
			return;
		}
		if ( shortcutsAllowed && !mouseCaptured && input.RightPressed && GameOptions.Current.RmbCancel )
			StopRide();
	}

	/// <summary>Draws the prepared frame without changing its timer or noise identity.</summary>
	private void DrawRideStatic( UiContext context )
	{
		if ( !rideStatic.ShowStatic )
			return;
		DrawStatic( context, rideStatic.NoiseFrame );
	}

	/// <summary>Full-screen TV static: a grid of grey cells, each from a hash of its place and the frame number, with a few brighter scan bands.</summary>
	// [EXT:ride-static] retained host hash noise, about 7 px cells at 720p, keyed at 30 Hz with grey levels and scan bands. The traced native generator
	// (0x53e760) instead fills 320 x 240 pixels with black/white/grey150 from CRT random draws on every invocation; that algorithm/sequence is deferred, not untraced.
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
