using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// GUEST-016 and UI-092 (world sources): what a staff member's sprite wrapper and source do, event by event, in the original's terms (tp.exe 0x004217F0, 0x004D4190, 0x00475C50, 0x00475B80, 0x004D4140,
/// 0x004D4170, 0x0005054D0 and the role setters, 0x004FA2A0, 0x00475360). The pure rules: no world, no renderer, no game data.
/// </summary>
[TestClass]
public class StaffSpriteSourceTests
{
	private sealed class Banks( SpriteAnimationFile? animations, int count = 1 ) : IStaffSpriteBanks
	{
		public SpriteAnimationFile? AnimationsOf( StaffType type, int variantKey ) => animations;
		public int BankCount( StaffType type ) => count;
	}

	private static StaffMember Member( StaffType type = StaffType.Handyman ) =>
		new() { Id = 1, ThingId = 5, Type = type, NameIndex = 0, Grade = 0, Variant = 0 };

	private static SpriteAnimationFile Bank( params (int Slot, int Frames)[] slots )
	{
		var builder = new SpriteSetBuilder( "SPR_ZZ" ) { FullDetail = false };
		foreach ( var (slot, count) in slots )
			builder.AddAnimation( slot, new[] { (IReadOnlyList<SpriteRgbaFrame>)Enumerable.Range( 0, count ).Select( _ => new SpriteRgbaFrame( 2, 2, new byte[16], 0, 0 ) ).ToArray() } );
		builder.SetPerformance( 0, 0, slots[0].Slot, 2, 40 );
		var data = builder.Build();
		return new SpriteAnimationFile( new MemoryStream( data.Files[".ESP"] ) );
	}

	private static ParkStaff Staff( int clock = 1000 )
	{
		var staff = EconomyTestData.Park().Staff;
		staff.SpriteClockMilliseconds = clock;
		return staff;
	}

	[TestMethod]
	public void ASourceIsCreatedInItsTemplateWithoutAFrameAndShowsOneOnlyAtTheFirstPassPastItsDueTime()
	{
		var member = Member();
		member.Sprite.Create( member.Type, 3, StaffSpriteEvents.Stand, 1000, null );
		var source = member.Sprite.Source!;
		// 0x004758F0: interval 62, due = the clock + 62, alpha 255, both scales 1, no output
		Assert.AreEqual( (62, 1062, 255u), (source.Interval, source.Due, source[StaffSpriteRegister.Alpha]) );
		Assert.AreEqual( 5u, source[StaffSpriteRegister.Kind], "the handyman's sprite category" );
		Assert.AreEqual( 3u, source[StaffSpriteRegister.Variant] );
		Assert.IsFalse( member.Sprite.HasOutput );
		Assert.AreEqual( StaffSpriteTickStatus.NotDue, member.Sprite.Tick( 1062 ).Status, "the clock must pass the due time: equality does nothing" );
		Assert.IsNull( member.Sprite.Output );
		Assert.AreEqual( StaffSpriteTickStatus.Yielded, member.Sprite.Tick( 1063 ).Status );
		Assert.AreEqual( (0, 0), member.Sprite.Output, "the stand: slot 0, frame 0" );
		Assert.AreEqual( 1063 + 62, member.Sprite.Source!.Due, "the next due time is the sample plus the interval, with no catching up" );
		Assert.AreEqual( StaffSpriteTickStatus.Yielded, member.Sprite.Tick( 5000 ).Status );
		Assert.AreEqual( 5062, member.Sprite.Source.Due );
	}

	[TestMethod]
	public void TheCategoryOfARoleIsTheOnesTheOriginalConstructorsStore()
	{
		CollectionAssert.AreEqual( new[] { 5, 6, 7, 8, 4 },
			new[] { StaffType.Handyman, StaffType.Mechanic, StaffType.Guard, StaffType.Researcher, StaffType.Entertainer }.Select( StaffSpriteState.CategoryOf ).ToArray() );
	}

