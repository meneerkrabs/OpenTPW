using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The All Attractions and All Visitors overview windows: the list models behind the windows
/// (rows from the live park, status and detail values, selection healing, scrolling) and the
/// visitor window's keyboard wiring. Each test drives a user action (select, sort, filter, delete,
/// key press) and asserts the model result it must produce.
/// </summary>
[TestClass]
public class OverviewWindowTests
{
	private static Guest At( int id, GuestState state = GuestState.WalkingAround, float happiness = 50, int money = 20, int rides = 0, GuestThought thought = GuestThought.None )
	{
		var guest = new Guest( id, 0 ) { State = state, Happiness = happiness, Money = money, RidesTaken = rides, Thought = thought };
		guest.Navigation.X = id * Fixed16.One;
		guest.Navigation.Y = id * Fixed16.One;
		return guest;
	}

	// The states GuestSimulation.IsInPark excludes (arrivals, departures and removed guests).
	private static readonly GuestState[] OutsidePark = { GuestState.CrossingRoad, GuestState.CrossingRoadToPark, GuestState.GoingToTicketBooth,
		GuestState.AtTicketBooth, GuestState.CrossingRoadHome, GuestState.WaitingToGoHome, GuestState.Gone };
	private static bool IsInPark( Guest guest ) => !OutsidePark.Contains( guest.State );

	private static string Money( long amount ) => "$" + amount.ToString( System.Globalization.CultureInfo.InvariantCulture );

	// ---- All Attractions ------------------------------------------------------------

	[TestMethod]
	public void AttractionRowsFilterByCategoryAndSortByNameWithStableIds()
	{
		var park = new[]
		{
			new AttractionListRow( 3, "Zoom", BuildCategory.Rides, true, false, 0, 0, 100, null ),
			new AttractionListRow( 1, "Arcade", BuildCategory.Sideshows, true, false, 0, 0, 100, null ),
			new AttractionListRow( 2, "Arcade", BuildCategory.Rides, true, false, 0, 0, 100, null ),
			new AttractionListRow( 4, "Burger", BuildCategory.Shops, true, false, 0, 0, 100, null ),
		};
		var rides = ParkAttractionRoster.Rows( park, BuildCategory.Rides, descending: false );
		CollectionAssert.AreEqual( new[] { "Arcade", "Zoom" }, rides.Select( row => row.Name ).ToArray(), "rides tab lists its category by name" );
		Assert.AreEqual( 2, rides[0].AttractionId, "the name tie keeps the lower attraction id first" );
		Assert.AreEqual( 4, ParkAttractionRoster.Rows( park, null, descending: false ).Count, "a null filter lists every category" );
		Assert.AreEqual( 1, ParkAttractionRoster.Rows( park, null, descending: false ).Count( row => row.Category == BuildCategory.Sideshows ) );
		var descending = ParkAttractionRoster.Rows( park, BuildCategory.Rides, descending: true );
		CollectionAssert.AreEqual( new[] { "Zoom", "Arcade" }, descending.Select( row => row.Name ).ToArray(), "a repeated header press reverses the list" );
		Assert.AreEqual( 0, ParkAttractionRoster.Rows( park, BuildCategory.Features, descending: false ).Count, "an empty category stays empty" );
	}

	[TestMethod]
	public void AttractionStatusUsesTheOriginalAbnormalStateStrings()
	{
		Assert.AreEqual( UIStrings.Blank, ParkAttractionRoster.StatusOf( new AttractionListRow( 1, "", BuildCategory.Rides, true, false, 0, 0, 100, null ) ), "an open working attraction shows no status" );
		Assert.AreEqual( UIStrings.Closed, ParkAttractionRoster.StatusOf( new AttractionListRow( 1, "", BuildCategory.Rides, false, false, 0, 0, 100, null ) ) );
		Assert.AreEqual( UIStrings.BrokenDown, ParkAttractionRoster.StatusOf( new AttractionListRow( 1, "", BuildCategory.Rides, true, true, 0, 0, 100, null ) ) );
		Assert.AreEqual( UIStrings.BrokenDown, ParkAttractionRoster.StatusOf( new AttractionListRow( 1, "", BuildCategory.Rides, false, true, 0, 0, 100, null ) ), "a broken attraction is broken down, open or not" );
	}

