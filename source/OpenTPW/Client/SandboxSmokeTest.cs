using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;

namespace OpenTPW;

internal sealed class SandboxSmokeTest : IDisposable
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

	public SandboxSmokeTest( Level level )
	{
		this.level = level;
		originalSaveFileSystem = SaveFileSystem;
		Directory.CreateDirectory( temporaryDirectory );
		SaveFileSystem = new BaseFileSystem( temporaryDirectory );
		if ( level.OriginalPark != null )
			rideSite = VerifyOriginalLevel( level.OriginalPark );
		Require( level.PlaceRide( rideSite ), "place original Totem" );
		level.PlacedRide!.Start();
	}

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
			if ( level.PlacedRide!.MotionHeight > 0 )
				motionFrame = frame;
			else
				Require( sinceOpened.Elapsed.TotalSeconds < MaximumSecondsUntilMotion, "original Totem script starts the ride motion" );
			return;
		}
		var step = frame - motionFrame;
		if ( step == 10 )
		{
			Require( level.PlacedRide!.IsAnimating, "script-triggered ANIM_Main plays the original Totem animation" );
			earlyPixels = CaptureFrame( "ride-early.png" ).Pixels;
			earlyPose = level.PlacedRide.NodeTransforms.ToArray();
			earlyTick = level.PlacedRide.AnimationTick;
		}
		if ( step == 30 )
		{
			Require( level.PlacedRide!.Script.State != RideVMState.Faulted && level.PlacedRide.Script[RideVariables.VAR_RUNNING] == 1, "original script reports the ride running" );
			var parkFrame = CaptureFrame( level.OriginalPark == null ? "park.png" : "original-park.png" );
			VerifyPoseChanged( level.PlacedRide, parkFrame.Pixels );
			VerifyText( parkFrame );
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
		if ( step == 90 )
		{
			Device.WaitForIdle();
			completed = true;
			Log.Trace( level.OriginalPark == null
				? $"Native sandbox smoke test passed: {frame} frames, original ride and RSE script, script-triggered motion, original animation readback, close, remove, isolated save/load, BF4 text and GPU readback."
				: $"Native original-level smoke test passed: {frame} frames, {level.OriginalPark.LevelName} terrain/import, MAP/save build rules, original ride and RSE script, animation readback, close, remove, BF4 text and GPU readback." );
			Render.Window.SdlWindow.Close();
		}
	}

	internal static (byte[] Pixels, int Width, int Height) CaptureFrame( string name )
	{
		var source = Render.ResolveColorTexture;
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
		Log.Trace( $"BF4 text readback: {textPixels} text pixels in {width}x{height} panel; {inkPixels} atlas ink texels; max channel difference {maximumDifference}." );
		Require( textPixels >= inkPixels * 9 / 10 && inkPixels > 200, "BF4 text pixels are present in GPU readback" );
		Require( maximumDifference <= 2, "BF4 text readback matches the CPU composite" );
		var crop = new byte[width * height * 4];
		for ( var row = 0; row < height; ++row )
			Array.Copy( frame.Pixels, ((y + row) * frame.Width + x) * 4, crop, row * width * 4, width * 4 );
		using var image = Image.LoadPixelData<Bgra32>( crop, width, height );
		image.SaveAsPng( Path.Combine( Path.GetFullPath( "artifacts" ), "native-smoke-text.png" ) );
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

	internal static void Require( bool condition, string step )
	{
		if ( !condition )
			throw new InvalidOperationException( $"Native sandbox smoke test failed: {step}." );
	}

	public void Dispose()
	{
		Render.PostUpdate -= Update;
		SaveFileSystem = originalSaveFileSystem;
		Directory.Delete( temporaryDirectory, true );
	}
}
