using System.Globalization;
using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// The per-visitor window, built from tp.exe's table 0x7575e0: the window1 frame, the visitor's number as the title, the portrait frame, the stats frame (cash remaining, time
/// in park, rides ridden, purchases made, sideshows played and won, the happiness bar and the current status), the thought frame with the thought's icon and text, the
/// cycle arrows (b_arup, b_ardown), the locate (b_locate) and All Visitors (b_kids) buttons and the tick.
/// </summary>
// [BIN:TPW-EXE:0x004b79b0 visitor window] the opener (on the window object 0x7cc408, kind word -2 set by its constructor at 0x4b6b6e) stores the visitor, builds table 0x7575e0 through 0x48cea0
// (handler 0x4b7720, frames 0x3722 and 0x3723; it first closes the window that is open, 0x485b40), plays the sound 0x81 and posts help 0x12e; the window's refresh 0x4b72a0 (vtable +0x14)
// runs when it is built (0x4b7660) and on timer 0x80080 (4000 ms, armed on message 0x15), and closes the window when the selected thing [0x7c2658] is gone
// [BIN:TPW-EXE:0x004879d0 visitor click] the park click in cursor mode 7 and the All Visitors list's row message 0x400 (0x49351e) both open it for the picked or the row's visitor
// [APPROX:UI-910] the portrait frame observes current ordinary kid/costume world output at yaw zero; unavailable, riding and specialized sources stay empty. Independent native clone VM timing, source-loss epochs, screen-space rotation/depth and exact viewport/raster fidelity remain open. Arrows share the live type-1 used-chain projection (0x4824d0 / 0x482da0, mask 0x40), with an explicit host-only fallback for pool-free fixtures.
public sealed partial class ParkHud
{
	private static LayoutTable VisitorInfoTable => OriginalLayoutTables.VisitorInfoWindow;

	/// <summary>Seconds between two refreshes of the stats frame (timer 0x80080, 4000 ms, armed on message 0x15 at 0x4b7740).</summary>
	public const float VisitorInfoRefreshSeconds = 4f;

	private UiScreen? visitorInfoScreen;
	private float visitorInfoAge;
	private VisitorInfoValues visitorInfoShown;
	private int visitorInfoTitleNumber;
	private long visitorPortraitSession;
	private Guest? visitorPortraitGuest;

	public bool VisitorInfoWindowOpen => visitorInfoScreen != null && Stack.Screens.Contains( visitorInfoScreen );
	/// <summary>The visitor the window shows (tp.exe's selected thing 0x7c2658), null when it is closed or the visitor left.</summary>
	public Guest? InfoVisitor => VisitorInfoWindowOpen && visitorInfoId != 0 ? (level.Guests?.Guests ?? Array.Empty<Guest>()).FirstOrDefault( guest => guest.Id == visitorInfoId && guest.State != GuestState.Gone ) : null;
	/// <summary>The stats frame as last refreshed.</summary>
	public VisitorInfoValues VisitorInfoShown => visitorInfoShown;

	/// <summary>Opens the window on <paramref name="guest"/>; the All Visitors window gives way to it.</summary>
	public bool OpenVisitorInfoWindow( Guest guest )
	{
		ArgumentNullException.ThrowIfNull( guest );
		if ( !(level.Guests?.Guests.Any( item => ReferenceEquals( item, guest ) ) ?? false) || guest.State == GuestState.Gone )
			return false;
		CloseVisitorWindow();
		if ( VisitorInfoWindowOpen )
			CloseVisitorInfoWindow();
		visitorInfoId = guest.Id;
		visitorInfoTitleNumber = guest.VisitorNumber;
		RefreshVisitorInfo();
		visitorInfoScreen ??= CreateVisitorInfoScreen();
		BindVisitorPortrait( guest );
		Stack.Push( visitorInfoScreen );
		visitorInfoScreen.Focus( visitorInfoScreen.Find( "exit" ) );
		// [BIN:TPW-EXE:0x004b79b0 open sound] 0x51bfc0(0, global 0x803a24, 0x81) as the window opens
		GameAudio.PlayVisitorClick();
		return true;
	}

