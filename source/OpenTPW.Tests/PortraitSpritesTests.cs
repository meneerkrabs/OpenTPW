using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The sprites the windows own: the employee and visitor portrait clones (tp.exe 0x486530 full copy, 0x486610 partial copy, 0x486630 delete) and the hire figure (0x49BA00, 0x49B7B4), stepped by the sprite scheduler.
/// </summary>
[TestClass]
public class PortraitSpritesTests
{
	private static StaffMember Member( int id = 1 ) => new() { Id = id, ThingId = id + 100, Type = StaffType.Handyman, NameIndex = 0, Grade = 1, Variant = 0, CellX = 4, CellY = 4 };

	/// <summary>A member with a source in the walk template, created at clock 0: due 62, interval 62, nothing shown yet.</summary>
	private static StaffMember Sourced( int id = 1 )
	{
		var member = Member( id );
		member.Sprite.Create( member.Type, 0, StaffSpriteEvents.Stand, 0, null );
		member.Sprite.QueueTemplate( 1 );
		member.Sprite.ApplyPending( member.Type, 0, null );
		return member;
	}

	private static (uint X, uint Y, uint Z) Place => PortraitSprites.Placement( 386, 280, 650, 544, PortraitSprites.EmployeeScale, 2 );

	[TestMethod]
	public void PlacementFollowsTheTracedFormulaOfThe486530Clone()
	{
		// x = float(left + ((right - left) >> 1) - 0x3F8) / 1024, y = float(((bottom - top) >> 2) - trunc(scale * -0.25) - bottom + 0x300) / 768, z = 1 - depth / 40
		var (x, y, z) = PortraitSprites.Placement( 386, 280, 650, 544, 8f, 2 );
		Assert.AreEqual( (386 + 132 - 0x3F8) / 1024f, BitConverter.UInt32BitsToSingle( x ) );
		Assert.AreEqual( (66 + 2 - 544 + 0x300) / 768f, BitConverter.UInt32BitsToSingle( y ), 1e-6f );
		Assert.AreEqual( 1f - 2f / 40f, BitConverter.UInt32BitsToSingle( z ) );
		// the visitor window's scale 12 lifts the sprite by three, not two
		var (_, visitorY, _) = PortraitSprites.Placement( 386, 280, 650, 544, 12f, 3 );
		Assert.AreEqual( (66 + 3 - 544 + 0x300) / 768f, BitConverter.UInt32BitsToSingle( visitorY ), 1e-6f );
		// signed shifts: an inverted rectangle halves and quarters towards minus infinity
		var (invertedX, invertedY, _) = PortraitSprites.Placement( 20, 30, 7, 15, 8f, 1 );
		Assert.AreEqual( (20 + ((7 - 20) >> 1) - 0x3F8) / 1024f, BitConverter.UInt32BitsToSingle( invertedX ) );
		Assert.AreEqual( (((15 - 30) >> 2) + 2 - 15 + 0x300) / 768f, BitConverter.UInt32BitsToSingle( invertedY ), 1e-6f );
	}

	[TestMethod]
	public void TheFullCopyIsTheWholeRecordWithItsOwnPlacementScaleAndPortraitFlags()
	{
		var member = Sourced();
		Assert.AreEqual( StaffSpriteTickStatus.Yielded, member.Sprite.Tick( 70 ).Status );
		var portraits = new PortraitSprites();
		portraits.CloneStaff( member, Place, PortraitSprites.EmployeeScale );
		var clone = portraits.StaffClone!;
		var source = member.Sprite.Source!;
		Assert.IsTrue( portraits.HasSource );
		Assert.AreEqual( source.CurrentPc, clone.CurrentPc ); Assert.AreEqual( source.RestartPc, clone.RestartPc );
		Assert.AreEqual( source.Due, clone.Due ); Assert.AreEqual( source.Interval, clone.Interval );
		Assert.AreEqual( member.Sprite.Output, portraits.StaffOutput, "a clone made after the source's first frame shows it" );
		Assert.AreEqual( Place.X, clone[StaffSpriteRegister.X] ); Assert.AreEqual( Place.Y, clone[StaffSpriteRegister.Y] ); Assert.AreEqual( Place.Z, clone[StaffSpriteRegister.Z] );
		Assert.AreEqual( BitConverter.SingleToUInt32Bits( 8f ), clone[StaffSpriteRegister.PrimaryScale] );
		Assert.AreEqual( 0x1000u | 0x50000u, clone[StaffSpriteRegister.Flags], "the walk program's 0x1200 loses 0x200 and gains 0x50000" );
		Assert.AreEqual( 0x1200u, source[StaffSpriteRegister.Flags], "the source's own flags are not touched" );
		// the copy is independent: stepping the source leaves the clone where it was
		member.Sprite.Tick( 140 );
		Assert.AreNotEqual( member.Sprite.Output, portraits.StaffOutput );
	}

