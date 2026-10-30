using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// Staff placement check of the native smoke test (docs/STAFF-PLACEMENT.md): a handyman is hired through the staff window, placed with a click on the
/// park view, drawn (GPU readback with and without the staff renderer around its feet), picked up through the window's pick-up button and dropped on another
/// cell, all through <see cref="OpenTPW.Hud.ParkHud.Update"/> at projected cell centres; while each tool runs the pointer also visits three nearby points and the
/// dangling person has to hang at the ground point under each one.
/// </summary>
internal sealed partial class SandboxSmokeTest
{
	private int staffStage;
	private int staffStageFrame;
	private bool staffSpriteSeen;
	private int staffDangleWait;
	private (int X, int Y) staffCellA, staffCellB;
	private System.Numerics.Vector2 staffPointA, staffPointB;
	private StaffMember? smokeHandyman;
	private byte[]? staffVisiblePixels;
	private int staffProbe, staffCandidateId, staffEmployed, staffProbeInjected, staffProbeLastFrame = -1;
	private readonly List<(float X, float Y)> staffDangleSeen = new();

	/// <summary>Three pointer positions a few pixels apart (inside one cell or across two): the dangling sprite has to follow each one, not stay at a cell centre.</summary>
	private System.Numerics.Vector2 StaffProbePoint( System.Numerics.Vector2 origin, int index ) => origin + index switch
	{
		0 => System.Numerics.Vector2.Zero,
		1 => new System.Numerics.Vector2( 14, 0 ),
		_ => new System.Numerics.Vector2( 14, -9 )
	};

	/// <summary>
	/// One probe of the sprite-follows-the-pointer check: the pointer sits at the probe point for three frames in a row; then the sprite has to hang at the ground point under it
	/// and the hire window must not cover the park. False while the probe is still waiting; true when all probes have passed.
	/// </summary>
	private bool ProbeDangle( System.Numerics.Vector2 origin, System.Numerics.Vector2 viewport, StaffRenderer renderer, string tool )
	{
		// the injected pointer lasts one frame: a frame this update skips (the ride-head readback returns early) hands the HUD the real pointer, so the count starts again
		if ( frame != staffProbeLastFrame + 1 )
			staffProbeInjected = 0;
		staffProbeLastFrame = frame;
		if ( staffProbeInjected < 3 )
		{
			injectInput?.Invoke( UiInput.Idle( StaffProbePoint( origin, staffProbe ) ) );
			staffProbeInjected++;
			return false;
		}
		var point = StaffProbePoint( origin, staffProbe );
		// the tool's sprite shows from the sprite scheduler's first pass (62 ms of game time after the tool started): wait for it, bounded
		if ( !renderer.DrewDangling && ++staffDangleWait < 600 )
			return false;
		staffDangleWait = 0;
		Require( renderer.DrewDangling && renderer.DangleAt != null, $"the {tool} tool draws the dangling sprite at probe {staffProbe}" );
		Require( renderer.DangleFrameSize is { Width: > 16, Height: > 16 }, $"the {tool} tool's dangling frame has art, not a 2 x 2 placeholder ({renderer.DangleFrameSize})" );
		var at = renderer.DangleAt!.Value;
		Require( level.TryGetGroundCell( point, viewport, out var wantX, out var wantY ), "the probe point hits the ground" );
		Require( Math.Abs( at.X - wantX ) < 0.02f && Math.Abs( at.Y - wantY ) < 0.02f, $"the {tool} sprite hangs at the pointer's ground point ({at} vs {(wantX, wantY)}), not at a cell centre" );
		// the person hangs 5 units above the pick's engine point plus the terrain's height under it (the sprite pass adds the terrain to the height the tool stores, StaffRenderer.DangleAnchor)
		var lift = StaffRenderer.DangleLift + (level.OriginalPark?.Heightfield.GetHeight( at.X, at.Y ) ?? 0f);
		Require( level.TryGetGroundHit( point, viewport, out var pickHit ) && renderer.DangleAnchorAt is { } anchor
			&& System.Numerics.Vector3.Distance( anchor, pickHit.GetSystemVector3() + System.Numerics.Vector3.UnitZ * (lift * OriginalParkPlacement.ModelScale) ) < 0.01f,
			$"the {tool} sprite's anchor is the pick's engine point lifted by (5 + the terrain's height) x ModelScale ({renderer.DangleAnchorAt} vs {pickHit}, lift {lift})" );
		Require( !hud!.StaffWindowOpen && !hud.EmployeeWindowOpen && hud.Stack.Screens.Count == 1, $"no hire or employee window covers the park while the {tool} tool runs" );
		// [EXT:debug-hud] OPENTPW_STAFF_CAPTURE=1 also saves the world with the interface for each probe (artifacts/native-smoke-staff-<tool>-<probe>.png), for the visual check of the fix
		if ( Environment.GetEnvironmentVariable( "OPENTPW_STAFF_CAPTURE" ) == "1" )
			CaptureOutputFrame( $"staff-{tool.ToLowerInvariant()}-{staffProbe}.png" );
		staffDangleSeen.Add( at );
		staffProbe++;
		staffStageFrame = frame;
		if ( staffProbe < 3 )
		{
			injectInput?.Invoke( UiInput.Idle( StaffProbePoint( origin, staffProbe ) ) );
			staffProbeInjected = 1;
			return false;
		}
		Require( staffDangleSeen.Distinct().Count() == 3, $"the {tool} sprite moved with the pointer on every probe, not snapped to one spot" );
		staffDangleSeen.Clear();
		staffProbe = 0;
		return true;
	}