	[TestMethod]
	public void ThePreUpdateAppliesTheTemplateThenTheIntervalAndClearsBothEvenWithoutASource()
	{
		var member = Member( StaffType.Mechanic );
		member.Sprite.Create( member.Type, 0, StaffSpriteEvents.Stand, 0, null );
		member.Sprite.QueueTemplate( StaffSpriteEvents.Repair );
		member.Sprite.QueueInterval( 148 );
		// queuing alters nothing live
		Assert.AreEqual( new StaffSpritePc( StaffSpriteProgram.Stand, 0 ), member.Sprite.Source!.RestartPc );
		Assert.AreEqual( 62, member.Sprite.Source.Interval );
		member.Sprite.ApplyPending( member.Type, 0, null );
		Assert.AreEqual( new StaffSpritePc( StaffSpriteProgram.Repair, 0 ), member.Sprite.Source.RestartPc );
		Assert.AreEqual( 148, member.Sprite.Source.Interval );
		Assert.AreEqual( 62, member.Sprite.Source.Due, "a new interval does not rebase the due time" );
		Assert.AreEqual( (0, 0), (member.Sprite.PendingTemplate, member.Sprite.PendingInterval) );
		// a person without a source still clears what was queued (the underlying calls ignore a zero handle)
		var carried = Member();
		carried.Sprite.QueueTemplate( StaffSpriteEvents.Patrol );
		carried.Sprite.QueueInterval( 30 );
		carried.Sprite.ApplyPending( carried.Type, 0, null );
		Assert.AreEqual( (0, 0), (carried.Sprite.PendingTemplate, carried.Sprite.PendingInterval) );
		Assert.IsNull( carried.Sprite.Source );
		// 0 asks for no change
		member.Sprite.QueueInterval( 0 );
		member.Sprite.ApplyPending( member.Type, 0, null );
		Assert.AreEqual( 148, member.Sprite.Source.Interval );
		member.Sprite.QueueInterval( 9999 );
		Assert.AreEqual( 250, member.Sprite.PendingInterval, "the interval is clamped to 250 (0x004D41D0)" );
	}

	[TestMethod]
	public void CreatingAndDeletingASourceLeaveThePendingWordsAlone()
	{
		var member = Member();
		member.Sprite.QueueTemplate( StaffSpriteEvents.Sweep );
		member.Sprite.QueueInterval( 77 );
		member.Sprite.Create( member.Type, 0, StaffSpriteEvents.Stand, 0, null );
		Assert.AreEqual( (StaffSpriteEvents.Sweep, 77), (member.Sprite.PendingTemplate, member.Sprite.PendingInterval) );
		member.Sprite.Delete();
		Assert.IsNull( member.Sprite.Source );
		Assert.AreEqual( (StaffSpriteEvents.Sweep, 77), (member.Sprite.PendingTemplate, member.Sprite.PendingInterval) );
	}

