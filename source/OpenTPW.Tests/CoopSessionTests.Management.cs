using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] [EXT:online-coop-permissions] [EXT:online-coop-money] A guest's research, finance and loans windows in a shared park (C4b-1, docs/COOP.md "A guest's management
/// windows"): with the right, each action goes to the host through the session's management queue and its control draws what was asked until it came back; without it, the rights' note and
/// nothing sent. The loans' confirmation waits for its outcome, the door's advisor event follows an accepted door, and the notes wait until the HUD's feedback lines can be seen.
/// </summary>
public partial class CoopSessionTests
{
	private static CoopManagementRequests Management( Park guest ) => guest.Session!.Management!;

	/// <summary>Frames as the game runs them until the guest's management requests all came back and its HUD took their outcomes.</summary>
	private void UntilManaged( Park host, Park guest, View view, int limit = 600 ) =>
		RoundsUntil( () => !Management( guest ).Pending.Any(), host, guest, view, "the guest's management requests came back", limit );

	/// <summary>The guest's session step once its queue's pacing allows a send: its next management request goes to the host.</summary>
	private void SendNext( Park guest )
	{
		clock += CoopManagementRequests.SendSeconds + 0.01;
		guest.Enter();
		guest.Session!.Step();
	}

	/// <summary>Frames of the host and the guest without the guest's HUD, until <paramref name="done"/>: an outcome came back that no HUD took yet.</summary>
	private void FramesWithoutHud( Park host, Park guest, Func<bool> done, string what )
	{
		for ( var round = 0; round < 600 && !done(); round++ )
		{
			clock += 1 / 60.0;
			host.Frame( 1 / 60.0 );
			guest.Frame( 1 / 60.0 );
			guest.Session!.Step();
		}
		Assert.IsTrue( done(), what );
	}

	/// <summary>
	/// Brings the guest to the host's tick with every record applied, then changes both parks' money alike outside the commands (the test's own), so they stay equal: a park's balance is
	/// read by its records.
	/// </summary>
	private void Alike( Park host, Park guest, Action<ParkEconomy> change )
	{
		host.Holds = true;
		hostTimeFrozenAt = clock;
		var hostStream = host.Session!.HostStream!;
		var guestStream = guest.Session!.GuestStream!;
		for ( var round = 0; round < 3000 && (guest.Level.TickCount != host.Level.TickCount || guestStream.LastRecord != hostStream.LastRecord
			|| guest.Level.Commands.ScheduledCount > 0); round++ )
			Rounds( 1, host, guest );
		Assert.AreEqual( host.Level.TickCount, guest.Level.TickCount );
		change( host.Economy );
		change( guest.Economy );
		hostTimeFrozenAt = null;
		host.Holds = false;
	}

	/// <summary>Spends all of a park's money.</summary>
	private static void Spend( ParkEconomy economy ) => economy.Ledger.Post( LedgerCategory.OtherCosts, Math.Max( 0, economy.Balance ) );

	/// <summary>Gives a park money back.</summary>
	private static void Earn( ParkEconomy economy ) => economy.Ledger.Post( LedgerCategory.OtherIncome, 2_000_000 );

	/// <summary>An element of the loans window's screen, wherever it stands on the stack (a tab under its confirmation, for the queue's order).</summary>
	private static T LoansElement<T>( View view, string id ) where T : UiElement => (T)view.Hud.Stack.Screens.Single( screen => screen.Name == "loans" ).Find( id )!;

	/// <summary>The HUD's frame with <paramref name="keys"/> down for it alone (the door's Ctrl+O and Ctrl+C).</summary>
	private static void Keys( View view, params Key[] keys )
	{
		var typing = Input.TextEntryActive;
		Input.TextEntryActive = false;
		try
		{
			using ( Input.InjectKeyDown( keys ) )
				Input.UpdateButtons();
			view.Update();
		}
		finally
		{
			Input.UpdateButtons();
			Input.TextEntryActive = typing;
		}
	}

	/// <summary>How many requests the guest's session numbered so far.</summary>
	private static uint Numbered( Park guest ) => guest.Session!.GuestCommands!.LastSubmitted?.Sequence ?? 0;

	private static T Element<T>( View view, string id ) where T : UiElement => (T)view.Hud.Stack.Top!.Find( id )!;

	private static string Top( View view ) => view.Hud.Stack.Top!.Name;

