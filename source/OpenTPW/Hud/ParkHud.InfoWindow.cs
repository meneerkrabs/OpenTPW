using OpenTPW.UI.Original;
using Veldrid;

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
	// [BIN:TPW-EXE:0x753C48 series mask] the graph's series mask and the period (0x7CB39C) are globals the window procedure keeps across openings; they start at the people-in-park series and 1 year
	// [APPROX:UI-914] the choice is kept across openings within a park (it lives on the park's HUD), where tp.exe's two globals persist for the whole process and so carry over to the next park
	private ParkInfoSeries? parkInfoSeriesChoice;
	private ParkInfoSeries parkInfoSeries { get => parkInfoSeriesChoice ?? ParkInfoGraph.InitialSeries; set => parkInfoSeriesChoice = value; }
	private int parkInfoPeriod;
	private int parkInfoPeople, parkInfoSad, parkInfoHappy;
	private long parkInfoVisitors;

	/// <summary>The series the window's graph shows.</summary>
	public ParkInfoSeries ParkInfoSelectedSeries => parkInfoSeries;
	/// <summary>The graph's period: 0 for 1 year, 1 for 3 years, 2 for 12 years.</summary>
	public int ParkInfoPeriod => parkInfoPeriod;
	/// <summary>The counts frame as last refreshed: the guests in the park and the visitors so far.</summary>
	public (int People, long Visitors) ParkInfoCounts => (parkInfoPeople, parkInfoVisitors);
	/// <summary>The happiness levels as last refreshed: the percent of the guests that are sad (happiness below 33) and happy (above 66).</summary>
	public (int Sad, int Happy) ParkInfoHappiness => (parkInfoSad, parkInfoHappy);

	/// <summary>Presses or releases a series toggle, as the window procedure does on its message 0x100 (a plain checkbox: the others keep their state).</summary>
	// [BIN:TPW-EXE:0x4A4B90 series toggle] each toggle sets or clears its bit of the mask by its pressed state (1 sets) and the window re-reads the scale; the toggles are independent of each other, unlike the finance graph's
	public void SetParkInfoSeries( ParkInfoSeries series, bool on ) => parkInfoSeries = on ? parkInfoSeries | series : parkInfoSeries & ~series;

	/// <summary>Chooses the graph's period (0, 1 or 2); any other index is ignored.</summary>
	// [BIN:TPW-EXE:0x4A4B90 period radio] the three buttons set the period index 0 (0x4A68C3), 1 (0x4A68C4) or 2 (0x4A68C2) and redraw the graph with the period's 12, 36 or 144 samples
	public void SetParkInfoPeriod( int period )
	{
		if ( period is >= 0 and <= 2 )
			parkInfoPeriod = period;
	}

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
		// [BIN:TPW-EXE:0x004A4830 counts and happiness] the timer message reads the guests in the park (0x4C9130) and the visitors so far (0x4C94C0, the lifetime counter +0x21C08) into the counts frame (written only when they change), and the sad (0x4C9750 with 0x21) and happy (0x4C9670 with 0x42) percents into the two bars
		if ( level.PublishedParkStats == null && level.Guests is { } owner )
		{
			parkInfoPeople = ParkGate.CountCensus( owner );
			parkInfoVisitors = (Status.EconomySource as ParkEconomy)?.Counters[ParkCounters.Admissions] ?? 0;
			(parkInfoSad, parkInfoHappy) = owner.GetHappinessLevels();
		}
		else
		{
			parkInfoPeople = level.PublishedParkStats?.Visitors ?? 0;
			parkInfoVisitors = 0;
			(parkInfoSad, parkInfoHappy) = (0, 0);
		}
	}

	/// <summary>The fill of the happiness bar: <c>(100 - happy) x 1024 / 100</c>, the bar under the sad one (0x4A68CF).</summary>
	// [BIN:TPW-EXE:0x004A4830 happiness bars] the bar of hap.wct (0x4A68CF) is set to (100 - the happy percent) << 10 / 100 and the bar of sad_undec.wct (0x4A68D0) to the sad percent << 10 / 100, both on the 0..1024 scale of a type-9 gauge
	public static int ParkInfoHappyBarValue( int happyPercent ) => ((100 - happyPercent) << 10) / 100;

	/// <summary>The fill of the sad bar: <c>sad x 1024 / 100</c> (0x4A68D0).</summary>
	public static int ParkInfoSadBarValue( int sadPercent ) => (sadPercent << 10) / 100;

	/// <summary>Closes the park information window and the screens above it.</summary>
	public void CloseParkInfoWindow()
	{
		if ( parkInfoScreen == null || !ParkInfoWindowOpen )
			return;
		while ( Stack.Top != parkInfoScreen && Stack.Screens.Count > 1 )
			Stack.Pop();
		if ( Stack.Top == parkInfoScreen )
			Stack.Pop();
	}

	/// <summary>
	/// The park information window (the original's table 0x7537D0): the line graph with its five series toggles and 1 / 3 / 12 year buttons, the counts frame, the happiness levels, the Top 3 Thoughts
	/// and the buttons to the visitors, staff and attractions windows. A read-only visit shows the statistics the owner published in the graph's place.
	/// </summary>
	// [BIN:TPW-EXE:0x004A52A0 park information window] table 0x7537D0 (OriginalLayoutTables.ParkInfoWindow): the window3 frame, the title 0x4A68C5 (UITEXT 0x96), the graph 0x4A68BB with its five toggles (b_ltoggle1..5, UITEXT 0x97..0x9B, pressed by the mask 0x753C48, initially 8) and the three period buttons
	// (b_stoggle, UITEXT 0x16A..0x16C, selected by 0x7CB39C), the Top 3 Thoughts frame "toughts" 0x4A68D1 with its title 0x4A68CE (UITEXT 0x9C, help 186) and three ti_hungry icons 0x4A68D2..0x4A68D4, the counts frame 0x4A68C9 (labels UITEXT 0x97 and 0x9E, values 0x4A68CC and 0x4A68CA), the happiness bars 0x4A68CF / 0x4A68D0 (UITEXT 0x9D on the first) and the buttons b_kids, b_allstaff and b_allthings
	// [EXT:park-summary] a visit has no history or guests: the published figures stand in the graph's area, as the visit's functional summary
	public void OpenParkInfoWindow()
	{
		if ( Stack.Screens.Any( screen => screen.Name == "parkInfo" ) )
			return;
		var table = ParkInfoTable;
		var screen = new ParkInfoScreen( this );
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = table.Root.Rect, Anchor = UiAnchor.Center } );
		var title = table.Control( 0x4A68C5 );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[UIStrings.ParkInformation], Font = fonts => fonts.Title, Shadow = true, Color = UiColors.Title, Align = UiAlign.Center, Fit = true,
			Bounds = title.Rect, Anchor = UiAnchor.Center } );
		// A read-only visit never runs the owner's economy: the window shows the statistics the owner published
		// (the original's visitor saw the park as saved; docs/reverse/ONLINE-VISIT.md), never simulated values.
		// [EXT:ONLINE-074] the published park's figures are shown in the park information window of a visit.
		var published = level.PublishedParkStats;
		var graphNode = table.Control( 0x4A68BB );
		var graph = graphNode.Inner!.Value;
		if ( published is { } publishedStats )
			AddPublishedStatistics( screen, publishedStats, graph );
		else
			AddParkInfoGraph( screen, table, graph );
		AddParkInfoCounts( screen, table, published != null );
		AddParkInfoNavigation( screen, table );
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
			// [BIN:TPW-EXE:0x004A4700 icon hover] message 0xC calls the icon's vtable +0x20, adds 1 and takes that entry of the THOUGHTS.str table 0x78BA9C (0x6ACE60) as its text
			// [BIN:TPW-EXE:0x0066DA4D frame setter] the icons are control type 6 of table 0x7537D0 (factory 0x65DD7B, case 0x65E057, constructor 0x66D9E0, vtable 0x705C58): slot 7 (+0x1C, 0x66DA4D), which 0x4A4AB0 calls with the thought id less 1, stores the frame at +0x13C before the base setter 0x661525, and slot 8 (+0x20, 0x66DAD2) returns it, so each icon names the THOUGHTS.str entry of its own frame plus 1
			screen.Add( new FrameIcon { Id = $"thought{slot}", Model = icon.Model!, Frame = () => ParkInfoIconFrame( shown ),
				HelpSource = () => ParkInfoIconFrame( shown ) is var frame and >= 0 ? strings.Thought( frame + 1 ) : null, Bounds = icon.Rect, Anchor = UiAnchor.Center } );
		}
		var okay = table.Model( "b_okay" );
		screen.Add( new UiButton { Id = "close", Model = okay.Model, Help = strings.Help( okay.Help ), Clicked = CloseParkInfoWindow, Bounds = okay.Rect, Anchor = UiAnchor.Center } );
		screen.Back = CloseParkInfoWindow;
		Array.Clear( parkInfoIconHidden );
		RefreshParkInfo();
		parkInfoScreen = screen;
		Stack.Push( screen );
	}

	/// <summary>The graph control with its five series toggles and its three period buttons.</summary>
	private void AddParkInfoGraph( UiScreen screen, LayoutTable table, UiRect graph )
	{
		screen.Add( new ParkInfoGraphElement( this ) { Id = "graph", Bounds = graph, Anchor = UiAnchor.Center } );
		// [BIN:TPW-EXE:0x004A52A0 series toggles] b_ltoggle1 (0x4A68BF) is the people in the park (UITEXT 0x97, bit 8), b_ltoggle2 (0x4A68BC) the arrival rate (0x98, bit 1), b_ltoggle3 (0x4A68BD) the average happiness (0x99, bit 2),
		// b_ltoggle4 (0x4A68BE) the average time in the park (0x9A, bit 4) and b_ltoggle5 (0x4A68C0) the park rating (0x9B, bit 0x10); each is a text control in font slot 6
		foreach ( var (control, series, label) in new[]
		{
			(0x4A68BFu, ParkInfoSeries.People, UIStrings.PeopleInPark), (0x4A68BCu, ParkInfoSeries.Arrivals, UIStrings.ArrivalRate), (0x4A68BDu, ParkInfoSeries.Happiness, UIStrings.AvgHappiness),
			(0x4A68BEu, ParkInfoSeries.Stay, UIStrings.AvgTimeInPark), (0x4A68C0u, ParkInfoSeries.Rating, UIStrings.ParkRating)
		} )
		{
			var node = table.Control( control );
			var shown = series;
			// The series toggles' parent is the graph panel (type 1, style 0x1), which lacks bit 0x10, so they play 0x1F.
			screen.Add( new UiButton { Id = $"series{series}", Model = node.Model, Text = () => strings[label], Font = fonts => fonts.Small, Help = strings.Help( node.Help ),
				ClickSound = table.ClickSoundOf( node ), Selected = () => parkInfoSeries.HasFlag( shown ), Clicked = () => SetParkInfoSeries( shown, !parkInfoSeries.HasFlag( shown ) ), Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		// [BIN:TPW-EXE:0x004A52A0 period buttons] 0x4A68C3 (period 0, UITEXT 0x16A), 0x4A68C4 (period 1, 0x16B) and 0x4A68C2 (period 2, 0x16C), each b_stoggle with help 183, 184 and 185 and font slot 6; the one the period names starts pressed (0x66A295)
		foreach ( var (control, period) in new[] { (0x4A68C3u, 0), (0x4A68C4u, 1), (0x4A68C2u, 2) } )
		{
			var node = table.Control( control );
			var shown = period;
			// The period buttons' parent is radio group 0x4A68C1 (type 8, style 0x11), so they play 0xBD.
			screen.Add( new UiButton { Id = $"period{period}", Model = node.Model, Text = () => strings[UIStrings.Year1 + (shown == 0 ? 0 : shown == 1 ? 1 : 2)], Font = fonts => fonts.Small, Help = strings.Help( node.Help ),
				ClickSound = table.ClickSoundOf( node ), Selected = () => parkInfoPeriod == shown, Clicked = () => SetParkInfoPeriod( shown ), Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		// [EXT:guest-insights] with the extension on, the refusal-reason counters stand in the two free strips beside the window's title, clear of the plot and the toggles: a title and the first two reasons on the left, the other three on the right; the counters are the simulation's passive observer, never saved, and a visit has none
		if ( FeatureGates.GuestInsights && level.Guests?.RefusalCounters is { } counters )
		{
			screen.Add( new UiLabel { Id = "refusalsTitle", Text = () => strings.Extra( OpenTpwText.RefusalsTitle ), Font = fonts => fonts.Label, Color = UiColors.Text, Align = UiAlign.Left, Fit = true,
				Bounds = new UiRect( 291, 78, 420, 28 ), Anchor = UiAnchor.Center } );
			var reasons = new (RefusalReason Reason, OpenTpwText Text)[]
			{
				(RefusalReason.TooExpensive, OpenTpwText.RefusalTooExpensive), (RefusalReason.QueueTooLong, OpenTpwText.RefusalQueueTooLong), (RefusalReason.TooScary, OpenTpwText.RefusalTooScary),
				(RefusalReason.TooBoring, OpenTpwText.RefusalTooBoring), (RefusalReason.RideUnavailable, OpenTpwText.RefusalRideUnavailable)
			};
			for ( var index = 0; index < reasons.Length; index++ )
			{
				var (reason, text) = reasons[index];
				// the first two reasons sit under the title on the left, the other three in a column on the right (table coordinates)
				var x = index < 2 ? 291f : 1230f;
				var top = index < 2 ? 106f + index * 28 : 78f + (index - 2) * 28;
				screen.Add( new UiLabel { Id = $"refusal{index}", Text = () => strings.Extra( text ), Font = fonts => fonts.Label, Color = UiColors.Text, Fit = true,
					Bounds = new UiRect( x, top, 300, 26 ), Anchor = UiAnchor.Center } );
				screen.Add( new UiLabel { Id = $"refusalCount{index}", Text = () => counters.Count( reason ).ToString( System.Globalization.CultureInfo.InvariantCulture ), Font = fonts => fonts.Label,
					Color = UiColors.Text, Align = UiAlign.Right, Fit = true, Bounds = new UiRect( x + 310, top, 100, 26 ), Anchor = UiAnchor.Center } );
			}
		}
	}

	/// <summary>The counts frame and the happiness levels: the guests in the park and the visitors so far, and the bar of the sad, undecided and happy shares.</summary>
	private void AddParkInfoCounts( UiScreen screen, LayoutTable table, bool visit )
	{
		// [BIN:TPW-EXE:0x004A52A0 counts frame] the frame 0x4A68C9 holds the labels 0x4A68CB (UITEXT 0x97) and 0x4A68CD (UITEXT 0x9E) as text controls in font slot 6 and the values 0x4A68CC (people in the park) and 0x4A68CA (visitors) as count cells (0x48FDE0); none gets the centring call 0x65C428
		// [BIN:TPW-EXE:0x48fe28 count cell alignment] the two values are drawn left-aligned: the count cell's constructor 0x48fde0 stores 0 in the alignment byte +0xd8 (0x48fe28) that the text drawer 0x48f830 switches on (0 left, 1 centred, 2 right), and only the centring call 0x65c428 (message 0xc00, handler 0x48fc40) would change it
		var frame = table.Control( 0x4A68C9 );
		screen.Add( new UiModelImage { Id = "countsFrame", Model = frame.Model!, Bounds = frame.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "peopleLabel", Text = () => strings[UIStrings.PeopleInPark], Font = fonts => fonts.Label, Color = UiColors.Text, Bounds = table.Control( 0x4A68CB ).Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "peopleValue", Text = () => parkInfoPeople.ToString( System.Globalization.CultureInfo.InvariantCulture ), Font = fonts => fonts.Label, Color = UiColors.Text,
			Bounds = table.Control( 0x4A68CC ).Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "visitorsLabel", Text = () => strings[UIStrings.TotalParkVisitors], Font = fonts => fonts.Label, Color = UiColors.Text, Bounds = table.Control( 0x4A68CD ).Rect, Anchor = UiAnchor.Center } );
		// [EXT:ONLINE-074] a visit's published statistics carry no visitor total
		screen.Add( new UiLabel { Id = "visitorsValue", Text = () => visit ? strings.Extra( OpenTpwText.NotAvailable ) : parkInfoVisitors.ToString( System.Globalization.CultureInfo.InvariantCulture ),
			Font = fonts => fonts.Label, Color = UiColors.Text, Bounds = table.Control( 0x4A68CA ).Rect, Anchor = UiAnchor.Center } );
		// [BIN:TPW-EXE:0x004A52A0 happiness bars] 0x4A68D0 is a !frame (help 187) holding the bar of sad_undec.wct and 0x4A68CF the bar of hap.wct with the caption UITEXT 0x9D (font slot 6, black, centred) over the same rectangle
		var bars = table.Control( 0x4A68D0 );
		screen.Add( new UiModelImage { Id = "happinessFrame", Model = bars.Model!, Help = strings.Help( bars.Help ), Bounds = bars.Rect, Anchor = UiAnchor.Center } );
		var barRect = table.Control( 0x4A68CF ).Rect;
		screen.Add( new ParkInfoHappinessBars( this ) { Id = "happinessBars", Bounds = barRect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "happinessCaption", Text = () => strings[UIStrings.HappinessLevels], Font = fonts => fonts.Label, Color = new RgbaByte( 0, 0, 0, 255 ), Align = UiAlign.Center, Fit = true,
			Shadow = false, Bounds = barRect, Anchor = UiAnchor.Center } );
	}

	/// <summary>The three buttons to the visitors, staff and attractions windows.</summary>
	private void AddParkInfoNavigation( UiScreen screen, LayoutTable table )
	{
		// [BIN:TPW-EXE:0x004A4830 navigation] b_kids (0x4A68C6) runs 0x493530 (All Visitors), b_allstaff (0x4A68C7) 0x496620 with -1 (All Staff, on its last tab) and b_allthings (0x4A68C8) 0x495aa0 with -1 (the attractions window, on its last category), each replacing this window
		var kids = table.Control( 0x4A68C6 );
		screen.Add( new UiButton { Id = "allVisitors", Model = kids.Model, Help = strings.Help( kids.Help ), Clicked = () => { CloseParkInfoWindow(); OpenVisitorWindow(); }, Bounds = kids.Rect, Anchor = UiAnchor.Center } );
		var staff = table.Control( 0x4A68C7 );
		screen.Add( new UiButton { Id = "allStaff", Model = staff.Model, Help = strings.Help( staff.Help ), Clicked = () => { CloseParkInfoWindow(); OpenStaffWindow(); ShowEmployedStaff( true ); }, Bounds = staff.Rect, Anchor = UiAnchor.Center } );
		var things = table.Control( 0x4A68C8 );
		screen.Add( new UiButton { Id = "allThings", Model = things.Model, Help = strings.Help( things.Help ), Clicked = () => { CloseParkInfoWindow(); OpenAttractionsWindow(); }, Bounds = things.Rect, Anchor = UiAnchor.Center } );
	}

	/// <summary>A visit's published statistics, written in the graph's area.</summary>
	private void AddPublishedStatistics( UiScreen screen, OpenTPW.Online.Packages.ParkStatsRecord stats, UiRect graph )
	{
		var rows = new (UIStrings Label, Func<string> Value)[]
		{
			(UIStrings.PeopleInPark, () => stats.Visitors.ToString( System.Globalization.CultureInfo.InvariantCulture )),
			(UIStrings.BankBalance, () => FormatMoney( stats.Balance )),
			(UIStrings.ParkValue, () => stats.Value.ToString( System.Globalization.CultureInfo.InvariantCulture )),
			(UIStrings.ParkRating, () => stats.Rating.ToString( System.Globalization.CultureInfo.InvariantCulture )),
			(UIStrings.AllStaff, () => strings.Extra( OpenTpwText.NotAvailable )),
		};
		for ( var index = 0; index < rows.Length; index++ )
		{
			var row = rows[index];
			screen.Add( new UiLabel { Id = $"label{index}", Text = () => strings[row.Label],
				Bounds = new UiRect( graph.X + 16, graph.Y + 14 + index * 105, 520, 80 ), Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = $"value{index}", Text = row.Value,
				Bounds = new UiRect( graph.X + 546, graph.Y + 14 + index * 105, 330, 80 ), Anchor = UiAnchor.Center } );
		}
		// The published in-game date and the read-only note close the visit's window.
		screen.Add( new UiLabel { Id = "published", Text = () => OnlineStrings.Get( OnlineLabel.PublishedOn ) + stats.Date,
			Font = fonts => fonts.Label, Bounds = new UiRect( graph.X + 16, graph.Bottom - 190, 870, 70 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "note", Text = () => OnlineStrings.Get( OnlineLabel.PublishedStatsNote ),
			Font = fonts => fonts.Label, Fit = true, Bounds = new UiRect( graph.X + 16, graph.Bottom - 110, 870, 70 ), Anchor = UiAnchor.Center } );
	}

	/// <summary>The happiness levels' two bars over one rectangle: sad_undec.wct (blue bar over an orange track) filled to the sad share, hap.wct (a clear bar over a yellow track) filled to the not-happy share.</summary>
	// [DATA:ui.wad sad_undec.wct, hap.wct] sad_undec.wct is 32 x 32 with a blue top half (the bar) and an orange bottom half (the track); hap.wct has a fully transparent top half and a yellow bottom half, so its bar clears the first (100 - happy) percent of the rectangle and its track leaves the happy share yellow: blue, orange, yellow from the left
	private sealed class ParkInfoHappinessBars( ParkHud owner ) : UiElement
	{
		public const string HappyArt = "/ui/textures/hap.wct", SadArt = "/ui/textures/sad_undec.wct";

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var rect = ScreenRect( context.Canvas );
			UiGauge.Draw( context.Batch, rect, ParkInfoSadBarValue( owner.parkInfoSad ) / (float)UiGauge.FullValue, SadArt );
			var cleared = Math.Clamp( ParkInfoHappyBarValue( owner.parkInfoHappy ) / (float)UiGauge.FullValue, 0f, 1f );
			if ( cleared < 1f )
			{
				var yellow = new UiRect( rect.X + rect.Width * cleared, rect.Y, rect.Width * (1f - cleared), rect.Height );
				context.Batch.AddQuad( UiTexture.Image( HappyArt ), yellow, new System.Numerics.Vector2( 0.5f / 32f + 31f * cleared / 32f, 16.5f / 32f ), new System.Numerics.Vector2( 31.5f / 32f, 31.5f / 32f ), RgbaByte.White );
			}
		}
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