	[TestMethod]
	public void TheWalkStepRequestsTheWalkRunOrHoldUnlessTheSourceRestartsThere()
	{
		var handyman = Member();
		// a person without a source compares false: the request is queued
		StaffSpriteEvents.Walking( handyman );
		Assert.AreEqual( StaffSpriteEvents.Walk, handyman.Sprite.PendingTemplate );
		// a source made afterwards finds the request still waiting and takes it
		handyman.Sprite.Create( handyman.Type, 0, StaffSpriteEvents.Stand, 0, null );
		Assert.AreEqual( StaffSpriteEvents.Walk, handyman.Sprite.PendingTemplate );
		handyman.Sprite.ApplyPending( handyman.Type, 0, null );
		Assert.IsTrue( handyman.Sprite.RestartsAt( StaffSpriteEvents.Walk ) );
		StaffSpriteEvents.Walking( handyman );
		Assert.AreEqual( 0, handyman.Sprite.PendingTemplate, "the restart PC is already the walk's start" );
		// a stand source is asked for the walk
		var standing = Member();
		standing.Sprite.Create( standing.Type, 0, StaffSpriteEvents.Stand, 0, null );
		StaffSpriteEvents.Walking( standing );
		Assert.AreEqual( StaffSpriteEvents.Walk, standing.Sprite.PendingTemplate );
		// the comparison reads the restart PC only: a source that has stepped on in its loop still restarts at the walk
		for ( var now = 100; now < 2000; now += 100 )
			handyman.Sprite.Tick( now );
		StaffSpriteEvents.Walking( handyman );
		Assert.AreEqual( 0, handyman.Sprite.PendingTemplate );
		// a speed bonus above 0 runs, but only a guard (the thing type 7; a guest is the other type that runs)
		handyman.SpeedBonus = 25;
		StaffSpriteEvents.Walking( handyman );
		Assert.AreEqual( 0, handyman.Sprite.PendingTemplate, "a handyman walks whatever his bonus is: only the types 1 (guest) and 7 (guard) run" );
		var guard = Member( StaffType.Guard );
		guard.Sprite.Create( guard.Type, 0, StaffSpriteEvents.Stand, 0, null );
		guard.SpeedBonus = 25;
		StaffSpriteEvents.Walking( guard );
		Assert.AreEqual( StaffSpriteEvents.Run, guard.Sprite.PendingTemplate );
		guard.Sprite.ApplyPending( guard.Type, 0, null );
		StaffSpriteEvents.Walking( guard );
		Assert.AreEqual( 0, guard.Sprite.PendingTemplate );
		// the bonus word is read as an unsigned 16-bit number
		guard.SpeedBonus = 0x10000;
		guard.Job = StaffJob.Escorting;
		StaffSpriteEvents.Walking( guard );
		Assert.AreEqual( StaffSpriteEvents.Hold, guard.Sprite.PendingTemplate, "an escorting guard without a bonus holds (the program 0x74EF90, template 0x17)" );
		guard.SpeedBonus = 25;
		guard.Sprite.ApplyPending( guard.Type, 0, null );
		StaffSpriteEvents.Walking( guard );
		Assert.AreEqual( StaffSpriteEvents.Run, guard.Sprite.PendingTemplate, "the run wins while the bonus is set" );
		guard.SpeedBonus = 0;
		guard.Job = StaffJob.Patrol;
		guard.Sprite.ApplyPending( guard.Type, 0, null );
		StaffSpriteEvents.Walking( guard );
		Assert.AreEqual( StaffSpriteEvents.Walk, guard.Sprite.PendingTemplate );
	}

	[TestMethod]
	public void TheStateSettersQueueTheTemplatesOfTheTableAndTheGuardsQueueNone()
	{
		(StaffType Type, StaffJob Job, int Template)[] table =
		{
			// 0x005054D0: 0 stand, 1 and 4 the patrol walk, 2 the walk, 5 the strike
			(StaffType.Handyman, StaffJob.Idle, 3), (StaffType.Mechanic, StaffJob.Idle, 3), (StaffType.Entertainer, StaffJob.Idle, 3), (StaffType.Guard, StaffJob.Idle, 3), (StaffType.Researcher, StaffJob.Idle, 3),
			(StaffType.Handyman, StaffJob.Patrol, 9), (StaffType.Guard, StaffJob.Patrol, 9), (StaffType.Researcher, StaffJob.Patrol, 9),
			(StaffType.Handyman, StaffJob.ToRestArea, 1), (StaffType.Mechanic, StaffJob.ToRestArea, 1),
			(StaffType.Handyman, StaffJob.ToStrike, 9), (StaffType.Entertainer, StaffJob.ToStrike, 9),
			(StaffType.Handyman, StaffJob.Striking, 0x13), (StaffType.Guard, StaffJob.Striking, 0x13),
			// the handyman's 0x004D7330
			(StaffType.Handyman, StaffJob.ToLitter, 9), (StaffType.Handyman, StaffJob.ToToilet, 9), (StaffType.Handyman, StaffJob.Sweeping, 0x11), (StaffType.Handyman, StaffJob.Cleaning, 0x11),
			// the mechanic's 0x004DA370
			(StaffType.Mechanic, StaffJob.ToRide, 9), (StaffType.Mechanic, StaffJob.Repairing, 0x12),
			// the researcher's 0x00502C20
			(StaffType.Researcher, StaffJob.Researching, 10),
			// the guard's 0x004D65D0 queues nothing for 16, 18 and 19; the entertainer's performance is queued by its search
			(StaffType.Guard, StaffJob.Chasing, 0), (StaffType.Guard, StaffJob.Escorting, 0), (StaffType.Guard, StaffJob.ReturningFromEscort, 0), (StaffType.Entertainer, StaffJob.Performing, 0),
			// the carried and the resting member have no source to queue on
			(StaffType.Handyman, StaffJob.Resting, 0), (StaffType.Handyman, StaffJob.PickedUp, 0)
		};
		var staff = Staff();
		foreach ( var (type, job, template) in table )
		{
			var member = Member( type );
			member.Sprite.Create( type, 0, StaffSpriteEvents.Stand, 0, null );
			StaffSpriteEvents.EnterJob( staff, member, job, StaffJob.Idle );
			Assert.AreEqual( template, member.Sprite.PendingTemplate, $"{type} entering {job}" );
			Assert.AreEqual( job is StaffJob.Resting or StaffJob.PickedUp, member.Sprite.Source == null, $"{type} entering {job}: states 3 and 7 delete the source" );
		}
	}

