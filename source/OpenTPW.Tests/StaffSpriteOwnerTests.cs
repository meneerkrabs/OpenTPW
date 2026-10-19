using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

internal static class StaffOwnerFixture
{
	internal static StaffMember Member( int id = 1, StaffType type = StaffType.Handyman ) =>
		new() { Id = id, ThingId = id + 100, Type = type, NameIndex = 0, Grade = 1, Variant = 0, CellX = 4, CellY = 5 };
	internal static StaffSpriteMetadataCatalog Catalog( bool missing = false, int performanceCount = 4 )
	{
		var slots = Enumerable.Range( 0, SpriteAnimationFile.SlotCount ).Select( slot => new SpriteAnimation( slot, slot * 40, 8, 5 ) ).ToArray();
		var frames = Enumerable.Range( 0, slots.Length * 40 ).Select( _ => new GuestAtlasFrame( 0, 0, 2, 3, -1, -3 ) ).ToArray();
		return new StaffSpriteMetadataCatalog( Enumerable.Range( 4, 5 ).Select( kind => missing
			? StaffSpriteBankMetadata.Unavailable( (uint)kind, 0, $"synthetic-{kind}" )
			: new StaffSpriteBankMetadata( (uint)kind, 0, kind - 4, $"synthetic-{kind}", 2, 3, slots, frames,
				Enumerable.Range( 0, 4 ).Select( index => new SpritePerformance( index, 4, 2, 3 ) ), performanceCount ) ) );
	}
	internal sealed class Rig : IDisposable
	{
		internal readonly List<StaffMember> Members;
		internal readonly StaffMember Member;
		internal readonly StaffSpritePresentation Presentation;
		internal readonly EmployeePortraitOwner Window;
		internal StaffBodySprite Body => Member.BodySprite!;
		internal StaffSpriteVmState Source { get { Assert.IsTrue( Body.TryResolve( out var state ) ); return state; } }
		internal StaffSpriteVmState Clone { get { Assert.IsTrue( Window.TryResolve( out var state ) ); return state; } }
		internal Rig( bool missing = false, bool create = true, long tick = 0 )
		{
			Member = StaffOwnerFixture.Member(); Members = new() { Member };
			Presentation = new StaffSpritePresentation( Catalog( missing ), () => Members, tick );
			Window = new EmployeePortraitOwner( Presentation );
			if ( create ) Body.EnsureInitialized();
		}
		internal void Full() => Window.FullRefresh( Member, -498f / 1024, 292f / 768, 39f / 40 );
		public void Dispose() { Window.Dispose(); Presentation.Dispose(); }
	}
	internal static uint[] Words( StaffSpriteVmState state, bool withoutHandle = false ) => new[]
	{
		state.OpaqueWord, withoutHandle ? 0 : state.Handle, state.CurrentPc.Word, state.RestartPc.Word, state.LastPc.Word, state.ProgramTable,
		(uint)state.RunState, unchecked( (uint)state.StackPointer )
	}.Concat( state.Stack ).Concat( new[] { unchecked( (uint)state.IfDepth ), unchecked( (uint)state.CallDepth ), unchecked( (uint)state.LoopDepth ),
		unchecked( (uint)state.Due ), unchecked( (uint)state.Interval ) } ).Concat( state.Registers ).Concat( state.DrawCache ).Append( state.OutputValid ).ToArray();
	internal static void Stamp( StaffSpriteVmState state, uint seed )
	{
		state.OpaqueWord = seed; state.CurrentPc = new( seed + 2 ); state.RestartPc = new( seed + 3 ); state.LastPc = new( seed + 4 );
		state.ProgramTable = seed + 5; state.RunState = (StaffSpriteRunState)(seed + 6); state.StackPointer = (int)seed + 7;
		state.IfDepth = (int)seed + 28; state.CallDepth = (int)seed + 29; state.LoopDepth = (int)seed + 30;
		state.Due = (int)seed + 31; state.Interval = (int)seed + 32; state.OutputValid = seed + 69;
		for ( var index = 0; index < state.Stack.Length; index++ ) state.Stack[index] = seed + 8 + (uint)index;
		for ( var index = 0; index < state.Registers.Length; index++ ) state.Registers[index] = seed + 33 + (uint)index;
		for ( var index = 0; index < state.DrawCache.Length; index++ ) state.DrawCache[index] = seed + 52 + (uint)index;
	}
}

