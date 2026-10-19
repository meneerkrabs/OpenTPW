using Veldrid;
using System.Diagnostics;

namespace OpenTPW;

/// <summary>
/// Frame structure (docs/UPSCALING-DESIGN.md): the 3D world renders into a 4x MSAA target at the
/// internal size (<see cref="Scaling"/>), resolves to <see cref="ResolveColorTexture"/>, and the blit
/// pass scales it to the swapchain at output size (drawable pixels). BF4 UI (<see cref="OnOverlayRender"/>)
/// and ImGui then draw at output size, so they never go through the world upscaler.
/// </summary>
public partial class Renderer : IDisplaySettings
{
	private long _lastFrame;
	public CommandList CommandList = null!;
	/// <summary>Uniform updates join this command list only between its Begin and End.</summary>
	internal bool IsFrameRecording { get; private set; }

	public Window Window;
	public ImGuiRenderer imGuiRenderer;

	public Action? PreUpdate;
	public Action? OnUpdate;
	public Action? PostUpdate;

	public Action? OnRender;
	/// <summary>Screen-space UI drawn at output size into <see cref="OverlayFramebuffer"/>.</summary>
	public Action? OnOverlayRender;

	/// <summary>Current display preferences; change them through <see cref="ChangeDisplaySettings"/>.</summary>
	public DisplaySettings DisplaySettings { get; private set; }
	/// <summary>Requested versus effective scaling, internal and output size, fallback reason.</summary>
	public RenderScaleResult Scaling { get; private set; }
	public DisplayMetrics Metrics { get; private set; }
	/// <summary>Display problems found while applying the settings (shown in the park panel).</summary>
	public List<string> DisplayDiagnostics { get; } = new();
	/// <summary>False for screens without a 3D world (movies): they always render at output size.</summary>
	public bool WorldScalingAllowed
	{
		get => worldScalingAllowed;
		set { worldScalingAllowed = value; displayDirty = true; }
	}
	/// <summary>Also composes each frame (world blit + BF4 UI, no ImGui) into <see cref="OutputCaptureTexture"/> for readback.</summary>
	public bool CaptureOutput
	{
		get => captureOutput;
		set { captureOutput = value; displayDirty = true; }
	}

	public Framebuffer MultisampledFramebuffer = null!;
	public Veldrid.Texture ResolveColorTexture = null!;
	public Veldrid.Texture? OutputCaptureTexture { get; private set; }
	/// <summary>The framebuffer the overlay pass is drawing into (swapchain or output capture).</summary>
	public Framebuffer OverlayFramebuffer { get; private set; } = null!;
	/// <summary>Where composed frames go: the swapchain, or the offscreen target of a hidden-window run.</summary>
	public PresentTarget Target { get; private set; } = null!;
	/// <summary>Output description of the present target; overlay pipelines use it.</summary>
	public OutputDescription OutputDescription => Target.OutputDescription;
	/// <summary>Number of rendertarget resources this renderer currently owns (must not grow across changes).</summary>
	public int OwnedTargetResourceCount => ownedTargets.Count;
	/// <summary>How often the size-dependent targets were recreated.</summary>
	public int TargetGeneration { get; private set; }

	private readonly string? settingsPath;
	private bool settingsChanged;
	private bool worldScalingAllowed = true;
	private bool captureOutput;
	private bool displayDirty = true;
	private readonly List<IDisposable> ownedTargets = new();
	private Framebuffer? outputCaptureFramebuffer;
	private uint maximumTextureSize;

	/// <summary>False with <c>--no-vsync</c>: frames are presented as fast as they are made (for measurements).</summary>
	internal static bool SyncToVerticalBlank = true;
	/// <summary>Set by <c>--frame-stats</c>.</summary>
	internal FrameStats? FrameStats;

	/// <summary>
	/// Frames the CPU may record while the GPU still reads earlier ones. Data the CPU writes straight into GPU
	/// memory during a frame (interface vertices) keeps one copy per slot; <see cref="FrameNumber"/> moves to the
	/// next slot only once the GPU has finished the frame that used it last.
	/// </summary>
	public const int FramesInFlight = 3;
	/// <summary>The frame being recorded; it counts up after each submission.</summary>
	public long FrameNumber { get; private set; }
	public static int SlotOf( long frameNumber ) => (int)(frameNumber % FramesInFlight);
	private Fence[] frameFences = null!;

