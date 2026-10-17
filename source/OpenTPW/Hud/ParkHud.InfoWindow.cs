using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	private static LayoutTable ParkInfoTable => OriginalLayoutTables.ParkInfoWindow;

	/// <summary>Seconds between two refreshes of the window's counts and Top 3 Thoughts (timer 0x80083, 1000 ms).</summary>
	// [BIN:TPW-EXE:0x004A52A0 refresh timer] the builder arms timer 0x80083 at 1000 ms (0x65EF90 with 0x3E8) and the window procedure 0x4A4830 answers its message 0x10 with the counts, the happiness bars and the thoughts (0x4A4AB0);
	// the builder runs the same update once as it builds (0x4A5BB4 ... 0x4A5416), so the icons are never empty on the first frame
	public const float ParkInfoRefreshSeconds = 1f;

	private TopThoughts parkInfoTop;
	private float parkInfoAge;
	// [BIN:TPW-EXE:0x004A4AB0 hidden icons stay hidden] a slot with no icon calls 0x65D9DD(0), which clears bit 0 of the control's +0x48; a slot with one only sets the frame (vtable +0x1C, 0x661525, which forwards message 0x80 to the
	// control's two model objects and writes no flag), and neither 0x4A4AB0 nor the window procedure calls 0x65D9DD(1), so a hidden icon stays hidden while the window is open
	// [APPROX:UI-914] the port follows that reading (an icon hidden by an empty slot stays hidden until the window is reopened); the model objects' handler of message 0x80 was not read, so a show there is not ruled out
	private bool[]? parkInfoIcons;
	private bool[] parkInfoIconHidden => parkInfoIcons ??= new bool[3];
	private UiScreen? parkInfoScreen;

	/// <summary>Whether the park information window is open.</summary>
	public bool ParkInfoWindowOpen => parkInfoScreen != null && Stack.Screens.Contains( parkInfoScreen );

	/// <summary>The Top 3 Thoughts as the window last refreshed them.</summary>
	public TopThoughts ParkInfoTopThoughts => parkInfoTop;

	/// <summary>The <c>ti_hungry</c> frame the icon of <paramref name="slot"/> shows, or -1 while it draws nothing (empty, or hidden earlier and never shown again).</summary>
	public int ParkInfoIconFrame( int slot ) => parkInfoIconHidden[slot] ? -1 : parkInfoTop.FrameAt( slot );

	/// <summary>Reads the Top 3 Thoughts again, as the window's one-second timer does.</summary>
	// [BIN:TPW-EXE:0x004A4AB0 thoughts refresh] 0x4C8430 fills three bytes from the guests now; the icons take them as a whole, so the three always belong to one reading
	private void RefreshParkInfo()
	{
		parkInfoAge = 0;
		// A read-only visit never has the owner's guests: the published statistics carry no thoughts, so the icons stay empty
		// [EXT:ONLINE-074] the park information window of a visit shows the published figures only, and no thoughts
		parkInfoTop = level.PublishedParkStats == null && level.Guests is { } guests ? guests.GetTopThoughts() : new TopThoughts( TopThoughtRanking.NoThought, TopThoughtRanking.NoThought, TopThoughtRanking.NoThought );
		for ( var slot = 0; slot < parkInfoIconHidden.Length; slot++ )
			if ( parkInfoTop.FrameAt( slot ) < 0 )
				parkInfoIconHidden[slot] = true;
	}

	/// <summary>The park information window: the original's Top 3 Thoughts frame and a live park summary using existing simulation values; a read-only visit shows the published statistics.</summary>
	// [BIN:TPW-EXE:0x004A52A0 park information window] table 0x7537D0 (OriginalLayoutTables.ParkInfoWindow): the window3 frame, the title 0x4A68C5 (UITEXT 0x96), the line graph with its toggles
	// (not built here), the Top 3 Thoughts frame "toughts" 0x4A68D1 with its title 0x4A68CE (UITEXT 0x9C, help 186) and three ti_hungry icons 0x4A68D2..0x4A68D4, the counts frame 0x4A68C9, the
	// happiness levels 0x4A68CF / 0x4A68D0 (UITEXT 0x9D) and the buttons b_kids, b_allstaff, b_allthings and b_okay
	// [APPROX:UI-914] the line graph, the counts frame, the happiness levels and the three navigation buttons are not built: the summary rows below stand in the graph's area, and OpenTPW's economy rows are not the original's
	// [EXT:park-summary] the summary rows are a functional host summary; the original information-screen layout beyond the Top 3 Thoughts remains unqualified
	public void OpenParkInfoWindow()
	{
		if ( Stack.Screens.Any( screen => screen.Name == "parkInfo" ) )
			return;
		var table = ParkInfoTable;
		var screen = new ParkInfoScreen( this );
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = table.Root.Rect, Anchor = UiAnchor.Center } );
		var title = table.Control( 0x4A68C5 );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[UIStrings.ParkInformation], Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center, Fit = true,
			Bounds = title.Rect, Anchor = UiAnchor.Center } );
		// A read-only visit never runs the owner's economy: the window shows the statistics the owner published
		// (the original's visitor saw the park as saved; docs/reverse/ONLINE-VISIT.md), never simulated values.
		// [EXT:ONLINE-074] the published park's figures are shown in the park information window of a visit.
		var published = level.PublishedParkStats;
		var rows = published is { } stats
			? new (UIStrings Label, Func<string> Value)[]
			{
				(UIStrings.PeopleInPark, () => stats.Visitors.ToString( System.Globalization.CultureInfo.InvariantCulture )),
				(UIStrings.BankBalance, () => FormatMoney( stats.Balance )),
				(UIStrings.ParkValue, () => stats.Value.ToString( System.Globalization.CultureInfo.InvariantCulture )),
				(UIStrings.ParkRating, () => stats.Rating.ToString( System.Globalization.CultureInfo.InvariantCulture )),
				(UIStrings.AllStaff, () => strings.Extra( OpenTpwText.NotAvailable )),
			}
			: new (UIStrings Label, Func<string> Value)[]
			{
				(UIStrings.PeopleInPark, () => (level.Guests?.GetStatistics().InPark ?? 0).ToString()),
				(UIStrings.BankBalance, () => BalanceWithCurrency),
				(UIStrings.ParkValue, () => (Status.EconomySource as ParkEconomy)?.ParkValue.ToString() ?? strings.Extra( OpenTpwText.NotAvailable )),
				(UIStrings.ParkRating, () => (Status.EconomySource as ParkEconomy)?.ParkRating.ToString() ?? strings.Extra( OpenTpwText.NotAvailable )),
				(UIStrings.AllStaff, () => (Status.EconomySource as ParkEconomy)?.Staff.Members.Count.ToString() ?? "0"),
			};
		// The rows stand in the area of the original's line graph, to the right of the toggles' column
		var graph = table.Control( 0x4A68BB ).Inner!.Value;
		for ( var index = 0; index < rows.Length; index++ )
		{
			var row = rows[index];
			screen.Add( new UiLabel { Id = $"label{index}", Text = () => strings[row.Label],
				Bounds = new UiRect( graph.X + 16, graph.Y + 14 + index * 105, 520, 80 ), Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = $"value{index}", Text = row.Value,
				Bounds = new UiRect( graph.X + 546, graph.Y + 14 + index * 105, 330, 80 ), Anchor = UiAnchor.Center } );
		}
		if ( published is { } publishedStats )
		{
			// The published in-game date and the read-only note close the visit's window.
			screen.Add( new UiLabel { Id = "published", Text = () => OnlineStrings.Get( OnlineLabel.PublishedOn ) + publishedStats.Date,
				Font = fonts => fonts.Label, Bounds = new UiRect( graph.X + 16, graph.Bottom - 190, 870, 70 ), Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = "note", Text = () => OnlineStrings.Get( OnlineLabel.PublishedStatsNote ),
				Font = fonts => fonts.Label, Fit = true, Bounds = new UiRect( graph.X + 16, graph.Bottom - 110, 870, 70 ), Anchor = UiAnchor.Center } );
		}
		// [EXT:guest-insights] with the extension on, the refusal-reason counters stand in the column where the original keeps its line toggles (b_ltoggle1..5, not built here, UI-914): a title and one
		// count per reason; the counters are the simulation's passive observer, never saved, and a visit has none
		if ( published == null && FeatureGates.GuestInsights && level.Guests?.RefusalCounters is { } counters )
		{
			var toggles = table.Control( 0x4A68BF ).Rect;
			screen.Add( new UiLabel { Id = "refusalsTitle", Text = () => strings.Extra( OpenTpwText.RefusalsTitle ), Font = fonts => fonts.Label, Color = UiColors.Text, Align = UiAlign.Center, Fit = true,
				Bounds = new UiRect( toggles.X, toggles.Y, toggles.Width, 60 ), Anchor = UiAnchor.Center } );
			var reasons = new (RefusalReason Reason, OpenTpwText Text)[]
			{
				(RefusalReason.TooExpensive, OpenTpwText.RefusalTooExpensive), (RefusalReason.QueueTooLong, OpenTpwText.RefusalQueueTooLong), (RefusalReason.TooScary, OpenTpwText.RefusalTooScary),
				(RefusalReason.TooBoring, OpenTpwText.RefusalTooBoring), (RefusalReason.RideUnavailable, OpenTpwText.RefusalRideUnavailable)
			};
			for ( var index = 0; index < reasons.Length; index++ )
			{
				var (reason, text) = reasons[index];
				var top = toggles.Y + 70 + index * 56;
				screen.Add( new UiLabel { Id = $"refusal{index}", Text = () => strings.Extra( text ), Font = fonts => fonts.Label, Color = UiColors.Text, Fit = true,
					Bounds = new UiRect( toggles.X, top, 330, 50 ), Anchor = UiAnchor.Center } );
				screen.Add( new UiLabel { Id = $"refusalCount{index}", Text = () => counters.Count( reason ).ToString( System.Globalization.CultureInfo.InvariantCulture ), Font = fonts => fonts.Label,
					Color = UiColors.Value, Align = UiAlign.Right, Fit = true, Bounds = new UiRect( toggles.X + 340, top, toggles.Width - 340, 50 ), Anchor = UiAnchor.Center } );
			}
		}
		// [BIN:TPW-EXE:0x004A52A0 top 3 thoughts] the frame (help 186), its title (UITEXT 0x9C, font slot 6) and the three icons: the ranking's slot 0 goes to 0x4A68D4 (the rightmost), slot 1 to 0x4A68D3 and slot 2 to 0x4A68D2
		var thoughts = table.Control( 0x4A68D1 );
		screen.Add( new UiModelImage { Id = "thoughtsFrame", Model = thoughts.Model!, Help = strings.Help( thoughts.Help ), Bounds = thoughts.Rect, Anchor = UiAnchor.Center } );
		var thoughtsTitle = table.Control( 0x4A68CE );
		screen.Add( new UiLabel { Id = "thoughtsTitle", Text = () => strings[UIStrings.Top3Thoughts], Font = fonts => fonts.Label, Color = UiColors.Text, Align = UiAlign.Center, Fit = true,
			Bounds = thoughtsTitle.Rect, Anchor = UiAnchor.Center } );
		foreach ( var (slot, control) in new[] { (2, 0x4A68D2u), (1, 0x4A68D3u), (0, 0x4A68D4u) } )
		{
			var icon = table.Control( control );
			var shown = slot;
			// [BIN:TPW-EXE:0x004A4700 icon hover] message 0xC reads the icon's frame (vtable +0x20), adds 1 and takes that entry of the THOUGHTS.str table 0x78BA9C (0x6ACE60) as its text; the visitor window reads the same table (0x4B7572)
			// [APPROX:UI-914] the text follows the frame shown; the icon class's frame getter (0x661596, vtable 0x7059F0 slot 8) is a stub that returns 0, so the original may name thought 1 for every icon
			screen.Add( new FrameIcon { Id = $"thought{slot}", Model = icon.Model!, Frame = () => ParkInfoIconFrame( shown ),
				HelpSource = () => ParkInfoIconFrame( shown ) is var frame and >= 0 ? strings.Thought( frame + 1 ) : null, Bounds = icon.Rect, Anchor = UiAnchor.Center } );
		}
		var okay = table.Model( "b_okay" );
		screen.Add( new UiButton { Id = "close", Model = okay.Model, Help = strings.Help( okay.Help ), Clicked = ClosePark, Bounds = okay.Rect, Anchor = UiAnchor.Center } );
		screen.Back = ClosePark;
		Array.Clear( parkInfoIconHidden );
		RefreshParkInfo();
		parkInfoScreen = screen;
		Stack.Push( screen );

		void ClosePark() => Stack.Pop();
	}

	/// <summary>The window's screen: refreshes the thoughts every second.</summary>
	private sealed class ParkInfoScreen : UiScreen
	{
		private readonly ParkHud owner;
		public ParkInfoScreen( ParkHud owner ) : base( "parkInfo" ) => this.owner = owner;
		public override bool Update( UiContext context, UiInput input )
		{
			owner.parkInfoAge += Math.Max( 0, context.Delta );
			// [BIN:TPW-EXE:0x004A4830 refresh] message 0x10 of timer 0x80083 (every 1000 ms) re-reads the counts and the thoughts
			if ( owner.parkInfoAge >= ParkInfoRefreshSeconds )
				owner.RefreshParkInfo();
			return base.Update( context, input );
		}
	}
}
