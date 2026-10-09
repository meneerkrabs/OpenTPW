using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;

namespace OpenTPW;

internal sealed partial class SandboxSmokeTest : IDisposable
{
	private readonly Level level;
	private readonly BaseFileSystem originalSaveFileSystem;
	private readonly string temporaryDirectory = Path.Combine( Path.GetTempPath(), $"opentpw-smoke-{Guid.NewGuid():N}" );
	private const double MaximumSecondsUntilMotion = 30;
	private readonly System.Diagnostics.Stopwatch sinceOpened = System.Diagnostics.Stopwatch.StartNew();
	private int frame;
	private int motionFrame;
	private bool completed;
	private byte[]? earlyPixels;
	private System.Numerics.Matrix4x4[]? earlyPose;
	private float earlyTick;
	private bool guestsVerified;
	private int guestCaptureFrame;
	private byte[]? guestPixels;
	private double motionScriptMilliseconds;
	private readonly System.Diagnostics.Stopwatch sinceMotion = new();
	private const double MaximumSecondsUntilRelease = 60;

	public SandboxSmokeTest( Level level )
	{
		this.level = level;
		originalSaveFileSystem = SaveFileSystem;
		Directory.CreateDirectory( temporaryDirectory );
		SaveFileSystem = new BaseFileSystem( temporaryDirectory );
		if ( level.OriginalPark != null )
			rideSite = VerifyOriginalLevel( level.OriginalPark );
		Require( level.PlaceRide( rideSite ), "place original Totem" );
		PlaceObjects();
		level.PlacedRide!.Start();
		Render.CaptureOutput = true;
		// Safety net: a smoke test that stops getting frames (e.g. paused rendering) must fail, not hang.
		watchdog = new System.Threading.Timer( _ =>
		{
			Console.Error.WriteLine( $"Native sandbox smoke test failed: timed out after {MaximumSeconds} s at frame {frame}; display {Render.Metrics}, {Render.Scaling.Describe()}" );
			Environment.Exit( 1 );
		}, null, TimeSpan.FromSeconds( MaximumSeconds ), System.Threading.Timeout.InfiniteTimeSpan );
		if ( level.Guests != null )
			SpawnGuestsNearRide( level.Guests, level.PlacedRide );
	}