[TestClass]
public class EmployeePortraitOwnerTests
{
	[TestMethod]
	public void FullOwnerEventCopiesAllSeventyWordsWithOnlyAuthoredOverridesAndNoAliases()
	{
		using var rig = new StaffOwnerFixture.Rig(); StaffOwnerFixture.Stamp( rig.Source, 0x1000 );
		var before = StaffOwnerFixture.Words( rig.Source ); rig.Full(); var expected = (uint[])before.Clone();
		expected[1] = (uint)rig.Window.CloneTicket!.Value.Slot;
		expected[34] = BitConverter.SingleToUInt32Bits( -498f / 1024 ); expected[35] = BitConverter.SingleToUInt32Bits( 292f / 768 );
		expected[36] = BitConverter.SingleToUInt32Bits( 39f / 40 ); expected[41] = BitConverter.SingleToUInt32Bits( 8f );
		expected[49] = (before[49] & ~0x200u) | 0x50000u;
		CollectionAssert.AreEqual( expected, StaffOwnerFixture.Words( rig.Clone ) );
		Assert.AreNotSame( rig.Source.Stack, rig.Clone.Stack ); Assert.AreNotSame( rig.Source.Registers, rig.Clone.Registers ); Assert.AreNotSame( rig.Source.DrawCache, rig.Clone.DrawCache );
		rig.Clone.Stack[0]++; rig.Clone.DrawCache[16]++; rig.Clone.Registers[0]++;
		CollectionAssert.AreEqual( before, StaffOwnerFixture.Words( rig.Source ) );
	}

	[TestMethod]
	public void PartialOwnerEventHasExactAllowlistAndRetainsOwnFlagsClockControlAndAuthoredPose()
	{
		using var rig = new StaffOwnerFixture.Rig(); rig.Full();
		StaffOwnerFixture.Stamp( rig.Source, 0x1100 ); StaffOwnerFixture.Stamp( rig.Clone, 0x2200 );
		rig.Source[StaffSpriteRegister.Flags] = 0x12345678; rig.Clone[StaffSpriteRegister.Flags] = 0xA0800201;
		var source = StaffOwnerFixture.Words( rig.Source ); var expected = StaffOwnerFixture.Words( rig.Clone );
		foreach ( var index in new[] { 5, 2, 43, 44, 45, 50, 51, 40, 42, 48, 69, 46, 47 } ) expected[index] = source[index];
		expected[49] = (expected[49] & ~0x200u) | 0x50000u;
		rig.Window.PartialRefresh(); CollectionAssert.AreEqual( expected, StaffOwnerFixture.Words( rig.Clone ) );
		CollectionAssert.AreEqual( source, StaffOwnerFixture.Words( rig.Source ) );
	}

	[TestMethod]
	public void AbsentOpenCannotLateBindAndFullWhileAbsentDestroysPreviousClone()
	{
		using var rig = new StaffOwnerFixture.Rig( create: false ); rig.Full();
		Assert.AreEqual( EmployeePortraitState.OpenUnbound, rig.Window.State ); Assert.IsNull( rig.Window.BoundIdentity );
		rig.Body.CreateSource(); for ( var index = 0; index < 3; index++ ) rig.Window.PartialRefresh();
		Assert.IsNull( rig.Window.CloneTicket ); Assert.AreEqual( 1, rig.Presentation.Registry.Count );
		rig.Full(); Assert.AreEqual( EmployeePortraitState.OpenBound, rig.Window.State );
		rig.Body.DeleteSource(); rig.Full(); rig.Body.CreateSource(); rig.Window.PartialRefresh();
		Assert.AreEqual( EmployeePortraitState.OpenUnbound, rig.Window.State ); Assert.IsNull( rig.Window.CloneTicket );
	}

