using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

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
	public void AttractionDetailShowsLiveWearAndExcitementAndHonestGaps()
	{
		var ride = new AttractionListRow( 1, "Zoom", BuildCategory.Rides, true, false, 6, 4, 73.6, null );
		var rideRows = ParkAttractionRoster.StatRows( ride );
		Assert.AreEqual( 3, rideRows.Length );
		Assert.AreEqual( (UIStrings.UsersLastMonthAttr, (string?)null), rideRows[0], "per-attraction monthly users are not simulated" );
		Assert.AreEqual( "74%", rideRows[1].Value, "state of repair is the live wear gauge, rounded to a whole percent" );
		Assert.IsNull( rideRows[2].Value, "remaining life is not simulated" );
		var sideshow = ParkAttractionRoster.StatRows( new AttractionListRow( 2, "Arcade", BuildCategory.Sideshows, true, false, 0, 0, 100, 82 ) );
		Assert.AreEqual( (UIStrings.ExcitementGame, "82%"), sideshow[1], "sideshow excitement is the bridge's live level" );
		Assert.AreEqual( 5, sideshow.Length );
		Assert.IsTrue( sideshow.Take( 1 ).Concat( sideshow.Skip( 2 ) ).All( row => row.Value == null ), "sideshow customers, profit and satisfaction are not simulated" );
		Assert.AreEqual( 4, ParkAttractionRoster.StatRows( new AttractionListRow( 3, "Burger", BuildCategory.Shops, true, false, 0, 0, 100, null ) ).Length );
		Assert.AreEqual( 0, ParkAttractionRoster.StatRows( new AttractionListRow( 4, "Well", BuildCategory.Features, true, false, 0, 0, 100, null ) ).Length,
			"features have no original statistic labels of their own" );
		Assert.AreEqual( "0%", ParkAttractionRoster.StatRows( new AttractionListRow( 5, "Old", BuildCategory.Rides, true, false, 0, 0, -20, null ) )[1].Value, "wear clamps at 0%" );
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

	[TestMethod]
	public void VisitorRowsListOnlyInParkGuestsInNumberOrder()
	{
		var guests = new[] { At( 9, GuestState.Queueing ), At( 2, GuestState.WalkingAround ), At( 12, GuestState.CrossingRoad ),
			At( 5, GuestState.Gone ), At( 3, GuestState.Using ), At( 7, GuestState.LeavingPark ) };
		var rows = ParkVisitorRoster.Rows( guests, IsInPark, byHappiness: false, descending: false );
		CollectionAssert.AreEqual( new[] { 2, 3, 7, 9 }, rows.Select( guest => guest.Id ).ToArray(),
			"queueing, riding and leaving visitors are in the park; arrivals and gone guests are not" );
		CollectionAssert.AreEqual( new[] { 9, 7, 3, 2 }, ParkVisitorRoster.Rows( guests, IsInPark, byHappiness: false, descending: true )
			.Select( guest => guest.Id ).ToArray(), "a repeated header press lists the numbers backwards" );
		Assert.AreEqual( 0, ParkVisitorRoster.Rows( guests, _ => false, false, false ).Count );
	}

	[TestMethod]
	public void VisitorStatusAndThoughtComeFromTheOriginalStringTables()
	{
		var kidStates = new string[16];
		kidStates[(int)GuestState.WalkingAround] = "Walking around";
		kidStates[(int)GuestState.Queueing] = "";
		Assert.AreEqual( "Walking around", ParkVisitorRoster.StatusOf( At( 1, GuestState.WalkingAround ), kidStates ) );
		Assert.AreEqual( GuestState.Queueing.ToString(), ParkVisitorRoster.StatusOf( At( 1, GuestState.Queueing ), kidStates ),
			"an empty table entry falls back to the state's name" );
		Assert.AreEqual( GuestState.Gone.ToString(), ParkVisitorRoster.StatusOf( At( 1, GuestState.Gone ), kidStates ),
			"state 255 is not a KIDSTATES entry" );
		var thoughts = new string[18];
		thoughts[(int)GuestThought.Hungry] = "I am hungry";
		var hungry = At( 1, thought: GuestThought.Hungry );
		Assert.AreEqual( "I am hungry", ParkVisitorRoster.ThoughtOf( hungry, thoughts ) );
		Assert.AreEqual( "", ParkVisitorRoster.ThoughtOf( At( 2 ), thoughts ), "no thought shows nothing" );
		Assert.AreEqual( "", ParkVisitorRoster.ThoughtOf( hungry, System.Array.Empty<string>() ), "a missing table shows nothing" );
	}

	[TestMethod]
	public void VisitorDetailShowsTheGuestsOwnLiveValues()
	{
		var guest = At( 14, GuestState.Queueing, happiness: 61.4f, money: 235, rides: 3, thought: GuestThought.Pleased );
		var kidStates = new string[16];
		kidStates[(int)GuestState.Queueing] = "Queuing";
		var rows = ParkVisitorRoster.StatRows( guest, kidStates, Money );
		Assert.AreEqual( (UIStrings.CurrentStatus, "Queuing"), rows[0], "selecting a visitor shows that visitor's state" );
		Assert.AreEqual( (UIStrings.CashRemainingVisitor, "$235"), rows[1], "cash is the guest's simulated money" );
		Assert.AreEqual( (UIStrings.TimeInParkVisitor, (string?)null), rows[2], "time in park is honestly not simulated" );
		Assert.AreEqual( (UIStrings.RidesRiddenVisitor, "3"), rows[3], "rides ridden is the guest's own count" );
		Assert.AreEqual( (UIStrings.HappinessVisitor, "61%"), rows[4] );
		Assert.AreEqual( 100, ParkVisitorRoster.HappinessPercent( 140 ), "happiness clamps at 100" );
		Assert.AreEqual( 0, ParkVisitorRoster.HappinessPercent( -10 ), "happiness clamps at 0" );
	}

	[TestMethod]
	public void VisitorSelectionHealsWhenTheSelectedGuestLeaves()
	{
		var rows = ParkVisitorRoster.Rows( new[] { At( 3 ), At( 8 ), At( 11 ) }, IsInPark, false, false );
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