	private readonly Action<UiInput>? injectInput;

	private bool StaffPhaseSettled => hud == null || hudContext == null || level.StaffPlacement == null || level.StaffRenderer == null || staffStage >= 20;

	private void UpdateStaffPhase()
	{
		if ( staffStage >= 20 || hud == null || hudContext == null || level.StaffPlacement is not { } placement || level.StaffRenderer is not { } renderer
			|| level.Park == null || level.Guests == null || (staffStage == 0 && frame < 8) )
			return;
		var economy = level.Park.Economy;
		var canvas = hudContext.Canvas;
		var viewport = new System.Numerics.Vector2( canvas.Width, canvas.Height );
		System.Numerics.Vector2 Press( System.Numerics.Vector2 point ) { hud.Update( hudContext, new UiInput( point, true, true, false, false, UiKeys.None ) ); hud.Update( hudContext, new UiInput( point, false, false, true, false, UiKeys.None ) ); return point; }
		switch ( staffStage )
		{
			case 0:
			{
				bool Clickable( (int X, int Y) cell, out System.Numerics.Vector2 point ) =>
					level.TryProjectCell( cell.X, cell.Y, viewport, out point )
					&& point.X > canvas.Width * 0.3f && point.X < canvas.Width * 0.7f && point.Y > canvas.Height * 0.2f && point.Y < canvas.Height * 0.6f
					&& !hud.Stack.Covers( canvas, point ) && level.TryGetGridCell( point, viewport, out var x, out var y ) && (x, y) == cell
					&& placement.IsValid( x, y ) && !PickFollowsAnObject( point );
				// the cursor pick tests an object's model at its current pose (ObjectRayPick): over a cell whose pick passes a model, an animated part (on the jungle the
				// water track's Splish Splash beside the found cells) can move into the ray between this search and the click and turn the hovered cell, so the tools
				// only use pointers (the cell centre and its probe points) whose pick is the terrain's whatever the animations do
				bool PickFollowsAnObject( System.Numerics.Vector2 point ) =>
					Enumerable.Range( 0, 3 ).Any( probe => level.CursorPickTestsObjectModels( StaffProbePoint( point, probe ), viewport ) );
				var grid = level.Guests.Grid;
				var found = new List<(int X, int Y)>();
				for ( var index = 0; index < grid.CountX * grid.CountY && found.Count < 400; index++ )
				{
					var cell = (X: index % grid.CountX, Y: index / grid.CountX);
					if ( Clickable( cell, out _ ) )
						found.Add( cell );
				}
				Require( found.Count > 0, "find valid on-screen cells for the staff tools" );
				staffCellA = found[found.Count / 3];
				staffCellB = found.FirstOrDefault( cell => Math.Abs( cell.X - staffCellA.X ) + Math.Abs( cell.Y - staffCellA.Y ) >= 3 );
				Require( staffCellB != default, "find a second valid cell for the drop" );
				Clickable( staffCellA, out staffPointA );
				Clickable( staffCellB, out staffPointB );
				// the hire window: the applicant is selected, hired, and the Place tool starts with nobody employed yet
				if ( !economy.Staff.CanHire( StaffType.Handyman ) )
					economy.Fire( economy.Staff.OfType( StaffType.Handyman ).First().Id );
				hud.OpenStaffWindow();
				hud.FilterStaff( StaffType.Handyman );
				hud.ShowEmployedStaff( false );
				Require( hud.StaffCandidates.Count > 0, "the staff pool offers a handyman" );
				hud.SelectStaff( 0 );
				var candidateId = staffCandidateId = hud.SelectedCandidateId;
				var employed = staffEmployed = economy.Staff.Members.Count;
				Require( hud.HireSelectedStaff() && !hud.StaffWindowOpen, "the hire closes the staff window" );
				Require( placement.Kind == StaffToolKind.Place && economy.Staff.Members.Count == employed
					&& economy.Staff.Candidates.Any( item => item.Id == candidateId ), "a hire starts the Place tool and employs nobody yet" );
				hud.Update( hudContext, UiInput.Idle( staffPointA ) );
				Require( placement.HoverCell == staffCellA && placement.HoverValid, "the Place tool follows the hovered cell" );
				// before the click: the hired handyman hangs at the pointer (probes), and nothing covers the park
				staffStage = 8;
				staffStageFrame = frame;
				staffProbe = staffProbeInjected = 0;
				break;
			}
			case 8:
			{
				if ( !ProbeDangle( staffPointA, viewport, renderer, "Place" ) )
					return;
				hud.Update( hudContext, UiInput.Idle( staffPointA ) );
				Require( placement.Kind == StaffToolKind.Place && placement.HoverCell == staffCellA && placement.HoverValid, $"the Place tool is still armed over its cell after the probes ({placement.Kind}, {placement.HoverCell} valid {placement.HoverValid}, wanted {staffCellA})" );
				Press( staffPointA );
				var candidateId = staffCandidateId;
				Require( !placement.IsActive && economy.Staff.Members.Count == staffEmployed + 1, "a click on a valid cell hires and places" );
				smokeHandyman = economy.Staff.Members.Single( member => member.Id == candidateId );
				Require( smokeHandyman.Type == StaffType.Handyman && StaffPlacementRules.CellOf( smokeHandyman ) == staffCellA && smokeHandyman.CellX == staffCellA.X + 0.5f, "the handyman stands on the cell centre" );
				staffStage = 1;
				staffStageFrame = frame;
				break;
			}
			case 1:
				// a placed person's sprite shows from the sprite scheduler's first pass (62 ms of game time after the drop), so wait for it, then two more frames for the readback
				if ( !staffSpriteSeen )
				{
					if ( renderer.DrawnStaff == 0 || !smokeHandyman!.Sprite.HasOutput )
					{
						Require( frame < staffStageFrame + 600, "draw the placed handyman's sprite (the sprite scheduler's first pass never came)" );
						return;
					}
					staffSpriteSeen = true;
					staffStageFrame = frame;
				}
				if ( frame < staffStageFrame + 2 )
					return;
				staffSpriteSeen = false;
				Require( renderer.DrawnStaff > 0, "draw the placed handyman's sprite" );
				staffVisiblePixels = CaptureFrame( "staff-placed.png" ).Pixels;
				renderer.Visible = false;
				staffStage = 2;
				staffStageFrame = frame;
				break;
			case 2:
			{
				var (hidden, width, height) = CaptureFrame( "staff-hidden.png" );
				renderer.Visible = true;
				// the sprite stands above its feet: count the changed pixels of the window around the projected cell
				var standing = StaffPlacementRules.CellOf( smokeHandyman! ) ?? staffCellA;
				level.TryProjectCell( standing.X, standing.Y, viewport, out var feet );
				var centreX = (int)(feet.X / viewport.X * width);
				var centreY = (int)(feet.Y / viewport.Y * height);
				var changed = 0;
				for ( var y = Math.Max( 0, centreY - height / 6 ); y < Math.Min( height, centreY + height / 40 ); y++ )
					for ( var x = Math.Max( 0, centreX - width / 14 ); x < Math.Min( width, centreX + width / 14 ); x++ )
					{
						var index = (y * width + x) * 4;
						if ( !hidden.AsSpan( index, 3 ).SequenceEqual( staffVisiblePixels!.AsSpan( index, 3 ) ) )
							changed++;
					}
				var minimum = Math.Max( 20, 40 * width * height / (1280 * 720) );
				Log.Trace( $"HUD staff placement: handyman {smokeHandyman.Id} placed on {staffCellA} through the staff window and a park click; {changed} sprite pixels in the readback around it (feet at {centreX}, {centreY})." );
				Require( changed > minimum, $"the handyman's sprite changes the world readback ({changed} > {minimum} pixels)" );
				staffStage = 3;
				break;
			}
			case 3:
			{
				// pick up through the per-employee window's button, opened from the employee view's row
				hud.OpenStaffWindow();
				hud.FilterStaff( StaffType.Handyman );
				hud.ShowEmployedStaff( true );
				var index = hud.StaffItems.ToList().FindIndex( member => ReferenceEquals( member, smokeHandyman ) );
				Require( index >= 0, "the employee view lists the placed handyman" );
				hud.SelectStaff( index );
				Require( hud.OpenSelectedEmployee() && hud.EmployeeWindowOpen && !hud.StaffWindowOpen && ReferenceEquals( hud.Employee, smokeHandyman ),
					"the employee's row opens the per-employee window in place of the staff window" );
				Require( hud.CanPickUpEmployee, "the pick-up button is enabled for a placed handyman" );
				Require( hud.PickUpEmployee() && !hud.EmployeeWindowOpen, "the pick-up button closes the window" );
				Require( placement.Kind == StaffToolKind.Carry && ReferenceEquals( placement.Carried, smokeHandyman ) && smokeHandyman!.State == StaffState.PickedUp, "the pick-up starts the Carry tool" );
				staffStage = 4;
				staffStageFrame = frame;
				injectInput?.Invoke( UiInput.Idle( staffPointB ) );
				break;
			}
			case 4:
				// the real HUD update of a frame runs before its draw and reads the real pointer: inject the pointer over the target cell for the next frame
				injectInput?.Invoke( UiInput.Idle( staffPointB ) );
				if ( frame < staffStageFrame + 2 )
					return;
				// the tool's sprite shows from the sprite scheduler's first pass (62 ms of game time), so wait for it, bounded
				if ( !renderer.DrewDangling && frame < staffStageFrame + 600 )
					return;
				Require( renderer.DrewDangling, "the Carry tool draws the dangling sprite" );
				Require( placement.HoverCell == staffCellB && placement.HoverValid, "the Carry tool follows the hovered cell" );
				staffStage = 9;
				staffStageFrame = frame;
				staffProbe = staffProbeInjected = 0;
				break;
			case 9:
				if ( !ProbeDangle( staffPointB, viewport, renderer, "Carry" ) )
					return;
				injectInput?.Invoke( UiInput.Idle( staffPointB ) );
				staffStage = 10;
				staffStageFrame = frame;
				break;
			case 10:
				// back over the drop cell for the click
				injectInput?.Invoke( UiInput.Idle( staffPointB ) );
				if ( frame < staffStageFrame + 3 )
					return;
				staffStage = 5;
				break;
			case 5:
				hud.Update( hudContext, UiInput.Idle( staffPointB ) );
				Require( placement.Kind == StaffToolKind.Carry && placement.HoverCell == staffCellB && placement.HoverValid, $"the Carry tool is still armed over the drop cell after the probes ({placement.Kind}, {placement.HoverCell} valid {placement.HoverValid}, wanted {staffCellB})" );
				Press( staffPointB );
				Require( !placement.IsActive && StaffPlacementRules.CellOf( smokeHandyman! ) == staffCellB && smokeHandyman!.State == StaffState.Idle && smokeHandyman.Navigation == null,
					"a click on another valid cell drops the handyman there" );
				staffStage = 6;
				staffStageFrame = frame;
				break;
			case 6:
				if ( frame < staffStageFrame + 2 )
					return;
				Require( renderer.DrawnStaff > 0 && !renderer.DrewDangling, "the dropped handyman is drawn again and the dangling sprite is gone" );
				economy.Fire( smokeHandyman!.Id );
				Log.Trace( $"HUD staff pick-up: handyman {smokeHandyman.Id} carried and dropped on {staffCellB}; dangling sprite drawn while carried." );
				staffStage = 20;
				break;
		}
	}
}