	[TestMethod]
	public void AGuestsEffortDragGoesOnceWithItsLatestValueAndWithoutTheRightNothingGoes()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		GiveRights( host, guest, view, CoopRights.Research );
		view.Hud.OpenResearchWindow();
		view.Update();
		var slider = Element<UiSlider>( view, "effort0" );
		var before = Numbered( guest );
		var effort = guest.Economy.Research.Effort[(int)ResearchCategory.Ride];
		var target = effort == 30 ? 33 : 30;
		// a drag's steps in one frame: one entry, the latest value, drawn at once
		foreach ( var step in new[] { target - 2, target - 1, target } )
			slider.Changed!( step );
		Assert.AreEqual( target, slider.Value(), "the slider draws the value asked for" );
		Assert.AreEqual( 1, Management( guest ).Pending.Count() );
		UntilManaged( host, guest, view );
		Assert.AreEqual( before + 1, Numbered( guest ), "one request went" );
		Assert.AreEqual( (target, target), (guest.Economy.Research.Effort[0], host.Economy.Research.Effort[0]) );
		Assert.AreEqual( target, slider.Value() );
		// without the right: the rights' note once for the run, and nothing queued or sent
		GiveRights( host, guest, view, CoopRights.Finance );
		var refusals = guest.Level.SessionRefusals;
		foreach ( var step in new[] { 50, 51, 52 } )
			slider.Changed!( step );
		view.Update();
		Assert.AreEqual( refusals + 3, guest.Level.SessionRefusals );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotPermitted ), "said once for the run" );
		Assert.AreEqual( 0, Management( guest ).Pending.Count() );
		Assert.AreEqual( target, slider.Value(), "the park's value" );
		Rounds( 30, host, guest );
		Assert.AreEqual( before + 1, Numbered( guest ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsWorkloadKeysGoAndARefusalReturnsTheKnobWithoutWritingIt()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		GiveRights( host, guest, view, CoopRights.Research );
		// a lab whose workload the knob shows (70..100)
		Assert.IsTrue( host.Issue( new SetResearchWorkloadCommand( 80 ) ).Accepted );
		RoundsUntil( () => guest.Economy.Research.Workload == 80, host, guest, view, "the host's workload" );
		view.Hud.OpenResearchWindow();
		view.Update();
		var knob = Element<ParkHud.ResearchWorkloadKnob>( view, "workloadKnob" );
		var before = Numbered( guest );
		// two key presses: one entry, counted from the drawn workload
		knob.Adjust( 1 );
		knob.Adjust( 1 );
		Assert.AreEqual( 1, Management( guest ).Pending.Count() );
		Assert.AreEqual( new SetResearchWorkloadCommand( 82 ), Management( guest ).PendingCommand( ManagementTarget.Research, ManagementKnob.Workload ) );
		UntilManaged( host, guest, view );
		Assert.AreEqual( (82, 82, before + 1), (guest.Economy.Research.Workload, host.Economy.Research.Workload, Numbered( guest )) );
		// a refusal at the record (the right taken back while the request was on its way): the knob shows the lab's workload again, and nothing is written
		var value = knob.Value;
		knob.Adjust( 1 );
		Assert.AreNotEqual( value, knob.Value, "the knob follows its player while it waits" );
		SendNext( guest );
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( guest.Level.Commands.Player, CoopRights.Finance ) ).Accepted );
		UntilManaged( host, guest, view );
		view.Update();
		Assert.AreEqual( (82, 82), (guest.Economy.Research.Workload, host.Economy.Research.Workload), "refused on every peer" );
		Assert.AreEqual( value, knob.Value, "the knob shows the lab's workload again" );
		Assert.AreEqual( before + 2, Numbered( guest ), "the projection wrote nothing" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotPermitted ) );
		CatchUpAndCompare( host, guest );
	}

	/// <summary>A lab with projects to choose from (the asset-free park's first projects researched alike on every peer before the session starts).</summary>
	private static void Researched( Park park, bool host )
	{
		var research = park.Economy.Research;
		for ( var round = 0; round < 200 && Enum.GetValues<ResearchCategory>().All( category => research.SelectableItems( category ).Count < 2 ); round++ )
			research.AddResearcherPoints( 50 );
	}

	[TestMethod]
	public void AGuestsProjectMoveNamesTheProjectItShowsAndASecondQuickClickIsNoClick()
	{
		var (host, guest, _, _) = Shared( Researched );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		GiveRights( host, guest, view, CoopRights.Research );
		var rows = new[] { ResearchCategory.Ride, ResearchCategory.Upgrade, ResearchCategory.Shop, ResearchCategory.Sideshow, ResearchCategory.Feature };
		var category = rows.FirstOrDefault( each => guest.Economy.Research.SelectableItems( each ).Count > 1 );
		Assert.IsTrue( guest.Economy.Research.SelectableItems( category ).Count > 1, "a category with projects to choose from" );
		var row = Array.IndexOf( rows, category );
		view.Hud.OpenResearchWindow();
		view.Update();
		var next = Element<UiButton>( view, $"next{row}" );
		var shown = guest.Economy.Research.Current( category )!;
		var before = Numbered( guest );
		// two quick clicks: the second is no click while the first waits
		next.Activate();
		next.Activate();
		Assert.AreEqual( 1, Management( guest ).Pending.Count() );
		Assert.AreEqual( new MoveResearchCommand( category, 1, shown.InfoId, shown.Level ), Management( guest ).Pending.Single().Command, "it names the project shown" );
		UntilManaged( host, guest, view );
		Assert.AreEqual( before + 1, Numbered( guest ) );
		Assert.AreNotEqual( shown, guest.Economy.Research.Current( category ) );
		Assert.AreEqual( host.Economy.Research.Current( category ), guest.Economy.Research.Current( category ) );
		// the host moved first: the guest's move of the project its window showed is stale, and says so as the host's window would
		var showing = guest.Economy.Research.Current( category )!;
		next.Activate();
		Assert.IsTrue( host.Issue( new MoveResearchCommand( category, -1 ) ).Accepted );
		UntilManaged( host, guest, view );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.NotAvailable ) );
		Assert.AreNotEqual( showing, host.Economy.Research.Current( category ), "the host's move alone applied" );
		Assert.AreEqual( host.Economy.Research.Current( category ), guest.Economy.Research.Current( category ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsDoorGoesByItsButtonAndKeysAndTheAdvisorHearsOnlyAnAcceptedDoor()
	{
		var (host, guest, _, _) = Shared();
		var doors = new List<bool>();
		var view = ViewOf( guest, hudHost: new HudHost { ParkDoorPressed = doors.Add } );
		Rounds( 20, host, guest );
		GiveRights( host, guest, view, CoopRights.Finance );
		var open = guest.Economy.IsParkOpen;
		// the button asks for the opposite of the drawn door and draws it at once
		view.Hud.OpenEntryPriceWindow();
		view.Update();
		var door = Element<UiButton>( view, "door" );
		Assert.AreEqual( !open, door.Selected!(), "a closed door is drawn down" );
		door.Activate();
		Assert.AreEqual( open, door.Selected!(), "drawn as asked" );
		Assert.AreEqual( 0, doors.Count, "the advisor waits for the host" );
		UntilManaged( host, guest, view );
		Assert.AreEqual( (!open, !open), (guest.Economy.IsParkOpen, host.Economy.IsParkOpen) );
		CollectionAssert.AreEqual( new[] { !open }, doors, "the accepted door's event, once" );
		view.Escape();
		Assert.AreEqual( "hud", Top( view ) );
		// the keys ask for the state they name: the drawn state's own key is no click
		var same = !open ? Key.O : Key.C;
		var other = !open ? Key.C : Key.O;
		Keys( view, Key.ControlLeft, same );
		Assert.AreEqual( 0, Management( guest ).Pending.Count(), "the door is drawn in that state already" );
		Keys( view, Key.ControlLeft, other );
		Assert.AreEqual( 1, Management( guest ).Pending.Count() );
		UntilManaged( host, guest, view );
		Assert.AreEqual( open, guest.Economy.IsParkOpen );
		CollectionAssert.AreEqual( new[] { !open, open }, doors );
		// refused (the right taken back while it was on its way): no event, the rights' note
		Keys( view, Key.ControlLeft, same );
		SendNext( guest );
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( guest.Level.Commands.Player, CoopRights.None ) ).Accepted );
		UntilManaged( host, guest, view );
		view.Update();
		Assert.AreEqual( open, guest.Economy.IsParkOpen );
		Assert.AreEqual( 2, doors.Count, "no event for a refused door" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotPermitted ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TwoQuickFeeClicksDrawTheSecondUntilItComesBackAndARenameClosesAtOnce()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		GiveRights( host, guest, view, CoopRights.Finance );
		view.Hud.OpenEntryPriceWindow();
		view.Update();
		var fee = guest.Economy.EntranceFee;
		var increase = Element<UiButton>( view, "priceIncrease" );
		var value = Element<UiLabel>( view, "priceValue" );
		// the first click goes; the second counts from the drawn fee
		increase.Activate();
		SendNext( guest );
		increase.Activate();
		Assert.AreEqual( new SetEntranceFeeCommand( fee + 2 ), Management( guest ).PendingCommand( ManagementTarget.Finance, ManagementKnob.Fee ) );
		// the first's outcome comes between them: the second's value stays drawn
		FramesWithoutHud( host, guest, () => guest.Economy.EntranceFee == fee + 1, "the first fee applied" );
		view.Update();
		Assert.AreEqual( view.Hud.FormatMoney( fee + 2 ), value.Text!(), "the second fee is drawn" );
		UntilManaged( host, guest, view );
		Assert.AreEqual( (fee + 2, fee + 2), (guest.Economy.EntranceFee, host.Economy.EntranceFee) );
		// the rename closes at once, and the window draws the name until it applies
		Element<UiButton>( view, "parkName" ).Activate();
		Assert.AreEqual( "parkRename", Top( view ) );
		Element<UiTextField>( view, "line1" ).Text = "Shared";
		Element<UiTextField>( view, "line2" ).Text = "Land";
		Element<UiButton>( view, "apply" ).Activate();
		Assert.AreEqual( "entryPrice", Top( view ), "closed at once" );
		var name = new ParkNameLines( "Shared", "Land" ).DisplayName;
		Assert.AreEqual( name, Element<UiButton>( view, "parkName" ).Text!() );
		Assert.AreNotEqual( name, guest.Level.ParkName!.DisplayName, "not applied yet" );
		UntilManaged( host, guest, view );
		Assert.AreEqual( (name, name), (guest.Level.ParkName!.DisplayName, host.Level.ParkName!.DisplayName) );
		CatchUpAndCompare( host, guest );
	}

	/// <summary>
	/// A park the bank lends to (the asset-free park's value is below every offer's amount): the credit test reads the park's value from its own cell map, as a headless economy's
	/// (<see cref="ParkEconomyTests"/>), the same on every peer before the session starts.
	/// </summary>
	private static void Bankable( Park park, bool host ) => park.Economy.Litter.Cells = new ParkCellMap( 128, 128 );

	/// <summary>The guest's loans window, open on its available offers once its refresh came back, the first offer chosen.</summary>
	private void OpenLoans( Park host, Park guest, View view )
	{
		view.Hud.OpenLoansWindow();
		view.Update();
		UntilManaged( host, guest, view );
		Assert.AreEqual( "loans", Top( view ) );
		Assert.IsTrue( guest.Economy.AvailableLoans.Any(), "offers to take" );
	}

	/// <summary>The chosen loan's confirmation, and its Yes.</summary>
	private static void Confirm( View view )
	{
		Element<UiElement>( view, "loanList" ).Activate();
		Assert.AreEqual( "loanConfirm", Top( view ) );
		Element<UiButton>( view, "choice0" ).Activate();
	}

	[TestMethod]
	public void AGuestsTakeWaitsInItsConfirmationAndClosesWhenItApplies()
	{
		var (host, guest, _, _) = Shared( Bankable );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		GiveRights( host, guest, view, CoopRights.Finance );
		OpenLoans( host, guest, view );
		var loans = guest.Economy.Loans.Count;
		Confirm( view );
		Assert.AreEqual( "loanConfirm", Top( view ), "it waits for the host" );
		StringAssert.Contains( Element<UiLabel>( view, "message" ).Text!(), view.Strings.Extra( OpenTpwText.CoopWaitingForHost ) );
		// its buttons are inert while it waits
		Element<UiButton>( view, "choice1" ).Activate();
		Element<UiButton>( view, "choice0" ).Activate();
		Assert.AreEqual( ("loanConfirm", 1), (Top( view ), Management( guest ).Pending.Count()) );
		UntilManaged( host, guest, view );
		Assert.AreEqual( "loans", Top( view ), "accepted: it closed" );
		Assert.AreEqual( (loans + 1, loans + 1), (guest.Economy.Loans.Count, host.Economy.Loans.Count) );
		Assert.AreEqual( 0, view.Hud.Messages.Count, "an accepted action says nothing" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ALoanConfirmationRemovedWhileItWaitsHearsNothingMoreAndItsOutcomeIsANote()
	{
		var (host, guest, _, _) = Shared( Bankable );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		var seat = guest.Level.Commands.Player;
		// each removal: Back, the window's close and the HUD's teardown (a resync's swap clears its stack), with the outcome before or after it
		foreach ( var (remove, what) in new (Action, string)[] { (() => view.Escape(), "Back"), (() => view.Hud.CloseLoansWindow(), "the window's close"),
			(() => view.Hud.Stack.RemoveWhere( screen => screen != view.Hud.Screen ), "the teardown") } )
		{
			foreach ( var before in new[] { true, false } )
			{
				GiveRights( host, guest, view, CoopRights.Finance );
				if ( Top( view ) != "loans" )
					OpenLoans( host, guest, view );
				Confirm( view );
				SendNext( guest );
				// the right is taken back while the take is on its way: refused at its record
				Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, CoopRights.None ) ).Accepted );
				if ( before )
					FramesWithoutHud( host, guest, () => !Management( guest ).Pending.Any(), $"{what}: the outcome came" );
				remove();
				Assert.AreNotEqual( "loanConfirm", Top( view ), $"{what}: removed" );
				UntilManaged( host, guest, view );
				view.Update();
				Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotPermitted ), $"{what}, outcome {(before ? "before" : "after")}: only the note" );
				Assert.IsFalse( view.Hud.Stack.Screens.Any( screen => screen.Name is "loanConfirm" or "loanFailure" ), $"{what}: no dialog" );
				// the note's lines go before the next round
				view.Pass( ParkHud.MessageSeconds + 1 );
			}
		}
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ALoanFailureOpensOverTheLoansWindowOrIsSaidWhenAnotherScreenCoversItsConfirmation()
	{
		var (host, guest, _, _) = Shared( Bankable );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		var seat = guest.Level.Commands.Player;
		GiveRights( host, guest, view, CoopRights.Finance );
		OpenLoans( host, guest, view );
		// refused at its record: the failure dialog over the loans window, whose Back is the loans window's
		Confirm( view );
		SendNext( guest );
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, CoopRights.None ) ).Accepted );
		UntilManaged( host, guest, view );
		Assert.AreEqual( "loanFailure", Top( view ) );
		Assert.AreEqual( view.Strings.Extra( OpenTpwText.CoopNotPermitted ), Element<UiLabel>( view, "message" ).Text!() );
		view.Escape();
		Assert.AreEqual( "loans", Top( view ), "Back returns to the loans window, never to a waiting confirmation" );
		// the host could not take it (its bucket of the guest's requests ran dry): not taken, and the player tries again
		GiveRights( host, guest, view, CoopRights.Finance );
		Confirm( view );
		guest.Enter();
		for ( var index = 0; index < CoopHostStream.GuestCommandBurst; index++ )
			guest.Session!.GuestCommands!.Submit( new SetEntranceFeeCommand( guest.Economy.EntranceFee ) );
		SendNext( guest );
		UntilManaged( host, guest, view );
		Assert.AreEqual( "loanFailure", Top( view ) );
		Assert.AreEqual( view.Strings.Extra( OpenTpwText.CoopRequestNotTaken ), Element<UiLabel>( view, "message" ).Text!() );
		view.Escape();
		Assert.AreEqual( "loans", Top( view ) );
		// another screen over the confirmation when its outcome comes: the confirmation goes, and the failure is a note, not a dialog
		Rounds( 60, host, guest );
		Confirm( view );
		var cover = new UiScreen( "cover" );
		view.Hud.Stack.Push( cover );
		SendNext( guest );
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, CoopRights.None ) ).Accepted );
		UntilManaged( host, guest, view );
		view.Update();
		Assert.AreEqual( "cover", Top( view ) );
		Assert.IsFalse( view.Hud.Stack.Screens.Any( screen => screen.Name is "loanConfirm" or "loanFailure" ) );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotPermitted ) );
		view.Hud.Stack.Pop();
		Assert.AreEqual( "loans", Top( view ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheRightsComeBeforeTheMoneyAndAreAskedAgainAtTheYes()
	{
		var (host, guest, _, _) = Shared( Bankable );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		var seat = guest.Level.Commands.Player;
		GiveRights( host, guest, view, CoopRights.Finance );
		OpenLoans( host, guest, view );
		Confirm( view );
		UntilManaged( host, guest, view );
		Assert.AreEqual( 1, guest.Economy.Loans.Count );
		// the repay with neither the right nor the money: the rights' note alone, before the balance's check
		GiveRights( host, guest, view, CoopRights.None );
		Element<UiButton>( view, "outstanding" ).Activate();
		view.Update();
		Alike( host, guest, Spend );
		var refusals = guest.Level.SessionRefusals;
		Element<UiElement>( view, "loanList" ).Activate();
		Assert.AreEqual( ("loans", refusals + 1), (Top( view ), guest.Level.SessionRefusals), "the rights' note, no funds dialog" );
		// the right taken back between the repay's confirmation opening and its Yes: the rights' note, and nothing sent
		Alike( host, guest, Earn );
		GiveRights( host, guest, view, CoopRights.Finance );
		Element<UiButton>( view, "outstanding" ).Activate();
		UntilManaged( host, guest, view );
		view.Pass( ParkHud.MessageSeconds + 1 );
		var before = Numbered( guest );
		Element<UiElement>( view, "loanList" ).Activate();
		Assert.AreEqual( "loanConfirm", Top( view ) );
		GiveRights( host, guest, view, CoopRights.None );
		refusals = guest.Level.SessionRefusals;
		Element<UiButton>( view, "choice0" ).Activate();
		Assert.AreEqual( ("loans", refusals + 1, before), (Top( view ), guest.Level.SessionRefusals, Numbered( guest )) );
		// the right taken back after a Yes whose repay waits behind an open refresh: the confirmation closes with the rights' failure dialog, one message
		GiveRights( host, guest, view, CoopRights.Finance );
		view.Pass( ParkHud.MessageSeconds + 1 );
		Element<UiButton>( view, "outstanding" ).Activate();
		SendNext( guest );
		Element<UiElement>( view, "loanList" ).Activate();
		Element<UiButton>( view, "choice0" ).Activate();
		Assert.AreEqual( 2, Management( guest ).Pending.Count(), "the refresh open, the repay behind it" );
		// the refresh comes back accepted, and the right goes before the repay's send (a fifth of a second after the refresh's)
		FramesWithoutHud( host, guest, () => Management( guest ).Pending.Count() == 1, "the refresh came back" );
		Assert.AreEqual( ManagementEntryState.Unsent, Management( guest ).Pending.Single().State );
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, CoopRights.None ) ).Accepted );
		UntilManaged( host, guest, view );
		Assert.AreEqual( "loanFailure", Top( view ) );
		Assert.AreEqual( view.Strings.Extra( OpenTpwText.CoopNotPermitted ), Element<UiLabel>( view, "message" ).Text!() );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopNotPermitted ), "said by the dialog alone" );
		view.Escape();
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ARepayTheParkCannotAffordAndOneTheHostRepaidFirstSayWhatTheHostsWindowSays()
	{
		var (host, guest, _, _) = Shared( Bankable );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		GiveRights( host, guest, view, CoopRights.Finance );
		OpenLoans( host, guest, view );
		Confirm( view );
		UntilManaged( host, guest, view );
		var loan = guest.Economy.Loans.Single();
		Element<UiButton>( view, "outstanding" ).Activate();
		UntilManaged( host, guest, view );
		// the park spent its money after the Yes, before the repay went: the park could not afford it
		Element<UiElement>( view, "loanList" ).Activate();
		Element<UiButton>( view, "choice0" ).Activate();
		Alike( host, guest, Spend );
		UntilManaged( host, guest, view );
		Assert.AreEqual( "loanFailure", Top( view ) );
		Assert.AreEqual( view.Strings[UIStrings.InsufficientFunds], Element<UiLabel>( view, "message" ).Text!() );
		view.Escape();
		Alike( host, guest, Earn );
		// the host repaid it first: no longer outstanding at that balance
		Element<UiElement>( view, "loanList" ).Activate();
		Element<UiButton>( view, "choice0" ).Activate();
		Assert.IsTrue( host.Issue( new RepayLoanCommand( loan.OfferIndex, loan.RemainingBalance ) ).Accepted );
		UntilManaged( host, guest, view );
		Assert.AreEqual( "loanFailure", Top( view ) );
		Assert.AreEqual( view.Strings.Extra( OpenTpwText.NotAvailable ), Element<UiLabel>( view, "message" ).Text!() );
		Assert.AreEqual( 0, guest.Economy.Loans.Count );
		view.Escape();
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheLoansWindowsRefreshesGoInOrderAndRefillOnlyTheirOwnOpening()
	{
		var (host, guest, _, _) = Shared( Bankable );
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		var seat = guest.Level.Commands.Player;
		// without the finance right the window sends nothing on its opening and tabs
		var before = Numbered( guest );
		view.Hud.OpenLoansWindow();
		Element<UiButton>( view, "outstanding" ).Activate();
		Element<UiButton>( view, "available" ).Activate();
		Assert.AreEqual( 0, Management( guest ).Pending.Count() );
		view.Hud.CloseLoansWindow();
		// with it: a refresh, a take and a refresh go in that order on the loans target (the last tab pressed under the waiting confirmation, as the queue's order sees it)
		GiveRights( host, guest, view, CoopRights.Finance );
		OpenLoans( host, guest, view );
		view.Hud.CloseLoansWindow();
		before = Numbered( guest );
		view.Hud.OpenLoansWindow();
		Element<UiElement>( view, "loanList" ).Activate();
		Element<UiButton>( view, "choice0" ).Activate();
		LoansElement<UiButton>( view, "available" ).Activate();
		CollectionAssert.AreEqual( new[] { typeof( RefreshLoanOffersCommand ), typeof( TakeLoanCommand ), typeof( RefreshLoanOffersCommand ) },
			Management( guest ).Pending.Select( entry => entry.Command.GetType() ).ToArray() );
		UntilManaged( host, guest, view );
		Assert.AreEqual( before + 3, Numbered( guest ) );
		Assert.AreEqual( "loans", Top( view ), "the take closed its confirmation" );
		Assert.AreEqual( 1, guest.Economy.Loans.Count );
		// two refresh outcomes before the HUD's next frame, under a (repay's) confirmation: both taken in order, the list refilled and the refused one's note said
		Element<UiButton>( view, "available" ).Activate();
		SendNext( guest );
		Element<UiButton>( view, "outstanding" ).Activate();
		Element<UiElement>( view, "loanList" ).Activate();
		Assert.AreEqual( "loanConfirm", Top( view ) );
		FramesWithoutHud( host, guest, () => Management( guest ).Pending.Count() == 1, "the first refresh came back" );
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, CoopRights.None ) ).Accepted );
		FramesWithoutHud( host, guest, () => guest.Level.RightsOf( seat ) == CoopRights.None, "the right went" );
		FramesWithoutHud( host, guest, () => !Management( guest ).Pending.Any(), "the second refresh refused at the send" );
		view.Update();
		Assert.AreEqual( "loanConfirm", Top( view ), "the confirmation is not the refreshes'" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotPermitted ) );
		Element<UiButton>( view, "choice1" ).Activate();
		// the window closed and reopened while the old opening's refresh is unsent: the old one's outcome refills nothing, the new opening's refresh goes after it
		GiveRights( host, guest, view, CoopRights.Finance );
		view.Pass( ParkHud.MessageSeconds + 1 );
		Element<UiButton>( view, "outstanding" ).Activate();
		SendNext( guest );
		Element<UiButton>( view, "available" ).Activate();
		view.Hud.CloseLoansWindow();
		view.Hud.OpenLoansWindow();
		Assert.AreEqual( 3, Management( guest ).Pending.Count(), "the old opening's two and the new one's" );
		Assert.AreEqual( 2, Management( guest ).Pending.Select( entry => entry.Waiter ).Distinct().Count() );
		UntilManaged( host, guest, view );
		Assert.AreEqual( "loans", Top( view ) );
		Assert.AreEqual( 0, view.Hud.Messages.Count, "accepted refreshes say nothing" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void NotesWaitWhileTheFeedbackIsHiddenAndAreShownAsItsLinesHaveRoom()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		var seat = guest.Level.Commands.Player;
		void RefusedFee()
		{
			GiveRights( host, guest, view, CoopRights.Finance );
			Assert.IsTrue( Management( guest ).Enqueue( new[] { new ManagementItem( ManagementTarget.Finance, ManagementKnob.Fee, new SetEntranceFeeCommand( guest.Economy.EntranceFee + 1 ) ) } ).Queued );
			SendNext( guest );
			Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, CoopRights.None ) ).Accepted );
		}
		// a screen that hides the park, the info arm and an opened message each keep a note back for longer than its lines would show it
		foreach ( var (hide, show, what) in new (Action, Action, string)[]
		{
			(() => view.Hud.Stack.Push( new UiScreen( "full" ) { HidesBelow = true } ), () => view.Hud.Stack.Pop(), "a screen that hides the park"),
			(() => view.Hud.SelectObject( guest.One ), () => view.Hud.SelectObject( null ), "the info arm"),
			(() => { Assert.IsTrue( view.Hud.PostGameMessage( "News" ) ); Assert.IsTrue( view.Hud.OpenPendingMessage( 0 ) ); }, () => view.Hud.CloseMessagePanel(), "an opened message")
		} )
		{
			hide();
			view.Update();
			RefusedFee();
			UntilManaged( host, guest, view );
			view.Pass( ParkHud.MessageSeconds + 1 );
			Assert.AreEqual( 0, view.Says( OpenTpwText.CoopNotPermitted ), $"{what}: not yet" );
			show();
			view.Update();
			Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotPermitted ), $"{what}: said once it shows, with its full time" );
			view.Pass( ParkHud.MessageSeconds - 1 );
			Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotPermitted ), $"{what}: still on screen" );
			view.Pass( 2 );
		}
		// four notes at once: three lines, the fourth when a line has room, none pushed out unseen
		view.Hud.Stack.Push( new UiScreen( "full" ) { HidesBelow = true } );
		var texts = new[] { OpenTpwText.CoopNotPermitted, OpenTpwText.CoopRequestNotTaken, OpenTpwText.CoopRequestUnknown, OpenTpwText.NotAvailable };
		var notes = (Queue<ManagementNote>)typeof( ParkHud ).GetField( "managementNotes", NonPublic )!.GetValue( view.Hud )!;
		foreach ( var kind in new[] { ManagementNoteKind.Rights, ManagementNoteKind.NotTaken, ManagementNoteKind.Unknown, ManagementNoteKind.NotAvailable } )
			notes.Enqueue( new ManagementNote( kind, CoopRefusal.NotPermitted ) );
		view.Update();
		Assert.AreEqual( 0, view.Hud.Messages.Count );
		view.Hud.Stack.Pop();
		view.Update();
		CollectionAssert.AreEqual( texts.Take( 3 ).Select( view.Strings.Extra ).ToArray(), view.Hud.Messages.ToArray(), "three lines" );
		view.Pass( ParkHud.MessageSeconds + 1 );
		view.Update();
		CollectionAssert.AreEqual( new[] { view.Strings.Extra( texts[3] ) }, view.Hud.Messages.ToArray(), "the fourth once the lines had room" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TwoPlayersOnOneTargetEndAlikeAndARightTakenBackWithEntriesQueuedIsSaidOnce()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		var seat = guest.Level.Commands.Player;
		GiveRights( host, guest, view, CoopRights.Finance | CoopRights.Research );
		// the host and the guest change the fee and the effort at once: every peer ends alike, and the guest's controls show the park's values
		view.Hud.OpenEntryPriceWindow();
		view.Update();
		Element<UiButton>( view, "priceIncrease" ).Activate();
		Assert.IsTrue( host.Issue( new SetEntranceFeeCommand( 77 ) ).Accepted );
		Assert.IsTrue( Management( guest ).Enqueue( new[] { new ManagementItem( ManagementTarget.Research, ManagementKnob.Effort( ResearchCategory.Shop ),
			new SetResearchEffortCommand( ResearchCategory.Shop, 12 ) ) } ).Queued );
		Assert.IsTrue( host.Issue( new SetResearchEffortCommand( ResearchCategory.Shop, 64 ) ).Accepted );
		UntilManaged( host, guest, view );
		Rounds( 30, host, guest );
		Assert.AreEqual( host.Economy.EntranceFee, guest.Economy.EntranceFee );
		Assert.AreEqual( view.Hud.FormatMoney( guest.Economy.EntranceFee ), Element<UiLabel>( view, "priceValue" ).Text!(), "the park's fee" );
		// a right taken back with entries queued: the open one refused at its record on every peer, the next ones at the pump, said once
		var before = Numbered( guest );
		var increase = Element<UiButton>( view, "priceIncrease" );
		increase.Activate();
		SendNext( guest );
		Element<UiButton>( view, "door" ).Activate();
		increase.Activate();
		Assert.AreEqual( 3, Management( guest ).Pending.Count() );
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, CoopRights.Research ) ).Accepted );
		UntilManaged( host, guest, view );
		view.Update();
		Assert.AreEqual( before + 1, Numbered( guest ), "only the open one had gone" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotPermitted ), "said once" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ABurstOfManagementBesideTheToolsTrafficResolvesEachOnceInItsTargetsOrder()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		GiveRights( host, guest, view, CoopRights.Finance | CoopRights.Research | CoopRights.Build );
		var queue = Management( guest );
		var entries = new List<ManagementEntry>();
		var outcomes = new List<ManagementOutcome>();
		// thirty entries across the three targets, each with a waiter of its own so the test sees every outcome
		for ( var index = 0; index < 30; index++ )
		{
			var item = (index % 3) switch
			{
				0 => new ManagementItem( ManagementTarget.Finance, null, new SetEntranceFeeCommand( 10 + index ) ),
				1 => new ManagementItem( ManagementTarget.Research, null, new SetResearchEffortCommand( ResearchCategory.Ride, index ) ),
				_ => new ManagementItem( ManagementTarget.Loans, null, new RefreshLoanOffersCommand() )
			};
			entries.AddRange( queue.Enqueue( new[] { item }, queue.NewWaiter() ).Entries );
		}
		Assert.AreEqual( 30, entries.Count );
		// beside it, the tools' requests go at once, outside the queue, and more of them than the host's bucket takes at a time
		for ( var round = 0; round < 1200 && queue.Pending.Any(); round++ )
		{
			if ( round % 20 == 0 )
				for ( var tool = 0; tool < 25; tool++ )
					guest.Session!.GuestCommands!.Submit( new BuyLandCommand( 40 + tool, 40, 40 + tool, 40 ) );
			clock += 1 / 60.0;
			host.Frame( 1 / 60.0 );
			guest.Frame( 1 / 60.0 );
			guest.Session!.Step();
			foreach ( var entry in entries.Where( entry => entry.State == ManagementEntryState.Resolved ) )
				if ( entry.Waiter is { } waiter )
					outcomes.AddRange( queue.TakeOutcomes( waiter ) );
		}
		Assert.IsFalse( queue.Pending.Any(), "every entry resolved" );
		Assert.AreEqual( 30, outcomes.Count, "each once" );
		Assert.IsTrue( outcomes.All( outcome => outcome.Kind is ManagementOutcomeKind.Accepted or ManagementOutcomeKind.NotTaken ), "applied, or not taken" );
		foreach ( var target in new[] { ManagementTarget.Finance, ManagementTarget.Research, ManagementTarget.Loans } )
		{
			var order = outcomes.Where( outcome => outcome.Entry.Target == target ).Select( outcome => outcome.Entry.Order ).ToArray();
			CollectionAssert.AreEqual( order.Order().ToArray(), order, $"{target.Kind} in its order" );
		}
		CatchUpAndCompare( host, guest );
	}
	/// <summary>A host and two guests of one in-memory session, every park alike (<paramref name="prepare"/> on each before the session starts), as <see cref="Shared"/> makes one guest.</summary>
	private (Park Host, Park First, Park Second) SharedWithTwo( Action<Park, bool>? prepare = null )
	{
		network = new CoopTestNetwork( () => clock );
		var hostLink = network.Add( host: true );
		var firstLink = network.Add();
		var secondLink = network.Add();
		var (host, first, second) = (NewPark(), NewPark(), NewPark());
		foreach ( var park in new[] { host, first, second } )
		{
			park.Enter();
			prepare?.Invoke( park, park == host );
		}
		host.Pump = () => hostLink.Deliver();
		first.Pump = () => firstLink.Deliver();
		second.Pump = () => secondLink.Deliver();
		foreach ( var link in new[] { hostLink, firstLink, secondLink } )
			link.Deliver();
		host.Enter();
		host.Session = CoopSession.Host( host.Level, hostLink, () => clock, HostTime );
		first.Enter();
		first.Session = CoopSession.Join( first.Level, firstLink, () => clock );
		second.Enter();
		second.Session = CoopSession.Join( second.Level, secondLink, () => clock );
		return (host, first, second);
	}

	/// <summary>Frames of the host and the guests as the game runs them, each guest's session step after its park (where its management queue sends).</summary>
	private void SteppedRounds( int count, Park host, params Park[] guests )
	{
		for ( var round = 0; round < count; round++ )
		{
			clock += 1 / 60.0;
			host.Frame( 1 / 60.0 );
			foreach ( var guest in guests )
			{
				guest.Frame( 1 / 60.0 );
				guest.Session!.Step();
			}
		}
	}

	/// <summary>
	/// A cut: the host holds its park and its clock while every guest comes to the host's tick with every record applied; then each guest's money equals the host's (the balance, the
	/// month's ledger lines, the loans), and so do the digests, the world hashes and the logs.
	/// </summary>
	private void CutAndCompare( Park host, params Park[] guests )
	{
		host.Holds = true;
		hostTimeFrozenAt = clock;
		var hostStream = host.Session!.HostStream!;
		for ( var round = 0; round < 3000 && guests.Any( guest => guest.Level.TickCount != host.Level.TickCount || guest.Session!.GuestStream!.LastRecord != hostStream.LastRecord
			|| guest.Level.Commands.ScheduledCount > 0 ); round++ )
			Rounds( 1, new[] { host }.Concat( guests ).ToArray() );
		hostTimeFrozenAt = null;
		host.Holds = false;
		foreach ( var guest in guests )
		{
			Compare( host, guest );
			Assert.AreEqual( host.Economy.Balance, guest.Economy.Balance, "the balance" );
			CollectionAssert.AreEqual( host.Economy.Ledger.CurrentTotals.OrderBy( line => line.Key ).ToArray(), guest.Economy.Ledger.CurrentTotals.OrderBy( line => line.Key ).ToArray(),
				"the month's ledger lines" );
			CollectionAssert.AreEqual( host.Economy.Loans.ToArray(), guest.Economy.Loans.ToArray(), "the loans" );
		}
	}

	[TestMethod]
	public void AGuestsLoanAndFeeKeepEveryPeersMoneyEqualAtRecordCutsAndTickCutsWhileTheHostChangesRights()
	{
		var (host, first, second) = SharedWithTwo( Bankable );
		Rounds( 20, host, first, second );
		var (one, two) = (first.Level.Commands.Player, second.Level.Commands.Player);
		Assert.AreNotEqual( one, two );
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( one, CoopRights.Finance ) ).Accepted );
		CutAndCompare( host, first, second );
		// the first guest refreshes the offers and takes one, the host gives the second guest research meanwhile; a cut right after each record of the guest's
		var queue = Management( first );
		var waiter = queue.NewWaiter();
		first.Enter();
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Loans, ManagementKnob.Refresh, new RefreshLoanOffersCommand() ) } ).Queued );
		SteppedRounds( 1, host, first, second );
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( two, CoopRights.Research ) ).Accepted );
		for ( var round = 0; round < 600 && queue.Pending.Any(); round++ )
			SteppedRounds( 1, host, first, second );
		CutAndCompare( host, first, second );
		var offer = first.Economy.AvailableLoans.First();
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Loans, null, new TakeLoanCommand( offer.Index, offer.Amount ) ) }, waiter ).Queued );
		for ( var round = 0; round < 600 && queue.Pending.Any(); round++ )
			SteppedRounds( 1, host, first, second );
		Assert.AreEqual( ManagementOutcomeKind.Accepted, queue.TakeOutcomes( waiter ).Single().Kind );
		CutAndCompare( host, first, second );
		Assert.AreEqual( 1, host.Economy.Loans.Count );
		// the fee, while the host gives the first guest building too: a cut after its record, then a tick cut a while later
		Assert.IsTrue( queue.Enqueue( new[] { new ManagementItem( ManagementTarget.Finance, ManagementKnob.Fee, new SetEntranceFeeCommand( host.Economy.EntranceFee + 3 ) ) } ).Queued );
		SteppedRounds( 1, host, first, second );
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( one, CoopRights.Finance | CoopRights.Build ) ).Accepted );
		for ( var round = 0; round < 600 && queue.Pending.Any(); round++ )
			SteppedRounds( 1, host, first, second );
		CutAndCompare( host, first, second );
		SteppedRounds( 240, host, first, second );
		CutAndCompare( host, first, second );
		Assert.AreEqual( (CoopRights.Finance | CoopRights.Build, CoopRights.Research), (second.Level.RightsOf( one ), first.Level.RightsOf( two )), "every peer holds every seat's rights" );
	}
}
