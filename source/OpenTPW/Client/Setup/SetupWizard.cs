using System.Diagnostics;
using ImGuiNET;
using Veldrid;
using Numerics = System.Numerics;

namespace OpenTPW;

/// <summary>
/// First-run setup (docs/SETUP.md): a small window that asks for the original game files before
/// any of them can be read, so it draws with ImGui instead of the game's own fonts. Pages: welcome
/// (with folders found automatically), game folder, done. Everything else, such as the CD for music
/// and movies, is set later in the original-style Options > Game files screen.
/// </summary>
internal sealed class SetupWizard : IDisposable
{
	public sealed record Result( string GamePath, string? CdPath );

	private enum Page { Welcome, GameFolder, Done }

	private readonly Window window;
	private readonly GraphicsDevice device;
	private readonly ImGuiRenderer imGui;
	private readonly CommandList commandList;
	private IReadOnlyList<InstallationReport> detected;
	private readonly CancellationTokenSource lifetime = new();
	private Task<InstallationDiscoveryResult>? discovery;
	private bool discoveryTimedOut;
	private Task<InstallationDiscoveryResult>? inspection;
	private CancellationTokenSource? inspectionCancellation;
	private string? pendingInspection;
	private Point2 logicalSize;
	internal const int MinimumWidth = 520;
	internal const int MinimumHeight = 420;
	internal static float DpiScale( Point2 logical, Point2 pixels ) => Math.Max( 1f, pixels.X / (float)Math.Max( 1, logical.X ) );
	private float scale;

	private Page page;
	private string gamePath;
	private readonly string? savedCd;
	private InstallationReport? gameReport;
	private Task<InstallationDiscoveryResult>? picker;
	private Action<string>? pickerTarget;
	private string? dropped;
	private Result? result;
	private bool cancelled;

	private SetupWizard( SetupSettings saved, InstallationDiscoveryResult? initialDiscovery )
	{
		detected = initialDiscovery?.Reports ?? Array.Empty<InstallationReport>();
		discoveryTimedOut = initialDiscovery?.TimedOut == true;
		gamePath = saved.GamePath ?? detected.FirstOrDefault()?.Path ?? "";
		savedCd = saved.CdPath;
		if ( gamePath.Length > 0 )
			pendingInspection = gamePath;

		window = new Window( 760, 560, "OpenTPW setup" );
		SdlDisplay.SetMinimumSize( window.SdlWindow, MinimumWidth, MinimumHeight );
		logicalSize = window.Size;
		if ( initialDiscovery == null )
			discovery = InstallationDiscovery.SearchAsync( cancellation: lifetime.Token );
		window.SdlWindow.DragDrop += drop => dropped = drop.File;
		device = Renderer.CreateDevice( window );
		var pixels = window.PixelSize;
		scale = DpiScale( logicalSize, pixels );
		imGui = new ImGuiRenderer( device, device.MainSwapchain.Framebuffer.OutputDescription, pixels.X, pixels.Y );
		ImGui.GetIO().FontGlobalScale = scale * 1.25f;
		ImGui.GetStyle().ScaleAllSizes( scale );
		commandList = device.ResourceFactory.CreateCommandList();
	}

	/// <summary>Shows the wizard until the player finishes (result) or closes it (null).</summary>
	public static Result? Run( SetupSettings saved, InstallationDiscoveryResult? discovery = null )
	{
		using var wizard = new SetupWizard( saved, discovery );
		return wizard.Loop();
	}

	private Result? Loop()
	{
		var clock = Stopwatch.StartNew();
		var previous = clock.Elapsed.TotalSeconds;
		while ( window.SdlWindow.Exists && result == null && !cancelled )
		{
			var snapshot = window.SdlWindow.PumpEvents();
			if ( !window.SdlWindow.Exists )
				break;
			var pixels = window.PixelSize;
			if ( pixels.X <= 0 || pixels.Y <= 0 || window.Size.X <= 0 )
			{
				// Minimized: nothing to draw into.
				Thread.Sleep( 50 );
				continue;
			}
			if ( pixels.X != device.MainSwapchain.Framebuffer.Width || pixels.Y != device.MainSwapchain.Framebuffer.Height )
			{
				device.MainSwapchain.Resize( (uint)pixels.X, (uint)pixels.Y );
				imGui.WindowResized( pixels.X, pixels.Y );
			}
			// Logical size can change while the drawable stays fixed during a DPI transition.
			logicalSize = window.Size;
			var newScale = DpiScale( logicalSize, pixels );
			if ( Math.Abs( newScale - scale ) > 0.01f )
			{
				ImGui.GetStyle().ScaleAllSizes( newScale / scale );
				scale = newScale;
				ImGui.GetIO().FontGlobalScale = scale * 1.25f;
			}
			var now = clock.Elapsed.TotalSeconds;
			imGui.Update( (float)(now - previous), new ScaledInput( snapshot, scale ) );
			previous = now;

			ApplyPickerAndDrop();
			Draw( pixels );

			commandList.Begin();
			commandList.SetFramebuffer( device.MainSwapchain.Framebuffer );
			commandList.ClearColorTarget( 0, new RgbaFloat( 0.11f, 0.13f, 0.16f, 1f ) );
			imGui.Render( device, commandList );
			commandList.End();
			device.SubmitCommands( commandList );
			device.SwapBuffers( device.MainSwapchain );
		}
		return result;
	}