	[TestMethod]
	public void EachCategoryListsTheOriginalColumns()
	{
		// tp.exe 0x494ec0 / 0x494c70 / 0x494250 / 0x495110: headers UITEXT 82-99 with their help ids
		static (AttractionColumnKind, int, int)[] Of( BuildCategory category ) => ParkAttractionRoster.Columns( category ).Select( column => (column.Kind, (int)column.Header, column.Help) ).ToArray();
		CollectionAssert.AreEqual( new[] { (AttractionColumnKind.Name, 82, 105), (AttractionColumnKind.Users, 83, 106), (AttractionColumnKind.Excitement, 84, 107),
			(AttractionColumnKind.Repair, 85, 108), (AttractionColumnKind.Life, 86, 109) }, Of( BuildCategory.Rides ) );
		CollectionAssert.AreEqual( new[] { (AttractionColumnKind.Name, 87, 105), (AttractionColumnKind.Customers, 88, 584), (AttractionColumnKind.ProfitLastMonth, 89, 585),
			(AttractionColumnKind.TotalProfit, 90, 586), (AttractionColumnKind.Satisfaction, 91, 587) }, Of( BuildCategory.Shops ) );
		CollectionAssert.AreEqual( new[] { (AttractionColumnKind.Name, 92, 105), (AttractionColumnKind.Customers, 93, 584), (AttractionColumnKind.Excitement, 94, 107),
			(AttractionColumnKind.ProfitLastMonth, 95, 585), (AttractionColumnKind.TotalProfit, 96, 586), (AttractionColumnKind.Satisfaction, 97, 587) }, Of( BuildCategory.Sideshows ) );
		CollectionAssert.AreEqual( new[] { (AttractionColumnKind.Name, 98, 105), (AttractionColumnKind.NumberOwned, 99, 588) }, Of( BuildCategory.Features ) );
		Assert.IsTrue( ParkAttractionRoster.IsGauge( AttractionColumnKind.Repair ) && !ParkAttractionRoster.IsGauge( AttractionColumnKind.Users ) );
		Assert.IsTrue( ParkAttractionRoster.IsMoney( AttractionColumnKind.TotalProfit ) && !ParkAttractionRoster.IsMoney( AttractionColumnKind.Customers ) );
	}

	[TestMethod]
	public void TheListTablesGiveOneHeaderPerColumn()
	{
		// tables 0x750ab8 (rides and shops), 0x750ba0 (sideshows), 0x750ca0 (features): list 400 with header controls 0x10.. and the !slider scroll bar
		foreach ( var (table, columns) in new[] { (OriginalLayoutTables.RideShopList, 5), (OriginalLayoutTables.SideshowList, 6), (OriginalLayoutTables.FeatureList, 2) } )
		{
			Assert.AreEqual( 0x190u, table.Root.Id );
			Assert.AreEqual( columns, table.Root.Children.Count( node => node.Type == 2 && node.Id is >= 0x10 and < 0x20 ) );
			Assert.AreEqual( "!slider", table.Root.Children.Single( node => node.Type == 3 ).Model2 );
		}
		Assert.AreEqual( new UiRect( 275, 428, 1739 - 275, 875 - 428 ), OriginalLayoutTables.RideShopList.Root.Inner );
		var window = OriginalLayoutTables.AttractionsWindow;
		Assert.AreEqual( "window4", window.Root.Model );
		CollectionAssert.AreEqual( new[] { 111, 112, 113 }, new[] { "b_pkinfo", "b_allstaff", "b_kids" }.Select( model => window.Model( model ).Help ).ToArray() );
	}

