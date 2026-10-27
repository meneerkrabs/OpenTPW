using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] [EXT:online-coop-claims] A guest's attraction windows through the session's management queue (C4b-2, docs/COOP.md "A guest's management windows"), by the HUD's
/// buttons and rows as a player clicks them, each checked on every peer at record and tick cuts: the ride door, the call button and the upgrade rows are open-state actions, one per object
/// at a time, greyed while theirs waits, and the window draws the object's record as the waiting action will leave it; the host and the park act between a guest's action and its record;
/// a toilet's door and call; the projection's own checks; the upgrade list and its outcomes; the claim guard at the click, before the send and at the record; and the host's and a single
/// player's windows, which issue at once.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>Ride <c>One</c>'s next level researched alike on every peer before the session starts (<c>ParkCommandLevelTests.ResearchedRide</c>), so its list offers a level row.</summary>
	private static void RideResearched( Park park, bool host )
	{
		EconomyTestData.HireBest( park.Economy, StaffType.Researcher );
		park.Economy.SetResearchEffort( ResearchCategory.Ride, 0 );
		for ( var day = 0; day < 100 && !park.Economy.Research.IsAvailable( 1100, 1 ); day++ )
			park.Economy.AdvanceDays( 1 );
		Assert.IsTrue( park.Economy.Research.IsAvailable( 1100, 1 ), "the ride's next level is researched" );
	}

	/// <summary>The park's mechanics dismissed, alike on every peer: nobody to call (UIHELPTEXT 574) or to install an upgrade.</summary>
	private static void NoMechanics( Park park, bool host )
	{
		foreach ( var member in park.Economy.Staff.OfType( StaffType.Mechanic ).ToList() )
			park.Economy.Fire( member.Id );
		Assert.IsFalse( park.Economy.Staff.OfType( StaffType.Mechanic ).Any() );
	}

	/// <summary>The mechanics on strike, alike on every peer (UIHELPTEXT 575).</summary>
	private static void MechanicsOnStrike( Park park, bool host )
	{
		var active = new bool[StaffStrikeBoard.Roles];
		active[(int)StaffType.Mechanic] = true;
		park.Economy.Staff.Strikes.Restore( new int[StaffStrikeBoard.Roles], active, new uint[StaffStrikeBoard.Roles] );
		Assert.IsTrue( park.Economy.Staff.Strikes.IsActive( StaffType.Mechanic ) );
	}

	/// <summary>A host and a guest who holds <paramref name="rights"/>, ride <c>One</c>'s next level researched (and <paramref name="prepare"/> run) alike on both parks, the guest's HUD up.</summary>
	private (Park Host, Park Guest, View View) AttractionsSession( Action<Park, bool>? prepare = null, CoopRights rights = CoopRights.Attractions )
	{
		var (host, guest, _, _) = Shared( ( park, isHost ) =>
		{
			RideResearched( park, isHost );
			prepare?.Invoke( park, isHost );
		} );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		if ( rights != CoopRights.None )
			GiveRights( host, guest, view, rights );
		return (host, guest, view);
	}

	/// <summary>
	/// Runs <paramref name="body"/> with the park camera, which the call and upgrade buttons need to be enabled (<c>PriceViewAllowed</c>), and puts the camera and the process's text entry back.
	/// </summary>
	private static void InParkView( Action body ) => WithPriceEditor( body );

	private static UiButton DoorButton( View view ) => (UiButton)view.Hud.Screen.Find( "openRide" )!;

	private static UiButton CallButton( View view ) => (UiButton)view.Hud.Screen.Find( "callStaff" )!;

	/// <summary>The status banner's text, none while it shows nothing.</summary>
	private static string Banner( View view ) => view.Hud.Screen.Find( "infoStatus" ) is ParkHud.StatusBanner { Visible: true } banner ? banner.Status : "";

	/// <summary>What the window's open-state controls draw: the door (closed is its down frame), the call button, the status banner and the info arm's open word.</summary>
	private readonly record struct Look( bool Closed, bool DoorEnabled, int DoorFrame, string? DoorHelp, bool Called, bool CallEnabled, int CallFrame, string? CallHelp, string Banner, bool InfoOpen );

	private static Look LookOf( View view )
	{
		var (door, call) = (DoorButton( view ), CallButton( view ));
		return new Look( door.Selected!(), door.Enabled, door.StateFrame( false, false ), door.Help, call.Selected!(), call.Enabled, call.StateFrame( false, false ), call.Help, Banner( view ),
			view.Hud.SelectedInfo!.IsOpen );
	}

	/// <summary>The door's help of a window as the table gives it: the prompt to close while it is open, to open while it is down (closed).</summary>
	private static string DoorHelp( View view, bool closed, bool ride = true )
	{
		var node = (ride ? OriginalLayoutTables.RideWindow : OriginalLayoutTables.ShopWindow).Model( "b_door" );
		return view.Strings.Help( closed ? node.AltHelp : node.Help );
	}

	/// <summary>The call button's frame while it is enabled, normal, and while greyed or subdued.</summary>
	private const int Normal = UiButton.NormalFrame, Greyed = UiButton.DisabledFrame;

	/// <summary>The object window of <paramref name="view"/> open on <paramref name="ride"/> and its frame run.</summary>
	private static void Show( View view, OriginalObject ride )
	{
		view.Hud.SelectObject( ride );
		view.Update();
	}

	/// <summary>The management queue's open-state action on attraction <paramref name="attraction"/> as the guest's window sees it.</summary>
	private static ParkCommand? Waiting( Park guest, int attraction ) => Management( guest ).OpenStateCommand( ManagementTarget.Attraction( attraction ) );

	/// <summary>The upgrade list opened from the shown window's button, as a click.</summary>
	private static void OpenUpgradeList( View view )
	{
		view.Update();
		var button = (UiButton)view.Hud.Screen.Find( "rideUpgrade" )!;
		Assert.IsTrue( button.Visible && button.Enabled, "the upgrade button" );
		button.Activate();
		Assert.AreEqual( "rideUpgrade", Top( view ), "the list opened" );
	}

	/// <summary>The list's selected row activated, as Enter does it.</summary>
	private static void ActivateRow( View view ) => view.Input( UiInput.Key( UiKeys.Accept ) );

	/// <summary>The kinds of the rows the open list shows (its waiting line has none).</summary>
	private static RideUpgradeRowKind?[] RowKinds( View view ) => view.Hud.UpgradeListRows().Select( row => row.Row?.Kind ).ToArray();

	/// <summary>The sounds the UI played while <paramref name="body"/> ran.</summary>
	private static List<uint> Sounds( Action body )
	{
		var played = new List<uint>();
		GameAudio.UiGlobalSoundObserver = played.Add;
		try
		{
			body();
		}
		finally
		{
			GameAudio.UiGlobalSoundObserver = null;
		}
		return played;
	}

	[TestMethod]
	public void AGuestsDoorWaitsGreyedTakesNoSecondClickAndDrawsTheRideAsItsRecordWillLeaveIt()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			Show( view, ride );
			var sent = Numbered( guest );
			// the park's door, drawn and enabled as the host's rules say
			Assert.AreEqual( new Look( false, true, Normal, DoorHelp( view, closed: false ), false, true, Normal, "help535", "", true ), LookOf( view ) );
			// a click asks for the opposite of the drawn word, as an open-state action nothing was sent for yet
			WindowClick( view, "openRide" );
			Assert.AreEqual( new SetAttractionOpenCommand( id, false ), Waiting( guest, id ) );
			Assert.AreEqual( sent, Numbered( guest ) );
			// a second click in the same frame is no click, and the next frame greys the door and the call button
			WindowClick( view, "openRide" );
			WindowClick( view, "callStaff" );
			Assert.AreEqual( 1, Management( guest ).Pending.Count() );
			view.Update();
			var closing = new Look( true, false, Greyed, DoorHelp( view, closed: true ), false, false, Greyed, "help535", view.Strings[UIStrings.Closed], false );
			Assert.AreEqual( closing, LookOf( view ), "the window draws the closed ride" );
			WindowClick( view, "openRide" );
			WindowClick( view, "callStaff" );
			Assert.AreEqual( 1, Management( guest ).Pending.Count(), "greyed: no second click" );
			Assert.AreEqual( (true, true, true), (ride.IsOpen, host.One.IsOpen, StateOf( guest, id ).IsOpen), "the projection wrote nothing to the park" );
			// the record comes: the door is closed on every peer and the window shows the park again, its buttons enabled
			UntilManaged( host, guest, view );
			Assert.AreEqual( sent + 1, Numbered( guest ), "one request" );
			Assert.AreEqual( (false, false), (host.One.IsOpen, ride.IsOpen) );
			Assert.AreEqual( new Look( true, true, UiButton.DownFrame, DoorHelp( view, closed: true ), false, true, Normal, "help535", view.Strings[UIStrings.Closed], false ), LookOf( view ) );
			CutAndCompare( host, guest );
			// the opposite click opens it again, drawn open while it waits
			WindowClick( view, "openRide" );
			Assert.AreEqual( new SetAttractionOpenCommand( id, true ), Waiting( guest, id ) );
			view.Update();
			Assert.AreEqual( new Look( false, false, Greyed, DoorHelp( view, closed: false ), false, false, Greyed, "help535", "", true ), LookOf( view ) );
			Assert.IsFalse( ride.IsOpen, "the park's door is still closed" );
			UntilManaged( host, guest, view );
			Assert.AreEqual( (true, true), (host.One.IsOpen, ride.IsOpen) );
			Assert.AreEqual( new Look( false, true, Normal, DoorHelp( view, closed: false ), false, true, Normal, "help535", "", true ), LookOf( view ) );
			Rounds( 90, host, guest );
			CutAndCompare( host, guest );
			Assert.AreEqual( (0, 0, sent + 2), (guest.Level.SessionRefusals, Management( guest ).TakeNotes().Count, Numbered( guest )), "no refusal, no note" );
			Show( view, null! );
			Assert.AreEqual( 0, view.Hud.Messages.Count );
		} );
	}

	[TestMethod]
	public void AGuestsCallWaitsGreyedAndACancelledCallDrawsTheDoorOpenAndTheBannerWithoutTheCall()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			Show( view, ride );
			var sent = Numbered( guest );
			// the call: the flag set and the ride closed while it waits, its status the repair's wait, the button down with the cancel's help
			WindowClick( view, "callStaff" );
			Assert.AreEqual( new RequestMaintenanceCommand( id, true ), Waiting( guest, id ) );
			WindowClick( view, "callStaff" );
			WindowClick( view, "openRide" );
			Assert.AreEqual( 1, Management( guest ).Pending.Count(), "no second click" );
			view.Update();
			Assert.AreEqual( new Look( true, false, Greyed, DoorHelp( view, closed: true ), true, false, Greyed, "help570", view.Strings[UIStrings.RepairWaitingForMechanic], false ), LookOf( view ) );
			Assert.AreEqual( (false, true, true), (StateOf( guest, id ).MaintenanceRequested, ride.IsOpen, StateOf( guest, id ).IsOpen), "the park is as it was" );
			UntilManaged( host, guest, view );
			Assert.AreEqual( sent + 1, Numbered( guest ) );
			foreach ( var park in new[] { host, guest } )
				Assert.AreEqual( (true, false), (StateOf( park, id ).MaintenanceRequested, park.One.IsOpen), "the call is set and closes the ride on every peer" );
			CutAndCompare( host, guest );
			// the call as the park shows it: its banner names the service, whoever is on the way
			Assert.IsTrue( LookOf( view ).Called );
			// the cancel: the flag cleared and the ride open, the banner without the call, the door the guard allows
			WindowClick( view, "callStaff" );
			Assert.AreEqual( new RequestMaintenanceCommand( id, false ), Waiting( guest, id ) );
			view.Update();
			Assert.AreEqual( new Look( false, false, Greyed, DoorHelp( view, closed: false ), false, false, Greyed, "help535", "", true ), LookOf( view ), "the cancelled call opens the door where the guard allows" );
			Assert.AreEqual( (true, false), (StateOf( guest, id ).MaintenanceRequested, ride.IsOpen), "the park's call stands meanwhile" );
			UntilManaged( host, guest, view );
			foreach ( var park in new[] { host, guest } )
				Assert.AreEqual( (false, true), (StateOf( park, id ).MaintenanceRequested, park.One.IsOpen), "the cancel reopens the ride on every peer" );
			Assert.AreEqual( new Look( false, true, Normal, DoorHelp( view, closed: false ), false, true, Normal, "help535", "", true ), LookOf( view ) );
			CutAndCompare( host, guest );
			Rounds( 60, host, guest );
			CutAndCompare( host, guest );
			Assert.AreEqual( (0, 0, sent + 2), (guest.Level.SessionRefusals, Management( guest ).TakeNotes().Count, Numbered( guest )) );
		} );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ABoughtLevelDrawsTheRideClosedWithTheUpgradesStatusWhetherOrNotAMechanicIsHired( bool mechanics )
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession( mechanics ? null : NoMechanics );
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			Show( view, ride );
			var sent = Numbered( guest );
			var balance = guest.Economy.Balance;
			OpenUpgradeList( view );
			CollectionAssert.AreEqual( new RideUpgradeRowKind?[] { RideUpgradeRowKind.Level }, RowKinds( view ) );
			ActivateRow( view );
			Assert.AreEqual( new BuyUpgradeCommand( id, 1 ), Waiting( guest, id ), "the buy names the level the list showed" );
			view.Update();
			// the list waits with its line, and the window under it draws the upgrading ride: closed, the upgrade's status (waiting for a mechanic, or none hired)
			Assert.AreEqual( "rideUpgrade", Top( view ) );
			CollectionAssert.AreEqual( new[] { view.Strings.Extra( OpenTpwText.CoopWaitingForHost ) }, view.Hud.UpgradeListRows().Select( row => row.Text ).ToArray() );
			Assert.IsNull( view.Hud.UpgradeListRows().Single().Row, "the waiting line is no row" );
			var status = mechanics ? UIStrings.UpgradeWaitingForMechanic : UIStrings.UpgradeNoMechanics;
			var drawn = LookOf( view );
			Assert.AreEqual( (true, false, Greyed, DoorHelp( view, closed: true ), false, view.Strings[status], false), (drawn.Closed, drawn.DoorEnabled, drawn.DoorFrame, drawn.DoorHelp, drawn.Called, drawn.Banner, drawn.InfoOpen) );
			Assert.AreEqual( (0, true, true), (StateOf( guest, id ).PendingLevel, ride.IsOpen, StateOf( host, id ).IsOpen), "the park is as it was" );
			// the record: accepted, the list closes and says nothing, the level is pending and the ride closed on every peer, nothing debited yet
			UntilManaged( host, guest, view );
			Assert.AreEqual( sent + 1, Numbered( guest ) );
			Assert.AreEqual( "hud", Top( view ), "an accepted buy closes the list" );
			foreach ( var park in new[] { host, guest } )
				Assert.AreEqual( (1, 1, false, balance), (StateOf( park, id ).PendingLevel, StateOf( park, id ).UpgradePurchases, park.One.IsOpen, park.Economy.Balance), "the upgrade is bought and nothing is debited" );
			Assert.AreEqual( 0, Management( guest ).TakeNotes().Count );
			CutAndCompare( host, guest );
			Show( view, null! );
			Assert.AreEqual( 0, view.Hud.Messages.Count, "an accepted buy says nothing" );
			Assert.AreEqual( 0, guest.Level.SessionRefusals );
		} );
	}

	[TestMethod]
	public void ABoughtLevelOnABrokenRideKeepsTheBreakdownsStatus()
	{
		InParkView( () =>
		{
			// the ride broke down before the session, alike on every peer: its record broken and its door closed, no mechanic hired
			var (host, guest, view) = AttractionsSession( ( park, isHost ) =>
			{
				NoMechanics( park, isHost );
				park.Enter();
				var record = StateOf( park, park.One.Runtime.AttractionId );
				record.IsBrokenDown = true;
				record.IsOpen = false;
				park.One.Runtime.Close();
			} );
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			Show( view, ride );
			Assert.AreEqual( view.Strings[UIStrings.BrokenDown], Banner( view ) );
			OpenUpgradeList( view );
			CollectionAssert.AreEqual( new RideUpgradeRowKind?[] { RideUpgradeRowKind.Level }, RowKinds( view ), "a broken ride whose upgrade waits not yet lists its level" );
			ActivateRow( view );
			Assert.AreEqual( new BuyUpgradeCommand( id, 1 ), Waiting( guest, id ) );
			view.Update();
			// the projected record holds the pending level, and still the breakdown's status and a closed door
			var drawn = LookOf( view );
			Assert.AreEqual( (true, false, view.Strings[UIStrings.BrokenDown], false), (drawn.Closed, drawn.DoorEnabled, drawn.Banner, drawn.InfoOpen) );
			UntilManaged( host, guest, view );
			foreach ( var park in new[] { host, guest } )
				Assert.AreEqual( (1, false, true), (StateOf( park, id ).PendingLevel, park.One.IsOpen, StateOf( park, id ).IsBrokenDown) );
			Assert.AreEqual( view.Strings[UIStrings.BrokenDown], Banner( view ), "the park's status after the record: the breakdown still comes first" );
			CutAndCompare( host, guest );
			Show( view, null! );
			Assert.AreEqual( (0, 0), (view.Hud.Messages.Count, guest.Level.SessionRefusals) );
		} );
	}

	/// <summary>Frames of the host and the guest as the game runs them, but for the guest's session step: nothing the guest's queue holds is sent meanwhile; until <paramref name="done"/>.</summary>
	private void RoundsHeld( Func<bool> done, Park host, Park guest, string what, int limit = 3000 )
	{
		for ( var round = 0; round < limit && !done(); round++ )
			Rounds( 1, host, guest );
		Assert.IsTrue( done(), what );
	}

	/// <summary>The kinds of the economy events each park raises from now on, in order.</summary>
	private static Dictionary<Park, List<ParkEventKind>> Events( params Park[] parks )
	{
		var logs = parks.ToDictionary( park => park, _ => new List<ParkEventKind>() );
		foreach ( var park in parks )
			park.Economy.EventRaised += raised => logs[park].Add( raised.Kind );
		return logs;
	}

	[TestMethod]
	public void AnOpeningTheGuardRefusesAtItsRecordIsSilentAndTheDoorReturnsToTheParksState()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			var seat = guest.Level.Commands.Player;
			Show( view, ride );
			// the door closes first; its click then asks to open, which the guard allows when it is clicked
			WindowClick( view, "openRide" );
			UntilManaged( host, guest, view );
			Assert.AreEqual( (false, false), (host.One.IsOpen, ride.IsOpen) );
			WindowClick( view, "openRide" );
			Assert.AreEqual( new SetAttractionOpenCommand( id, true ), Waiting( guest, id ) );
			view.Update();
			Assert.AreEqual( new Look( false, false, Greyed, DoorHelp( view, closed: false ), false, false, Greyed, "help535", "", true ), LookOf( view ), "drawn open while it waits" );
			// the host calls a mechanic before the record: the call closes the ride and the guard refuses an opening while the call stands
			Assert.IsTrue( host.Issue( new RequestMaintenanceCommand( id, true ) ).Accepted );
			RoundsHeld( () => StateOf( guest, id ).MaintenanceRequested, host, guest, "the call reached the guest" );
			view.Update();
			var refused = LookOf( view );
			Assert.AreEqual( (true, false, true, false), (refused.Closed, refused.DoorEnabled, refused.Called, refused.InfoOpen), "the guard refuses the waiting opening: the copy keeps the ride closed" );
			CollectionAssert.Contains( new[] { view.Strings[UIStrings.RepairWaitingForMechanic], view.Strings[UIStrings.RepairMechanicOnWay], view.Strings[UIStrings.RepairsInProgress] }, refused.Banner );
			// the record: refused by the park without a word, and the door shows the park's state again
			UntilManaged( host, guest, view );
			Assert.AreEqual( (false, false), (host.One.IsOpen, ride.IsOpen), "the call keeps it closed" );
			Assert.AreEqual( (0, 0), (Management( guest ).TakeNotes().Count, guest.Level.SessionRefusals), "no note, no refusal" );
			var after = LookOf( view );
			Assert.AreEqual( (true, false, true, false), (after.Closed, after.DoorEnabled, after.Called, after.InfoOpen), "the door is greyed by the park's own rule while the call stands" );
			CollectionAssert.AreEqual( IssuedBy( seat, new SetAttractionOpenCommand( id, false ), new SetAttractionOpenCommand( id, true ) ), HostCommandsOf( host, seat ),
				"the refused opening is a record that changed nothing" );
			Show( view, null! );
			Assert.AreEqual( 0, view.Hud.Messages.Count, "said nothing" );
			CutAndCompare( host, guest );
		} );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void TheCallButtonWithoutStaffOrOnStrikeIsDisabledForTheHostSubduedButClickableForAGuestAndItsClickSaysTheRightsNoteOrSoundsTheError( bool strike )
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession( strike ? MechanicsOnStrike : NoMechanics );
			var hostView = ViewOf( host );
			var id = guest.One.Runtime.AttractionId;
			var help = strike ? "help575" : "help574";
			// the host's button is disabled by the call's refusal, as before
			Show( hostView, host.One );
			var hostCall = CallButton( hostView );
			Assert.AreEqual( (false, null, Greyed, help), (hostCall.Enabled, hostCall.Subdued, hostCall.StateFrame( false, false ), hostCall.Help), "the host's button" );
			Assert.AreEqual( 0, Sounds( () => WindowClick( hostView, "callStaff" ) ).Count, "no click on a disabled button" );
			// a guest with the right: clickable, drawn subdued with the refusal's help; the click plays the error sound alone and sends nothing
			Show( view, guest.One );
			var call = CallButton( view );
			Assert.AreEqual( (true, true, Greyed, help), (call.Enabled, call.Subdued?.Invoke(), call.StateFrame( false, false ), call.Help), "the guest's button" );
			var sent = Numbered( guest );
			CollectionAssert.AreEqual( new[] { GameAudio.ErrorEvent }, Sounds( () => WindowClick( view, "callStaff" ) ), "the error sound, and no click sound for a subdued button" );
			view.Update();
			Assert.AreEqual( (0, sent, null, 0), (Management( guest ).Pending.Count(), Numbered( guest ), Management( guest ).PeekNote(), guest.Level.SessionRefusals), "nothing queued, sent or said" );
			Assert.IsNull( Waiting( guest, id ) );
			Rounds( 30, host, guest );
			Assert.AreEqual( (false, true), (StateOf( host, id ).MaintenanceRequested, host.One.IsOpen), "no call was made" );
			// without the right: the rights' note first, through the session's notes, with the error sound; said once the window closes
			GiveRights( host, guest, view, CoopRights.Finance );
			Show( view, guest.One );
			CollectionAssert.AreEqual( new[] { GameAudio.ErrorEvent }, Sounds( () => WindowClick( view, "callStaff" ) ) );
			Assert.AreEqual( new ManagementNote( ManagementNoteKind.Rights, CoopRefusal.NotPermitted ), Management( guest ).PeekNote()?.Note );
			view.Update();
			Assert.AreEqual( 0, view.Says( OpenTpwText.CoopNotPermitted ), "not said while the object window's arm is out" );
			Show( view, null! );
			Assert.AreEqual( (1, sent, 0), (view.Says( OpenTpwText.CoopNotPermitted ), Numbered( guest ), guest.Level.SessionRefusals), "said once after it closed; nothing sent, no session refusal" );
			CatchUpAndCompare( host, guest );
		} );
	}

	[TestMethod]
	public void TheHostsDoorClickBeforeAGuestsDoorRecordLeavesTheGuestsRecordAcceptedWithNothingMore()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var hostView = ViewOf( host );
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			var seat = guest.Level.Commands.Player;
			Show( view, ride );
			Show( hostView, host.One );
			WindowClick( view, "openRide" );
			Assert.AreEqual( new SetAttractionOpenCommand( id, false ), Waiting( guest, id ) );
			// the host closes the door first: at once on its park, then on the guest's
			WindowClick( hostView, "openRide" );
			Assert.IsFalse( host.One.IsOpen );
			RoundsHeld( () => !ride.IsOpen, host, guest, "the host's record reached the guest" );
			view.Update();
			var look = LookOf( view );
			Assert.AreEqual( (true, false, false, view.Strings[UIStrings.Closed]), (look.Closed, look.DoorEnabled, look.InfoOpen, look.Banner), "the waiting close changes nothing the park did not" );
			UntilManaged( host, guest, view );
			Assert.AreEqual( (false, false), (host.One.IsOpen, ride.IsOpen) );
			CollectionAssert.AreEqual( IssuedBy( ParkCommands.LocalPlayer, new SetAttractionOpenCommand( id, false ) ).Concat( IssuedBy( seat, new SetAttractionOpenCommand( id, false ) ) ).ToArray(),
				host.Level.Commands.Log.Select( entry => entry.Command ).Where( command => command is SetAttractionOpenCommand ).ToArray(), "both records, the host's first, the guest's accepted as a no-op" );
			Assert.AreEqual( (0, 0), (Management( guest ).TakeNotes().Count, guest.Level.SessionRefusals), "accepted: nothing said" );
			CutAndCompare( host, guest );
			Rounds( 60, host, guest );
			CutAndCompare( host, guest );
		} );
	}

	[TestMethod]
	public void InAPausedParkAGuestsDoorIsDrawnFromTheRuntimesOpenWordBeforeTheRecordMirrorsIt()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var hostView = ViewOf( host );
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			Show( view, ride );
			Show( hostView, host.One );
			// the host pauses the park and closes the ride's door: the door command changes the runtime at once and the record mirrors it at the next tick, which does not come
			Assert.IsTrue( host.Issue( new SetGameSpeedCommand( GameSpeed.Paused ) ).Accepted );
			Rounds( 10, host, guest );
			WindowClick( hostView, "openRide" );
			Rounds( 10, host, guest );
			Assert.AreEqual( (false, false), (host.One.IsOpen, ride.IsOpen), "the door is closed on both parks" );
			Assert.AreEqual( (true, true), (StateOf( host, id ).IsOpen, StateOf( guest, id ).IsOpen), "the records have not mirrored it" );
			// the guest draws the runtime's word: closed, the banner's Closed, and its click asks to open
			view.Update();
			Assert.AreEqual( new Look( true, true, UiButton.DownFrame, DoorHelp( view, closed: true ), false, true, Normal, "help535", view.Strings[UIStrings.Closed], false ), LookOf( view ) );
			WindowClick( view, "openRide" );
			Assert.AreEqual( new SetAttractionOpenCommand( id, true ), Waiting( guest, id ), "the opposite of the drawn word, not of the record's" );
			view.Update();
			Assert.AreEqual( new Look( false, false, Greyed, DoorHelp( view, closed: false ), false, false, Greyed, "help535", "", true ), LookOf( view ), "projected open while it waits" );
			Assert.IsFalse( ride.IsOpen );
			UntilManaged( host, guest, view );
			Assert.AreEqual( (true, true), (host.One.IsOpen, ride.IsOpen), "applied in the paused park" );
			Assert.IsTrue( host.Issue( new SetGameSpeedCommand( GameSpeed.Normal ) ).Accepted );
			Rounds( 60, host, guest );
			CutAndCompare( host, guest );
		} );
	}

	[TestMethod]
	public void TheHostsCallBeforeAGuestsCallLeavesTheProjectionAndTheOpenWordUnchangedAndTheGuestsCallAcceptedWithNothingMore()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var hostView = ViewOf( host );
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			var events = Events( host, guest );
			Show( view, ride );
			Show( hostView, host.One );
			WindowClick( view, "callStaff" );
			Assert.AreEqual( new RequestMaintenanceCommand( id, true ), Waiting( guest, id ) );
			view.Update();
			var waiting = LookOf( view );
			Assert.AreEqual( (true, true, false), (waiting.Closed, waiting.Called, waiting.InfoOpen), "the call projected: the flag set and the ride closed" );
			// the host's call comes first
			WindowClick( hostView, "callStaff" );
			Assert.AreEqual( (true, false), (StateOf( host, id ).MaintenanceRequested, host.One.IsOpen) );
			RoundsHeld( () => StateOf( guest, id ).MaintenanceRequested, host, guest, "the host's call reached the guest" );
			view.Update();
			Assert.AreEqual( waiting, LookOf( view ), "the waiting call is a no-op now: the projection and the drawn open word are what the park shows" );
			Assert.AreEqual( false, ride.IsOpen );
			UntilManaged( host, guest, view );
			Assert.AreEqual( (0, 0), (Management( guest ).TakeNotes().Count, guest.Level.SessionRefusals), "accepted, nothing said" );
			foreach ( var park in new[] { host, guest } )
			{
				Assert.AreEqual( (true, false), (StateOf( park, id ).MaintenanceRequested, park.One.IsOpen), "the open word is unchanged" );
				Assert.AreEqual( 1, events[park].Count( kind => kind == ParkEventKind.MaintenanceRequested ), "one call raised: the guest's accepted record raised nothing more" );
			}
			CutAndCompare( host, guest );
			Rounds( 60, host, guest );
			CutAndCompare( host, guest );
		} );
	}

	[TestMethod]
	public void TheHostsCancelBeforeAGuestsCancelLeavesTheProjectionAndTheOpenWordUnchangedAndTheGuestsCancelAcceptedWithNothingMore()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var hostView = ViewOf( host );
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			Show( view, ride );
			Show( hostView, host.One );
			// a call stands on both parks
			WindowClick( hostView, "callStaff" );
			RoundsHeld( () => StateOf( guest, id ).MaintenanceRequested, host, guest, "the call reached the guest" );
			var events = Events( host, guest );
			view.Update();
			Assert.IsTrue( LookOf( view ).Called );
			WindowClick( view, "callStaff" );
			Assert.AreEqual( new RequestMaintenanceCommand( id, false ), Waiting( guest, id ) );
			view.Update();
			Assert.AreEqual( (false, false, true), (LookOf( view ).Called, LookOf( view ).Closed, LookOf( view ).InfoOpen), "the cancel projected: the ride open" );
			// the host cancels first: the ride reopens on its park, then on the guest's
			view.Update();
			WindowClick( hostView, "callStaff" );
			Assert.AreEqual( (false, true), (StateOf( host, id ).MaintenanceRequested, host.One.IsOpen) );
			RoundsHeld( () => !StateOf( guest, id ).MaintenanceRequested && ride.IsOpen, host, guest, "the host's cancel reached the guest" );
			view.Update();
			var look = LookOf( view );
			Assert.AreEqual( (false, false, false, true, ""), (look.Called, look.Closed, look.CallEnabled, look.InfoOpen, look.Banner), "the waiting cancel is a no-op: nothing the park does not show" );
			UntilManaged( host, guest, view );
			Assert.AreEqual( (0, 0), (Management( guest ).TakeNotes().Count, guest.Level.SessionRefusals) );
			foreach ( var park in new[] { host, guest } )
			{
				Assert.AreEqual( (false, true), (StateOf( park, id ).MaintenanceRequested, park.One.IsOpen) );
				Assert.AreEqual( 1, events[park].Count( kind => kind == ParkEventKind.MaintenanceCancelled ), "one cancel raised: the guest's accepted record raised nothing more" );
			}
			CutAndCompare( host, guest );
			Rounds( 60, host, guest );
			CutAndCompare( host, guest );
		} );
	}

	[TestMethod]
	public void ARepairFinishingWhileAGuestsCancelledCallWaitsLeavesTheCancelAcceptedWithNothingToUndoAndTheRideOpenedOnce()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			Show( view, ride );
			WindowClick( view, "callStaff" );
			UntilManaged( host, guest, view );
			Assert.AreEqual( (true, false), (StateOf( guest, id ).MaintenanceRequested, ride.IsOpen) );
			var events = Events( host, guest );
			WindowClick( view, "callStaff" );
			Assert.AreEqual( new RequestMaintenanceCommand( id, false ), Waiting( guest, id ) );
			view.Update();
			Assert.AreEqual( (false, false, true), (LookOf( view ).Called, LookOf( view ).Closed, LookOf( view ).InfoOpen), "the cancel projected: the ride open" );
			// the mechanic finishes the repair the call asked for before the cancel is sent: the call is cleared and the ride opened by the repair
			RoundsHeld( () => !StateOf( host, id ).MaintenanceRequested && !StateOf( guest, id ).MaintenanceRequested, host, guest, "the repair finished on both parks" );
			Assert.AreEqual( (true, true), (host.One.IsOpen, ride.IsOpen), "the repair opened the ride" );
			view.Update();
			var look = LookOf( view );
			Assert.AreEqual( (false, false, true, ""), (look.Called, look.Closed, look.InfoOpen, look.Banner), "the waiting cancel finds nothing to undo: the park's state is drawn" );
			UntilManaged( host, guest, view );
			Assert.AreEqual( (0, 0), (Management( guest ).TakeNotes().Count, guest.Level.SessionRefusals), "accepted, nothing said" );
			foreach ( var park in new[] { host, guest } )
			{
				Assert.AreEqual( (false, true), (StateOf( park, id ).MaintenanceRequested, park.One.IsOpen) );
				Assert.AreEqual( (1, 0), (events[park].Count( kind => kind == ParkEventKind.RideRepaired ), events[park].Count( kind => kind == ParkEventKind.MaintenanceCancelled )),
					"the ride was opened once, by the repair: the accepted cancel raised no cancel" );
			}
			CutAndCompare( host, guest );
		} );
	}

	[TestMethod]
	public void AnUpgradeCompletingWhileAGuestsCallWaitsLetsTheCallCloseTheRideAgain()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var hostView = ViewOf( host );
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			Show( view, ride );
			Show( hostView, host.One );
			// the host buys the level by its list: the ride closes until a mechanic has installed it
			OpenUpgradeList( hostView );
			ActivateRow( hostView );
			Assert.AreEqual( "hud", Top( hostView ), "the host's row issued at once and closed the list" );
			Assert.AreEqual( (1, false), (StateOf( host, id ).PendingLevel, host.One.IsOpen) );
			RoundsHeld( () => StateOf( guest, id ).PendingLevel == 1, host, guest, "the purchase reached the guest" );
			view.Update();
			WindowClick( view, "callStaff" );
			Assert.AreEqual( new RequestMaintenanceCommand( id, true ), Waiting( guest, id ) );
			// the upgrade is installed while the call waits: the ride opens
			RoundsHeld( () => StateOf( host, id ).Level == 1 && StateOf( guest, id ).Level == 1, host, guest, "the upgrade was installed on both parks" );
			Assert.AreEqual( (true, true), (host.One.IsOpen, ride.IsOpen), "the installed upgrade opened the ride" );
			view.Update();
			var look = LookOf( view );
			Assert.AreEqual( (true, true, false), (look.Closed, look.Called, look.InfoOpen), "the call is projected onto the new state: the ride closed again" );
			Assert.AreEqual( view.Strings[UIStrings.RepairWaitingForMechanic], look.Banner );
			UntilManaged( host, guest, view );
			foreach ( var park in new[] { host, guest } )
				Assert.AreEqual( (true, false, 1), (StateOf( park, id ).MaintenanceRequested, park.One.IsOpen, StateOf( park, id ).Level), "the call closed the ride again" );
			CutAndCompare( host, guest );
		} );
	}

	/// <summary>
	/// A toilet of the test catalogue on <paramref name="park"/>, placed and linked to its economy record as a placed one is, before the session starts: the small toilet (1402), a Features
	/// object whose record provides relief (<c>UsageInfo.ProvidesRelief</c>), which is what makes the call button a toilet's (<c>CallTarget</c>'s <c>toilet</c>: the handymen's).
	/// </summary>
	private OriginalObject AddToilet( Park park, int x, int y = 2 )
	{
		var root = (string)typeof( UpgradeCatalogueDefaultsTests ).GetField( "root", NonPublic )!.GetValue( fixture )!;
		var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Feature, $"/toilet{x}", new ObjectSettings( new[] { new ObjectSettingsFile(
				$"Info.Id 1402\nInfo.Name SyntheticToilet{x}\nInfo.WhichUIType {(int)BuildCategory.Features}\nInfo.IsChoosable 1\nUsageInfo.ProvidesRelief 1\n", $"toilet{x}.sam" ) } ),
			new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(),
			new BaseFileSystem( root ), null );
		var placed = park.Level.Objects.TryPlace( entry, x, y, 0, out _ )!;
		Assert.IsNotNull( placed );
		var state = park.Economy.RegisterExisting( 1402 );
		typeof( Level ).GetMethod( "LinkEconomy", NonPublic )!.Invoke( park.Level, new object[] { placed, state.Id } );
		Assert.IsTrue( park.Economy.Catalog.TryGet( 1402, out var info ) && info.ProvidesRelief, "a relief object: the call is the handymen's" );
		return placed;
	}

	/// <summary>The object of <paramref name="park"/> with attraction id <paramref name="attraction"/>.</summary>
	private static OriginalObject ObjectOf( Park park, int attraction ) => park.Level.Objects.Objects.Single( placed => !placed.IsDeleted && placed.Runtime.AttractionId == attraction );

	/// <summary>
	/// Brings the guest to the host's tick with every record applied, then runs <paramref name="change"/> on each park in turn (its own process statics in place), the same on every peer: a
	/// change of the park outside the commands that a record cannot make, as <see cref="Alike"/> does for the money.
	/// </summary>
	private void AlikeIn( Park host, Park guest, Action<Park> change )
	{
		host.Holds = true;
		hostTimeFrozenAt = clock;
		var hostStream = host.Session!.HostStream!;
		var guestStream = guest.Session!.GuestStream!;
		for ( var round = 0; round < 3000 && (guest.Level.TickCount != host.Level.TickCount || guestStream.LastRecord != hostStream.LastRecord
			|| guest.Level.Commands.ScheduledCount > 0); round++ )
			Rounds( 1, host, guest );
		Assert.AreEqual( host.Level.TickCount, guest.Level.TickCount );
		foreach ( var park in new[] { host, guest } )
		{
			park.Enter();
			change( park );
		}
		hostTimeFrozenAt = null;
		host.Holds = false;
	}

	/// <summary>
	/// A handyman's clean of <paramref name="attraction"/> finishes on <paramref name="park"/> (the end of his job, <see cref="ParkEconomy.FinishToiletClean"/>): the claim and the call clear and the
	/// toilet is told cleaned, which reopens it. The test level's handymen find no toilet to walk to (its toilets have no access cell), so the job's end is the test's own, alike on every peer.
	/// </summary>
	private static void CleanFinishes( Park park, int attraction )
	{
		var record = StateOf( park, attraction );
		var handyman = park.Economy.Staff.OfType( StaffType.Handyman ).First();
		handyman.ToiletId = record.Id;
		record.MechanicId = handyman.Id;
		park.Economy.FinishToiletClean( handyman );
		Assert.IsFalse( record.MaintenanceRequested );
	}

	[TestMethod]
	public void APriceADoorCloseAndAPriceByClicksGoInClickOrderAndEachRecordIsAppliedAtItsCut()
	{
		InParkView( () =>
		{
			OriginalObject? shop = null;
			var (host, guest, view) = AttractionsSession( ( park, isHost ) =>
			{
				var placed = AddPriced( park, shop: true, 20 );
				if ( !isHost )
					shop = placed;
			} );
			var id = shop!.Runtime.AttractionId;
			var seat = guest.Level.Commands.Player;
			var price0 = StateOf( guest, id ).Price;
			Assert.IsTrue( price0 != 10 && price0 != 50 );
			Show( view, shop );
			OpenPriceEditor( view );
			TypePrice( view, 10 );
			WindowClick( view, "openRide" );
			OpenPriceEditor( view );
			TypePrice( view, 50 );
			// the door entry between them keeps the price entries apart: three entries, in the order of their clicks
			CollectionAssert.AreEqual( new ParkCommand[] { new SetPriceCommand( id, 10 ), new SetAttractionOpenCommand( id, false ), new SetPriceCommand( id, 50 ) },
				Management( guest ).Pending.Select( entry => entry.Command ).ToArray() );
			Assert.AreEqual( 50, view.Hud.PendingSettings.Price, "the window draws the latest price asked" );
			// each record goes alone, and at its cut every peer holds what the records before it left
			var expected = new (int Price, bool Open)[] { (10, true), (10, false), (50, false) };
			for ( var index = 0; index < expected.Length; index++ )
			{
				SendNext( guest );
				RoundsUntil( () => Management( guest ).Pending.Count() == expected.Length - 1 - index, host, guest, view, $"record {index + 1} came back" );
				CutAndCompare( host, guest );
				foreach ( var park in new[] { host, guest } )
					Assert.AreEqual( expected[index], (StateOf( park, id ).Price, ObjectOf( park, id ).IsOpen), $"after record {index + 1}" );
			}
			CollectionAssert.AreEqual( IssuedBy( seat, new SetPriceCommand( id, 10 ), new SetAttractionOpenCommand( id, false ), new SetPriceCommand( id, 50 ) ), HostCommandsOf( host, seat ),
				"the records in click order" );
			Rounds( 60, host, guest );
			CutAndCompare( host, guest );
			Assert.AreEqual( (0, 0), (guest.Level.SessionRefusals, Management( guest ).TakeNotes().Count) );
		} );
	}

	[TestMethod]
	public void AToiletsDoorAndHandymanCallWaitGreyedDrawTheProjectionTheCallClosesItAndTheCleanReopensIt()
	{
		InParkView( () =>
		{
			OriginalObject? toilet = null;
			var (host, guest, view) = AttractionsSession( ( park, isHost ) =>
			{
				var placed = AddToilet( park, 8 );
				if ( !isHost )
					toilet = placed;
			} );
			var id = toilet!.Runtime.AttractionId;
			var instance = guest.Level.GetEconomyInstance( toilet )!.Value;
			Show( view, toilet );
			string Help( bool closed ) => DoorHelp( view, closed, ride: false );
			Assert.AreEqual( new Look( false, true, Normal, Help( false ), false, true, Normal, "help535", "", true ), LookOf( view ), "the toilet's own window" );
			// its door: greyed while it waits, drawn closed from the projected record
			WindowClick( view, "openRide" );
			Assert.AreEqual( new SetAttractionOpenCommand( id, false ), Waiting( guest, id ) );
			WindowClick( view, "callStaff" );
			Assert.AreEqual( 1, Management( guest ).Pending.Count() );
			view.Update();
			Assert.AreEqual( new Look( true, false, Greyed, Help( true ), false, false, Greyed, "help535", view.Strings[UIStrings.Closed], false ), LookOf( view ) );
			UntilManaged( host, guest, view );
			Assert.AreEqual( (false, false), (host.Level.Objects.Objects.Single( placed => placed.Runtime.AttractionId == id ).IsOpen, toilet.IsOpen) );
			CutAndCompare( host, guest );
			WindowClick( view, "openRide" );
			view.Update();
			Assert.AreEqual( (false, false), (LookOf( view ).Closed, LookOf( view ).DoorEnabled), "drawn open while the reopening waits" );
			UntilManaged( host, guest, view );
			Assert.AreEqual( (true, true), (ObjectOf( host, id ).IsOpen, toilet.IsOpen) );
			// its call: the handymen's, the flag set and the toilet closed while it waits, the cleaning's status
			WindowClick( view, "callStaff" );
			Assert.AreEqual( new RequestMaintenanceCommand( id, true ), Waiting( guest, id ) );
			WindowClick( view, "callStaff" );
			WindowClick( view, "openRide" );
			Assert.AreEqual( 1, Management( guest ).Pending.Count(), "no second click" );
			view.Update();
			Assert.AreEqual( new Look( true, false, Greyed, Help( true ), true, false, Greyed, "help570", view.Strings[UIStrings.CleaningWaitingForCleaner], false ), LookOf( view ) );
			Assert.IsTrue( toilet.IsOpen, "the park's toilet is open still" );
			UntilManaged( host, guest, view );
			foreach ( var park in new[] { host, guest } )
				Assert.AreEqual( (true, false), (StateOf( park, id ).MaintenanceRequested, ObjectOf( park, id ).IsOpen), "the call closes the toilet on every peer" );
			CutAndCompare( host, guest );
			// the toilet wears dirty and the handyman cleans it: the clean clears the call and reopens it on every peer
			var events = Events( host, guest );
			AlikeIn( host, guest, park => CleanFinishes( park, id ) );
			foreach ( var park in new[] { host, guest } )
			{
				Assert.IsTrue( ObjectOf( park, id ).IsOpen, "the clean reopened it" );
				Assert.AreEqual( 1, events[park].Count( kind => kind == ParkEventKind.ToiletCleaned ) );
			}
			view.Update();
			Assert.AreEqual( new Look( false, true, Normal, Help( false ), false, true, Normal, "help535", "", true ), LookOf( view ) );
			CutAndCompare( host, guest );
			// a call cancelled while the clean finishes: its cancel finds nothing to undo, and the toilet is opened once
			WindowClick( view, "callStaff" );
			UntilManaged( host, guest, view );
			Assert.AreEqual( (true, false), (StateOf( guest, id ).MaintenanceRequested, toilet.IsOpen) );
			var cleaned = Events( host, guest );
			WindowClick( view, "callStaff" );
			Assert.AreEqual( new RequestMaintenanceCommand( id, false ), Waiting( guest, id ) );
			view.Update();
			Assert.AreEqual( (false, false, true), (LookOf( view ).Called, LookOf( view ).Closed, LookOf( view ).InfoOpen), "the cancel projected: the toilet open" );
			AlikeIn( host, guest, park => CleanFinishes( park, id ) );
			Assert.AreEqual( (true, true), (ObjectOf( host, id ).IsOpen, toilet.IsOpen), "the clean reopened the toilet" );
			view.Update();
			Assert.AreEqual( (false, false, true, ""), (LookOf( view ).Called, LookOf( view ).Closed, LookOf( view ).InfoOpen, LookOf( view ).Banner) );
			UntilManaged( host, guest, view );
			foreach ( var park in new[] { host, guest } )
			{
				Assert.AreEqual( (false, true), (StateOf( park, id ).MaintenanceRequested, ObjectOf( park, id ).IsOpen) );
				Assert.AreEqual( (1, 0), (cleaned[park].Count( kind => kind == ParkEventKind.ToiletCleaned ), cleaned[park].Count( kind => kind == ParkEventKind.MaintenanceCancelled )),
					"opened once, by the clean: the accepted cancel raised no cancel" );
			}
			CutAndCompare( host, guest );
			Rounds( 60, host, guest );
			CutAndCompare( host, guest );
			Assert.AreEqual( (0, 0), (Management( guest ).TakeNotes().Count, guest.Level.SessionRefusals) );
			Assert.IsNotNull( instance );
		} );
	}

	[TestMethod]
	public void WithoutTheAttractionsRightAToiletsDoorAndCallSayTheRightsNoteOnceAndSendNothing()
	{
		InParkView( () =>
		{
			OriginalObject? toilet = null;
			var (host, guest, view) = AttractionsSession( ( park, isHost ) =>
			{
				var placed = AddToilet( park, 8 );
				if ( !isHost )
					toilet = placed;
			}, CoopRights.Finance );
			var id = toilet!.Runtime.AttractionId;
			var sent = Numbered( guest );
			Show( view, toilet );
			var sounds = Sounds( () =>
			{
				WindowClick( view, "openRide" );
				WindowClick( view, "callStaff" );
			} );
			Assert.AreEqual( 2, sounds.Count( sound => sound == GameAudio.ErrorEvent ), "each click sounds the error" );
			Assert.IsNull( Waiting( guest, id ) );
			Assert.AreEqual( (0, sent), (Management( guest ).Pending.Count(), Numbered( guest )), "nothing queued or sent" );
			// the two refusals are one note of the same words, which waits while the info arm is out
			view.Update();
			Assert.AreEqual( 0, view.Says( OpenTpwText.CoopNotPermitted ) );
			Show( view, null! );
			Assert.AreEqual( (1, 0), (view.Says( OpenTpwText.CoopNotPermitted ), guest.Level.SessionRefusals), "said once after the window closed, and no session refusal" );
			Rounds( 30, host, guest );
			Assert.AreEqual( (false, true), (StateOf( host, id ).MaintenanceRequested, ObjectOf( host, id ).IsOpen) );
			CatchUpAndCompare( host, guest );
		} );
	}

	[TestMethod]
	public void AnUpgradeTheHostsMechanicInstallsBeforeAGuestsWaitingBuyRecordMakesTheBuyStaleAndTheCopyUnchanged()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var hostView = ViewOf( host );
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			var seat = guest.Level.Commands.Player;
			Show( view, ride );
			Show( hostView, host.One );
			OpenUpgradeList( view );
			ActivateRow( view );
			Assert.AreEqual( new BuyUpgradeCommand( id, 1 ), Waiting( guest, id ) );
			// the host buys the same level first, by its list
			OpenUpgradeList( hostView );
			ActivateRow( hostView );
			RoundsHeld( () => StateOf( guest, id ).PendingLevel == 1, host, guest, "the host's purchase reached the guest" );
			view.Update();
			Assert.AreEqual( (true, false), (LookOf( view ).Closed, LookOf( view ).InfoOpen), "the waiting buy changes nothing the park does not show: the ride is closed for the host's upgrade" );
			// the host's mechanic installs it before the guest's record: the level is the guest's next level no more
			RoundsHeld( () => StateOf( host, id ).Level == 1 && StateOf( guest, id ).Level == 1, host, guest, "the upgrade was installed on both parks" );
			view.Update();
			Assert.AreEqual( (false, true, ""), (LookOf( view ).Closed, LookOf( view ).InfoOpen, LookOf( view ).Banner), "the copy is the park's: the ride open, no upgrade pending" );
			Assert.AreEqual( "rideUpgrade", Top( view ), "the list still waits" );
			UntilManaged( host, guest, view );
			// stale: the list closes and says what the host's would, once the window closes; the host's purchase is the one that stands
			Assert.AreEqual( "hud", Top( view ) );
			Assert.AreEqual( new ManagementNote( ManagementNoteKind.NotAvailable ), Management( guest ).PeekNote()?.Note );
			Show( view, null! );
			Assert.AreEqual( (1, 0), (view.Says( OpenTpwText.NotAvailable ), guest.Level.SessionRefusals) );
			foreach ( var park in new[] { host, guest } )
				Assert.AreEqual( (1, 0, 1, true), (StateOf( park, id ).Level, StateOf( park, id ).PendingLevel, StateOf( park, id ).UpgradePurchases, ObjectOf( park, id ).IsOpen), "the stale buy bought nothing" );
			Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is BuyUpgradeCommand ), "its record is in the stream" );
			CutAndCompare( host, guest );
		} );
	}

	[TestMethod]
	public void ABreakdownBeforeAGuestsWaitingCancelRefusesItSilentlyAndTheCopyKeepsTheRideClosed()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			var instance = guest.Level.GetEconomyInstance( ride )!.Value;
			Show( view, ride );
			OpenUpgradeList( view );
			ActivateRow( view );
			UntilManaged( host, guest, view );
			Assert.AreEqual( (1, 1, false), (StateOf( guest, id ).PendingLevel, StateOf( guest, id ).UpgradePurchases, ride.IsOpen) );
			// the cancel row, clicked and held
			OpenUpgradeList( view );
			CollectionAssert.AreEqual( new RideUpgradeRowKind?[] { RideUpgradeRowKind.CancelLevel }, RowKinds( view ) );
			ActivateRow( view );
			Assert.AreEqual( new CancelUpgradeCommand( id, 1, 1 ), Waiting( guest, id ) );
			view.Update();
			Assert.AreEqual( (false, true), (LookOf( view ).Closed, LookOf( view ).InfoOpen), "the cancel projected: no level pending and the ride open" );
			// the ride breaks down on every peer before the record: it is no upgrading ride then, and the record refuses the cancel
			AlikeIn( host, guest, park =>
			{
				Assert.IsTrue( park.Economy.TryGetObject( instance, out var record ) );
				record.IsBrokenDown = true;
			} );
			view.Update();
			var look = LookOf( view );
			Assert.AreEqual( (true, false), (look.Closed, look.InfoOpen), "refused by the record's checks: the copy keeps the ride closed, no projected reopening" );
			CollectionAssert.Contains( new[] { view.Strings[UIStrings.BrokenDown], view.Strings[UIStrings.RepairsInProgress] }, look.Banner );
			UntilManaged( host, guest, view );
			Assert.AreEqual( "hud", Top( view ), "the list closed as after every row" );
			Assert.AreEqual( (0, 0), (Management( guest ).TakeNotes().Count, guest.Level.SessionRefusals), "refused silently, as the host's list refuses" );
			foreach ( var park in new[] { host, guest } )
				Assert.AreEqual( (1, true, false), (StateOf( park, id ).PendingLevel, StateOf( park, id ).IsBrokenDown, ObjectOf( park, id ).IsOpen), "nothing was cancelled" );
			CutAndCompare( host, guest );
		} );
	}

	/// <summary>The host's list row for the shown ride, clicked: its next level bought, or its pending one cancelled, at once (the list closes after the row).</summary>
	private static void HostRow( View hostView, OriginalObject ride, RideUpgradeRowKind expected )
	{
		Show( hostView, ride );
		OpenUpgradeList( hostView );
		CollectionAssert.AreEqual( new RideUpgradeRowKind?[] { expected }, RowKinds( hostView ), "the host's list" );
		ActivateRow( hostView );
		Assert.AreEqual( "hud", Top( hostView ), "the host's row issued at once and closed the list" );
	}

	/// <summary>The guest buys the next level by its list: its entry sent and applied on every peer, the list closed.</summary>
	private void GuestBuys( Park host, Park guest, View view, OriginalObject ride )
	{
		Show( view, ride );
		OpenUpgradeList( view );
		ActivateRow( view );
		UntilManaged( host, guest, view );
		Assert.AreEqual( "hud", Top( view ) );
	}

	[TestMethod]
	public void ACancelAReBuyAndALateCancelLeaveTheHostsPurchaseStandingAndTheGuestsCancelStale()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var hostView = ViewOf( host );
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			var seat = guest.Level.Commands.Player;
			GuestBuys( host, guest, view, ride );
			Assert.AreEqual( (1, 1), (StateOf( host, id ).PendingLevel, StateOf( host, id ).UpgradePurchases) );
			// the guest's cancel row, clicked and held: it names the pending level and the purchase its list showed
			OpenUpgradeList( view );
			CollectionAssert.AreEqual( new RideUpgradeRowKind?[] { RideUpgradeRowKind.CancelLevel }, RowKinds( view ) );
			ActivateRow( view );
			Assert.AreEqual( new CancelUpgradeCommand( id, 1, 1 ), Waiting( guest, id ) );
			// the host cancels and buys the same level again before the guest's cancel arrives
			HostRow( hostView, host.One, RideUpgradeRowKind.CancelLevel );
			Assert.AreEqual( (0, 1, true), (StateOf( host, id ).PendingLevel, StateOf( host, id ).UpgradePurchases, host.One.IsOpen), "cancelled: no level pending, the ride open" );
			HostRow( hostView, host.One, RideUpgradeRowKind.Level );
			Assert.AreEqual( (1, 2, false), (StateOf( host, id ).PendingLevel, StateOf( host, id ).UpgradePurchases, host.One.IsOpen), "bought again: the second purchase of the level" );
			RoundsHeld( () => StateOf( guest, id ).UpgradePurchases == 2 && StateOf( guest, id ).PendingLevel == 1, host, guest, "the host's purchases reached the guest" );
			view.Update();
			Assert.AreEqual( (true, false), (LookOf( view ).Closed, LookOf( view ).InfoOpen), "the waiting cancel names the first purchase: the copy keeps the second one's closed ride" );
			// the guest's cancel at its record: stale, `NotAvailable`, and the host's purchase stands
			UntilManaged( host, guest, view );
			Assert.AreEqual( "hud", Top( view ) );
			Assert.AreEqual( new ManagementNote( ManagementNoteKind.NotAvailable ), Management( guest ).PeekNote()?.Note );
			foreach ( var park in new[] { host, guest } )
				Assert.AreEqual( (1, 2, false), (StateOf( park, id ).PendingLevel, StateOf( park, id ).UpgradePurchases, ObjectOf( park, id ).IsOpen), "the late cancel cancelled nothing" );
			CollectionAssert.AreEqual( IssuedBy( seat, new BuyUpgradeCommand( id, 1 ), new CancelUpgradeCommand( id, 1, 1 ) ), HostCommandsOf( host, seat ), "the guest's two records" );
			Show( view, null! );
			Assert.AreEqual( (1, 0), (view.Says( OpenTpwText.NotAvailable ), guest.Level.SessionRefusals) );
			CutAndCompare( host, guest );
			Rounds( 60, host, guest );
			CutAndCompare( host, guest );
		} );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void TheHostsBuyBeforeAGuestsBuyMakesItStaleAndAGuestsRecordFirstMakesTheHostsBuyUpgradeInProgress( bool guestFirst )
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var hostView = ViewOf( host );
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			var seat = guest.Level.Commands.Player;
			Show( view, ride );
			OpenUpgradeList( view );
			ActivateRow( view );
			Assert.AreEqual( new BuyUpgradeCommand( id, 1 ), Waiting( guest, id ) );
			if ( !guestFirst )
			{
				// the host's record first: the guest's buy then finds the level asked for already, stale
				HostRow( hostView, host.One, RideUpgradeRowKind.Level );
				UntilManaged( host, guest, view );
				Assert.AreEqual( new ManagementNote( ManagementNoteKind.NotAvailable ), Management( guest ).PeekNote()?.Note, "stale: what the host's window would say" );
				CollectionAssert.AreEqual( IssuedBy( ParkCommands.LocalPlayer, new BuyUpgradeCommand( id ) ).Concat( IssuedBy( seat, new BuyUpgradeCommand( id, 1 ) ) ).ToArray(),
					host.Level.Commands.Log.Select( entry => entry.Command ).Where( command => command is BuyUpgradeCommand ).ToArray(), "the host's record, then the guest's" );
				foreach ( var park in new[] { host, guest } )
					Assert.AreEqual( (1, 1), (StateOf( park, id ).PendingLevel, StateOf( park, id ).UpgradePurchases), "the host's purchase stands and the stale one counted none" );
				Show( view, null! );
				Assert.AreEqual( 1, view.Says( OpenTpwText.NotAvailable ) );
			}
			else
			{
				// the guest's record first: accepted, and the host's buy of the same level then finds it in progress (the command layer's own answer: its list shows the cancel row by then)
				UntilManaged( host, guest, view );
				Assert.AreEqual( (0, 0), (Management( guest ).TakeNotes().Count, guest.Level.SessionRefusals), "accepted: nothing said" );
				Assert.AreEqual( (1, 1), (StateOf( host, id ).PendingLevel, StateOf( host, id ).UpgradePurchases) );
				var late = host.Issue( new BuyUpgradeCommand( id ) );
				Assert.IsFalse( late.Accepted );
				Assert.AreEqual( ParkEconomy.PurchaseResult.UpgradeInProgress, late.As<ParkEconomy.PurchaseResult>(), "refused by the economy, as the host's own refusal" );
				foreach ( var park in new[] { host, guest } )
					Assert.AreEqual( (1, 1), (StateOf( park, id ).PendingLevel, StateOf( park, id ).UpgradePurchases), "the guest's purchase stands" );
				Show( view, null! );
				Assert.AreEqual( 0, view.Hud.Messages.Count );
			}
			CutAndCompare( host, guest );
			Rounds( 60, host, guest );
			CutAndCompare( host, guest );
			Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is BuyUpgradeCommand ), "the guest's one buy is in the stream either way" );
		} );
	}

	[TestMethod]
	public void TheHostsCancelBeforeAGuestsCancelMakesTheGuestsCancelStale()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var hostView = ViewOf( host );
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			GuestBuys( host, guest, view, ride );
			OpenUpgradeList( view );
			ActivateRow( view );
			Assert.AreEqual( new CancelUpgradeCommand( id, 1, 1 ), Waiting( guest, id ) );
			HostRow( hostView, host.One, RideUpgradeRowKind.CancelLevel );
			RoundsHeld( () => StateOf( guest, id ).PendingLevel == 0, host, guest, "the host's cancel reached the guest" );
			view.Update();
			Assert.AreEqual( (false, true), (LookOf( view ).Closed, LookOf( view ).InfoOpen), "the waiting cancel is a no-op now: the park's open ride" );
			UntilManaged( host, guest, view );
			Assert.AreEqual( new ManagementNote( ManagementNoteKind.NotAvailable ), Management( guest ).PeekNote()?.Note, "stale: the host's cancel stands" );
			foreach ( var park in new[] { host, guest } )
				Assert.AreEqual( (0, 1, true), (StateOf( park, id ).PendingLevel, StateOf( park, id ).UpgradePurchases, ObjectOf( park, id ).IsOpen) );
			Show( view, null! );
			Assert.AreEqual( 1, view.Says( OpenTpwText.NotAvailable ) );
			CutAndCompare( host, guest );
		} );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void EscapeWhileAListRowWaitsClosesTheListAndLeavesItsOutcomeAsANote( bool stale )
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var hostView = ViewOf( host );
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			Show( view, ride );
			OpenUpgradeList( view );
			ActivateRow( view );
			Assert.AreEqual( "rideUpgrade", Top( view ) );
			if ( stale )
				HostRow( hostView, host.One, RideUpgradeRowKind.Level );
			view.Escape();
			Assert.AreEqual( "hud", Top( view ), "Escape closed the waiting list" );
			Assert.AreEqual( new BuyUpgradeCommand( id, 1 ), Waiting( guest, id ), "its entry is on its way all the same" );
			view.Update();
			Assert.IsFalse( DoorButton( view ).Enabled, "the ride's open-state action still waits" );
			UntilManaged( host, guest, view );
			Assert.AreEqual( stale ? new ManagementNote( ManagementNoteKind.NotAvailable ) : ManagementNote.None, Management( guest ).PeekNote()?.Note ?? ManagementNote.None,
				stale ? "the outcome is a note" : "an accepted buy says nothing" );
			Show( view, null! );
			Assert.AreEqual( (stale ? 1 : 0, 0), (view.Says( OpenTpwText.NotAvailable ), guest.Level.SessionRefusals) );
			foreach ( var park in new[] { host, guest } )
				Assert.AreEqual( (1, 1), (StateOf( park, id ).PendingLevel, StateOf( park, id ).UpgradePurchases) );
			CutAndCompare( host, guest );
		} );
	}

	[TestMethod]
	public void TheWindowMovingOnWhileAListRowWaitsClosesTheListAndLeavesItsOutcomeAsANote()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var hostView = ViewOf( host );
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			Show( view, ride );
			OpenUpgradeList( view );
			ActivateRow( view );
			HostRow( hostView, host.One, RideUpgradeRowKind.Level );
			// the arrows' next object: the list belongs to the window it opened from, which has gone
			view.Hud.CycleSelectedObject( 1 );
			view.Update();
			Assert.AreNotSame( ride, view.Hud.SelectedObject );
			Assert.AreEqual( "hud", Top( view ), "the list closed with its window" );
			Assert.AreEqual( new BuyUpgradeCommand( id, 1 ), Waiting( guest, id ) );
			// the other ride's window draws its own park state: nothing waits for it
			var other = view.Hud.SelectedObject!;
			view.Update();
			Assert.IsTrue( DoorButton( view ).Enabled && CallButton( view ).Enabled, "the next object's buttons take clicks" );
			Assert.IsFalse( LookOf( view ).Closed );
			UntilManaged( host, guest, view );
			Assert.AreEqual( new ManagementNote( ManagementNoteKind.NotAvailable ), Management( guest ).PeekNote()?.Note );
			view.Update();
			Assert.AreEqual( 0, view.Says( OpenTpwText.NotAvailable ), "the window is out: not yet said" );
			Show( view, null! );
			Assert.AreEqual( (1, 0), (view.Says( OpenTpwText.NotAvailable ), guest.Level.SessionRefusals) );
			Assert.IsNotNull( other );
			CutAndCompare( host, guest );
		} );
	}

	[DataTestMethod]
	[DataRow( "door" )]
	[DataRow( "call" )]
	[DataRow( "level" )]
	public void TheRowsOfAReopenedListAreInertWhileAnyOpenStateActionOfTheRideWaits( string waiting )
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			Show( view, ride );
			switch ( waiting )
			{
				case "door":
					WindowClick( view, "openRide" );
					break;
				case "call":
					WindowClick( view, "callStaff" );
					break;
				default:
					OpenUpgradeList( view );
					ActivateRow( view );
					view.Escape();
					break;
			}
			var entry = Waiting( guest, id );
			Assert.IsNotNull( entry );
			// the list opens, and its row takes no click: nothing is queued and the list stays as it is
			OpenUpgradeList( view );
			// the rows as the projection shows them: a waiting level lists its cancel row (a door's or a call's projected copy is no record the level row's catalogue check knows, so it lists none)
			if ( waiting == "level" )
				CollectionAssert.AreEqual( new RideUpgradeRowKind?[] { RideUpgradeRowKind.CancelLevel }, RowKinds( view ) );
			ActivateRow( view );
			ActivateRow( view );
			Assert.AreEqual( (1, "rideUpgrade", entry), (Management( guest ).Pending.Count(), Top( view ), Waiting( guest, id )), "inert: no second action, the list stays open" );
			view.Update();
			Assert.AreEqual( (1, "rideUpgrade"), (Management( guest ).Pending.Count(), Top( view )) );
			UntilManaged( host, guest, view );
			Assert.AreEqual( "rideUpgrade", Top( view ) );
			// the action came back: the open list's row acts again
			ActivateRow( view );
			Assert.AreEqual( 1, Management( guest ).Pending.Count(), "its row queued its action" );
			Assert.IsNotNull( Waiting( guest, id ) );
			UntilManaged( host, guest, view );
			CutAndCompare( host, guest );
		} );
	}

	/// <summary>
	/// A synthetic add-on of ride type 1100 in the park's object catalogue, alike on every peer before the session starts (the upgrade list's add-on row, priced as the test catalogue's 1203):
	/// the rows of ride <c>One</c>'s list are the add-on, then its level.
	/// </summary>
	private void WithAddOn( Park park, bool host )
	{
		var root = (string)typeof( UpgradeCatalogueDefaultsTests ).GetField( "root", NonPublic )!.GetValue( fixture )!;
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile(
			"Info.Id 1203\nInfo.Name SyntheticJump\nInfo.WhichUIType 0\nAddOn.UpgradesId 1100\nAddOn.UpgradeType 1\nBumper.WhichTrackType 1\n", "addon.sam" ) } );
		var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Upgrade, "/addon", settings, new ObjectShape( new[] { "++", "++" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(),
			Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
		var catalog = (ObjectCatalog)typeof( ObjectCatalog ).GetConstructor( NonPublic, null, new[] { typeof( string ), typeof( IReadOnlyList<ObjectCatalogEntry> ) }, null )!
			.Invoke( new object[] { "jungle", new[] { entry } } );
		typeof( ParkObjects ).GetField( "<Catalog>k__BackingField", NonPublic )!.SetValue( park.Level.Objects, catalog );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void TheAddOnRowWithNeitherTheBuildingGrantNorTheMoneySaysTheGrantsNoteAloneAndWithTheGrantTheMoneyCheckSoundsTheError( bool grant )
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession( WithAddOn, grant ? CoopRights.Attractions | CoopRights.Build : CoopRights.Attractions );
			var ride = guest.One;
			Alike( host, guest, Spend );
			Assert.IsTrue( guest.Economy.Balance <= 0 );
			Show( view, ride );
			OpenUpgradeList( view );
			CollectionAssert.AreEqual( new RideUpgradeRowKind?[] { RideUpgradeRowKind.AddOn, RideUpgradeRowKind.Level }, RowKinds( view ), "the add-on row first" );
			var sent = Numbered( guest );
			var sounds = Sounds( () => ActivateRow( view ) );
			Assert.AreEqual( "hud", Top( view ), "the list closed as after every row" );
			CollectionAssert.AreEqual( new[] { GameAudio.ErrorEvent }, sounds, "the error sound" );
			Assert.IsNull( guest.Level.BuildEntry, "no build tool armed" );
			Assert.AreEqual( (0, sent, 0), (Management( guest ).Pending.Count(), Numbered( guest ), guest.Level.SessionRefusals), "nothing queued or sent" );
			if ( grant )
				Assert.IsNull( Management( guest ).PeekNote(), "with the grant the price check's error sound is all the host's list gives" );
			else
				Assert.AreEqual( new ManagementNote( ManagementNoteKind.Rights, CoopRefusal.NotGranted ), Management( guest ).PeekNote()?.Note, "the grant's note, not the money's" );
			view.Update();
			Assert.AreEqual( 0, view.Says( OpenTpwText.CoopNotGranted ), "the window is out: not yet said" );
			Show( view, null! );
			Assert.AreEqual( grant ? 0 : 1, view.Says( OpenTpwText.CoopNotGranted ) );
			Assert.AreEqual( 0, view.Says( OpenTpwText.NotEnoughMoney ) );
			CatchUpAndCompare( host, guest );
		} );
	}

	/// <summary>
	/// A claimable ride of ride type 1100 (a kart ride, so its sale, track and tool edits can be claimed, with the economy record and the level's catalogue tier a list needs) placed alike on every
	/// peer before the session starts, beside <see cref="PlaceKartRide"/>'s own, which sets the level's track builders up.
	/// </summary>
	private OriginalObject PlaceClaimableRide( Park park )
	{
		PlaceKartRide( park );
		var root = (string)typeof( UpgradeCatalogueDefaultsTests ).GetField( "root", NonPublic )!.GetValue( fixture )!;
		var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/claimable", new ObjectSettings( new[] { new ObjectSettingsFile(
				"Info.Id 1100\nInfo.Name SyntheticClaimable\nInfo.WhichUIType 0\nInfo.IsChoosable 1\nBumper.WhichTrackType 1\nBumper.BumperType -4\nUsageInfo.MinCapacity 1\nUsageInfo.MaxCapacity 100\n"
				+ "UsageInfo.MinDuration 1\nUsageInfo.MaxDuration 200\nUpgrades[0].InitCapacity 1\nUpgrades[0].InitDuration 10\nUpgrades[0].InitSpeed 50\nUpgrades[0].QueueWaitTimeConstant 130\n"
				+ "Upgrades[1].InitCapacity 3\nUpgrades[1].InitDuration 20\nUpgrades[1].InitSpeed 100\nUpgrades[1].QueueWaitTimeConstant 200\n", "claimable.sam" ) } ),
			new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(),
			new BaseFileSystem( root ), null );
		var ride = park.Level.Objects.TryPlace( entry, 26, 9, 0, out var reason )!;
		Assert.IsNotNull( ride, $"{reason}" );
		var state = park.Economy.RegisterExisting( 1100 );
		typeof( Level ).GetMethod( "LinkEconomy", NonPublic )!.Invoke( park.Level, new object[] { ride, state.Id } );
		Assert.IsTrue( park.Level.IsClaimableRide( ride ), "a track ride's station" );
		return ride;
	}

	/// <summary>A host and two guests, the first with <paramref name="firstRights"/> and the second (who holds the claim) with attractions and building, ride type 1100 researched and a claimable ride of it placed alike.</summary>
	private (Park Host, Park First, Park Second, View View, int Ride) ClaimedRideSession( CoopRights firstRights = CoopRights.Attractions, bool claimed = true )
	{
		var (host, first, second) = SharedWithTwo( ( park, isHost ) =>
		{
			RideResearched( park, isHost );
			PlaceClaimableRide( park );
		} );
		var view = ViewOf( first );
		SteppedRounds( 20, host, first, second );
		var (one, two) = (first.Level.Commands.Player, second.Level.Commands.Player);
		if ( firstRights != CoopRights.None )
			Assert.IsTrue( host.Issue( new SetSeatRightsCommand( one, firstRights ) ).Accepted );
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( two, CoopRights.Attractions | CoopRights.Build ) ).Accepted );
		for ( var round = 0; round < 600 && (first.Level.RightsOf( one ) != firstRights || second.Level.RightsOf( two ) != (CoopRights.Attractions | CoopRights.Build)); round++ )
			SteppedRounds( 1, host, first, second );
		var ride = host.Level.TrackStations.Keys.Single( item => item.Entry.InfoId == 1100 ).Runtime.AttractionId;
		if ( claimed )
		{
			GuestClaims( host, second, ride );
			CutAndCompare( host, first, second );
			Assert.AreEqual( two, first.Level.ClaimOf( ride ), "the claim reached the first guest" );
		}
		return (host, first, second, view, ride);
	}

	[TestMethod]
	public void AGuestsSettingsLevelsAndCallOnARideAnotherGuestClaimsAreRefusedAtTheClickWithTheClaimsNoteAndNothingIsSent()
	{
		InParkView( () =>
		{
			var (host, first, second, view, ride) = ClaimedRideSession();
			var queue = Management( first );
			var item = ObjectOf( first, ride );
			Show( view, item );
			var sent = Numbered( first );
			var claimed = new ManagementNote( ManagementNoteKind.Rights, CoopRefusal.Claimed );
			void Refused( Action click, string what )
			{
				var sounds = Sounds( click );
				Assert.IsTrue( sounds.Contains( GameAudio.ErrorEvent ), $"{what}: the error sound" );
				Assert.AreEqual( claimed, queue.TakeNote()?.Note, $"{what}: the claim's note" );
				Assert.AreEqual( (0, sent, 0), (queue.Pending.Count(), Numbered( first ), first.Level.SessionRefusals), $"{what}: nothing queued or sent, no session refusal" );
				Assert.IsNull( Waiting( first, ride ), what );
			}
			// the door: the rights' answer the click asks, as the HUD asks it (the unfinished kart ride's door button is greyed by the window's own track rule)
			var door = new SetAttractionOpenCommand( ride, false );
			Assert.AreEqual( (true, Level.ClaimedReason), (first.Level.Commands.RightsRefusalOf( door )?.RightsRefused, first.Level.Commands.RightsRefusalOf( door )?.Reason), "the door's command" );
			// its settings: a slider's step asks for the ride's group
			var speed = WindowSlider( view, "rideSpeed" );
			var drawn = speed.Value();
			Refused( () => speed.Changed!( drawn + 1 < speed.Steps() ? drawn + 1 : drawn - 1 ), "settings" );
			Assert.AreEqual( drawn, speed.Value(), "the control returned" );
			// its call
			Refused( () => WindowClick( view, "callStaff" ), "call" );
			// its level bought, by the list's row
			OpenUpgradeList( view );
			CollectionAssert.AreEqual( new RideUpgradeRowKind?[] { RideUpgradeRowKind.Level }, RowKinds( view ) );
			Refused( () => ActivateRow( view ), "level bought" );
			Assert.AreEqual( "hud", Top( view ), "the list closed" );
			// its level cancelled: the host (allowed on a claimed ride) bought one, and the list offers the cancel
			Assert.IsTrue( host.Issue( new BuyUpgradeCommand( ride ) ).Accepted, "the host's management of a claimed ride is allowed" );
			RoundsUntil( () => StateOf( first, ride ).PendingLevel == 1, host, first, view, "the host's purchase reached the guest" );
			OpenUpgradeList( view );
			CollectionAssert.AreEqual( new RideUpgradeRowKind?[] { RideUpgradeRowKind.CancelLevel }, RowKinds( view ) );
			Refused( () => ActivateRow( view ), "level cancelled" );
			Assert.AreEqual( "hud", Top( view ) );
			// said once for a run after the window closes
			view.Update();
			WindowClick( view, "callStaff" );
			view.Update();
			Assert.AreEqual( 0, view.Says( OpenTpwText.CoopClaimed ), "the window is out: not yet said" );
			Show( view, null! );
			Assert.AreEqual( 1, view.Says( OpenTpwText.CoopClaimed ), "said once after it closed" );
			CutAndCompare( host, first, second );
		} );
	}

	/// <summary>Frames of the host and both guests (the second one's HUD up) until the second guest's management requests all came back.</summary>
	private void UntilManagedBy( Park host, Park first, Park second, View secondView, int limit = 600 )
	{
		for ( var round = 0; round < limit && Management( second ).Pending.Any(); round++ )
		{
			SteppedRounds( 1, host, first, second );
			secondView.Update();
		}
		Assert.IsFalse( Management( second ).Pending.Any(), "the second guest's management requests came back" );
	}

	[DataTestMethod]
	[DataRow( "door", false )]
	[DataRow( "settings", false )]
	[DataRow( "call", false )]
	[DataRow( "level", false )]
	[DataRow( "cancel", false )]
	[DataRow( "door", true )]
	[DataRow( "settings", true )]
	[DataRow( "call", true )]
	[DataRow( "level", true )]
	[DataRow( "cancel", true )]
	public void AClaimTakenAfterTheClickRefusesTheEntryBeforeItsSendOrAtItsRecordWithTheClaimsNoteOnEveryPeer( string kind, bool afterSend )
	{
		InParkView( () =>
		{
			var (host, first, second, view, ride) = ClaimedRideSession( claimed: false );
			var (one, two) = (first.Level.Commands.Player, second.Level.Commands.Player);
			var queue = Management( first );
			var item = ObjectOf( first, ride );
			if ( kind == "cancel" )
			{
				// a level the host bought stands for the list's cancel row
				Assert.IsTrue( host.Issue( new BuyUpgradeCommand( ride ) ).Accepted );
				SteppedRounds( 30, host, first, second );
			}
			Show( view, item );
			var speed = WindowSlider( view, "rideSpeed" );
			var drawn = speed.Value();
			var sent = Numbered( first );
			var snapshot = ( ParkObjectState state ) => (state.MaintenanceRequested, state.PendingLevel, state.UpgradePurchases, state.IsOpen);
			var before = snapshot( StateOf( host, ride ) );
			// the click, with no claim on the ride: its entry is queued
			switch ( kind )
			{
				case "door":
					Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Attraction( ride ), ManagementKnob.Door, new SetAttractionOpenCommand( ride, false ) ) } ).Queued );
					break;
				case "settings":
					speed.Changed!( drawn + 1 < speed.Steps() ? drawn + 1 : drawn - 1 );
					break;
				case "call":
					WindowClick( view, "callStaff" );
					break;
				default:
					OpenUpgradeList( view );
					ActivateRow( view );
					break;
			}
			Assert.AreEqual( 1, queue.Pending.Count(), "queued" );
			if ( afterSend )
			{
				// the claim of the second guest first on its way, then the first guest's entry sent: the host takes the claim before the entry
				Assert.IsTrue( second.Issue( new ClaimRideCommand( ride ) ).IsPending );
				SendNext( first );
				Assert.AreEqual( sent + 1, Numbered( first ), "sent" );
			}
			else
			{
				GuestClaims( host, second, ride );
				CutAndCompare( host, first, second );
				Assert.AreEqual( (two, sent), (first.Level.ClaimOf( ride ), Numbered( first )), "the claim reached the first guest, and nothing is sent yet" );
			}
			for ( var round = 0; round < 600 && queue.Pending.Any(); round++ )
			{
				SteppedRounds( 1, host, first, second );
				view.Update();
			}
			Assert.IsFalse( queue.Pending.Any(), "the entry resolved" );
			CutAndCompare( host, first, second );
			Assert.AreEqual( two, host.Level.ClaimOf( ride ), "the second guest holds the claim on every peer" );
			Assert.AreEqual( new ManagementNote( ManagementNoteKind.Rights, CoopRefusal.Claimed ), queue.PeekNote()?.Note, "the claim's note, whichever the phase" );
			Assert.AreEqual( afterSend ? sent + 1 : sent, Numbered( first ), afterSend ? "refused at its record" : "refused before its send: nothing went" );
			Assert.AreEqual( afterSend ? 1 : 0, HostCommandsOf( host, one ).Count( command => command is not ClaimRideCommand ), afterSend ? "the stream holds the refused record, which every peer refuses alike" : "the host's stream holds nothing of the entry" );
			foreach ( var park in new[] { host, first, second } )
				Assert.AreEqual( before, snapshot( StateOf( park, ride ) ), $"nothing changed on {(park == host ? "the host" : "a guest")}'s park" );
			Assert.AreEqual( (0, 0), (queue.Pending.Count(), first.Level.SessionRefusals), "no session refusal" );
			Show( view, null! );
			Assert.AreEqual( 1, view.Says( OpenTpwText.CoopClaimed ), "said once after the window closed" );
			Assert.AreEqual( 0, view.Says( OpenTpwText.CoopNotPermitted ) );
		} );
	}

	[TestMethod]
	public void TheClaimingGuestsAndTheHostsOwnManagementOfAClaimedRideIsAllowed()
	{
		InParkView( () =>
		{
			var (host, first, second, _, ride) = ClaimedRideSession();
			var secondView = ViewOf( second );
			var hostView = ViewOf( host );
			var seat = second.Level.Commands.Player;
			// the claimer's HUD would let go of a claim its track tool is not on (SettleClaim, once a second): this claimer holds it without a tool, so that release is held off
			second.Session!.LastStrayClaimRelease = double.PositiveInfinity;
			// the claiming guest's call goes to the host and is taken
			Show( secondView, ObjectOf( second, ride ) );
			WindowClick( secondView, "callStaff" );
			Assert.AreEqual( new RequestMaintenanceCommand( ride, true ), Waiting( second, ride ), "queued, not refused" );
			UntilManagedBy( host, first, second, secondView );
			CutAndCompare( host, first, second );
			foreach ( var park in new[] { host, first, second } )
				Assert.AreEqual( (true, false), (StateOf( park, ride ).MaintenanceRequested, ObjectOf( park, ride ).IsOpen), "the claiming guest's call is applied on every peer" );
			Assert.AreEqual( (0, 0, 0), (Management( second ).TakeNotes().Count, second.Level.SessionRefusals, secondView.Hud.Messages.Count) );
			CollectionAssert.AreEqual( IssuedBy( seat, new RequestMaintenanceCommand( ride, true ) ), HostCommandsOf( host, seat ).Where( command => command is RequestMaintenanceCommand ).ToArray() );
			// its settings: a slider's step is queued and taken too
			var speed = WindowSlider( secondView, "rideSpeed" );
			var drawn = speed.Value();
			speed.Changed!( drawn + 1 < speed.Steps() ? drawn + 1 : drawn - 1 );
			Assert.AreEqual( 1, Management( second ).Pending.Count() );
			UntilManagedBy( host, first, second, secondView );
			Assert.AreEqual( 1, HostCommandsOf( host, seat ).Count( command => command is SetRideSettingsCommand ), "its settings record is in the stream" );
			Assert.AreEqual( (0, 0), (Management( second ).TakeNotes().Count, second.Level.SessionRefusals) );
			// the host's management of the claimed ride stays allowed (a C3 log replays): its call button cancels the call at once, and its door command is not refused by the rights
			Show( hostView, ObjectOf( host, ride ) );
			WindowClick( hostView, "callStaff" );
			Assert.IsFalse( StateOf( host, ride ).MaintenanceRequested );
			var door = host.Issue( new SetAttractionOpenCommand( ride, true ) );
			Assert.IsFalse( door.RightsRefused, $"the host's door: {door.Reason}" );
			CutAndCompare( host, first, second );
			foreach ( var park in new[] { host, first, second } )
				Assert.AreEqual( seat, park.Level.ClaimOf( ride ), "the claim stands on every peer" );
		} );
	}

	[TestMethod]
	public void WithoutTheAttractionsRightAGuestsClickOnAClaimedRideSaysTheRightsNoteInstead()
	{
		InParkView( () =>
		{
			var (host, first, second, view, ride) = ClaimedRideSession( CoopRights.Research );
			var queue = Management( first );
			Show( view, ObjectOf( first, ride ) );
			var sent = Numbered( first );
			WindowClick( view, "callStaff" );
			var door = first.Level.Commands.RightsRefusalOf( new SetAttractionOpenCommand( ride, false ) );
			Assert.AreEqual( (true, CoopBuildRights.NotPermittedReason), (door?.RightsRefused, door?.Reason), "the door's rights' answer: the right comes before the claim" );
			Assert.AreEqual( new ManagementNote( ManagementNoteKind.Rights, CoopRefusal.NotPermitted ), queue.PeekNote()?.Note );
			Assert.AreEqual( (0, sent), (queue.Pending.Count(), Numbered( first )) );
			view.Update();
			Show( view, null! );
			Assert.AreEqual( (1, 0, 0), (view.Says( OpenTpwText.CoopNotPermitted ), view.Says( OpenTpwText.CoopClaimed ), first.Level.SessionRefusals) );
			CutAndCompare( host, first, second );
		} );
	}

	/// <summary>
	/// A host's or a single player's window as it always was: its door, its call and its list rows issue at once (no queue, nothing greyed while waiting, the list closing after its row), each
	/// change of the park there before the next frame.
	/// </summary>
	private void OwnWindowIssuesAtOnce( Park park, View view )
	{
		var ride = park.One;
		var id = ride.Runtime.AttractionId;
		Assert.IsFalse( park.Level.Commands.SendsRequests, "no guest: nothing is queued for a host" );
		Show( view, ride );
		Assert.AreEqual( new Look( false, true, Normal, DoorHelp( view, closed: false ), false, true, Normal, "help535", "", true ), LookOf( view ) );
		// the door
		WindowClick( view, "openRide" );
		Assert.IsFalse( ride.IsOpen, "the door closed at once" );
		view.Update();
		Assert.AreEqual( new Look( true, true, UiButton.DownFrame, DoorHelp( view, closed: true ), false, true, Normal, "help535", view.Strings[UIStrings.Closed], false ), LookOf( view ), "nothing greyed" );
		WindowClick( view, "openRide" );
		Assert.IsTrue( ride.IsOpen, "and opened at once" );
		// the call
		view.Update();
		WindowClick( view, "callStaff" );
		Assert.AreEqual( (true, false), (StateOf( park, id ).MaintenanceRequested, ride.IsOpen), "the call set and the ride closed at once" );
		view.Update();
		var called = LookOf( view );
		Assert.AreEqual( (true, true, true, false, "help570"), (called.Called, called.Closed, called.CallEnabled, called.DoorEnabled, called.CallHelp),
			"the call button takes its cancel; the door is greyed by the park's own guard while the call stands, as it always was" );
		WindowClick( view, "callStaff" );
		Assert.AreEqual( (false, true), (StateOf( park, id ).MaintenanceRequested, ride.IsOpen), "the call cancelled and the ride open at once" );
		// the list: its level row buys at once and closes the list; its cancel row cancels at once and closes it
		view.Update();
		OpenUpgradeList( view );
		CollectionAssert.AreEqual( new RideUpgradeRowKind?[] { RideUpgradeRowKind.Level }, RowKinds( view ) );
		ActivateRow( view );
		Assert.AreEqual( ("hud", 1, false), (Top( view ), StateOf( park, id ).PendingLevel, ride.IsOpen), "bought at once, the list closed after its row" );
		view.Update();
		OpenUpgradeList( view );
		CollectionAssert.AreEqual( new RideUpgradeRowKind?[] { RideUpgradeRowKind.CancelLevel }, RowKinds( view ) );
		ActivateRow( view );
		Assert.AreEqual( ("hud", 0, true), (Top( view ), StateOf( park, id ).PendingLevel, ride.IsOpen), "cancelled at once, the list closed after its row" );
		Assert.AreEqual( 0, view.Hud.Messages.Count, "no note" );
	}

	[TestMethod]
	public void AHostsDoorListAndCallIssueAtOnceAsBefore()
	{
		InParkView( () =>
		{
			var (host, guest, _) = AttractionsSession();
			var hostView = ViewOf( host );
			var id = host.One.Runtime.AttractionId;
			OwnWindowIssuesAtOnce( host, hostView );
			CollectionAssert.AreEqual( IssuedBy( ParkCommands.LocalPlayer, new SetAttractionOpenCommand( id, false ), new SetAttractionOpenCommand( id, true ), new RequestMaintenanceCommand( id, true ),
				new RequestMaintenanceCommand( id, false ), new BuyUpgradeCommand( id ), new CancelUpgradeCommand( id ) ),
				HostCommandsOf( host, ParkCommands.LocalPlayer ).Where( command => command is SetAttractionOpenCommand or RequestMaintenanceCommand or BuyUpgradeCommand or CancelUpgradeCommand ).ToArray(),
				"the host's own commands, with no expectation of their own" );
			Rounds( 60, host, guest );
			CutAndCompare( host, guest );
			Assert.AreEqual( (true, 0), (guest.One.IsOpen, StateOf( guest, id ).PendingLevel), "the guest follows every record" );
		} );
	}

	[TestMethod]
	public void ASinglePlayersDoorListAndCallIssueAtOnceAsBefore()
	{
		InParkView( () =>
		{
			var park = NewPark();
			park.Enter();
			RideResearched( park, true );
			var view = ViewOf( park );
			Rounds( 20, park );
			Assert.IsNull( park.Level.CoopSession );
			OwnWindowIssuesAtOnce( park, view );
			Rounds( 60, park );
			Assert.AreEqual( 0, park.Level.SessionRefusals );
		} );
	}

	[TestMethod]
	public void AnAcceptedCancelRowClosesTheListAndOpensTheRideOnEveryPeer()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession();
			var ride = guest.One;
			var id = ride.Runtime.AttractionId;
			GuestBuys( host, guest, view, ride );
			Assert.AreEqual( (1, false), (StateOf( guest, id ).PendingLevel, ride.IsOpen) );
			OpenUpgradeList( view );
			CollectionAssert.AreEqual( new RideUpgradeRowKind?[] { RideUpgradeRowKind.CancelLevel }, RowKinds( view ) );
			ActivateRow( view );
			Assert.AreEqual( new CancelUpgradeCommand( id, 1, 1 ), Waiting( guest, id ), "the cancel names the pending level and the purchase" );
			view.Update();
			Assert.AreEqual( (false, true), (LookOf( view ).Closed, LookOf( view ).InfoOpen), "drawn open while it waits" );
			UntilManaged( host, guest, view );
			Assert.AreEqual( "hud", Top( view ), "an accepted cancel closes the list" );
			foreach ( var park in new[] { host, guest } )
				Assert.AreEqual( (0, 1, true), (StateOf( park, id ).PendingLevel, StateOf( park, id ).UpgradePurchases, ObjectOf( park, id ).IsOpen), "cancelled, the count never lowered" );
			Assert.AreEqual( (0, 0), (Management( guest ).TakeNotes().Count, guest.Level.SessionRefusals), "accepted: nothing said" );
			CutAndCompare( host, guest );
			Rounds( 60, host, guest );
			CutAndCompare( host, guest );
		} );
	}
}