	/// <summary>Seeds visitors on the path cells around the Totem's entrance so its script sees passengers early.</summary>
	private static void SpawnGuestsNearRide( GuestSimulation guests, PrototypeRide ride )
	{
		Require( guests.Grid.WalkableCount > 0 && guests.Attractions.Contains( ride.Visitors ), "register the Totem with the guest simulation" );
		var (ex, ey) = ride.Visitors.EntranceCell;
		var cells = Enumerable.Range( 0, guests.Grid.CountX * guests.Grid.CountY )
			.Select( index => (X: index % guests.Grid.CountX, Y: index / guests.Grid.CountX) )
			.Where( cell => guests.Grid.Distance( cell.X, cell.Y, ex, ey ) is >= 1 and <= 4 )
			.OrderBy( cell => guests.Grid.Distance( cell.X, cell.Y, ex, ey ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ).ToArray();
		Require( cells.Length > 0, "find path cells near the Totem entrance" );
		for ( var index = 0; index < 10; index++ )
			guests.SpawnInPark( cells[index % cells.Length].X, cells[index % cells.Length].Y );
	}

	private const int MaximumSeconds = 180;
	private readonly System.Threading.Timer watchdog;

	private Vector3 rideSite = Vector3.Zero;

	/// <summary>Checks the import and the MAP/save build rules, and returns a buildable site.</summary>
	private Vector3 VerifyOriginalLevel( OriginalPark park )
	{
		var field = park.Heightfield;
		Require( level.OriginalTerrain != null && level.OriginalTerrain.SurfaceCellCount > 0 && level.OriginalTerrain.TerrainMeshCount > 0, "build original terrain" );
		var cells = Enumerable.Range( 0, field.CellCountX * field.CellCountZ ).Select( index => (X: index % field.CellCountX, Y: index / field.CellCountX) ).ToArray();
		var water = cells.First( cell => park.Map.GetFlagsAt( cell.X, cell.Y ).HasFlag( MapCellFlags.Water ) || park.Map.GetFlagsAt( cell.X, cell.Y ).HasFlag( MapCellFlags.EntranceArea ) );
		Require( !level.PlaceRide( OriginalParkPlacement.GetCellCenter( field, water.X, water.Y ) ) && level.PlacedRide == null, "reject water/entrance cells" );
		if ( park.Save != null )
		{
			Require( park.Save.PathCells.Count > 0 && park.Save.PlacedObjects.Count > 0, "import original save paths and objects" );
			var path = park.Save.PathCells[park.Save.PathCells.Count / 2];
			Require( !level.PlaceRide( OriginalParkPlacement.GetCellCenter( field, path.X, path.Y ) ), "reject imported path cells" );
			var item = park.Save.PlacedObjects[0];
			Require( !level.PlaceRide( OriginalParkPlacement.GetCellCenter( field, item.MinX, item.MinY ) ), "reject imported object footprints" );
		}
		// Nearest buildable site to the initial path, so the capture shows the imported park.
		var start = cells.Where( cell => park.Map.GetFlagsAt( cell.X, cell.Y ).HasFlag( MapCellFlags.InitialPath ) ).DefaultIfEmpty( (X: field.CellCountX / 2, Y: field.CellCountZ / 2) ).First();
		var site = cells.OrderBy( cell => Math.Abs( cell.X - start.X ) + Math.Abs( cell.Y - start.Y ) )
			.First( cell => level.CheckOriginalPlacement( OriginalParkPlacement.GetCellCenter( field, cell.X, cell.Y ) ) == OriginalPlacementResult.Allowed );
		return OriginalParkPlacement.GetCellCenter( field, site.X, site.Y );
	}

	public void Update()
	{
		++frame;
		if ( motionFrame == 0 )
		{
			// Totem.RSE waits up to 10 s for passengers before it triggers its main animation.
			// Totem.RSE first plays its create clip (totemc); the cart cycle is ANIM_Main (totemm1).
			if ( level.PlacedRide!.IsPlayingMainAnimation && level.PlacedRide.MotionHeight > 0 )
			{
				motionFrame = frame;
				motionScriptMilliseconds = level.PlacedRide.Script.TimeMilliseconds;
				sinceMotion.Start();
				if ( level.Guests != null && !guestsVerified )
				{
					var riders = level.PlacedRide.Script[RideVariables.VAR_ONRIDE];
					Log.Trace( $"Totem started at script time {motionScriptMilliseconds:F0} ms with {riders} passenger(s); {level.PlacedRide.Visitors.BoardedTotal} boarded so far." );
					Require( riders > 0 && level.PlacedRide.Visitors.Riders.Count == riders, "Totem.RSE sees real guests on board (VAR_ONRIDE)" );
					// With every imported attraction live, guests spread out; the Totem may leave on its 10 s passenger time-out
					// (after its create clip) with a partial load, which still proves real guests ride it.
					Log.Trace( $"Totem passenger loop: {motionScriptMilliseconds - level.PlacedRide.CreateAnimationMilliseconds:F0} ms after its create clip." );
				}
			}
			else
				Require( sinceOpened.Elapsed.TotalSeconds < MaximumSecondsUntilMotion, "original Totem script starts the ride motion" );
			return;
		}
		if ( level.Guests != null && !guestsVerified )
		{
			UpdateGuestPhase( level.Guests );
			if ( guestsVerified )
			{
				// Run the animation/close/remove checks on the next ride cycle.
				motionFrame = 0;
				sinceOpened.Restart();
			}
			return;
		}
		var step = frame - motionFrame;
		if ( step == 10 )
		{
			Require( level.PlacedRide!.IsPlayingMainAnimation, "script-triggered ANIM_Main plays the original Totem animation" );
			earlyPixels = CaptureFrame( "ride-early.png" ).Pixels;
			earlyPose = level.PlacedRide.NodeTransforms.ToArray();
			earlyTick = level.PlacedRide.AnimationTick;
			SnapshotObjects();
		}
		if ( step == 30 )
		{
			Require( level.PlacedRide!.Script.State != RideVMState.Faulted && level.PlacedRide.Script[RideVariables.VAR_RUNNING] == 1, "original script reports the ride running" );
			var parkFrame = CaptureFrame( level.OriginalPark == null ? "park.png" : "original-park.png" );
			VerifyPoseChanged( level.PlacedRide, parkFrame.Pixels );
			VerifyDisplaySizes();
			VerifyPicking();
			VerifyText( CaptureOutputFrame( level.OriginalPark == null ? "output.png" : "original-output.png" ) );
			VerifyObjectsAnimate();
			if ( level.OriginalPark == null )
				level.SaveSandbox();
			level.PlacedRide!.Stop();
			Require( !level.PlacedRide.IsOpen, "close ride" );
			level.RemoveRide();
			Require( level.PlacedRide == null, "remove ride" );
			if ( level.OriginalPark == null )
			{
				level.LoadSandbox();
				Require( level.PlacedRide != null && level.PlacedRide.IsOpen, "restore open ride" );
			}
			else
				Require( Throws( level.SaveSandbox ) && Throws( level.LoadSandbox ), "sandbox saves disabled for original levels" );
		}
		if ( step == 60 && level.OriginalPark == null )
		{
			level.RemoveRide();
			level.SaveSandbox();
			level.LoadSandbox();
			Require( level.PlacedRide == null, "restore empty park" );
		}
		if ( step == 65 )
		{
			CaptureFrame( level.OriginalPark == null ? "terrain.png" : "original-terrain.png" );
			Require( level.PlaceRide( level.OriginalPark == null ? new Vector3( 10, 10, 0 ) : rideSite ), "place ride again" );
		}
		if ( step >= 66 && !displayChangesDone )
			displayChangesDone = StepDisplayChanges();
		if ( step >= 90 && displayChangesDone && !completed )
		{
			if ( level.Park != null )
				VerifyParkEconomy( level.Park, level.Guests );
			Device.WaitForIdle();
			completed = true;
			Log.Trace( level.OriginalPark == null
				? $"Native sandbox smoke test passed: {frame} frames, original ride and RSE script, script-triggered motion, original animation readback, close, remove, isolated save/load, BF4 text and GPU readback; {displaySummary}."
				: $"Native original-level smoke test passed: {frame} frames, {level.OriginalPark.LevelName} terrain/import, MAP/save build rules, original ride and RSE script, guests boarding/riding/released by Totem.RSE, guest sprite readback, animation readback, close, remove, BF4 text, park economy and GPU readback; {displaySummary}." );
			Render.Window.SdlWindow.Close();
		}
	}

	/// <summary>Checks the park clock/money on the fixed clock, a month-end wage payment and a park save round trip.</summary>
	private static void VerifyParkEconomy( ParkEconomyRuntime park, GuestSimulation? guests )
	{
		var economy = park.Economy;
		Require( economy.Tick > 0, "park clock advances on the fixed simulation clock" );
		if ( guests != null )
		{
			long Booked( LedgerCategory category ) => economy.Ledger.CurrentTotals.GetValueOrDefault( category ) + economy.Ledger.History.Sum( month => month[category] );
			var gate = Booked( LedgerCategory.GateTakings );
			Require( guests.Payments == park.Guests && economy.Counters[ParkCounters.Admissions] == guests.Admissions, "every guest admission goes through the park economy" );
			Require( guests.Admissions == 0 || gate > 0, "admission revenue enters the ledger" );
			Log.Trace( $"Park economy guests: {guests.Admissions} admissions at ${economy.EntranceFee}: gate takings ${gate}, shop ${Booked( LedgerCategory.ShopTakings )}, sideshow ${Booked( LedgerCategory.SideshowTakings )}; "
				+ $"{guests.GetStatistics().InPark} in park, rating {economy.ParkRating}, balance ${economy.Balance}." );
		}
		var startDate = economy.Date;
		var startBalance = economy.Balance;
		var candidate = economy.Staff.Candidates.FirstOrDefault( item => item.Type == StaffType.Mechanic );
		Require( candidate != null, "staff pool offers a mechanic" );
		var mechanic = economy.Hire( candidate!.Id );
		var wage = economy.Staff.MonthlyWage( mechanic );
		var monthEnd = (ParkCalendar.MonthIndex( economy.Tick ) + 1) * ParkCalendar.DaysPerMonth * ParkCalendar.TicksPerDay;
		economy.Advance( monthEnd - economy.Tick );
		Require( economy.Tick == monthEnd && economy.Date.Day == 1 && economy.Balance == startBalance - wage, "month-end wages leave the bank account" );
		var path = SaveFileSystem.GetAbsolutePath( "opentpw-park.json" );
		var before = ParkSaveFile.Serialize( economy );
		park.Save( path );
		park.Load( path );
		Require( ParkSaveFile.Serialize( park.Economy ) == before, "park save/load round trip" );
		Log.Trace( $"Park economy smoke: {startDate} -> {park.Economy.Date}; balance ${startBalance} -> ${park.Economy.Balance} after ${wage} mechanic wages; save/load round trip identical." );
	}

	/// <summary>
	/// Waits until Totem.RSE releases a rider through VAR_LETMEOFF, then compares GPU readback with and
	/// without the guest sprites.
	/// </summary>
	private void UpdateGuestPhase( GuestSimulation guests )
	{
		var ride = level.PlacedRide!;
		if ( guestPixels == null )
		{
			if ( ride.Visitors.ReleasedTotal == 0 )
			{
				Require( sinceMotion.Elapsed.TotalSeconds < MaximumSecondsUntilRelease, "Totem.RSE releases its riders through VAR_LETMEOFF" );
				return;
			}
			Require( level.GuestRenderer != null && level.GuestRenderer.DrawnGuests > 0, "draw guest sprites" );
			guestPixels = CaptureFrame( "original-guests.png" ).Pixels;
			level.GuestRenderer!.Visible = false;
			guestCaptureFrame = frame;
			return;
		}
		if ( frame == guestCaptureFrame + 1 )
		{
			var hidden = CaptureFrame( "original-guests-hidden.png" ).Pixels;
			level.GuestRenderer!.Visible = true;
			var changed = 0;
			for ( var index = 0; index < hidden.Length; index += 4 )
				changed += hidden.AsSpan( index, 3 ).SequenceEqual( guestPixels.AsSpan( index, 3 ) ) ? 0 : 1;
			var stats = guests.GetStatistics();
			Log.Trace( $"Guests: {guests.Guests.Count} total, {stats.InPark} in park ({stats.Walking} walking, {stats.Queueing} queueing, {stats.OnRides} riding), {stats.Arriving} arriving, {stats.Leaving} leaving; "
				+ $"{stats.Admissions} admissions, Totem boarded {ride.Visitors.BoardedTotal}/released {ride.Visitors.ReleasedTotal}; sprite pixels in readback: {changed}." );
			// Sprites are world geometry: their pixel footprint scales with the world target (render scale), not with the UI scale.
			var minimumChanged = Math.Max( 50, 200L * (hidden.Length / 4) / (1280 * 720) );
			Require( changed > minimumChanged, $"guest sprites change the world readback ({changed} > {minimumChanged} pixels at {Render.Scaling.Describe()})" );
			Require( guests.Guests.Any( guest => guest.IsVisible && guests.Grid.IsWalkable( guest.Cell.X, guest.Cell.Y ) && guest.State is GuestState.WalkingAround or GuestState.GoingToRide or GuestState.ExitingRide ), "guests walk the imported paths" );
			Require( stats.Admissions > 0 || stats.Arriving > 0, "guests arrive from the bus stops" );
			guestsVerified = true;
		}
	}

	/// <summary>World-only readback: the resolved internal-size 3D target before scaling and UI.</summary>
	private static (byte[] Pixels, int Width, int Height) CaptureFrame( string name ) => Capture( Render.ResolveColorTexture, name );

	/// <summary>End-to-end readback of the output after world scaling and the BF4 UI (ImGui excluded).</summary>
	private static (byte[] Pixels, int Width, int Height) CaptureOutputFrame( string name ) => Capture( Render.OutputCaptureTexture!, name );

	private static (byte[] Pixels, int Width, int Height) Capture( Veldrid.Texture source, string name )
	{
		using var staging = Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D(
			source.Width, source.Height, 1, 1, source.Format, TextureUsage.Staging ) );
		using var commands = Device.ResourceFactory.CreateCommandList();
		commands.Begin();
		commands.CopyTexture( source, staging );
		commands.End();
		Device.SubmitCommands( commands );
		Device.WaitForIdle();
		var mapped = Device.Map( staging, MapMode.Read );
		try
		{
			var rowBytes = checked((int)source.Width * 4);
			var pixels = new byte[checked(rowBytes * (int)source.Height)];
			for ( var row = 0; row < source.Height; ++row )
				Marshal.Copy( IntPtr.Add( mapped.Data, checked((int)(row * mapped.RowPitch)) ), pixels, row * rowBytes, rowBytes );
			if ( source.Format is PixelFormat.R8_G8_B8_A8_UNorm )
				for ( var pixel = 0; pixel < pixels.Length; pixel += 4 )
					(pixels[pixel], pixels[pixel + 2]) = (pixels[pixel + 2], pixels[pixel]);
			Require( source.Format is PixelFormat.B8_G8_R8_A8_UNorm or PixelFormat.R8_G8_B8_A8_UNorm, "readback format is 8-bit RGBA" );
			Require( pixels.Where( ( value, index ) => index % 4 != 3 ).Any( value => value != 0 ), "terrain framebuffer is not black" );
			var colors = new HashSet<uint>();
			for ( var pixel = 0; pixel < pixels.Length && colors.Count <= 32; pixel += 4 )
				colors.Add( BitConverter.ToUInt32( pixels, pixel ) );
			Require( colors.Count > 32, "terrain framebuffer retains texture detail" );
			using var image = Image.LoadPixelData<Bgra32>( pixels, (int)source.Width, (int)source.Height );
			var artifactDirectory = Path.GetFullPath( "artifacts" );
			Directory.CreateDirectory( artifactDirectory );
			image.SaveAsPng( Path.Combine( artifactDirectory, $"native-smoke-{name}" ) );
			return (pixels, (int)source.Width, (int)source.Height);
		}
		finally
		{
			Device.Unmap( staging );
		}
	}