	[TestMethod]
	public void ThePartialRefreshTakesPositionAndFrameAndKeepsTheClonesOwnClock()
	{
		var member = Sourced();
		member.Sprite.Tick( 70 );
		var portraits = new PortraitSprites();
		portraits.CloneStaff( member, Place, PortraitSprites.EmployeeScale );
		var clone = portraits.StaffClone!;
		// the source goes on, takes another interval and another program
		member.Sprite.QueueInterval( 148 );
		member.Sprite.QueueTemplate( 2 );
		member.Sprite.ApplyPending( member.Type, 0, null );
		member.Sprite.Tick( 140 );
		member.HeadingX = 1;
		member.HeadingY = 0;
		var due = clone.Due; var interval = clone.Interval; var restart = clone.RestartPc;
		portraits.Refresh();
		var source = member.Sprite.Source!;
		Assert.AreEqual( source.CurrentPc, clone.CurrentPc, "the program counter is copied" );
		Assert.AreEqual( member.Sprite.Output, portraits.StaffOutput, "the frame and its validity are copied" );
		Assert.AreEqual( source[StaffSpriteRegister.Slot], clone[StaffSpriteRegister.Slot] );
		Assert.AreEqual( (uint)GuestSpriteAtlas.HeadingSector( 1, 0 ), clone[StaffSpriteRegister.Heading], "the heading word is the member's current sector" );
		Assert.AreEqual( due, clone.Due, "the due time stays" ); Assert.AreEqual( interval, clone.Interval, "the interval stays" );
		Assert.AreEqual( restart, clone.RestartPc, "the restart counter stays" );
		Assert.AreNotEqual( source.RestartPc, clone.RestartPc );
		Assert.AreEqual( 148, source.Interval ); Assert.AreEqual( 62, clone.Interval );
	}

	[TestMethod]
	public void ASourceThatIsGoneLeavesTheCloneRunningOnItsOwnClock()
	{
		var member = Sourced();
		member.Sprite.Tick( 70 );
		var portraits = new PortraitSprites();
		portraits.CloneStaff( member, Place, PortraitSprites.EmployeeScale );
		var first = portraits.StaffOutput;
		Assert.IsNotNull( first );
		member.Sprite.Delete();
		portraits.Refresh();
		Assert.AreEqual( first, portraits.StaffOutput, "the clone stands as it was copied" );
		Assert.IsTrue( portraits.HasSource, "the window still holds its person" );
		// the scheduler steps the clone: the walk loop's next frames, one per due pass
		portraits.Tick( 140 );
		Assert.AreEqual( (1, 1), portraits.StaffOutput );
		portraits.Tick( 200 );
		Assert.AreEqual( (1, 1), portraits.StaffOutput, "62 ms after 140 is not past due yet" );
		portraits.Tick( 210 );
		Assert.AreEqual( (1, 2), portraits.StaffOutput );
	}

