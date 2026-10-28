using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>Which locator panel is out (tp.exe 0x7cc2f0: 1 staff, 2 visitors).</summary>
public enum LocatorMode { None, Staff, Visitor }

/// <summary>
/// The locator panels' state machine (tp.exe 0x4b3f60, 0x4b4280 and the panel procedure 0x4b3790): the chosen role (0x7cc30c: 1 handymen, 2 mechanics,
/// 3 guards, 4 entertainers, 5 researchers, 6 visitors), the located actor (0x7cc308) and whether the camera follows it (camera flag 0x1000).
/// Presentation only: it reads the simulation and changes nothing in it.
/// </summary>
public sealed class LocatorModel
{
	public const int VisitorRole = 6;
	public LocatorMode Mode { get; private set; }
	/// <summary>The chosen role, 0 when no radio is chosen.</summary>
	public int Role { get; private set; }
	/// <summary>The located actor (a staff or visitor id), 0 when none.</summary>
	public int ActorId { get; private set; }
	/// <summary>Whether the strip with the actor's name and state is out.</summary>
	public bool StripShown { get; private set; }
	/// <summary>Whether the camera follows the actor.</summary>
	public bool Following { get; private set; }
	/// <summary>Whether the role's radio is chosen in its group (0x66a295); the visitor panel opens with its role set but no radio chosen.</summary>
	public bool RadioChosen { get; private set; }
	/// <summary>The actor whose name the strip last showed (the text 0x4b3490 wrote), 0 before any; the strip keeps it when a key reopens the panels without an actor.</summary>
	public int StripActorId { get; private set; }
	/// <summary>The role <see cref="StripActorId"/> was located in.</summary>
	public int StripRole { get; private set; }

	/// <summary>The role of a staff type (the radios 0x95e..0x962 in 0x4b3f60's type switch).</summary>
	// [BIN:TPW-EXE:0x004b3fe5 locator roles] thing type 5 (handyman) → role 1, 4 (mechanic) → 2, 7 (guard) → 3, 6 (entertainer) → 4, 8 (researcher) → 5, 1 (visitor) → 6
	public static int RoleOf( StaffType type ) => type switch
	{
		StaffType.Handyman => 1,
		StaffType.Mechanic => 2,
		StaffType.Guard => 3,
		StaffType.Entertainer => 4,
		_ => 5,
	};

	public static StaffType StaffTypeOf( int role ) => role switch
	{
		1 => StaffType.Handyman,
		2 => StaffType.Mechanic,
		3 => StaffType.Guard,
		4 => StaffType.Entertainer,
		_ => StaffType.Researcher,
	};

	/// <summary>Ctrl+S, or Ctrl+V with a visitor (0x4b3f60): the panel of the actor's kind, its role chosen and the camera on it; without an actor the staff panel with no role.</summary>
	public void Open( LocatorMode mode, int role, int actorId )
	{
		// [BIN:TPW-EXE:0x004b3f60 locator open] an actor sets the mode from its type, chooses its radio, shows the strip and sets the follow flag; without one the actor (0x7cc308)
		// and role (0x7cc30c) go to 0 and no radio is chosen, while the strip window 0x7cc304 (with its last text) and the follow flag 0x1000 stay as they were
		Mode = mode;
		if ( actorId == 0 )
		{
			Role = 0;
			ActorId = 0;
			RadioChosen = false;
			return;
		}
		Role = role;
		ActorId = actorId;
		RadioChosen = true;
		StripShown = true;
		Following = true;
		WriteStrip();
	}

	// the strip's text and icons (0x4b3490) are written only when an actor is located
	private void WriteStrip()
	{
		StripActorId = ActorId;
		StripRole = Role;
	}

	/// <summary>Ctrl+V without a visitor (0x4b4280): the visitors panel with the visitor role set but its radio not chosen and no actor.</summary>
	public void OpenVisitors()
	{
		// [BIN:TPW-EXE:0x004b4280 visitor locator open] the role becomes 6 and the actor 0 with no radio chosen; the strip window keeps its visibility and last name, with the
		// state icon 0x148 hidden and the thought icon 0x14a shown; the follow flag 0x1000 is not touched (0x4b3640 follows nothing without an actor)
		Mode = LocatorMode.Visitor;
		Role = VisitorRole;
		ActorId = 0;
		RadioChosen = false;
	}

