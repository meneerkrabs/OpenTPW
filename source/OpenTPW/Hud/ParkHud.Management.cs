using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	private UiScreen? entryPriceScreen;
	private UiScreen? staffCostsScreen;
	// each training row's gauge fill, by role, set when the window opens (null without a park economy)
	private readonly float?[] trainingGaugeFills = new float?[Enum.GetValues<StaffType>().Length];
	public bool EntryPriceWindowOpen => entryPriceScreen != null && Stack.Screens.Contains( entryPriceScreen );
	public bool StaffCostsWindowOpen => staffCostsScreen != null && Stack.Screens.Contains( staffCostsScreen );

	// [EXT:entry-price-window] the entry-price window is a host layout over the economy values, a design choice: the original's table 0x751798 (window 0x4f3aa (328,130)-(1720,901), the
	// spinner 0x4f3ae (773,446)-(1197,619) of range 0..10000 and step 1 holding the entry price (0x498f3b-0x498f9e), the door button 0x4f3b1, three buttons at (1568, 303/411/518)
	// and the tick, handler 0x498c60) has no behaviour beyond setting the price, which the host layout does with the original's one-pound step and range
	public void OpenEntryPriceWindow()
	{
		if ( EntryPriceWindowOpen || FinanceEconomy == null ) return;
		SetInfoArm( false );
		level.CellTool.Cancel();
		entryPriceScreen ??= CreateManagementScreen( "entryPrice", UIStrings.PriceOfEntry );
		Stack.Push( entryPriceScreen );
	}

	public void OpenStaffCostsWindow()
	{
		if ( StaffCostsWindowOpen || FinanceEconomy == null ) return;
		SetInfoArm( false );
		level.CellTool.Cancel();
		staffCostsScreen ??= CreateTrainingBudgetScreen();
		SetTrainingGauges();
		Stack.Push( staffCostsScreen );
	}

	/// <summary>Sets the admission price through the park's commands (<see cref="SetEntranceFeeCommand"/>): refused for a read-only visit, a park without an economy or a negative price.</summary>
	public static bool TrySetEntryPrice( Level level, int price ) => level.Commands.Issue( new SetEntranceFeeCommand( price ) ).Accepted;

	/// <summary>
	/// Moves the park's door: open when closed, closed when open. The economy's open/close events command the
	/// entrance gate's script and close or open the park's attractions (<see cref="Level.Gate"/>); a closed park
	/// stops admissions at the booth.
	/// </summary>
	public void ToggleParkDoor()
	{
		if ( RejectReadOnlyAction() || FinanceEconomy is not { } economy )
			return;
		// [EXT:online-coop-commands] a guest's click asks the host for the opposite of the drawn door (C4b)
		if ( GuestManagement is { } queue )
		{
			RequestParkDoor( queue, !DrawnParkOpen );
			return;
		}
		var opens = !economy.IsParkOpen;
		level.Commands.Issue( new SetParkOpenCommand( opens ) );
		// [BIN:TPW-EXE:0x00519EF0 park door routine] every press posts the advisor's game event 3 (open) or 4 (close), also when the park was already in that state or a bankrupt park refuses to open
		host?.ParkDoorPressed?.Invoke( opens );
	}

	/// <summary>Changes the model's monthly training allocation without charging money early (<see cref="SetTrainingBudgetCommand"/>).</summary>
	public static bool TrySetStaffTrainingBudget( Level level, StaffType role, long budget ) =>
		level.Commands.Issue( new SetTrainingBudgetCommand( role, budget ) ).Accepted;

	/// <summary>
	/// The training-budget window (the T key) on the original table: the w_park frame (208,30)-(1840,1007), the mini balance at the left, one f_train row
	/// per role with its icon, the budget between the minus and plus buttons, and the tick with the entry price, finance and loans buttons.
	/// </summary>
	// [BIN:TPW-EXE:0x756400 training-budget table] window id 0x2d9, title box 0x2da (805,74)-(1277,153), tick (1711,820), b_entry 0x2db, b_finance 0x2dc and b_loans 0x2dd (help 176, 175, 174) in the right column, the mini balance frame 0x2de (267,183)-(765,411) with four label/value rows, and five f_train rows (0x2e7, 0x2ec, 0x2ea, 0x2eb, 0x2ed: help 164-168) of 150 units with the role icon (h_handy, h_mech, h_enter, h_guard, h_resrcher), b_minus, b_plus, the value in the row's text box and a gauge (type 9, help 169-173)
	// [DATA:video 22:07-22:08] a mini balance on the left (Inkomsten, Werknemerskosten, Overige kosten, Saldo) and five role rows with a role icon, a "$ n" value with the minus and plus buttons and a skill gauge
	// [BIN:TPW-EXE:0x4b2797 training-budget window] each row is a spinner (type 12) of range 0..10000 (0x66a5c4) and step 25 (0x66a68d) holding the role's budget; its b_minus and b_plus
	// move the value one step, clamped to the range (0x66a7e0 → 0x66a71c), and the handler 0x4b24b0 stores the new value (message 0x800, jump table 0x4b2728) as the role's budget
	// [BIN:TPW-EXE:0x4c9c00 staff grade share] the row's gauge (child 0x2e8, type 9) is set once when the window opens to share × 1024 / 100, share being the role's employees' summed
	// grade (+0x1e4) × 100 / (count × 5) in integers, 0 without employees (0x4b28e4..0x4b2906)
	private UiScreen CreateTrainingBudgetScreen()
	{
		var table = OriginalLayoutTables.TrainingBudget;
		var screen = new UiScreen( "staffCosts" );
		var adjustmentButtons = new List<(UiButton Button, Func<bool> Available)>();
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = table.Root.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[UIStrings.StaffTrainingBudgets], Font = fonts => fonts.Label, Color = UiColors.Title, Align = UiAlign.Center, Fit = true,
			Bounds = table.Control( 0x2DA ).Rect, Anchor = UiAnchor.Center } );
		// the mini balance: the current month's money in, staff costs, other costs and what is left
		var frame = table.Control( 0x2DE );
		screen.Add( new UiModelImage { Id = "balanceFrame", Model = frame.Model!, Bounds = frame.Rect, Anchor = UiAnchor.Center } );
		var labels = frame.Children.Where( node => node.Rect.X < 570 ).OrderBy( node => node.Rect.Y ).ToArray();
		var values = frame.Children.Where( node => node.Rect.X >= 570 ).OrderBy( node => node.Rect.Y ).ToArray();
		var balanceLabels = new[] { UIStrings.CashIn, UIStrings.StaffCosts, UIStrings.OtherCosts, UIStrings.Balance };
		for ( var row = 0; row < Math.Min( labels.Length, 4 ); row++ )
		{
			var index = row;
			screen.Add( new UiLabel { Id = $"balanceLabel{row}", Text = () => strings[balanceLabels[index]], Font = fonts => fonts.Small, Fit = true,
				ColorSource = () => index is 1 or 2 ? UiColors.Negative : UiColors.Text, Bounds = labels[row].Rect, Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = $"balanceValue{row}", Text = () => TrainingBalanceText( index ), Font = fonts => fonts.Small, Align = UiAlign.Right, Fit = true,
				ColorSource = () => index is 1 or 2 ? UiColors.Negative : UiColors.Text, Bounds = values[row].Rect, Anchor = UiAnchor.Center } );
		}
		// the roles' rows, found by the icon model of each
		var icons = new Dictionary<string, StaffType> { ["h_handy"] = StaffType.Handyman, ["h_mech"] = StaffType.Mechanic, ["h_enter"] = StaffType.Entertainer, ["h_guard"] = StaffType.Guard, ["h_resrcher"] = StaffType.Researcher };
		foreach ( var row in table.All.Where( node => node.Type == 12 ) )
		{
			var icon = row.Children.First( node => node.Model != null && icons.ContainsKey( node.Model ) );
			var role = icons[icon.Model!];
			var minus = row.Children.First( node => node.Model == "b_minus" );
			var plus = row.Children.First( node => node.Model == "b_plus" );
			screen.Add( new UiModelImage { Id = $"row{role}", Model = row.Model!, Bounds = row.Rect, Anchor = UiAnchor.Center } );
			screen.Add( new UiModelImage { Id = $"icon{role}", Model = icon.Model!, Bounds = icon.Rect, Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = $"budget{role}Value", Text = () => FormatMoney( FinanceEconomy?.Staff.GetTrainingBudget( role ) ?? 0 ), Font = fonts => fonts.Label,
				Color = UiColors.Text, Align = UiAlign.Center, Fit = true, Bounds = row.Inner!.Value, Anchor = UiAnchor.Center } );
			var gauge = row.Children.First( node => node.Type == 9 );
			screen.Add( new UiGaugeBar { Id = $"skill{role}", Value = () => trainingGaugeFills[(int)role],
				Help = strings.Help( gauge.Help ), Bounds = gauge.Rect, Anchor = UiAnchor.Center } );
			foreach ( var (node, delta) in new[] { (minus, -1), (plus, 1) } )
			{
				var step = delta;
				var button = screen.Add( new UiButton { Id = $"budget{role}" + (step < 0 ? "Decrease" : "Increase"), Model = node.Model, Help = strings.Help( row.Help ),
					Clicked = () => { var current = FinanceEconomy?.Staff.GetTrainingBudget( role ) ?? 0; var next = StepTrainingBudget( current, step ); if ( next != current ) TrySetStaffTrainingBudget( level, role, next ); },
					Bounds = node.Rect, Anchor = UiAnchor.Center } );
				adjustmentButtons.Add( (button, () => step < 0 ? (FinanceEconomy?.Staff.GetTrainingBudget( role ) ?? 0) > 0 : true) );
			}
		}
		void Close() { if ( Stack.Top == screen ) Stack.Pop(); }
		var tick = table.Model( "b_okay" );
		screen.Add( new UiButton { Id = "exit", Model = tick.Model, Help = strings.Help( tick.Help ), Clicked = Close, Bounds = tick.Rect, Anchor = UiAnchor.Center } );
		// the right column opens the entry price, finance and loans windows in place of this one
		foreach ( var (id, model, open) in new (string, string, Action)[] { ("entryPriceWindow", "b_entry", OpenEntryPriceWindow), ("financeWindow", "b_finance", OpenFinanceWindow), ("loansWindow", "b_loans", OpenLoansWindow) } )
		{
			var node = table.Model( model );
			var action = open;
			screen.Add( new UiButton { Id = id, Model = model, Help = strings.Help( node.Help ), Clicked = () => { Close(); action(); }, Bounds = node.Rect, Anchor = UiAnchor.Center } );
		}
		void Refresh()
		{
			foreach ( var (button, available) in adjustmentButtons )
				button.Enabled = !level.IsReadOnlyVisit && FinanceEconomy != null && available();
		}
		Refresh();
		screen.Updating = _ => Refresh();
		screen.Back = Close;
		return screen;
	}

	/// <summary>The training-budget spinners' range and step (tp.exe 0x4b2797: range 0..10000, step 25).</summary>
	public const long TrainingBudgetMaximum = 10000, TrainingBudgetStep = 25;

	/// <summary>A budget after one press of b_minus (<paramref name="direction"/> -1) or b_plus (+1): one step, clamped to 0..10000.</summary>
	public static long StepTrainingBudget( long current, int direction ) =>
		Math.Clamp( current + Math.Sign( direction ) * TrainingBudgetStep, 0, TrainingBudgetMaximum );

	/// <summary>The fill of a training row's gauge for the grades (0..4) of the role's employees: the integer share of grade 5, one above the best grade, then of 1024, so grade-4 staff fill 80% (tp.exe 0x4c9c00: sum × 100 / (count × 5); 0x4b28eb: × 1024 / 100).</summary>
	public static float TrainingGaugeFill( IReadOnlyCollection<int> grades )
	{
		if ( grades.Count == 0 )
			return 0;
		var share = grades.Sum() * 100 / (grades.Count * 5);
		return share * 1024 / 100 / 1024f;
	}

	/// <summary>Sets every training row's gauge from the employees' grades, as the original does once when the window opens (0x4b28e4..0x4b2906).</summary>
	private void SetTrainingGauges()
	{
		var economy = FinanceEconomy;
		foreach ( var role in Enum.GetValues<StaffType>() )
			trainingGaugeFills[(int)role] = economy == null ? null : TrainingGaugeFill( economy.Staff.OfType( role ).Select( member => member.Grade ).ToArray() );
	}

	private string TrainingBalanceText( int row )
	{
		if ( FinanceMonths() is not { Count: > 0 } months )
			return "";
		var month = months[^1];
		var staff = month[LedgerCategory.StaffCosts];
		return FormatMoney( row switch { 0 => month.MoneyIn, 1 => staff, 2 => month.MoneyOut - staff, _ => month.MoneyIn - month.MoneyOut } );
	}

	private UiScreen CreateManagementScreen( string id, UIStrings title )
	{
		var screen = new UiScreen( id );
		var adjustmentButtons = new List<(UiButton Button, Func<bool> Available)>();
		screen.Add( new UiModelImage { Id = "window", Model = "w_big", Bounds = Rect( 186, 30, 2018, 1007 ), Anchor = UiAnchor.Center } );
		void Label( string key, Func<string> text, UiRect bounds ) => screen.Add( new UiLabel { Id = key, Text = text,
			Font = fonts => fonts.Label, Color = UiColors.Text, Fit = true, Bounds = bounds, Anchor = UiAnchor.Center } );
		Label( "title", () => strings[title], Rect( 580, 80, 1600, 160 ) );
		void Adjustment( string key, float y, Func<long> value, Func<long, bool> change )
		{
			Label( key + "Value", () => FormatMoney( value() ), Rect( 1100, y, 1460, y + 75 ) );
			foreach ( var direction in new[] { -1, 1 } )
			{
				var delta = direction;
				var button = new UiButton { Id = key + (delta < 0 ? "Decrease" : "Increase"), Model = delta < 0 ? "b_sleft" : "b_sright",
					Clicked = () => { var current = value(); if ( delta < 0 ? current > 0 : current < (id == "entryPrice" ? int.MaxValue : TrainingBudgetMaximum) ) change( current + delta ); },
					Bounds = Rect( delta < 0 ? 1480 : 1580, y, delta < 0 ? 1563 : 1663, y + 83 ), Anchor = UiAnchor.Center };
				screen.Add( button );
				adjustmentButtons.Add( (button, () => delta < 0 ? value() > 0 : value() < (id == "entryPrice" ? int.MaxValue : TrainingBudgetMaximum)) );
			}
		}
		UiButton? door = null;
		if ( id == "entryPrice" )
		{
			// [BIN:TPW-EXE:0x00498D80 ticket price window] the park's name (0x4E2780 via 0x486280) is shown on the window's control 0x4F3AF, whose click (message 0x10006, handler 0x499060) opens the rename dialog 0x4990F0
			// [EXT:park-rename-layout] the name button's place in OpenTPW's entry price window and the rename dialog's layout are OpenTPW's; the original window and dialog resources (0x751798, 0x751928) are not decoded
			// [EXT:online-coop-commands] a guest's name not back from the host yet is drawn as asked (C4b)
			var parkNameButton = screen.Add( new UiButton { Id = "parkName", Text = () => DrawnParkName, Font = fonts => fonts.Label,
				Clicked = OpenParkRenameDialog, Bounds = Rect( 370, 180, 1663, 270 ), Anchor = UiAnchor.Center } );
			adjustmentButtons.Add( (parkNameButton, () => level.ParkName != null) );
			Label( "priceLabel", () => strings[UIStrings.TicketPrice], Rect( 370, 300, 1000, 380 ) );
			// [EXT:online-coop-commands] a guest's fee is drawn as asked until it comes back, and each step counts from it (C4b)
			Adjustment( "price", 300, () => DrawnEntranceFee,
				value => IssueManagement( ManagementTarget.Finance, ManagementKnob.Fee, new SetEntranceFeeCommand( (int)value ) ) );
			// The door: whether the park is open to visitors at all. The original's entryprice screen builds it
			// as b_door at (1350,678)-(1555,847) with help row 194, shown down for a closed park; its handler calls
			// the door routine with the switch's new state. [EXT:maexah/OpenTPW ParkEntryPriceScreen.cs (PC 0x00498d33, 0x00498fc3)]
			// [DATA:ui.wad b_door] [DATA:UIHELPTEXT 193/194] the help names the action the click performs (open when closed, close when open)
			door = screen.Add( new UiButton
			{
				Id = "door",
				Model = "b_door",
				Help = strings.Help( 194 ),
				Clicked = () => ToggleParkDoor(),
				// [EXT:online-coop-commands] a guest's door request not back from the host yet is drawn as asked (C4b)
				Selected = () => FinanceEconomy != null && !DrawnParkOpen,
				Bounds = Rect( 1350, 678, 1555, 847 ),
				Anchor = UiAnchor.Center
			} );
			adjustmentButtons.Add( (door, () => true) );
		}
		else
		{
			Label( "wagesLabel", () => strings[UIStrings.MonthlyWageStaff], Rect( 370, 210, 1000, 280 ) );
			Label( "wages", () => FormatMoney( FinanceEconomy?.Staff.TotalMonthlyWages ?? 0 ), Rect( 1100, 210, 1460, 280 ) );
			Label( "budgetLabel", () => strings[UIStrings.MonthlyBudget], Rect( 1100, 290, 1663, 350 ) );
			foreach ( var role in Enum.GetValues<StaffType>() )
			{
				var type = role;
				var y = 370 + (int)role * 95;
				Label( $"role{role}", () => StaffRole( type ), Rect( 370, y, 1000, y + 75 ) );
				Adjustment( $"budget{role}", y, () => FinanceEconomy?.Staff.GetTrainingBudget( type ) ?? 0,
					value => TrySetStaffTrainingBudget( level, type, value ) );
			}
		}
		void Close() { if ( Stack.Top == screen ) Stack.Pop(); }
		screen.Add( new UiButton { Id = "exit", Model = "b_exit", Help = strings.Help( 1 ), Clicked = Close,
			Bounds = Rect( 1889, 820, 1972, 903 ), Anchor = UiAnchor.Center } );
		void Refresh()
		{
			foreach ( var (button, available) in adjustmentButtons )
				button.Enabled = !level.IsReadOnlyVisit && FinanceEconomy != null && available();
			if ( door != null )
				door.Help = strings.Help( FinanceEconomy != null && DrawnParkOpen ? 194 : 193 );
		}
		Refresh();
		screen.Updating = _ => Refresh();
		screen.Back = Close;
		return screen;
	}

	/// <summary>
	/// The park rename dialog: the park name's two lines as two fields of 14 characters; OK stores them and redraws the gate
	/// sign (<see cref="Level.RenamePark"/>), the cross closes without a change.
	/// </summary>
	// [BIN:TPW-EXE:0x004990F0 park rename dialog] title UITEXT 0xC2 (Rename Item), fields 0x3943C and 0x3943D filled from the gate's lines (0x4E2860, 0x4E2920) with a limit of 14; its handler 0x4993E0 applies the fields on OK and closes on cancel
	internal void OpenParkRenameDialog()
	{
		if ( level.IsReadOnlyVisit || level.ParkName is not { } current ) return;
		// [EXT:online-coop-commands] reopening edits the latest requested name, not the park's older applied lines (C4b)
		if ( PendingManagement( ManagementTarget.Finance, ManagementKnob.Name ) is RenameParkCommand pending )
			current = new ParkNameLines( pending.Line1, pending.Line2 );
		var screen = new ShopPriceScreen( "parkRename" );
		var window = UiDialogs.CenteredWindow( 1100, 760 );
		UiDialogs.AddWindow( screen, window, "w_dialog", () => strings[UIStrings.RenameItem] );
		var (initial1, initial2) = current.DialogFields;
		UiTextField Field( string id, string text, float y ) => screen.Add( new UiTextField
		{
			Id = id, Text = text, MaximumLength = ParkNameLines.DialogFieldLength, Model = "f_text1",
			Bounds = new UiRect( window.X + 180, window.Y + y, 740, 100 ), Anchor = UiAnchor.Center
		} );
		var first = Field( "line1", initial1, 200 );
		var second = Field( "line2", initial2, 330 );
		void Cancel() { if ( Stack.Top == screen ) Stack.Pop(); }
		void Apply()
		{
			if ( Stack.Top != screen ) return;
			// [EXT:online-coop-commands] a guest's name goes to the host and the dialog closes at once; the window's name draws it until it comes back (C4b). A name the park would refuse
			// (lines a save could not hold, the secret name in a session) keeps the dialog open, as the host's refusal does
			if ( GuestManagement != null )
			{
				var lines = new ParkNameLines( first.Text, second.Text );
				if ( !lines.IsValid || SecretParkName.Matches( first.Text, second.Text ) ) return;
				if ( IssueManagement( ManagementTarget.Finance, ManagementKnob.Name, new RenameParkCommand( first.Text, second.Text ) ) ) Cancel();
				return;
			}
			// A refused name (a read-only visit, or lines a save could not hold) keeps the dialog open.
			if ( !level.Commands.Issue( new RenameParkCommand( first.Text, second.Text ) ).Accepted ) return;
			Cancel();
		}
		var tick = screen.Add( new UiButton { Id = "apply", Model = "b_okay", Clicked = Apply,
			Bounds = new UiRect( window.X + 180, window.Bottom - 180, 200, 100 ), Anchor = UiAnchor.Center } );
		// [BIN:TPW-EXE:0x4996ac rename Enter] Enter (0x802) in field 0x3943C only moves the focus to 0x3943D (0x499711, no 0x100); Enter in the other field makes the procedure 0x4993E0 post 0x100 with control -1 to its own window (0x49973e), so the hook clicks 0x1F by the window's style 1 and the tick applies; Escape (0x804) closes without a click. The gate's own rename dialog (0x48D370, table 0x750090, procedure 0x48D640) routes its keys the same way (0x48d83b)
		first.Submitted = () => screen.Focus( second );
		second.Submitted = tick.Activate;
		screen.Add( new UiButton { Id = "cancel", Model = "b_exit", Help = strings.Help( 2 ), Clicked = Cancel,
			Bounds = new UiRect( window.Right - 400, window.Bottom - 180, 200, 100 ), Anchor = UiAnchor.Center } );
		screen.Back = Cancel;
		Stack.Push( screen );
		screen.Focus( first );
	}
}
