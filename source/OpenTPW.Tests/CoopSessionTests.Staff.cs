using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] What a park holds of a guest seat's staff, around the session (C4c-1): a save writes a guest's employee in hand as its drop back leaves it while the live park
/// keeps it in hand, the host's teardown releases a guest seat's staff state in records before its last checkpoint (or, inside a tick, after it), and the host's and a single player's own
/// staff tool follows what the park holds: a dismissed carried employee ends the tool, and a hire another seat's reservation refuses keeps it.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>A handyman hired on <paramref name="park"/> and standing idle on a free cell near (<paramref name="x"/>, <paramref name="y"/>), where it may be picked up.</summary>
	private static StaffMember StandingHandyman( Park park, int x = 18, int y = 12 )
	{
		park.Enter();
		var member = EconomyTestData.HireBest( park.Economy, StaffType.Handyman );
		var placement = park.Level.StaffPlacement!;
		var cell = Enumerable.Range( 0, 12 ).SelectMany( dx => Enumerable.Range( 0, 6 ).Select( dy => (X: x + dx, Y: y + dy) ) ).First( cell => placement.IsValid( cell.X, cell.Y ) );
		StaffPlacement.PutDown( member, cell.X, cell.Y );
		return member;
	}

	/// <summary>The fields of <paramref name="member"/>'s walk a put-down writes, to compare the live member before and after a save.</summary>
	private static (float? X, float? Y, double Travel, object? Navigation, StaffState State, int SpeedBonus, StaffJob Job, long IdleStamp, object Leg, int LegCount) WalkOf( StaffMember member ) =>
		(member.CellX, member.CellY, member.TravelSeconds, member.Navigation, member.State, member.SpeedBonus, member.Job, member.IdleStamp, member.OffGridLeg, member.OffGridLeg.Count);

	/// <summary>
	/// <see cref="FirstReplayMismatch"/> for a session whose parks the test changed once outside the commands, alike on every peer, after tick <paramref name="changeTick"/> (<see cref="AlikeIn"/>):
	/// the replay makes the same <paramref name="change"/> after that tick, past its checkpoint, as the host made it between two frames.
	/// </summary>
	private long? FirstReplayMismatchWith( ParkCommandLogFile log, Action<Park> prepare, long changeTick, Action<Park> change )
	{
		var twin = NewPark();
		twin.Enter();
		prepare( twin );
		Assert.AreEqual( (log.StartTick, log.StartHash), (twin.Level.TickCount, twin.Level.ComputeStateHash()), "the twin is the session's starting park" );
		var hashes = new Dictionary<long, ulong>();
		ParkCommandReplay.Run( twin.Level.Commands, log, () => twin.Level.Commands.NextTick, () => twin.Level.RunTicks( 1 ), log.Checkpoints[^1].Ticks, ticks =>
		{
			hashes[ticks] = twin.Level.ComputeStateHash();
			if ( ticks == changeTick )
			{
				twin.Enter();
				change( twin );
			}
		}, new CoopBuildRights( twin.Level ) );
		hashes[log.Checkpoints[^1].Ticks] = twin.Level.ComputeStateHash();
		return ParkCommandReplay.FirstMismatch( log, hashes )?.Ticks;
	}

	[TestMethod]
	public void GuestSeatsEmployeesAreSavedDroppedBackWhereTheyWerePickedUpAndTheLiveParkKeepsThemInHand()
	{
		var park = NewPark();
		var members = new[] { StandingHandyman( park, 18, 12 ), StandingHandyman( park, 30, 12 ) };
		var origins = members.Select( member => StaffPlacementRules.CellOf( member )!.Value ).ToArray();
		var hands = park.Level.StaffHands!;
		// seats 1 and 2 pick one up each, set up directly (no guest holds staff before C4c-2's switch), and each employee then has a walk of its own: a leg off the grid and a travel phase
		for ( var index = 0; index < members.Length; index++ )
		{
			var (member, origin) = (members[index], origins[index]);
			Assert.IsTrue( hands.Apply( new PickUpStaffCommand( member.Id ) { Player = index + 1 } ).Accepted );
			(member.CellX, member.CellY, member.TravelSeconds, member.SpeedBonus, member.Job, member.IdleStamp) = (origin.X + 2.25f, origin.Y + 0.75f, 0.125, 3, StaffJob.PickedUp, 77 + index);
			member.OffGridLeg.Add( (1, 2, 3, index) );
		}
		// and seat 3 places a candidate: a pending hire is not saved, its candidate is, unreserved
		var candidate = park.Economy.Staff.Candidates.First( item => park.Economy.Staff.CanHire( item.Type ) );
		Assert.IsTrue( hands.Apply( new BeginStaffHireCommand( candidate.Id ) { Player = 3 } ).Accepted );
		var before = members.Select( WalkOf ).ToArray();
		// the manual save's capture, checked as it is taken, and the autosave's, checked afterwards as the autosave does over later frames
		var manual = park.Level.CaptureResumeSnapshot( null, "shared", park.Level.AutosaveAssets() );
		var automatic = park.Level.CaptureResumeSnapshotUnchecked( null, "shared", out var validate, park.Level.AutosaveAssets() );
		CollectionAssert.AreEqual( before, members.Select( WalkOf ).ToArray(), "the live members as they were before the autosave's check" );
		validate();
		foreach ( var (snapshot, what) in new[] { (manual, "the manual save"), (automatic, "the autosave") } )
		for ( var index = 0; index < members.Length; index++ )
		{
			var (member, origin) = (members[index], origins[index]);
			var saved = snapshot.Economy.Staff!.Members!.Single( item => item.Id == member.Id );
			Assert.AreEqual( (origin.X + 0.5f, origin.Y + 0.5f, StaffState.Idle), (saved.CellX, saved.CellY, saved.State), $"seat {index + 1}'s employee in the economy, dropped back" );
			Assert.IsTrue( saved.WorldState is null || (saved.WorldState.Navigation == null && saved.WorldState.Job == StaffJob.Idle && saved.WorldState.SpeedBonus == 0
				&& saved.WorldState.IdleStamp == 0), $"seat {index + 1}'s employee: no walk left" );
			var actor = snapshot.Staff!.Actors.Single( item => item.Id == member.Id );
			Assert.AreEqual( (origin.X + 0.5f, origin.Y + 0.5f, 0.0), (actor.CellX, actor.CellY, actor.TravelSeconds), $"{what}, seat {index + 1}'s employee: the staff world's actor agrees" );
		}
		// each file reads back and loads: the employees stand idle where they were picked up, with no walk, and the candidate is in the pool, placed by nobody
		foreach ( var snapshot in new[] { manual, automatic } )
		{
			var loaded = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snapshot ) );
			var economy = ParkSaveFile.Restore( loaded.Economy, park.Economy.Settings, park.Economy.Catalog );
			for ( var index = 0; index < members.Length; index++ )
			{
				var restored = economy.Staff.Members.Single( item => item.Id == members[index].Id );
				Assert.AreEqual( (origins[index].X + 0.5f, origins[index].Y + 0.5f, StaffState.Idle, StaffJob.Idle, 0.0), (restored.CellX, restored.CellY, restored.State, restored.Job, restored.TravelSeconds) );
				Assert.IsNull( restored.Navigation );
				Assert.AreEqual( 0, restored.OffGridLeg.Count );
			}
			Assert.IsTrue( economy.Staff.Candidates.Any( item => item.Id == candidate.Id ), "the candidate is saved" );
			Assert.AreEqual( 0, economy.Staff.PendingHires.Count, "a pending hire is not" );
		}
		CollectionAssert.AreEqual( before, members.Select( WalkOf ).ToArray(), "the live members as they were" );
		Assert.AreEqual( ((members[0].Id, origins[0].X, origins[0].Y), (members[1].Id, origins[1].X, origins[1].Y)), (hands.Of( 1 )!.Value, hands.Of( 2 )!.Value), "and still in hand" );
		Assert.AreEqual( candidate.Id, park.Economy.Staff.PendingHireOf( 3 ), "and the live pending hire stays" );
		Assert.IsTrue( members.All( member => member.State == StaffState.PickedUp ) );

		// a capture that throws after the put-down leaves the live members as they were too
		var particles = Level.ParticlesCaptured;
		Level.ParticlesCaptured = _ => throw new InvalidOperationException( "a capture that fails" );
		try
		{
			Assert.ThrowsException<InvalidOperationException>( () => park.Level.CaptureResumeSnapshot( null, "shared", park.Level.AutosaveAssets() ) );
			Assert.ThrowsException<InvalidOperationException>( () => park.Level.CaptureResumeSnapshotUnchecked( null, "shared", out _, park.Level.AutosaveAssets() ) );
		}
		finally
		{
			Level.ParticlesCaptured = particles;
		}
		CollectionAssert.AreEqual( before, members.Select( WalkOf ).ToArray(), "the failed capture gave every field back" );

		// a join carries the employees as they are, with the hands
		var join = park.Level.CaptureJoinSnapshot();
		foreach ( var member in members )
			Assert.AreEqual( (StaffState.PickedUp, member.CellX), (join.Economy.Staff!.Members!.Single( item => item.Id == member.Id ).State, join.Economy.Staff.Members!.Single( item => item.Id == member.Id ).CellX) );
	}

	[TestMethod]
	public void TheHostsTeardownReleasesAGuestSeatsStaffStateInRecordsBeforeItsLastCheckpoint()
	{
		var (host, guest, _, _) = Shared();
		Rounds( 20, host, guest );
		var seat = guest.Level.Commands.Player;
		var ids = new (int Member, int Candidate)[2];
		// the guest's seat holds an employee and the next seat a pending hire, set up alike on every peer (no guest holds staff before C4c-2's switch)
		void Hold( Park park )
		{
			var member = StandingHandyman( park );
			var candidate = park.Economy.Staff.Candidates.First( item => park.Economy.Staff.CanHire( item.Type ) );
			Assert.IsTrue( park.Level.StaffHands!.Apply( new PickUpStaffCommand( member.Id ) { Player = seat } ).Accepted );
			Assert.IsTrue( park.Level.StaffHands.Apply( new BeginStaffHireCommand( candidate.Id ) { Player = seat + 1 } ).Accepted );
			ids[park == host ? 0 : 1] = (member.Id, candidate.Id);
		}
		var changeTick = 0L;
		AlikeIn( host, guest, park =>
		{
			changeTick = host.Level.TickCount;
			Hold( park );
		} );
		CatchUpAndCompare( host, guest );
		host.Enter();
		var hostStream = host.Session!.HostStream!;
		host.Session.End( "The host closed the park." );
		var log = ParkCommandLogFile.Deserialize( hostStream.SessionLog!.Serialize() );
		var released = log.Entries.Where( entry => entry.Command is ReleaseSeatCommand ).Select( entry => ((ReleaseSeatCommand)entry.Command).Seat ).ToArray();
		CollectionAssert.AreEqual( new[] { seat, seat + 1 }, released, "each seat holding staff state was released in a record" );
		Assert.IsTrue( log.Entries.Where( entry => entry.Command is ReleaseSeatCommand ).All( entry => entry.Tick <= log.Checkpoints[^1].Ticks ), "before the last checkpoint" );
		Assert.AreEqual( host.Level.ComputeStateHash(), log.Checkpoints[^1].Hash, "the last checkpoint is the park after the releases" );
		Assert.AreEqual( (0, 0), (host.Level.StaffHands!.All.Count(), host.Economy.Staff.PendingHires.Count), "nothing of the seats' staff is left" );
		var member = host.Economy.Staff.Members.Single( item => item.Id == ids[0].Member );
		Assert.AreEqual( StaffState.Idle, member.State, "the employee dropped back where it was picked up" );
		Assert.IsTrue( host.Economy.Staff.Candidates.Any( item => item.Id == ids[0].Candidate ), "the candidate stays in the pool" );
		Assert.IsNull( FirstReplayMismatchWith( log, _ => { }, changeTick, Hold ), "the log replays to its last checkpoint, the releases included" );
		guest.Enter();
		guest.Session!.End( "The guest left." );
		Assert.AreEqual( (0, 0), (guest.Level.StaffHands!.All.Count(), guest.Economy.Staff.PendingHires.Count), "the guest's park clears them on its own" );
	}

	/// <summary>
	/// <see cref="AlikeIn"/> for a host and every guest of <paramref name="guests"/>: once each guest is at the host's tick with every record applied, <paramref name="change"/> runs on every park.
	/// Returns the tick it ran after.
	/// </summary>
	private long AlikeInAll( Park host, Park[] guests, Action<Park> change )
	{
		host.Holds = true;
		hostTimeFrozenAt = clock;
		var hostStream = host.Session!.HostStream!;
		for ( var round = 0; round < 3000 && guests.Any( guest => guest.Level.TickCount != host.Level.TickCount || guest.Session!.GuestStream!.LastRecord != hostStream.LastRecord
			|| guest.Level.Commands.ScheduledCount > 0 ); round++ )
			Rounds( 1, new[] { host }.Concat( guests ).ToArray() );
		var tick = host.Level.TickCount;
		foreach ( var park in new[] { host }.Concat( guests ) )
		{
			park.Enter();
			change( park );
		}
		hostTimeFrozenAt = null;
		host.Holds = false;
		return tick;
	}

	[TestMethod]
	public void AnEndInsideTheHostsTickRecordsNoReleaseAndClearsTheGuestSeatsStaffAfterTheLog()
	{
		var (host, first, second) = SharedWithTwo();
		Rounds( 20, host, first, second );
		var (one, two) = (first.Level.Commands.Player, second.Level.Commands.Player);
		// the first guest's seat holds an employee and the second's a pending hire, set up alike on every peer (no guest holds staff before C4c-2's switch)
		void Hold( Park park )
		{
			var member = StandingHandyman( park );
			var candidate = park.Economy.Staff.Candidates.First( item => park.Economy.Staff.CanHire( item.Type ) );
			Assert.IsTrue( park.Level.StaffHands!.Apply( new PickUpStaffCommand( member.Id ) { Player = one } ).Accepted );
			Assert.IsTrue( park.Level.StaffHands.Apply( new BeginStaffHireCommand( candidate.Id ) { Player = two } ).Accepted );
		}
		AlikeInAll( host, new[] { first, second }, Hold );
		CutAndCompare( host, first, second );
		var session = host.Session!;
		var hostStream = session.HostStream!;
		// no game path ends a session inside a tick: the test seam does, outside a strict run, which throws at a command applied there
		var strict = ParkCommands.Strict;
		ParkCommands.Strict = false;
		ulong? beforeEnd = null, afterEnd = null;
		try
		{
			host.Level.TickCompleted = _ =>
			{
				host.Level.TickCompleted = null;
				beforeEnd = host.Level.ComputeStateHash();
				session.End( "The host closed the park." );
				afterEnd = host.Level.ComputeStateHash();
			};
			for ( var round = 0; round < 600 && session.EndedWith == null; round++ )
				Rounds( 1, host, first, second );
		}
		finally
		{
			ParkCommands.Strict = strict;
			host.Level.TickCompleted = null;
		}
		Assert.IsNotNull( session.EndedWith, "the session ended inside a tick" );
		var log = ParkCommandLogFile.Deserialize( hostStream.SessionLog!.Serialize() );
		Assert.IsFalse( log.Entries.Any( entry => entry.Command is ReleaseSeatCommand ), "no release was recorded inside the tick" );
		Assert.AreEqual( (0, 0), (host.Level.StaffHands!.All.Count(), host.Economy.Staff.PendingHires.Count), "the park's own end returned the hand and ended the hire" );
		// the return came after the log's last checkpoint: the checkpoint is the park as the end found it, the employee still in hand, and the return then changed it (a checkpoint taken
		// inside a tick bears the tick's number before it ends, so no replay reaches it; the two hashes pin the order instead)
		Assert.AreEqual( beforeEnd, log.Checkpoints[^1].Hash, "the last checkpoint is the park before the employee went back" );
		Assert.AreNotEqual( beforeEnd, afterEnd, "the return came after it" );
	}

	[TestMethod]
	public void AHostsPickUpThenDismissalOfItsCarriedEmployeeReplaysToItsCheckpoints()
	{
		var id = 0;
		var (host, guest, _, _) = Shared( ( park, isHost ) =>
		{
			var member = StandingHandyman( park );
			if ( isHost )
				id = member.Id;
		} );
		Rounds( 20, host, guest );
		host.Enter();
		var carried = host.Economy.Staff.Members.Single( item => item.Id == id );
		var origin = StaffPlacementRules.CellOf( carried )!.Value;
		Assert.IsTrue( host.Level.StaffPlacement!.PickUp( carried ) );
		Rounds( 5, host, guest );
		Assert.IsTrue( host.Issue( new FireStaffCommand( id ) ).Accepted, "the host dismisses the employee it carries" );
		Assert.IsNull( host.Level.StaffHands!.Of( 0 ), "and its hand empties with it" );
		// a log of before C4c dropped the dismissed employee's hand by the tool's next click: that drop finds no hand now and changes nothing
		Assert.IsFalse( host.Issue( new DropStaffCommand( id, origin.X, origin.Y ) ).Accepted );
		host.Level.StaffPlacement.FollowPark();
		Assert.AreEqual( StaffToolKind.None, host.Level.StaffPlacement.Kind );
		Rounds( 20, host, guest );
		CatchUpAndCompare( host, guest );
		host.Enter();
		var hostStream = host.Session!.HostStream!;
		host.Session.End( "The host closed the park." );
		var log = ParkCommandLogFile.Deserialize( hostStream.SessionLog!.Serialize() );
		Assert.IsTrue( log.Entries.Any( entry => entry.Command is FireStaffCommand ), "the dismissal is in the log" );
		Assert.IsNull( FirstReplayMismatch( log, park => StandingHandyman( park ) ), "the log replays to every checkpoint" );
	}

	[TestMethod]
	public void AHostsOrSinglePlayersCarryToolEndsWhenItsEmployeeIsDismissedAndAHireAnotherSeatReservesKeepsItsTool()
	{
		var park = NewPark();
		var member = StandingHandyman( park );
		var placement = park.Level.StaffPlacement!;
		Assert.IsTrue( placement.PickUp( member ) );
		Assert.AreEqual( StaffToolKind.Carry, placement.Kind );
		// a hire of a candidate another seat places is refused before the running tool ends
		var reserved = park.Economy.Staff.Candidates.First( item => park.Economy.Staff.CanHire( item.Type ) );
		Assert.IsTrue( park.Level.StaffHands!.Apply( new BeginStaffHireCommand( reserved.Id ) { Player = 2 } ).Accepted );
		Assert.IsFalse( placement.BeginHire( reserved.Id ) );
		Assert.AreEqual( (StaffToolKind.Carry, member.Id), (placement.Kind, park.Level.StaffHands.Of( 0 )!.Value.Staff), "the carry stays" );
		var other = park.Economy.Staff.Candidates.First( item => item.Id != reserved.Id && park.Economy.Staff.CanHire( item.Type ) );
		Assert.IsTrue( placement.BeginHire( other.Id ), "a free candidate: the carry drops back first" );
		Assert.IsFalse( placement.BeginHire( reserved.Id ) );
		Assert.AreEqual( (StaffToolKind.Place, other.Id), (placement.Kind, park.Economy.Staff.PendingPlacementId), "the pending hire stays" );
		placement.Cancel();

		// the carried employee dismissed from its window: the tool follows the park at the next HUD frame, also one whose pointer no tool dispatches (the ordered pointer routing of a
		// single player's frame without a park event), and a click places nothing
		var view = ViewOf( park );
		Assert.IsTrue( placement.PickUp( member ) );
		Assert.IsTrue( park.Issue( new FireStaffCommand( member.Id ) ).Accepted );
		Assert.IsNull( park.Level.StaffHands.Of( 0 ), "the dismissal emptied the hand" );
		Assert.AreEqual( StaffToolKind.Carry, placement.Kind );
		view.Input( UiInput.Idle( new System.Numerics.Vector2( -1 ) ) with { OrderedEvents = Array.Empty<OrderedInputEvent>() } );
		Assert.AreEqual( StaffToolKind.None, placement.Kind );
		var cell = StaffPlacementRules.CellOf( park.Economy.Staff.Members.First() ) ?? (18, 12);
		Assert.AreEqual( StaffClickResult.None, placement.Click( cell.X, cell.Y ) );
		// a Place tool whose pending hire went (a load, a release) ends too
		Assert.IsTrue( placement.BeginHire( other.Id ) );
		park.Economy.Staff.PendingPlacementId = 0;
		placement.FollowPark();
		Assert.AreEqual( StaffToolKind.None, placement.Kind );
	}
}