	/// <summary>
	/// A radio press (message 0x101 to 0x4b3790): a new role starts from that role's first actor, the chosen role again steps to the next actor, wrapping;
	/// no actor of the role hides the strip and stops following.
	/// </summary>
	/// <param name="next">The next actor of a role after an actor (0 = from the start), 0 when the role has none after it.</param>
	public void Press( int role, Func<int, int, int> next )
	{
		ArgumentNullException.ThrowIfNull( next );
		// [BIN:TPW-EXE:0x004b3790 locator radios] no mode ignores the press; from no role the role is set and its next actor taken; else a different role drops
		// the actor first; 0x4b3340 asks 0x4824d0 for the actor after the current one and, without one, for the first; an actor shows the strip (0x65f447) and
		// sets the follow flag 0x1000 with the text and camera update 0x4b3490, no actor hides the strip and clears the flag
		if ( Mode == LocatorMode.None )
			return;
		var previous = ActorId;
		RadioChosen = true;
		if ( Role != 0 && Role != role )
			ActorId = 0;
		Role = role;
		var found = next( role, ActorId );
		ActorId = found != 0 ? found : next( role, 0 );
		if ( ActorId == 0 )
		{
			StripShown = false;
			Following = false;
			return;
		}
		if ( previous == 0 || !StripShown )
			StripShown = true;
		Following = true;
		WriteStrip();
	}

	/// <summary>The player moved the camera: it stops following (the camera code clears flag 0x1000, e.g. 0x42ae52); the strip stays.</summary>
	public void StopFollowing() => Following = false;

	/// <summary>The located actor left: the strip goes and the camera stops following.</summary>
	public void LoseActor()
	{
		ActorId = 0;
		StripActorId = 0;
		StripShown = false;
		Following = false;
	}

	/// <summary>The panel went back into the HUD (message 0x1001d8): no actor, no strip, no mode.</summary>
	public void Close()
	{
		Mode = LocatorMode.None;
		Role = 0;
		ActorId = 0;
		RadioChosen = false;
		StripShown = false;
		Following = false;
		StripActorId = 0;
		StripRole = 0;
	}
}

/// <summary>
/// The staff and visitor locator panels of the HUD arm (tp.exe table 0x756950: five role radios, and one visitor radio) and their strip (table 0x756ad8: the actor's
/// name, the staff state icon or the visitor's thought). Ctrl+S and Ctrl+V open them; a radio locates its role's next actor and the camera follows it.
/// </summary>
// [EXT:visitor-info-window] the visitor info window that Ctrl+V tests ([0x7cc418], window object 0x7cc408) is not ported, by design: a left click on an All Visitors row or a visitor picked in the park stands in for opening it and it closes with the All Visitors window, where the original's stays open on its own
// [APPROX:UI-091] staff locator radios preserve independent saved role chains (+0x210), with qualified original-malformation and legacy-save reconstruction; visitor radios use the live ThingPool chain with an explicit out-of-pool fixture fallback; visitor position failure still clears the selection; camera follow lacks the original per-frame smoothing (0x42b1c0), panel sliding remains unimplemented — evidence needed: slide geometry; camera and real-data visual comparison
public sealed partial class ParkHud
{
	private static LayoutTable LocatorTable => OriginalLayoutTables.LocatorPanels;
	private static LayoutTable LocatorStripTable => OriginalLayoutTables.LocatorStrip;
	private static readonly (int Role, string Model)[] LocatorRadios =
	{
		(1, "b_shandy"), (2, "b_smech"), (3, "b_sguard"), (4, "b_senter"), (5, "b_sresrcher"), (LocatorModel.VisitorRole, "b_skids"),
	};

