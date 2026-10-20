using System.Globalization;
using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>The bank dialog of tp.exe's loans table <c>0x7525a0</c> (built by <c>0x49fb30</c>).</summary>
public sealed partial class ParkHud
{
	// [BIN:TPW-EXE:0x7525a0 loans window] root window4 (186,30)-(2018,1007), the f_loans list 0x12c4bd (292,131)-(1804,727) with the content region (326,387)-(1704,704),
	// attribute 0x91 (header sort), six header buttons 0x10-0x15, the b_sloan/b_spayup radio pair 0x12c4be and the scroller (1738,335)-(1797,629) with b_up, b_down and
	// b_scroller; the summary frame 0x12c4c1 and the title 0x12c4ca; b_finance (help 235), b_entry (236), b_staffcost (237) and b_exit (2)
	private static readonly LayoutTable LoansTable = OriginalLayoutTables.LoansWindow;
	private static readonly UiRect LoanContent = LoansTable.Control( 0x12C4BD ).Inner!.Value;
	private static readonly UiRect LoanTrack = LoansTable.All.First( node => node.Model2 == "!slider" ).Rect;
	private static readonly UiRect LoanKnob = LoansTable.Model( "b_scroller" ).Rect;

	// [BIN:TPW-EXE:0x4a02b0 loans list row] the loans list's row factory makes each row as high as font slot 7's line height x 1536 / screen height + 6, computed once
	// ([0x7cb264]), the formula and slot of the buy list's rows (0x4ad240; the cells [0x755134] and [0x752934] both hold 7), so the two lists share one pitch
	// [EXT:list-row-pitch] the loans list takes the buy list's fixed pitch, a design choice (see BuyRowHeight)
	private const float LoanRowHeight = BuyRowHeight;
	/// <summary>Rows of the loans list that fit its content region at the row pitch.</summary>
	// [BIN:TPW-EXE:0x663718 list rows] a list shows (content bottom - content top) / its first row's height whole rows (+0x172): (704 - 387) / 41 = 7
	public static readonly int LoanVisibleRows = (int)(LoanContent.Height / LoanRowHeight);

	private static readonly (float Left, float Right)[] LoanColumns =
	{
		(332, 647), (663, 862), (880, 1078), (1092, 1283), (1306, 1484), (1507, 1701)
	};

	/// <summary>The summary rows top to bottom: the label control, the value control and the UITEXT label.</summary>
	// [BIN:TPW-EXE:0x49fc90 loans summary rows] the build gives 0x12c4c4 UITEXT 357 (Cash In) and 0x12c4c5 the money in at y 748, 0x12c4c9 UITEXT 360 (Loan Payments) and
	// 0x12c4c8 the repayments at y 799, 0x12c4c6 UITEXT 359 (Other Costs) and 0x12c4c7 the money out less the repayments at y 850, and 0x12c4c2 UITEXT 361 (Balance) and
	// 0x12c4c3 at y 902 (tp.exe's UITEXT ids above 237 are OpenTPW's + 1)
	internal static readonly (uint Label, uint Value, UIStrings Text)[] LoanSummaryRows =
	{
		(0x12C4C4, 0x12C4C5, UIStrings.CashIn), (0x12C4C9, 0x12C4C8, UIStrings.LoanPayments),
		(0x12C4C6, 0x12C4C7, UIStrings.OtherCosts), (0x12C4C2, 0x12C4C3, UIStrings.Balance)
	};

	private UiScreen? loansScreen;
	// [BIN:TPW-EXE:0x4a06b0 loans tab] the radio pair's message 0x101 sets the tab word [0x7cb260] (1 for b_spayup 0x12c4bf, else 0) and refills the list (0x49fa00); the
	// build (0x49fb30) chooses the radio from that word and nothing resets it, so the tab the player last chose stays for the session
	private bool outstandingLoans;
	private int selectedLoan = -1;
	private int loanFirstRow;
	private int loanSortColumn;
	private bool loanSortDescending;
	private ParkEconomy? LoansEconomy => Status.EconomySource as ParkEconomy;
	public bool LoansWindowOpen => loansScreen != null && Stack.Screens.Contains( loansScreen );

	internal readonly record struct LoanRow( int OfferIndex, int LenderNameIndex, long Amount, int AprPercent, int Months,
		long MonthlyRepayment, long FinalAmount );