	[TestMethod]
	public void ActualSourceDeletionKeepsIndependentCloneAndReturnCopiesIntoSameEpochAndTicket()
	{
		using var rig = new StaffOwnerFixture.Rig(); rig.Full(); var ticket = rig.Window.CloneTicket; var epoch = rig.Window.Epoch;
		var sourceTicket = rig.Body.SourceTicket; rig.Body.DeleteSource(); rig.Window.PartialRefresh();
		Assert.IsTrue( rig.Body.IsLive ); Assert.IsNotNull( rig.Window.ReadCloneState() );
		rig.Presentation.Registry.Schedule( 63 );
		Assert.AreEqual( 125, rig.Clone.Due ); Assert.AreEqual( StaffSpriteRoute.World, rig.Window.DrawRecord!.Route );
		Assert.AreEqual( StaffSpriteDiagnostic.WorldRouteNotImplemented, rig.Presentation.Catalog.ResolveFrame( rig.Window.DrawRecord! ).Diagnostic );
		Assert.AreEqual( BitConverter.SingleToUInt32Bits( -498f / 1024 ), rig.Clone[StaffSpriteRegister.X] );
		Assert.AreEqual( BitConverter.SingleToUInt32Bits( 292f / 768 ), rig.Clone[StaffSpriteRegister.Y] );
		Assert.AreEqual( BitConverter.SingleToUInt32Bits( 39f / 40 ), rig.Clone[StaffSpriteRegister.Z] );
		rig.Body.CreateSource(); Assert.AreNotEqual( sourceTicket, rig.Body.SourceTicket ); rig.Window.PartialRefresh();
		Assert.AreEqual( ticket, rig.Window.CloneTicket ); Assert.AreEqual( epoch, rig.Window.Epoch );
		Assert.AreEqual( 125, rig.Clone.Due ); Assert.AreEqual( StaffSpriteRoute.Screen, rig.Window.DrawRecord!.Route );
	}

	[TestMethod]
	public void SourceLossInsidePerformanceLoopStaysScreenUntilAnActualFlagInstruction()
	{
		using var rig = new StaffOwnerFixture.Rig(); rig.Body.RequestTemplate( 13 ); rig.Body.ApplyPending();
		rig.Presentation.Registry.Schedule( 63 ); rig.Full(); rig.Body.DeleteSource();
		rig.Presentation.Registry.Schedule( 126 );
		Assert.AreEqual( StaffSpriteRoute.Screen, rig.Window.DrawRecord!.Route );
		Assert.IsTrue( rig.Presentation.Catalog.ResolveFrame( rig.Window.DrawRecord! ).CanDraw );
	}

	[TestMethod]
	public void IdentityInvalidationClosesCloneAndReusedNumbersCannotReconnectIt()
	{
		using var rig = new StaffOwnerFixture.Rig(); rig.Full(); var old = rig.Body; var oldTicket = rig.Window.CloneTicket!.Value;
		old.Invalidate(); rig.Members.Clear(); var replacement = StaffOwnerFixture.Member(); rig.Members.Add( replacement );
		rig.Presentation.Attach( replacement ).EnsureInitialized(); rig.Window.PartialRefresh();
		Assert.AreEqual( EmployeePortraitState.Closed, rig.Window.State ); Assert.IsFalse( old.IsLive );
		Assert.IsFalse( rig.Presentation.Registry.TryResolve( oldTicket, out _ ) ); Assert.IsNull( rig.Window.DrawRecord );
	}