	[TestMethod]
	public void ColumnsSortByTheirValuesWithUnknownValuesFirst()
	{
		var rows = new[]
		{
			new AttractionListRow( 1, "B", BuildCategory.Shops, true, false, 0, 0, 100, null ),
			new AttractionListRow( 2, "A", BuildCategory.Shops, true, false, 0, 0, 100, null ),
			new AttractionListRow( 3, "C", BuildCategory.Shops, true, false, 0, 0, 100, null ),
			new AttractionListRow( 4, "D", BuildCategory.Rides, true, false, 0, 0, 100, null ),
		};
		var customers = new Dictionary<int, double?> { [1] = 7, [2] = null, [3] = 2 };
		IReadOnlyList<AttractionCell> Cells( AttractionListRow row ) => new[] { new AttractionCell( row.Name, null, null ), new AttractionCell( null, null, customers[row.AttractionId] ) };
		CollectionAssert.AreEqual( new[] { 2, 3, 1 }, ParkAttractionRoster.Rows( rows, BuildCategory.Shops, 1, false, Cells ).Select( row => row.AttractionId ).ToArray() );
		CollectionAssert.AreEqual( new[] { 1, 3, 2 }, ParkAttractionRoster.Rows( rows, BuildCategory.Shops, 1, true, Cells ).Select( row => row.AttractionId ).ToArray() );
		CollectionAssert.AreEqual( new[] { 2, 1, 3 }, ParkAttractionRoster.Rows( rows, BuildCategory.Shops, 0, false, Cells ).Select( row => row.AttractionId ).ToArray(), "column 0 sorts by name" );
	}

	[TestMethod]
	public void AttractionSelectionHealsWhenTheParkRemovesTheSelectedRow()
	{
		var rows = ParkAttractionRoster.Rows( new[]
		{
			new AttractionListRow( 1, "Arcade", BuildCategory.Rides, true, false, 0, 0, 100, null ),
			new AttractionListRow( 2, "Zoom", BuildCategory.Rides, true, false, 0, 0, 100, null ),
		}, BuildCategory.Rides, descending: false );
		Assert.AreEqual( 2, ParkAttractionRoster.HealedSelection( rows, 2 ), "a still-listed attraction keeps the selection" );
		var remaining = rows.Where( row => row.AttractionId != 2 ).ToArray();
		Assert.AreEqual( 1, ParkAttractionRoster.HealedSelection( remaining, 2 ), "deleting the selection hands it to the first row" );
		Assert.AreEqual( 0, ParkAttractionRoster.HealedSelection( System.Array.Empty<AttractionListRow>(), 1 ), "an empty list clears the selection" );
		Assert.AreEqual( 0, ParkAttractionRoster.MaxFirstRow( 8, 10 ), "a list that fits needs no scrolling" );
		Assert.AreEqual( 2, ParkAttractionRoster.MaxFirstRow( 12, 10 ) );
	}

	[TestMethod]
	public void AttractionDeleteConfirmationsUseTheOriginalCategoryLabels()
	{
		Assert.AreEqual( UIStrings.DeleteRide, ParkHud.DeleteLabelOf( BuildCategory.Rides ) );
		Assert.AreEqual( UIStrings.DeleteShop, ParkHud.DeleteLabelOf( BuildCategory.Shops ) );
		Assert.AreEqual( UIStrings.DeleteSideshow, ParkHud.DeleteLabelOf( BuildCategory.Sideshows ) );
		Assert.AreEqual( UIStrings.DeleteItem, ParkHud.DeleteLabelOf( BuildCategory.Features ) );
	}

	// ---- All Visitors ---------------------------------------------------------------

	private static VisitorRowValues Values( Guest guest ) => ParkVisitorRoster.ValuesOf( guest, refreshed: true );

