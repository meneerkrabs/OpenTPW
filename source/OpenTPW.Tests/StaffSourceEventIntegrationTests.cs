using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Real roster, placement and role-handler events over synthetic CPU metadata; no renderer or original assets.</summary>
[TestClass]
public class StaffSourceEventIntegrationTests
{
	private const int StaffRoomInfo = 1411;

	private sealed class Guests : IGuardGuests
	{
		public (int X, int Y) Cell = (2, 5);
		public bool Audience;
		public bool Catchable = true;
		public (int X, int Y)[]? ArrivalLane;
		public int Holds, Releases;
		public (int X, int Y)? CellOf( int guestId ) => guestId == 1 ? Cell : null;
		public bool IsCatchable( int guestId ) => Catchable;
		public void Hold( int guestId ) => Holds++;
		public bool Release( int guestId, (int X, int Y) cell, int? side = null ) { Releases++; return true; }
		public bool AnyGuestWithin( (int X, int Y) cell, int radius ) => Audience;
		public (int X, int Y)[]? Lane( int side ) => ArrivalLane;
	}

	private sealed class Fixture : IDisposable
	{
		public readonly GuestPathGrid Grid = new( 20, 20 );
		public readonly ParkEconomy Park;
		public readonly StaffWorldSimulation World;
		public readonly StaffPlacement Placement;
		public readonly StaffSpritePresentation Presentation;
		public readonly Guests Guests = new();
		public readonly List<RestSite> Rests = new();
		public readonly List<ToiletSite> Toilets = new();
		public readonly Dictionary<int, (int X, int Y)> Access = new();

		public Fixture( int performanceCount = 4, bool metadata = true, bool ready = true )
		{
			var room = new EconomyObjectInfo( StaffRoomInfo, "Synthetic staff room", ParkObjectKind.Feature, ResearchCategory.Feature, 0,
				new[] { new UpgradeLevelInfo( 0, 100, 0, 0, 0, new[] { 50, 30, 20, 10 } ) }, null, null, null, 0, 0, 0, 0, 0, 0, "synthetic/staff.wad", ChillsYouOut: true );
			Park = new ParkEconomy( EconomyTestData.Settings( 5_000_000 ), new EconomyObjectCatalog( EconomyTestData.Catalog().Objects.Append( room ) ), ParkGameMode.FullSimulation, 7 );
			Park.OpenPark();
			for ( var x = 0; x < 18; x++ ) Grid.SetPath( x, 5, true );
			Park.Litter.Cells = Grid.Cells;
			Presentation = new StaffSpritePresentation( Catalog( performanceCount, metadata ), () => Park.Staff.Members, ready: ready );
			Presentation.AttachRoster( Park.Staff );
			World = new StaffWorldSimulation( Grid, () => Park.Staff.Members, id => Access.TryGetValue( id, out var access ) ? access : null, (0, 5) )
			{
				Guests = Guests, RestAreas = () => Rests, Toilets = () => Toilets
			};
			Park.StaffWorld = World;
			Placement = new StaffPlacement( () => Park, () => Grid.Cells, static ( _, _ ) => false );
		}

		public StaffMember Hire( StaffType type, int x = 2, int y = 5 )
		{
			var member = EconomyTestData.HireBest( Park, type );
			Stand( member, x, y );
			World.Synchronize();
			return member;
		}

		public static void Stand( StaffMember member, int x, int y )
		{
			member.CellX = x + 0.5f; member.CellY = y + 0.5f;
			member.Navigation = new GuestNavigation { X = (x << 16) + 32768, Y = (y << 16) + 32768 };
			member.Navigation.SetDestination( member.Navigation.X, member.Navigation.Y, [] );
			member.Job = StaffJob.Idle; member.State = StaffState.Idle; member.IdleStamp = 100_000;
			member.Happiness = 50; member.Energy = 100;
		}

		public void WalkTo( StaffMember member, int x = 15, int y = 5 )
		{
			var (targetX, targetY) = GuestNavigation.Centre( (x, y) );
			Assert.IsTrue( member.Navigation!.Navigate( Grid.Cells, targetX, targetY ) );
		}

		public void Update( StaffMember member, long turn = 1000 ) => World.UpdateStaffMember( Park, member, turn );
		public void Dispose() => Presentation.Dispose();
	}

	private static StaffSpriteMetadataCatalog Catalog( int count, bool available ) => new(
		Enumerable.Range( 4, 5 ).Select( kind => available
			? new StaffSpriteBankMetadata( (uint)kind, 0, kind - 4, $"synthetic-{kind}", 8, 8,
				Enumerable.Range( 0, SpriteAnimationFile.SlotCount ).Select( slot => new SpriteAnimation( slot, 0, 8, 1 ) ),
				Enumerable.Range( 0, 8 ).Select( _ => new GuestAtlasFrame( 0, 0, 1, 1, 0, 0 ) ),
				Enumerable.Range( 0, 4 ).Select( index => new SpritePerformance( index, 10 + index, 2, 7 ) ), count )
			: StaffSpriteBankMetadata.Unavailable( (uint)kind, 0, $"unavailable-{kind}" ) ) );