	[TestMethod]
	public void SyntheticEndRetiresOnlyAfterStrictDeadlineAndPartialCannotUseReusedSlot()
	{
		using var rig = new StaffOwnerFixture.Rig(); rig.Full(); rig.Body.DeleteSource();
		var synthetic = (StaffSpriteProgram)60000; rig.Clone.CurrentPc = rig.Clone.RestartPc = new( synthetic, 0 );
		var programs = new StaffSpriteProgramSet( new Dictionary<StaffSpriteProgram, StaffSpriteInstruction[]> { [synthetic] = [new( StaffSpriteOp.End )] } );
		var old = rig.Window.CloneTicket!.Value; rig.Presentation.Registry.Schedule( 63, programs );
		Assert.AreEqual( StaffSpriteRunState.Ended, rig.Clone.RunState ); Assert.AreEqual( 0u, rig.Clone.OutputValid ); Assert.AreEqual( 125, rig.Clone.Due );
		rig.Presentation.Registry.Schedule( 125, programs ); Assert.AreEqual( EmployeePortraitState.OpenBound, rig.Window.State );
		rig.Presentation.Registry.Schedule( 126, programs ); Assert.AreEqual( EmployeePortraitState.OpenRetired, rig.Window.State );
		rig.Body.CreateSource(); var other = StaffOwnerFixture.Member( 2 ); rig.Members.Add( other ); rig.Presentation.Attach( other ).CreateSource();
		Assert.IsFalse( rig.Presentation.Registry.TryResolve( old, out _ ) ); rig.Window.PartialRefresh(); Assert.IsNull( rig.Window.CloneTicket );
		rig.Full(); Assert.AreEqual( EmployeePortraitState.OpenBound, rig.Window.State );
	}

	[TestMethod]
	public void InvalidCloneExecutionIsDiagnosticAndPartialCopyDoesNotRepairRetainedControl()
	{
		using var rig = new StaffOwnerFixture.Rig(); rig.Full(); rig.Clone.StackPointer = 21;
		rig.Presentation.Registry.Schedule( 63 ); Assert.AreEqual( StaffSpriteDiagnostic.InvalidVm, rig.Window.DrawRecord!.Diagnostic );
		Assert.AreEqual( 0u, rig.Clone.OutputValid ); rig.Window.PartialRefresh();
		Assert.AreEqual( 21, rig.Clone.StackPointer ); Assert.AreEqual( 125, rig.Clone.Due );
		Assert.AreEqual( StaffSpriteDiagnostic.InvalidVm, rig.Window.DrawRecord!.Diagnostic );
	}
}

[TestClass]
public class StaffSpriteRegistryTests
{
	[TestMethod]
	public void FirstFreeAscendingSlotsGrowByFiftyAndStaleTicketsNeverResolve()
	{
		using var rig = new StaffOwnerFixture.Rig(); var tickets = new List<StaffSpriteTicket> { rig.Body.SourceTicket!.Value };
		for ( var index = 2; index <= 7; index++ )
		{
			var member = StaffOwnerFixture.Member( index ); rig.Members.Add( member ); rig.Presentation.Attach( member ).CreateSource(); tickets.Add( member.BodySprite!.SourceTicket!.Value );
		}
		Assert.AreEqual( 50, rig.Presentation.Registry.Capacity ); rig.Presentation.Registry.Remove( tickets[1] ); rig.Full();
		Assert.AreEqual( 2, rig.Window.CloneTicket!.Value.Slot ); Assert.AreEqual( tickets[1].Generation + 1, rig.Window.CloneTicket.Value.Generation );
		Assert.IsFalse( rig.Presentation.Registry.TryResolve( tickets[1], out _ ) ); Assert.IsFalse( rig.Presentation.Registry.Remove( tickets[1] ) );
		var order = new List<int>(); rig.Presentation.Registry.Scheduled += (ticket, _, _) => order.Add( ticket.Slot );
		rig.Presentation.Registry.Schedule( 63 ); CollectionAssert.AreEqual( Enumerable.Range( 1, 7 ).ToArray(), order );
		for ( var index = 8; index <= 51; index++ ) { var member = StaffOwnerFixture.Member( index ); rig.Members.Add( member ); rig.Presentation.Attach( member ).CreateSource(); }
		Assert.AreEqual( 100, rig.Presentation.Registry.Capacity );
	}

