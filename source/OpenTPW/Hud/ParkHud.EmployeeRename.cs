using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	private UiScreen? employeeRenameScreen;

	private void CloseEmployeeRenameDialog()
	{
		if ( employeeRenameScreen is not { } screen ) return;
		Stack.RemoveWhere( candidate => ReferenceEquals( candidate, screen ) );
		if ( ReferenceEquals( employeeRenameScreen, screen ) ) employeeRenameScreen = null;
	}

	/// <summary>The employee title opens one child dialog, capturing the selected employee rather than a later selection.</summary>
	// [BIN:TPW-EXE:0x4b6110 staff rename] title property 0 is its owner (0x48d290), vtable +0x38 opens table 0x7572e8;
	// child properties 0/1 capture staff/owner, UITEXT 195, editor 0x5dab holds 32 UTF-16 units and initially selects the name.
	internal void OpenEmployeeRenameDialog()
	{
		if ( level.IsReadOnlyVisit || Employee is not { } member || Status.EconomySource is not ParkEconomy economy
			|| Stack.Top != employeeScreen || employeeRenameScreen != null ) return;
		var parent = employeeScreen;
		var thingId = member.ThingId;
		var screen = new ShopPriceScreen( "employeeRename" );
		var table = OriginalLayoutTables.EmployeeRenameDialog;
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = table.Root.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[UIStrings.RenameStaffMember], Font = fonts => fonts.Title,
			Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = table.Control( 0x5DAA ).Rect, Anchor = UiAnchor.Center } );
		var field = screen.Add( new UiTextField { Id = "name", Text = StaffName( member ), MaximumLength = StaffMember.MaximumNameLength,
			Model = table.Control( 0x5DAB ).Model, Bounds = table.Control( 0x5DAB ).Rect, Anchor = UiAnchor.Center } );
		bool TargetExists() => EmployeeWindowOpen && ReferenceEquals( employeeScreen, parent )
			&& ReferenceEquals( Status.EconomySource, economy ) && member.ThingId == thingId && economy.Staff.Members.Contains( member );
		void Cancel()
		{
			if ( ReferenceEquals( employeeRenameScreen, screen ) ) CloseEmployeeRenameDialog();
		}
		void Apply()
		{
			// Host safety: a stale callback, replaced roster or read-only visit cannot write through the captured target.
			if ( Stack.Top != screen || !ReferenceEquals( employeeRenameScreen, screen ) ) return;
			if ( level.IsReadOnlyVisit || !TargetExists() ) { Cancel(); return; }
			if ( !EditsOf( economy ).Issue( new RenameStaffCommand( member.Id, field.Text ) ).Accepted ) return;
			RefreshEmployeeStats();
			Cancel();
		}
		field.Submitted = Apply;
		foreach ( var (id, control, action) in new (string, uint, Action)[] { ("apply", uint.MaxValue, Apply), ("cancel", uint.MaxValue - 1, Cancel) } )
		{
			var node = table.Control( control );
			screen.Add( new UiButton { Id = id, Model = node.Model, Bounds = node.Rect, Anchor = UiAnchor.Center, Clicked = action } );
		}
		screen.Back = Cancel;
		screen.Updating = _ =>
		{
			if ( TargetExists() ) return;
			Cancel();
			if ( Employee == null ) CloseEmployeeWindow();
		};
		// [BIN:TPW-EXE:0x4b635c child destroy] clear the owner's child slot; the parent refresh/help resumes.
		screen.Removed = () =>
		{
			if ( !ReferenceEquals( employeeRenameScreen, screen ) ) return;
			employeeRenameScreen = null;
			if ( Employee is { } selected )
			{
				parent?.Focus( parent.Find( "title" ) );
				PostPatrolAreaHelp( selected );
			}
		};
		employeeRenameScreen = screen;
		Stack.Push( screen );
		screen.Focus( field );
		field.SelectAll();
	}
}
