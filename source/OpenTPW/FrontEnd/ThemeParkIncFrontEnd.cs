using OpenTPW.UI.Original;

namespace OpenTPW.FrontEnd;

/// <summary>What the Theme Park Inc front end asks of the game.</summary>
public sealed class ThemeParkIncFrontEndHost
{
	/// <summary>Starts the slice in a zone (<c>arabian</c>, <c>science</c> or <c>water</c>).</summary>
	public Action<string> EnterWorld { get; init; } = _ => { };
	/// <summary>Closes the window (the confirmed Quit Game).</summary>
	public Action Quit { get; init; } = () => { };
}

/// <summary>
/// The start screen of the experimental Theme Park Inc slice (phase 1): the Personnel File, drawn from Theme Park Inc's
/// <c>ui.wad</c> models at the rectangles of its layout table with its own strings and fonts, and the quit confirmation.
/// Theme Park Inc boots into its 3D intro world with front-end screens as overlays; phase 1 has a flat backdrop instead.
/// Enter Game starts the slice in the zone chosen on the page (science, the original's default for a new player).
/// </summary>
// [EXT:tpi-front-end-phase1] the screen follows the Personnel File's table and strings, but it is an OpenTPW start screen with one player (no profiles, no saves, no 3D intro world, no sound); the original's behaviour behind Load A Game, Delete Player, the name box and the Coaster Design Kit is not reproduced
public sealed class ThemeParkIncFrontEnd
{
	/// <summary>The zones the chooser offers, in page order.</summary>
	public static readonly IReadOnlyList<string> Zones = new[] { "science", "water", "arabian" };

	private readonly UiStringTable strings;
	private readonly ThemeParkIncFrontEndHost host;
	private readonly Func<string, FontAtlas>? loadFont;
	private readonly Dictionary<string, FontAtlas?> fonts = new( StringComparer.OrdinalIgnoreCase );
	private readonly UiScreen screen;

	/// <param name="loadFont">Loads a Theme Park Inc <c>.bf4</c> font by file name (the PERSONNELFILE family); null (or a missing font) draws with the shared UI fonts.</param>
	public ThemeParkIncFrontEnd( UiStringTable strings, ThemeParkIncFrontEndHost host, Func<string, FontAtlas>? loadFont = null )
	{
		this.strings = strings;
		this.host = host;
		this.loadFont = loadFont;
		screen = CreatePersonnelFile();
		Stack.Push( screen );
	}

	public UiScreenStack Stack { get; } = new();
	/// <summary>The Personnel File itself (below the quit confirmation).</summary>
	public UiScreen Screen => screen;
	/// <summary>The zone Enter Game starts.</summary>
	// [BIN:TPI-EXE:0x56ab40 enter game] a new player's world is -1 or 3 (the intro world) and becomes 1, science, before the world change 0x6a26b0
	public string Zone { get; private set; } = "science";

	public bool Update( UiContext context, UiInput input ) => Stack.Update( context, input );

	public void Draw( UiContext context ) => Stack.Draw( context );

	/// <summary>Chooses the zone Enter Game starts.</summary>
	// [EXT:tpi-front-end-zone-chooser] the original reaches Polar Zone and Arabian Nights only through its campaign and Overview Map; the page offers them directly
	public void SelectZone( string zone )
	{
		if ( !Zones.Contains( zone, StringComparer.OrdinalIgnoreCase ) )
			throw new ArgumentException( $"'{zone}' is not a Theme Park Inc zone the slice offers.", nameof( zone ) );
		Zone = zone.ToLowerInvariant();
	}

	/// <summary>Enter Game: starts the slice in the chosen zone.</summary>
	public void EnterGame() => host.EnterWorld( Zone );

	/// <summary>Quit Game (or Escape): the quit confirmation over the page.</summary>
	public void RequestQuit()
	{
		if ( Stack.Top?.Name == "tpiQuit" )
			return;
		Stack.Push( CreateQuitConfirm() );
	}

