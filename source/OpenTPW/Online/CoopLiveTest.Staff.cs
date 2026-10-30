using System.Text.Json.Nodes;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-commands] The live acceptance's staff scenario (C4c-2; docs/COOP.md, "The live acceptance"): after the baseline's lines the host gives the staff right through the rights
/// dialog; the guest hires a guard from the staff window and places it with a click in the park, picks it up from its window and drops it elsewhere, renames it, sets the guards' training
/// budget and dismisses it, each at its record on both parks.
/// </summary>
internal sealed partial class CoopLiveTest
{
	/// <summary>The name the staff scenario gives its employee.</summary>
	internal const string StaffName = "Live";

	/// <summary>
	/// A cell of the park the staff tool takes, clear of <paramref name="avoid"/>, that a click reaches in the middle of the view; the view moves over it, as a player scrolls there. Null
	/// when the park has none.
	/// </summary>
	private ((int X, int Y) Cell, System.Numerics.Vector2 Point)? StaffCell( ICollection<(int X, int Y)> avoid )
	{
		var level = ThePark;
		var tool = level.StaffPlacement ?? throw new InvalidOperationException( "Live co-op acceptance: the park has no staff tool." );
		var canvas = flow.Context.Canvas;
		var viewport = new System.Numerics.Vector2( canvas.Width, canvas.Height );
		bool Clickable( (int X, int Y) cell, out System.Numerics.Vector2 point ) =>
			level.TryProjectCell( cell.X, cell.Y, viewport, out point )
			&& point.X > canvas.Width * 0.3f && point.X < canvas.Width * 0.7f && point.Y > canvas.Height * 0.2f && point.Y < canvas.Height * 0.6f
			&& !TheHud.Stack.Covers( canvas, point )
			&& level.TryGetGridCell( point, viewport, out var x, out var y ) && (x, y) == cell;
		var grid = level.Guests!.Grid;
		for ( var index = 0; index < grid.CountX * grid.CountY; index++ )
		{
			var cell = (X: index % grid.CountX, Y: index / grid.CountX);
			if ( avoid.Contains( cell ) || !tool.IsValid( cell.X, cell.Y ) )
				continue;
			if ( !Clickable( cell, out _ ) )
				Look( (cell.X + 0.5f, cell.Y + 0.5f) );
			if ( Clickable( cell, out var point ) )
				return (cell, point);
		}
		return null;
	}

	/// <summary>
	/// A click of the armed staff tool on a free cell, as a player clicks the park; a click that sent nothing (a pick that found another cell, or none) is tried again on another cell. The
	/// request it sent goes to <paramref name="sent"/>.
	/// </summary>
	private IEnumerable<CoopLiveWait> StaffClick( ICollection<(int X, int Y)> avoid, Action<CoopGuestCommands.Request> sent )
	{
		var requests = TheSession.GuestCommands!;
		for ( var attempt = 0; attempt < LineTries; attempt++ )
		{
			yield return Wait( "the park's base screen alone", () => TheHud.Stack.Screens.Count == 1 && InputGiven, 30 );
			var before = requests.LastSubmitted;
			var found = StaffCell( avoid );
			Require( found != null, "the park has a free cell a click reaches" );
			avoid.Add( found!.Value.Cell );
			yield return Pause( "the view over the cell", 0.5 );
			foreach ( var step in ClickThrough( found.Value.Point ) )
				yield return step;
			yield return Wait( "the click", () => InputGiven, 10 );
			// the request is taken in the step of the frame after the click's, before that frame's pump could apply its record (a resync forced then has it inside its cut)
			var until = Now + 0.5;
			yield return Wait( "the click's request", () => requests.LastSubmitted != before || Now >= until, 5 );
			if ( requests.LastSubmitted is { } request && request != before && request.Command is PlaceStaffCommand or DropStaffCommand )
			{
				sent( request );
				yield break;
			}
			Log.Info( $"Live co-op acceptance: the staff tool's click at {found.Value.Cell} sent nothing; trying again." );
		}
		Require( false, $"a click of the staff tool sent its request within {LineTries} tries" );
	}

