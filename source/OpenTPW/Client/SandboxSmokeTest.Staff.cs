using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// Staff placement check of the native smoke test (docs/STAFF-PLACEMENT.md): a handyman is hired through the staff window, placed with a click on the
/// park view, drawn (GPU readback with and without the staff renderer around its feet), picked up through the window's pick-up button and dropped on another
/// cell, all through <see cref="OpenTPW.Hud.ParkHud.Update"/> at projected cell centres.
/// </summary>
internal sealed partial class SandboxSmokeTest
{
	private int staffStage;
	private int staffStageFrame;
	private (int X, int Y) staffCellA, staffCellB;
	private System.Numerics.Vector2 staffPointA, staffPointB;
	private StaffMember? smokeHandyman;
	private byte[]? staffVisiblePixels;

	private readonly Action<UiInput>? injectInput;

	private bool StaffPhaseSettled => hud == null || hudContext == null || level.StaffPlacement == null || level.StaffRenderer == null || staffStage >= 7;

	private void UpdateStaffPhase()
	{
		if ( staffStage >= 7 || hud == null || hudContext == null || level.StaffPlacement is not { } placement || level.StaffRenderer is not { } renderer
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
					&& placement.IsValid( x, y );
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
				var candidateId = hud.SelectedCandidateId;
				var employed = economy.Staff.Members.Count;
				Require( hud.HireSelectedStaff() && !hud.StaffWindowOpen, "the hire closes the staff window" );
				Require( placement.Kind == StaffToolKind.Place && economy.Staff.Members.Count == employed
					&& economy.Staff.Candidates.Any( item => item.Id == candidateId ), "a hire starts the Place tool and employs nobody yet" );
				hud.Update( hudContext, UiInput.Idle( staffPointA ) );
				Require( placement.HoverCell == staffCellA && placement.HoverValid, "the Place tool follows the hovered cell" );
				Press( staffPointA );
				Require( !placement.IsActive && economy.Staff.Members.Count == employed + 1, "a click on a valid cell hires and places" );
				smokeHandyman = economy.Staff.Members.Single( member => member.Id == candidateId );
				Require( smokeHandyman.Type == StaffType.Handyman && StaffPlacementRules.CellOf( smokeHandyman ) == staffCellA && smokeHandyman.CellX == staffCellA.X + 0.5f, "the handyman stands on the cell centre" );
				staffStage = 1;
				staffStageFrame = frame;
				break;
			}
			case 1:
				if ( frame < staffStageFrame + 2 )
					return;
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
				Require( renderer.DrewDangling, "the Carry tool draws the dangling sprite" );
				Require( placement.HoverCell == staffCellB && placement.HoverValid, "the Carry tool follows the hovered cell" );
				staffStage = 5;
				break;
			case 5:
				hud.Update( hudContext, UiInput.Idle( staffPointB ) );
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
				staffStage = 7;
				break;
		}
	}
}
