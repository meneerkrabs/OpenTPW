using System.Diagnostics;
using ImGuiNET;
using Veldrid;
using Numerics = System.Numerics;

namespace OpenTPW;

/// <summary>
/// First-run setup (docs/SETUP.md): a small window that asks for the original game files before
/// any of them can be read, so it draws with ImGui instead of the game's own fonts. Pages: welcome
/// (with folders found automatically), game folder, optional CD for music and movies, done.
/// </summary>
internal sealed class SetupWizard : IDisposable
{
	public sealed record Result( string GamePath, string? CdPath );

	private enum Page { Welcome, GameFolder, Cd, Done }

	private readonly Window window;
	private readonly GraphicsDevice device;
	private readonly ImGuiRenderer imGui;
	private readonly CommandList commandList;
	private readonly IReadOnlyList<InstallationReport> detected;
	private readonly float scale;

	private Page page;
	private string gamePath;
	private string cdPath;
	private InstallationReport? gameReport;
	private InstallationReport? cdReport;
	private Task<string?>? picker;
	private Action<string>? pickerTarget;
	private string? dropped;
	private Result? result;
	private bool cancelled;

	private SetupWizard( SetupSettings saved, IReadOnlyList<InstallationReport> detected )
	{
		this.detected = detected;
		gamePath = saved.GamePath ?? detected.FirstOrDefault()?.Path ?? "";
		cdPath = saved.CdPath ?? "";
		if ( gamePath.Length > 0 )
			gameReport = GameInstallation.Inspect( gamePath );
		if ( cdPath.Length > 0 )
			cdReport = GameInstallation.Inspect( cdPath );

		window = new Window( 760, 560, "OpenTPW setup" );
		window.SdlWindow.DragDrop += drop => dropped = drop.File;
		device = Renderer.CreateDevice( window );
		var pixels = window.PixelSize;
		scale = Math.Max( 1f, pixels.X / (float)Math.Max( 1, window.Size.X ) );
		imGui = new ImGuiRenderer( device, device.MainSwapchain.Framebuffer.OutputDescription, pixels.X, pixels.Y );
		ImGui.GetIO().FontGlobalScale = scale * 1.25f;
		ImGui.GetStyle().ScaleAllSizes( scale );
		commandList = device.ResourceFactory.CreateCommandList();
	}

	/// <summary>Shows the wizard until the player finishes (result) or closes it (null).</summary>
	public static Result? Run( SetupSettings saved, IEnumerable<string> candidates )
	{
		var detected = candidates.Select( GameInstallation.Inspect ).Where( report => report.IsUsable )
			.DistinctBy( report => report.Path ).ToArray();
		using var wizard = new SetupWizard( saved, detected );
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
			if ( pixels.X != device.MainSwapchain.Framebuffer.Width || pixels.Y != device.MainSwapchain.Framebuffer.Height )
			{
				device.MainSwapchain.Resize( (uint)pixels.X, (uint)pixels.Y );
				imGui.WindowResized( pixels.X, pixels.Y );
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
		if ( picker is { IsCompleted: true } )
		{
			var chosen = picker.Status == TaskStatus.RanToCompletion ? picker.Result : null;
			if ( chosen != null )
				pickerTarget?.Invoke( chosen );
			picker = null;
			pickerTarget = null;
		}
		if ( dropped != null )
		{
			// A dropped file means its folder.
			var folder = Directory.Exists( dropped ) ? dropped : Path.GetDirectoryName( dropped ) ?? dropped;
			if ( page == Page.Cd )
				SetCdPath( folder );
			else
			{
				SetGamePath( folder );
				page = Page.GameFolder;
			}
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
			case Page.Cd: DrawCd(); break;
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
			ImGui.TextWrapped( "No copy of Theme Park World was found automatically." );
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
		Footer( canGoBack: true, nextLabel: "Next", canGoNext: gameReport is { IsUsable: true }, next: () => page = Page.Cd, back: () => page = Page.Welcome );
	}

	private void DrawCd()
	{
		Heading( "Music and movies (optional)" );
		ImGui.TextWrapped( "The CD holds the park music and the movies. If your game folder lacks them, choose the CD drive or a copy of the CD; you can skip this step." );
		ImGui.Spacing();
		FolderField( "##cd", ref cdPath, SetCdPath );
		if ( cdReport != null )
		{
			if ( cdReport.DataDirectory != null )
				Good( "This looks like the Theme Park World CD." );
			else
				Bad( "No Data folder found here." );
		}
		Footer( canGoBack: true, nextLabel: cdPath.Length == 0 ? "Skip" : "Next", canGoNext: cdPath.Length == 0 || cdReport?.DataDirectory != null,
			next: () => page = Page.Done, back: () => page = Page.GameFolder );
	}

	private void DrawDone()
	{
		Heading( "Ready" );
		ImGui.TextWrapped( $"Game files: {gameReport?.Path}" );
		if ( cdPath.Length > 0 )
			ImGui.TextWrapped( $"CD: {cdReport?.Path}" );
		if ( gameReport is { Languages.Count: > 0 } )
			ImGui.TextWrapped( $"Languages: {string.Join( ", ", gameReport.Languages )}" );
		ImGui.Spacing();
		ImGui.TextWrapped( "OpenTPW remembers these folders. Start it with --setup to change them later." );
		Footer( canGoBack: true, nextLabel: "Start OpenTPW", canGoNext: true,
			next: () => result = new Result( gameReport!.Path, cdPath.Length > 0 ? cdReport?.Path : null ), back: () => page = Page.Cd );
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
			var initial = Directory.Exists( path ) ? path : null;
			pickerTarget = set;
			picker = Task.Run( () => FolderPicker.Pick( "Choose the Theme Park World folder", initial ) );
		}
		ImGui.EndDisabled();
	}

	private void SetGamePath( string path )
	{
		gamePath = path;
		gameReport = path.Trim().Length > 0 ? GameInstallation.Inspect( path ) : null;
		// Picking the Data folder is corrected to its parent; show the folder that will be used.
		if ( gameReport is { IsUsable: true } && gameReport.Path != path )
			gamePath = gameReport.Path;
	}

	private void SetCdPath( string path )
	{
		cdPath = path;
		cdReport = path.Trim().Length > 0 ? GameInstallation.Inspect( path ) : null;
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