	[TestMethod]
	public void ANewSourceOfTheSameMemberIsFollowedThroughTheCurrentHandle()
	{
		var member = Sourced();
		member.Sprite.Tick( 70 );
		var portraits = new PortraitSprites();
		portraits.CloneStaff( member, Place, PortraitSprites.EmployeeScale );
		member.Sprite.Delete();
		// a placed member gets a fresh source (stand, due 62 ms after its creation) and the next refresh reads that handle
		member.Sprite.Create( member.Type, 0, StaffSpriteEvents.Stand, 1000, null );
		member.Sprite.Tick( 1070 );
		portraits.Refresh();
		Assert.AreEqual( member.Sprite.Output, portraits.StaffOutput );
		Assert.AreEqual( (0, 0), portraits.StaffOutput );
	}

	[TestMethod]
	public void AFullCopyOfAMemberWithoutASourceBindsNothingEvenWhenOneAppearsLater()
	{
		var member = Member();
		var portraits = new PortraitSprites();
		portraits.CloneStaff( member, Place, PortraitSprites.EmployeeScale );
		Assert.IsFalse( portraits.HasSource ); Assert.IsNull( portraits.StaffClone );
		member.Sprite.Create( member.Type, 0, StaffSpriteEvents.Stand, 0, null );
		member.Sprite.Tick( 70 );
		portraits.Refresh();
		Assert.IsNull( portraits.StaffClone, "[0x7C24D8] was cleared by the full copy, so the partial refresh has nobody to follow (0x486610)" );
		Assert.IsNull( portraits.StaffOutput );
		// a refresh of the window (the full copy again) binds it
		portraits.CloneStaff( member, Place, PortraitSprites.EmployeeScale );
		Assert.IsTrue( portraits.HasSource ); Assert.AreEqual( (0, 0), portraits.StaffOutput );
	}

	[TestMethod]
	public void AnotherFullCopyReplacesTheEarlierCloneAndCloseDeletesIt()
	{
		var first = Sourced( 1 ); var second = Sourced( 2 );
		first.Sprite.Tick( 70 );
		var portraits = new PortraitSprites();
		portraits.CloneStaff( first, Place, PortraitSprites.EmployeeScale );
		var firstClone = portraits.StaffClone;
		portraits.CloneStaff( second, Place, PortraitSprites.EmployeeScale );
		Assert.AreNotSame( firstClone, portraits.StaffClone );
		Assert.AreSame( second, portraits.SourceMember );
		Assert.IsNull( portraits.StaffOutput, "the second member's source has shown nothing yet" );
		// the clone of a member without a source also drops the earlier clone
		portraits.CloneStaff( Member( 3 ), Place, PortraitSprites.EmployeeScale );
		Assert.IsNull( portraits.StaffClone ); Assert.IsFalse( portraits.HasSource );
		portraits.CloneStaff( first, Place, PortraitSprites.EmployeeScale );
		Assert.IsNotNull( portraits.StaffClone );
		portraits.Close();
		Assert.IsNull( portraits.StaffClone ); Assert.IsNull( portraits.SourceMember ); Assert.IsFalse( portraits.HasSource );
		portraits.Tick( 500 );
		portraits.Refresh();
		Assert.IsNull( portraits.StaffClone, "a closed window's clone is not refreshed or revived" );
	}

	[TestMethod]
	public void TheCloneIsSteppedByTheSchedulerOnItsOwnDueTime()
	{
		var member = Sourced();
		member.Sprite.Tick( 70 );
		var portraits = new PortraitSprites();
		portraits.CloneStaff( member, Place, PortraitSprites.EmployeeScale );
		var clone = portraits.StaffClone!;
		Assert.AreEqual( 132, clone.Due );
		portraits.Tick( 132 );
		Assert.AreEqual( 132, clone.Due, "strictly later: a sample equal to the due time does not run" );
		var counter = clone.CurrentPc;
		portraits.Tick( 133 );
		Assert.AreEqual( 133 + 62, clone.Due );
		Assert.AreNotEqual( counter, clone.CurrentPc );
	}

