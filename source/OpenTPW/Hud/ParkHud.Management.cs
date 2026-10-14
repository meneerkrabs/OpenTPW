using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	private UiScreen? entryPriceScreen;
	private UiScreen? staffCostsScreen;
	public bool EntryPriceWindowOpen => entryPriceScreen != null && Stack.Screens.Contains( entryPriceScreen );
	public bool StaffCostsWindowOpen => staffCostsScreen != null && Stack.Screens.Contains( staffCostsScreen );

	// [APPROX:UI-061] detail layouts and unit adjustment controls reuse original widgets; original detail-screen handlers remain unqualified
	// their arrangement and one-pound adjustment increments are OpenTPW presentation choices.
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
		staffCostsScreen ??= CreateManagementScreen( "staffCosts", UIStrings.StaffTrainingBudgets );
		Stack.Push( staffCostsScreen );
	}

	/// <summary>Applies an admission-price action only to an editable park economy.</summary>
	public static bool TrySetEntryPrice( ParkEconomy? economy, bool readOnly, int price )
	{
		if ( readOnly || economy == null || price < 0 ) return false;
		economy.SetEntranceFee( price );
		return true;
	}

	/// <summary>
	/// Moves the park's door: open when closed, closed when open. The economy's open/close events command the
	/// entrance gate's script and close or open the park's attractions (<see cref="Level.Gate"/>); a closed park
	/// stops admissions at the booth.
	/// </summary>
	public void ToggleParkDoor()
	{
		if ( RejectReadOnlyAction() || FinanceEconomy is not { } economy )
			return;
		var opens = !economy.IsParkOpen;
		if ( opens )
			economy.OpenPark();
		else
			economy.ClosePark();
		// [BIN:TPW-EXE:0x00519EF0 park door routine] every press posts the advisor's game event 3 (open) or 4 (close), also when the park was already in that state or a bankrupt park refuses to open
		host?.ParkDoorPressed?.Invoke( opens );
	}

	/// <summary>Changes the model's monthly training allocation without charging money early.</summary>
	public static bool TrySetStaffTrainingBudget( ParkEconomy? economy, bool readOnly, StaffType role, long budget )
	{
		if ( readOnly || economy == null || !Enum.IsDefined( role ) || budget < 0 ) return false;
		economy.SetTrainingBudget( role, budget );
		return true;
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
					Clicked = () => { var current = value(); if ( delta < 0 ? current > 0 : current < (id == "entryPrice" ? int.MaxValue : long.MaxValue) ) change( current + delta ); },
					Bounds = Rect( delta < 0 ? 1480 : 1580, y, delta < 0 ? 1563 : 1663, y + 83 ), Anchor = UiAnchor.Center };
				screen.Add( button );
				adjustmentButtons.Add( (button, () => delta < 0 ? value() > 0 : value() < (id == "entryPrice" ? int.MaxValue : long.MaxValue)) );
			}
		}
		UiButton? door = null;
		if ( id == "entryPrice" )
		{
			Label( "priceLabel", () => strings[UIStrings.TicketPrice], Rect( 370, 300, 1000, 380 ) );
			Adjustment( "price", 300, () => FinanceEconomy?.EntranceFee ?? 0,
				value => TrySetEntryPrice( FinanceEconomy, level.IsReadOnlyVisit, (int)value ) );
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
				Selected = () => FinanceEconomy is { IsParkOpen: false },
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
					value => TrySetStaffTrainingBudget( FinanceEconomy, level.IsReadOnlyVisit, type, value ) );
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
				door.Help = strings.Help( FinanceEconomy is { IsParkOpen: true } ? 194 : 193 );
		}
		Refresh();
		screen.Updating = _ => Refresh();
		screen.Back = Close;
		return screen;
	}
}