	private static StaffSpriteVmState Source( StaffMember member ) => member.BodySprite!.ReadSourceState()!;
	private static void AssertSameState( StaffSpriteVmState expected, StaffSpriteVmState actual )
	{
		Assert.AreEqual( (expected.OpaqueWord, expected.Handle, expected.CurrentPc, expected.RestartPc, expected.LastPc, expected.ProgramTable),
			(actual.OpaqueWord, actual.Handle, actual.CurrentPc, actual.RestartPc, actual.LastPc, actual.ProgramTable) );
		Assert.AreEqual( (expected.RunState, expected.StackPointer, expected.IfDepth, expected.CallDepth, expected.LoopDepth, expected.Due, expected.Interval, expected.OutputValid),
			(actual.RunState, actual.StackPointer, actual.IfDepth, actual.CallDepth, actual.LoopDepth, actual.Due, actual.Interval, actual.OutputValid) );
		CollectionAssert.AreEqual( expected.Stack, actual.Stack );
		CollectionAssert.AreEqual( expected.Registers, actual.Registers );
		CollectionAssert.AreEqual( expected.DrawCache, actual.DrawCache );
	}

	[TestMethod]
	public void PendingHireAllocatesNothingAndValidPlacementCreatesExactlyOneFreshStand()
	{
		using var f = new Fixture();
		var candidate = f.Park.Staff.Candidates.First( candidate => candidate.Type == StaffType.Researcher );
		Assert.IsTrue( f.Placement.BeginHire( candidate.Id ) );
		f.World.Synchronize();
		Assert.AreEqual( 0, f.Presentation.Registry.Count );
		Assert.AreEqual( 0, f.Park.Staff.Members.Count );
		Assert.AreEqual( StaffClickResult.Placed, f.Placement.Click( 7, 5 ) );
		var member = f.Placement.LastPlaced!;
		var ticket = member.BodySprite!.SourceTicket;
		var state = Source( member );
		Assert.AreEqual( 1, f.Presentation.Registry.Count );
		Assert.AreEqual( new StaffSpritePc( StaffSpriteProgram.Stand, 0 ), state.CurrentPc );
		Assert.AreEqual( (62, 62, 0u), (state.Due, state.Interval, state.OutputValid) );
		Assert.AreEqual( (0u, 0u, 0u), (state[StaffSpriteRegister.Heading], state[StaffSpriteRegister.Flags], state[StaffSpriteRegister.X]) );
		Assert.AreEqual( 3, member.BodySprite.PendingTemplate );
		f.World.Synchronize();
		Assert.AreEqual( ticket, member.BodySprite.SourceTicket );
		AssertSameState( state, Source( member ) );
	}

	[TestMethod]
	public void DirectHireAttachesOnlyUntilFinalPositionAndNeverRecreatesALostSource()
	{
		using var f = new Fixture();
		var member = EconomyTestData.HireBest( f.Park, StaffType.Researcher );
		Assert.IsNotNull( member.BodySprite );
		Assert.IsNull( member.BodySprite.SourceTicket );
		f.World.Synchronize();
		Assert.IsNotNull( member.BodySprite.SourceTicket );
		member.BodySprite.DeleteSource();
		member.IdleStamp = 100_000;
		for ( var i = 0; i < 3; i++ ) { f.World.Synchronize(); f.Update( member ); }
		Assert.IsNull( member.BodySprite.SourceTicket, "ordinary synchronization and role updates cannot resurrect a lost source" );
	}

