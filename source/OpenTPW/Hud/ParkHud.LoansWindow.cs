using System.Globalization;
using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>The bank dialog from the Mac loans table at <c>data:0x4a79c</c>.</summary>
public sealed partial class ParkHud
{
	private const int LoanVisibleRows = 7;
	// [APPROX:UI-063] Seven evenly spaced rows and scrollbar travel are inferred from the authored list content/track rectangles; the original row pitch and scrolling handler need a capture.
	private const float LoanRowHeight = 317f / LoanVisibleRows;
	private static readonly UiRect LoanContent = Rect( 326, 387, 1704, 704 );
	private static readonly UiRect LoanTrack = Rect( 1738, 335, 1797, 629 );
	private static readonly UiRect LoanKnob = Rect( 1730, 562, 1797, 629 );
	private static readonly (float Left, float Right)[] LoanColumns =
	{
		(332, 647), (663, 862), (880, 1078), (1092, 1283), (1306, 1484), (1507, 1701)
	};
	private static readonly UiRect[] LoanHeaders =
	{
		Rect( 327, 264, 652, 370 ), Rect( 657, 264, 868, 370 ), Rect( 873, 264, 1084, 370 ),
		Rect( 1089, 264, 1286, 370 ), Rect( 1291, 264, 1488, 370 ), Rect( 1493, 264, 1704, 370 )
	};
	private static readonly (UiRect Label, UiRect Value)[] LoanSummaryRows =
	{
		(Rect( 816, 748, 1094, 793 ), Rect( 1115, 748, 1280, 793 )),
		(Rect( 816, 799, 1094, 844 ), Rect( 1115, 799, 1280, 844 )),
		(Rect( 816, 850, 1094, 895 ), Rect( 1115, 850, 1280, 895 )),
		(Rect( 816, 902, 1094, 947 ), Rect( 1115, 902, 1280, 947 ))
	};

	private UiScreen? loansScreen;
	private bool outstandingLoans;
	private int selectedLoan = -1;
	private int loanFirstRow;
	private int loanSortColumn = -1;
	private bool loanSortDescending;
	private ParkEconomy? LoansEconomy => Status.EconomySource as ParkEconomy;
	public bool LoansWindowOpen => loansScreen != null && Stack.Screens.Contains( loansScreen );

	private readonly record struct LoanRow( int OfferIndex, int LenderNameIndex, long Amount, int AprPercent, int Months,
		long MonthlyRepayment, long FinalAmount );

	private IReadOnlyList<LoanRow> LoanRows()
	{
		if ( LoansEconomy is not { Features.Loans: true } economy )
			return Array.Empty<LoanRow>();
		IEnumerable<LoanRow> rows = outstandingLoans
			? economy.Loans.Select( account => new LoanRow( account.OfferIndex, account.LenderNameIndex, account.OriginalAmount,
				account.AprPercent, account.MonthsRemaining, account.MonthlyRepayment, account.RemainingBalance ) )
			: economy.AvailableLoans.Select( offer => new LoanRow( offer.Index, offer.LenderNameIndex, offer.Amount,
				offer.AprPercent, offer.Months, LoanMath.MonthlyRepayment( offer.Amount, offer.AprPercent, offer.Months ),
				LoanMath.TotalPayable( offer.Amount, offer.AprPercent, offer.Months ) ) );
		rows = loanSortColumn switch
		{
			0 => rows.OrderBy( row => row.LenderNameIndex ),
			1 => rows.OrderBy( row => row.Amount ),
			2 => rows.OrderBy( row => row.Months ),
			3 => rows.OrderBy( row => row.AprPercent ),
			4 => rows.OrderBy( row => row.MonthlyRepayment ),
			5 => rows.OrderBy( row => row.FinalAmount ),
			_ => rows.OrderBy( row => row.OfferIndex )
		};
		return (loanSortDescending ? rows.Reverse() : rows).ToArray();
	}

	private int LoanMaxFirstRow => Math.Max( 0, LoanRows().Count - LoanVisibleRows );
	private void ScrollLoans( int delta ) => loanFirstRow = Math.Clamp( loanFirstRow + delta, 0, LoanMaxFirstRow );
	private void SetLoanFirstRow( int first ) => loanFirstRow = Math.Clamp( first, 0, LoanMaxFirstRow );
	private void SelectLoan( int index )
	{
		var count = LoanRows().Count;
		selectedLoan = count == 0 ? -1 : Math.Clamp( index, 0, count - 1 );
		if ( selectedLoan < loanFirstRow ) loanFirstRow = selectedLoan;
		if ( selectedLoan >= loanFirstRow + LoanVisibleRows ) loanFirstRow = selectedLoan - LoanVisibleRows + 1;
		loanFirstRow = Math.Clamp( loanFirstRow, 0, LoanMaxFirstRow );
	}
	private void SelectLoanTab( bool outstanding )
	{
		outstandingLoans = outstanding;
		selectedLoan = -1;
		loanFirstRow = 0;
	}
	private void SortLoans( int column )
	{
		loanSortDescending = loanSortColumn == column && !loanSortDescending;
		loanSortColumn = column;
		selectedLoan = -1;
		loanFirstRow = 0;
	}

