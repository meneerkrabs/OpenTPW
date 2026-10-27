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
			yield return Pause( "the click's request", 0.3 );
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

	/// <summary>
	/// A resync forced with the guest seat's staff state <paramref name="state"/> held (C4c-2, the C4 plan's browser resync cases: a pending hire, then an employee in hand, in cuts of their
	/// own): the guest's own stream is forced to desync, the host's park is swapped in, and the staff tool arms again from the snapshot's state (<paramref name="armed"/>). Its evidence joins
	/// <paramref name="resyncs"/>.
	/// </summary>
	private IEnumerable<CoopLiveWait> ForcedStaffResync( string state, Func<bool> armed, JsonArray resyncs )
	{
		var stream = TheSession.GuestStream!;
		var (park, generation) = (ThePark, stream.Generation);
		stream.ForceDesync( ForcedDesyncReason );
		var report = stream.Desync ?? throw new InvalidOperationException( "Live co-op acceptance: the forced desync made no report." );
		yield return Wait( "the host's park swapped in", () => !ReferenceEquals( flow.Level, park ) && flow.Level?.CoopSession is { Phase: CoopGuestPhase.Following, GuestStream: { } now }
			&& now.Generation == generation + 1, 600 );
		yield return Wait( $"the staff tool armed again from the snapshot's {state}", armed, 30 );
		yield return Wait( "checkpoints compared after the swap", () => TheSession.GuestStream is { Stopped: false, Checks: >= 2 }, 60 );
		resyncs.Add( new JsonObject
		{
			["state"] = state,
			["generation"] = generation,
			["desync"] = new JsonObject { ["id"] = report.Id, ["kind"] = report.Kind.ToString(), ["reason"] = ForcedDesyncReason },
			["after"] = StreamEvidence(),
		} );
	}

	/// <summary>
	/// The guest's staff (C4c-2): the staff right reaches it; a guard hired from the staff window, a resync forced with its pending hire, the guard placed with a click, picked up from its
	/// window, a resync forced with it in hand, dropped with another click, renamed, the guards' training budget raised and the guard dismissed, each at its record.
	/// </summary>
	private IEnumerable<CoopLiveWait> GuestStaff()
	{
		var seat = ThePark.Commands.Player;
		yield return Wait( "the host gives the staff right", () => OtherSays( "staff" ) && ThePark.RightsOf( seat ).HasFlag( CoopRights.Staff ), 180 );
		// a resync swaps the park, its economy and its staff tool: each is read from the park open now
		ParkEconomy Economy() => ThePark.Park?.Economy ?? throw new InvalidOperationException( "Live co-op acceptance: the park has no economy." );
		StaffPlacement Tool() => ThePark.StaffPlacement ?? throw new InvalidOperationException( "Live co-op acceptance: the park has no staff tool." );
		var commands = new JsonArray();
		var resyncs = new JsonArray();
		var avoid = new List<(int X, int Y)>();
		void Record( CoopGuestCommands.Request request ) => commands.Add( StaffEvidence( request.Command, request.Record ) );

		// the hire: the staff window on the guards' applicants, Enter on the selected one; its Place tool arms when the hire comes back
		yield return Wait( "the park's base screen alone", () => TheHud.Stack.Screens.Count == 1 && InputGiven, 30 );
		TheHud.OpenStaffWindow();
		TheHud.FilterStaff( StaffType.Guard );
		TheHud.ShowEmployedStaff( false );
		Require( TheHud.StaffCandidates.Count > 0, "the staff pool offers a guard" );
		TheHud.SelectStaff( 0 );
		var candidate = TheHud.SelectedCandidateId;
		foreach ( var step in KeyThrough( UiKeys.Accept, "Enter" ) )
			yield return step;
		foreach ( var step in AtItsRecord( "the guest's hire", command => command is BeginStaffHireCommand hire && hire.Candidate == candidate, Record ) )
			yield return step;
		bool Placing() => Tool() is { Kind: StaffToolKind.Place } tool && tool.Candidate?.Id == candidate;
		yield return Wait( "the hire's Place tool", Placing, 10 );
		// [EXT:online-coop-snapshot-join] a resync with the pending hire: the new park's Place tool arms from the snapshot's
		foreach ( var step in ForcedStaffResync( "hire", Placing, resyncs ) )
			yield return step;
		// the placement with a click on a free cell
		CoopGuestCommands.Request? placement = null;
		foreach ( var step in StaffClick( avoid, request => placement = request ) )
			yield return step;
		foreach ( var step in AtItsRecord( "the guest's placement", command => command == placement!.Command, Record ) )
			yield return step;
		yield return Wait( "the Place tool ends with its employee placed", () => !Tool().IsActive && Economy().Staff.Members.Any( member => member.Id == candidate ), 10 );
		Publish( "staffPlaced", commands.DeepClone() );

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
		foreach ( var step in ForcedStaffResync( "hand", Carrying, resyncs ) )
			yield return step;
		// the drop with a click on another free cell
		CoopGuestCommands.Request? drop = null;
		foreach ( var step in StaffClick( avoid, request => drop = request ) )
			yield return step;
		foreach ( var step in AtItsRecord( "the guest's drop", command => command == drop!.Command, Record ) )
			yield return step;
		yield return Wait( "the Carry tool ends with its employee put down", () => !Tool().IsActive && ThePark.StaffHands?.Of( seat ) == null, 10 );

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
			Require( applied.Count == guest.Count && applied.Zip( guest ).All( pair => pair.First is JsonObject mine && pair.Second is JsonObject theirs
				&& mine["accepted"]?.GetValue<bool>() == true && JsonNode.DeepEquals( Without( mine, "accepted" ), theirs ) ),
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