	[TestMethod]
	public void StaffSubsetSchedulingOrderHasNoCrossInstanceReadsAndCopiesOnlyAfterTraversal()
	{
		using var first = new StaffOwnerFixture.Rig(); using var second = new StaffOwnerFixture.Rig();
		// Move second's source to slot 2 and free slot 1 before cloning; first remains source 1 / clone 2.
		second.Body.DeleteSource(); var filler = StaffOwnerFixture.Member( 2 ); second.Members.Add( filler ); second.Presentation.Attach( filler ).CreateSource();
		second.Body.CreateSource(); filler.BodySprite!.DeleteSource();
		first.Full(); second.Full(); Assert.AreEqual( 1, second.Window.CloneTicket!.Value.Slot );
		foreach ( var sample in new[] { 63, 126, 189, 252 } )
		{
			first.Presentation.Registry.Schedule( sample ); second.Presentation.Registry.Schedule( sample );
			first.Window.PartialRefresh(); second.Window.PartialRefresh();
			CollectionAssert.AreEqual( StaffOwnerFixture.Words( first.Source, true ), StaffOwnerFixture.Words( second.Source, true ) );
			CollectionAssert.AreEqual( StaffOwnerFixture.Words( first.Clone, true ), StaffOwnerFixture.Words( second.Clone, true ) );
		}
	}

	[TestMethod]
	public void InvalidationReleasesPersonReferencesAcrossRepeatedReuseAndDisposalIsIdempotent()
	{
		using var rig = new StaffOwnerFixture.Rig();
		for ( var index = 0; index < 1000; index++ )
		{
			var member = StaffOwnerFixture.Member( 2 ); rig.Members.Add( member ); rig.Presentation.Attach( member ).CreateSource();
			member.BodySprite!.Invalidate(); rig.Members.Remove( member );
			Assert.AreEqual( 1, rig.Presentation.BoundPersonCount ); Assert.AreEqual( 1, rig.Presentation.Registry.Count );
		}
		rig.Full(); rig.Presentation.Dispose(); rig.Presentation.Dispose();
		Assert.AreEqual( 0, rig.Presentation.BoundPersonCount ); Assert.AreEqual( 0, rig.Presentation.Registry.Count );
		Assert.AreEqual( EmployeePortraitState.Closed, rig.Window.State );
	}
}

[TestClass]
public class StaffSpritePresentationPhaseTests
{
	[TestMethod]
	public void PartialPrecedesHeadingPublicationAndNewSourceHeadingRemainsZeroUntilThatPhase()
	{
		using var rig = new StaffOwnerFixture.Rig(); Assert.AreEqual( 1024, rig.Body.PersonAngle ); rig.Full();
		Assert.AreEqual( 0u, rig.Clone[StaffSpriteRegister.Heading] );
		rig.Body.Moving( 0, 0, 100, 0, false, false ); Assert.AreEqual( 1536, rig.Body.PersonAngle );
		rig.Presentation.Present( rig.Window.PartialRefresh );
		Assert.AreEqual( 0u, rig.Clone[StaffSpriteRegister.Heading] ); Assert.AreEqual( 2u, rig.Source[StaffSpriteRegister.Heading] );
		rig.Presentation.Present( rig.Window.PartialRefresh ); Assert.AreEqual( 2u, rig.Clone[StaffSpriteRegister.Heading] );
	}

	[TestMethod]
	public void AbsentMovingQueuesDesiredEntryAndConsumptionVersusReturnPreservesExactOrder()
	{
		using var rig = new StaffOwnerFixture.Rig(); rig.Body.DeleteSource();
		rig.Body.Moving( 0, 0, 26214, 0, false, false ); Assert.AreEqual( 1, rig.Body.PendingTemplate ); Assert.AreEqual( 30, rig.Body.PendingInterval );
		rig.Body.ApplyPending(); rig.Body.CreateSource();
		Assert.AreEqual( 0, rig.Body.PendingTemplate ); Assert.AreEqual( StaffSpriteProgram.Stand, rig.Source.RestartPc.Program ); Assert.AreEqual( 62, rig.Source.Interval );
		rig.Body.DeleteSource(); rig.Body.Moving( 0, 0, 26214, 0, false, true ); rig.Body.CreateSource();
		Assert.AreEqual( 23, rig.Body.PendingTemplate ); rig.Body.ApplyPending();
		Assert.AreEqual( StaffSpriteProgram.Hold, rig.Source.RestartPc.Program ); Assert.AreEqual( 30, rig.Source.Interval );
	}