	/// <summary>A staff command's evidence: its kind, the employee or applicant, its cell where it has one, and its record.</summary>
	private static JsonObject StaffEvidence( ParkCommand command, long? record ) => command switch
	{
		BeginStaffHireCommand hire => new JsonObject { ["kind"] = command.Kind.ToString(), ["staff"] = hire.Candidate, ["record"] = record },
		PlaceStaffCommand place => new JsonObject { ["kind"] = command.Kind.ToString(), ["staff"] = place.Candidate, ["cell"] = new JsonArray( place.X, place.Y ), ["record"] = record },
		PickUpStaffCommand pickUp => new JsonObject { ["kind"] = command.Kind.ToString(), ["staff"] = pickUp.Staff, ["record"] = record },
		DropStaffCommand drop => new JsonObject { ["kind"] = command.Kind.ToString(), ["staff"] = drop.Staff, ["cell"] = new JsonArray( drop.X, drop.Y ), ["record"] = record },
		RenameStaffCommand rename => new JsonObject { ["kind"] = command.Kind.ToString(), ["staff"] = rename.Staff, ["name"] = rename.Name, ["record"] = record },
		SetTrainingBudgetCommand budget => new JsonObject { ["kind"] = command.Kind.ToString(), ["role"] = budget.Role.ToString(), ["budget"] = budget.Budget, ["record"] = record },
		FireStaffCommand fire => new JsonObject { ["kind"] = command.Kind.ToString(), ["staff"] = fire.Staff, ["record"] = record },
		_ => new JsonObject { ["kind"] = command.Kind.ToString(), ["record"] = record }
	};

	/// <summary>A desync the guest forced on its own stream: the park and the stream's generation it stopped, and the report.</summary>
	private sealed record ForcedDesync( Level Park, uint Generation, CoopDesyncReport Report );

	/// <summary>Forces a desync of the guest's own stream now (<see cref="CoopGuestStream.ForceDesync"/>): the session resyncs.</summary>
	private ForcedDesync ForceStaffDesync()
	{
		var stream = TheSession.GuestStream!;
		var (park, generation) = (ThePark, stream.Generation);
		stream.ForceDesync( ForcedDesyncReason );
		return new ForcedDesync( park, generation, stream.Desync ?? throw new InvalidOperationException( "Live co-op acceptance: the forced desync made no report." ) );
	}

	/// <summary>
	/// <paramref name="input"/>'s steps (a key or a click; on a page the driver's, whose word comes frames later), watching every frame from their start for the guest's next request that
	/// <paramref name="matches"/>: in the step of the frame after the one that sent it, before that frame's pump could apply its record, it goes to <paramref name="caught"/> (which forces the
	/// desync whose cut then holds the record).
	/// </summary>
	private IEnumerable<CoopLiveWait> CatchRequest( IEnumerable<CoopLiveWait> input, Func<ParkCommand, bool> matches, Action<CoopGuestCommands.Request> caught )
	{
		var requests = TheSession.GuestCommands!;
		var before = requests.LastSubmitted;
		CoopGuestCommands.Request? found = null;
		bool Watch()
		{
			if ( found == null && requests.LastSubmitted is { } request && request != before && matches( request.Command ) )
			{
				Require( request.IsOpen, "the request was caught before its record came back" );
				found = request;
				caught( request );
			}
			return found != null;
		}
		foreach ( var step in input )
			yield return step with { Done = () => { Watch(); return step.Done(); } };
		yield return Wait( "the request sent", Watch, 10 );
	}

