using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>The eight original finance graph series, in the Mac window's checkbox order.</summary>
public enum FinanceSeries { BankBalance, ParkValue, MoneyIn, GateTakings, ShopTakings, SideshowTakings, MoneyOut, StaffCosts }

/// <summary>The financial graph from Mac layout table <c>data:0x49ee4</c>.</summary>
public sealed partial class ParkHud
{
	// [DATA:Mac finance table 0x49ee4:74359-74366] toggle controls; labels are confirmed by docs/reverse/UI-MAP.md.
	private static readonly (FinanceSeries Series, UIStrings Label, string Model, UiRect Bounds)[] FinanceToggles =
	{
		(FinanceSeries.BankBalance, UIStrings.BankBalance, "b_ltoggle1", Rect( 287, 211, 729, 281 )),
		(FinanceSeries.ParkValue, UIStrings.ParkValue, "b_ltoggle2", Rect( 287, 286, 729, 357 )),
		(FinanceSeries.MoneyIn, UIStrings.MoneyIn, "b_ltoggle3", Rect( 287, 382, 729, 452 )),
		(FinanceSeries.GateTakings, UIStrings.GateTakings, "b_ltoggle4", Rect( 287, 457, 729, 528 )),
		(FinanceSeries.ShopTakings, UIStrings.ShopTakings, "b_ltoggle5", Rect( 287, 533, 729, 603 )),
		(FinanceSeries.SideshowTakings, UIStrings.SideshowTakings, "b_ltoggle6", Rect( 287, 608, 729, 679 )),
		(FinanceSeries.MoneyOut, UIStrings.MoneyOut, "b_ltoggle7", Rect( 287, 684, 729, 754 )),
		(FinanceSeries.StaffCosts, UIStrings.GraphStaffCosts, "b_ltoggle8", Rect( 287, 759, 729, 830 )),
	};

	// [BIN:TPW-EXE:0x49aa30 graph series] the series are bits 0..7 of the selection word 0x751fa0 in the toggles' row order (controls 0x12277, 0x1227b, 0x12279, 0x12278, 0x1227c, 0x1227d, 0x1227a, 0x1227e at table 0x751dac sit on the rows bank balance, park value, money in, gate, shop, sideshow, money out, staff costs); the sampler reads the ledger series at +0x1f100, 0x1f350, 0x1fc90, 0x1fee0, 0x20130, 0x20380, 0x1f5a0 and 0x1f7f0 and draws each in its colour record from 0x499f30 (set through 0x47ddc0 and 0x5f73a0)
	private static readonly RgbaByte[] FinanceColors =
	{
		new( 255, 255, 0, 255 ), new( 255, 0, 255, 255 ), new( 255, 0, 0, 255 ), new( 0, 189, 236, 255 ),
		new( 255, 144, 0, 255 ), new( 144, 211, 0, 255 ), new( 255, 117, 117, 255 ), new( 149, 88, 210, 255 )
	};

	private UiScreen? financeScreen;
	private int financeYears = 1;
	private readonly HashSet<FinanceSeries> financeVisible = new() { FinanceSeries.BankBalance };

	public bool FinanceWindowOpen => financeScreen != null && Stack.Screens.Contains( financeScreen );
	public int FinancePeriodYears => financeYears;
	public IReadOnlyCollection<FinanceSeries> FinanceVisibleSeries => financeVisible;
	private ParkEconomy? FinanceEconomy => Status.EconomySource as ParkEconomy;

	public void OpenFinanceWindow()
	{
		if ( FinanceWindowOpen )
			return;
		SetInfoArm( false );
		level.CellTool.Cancel();
		LeaveBuildTool();
		financeScreen ??= CreateFinanceScreen();
		if ( financeScreen.Find( "loans" ) is UiButton loans )
			loans.Enabled = FinanceEconomy?.Features.Loans == true;
		foreach ( var id in new[] { "entry", "staffCosts" } )
			if ( financeScreen.Find( id ) is UiButton detail ) detail.Enabled = FinanceEconomy != null;
		Stack.Push( financeScreen );
	}

	public void CloseFinanceWindow()
	{
		if ( !FinanceWindowOpen )
			return;
		while ( Stack.Top != financeScreen && Stack.Screens.Count > 1 )
			Stack.Pop();
		Stack.Pop();
	}

