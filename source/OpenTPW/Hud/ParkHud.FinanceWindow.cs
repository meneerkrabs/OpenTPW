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

	private static readonly RgbaByte[] FinanceColors =
	{
		new( 248, 218, 71, 255 ), new( 134, 235, 134, 255 ), new( 99, 204, 255, 255 ), new( 255, 155, 81, 255 ),
		new( 214, 152, 255, 255 ), new( 255, 123, 193, 255 ), new( 255, 93, 93, 255 ), new( 208, 208, 208, 255 )
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
		if ( pendingItem != null || level.BuildEntry != null )
		{
			pendingItem = null;
			level.IsPlacing = false;
			level.BuildEntry = null;
		}
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
		var wasSelected = financeVisible.Contains( series );
		if ( series is FinanceSeries.BankBalance or FinanceSeries.ParkValue )
			financeVisible.Clear();
		else
		{
			financeVisible.Remove( FinanceSeries.BankBalance );
			financeVisible.Remove( FinanceSeries.ParkValue );
		}
		// [BIN:STP-PPC:0x101503b0] bank balance/park value exclude each other and all cash-flow series;
		// a cash-flow selection clears that pair while the six cash-flow selections remain independent.
		if ( !wasSelected )
			financeVisible.Add( series );
		else
			financeVisible.Remove( series );
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
			Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = Rect( 789, 74, 1261, 153 ), Anchor = UiAnchor.Center } );
		screen.Add( new FinanceGraph( this ) { Id = "graph", Bounds = Rect( 741, 291, 1638, 828 ), Anchor = UiAnchor.Center } );
		// [DATA:Mac finance table 0x49ee4:74356-74358] three b_stoggle period buttons, 1 / 3 / 12 years.
		foreach ( var (years, label, bounds) in new[]
		{
			(1, UIStrings.Year1, Rect( 792, 211, 1050, 281 )),
			(3, UIStrings.Year3, Rect( 1055, 211, 1313, 281 )),
			(12, UIStrings.Year12, Rect( 1318, 211, 1577, 281 ))
		} )
		{
			var selectedYears = years;
			screen.Add( new UiButton { Id = $"period{years}", Model = "b_stoggle", Text = () => strings[label],
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

	private sealed class FinanceGraph : UiElement
	{
		private readonly ParkHud owner;
		public FinanceGraph( ParkHud owner ) => this.owner = owner;

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var rect = ScreenRect( context.Canvas );
			var plot = rect.Inflate( -Math.Max( 8, context.Canvas.Scale * 10 ) );
			var months = owner.FinanceMonths();
			var selected = owner.financeVisible.OrderBy( series => series ).ToArray();
			if ( months.Count == 0 || selected.Length == 0 )
				return;
			var values = selected.Select( series => months.Select( month => FinanceValue( month, series ) ).ToArray() ).ToArray();
			var minimum = Math.Min( 0L, values.Min( series => series.Min() ) );
			var maximum = Math.Max( 0L, values.Max( series => series.Max() ) );
			if ( minimum == maximum )
				maximum = minimum + 1;
			// [APPROX:UI-060] line colours, plot scaling and the default selected series are OpenTPW choices; original graph samples/style need a capture.
			for ( var line = 1; line < 4; line++ )
				context.Batch.AddRectangle( new UiRect( plot.X, plot.Y + plot.Height * line / 4, plot.Width, Math.Max( 1, context.Canvas.Scale ) ), new RgbaByte( 75, 86, 125, 130 ) );
			for ( var s = 0; s < selected.Length; s++ )
			{
				var color = FinanceColors[(int)selected[s]];
				for ( var index = 0; index < months.Count; index++ )
				{
					var x = plot.X + plot.Width * (months.Count == 1 ? 0.5f : index / (float)(months.Count - 1));
					var y = plot.Bottom - plot.Height * (float)((values[s][index] - (double)minimum) / (maximum - (double)minimum));
					if ( index == 0 )
					{
						context.Batch.AddRectangle( new UiRect( x - 3, y - 3, 6, 6 ), color );
						continue;
					}
					var previousX = plot.X + plot.Width * (index - 1) / (months.Count - 1);
					var previousY = plot.Bottom - plot.Height * (float)((values[s][index - 1] - (double)minimum) / (maximum - (double)minimum));
					var delta = new NVector2( x - previousX, y - previousY );
					if ( delta.LengthSquared() < 0.01f )
						continue;
					var normal = NVector2.Normalize( new NVector2( -delta.Y, delta.X ) ) * Math.Max( 1, context.Canvas.Scale * 2 );
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
