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

	private static SpriteBankFile Bank( byte blue, params (int Width, int Height)[] sizes )
	{
		var palette = new byte[SpriteBankFile.PaletteEntries * 4];
		(palette[0], palette[1], palette[2], palette[3]) = (blue, 0, (byte)(255 - blue), 255);
		var frames = sizes.Select( size => new SpriteFrame( size.Width, size.Height, -size.Width / 2, -size.Height, 128, 128, Enumerable.Repeat( (byte)1, size.Width * size.Height ).ToArray() ) ).ToArray();
		return new SpriteBankFile( new System.IO.MemoryStream( SpriteBankWriter.Write( palette, frames ) ) );
	}

	[TestMethod]
	public void TheDanglingSlotIsTakenFromTheLowDetailBankWithItsOwnPalette()
	{
		var slots = new SpriteAnimation[SpriteAnimationFile.SlotCount];
		for ( var slot = 0; slot < slots.Length; slot++ )
			slots[slot] = new SpriteAnimation( slot, 0, 0, 0 );
		slots[0] = new SpriteAnimation( 0, 0, 1, 1 );
		slots[StaffSpriteRules.Dangle.Slot] = new SpriteAnimation( StaffSpriteRules.Dangle.Slot, 1, 2, 1 );
		var esp = new SpriteAnimationFile( new System.IO.MemoryStream( SpriteAnimationWriter.Write( "SPR_ZZ.TPS", 1, 0, slots, new byte[16] ) ) );
		// the full-detail bank holds the slot as 2 x 2 placeholders, as every shipped staff FPC does; the low-detail bank has the art, in other colours
		var full = Bank( 0, (4, 4), (2, 2), (2, 2) );
		var low = Bank( 255, (3, 3), (5, 6), (7, 6) );

		var atlas = new GuestSpriteAtlas( "SPR_ZZ", full, esp, low, StaffSpriteRules.Dangle.Slot, "staff-test" );
		Assert.AreEqual( (4, 4), (atlas.Frames[0].Width, atlas.Frames[0].Height), "the other slots keep the chosen bank's frames" );
		Assert.AreEqual( (5, 6), (atlas.Frames[1].Width, atlas.Frames[1].Height), "the dangling frames come from the low-detail bank" );
		Assert.AreEqual( (7, 6), (atlas.Frames[2].Width, atlas.Frames[2].Height) );
		byte[] Pixel( int frame ) => atlas.Pixels.AsSpan( (atlas.Frames[frame].Y * atlas.Width + atlas.Frames[frame].X) * 4, 4 ).ToArray();
		CollectionAssert.AreEqual( new byte[] { 255, 0, 0, 255 }, Pixel( 0 ), "the chosen bank's palette (red)" );
		CollectionAssert.AreEqual( new byte[] { 0, 0, 255, 255 }, Pixel( 1 ), "a borrowed frame decodes with the low-detail bank's palette (blue)" );

		var plain = new GuestSpriteAtlas( "SPR_ZZ", full, esp, "staff-test" );
		Assert.AreEqual( (2, 2), (plain.Frames[1].Width, plain.Frames[1].Height), "without a low-detail bank nothing changes" );
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