	/// <summary>
	/// The resync of <paramref name="forced"/>, a desync forced with the guest seat's staff state <paramref name="state"/> (C4c-2, the browser resync cases): <paramref name="beforeSwap"/>'s
	/// steps run before the host's park is swapped in (a let-go), then the staff tool follows the new park's state (<paramref name="settled"/>). Its evidence joins <paramref name="resyncs"/>:
	/// the state of the request <paramref name="inside"/> the cut, and whether a tool was armed again.
	/// </summary>
	private IEnumerable<CoopLiveWait> StaffResync( string state, ForcedDesync forced, Func<bool> settled, JsonArray resyncs, CoopGuestCommands.Request? inside = null,
		Func<IEnumerable<CoopLiveWait>>? beforeSwap = null )
	{
		if ( beforeSwap != null )
		{
			foreach ( var step in beforeSwap() )
				yield return step;
			Require( ReferenceEquals( flow.Level, forced.Park ), $"the {state}'s let-go came before the swap" );
		}
		yield return Wait( "the host's park swapped in", () => !ReferenceEquals( flow.Level, forced.Park )
			&& flow.Level?.CoopSession is { Phase: CoopGuestPhase.Following, GuestStream: { } now } && now.Generation == forced.Generation + 1, 600 );
		yield return Wait( $"the staff tool after the {state}'s cut", settled, 30 );
		yield return Wait( "checkpoints compared after the swap", () => TheSession.GuestStream is { Stopped: false, Checks: >= 2 }, 60 );
		resyncs.Add( new JsonObject
		{
			["state"] = state,
			["generation"] = forced.Generation,
			["desync"] = new JsonObject { ["id"] = forced.Report.Id, ["kind"] = forced.Report.Kind.ToString(), ["reason"] = ForcedDesyncReason },
			["request"] = inside?.State.ToString(),
			["armed"] = ThePark.StaffPlacement?.IsActive == true,
			["after"] = StreamEvidence(),
		} );
	}

