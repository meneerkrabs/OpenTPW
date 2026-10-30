using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.Online.Coop;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's staff tool across a resync (C4c-2, docs/COOP.md "A guest's staff"): the session's staff controller survives the swap, a start inside the cut is armed
/// from the snapshot's state unless its player let go of it (then that state is cleaned up), an armed tool comes back and a placement or a drop inside the cut leaves none, a start refused after
/// the cut gives the old tool back, and a state the new view cannot arm a tool for is cleaned up.
/// </summary>
public partial class CoopJoinSessionTests
{
	/// <summary>A buy menu without items, for a guest's HUD.</summary>
	private sealed class NoBuildItems : IBuildCatalog
	{
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => Array.Empty<BuildItem>();
	}

	/// <summary>A guest's HUD as the game keeps one: made for its park, made again for the park a resync swaps in, and updated after each of the guest's frames.</summary>
	private sealed class StaffView
	{
		private readonly Peer guest;
		private readonly UiStringTable strings = OriginalUiTests.FakeStrings();
		private readonly UiContext context;
		private Level? shown;

		public StaffView( Peer guest )
		{
			this.guest = guest;
			var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
			context = new UiContext( strings, new UiFonts( _ => font ), new UiModels( name => throw new System.IO.FileNotFoundException( name ) ) )
			{ Canvas = new UiCanvas( 2048, 1536 ), ResolveTexture = _ => null };
		}

		public ParkHud Hud { get; private set; } = null!;

		/// <summary>The HUD's frame with <paramref name="input"/> (the pointer off the park by default), on a HUD made again when the guest's park was swapped.</summary>
		public void Update( UiInput? input = null )
		{
			if ( guest.Level is not { } level )
				return;
			guest.Enter();
			if ( !ReferenceEquals( shown, level ) )
			{
				Hud?.Retire();
				Hud = new ParkHud( level, strings, new EconomyParkStatus( () => level.Park!.Economy ), new NoBuildItems(), new HudHost() );
				// the asset-free park has no language files: every applicant is named by its role and id
				typeof( ParkHud ).GetField( "staffNames", BindingFlags.Instance | BindingFlags.NonPublic )!
					.SetValue( Hud, Enum.GetValues<StaffType>().ToDictionary( type => type, _ => Array.Empty<string>() ) );
				shown = level;
			}
			Hud.Update( context, input ?? UiInput.Idle( new N.Vector2( -1 ) ), false, false, false );
		}

		/// <summary>How many of the HUD's lines say <paramref name="text"/>.</summary>
		public int Says( OpenTpwText text ) => Hud.Messages.Count( message => message == strings.Extra( text ) );

		/// <summary>The hire of the first guard applicant <paramref name="which"/> takes, through the staff window. True when it went.</summary>
		public bool Hire( out int candidate, Func<StaffCandidate, bool>? which = null )
		{
			guest.Enter();
			Hud.OpenStaffWindow();
			Hud.FilterStaff( StaffType.Guard );
			Hud.ShowEmployedStaff( false );
			var index = Hud.StaffCandidates.ToList().FindIndex( item => which?.Invoke( item ) ?? true );
			Assert.IsTrue( index >= 0, "an applicant to hire" );
			Hud.SelectStaff( index );
			candidate = Hud.SelectedCandidateId;
			return Hud.HireSelectedStaff();
		}

		/// <summary>The pick-up of employee <paramref name="member"/> through its window. True when it went.</summary>
		public bool PickUp( int member )
		{
			guest.Enter();
			Assert.IsTrue( Hud.OpenEmployeeWindow( guest.Economy.Staff.Members.Single( item => item.Id == member ) ) );
			return Hud.PickUpEmployee();
		}
	}

	/// <summary>A handyman hired on <paramref name="level"/> and standing on a free cell near (18, 12), alike on every park of the session: its id.</summary>
	private static int StandHandyman( Level level )
	{
		var member = EconomyTestData.HireBest( level.Park!.Economy, StaffType.Handyman );
		var placement = level.StaffPlacement!;
		var cell = Enumerable.Range( 0, 12 ).SelectMany( dx => Enumerable.Range( 0, 6 ).Select( dy => (X: 18 + dx, Y: 12 + dy) ) ).First( cell => placement.IsValid( cell.X, cell.Y ) );
		StaffPlacement.PutDown( level.Park!.Economy.Staff, member, cell.X, cell.Y );
		return member.Id;
	}