	private LocatorModel? locator;
	/// <summary>The locator panels' state.</summary>
	public LocatorModel Locator => locator ??= new();
	// created on first use: some fixtures build the HUD without its constructor
	private List<UiElement>? staffLocatorElements, visitorLocatorElements, stripLocatorElements;
	private List<UiElement> locatorStaffElements => staffLocatorElements ??= new();
	private List<UiElement> locatorVisitorElements => visitorLocatorElements ??= new();
	private List<UiElement> locatorStripElements => stripLocatorElements ??= new();
	private Vector3? locatorCameraTarget;

	private void BuildLocatorPanels()
	{
		T Add<T>( T element, List<UiElement> group ) where T : UiElement
		{
			element.Anchor = UiAnchor.BottomLeft;
			element.Visible = false;
			group.Add( element );
			return hud.Add( element );
		}
		var staffRoot = LocatorTable.Roots[0];
		var visitorRoot = LocatorTable.Roots[1];
		foreach ( var (root, group) in new[] { (staffRoot, locatorStaffElements), (visitorRoot, locatorVisitorElements) } )
		{
			var plain = root.Children.Single( node => node.Model == "!f_plain" );
			Add( new UiModelImage { Id = root == staffRoot ? "locatorStaffPanel" : "locatorVisitorPanel", Model = plain.Model!, Bounds = plain.Rect }, group );
			foreach ( var node in plain.Children )
			{
				var role = LocatorRadios.Single( radio => radio.Model == node.Model ).Role;
				// [BIN:TPW-EXE:0x004b3bd1 locator group] a radio press chooses that radio and posts 0x101 to the panel, also when it is already chosen
				Add( new UiButton { Id = $"locatorRole{role}", Model = node.Model, Help = strings.Help( node.Help ), Bounds = node.Rect,
					Selected = () => Locator.RadioChosen && Locator.Role == role,
					Clicked = () => PressLocatorRole( role ) }, group );
			}
		}
		var strip = LocatorStripTable.Root;
		Add( new UiModelImage { Id = "locatorStrip", Model = strip.Model!, Help = strings.Help( strip.Help ), Bounds = strip.Rect }, locatorStripElements );
		// [BIN:TPW-EXE:0x004b3e1d locator strip] the name box 0x147 (font slot 11, white), the staff state icon 0x148 (staffstat) and the visitor thought icon 0x14a
		Add( new UiLabel { Id = "locatorName", Text = () => LocatorActorName, Font = fonts => fonts.Label, Color = UiColors.Text, Align = UiAlign.Center, Fit = true,
			Bounds = strip.Children.Single( node => node.Id == 0x147 ).Rect }, locatorStripElements );
		Add( new FrameIcon { Id = "locatorState", Model = "staffstat", Frame = () => LocatorStateFrame, Bounds = strip.Children.Single( node => node.Id == 0x148 ).Rect }, locatorStripElements );
		Add( new UiModelImage { Id = "locatorThought", Model = "ti_hungry", Frame = () => LocatorThoughtFrame ?? 0, Bounds = strip.Children.Single( node => node.Id == 0x14A ).Rect }, locatorStripElements );
	}

	/// <summary>Ctrl+S: the staff locator; with the per-employee window open it locates that window's employee, else it opens with no role chosen.</summary>
	public void OpenStaffLocator()
	{
		// [BIN:TPW-EXE:0x00481790 staff locator key] while the per-employee window is open ([0x7cc3c0], +0x10 of the window object 0x7cc3b0) the key opens 0x4b3f60 with
		// the window's employee [0x7c2658], else with no actor
		if ( Employee is { } member )
		{
			OpenLocator( LocatorMode.Staff, LocatorModel.RoleOf( member.Type ), member.Id );
			return;
		}
		OpenLocator( LocatorMode.Staff, 0, 0 );
	}

