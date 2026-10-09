using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>Services the options screen writes to.</summary>
public sealed class OptionsServices
{
	public required IDisplaySettings Display { get; init; }
	public required GameOptions Options { get; init; }
	public required IReadOnlyList<string> Languages { get; init; }
	public required string CurrentLanguage { get; init; }
	/// <summary>Persists <see cref="Options"/>.</summary>
	public Action SaveOptions { get; init; } = () => { };
	/// <summary>Persists the language for the next start.</summary>
	public Action<string> SaveLanguage { get; init; } = _ => { };
	/// <summary>Time to confirm a window size/mode change before it is reverted.</summary>
	public TimeSpan ConfirmTimeout { get; init; } = TimeSpan.FromSeconds( 15 );
}

/// <summary>
/// The Game Options screen (UITEXT 314) in the original window style: original rows (screen
/// resolution, sound/music/speech/movie volume, popup help) and OpenTPW rows in the same style
/// (window mode, upscaling, render scale, interface scale, language). Rows edit a pending copy of
/// the display settings; OK applies it through <see cref="IDisplaySettings"/>: window size/mode
/// changes use the keep-or-revert flow with the original messages (UITEXT 400 keep this setting?,
/// 401 original setting restored), other display changes apply directly. Back discards.
/// </summary>
public static class OptionsScreen
{
	// [EXT:upscaling] render-scale steps (presets 77/67/59/50 from the display slice plus 5% steps)
	public static readonly int[] RenderScaleSteps = { 100, 95, 90, 85, 80, 77, 75, 70, 67, 65, 60, 59, 55, 50 };

	private static readonly (int Width, int Height, UIStrings Label)[] OriginalResolutionLabels =
	{
		(512, 384, UIStrings.Resolution512x384), (640, 480, UIStrings.Resolution640x480), (800, 600, UIStrings.Resolution800x600),
		(1024, 768, UIStrings.Resolution1024x768), (1280, 1024, UIStrings.Resolution1280x1024), (1600, 1200, UIStrings.Resolution1600x1200),
		(400, 300, UIStrings.Resolution400x300)
	};

	/// <summary>Original label (" 640 x 480") when the original table has the size, else the same format.</summary>
	public static string ResolutionLabel( UiStringTable strings, Point2 resolution )
	{
		var match = OriginalResolutionLabels.FirstOrDefault( entry => entry.Width == resolution.X && entry.Height == resolution.Y );
		return match.Width != 0 ? strings[match.Label] : $" {resolution.X} x {resolution.Y}";
	}

	public static string RenderScaleLabel( UiStringTable strings, IDisplaySettings display, int percent ) =>
		percent >= 100 ? strings.Extra( OpenTpwText.UpscaleNative )
		: display.RenderScalePresets.Contains( percent ) ? $" {percent}%" : $"{strings[UIStrings.Custom]} {percent}%";

	public static string UiScaleLabel( UiStringTable strings, int scale ) =>
		scale <= 0 ? strings.Extra( OpenTpwText.Automatic ) : $" {scale}x";

	public static string WindowModeLabel( UiStringTable strings, WindowMode mode ) => strings.Extra( mode switch
	{
		WindowMode.Exclusive => OpenTpwText.Fullscreen,
		WindowMode.Borderless => OpenTpwText.Borderless,
		_ => OpenTpwText.Windowed
	} );

	public static string UpscaleLabel( UiStringTable strings, UpscaleMode mode ) => strings.Extra( mode switch
	{
		UpscaleMode.Linear => OpenTpwText.UpscaleLinear,
		UpscaleMode.Nearest => OpenTpwText.UpscaleNearest,
		_ => OpenTpwText.UpscaleNative
	} );

	public static T Cycle<T>( IReadOnlyList<T> values, T current, int direction )
	{
		if ( values.Count == 0 )
			return current;
		var index = -1;
		for ( var candidate = 0; candidate < values.Count; candidate++ )
		{
			if ( EqualityComparer<T>.Default.Equals( values[candidate], current ) )
				index = candidate;
		}
		if ( index < 0 )
			return values[0];
		return values[Math.Clamp( index + direction, 0, values.Count - 1 )];
	}

	private static bool SameSize( Point2 a, Point2 b ) => a.X == b.X && a.Y == b.Y;