	[TestMethod]
	public void TheRestAndTheDropMakeAStandSourceAndTheDropQueuesTheStandWhereAConstructorRunsItsSearchInstead()
	{
		var staff = Staff( 2000 );
		var member = Member();
		// the pick-up (state 7) and the rest (state 3) delete the source
		member.Sprite.Create( member.Type, 0, StaffSpriteEvents.Stand, 0, null );
		StaffSpriteEvents.PickUp( member );
		Assert.IsNull( member.Sprite.Source );
		// the drop (0x00505EA0): a fresh stand source at the clock sample, then state 0 queues the stand
		StaffSpriteEvents.Dropped( staff, member, constructed: false );
		Assert.AreEqual( 2062, member.Sprite.Source!.Due );
		Assert.AreEqual( new StaffSpritePc( StaffSpriteProgram.Stand, 0 ), member.Sprite.Source.RestartPc );
		Assert.AreEqual( StaffSpriteEvents.Stand, member.Sprite.PendingTemplate );
		// a constructor makes the source and runs the role's search (which queues its own template): nothing is queued here
		var hired = Member();
		StaffSpriteEvents.Dropped( staff, hired, constructed: true );
		Assert.IsNotNull( hired.Sprite.Source );
		Assert.AreEqual( 0, hired.Sprite.PendingTemplate );
		// the rest's end (0x00506D10) makes the stand source before state 0
		var resting = Member();
		StaffSpriteEvents.EnterJob( staff, resting, StaffJob.Resting, StaffJob.ToRestArea );
		Assert.IsNull( resting.Sprite.Source );
		StaffSpriteEvents.EnterJob( staff, resting, StaffJob.Idle, StaffJob.Resting );
		Assert.IsNotNull( resting.Sprite.Source );
		Assert.AreEqual( StaffSpriteEvents.Stand, resting.Sprite.PendingTemplate );
		// but another state entry does not make a source out of nothing
		var other = Member();
		StaffSpriteEvents.EnterJob( staff, other, StaffJob.Idle, StaffJob.Patrol );
		Assert.IsNull( other.Sprite.Source );
	}

	[TestMethod]
	public void TheGuardsCatchAndReleaseAreExplicitEventsAndEscortingAloneRequestsNoCatch()
	{
		var staff = Staff();
		var guard = Member( StaffType.Guard );
		guard.Sprite.Create( guard.Type, 0, StaffSpriteEvents.Stand, 0, null );
		StaffSpriteEvents.EnterJob( staff, guard, StaffJob.Escorting, StaffJob.Chasing );
		Assert.AreEqual( 0, guard.Sprite.PendingTemplate );
		StaffSpriteEvents.GuardCaught( guard );
		Assert.AreEqual( 0x16, guard.Sprite.PendingTemplate );
		guard.Sprite.ApplyPending( guard.Type, 0, null );
		Assert.AreEqual( new StaffSpritePc( StaffSpriteProgram.Catch, 0 ), guard.Sprite.Source!.RestartPc );
		StaffSpriteEvents.GuardReleased( guard );
		Assert.AreEqual( 0x18, guard.Sprite.PendingTemplate );
		guard.Sprite.ApplyPending( guard.Type, 0, null );
		Assert.AreEqual( new StaffSpritePc( StaffSpriteProgram.Template24, 0 ), guard.Sprite.Source.RestartPc );
	}