	public void OpenLoansWindow()
	{
		if ( LoansWindowOpen || RejectReadOnlyAction() || LoansEconomy is not { Features.Loans: true } )
			return;
		SetInfoArm( false );
		level.CellTool.Cancel();
		loansScreen ??= CreateLoansScreen();
		SelectLoanTab( false );
		Stack.Push( loansScreen );
		loansScreen.Focus( loansScreen.Find( "loanList" ) );
	}

	public void CloseLoansWindow()
	{
		if ( loansScreen == null || !LoansWindowOpen )
			return;
		while ( Stack.Top != loansScreen && Stack.Screens.Count > 1 )
			Stack.Pop();
		if ( Stack.Top == loansScreen )
			Stack.Pop();
	}

	private void ActivateLoan()
	{
		if ( level.IsReadOnlyVisit || LoansEconomy is not { Features.Loans: true } economy )
			return;
		var rows = LoanRows();
		if ( selectedLoan < 0 || selectedLoan >= rows.Count )
			return;
		var row = rows[selectedLoan];
		if ( outstandingLoans && row.FinalAmount > economy.Balance )
		{
			Stack.Push( UiDialogs.Message( "loanFunds", () => strings[UIStrings.InsufficientFunds],
				(() => strings.Extra( OpenTpwText.Back ), () => Stack.Pop()) ) );
			return;
		}
		var amount = outstandingLoans ? row.FinalAmount : row.Amount;
		var repayment = outstandingLoans;
		var prompt = repayment ? $"{strings[UIStrings.RemainingBalance]} {FormatMoney( amount )}?"
			: $"{strings[UIStrings.TakeOutLoan]} {amount.ToString( "#,0", CultureInfo.InvariantCulture )}?";
		Stack.Push( UiDialogs.Message( "loanConfirm", () => prompt,
			(() => strings[UIStrings.Yes], () =>
			{
				Stack.Pop();
				if ( level.IsReadOnlyVisit || LoansEconomy is not { Features.Loans: true } current )
				{
					ShowLoanFailure( strings.Extra( OpenTpwText.NotAvailable ) );
					return;
				}
				if ( repayment )
				{
					if ( current.Loans.FirstOrDefault( loan => loan.OfferIndex == row.OfferIndex ) is not { } loan || loan.RemainingBalance != amount )
						ShowLoanFailure( strings.Extra( OpenTpwText.NotAvailable ) );
					else if ( !current.TryRepayLoan( row.OfferIndex ) )
						ShowLoanFailure( strings[UIStrings.InsufficientFunds] );
				}
				else if ( current.IsBankrupt || current.AvailableLoans.FirstOrDefault( offer => offer.Index == row.OfferIndex ) is not { } offer || offer.Amount != amount )
					ShowLoanFailure( strings.Extra( OpenTpwText.NotAvailable ) );
				else
					current.TakeLoan( row.OfferIndex );
				SelectLoan( 0 );
			}),
			(() => strings[UIStrings.No], () => Stack.Pop()) ) );
	}

	private void ShowLoanFailure( string message ) => Stack.Push( UiDialogs.Message( "loanFailure", () => message,
		(() => strings.Extra( OpenTpwText.Back ), () => Stack.Pop()) ) );

	// [APPROX:UI-062] A missing lender-name entry has a numbered fallback rather than an invented institution name.
	private string LenderText( LoanRow row ) => strings.Lender( row.LenderNameIndex ) is { Length: > 0 } name ? name : $"#{row.LenderNameIndex + 1}";
	private string LoanCellText( LoanRow row, int column ) => column switch
	{
		0 => LenderText( row ),
		1 => FormatMoney( row.Amount ),
		2 => row.Months.ToString( CultureInfo.InvariantCulture ),
		3 => $"{row.AprPercent}%",
		4 => FormatMoney( row.MonthlyRepayment ),
		5 => FormatMoney( row.FinalAmount ),
		_ => ""
	};

	// [APPROX:UI-063] The original ledger uses lifetime park counters; the available ParkLedger history is bounded to 12 years, so older months can drop from these rows.
	private long LoanSummary( int row )
	{
		if ( LoansEconomy is not { } economy )
			return 0;
		var months = economy.Ledger.History.Append( economy.Ledger.CurrentMonth( 0, 0 ) );
		var cashIn = months.Sum( month => month.MoneyIn );
		var costs = months.Sum( month => month.MoneyOut );
		var loanPayments = months.Sum( month => month[LedgerCategory.LoanPayments] );
		return row switch { 0 => cashIn, 1 => costs - loanPayments, 2 => loanPayments, _ => cashIn - costs };
	}