	[TestMethod]
	public void VisitorRowsListOnlyInParkGuestsInNumberOrder()
	{
		var guests = new[] { At( 9, GuestState.Queueing ), At( 2, GuestState.WalkingAround ), At( 12, GuestState.CrossingRoad ),
			At( 5, GuestState.Gone ), At( 3, GuestState.Using ), At( 7, GuestState.LeavingPark ) };
		var rows = ParkVisitorRoster.Rows( guests, IsInPark, Values, 0, descending: false );
		CollectionAssert.AreEqual( new[] { 2, 3, 7, 9 }, rows.Select( guest => guest.Id ).ToArray(),
			"queueing, riding and leaving visitors are in the park; arrivals and gone guests are not" );
		CollectionAssert.AreEqual( new[] { 9, 7, 3, 2 }, ParkVisitorRoster.Rows( guests, IsInPark, Values, 0, descending: true )
			.Select( guest => guest.Id ).ToArray(), "a repeated header press lists the numbers backwards" );
		Assert.AreEqual( 0, ParkVisitorRoster.Rows( guests, _ => false, Values, 0, false ).Count );
	}

	[TestMethod]
	public void EveryVisitorColumnSortsByItsRowValue()
	{
		var guests = new[] { At( 4, money: 50, rides: 1, happiness: 90, thought: GuestThought.Bored ), At( 6, money: 10, rides: 5, happiness: 20 ),
			At( 8, money: 30, rides: 3, happiness: 55, thought: GuestThought.Hungry ) };
		int[] Order( int column ) => ParkVisitorRoster.Rows( guests, IsInPark, Values, column, false ).Select( guest => guest.Id ).ToArray();
		CollectionAssert.AreEqual( new[] { 6, 8, 4 }, Order( 1 ), "cash remaining" );
		CollectionAssert.AreEqual( new[] { 4, 8, 6 }, Order( 3 ), "rides ridden" );
		CollectionAssert.AreEqual( new[] { 8, 4, 6 }, Order( 4 ), "the thought frame, no thought (999) last" );
		CollectionAssert.AreEqual( new[] { 6, 8, 4 }, Order( 5 ), "happiness" );
		CollectionAssert.AreEqual( new[] { 4, 6, 8 }, Order( 2 ), "equal times in the park keep the number order" );
	}

	[TestMethod]
	public void VisitorRowValuesFollowTheOriginalCells()
	{
		var guest = At( 14, GuestState.Queueing, happiness: 61.9f, money: 235, rides: 3, thought: GuestThought.Pleased );
		var values = Values( guest );
		Assert.AreEqual( (14, 235, (long?)null, 3), (values.Number, values.Cash, values.Hours, values.Rides), "number, cash, unknown time, rides" );
		Assert.AreEqual( (int)GuestThought.Pleased - 1, values.ThoughtFrame, "the ti_hungry frame is the thought - 1" );
		Assert.AreEqual( 61 * 1024 / 100, values.HappinessGauge, "the truncated happiness byte on the 1024 scale" );
		Assert.AreEqual( -1, Values( At( 2 ) ).ThoughtFrame, "no thought, no icon" );
		Assert.AreEqual( ParkVisitorRoster.NoThought, Values( At( 2 ) ).SortKey( 4 ) );
		Assert.AreEqual( (300 & 0xff) * 1024 / 100, Values( At( 3, happiness: 300 ) ).HappinessGauge, "only the low byte counts" );
	}