	/// <summary>Ctrl+V: the visitor locator; with the per-visitor window open it locates that visitor at once.</summary>
	public void OpenVisitorLocator()
	{
		// [BIN:TPW-EXE:0x004817c0 visitor locator key] while the visitor info window is open (its window word [0x7cc418], +0x10 of the window object 0x7cc408 that 0x4b79b0 opens)
		// the key opens 0x4b3f60 with the info windows' current thing [0x7c2658] (written by the info window base 0x48d134 and its next/previous buttons), else 0x4b4280;
		// the visitors list's row click (0x400) opens that window for the row's visitor (0x49351e), as does a press on a visitor in the park
		if ( InfoVisitor is { } guest )
		{
			OpenLocator( LocatorMode.Visitor, LocatorModel.VisitorRole, guest.Id );
			return;
		}
		CloseOpenWindowsForLocator();
		Locator.OpenVisitors();
		// [BIN:TPW-EXE:0x004b4449 visitor locator help] 0x4b4280 posts help 0x11f (287) after it set the visitor role
		host?.HelpPosted?.Invoke( AdvisorToolHelp.VisitorLocator );
	}

	private void OpenLocator( LocatorMode mode, int role, int actorId )
	{
		CloseOpenWindowsForLocator();
		Locator.Open( mode, role, actorId );
		locatorCameraTarget = null;
		// [BIN:TPW-EXE:0x004b4099 locator help] 0x4b3f60 ends by posting help 0x11f (287) through 0x486b00 when the chosen role is 6 (a visitor) and 0xc6 (198) for every other role or none, whether or not an actor was given
		host?.HelpPosted?.Invoke( Locator.Role == LocatorModel.VisitorRole ? AdvisorToolHelp.VisitorLocator : AdvisorToolHelp.StaffLocator );
		if ( Locator.Following )
			FollowLocatorActor();
	}

	// [BIN:TPW-EXE:0x004a2590 arm panel] the panel goes into the HUD arm (control 0x21: message 0xb, then 10), where the info arm and the message panel go, and 0x485b40 closes the open window
	private void CloseOpenWindowsForLocator()
	{
		while ( Stack.Screens.Count > 1 )
			Stack.Pop();
		SetInfoArm( false );
		CloseMessagePanel();
		CloseCameraPanel();
	}

	/// <summary>The HUD arm's red X (b_retract): puts the arm's panel away, the message panel or a locator panel.</summary>
	private void CloseArmPanel()
	{
		CloseMessagePanel();
		CloseCameraPanel();
		CloseLocator();
	}

	public void CloseLocator()
	{
		// [BIN:TPW-EXE:0x004b37d5 locator close] the panel's close message 0x1001d8 withdraws help 0x11f (287) and then 0xc6 (198) through 0x486b40 (0x4b381d, 0x4b382a)
		if ( Locator.Mode != LocatorMode.None )
		{
			host?.HelpWithdrawn?.Invoke( AdvisorToolHelp.VisitorLocator );
			host?.HelpWithdrawn?.Invoke( AdvisorToolHelp.StaffLocator );
		}
		Locator.Close();
		locatorCameraTarget = null;
	}

	private void PressLocatorRole( int role )
	{
		Locator.Press( role, NextLocatorActor );
		locatorCameraTarget = null;
		if ( Locator.Following )
			FollowLocatorActor();
	}

	/// <summary>The actor of <paramref name="role"/> after <paramref name="current"/> (0: the first), 0 when there is none after it.</summary>
	private int NextLocatorActor( int role, int current ) => role == LocatorModel.VisitorRole
		? LocatorOrder.NextVisitor( level.Guests?.Guests ?? Array.Empty<Guest>(), level.Guests?.Things, current )
		: (Status.EconomySource as ParkEconomy)?.Staff.NextInRole( LocatorModel.StaffTypeOf( role ), current ) ?? 0;