	private UiScreen CreateLoansScreen()
	{
		var screen = new LoansScreen( this );
		void Model( string id, string model, UiRect bounds ) => screen.Add( new UiModelImage { Id = id, Model = model, Bounds = bounds, Anchor = UiAnchor.Center } );
		// [DATA:Mac loans table 0x4a79c] Root, list frame, summary frame and table-authored controls.
		Model( "window", "w_big", Rect( 186, 30, 2018, 1007 ) );
		Model( "listFrame", "f_loans", Rect( 292, 131, 1804, 727 ) );
		Model( "summaryFrame", "!frame", Rect( 792, 736, 1290, 964 ) );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[outstandingLoans ? UIStrings.GraphOutstandingLoans : UIStrings.GraphAvailableLoans],
			Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center, Fit = true,
			Bounds = Rect( 805, 80, 1277, 159 ), Anchor = UiAnchor.Center } );
		var captions = new[] { UIStrings.LenderName, UIStrings.Amount, UIStrings.LoanTerm, UIStrings.InterestRate, UIStrings.MonthlyRepayment, UIStrings.TotalPayable };
		var outstandingCaptions = new[] { UIStrings.LenderName, UIStrings.OriginalAmount, UIStrings.MonthsRemaining, UIStrings.InterestRate, UIStrings.MonthlyRepayment, UIStrings.RemainingBalance };
		for ( var column = 0; column < LoanHeaders.Length; column++ )
		{
			var index = column;
			screen.Add( new LoanHeader( this, index, () => strings[outstandingLoans ? outstandingCaptions[index] : captions[index]] )
				{ Id = $"header{index}", Bounds = LoanHeaders[index], Anchor = UiAnchor.Center } );
		}
		screen.Add( new LoanList( this ) { Id = "loanList", Bounds = LoanContent, Anchor = UiAnchor.Center } );
		screen.Add( new LoanScrollBar( this ) { Id = "loanScroll", Bounds = Rect( 1730, 335, 1797, 629 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "scrollUp", Model = "b_up", Clicked = () => ScrollLoans( -1 ),
			Bounds = Rect( 1737, 270, 1797, 330 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "scrollDown", Model = "b_down", Clicked = () => ScrollLoans( 1 ),
			Bounds = Rect( 1737, 636, 1797, 697 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "available", Model = "b_sloan", Help = strings.Help( 220 ), Clicked = () => SelectLoanTab( false ),
			Selected = () => !outstandingLoans, Bounds = Rect( 1455, 141, 1557, 244 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "outstanding", Model = "b_payup", Help = strings.Help( 221 ), Clicked = () => SelectLoanTab( true ),
			Selected = () => outstandingLoans, Bounds = Rect( 1585, 141, 1688, 244 ), Anchor = UiAnchor.Center } );
		var summaryLabels = new[] { UIStrings.CashIn, UIStrings.OtherCosts, UIStrings.LoanPayments, UIStrings.Balance };
		for ( var row = 0; row < LoanSummaryRows.Length; row++ )
		{
			var index = row;
			screen.Add( new UiLabel { Id = $"summaryLabel{index}", Text = () => strings[summaryLabels[index]], Font = fonts => fonts.Label,
				Color = UiColors.Text, Fit = true, Bounds = LoanSummaryRows[index].Label, Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = $"summaryValue{index}", Text = () => FormatMoney( LoanSummary( index ) ), Font = fonts => fonts.Label,
				Color = UiColors.Value, Align = UiAlign.Right, Fit = true, Bounds = LoanSummaryRows[index].Value, Anchor = UiAnchor.Center } );
		}
		screen.Add( new UiButton { Id = "finance", Model = "b_finance", Clicked = CloseLoansWindow,
			Bounds = Rect( 1866, 360, 1969, 462 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "entry", Model = "b_entry", Clicked = OpenEntryPriceWindow, Enabled = LoansEconomy != null,
			Bounds = Rect( 1866, 467, 1969, 570 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "staffCosts", Model = "b_staffcost", Clicked = OpenStaffCostsWindow, Enabled = LoansEconomy != null,
			Bounds = Rect( 1866, 575, 1969, 677 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "exit", Model = "b_exit", Help = strings.Help( 2 ), Clicked = CloseLoansWindow,
			Bounds = Rect( 1889, 820, 1972, 903 ), Anchor = UiAnchor.Center } );
		screen.Back = CloseLoansWindow;
		return screen;
	}

	private sealed class LoansScreen : UiScreen
	{
		private readonly ParkHud owner;
		public LoansScreen( ParkHud owner ) : base( "loans" ) => this.owner = owner;
		public override bool Update( UiContext context, UiInput input )
		{
			if ( input.Wheel != 0 )
			{
				owner.ScrollLoans( input.Wheel > 0 ? -1 : 1 );
				input = input with { Wheel = 0 };
			}
			return base.Update( context, input );
		}
	}

	private sealed class LoanList : UiElement, IUiRowList
	{
		private readonly ParkHud owner;
		public LoanList( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		public int Selected => owner.selectedLoan;
		public bool ArrowsSelect => true;
		public void Select( int index ) => owner.SelectLoan( index );
		public void Scroll( int rows ) => owner.ScrollLoans( rows );
		public override void Activate() => owner.ActivateLoan();
		public override void Adjust( int direction ) => owner.SelectLoan( Math.Max( 0, owner.selectedLoan ) + direction );
		public int RowAt( UiCanvas canvas, NVector2 point )
		{
			var content = canvas.Map( LoanContent, Anchor );
			if ( !content.Contains( point ) ) return -1;
			var visible = (int)MathF.Floor( (point.Y - content.Y) / (LoanRowHeight * canvas.Scale) );
			var index = owner.loanFirstRow + visible;
			return visible < 0 || visible >= LoanVisibleRows || index >= owner.LoanRows().Count ? -1 : index;
		}
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var rows = owner.LoanRows();
			for ( var visible = 0; visible < LoanVisibleRows && owner.loanFirstRow + visible < rows.Count; visible++ )
			{
				var index = owner.loanFirstRow + visible;
				var y = LoanContent.Y + visible * LoanRowHeight;
				var row = context.Canvas.Map( new UiRect( LoanContent.X, y, LoanContent.Width, LoanRowHeight ), Anchor );
				if ( index == owner.selectedLoan )
					context.Batch.AddRectangle( row, new RgbaByte( 60, 90, 200, focused ? (byte)170 : (byte)120 ) );
				for ( var column = 0; column < LoanColumns.Length; column++ )
				{
					var (left, right) = LoanColumns[column];
					var origin = context.Canvas.Map( new NVector2( left, y ), Anchor );
					var cell = new UiRect( origin.X, origin.Y, (right - left) * context.Canvas.Scale, LoanRowHeight * context.Canvas.Scale );
					context.DrawFittedText( context.Fonts.Label, owner.LoanCellText( rows[index], column ), cell,
						index == owner.selectedLoan ? UiColors.Highlight : UiColors.Text, column == 0 ? UiAlign.Left : UiAlign.Right );
				}
			}
		}
	}

	private sealed class LoanHeader : UiElement
	{
		private readonly ParkHud owner;
		private readonly int column;
		private readonly Func<string> text;
		public LoanHeader( ParkHud owner, int column, Func<string> text ) { this.owner = owner; this.column = column; this.text = text; }
		public override bool Focusable => Visible && Enabled;
		public override void Activate() => owner.SortLoans( column );
		public override void Draw( UiContext context, bool focused, bool pressed ) =>
			context.DrawFittedText( context.Fonts.Label, text(), ScreenRect( context.Canvas ), focused ? UiColors.Highlight : UiColors.Text, UiAlign.Center );
	}

	private sealed class LoanScrollBar : UiElement, IUiDragTarget
	{
		private readonly ParkHud owner;
		public LoanScrollBar( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		private UiRect KnobRect()
		{
			var travel = LoanTrack.Height - LoanKnob.Height;
			var top = LoanTrack.Y + (owner.LoanMaxFirstRow == 0 ? 0 : travel * owner.loanFirstRow / owner.LoanMaxFirstRow);
			return new UiRect( LoanKnob.X, top, LoanKnob.Width, LoanKnob.Height );
		}
		public void DragTo( UiCanvas canvas, NVector2 point )
		{
			var rect = ScreenRect( canvas );
			var travel = LoanTrack.Height - LoanKnob.Height;
			if ( rect.Height <= 0 || travel <= 0 || owner.LoanMaxFirstRow == 0 ) return;
			var authoredY = Bounds.Y + (point.Y - rect.Y) / rect.Height * Bounds.Height;
			var fraction = Math.Clamp( (authoredY - LoanTrack.Y - LoanKnob.Height / 2) / travel, 0, 1 );
			owner.SetLoanFirstRow( (int)MathF.Round( fraction * owner.LoanMaxFirstRow ) );
		}
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			context.DrawModel( "!slider", 0, context.Canvas.Map( LoanTrack, Anchor ) );
			context.DrawModel( "b_scroller", owner.LoanMaxFirstRow == 0 ? UiButton.DisabledFrame : pressed ? UiButton.DownFrame : focused ? UiButton.HighlightFrame : UiButton.NormalFrame,
				context.Canvas.Map( KnobRect(), Anchor ) );
		}
	}
}