	[TestMethod]
	public void APerformanceTemplateIsThirteenOrThirteenPlusTheDrawModOneLessThanTheBanksEntries()
	{
		var staff = Staff();
		var entertainer = Member( StaffType.Entertainer );
		int Queued( int entries, uint draw )
		{
			var animations = Bank( (4, 8) );
			// a bank of one entry is the shipped case; more entries are set up through the builder's performance table
			staff.AttachSpriteBanks( new Banks( entries == 1 ? animations : EntriesBank( entries ) ) );
			StaffSpriteEvents.Performance( staff, entertainer, draw );
			return entertainer.Sprite.PendingTemplate;
		}
		Assert.AreEqual( 13, Queued( 1, 123456789 ), "a single entry: always 13, whatever the draw" );
		Assert.IsTrue( Enumerable.Range( 0, 9 ).Select( draw => Queued( 4, (uint)draw ) ).Distinct().OrderBy( value => value ).SequenceEqual( new[] { 13, 14, 15 } ), "four entries: 13 + (draw mod 3), the fourth is never played" );
		Assert.AreEqual( 14, Queued( 4, 4 ) );
		Assert.AreEqual( 15, Queued( 4, 5 ) );
		// without a bank (a headless run) the template is the shipped one
		staff.AttachSpriteBanks( null );
		StaffSpriteEvents.Performance( staff, entertainer, 7 );
		Assert.AreEqual( 13, entertainer.Sprite.PendingTemplate );
	}

	private static SpriteAnimationFile EntriesBank( int entries )
	{
		var builder = new SpriteSetBuilder( "SPR_ZZ" ) { FullDetail = false };
		foreach ( var slot in new[] { 4, 10, 3, 11 } )
			builder.AddAnimation( slot, new[] { (IReadOnlyList<SpriteRgbaFrame>)Enumerable.Range( 0, 8 ).Select( _ => new SpriteRgbaFrame( 2, 2, new byte[16], 0, 0 ) ).ToArray() } );
		foreach ( var (entry, slot) in new[] { 4, 10, 3, 11 }.Take( entries ).Select( ( slot, entry ) => (entry, slot) ) )
			builder.SetPerformance( entry, 0, slot );
		return new SpriteAnimationFile( new MemoryStream( builder.Build().Files[".ESP"] ) );
	}