	public void CloseVisitorInfoWindow()
	{
		ClearVisitorPortrait();
		if ( visitorInfoScreen == null || !VisitorInfoWindowOpen )
			return;
		while ( Stack.Top != visitorInfoScreen && Stack.Screens.Count > 1 )
			Stack.Pop();
		if ( Stack.Top == visitorInfoScreen )
			Stack.Pop();
	}

	/// <summary>The window's title: "Visitor #" with the visitor's number, "New Visitor" while it has none (UITEXT 456, 455).</summary>
	// [BIN:TPW-EXE:0x004b7660 title] the opener formats UITEXT 0x1c8 with the number (+0x1d8), or 0x1c9 when it is 0; the refresh 0x4b72a0 only rewrites it while the stored number is still 0
	public string VisitorInfoTitle => InfoVisitor == null ? "" : VisitorInfoRules.Title( strings, visitorInfoTitleNumber );

	private void RefreshVisitorInfo()
	{
		visitorInfoAge = 0;
		var guest = (level.Guests?.Guests ?? Array.Empty<Guest>()).FirstOrDefault( item => item.Id == visitorInfoId );
		if ( guest == null )
		{
			visitorInfoShown = default;
			return;
		}
		if ( visitorInfoTitleNumber == 0 )
			visitorInfoTitleNumber = guest.VisitorNumber;
		var values = ParkVisitorRoster.ValuesOf( guest, refreshed: true );
		var status = strings.KidState( (int)guest.State, guest.State == GuestState.Using ? AttractionNameOf( guest.AttractionId ) : "" );
		// [EXT:pools-swim] the swim session's states have no KIDSTATES entry; a guest aboard a changing room is changing
		if ( guest.State is GuestState.Swimming or GuestState.ClimbingOut or GuestState.ChangingOut or GuestState.PoolActivity || level.Guests?.IsChangingIn( guest ) == true )
			status = strings.Extra( guest.State switch { GuestState.Swimming or GuestState.PoolActivity => OpenTpwText.PoolStateSwimming, GuestState.ClimbingOut => OpenTpwText.PoolStateClimbingOut, _ => OpenTpwText.PoolStateChanging } );
		visitorInfoShown = new VisitorInfoValues( guest.Money, values.Hours, guest.RidesTaken, guest.ShopsVisited, guest.SideshowsVisited, guest.SideshowsWon,
			values.HappinessGauge, status, strings.Thought( (int)guest.Thought ), guest.Thought == GuestThought.None ? -1 : (int)guest.Thought - 1 );
	}

	private string AttractionNameOf( int attractionId ) => attractionId == 0 ? "" :
		level.ListAttractions().FirstOrDefault( item => item.Runtime.AttractionId == attractionId ) is { } match ? EntryName( match.Entry ) : "";

	/// <summary>The arrows: the next (older) or previous (newer) visitor, wrapping, and the window shows it.</summary>
	// [BIN:TPW-EXE:0x004b7720 cycle buttons] 0x3721 (b_ardown, help 97) runs 0x48caf0 and 0x3720 (b_arup, help 98) 0x48cbe0 with the kind word -2 (mask 0x40: the thing list's type-1 things, 0x4824d0 and 0x482da0);
	// 0x483740 / 0x483770 wrap at the head / tail; the visitor pre-hook (vtable +0x3c, 0x5da3c0) is a no-op. Only a different visitor becomes the selected thing [0x7c2658] and refreshes (vtable +4).
	public bool CycleVisitorInfo( bool forward )
	{
		if ( InfoVisitor is not { } guest )
			return false;
		var next = VisitorInfoRules.Cycle( LocatorOrder.VisitorIds( level.Guests?.Guests ?? Array.Empty<Guest>(), level.Guests?.Things ), guest.Id, forward );
		if ( next == null || next == guest.Id )
			return false;
		visitorInfoId = next.Value;
		visitorInfoTitleNumber = (level.Guests?.Guests ?? Array.Empty<Guest>()).FirstOrDefault( item => item.Id == next.Value )?.VisitorNumber ?? 0;
		RefreshVisitorInfo();
		BindVisitorPortrait( InfoVisitor );
		return true;
	}