	/// <summary>Requires the original animation to move nodes and change the GPU readback between two frames.</summary>
	private void VerifyPoseChanged( PrototypeRide ride, byte[] pixels )
	{
		Require( ride.AnimationTick != earlyTick, "original Totem animation advances" );
		var changedNodes = ride.NodeTransforms.Where( ( transform, index ) => !transform.Equals( earlyPose![index] ) ).Count();
		Require( changedNodes >= 3, "animated cart and cogs change pose between frames" );
		var changedPixels = 0;
		for ( var index = 0; index < pixels.Length; index += 4 )
			changedPixels += pixels.AsSpan( index, 3 ).SequenceEqual( earlyPixels!.AsSpan( index, 3 ) ) ? 0 : 1;
		Require( changedPixels > 100, "animated pose changes the GPU readback" );
		Log.Trace( $"Totem animation ticks {earlyTick:F1} -> {ride.AnimationTick:F1}: {changedNodes} node transforms and {changedPixels} pixels changed." );
	}

	/// <summary>
	/// Compares the BF4 text panel in GPU readback with the CPU composite of the same quads.
	/// </summary>
	private void VerifyText( (byte[] Pixels, int Width, int Height) frame )
	{
		var overlay = level.TextOverlay;
		var (x, y, width, height) = overlay.Bounds;
		Require( width > 0 && x + width <= frame.Width && y + height <= frame.Height, "text panel fits the framebuffer" );
		var expected = (byte[])frame.Pixels.Clone();
		overlay.Batch.Composite( expected, frame.Width, frame.Height );
		var background = expected.AsSpan( (y * frame.Width + x) * 4, 3 ).ToArray();
		var textPixels = 0;
		var maximumDifference = 0;
		for ( var row = y; row < y + height; ++row )
		{
			for ( var column = x; column < x + width; ++column )
			{
				var pixel = (row * frame.Width + column) * 4;
				for ( var channel = 0; channel < 3; ++channel )
					maximumDifference = Math.Max( maximumDifference, Math.Abs( frame.Pixels[pixel + channel] - expected[pixel + channel] ) );
				if ( !frame.Pixels.AsSpan( pixel, 3 ).SequenceEqual( background ) )
					++textPixels;
			}
		}
		var inkPixels = overlay.Batch.Quads.Where( quad => quad.Atlas != null ).Sum( quad =>
			Enumerable.Range( 0, quad.Height ).Sum( row => quad.Atlas!.Alpha.AsSpan( (quad.AtlasY + row) * quad.Atlas.Width + quad.AtlasX, quad.Width ).ToArray().Count( value => value != 0 ) ) );
		var scale = overlay.Scale;
		Log.Trace( $"BF4 text readback: {textPixels} text pixels in {width}x{height} panel at UI scale {scale}; {inkPixels} atlas ink texels; max channel difference {maximumDifference}." );
		Require( textPixels >= inkPixels * scale * scale * 9 / 10 && inkPixels > 200, "BF4 text pixels are present in GPU readback" );
		Require( maximumDifference <= 2, "BF4 text readback matches the CPU composite" );
		var crop = new byte[width * height * 4];
		for ( var row = 0; row < height; ++row )
			Array.Copy( frame.Pixels, ((y + row) * frame.Width + x) * 4, crop, row * width * 4, width * 4 );
		using var image = Image.LoadPixelData<Bgra32>( crop, width, height );
		image.SaveAsPng( Path.Combine( Path.GetFullPath( "artifacts" ), "native-smoke-text.png" ) );
	}