	public void SetFinancePeriod( int years )
	{
		if ( years is 1 or 3 or 12 )
			financeYears = years;
	}

	public void ToggleFinanceSeries( FinanceSeries series )
	{
		if ( !Enum.IsDefined( series ) )
			return;
		ApplyFinanceToggle( financeVisible, series );
	}

	/// <summary>Toggles <paramref name="series"/> in the selection as tp.exe's handler 0x49a2e0 does.</summary>
	// [BIN:TPW-EXE:0x49a420 series toggles] selecting the bank balance or the park value clears the six cash-flow series (mask 0xfc, 0x49ab80) and keeps the other of the pair; selecting a cash-flow series clears the pair (mask 0x03); deselecting clears just that series
	internal static void ApplyFinanceToggle( HashSet<FinanceSeries> visible, FinanceSeries series )
	{
		if ( visible.Remove( series ) )
			return;
		if ( series is FinanceSeries.BankBalance or FinanceSeries.ParkValue )
			visible.RemoveWhere( other => other is not (FinanceSeries.BankBalance or FinanceSeries.ParkValue) );
		else
			visible.RemoveWhere( other => other is FinanceSeries.BankBalance or FinanceSeries.ParkValue );
		visible.Add( series );
	}

	/// <summary>Closed months plus the live current month, restricted to the selected period (the training budget window's mini balance, TrainingBalanceText; the graph itself reads the closed months only, <see cref="TryFinanceSample"/>).</summary>
	public IReadOnlyList<LedgerMonth> FinanceMonths()
	{
		var economy = FinanceEconomy;
		if ( economy == null )
			return Array.Empty<LedgerMonth>();
		return economy.Ledger.History.Append( economy.Ledger.CurrentMonth( economy.ParkRating, economy.ParkValue ) )
			.TakeLast( financeYears * ParkCalendar.MonthsPerYear ).ToArray();
	}

	public static long FinanceValue( LedgerMonth month, FinanceSeries series ) => series switch
	{
		FinanceSeries.BankBalance => month.ClosingBalance,
		FinanceSeries.ParkValue => month.ParkValue,
		FinanceSeries.MoneyIn => month.MoneyIn,
		FinanceSeries.GateTakings => month[LedgerCategory.GateTakings],
		FinanceSeries.ShopTakings => month[LedgerCategory.ShopTakings],
		FinanceSeries.SideshowTakings => month[LedgerCategory.SideshowTakings],
		FinanceSeries.MoneyOut => month.MoneyOut,
		FinanceSeries.StaffCosts => month[LedgerCategory.StaffCosts],
		_ => 0
	};

