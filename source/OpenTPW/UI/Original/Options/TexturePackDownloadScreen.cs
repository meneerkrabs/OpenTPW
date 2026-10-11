namespace OpenTPW.UI.Original;

/// <summary>A responsive download page reached through Options. The player can retry, or cancel and keep playing offline.</summary>
// [EXT:texture-pack] A release download, not an original-game dialog.
public static class TexturePackDownloadScreen
{
	private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

	/// <summary>True when Clarity needs installation; explicit originals, old/custom packs and local overrides do not download.</summary>
	public static bool NeedsDownload( string? name, bool installed, string? environmentOverride ) =>
		string.Equals( name, TexturePack.DefaultName, StringComparison.OrdinalIgnoreCase ) && !installed && string.IsNullOrEmpty( environmentOverride );

	/// <summary>Shows only when this release includes a download index and no local or bundled pack already satisfies it.</summary>
	public static bool TryShow( UiScreenStack stack, UiStringTable strings, string name, Action<string, bool?> apply )
	{
		if ( !NeedsDownload( name, TexturePack.IsInstalled(), Environment.GetEnvironmentVariable( "OPENTPW_TEXTURE_PACK" ) ) )
			return false;
		var directory = TexturePack.PackDirectory( TexturePack.DefaultName, TexturePack.BundledPacksDirectory() );
		if ( !File.Exists( Path.Combine( directory, TexturePackDownload.DistributionFileName ) ) )
			return false;
		stack.Push( Create( stack, strings, async ( progress, cancellation ) =>
		{
			var distribution = TexturePackDownload.LoadBundled( directory );
			await new TexturePackDownload( Client ).InstallAsync( distribution, TexturePack.PacksDirectory(), progress, cancellation );
		}, apply ) );
		return true;
	}

	/// <summary>The task seam also lets UI tests exercise retry/cancel without a window or network.</summary>
	internal static UiScreen Create( UiScreenStack stack, UiStringTable strings,
		Func<Action<TexturePackDownloadProgress>, CancellationToken, Task> install, Action<string, bool?> apply, Action<Task>? observeInstall = null )
	{
		var screen = new UiScreen( "texturePackDownload" );
		CancellationTokenSource? cancel = null;
		Task? task = null;
		TexturePackDownloadProgress? latest = null;
		var failed = false;
		var committed = false;
		UiButton? retry = null;
		string Text( OpenTpwText key ) => strings.Extra( key );

		void Start()
		{
			if ( screen.IsRemoved || task is { IsCompleted: false } ) return;
			cancel?.Dispose();
			cancel = new CancellationTokenSource();
			var token = cancel.Token;
			failed = false;
			if ( retry != null ) retry.Visible = false;
			Volatile.Write( ref latest, null );
			// Descriptor parsing, hashing, downloading and extraction all run away from the render thread.
			task = Task.Run( () => install( progress => Volatile.Write( ref latest, progress ), token ), token );
			observeInstall?.Invoke( task );
		}

		void Close( string choice, bool? enhancedInterface = null )
		{
			if ( screen.IsRemoved ) return;
			cancel?.Cancel();
			apply( choice, enhancedInterface );
			if ( stack.Top == screen ) stack.Pop();
		}

		screen.Back = () => Close( "" );
		screen.Removed = () =>
		{
			cancel?.Cancel();
			var lifetime = cancel;
			if ( task != null )
				_ = task.ContinueWith( finished => { _ = finished.Exception; lifetime?.Dispose(); }, TaskScheduler.Default );
			else lifetime?.Dispose();
		};
		screen.Add( new UiModelImage { Id = "window", Model = "f_screen", Bounds = new UiRect( 0, 0, 2048, 1536 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "title", Text = () => Text( OpenTpwText.TextureDownloadTitle ), Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = new UiRect( 430, 125, 1188, 140 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "explanation", Text = () => Text( OpenTpwText.TextureDownloadHelp ), Font = fonts => fonts.Label, Color = UiColors.Text, Align = UiAlign.Center, Fit = true, Bounds = new UiRect( 350, 360, 1348, 245 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel
		{
			Id = "progress", Text = () =>
			{
				if ( failed ) return Text( OpenTpwText.TextureDownloadFailed );
				var progress = Volatile.Read( ref latest );
				if ( progress == null ) return Text( OpenTpwText.TextureDownloadStarting );
				return progress.Phase == "download"
					? string.Format( Text( OpenTpwText.TextureDownloadBytes ), progress.Done / (1024 * 1024), progress.Total / (1024 * 1024) )
					: string.Format( Text( OpenTpwText.TextureDownloadVerify ), progress.Done, progress.Total );
			}, Font = fonts => fonts.Label, Color = UiColors.Text, Align = UiAlign.Center, Fit = true, Bounds = new UiRect( 400, 675, 1248, 140 ), Anchor = UiAnchor.Center
		} );
		retry = screen.Add( new UiButton { Id = "retry", Text = () => Text( OpenTpwText.TextureDownloadRetry ), Clicked = Start, Visible = false, Bounds = new UiRect( 550, 920, 948, 130 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "original", Text = () => Text( OpenTpwText.TextureDownloadOriginal ), Clicked = () => Close( "" ), Bounds = new UiRect( 390, 1110, 1268, 130 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "hd", Text = () => Text( OpenTpwText.TextureDownloadHd ), Clicked = () => Close( "", enhancedInterface: true ), Bounds = new UiRect( 390, 1270, 1268, 130 ), Anchor = UiAnchor.Center } );
		screen.DrawOverlay = context =>
		{
			var progress = Volatile.Read( ref latest );
			if ( progress == null || failed ) return;
			var bar = context.Canvas.Map( new UiRect( 430, 855, 1188, 25 ), UiAnchor.Center );
			context.Batch.AddRectangle( bar, UiColors.Shadow );
			context.Batch.AddRectangle( bar with { Width = bar.Width * Math.Clamp( progress.Done / (float)Math.Max( 1, progress.Total ), 0, 1 ) }, UiColors.Value );
		};
		screen.Updating = _ =>
		{
			retry.Visible = failed;
			if ( task?.IsCompleted != true || committed ) return;
			if ( task.IsCompletedSuccessfully )
			{
				committed = true;
				Close( TexturePack.DefaultName );
			}
			else if ( !failed )
			{
				failed = true;
				retry.Visible = true;
				Log?.Warning( $"Clarity texture download: {task.Exception?.GetBaseException().Message ?? "cancelled"}. Original textures remain available." );
			}
		};
		Start();
		screen.Focus( screen.Find( "original" ) );
		return screen;
	}
}