	private bool TryLocatorActorPosition( out Vector3 position )
	{
		position = default;
		if ( Locator.ActorId == 0 )
			return false;
		if ( Locator.Role == LocatorModel.VisitorRole )
		{
			if ( level.OriginalPark is not { } park || level.Guests?.Guests.FirstOrDefault( guest => guest.Id == Locator.ActorId ) is not { } guest
				|| !float.IsFinite( guest.X ) || !float.IsFinite( guest.Y ) )
				return false;
			var cell = GuestRenderer.CellToEngine( park.Heightfield, guest.X, guest.Y );
			if ( !float.IsFinite( cell.X ) || !float.IsFinite( cell.Y ) || !float.IsFinite( cell.Z ) )
				return false;
			position = new Vector3( cell.X, cell.Y, cell.Z );
			return true;
		}
		return level.TryGetStaffWorldPosition( Locator.ActorId, out position );
	}

	// [BIN:TPW-EXE:0x004b3656 locator follow] on every message 0x1e while flag 0x1000 is set the panel centres the camera on the actor (0x486860)
	private void FollowLocatorActor()
	{
		if ( Camera.Mode is not ParkCameraMode camera || !TryLocatorActorPosition( out var position ) )
			return;
		camera.Focus( position );
		locatorCameraTarget = camera.Target;
	}

	private void UpdateLocatorPanels()
	{
		if ( Locator.Mode != LocatorMode.None && (InfoArmOpen || OpenedMessage != null || CameraPanelOpen || IsRiding) )
			CloseLocator();
		// Staff identity survives temporarily unavailable coordinates. Keep the existing visitor position-loss policy.
		if ( Locator.ActorId != 0 && (Locator.Role == LocatorModel.VisitorRole
			? !TryLocatorActorPosition( out _ ) : !StaffMembers.Any( member => member.Id == Locator.ActorId )) )
			Locator.LoseActor();
		if ( Locator.Following )
		{
			// the player moved the camera since the last follow: it stays where he put it
			if ( Camera.Mode is ParkCameraMode camera && locatorCameraTarget is { } placed && Vector3.DistanceBetween( camera.Target, placed ) > 0.001f )
			{
				Locator.StopFollowing();
				locatorCameraTarget = null;
			}
			else
				FollowLocatorActor();
		}
		if ( hud == null )
			return;
		if ( Locator.Mode != LocatorMode.None && hud.Find( "messageClose" ) is { } close )
			close.Visible = true;
		foreach ( var element in locatorStaffElements )
			element.Visible = Locator.Mode == LocatorMode.Staff;
		foreach ( var element in locatorVisitorElements )
			element.Visible = Locator.Mode == LocatorMode.Visitor;
		foreach ( var element in locatorStripElements )
			element.Visible = Locator.Mode != LocatorMode.None && Locator.StripShown;
		// [BIN:TPW-EXE:0x004b4099 locator icons] the staff state icon shows outside the visitor role, the thought icon in it
		if ( hud.Find( "locatorState" ) is { } state )
			state.Visible &= Locator.Role != LocatorModel.VisitorRole;
		if ( hud.Find( "locatorThought" ) is { } thought )
			thought.Visible &= Locator.Role == LocatorModel.VisitorRole && LocatorThoughtFrame != null;
	}

	/// <summary>The strip's text: the staff member's name (0x506860), or "Visitor #" with the visitor's number (UITEXT 455; 456 "New Visitor" without one).</summary>
	// [BIN:TPW-EXE:0x004b3490 locator strip text] staff show their name, visitors format 0x1c8 with their number (+0x1d8), 0x1c9 when it is 0; tp.exe's UITEXT ids above 237 are OpenTPW's + 1
	private string LocatorActorName
	{
		get
		{
			var actor = Locator.StripActorId;
			if ( actor == 0 )
				return "";
			if ( Locator.StripRole == LocatorModel.VisitorRole )
			{
				var number = actor > 0 ? level.Guests?.Guests.FirstOrDefault( guest => guest.Id == actor )?.VisitorNumber ?? 0 : 0;
				return number > 0 ? strings[UIStrings.VisitorCount] + number.ToString( System.Globalization.CultureInfo.InvariantCulture ) : strings[UIStrings.NewVisitor];
			}
			return (Status.EconomySource as ParkEconomy)?.Staff.Members.FirstOrDefault( member => member.Id == actor ) is { } member ? StaffName( member ) : "";
		}
	}