	private IReadOnlyList<LoanRow> LoanRows()
	{
		if ( LoansEconomy is not { Features.Loans: true } economy )
			return Array.Empty<LoanRow>();
		var rows = outstandingLoans
			? economy.Loans.Select( account => new LoanRow( account.OfferIndex, account.LenderNameIndex, account.OriginalAmount,
				account.AprPercent, account.MonthsRemaining, account.MonthlyRepayment, account.RemainingBalance ) )
			: economy.AvailableLoans.Select( offer => new LoanRow( offer.Index, offer.LenderNameIndex, offer.Amount,
				offer.AprPercent, offer.Months, LoanMath.MonthlyRepayment( offer.Amount, offer.AprPercent, offer.Months ),
				LoanMath.TotalPayable( offer.Amount, offer.AprPercent, offer.Months ) ) );
		return SortLoanRows( rows.OrderBy( row => row.OfferIndex ), loanSortColumn, loanSortDescending, LenderText );
	}

	/// <summary>The rows of the loans list in display order for a sort <paramref name="column"/> and direction, from the rows in slot order.</summary>
	// [BIN:TPW-EXE:0x66403b sorted insert] a list with attribute 0x10 inserts each row (slot order, 0x49fa00) before the first row it compares less than (0x663edc): the lender
	// column (type 0, 0x6636b2) by wcscmp of the text (0x67c290), the others as 32-bit numbers, so equal keys keep the slot order in either direction
	// [BIN:TPW-EXE:0x662679 list sort default] a new list sorts by column 0 ascending (+0x168, +0x16a), and the loans window builds a new list each time it opens (0x49fb30;
	// its close clears the window word [0x7cb25c], 0x49f534), so every opening sorts by lender; a header press re-sorts as in the buy list (0x665a44)
	internal static IReadOnlyList<LoanRow> SortLoanRows( IEnumerable<LoanRow> rowsInSlotOrder, int column, bool descending, Func<LoanRow, string> lender )
	{
		var rows = rowsInSlotOrder.ToArray();
		if ( column == 0 )
			return descending ? rows.OrderByDescending( lender, StringComparer.Ordinal ).ToArray() : rows.OrderBy( lender, StringComparer.Ordinal ).ToArray();
		Func<LoanRow, int> key = column switch
		{
			1 => row => unchecked((int)row.Amount),
			2 => row => row.Months,
			3 => row => row.AprPercent,
			4 => row => unchecked((int)row.MonthlyRepayment),
			_ => row => unchecked((int)row.FinalAmount)
		};
		return descending ? rows.OrderByDescending( key ).ToArray() : rows.OrderBy( key ).ToArray();
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

	/// <summary>Refills the list for the current tab: from the top, with its first row chosen.</summary>
	// [BIN:TPW-EXE:0x66403b first row] the list's first added row is chosen (0x66525c(0), list row 0), so a filled list opens with its top row highlighted
	private void FillLoanList()
	{
		loanFirstRow = 0;
		selectedLoan = LoanRows().Count > 0 ? 0 : -1;
	}
	// [BIN:TPW-EXE:0x004A06B0 loan tabs] the dialog's open and each tab button build the list again, which runs every offer's credit test (0x0049FA00 → 0x004D06B0)
	private void SelectLoanTab( bool outstanding )
	{
		if ( LoansEconomy != null )
			level.Commands.Issue( new RefreshLoanOffersCommand() );
		outstandingLoans = outstanding;
		FillLoanList();
	}
	private void SortLoans( int column )
	{
		loanSortDescending = loanSortColumn == column && !loanSortDescending;
		loanSortColumn = column;
		selectedLoan = -1;
		loanFirstRow = 0;
	}

	/// <summary>Whether the loans list's scroller and its arrows show: only while the list has more rows than fit (0x664495, as in the buy list).</summary>
	private void UpdateLoanScrollVisibility()
	{
		if ( loansScreen == null )
			return;
		var shown = BuyScrollShown( LoanMaxFirstRow );
		foreach ( var id in new[] { "loanScroll", "scrollUp", "scrollDown" } )
			if ( loansScreen.Find( id ) is { } element )
				element.Visible = shown;
	}

	public void OpenLoansWindow()
	{
		if ( LoansWindowOpen || RejectReadOnlyAction() || LoansEconomy is not { Features.Loans: true } )
			return;
		SetInfoArm( false );
		level.CellTool.Cancel();
		loansScreen ??= CreateLoansScreen();
		loanSummary = LoansEconomy is { } economy ? LoanSummaryOf( economy ) : default;
		loanSortColumn = 0;
		loanSortDescending = false;
		SelectLoanTab( outstandingLoans );
		Stack.Push( loansScreen );
		loansScreen.Focus( loansScreen.Find( "loanList" ) );
		UpdateLoanScrollVisibility();
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
					else if ( !level.Commands.Issue( new RepayLoanCommand( row.OfferIndex, amount ) ).Accepted )
						ShowLoanFailure( strings[UIStrings.InsufficientFunds] );
				}
				else if ( current.IsBankrupt || current.AvailableLoans.FirstOrDefault( offer => offer.Index == row.OfferIndex ) is not { } offer || offer.Amount != amount )
					ShowLoanFailure( strings.Extra( OpenTpwText.NotAvailable ) );
				else
					level.Commands.Issue( new TakeLoanCommand( row.OfferIndex, amount ) );
				SelectLoan( 0 );
			}),
			(() => strings[UIStrings.No], () => Stack.Pop()) ) );
	}

	private void ShowLoanFailure( string message ) => Stack.Push( UiDialogs.Message( "loanFailure", () => message,
		(() => strings.Extra( OpenTpwText.Back ), () => Stack.Pop()) ) );

	// [DATA:LOANNAMES.str:0..17] LOANNAMES.str has 18 entries and every LoanInfo Lendername is 0..17, so the numbered fallback is unreachable with the shipped data; kept as a defensive default
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

	// The summary rows as they were when the window opened (the original fills them only when it builds the window).
	private (long CashIn, long OtherCosts, long LoanPayments, long Balance) loanSummary;

	/// <summary>The value a summary row with the label <paramref name="label"/> shows.</summary>
	internal static long LoanSummaryValue( (long CashIn, long OtherCosts, long LoanPayments, long Balance) summary, UIStrings label ) => label switch
	{
		UIStrings.CashIn => summary.CashIn, UIStrings.OtherCosts => summary.OtherCosts, UIStrings.LoanPayments => summary.LoanPayments, _ => summary.Balance
	};

	/// <summary>The loans window's summary: the last closed month's money in and out and the instalments of the loans now outstanding.</summary>
	// [BIN:TPW-EXE:0x0049fe00 loans summary] the window build 0x49fb30 reads the newest sample of the money-in history (+0x1FC94 ring, pushed with the +0x1FC90 total at the
	// month message 12 by 0x4c7720, 0 before the first) and of the money-out history (+0x1F5A4 ring, +0x1F5A0 total), sums the monthly repayment (+0x10) of every bought (+0x14)
	// loan slot of the eight (0x4d06b0), and shows Cash In = money in, Other Costs = money out − those repayments, Loan Payments = the repayments and Balance = money in − money out
	// (32-bit); taking or repaying a loan refills only the list (0x49f460 / 0x49f4a0 → 0x49fa00), so the rows keep their values until the window is built again
	internal static (long CashIn, long OtherCosts, long LoanPayments, long Balance) LoanSummaryOf( ParkEconomy economy )
	{
		var last = economy.Ledger.History.Count > 0 ? economy.Ledger.History[^1] : null;
		var moneyIn = unchecked((int)(last?.MoneyIn ?? 0));
		var moneyOut = unchecked((int)(last?.MoneyOut ?? 0));
		var repayments = 0;
		foreach ( var loan in economy.Loans )
			repayments = unchecked(repayments + (int)loan.MonthlyRepayment);
		return (moneyIn, unchecked(moneyOut - repayments), repayments, unchecked(moneyIn - moneyOut));
	}

	private UiScreen CreateLoansScreen()
	{
		var screen = new LoansScreen( this );
		var table = LoansTable;
		void Model( string id, LayoutNode node ) => screen.Add( new UiModelImage { Id = id, Model = node.Model!, Bounds = node.Rect, Anchor = UiAnchor.Center } );
		LayoutNode Control( uint id ) => table.Control( id );
		Model( "window", table.Root );
		Model( "listFrame", Control( 0x12C4BD ) );
		Model( "summaryFrame", Control( 0x12C4C1 ) );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[outstandingLoans ? UIStrings.GraphOutstandingLoans : UIStrings.GraphAvailableLoans],
			Font = fonts => fonts.Title, Color = UiColors.Title, Align = UiAlign.Center, Fit = true,
			Bounds = Control( 0x12C4CA ).Rect, Anchor = UiAnchor.Center } );
		var captions = new[] { UIStrings.LenderName, UIStrings.Amount, UIStrings.LoanTerm, UIStrings.InterestRate, UIStrings.MonthlyRepayment, UIStrings.TotalPayable };
		var outstandingCaptions = new[] { UIStrings.LenderName, UIStrings.OriginalAmount, UIStrings.MonthsRemaining, UIStrings.InterestRate, UIStrings.MonthlyRepayment, UIStrings.RemainingBalance };
		for ( var column = 0; column < LoanColumns.Length; column++ )
		{
			var index = column;
			screen.Add( new LoanHeader( this, index, () => strings[outstandingLoans ? outstandingCaptions[index] : captions[index]] )
				{ Id = $"header{index}", Bounds = Control( 0x10u + (uint)index ).Rect, Anchor = UiAnchor.Center } );
		}
		screen.Add( new LoanList( this ) { Id = "loanList", Bounds = LoanContent, Anchor = UiAnchor.Center } );
		screen.Add( new LoanScrollBar( this ) { Id = "loanScroll", Bounds = new UiRect( LoanKnob.X, LoanTrack.Y, LoanTrack.Right - LoanKnob.X, LoanTrack.Height ), Anchor = UiAnchor.Center } );
		// The arrows' parent is the type 3 slider (style 0x10) inside list 0x12C4BD. A held arrow repeats at 500 ms then 125 ms (0x6688b3, 0x668dd9) and a release after that is silent (0x668eff).
		void ScrollArrow( LayoutNode node, int step )
		{
			void Scroll() => ScrollLoans( step );
			screen.Add( new UiButton { Id = step < 0 ? "scrollUp" : "scrollDown", Model = node.Model, ClickSound = table.ClickSoundOf( node ),
				Clicked = Scroll, Repeat = UiButton.ScrollArrowRepeat( Scroll ), Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		ScrollArrow( table.Model( "b_up" ), -1 );
		ScrollArrow( table.Model( "b_down" ), 1 );
		var available = table.Model( "b_sloan" );
		var outstanding = table.Model( "b_spayup" );
		// The two tabs sit in radio group 0x12C4BE (type 8, style 0x11), so they play 0xBD.
		screen.Add( new UiButton { Id = "available", Model = available.Model, Help = strings.Help( available.Help ), ClickSound = table.ClickSoundOf( available ),
			Clicked = () => SelectLoanTab( false ), Selected = () => !outstandingLoans, Bounds = available.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "outstanding", Model = outstanding.Model, Help = strings.Help( outstanding.Help ), ClickSound = table.ClickSoundOf( outstanding ),
			Clicked = () => SelectLoanTab( true ), Selected = () => outstandingLoans, Bounds = outstanding.Rect, Anchor = UiAnchor.Center } );
		for ( var row = 0; row < LoanSummaryRows.Length; row++ )
		{
			var index = row;
			var (label, value, text) = LoanSummaryRows[index];
			screen.Add( new UiLabel { Id = $"summaryLabel{index}", Text = () => strings[text], Font = fonts => fonts.Label,
				Color = UiColors.Text, Fit = true, Bounds = Control( label ).Rect, Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = $"summaryValue{index}", Text = () => FormatMoney( LoanSummaryValue( loanSummary, text ) ), Font = fonts => fonts.Label,
				Color = UiColors.Value, Align = UiAlign.Right, Fit = true, Bounds = Control( value ).Rect, Anchor = UiAnchor.Center } );
		}
		UiButton TableButton( string id, string model, Action clicked, bool enabled = true )
		{
			var node = table.Model( model );
			return screen.Add( new UiButton { Id = id, Model = node.Model, Help = strings.Help( node.Help ), Clicked = clicked, Enabled = enabled,
				Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		TableButton( "finance", "b_finance", CloseLoansWindow );
		TableButton( "entry", "b_entry", OpenEntryPriceWindow, LoansEconomy != null );
		TableButton( "staffCosts", "b_staffcost", OpenStaffCostsWindow, LoansEconomy != null );
		TableButton( "exit", "b_exit", CloseLoansWindow );
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
			owner.UpdateLoanScrollVisibility();
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

		public bool HoverSelects => true;

		/// <summary>The pointer is over the row <paramref name="index"/> (-1: off the rows): a pointer move onto a row selects it, and the selection stays where it was when the pointer leaves.</summary>
		// [BIN:TPW-EXE:0x6656a0 list hover] the loans list has attribute 0x91 (0x7525a0), which includes 0x80: every pointer move over it selects the row under it, as for the buy list; the loans window (0x49f4e0) handles the click message 0x400 only (the confirmation) and no 0x401, so a hover moves nothing but the highlight; leaving the list changes nothing (0x665c35 has no leave handling)
		// [BIN:TPW-EXE:0x658840 pointer move] only a pointer move selects (UiScreen), so the arrow keys and the wheel keep their selection under a resting pointer
		public void Hover( int index, bool pointerMoved )
		{
			if ( pointerMoved && index >= 0 )
				owner.SelectLoan( index );
		}
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
		// The header's parent is list 0x12C4BD (type 7, style 0x91), which has bit 0x10, so the hook plays 0xBD.
		public override void Activate()
		{
			GameAudio.PlayUi( LayoutTable.ClickSoundFor( LoansTable.Control( 0x12C4BD ) ) );
			owner.SortLoans( column );
		}
		public override void Draw( UiContext context, bool focused, bool pressed ) =>
			context.DrawFittedText( context.Fonts.Label, text(), ScreenRect( context.Canvas ), focused ? UiColors.Highlight : UiColors.Text, UiAlign.Center );
	}

	private sealed class LoanScrollBar : UiElement, IUiDragTarget
	{
		private readonly ParkHud owner;
		public LoanScrollBar( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		private UiRect KnobRect() => ListScrollKnobRect( LoanTrack, LoanKnob, owner.loanFirstRow, owner.LoanMaxFirstRow );
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