	/// <summary>A guest with the staff right and a HUD, of a session whose parks hold a standing handyman: the guest, its view, its seat and the handyman's id.</summary>
	private (Peer Guest, StaffView View, int Seat, int Handyman) StaffGuest()
	{
		var handyman = 0;
		prepareParks = level => handyman = StandHandyman( level );
		var guest = Hosted( 1 ).Single();
		Build( 120, 64 );
		var seat = guest.Link.Slot;
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, CoopRights.Staff ) ).Accepted );
		RoundsUntil( () => guest.Level!.RightsOf( seat ) == CoopRights.Staff, "the right reached the guest" );
		var view = new StaffView( guest );
		guest.AfterFrame = () => view.Update();
		Rounds( 2 );
		return (guest, view, seat, handyman);
	}

	/// <summary>The guest's staff commands the host's log took, in order.</summary>
	private ParkCommand[] HostStaffCommands( int seat ) =>
		host.Level!.Commands.Log.Where( entry => entry.Command.Player == seat && entry.Command is StaffToolCommand ).Select( entry => entry.Command ).ToArray();

	/// <summary>The guest misses the stream from now on and asks for frames the host no longer holds: a resync begins.</summary>
	private void BeginResync( Peer guest )
	{
		guest.Link.DropFrame = _ => true;
		guest.Link.Send( host.Link.Id, new CoopResendRequest( 1, 0 ).Encode() );
		RoundsUntil( () => guest.Session!.Phase == CoopGuestPhase.Resyncing, "the guest lost the stream and asked for the park" );
	}

	/// <summary>The guest takes the stream again, and the resync swaps the host's park in; then <paramref name="settled"/> holds.</summary>
	private void FinishResync( Peer guest, Func<bool> settled, string what )
	{
		guest.Link.DropFrame = null;
		var old = guest.Level;
		RoundsUntil( () => guest.Level != old && guest.Session!.Phase == CoopGuestPhase.Following, "the park was swapped in" );
		RoundsUntil( settled, what );
	}

	/// <summary>
	/// A guest's hire or pick-up (<paramref name="pickUp"/>) whose record falls inside a resync's cut, its player letting go of it before (<paramref name="letGo"/>, Escape) or not: after the swap
	/// the tool the snapshot's state names is armed, or the state is cleaned up with no tool.
	/// </summary>
	private void StartInsideTheCut( bool pickUp, bool letGo )
	{
		var (guest, view, seat, handyman) = StaffGuest();
		var staff = guest.Session!.GuestStaff!;
		guest.Link.DropFrame = _ => true;
		var candidate = 0;
		Assert.IsTrue( pickUp ? view.PickUp( handyman ) : view.Hire( out candidate ) );
		if ( letGo )
			view.Update( UiInput.Key( UiKeys.Back ) );
		Assert.AreEqual( !letGo, staff.WantsTool );
		RoundsUntil( () => HostStaffCommands( seat ).Length == 1, "the host applied the start" );
		BeginResync( guest );
		if ( letGo )
		{
			FinishResync( guest, () => !staff.HasOpenRequest && !guest.Level!.HoldsStaff( seat ) && !host.Level!.HoldsStaff( seat ), "the let-go start's state was cleaned up" );
			Assert.AreEqual( StaffToolKind.None, guest.Level!.StaffPlacement!.Kind, "no tool came back" );
			CollectionAssert.AreEqual( new[] { pickUp ? ParkCommandKind.PickUpStaff : ParkCommandKind.BeginStaffHire, pickUp ? ParkCommandKind.DropStaff : ParkCommandKind.CancelStaffHire },
				HostStaffCommands( seat ).Select( command => command.Kind ).ToArray(), "one cleanup" );
			if ( pickUp )
				Assert.AreNotEqual( StaffState.PickedUp, host.Economy.Staff.Members.Single( item => item.Id == handyman ).State, "dropped back" );
		}
		else
		{
			FinishResync( guest, () => guest.Level!.StaffPlacement!.IsActive, "the snapshot's state armed the tool" );
			var tool = guest.Level!.StaffPlacement!;
			if ( pickUp )
				Assert.AreEqual( (StaffToolKind.Carry, handyman), (tool.Kind, tool.Carried!.Id) );
			else
				Assert.AreEqual( (StaffToolKind.Place, candidate), (tool.Kind, tool.Candidate!.Id) );
			Assert.IsTrue( staff.WantsTool );
			Assert.AreEqual( 1, HostStaffCommands( seat ).Length, "nothing more went" );
		}
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopRequestUnknown ), "a start inside the cut says nothing" );
		Build( 30, 65 );
		CatchUpAndCompare( guest );
	}

	[TestMethod]
	public void AWantedHireInsideTheCutIsArmedFromTheSnapshot() => StartInsideTheCut( pickUp: false, letGo: false );

	[TestMethod]
	public void AHireLetGoBeforeItsRecordEnteredTheCutIsCancelledWithNoTool() => StartInsideTheCut( pickUp: false, letGo: true );

	[TestMethod]
	public void AWantedPickUpInsideTheCutIsArmedFromTheSnapshot() => StartInsideTheCut( pickUp: true, letGo: false );

	[TestMethod]
	public void APickUpLetGoBeforeItsRecordEnteredTheCutIsDroppedBackWithNoTool() => StartInsideTheCut( pickUp: true, letGo: true );

	/// <summary>
	/// A guest's armed Place or Carry tool (<paramref name="carry"/>) across a resync: it comes back armed, unless its placement or drop (<paramref name="clickInsideTheCut"/>) applied
	/// inside the cut, which leaves no state and no tool.
	/// </summary>
	private void ToolAcrossTheCut( bool carry, bool clickInsideTheCut )
	{
		var (guest, view, seat, handyman) = StaffGuest();
		var staff = guest.Session!.GuestStaff!;
		var candidate = 0;
		Assert.IsTrue( carry ? view.PickUp( handyman ) : view.Hire( out candidate ) );
		RoundsUntil( () => guest.Level!.StaffPlacement!.IsActive, "the tool armed" );
		var cell = Enumerable.Range( 0, 12 ).SelectMany( dx => Enumerable.Range( 0, 6 ).Select( dy => (X: 22 + dx, Y: 14 + dy) ) )
			.First( cell => guest.Level!.StaffPlacement!.IsValid( cell.X, cell.Y ) );
		guest.Link.DropFrame = _ => true;
		if ( clickInsideTheCut )
		{
			// the tool's click on a valid cell, whose record the guest never gets
			guest.Enter();
			ParkCommand click = carry ? new DropStaffCommand( handyman, cell.X, cell.Y ) : new PlaceStaffCommand( candidate, cell.X, cell.Y );
			staff.Adopt( guest.Level!.Commands.Issue( click ).Request!, GuestStaffRequestKind.Click );
			RoundsUntil( () => HostStaffCommands( seat ).Length == 2, "the host applied the click" );
		}
		BeginResync( guest );
		if ( clickInsideTheCut )
		{
			FinishResync( guest, () => !staff.HasOpenRequest, "the click settled" );
			Assert.AreEqual( (StaffToolKind.None, false), (guest.Level!.StaffPlacement!.Kind, staff.WantsTool), "no state, no tool" );
			Assert.IsFalse( guest.Level.HoldsStaff( seat ) );
			// put down on the clicked cell inside the cut, and walking on since
			Assert.AreNotEqual( StaffState.PickedUp, guest.Economy.Staff.Members.Single( item => item.Id == (carry ? handyman : candidate) ).State );
			Assert.AreEqual( 2, HostStaffCommands( seat ).Length, "no cleanup" );
		}
		else
		{
			FinishResync( guest, () => guest.Level!.StaffPlacement!.IsActive, "the tool came back" );
			var tool = guest.Level!.StaffPlacement!;
			Assert.AreEqual( (carry ? StaffToolKind.Carry : StaffToolKind.Place, carry ? handyman : candidate), (tool.Kind, carry ? tool.Carried!.Id : tool.Candidate!.Id) );
			Assert.AreEqual( 1, HostStaffCommands( seat ).Length );
		}
		Build( 30, 66 );
		CatchUpAndCompare( guest );
	}

	[TestMethod]
	public void AnArmedPlaceToolComesBackAfterTheSwap() => ToolAcrossTheCut( carry: false, clickInsideTheCut: false );

	[TestMethod]
	public void AnArmedCarryToolComesBackAfterTheSwap() => ToolAcrossTheCut( carry: true, clickInsideTheCut: false );

	[TestMethod]
	public void APlacementInsideTheCutLeavesNoStateAndNoTool() => ToolAcrossTheCut( carry: false, clickInsideTheCut: true );

	[TestMethod]
	public void ADropInsideTheCutLeavesNoStateAndNoTool() => ToolAcrossTheCut( carry: true, clickInsideTheCut: true );

	[TestMethod]
	public void AHireRefusedAfterTheCutGivesTheOldToolBack()
	{
		var (guest, view, seat, _) = StaffGuest();
		var staff = guest.Session!.GuestStaff!;
		Assert.IsTrue( view.Hire( out var first ) );
		RoundsUntil( () => guest.Level!.StaffPlacement!.Kind == StaffToolKind.Place, "the first hire armed the Place tool" );
		// the host places another applicant itself, a record the guest never gets before the resync
		guest.Link.DropFrame = _ => true;
		var second = host.Economy.Staff.Candidates.First( item => item.Type == StaffType.Guard && item.Id != first ).Id;
		host.Enter();
		Assert.IsTrue( host.Level!.StaffPlacement!.BeginHire( second ) );
		BeginResync( guest );
		// during the resync the guest hires that applicant: its request waits, and goes against the new park
		Assert.IsTrue( view.Hire( out var asked, item => item.Id == second ) );
		Assert.AreEqual( second, asked );
		FinishResync( guest, () => !staff.HasOpenRequest && guest.Level!.StaffPlacement!.IsActive, "the hire came back refused" );
		var tool = guest.Level!.StaffPlacement!;
		Assert.AreEqual( (StaffToolKind.Place, first, true), (tool.Kind, tool.Candidate!.Id, staff.WantsTool), "the old tool back" );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.NotAvailable ), "the refusal's note" );
		CollectionAssert.AreEqual( new[] { ParkCommandKind.BeginStaffHire, ParkCommandKind.BeginStaffHire }, HostStaffCommands( seat ).Select( command => command.Kind ).ToArray() );
		host.Enter();
		host.Level.StaffPlacement.Cancel();
		Build( 30, 67 );
		CatchUpAndCompare( guest );
	}

	[TestMethod]
	public void AStaffStateTheNewViewCannotArmAToolForIsCleanedUp()
	{
		var (guest, view, seat, _) = StaffGuest();
		var staff = guest.Session!.GuestStaff!;
		Assert.IsTrue( view.Hire( out _ ) );
		RoundsUntil( () => guest.Level!.StaffPlacement!.Kind == StaffToolKind.Place, "the hire armed the Place tool" );
		// the swapped-in park has another tool on before the HUD's first frame
		guest.Join!.Completed = level => Assert.IsTrue( level.BeginPathTool() );
		BeginResync( guest );
		FinishResync( guest, () => !staff.HasOpenRequest && !guest.Level!.HoldsStaff( seat ) && !host.Level!.HoldsStaff( seat ), "the state was cleaned up" );
		Assert.AreEqual( StaffToolKind.None, guest.Level!.StaffPlacement!.Kind );
		Assert.IsTrue( guest.Level.CellTool.IsActive, "the other tool stays" );
		CollectionAssert.AreEqual( new[] { ParkCommandKind.BeginStaffHire, ParkCommandKind.CancelStaffHire }, HostStaffCommands( seat ).Select( command => command.Kind ).ToArray() );
		Build( 30, 68 );
		CatchUpAndCompare( guest );
	}
}