	/// <summary>
	/// The guest's staff (C4c-2): the staff right reaches it; a guard hired and let go with Escape while a forced resync has its record inside the cut (the new park's pending hire cleaned
	/// up, no tool); another hired and placed with a click, picked up from its window with a resync forced while it is in hand (the Carry tool armed again from the snapshot); once the early
	/// window after that join passed, dropped with another click whose record a third forced resync's cut holds (no state, no tool); renamed, the guards' training budget raised and the guard
	/// dismissed. Every staff command is accepted at the same record on both parks, the two inside a cut at their records on the host.
	/// </summary>
	private IEnumerable<CoopLiveWait> GuestStaff()
	{
		var seat = ThePark.Commands.Player;
		yield return Wait( "the host gives the staff right", () => OtherSays( "staff" ) && ThePark.RightsOf( seat ).HasFlag( CoopRights.Staff ), 180 );
		// a resync swaps the park, its economy and its staff tool: each is read from the park open now
		ParkEconomy Economy() => ThePark.Park?.Economy ?? throw new InvalidOperationException( "Live co-op acceptance: the park has no economy." );
		StaffPlacement Tool() => ThePark.StaffPlacement ?? throw new InvalidOperationException( "Live co-op acceptance: the park has no staff tool." );
		var requests = TheSession.GuestCommands!;
		var staff = TheSession.GuestStaff!;
		var commands = new JsonArray();
		var resyncs = new JsonArray();
		var avoid = new List<(int X, int Y)>();
		void Record( CoopGuestCommands.Request request ) => commands.Add( StaffEvidence( request.Command, request.Record ) );
		bool Settled() => !staff.HasOpenRequest && !ThePark.HoldsStaff( seat );

		// the staff window on the guards' applicants, one selected: Enter hires it
		IEnumerable<CoopLiveWait> SelectAGuard( Func<StaffCandidate, bool> which, Action<int> chosen )
		{
			yield return Wait( "the park's base screen alone", () => TheHud.Stack.Screens.Count == 1 && InputGiven, 30 );
			TheHud.OpenStaffWindow();
			TheHud.FilterStaff( StaffType.Guard );
			TheHud.ShowEmployedStaff( false );
			var index = TheHud.StaffCandidates.ToList().FindIndex( item => which( item ) );
			Require( index >= 0, "the staff pool offers a guard" );
			TheHud.SelectStaff( index );
			chosen( TheHud.SelectedCandidateId );
		}
		var first = 0;
		foreach ( var step in SelectAGuard( _ => true, id => first = id ) )
			yield return step;
		// [EXT:online-coop-snapshot-join] the first hire's record inside a forced resync's cut, the player letting go with Escape before the swap: the new park's pending hire is cleaned up,
		// no tool
		CoopGuestCommands.Request? letGo = null;
		ForcedDesync? hireCut = null;
		foreach ( var step in CatchRequest( KeyThrough( UiKeys.Accept, "Enter" ), command => command is BeginStaffHireCommand hire && hire.Candidate == first,
			request => (letGo, hireCut) = (request, ForceStaffDesync()) ) )
			yield return step;
		commands.Add( StaffEvidence( letGo!.Command, null ) );
		IEnumerable<CoopLiveWait> Escape()
		{
			foreach ( var step in KeyThrough( UiKeys.Back, "Escape" ) )
				yield return step;
			yield return Wait( "Escape lets go of the waiting hire", () => InputGiven && !staff.WantsTool, 10 );
		}
		foreach ( var step in StaffResync( "hire", hireCut!, () => Settled() && !Tool().IsActive, resyncs, letGo, Escape ) )
			yield return step;
		Require( requests.LastSubmitted is { Command: CancelStaffHireCommand, State: CoopGuestRequestState.Applied } cancel && cancel.Accepted,
			"the let-go hire's cleanup was its cancel, applied" );
		Record( requests.LastSubmitted! );

		// the second hire, at its record, and its placement with a click on a free cell
		var candidate = 0;
		foreach ( var step in SelectAGuard( item => item.Id != first, id => candidate = id ) )
			yield return step;
		foreach ( var step in KeyThrough( UiKeys.Accept, "Enter" ) )
			yield return step;
		foreach ( var step in AtItsRecord( "the guest's hire", command => command is BeginStaffHireCommand hire && hire.Candidate == candidate, Record ) )
			yield return step;
		yield return Wait( "the hire's Place tool", () => Tool() is { Kind: StaffToolKind.Place } tool && tool.Candidate?.Id == candidate, 10 );
		CoopGuestCommands.Request? placement = null;
		foreach ( var step in StaffClick( avoid, request => placement = request ) )
			yield return step;
		foreach ( var step in AtItsRecord( "the guest's placement", command => command == placement!.Command, Record ) )
			yield return step;
		yield return Wait( "the Place tool ends with its employee placed", () => !Tool().IsActive && Economy().Staff.Members.Any( member => member.Id == candidate ), 10 );

		// the pick-up from the employee's window
		StaffMember Member() => Economy().Staff.Members.Single( item => item.Id == candidate );
		yield return Wait( "the employee can be picked up", () => TheHud.Stack.Screens.Count == 1 && InputGiven && ThePark.Guests?.Grid.Cells is { } cells
			&& StaffPlacementRules.CanBePickedUp( Member(), cells ), 60 );
		Require( TheHud.OpenEmployeeWindow( Member() ), "the employee's window opened" );
		yield return Wait( "the pick-up button", () => Usable<UiButton>( "employee", "pickUpStaff" ), 10 );
		Press( "employee", "pickUpStaff" );
		foreach ( var step in AtItsRecord( "the guest's pick-up", command => command is PickUpStaffCommand pickUp && pickUp.Staff == candidate, Record ) )
			yield return step;
		bool Carrying() => Tool() is { Kind: StaffToolKind.Carry } tool && tool.Carried?.Id == candidate;
		yield return Wait( "the pick-up's Carry tool", Carrying, 10 );
		// [EXT:online-coop-snapshot-join] a resync with the employee in hand: the new park's Carry tool arms from the snapshot's hand
		foreach ( var step in StaffResync( "hand", ForceStaffDesync(), Carrying, resyncs ) )
			yield return step;
		// [EXT:online-coop-snapshot-join] the drop's record inside a third forced resync's cut: no state and no tool after it. Within the early window after a join a forced desync counts as
		// that join's failure, and a session fails at its third: the third comes after the window
		yield return Pause( "the early window after the last join", CoopSession.EarlyFailureSeconds + 5 );
		Require( Carrying(), "the Carry tool stays armed" );
		var found = StaffCell( avoid );
		Require( found != null, "the park has a free cell a click reaches" );
		yield return Pause( "the view over the cell", 0.5 );
		CoopGuestCommands.Request? drop = null;
		ForcedDesync? dropCut = null;
		foreach ( var step in CatchRequest( ClickThrough( found!.Value.Point ), command => command is DropStaffCommand put && put.Staff == candidate,
			request => (drop, dropCut) = (request, ForceStaffDesync()) ) )
			yield return step;
		commands.Add( StaffEvidence( drop!.Command, null ) );
		foreach ( var step in StaffResync( "drop", dropCut!, () => Settled() && !Tool().IsActive, resyncs, drop ) )
			yield return step;
		Require( !staff.WantsTool && Member().State != StaffState.PickedUp, "the employee put down, no tool wanted" );

		// the rename from the employee's window
		Require( TheHud.OpenEmployeeWindow( Member() ), "the employee's window opened again" );
		yield return Wait( "the employee's window", () => Top( "employee" ), 10 );
		TheHud.OpenEmployeeRenameDialog();
		yield return Wait( "the rename dialog", () => Top( "employeeRename" ), 10 );
		TypeInto( "employeeRename", "name", StaffName );
		Press( "employeeRename", "apply" );
		yield return Wait( "the dialog closes at once", () => Top( "employee" ) && LabelText( "employee", "title" ) == StaffName, 10 );
		foreach ( var step in AtItsRecord( "the guest's rename", command => command is RenameStaffCommand rename && rename.Staff == candidate && rename.Name == StaffName, Record ) )
			yield return step;
		Require( Member().Name == StaffName, "the name in the guest's park" );
		foreach ( var step in CloseWindows() )
			yield return step;

		// the guards' training budget, one step up
		var budget = ParkHud.StepTrainingBudget( Economy().Staff.GetTrainingBudget( StaffType.Guard ), 1 );
		TheHud.OpenStaffCostsWindow();
		yield return Wait( "the training budgets", () => Usable<UiButton>( "staffCosts", $"budget{StaffType.Guard}Increase" ), 10 );
		Press( "staffCosts", $"budget{StaffType.Guard}Increase" );
		foreach ( var step in AtItsRecord( "the guest's training budget", command => command is SetTrainingBudgetCommand set && set.Role == StaffType.Guard && set.Budget == budget, Record ) )
			yield return step;
		Require( Economy().Staff.GetTrainingBudget( StaffType.Guard ) == budget, "the budget in the guest's park" );
		foreach ( var step in CloseWindows() )
			yield return step;

		// the dismissal from the employee's window, through its confirmation when the options ask for one
		Require( TheHud.OpenEmployeeWindow( Member() ), "the employee's window opened a third time" );
		yield return Wait( "the dismiss button", () => Usable<UiButton>( "employee", "dismissStaff" ), 10 );
		Press( "employee", "dismissStaff" );
		if ( GameOptions.Current.Confirmations )
		{
			yield return Wait( "the dismissal's confirmation", () => Top( "confirmDismissStaff" ), 10 );
			Press( "confirmDismissStaff", "choice0" );
		}
		foreach ( var step in AtItsRecord( "the guest's dismissal", command => command is FireStaffCommand fire && fire.Staff == candidate, Record ) )
			yield return step;
		yield return Wait( "the employee gone", () => Economy().Staff.Members.All( item => item.Id != candidate ), 10 );
		foreach ( var step in CloseWindows() )
			yield return step;
		Keep( "staff", new JsonObject { ["seat"] = seat, ["commands"] = commands.DeepClone(), ["resyncs"] = resyncs.DeepClone() } );
		Publish( "staffDone", new JsonObject { ["commands"] = commands.DeepClone(), ["resyncs"] = resyncs.DeepClone() } );
		yield return Wait( "the host saw the guest's staff", () => OtherSays( "sawStaff" ), 180 );
	}