	/// <summary>The locate button: the visitor locator panel opens on the visitor (its role chosen, the camera following) and the window closes.</summary>
	// [BIN:TPW-EXE:0x004b7720 locate] control 0x3737 (b_locate, help 99) runs the locator opener 0x4b3f60 with the selected thing [0x7c2658], which closes the open window
	public bool LocateVisitorInfo()
	{
		if ( InfoVisitor is not { } guest || Camera.Mode is not ParkCameraMode || !float.IsFinite( guest.X ) || !float.IsFinite( guest.Y ) )
			return false;
		lastLocatedVisitorId = guest.Id;
		OpenLocator( LocatorMode.Visitor, LocatorModel.VisitorRole, guest.Id );
		return true;
	}

	/// <summary>The All Visitors button: the visitors list opens with the visitor selected and this window gives way to it.</summary>
	// [BIN:TPW-EXE:0x004b7720 all visitors] control 0x3738 (b_kids, help 100) runs 0x493530 with the selected thing, the All Visitors window's opener
	public void ShowAllVisitorsOfInfo()
	{
		var id = visitorInfoId;
		CloseVisitorInfoWindow();
		OpenVisitorWindow();
		var index = VisitorRowIndex( id );
		if ( index >= 0 )
			SelectVisitor( index );
	}

	private void ClearVisitorPortrait()
	{
		visitorPortraitSession++;
		visitorPortraitGuest = null;
		if ( visitorInfoScreen?.Find( "portraitFigure" ) is UiSpriteFigure figure )
			figure.Frame = () => null;
	}

	private void BindVisitorPortrait( Guest? guest )
	{
		var session = ++visitorPortraitSession;
		visitorPortraitGuest = guest;
		var simulation = level.Guests;
		if ( visitorInfoScreen?.Find( "portraitFigure" ) is not UiSpriteFigure figure )
			return;
		// A retained callback from a previous selection/open can never follow an ID replacement or a later session.
		figure.Frame = () => guest != null && session == visitorPortraitSession && ReferenceEquals( guest, visitorPortraitGuest )
			&& ReferenceEquals( level, Level.Current ) && ReferenceEquals( simulation, level.Guests ) && ReferenceEquals( guest, InfoVisitor )
			? level.GuestRenderer?.VisitorFrame( guest ) ?? level.CostumeRenderer?.VisitorFrame( guest ) : null;
	}