	[TestMethod]
	public void TimeInParkShowsHoursThenDays()
	{
		Assert.AreEqual( 0L, ParkVisitorRoster.DisplayHours( 3, refreshed: true ), "fewer than four turns are no whole step" );
		Assert.AreEqual( 4 * 3750 / 3600, ParkVisitorRoster.DisplayHours( 7, refreshed: true ), "one 4-turn step is 15000 park seconds" );
		Assert.AreEqual( 100L * 4 * 3750 / 3600, ParkVisitorRoster.DisplayHours( 403, refreshed: true ) );
		Assert.AreEqual( 100L * 3750 / 3600, ParkVisitorRoster.DisplayHours( 403, refreshed: false ), "a new row shows a quarter until the refresh (0x493800 without << 2)" );
		Assert.AreEqual( (UiTextRecord.Hours, 24L), (ParkVisitorRoster.DurationRecord( 24 ), ParkVisitorRoster.DurationValue( 24 )) );
		Assert.AreEqual( (UiTextRecord.Day, 1L), (ParkVisitorRoster.DurationRecord( 25 ), ParkVisitorRoster.DurationValue( 25 )) );
		Assert.AreEqual( (UiTextRecord.Day, 2L), (ParkVisitorRoster.DurationRecord( 48 ), ParkVisitorRoster.DurationValue( 48 )), "48 hours read \"2 day\"" );
		Assert.AreEqual( (UiTextRecord.Days, 2L), (ParkVisitorRoster.DurationRecord( 49 ), ParkVisitorRoster.DurationValue( 49 )) );
		var guest = At( 1 );
		guest.TimeInParkSeconds = 59.99;
		Assert.AreEqual( 240L * 3750 / 3600, Values( guest ).Hours, "241 turns of 248 ms, 60 whole steps" );
	}

	[TestMethod]
	public void VisitorSelectionHealsWhenTheSelectedGuestLeaves()
	{
		var rows = ParkVisitorRoster.Rows( new[] { At( 3 ), At( 8 ), At( 11 ) }, IsInPark, Values, 0, false );
		Assert.AreEqual( 8, ParkVisitorRoster.HealedSelection( rows, 8 ), "a visitor still in the park keeps the selection" );
		var remaining = rows.Where( guest => guest.Id != 8 ).ToArray();
		Assert.AreEqual( 3, ParkVisitorRoster.HealedSelection( remaining, 8 ), "a left visitor hands the selection to the first row" );
		Assert.AreEqual( 0, ParkVisitorRoster.HealedSelection( System.Array.Empty<Guest>(), 3 ), "an empty park clears the selection" );
	}

	// ---- Keyboard wiring ------------------------------------------------------------

	[TestMethod]
	public void TheVisitorsKeyOpensTheVisitorsWindowAndThePanKeysStayWithTheCamera()
	{
		Assert.AreEqual( HudScreenShortcut.VisitorWindow, ParkHud.ShortcutFor( InputButton.AllVisitors ), "V" );
		Assert.AreEqual( HudScreenShortcut.None, ParkHud.ShortcutFor( InputButton.AllAttractions ),
			"A stays unhandled: the host camera pans with A (Input's WASD axes), and one press cannot be both" );
		Assert.AreEqual( HudScreenShortcut.None, ParkHud.ShortcutFor( InputButton.AllStaff ), "S pans too" );
		Assert.IsTrue( ParkHud.OpensWindow( HudScreenShortcut.VisitorWindow ), "V is a window key: it may close its own window again" );
		Assert.IsTrue( ParkHud.TogglableWindowNames.Contains( "visitors" ) );
		Assert.IsTrue( ParkHud.TogglableWindowNames.Contains( "attractions" ) );
		Assert.IsTrue( ParkHud.WindowShortcutsAllowed( false, "visitors" ), "the visitors window accepts its own key on top" );
		Assert.IsTrue( ParkHud.WindowShortcutsAllowed( false, "attractions" ), "window keys act over the attractions window too" );
	}

	[TestMethod]
	public void VisitorWindowTogglesOpenAndCloseLikeTheOtherWindowKeys()
	{
		var opened = 0;
		var closed = 0;
		ParkHud.ToggleWindow( false, () => opened++, () => closed++ );
		Assert.AreEqual( (1, 0), (opened, closed), "a closed window opens" );
		ParkHud.ToggleWindow( true, () => opened++, () => closed++ );
		Assert.AreEqual( (1, 1), (opened, closed), "an open window closes on the same key" );
	}
}