	/// <summary>
	/// A PERSONNELFILE font sized for the canvas tier (<paramref name="offset"/> steps smaller); the shared UI font of the role when the
	/// family is not available (a headless fixture, or a language folder without it).
	/// </summary>
	private FontAtlas Pick( UiFonts shared, int offset, Func<UiFonts, FontAtlas> fallback )
	{
		if ( loadFont == null )
			return fallback( shared );
		var tier = Math.Clamp( (int)shared.Tier + offset, 0, 2 );
		var name = tier switch { 0 => "PERSONNELFILESMALL", 1 => "PERSONNELFILEMED", _ => "PERSONNELFILEBIG" };
		if ( !fonts.TryGetValue( name, out var font ) )
		{
			try { font = loadFont( name + ".bf4" ); }
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or KeyNotFoundException or FileNotFoundException ) { font = null; }
			fonts[name] = font;
		}
		return font ?? fallback( shared );
	}

	private Func<UiFonts, FontAtlas> TitleFont => shared => Pick( shared, 0, fonts => fonts.Title );
	private Func<UiFonts, FontAtlas> ButtonFont => shared => Pick( shared, -1, fonts => fonts.Menu );
	private Func<UiFonts, FontAtlas> SmallFont => shared => Pick( shared, -1, fonts => fonts.Label );

	private UiScreen CreatePersonnelFile()
	{
		var table = ThemeParkIncLayoutTables.PersonnelFile;
		var page = new UiScreen( "tpiPersonnelFile" ) { Modal = false, Back = RequestQuit };
		string Text( int id ) => strings.Entry( id );
		UiModelImage Image( string id, uint control, string? model = null ) => page.Add( new UiModelImage
		{
			Id = id,
			Model = model ?? table.Control( control ).Model!,
			Bounds = table.Control( control ).Rect,
			Anchor = UiAnchor.Center
		} );
		// [BIN:TPI-EXE:0x9c0fbc personnel file table] window !window_s (273,72)-(1775,1464) with the title text box 0xc75c (397,82)-(1469,161), the file page 0xc73b (389,318)-(1690,1215), the framed mugshot 0xc75d/0xc75e (1816,1240)-(2009,1434) and the Coaster Design Kit and Quit Game toggles in !f_plain frames (0xc739, 0xc75a); the builder 0x56adb0 draws the window first and the page over it
		Image( "window", 0xC738 );
		page.Add( new UiLabel
		{
			Id = "title",
			Text = () => Text( ThemeParkIncText.PersonnelFileTitle ),
			Font = TitleFont,
			// [BIN:TPI-EXE:0x515fe0 title control] Theme Park Inc's title class (constructor 0x515dc0, vtable 0x93c368; set by 0x501d50, called at 0x56ae29 for this title) draws the same (-2, +2) copy as the Theme Park World one, with colour alpha 0xc8 (0x5162d2-0x5162f4)
			Shadow = true, ShadowAlpha = UiBatch.TpiTitleCopyAlpha,
			Color = UiColors.Title,
			Align = UiAlign.Center,
			Fit = true,
			Bounds = table.Control( 0xC75C ).Rect,
			Anchor = UiAnchor.Center
		} );
		Image( "page", 0xC73B );
		// [BIN:TPI-EXE:0x56adb0 file tabs] four filetab buttons (0xc73d to 0xc740) across the page's top edge, each showing its player's name or UITEXT 20, "New Player", for a free slot
		// [EXT:tpi-front-end-one-player] OpenTPW has no player profiles ([EXT:no-player-profiles]): the first slot is the one player and is shown selected, the other three are drawn inactive
		var tabs = new[] { 0xC73Du, 0xC73Eu, 0xC73Fu, 0xC740u };
		for ( var index = 0; index < tabs.Length; index++ )
		{
			var slot = index;
			page.Add( new UiButton
			{
				Id = $"slot{index + 1}",
				Model = table.Control( tabs[index] ).Model,
				Text = () => Text( ThemeParkIncText.NewPlayer ),
				Font = SmallFont,
				Selected = () => slot == 0,
				// the one player's slot shows its selected (down) frame without being clickable; the free slots show the disabled frame
				Inert = slot == 0,
				Enabled = slot == 0,
				Bounds = table.Control( tabs[index] ).Rect,
				Anchor = UiAnchor.Center
			} );
		}
		// [BIN:TPI-EXE:0x56adb0 name box] the name box is the filebox panel 0xc756 (729,360)-(1361,462) with the filebutok button 0xc757 and the text field 0xc758, whose prompt is UITEXT 32, "Enter name here"
		// [EXT:tpi-front-end-one-player] typing a name makes a profile, which OpenTPW does not have: the box is drawn with its prompt and the tick, both inactive
		Image( "nameBox", 0xC756 );
		page.Add( new UiButton { Id = "nameOk", Model = table.Control( 0xC757 ).Model, Enabled = false, Bounds = table.Control( 0xC757 ).Rect, Anchor = UiAnchor.Center } );
		page.Add( new UiLabel
		{
			Id = "namePrompt",
			Text = () => Text( ThemeParkIncText.EnterNameHere ),
			Font = SmallFont,
			Color = UiColors.Disabled,
			Bounds = table.Control( 0xC758 ).Rect,
			Anchor = UiAnchor.Center
		} );
		// [BIN:TPI-EXE:0x56ab40 enter game] Enter Game (0xc754, filebut, UITEXT 34, help 287) saves the player and enters the player's world; Load A Game (0xc759, UITEXT 35, help 287) opens table 0x9ba364 and Delete Player (0xc755, UITEXT 33, help 288) asks first
		var enter = FileButton( page, table, "enterGame", 0xC754, ThemeParkIncText.EnterGame, EnterGame, enabled: true );
		FileButton( page, table, "loadGame", 0xC759, ThemeParkIncText.LoadGame, () => { }, enabled: false );
		FileButton( page, table, "deletePlayer", 0xC755, ThemeParkIncText.DeletePlayer, () => { }, enabled: false );
		// [EXT:tpi-front-end-zone-chooser] the original reaches the zones through its campaign; a latched button per zone chooses the one Enter Game starts (science is the new player's default)
		page.Add( new UiLabel
		{
			Id = "zoneLabel",
			Text = () => strings.Extra( OpenTpwText.TpiZoneChooser ),
			Font = SmallFont,
			Color = UiColors.Text,
			Bounds = new UiRect( 440, 560, 1200, 60 ),
			Anchor = UiAnchor.Center
		} );
		var toggle = table.Control( 0xC73A );
		var columns = new[] { 440f, 850f, 1260f };
		for ( var index = 0; index < Zones.Count; index++ )
		{
			var zone = Zones[index];
			page.Add( new UiButton
			{
				Id = $"zone_{zone}",
				Model = toggle.Model,
				Text = () => ZoneName( zone ),
				Font = SmallFont,
				Selected = () => Zone == zone,
				Clicked = () => SelectZone( zone ),
				Bounds = new UiRect( columns[index], 630, 380, 90 ),
				Anchor = UiAnchor.Center
			} );
		}
		// [BIN:TPI-EXE:0x568d40 design kit and quit] a click on the toggle 0xc73a (help 290) opens the Coaster Design Kit; a click on 0xc75b (UITEXT 30, help 289) asks the quit confirmation; both toggles stand in !f_plain frames (0xc739, 0xc75a)
		// [EXT:tpi-front-end-phase1] the Coaster Design Kit is not part of the slice: its toggle is drawn inactive
		Image( "designKitFrame", 0xC739 );
		page.Add( new UiButton
		{
			Id = "designKit",
			Model = toggle.Model,
			Text = () => Text( ThemeParkIncText.CoasterDesignKit ),
			Font = SmallFont,
			Help = strings.Help( toggle.Help ),
			Enabled = false,
			Bounds = toggle.Rect,
			Anchor = UiAnchor.Center
		} );
		Image( "quitFrame", 0xC75A );
		var quit = table.Control( 0xC75B );
		page.Add( new UiButton
		{
			Id = "quit",
			Model = quit.Model,
			Text = () => Text( ThemeParkIncText.QuitGame ),
			Font = SmallFont,
			Help = strings.Help( quit.Help ),
			Clicked = RequestQuit,
			Bounds = quit.Rect,
			Anchor = UiAnchor.Center
		} );
		// [BIN:TPI-EXE:0x9c0fbc mugshot] the player's picture: !frame 0xc75d (1816,1240)-(2009,1434) around the mugshot control 0xc75e
		Image( "mugshotFrame", 0xC75D );
		Image( "mugshot", 0xC75E );
		page.Focus( enter );
		return page;
	}

	private UiButton FileButton( UiScreen page, LayoutTable table, string id, uint control, int text, Action clicked, bool enabled )
	{
		var node = table.Control( control );
		return page.Add( new UiButton
		{
			Id = id,
			Model = node.Model,
			Text = () => strings.Entry( text ),
			Font = ButtonFont,
			Help = strings.Help( node.Help ),
			Clicked = clicked,
			Enabled = enabled,
			Bounds = node.Rect,
			Anchor = UiAnchor.Center
		} );
	}

	/// <summary>The zone's name in THEMENAMES.str.</summary>
	public string ZoneName( string zone )
	{
		var name = strings.Theme( ThemeParkIncText.ThemeNameIndex( zone ) );
		return name.Length > 0 ? name : zone;
	}

	/// <summary>
	/// The quit confirmation, from the generic confirm table of Theme Park Inc: the window, a caption (the text up to its blank line), the question below it and the
	/// tick and cross buttons at the right-hand side.
	/// </summary>
	private UiScreen CreateQuitConfirm()
	{
		var table = ThemeParkIncLayoutTables.QuitConfirm;
		var dialog = new UiScreen( "tpiQuit" );
		var root = table.Root;
		dialog.Add( new UiModelImage { Id = "window", Model = root.Model!, Bounds = root.Rect, Anchor = UiAnchor.Center } );
		var (caption, body) = UiDialogs.SplitCaption( strings.Entry( ThemeParkIncText.QuitConfirmation ) );
		var captionRect = table.Control( 0xEA64 ).Rect;
		// [BIN:TPI-EXE:0x515fe0 title control] the quit caption is a title control too (set by 0x501d50 at 0x4f4902, box 0xea64): the copy alpha is 0xc8
		dialog.Add( new UiLabel { Id = "caption", Text = () => caption, Font = TitleFont, Shadow = true, ShadowAlpha = UiBatch.TpiTitleCopyAlpha, Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = captionRect, Anchor = UiAnchor.Center } );
		// [BIN:TPI-EXE:0x4f48c2 confirm text] the builder 0x4f47f0 cuts the text at its blank line (0x8717ed finds the separator 0x9b19c4): the first part is the caption box 0xea64 and the rest is the window's own text
		// [EXT:tpi-front-end-phase1] the window's own text area is not traced: the question is centred below the caption and left of the buttons
		var okay = table.Control( 0xEA62 );
		var cancel = table.Control( 0xEA61 );
		var bodyRect = new UiRect( root.Rect.X + 60, captionRect.Bottom + 30, cancel.Rect.X - 40 - (root.Rect.X + 60), root.Rect.Bottom - captionRect.Bottom - 90 );
		dialog.Add( new UiLabel { Id = "message", Text = () => body, Font = SmallFont, Align = UiAlign.Center, Wrap = true, Bounds = bodyRect, Anchor = UiAnchor.Center } );
		// [BIN:TPI-EXE:0x568da1 quit call] the quit call passes the mode words 2 and 1: the table's first button 0xea61 becomes b_cancel (help 3, 0x4f4937) and the second 0xea62 b_okay (help 0, 0x4f493e), whose action 0x568e60 closes the game; the close button 0xea63 stays hidden for a two-button dialog
		var cancelButton = dialog.Add( new UiButton { Id = "cancel", Model = "b_cancel", Help = strings.Help( ThemeParkIncText.HelpCancel ), Clicked = CloseQuit, Bounds = cancel.Rect, Anchor = UiAnchor.Center } );
		dialog.Add( new UiButton { Id = "ok", Model = "b_okay", Help = strings.Help( ThemeParkIncText.HelpOk ), Clicked = () => { CloseQuit(); host.Quit(); }, Bounds = okay.Rect, Anchor = UiAnchor.Center } );
		dialog.Back = CloseQuit;
		dialog.Focus( cancelButton );
		return dialog;

		void CloseQuit()
		{
			if ( dialog is { IsRemoved: false } && ReferenceEquals( Stack.Top, dialog ) )
				Stack.Pop();
		}
	}
}