	public static UiScreen Create( UiScreenStack stack, UiStringTable strings, OptionsServices services, Action closed )
	{
		var display = services.Display;
		var options = services.Options;
		var language = services.CurrentLanguage;
		var pending = display.Current;
		var screen = new UiScreen( "options" );
		// [APPROX:UI-013] options window size, row pitch 84, OK/Back placement — evidence needed: capture of the original options screen
		var window = UiDialogs.CenteredWindow( 1760, 1380 );
		UiDialogs.AddWindow( screen, window, "w_med", () => strings[UIStrings.GameOptions] );

		var rowTop = window.Y + 150;
		var rowIndex = 0;
		void Row( string id, Func<string> label, Func<string> value, Action<int> changed )
		{
			screen.Add( new UiOptionRow
			{
				Id = id,
				Label = label,
				Value = value,
				Changed = changed,
				Bounds = new UiRect( window.X + 90, rowTop + rowIndex++ * 84, window.Width - 330, 76 ),
				Anchor = UiAnchor.Center
			} );
		}
		static string Volume( int value ) => $" {value}";
		static int Step( int value, int direction ) => Math.Clamp( value + direction, 0, GameOptions.MaximumVolume );

		// [DATA:UITEXT.str:318,340-346] original row label and resolution labels
		Row( "resolution", () => strings[UIStrings.ScreenResolution], () => ResolutionLabel( strings, new Point2( pending.Width, pending.Height ) ), direction =>
		{
			var sizes = display.GetResolutions( pending.Mode );
			var index = sizes.ToList().FindIndex( size => SameSize( size, new Point2( pending.Width, pending.Height ) ) );
			var next = sizes.Count == 0 ? new Point2( pending.Width, pending.Height ) : sizes[index < 0 ? 0 : Math.Clamp( index + direction, 0, sizes.Count - 1 )];
			pending = pending with { Width = next.X, Height = next.Y };
		} );
		// [EXT:display] window mode, upscaling, render scale and interface scale rows are OpenTPW extensions
		Row( "windowMode", () => strings.Extra( OpenTpwText.WindowMode ), () => WindowModeLabel( strings, pending.Mode ),
			direction => pending = pending with { Mode = Cycle( Enum.GetValues<WindowMode>(), pending.Mode, direction ) } );
		Row( "upscaling", () => strings.Extra( OpenTpwText.Upscaling ), () => UpscaleLabel( strings, pending.Upscale ), direction =>
		{
			var mode = Cycle( Enum.GetValues<UpscaleMode>(), pending.Upscale, direction );
			var percent = mode == UpscaleMode.Native ? 100 : pending.RenderScale >= 100 ? RenderScaling.DefaultPreset : pending.RenderScale;
			pending = pending with { Upscale = mode, RenderScale = percent };
		} );
		Row( "renderScale", () => strings.Extra( OpenTpwText.RenderScale ), () => RenderScaleLabel( strings, display, pending.Upscale == UpscaleMode.Native ? 100 : pending.RenderScale ), direction =>
		{
			var steps = RenderScaleSteps.Where( step => step >= display.MinimumRenderScale && step <= display.MaximumRenderScale ).ToArray();
			var percent = Cycle( steps, steps.Contains( pending.RenderScale ) ? pending.RenderScale : 100, -direction );
			pending = pending with { RenderScale = percent, Upscale = percent >= 100 ? UpscaleMode.Native : pending.Upscale == UpscaleMode.Native ? UpscaleMode.Linear : pending.Upscale };
		} );
		Row( "uiScale", () => strings.Extra( OpenTpwText.UiScale ), () => UiScaleLabel( strings, pending.UiScale ),
			direction => pending = pending with { UiScale = Math.Clamp( pending.UiScale + direction, 0, display.MaximumUiScale ) } );
		Row( "effects", () => strings[UIStrings.SoundEffectsVolume], () => Volume( options.SoundEffectsVolume ), direction => options.SoundEffectsVolume = Step( options.SoundEffectsVolume, direction ) );
		Row( "music", () => strings[UIStrings.MusicVolume], () => Volume( options.MusicVolume ), direction => options.MusicVolume = Step( options.MusicVolume, direction ) );
		Row( "speech", () => strings[UIStrings.SpeechVolume], () => Volume( options.SpeechVolume ), direction => options.SpeechVolume = Step( options.SpeechVolume, direction ) );
		Row( "movie", () => strings[UIStrings.MovieVolume], () => Volume( options.MovieVolume ), direction => options.MovieVolume = Step( options.MovieVolume, direction ) );
		Row( "popupHelp", () => strings[UIStrings.PopupHelp], () => strings[options.PopupHelp ? UIStrings.Yes : UIStrings.No], _ => options.PopupHelp = !options.PopupHelp );
		// [EXT:language] language row (original installs had one language; OpenTPW reads CD overlays)
		Row( "language", () => strings.Extra( OpenTpwText.Language ),
			() => " " + (SupplementaryStrings.LanguageNames.TryGetValue( language, out var name ) ? name : language),
			direction => language = Cycle( services.Languages, language, direction ) );

		screen.Add( new UiLabel
		{
			Id = "effective",
			Text = () =>
			{
				var effective = display.Effective;
				var line = string.Format( strings.Extra( OpenTpwText.EffectiveSize ), effective.InternalSize.X, effective.InternalSize.Y, effective.OutputSize.X, effective.OutputSize.Y );
				var canvas = new UiCanvas( effective.OutputSize.X, effective.OutputSize.Y, display.EffectiveUiScale );
				if ( canvas.TextScale < canvas.UiScale )
					line += "\n" + string.Format( strings.Extra( OpenTpwText.Fallback ), $"{strings.Extra( OpenTpwText.UiScale )} {canvas.UiScale}x -> {canvas.TextScale}x" );
				var reason = effective.FallbackReason ?? display.Diagnostics.LastOrDefault();
				return reason == null ? line : line + "\n" + string.Format( strings.Extra( OpenTpwText.Fallback ), reason );
			},
			Font = fonts => fonts.Small,
			Wrap = true,
			Bounds = new UiRect( window.X + 120, rowTop + rowIndex * 84 + 4, window.Width - 600, 100 ),
			Anchor = UiAnchor.Center
		} );

		var original = (options.SoundEffectsVolume, options.MusicVolume, options.SpeechVolume, options.MovieVolume, options.PopupHelp);
		void Cancel()
		{
			(options.SoundEffectsVolume, options.MusicVolume, options.SpeechVolume, options.MovieVolume, options.PopupHelp) = original;
			stack.Pop();
			closed();
		}
		void Accept()
		{
			services.SaveOptions();
			var languageChanged = !string.Equals( language, services.CurrentLanguage, StringComparison.OrdinalIgnoreCase );
			if ( languageChanged )
				services.SaveLanguage( language );
			var current = display.Current;
			var needsConfirmation = pending.Width != current.Width || pending.Height != current.Height || pending.Mode != current.Mode;
			stack.Pop();
			if ( needsConfirmation )
			{
				display.ApplyWithConfirmation( pending, services.ConfirmTimeout );
				stack.Push( ConfirmDisplay( stack, strings, display, closed ) );
				return;
			}
			if ( pending != current )
				display.Apply( pending );
			if ( languageChanged )
				stack.Push( UiDialogs.Message( "restart", () => strings[UIStrings.RestartGame], (() => strings.Extra( OpenTpwText.Back ), () => { stack.Pop(); closed(); }) ) );
			else
				closed();
		}
		screen.Add( new UiButton
		{
			Id = "ok",
			Model = "b_okay",
			Help = strings.Help( 400 ),
			Clicked = Accept,
			Bounds = new UiRect( window.Right - 420, window.Bottom - 200, 120, 120 ),
			Anchor = UiAnchor.Center
		} );
		screen.Add( new UiButton
		{
			Id = "back",
			Text = () => strings.Extra( OpenTpwText.Back ),
			Help = strings.Help( 2 ),
			Clicked = Cancel,
			Bounds = new UiRect( window.X + 120, window.Bottom - 190, 420, 104 ),
			Anchor = UiAnchor.Center
		} );
		screen.Back = Cancel;
		screen.Focus( screen.FocusableElements.First() );
		return screen;
	}

