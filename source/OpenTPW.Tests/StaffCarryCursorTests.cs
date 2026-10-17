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

	private static byte[] BankBytes( byte blue, params (int Width, int Height)[] sizes )
	{
		var palette = new byte[SpriteBankFile.PaletteEntries * 4];
		(palette[0], palette[1], palette[2], palette[3]) = (blue, 0, (byte)(255 - blue), 255);
		var frames = sizes.Select( size => new SpriteFrame( size.Width, size.Height, -size.Width / 2, -size.Height, 128, 128, Enumerable.Repeat( (byte)1, size.Width * size.Height ).ToArray() ) ).ToArray();
		return SpriteBankWriter.Write( palette, frames );
	}

	private static SpriteBankFile Bank( byte blue, params (int Width, int Height)[] sizes ) => new( new System.IO.MemoryStream( BankBytes( blue, sizes ) ) );

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
	public void TheDanglingFramesAreTakenFromTheLowDetailBankWithItsOwnPalette()
	{
		var esp = new SpriteAnimationFile( new System.IO.MemoryStream( EspBytes( 1 ) ) );
		var dangle = (StaffSpriteRules.Dangle.Slot, StaffRenderer.DangleDirection);
		var atlas = new GuestSpriteAtlas( "SPR_ZZ", Bank( 0, Placeholders ), esp, Bank( 255, Art ), dangle, "staff-test" );
		Assert.AreEqual( (4, 4), (atlas.Frames[0].Width, atlas.Frames[0].Height), "the other slots keep the chosen bank's frames" );
		Assert.AreEqual( (7, 6), (atlas.Frames[5].Width, atlas.Frames[5].Height), "the drawn direction (4) comes from the low-detail bank" );
		Assert.AreEqual( (2, 2), (atlas.Frames[1].Width, atlas.Frames[1].Height), "only the direction the tools draw is taken" );
		byte[] Pixel( int frame ) => atlas.Pixels.AsSpan( (atlas.Frames[frame].Y * atlas.Width + atlas.Frames[frame].X) * 4, 4 ).ToArray();
		CollectionAssert.AreEqual( new byte[] { 255, 0, 0, 255 }, Pixel( 0 ), "the chosen bank's palette (red)" );
		CollectionAssert.AreEqual( new byte[] { 0, 0, 255, 255 }, Pixel( 5 ), "a taken frame decodes with the low-detail bank's palette (blue)" );

		var ownArt = new GuestSpriteAtlas( "SPR_ZZ", Bank( 0, (4, 4), (9, 9), (9, 9), (9, 9), (9, 9), (9, 9) ), esp, Bank( 255, Art ), dangle, "staff-test" );
		Assert.AreEqual( (9, 9), (ownArt.Frames[5].Width, ownArt.Frames[5].Height), "a bank with art of its own in the slot keeps it" );
		var otherLayout = new GuestSpriteAtlas( "SPR_ZZ", Bank( 0, Placeholders ), esp, Bank( 255, Art.Append( (8, 8) ).ToArray() ), dangle, "staff-test" );
		Assert.AreEqual( (2, 2), (otherLayout.Frames[5].Width, otherLayout.Frames[5].Height), "a low-detail bank with other frames is not mixed in" );
		var plain = new GuestSpriteAtlas( "SPR_ZZ", Bank( 0, Placeholders ), esp, "staff-test" );
		Assert.AreEqual( (2, 2), (plain.Frames[5].Width, plain.Frames[5].Height), "without a low-detail bank nothing changes" );
	}

	[TestMethod]
	public void LoadSetsFindsTheLowDetailBankBesideTheChosenOne()
	{
		var previous = FileSystem;
		var root = System.IO.Path.Combine( System.IO.Path.GetTempPath(), $"opentpw-staff-banks-{Guid.NewGuid():N}" );
		try
		{
			void Write( string folder, string file, byte[] bytes )
			{
				System.IO.Directory.CreateDirectory( System.IO.Path.Combine( root, folder ) );
				System.IO.File.WriteAllBytes( System.IO.Path.Combine( root, folder, file ), bytes );
			}
			// a full-detail set (byte 0x10C = 1): the FPC is chosen and its placeholders are filled from the TPC
			Write( "Full", "SPR_ZZ.ESP", EspBytes( 1 ) );
			Write( "Full", "SPR_ZZ.FPC", BankBytes( 0, Placeholders ) );
			Write( "Full", "SPR_ZZ.TPC", BankBytes( 255, Art ) );
			// a set that already draws its TPC
			Write( "Low", "SPR_ZZ.ESP", EspBytes( 0 ) );
			Write( "Low", "SPR_ZZ.TPC", BankBytes( 0, Placeholders ) );
			// a TPC that does not decode is left out, the set still loads
			Write( "Broken", "SPR_ZZ.ESP", EspBytes( 1 ) );
			Write( "Broken", "SPR_ZZ.FPC", BankBytes( 0, Placeholders ) );
			Write( "Broken", "SPR_ZZ.TPC", new byte[] { 1, 2, 3 } );
			FileSystem = new BaseFileSystem( root );
			var dangle = (StaffSpriteRules.Dangle.Slot, StaffRenderer.DangleDirection);

			var full = GuestSpriteAtlas.LoadSets( "/Full", "staff-test", lowDetailFrames: dangle ).Single();
			Assert.AreEqual( (7, 6), (full.Frames[5].Width, full.Frames[5].Height), "the FPC's placeholder is filled from the TPC" );
			Assert.AreEqual( (4, 4), (full.Frames[0].Width, full.Frames[0].Height), "the rest stays the FPC's" );
			Assert.AreEqual( (2, 2), (GuestSpriteAtlas.LoadSets( "/Full", "staff-test" ).Single().Frames[5].Width, 2), "only when asked for" );
			Assert.AreEqual( (2, 2), (GuestSpriteAtlas.LoadSets( "/Low", "staff-test", lowDetailFrames: dangle ).Single().Frames[5].Width, 2), "a set drawing its TPC keeps it" );
			Assert.AreEqual( (2, 2), (GuestSpriteAtlas.LoadSets( "/Broken", "staff-test", lowDetailFrames: dangle ).Single().Frames[5].Width, 2), "a broken TPC is not used" );
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
		var placement = new StaffPlacement( () => economy, () => grid.Cells, ( _, _ ) => false );
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
