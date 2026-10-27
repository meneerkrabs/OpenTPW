using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's staff in a shared park (C4c-2, docs/COOP.md "A guest's staff"): the hire and the pick-up go to the host as requests and arm their tool from the seat's
/// state when they come back, the tool's clicks wait for the host one at a time, letting go (Escape, a right click, another tool) cleans the seat's state up after an open request, a refused
/// start gives the old tool back, the tool follows a right taken back in a paused park, the session's end turns it off, and the staff windows go through the management queue, drawing what
/// their player asked for.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>
	/// A host and a guest whose parks hold a handyman standing on a free cell (alike, beside the test park's own mechanic and handyman), the guest with <paramref name="rights"/> and a view: the
	/// guest's seat and the standing handyman's id.
	/// </summary>
	private (Park Host, Park Guest, View View, int Seat, int Handyman) StaffSession( CoopRights rights = CoopRights.Staff )
	{
		var handyman = 0;
		var (host, guest, _, _) = Shared( ( park, _ ) => handyman = StandingHandyman( park ).Id );
		var view = ViewOf( guest );
		WithoutStaffNames( view.Hud );
		Rounds( 20, host, guest );
		if ( rights != CoopRights.None )
			GiveRights( host, guest, view, rights );
		return (host, guest, view, guest.Level.Commands.Player, handyman);
	}

	/// <summary>The HUD's staff names without the game's language files (the asset-free park has none): every employee and applicant is named by its role and id.</summary>
	private static void WithoutStaffNames( ParkHud hud ) =>
		typeof( ParkHud ).GetField( "staffNames", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( hud, Enum.GetValues<StaffType>().ToDictionary( type => type, _ => Array.Empty<string>() ) );

	/// <summary>The employee <paramref name="id"/> of <paramref name="park"/>.</summary>
	private static StaffMember MemberOf( Park park, int id ) => park.Economy.Staff.Members.Single( member => member.Id == id );

	/// <summary>The guest's staff controller.</summary>
	private static CoopGuestStaff GuestStaffOf( Park guest ) => guest.Session!.GuestStaff!;

	/// <summary>
	/// The guest's hire through its staff window, as a click on an applicant's row does: the first applicant of <paramref name="role"/> (a guard by default: the test park employs none, and
	/// its pool offers three) that <paramref name="which"/> takes (any by default). True when the hire went.
	/// </summary>
	private static bool GuestHires( Park guest, View view, out int candidate, Func<StaffCandidate, bool>? which = null, StaffType role = StaffType.Guard )
	{
		guest.Enter();
		var hud = view.Hud;
		hud.OpenStaffWindow();
		hud.FilterStaff( role );
		hud.ShowEmployedStaff( false );
		var index = hud.StaffCandidates.ToList().FindIndex( item => which?.Invoke( item ) ?? true );
		Assert.IsTrue( index >= 0, "an applicant to hire" );
		hud.SelectStaff( index );
		candidate = hud.SelectedCandidateId;
		return hud.HireSelectedStaff();
	}

	/// <summary>The guest's pick-up of <paramref name="member"/> through its employee window's button. True when it went.</summary>
	private static bool GuestPicksUp( Park guest, View view, int member )
	{
		guest.Enter();
		Assert.IsTrue( view.Hud.OpenEmployeeWindow( MemberOf( guest, member ) ) );
		return view.Hud.PickUpEmployee();
	}

	/// <summary>A cell of <paramref name="park"/> a staff member may be put on, at least <paramref name="away"/> cells from (<paramref name="fromX"/>, <paramref name="fromY"/>) when given.</summary>
	private static (int X, int Y) FreeCell( Park park, (int X, int Y)? from = null, int away = 3 )
	{
		var placement = park.Level.StaffPlacement!;
		return Enumerable.Range( 0, 30 ).SelectMany( dx => Enumerable.Range( 0, 12 ).Select( dy => (X: 14 + dx, Y: 10 + dy) ) )
			.First( cell => placement.IsValid( cell.X, cell.Y ) && (from is not { } other || Math.Abs( cell.X - other.X ) + Math.Abs( cell.Y - other.Y ) >= away) );
	}

	/// <summary>
	/// The view's frame with the pointer over the centre of cell (<paramref name="x"/>, <paramref name="y"/>) and the buttons <paramref name="buttons"/> sets: the camera looks straight down on
	/// the cell, and the asset-free park's pointer frame uses the sandbox's ground plane (its original park set aside for the frame), as the coaster tool's tests point.
	/// </summary>
	private static void AtCell( Park park, View view, int x, int y, Func<UiInput, UiInput>? buttons = null )
	{
		var (mode, viewMatrix, projection) = (Camera.Mode, Camera.ViewMatrix, Camera.ProjMatrix);
		var originalPark = typeof( Level ).GetProperty( nameof( Level.OriginalPark ) )!;
		park.Enter();
		var saved = originalPark.GetValue( park.Level );
		try
		{
			Camera.Mode = new ParkCameraMode();
			var origin = park.Level.Objects.Grid.Origin;
			var centre = new N.Vector3( origin.X + (x + 0.5f) * ObjectPlacement.CellSize, origin.Y + (y + 0.5f) * ObjectPlacement.CellSize, 0 );
			Camera.ViewMatrix = N.Matrix4x4.CreateLookAt( centre + new N.Vector3( 0, 0, 30 ), centre, N.Vector3.UnitY );
			Camera.ProjMatrix = N.Matrix4x4.CreatePerspectiveFieldOfView( 0.8f, 2048f / 1536f, 0.5f, 500 );
			originalPark.SetValue( park.Level, null );
			var input = UiInput.Idle( new N.Vector2( 1024, 768 ) );
			view.Input( buttons?.Invoke( input ) ?? input );
		}
		finally
		{
			originalPark.SetValue( park.Level, saved );
			(Camera.Mode, Camera.ViewMatrix, Camera.ProjMatrix) = (mode, viewMatrix, projection);
		}
	}

	/// <summary>A click of the staff tool on cell (<paramref name="x"/>, <paramref name="y"/>): its press (the staff tools take the press), then its release.</summary>
	private static void ClickCell( Park park, View view, int x, int y )
	{
		AtCell( park, view, x, y, input => input with { LeftDown = true, LeftPressed = true } );
		AtCell( park, view, x, y, input => input with { LeftReleased = true } );
	}

	/// <summary>The view's frame with a quick right click on the park view, which ends a tool with the RMB cancel option.</summary>
	private static void RightClick( View view ) => view.Input( UiInput.Idle( new N.Vector2( -1 ) ) with { RightClicked = true } );

	/// <summary>The guest's staff commands the host's log took, in order.</summary>
	private static ParkCommand[] HostStaffCommandsOf( Park host, int seat ) => HostCommandsOf( host, seat ).Where( command => command is StaffToolCommand ).ToArray();

	[TestMethod]
	public void AGuestsHireWaitsForTheHostArmsThePlaceToolAndItsPlacementHiresOnEveryPeer()
	{
		var (host, guest, view, seat, handyman) = StaffSession();
		var staff = GuestStaffOf( guest );
		var tool = guest.Level.StaffPlacement!;
		var employed = guest.Economy.Staff.Members.Count;
		Assert.IsTrue( GuestHires( guest, view, out var candidate ), "the hire went" );
		Assert.IsFalse( view.Hud.StaffWindowOpen, "the staff window closed" );
		Assert.AreEqual( StaffToolKind.None, tool.Kind, "no tool before the host's answer" );
		Assert.IsTrue( staff.StartWaits && staff.HasOpenRequest );
		Assert.AreEqual( 0, guest.Economy.Staff.PendingHireOf( seat ), "nothing changed here yet" );
		RoundsUntil( () => tool.Kind == StaffToolKind.Place, host, guest, view, "the accepted hire armed the Place tool" );
		Assert.AreEqual( (candidate, candidate, candidate), (tool.Candidate!.Id, guest.Economy.Staff.PendingHireOf( seat ), host.Economy.Staff.PendingHireOf( seat )) );
		Assert.IsFalse( staff.HasOpenRequest );
		// a click on a valid cell goes to the host; the tool stays on, inert, and another click is no click, said once
		var cell = FreeCell( guest );
		var sent = Numbered( guest );
		ClickCell( guest, view, cell.X, cell.Y );
		Assert.AreEqual( sent + 1, Numbered( guest ), "the placement went" );
		Assert.AreEqual( StaffToolKind.Place, tool.Kind, "the tool waits for it" );
		ClickCell( guest, view, cell.X, cell.Y );
		view.Update();
		Assert.AreEqual( sent + 1, Numbered( guest ), "one staff request at a time" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopWaitingForHost ), "the waiting note" );
		RoundsUntil( () => tool.Kind == StaffToolKind.None, host, guest, view, "the accepted placement ended the tool" );
		Assert.AreEqual( employed + 1, guest.Economy.Staff.Members.Count );
		var hired = guest.Economy.Staff.Members.Single( member => member.Id == candidate );
		Assert.AreEqual( cell, StaffPlacementRules.CellOf( hired ), "the new employee stands on the clicked cell" );
		Assert.IsFalse( staff.WantsTool || staff.HasOpenRequest );
		CollectionAssert.AreEqual( IssuedBy( seat, new BeginStaffHireCommand( candidate ), new PlaceStaffCommand( candidate, cell.X, cell.Y ) ), HostStaffCommandsOf( host, seat ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsPickUpArmsTheCarryToolAndItsDropPutsTheEmployeeDownOnEveryPeer()
	{
		var (host, guest, view, seat, handyman) = StaffSession();
		var staff = GuestStaffOf( guest );
		var tool = guest.Level.StaffPlacement!;
		var member = handyman;
		Assert.IsTrue( GuestPicksUp( guest, view, member ), "the pick-up went" );
		Assert.IsFalse( view.Hud.EmployeeWindowOpen, "the employee window closed" );
		Assert.AreEqual( StaffToolKind.None, tool.Kind );
		RoundsUntil( () => tool.Kind == StaffToolKind.Carry, host, guest, view, "the accepted pick-up armed the Carry tool" );
		var hand = guest.Level.StaffHands!.Of( seat )!.Value;
		Assert.AreEqual( (member, member), (tool.Carried!.Id, hand.Staff) );
		Assert.AreEqual( (hand.X, hand.Y), tool.Origin );
		Assert.AreEqual( member, host.Level.StaffHands!.Of( seat )!.Value.Staff, "in the seat's hand on the host" );
		var cell = FreeCell( guest, (hand.X, hand.Y) );
		ClickCell( guest, view, cell.X, cell.Y );
		Assert.AreEqual( StaffToolKind.Carry, tool.Kind, "the tool waits for the drop" );
		RoundsUntil( () => tool.Kind == StaffToolKind.None, host, guest, view, "the accepted drop ended the tool" );
		Assert.IsNull( guest.Level.StaffHands.Of( seat ) );
		foreach ( var park in new[] { host, guest } )
			Assert.AreEqual( cell, StaffPlacementRules.CellOf( MemberOf( park, member ) ), "put down on the clicked cell" );
		Assert.IsFalse( staff.WantsTool );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ARefusedHireGivesTheOldToolBackAndAnAcceptedReplacementArmsTheNewOne()
	{
		var (host, guest, view, seat, handyman) = StaffSession();
		var staff = GuestStaffOf( guest );
		var tool = guest.Level.StaffPlacement!;
		Assert.IsTrue( GuestHires( guest, view, out var first ) );
		RoundsUntil( () => tool.Kind == StaffToolKind.Place, host, guest, view, "the first hire armed the Place tool" );
		// a second hire whose candidate the host places itself before the guest's request is taken: refused at its record, the first tool back
		Assert.IsTrue( GuestHires( guest, view, out var second, item => item.Id != first ) );
		Assert.AreEqual( (StaffToolKind.Place, first), (tool.Kind, tool.Candidate!.Id), "the old tool stays on, inert, while the start waits" );
		host.Enter();
		Assert.IsTrue( host.Level.StaffPlacement!.BeginHire( second ), "the host's own hire of the same applicant comes first" );
		RoundsUntil( () => !staff.HasOpenRequest, host, guest, view, "the second hire came back" );
		Assert.AreEqual( (StaffToolKind.Place, first, first), (tool.Kind, tool.Candidate!.Id, guest.Economy.Staff.PendingHireOf( seat )), "the old tool and hire stand" );
		Assert.IsTrue( staff.WantsTool );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.NotAvailable ), "the refusal's note" );
		host.Enter();
		host.Level.StaffPlacement.Cancel();
		// an accepted replacement: the old tool stays until the answer, then the new one arms and the old applicant is free again
		Assert.IsTrue( GuestHires( guest, view, out var third, item => item.Id != first && item.Id != second ) );
		Assert.AreEqual( first, tool.Candidate!.Id );
		RoundsUntil( () => tool.Candidate?.Id == third, host, guest, view, "the replacement armed its tool" );
		Assert.AreEqual( (third, third), (guest.Economy.Staff.PendingHireOf( seat ), host.Economy.Staff.PendingHireOf( seat )) );
		Assert.IsNull( host.Economy.Staff.ReservedBy( first ), "the old applicant is free again" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AHireOverACarryToolDropsTheEmployeeBackAndARefusedPickUpOverAPlaceToolGivesItBack()
	{
		var (host, guest, view, seat, handyman) = StaffSession();
		var staff = GuestStaffOf( guest );
		var tool = guest.Level.StaffPlacement!;
		Assert.IsTrue( GuestPicksUp( guest, view, handyman ) );
		RoundsUntil( () => tool.Kind == StaffToolKind.Carry, host, guest, view, "the pick-up armed the Carry tool" );
		var origin = tool.Origin!.Value;
		// a hire over the Carry tool: the tool stays, inert, until the answer; its record drops the employee back where it was picked up
		Assert.IsTrue( GuestHires( guest, view, out var candidate ) );
		Assert.AreEqual( StaffToolKind.Carry, tool.Kind );
		RoundsUntil( () => tool.Kind == StaffToolKind.Place, host, guest, view, "the hire armed the Place tool" );
		Assert.AreEqual( candidate, tool.Candidate!.Id );
		foreach ( var park in new[] { host, guest } )
		{
			Assert.IsNull( park.Level.StaffHands!.Of( seat ), "the hand is empty" );
			Assert.AreEqual( origin, StaffPlacementRules.CellOf( MemberOf( park, handyman ) ), "dropped back where it was picked up" );
		}
		// a pick-up over the Place tool that the host's own pick-up of the employee comes before: refused at its record, the Place tool back
		Assert.IsTrue( GuestPicksUp( guest, view, handyman ) );
		host.Enter();
		Assert.IsTrue( host.Level.StaffPlacement!.PickUp( MemberOf( host, handyman ) ) );
		RoundsUntil( () => !staff.HasOpenRequest, host, guest, view, "the pick-up came back" );
		Assert.AreEqual( (StaffToolKind.Place, candidate, candidate), (tool.Kind, tool.Candidate!.Id, guest.Economy.Staff.PendingHireOf( seat )), "the Place tool and its hire stand" );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.NotAvailable ), "the refusal's note" );
		host.Enter();
		host.Level.StaffPlacement.Cancel();
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsHireFromAnActiveMoveOrBuildToolEndsThatToolFirst()
	{
		var (host, guest, view, seat) = MoveSession();
		WithoutStaffNames( view.Hud );
		GiveRights( host, guest, view, CoopRights.Build | CoopRights.Staff );
		var staff = GuestStaffOf( guest );
		var tool = guest.Level.StaffPlacement!;
		// from the Move tool: its end goes first, then the hire
		PickUp( host, guest, view );
		Assert.IsTrue( GuestHires( guest, view, out var first ) );
		Assert.IsFalse( guest.Level.IsMoving, "the Move tool ended at once" );
		RoundsUntil( () => tool.Kind == StaffToolKind.Place, host, guest, view, "the hire armed the Place tool" );
		CollectionAssert.AreEqual( new[] { ParkCommandKind.PickUpObject, ParkCommandKind.EndMove, ParkCommandKind.BeginStaffHire },
			HostCommandsOf( host, seat ).Select( command => command.Kind ).ToArray(), "the Move's end, then the hire" );
		view.Escape();
		RoundsUntil( () => !staff.HasOpenRequest && guest.Economy.Staff.PendingHireOf( seat ) == 0, host, guest, view, "Escape cancelled the hire" );
		// from the build tool: it ends at once, and the Place tool arms with the answer
		guest.Enter();
		guest.Level.BuildEntry = guest.One.Entry;
		Assert.IsTrue( GuestHires( guest, view, out var second, item => item.Id != first ) );
		Assert.IsNull( guest.Level.BuildEntry, "the build tool ended at once" );
		RoundsUntil( () => tool.Kind == StaffToolKind.Place, host, guest, view, "the second hire armed the Place tool" );
		Assert.AreEqual( second, tool.Candidate!.Id );
		CollectionAssert.AreEqual( new[] { ParkCommandKind.PickUpObject, ParkCommandKind.EndMove, ParkCommandKind.BeginStaffHire, ParkCommandKind.CancelStaffHire, ParkCommandKind.BeginStaffHire },
			HostCommandsOf( host, seat ).Select( command => command.Kind ).ToArray() );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void EscapeOrARightClickDuringAFirstHireLetsGoAndTheAcceptedHireIsCancelledOnce()
	{
		foreach ( var escape in new[] { true, false } )
		{
			var (host, guest, view, seat, handyman) = StaffSession();
			var staff = GuestStaffOf( guest );
			var tool = guest.Level.StaffPlacement!;
			Assert.IsTrue( GuestHires( guest, view, out var candidate ) );
			Assert.IsTrue( staff.StartWaits );
			if ( escape )
				view.Escape();
			else
				RightClick( view );
			Assert.IsFalse( staff.WantsTool || staff.StartWaits, "let go" );
			Assert.AreNotEqual( ParkHud.PauseMenuScreen, view.Hud.Stack.Top?.Name, "the key went to the waiting hire, not to the pause menu" );
			Assert.IsTrue( staff.HasOpenRequest, "the hire still waits for its answer" );
			RoundsUntil( () => !staff.HasOpenRequest && guest.Economy.Staff.PendingHireOf( seat ) == 0 && HostStaffCommandsOf( host, seat ).Length == 2, host, guest, view,
				"the accepted hire was cancelled" );
			Assert.AreEqual( StaffToolKind.None, tool.Kind, "no tool came back" );
			CollectionAssert.AreEqual( IssuedBy( seat, new BeginStaffHireCommand( candidate ), new CancelStaffHireCommand() ), HostStaffCommandsOf( host, seat ), escape ? "Escape" : "a right click" );
			Rounds( 120, host, guest );
			Assert.AreEqual( 2, HostStaffCommandsOf( host, seat ).Length, "one cleanup" );
			CatchUpAndCompare( host, guest );
			host.Session!.End( "The test ends this session." );
		}
	}

	[TestMethod]
	public void AToolSwitchThroughTheLevelWhileAReplacementHireWaitsSendsNoSecondStaffRequest()
	{
		var (host, guest, view, seat, handyman) = StaffSession();
		var staff = GuestStaffOf( guest );
		var tool = guest.Level.StaffPlacement!;
		Assert.IsTrue( GuestHires( guest, view, out var first ) );
		RoundsUntil( () => tool.Kind == StaffToolKind.Place, host, guest, view, "the first hire armed the Place tool" );
		Assert.IsTrue( GuestHires( guest, view, out var second, item => item.Id != first ) );
		var sent = Numbered( guest );
		// the path tool through the level: the staff tool ends without a command, and its exit waits for the replacement's answer
		guest.Enter();
		Assert.IsTrue( guest.Level.BeginPathTool() );
		Assert.AreEqual( StaffToolKind.None, tool.Kind );
		Assert.IsFalse( staff.WantsTool );
		Assert.AreEqual( sent, Numbered( guest ), "no second staff request beside the open one" );
		RoundsUntil( () => !staff.HasOpenRequest && guest.Economy.Staff.PendingHireOf( seat ) == 0, host, guest, view, "the replacement was cancelled after its answer" );
		CollectionAssert.AreEqual( IssuedBy( seat, new BeginStaffHireCommand( first ), new BeginStaffHireCommand( second ), new CancelStaffHireCommand() ), HostStaffCommandsOf( host, seat ),
			"the accepted replacement's state cleaned up, once" );
		Assert.IsTrue( guest.Level.CellTool.IsActive, "the path tool stays" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsHireAsksTheRightsFirstThenTheParksOwnChecks()
	{
		var (host, guest, view, seat, handyman) = StaffSession( CoopRights.Build );
		var sent = Numbered( guest );
		// without the staff right: the rights' note, nothing sent, and the hire list's refusal says nothing more
		Assert.IsFalse( GuestHires( guest, view, out _ ) );
		view.Hud.RefuseHire();
		Assert.AreEqual( sent, Numbered( guest ) );
		Assert.AreEqual( 0, view.Says( OpenTpwText.NotAvailable ), "the hire's own refusal is not said" );
		view.Hud.CloseStaffWindow();
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotPermitted ), "the rights' note" );
		// with it: an applicant another seat places is refused here, before anything is sent
		GiveRights( host, guest, view, CoopRights.Staff );
		var reserved = host.Economy.Staff.Candidates.First( item => item.Type == StaffType.Guard );
		host.Enter();
		Assert.IsTrue( host.Level.StaffPlacement!.BeginHire( reserved.Id ) );
		RoundsUntil( () => guest.Economy.Staff.ReservedBy( reserved.Id ) == ParkCommands.LocalPlayer, host, guest, view, "the host's hire reached the guest" );
		sent = Numbered( guest );
		Assert.IsFalse( GuestHires( guest, view, out _, item => item.Id == reserved.Id ) );
		view.Hud.RefuseHire();
		Assert.AreEqual( sent, Numbered( guest ) );
		Assert.AreEqual( 1, view.Says( OpenTpwText.NotAvailable ), "the hire list's refusal" );
		Assert.IsFalse( GuestStaffOf( guest ).WantsTool );
		host.Enter();
		host.Level.StaffPlacement.Cancel();
		CatchUpAndCompare( host, guest );
		Assert.AreEqual( 0, HostStaffCommandsOf( host, seat ).Length );
	}

	[TestMethod]
	public void ARefusedPlacementKeepsTheToolForABadCellAndEndsItWithOneCancelForAnApplicantNoLongerHireable()
	{
		var (host, guest, view, seat, handyman) = StaffSession();
		var staff = GuestStaffOf( guest );
		var tool = guest.Level.StaffPlacement!;
		Assert.IsTrue( GuestHires( guest, view, out var candidate ) );
		RoundsUntil( () => tool.Kind == StaffToolKind.Place, host, guest, view, "the hire armed the Place tool" );
		// a placement the host refuses for its cell (made by hand: the tool's own click tests the cell first): the hire stands, and so does the tool
		guest.Enter();
		var bad = Enumerable.Range( 0, 40 ).Select( x => (X: x, Y: 0) ).First( cell => !tool.IsValid( cell.X, cell.Y ) );
		staff.Adopt( guest.Level.Commands.Issue( new PlaceStaffCommand( candidate, bad.X, bad.Y ) ).Request!, GuestStaffRequestKind.Click );
		RoundsUntil( () => !staff.HasOpenRequest, host, guest, view, "the bad cell came back" );
		Assert.AreEqual( (StaffToolKind.Place, candidate, true), (tool.Kind, guest.Economy.Staff.PendingHireOf( seat ), staff.WantsTool), "a bad cell keeps the tool" );
		// a placement refused because the park can no longer hire the applicant (its role full): the tool ends, and one cancel goes
		var cell = FreeCell( guest );
		ClickCell( guest, view, cell.X, cell.Y );
		// the guards' role is full from now on, alike on every park: the placement's record refuses the applicant
		var settings = new[] { host, guest }.Select( park => (StaffRoleSettings[])park.Economy.Settings.Roles ).Distinct().ToArray();
		var saved = settings.Select( roles => roles[(int)StaffType.Guard] ).ToArray();
		try
		{
			foreach ( var roles in settings )
				roles[(int)StaffType.Guard] = roles[(int)StaffType.Guard] with { MaximumInPark = 0 };
			RoundsUntil( () => !staff.HasOpenRequest && guest.Economy.Staff.PendingHireOf( seat ) == 0, host, guest, view, "the refused placement ended the hire" );
		}
		finally
		{
			for ( var index = 0; index < settings.Length; index++ )
				settings[index][(int)StaffType.Guard] = saved[index];
		}
		Assert.AreEqual( (StaffToolKind.None, false), (tool.Kind, staff.WantsTool) );
		CollectionAssert.AreEqual( IssuedBy( seat, new BeginStaffHireCommand( candidate ), new PlaceStaffCommand( candidate, bad.X, bad.Y ), new PlaceStaffCommand( candidate, cell.X, cell.Y ),
			new CancelStaffHireCommand() ), HostStaffCommandsOf( host, seat ), "one cancel" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheGuestsToolFollowsARightTakenBackInAPausedPark()
	{
		var (host, guest, view, seat, handyman) = StaffSession();
		var staff = GuestStaffOf( guest );
		var tool = guest.Level.StaffPlacement!;
		var member = handyman;
		Assert.IsTrue( host.Issue( new SetGameSpeedCommand( GameSpeed.Paused ) ).Accepted );
		Assert.IsTrue( GuestPicksUp( guest, view, member ) );
		RoundsUntil( () => tool.Kind == StaffToolKind.Carry, host, guest, view, "the pick-up armed the Carry tool" );
		var origin = tool.Origin!.Value;
		var ticks = guest.Level.TickCount;
		Assert.IsTrue( host.Issue( new SetSeatRightsCommand( seat, CoopRights.None ) ).Accepted );
		RoundsUntil( () => tool.Kind == StaffToolKind.None, host, guest, view, "the right taken back turned the tool off" );
		Assert.AreEqual( ticks, guest.Level.TickCount, "in a paused park" );
		Assert.IsNull( guest.Level.StaffHands!.Of( seat ) );
		Assert.AreEqual( origin, StaffPlacementRules.CellOf( MemberOf( guest, member ) ), "dropped back where it was picked up" );
		Assert.IsFalse( staff.WantsTool );
		Rounds( 120, host, guest );
		CollectionAssert.AreEqual( IssuedBy( seat, new PickUpStaffCommand( member ) ), HostStaffCommandsOf( host, seat ), "the release sent nothing of the guest's" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheSessionsEndTurnsAnArmedToolOffAndCountsAWaitingStaffRequest()
	{
		foreach ( var carry in new[] { false, true } )
		{
			var (host, guest, view, seat, handyman) = StaffSession();
			var tool = guest.Level.StaffPlacement!;
			Assert.IsTrue( host.Issue( new SetGameSpeedCommand( GameSpeed.Paused ) ).Accepted );
			if ( carry )
				Assert.IsTrue( GuestPicksUp( guest, view, handyman ) );
			else
				Assert.IsTrue( GuestHires( guest, view, out _ ) );
			RoundsUntil( () => tool.IsActive, host, guest, view, "the tool armed" );
			// a click whose answer never comes: the session ends with it waiting
			guest.Pump = () => { };
			var cell = FreeCell( guest, tool.Origin );
			ClickCell( guest, view, cell.X, cell.Y );
			Assert.IsTrue( GuestStaffOf( guest ).HasOpenRequest );
			guest.Enter();
			guest.Session!.End( "The guest left." );
			Assert.AreEqual( StaffToolKind.None, tool.Kind, carry ? "the Carry tool is off" : "the Place tool is off" );
			Assert.IsTrue( guest.Session.ManagementUnfinished, "the end line counts the staff request" );
			Assert.IsNull( guest.Level.StaffHands!.Of( seat ), "the guest seat's staff state is cleared" );
			Assert.AreEqual( 0, guest.Economy.Staff.PendingHireOf( seat ) );
			view.Update();
			Assert.AreEqual( StaffToolKind.None, tool.Kind, "and stays off" );
		}
	}

	[TestMethod]
	public void AGuestsStaffWindowsGoThroughTheManagementQueueAndDrawWhatItAskedFor()
	{
		var (host, guest, view, seat, handyman) = StaffSession( CoopRights.Build );
		var member = MemberOf( guest, handyman );
		var hud = view.Hud;
		guest.Enter();
		// without the staff right: the patrol toggle and the dismissal say the rights' note and send nothing, and the window stays
		var sent = Numbered( guest );
		Assert.IsTrue( hud.OpenEmployeeWindow( member ) );
		Assert.IsFalse( hud.PressEmployeePatrol() );
		hud.ConfirmDismissEmployee();
		Assert.IsTrue( hud.EmployeeWindowOpen, "the window stays" );
		hud.CloseEmployeeWindow();
		view.Update();
		Assert.AreEqual( (sent, 1), (Numbered( guest ), view.Says( OpenTpwText.CoopNotPermitted )), "the rights first, said once" );
		GiveRights( host, guest, view, CoopRights.Staff );
		guest.Enter();
		// the rename: the dialog closes at once, and the title draws the asked-for name until it comes back
		Assert.IsTrue( hud.OpenEmployeeWindow( member ) );
		hud.OpenEmployeeRenameDialog();
		((UiTextField)hud.Stack.Top!.Find( "name" )!).Text = "Bob";
		((UiButton)hud.Stack.Top.Find( "apply" )!).Activate();
		Assert.AreEqual( "employee", hud.Stack.Top!.Name, "the dialog closed at once" );
		var title = (UiLabel)hud.Stack.Top.Find( "title" )!;
		Assert.AreEqual( "Bob", title.Text!() );
		Assert.AreNotEqual( "Bob", member.Name, "not applied here yet" );
		// the patrol area set by the tool's two corners: the tool ends at once, and the toggle draws it set
		Assert.IsTrue( hud.PressEmployeePatrol() );
		var patrol = guest.Level.PatrolTool!;
		Assert.IsTrue( patrol.IsActive && !hud.EmployeeWindowOpen );
		Assert.IsTrue( patrol.Click( 20, 12 ) && patrol.Click( 24, 15 ) );
		Assert.IsFalse( patrol.IsActive, "the tool ended at once" );
		Assert.IsTrue( hud.OpenEmployeeWindow( member ) );
		title = (UiLabel)hud.Stack.Top!.Find( "title" )!;
		var toggle = (UiButton)hud.Stack.Top.Find( "patrol" )!;
		Assert.IsTrue( toggle.Selected!(), "drawn set" );
		Assert.IsFalse( member.HasPatrolArea, "not applied here yet" );
		RoundsUntil( () => member.Name == "Bob" && member.HasPatrolArea, host, guest, view, "the rename and the area came back" );
		Assert.AreEqual( ("Bob", true), (title.Text!(), toggle.Selected!()) );
		// the toggle clears it, drawn at once
		Assert.IsTrue( hud.PressEmployeePatrol() );
		Assert.IsFalse( toggle.Selected!(), "drawn cleared" );
		RoundsUntil( () => !member.HasPatrolArea, host, guest, view, "the clear came back" );
		hud.CloseEmployeeWindow();
		// two steps of a training budget: drawn from the latest, sent once with it
		var budget = guest.Economy.Staff.GetTrainingBudget( StaffType.Handyman );
		hud.OpenStaffCostsWindow();
		var value = (UiLabel)hud.Stack.Top!.Find( $"budget{StaffType.Handyman}Value" )!;
		var shown = value.Text!();
		var increase = (UiButton)hud.Stack.Top.Find( $"budget{StaffType.Handyman}Increase" )!;
		increase.Activate();
		increase.Activate();
		var asked = value.Text!();
		Assert.AreNotEqual( shown, asked, "drawn as asked" );
		var wanted = budget + 2 * ParkHud.TrainingBudgetStep;
		RoundsUntil( () => guest.Economy.Staff.GetTrainingBudget( StaffType.Handyman ) == wanted, host, guest, view, "the budget came back" );
		Assert.AreEqual( asked, value.Text!(), "the park's value is the asked-for one" );
		CollectionAssert.AreEqual( IssuedBy( seat, new RenameStaffCommand( member.Id, "Bob" ), new SetPatrolAreaCommand( member.Id, 20, 12, 24, 15 ),
			new SetPatrolAreaCommand( member.Id, 0, 0, 0, 0, Clear: true ), new SetTrainingBudgetCommand( StaffType.Handyman, wanted ) ),
			HostCommandsOf( host, seat ).Where( command => command is RenameStaffCommand or SetPatrolAreaCommand or SetTrainingBudgetCommand ).ToArray(), "each once, the budget with its latest value" );
		hud.Stack.Pop();
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsDismissalConfirmationWaitsForTheHostAndSaysWhyItWasRefused()
	{
		var options = GameOptions.Current;
		var (host, guest, view, seat, handyman) = StaffSession();
		var hud = view.Hud;
		var member = MemberOf( guest, handyman );
		try
		{
			GameOptions.Current = new GameOptions { Confirmations = true };
			// the host carries the employee: the guest's dismissal is refused as being moved
			host.Enter();
			Assert.IsTrue( host.Level.StaffPlacement!.PickUp( MemberOf( host, handyman ) ) );
			RoundsUntil( () => guest.Level.StaffHands!.Of( ParkCommands.LocalPlayer ) != null, host, guest, view, "the host's pick-up reached the guest" );
			guest.Enter();
			Assert.IsTrue( hud.OpenEmployeeWindow( member ) );
			hud.ConfirmDismissEmployee();
			Assert.IsFalse( hud.EmployeeWindowOpen, "the window closed before the box" );
			Assert.AreEqual( "confirmDismissStaff", hud.Stack.Top!.Name );
			((UiButton)hud.Stack.Top.Elements.OfType<UiButton>().First()).Activate();
			Assert.AreEqual( "confirmDismissStaff", hud.Stack.Top!.Name, "the box waits" );
			((UiButton)hud.Stack.Top.Elements.OfType<UiButton>().Last()).Activate();
			Assert.AreEqual( "confirmDismissStaff", hud.Stack.Top!.Name, "its buttons are inert while it waits" );
			RoundsUntil( () => hud.Stack.Top?.Name != "confirmDismissStaff", host, guest, view, "the dismissal came back" );
			view.Update();
			Assert.AreEqual( 1, view.Says( OpenTpwText.CoopStaffBeingMoved ), "being moved" );
			Assert.IsTrue( guest.Economy.Staff.Members.Contains( member ) );
			host.Enter();
			host.Level.StaffPlacement.Cancel();
			// the employee put down: a second dismissal is accepted, the employee leaves every roster
			Rounds( 30, host, guest );
			guest.Enter();
			Assert.IsTrue( hud.OpenEmployeeWindow( member ) );
			hud.ConfirmDismissEmployee();
			((UiButton)hud.Stack.Top!.Elements.OfType<UiButton>().First()).Activate();
			RoundsUntil( () => hud.Stack.Top?.Name != "confirmDismissStaff", host, guest, view, "the second dismissal came back" );
			Assert.IsFalse( guest.Economy.Staff.Members.Any( item => item.Id == handyman ) || host.Economy.Staff.Members.Any( item => item.Id == handyman ), "dismissed on every peer" );
			Assert.IsTrue( HostCommandsOf( host, seat ).OfType<FireStaffCommand>().Count() == 2 );
			CatchUpAndCompare( host, guest );
		}
		finally
		{
			GameOptions.Current = options;
		}
	}

	[TestMethod]
	public void TheHostsDismissalOfAnEmployeeAGuestCarriesSaysItIsBeingMoved()
	{
		var options = GameOptions.Current;
		var (host, guest, view, seat, handyman) = StaffSession();
		try
		{
			GameOptions.Current = new GameOptions { Confirmations = false };
			Assert.IsTrue( GuestPicksUp( guest, view, handyman ) );
			RoundsUntil( () => host.Level.StaffHands!.Of( seat ) != null, host, guest, view, "the guest's pick-up reached the host" );
			var hostView = ViewOf( host );
			WithoutStaffNames( hostView.Hud );
			host.Enter();
			Assert.IsTrue( hostView.Hud.OpenEmployeeWindow( MemberOf( host, handyman ) ) );
			hostView.Hud.ConfirmDismissEmployee();
			Assert.IsTrue( hostView.Hud.EmployeeWindowOpen, "refused: the window stays" );
			Assert.AreEqual( 1, hostView.Says( OpenTpwText.CoopStaffBeingMoved ) );
			Assert.IsTrue( host.Economy.Staff.Members.Any( item => item.Id == handyman ), "still employed" );
			CatchUpAndCompare( host, guest );
		}
		finally
		{
			GameOptions.Current = options;
		}
	}
}