	[TestMethod]
	public void ApplyingAPerformanceTakesTheSlotAndFrameCountFromTheBankButNotTheEntrysIntroAndHold()
	{
		// the bank lists intro 2 and hold 40 for its entry (Hallow SPR_DR's bytes); 0x00475B80 reads them into locals and writes only the slot and the frame count
		var animations = Bank( (4, 8), (0, 1) );
		var banks = new Banks( animations );
		var member = Member( StaffType.Entertainer );
		member.Sprite.Create( member.Type, 0, StaffSpriteEvents.Stand, 0, banks );
		member.Sprite.QueueTemplate( StaffSpriteState.FirstPerformanceTemplate );
		member.Sprite.ApplyPending( member.Type, 0, banks );
		var source = member.Sprite.Source!;
		Assert.AreEqual( 4u, source[StaffSpriteRegister.Slot] );
		Assert.AreEqual( 8u, source[StaffSpriteRegister.FrameCount] );
		Assert.AreEqual( (0u, 0u), (source[StaffSpriteRegister.Intro], source[StaffSpriteRegister.Hold]), "only a sprite created with a performance entry stores them" );
		Assert.AreEqual( new StaffSpritePc( StaffSpriteProgram.Performance0, 0 ), source.RestartPc );
		var frames = new List<int>();
		for ( var now = 100; frames.Count < 17; now += 100 )
			if ( member.Sprite.Tick( now ).Status == StaffSpriteTickStatus.Yielded )
				frames.Add( member.Sprite.Output!.Value.Frame );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 17 ).Select( step => step % 8 ).ToArray(), frames, "the slot's frames, looping" );
	}

	[TestMethod]
	public void ASnapshotRestoresTheWrapperAndTheSourceExactlyAndAnInconsistentOneIsRefused()
	{
		var member = Member( StaffType.Guard );
		member.Sprite.Create( member.Type, 1, StaffSpriteEvents.Stand, 500, null );
		member.Sprite.QueueTemplate( StaffSpriteEvents.Run );
		member.Sprite.QueueInterval( 31 );
		member.Sprite.ApplyPending( member.Type, 1, null );
		member.Sprite.QueueTemplate( StaffSpriteEvents.Catch );
		for ( var now = 600; now < 1500; now += 90 )
			member.Sprite.Tick( now );
		var snapshot = member.Sprite.CaptureSnapshot();
		var restored = Member( StaffType.Guard );
		restored.Sprite.RestoreSnapshot( snapshot );
		Assert.AreEqual( snapshot, restored.Sprite.CaptureSnapshot() );
		Assert.AreEqual( member.Sprite.Output, restored.Sprite.Output );
		for ( var now = 1500; now < 3000; now += 45 )
			Assert.AreEqual( member.Sprite.Tick( now ), restored.Sprite.Tick( now ) );
		Assert.AreEqual( member.Sprite.CaptureSnapshot().Source!.Registers.ToArray().Length, 19 );
		// a source-less wrapper is a snapshot too
		var carried = Member();
		carried.Sprite.QueueTemplate( 3 );
		var bare = carried.Sprite.CaptureSnapshot();
		Assert.IsNull( bare.Source );
		Member().Sprite.RestoreSnapshot( bare );
		var before = restored.Sprite.CaptureSnapshot();
		foreach ( var broken in new[]
		{
			snapshot with { PendingTemplate = 99 },
			snapshot with { PendingInterval = 251 },
			snapshot with { PendingInterval = -1 },
			snapshot with { Source = snapshot.Source! with { Interval = 0 } },
			snapshot with { Source = snapshot.Source! with { CurrentPc = 0xFFFF0000u } },
			snapshot with { Source = snapshot.Source! with { RunState = 9 } },
			snapshot with { Source = snapshot.Source! with { StackPointer = 21 } },
			snapshot with { Source = snapshot.Source! with { Stack = new uint[3] } },
			snapshot with { Source = snapshot.Source! with { Registers = new uint[5] } },
			snapshot with { Source = snapshot.Source! with { Registers = snapshot.Source.Registers.Select( ( word, index ) => index == (int)StaffSpriteRegister.Slot ? 16u : word ).ToArray() } },
			snapshot with { Source = snapshot.Source! with { OutputValid = 2 } }
		} )
		{
			Assert.ThrowsException<InvalidDataException>( () => restored.Sprite.RestoreSnapshot( broken ) );
			Assert.AreEqual( before, restored.Sprite.CaptureSnapshot(), "a refused snapshot changes nothing" );
		}
	}

	[TestMethod]
	public void AMemberThatShouldShowASpriteAndHasNoneGetsAStandSourceAndItsStatesTemplateAtThePreUpdate()
	{
		var staff = Staff( 4000 );
		foreach ( var (type, job, template) in new[]
		{
			(StaffType.Handyman, StaffJob.Sweeping, 0x11), (StaffType.Mechanic, StaffJob.Repairing, 0x12), (StaffType.Guard, StaffJob.Chasing, 2), (StaffType.Guard, StaffJob.Escorting, 0x17),
			(StaffType.Entertainer, StaffJob.Performing, 13), (StaffType.Researcher, StaffJob.Researching, 10), (StaffType.Handyman, StaffJob.Idle, 3)
		} )
		{
			var member = Member( type );
			member.Job = job;
			StaffSpriteEvents.PreUpdate( staff, member );
			Assert.IsNotNull( member.Sprite.Source, $"{type} {job}" );
			Assert.AreEqual( 4062, member.Sprite.Source.Due );
			var expected = template == 13 ? StaffSpriteProgram.Performance0 : (StaffSpriteProgram)template;
			Assert.AreEqual( expected, member.Sprite.Source.RestartPc.Program, $"{type} {job}: the state's program is applied at once" );
		}
		// a carried or resting member stays without one
		var carried = Member();
		carried.Job = StaffJob.PickedUp;
		carried.State = StaffState.PickedUp;
		StaffSpriteEvents.PreUpdate( staff, carried );
		Assert.IsNull( carried.Sprite.Source );
	}
}