	private Pipeline _blitPipeline = null!;
	private ResourceSet? _blitResourceSet;
	private ResourceLayout _blitResourceLayout = null!;

	/// <param name="settings">Validated display settings.</param>
	/// <param name="settingsPath">Where runtime changes are saved; null keeps them in memory (smoke tests).</param>
	public Renderer( DisplaySettings settings, string? settingsPath )
	{
		DisplaySettings = settings;
		this.settingsPath = settingsPath;
		Window = new( settings.Width, settings.Height, "Theme Park World", true, Window.OffscreenRequested );
		Window.OnResized = OnWindowResized;
		ApplyWindowMode();
		Window.Visible = true; // no-op for an offscreen window

		CreateGraphicsDevice();
		// Swap the buffers so that the screen isn't a mangled mess
		Target.Present();
		if ( Window.IsOffscreen )
			Log.Trace( $"Offscreen mode: window hidden (SDL flags {Window.SdlFlags}), frames render into a {Target.Width}x{Target.Height} offscreen framebuffer, never presented." );
		maximumTextureSize = QueryMaximumTextureSize();
		ApplyDisplayChanges();
		_ = AvailableResolutions;
		IDisplaySettings.Instance = this;
		var desktop = SdlDisplay.GetDesktopSize( SdlDisplay.GetWindowDisplayIndex( Window.SdlWindow ) );
		if ( settings.Mode == WindowMode.Windowed && desktop.X > 0 && (settings.Width > desktop.X || settings.Height > desktop.Y) )
		{
			var message = $"Window {settings.Width}x{settings.Height} is larger than the {desktop.X}x{desktop.Y} desktop; the system may shrink it. Use --fullscreen for the full display.";
			Log.Warning( message );
			DisplayDiagnostics.Add( message );
		}

		imGuiRenderer = new ImGuiRenderer( Device, OutputDescription, Window.Size.X, Window.Size.Y );
		ModKit.GlobalNamespace.ImGuiManager = imGuiRenderer;
		new Editor( imGuiRenderer, Device );

		CommandList = Device.ResourceFactory.CreateCommandList();
		// Slots not used yet start signaled; the first frame's own fence is reset like every later one.
		frameFences = Enumerable.Range( 0, FramesInFlight ).Select( slot => Device.ResourceFactory.CreateFence( signaled: slot != 0 ) ).ToArray();
		_lastFrame = Stopwatch.GetTimestamp();
	}

	public Renderer() : this( DisplaySettings.Default, null ) { }

	private uint QueryMaximumTextureSize()
	{
		if ( Device.GetPixelFormatSupport( PixelFormat.B8_G8_R8_A8_UNorm, TextureType.Texture2D, TextureUsage.RenderTarget, out var properties ) )
			return Math.Min( properties.MaxWidth, properties.MaxHeight );
		return 8192;
	}

	/// <summary>Replaces the display settings; targets are recreated at the next frame boundary.</summary>
	public void ChangeDisplaySettings( DisplaySettings settings )
	{
		var diagnostics = new List<string>();
		settings = settings.Validate( diagnostics );
		foreach ( var diagnostic in diagnostics )
			Log.Warning( diagnostic );
		var modeChanged = settings.Mode != DisplaySettings.Mode || (settings.Mode == WindowMode.Windowed && (settings.Width != DisplaySettings.Width || settings.Height != DisplaySettings.Height));
		DisplaySettings = settings;
		settingsChanged = true;
		if ( modeChanged )
			ApplyWindowMode();
		displayDirty = true;
	}

	/// <summary>Alt+Enter / F11: windowed versus the configured (or borderless) fullscreen.</summary>
	public void ToggleFullscreen()
	{
		var fullscreen = DisplaySettings.Mode == WindowMode.Windowed ? (lastFullscreenMode ?? WindowMode.Borderless) : WindowMode.Windowed;
		if ( DisplaySettings.Mode != WindowMode.Windowed )
			lastFullscreenMode = DisplaySettings.Mode;
		ChangeDisplaySettings( DisplaySettings with { Mode = fullscreen } );
	}

	private WindowMode? lastFullscreenMode;

	private (IReadOnlyList<Point2> Modes, Point2 Desktop)? displayModes;