	private void ApplyPickerAndDrop()
	{
		if ( discovery is { IsCompleted: true } )
		{
			if ( discovery.IsCompletedSuccessfully )
			{
				detected = discovery.Result.Reports.Where( report => report.IsUsable ).DistinctBy( report => report.Path ).ToArray();
				discoveryTimedOut = discovery.Result.TimedOut;
			}
			discovery = null;
		}
		if ( inspection is { IsCompleted: true } )
		{
			if ( inspection.IsCompletedSuccessfully && inspectionCancellation?.IsCancellationRequested == false )
			{
				var report = inspection.Result.Reports.FirstOrDefault() ?? new InstallationReport( gamePath, null,
					Array.Empty<string>(), new[] { "This folder could not be inspected. Choose an available local folder or CD." }, Array.Empty<string>() );
				gameReport = report;
				if ( report.IsUsable ) gamePath = report.Path;
			}
			inspection = null;
			inspectionCancellation?.Dispose();
			inspectionCancellation = null;
		}
		if ( inspection == null && pendingInspection is { } pending )
		{
			pendingInspection = null;
			inspectionCancellation = CancellationTokenSource.CreateLinkedTokenSource( lifetime.Token );
			inspection = InstallationDiscovery.InspectAsync( pending, cancellation: inspectionCancellation.Token );
		}
		if ( picker is { IsCompleted: true } )
		{
			var chosen = picker.Status == TaskStatus.RanToCompletion ? picker.Result.Reports.FirstOrDefault()?.Path : null;
			if ( chosen != null )
				pickerTarget?.Invoke( chosen );
			picker = null;
			pickerTarget = null;
		}
		if ( dropped != null )
		{
			// A dropped file means its folder.
			SetGamePath( dropped );
			page = Page.GameFolder;
			dropped = null;
		}
	}

	private void Draw( Point2 pixels )
	{
		ImGui.SetNextWindowPos( Numerics.Vector2.Zero );
		ImGui.SetNextWindowSize( new Numerics.Vector2( pixels.X, pixels.Y ) );
		ImGui.Begin( "OpenTPW setup", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings );
		switch ( page )
		{
			case Page.Welcome: DrawWelcome(); break;
			case Page.GameFolder: DrawGameFolder(); break;
			case Page.Done: DrawDone(); break;
		}
		ImGui.End();
	}

	private void DrawWelcome()
	{
		Heading( "Welcome to OpenTPW" );
		ImGui.TextWrapped( "OpenTPW is a new engine for Theme Park World (1999). It needs the files of the original game, which it does not include: an installed copy, a copy of the CD or the CD itself." );
		ImGui.Spacing();
		if ( detected.Count > 0 )
		{
			ImGui.TextWrapped( detected.Count == 1 ? "Theme Park World was found here:" : "Theme Park World was found in these folders:" );
			foreach ( var report in detected )
			{
				ImGui.PushID( report.Path );
				if ( ImGui.Button( "Use" ) )
				{
					SetGamePath( report.Path );
					page = Page.GameFolder;
				}
				ImGui.SameLine();
				ImGui.TextUnformatted( report.Path );
				ImGui.PopID();
			}
		}
		else
			ImGui.TextWrapped( discovery != null ? "Looking for Theme Park World..." : discoveryTimedOut
				? "Automatic search could not finish. You can choose the game folder below."
				: "No copy of Theme Park World was found automatically." );
		ImGui.Spacing();
		Footer( canGoBack: false, nextLabel: "Choose a folder", canGoNext: true, next: () => page = Page.GameFolder );
	}

	private void DrawGameFolder()
	{
		Heading( "Where is Theme Park World?" );
		ImGui.TextWrapped( "Choose the folder that contains the game's Data folder: where it is installed, a copy of the CD or the CD drive. You can also drag the folder onto this window." );
		ImGui.Spacing();
		FolderField( "##game", ref gamePath, SetGamePath );
		Report( gameReport );
		Footer( canGoBack: true, nextLabel: "Next", canGoNext: gameReport is { IsUsable: true }, next: () => page = Page.Done, back: () => page = Page.Welcome );
	}

