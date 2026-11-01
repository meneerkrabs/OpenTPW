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
		// [BIN:TPW-EXE:0x4A2AC0 camcorder screen] the walk and the ride both hide the HUD window and show the camcorder window with the viewfinder and the eject button (0x42AB20 -> 0x481470 -> 0x4A2AC0)
		var riding = IsRiding || level.FreeWalk != null;
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
			Clicked = LeaveEyeView,
			Bounds = EjectBounds,
			Anchor = UiAnchor.BottomRight
		} );
		screen.Back = LeaveEyeView;
		screen.DrawOverlay = DrawRideStatic;
		return screen;
	}

	/// <summary>Leaves the ride, or the camcorder walk when there is no ride: the eject button and Escape of the camcorder screen.</summary>
	private void LeaveEyeView()
	{
		if ( IsRiding )
			StopRide();
		else
			CancelCamcorder();
	}

	/// <summary>Leaves the ride view when its exit key or a right click asks for it (Escape and the eject button are the ride screen's own); the walk leaves by the right click only (its C key is the camcorder's).</summary>
	// [BIN:TPW-EXE:0x488AB7 right click leaves] the camcorder window's procedure 0x488A00 acts on the click message 0x10006 (the right button's release within 200 ms and 8 px, 0x65F995) with RMBCancel on, for the walk (0x42AE70) and the ride (0x42A190) alike
	private void UpdateRideExits( UiInput input, bool shortcutsAllowed, bool mouseCaptured )
	{
		if ( !IsRiding )
		{
			if ( level.FreeWalk != null && shortcutsAllowed && !mouseCaptured && input.RightClicked && GameOptions.Current.RmbCancel )
				CancelCamcorder();
			return;
		}
		// Alt+C is no C press either ([BIN:TPW-EXE:0x40C900] exact modifier match): it is the cheats window's key
		var control = Input.IsFrameKeyDown( Key.ControlLeft ) || Input.IsFrameKeyDown( Key.ControlRight ) || (Input.ModifierMaskDown & 0x30) != 0;
		// The C key is the camcorder's shortcut (key table action 0x10, tp.exe 0x488a00); E also leaves, as it enters ([EXT:ride-view-key]).
		if ( shortcutsAllowed && ((!control && Input.Pressed( InputButton.CamcorderMode )) || Input.Pressed( InputButton.RideView )) )
		{
			StopRide();
			return;
		}
		if ( shortcutsAllowed && !mouseCaptured && input.RightClicked && GameOptions.Current.RmbCancel )
			StopRide();
	}

	/// <summary>Draws the prepared frame without changing its timer or noise identity.</summary>
	private void DrawRideStatic( UiContext context )
	{
		if ( !rideStatic.ShowStatic )
			return;
		DrawStatic( context, rideStatic.NoiseFrame );
	}

	/// <summary>The noise's pixels, one byte each (<see cref="RideStaticNoise"/>); the camcorder's static is one at a time on the one UI thread.</summary>
	private static readonly byte[] StaticPixels = new byte[RideStaticNoise.Width * RideStaticNoise.Height];

	/// <summary>
	/// Full-screen TV static: a 320 x 240 source of black, white and grey 150 pixels stretched over the screen, drawn as the black ground with a run of rectangles for each stretch of white or grey.
	/// <paramref name="frame"/> keys the noise (a new one each 1/30 s here).
	/// </summary>
	// [BIN:TPW-EXE:0x53E760 ride static] a 320 x 240 source is regenerated with every invocation (the frame loop calls it, 0x54FB3F, instead of drawing the scene while the budget lasts) and stretched over the screen (0x5F92E0): for each pixel, column by
	// column, one CRT draw with its low two bits 0 (a quarter) makes the pixel white or grey 150 by the low bit of a second draw, else it is black
	// [BIN:TPW-EXE:0x67B5C0 CRT rand] the draws are OpenTPW's own copy of the CRT's generator (214013 x state + 2531011, bits 16 to 30) seeded by the frame key: the original's is seeded from the clock at start, so any seed is a draw of the same process, and no stream of the world or the guests is touched
	internal static void DrawStatic( UiContext context, int frame )
	{
		var canvas = context.Canvas;
		var pixels = StaticPixels;
		RideStaticNoise.Fill( pixels, unchecked((uint)frame * 2654435761u + 0x9E3779B9u) );
		var cellWidth = canvas.Width / RideStaticNoise.Width;
		var cellHeight = canvas.Height / RideStaticNoise.Height;
		var black = new RgbaByte( 0, 0, 0, 255 );
		context.Batch.AddRectangle( new UiRect( 0, 0, canvas.Width, canvas.Height ), black );
		for ( var row = 0; row < RideStaticNoise.Height; row++ )
		{
			var column = 0;
			while ( column < RideStaticNoise.Width )
			{
				var kind = pixels[row * RideStaticNoise.Width + column];
				if ( kind == RideStaticNoise.Black )
				{
					column++;
					continue;
				}
				var start = column;
				while ( column < RideStaticNoise.Width && pixels[row * RideStaticNoise.Width + column] == kind )
					column++;
				var grey = kind == RideStaticNoise.White ? (byte)255 : (byte)RideStaticNoise.GreyLevel;
				context.Batch.AddRectangle( new UiRect( start * cellWidth, row * cellHeight, (column - start) * cellWidth + 0.5f, cellHeight + 0.5f ), new RgbaByte( grey, grey, grey, 255 ) );
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