	private string displaySummary = "";

	/// <summary>
	/// Rendertarget sizes against the pure size computation: swapchain = drawable pixels, world target
	/// = render-scaled internal size, output capture = output size.
	/// </summary>
	private void VerifyDisplaySizes()
	{
		var metrics = Render.Metrics;
		var scaling = Render.Scaling;
		var settings = Render.DisplaySettings;
		var swapchain = Device.MainSwapchain.Framebuffer;
		var expected = RenderScaling.Compute( metrics.PixelSize, settings.Upscale, settings.RenderScale, uint.MaxValue, Render.WorldScalingAllowed );
		Require( swapchain.Width == metrics.PixelSize.X && swapchain.Height == metrics.PixelSize.Y, "swapchain matches the drawable pixel size" );
		if ( Window.TestPixelScale > 1 )
			Require( metrics.PixelSize.X >= metrics.LogicalSize.X * Window.TestPixelScale && metrics.PixelSize.Y >= metrics.LogicalSize.Y * Window.TestPixelScale, "test pixel scale enlarges the drawable" );
		Require( expected.InternalSize.Equals( scaling.InternalSize ) && expected.Mode == scaling.Mode, "renderer uses the computed internal size" );
		var world = Render.ResolveColorTexture;
		Require( world.Width == scaling.InternalSize.X && world.Height == scaling.InternalSize.Y && Render.MultisampledFramebuffer.Width == world.Width, "world target has the internal size" );
		var output = Render.OutputCaptureTexture!;
		Require( output.Width == metrics.PixelSize.X && output.Height == metrics.PixelSize.Y, "output capture has the output size" );
		Require( Screen.UiScale == UiScaling.Resolve( settings.UiScale, metrics.PixelSize ), "UI scale follows the output size" );
		displaySummary = $"output {metrics}, world {world.Width}x{world.Height} ({scaling.Mode} {scaling.EffectivePercent}%), UI scale {Screen.UiScale}";
		Log.Trace( $"Display readback: {displaySummary}; swapchain {swapchain.Width}x{swapchain.Height}; {Render.OwnedTargetResourceCount} owned target resources." );
	}

