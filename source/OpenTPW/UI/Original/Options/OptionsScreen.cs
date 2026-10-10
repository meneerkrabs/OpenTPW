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
	/// <summary>Graphics settings (enhanced textures row); null hides the row.</summary>
	public IGraphicsSettings? Graphics { get; init; }
	/// <summary>True when a usable local texture pack exists (docs/TEXTURE-PACKS.md).</summary>
	public bool TexturePackAvailable { get; init; }
}

/// <summary>
/// The Game Options screen (UITEXT 314) laid out like the original (options table of the Mac build,
/// authored 2048x1536 coordinates): 3D card rendering and videocard (fixed, disabled), screen resolution,
/// graphics quality and audio quality sliders, sound effects / music / speech / movie volume sliders with
/// mute toggles, and the Advisor, Tutorial, Popup help, Confirmations, RMB cancel, Rotation and Scroll
/// toggles. OK (b_okay) applies, Cancel (b_exit), Escape and right click discard. Everything OpenTPW adds
/// (window mode, upscaling, render and interface scale, enhanced textures, language, game files) is on the
/// separate OpenTPW page, which edits the same pending state: its Back returns here with the edits kept
/// pending, and OK on this page applies both pages. Window size/mode changes use the keep-or-revert flow
/// with the original messages (UITEXT 400 keep this setting?, 401 original setting restored).
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

	/// <summary>Edits shared by the original page and the OpenTPW page until OK applies them.</summary>
	private sealed class OptionsState
	{
		public required DisplaySettings Display;
		public required string Language;
		public bool EnhancedTextures;
		public GraphicsPreset Preset;
	}

	/// <summary>Vertical distance between OpenTPW-page rows (virtual units).</summary>
	public const int RowPitch = 80;

	/// <summary>Quality steps of the graphics slider: Low/Medium/High (as far as the original files exist), Enhanced, and Custom while it is current.</summary>
	public static IReadOnlyList<GraphicsPreset> QualitySteps( IGraphicsSettings graphics ) =>
		graphics.Presets.Where( preset => preset != GraphicsPreset.Custom || graphics.Current.Preset == GraphicsPreset.Custom ).OrderBy( preset => (int)preset ).ToArray();

	public static string QualityLabel( UiStringTable strings, GraphicsPreset preset ) => preset switch
	{
		GraphicsPreset.Low => strings[UIStrings.Low],
		GraphicsPreset.Medium => strings[UIStrings.Medium],
		GraphicsPreset.High => strings[UIStrings.High],
		GraphicsPreset.Custom => strings[UIStrings.Custom],
		// [EXT:COMPAT-GFX-ENHANCED] the Enhanced preset is not in the original game; it is the slider's last step
		_ => " " + strings.Extra( OpenTpwText.EnhancedQuality )
	};

	/// <summary>Adds the original page pieces (panels, dark labels in one shared size, toggles, sliders) to a screen.</summary>
	private sealed class PageBuilder
	{
		private readonly UiScreen screen;
		public PageBuilder( UiScreen screen ) => this.screen = screen;
		public UiLabelGroup Group { get; } = new();

		public static UiRect Rect( float left, float top, float right, float bottom ) => new( left, top, right - left, bottom - top );

		public void Frame( Func<string> title )
		{
			// [DATA:ui.wad:f_screen] the full-screen frame with the tiled wave background
			screen.Add( new UiModelImage { Id = "window", Model = "f_screen", Bounds = new UiRect( 0, 0, 2048, 1536 ), Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel
			{
				Id = "title",
				Text = title,
				Font = fonts => fonts.Title,
				Color = UiColors.Title,
				Align = UiAlign.Center,
				Fit = true,
				// [DATA:options table 0x4b0dc:120020] title text rectangle
				Bounds = new UiRect( 788, 46, 472, 80 ),
				Anchor = UiAnchor.Center
			} );
		}

		public void Panel( string id, string model, UiRect rect ) =>
			screen.Add( new UiModelImage { Id = id + "Panel", Model = model, Bounds = rect, Anchor = UiAnchor.Center } );

		public void Label( string id, UiRect rect, Func<string> text, Func<IEnumerable<string>>? variants = null )
		{
			var label = screen.Add( new UiLabel
			{
				Id = id + "Label",
				Text = text,
				Variants = variants,
				Font = fonts => fonts.Label,
				Color = UiColors.OptionText,
				Shadow = false,
				Group = Group,
				Bounds = rect,
				Anchor = UiAnchor.Center
			} );
			Group.Members.Add( label );
		}

		// b_on: normal/hilite frames show ON, the down frame OFF; b_on2 is a plain press button
		public UiButton Toggle( string id, string model, UiRect rect, Func<bool> isOn, Action toggled, bool enabled = true, Action<int>? adjusted = null ) => screen.Add( new UiButton
		{
			Id = id,
			Model = model,
			Selected = model == "b_on" ? () => !isOn() : null,
			Clicked = toggled,
			Adjusted = adjusted,
			WheelAdjusts = adjusted != null,
			Enabled = enabled,
			Bounds = rect,
			Anchor = UiAnchor.Center
		} );

		public void Slider( string id, UiRect track, UiRect hit, UiRect knob, Func<int> steps, Func<int> value, Action<int> changed, bool enabled = true ) => screen.Add( new UiSlider
		{
			Id = id,
			Track = track,
			KnobSize = new System.Numerics.Vector2( knob.Width, knob.Height ),
			KnobTop = knob.Y,
			Steps = steps,
			Value = value,
			Changed = changed,
			Enabled = enabled,
			Bounds = hit,
			Anchor = UiAnchor.Center
		} );
	}

	private static bool SameSize( Point2 a, Point2 b ) => a.X == b.X && a.Y == b.Y;

	public static UiScreen Create( UiScreenStack stack, UiStringTable strings, OptionsServices services, Action closed )
	{
		var display = services.Display;
		var options = services.Options;
		var graphics = services.Graphics;
		var state = new OptionsState
		{
			Display = display.Current,
			Language = services.CurrentLanguage,
			EnhancedTextures = graphics?.Current.EnhancedTextures ?? false,
			Preset = graphics?.Current.Preset ?? GraphicsPreset.High
		};
		var snapshot = options.Clone();
		var screen = new UiScreen( "options" );
		var page = new PageBuilder( screen );
		page.Frame( () => strings[UIStrings.GameOptions] );
		static UiRect Rect( float left, float top, float right, float bottom ) => PageBuilder.Rect( left, top, right, bottom );
		void Panel( string id, string model, UiRect rect ) => page.Panel( id, model, rect );
		void Label( string id, UiRect rect, Func<string> text, Func<IEnumerable<string>>? variants = null ) => page.Label( id, rect, text, variants );
		void Toggle( string id, string model, UiRect rect, Func<bool> isOn, Action toggled, bool enabled = true ) => page.Toggle( id, model, rect, isOn, toggled, enabled );
		void Slider( string id, UiRect track, UiRect hit, UiRect knob, Func<int> steps, Func<int> value, Action<int> changed, bool enabled = true ) => page.Slider( id, track, hit, knob, steps, value, changed, enabled );
		static string Percent( int volume ) => $" {volume * 10} %";

		// [DATA:options table 0x4b0dc] control rectangles of the original page; meanings from the supplied PC capture
		// Top left: 3D card rendering and videocard. OpenTPW always renders on the GPU of the window.
		// [APPROX:UI-038] 3D card rendering, videocard and audio quality are shown fixed (disabled) — OpenTPW has no software renderer, video card or audio quality choice
		Panel( "gpu", "f_optpanel", Rect( 57, 167, 668, 316 ) );
		Label( "gpu", Rect( 124, 218, 424, 263 ), () => strings[UIStrings.GPURendering] );
		Toggle( "gpu", "b_on2", Rect( 521, 190, 624, 292 ), () => true, () => { }, false );
		Panel( "videocard", "f_optpanel", Rect( 675, 167, 1286, 316 ) );
		Label( "videocard", Rect( 739, 218, 1039, 263 ), () => strings[UIStrings.Videocard] + strings[UIStrings.Primary] );
		Toggle( "videocard", "b_on2", Rect( 1140, 190, 1242, 292 ), () => true, () => { }, false );

		// Slider rows (f_optpanel3): every row has the same offsets from its panel top.
		void SliderRow( string id, string model, float top, float panelLeft, float panelRight, float panelBottom, float labelTop, float labelBottom, float trackTop, float trackBottom,
			float hitTop, float hitBottom, float knobTop, float knobBottom, Func<string> label, Func<int> steps, Func<int> value, Action<int> changed, bool enabled = true, Func<IEnumerable<string>>? variants = null )
		{
			Panel( id, model, Rect( panelLeft, top, panelRight, panelBottom ) );
			Label( id, Rect( 117, labelTop, 706, labelBottom ), label, variants );
			Slider( id, Rect( 990, trackTop, 1248, trackBottom ), Rect( 962, hitTop, 1281, hitBottom ), Rect( 1023, knobTop, 1090, knobBottom ), steps, value, changed, enabled );
		}
		Func<IReadOnlyList<Point2>> sizes = () => display.GetResolutions( state.Display.Mode );
		SliderRow( "resolution", "f_optpanel3", 323, 57, 1289, 472, 375, 420, 361, 432, 331, 466, 364, 431,
			() => strings[UIStrings.ScreenResolution] + ResolutionLabel( strings, new Point2( state.Display.Width, state.Display.Height ) ),
			() => sizes().Count,
			() => Math.Max( 0, sizes().ToList().FindIndex( size => SameSize( size, new Point2( state.Display.Width, state.Display.Height ) ) ) ),
			index =>
			{
				var list = sizes();
				if ( index >= 0 && index < list.Count )
					state.Display = state.Display with { Width = list[index].X, Height = list[index].Y };
			}, true, () => sizes().Select( size => strings[UIStrings.ScreenResolution] + ResolutionLabel( strings, size ) ) );
		var steps = graphics == null ? Array.Empty<GraphicsPreset>() : QualitySteps( graphics );
		SliderRow( "quality", "f_optpanel3", 479, 57, 1289, 628, 532, 576, 518, 589, 487, 623, 520, 588,
			() => strings[UIStrings.GraphicsQuality] + QualityLabel( strings, state.Preset ),
			() => steps.Count,
			() => Math.Max( 0, steps.ToList().IndexOf( state.Preset ) ),
			index => state.Preset = steps[index], steps.Count > 1, () => steps.Select( preset => strings[UIStrings.GraphicsQuality] + QualityLabel( strings, preset ) ).Append( strings[UIStrings.GraphicsQuality] + QualityLabel( strings, state.Preset ) ) );
		SliderRow( "audio", "f_optpanel3", 638, 57, 1289, 787, 691, 735, 677, 748, 646, 782, 679, 747,
			() => strings[UIStrings.AudioQuality] + Percent( GameOptions.MaximumVolume ),
			() => GameOptions.MaximumVolume + 1, () => GameOptions.MaximumVolume, _ => { }, false );

		// Volume rows (f_optpanel2) with a mute toggle (b_on).
		void VolumeRow( string id, UIStrings text, float panelTop, float panelBottom, float toggleTop, float toggleBottom, float trackTop, float trackBottom,
			float hitTop, float hitBottom, float knobTop, float knobBottom, float labelTop, float labelBottom, Func<int> get, Action<int> set, Func<bool> isOn, Action<bool> setOn )
		{
			SliderRow( id, "f_optpanel2", panelTop, 52, 1289, panelBottom, labelTop, labelBottom, trackTop, trackBottom, hitTop, hitBottom, knobTop, knobBottom,
				() => strings[text] + Percent( get() ), () => GameOptions.MaximumVolume + 1, get, set, true, () => new[] { strings[text] + Percent( GameOptions.MaximumVolume ) } );
			Toggle( id + "On", "b_on", Rect( 801, toggleTop, 945, toggleBottom ), isOn, () => setOn( !isOn() ) );
		}
		VolumeRow( "effects", UIStrings.SoundEffectsVolume, 838, 988, 862, 964, 877, 948, 845, 981, 880, 947, 891, 936,
			() => options.SoundEffectsVolume, value => options.SoundEffectsVolume = value, () => options.SoundEffectsOn, on => options.SoundEffectsOn = on );
		VolumeRow( "music", UIStrings.MusicVolume, 994, 1144, 1018, 1120, 1033, 1104, 1004, 1140, 1035, 1103, 1047, 1091,
			() => options.MusicVolume, value => options.MusicVolume = value, () => options.MusicOn, on => options.MusicOn = on );
		VolumeRow( "speech", UIStrings.SpeechVolume, 1151, 1301, 1175, 1277, 1190, 1261, 1157, 1292, 1192, 1260, 1204, 1248,
			() => options.SpeechVolume, value => options.SpeechVolume = value, () => options.SpeechOn, on => options.SpeechOn = on );
		VolumeRow( "movie", UIStrings.MovieVolume, 1307, 1457, 1331, 1433, 1346, 1417, 1313, 1448, 1348, 1416, 1360, 1404,
			() => options.MovieVolume, value => options.MovieVolume = value, () => options.MovieOn, on => options.MovieOn = on );

		// Right column.
		string OnOff( bool on ) => strings[on ? UIStrings.On : UIStrings.Off];
		void OnRow( string id, UIStrings text, float top, float bottom, float labelTop, float labelBottom, float toggleTop, float toggleBottom, Func<bool> get, Action<bool> set )
		{
			Panel( id, "f_optpanel", Rect( 1331, top, 1964, bottom ) );
			Label( id, Rect( 1401, labelTop, 1701, labelBottom ), () => strings[text] + OnOff( get() ), () => new[] { strings[text] + OnOff( true ), strings[text] + OnOff( false ) } );
			Toggle( id, "b_on", Rect( 1786, toggleTop, 1930, toggleBottom ), get, () => set( !get() ) );
		}
		OnRow( "advisor", UIStrings.Advisor, 167, 316, 215, 260, 190, 292, () => options.Advisor, on => options.Advisor = on );
		OnRow( "tutorial", UIStrings.Tutorial, 323, 472, 371, 416, 346, 449, () => options.Tutorial, on => options.Tutorial = on );
		OnRow( "popupHelp", UIStrings.PopupHelp, 479, 628, 528, 573, 503, 605, () => options.PopupHelp, on => options.PopupHelp = on );
		OnRow( "confirmations", UIStrings.Confirmations, 681, 830, 732, 777, 705, 807, () => options.Confirmations, on => options.Confirmations = on );
		OnRow( "rmbCancel", UIStrings.RmbCancel, 837, 986, 888, 932, 861, 963, () => options.RmbCancel, on => options.RmbCancel = on );
		Panel( "rotation", "f_optpanel", Rect( 1331, 994, 1964, 1143 ) );
		Label( "rotation", Rect( 1401, 1044, 1701, 1089 ), () => strings[UIStrings.Rotation] + strings[options.Rotation == RotationMode.Smooth ? UIStrings.Smooth : UIStrings.Rotation90Degs],
			() => new[] { strings[UIStrings.Rotation] + strings[UIStrings.Smooth], strings[UIStrings.Rotation] + strings[UIStrings.Rotation90Degs] } );
		Toggle( "rotation", "b_on2", Rect( 1807, 1017, 1910, 1119 ), () => true, () => options.Rotation = options.Rotation == RotationMode.Smooth ? RotationMode.Ninety : RotationMode.Smooth );
		Panel( "scroll", "f_optpanel", Rect( 1331, 1150, 1964, 1299 ) );
		Label( "scroll", Rect( 1401, 1200, 1701, 1245 ), () => strings[UIStrings.Scroll] + strings[options.Scroll == ScrollMode.Pushscroll ? UIStrings.Pushscroll : UIStrings.RightButton],
			() => new[] { strings[UIStrings.Scroll] + strings[UIStrings.Pushscroll], strings[UIStrings.Scroll] + strings[UIStrings.RightButton] } );
		Toggle( "scroll", "b_on2", Rect( 1807, 1173, 1910, 1276 ), () => true, () => options.Scroll = options.Scroll == ScrollMode.Pushscroll ? ScrollMode.RightButton : ScrollMode.Pushscroll );

		void Cancel()
		{
			options.CopyFrom( snapshot );
			stack.Pop();
			closed();
		}
		void Accept()
		{
			services.SaveOptions();
			var languageChanged = !string.Equals( state.Language, services.CurrentLanguage, StringComparison.OrdinalIgnoreCase );
			if ( languageChanged )
				services.SaveLanguage( state.Language );
			var texturesChanged = graphics != null && state.EnhancedTextures != graphics.Current.EnhancedTextures;
			var presetChanged = graphics != null && steps.Count > 0 && state.Preset != graphics.Current.Preset;
			var graphicsRestart = false;
			if ( texturesChanged || presetChanged )
			{
				graphics!.Apply( graphics.Current with { Preset = presetChanged ? state.Preset : graphics.Current.Preset, EnhancedTextures = state.EnhancedTextures } );
				graphicsRestart = texturesChanged || graphics.RestartRequired;
			}
			var pending = state.Display;
			var current = display.Current;
			var needsConfirmation = pending.Width != current.Width || pending.Height != current.Height || pending.Mode != current.Mode;
			// Language and graphics changes may need a restart; tell the player after any display confirmation.
			void Finish()
			{
				if ( languageChanged || graphicsRestart )
					stack.Push( UiDialogs.Message( "restart", () => strings[UIStrings.RestartGame], (() => strings.Extra( OpenTpwText.Back ), () => { stack.Pop(); closed(); }) ) );
				else
					closed();
			}
			stack.Pop();
			if ( needsConfirmation )
			{
				display.ApplyWithConfirmation( pending, services.ConfirmTimeout );
				stack.Push( ConfirmDisplay( stack, strings, display, Finish ) );
				return;
			}
			if ( pending != current )
				display.Apply( pending );
			Finish();
		}
		// [DATA:options table 0x4b0dc:120031,OK,Cancel] bottom-right panel with the OK and Cancel buttons
		screen.Add( new UiModelImage { Id = "buttonsPanel", Model = "!f_plain", Bounds = Rect( 1742, 1321, 1947, 1443 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "ok", Model = "b_okay", Help = strings.Help( 400 ), Clicked = Accept, Bounds = Rect( 1763, 1340, 1846, 1424 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "cancel", Model = "b_exit", Help = strings.Help( 2 ), Clicked = Cancel, Bounds = Rect( 1853, 1340, 1936, 1424 ), Anchor = UiAnchor.Center } );
		// [EXT:opentpw-page] button to the OpenTPW page, in the free area left of the OK panel
		screen.Add( new UiButton
		{
			Id = "openTpw",
			Text = () => strings.Extra( OpenTpwText.OpenTpwPage ),
			Clicked = () => stack.Push( CreateOpenTpwPage( stack, strings, services, state ) ),
			Bounds = Rect( 1331, 1330, 1730, 1434 ),
			Anchor = UiAnchor.Center
		} );
		screen.Back = Cancel;
		screen.Focus( screen.FocusableElements.First() );
		return screen;
	}

	/// <summary>Next value of <paramref name="values"/> after <paramref name="current"/> in <paramref name="direction"/>, wrapping around.</summary>
	public static T CycleWrap<T>( IReadOnlyList<T> values, T current, int direction )
	{
		if ( values.Count == 0 )
			return current;
		var index = -1;
		for ( var candidate = 0; candidate < values.Count; candidate++ )
		{
			if ( EqualityComparer<T>.Default.Equals( values[candidate], current ) )
				index = candidate;
		}
		return index < 0 ? values[0] : values[((index + direction) % values.Count + values.Count) % values.Count];
	}

	/// <summary>
	/// The OpenTPW page ([EXT:opentpw-page]): everything OpenTPW adds to the original options, built from the
	/// original pieces like the main page: an <c>f_screen</c> page whose <c>f_optpanel</c> bars carry dark labels
	/// in one shared size and a <c>b_on2</c> button that cycles the value (click or wheel up/Right forward, wheel
	/// down/Left back, both wrapping). It edits the shared pending state; Back (the <c>b_okay</c> button or Escape)
	/// returns to the original page with the edits still pending, and OK there applies them.
	/// </summary>
	private static UiScreen CreateOpenTpwPage( UiScreenStack stack, UiStringTable strings, OptionsServices services, OptionsState state )
	{
		var display = services.Display;
		var graphics = services.Graphics;
		var screen = new UiScreen( "openTpwOptions" );
		var page = new PageBuilder( screen );
		page.Frame( () => strings.Extra( OpenTpwText.OpenTpwPage ) );
		static UiRect Rect( float left, float top, float right, float bottom ) => PageBuilder.Rect( left, top, right, bottom );

		// Two columns with the authored right column's panel size and pitch (x 1331..1964; the left column starts at 57).
		var rowIndex = 0;
		void Row( string id, Func<string> text, Func<IEnumerable<string>> variants, Action<int> change )
		{
			var column = rowIndex / 3;
			var left = column == 0 ? 57f : 1331f;
			var top = 167f + rowIndex % 3 * 156f;
			rowIndex++;
			page.Panel( id, "f_optpanel", Rect( left, top, left + 633, top + 149 ) );
			page.Label( id, Rect( left + 70, top + 50, left + 370, top + 95 ), text, variants );
			page.Toggle( id, "b_on2", Rect( left + 476, top + 23, left + 579, top + 125 ), () => true, () => change( 1 ), true, change );
		}
		// [EXT:display] window mode, upscaling, render scale and interface scale rows are OpenTPW extensions
		string Prefix( OpenTpwText key ) => strings.Extra( key );
		var renderSteps = RenderScaleSteps.Where( step => step >= display.MinimumRenderScale && step <= display.MaximumRenderScale ).ToArray();
		Row( "windowMode", () => Prefix( OpenTpwText.WindowMode ) + WindowModeLabel( strings, state.Display.Mode ),
			() => Enum.GetValues<WindowMode>().Select( mode => Prefix( OpenTpwText.WindowMode ) + WindowModeLabel( strings, mode ) ),
			direction =>
			{
				var mode = CycleWrap( Enum.GetValues<WindowMode>(), state.Display.Mode, direction );
				// A size the new mode does not offer (exclusive full screen lists only display modes) snaps to the
				// nearest one it does, so the resolution slider always shows a selectable size.
				var offered = display.GetResolutions( mode );
				var size = new Point2( state.Display.Width, state.Display.Height );
				if ( offered.Count > 0 && !offered.Any( candidate => SameSize( candidate, size ) ) )
					size = offered.MinBy( candidate => Math.Abs( (long)candidate.X * candidate.Y - (long)size.X * size.Y ) );
				state.Display = state.Display with { Mode = mode, Width = size.X, Height = size.Y };
			} );
		Row( "upscaling", () => Prefix( OpenTpwText.Upscaling ) + UpscaleLabel( strings, state.Display.Upscale ),
			() => Enum.GetValues<UpscaleMode>().Select( mode => Prefix( OpenTpwText.Upscaling ) + UpscaleLabel( strings, mode ) ),
			direction =>
			{
				var mode = CycleWrap( Enum.GetValues<UpscaleMode>(), state.Display.Upscale, direction );
				var percent = mode == UpscaleMode.Native ? 100 : state.Display.RenderScale >= 100 ? RenderScaling.DefaultPreset : state.Display.RenderScale;
				state.Display = state.Display with { Upscale = mode, RenderScale = percent };
			} );
		Row( "renderScale", () => Prefix( OpenTpwText.RenderScale ) + RenderScaleLabel( strings, display, state.Display.Upscale == UpscaleMode.Native ? 100 : state.Display.RenderScale ),
			() => renderSteps.Select( step => Prefix( OpenTpwText.RenderScale ) + RenderScaleLabel( strings, display, step ) ),
			direction =>
			{
				var percent = CycleWrap( renderSteps, renderSteps.Contains( state.Display.RenderScale ) ? state.Display.RenderScale : 100, -direction );
				state.Display = state.Display with { RenderScale = percent, Upscale = percent >= 100 ? UpscaleMode.Native : state.Display.Upscale == UpscaleMode.Native ? UpscaleMode.Linear : state.Display.Upscale };
			} );
		Row( "uiScale", () => Prefix( OpenTpwText.UiScale ) + UiScaleLabel( strings, state.Display.UiScale ),
			() => Enumerable.Range( 0, display.MaximumUiScale + 1 ).Select( scale => Prefix( OpenTpwText.UiScale ) + UiScaleLabel( strings, scale ) ),
			direction => state.Display = state.Display with { UiScale = ((state.Display.UiScale + direction) % (display.MaximumUiScale + 1) + display.MaximumUiScale + 1) % (display.MaximumUiScale + 1) } );
		// [EXT:texture-pack] optional locally built upscaled textures; off unless a pack exists and the player turns it on
		string Textures() => services.TexturePackAvailable || state.EnhancedTextures ? strings[state.EnhancedTextures ? UIStrings.Yes : UIStrings.No] : " " + strings.Extra( OpenTpwText.TexturePackMissing );
		if ( graphics != null )
			Row( "enhancedTextures", () => Prefix( OpenTpwText.EnhancedTextures ) + Textures(),
				() => new[] { Prefix( OpenTpwText.EnhancedTextures ) + strings[UIStrings.Yes], Prefix( OpenTpwText.EnhancedTextures ) + strings[UIStrings.No], Prefix( OpenTpwText.EnhancedTextures ) + " " + strings.Extra( OpenTpwText.TexturePackMissing ) },
				_ => { if ( services.TexturePackAvailable || state.EnhancedTextures ) state.EnhancedTextures = !state.EnhancedTextures; } );
		// [EXT:language] language row (original installs had one language; OpenTPW reads CD overlays)
		string LanguageName( string language ) => " " + (SupplementaryStrings.LanguageNames.TryGetValue( language, out var name ) ? name : language);
		Row( "language", () => Prefix( OpenTpwText.Language ) + LanguageName( state.Language ),
			() => services.Languages.Select( language => Prefix( OpenTpwText.Language ) + LanguageName( language ) ),
			direction => state.Language = CycleWrap( services.Languages, state.Language, direction ) );

		// Effective size and fallback reason as small dark text in the free area below the rows.
		screen.Add( new UiLabel
		{
			Id = "effective",
			Text = () =>
			{
				var effective = display.Effective;
				var line = string.Format( strings.Extra( OpenTpwText.EffectiveSize ), effective.InternalSize.X, effective.InternalSize.Y, effective.OutputSize.X, effective.OutputSize.Y );
				var canvas = new UiCanvas( effective.OutputSize.X, effective.OutputSize.Y, display.EffectiveUiScale, Screen.PixelDensity );
				if ( canvas.TextScale < canvas.UiScale )
					line += "\n" + string.Format( strings.Extra( OpenTpwText.Fallback ), $"{strings.Extra( OpenTpwText.UiScale )} {canvas.UiScale}x -> {canvas.TextScale}x" );
				var reason = effective.FallbackReason ?? display.Diagnostics.LastOrDefault();
				return reason == null ? line : line + "\n" + string.Format( strings.Extra( OpenTpwText.Fallback ), reason );
			},
			Font = fonts => fonts.Small,
			Color = UiColors.OptionText,
			Shadow = false,
			Wrap = true,
			Bounds = Rect( 57, 700, 1964, 900 ),
			Anchor = UiAnchor.Center
		} );

		void Back() => stack.Pop();
		// [EXT:SETUP] Game files (game folder and CD), where the main page has its OpenTPW button
		screen.Add( new UiButton
		{
			Id = "gameFiles",
			Text = () => strings.Extra( OpenTpwText.GameFiles ),
			Clicked = () => stack.Push( GameFilesScreen.Create( stack, strings ) ),
			Bounds = Rect( 1331, 1330, 1730, 1434 ),
			Anchor = UiAnchor.Center
		} );
		screen.Add( new UiModelImage { Id = "buttonsPanel", Model = "!f_plain", Bounds = Rect( 1742, 1321, 1947, 1443 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "back", Model = "b_okay", Help = strings.Help( 2 ), Clicked = Back, Bounds = Rect( 1763, 1340, 1846, 1424 ), Anchor = UiAnchor.Center } );
		screen.Back = Back;
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