	/// <summary>
	/// Original keep-this-setting question (UITEXT 400) with the display's own countdown; "No" or the
	/// display's timeout revert and show UITEXT 401 (original setting restored).
	/// </summary>
	private static UiScreen ConfirmDisplay( UiScreenStack stack, UiStringTable strings, IDisplaySettings display, Action closed )
	{
		var done = false;
		void ShowRestored()
		{
			stack.Pop();
			stack.Push( UiDialogs.Message( "restored", () => strings[UIStrings.ChangeScreenResolutionRestored], (() => strings.Extra( OpenTpwText.Back ), () => { stack.Pop(); closed(); }) ) );
		}
		void Keep()
		{
			if ( done )
				return;
			done = true;
			display.Confirm();
			stack.Pop();
			closed();
		}
		void Restore()
		{
			if ( done )
				return;
			done = true;
			display.Revert();
			ShowRestored();
		}
		var screen = UiDialogs.Message( "confirmDisplay", () => $"{strings[UIStrings.ChangeScreenResolution]}\n\n{display.ConfirmationSecondsRemaining}",
			(() => strings.Value( UIStrings.Yes ), Keep), (() => strings.Value( UIStrings.No ), Restore) );
		// The display reverts by itself after its timeout; follow it.
		screen.Updating = _ =>
		{
			if ( !done && !display.IsConfirmationPending )
			{
				done = true;
				ShowRestored();
			}
		};
		return screen;
	}
}