	/// <summary>
	/// Projects the Totem cell centre to output pixels, converts to logical mouse units as SDL reports
	/// them, and requires the placement pick to return the same cell (no render-scale or DPI offset).
	/// </summary>
	private void VerifyPicking()
	{
		var offCentre = new Vector3( 10, 10, 0 );
		if ( level.OriginalPark != null )
		{
			var field = level.OriginalPark.Heightfield;
			Require( OriginalParkPlacement.TryGetCell( field, rideSite.X, rideSite.Y, out var x, out var y ), "Totem site is a terrain cell" );
			offCentre = OriginalParkPlacement.GetCellCenter( field, Math.Min( x + 4, field.CellCountX - 1 ), Math.Min( y + 3, field.CellCountZ - 1 ) );
		}
		VerifyPick( rideSite, "Totem cell" );
		VerifyPick( offCentre, "off-centre cell" );
	}

	private void VerifyPick( Vector3 cell, string label )
	{
		var metrics = Render.Metrics;
		var site = new System.Numerics.Vector3( cell.X, cell.Y, 0 );
		Require( ScreenMapping.TryProjectToPixel( site, Camera.ViewMatrix, Camera.ProjMatrix, metrics.PixelSize, out var pixel ), $"{label} projects onto the screen" );
		var mouse = metrics.PixelToLogical( pixel );
		Require( level.TryGetPlacementPosition( mouse, new Vector2( Screen.Size.X, Screen.Size.Y ), out var picked ), $"pick the {label}" );
		Log.Trace( $"Picking: {label} {cell} at output pixel ({pixel.X:F1}, {pixel.Y:F1}), mouse ({mouse.X:F1}, {mouse.Y:F1}) logical -> {picked}." );
		Require( picked.X == cell.X && picked.Y == cell.Y, $"picking returns the {label}" );
	}