	private UiScreen CreateFinanceScreen()
	{
		var screen = new UiScreen( "finance" );
		void Model( string id, string model, UiRect bounds ) => screen.Add( new UiModelImage { Id = id, Model = model, Bounds = bounds, Anchor = UiAnchor.Center } );
		// [DATA:Mac finance table 0x49ee4:74352,74354] w_park/window3 and f_graph/graph at their authored rectangles.
		Model( "window", "w_park", Rect( 208, 30, 1840, 1007 ) );
		Model( "graphFrame", "f_graph", Rect( 268, 199, 1648, 846 ) );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[UIStrings.FinancialInformation], Font = fonts => fonts.Title,
			Shadow = true, Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = Rect( 789, 74, 1261, 153 ), Anchor = UiAnchor.Center } );
		screen.Add( new FinanceGraph( this ) { Id = "graph", Bounds = Rect( 741, 291, 1638, 828 ), Anchor = UiAnchor.Center } );
		// [DATA:Mac finance table 0x49ee4:74356-74358] three b_stoggle period buttons, 1 / 3 / 12 years.
		// Finance table 0x751ca8 is not generated. These three are the year radio group (type 8, style 0x11), so they play 0xBD. The series toggles below are not that group and stay on 0x1F.
		foreach ( var (years, label, bounds) in new[]
		{
			(1, UIStrings.Year1, Rect( 792, 211, 1050, 281 )),
			(3, UIStrings.Year3, Rect( 1055, 211, 1313, 281 )),
			(12, UIStrings.Year12, Rect( 1318, 211, 1577, 281 ))
		} )
		{
			var selectedYears = years;
			screen.Add( new UiButton { Id = $"period{years}", Model = "b_stoggle", Text = () => strings[label], ClickSound = GameAudio.SelectClickEvent,
				Font = fonts => fonts.Small, Selected = () => financeYears == selectedYears, Clicked = () => SetFinancePeriod( selectedYears ),
				Bounds = bounds, Anchor = UiAnchor.Center } );
		}
		foreach ( var toggle in FinanceToggles )
		{
			var item = toggle;
			screen.Add( new UiButton { Id = $"series{item.Series}", Model = item.Model, Text = () => strings[item.Label],
				Font = fonts => fonts.Small, Selected = () => financeVisible.Contains( item.Series ), Clicked = () => ToggleFinanceSeries( item.Series ),
				Bounds = item.Bounds, Anchor = UiAnchor.Center } );
		}
		// [DATA:Mac finance table 0x49ee4:74367-74369] loans, staff-cost and entry buttons.
		screen.Add( new UiButton { Id = "loans", Model = "b_loans", Help = strings.Help( 219 ),
			Clicked = OpenLoansWindow, Enabled = FinanceEconomy?.Features.Loans == true,
			Bounds = Rect( 1688, 575, 1790, 677 ), Anchor = UiAnchor.Center } );
		// Management extensions reuse the original finance navigation icons.
		screen.Add( new UiButton { Id = "staffCosts", Model = "b_staffcost", Help = strings.Help( 218 ), Clicked = OpenStaffCostsWindow, Enabled = FinanceEconomy != null,
			Bounds = Rect( 1688, 467, 1790, 570 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "entry", Model = "b_entry", Help = strings.Help( 217 ), Clicked = OpenEntryPriceWindow, Enabled = FinanceEconomy != null,
			Bounds = Rect( 1688, 360, 1790, 462 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "exit", Model = "b_exit", Help = strings.Help( 1 ), Clicked = CloseFinanceWindow,
			Bounds = Rect( 1711, 820, 1794, 903 ), Anchor = UiAnchor.Center } );
		screen.Back = CloseFinanceWindow;
		return screen;
	}

	/// <summary>The finance series in selection-bit order (0..7 of the selection word 0x751FA0).</summary>
	private static readonly FinanceSeries[] FinanceSeriesOrder = Enum.GetValues<FinanceSeries>();

	/// <summary>The months the finance rings hold: the closed months, at most the ring's 144 entries.</summary>
	// [BIN:TPW-EXE:0x480460 sample count] a graph reads the ring's size once it has wrapped, else its head + 1, as the samples it can show; the rings of 0x240 bytes hold 144 entries, and a month's sample goes in when the month closes (0x4C7720 pushes each ring through 0x4CE290, the bank at 0x4C7771), so the running month is on no ring
	internal static int FinanceSamplesHeld( ParkEconomy economy ) => Math.Min( economy.Ledger.History.Count, ParkLedger.MaximumHistoryMonths );

	/// <summary>The sample <paramref name="age"/> closed months back (0: the newest) of a series; false past the months held.</summary>
	internal static bool TryFinanceSample( ParkEconomy economy, int age, FinanceSeries series, out long value )
	{
		value = 0;
		if ( age < 0 || age >= FinanceSamplesHeld( economy ) )
			return false;
		var history = economy.Ledger.History;
		value = FinanceValue( history[history.Count - 1 - age], series );
		return true;
	}

	/// <summary>
	/// The word the maximum scanner reads when no sample was above 0: the ring slot after the head. A wrapped ring's is its oldest month, and with the head at slot 143 it is
	/// the head word itself; an unwrapped ring's is a slot not yet written: a new park's is uninitialised heap (the ring is malloc'd, 0x67ACC0 at 0x5188C4, and its constructor 0x51AE30 does not clear it), read as 0 here
	/// ([EXT:original-undefined-reads]), and a loaded save's the raw saved word (loader 0x4C5D70, loop at 0x4C6307), which OpenTPW's own finance history, built from the ledger, does not carry (an original save's finance rings are not imported, SAVE-010).
	/// </summary>
	// [BIN:TPW-EXE:0x480263 max scanner quirk] 0x4801D0 leaves its index at -1 when nothing is above 0 and then still reads slot head + 1 (0x480273, 0x480287) and keeps it if it is above the maximum; the minimum scanner guards this case (0x480413)
	internal static long FinanceScaleSlotAfterHead( ParkEconomy economy, FinanceSeries series )
	{
		var ledger = economy.Ledger;
		var history = ledger.History;
		if ( history.Count < ParkLedger.MaximumHistoryMonths )
			return 0;
		// 144 closed months: the ring has gone round once its closes exceed 144 (counted since the ledger started, so an imported park's empty history does not shift the head)
		var closes = Math.Max( ledger.ClosesSinceStart, ParkLedger.MaximumHistoryMonths );
		var head = (int)((closes - 1) % ParkLedger.MaximumHistoryMonths);
		if ( head + 1 >= ParkLedger.MaximumHistoryMonths )
			return head;
		return closes > ParkLedger.MaximumHistoryMonths ? FinanceValue( history[0], series ) : 0;
	}

	private sealed class FinanceGraph : UiElement
	{
		private readonly ParkHud owner;
		/// <summary>The points of the series being drawn (the longest period, 144 samples, plus the extra oldest point).</summary>
		private readonly long[] points = new long[ParkLedger.MaximumHistoryMonths + 1];
		private readonly LabelCache[] labels = new LabelCache[8];
		public FinanceGraph( ParkHud owner ) => this.owner = owner;

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var rect = ScreenRect( context.Canvas );
			var economy = owner.FinanceEconomy;
			var samples = owner.financeYears * ParkCalendar.MonthsPerYear;
			// [BIN:TPW-EXE:0x49A7B0 graph scale] the scale runs from min(0, the lowest) to max(0, the highest) of the selected series' samples 0 .. the period's sample count (0x4801D0 and 0x480380 stop at the first sample the ring cannot read), in ten divisions
			long minimum = 0, maximum = 0;
			var any = false;
			if ( economy != null )
				foreach ( var series in FinanceSeriesOrder )
				{
					if ( !owner.financeVisible.Contains( series ) )
						continue;
					any = true;
					var positive = false;
					for ( var age = 0; age <= samples && TryFinanceSample( economy, age, series, out var value ); age++ )
					{
						positive |= value > 0;
						maximum = Math.Max( maximum, value );
						minimum = Math.Min( minimum, value );
					}
					if ( !positive )
						maximum = Math.Max( maximum, FinanceScaleSlotAfterHead( economy, series ) );
				}
			var pixel = GraphBackdropRenderer.Pixel( context.Canvas );
			var backdrop = new GraphBackdrop( (int)(rect.Width / pixel), (int)(rect.Height / pixel), samples, minimum, maximum );
			var canDrawSeries = GraphBackdropRenderer.Draw( context, rect, pixel, backdrop );
			if ( economy == null || !any || !canDrawSeries )
				return;
			var plot = backdrop.Plot;
			var font = context.Fonts.Small;
			// [BIN:TPW-EXE:0x480460 series points] n = min(period samples, closed months held); the buffer holds n + 1 values, the sample n months back down to the newest closed month, an unreadable sample (the one past the months held) keeping the previous value (0 at the start): a graph of few months fills from the left over x steps of width / samples and starts at 0 before its first month; the running month is not on the ring, so a new park draws one 0 point
			// [EXT:graph-pixel-adapter] the original draws into the control's bitmap at the display mode's pixel size, which follows the host's resolution; OpenTPW takes one pixel as 2 canvas units (the 1024 x 768 mode of the video; user-approved 2026-10-30)
			var n = Math.Min( samples, FinanceSamplesHeld( economy ) );
			foreach ( var series in FinanceSeriesOrder )
			{
				if ( !owner.financeVisible.Contains( series ) )
					continue;
				long last = 0;
				for ( var age = n; age >= 0; age-- )
				{
					if ( TryFinanceSample( economy, age, series, out var value ) )
						last = value;
					points[n - age] = last;
				}
				GraphBackdropRenderer.DrawSeries( context, points.AsSpan( 0, n + 1 ), samples, minimum, maximum, plot, FinanceColors[(int)series], rect, pixel, font, ref labels[(int)series] );
			}
		}
	}
}