	[TestMethod]
	public void AVisitorCloneIsTheBodyPlayersCopyAndKeepsItsOwnClock()
	{
		var guest = new Guest( 7, 0 ) { State = GuestState.WalkingAround, HeadingX = 0, HeadingY = 1 };
		guest.BodySprite.CreatePlayer( VisitorSpriteTemplate.Walk, 0 );
		guest.BodySprite.Tick( 70 );
		var portraits = new PortraitSprites();
		portraits.CloneGuest( guest );
		var clone = portraits.GuestClone!;
		var player = guest.BodySprite.Player!;
		Assert.AreEqual( player.Output, clone.Output ); Assert.AreEqual( player.NextDue, clone.NextDue ); Assert.AreEqual( player.Interval, clone.Interval );
		// the player takes a faster interval and runs on; the clone's partial copy takes the frame and the position, not the clock
		guest.BodySprite.QueueInterval( 20 );
		guest.BodySprite.ApplyPending();
		guest.BodySprite.Tick( 140 );
		guest.BodySprite.Tick( 170 );
		var due = clone.NextDue;
		portraits.Refresh();
		Assert.AreEqual( player.Output, clone.Output ); Assert.AreEqual( player.NextFrame, clone.NextFrame );
		Assert.AreEqual( due, clone.NextDue ); Assert.AreEqual( SpritePlayer.DefaultInterval, clone.Interval ); Assert.AreEqual( 20, player.Interval );
		// the body is deleted (a ride that keeps no body): the clone plays on
		guest.BodySprite.DeletePlayer();
		var shown = clone.Output;
		portraits.Refresh();
		Assert.AreEqual( shown, clone.Output );
		portraits.Tick( 400 );
		Assert.AreNotEqual( shown, clone.Output );
	}

	[TestMethod]
	public void AGuestWithoutABodyBindsNothing()
	{
		var guest = new Guest( 7, 0 ) { State = GuestState.WalkingAround };
		var portraits = new PortraitSprites();
		portraits.CloneGuest( guest );
		Assert.IsFalse( portraits.HasSource ); Assert.IsNull( portraits.GuestClone );
		guest.BodySprite.CreatePlayer( VisitorSpriteTemplate.Walk, 0 );
		portraits.Refresh();
		Assert.IsNull( portraits.GuestClone );
	}

	[TestMethod]
	public void TheHireFigureShowsNothingBeforeItsFirstDuePassAndLoopsItsEightFrames()
	{
		var portraits = new PortraitSprites();
		portraits.ShowHire( StaffType.Mechanic, 1, 1000 );
		var figure = portraits.Hire!;
		Assert.AreEqual( 1062, figure.Due ); Assert.IsNull( portraits.HireOutput );
		portraits.Tick( 1062 );
		Assert.IsNull( portraits.HireOutput, "the pass must be strictly later than the due time (0x475360)" );
		var now = 1063;
		for ( var round = 0; round < 2; round++ )
			for ( var frame = 0; frame < 8; frame++ )
			{
				portraits.Tick( now );
				Assert.AreEqual( (StaffSpriteRules.PortraitSlot, frame), portraits.HireOutput, $"round {round}, frame {frame}" );
				now += 63;
			}
		Assert.AreEqual( 0x51000u, figure[StaffSpriteRegister.Flags], "the script sets the flags 0x51000 (screen space, no camera turn)" );
		Assert.AreEqual( (uint)StaffSpriteRules.PortraitDirection, figure[StaffSpriteRegister.Heading] );
		Assert.AreEqual( (uint)StaffSpriteState.CategoryOf( StaffType.Mechanic ), figure[StaffSpriteRegister.Kind] );
		Assert.AreEqual( 1u, figure[StaffSpriteRegister.Variant] );
		Assert.AreEqual( BitConverter.SingleToUInt32Bits( 8f ), figure[StaffSpriteRegister.PrimaryScale] );
	}

