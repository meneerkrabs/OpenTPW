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

	/// <summary>Closed months plus the live current month, restricted to the selected period.</summary>
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

	/// <summary>A hollow 3 x 3 pixel mark of four one-pixel edges round (<paramref name="x"/>, <paramref name="y"/>).</summary>
	internal static void HollowMark( UiBatch batch, float x, float y, float pixel, RgbaByte color )
	{
		var size = 3 * pixel;
		var (left, top) = (x - size / 2, y - size / 2);
		batch.AddRectangle( new UiRect( left, top, size, pixel ), color );
		batch.AddRectangle( new UiRect( left, top + size - pixel, size, pixel ), color );
		batch.AddRectangle( new UiRect( left, top + pixel, pixel, size - 2 * pixel ), color );
		batch.AddRectangle( new UiRect( left + size - pixel, top + pixel, pixel, size - 2 * pixel ), color );
	}

	private sealed class FinanceGraph : UiElement
	{
		private readonly ParkHud owner;
		public FinanceGraph( ParkHud owner ) => this.owner = owner;

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var rect = ScreenRect( context.Canvas );
			var months = owner.FinanceMonths();
			var selected = owner.financeVisible.OrderBy( series => series ).ToArray();
			var hasSeries = months.Count > 0 && selected.Length > 0;
			var values = hasSeries ? selected.Select( series => months.Select( month => FinanceValue( month, series ) ).ToArray() ).ToArray() : Array.Empty<long[]>();
			var minimum = hasSeries ? Math.Min( 0L, values.Min( series => series.Min() ) ) : 0L;
			var maximum = hasSeries ? Math.Max( 0L, values.Max( series => series.Max() ) ) : 0L;
			var pixel = GraphBackdropRenderer.Pixel( context.Canvas );
			var backdrop = new GraphBackdrop( (int)(rect.Width / pixel), (int)(rect.Height / pixel), owner.financeYears * ParkCalendar.MonthsPerYear, minimum, maximum );
			var canDrawSeries = GraphBackdropRenderer.Draw( context, rect, pixel, backdrop );
			if ( !hasSeries || !canDrawSeries )
				return;
			var plot = GraphBackdropRenderer.ToScreen( backdrop.PlotFill, rect, pixel );
			// [BIN:TPW-EXE:0x47fe90 graph drawer] a flat range (max == min) puts every point on the baseline without dividing; the scale always includes 0, so that is an all-zero series and the one-unit span below only keeps the arithmetic finite
			if ( minimum == maximum )
				maximum = minimum + 1;
			// [BIN:TPW-EXE:0x47fb30 graph scale] the scale runs from min(0, the lowest selected sample of the period) to max(0, the highest) in ten divisions (0x49a7b0, 0x49a8f0); the bank balance alone is the default (0x751fa0 = 1)
			// [BIN:TPW-EXE:0x47fe90 graph drawer] each series is drawn as one-pixel lines from sample to sample (the line primitive 0x5f97f0, not a thick stroke), and when the period has fewer than 0x90 samples (the 1 and 3 year views: 0x480460 passes 12, 36 or 144) each sample end gets a hollow 3 x 3 pixel mark of four such lines (0x47ffe0..0x480077)
			// [APPROX:UI-060] the original draws into the control's bitmap at the display mode's pixel size; OpenTPW takes one pixel as 2 canvas units (the 1024 x 768 mode of the video) — evidence needed: the display mode the video ran in, and whether the 12 year view's 144 samples map one to one onto OpenTPW's months
			var marks = owner.financeYears < 12;
			for ( var s = 0; s < selected.Length; s++ )
			{
				var color = FinanceColors[(int)selected[s]];
				for ( var index = 0; index < months.Count; index++ )
				{
					var x = plot.X + plot.Width * (months.Count == 1 ? 0.5f : index / (float)(months.Count - 1));
					var y = plot.Bottom - plot.Height * (float)((values[s][index] - (double)minimum) / (maximum - (double)minimum));
					if ( index == 0 )
						continue;
					var previousX = plot.X + plot.Width * (index - 1) / (months.Count - 1);
					var previousY = plot.Bottom - plot.Height * (float)((values[s][index - 1] - (double)minimum) / (maximum - (double)minimum));
					if ( marks )
						HollowMark( context.Batch, x, y, pixel, color );
					var delta = new NVector2( x - previousX, y - previousY );
					if ( delta.LengthSquared() < 0.01f )
						continue;
					var normal = NVector2.Normalize( new NVector2( -delta.Y, delta.X ) ) * pixel / 2;
					var a = new UiVertex( new NVector2( previousX, previousY ) + normal, NVector2.Zero, color );
					var b = new UiVertex( new NVector2( previousX, previousY ) - normal, NVector2.Zero, color );
					var c = new UiVertex( new NVector2( x, y ) + normal, NVector2.Zero, color );
					var d = new UiVertex( new NVector2( x, y ) - normal, NVector2.Zero, color );
					context.Batch.AddTriangle( UiTexture.Solid, a, b, c );
					context.Batch.AddTriangle( UiTexture.Solid, c, b, d );
				}
			}
		}
	}
}