	private UiScreen CreateVisitorInfoScreen()
	{
		var screen = new VisitorInfoScreen( this ) { Removed = ClearVisitorPortrait };
		var table = VisitorInfoTable;
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = table.Root.Rect, Anchor = UiAnchor.Center } );
		var portrait = table.Control( 0x3722 );
		var stats = table.Control( 0x3723 );
		screen.Add( new UiModelImage { Id = "portraitFrame", Model = portrait.Model!, Bounds = portrait.Rect, Anchor = UiAnchor.Center } );
		// [BIN:TPW-EXE:0x004B6B90 portrait] source body +0x0c, UI depth +0xe0 + 2, primary scale 12 (not a figure id or animation speed).
		// [BIN:TPW-EXE:0x00486530 position] uses the control's inner rectangle; integer shifts precede host canvas scaling.
		var inner = portrait.Inner!.Value;
		screen.Add( new UiSpriteFigure { Id = "portraitFigure", Bounds = inner, Anchor = UiAnchor.Center,
			FeetInCanvas = VisitorInfoRules.PortraitFeet( inner ), CanvasUnitsPerPixel = 3.6f } );
		screen.Add( new UiModelImage { Id = "statsFrame", Model = stats.Model!, Bounds = stats.Rect, Anchor = UiAnchor.Center } );
		var title = table.Control( 0x3736 );
		screen.Add( new UiLabel { Id = "title", Text = () => VisitorInfoTitle, Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center, Fit = true,
			Bounds = title.Rect, Anchor = UiAnchor.Center } );
		// [BIN:TPW-EXE:0x004b6c20 stats labels] the labels take UITEXT 70 (0x372c), 71 (0x3725), 72 (0x3726), 75 (0x3727), 73 (0x3728), 74 (0x3729), 76 (0x372a) and 77 (0x372b); beside them
		// 0x3724 is a money cell (0x490250), 0x372d a duration cell (0x490e60), 0x372e..0x3731 count cells (0x48fde0), 0x3732 the happiness bar (happygrad.wct, inset 0 and 10) and 0x3733 the status text
		// [BIN:TPW-EXE:0x48fe28 value cell alignment] every value is drawn left-aligned: the three cell constructors store 0 (left) in the alignment byte +0xd8 (count 0x48fe28, money 0x490298,
		// duration 0x490ea8) and the builder 0x4b6c20, unlike the All Visitors row factory 0x4939d0, sends none of them the alignment call 0x65c428
		void Label( uint id, UIStrings text )
		{
			var node = table.Control( id );
			screen.Add( new UiLabel { Id = $"label{id:x}", Text = () => strings[text], Font = fonts => fonts.Label, Color = UiColors.Text, Fit = true,
				Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		Label( 0x372C, UIStrings.CashRemaining );
		Label( 0x3725, UIStrings.TimeInPark );
		Label( 0x3726, UIStrings.RidesRidden );
		Label( 0x3727, UIStrings.PurchasesMade );
		Label( 0x3728, UIStrings.SideshowsPlayed );
		Label( 0x3729, UIStrings.SideshowsWon );
		Label( 0x372A, (UIStrings)76 );
		Label( 0x372B, (UIStrings)77 );
		void Value( string id, uint control, UiAlign align, Func<VisitorInfoValues, string> text ) => screen.Add( new UiLabel { Id = id,
			Text = () => InfoVisitor == null ? "" : text( visitorInfoShown ), Font = fonts => fonts.Label, Color = UiColors.Value, Align = align, Fit = true,
			Bounds = table.Control( control ).Rect, Anchor = UiAnchor.Center } );
		Value( "cash", 0x3724, UiAlign.Left, values => FormatMoney( values.Cash ) );
		Value( "timeInPark", 0x372D, UiAlign.Left, values => values.Hours is { } hours ? VisitorDuration( hours ) : "—" );
		Value( "rides", 0x372E, UiAlign.Left, values => values.Rides.ToString( CultureInfo.InvariantCulture ) );
		Value( "purchases", 0x372F, UiAlign.Left, values => values.Purchases.ToString( CultureInfo.InvariantCulture ) );
		Value( "sideshowsPlayed", 0x3730, UiAlign.Left, values => values.SideshowsPlayed.ToString( CultureInfo.InvariantCulture ) );
		Value( "sideshowsWon", 0x3731, UiAlign.Left, values => values.SideshowsWon.ToString( CultureInfo.InvariantCulture ) );
		Value( "status", 0x3733, UiAlign.Left, values => values.Status );
		screen.Add( new VisitorInfoBar { Id = "happinessBar", Value = () => InfoVisitor == null ? null : visitorInfoShown.HappinessGauge / (float)EmployeeWindowRules.GaugeFull,
			Bounds = table.Control( 0x3732 ).Rect, Anchor = UiAnchor.Center } );
		var thoughts = table.Control( 0x3734 );
		screen.Add( new UiModelImage { Id = "thoughtFrame", Model = thoughts.Model!, Bounds = thoughts.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "thought", Text = () => InfoVisitor == null ? "" : visitorInfoShown.Thought, Help = strings.Help( thoughts.Help ), Font = fonts => fonts.Label,
			Color = UiColors.Text, Align = UiAlign.Left, Fit = true, Bounds = thoughts.Inner!.Value, Anchor = UiAnchor.Center } );
		var icon = table.Control( 0x3735 );
		screen.Add( new FrameIcon { Id = "thoughtIcon", Model = icon.Model!, Frame = () => InfoVisitor == null ? -1 : visitorInfoShown.ThoughtFrame, Bounds = icon.Rect, Anchor = UiAnchor.Center } );
		UiButton Button( string id, string model, Action clicked )
		{
			var node = table.Model( model );
			return screen.Add( new UiButton { Id = id, Model = node.Model, Help = strings.Help( node.Help ), Clicked = clicked, Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		Button( "previousVisitor", "b_arup", () => CycleVisitorInfo( false ) );
		Button( "nextVisitor", "b_ardown", () => CycleVisitorInfo( true ) );
		Button( "locateVisitorInfo", "b_locate", () => LocateVisitorInfo() );
		Button( "allVisitors", "b_kids", ShowAllVisitorsOfInfo );
		Button( "exit", "b_okay", CloseVisitorInfoWindow );
		screen.Back = CloseVisitorInfoWindow;
		return screen;
	}

	/// <summary>The window's screen: closes when the visitor leaves and refreshes the stats every 4 s.</summary>
	private sealed class VisitorInfoScreen : UiScreen
	{
		private readonly ParkHud owner;
		public VisitorInfoScreen( ParkHud owner ) : base( "visitorInfo" ) => this.owner = owner;
		public override bool Update( UiContext context, UiInput input )
		{
			if ( owner.InfoVisitor == null )
			{
				owner.CloseVisitorInfoWindow();
				return true;
			}
			owner.visitorInfoAge += Math.Max( 0, context.Delta );
			// [BIN:TPW-EXE:0x004b7720 refresh] message 0x10 of timer 0x80080 refreshes the frames (0x4b72a0)
			if ( owner.visitorInfoAge >= VisitorInfoRefreshSeconds )
				owner.RefreshVisitorInfo();
			if ( Find( "locateVisitorInfo" ) is { } locate )
				locate.Enabled = Camera.Mode is ParkCameraMode && owner.InfoVisitor is { } guest && float.IsFinite( guest.X ) && float.IsFinite( guest.Y );
			return base.Update( context, input );
		}
	}

	/// <summary>The happiness bar: the control's rectangle less 10 units top and bottom (0x485e30 with 0 and 10).</summary>
	private sealed class VisitorInfoBar : UiElement
	{
		public Func<float?> Value { get; init; } = () => null;
		public override void Draw( UiContext context, bool focused, bool pressed ) =>
			UiGauge.Draw( context.Batch, UiGauge.BarRect( ScreenRect( context.Canvas ), context.Canvas ), Value(), UiGauge.HappinessArtPath );
	}
}

/// <summary>The stats frame of the per-visitor window as refreshed (the bar on the original's 0..1024 scale).</summary>
public readonly record struct VisitorInfoValues( int Cash, long? Hours, int Rides, int Purchases, int SideshowsPlayed, int SideshowsWon, int HappinessGauge, string Status, string Thought, int ThoughtFrame );

/// <summary>The per-visitor window's rules (tp.exe 0x4b7660, 0x483740 / 0x483770).</summary>
public static class VisitorInfoRules
{
	/// <summary>Visitor-only nominal canvas hotspot. The original signed integer shifts of the inner rectangle precede viewport scaling.</summary>
	// [BIN:TPW-EXE:0x00486530 position] scale 12 gives trunc(12 * -0.25) = -3; inner (477,281)..(704,510) gives (598,450).
	public static System.Numerics.Vector2 PortraitFeet( UiRect inner ) =>
		new( (int)inner.X + (((int)inner.Right - (int)inner.X) >> 1) + 8,
			(int)inner.Bottom - (((int)inner.Bottom - (int)inner.Y) >> 2) - 3 );

	/// <summary>"Visitor #" with the number, "New Visitor" without one.</summary>
	public static string Title( UiStringTable strings, int number ) => number == 0 ? strings[UIStrings.NewVisitor]
		: strings.Format( UIStrings.VisitorCount, number.ToString( CultureInfo.InvariantCulture ) );

	/// <summary>
	/// The visitor the arrows move to from <paramref name="currentId"/>: <paramref name="forward"/> the next older one, wrapping to the newest, else the next newer
	/// one, wrapping to the oldest; <paramref name="orderedIds"/> already follows the shared live visitor projection of the thing list.
	/// An absent current uses the head / tail for host fixtures; the HUD separately rejects a missing or Gone selection.
	/// </summary>
	// [BIN:TPW-EXE:0x004824d0 next thing] mask 0x40 walks the thing list (a new thing goes to its head, 0x516270) from the visitor's next node to the first type-1 thing, and 0x483740 starts at the head when
	// none follows; 0x482da0 walks the previous nodes and 0x483770 starts at the tail
	public static int? Cycle( IEnumerable<int> orderedIds, int currentId, bool forward )
	{
		var list = orderedIds.ToArray();
		if ( list.Length == 0 )
			return null;
		var index = Array.IndexOf( list, currentId );
		if ( index < 0 )
			return forward ? list[0] : list[^1];
		return forward ? list[index < list.Length - 1 ? index + 1 : 0] : list[index > 0 ? index - 1 : list.Length - 1];
	}
}