	private int originalResourceCount;
	private IEnumerator<bool>? displayChanges;
	private bool displayChangesDone;

	/// <summary>Advances the runtime display-change sequence by one frame; true when it has finished.</summary>
	private bool StepDisplayChanges()
	{
		displayChanges ??= DisplayChangeSequence();
		return !displayChanges.MoveNext();
	}

	/// <summary>
	/// Switches render scale and method, resizes the window, lets an unconfirmed change revert on its
	/// timeout and toggles fullscreen (Alt+Enter path) at runtime. Every change must recreate the
	/// targets at the right size without growing the owned GPU resources.
	/// </summary>
	private IEnumerator<bool> DisplayChangeSequence()
	{
		var original = Render.DisplaySettings;
		originalResourceCount = Render.OwnedTargetResourceCount;
		var generation = Render.TargetGeneration;
		var originalLogical = Render.Metrics.LogicalSize;

		foreach ( var (mode, percent) in new[] { (UpscaleMode.Linear, 77), (UpscaleMode.Nearest, 59), (UpscaleMode.Native, 100) } )
		{
			Render.ChangeDisplaySettings( original with { Upscale = mode, RenderScale = percent } );
			for ( var frame = 0; frame < 3; frame++ )
				yield return true;
			VerifyApplied( percent );
		}

		if ( original.Mode == WindowMode.Windowed )
		{
			var target = new Point2( originalLogical.X - 64, originalLogical.Y - 36 );
			Render.ChangeDisplaySettings( Render.DisplaySettings with { Width = target.X, Height = target.Y } );
			foreach ( var wait in WaitFor( () => Render.Metrics.LogicalSize.Equals( target ), 5 ) )
				yield return wait;
			Require( Render.Metrics.LogicalSize.Equals( target ), "runtime window resize applies" );
			VerifyApplied( 100 );
			Render.ChangeDisplaySettings( Render.DisplaySettings with { Width = originalLogical.X, Height = originalLogical.Y } );
			foreach ( var wait in WaitFor( () => Render.Metrics.LogicalSize.Equals( originalLogical ), 5 ) )
				yield return wait;
			Require( Render.Metrics.LogicalSize.Equals( originalLogical ), "window size restored" );
		}

		// "Keep these settings?" flow: an unconfirmed change reverts on its own after the timeout.
		var display = (IDisplaySettings)Render;
		var beforeConfirmation = display.Current;
		DisplaySettings? reverted = null;
		Action<DisplaySettings> onReverted = settings => reverted = settings;
		display.Reverted += onReverted;
		display.ApplyWithConfirmation( beforeConfirmation with { Upscale = UpscaleMode.Linear, RenderScale = 50 }, TimeSpan.FromSeconds( 0.5 ) );
		for ( var frame = 0; frame < 3; frame++ )
			yield return true;
		Require( display.IsConfirmationPending, "unconfirmed change is pending" );
		VerifyApplied( 50 );
		foreach ( var wait in WaitFor( () => !display.IsConfirmationPending, 5 ) )
			yield return wait;
		for ( var frame = 0; frame < 3; frame++ )
			yield return true;
		display.Reverted -= onReverted;
		Require( reverted == beforeConfirmation && display.Current == beforeConfirmation, "unconfirmed change reverts after its timeout" );
		VerifyApplied( 100 );

		// Fullscreen toggle and back (Alt+Enter / F11 call the same method).
		var before = Render.Metrics.LogicalSize;
		Render.ToggleFullscreen();
		foreach ( var wait in WaitFor( () => !Render.Metrics.LogicalSize.Equals( before ), 10 ) )
			yield return wait;
		for ( var frame = 0; frame < 30; frame++ )
			yield return true;
		var toggled = Render.Metrics;
		Require( !toggled.LogicalSize.Equals( before ), "fullscreen toggle changes the output size" );
		VerifyApplied( 100 );
		Render.ToggleFullscreen();
		foreach ( var wait in WaitFor( () => Render.Metrics.LogicalSize.Equals( before ), 10 ) )
			yield return wait;
		for ( var frame = 0; frame < 30; frame++ )
			yield return true;
		Require( Render.Metrics.LogicalSize.Equals( before ), "fullscreen toggle returns to the previous size" );
		Log.Trace( $"Fullscreen toggle: {before.X}x{before.Y} -> {toggled} -> {Render.Metrics}." );

		Render.ChangeDisplaySettings( original );
		for ( var frame = 0; frame < 3; frame++ )
			yield return true;
		VerifyApplied( original.Upscale == UpscaleMode.Native ? 100 : original.RenderScale );
		Log.Trace( $"Display changes: {Render.TargetGeneration - generation} target recreations at runtime; {Render.OwnedTargetResourceCount} owned target resources before and after." );
	}