	[TestMethod]
	public void TheHireFigureKeepsItsRoleAndVariantAndCloseForgetsThem()
	{
		var portraits = new PortraitSprites();
		portraits.ShowHire( StaffType.Guard, 2, 0 );
		var figure = portraits.Hire;
		portraits.Tick( 63 );
		portraits.ShowHire( StaffType.Guard, 2, 100 );
		Assert.AreSame( figure, portraits.Hire, "the same pair keeps the figure (0x7523E8, 0x7523EC)" );
		Assert.AreEqual( (1, 0), portraits.HireOutput );
		portraits.ShowHire( StaffType.Guard, 3, 200 );
		Assert.AreNotSame( figure, portraits.Hire ); Assert.IsNull( portraits.HireOutput, "a new variant starts a new figure" );
		figure = portraits.Hire;
		portraits.ShowHire( StaffType.Researcher, 3, 200 );
		Assert.AreNotSame( figure, portraits.Hire, "a new role does too" );
		portraits.CloseHire();
		Assert.IsNull( portraits.Hire ); Assert.IsNull( portraits.HireKey );
		portraits.ShowHire( StaffType.Researcher, 3, 300 );
		Assert.IsNotNull( portraits.Hire, "the next open starts a new one, whatever it selects" );
	}

	[TestMethod]
	public void TheHireFigureAndTheCloneStepTogetherOnOneSchedulerPass()
	{
		var member = Sourced();
		member.Sprite.Tick( 70 );
		var portraits = new PortraitSprites();
		portraits.CloneStaff( member, Place, PortraitSprites.EmployeeScale );
		portraits.ShowHire( StaffType.Handyman, 0, 100 );
		portraits.Tick( 200 );
		Assert.IsNotNull( portraits.HireOutput ); Assert.AreEqual( 200 + 62, portraits.StaffClone!.Due ); Assert.AreEqual( 200 + 62, portraits.Hire!.Due );
	}

	[TestMethod]
	public void TheHireScriptIsTheSixInstructionsOfTheTable()
	{
		// 0x74F758: SET 0x10 0x51000, SLOT 1, SET 0xF 5, FRAME 0..7, JMP 0x74F758
		var program = StaffSpritePrograms.Builtin;
		var ops = Enumerable.Range( 0, 13 ).Select( index => program.TryRead( new StaffSpritePc( StaffSpriteProgram.HireFigure, index ), out var instruction ) ? instruction : default ).ToArray();
		Assert.AreEqual( StaffSpriteOp.Set, ops[0].Op ); Assert.AreEqual( 16, ops[0].A ); Assert.AreEqual( 0x51000, ops[0].B );
		Assert.AreEqual( StaffSpriteOp.Slot, ops[1].Op ); Assert.AreEqual( 1, ops[1].A );
		Assert.AreEqual( StaffSpriteOp.Set, ops[2].Op ); Assert.AreEqual( 15, ops[2].A ); Assert.AreEqual( 5, ops[2].B );
		for ( var frame = 0; frame < 8; frame++ )
		{
			Assert.AreEqual( StaffSpriteOp.Frame, ops[3 + frame].Op ); Assert.AreEqual( frame, ops[3 + frame].A );
		}
		Assert.AreEqual( StaffSpriteOp.Jump, ops[11].Op ); Assert.AreEqual( new StaffSpritePc( StaffSpriteProgram.HireFigure, 0 ), ops[11].Target );
		Assert.IsFalse( program.TryRead( new StaffSpritePc( StaffSpriteProgram.HireFigure, 12 ), out _ ) );
	}

	// the windows: the full copy at the refresh, the partial copy at every update (message 0x1E), the delete at the close

