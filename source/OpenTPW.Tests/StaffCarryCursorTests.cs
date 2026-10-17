using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>
/// The hired or picked-up staff member hangs at the pointer every frame (tp.exe 0x46c480) and the hire window does not stay in the way while a tool runs.
/// </summary>
[TestClass]
public class StaffCarryCursorTests
{
	private static StaffPlacement Placement( out ParkEconomy park )
	{
		var grid = new GuestPathGrid( 20, 20 );
		for ( var x = 0; x < 16; x++ )
			grid.SetPath( x, 5, true );
		var economy = park = EconomyTestData.Park();
		return new StaffPlacement( () => economy, () => grid.Cells, ( _, _ ) => false );
	}

	[TestMethod]
	public void TheDanglingSpriteFollowsThePointerEveryFrameNotTheHoveredCellCentre()
	{
		var placement = Placement( out var park );
		Assert.IsTrue( placement.BeginHire( park.Staff.Candidates.First( item => item.Type == StaffType.Handyman ).Id ) );
		Assert.IsNull( StaffRenderer.DanglePoint( placement ), "no pointer yet: nothing to draw" );

		var path = new (float X, float Y)[] { (3.1f, 5.2f), (3.4f, 5.3f), (3.9f, 5.8f), (7.25f, 4.75f) };
		var seen = new System.Collections.Generic.List<(float X, float Y)>();
		foreach ( var point in path )
		{
			// one HUD frame: the exact ground point and the hovered cell
			placement.SetCursor( point.X, point.Y );
			placement.Hover( (int)point.X, (int)point.Y );
			seen.Add( StaffRenderer.DanglePoint( placement )!.Value );
		}
		CollectionAssert.AreEqual( path, seen, "the sprite is at the pointer's ground point in every frame, also within one cell" );
		Assert.AreEqual( path.Length, seen.Distinct().Count(), "it moves each frame instead of snapping to a cell centre" );

		placement.SetCursor( null, null );
		Assert.AreEqual( (7.5f, 4.5f), StaffRenderer.DanglePoint( placement ), "without a ground point the hovered cell's centre is the fallback" );
	}

	[TestMethod]
	public void ACarriedMemberDanglesAtThePointerAndTheToolEndClearsIt()
	{
		var placement = Placement( out var park );
		var member = EconomyTestData.HireBest( park, StaffType.Handyman );
		(member.CellX, member.CellY) = (2.5f, 5.5f);
		Assert.IsTrue( placement.PickUp( member ) );
		placement.SetCursor( 6.3f, 5.9f );
		Assert.AreEqual( (6.3f, 5.9f), StaffRenderer.DanglePoint( placement ) );
		placement.SetCursor( 6.4f, 5.4f );
		Assert.AreEqual( (6.4f, 5.4f), StaffRenderer.DanglePoint( placement ), "the next frame moves it" );
		placement.Cancel();
		Assert.IsNull( placement.Cursor, "the dropped member is no longer drawn at the pointer" );
		placement.SetCursor( 1f, 1f );
		Assert.IsNull( placement.Cursor, "an idle placement ignores the pointer" );
	}

	[TestMethod]
	public void TheHireClosesTheStaffWindowSoNothingCoversThePark()
	{
		var economy = EconomyTestData.Park();
		var hud = StaffWindowTests.Hud( economy );
		var grid = new GuestPathGrid( 20, 20 );
		var placement = new StaffPlacement( () => economy, () => grid.Cells, ( _, _ ) => false );
		var runtime = (ParkEconomyRuntime)Activator.CreateInstance( typeof( ParkEconomyRuntime ), BindingFlags.Instance | BindingFlags.NonPublic, null, new object?[] { economy, null }, null )!;
		var levelField = typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!;
		var level = (Level)levelField.GetValue( hud )!;
		void Set( string name, object? value ) => typeof( Level ).GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, value );
		Set( "<StaffPlacement>k__BackingField", placement );
		Set( "<Park>k__BackingField", runtime );
		Set( "<LastActionMessage>k__BackingField", "" );

		var baseline = hud.Stack.Screens.Count;
		hud.OpenStaffWindow();
		Assert.IsTrue( hud.StaffWindowOpen );
		Assert.AreEqual( baseline + 1, hud.Stack.Screens.Count, "the open window is what makes the park view 'over UI'" );
		hud.FilterStaff( StaffType.Handyman );
		hud.SelectStaff( 0 );
		Assert.IsTrue( hud.HireSelectedStaff() );
		Assert.AreEqual( StaffToolKind.Place, placement.Kind );
		Assert.IsFalse( hud.StaffWindowOpen, "the hire window is closed while the candidate is in hand" );
		Assert.AreEqual( baseline, hud.Stack.Screens.Count, "no screen stays on the stack to block clicks and drops on the park" );
	}
}