	/// <summary>Yields frames until the condition holds or the timeout passes.</summary>
	private static IEnumerable<bool> WaitFor( Func<bool> condition, double seconds )
	{
		var timer = System.Diagnostics.Stopwatch.StartNew();
		while ( !condition() && timer.Elapsed.TotalSeconds < seconds )
			yield return true;
	}

	private void VerifyApplied( int percent )
	{
		var scaling = Render.Scaling;
		var output = Render.Metrics.PixelSize;
		Require( scaling.EffectivePercent == percent, $"render scale {percent}% applies" );
		Require( Render.ResolveColorTexture.Width == RenderScaling.ScaleDimension( output.X, percent ) && Render.ResolveColorTexture.Height == RenderScaling.ScaleDimension( output.Y, percent ), $"world target follows {percent}%" );
		Require( Device.MainSwapchain.Framebuffer.Width == output.X && Render.OutputCaptureTexture!.Width == output.X, "output stays at the drawable size" );
		Require( Render.OwnedTargetResourceCount == originalResourceCount, "owned GPU targets do not grow" );
	}

	private static bool Throws( Action action )
	{
		try
		{
			action();
			return false;
		}
		catch ( InvalidOperationException )
		{
			return true;
		}
	}

	public void VerifyCompleted() => Require( completed, "complete all native smoke-test frames" );

	private static void Require( bool condition, string step )
	{
		if ( !condition )
			throw new InvalidOperationException( $"Native sandbox smoke test failed: {step}." );
	}

	public void Dispose()
	{
		watchdog.Dispose();
		Render.PostUpdate -= Update;
		SaveFileSystem = originalSaveFileSystem;
		Directory.Delete( temporaryDirectory, true );
	}
}