	/// <summary>Sizes offered for the current window mode (see <see cref="GetResolutions"/>).</summary>
	public IReadOnlyList<Point2> AvailableResolutions => GetResolutions( DisplaySettings.Mode );

	public IReadOnlyList<Point2> GetResolutions( WindowMode mode )
	{
		var (modes, desktop) = displayModes ??= QueryDisplayModes();
		return DisplayModes.Build( modes, desktop, [new Point2( DisplaySettings.Width, DisplaySettings.Height ), Metrics.LogicalSize, Metrics.PixelSize], mode );
	}

	private (IReadOnlyList<Point2>, Point2) QueryDisplayModes()
	{
		var display = SdlDisplay.GetWindowDisplayIndex( Window.SdlWindow );
		var modes = SdlDisplay.GetDisplayModeSizes( display );
		var desktop = SdlDisplay.GetDesktopSize( display );
		var list = DisplayModes.Build( modes, desktop );
		Log.Trace( $"Display {display}: desktop {desktop.X}x{desktop.Y}, {modes.Count} SDL mode sizes; offering {list.Count} windowed sizes ({string.Join( ", ", list.Select( size => $"{size.X}x{size.Y}" ) )})." );
		return (modes, desktop);
	}

	// IDisplaySettings
	DisplaySettings IDisplaySettings.Current => DisplaySettings;
	public Point2 CurrentResolution => DisplaySettings.Mode == WindowMode.Windowed ? new Point2( DisplaySettings.Width, DisplaySettings.Height ) : Metrics.LogicalSize;
	public IReadOnlyList<int> RenderScalePresets => RenderScaling.Presets;
	public int MinimumRenderScale => DisplaySettings.MinimumRenderScale;
	public int MaximumRenderScale => DisplaySettings.MaximumRenderScale;
	public int MaximumUiScale => DisplaySettings.MaximumUiScale;
	public RenderScaleResult Effective => Scaling;
	public int EffectiveUiScale => Screen.UiScale;
	IReadOnlyList<string> IDisplaySettings.Diagnostics => DisplayDiagnostics;
	public event Action? Changed;
	public event Action<DisplaySettings>? Reverted;

	private readonly DisplayChangeConfirmation confirmation = new();
	private readonly Stopwatch clock = Stopwatch.StartNew();

	public void Apply( DisplaySettings settings )
	{
		confirmation.Clear();
		ChangeDisplaySettings( settings );
		SaveDisplaySettings();
	}

	public void ApplyWithConfirmation( DisplaySettings settings, TimeSpan timeout )
	{
		confirmation.Begin( DisplaySettings, clock.Elapsed, timeout );
		ChangeDisplaySettings( settings );
	}

	public bool IsConfirmationPending => confirmation.IsPending;
	public int ConfirmationSecondsRemaining => confirmation.SecondsRemaining( clock.Elapsed );

	public void Confirm()
	{
		if ( confirmation.Take() != null )
			SaveDisplaySettings();
	}

	public void Revert() => RevertTo( confirmation.Take() );

	private void RevertTo( DisplaySettings? previous )
	{
		if ( previous == null )
			return;
		ChangeDisplaySettings( previous );
		Log.Trace( $"Display change not confirmed; restored {previous.Describe()}." );
		Reverted?.Invoke( previous );
	}

	private void ApplyWindowMode()
	{
		if ( Window.IsOffscreen )
		{
			// No OS window state to change: keep the logical state (size and mode) the window would have had.
			var desktop = DisplaySettings.Mode == WindowMode.Borderless
				? SdlDisplay.GetDesktopSize( SdlDisplay.GetWindowDisplayIndex( Window.SdlWindow ) )
				: default;
			Window.SetVirtualSize( DisplaySettings.Mode == WindowMode.Borderless && desktop.X > 0 && desktop.Y > 0
				? desktop
				: new Point2( DisplaySettings.Width, DisplaySettings.Height ) );
			displayDirty = true;
			return;
		}
		var window = Window.SdlWindow;
		switch ( DisplaySettings.Mode )
		{
			case WindowMode.Exclusive:
				if ( SdlDisplay.TrySetExclusiveMode( window, new Point2( DisplaySettings.Width, DisplaySettings.Height ), out var reason ) )
				{
					window.WindowState = WindowState.FullScreen;
					break;
				}
				var message = $"Exclusive fullscreen unavailable ({reason}); using borderless fullscreen.";
				Log.Warning( message );
				DisplayDiagnostics.Add( message );
				window.WindowState = WindowState.BorderlessFullScreen;
				break;
			case WindowMode.Borderless:
				window.WindowState = WindowState.BorderlessFullScreen;
				break;
			default:
				if ( window.WindowState != WindowState.Normal && window.WindowState != WindowState.Hidden )
					window.WindowState = WindowState.Normal;
				window.Width = DisplaySettings.Width;
				window.Height = DisplaySettings.Height;
				break;
		}
		displayDirty = true;
	}

