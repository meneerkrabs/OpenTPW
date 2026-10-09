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
	/// <summary>Seconds before an unconfirmed display change is reverted.</summary>
	public float ConfirmSeconds { get; init; } = 15;
}

/// <summary>
/// The Game Options screen (UITEXT 314) in the original window style: original rows (screen
/// resolution, sound/music/speech/movie volume, popup help) and OpenTPW rows in the same style
/// (window mode, upscaling, render scale, interface scale, language). OK applies; display changes go
/// through the display settings' apply/confirm/revert flow with the original resolution messages.
/// </summary>
public static class OptionsScreen
{
	public static readonly int[] RenderScaleSteps = { 100, 95, 90, 85, 80, 77, 75, 70, 67, 65, 60, 59, 55, 50 };
	public static readonly float[] UiScaleSteps = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f };

	private static readonly (int Width, int Height, UIStrings Label)[] OriginalResolutionLabels =
	{
		(512, 384, UIStrings.Resolution512x384), (640, 480, UIStrings.Resolution640x480), (800, 600, UIStrings.Resolution800x600),
		(1024, 768, UIStrings.Resolution1024x768), (1280, 1024, UIStrings.Resolution1280x1024), (1600, 1200, UIStrings.Resolution1600x1200),
		(400, 300, UIStrings.Resolution400x300)
	};

	/// <summary>Original label (" 640 x 480") when the original table has the size, else the same format.</summary>
	public static string ResolutionLabel( UiStringTable strings, DisplayResolution resolution )
	{
		var match = OriginalResolutionLabels.FirstOrDefault( entry => entry.Width == resolution.Width && entry.Height == resolution.Height );
		return match.Width != 0 ? strings[match.Label] : $" {resolution.Width} x {resolution.Height}";
	}

	public static string RenderScaleLabel( UiStringTable strings, IDisplaySettings display, int percent ) =>
		percent == 100 ? strings.Extra( OpenTpwText.UpscaleNative )
		: display.RenderScalePresets.Contains( percent ) ? $" {percent}%" : $"{strings[UIStrings.Custom]} {percent}%";

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

	public static UiScreen Create( UiScreenStack stack, UiStringTable strings, OptionsServices services, Action closed )
	{
		var display = services.Display;
		var options = services.Options;
		var language = services.CurrentLanguage;
		var screen = new UiScreen( "options" );
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

		Row( "resolution", () => strings[UIStrings.ScreenResolution], () => ResolutionLabel( strings, display.Resolution ),
			direction => display.Resolution = Cycle( display.AvailableResolutions, display.Resolution, direction ) );
		Row( "windowMode", () => strings.Extra( OpenTpwText.WindowMode ), () => strings.Extra( display.WindowMode switch
		{
			DisplayWindowMode.Fullscreen => OpenTpwText.Fullscreen,
			DisplayWindowMode.Borderless => OpenTpwText.Borderless,
			_ => OpenTpwText.Windowed
		} ), direction => display.WindowMode = Cycle( Enum.GetValues<DisplayWindowMode>(), display.WindowMode, direction ) );
		Row( "upscaling", () => strings.Extra( OpenTpwText.Upscaling ), () => strings.Extra( display.UpscaleMethod switch
		{
			DisplayUpscaleMethod.Linear => OpenTpwText.UpscaleLinear,
			DisplayUpscaleMethod.Nearest => OpenTpwText.UpscaleNearest,
			_ => OpenTpwText.UpscaleNative
		} ), direction => display.UpscaleMethod = Cycle( Enum.GetValues<DisplayUpscaleMethod>(), display.UpscaleMethod, direction ) );
		Row( "renderScale", () => strings.Extra( OpenTpwText.RenderScale ), () => RenderScaleLabel( strings, display, display.RenderScalePercent ),
			direction => display.RenderScalePercent = Cycle( RenderScaleSteps, RenderScaleSteps.Contains( display.RenderScalePercent ) ? display.RenderScalePercent : 100, -direction ) );
		Row( "uiScale", () => strings.Extra( OpenTpwText.UiScale ), () => $" {MathF.Round( display.UiScale * 100 )}%",
			direction => display.UiScale = Cycle( UiScaleSteps, UiScaleSteps.Contains( display.UiScale ) ? display.UiScale : 1f, direction ) );
		Row( "effects", () => strings[UIStrings.SoundEffectsVolume], () => Volume( options.SoundEffectsVolume ), direction => options.SoundEffectsVolume = Step( options.SoundEffectsVolume, direction ) );
		Row( "music", () => strings[UIStrings.MusicVolume], () => Volume( options.MusicVolume ), direction => options.MusicVolume = Step( options.MusicVolume, direction ) );
		Row( "speech", () => strings[UIStrings.SpeechVolume], () => Volume( options.SpeechVolume ), direction => options.SpeechVolume = Step( options.SpeechVolume, direction ) );
		Row( "movie", () => strings[UIStrings.MovieVolume], () => Volume( options.MovieVolume ), direction => options.MovieVolume = Step( options.MovieVolume, direction ) );
		Row( "popupHelp", () => strings[UIStrings.PopupHelp], () => strings[options.PopupHelp ? UIStrings.Yes : UIStrings.No], _ => options.PopupHelp = !options.PopupHelp );
		Row( "language", () => strings.Extra( OpenTpwText.Language ),
			() => " " + (SupplementaryStrings.LanguageNames.TryGetValue( language, out var name ) ? name : language),
			direction => language = Cycle( services.Languages, language, direction ) );

		screen.Add( new UiLabel
		{
			Id = "effective",
			Text = () =>
			{
				var effective = display.EffectiveInternalSize;
				var output = display.OutputSize;
				var line = string.Format( strings.Extra( OpenTpwText.EffectiveSize ), effective.Width, effective.Height, output.Width, output.Height );
				return display.FallbackReason == null ? line : line + "\n" + string.Format( strings.Extra( OpenTpwText.Fallback ), display.FallbackReason );
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
			display.Revert();
			stack.Pop();
			closed();
		}
		void Accept()
		{
			services.SaveOptions();
			var languageChanged = !string.Equals( language, services.CurrentLanguage, StringComparison.OrdinalIgnoreCase );
			if ( languageChanged )
				services.SaveLanguage( language );
			var result = display.Apply();
			stack.Pop();
			if ( result == DisplayApplyResult.NeedsConfirmation )
				stack.Push( ConfirmDisplay( stack, strings, services, closed ) );
			else if ( result == DisplayApplyResult.RestartRequired || languageChanged )
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

	/// <summary>Original keep-this-setting question (UITEXT 400) with automatic revert (401) after the timeout.</summary>
	private static UiScreen ConfirmDisplay( UiScreenStack stack, UiStringTable strings, OptionsServices services, Action closed )
	{
		var remaining = services.ConfirmSeconds;
		var done = false;
		void Keep()
		{
			if ( done )
				return;
			done = true;
			services.Display.Confirm();
			stack.Pop();
			closed();
		}
		void Restore()
		{
			if ( done )
				return;
			done = true;
			services.Display.Revert();
			services.Display.Apply();
			stack.Pop();
			stack.Push( UiDialogs.Message( "restored", () => strings[UIStrings.ChangeScreenResolutionRestored], (() => strings.Extra( OpenTpwText.Back ), () => { stack.Pop(); closed(); }) ) );
		}
		var screen = UiDialogs.Message( "confirmDisplay", () => $"{strings[UIStrings.ChangeScreenResolution]}\n\n{Math.Ceiling( Math.Max( 0, remaining ) )}",
			(() => strings.Value( UIStrings.Yes ), Keep), (() => strings.Value( UIStrings.No ), Restore) );
		screen.Updating = context =>
		{
			remaining -= Math.Max( 0, context.Delta );
			if ( remaining <= 0 )
				Restore();
		};
		return screen;
	}
}