	/// <summary>
	/// The host's side of the staff scenario (C4c-2): the staff right through the rights dialog; every staff command of the guest's applied in this park, accepted, at the record the guest's
	/// park applied it at, the guest's employee dismissed here too; and the guest's two forced resyncs: exactly their two desync reports and a capture for each.
	/// </summary>
	private IEnumerable<CoopLiveWait> HostStaff( string guestName, int seat )
	{
		var level = ThePark;
		var hostStream = TheSession.HostStream!;
		var applied = new JsonArray();
		void OnApplied( ParkCommandLogEntry entry, ParkCommandResult result )
		{
			if ( entry.Command.Player != seat || entry.Command is not (StaffToolCommand or RenameStaffCommand or SetTrainingBudgetCommand or FireStaffCommand) )
				return;
			var evidence = StaffEvidence( entry.Command, hostStream.LastRecord );
			evidence["accepted"] = result.Accepted;
			applied.Add( evidence );
		}
		var desyncs = new JsonArray();
		void OnDesynced( uint guest, CoopDesyncReport report ) =>
			desyncs.Add( new JsonObject { ["guest"] = guest, ["id"] = report.Id, ["kind"] = report.Kind.ToString(), ["tick"] = report.Tick } );
		level.Commands.Applied += OnApplied;
		hostStream.Desynced += OnDesynced;
		try
		{
			// [EXT:online-coop-permissions] a rights change through the dialog: the staff right
			foreach ( var step in GiveTheGuestARight( guestName, seat, CoopRights.Staff, "staffGrant" ) )
				yield return step;
			var captures = hostStream.CapturesTaken;
			Publish( "staff", true );
			yield return Wait( "the guest's staff", () => Other( "staffDone" ) is JsonObject, 900 );
			var done = (JsonObject)Other( "staffDone" )!;
			var guest = (JsonArray)done["commands"]!;
			yield return Wait( "the guest's staff in the host's park", () => applied.Count >= guest.Count, 60 );
			// a command whose record the guest's park took inside a cut has no record known there: the rest of it is compared
			bool Same( JsonObject mine, JsonObject theirs ) => theirs["record"] is null
				? JsonNode.DeepEquals( Without( Without( mine, "accepted" ), "record" ), Without( theirs, "record" ) )
				: JsonNode.DeepEquals( Without( mine, "accepted" ), theirs );
			Require( applied.Count == guest.Count && applied.Zip( guest ).All( pair => pair.First is JsonObject mine && pair.Second is JsonObject theirs
				&& mine["accepted"]?.GetValue<bool>() == true && Same( mine, theirs ) ),
				$"every staff command of the guest's applied here, accepted, at its record ({applied.ToJsonString()} against {guest.ToJsonString()})" );
			var hired = guest.OfType<JsonObject>().First( item => item["kind"]?.GetValue<string>() == nameof( ParkCommandKind.PlaceStaff ) )["staff"]!.GetValue<int>();
			var economy = level.Park!.Economy;
			Require( economy.Staff.Members.All( member => member.Id != hired ), "the guest's employee is dismissed in the host's park" );
			// the guest's two forced resyncs: their desync reports alone, and a capture for each
			var forced = ((JsonArray)done["resyncs"]!).OfType<JsonObject>().Select( resync => resync["desync"]!["id"]!.GetValue<uint>() ).ToArray();
			Require( desyncs.Count == forced.Length && desyncs.OfType<JsonObject>().Select( report => report["id"]!.GetValue<uint>() ).SequenceEqual( forced )
				&& desyncs.OfType<JsonObject>().All( report => report["kind"]!.GetValue<string>() == nameof( CoopDesyncKind.Protocol ) ),
				$"the host saw exactly the guest's forced desync reports ({desyncs.ToJsonString()})" );
			Require( hostStream.CapturesTaken == captures + forced.Length, "a new capture for each" );
			Keep( "staff", new JsonObject
			{
				["seat"] = seat,
				["commands"] = applied.DeepClone(),
				["trainingBudget"] = economy.Staff.GetTrainingBudget( StaffType.Guard ),
				["forced"] = new JsonObject { ["desyncs"] = desyncs.DeepClone(), ["capturesBefore"] = captures, ["capturesAfter"] = hostStream.CapturesTaken },
			} );
			Publish( "sawStaff", true );
		}
		finally
		{
			level.Commands.Applied -= OnApplied;
			hostStream.Desynced -= OnDesynced;
		}
	}

	/// <summary>A copy of <paramref name="evidence"/> without its field <paramref name="name"/>.</summary>
	private static JsonObject Without( JsonObject evidence, string name )
	{
		var copy = (JsonObject)evidence.DeepClone();
		copy.Remove( name );
		return copy;
	}
}