	private void CreateWorldTargets( Point2 size )
	{
		var colorTextureInfo = TextureDescription.Texture2D(
			(uint)size.X,
			(uint)size.Y,
			1,
			1,
			PixelFormat.B8_G8_R8_A8_UNorm,
			TextureUsage.RenderTarget,
			TextureSampleCount.Count4
		);

		var colorTexture = Own( Device.ResourceFactory.CreateTexture( colorTextureInfo ) );

		var depthTextureInfo = TextureDescription.Texture2D(
			(uint)size.X,
			(uint)size.Y,
			1,
			1,
			PixelFormat.D32_Float_S8_UInt,
			TextureUsage.DepthStencil,
			TextureSampleCount.Count4
		);

		var depthTexture = Own( Device.ResourceFactory.CreateTexture( depthTextureInfo ) );

		colorTextureInfo.SampleCount = TextureSampleCount.Count1;
		colorTextureInfo.Usage = TextureUsage.Sampled;

		ResolveColorTexture = Own( Device.ResourceFactory.CreateTexture( colorTextureInfo ) );

		var framebufferAttachmentInfo = new FramebufferAttachmentDescription( colorTexture, 0 );
		var depthAttachmentInfo = new FramebufferAttachmentDescription( depthTexture, 0 );
		var framebufferDescription = new FramebufferDescription()
		{
			ColorTargets = [framebufferAttachmentInfo],
			DepthTarget = depthAttachmentInfo
		};

		MultisampledFramebuffer = Own( Device.ResourceFactory.CreateFramebuffer( framebufferDescription ) );
	}

	private T Own<T>( T resource ) where T : IDisposable
	{
		ownedTargets.Add( resource );
		return resource;
	}

	/// <summary>Disposes every owned colour/depth texture, framebuffer and resource set (not only the framebuffer).</summary>
	private void DestroyTargets()
	{
		for ( var index = ownedTargets.Count - 1; index >= 0; index-- )
			ownedTargets[index].Dispose();
		ownedTargets.Clear();
		_blitResourceSet = null;
		outputCaptureFramebuffer = null;
		OutputCaptureTexture = null;
	}

	private void CreateBlitResourceSet()
	{
		if ( _blitResourceLayout == null || ResolveColorTexture == null )
			return;
		var sampler = Scaling.Mode == UpscaleMode.Nearest ? Device.PointSampler : Device.LinearSampler;
		_blitResourceSet = Own( Device.ResourceFactory.CreateResourceSet( new ResourceSetDescription(
			_blitResourceLayout,
			ResolveColorTexture,
			sampler
		) ) );
	}