	private void DrawDone()
	{
		Heading( "Ready" );
		ImGui.TextWrapped( $"Game files: {gameReport?.Path}" );
		if ( gameReport is { Languages.Count: > 0 } )
			ImGui.TextWrapped( $"Languages: {string.Join( ", ", gameReport.Languages )}" );
		ImGui.Spacing();
		ImGui.TextWrapped( "OpenTPW remembers this folder. Change it, or add the CD for music and movies, later in Options > Game files." );
		Footer( canGoBack: true, nextLabel: "Start OpenTPW", canGoNext: true,
			next: () => result = new Result( gameReport!.Path, savedCd ), back: () => page = Page.GameFolder );
	}

	private void FolderField( string id, ref string path, Action<string> set )
	{
		var busy = picker != null;
		ImGui.SetNextItemWidth( ImGui.GetContentRegionAvail().X - (FolderPicker.IsAvailable ? 120 * scale : 0) );
		if ( ImGui.InputText( id, ref path, 1024 ) )
			set( path );
		if ( !FolderPicker.IsAvailable )
			return;
		ImGui.SameLine();
		ImGui.BeginDisabled( busy );
		if ( ImGui.Button( busy ? "Waiting..." : "Browse...", new Numerics.Vector2( 110 * scale, 0 ) ) )
		{
			var initial = gameReport?.Path;
			pickerTarget = set;
			picker = InstallationDiscovery.PickAsync( "Choose the Theme Park World folder", initial, lifetime.Token );
		}
		ImGui.EndDisabled();
	}

	private void SetGamePath( string path )
	{
		gamePath = path;
		gameReport = null;
		inspectionCancellation?.Cancel();
		pendingInspection = path.Trim().Length > 0 ? path : null;
	}

	private static void Report( InstallationReport? report )
	{
		if ( report == null )
			return;
		ImGui.Spacing();
		if ( report.IsUsable )
			Good( $"Theme Park World found ({string.Join( ", ", report.Languages )})." );
		foreach ( var problem in report.Problems )
			Bad( problem );
		foreach ( var warning in report.Warnings )
			ImGui.TextColored( new Numerics.Vector4( 0.95f, 0.78f, 0.3f, 1f ), "! " + warning );
	}

	private static void Good( string text )
	{
		ImGui.PushStyleColor( ImGuiCol.Text, new Numerics.Vector4( 0.45f, 0.85f, 0.45f, 1f ) );
		ImGui.TextWrapped( "OK  " + text );
		ImGui.PopStyleColor();
	}

	private static void Bad( string text )
	{
		ImGui.PushStyleColor( ImGuiCol.Text, new Numerics.Vector4( 0.95f, 0.45f, 0.4f, 1f ) );
		ImGui.TextWrapped( "X  " + text );
		ImGui.PopStyleColor();
	}

	private void Heading( string text )
	{
		ImGui.SetWindowFontScale( 1.4f );
		ImGui.TextUnformatted( text );
		ImGui.SetWindowFontScale( 1f );
		ImGui.Separator();
		ImGui.Spacing();
	}

	private void Footer( bool canGoBack, string nextLabel, bool canGoNext, Action next, Action? back = null )
	{
		var buttonHeight = ImGui.GetFrameHeightWithSpacing();
		ImGui.SetCursorPosY( ImGui.GetWindowHeight() - buttonHeight - 16 * scale );
		if ( ImGui.Button( "Quit", new Numerics.Vector2( 90 * scale, 0 ) ) )
			cancelled = true;
		var width = 150 * scale;
		ImGui.SameLine( ImGui.GetWindowWidth() - 2 * width - 24 * scale );
		ImGui.BeginDisabled( !canGoBack || picker != null );
		if ( ImGui.Button( "Back", new Numerics.Vector2( width, 0 ) ) )
			back?.Invoke();
		ImGui.EndDisabled();
		ImGui.SameLine();
		ImGui.BeginDisabled( !canGoNext || picker != null );
		if ( ImGui.Button( nextLabel, new Numerics.Vector2( width, 0 ) ) )
			next();
		ImGui.EndDisabled();
	}

	public void Dispose()
	{
		lifetime.Cancel();
		inspectionCancellation?.Cancel();
		lifetime.Dispose();
		commandList.Dispose();
		imGui.Dispose();
		device.Dispose();
		if ( window.SdlWindow.Exists )
			window.SdlWindow.Close();
	}

	/// <summary>ImGui works in framebuffer pixels; SDL reports the mouse in logical points.</summary>
	private sealed class ScaledInput( InputSnapshot inner, float scale ) : InputSnapshot
	{
		public IReadOnlyList<KeyEvent> KeyEvents => inner.KeyEvents;
		public IReadOnlyList<MouseEvent> MouseEvents => inner.MouseEvents;
		public IReadOnlyList<char> KeyCharPresses => inner.KeyCharPresses;
		public Numerics.Vector2 MousePosition => inner.MousePosition * scale;
		public float WheelDelta => inner.WheelDelta;
		public bool IsMouseDown( MouseButton button ) => inner.IsMouseDown( button );
	}
}