	[TestMethod]
	public void PerformanceRequestsPreserveIntroHoldButDirectConstructionInitializesThem()
	{
		using var rig = new StaffOwnerFixture.Rig();
		for ( var index = 0; index < 4; index++ )
		{
			rig.Body.RequestTemplate( 13 + index ); rig.Body.ApplyPending();
			Assert.AreEqual( (StaffSpriteProgram)(256 + index), rig.Source.RestartPc.Program );
			Assert.AreEqual( 0u, rig.Source[StaffSpriteRegister.Intro] ); Assert.AreEqual( 0u, rig.Source[StaffSpriteRegister.Hold] );
		}
		rig.Body.DeleteSource(); Assert.IsTrue( rig.Body.CreatePerformanceSource( 3 ) );
		Assert.AreEqual( 2u, rig.Source[StaffSpriteRegister.Intro] ); Assert.AreEqual( 3u, rig.Source[StaffSpriteRegister.Hold] );
	}

	[TestMethod]
	public void RegistryUsesDirectSignedDeadlineWrapAndOneBoundedRunPerOpportunity()
	{
		using var rig = new StaffOwnerFixture.Rig(); rig.Source.Due = int.MaxValue - 1; rig.Source.Interval = 62;
		rig.Presentation.Registry.Schedule( int.MaxValue - 1 ); Assert.AreEqual( int.MaxValue - 1, rig.Source.Due );
		rig.Presentation.Registry.Schedule( int.MaxValue ); Assert.AreEqual( unchecked( int.MaxValue + 62 ), rig.Source.Due );
		Assert.AreEqual( new StaffSpritePc( StaffSpriteProgram.Stand, 2 ), rig.Source.LastPc );
		var previous = StaffOwnerFixture.Words( rig.Source ); rig.Presentation.Registry.Schedule( int.MinValue );
		CollectionAssert.AreEqual( previous, StaffOwnerFixture.Words( rig.Source ) );
		rig.Source.Due = -10000; rig.Presentation.Registry.Schedule( 1000000 ); Assert.AreEqual( 1000062, rig.Source.Due );
	}

	[TestMethod]
	public void SourceAndCloneKeepIndependentDeadlinesAcrossIntervalChangeAndSourceLoss()
	{
		using var rig = new StaffOwnerFixture.Rig(); rig.Source.Due = 100; rig.Full(); rig.Body.RequestInterval( 20 ); rig.Body.ApplyPending();
		rig.Presentation.Registry.Schedule( 100 ); Assert.AreEqual( 100, rig.Clone.Due );
		rig.Presentation.Registry.Schedule( 101 ); rig.Window.PartialRefresh();
		Assert.AreEqual( 121, rig.Source.Due ); Assert.AreEqual( 163, rig.Clone.Due ); Assert.AreEqual( 62, rig.Clone.Interval );
		rig.Presentation.Registry.Schedule( 122 ); Assert.AreEqual( 142, rig.Source.Due ); Assert.AreEqual( 163, rig.Clone.Due );
		rig.Body.DeleteSource(); rig.Presentation.Registry.Schedule( 164 ); Assert.AreEqual( 226, rig.Clone.Due );
	}

	[TestMethod]
	public void DifferentPartialBoundariesCanRetainDifferentLastPcDespiteSameSamplesAndFinalVisibleOutput()
	{
		using var a = new StaffOwnerFixture.Rig(); using var b = new StaffOwnerFixture.Rig();
		foreach ( var rig in new[] { a, b } ) { rig.Body.RequestTemplate( 1 ); rig.Body.ApplyPending(); rig.Source.Due = 0; rig.Source.Interval = 100; rig.Full(); rig.Source.Interval = 10; }
		foreach ( var sample in new[] { 1, 12 } ) { a.Presentation.Registry.Schedule( sample ); b.Presentation.Registry.Schedule( sample ); }
		a.Window.PartialRefresh(); a.Presentation.Registry.Schedule( 102 ); b.Presentation.Registry.Schedule( 102 );
		Assert.AreEqual( 2u, a.Clone[StaffSpriteRegister.Frame] ); Assert.AreEqual( 1u, b.Clone[StaffSpriteRegister.Frame] );
		a.Window.PartialRefresh(); b.Window.PartialRefresh();
		Assert.AreEqual( a.Clone.CurrentPc, b.Clone.CurrentPc ); Assert.AreEqual( a.Clone[StaffSpriteRegister.Frame], b.Clone[StaffSpriteRegister.Frame] );
		Assert.AreEqual( new StaffSpritePc( StaffSpriteProgram.Walk, 4 ), a.Clone.LastPc );
		Assert.AreEqual( new StaffSpritePc( StaffSpriteProgram.Walk, 3 ), b.Clone.LastPc );
	}