	private static Level LevelOf( ParkHud hud ) => (Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new System.IO.FileNotFoundException( name ) ) ) { Delta = 0.016f };
	private static void Update( ParkHud hud ) => hud.Stack.Top!.Update( Context(), UiInput.Idle( System.Numerics.Vector2.Zero ) );

	[TestMethod]
	public void TheEmployeeWindowCopiesAtItsRefreshRefreshesEveryUpdateAndDeletesAtTheClose()
	{
		var economy = EconomyTestData.Park();
		var first = EconomyTestData.HireBest( economy, StaffType.Handyman );
		var second = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		foreach ( var member in new[] { first, second } )
		{
			member.Sprite.Create( member.Type, 0, StaffSpriteEvents.Stand, 0, null );
			member.Sprite.QueueTemplate( 1 );
			member.Sprite.ApplyPending( member.Type, 0, null );
		}
		first.Sprite.Tick( 70 );
		var hud = StaffWindowTests.Hud( economy ); var portraits = LevelOf( hud ).Portraits;
		Assert.IsTrue( hud.OpenEmployeeWindow( first ) );
		Assert.AreSame( first, portraits.SourceMember ); Assert.AreEqual( first.Sprite.Output, portraits.StaffOutput );
		// the source goes on; the clone follows at the window's next update, not before
		first.Sprite.Tick( 140 );
		Assert.AreNotEqual( first.Sprite.Output, portraits.StaffOutput );
		Update( hud );
		Assert.AreEqual( first.Sprite.Output, portraits.StaffOutput, "message 0x1E refreshes the clone" );
		// the arrows make a new full copy of the other member, whose source has shown nothing yet
		Assert.IsTrue( hud.CycleEmployee( true ) );
		Assert.AreSame( hud.Employee, portraits.SourceMember ); Assert.AreNotSame( first, portraits.SourceMember );
		Assert.IsNull( portraits.StaffOutput );
		hud.CloseEmployeeWindow();
		Assert.IsNull( portraits.StaffClone ); Assert.IsFalse( portraits.HasSource, "the close hook 0x486630 deletes the clone" );
	}

	[TestMethod]
	public void AnEmployeeWithoutASourceGetsNoCloneEvenAfterOneIsMade()
	{
		var economy = EconomyTestData.Park();
		var member = EconomyTestData.HireBest( economy, StaffType.Guard );
		var hud = StaffWindowTests.Hud( economy ); var portraits = LevelOf( hud ).Portraits;
		Assert.IsTrue( hud.OpenEmployeeWindow( member ) );
		Assert.IsFalse( portraits.HasSource, "a resting member has no sprite (+0x0C is 0)" );
		member.Sprite.Create( member.Type, 0, StaffSpriteEvents.Stand, 0, null );
		member.Sprite.Tick( 70 );
		Update( hud );
		Assert.IsNull( portraits.StaffClone, "the window never binds a source that appears after its full copy" );
		hud.CloseEmployeeWindow();
		Assert.IsTrue( hud.OpenEmployeeWindow( member ) );
		Assert.IsNotNull( portraits.StaffClone, "a refresh of the window binds it" );
	}

	[TestMethod]
	public void TheVisitorWindowCopiesTheBodyAtItsRefreshAndDeletesTheCloneAtTheClose()
	{
		var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out _ ); var portraits = LevelOf( hud ).Portraits;
		var guest = new Guest( 7, 0 ) { State = GuestState.WalkingAround };
		var other = new Guest( 8, 0 ) { State = GuestState.WalkingAround };
		((System.Collections.Generic.List<Guest>)simulation.Guests).AddRange( new[] { guest, other } );
		guest.BodySprite.CreatePlayer( VisitorSpriteTemplate.Walk, 0 );
		guest.BodySprite.Tick( 70 );
		Assert.IsTrue( hud.OpenVisitorInfoWindow( guest ) );
		Assert.AreSame( guest, portraits.SourceGuest ); Assert.AreEqual( guest.BodySprite.Player!.Output, portraits.GuestClone!.Output );
		guest.BodySprite.Tick( 140 );
		Assert.AreNotEqual( guest.BodySprite.Player.Output, portraits.GuestClone.Output );
		Update( hud );
		Assert.AreEqual( guest.BodySprite.Player.Output, portraits.GuestClone.Output );
		// a visitor without a body leaves no clone
		Assert.IsTrue( hud.OpenVisitorInfoWindow( other ) );
		Assert.IsFalse( portraits.HasSource ); Assert.IsNull( portraits.GuestClone );
		hud.CloseVisitorInfoWindow();
		Assert.IsFalse( portraits.HasSource );
	}
}
