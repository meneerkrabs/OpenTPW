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
		return StaffPlacementRig.Create( () => economy, () => grid.Cells, ( _, _ ) => false );
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
	public void TheDanglingPersonHangsAtTheTerrainsHeightAgainAbovePickedPointPlusFive()
	{
		// [BIN:TPW-EXE:0x00542010 / 0x0046C480] the tool stores the picker's absolute height + 5 and the sprite pass adds the terrain's height under the sprite to the stored word (the sprite's flag 0x1000000 is clear)
		var placement = Placement( out var park );
		var candidate = park.Staff.Candidates.First( item => item.Type == StaffType.Handyman );
		var heights = Enumerable.Repeat( 30f, 21 * 21 ).ToArray();
		var raised = new ModelHeightfield( 20, 20, 10, 10, 0, 0, Array.Empty<uint>(), heights, new uint[20 * 20] );
		Assert.IsTrue( placement.BeginHire( candidate.Id ) );
		// the picked point on a model 19 engine units up over ground 30 original units (6 engine units) high: 5 + 30 original units on top of the point
		var picked = new System.Numerics.Vector3( 4, 5, 19 );
		placement.SetCursor( 6.3f, 5.9f, picked );
		Assert.AreEqual( 19f + (5f + 30f) * OriginalParkPlacement.ModelScale, StaffRenderer.DangleAnchor( placement, raised )!.Value.Z, 1e-4f );
		// the terrain-only point: the ground (30), then the stored 30 + 5 on top
		placement.SetCursor( 6.4f, 5.4f );
		Assert.AreEqual( GuestRenderer.CellToEngine( raised, 6.4f, 5.4f, 35f ), StaffRenderer.DangleAnchor( placement, raised ) );
		Assert.AreEqual( GuestRenderer.CellToEngine( raised, 6.4f, 5.4f ).Z + 35f * OriginalParkPlacement.ModelScale, StaffRenderer.DangleAnchor( placement, raised )!.Value.Z, 1e-4f );
	}

	[TestMethod]
	public void CellOnlyCursorUpdatesAndToolReplacementCannotReuseAnOldModelHeight()
	{
		var placement = Placement( out var park );
		var candidate = park.Staff.Candidates.First( item => item.Type == StaffType.Handyman );
		var field = new ModelHeightfield( 20, 20, 10, 10, 0, 0, Array.Empty<uint>(), new float[21 * 21], new uint[20 * 20] );
		var elevated = new System.Numerics.Vector3( 4, 5, 19 );
		Assert.IsTrue( placement.BeginHire( candidate.Id ) );
		placement.SetCursor( 6.3f, 5.9f, elevated );
		Assert.AreEqual( 20f, StaffRenderer.DangleAnchor( placement, field )!.Value.Z );

		placement.SetCursor( 6.4f, 5.4f );
		Assert.IsNull( placement.CursorEnginePoint, "a cell-only refresh must not reuse the previous model hit" );
		Assert.AreEqual( GuestRenderer.CellToEngine( field, 6.4f, 5.4f, StaffRenderer.DangleLift ), StaffRenderer.DangleAnchor( placement, field ) );
		placement.Hover( 6, 5 );
		placement.SetCursor( null, null );
		Assert.AreEqual( GuestRenderer.CellToEngine( field, 6.5f, 5.5f, StaffRenderer.DangleLift ), StaffRenderer.DangleAnchor( placement, field ), "the existing hover fallback is unchanged" );

		placement.SetCursor( 6.3f, 5.9f, elevated );
		Assert.IsTrue( placement.BeginHire( candidate.Id ) );
		Assert.IsNull( placement.CursorEnginePoint, "replacing the tool begins without a stale point" );
		placement.SetCursor( 6.3f, 5.9f, elevated );
		placement.Abandon();
		Assert.IsNull( placement.CursorEnginePoint, "a replacement park drops the old presentation point" );
		placement.SetCursor( 6.3f, 5.9f, elevated );
		Assert.IsNull( placement.CursorEnginePoint, "an inactive tool cannot retain a hit" );
		Assert.IsNull( StaffRenderer.DangleAnchor( placement, field ) );
	}

	private static byte[] BankBytes( byte blue, params (int Width, int Height)[] sizes )
	{
		var palette = new byte[SpriteBankFile.PaletteEntries * 4];
		(palette[0], palette[1], palette[2], palette[3]) = (blue, 0, (byte)(255 - blue), 255);
		var frames = sizes.Select( size => new SpriteFrame( size.Width, size.Height, -size.Width / 2, -size.Height, 128, 128, Enumerable.Repeat( (byte)1, size.Width * size.Height ).ToArray() ) ).ToArray();
		return SpriteBankWriter.Write( palette, frames );
	}


	/// <summary>An ESP whose slot 0 is frame 0 and whose dangling slot 6 is frames 1-5, one frame for each of five directions.</summary>
	private static byte[] EspBytes( byte fullDetail )
	{
		var slots = new SpriteAnimation[SpriteAnimationFile.SlotCount];
		for ( var slot = 0; slot < slots.Length; slot++ )
			slots[slot] = new SpriteAnimation( slot, 0, 0, 0 );
		slots[0] = new SpriteAnimation( 0, 0, 1, 1 );
		slots[StaffSpriteRules.Dangle.Slot] = new SpriteAnimation( StaffSpriteRules.Dangle.Slot, 1, 1, 5 );
		return SpriteAnimationWriter.Write( "SPR_ZZ.TPS", fullDetail, 0, slots, new byte[16] );
	}

	private static readonly (int Width, int Height)[] Placeholders = { (4, 4), (2, 2), (2, 2), (2, 2), (2, 2), (2, 2) };
	private static readonly (int Width, int Height)[] Art = { (3, 3), (5, 6), (5, 6), (5, 6), (5, 6), (7, 6) };

	[TestMethod]
	public void TheDanglingPersonTurnsWithTheCamera()
	{
		// heading 4 (0x46C480) plus the camera's yaw sector (0x542010): the person keeps its heading in the world while the camera goes round it
		var seen = new[] { 0f, MathF.PI / 2, MathF.PI, 3 * MathF.PI / 2 }.Select( yaw => GuestSpriteAtlas.SelectOriginalDirection( StaffRenderer.DangleHeading, yaw, 5 ) ).ToArray();
		CollectionAssert.AreEqual( new[] { (4, false), (2, false), (0, false), (2, true) }, seen );
	}

	[TestMethod]
	public void EverySetIsDrawnFromItsTpcAsTheGameDoes()
	{
		var previous = FileSystem;
		var root = System.IO.Path.Combine( System.IO.Path.GetTempPath(), $"opentpw-sprite-banks-{Guid.NewGuid():N}" );
		try
		{
			void Write( string folder, string file, byte[] bytes )
			{
				System.IO.Directory.CreateDirectory( System.IO.Path.Combine( root, folder ) );
				System.IO.File.WriteAllBytes( System.IO.Path.Combine( root, folder, file ), bytes );
			}
			// a full-detail set (byte 0x10C = 1) with both banks: the FPC holds the slot as placeholders, as every staff FPC does, the TPC has the art
			Write( "Full", "SPR_ZZ.ESP", EspBytes( 1 ) );
			Write( "Full", "SPR_ZZ.FPC", BankBytes( 0, Placeholders ) );
			Write( "Full", "SPR_ZZ.TPC", BankBytes( 255, Art ) );
			// a set with only an FPC is not drawn (the game opens the TPC)
			Write( "FpcOnly", "SPR_ZZ.ESP", EspBytes( 1 ) );
			Write( "FpcOnly", "SPR_ZZ.FPC", BankBytes( 0, Placeholders ) );
			Write( "Low", "SPR_ZZ.ESP", EspBytes( 0 ) );
			Write( "Low", "SPR_ZZ.TPC", BankBytes( 255, Art ) );
			FileSystem = new BaseFileSystem( root );

			var full = GuestSpriteAtlas.LoadSets( "/Full", "staff-test" ).Single();
			Assert.AreEqual( (7, 6), (full.Frames[5].Width, full.Frames[5].Height), "the TPC is drawn, with the dangling art" );
			Assert.AreEqual( "esprites/generic/staff-test/spr_zz.tpc.atlas", full.PackKey, "a set that also ships an FPC names the TPC in its texture-pack key" );
			CollectionAssert.AreEqual( new byte[] { 0, 0, 255, 255 }, full.Pixels.AsSpan( (full.Frames[5].Y * full.Width + full.Frames[5].X) * 4, 4 ).ToArray(), "the TPC's palette" );
			Assert.AreEqual( 0, GuestSpriteAtlas.LoadSets( "/FpcOnly", "staff-test" ).Count );
			Assert.AreEqual( "esprites/generic/staff-test/spr_zz.atlas", GuestSpriteAtlas.LoadSets( "/Low", "staff-test" ).Single().PackKey, "a TPC-only set keeps its key" );
		}
		finally
		{
			FileSystem = previous;
			if ( System.IO.Directory.Exists( root ) )
				System.IO.Directory.Delete( root, true );
		}
	}

	[TestMethod]
	public void TheHireClosesTheStaffWindowSoNothingCoversThePark()
	{
		var economy = EconomyTestData.Park();
		var hud = StaffWindowTests.Hud( economy );
		var grid = new GuestPathGrid( 20, 20 );
		var placement = StaffPlacementRig.Create( () => economy, () => grid.Cells, ( _, _ ) => false );
		var levelField = typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!;
		var level = (Level)levelField.GetValue( hud )!;
		void Set( string name, object? value ) => typeof( Level ).GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, value );
		Set( "<StaffPlacement>k__BackingField", placement );
		Set( "<Park>k__BackingField", ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ) );

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