	[TestMethod]
	public void FullEventsDoNotCommuteWithSchedulerAndAreNeverCoalescedToFinalIdentity()
	{
		using var before = new StaffOwnerFixture.Rig(); using var after = new StaffOwnerFixture.Rig();
		before.Full(); before.Presentation.Registry.Schedule( 63 ); after.Presentation.Registry.Schedule( 63 ); after.Full();
		Assert.AreEqual( StaffSpriteRoute.World, before.Window.DrawRecord!.Route ); Assert.AreEqual( StaffSpriteRoute.Screen, after.Window.DrawRecord!.Route );
		var tape = new List<string>();
		void Full() { before.Full(); tape.Add( $"Full:{before.Window.Epoch}" ); }
		Full(); before.Body.DeleteSource(); tape.Add( "Delete" ); before.Window.Close(); tape.Add( "Close" );
		Full(); before.Body.CreateSource(); tape.Add( "Create" ); before.Window.PartialRefresh(); tape.Add( "Partial" );
		Assert.AreEqual( EmployeePortraitState.OpenUnbound, before.Window.State ); Full();
		CollectionAssert.AreEqual( new[] { "Full:2", "Delete", "Close", "Full:3", "Create", "Partial", "Full:4" }, tape );
	}

	[TestMethod]
	public void EqualEventBoundariesGiveCompleteEqualityRegardlessOfBatchGroupingAndRepeatedReads()
	{
		using var a = new StaffOwnerFixture.Rig(); using var b = new StaffOwnerFixture.Rig(); a.Full(); b.Full();
		foreach ( var sample in new[] { 1, 63, 100, 126, 189 } )
		{
			a.Presentation.Registry.Schedule( sample ); b.Presentation.Registry.Schedule( sample );
			a.Presentation.Present( a.Window.PartialRefresh ); b.Presentation.Present( b.Window.PartialRefresh );
			var unchanged = StaffOwnerFixture.Words( a.Clone ); for ( var index = 0; index < 30; index++ ) { _ = a.Window.DrawRecord; _ = a.Body.DrawRecord; }
			CollectionAssert.AreEqual( unchanged, StaffOwnerFixture.Words( a.Clone ) );
			CollectionAssert.AreEqual( StaffOwnerFixture.Words( a.Source, true ), StaffOwnerFixture.Words( b.Source, true ) );
			CollectionAssert.AreEqual( StaffOwnerFixture.Words( a.Clone, true ), StaffOwnerFixture.Words( b.Clone, true ) );
		}
	}

	[TestMethod]
	public void FreshReconstructionWaitsForFinalClockAndPoseWithoutRestoringUnsavedHistory()
	{
		var members = new List<StaffMember> { StaffOwnerFixture.Member() };
		using var presentation = new StaffSpritePresentation( StaffOwnerFixture.Catalog(), () => members, 0, ready: false );
		var body = members[0].BodySprite!; body.EnsureInitialized(); Assert.IsNull( body.SourceTicket );
		members[0].CellX = 9; presentation.Reconstruct( 600 );
		Assert.AreEqual( 10000, presentation.LastSample ); Assert.AreEqual( 10062, body.ReadSourceState()!.Due ); Assert.IsTrue( body.Reconstructed );
		var ticket = body.SourceTicket; presentation.Reconstruct( 600 ); Assert.AreEqual( ticket, body.SourceTicket );
		Assert.AreEqual( 0u, body.ReadSourceState()![StaffSpriteRegister.X] );
		presentation.Present(); Assert.AreEqual( BitConverter.SingleToUInt32Bits( 9 ), body.ReadSourceState()![StaffSpriteRegister.X] );
	}
}