	/// <summary>The staff state icon's frame: idle and patrolling 0, working 1, resting 2, striking 3, picked up 9 (0x5064f0 → 0x485ec0).</summary>
	// [BIN:TPW-EXE:0x00485ec0 staff state frame] display state 0 and 1 → 0, 2 → 1, 3 → 2, 4 → 3, any other → 9 (staffstat has frames staffstat, busy, resting, strike)
	internal static int StaffStateFrame( StaffState state ) => state switch
	{
		StaffState.Idle or StaffState.Patrolling => 0,
		StaffState.Working => 1,
		StaffState.Resting => 2,
		StaffState.OnStrike => 3,
		_ => 9,
	};

	private int LocatorStateFrame => Locator.StripRole != LocatorModel.VisitorRole
		&& (Status.EconomySource as ParkEconomy)?.Staff.Members.FirstOrDefault( member => member.Id == Locator.StripActorId ) is { } member ? StaffStateFrame( member.State ) : 0;

	/// <summary>The visitor's thought frame of the thoughts model (ti_hungry ... ti_confused): the thought less 1, null without a thought.</summary>
	// [BIN:TPW-EXE:0x004b35dd visitor thought icon] the icon 0x14a takes the visitor's thought less 1, and 0x29a (no frame) without one (medium confidence: the word read is the visitor's +0x30)
	private int? LocatorThoughtFrame => Locator.Role == LocatorModel.VisitorRole && Locator.StripRole == LocatorModel.VisitorRole
		&& level.Guests?.Guests.FirstOrDefault( guest => guest.Id == Locator.StripActorId ) is { Thought: not GuestThought.None } guest
		? (int)guest.Thought - 1 : null;
}

/// <summary>The order the locator radios step through a role's actors.</summary>
internal static class LocatorOrder
{
	/// <summary>The live visitor after <paramref name="current"/> (0: the first) in the pool's used-chain order; 0 at the tail or for a stale current.</summary>
	// [BIN:TPW-EXE:0x00482757 visitor locator order] mask 64 walks the used head (0x516130) and older links (0x516160), accepting type 1 regardless of sprite visibility;
	// a destroy request enqueues at 0x50B8FD, then changes the type to 2 at 0x50B906 before returning. The caller 0x4B3340 wraps by retrying with a null current.
	public static int NextVisitor( IEnumerable<Guest> guests, ThingPool? things, int current )
	{
		var afterCurrent = current == 0;
		foreach ( var id in VisitorIds( guests, things ) )
		{
			if ( afterCurrent )
				return id;
			afterCurrent = id == current;
		}
		return 0;
	}

	/// <summary>The shared live visitor projection for locator radios and info-window arrows; pool-free fixtures have an explicit host-only fallback.</summary>
	public static IEnumerable<int> VisitorIds( IEnumerable<Guest> guests, ThingPool? things )
	{
		// Unsupported fixtures can repeat IDs. The first roster claim wins, as in the HUD's position/name lookup;
		// retain Gone claims here so a later duplicate cannot revive them through the compatibility fallback.
		var byId = new Dictionary<int, Guest>();
		foreach ( var guest in guests )
			if ( guest.Id > 0 )
				byId.TryAdd( guest.Id, guest );
		if ( things != null )
			foreach ( var slot in things.UsedNewestFirst() )
				if ( slot.Model == SaveThingModel.Guest && !things.IsQueued( slot.Id ) && !things.IsHeld( slot.Id )
					&& byId.TryGetValue( slot.Id, out var guest ) && guest.State != GuestState.Gone )
					yield return slot.Id;
		// Host compatibility only: active, pool-free members follow valid pooled visitors in ascending ID order.
		// A used ID rejected above (wrong model, queued or placeholder) must never re-enter through this path.
		foreach ( var guest in byId.Values.Where( guest => guest.State != GuestState.Gone && !(things?.IsUsed( guest.Id ) ?? false) ).OrderBy( guest => guest.Id ) )
			yield return guest.Id;
	}
}