	[TestMethod]
	public void PickupReturnsPreviousMemberFirstAndFailedPickupDoesNotTouchEitherSource()
	{
		using var f = new Fixture();
		var first = f.Hire( StaffType.Researcher );
		var second = f.Hire( StaffType.Guard, 8 );
		var firstTicket = first.BodySprite!.SourceTicket;
		var secondTicket = second.BodySprite!.SourceTicket;
		Assert.IsTrue( f.Placement.PickUp( first ) );
		var firstReturnedBeforeSecondDeletion = false;
		f.Presentation.Registry.Removed += ( ticket, _ ) =>
		{
			if ( ticket == secondTicket ) firstReturnedBeforeSecondDeletion = first.BodySprite.SourceTicket != null;
		};
		Assert.IsTrue( f.Placement.PickUp( second ) );
		Assert.IsTrue( firstReturnedBeforeSecondDeletion );
		Assert.AreNotEqual( firstTicket, first.BodySprite.SourceTicket );
		Assert.IsNull( second.BodySprite.SourceTicket );
		first.Job = StaffJob.Striking; first.State = StaffState.OnStrike;
		var returned = first.BodySprite.SourceTicket;
		Assert.IsFalse( f.Placement.PickUp( first ) );
		Assert.AreEqual( returned, first.BodySprite.SourceTicket );
		Assert.AreSame( second, f.Placement.Carried );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	public void DropCancelAndSaveWhileCarryingEachCreateOneNewClockAndNoMovementInterval( int operation )
	{
		using var f = new Fixture();
		var member = f.Hire( StaffType.Researcher );
		var body = member.BodySprite!;
		var before = body.SourceTicket;
		body.RequestTemplate( 10 ); body.RequestInterval( 17 ); body.ApplyPending();
		f.Presentation.Registry.Schedule( 100 );
		Assert.IsTrue( f.Placement.PickUp( member ) );
		f.Presentation.SetHostTick( 80 );
		if ( operation == 0 ) Assert.AreEqual( StaffClickResult.Dropped, f.Placement.Click( 12, 5 ) );
		else if ( operation == 1 ) f.Placement.Cancel();
		else f.Placement.PrepareForSave();
		Assert.AreNotEqual( before, body.SourceTicket );
		Assert.AreEqual( 1, f.Presentation.Registry.Count );
		var returned = body.SourceTicket;
		var state = Source( member );
		Assert.AreEqual( new StaffSpritePc( StaffSpriteProgram.Stand, 0 ), state.CurrentPc );
		Assert.AreEqual( (unchecked( f.Presentation.LastSample + 62 ), 62, 0u), (state.Due, state.Interval, state.OutputValid) );
		Assert.AreEqual( 0, body.PendingInterval, "the drop is a teleport, not a WALK event" );
		Assert.AreEqual( 3, body.PendingTemplate );
		f.Placement.Cancel(); f.World.Synchronize();
		Assert.AreEqual( returned, body.SourceTicket );
	}

	[TestMethod]
	public void PickedUpPreUpdateConsumesPendingFieldsWithoutReplayOrDuplicateRetirement()
	{
		using var f = new Fixture();
		var member = f.Hire( StaffType.Researcher );
		var body = member.BodySprite!;
		var removals = 0;
		f.Presentation.Registry.Removed += ( _, _ ) => removals++;
		body.RequestTemplate( 9 ); body.RequestInterval( 30 );
		Assert.IsTrue( f.Placement.PickUp( member ) );
		Assert.AreEqual( (9, 30), (body.PendingTemplate, body.PendingInterval), "deletion preserves pending requests" );
		f.Update( member );
		Assert.AreEqual( (0, 0), (body.PendingTemplate, body.PendingInterval) );
		Assert.AreEqual( 1, removals );
		Assert.IsNull( body.SourceTicket );
		f.Placement.Cancel();
		Assert.AreEqual( 62, Source( member ).Interval );
		Assert.AreEqual( StaffSpriteProgram.Stand, Source( member ).RestartPc.Program );
	}

	[TestMethod]
	public void UnpositionedPickedUpPreUpdateAlsoConsumesRequests()
	{
		using var f = new Fixture();
		var member = EconomyTestData.HireBest( f.Park, StaffType.Guard );
		member.State = StaffState.PickedUp;
		member.BodySprite!.RequestTemplate( 22 ); member.BodySprite.RequestInterval( 29 );
		f.Update( member );
		Assert.AreEqual( (0, 0), (member.BodySprite.PendingTemplate, member.BodySprite.PendingInterval) );
		Assert.IsNull( member.BodySprite.SourceTicket );
	}

	[TestMethod]
	public void ImportedPickedUpReconciliationCreatesOnceAfterFinalPosition()
	{
		using var f = new Fixture( ready: false );
		var member = f.Hire( StaffType.Researcher );
		member.Job = StaffJob.PickedUp; member.State = StaffState.PickedUp;
		f.Placement.Reconcile();
		f.Presentation.Reconstruct( 900 );
		var ticket = member.BodySprite!.SourceTicket;
		Assert.IsNotNull( ticket );
		f.Update( member ); f.World.Synchronize();
		Assert.AreEqual( ticket, member.BodySprite.SourceTicket );
		Assert.IsTrue( member.BodySprite.Reconstructed );
	}

	[TestMethod]
	public void CommonPreUpdateAppliesLastTemplateThenIntervalWithoutRebasingAndDefersHandlerRequests()
	{
		using var f = new Fixture();
		var member = f.Hire( StaffType.Researcher );
		var body = member.BodySprite!;
		body.RequestTemplate( 9 ); body.RequestTemplate( 1 ); body.RequestInterval( 30 );
		f.Update( member );
		Assert.AreEqual( (StaffSpriteProgram.Walk, 30, 62, 0u), (Source( member ).RestartPc.Program, Source( member ).Interval, Source( member ).Due, Source( member ).OutputValid) );
		Assert.AreEqual( (0, 0), (body.PendingTemplate, body.PendingInterval) );
		member.Job = StaffJob.Patrol; member.State = StaffState.Patrolling;
		body.RequestTemplate( 9 ); f.WalkTo( member );
		f.Update( member );
		Assert.AreEqual( StaffSpriteProgram.Patrol, Source( member ).RestartPc.Program, "this update applies the earlier request" );
		Assert.AreEqual( 1, body.PendingTemplate, "the actual moving branch's WALK waits for the next pre-update" );
	}

	[TestMethod]
	public void RepeatedSameIdleSetterRequestsStandAgain()
	{
		using var f = new Fixture();
		var guard = f.Hire( StaffType.Guard );
		guard.IdleStamp = 0;
		f.Update( guard, 1000 );
		Assert.AreEqual( StaffJob.Idle, guard.Job );
		Assert.AreEqual( 3, guard.BodySprite!.PendingTemplate );
		f.Update( guard, 1004 );
		Assert.AreEqual( 3, guard.BodySprite.PendingTemplate, "same-job setter after consumption must request again" );
	}

	[DataTestMethod]
	[DataRow( StaffType.Guard, 0, 1 )]
	[DataRow( StaffType.Guard, 1, 2 )]
	[DataRow( StaffType.Guard, 25, 2 )]
	[DataRow( StaffType.Guard, -1, 2 )]
	[DataRow( StaffType.Handyman, 25, 1 )]
	[DataRow( StaffType.Mechanic, 25, 1 )]
	public void ActualMovingBranchUsesGuardOnlyUnsignedBonusAndActualDisplacement( StaffType type, int bonus, int request )
	{
		using var f = new Fixture();
		var member = f.Hire( type );
		member.Job = StaffJob.Patrol; member.State = StaffState.Patrolling; member.SpeedBonus = bonus;
		f.WalkTo( member );
		var (beforeX, beforeY) = (member.Navigation!.X, member.Navigation.Y);
		f.Update( member );
		Assert.AreEqual( request, member.BodySprite!.PendingTemplate );
		Assert.AreEqual( StaffSpriteMotionMath.IntervalForStep( beforeX, beforeY, member.Navigation.X, member.Navigation.Y, request == 2 ), member.BodySprite.PendingInterval );
		Assert.AreEqual( StaffSpriteProgram.Stand, Source( member ).RestartPc.Program );
	}

	[TestMethod]
	public void MovingEscortRequestsHoldAndReturningGuardRequestsWalk()
	{
		using var f = new Fixture();
		var member = f.Hire( StaffType.Guard );
		member.Job = StaffJob.Escorting; member.State = StaffState.Working; member.TargetGuestId = 1;
		f.WalkTo( member ); f.Update( member );
		Assert.AreEqual( 23, member.BodySprite!.PendingTemplate );
		member.BodySprite.ApplyPending();
		member.Job = StaffJob.ReturningFromEscort;
		f.Update( member );
		Assert.AreEqual( 1, member.BodySprite.PendingTemplate );
	}

	[TestMethod]
	public void MovingComparesRestartEntryNotCurrentPcOrAnOlderPendingRequest()
	{
		using var f = new Fixture();
		var member = f.Hire( StaffType.Guard );
		member.Job = StaffJob.Patrol; member.State = StaffState.Patrolling;
		var body = member.BodySprite!;
		body.RequestTemplate( 1 ); body.ApplyPending();
		f.Presentation.Registry.Schedule( 63 );
		Assert.AreNotEqual( Source( member ).RestartPc, Source( member ).CurrentPc );
		f.WalkTo( member ); f.Update( member );
		Assert.AreEqual( 0, body.PendingTemplate, "current PC has advanced, but the restart entry already is WALK" );
		// This event occurs after pre-update: an older pending request cannot stand in for the current restart entry.
		body.RequestTemplate( 9 );
		body.Moving( 0, 0, 50, 0, fast: false, escort: false );
		Assert.AreEqual( 9, body.PendingTemplate );
	}

	[TestMethod]
	public void ZeroDeltaContinuationWritesAngleZeroButArrivedAndStuckDoNotWriteAngleOrInterval()
	{
		using var f = new Fixture();
		var member = f.Hire( StaffType.Guard );
		member.Job = StaffJob.Patrol; member.State = StaffState.Patrolling;
		member.OffGridLeg = [(10, 5, 128, 128)];
		f.Update( member );
		Assert.AreEqual( (0, 1, 0), (member.BodySprite!.PersonAngle, member.BodySprite.PendingTemplate, member.BodySprite.PendingInterval) );
		foreach ( var stuck in new[] { false, true } )
		{
			Fixture.Stand( member, 2, 5 );
			member.BodySprite.Moving( 0, 0, 1, 0, false, false ); member.BodySprite.ApplyPending();
			member.Job = StaffJob.ToRestArea; member.State = StaffState.Resting;
			if ( stuck ) Assert.IsFalse( member.Navigation!.Navigate( f.Grid.Cells, 10 << 16, 18 << 16 ) );
			f.Update( member );
			Assert.AreEqual( 1536, member.BodySprite.PersonAngle );
			Assert.AreEqual( 0, member.BodySprite.PendingInterval );
			Assert.AreEqual( 3, member.BodySprite.PendingTemplate, "only the failed-rest Idle setter requests" );
		}
	}

	[TestMethod]
	public void HandymanSearchArrivalAndCleaningCompletionEmitTheirActualRequests()
	{
		using var f = new Fixture();
		var member = f.Hire( StaffType.Handyman );
		member.IdleStamp = 0;
		f.Park.Litter.Drop( 3, 5, (LitterType)1 );
		f.Update( member, 1001 );
		Assert.AreEqual( StaffJob.ToLitter, member.Job );
		Assert.AreEqual( 9, member.BodySprite!.PendingTemplate );
		var navigation = member.Navigation!;
		(navigation.X, navigation.Y) = (navigation.TargetX, navigation.TargetY);
		navigation.SetDestination( navigation.X, navigation.Y, [] );
		f.Update( member, 1002 );
		Assert.AreEqual( StaffJob.Sweeping, member.Job );
		Assert.AreEqual( 17, member.BodySprite.PendingTemplate );
		Assert.AreEqual( 0, member.BodySprite.PendingInterval, "the arrival snap is not a moving event" );

		var toilet = f.Park.RegisterExisting( 1402 );
		Fixture.Stand( member, 2, 5 );
		member.Job = StaffJob.ToToilet; member.State = StaffState.Working; member.ToiletId = toilet.Id;
		f.Toilets.Add( new ToiletSite( toilet.Id, (2, 6), (2, 5) ) );
		f.Park.Claim( toilet, member );
		f.Update( member, 1100 );
		Assert.AreEqual( StaffJob.Cleaning, member.Job );
		Assert.AreEqual( 17, member.BodySprite.PendingTemplate );
		f.Update( member, 2000 );
		Assert.AreEqual( StaffJob.Idle, member.Job );
		Assert.AreEqual( 3, member.BodySprite.PendingTemplate, "clean completion and Idle overwrite the same pending Stand field" );
	}

	[TestMethod]
	public void MechanicArrivalRequestsRepairWithoutBusyTicksAndCountdownDoesNotRestart()
	{
		using var f = new Fixture();
		var ride = f.Park.RegisterExisting( 1100 );
		ride.IsBrokenDown = true; ride.Repair = 0;
		f.Access[ride.Id] = (2, 5);
		var member = f.Hire( StaffType.Mechanic );
		member.IdleStamp = 0;
		f.Update( member );
		Assert.AreEqual( StaffJob.ToRide, member.Job );
		Assert.AreEqual( 9, member.BodySprite!.PendingTemplate );
		var navigation = member.Navigation!;
		navigation.SetDestination( navigation.X, navigation.Y, [] );
		f.Update( member, 1001 );
		Assert.AreEqual( StaffJob.Repairing, member.Job );
		Assert.AreEqual( 0L, member.BusyTicks );
		Assert.AreEqual( 18, member.BodySprite.PendingTemplate );
		var timer = member.JobStamp;
		f.Update( member, 1002 );
		Assert.AreEqual( timer - 1, member.JobStamp );
		Assert.AreEqual( StaffSpriteProgram.Repair, Source( member ).RestartPc.Program );
		f.Presentation.Registry.Schedule( 63 );
		var state = Source( member );
		f.Update( member, 1003 );
		Assert.AreEqual( 0, member.BodySprite.PendingTemplate );
		AssertSameState( state, Source( member ) );
	}

	[TestMethod]
	public void ResearchEntryRequestsResearchButFailedWanderOnlyRestamps()
	{
		using var f = new Fixture();
		for ( var x = 0; x < 18; x++ ) if ( x != 2 ) f.Grid.SetPath( x, 5, false );
		var member = f.Hire( StaffType.Researcher );
		member.IdleStamp = 0;
		f.Update( member );
		Assert.AreEqual( StaffJob.Researching, member.Job );
		Assert.AreEqual( 10, member.BodySprite!.PendingTemplate );
		member.BodySprite.ApplyPending();
		f.Presentation.Registry.Schedule( 63 );
		var state = Source( member );
		f.Update( member, 2000 );
		Assert.AreEqual( 2000L, member.JobStamp );
		Assert.AreEqual( StaffJob.Researching, member.Job );
		Assert.AreEqual( 0, member.BodySprite.PendingTemplate );
		AssertSameState( state, Source( member ) );
	}

	private static uint PerformanceSeed()
	{
		for ( uint seed = 0; ; seed++ )
			if ( new WorldRng( seed ).NextWord() % 3 == 0 ) return seed;
	}

	[DataTestMethod]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 4 )]
	public void RepeatedPerformingEntriesUseExactlyTheExistingChoiceDrawEvenForCountOne( int count )
	{
		using var f = new Fixture( performanceCount: count );
		var member = f.Hire( StaffType.Entertainer );
		f.Guests.Audience = true;
		member.Job = StaffJob.Performing; member.State = StaffState.Working; member.JobStamp = 0;
		foreach ( var turn in new[] { 1000L, 2000L } )
		{
			var seed = PerformanceSeed();
			f.Park.World.Restore( seed );
			var replay = new WorldRng( seed );
			Assert.AreEqual( 0u, replay.NextWord() % 3 );
			var choice = replay.NextWord();
			var index = count == 1 ? 0 : (int)(choice % (uint)(count - 1));
			f.Update( member, turn );
			Assert.AreEqual( StaffJob.Performing, member.Job );
			Assert.AreEqual( turn, member.JobStamp );
			Assert.AreEqual( replay.State, f.Park.World.State );
			Assert.AreEqual( 13 + index, member.BodySprite!.PendingTemplate );
			f.Update( member, turn + 1 );
			Assert.AreEqual( (StaffSpriteProgram)(256 + index), Source( member ).RestartPc.Program );
			Assert.AreEqual( (uint)(10 + index), Source( member )[StaffSpriteRegister.Slot] );
			Assert.AreEqual( (0u, 0u), (Source( member )[StaffSpriteRegister.Intro], Source( member )[StaffSpriteRegister.Hold]), "ordinary performance requests do not initialize constructor-only parameters" );
		}
	}

	[DataTestMethod]
	[DataRow( 0, true, StaffSpriteDiagnostic.InvalidPerformance )]
	[DataRow( 5, true, StaffSpriteDiagnostic.InvalidPerformance )]
	[DataRow( 4, false, StaffSpriteDiagnostic.MetadataUnavailable )]
	public void InvalidPerformanceChoiceConsumesDrawAndRequestWithoutChangingOrReplayingVm( int count, bool available, StaffSpriteDiagnostic diagnostic )
	{
		using var f = new Fixture( count, available );
		var member = f.Hire( StaffType.Entertainer );
		member.Job = StaffJob.Performing; member.State = StaffState.Working; member.JobStamp = 0;
		f.Guests.Audience = true;
		var state = Source( member );
		var seed = PerformanceSeed(); f.Park.World.Restore( seed );
		var replay = new WorldRng( seed ); replay.NextWord(); replay.NextWord();
		f.Update( member ); f.Update( member, 1001 );
		Assert.AreEqual( replay.State, f.Park.World.State );
		Assert.AreEqual( 0, member.BodySprite!.PendingTemplate );
		Assert.AreEqual( diagnostic, member.BodySprite.Diagnostic );
		AssertSameState( state, Source( member ) );
		member.BodySprite.DeleteSource(); member.BodySprite.CreateSource();
		Assert.AreEqual( StaffSpriteProgram.Stand, Source( member ).RestartPc.Program );
		Assert.AreEqual( 0, member.BodySprite.PendingTemplate );
	}

	[DataTestMethod]
	[DataRow( false, 3, StaffJob.Idle )]
	[DataRow( true, 22, StaffJob.Escorting )]
	public void CatchRequestPrecedesRouteAttemptsAndFailedCatchReturnIsOverwrittenByIdle( bool route, int request, StaffJob job )
	{
		using var f = new Fixture();
		var member = f.Hire( StaffType.Guard );
		member.Job = StaffJob.Chasing; member.State = StaffState.Working; member.TargetGuestId = 1; member.JobStamp = 100;
		if ( route ) f.Guests.ArrivalLane = [(0, 0), (0, 1), (0, 2), (14, 5), (15, 5)];
		f.Update( member );
		Assert.AreEqual( 1, f.Guests.Holds );
		Assert.AreEqual( route ? 0 : 1, f.Guests.Releases );
		Assert.AreEqual( job, member.Job );
		Assert.AreEqual( request, member.BodySprite!.PendingTemplate );
		Assert.AreEqual( StaffSpriteProgram.Stand, Source( member ).RestartPc.Program );
		member.BodySprite.ApplyPending();
		Assert.AreEqual( (StaffSpriteProgram)request, Source( member ).RestartPc.Program );
	}

	[DataTestMethod]
	[DataRow( false, 3, StaffJob.Idle )]
	[DataRow( true, 24, StaffJob.ReturningFromEscort )]
	public void EscortCompletionRequestsReleaseBeforeSetDownAndReturnFailureCanOverwriteIt( bool route, int request, StaffJob job )
	{
		using var f = new Fixture();
		var member = f.Hire( StaffType.Guard );
		member.Job = StaffJob.Escorting; member.State = StaffState.Working; member.TargetGuestId = 1;
		if ( route ) f.Guests.ArrivalLane = [(0, 0), (0, 1), (0, 2), (14, 5), (15, 5)];
		var replay = new WorldRng( f.Park.World.State ); replay.NextWord();
		f.Update( member );
		Assert.AreEqual( 1, f.Guests.Releases );
		Assert.AreEqual( replay.State, f.Park.World.State, "release keeps its one existing side-choice draw" );
		Assert.AreEqual( job, member.Job );
		Assert.AreEqual( request, member.BodySprite!.PendingTemplate );
	}

	[TestMethod]
	public void RestWalkKeepsSourceActualRestDeletesAndRestEndConstructsBeforeSearch()
	{
		using var f = new Fixture();
		var room = f.Park.RegisterExisting( StaffRoomInfo );
		f.Rests.Add( new RestSite( room.Id, (8, 6), (8, 5) ) );
		var member = f.Hire( StaffType.Researcher );
		var original = member.BodySprite!.SourceTicket;
		member.Energy = 0; member.IdleStamp = 0;
		f.Update( member );
		Assert.AreEqual( StaffJob.ToRestArea, member.Job );
		Assert.AreEqual( StaffState.Resting, member.State );
		Assert.AreEqual( original, member.BodySprite.SourceTicket );
		Assert.AreEqual( 1, member.BodySprite.PendingTemplate );
		var navigation = member.Navigation!;
		(navigation.X, navigation.Y) = GuestNavigation.Centre( (8, 5) );
		navigation.SetDestination( navigation.X, navigation.Y, [] );
		f.Update( member, 1001 );
		Assert.AreEqual( StaffJob.Resting, member.Job );
		Assert.IsNull( member.BodySprite.SourceTicket );
		f.World.Synchronize();
		Assert.IsNull( member.BodySprite.SourceTicket );
		member.Energy = 100;
		f.Update( member, 1002 );
		Assert.IsNotNull( member.BodySprite.SourceTicket );
		Assert.AreNotEqual( original, member.BodySprite.SourceTicket );
		Assert.AreEqual( StaffSpriteProgram.Stand, Source( member ).RestartPc.Program, "search requests are pending after the fresh Stand construction" );
		Assert.AreEqual( 0u, Source( member ).OutputValid );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RemovingRestBuildingDistinguishesWalkingAndActualRestWithoutDoubleCreation( bool resting )
	{
		using var f = new Fixture();
		var room = f.Park.RegisterExisting( StaffRoomInfo );
		var member = f.Hire( StaffType.Researcher );
		member.Job = resting ? StaffJob.Resting : StaffJob.ToRestArea; member.State = StaffState.Resting; member.RestAreaId = room.Id;
		var old = member.BodySprite!.SourceTicket;
		if ( resting ) member.BodySprite.DeleteSource();
		f.Park.Sell( room.Id );
		Assert.AreEqual( StaffJob.Idle, member.Job );
		Assert.AreEqual( 3, member.BodySprite.PendingTemplate );
		Assert.IsNotNull( member.BodySprite.SourceTicket );
		if ( resting )
		{
			Assert.AreNotEqual( old, member.BodySprite.SourceTicket );
			Assert.AreEqual( old!.Value.Generation + 1, member.BodySprite.SourceTicket!.Value.Generation, "ObjectRemoved plus ReleaseJobsOn must create only once" );
		}
		else Assert.AreEqual( old, member.BodySprite.SourceTicket );
	}

	[DataTestMethod]
	[DataRow( StaffType.Mechanic, StaffJob.ToRide, 1100 )]
	[DataRow( StaffType.Mechanic, StaffJob.Repairing, 1100 )]
	[DataRow( StaffType.Handyman, StaffJob.ToToilet, 1402 )]
	[DataRow( StaffType.Handyman, StaffJob.Cleaning, 1402 )]
	public void EconomyDirectObjectReleaseQueuesIdleFromEveryWorkState( StaffType type, StaffJob job, int infoId )
	{
		using var f = new Fixture();
		var item = f.Park.RegisterExisting( infoId );
		var member = f.Hire( type );
		member.Job = job; member.State = StaffState.Working;
		if ( type == StaffType.Mechanic ) member.RideId = item.Id; else member.ToiletId = item.Id;
		f.Park.Sell( item.Id );
		Assert.AreEqual( StaffJob.Idle, member.Job );
		Assert.AreEqual( 3, member.BodySprite!.PendingTemplate );
	}

	[TestMethod]
	public void EconomyRestFallbackCreatesOnlyForActualRestEvenWithoutNavigation()
	{
		foreach ( var resting in new[] { false, true } )
		{
			using var f = new Fixture();
			var room = f.Park.RegisterExisting( StaffRoomInfo );
			var member = f.Hire( StaffType.Researcher );
			member.Navigation = null; member.Job = resting ? StaffJob.Resting : StaffJob.ToRestArea;
			member.State = StaffState.Resting; member.RestAreaId = room.Id;
			var old = member.BodySprite!.SourceTicket;
			if ( resting ) member.BodySprite.DeleteSource();
			f.Park.Sell( room.Id );
			Assert.AreEqual( StaffJob.Idle, member.Job );
			Assert.IsNotNull( member.BodySprite.SourceTicket );
			Assert.AreEqual( 3, member.BodySprite.PendingTemplate );
			Assert.AreEqual( resting, old != member.BodySprite.SourceTicket );
		}
	}

	[TestMethod]
	public void LegacyStrikingNormalizationQueuesStrikeAndCapturesOnlyTheExistingRandomDraw()
	{
		using var f = new Fixture();
		var member = f.Hire( StaffType.Guard );
		f.Park.Staff.Strikes.Restore( new int[5], Enumerable.Range( 0, 5 ).Select( index => index == (int)StaffType.Guard ).ToArray(), new uint[5] );
		member.State = StaffState.OnStrike; member.Job = StaffJob.Idle;
		var replay = new WorldRng( f.Park.World.State );
		var word = replay.NextWord();
		f.Update( member );
		Assert.AreEqual( StaffJob.Striking, member.Job );
		Assert.AreEqual( 19, member.BodySprite!.PendingTemplate );
		Assert.AreEqual( StaffSpriteMotionMath.StrikeAngle( 0, word ), member.BodySprite.PersonAngle );
		Assert.AreEqual( replay.State, f.Park.World.State );
		Assert.AreEqual( 0u, Source( member )[StaffSpriteRegister.Heading], "person angle waits for the presentation publication phase" );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DirectRosterAndEconomyFireImmediatelyInvalidateIdentityAndSource( bool throughEconomy )
	{
		using var f = new Fixture();
		var member = f.Hire( StaffType.Researcher );
		var body = member.BodySprite!;
		var ticket = body.SourceTicket!.Value;
		if ( throughEconomy ) f.Park.Fire( member.Id ); else f.Park.Staff.Fire( member.Id );
		Assert.IsFalse( body.IsLive );
		Assert.IsNull( member.BodySprite );
		Assert.IsFalse( f.Presentation.Registry.TryResolve( ticket, out _ ) );
		body.CreateSource();
		Assert.IsNull( body.SourceTicket );
	}

	[TestMethod]
	public void SuccessfulRosterReplacementRejectsOldObjectEvenWhenNumericIdentityIsReused()
	{
		using var f = new Fixture();
		var old = f.Hire( StaffType.Researcher );
		var body = old.BodySprite!;
		var replacement = new StaffMember { Id = old.Id, ThingId = old.ThingId, Type = old.Type, NameIndex = old.NameIndex, Grade = old.Grade };
		Fixture.Stand( replacement, 4, 5 );
		f.Park.Staff.Restore( new[] { replacement }, Array.Empty<StaffCandidate>(), new long[5], old.Id + 1, 100 );
		Assert.IsFalse( body.IsLive );
		Assert.IsNull( body.SourceTicket );
		Assert.IsNotNull( replacement.BodySprite );
		Assert.IsNull( replacement.BodySprite.SourceTicket, "roster attachment does not prematurely initialize" );
		f.World.Synchronize();
		Assert.IsNotNull( replacement.BodySprite.SourceTicket );
		Assert.AreNotSame( body.Identity, replacement.BodySprite.Identity );
	}

	[TestMethod]
	public void FailedReplacementEnumerationDoesNotInvalidateTheLiveSource()
	{
		using var f = new Fixture();
		var member = f.Hire( StaffType.Researcher );
		var body = member.BodySprite!;
		var ticket = body.SourceTicket;
		IEnumerable<StaffMember> Failed()
		{
			yield return new StaffMember { Id = 900, Type = StaffType.Guard, NameIndex = 1, Grade = 1 };
			throw new InvalidDataException( "Synthetic invalid final record." );
		}
		Assert.ThrowsException<InvalidDataException>( () => f.Park.Staff.Restore( Failed(), Array.Empty<StaffCandidate>(), new long[5], 1000, 100 ) );
		Assert.IsTrue( body.IsLive );
		Assert.AreEqual( ticket, body.SourceTicket );
		Assert.AreSame( member, f.Park.Staff.Members.Single() );
	}

	[TestMethod]
	public void PresentationAndRepeatedDrawingDoNotChangeGameplaySerializationOrWorldRandomness()
	{
		using var observed = new Fixture();
		using var control = new Fixture();
		var roles = Enum.GetValues<StaffType>();
		var actors = roles.Select( type => observed.Hire( type ) ).ToArray();
		var controls = roles.Select( type => control.Hire( type ) ).ToArray();
		foreach ( var member in controls ) member.BodySprite!.Invalidate();
		for ( var turn = 1; turn <= 120; turn++ )
		{
			for ( var index = 0; index < actors.Length; index++ )
			{
				actors[index].IdleStamp = controls[index].IdleStamp = 0;
				observed.Update( actors[index], turn ); control.Update( controls[index], turn );
			}
			observed.Presentation.SetHostTick( turn * 4 ); observed.Presentation.Schedule();
			observed.Presentation.Present();
			foreach ( var member in actors )
			{
				_ = member.BodySprite!.DrawRecord; _ = member.BodySprite.DrawRecord;
			}
			Assert.AreEqual( control.Park.World.State, observed.Park.World.State, $"turn {turn}" );
			Assert.AreEqual( ParkSaveFile.Serialize( control.Park ), ParkSaveFile.Serialize( observed.Park ), $"turn {turn}" );
		}
	}
	[TestMethod]
	public void StrikeWalkAndArrivalRequestPatrolThenStrikeWithZeroAngle()
	{
		using var f = new Fixture();
		var member = f.Hire( StaffType.Guard );
		f.Guests.ArrivalLane = [(0, 0), (0, 1), (0, 2), (14, 5), (15, 5)];
		f.World.GatesOpen = () => true;
		f.Park.Staff.Strikes.Restore( new int[5], Enumerable.Range( 0, 5 ).Select( index => index == (int)StaffType.Guard ).ToArray(), new uint[5] );
		member.IdleStamp = 0;
		f.Update( member );
		Assert.AreEqual( StaffJob.ToStrike, member.Job );
		Assert.AreEqual( 9, member.BodySprite!.PendingTemplate );
		member.OffGridLeg.Clear();
		member.Navigation!.SetDestination( member.Navigation.X, member.Navigation.Y, [] );
		f.Update( member, 1001 );
		Assert.AreEqual( StaffJob.Striking, member.Job );
		Assert.AreEqual( (19, 0), (member.BodySprite.PendingTemplate, member.BodySprite.PersonAngle) );
		Assert.AreEqual( StaffSpriteProgram.Patrol, Source( member ).RestartPc.Program );
	}

	[TestMethod]
	public void InvalidAngleEventPreservesPriorAngleAndContinuesTemplateAndSafeIntervalLogic()
	{
		using var f = new Fixture();
		var member = f.Hire( StaffType.Guard );
		var body = member.BodySprite!;
		body.Moving( 0, 0, 1, 0, false, false ); body.ApplyPending();
		body.Moving( 0, 0, int.MinValue, 0, true, false );
		Assert.AreEqual( 1536, body.PersonAngle );
		Assert.AreEqual( StaffSpriteDiagnostic.InvalidAngleArithmetic, body.Diagnostic );
		Assert.AreEqual( (2, 0), (body.PendingTemplate, body.PendingInterval) );
		body.ApplyPending();
		Assert.AreEqual( StaffSpriteProgram.Run, Source( member ).RestartPc.Program );
		Assert.AreEqual( 250, Source( member ).Interval, "safe zero does not replace the preceding interval" );
	}

	[TestMethod]
	public void InvalidRestoredThingIdentitiesLeaveOldRosterPoolAndSourceUntouched()
	{
		using var f = new Fixture();
		var pool = new ThingPool();
		f.Park.Staff.AttachThings( pool );
		var member = f.Hire( StaffType.Researcher );
		var body = member.BodySprite!;
		var ticket = body.SourceTicket;
		var before = pool.CaptureSnapshot();
		var replacements = new[]
		{
			new StaffMember { Id = 900, ThingId = member.ThingId, Type = member.Type, NameIndex = 0, Grade = 1 },
			new StaffMember { Id = 901, ThingId = member.ThingId, Type = member.Type, NameIndex = 1, Grade = 1 }
		};
		Assert.ThrowsException<InvalidDataException>( () => f.Park.Staff.Restore( replacements, Array.Empty<StaffCandidate>(), new long[5], 1000, 100 ) );
		Assert.AreSame( member, f.Park.Staff.Members.Single() );
		Assert.IsTrue( body.IsLive );
		Assert.AreEqual( ticket, body.SourceTicket );
		CollectionAssert.AreEqual( before.Used.ToArray(), pool.CaptureSnapshot().Used.ToArray() );
		CollectionAssert.AreEqual( before.FreeFirst.ToArray(), pool.CaptureSnapshot().FreeFirst.ToArray() );
	}

	[TestMethod]
	public void LegacyOneCellCompatibilityRouteDoesNotInventSteeringOrSourceEvents()
	{
		using var f = new Fixture();
		var member = f.Hire( StaffType.Mechanic );
		var ride = f.Park.RegisterExisting( 1100 );
		f.Access[ride.Id] = (5, 5);
		member.AssignedInstanceId = ride.Id; member.State = StaffState.Working;
		var state = Source( member );
		f.World.Tick( 1 );
		Assert.AreEqual( 3.5f, member.CellX );
		Assert.AreEqual( (0, 0), (member.BodySprite!.PendingTemplate, member.BodySprite.PendingInterval) );
		AssertSameState( state, Source( member ) );
	}

}