	private void CreateOutputCapture( Point2 size )
	{
		var format = Target.ColorFormat;
		OutputCaptureTexture = Own( Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D(
			(uint)size.X, (uint)size.Y, 1, 1, format, TextureUsage.RenderTarget ) ) );
		outputCaptureFramebuffer = Own( Device.ResourceFactory.CreateFramebuffer( new FramebufferDescription( null, OutputCaptureTexture ) ) );
	}

	/// <summary>
	/// Applies pending window/DPI/settings changes at a frame boundary: waits for the GPU, resizes the
	/// swapchain, and replaces all size-dependent targets. Allocation failure at the scaled size falls
	/// back to native with a reason; failure at native size is an error.
	/// </summary>
	private void ApplyDisplayChanges()
	{
		displayDirty = false;
		var logical = Window.Size;
		var pixels = Window.PixelSize;
		Metrics = new DisplayMetrics( logical, pixels );
		Screen.UpdateFrom( logical, pixels );
		Screen.UiScale = UiScaling.Resolve( DisplaySettings.UiScale, pixels, Metrics.IntegerPixelDensity );
		var scaling = RenderScaling.Compute( pixels, DisplaySettings.Upscale, DisplaySettings.RenderScale, maximumTextureSize, worldScalingAllowed );
		if ( scaling.IsPaused )
		{
			// Minimised / zero-size: keep the current targets, render nothing, allocate nothing.
			Scaling = scaling;
			return;
		}
		var previous = Scaling;
		Device.WaitForIdle();
		if ( Target.Width != (uint)pixels.X || Target.Height != (uint)pixels.Y )
			Target.Resize( (uint)pixels.X, (uint)pixels.Y );
		DestroyTargets();
		try
		{
			CreateWorldTargets( scaling.InternalSize );
		}
		catch ( VeldridException exception ) when ( !scaling.InternalSize.Equals( pixels ) )
		{
			DestroyTargets();
			scaling = scaling with { Mode = UpscaleMode.Native, EffectivePercent = 100, InternalSize = pixels, FallbackReason = $"allocating {scaling.InternalSize.X}x{scaling.InternalSize.Y} failed: {exception.Message}" };
			try
			{
				CreateWorldTargets( pixels );
			}
			catch ( VeldridException nativeException )
			{
				throw new InvalidOperationException( $"Cannot allocate the {pixels.X}x{pixels.Y} world rendertargets: {nativeException.Message}", nativeException );
			}
		}
		Scaling = scaling;
		CreateBlitResourceSet();
		if ( captureOutput )
			CreateOutputCapture( pixels );
		TargetGeneration++;
		imGuiRenderer?.WindowResized( logical.X, logical.Y );
		if ( !previous.Equals( scaling ) )
			Log.Trace( $"Display: {DisplaySettings.Mode}, output {Metrics} via {SdlDisplay.PixelSizeSource}, UI scale {Screen.UiScale}. {scaling.Describe()}" );
		if ( scaling.FallbackReason != null && scaling.FallbackReason != previous.FallbackReason )
			Log.Warning( $"Display fallback: {scaling.FallbackReason}." );
		Changed?.Invoke();
	}

	/// <summary>Runs frames until the window closes, then saves changed display settings.</summary>
	public void Run()
	{
		Start();
		while ( Frame() )
		{
		}
		Stop();
	}

	/// <summary>
	/// Creates the per-run pipeline state. <see cref="Run"/> calls it; a host that drives frames
	/// itself (the browser, from requestAnimationFrame: docs/WEB.md) calls it once before <see cref="Frame"/>.
	/// </summary>
	public void Start()
	{
		var layoutDescription = new ResourceLayoutDescription(
			new ResourceLayoutElementDescription( "g_tInput", ResourceKind.TextureReadOnly, ShaderStages.Fragment ),
			new ResourceLayoutElementDescription( "g_sSampler", ResourceKind.Sampler, ShaderStages.Fragment )
		);

		_blitResourceLayout = Device.ResourceFactory.CreateResourceLayout( layoutDescription );

		// Create shader
		var shader = new Shader( "content/shaders/blit.shader" );

		var pipelineDescription = new GraphicsPipelineDescription(
			BlendStateDescription.SingleAlphaBlend,
			DepthStencilStateDescription.Disabled,
			RasterizerStateDescription.CullNone,
			PrimitiveTopology.TriangleList,
			new ShaderSetDescription(
				Array.Empty<VertexLayoutDescription>(),
				shader.ShaderProgram
			),
			[_blitResourceLayout],
			OutputDescription
		);

		_blitPipeline = Device.ResourceFactory.CreateGraphicsPipeline( pipelineDescription );
		displayDirty = true;
	}

	private readonly FrameLimiter frameLimiter = new();

	/// <summary>Frames updated and drawn since start; frames the cap skipped do not count.</summary>
	public long FramesDrawn { get; private set; }

	/// <summary>Frame rate of an offscreen run without a frame cap (pass <c>--no-vsync</c> for unpaced frames).</summary>
	public const int OffscreenFrameRate = 60;

	/// <summary>
	/// Updates and draws one frame; false once the window has closed. Under a frame cap (<see cref="DisplaySettings.FrameCap"/>) a frame that
	/// comes too early is skipped in the browser, which calls again at its next animation frame, and waited for on the desktop.
	/// </summary>
	public bool Frame()
	{
		if ( !Window.SdlWindow.Exists )
			return false;
		// An offscreen target is never presented, so nothing paces it: without a cap it would run thousands of frames per second and
		// wall-clock-timed work (loading, construction) would span far fewer frames than on a vsynced window. Pace it like a 60 Hz display.
		var cap = DisplaySettings.FrameCap > 0 ? DisplaySettings.FrameCap : Window.IsOffscreen && SyncToVerticalBlank ? OffscreenFrameRate : 0;
		// The browser paces frames itself and cannot block its only thread.
		if ( !OperatingSystem.IsBrowser() && frameLimiter.WaitSeconds( clock.Elapsed.TotalSeconds, cap ) is > 0 and var wait )
			Thread.Sleep( TimeSpan.FromSeconds( wait ) );
		if ( !frameLimiter.Due( clock.Elapsed.TotalSeconds, cap ) )
			return true;
		if ( Update() )
			FramesDrawn++;
		return Window.SdlWindow.Exists;
	}

	/// <summary>Ends a run started with <see cref="Start"/>.</summary>
	public void Stop()
	{
		FrameStats?.Dispose();
		SaveDisplaySettings();
	}

	/// <summary>Saves the confirmed settings (a pending, unconfirmed change saves what it would revert to).</summary>
	private void SaveDisplaySettings()
	{
		if ( settingsPath == null || !settingsChanged )
			return;
		var settings = confirmation.Previous ?? DisplaySettings;
		try
		{
			settings.Save( settingsPath );
			settingsChanged = false;
			Log.Trace( $"Display settings saved to {settingsPath}: {settings.Describe()}." );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			Log.Warning( $"Display settings not saved ({exception.Message})." );
		}
	}

	private void PreRender()
	{
		foreach ( var shader in Asset.All.OfType<Shader>().Where( x => x.IsDirty ) )
		{
			shader.Recompile();
		}

		CommandList.Begin();
		IsFrameRecording = true;
	}

	private void PostRender()
	{
		CommandList.SetFramebuffer( MultisampledFramebuffer ); // Use MSAA framebuffer
		CommandList.SetViewport( 0, new Viewport( 0, 0, MultisampledFramebuffer.Width, MultisampledFramebuffer.Height, 0, 1 ) );
		CommandList.SetFullViewports();
		CommandList.SetFullScissorRects();
		CommandList.ClearDepthStencil( 1 );
		CommandList.ClearColorTarget( 0, RgbaFloat.Black );

		// Render level to MSAA buffer
		CommandList.PushDebugGroup( "Main Render" );
		var renderStart = FrameStats.Begin();
		using ( FrameMarks.Measure( "draw.world" ) )
			OnRender?.Invoke();
		FrameStats.EndRender( renderStart );
		CommandList.PopDebugGroup();

		// Resolve MSAA to non-MSAA texture
		CommandList.ResolveTexture( MultisampledFramebuffer.ColorTargets[0].Target, ResolveColorTexture );

		// Scale to output size and draw the BF4 UI at output size
		if ( outputCaptureFramebuffer != null )
			Compose( outputCaptureFramebuffer );
		Compose( Target.Framebuffer );

		// ImGui works in logical units; scale its clip rectangles to the drawable pixels.
		ImGuiNET.ImGui.GetIO().DisplayFramebufferScale = Metrics.PixelsPerLogical;
		Editor.Instance?.Render( CommandList );

		CommandList.End();
		IsFrameRecording = false;

		var fence = frameFences[SlotOf( FrameNumber )];
		var submitStart = FrameStats.Begin();
		using ( FrameMarks.Measure( "draw.submit" ) )
		{
			Device.SubmitCommands( CommandList, fence );
			Target.Present();
		}
		FrameStats.EndSubmit( submitStart );
		FrameStats?.FrameEnded();
		AdvanceFrame();
	}

	/// <summary>Moves to the next frame slot, waiting until the GPU no longer reads the frame that used it before.</summary>
	private void AdvanceFrame()
	{
		FrameNumber++;
		var fence = frameFences[SlotOf( FrameNumber )];
		var fenceStart = FrameStats.Begin();
		Device.WaitForFence( fence );
		Device.ResetFence( fence );
		FrameStats.EndFence( fenceStart );
	}

	private void Compose( Framebuffer target )
	{
		CommandList.PushDebugGroup( "Compose" );
		CommandList.SetFramebuffer( target );
		CommandList.SetFullViewports();
		CommandList.SetFullScissorRects();
		CommandList.ClearColorTarget( 0, RgbaFloat.Black );
		CommandList.SetPipeline( _blitPipeline );
		CommandList.SetGraphicsResourceSet( 0, _blitResourceSet );
		CommandList.Draw( 3, 1, 0, 0 );
		OverlayFramebuffer = target;
		var overlayStart = FrameStats.Begin();
		using ( FrameMarks.Measure( "draw.overlay" ) )
			OnOverlayRender?.Invoke();
		FrameStats.EndOverlay( overlayStart );
		CommandList.PopDebugGroup();
	}

	/// <summary>One update and draw; false when the window closed or drawing is paused, so nothing was drawn.</summary>
	private bool Update()
	{
		var totalStart = FrameStats.Begin();
		try
		{
			return UpdateFrame();
		}
		finally
		{
			FrameStats.EndTotal( totalStart );
		}
	}

	private bool UpdateFrame()
	{
		var currentFrame = Stopwatch.GetTimestamp();
		float deltaTime = (float)Stopwatch.GetElapsedTime( _lastFrame, currentFrame ).TotalSeconds;
		_lastFrame = currentFrame;

		InputSnapshot inputSnapshot = Window.SdlWindow.PumpEvents();
		if ( !Window.SdlWindow.Exists )
			return false;

		Time.Update( deltaTime );
		Input.UpdateFrom( inputSnapshot );

		if ( IsFullscreenTogglePressed( inputSnapshot ) )
			ToggleFullscreen();
		RevertTo( confirmation.Poll( clock.Elapsed ) );

		// Safe frame boundary: the previous frame is submitted and nothing is recorded yet.
		if ( displayDirty || !Window.PixelSize.Equals( Metrics.PixelSize ) || !Window.Size.Equals( Metrics.LogicalSize ) )
			ApplyDisplayChanges();
		if ( Scaling.IsPaused || _blitResourceSet == null )
		{
			// The browser paces frames itself and cannot block its only thread.
			if ( !OperatingSystem.IsBrowser() )
				Thread.Sleep( 16 );
			return false;
		}

		if ( Input.Pressed( InputButton.EditorToggle ) )
			Editor.Instance.shouldRender = !Editor.Instance.shouldRender;
		if ( Editor.Instance.shouldRender )
			Editor.Instance.UpdateFrom( inputSnapshot );
		else
			imGuiRenderer.Update( Math.Clamp( deltaTime, 0.001f, 0.1f ), inputSnapshot );

		try
		{
			using ( FrameMarks.Measure( "frame.pre" ) )
			{
				PreRender();
				PreUpdate?.Invoke();
			}
			var updateStart = FrameStats.Begin();
			using ( FrameMarks.Measure( "frame.update" ) )
				OnUpdate?.Invoke();
			FrameStats.EndUpdate( updateStart );
			using ( FrameMarks.Measure( "frame.draw" ) )
				PostRender();
		}
		finally
		{
			// A failing update/draw must not send subsequent uploads to an abandoned frame.
			IsFrameRecording = false;
		}
		using ( FrameMarks.Measure( "frame.post" ) )
		{
			PostUpdate?.Invoke();
			ProcessDeletionQueue();
		}
		return true;
	}

	private static bool IsFullscreenTogglePressed( InputSnapshot snapshot )
	{
		foreach ( var key in snapshot.KeyEvents )
		{
			if ( !key.Down || key.Repeat )
				continue;
			if ( key.Key == Key.F11 || ((key.Key == Key.Enter || key.Key == Key.KeypadEnter) && (key.Modifiers & ModifierKeys.Alt) != 0) )
				return true;
		}
		return false;
	}

	private void CreateGraphicsDevice()
	{
		Device = CreateDevice( Window );
		var pixels = Window.PixelSize;
		Target = Window.IsOffscreen
			? new OffscreenPresentTarget( Device, (uint)pixels.X, (uint)pixels.Y )
			: new SwapchainPresentTarget( Device );
		Log.Trace( $"Graphics backend: {Device.BackendType}; process: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}" );
	}

	/// <summary>Metal on macOS, Direct3D 11 on Windows, Vulkan elsewhere, with readable errors when the platform cannot start one.</summary>
	internal static GraphicsDevice CreateDevice( Window window )
	{
		var options = new GraphicsDeviceOptions()
		{
			PreferStandardClipSpaceYDirection = true,
			PreferDepthRangeZeroToOne = true,
			SwapchainDepthFormat = null,
			SwapchainSrgbFormat = false,
			SyncToVerticalBlank = SyncToVerticalBlank,
			HasMainSwapchain = true
		};

		SwapchainSource swapchainSource;
		try
		{
			swapchainSource = Veldrid.StartupUtilities.VeldridStartup.GetSwapchainSource( window.SdlWindow );
		}
		catch ( PlatformNotSupportedException exception ) when ( OperatingSystem.IsLinux() )
		{
			throw new PlatformNotSupportedException( "SDL opened no X11 or Wayland window (is DISPLAY or WAYLAND_DISPLAY set?). Headless runs need a virtual display such as xvfb-run.", exception );
		}
		var pixels = window.PixelSize;
		var description = new SwapchainDescription( swapchainSource, (uint)Math.Max( 1, pixels.X ), (uint)Math.Max( 1, pixels.Y ), options.SwapchainDepthFormat, options.SyncToVerticalBlank, options.SwapchainSrgbFormat );
		try
		{
			return OperatingSystem.IsMacOS()
				? GraphicsDevice.CreateMetal( options, description )
				: OperatingSystem.IsWindows()
					? GraphicsDevice.CreateD3D11( options, description )
					: GraphicsDevice.CreateVulkan( options, description );
		}
		catch ( Exception exception ) when ( OperatingSystem.IsLinux() && exception is TypeInitializationException { TypeName: "Vulkan.VulkanNative" } or VeldridException )
		{
			// No loader (libvulkan.so.1) or no installed driver/ICD (no VK_KHR_surface).
			throw new PlatformNotSupportedException( $"Vulkan could not be started ({exception.Message}). Install the Vulkan loader and a driver, e.g. libvulkan1 and mesa-vulkan-drivers.", exception );
		}
	}

	/// <summary>Window resize/fullscreen/DPI events: remembered and applied at the next frame boundary.</summary>
	public void OnWindowResized( Point2 newSize )
	{
		displayDirty = true;
		if ( DisplaySettings.Mode == WindowMode.Windowed && Window.SdlWindow.WindowState == WindowState.Normal
			&& newSize.X >= DisplaySettings.MinimumWindowWidth && newSize.Y >= DisplaySettings.MinimumWindowHeight
			&& (newSize.X != DisplaySettings.Width || newSize.Y != DisplaySettings.Height) )
		{
			// Remember user-resized windowed sizes.
			DisplaySettings = DisplaySettings with { Width = newSize.X, Height = newSize.Y };
			settingsChanged = true;
		}
	}

	public void ImmediateSubmit( Action<CommandList> action )
	{
		var commandList = Device.ResourceFactory.CreateCommandList();
		commandList.Begin();

		action( commandList );

		commandList.End();
		Device.SubmitCommands( commandList );

		ScheduleDelete( commandList.Dispose );
	}

	/// <summary>Records per-draw uniforms in order, retaining immediate upload outside a frame.</summary>
	internal void UpdateUniform<T>( DeviceBuffer buffer, T value ) where T : unmanaged
	{
		if ( IsFrameRecording )
			CommandList.UpdateBuffer( buffer, 0, ref value );
		else
			SubmitUniform( buffer, value );
	}

	/// <summary>
	/// [EXT:point-lights] Records the first <paramref name="sizeInBytes"/> bytes of <paramref name="value"/> as a per-draw uniform upload: a block followed by the draw's point
	/// lights, uploaded in one call like the block alone (the lights ride in the same upload, never a second one).
	/// </summary>
	internal void UpdateUniform<T>( DeviceBuffer buffer, ref T value, uint sizeInBytes ) where T : unmanaged
	{
		if ( IsFrameRecording )
		{
			CommandList.UpdateBuffer( buffer, 0, ref value, sizeInBytes );
			return;
		}
		var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes( new ReadOnlySpan<T>( in value ) )[..(int)sizeInBytes].ToArray();
		ImmediateSubmit( commands => commands.UpdateBuffer( buffer, 0, bytes ) );
	}

	// Keep the rare immediate-upload closure out of the per-draw allocation path.
	private void SubmitUniform<T>( DeviceBuffer buffer, T value ) where T : unmanaged =>
		ImmediateSubmit( commands => commands.UpdateBuffer( buffer, 0, value ) );
}
