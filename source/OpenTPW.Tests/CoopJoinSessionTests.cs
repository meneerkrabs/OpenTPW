using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Coop;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-snapshot-join] Joining a running session and resync (docs/COOP.md, "Joining a running session") over the in-memory server, with the stream tests' asset-free park. The
/// host's captures are real (cut, session state, save, payload, upload); a guest's candidate park is built by replay: a twin from the identical start driven by the host's records to the cut,
/// its per-player state cleared and the capture's session state applied. So these tests prove the stream's join, the cut, the generations and the handshake; the save's completeness is the
/// game's join's to prove (C2c-2, with data), since the asset-free park has no full restore.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CoopJoinSessionTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;
	private List<Entity>? entities;
	private Level? current;
	private float timeNow, timeDelta;
	private readonly List<Peer> peers = new();
	private readonly List<List<Entity>> built = new();
	private double clock;
	private double? hostTimeFrozenAt;
	private CoopTestNetwork network = null!;
	private Peer host = null!;
	/// <summary>The host's log entries before its stream started: the stream's records are the entries after them.</summary>
	private int hostLogStart;

	/// <summary>One member's game: its park (none while a joiner waits for its capture), the park's entities, its link and its session.</summary>
	private sealed class Peer
	{
		public Level? Level { get; set; }
		public List<Entity> Entities { get; set; } = new();
		public CoopTestNetwork.Member Link { get; init; } = null!;
		public CoopSession? Session { get; set; }
		public ReplayJoin? Join { get; set; }
		/// <summary>The host's park holds (its time scale 0) while the guests catch up at the end.</summary>
		public bool Holds { get; set; }
		/// <summary>This member runs no game frames (a stalled machine).</summary>
		public bool Frozen { get; set; }
		public ParkEconomy Economy => Level!.Park!.Economy;

		public void Enter()
		{
			Entity.All = Entities;
			OpenTPW.Level.Current = Level!;
		}

		/// <summary>One game frame as the game runs it: the pump, the session's step (which may swap the park in), then the park at its session time scale.</summary>
		public void Frame( double seconds )
		{
			Enter();
			Link.Deliver();
			Session?.Step();
			Enter();
			if ( Level == null )
				return;
			Level.SimulationTimeScale = Holds ? 0f : GameFlow.SessionTimeScale( Level ) ?? 1f;
			Time.Update( (float)seconds );
			Level.AdvanceFrame( seconds );
		}

		public ParkCommandResult Issue( ParkCommand command )
		{
			Enter();
			return Level!.Commands.Issue( command );
		}
	}

	/// <summary>The game's join for a test member: the candidate built by replay (<see cref="Twin"/>), the old park torn down and the candidate made the member's park.</summary>
	private sealed class ReplayJoin( CoopJoinSessionTests tests, Peer member ) : ICoopJoinHost
	{
		private readonly Dictionary<Level, List<Entity>> candidates = new();
		public int Builds { get; private set; }
		/// <summary>Breaks the next builds (a failed check), while set.</summary>
		public bool Breaks { get; set; }

		public Level Build( CoopJoinPayload payload )
		{
			Builds++;
			if ( Breaks )
				throw new System.IO.InvalidDataException( "A broken build." );
			var (level, list) = tests.Twin( payload );
			candidates[level] = list;
			return level;
		}

		public void Install( Level? old, Level candidate )
		{
			if ( old != null )
			{
				member.Enter();
				old.Dispose();
				foreach ( var entity in member.Entities.ToArray() )
					entity.Delete();
			}
			member.Level = candidate;
			member.Entities = candidates[candidate];
			candidates.Remove( candidate );
		}
	}

	[TestInitialize]
	public void Initialize()
	{
		entities = Entity.All;
		current = Level.Current;
		(timeNow, timeDelta) = (Time.Now, Time.Delta);
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var peer in peers )
		{
			peer.Session?.End( "The test ended." );
			if ( peer.Level != null )
				peer.Enter();
		}
		foreach ( var list in built )
		{
			Entity.All = list;
			foreach ( var entity in list.ToArray() )
				entity.Delete();
		}
		Entity.All = new List<Entity>();
		fixture?.Cleanup();
		renderer?.Dispose();
		GameEditions.Select( GameEdition.ThemeParkWorld );
		(Time.Now, Time.Delta) = (timeNow, timeDelta);
		if ( entities != null )
			Entity.All = entities;
		Level.Current = current!;
	}

	/// <summary>A park of the identical start, as the stream tests build it.</summary>
	private (Level Level, List<Entity> Entities) NewPark()
	{
		var park = CoopStreamTests.BuildPark( fixture! );
		built.Add( park.Entities );
		// the asset-free park has no name of its own yet and no track renderers (a closing park disposes them)
		typeof( Level ).GetProperty( nameof( Level.ParkName ) )!.SetValue( park.Level, new ParkNameLines( "Test", "Park" ) );
		typeof( Level ).GetField( "trackRenderers", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( park.Level, new Dictionary<TrackFamily, TrackRenderer>() );
		return (park.Level, park.Entities);
	}

	/// <summary>
	/// The candidate a guest builds from <paramref name="payload"/>, by replay: a twin from the identical start, the host's first <see cref="CoopJoinPayload.Records"/> records applied under
	/// the session's rules before their ticks up to the cut's tick, then its per-player state cleared (as a load of the save clears it) and the capture's session state applied.
	/// </summary>
	private (Level Level, List<Entity> Entities) Twin( CoopJoinPayload payload )
	{
		var (level, list) = NewPark();
		var commands = level.Commands;
		var (rules, inTick, rights) = (commands.SessionRules, commands.InTick, commands.Rights);
		commands.SessionRules = true;
		commands.InTick = ParkCommandInTickRule.Drop;
		commands.Rights = CoopHostOnlyRights.Instance;
		var records = host.Level!.Commands.Log.Skip( hostLogStart ).Take( (int)payload.Records ).ToList();
		Assert.AreEqual( payload.Records, records.Count, "the host's log holds every record up to the cut" );
		ParkCommandReplay.Run( commands, records, () => level.Commands.NextTick, () => level.RunTicks( 1 ), payload.Ticks );
		(commands.SessionRules, commands.InTick, commands.Rights) = (rules, inTick, rights);
		typeof( Level ).GetField( "carriedEntries", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, null );
		level.StaffHands?.Clear();
		level.Park!.Economy.Staff.PendingPlacementId = 0;
		level.Park.Economy.Staff.StaleOnPointerId = 0;
		level.Objects.ScriptWorld.SessionTime = null;
		level.ApplyJoinState( payload.State );
		return (level, list);
	}

	/// <summary>The host's local civil time in the tests: an afternoon, moving with the virtual clock.</summary>
	private DateTime HostTime() => new DateTime( 2026, 10, 21, 15, 0, 0 ).AddSeconds( hostTimeFrozenAt ?? clock );

	/// <summary>A session hosted on an identical-start park, with <paramref name="guests"/> guests of the identical start (generation 0) that resync through their replay joins.</summary>
	private Peer[] Hosted( int guests = 0 )
	{
		network = new CoopTestNetwork( () => clock );
		var hostLink = network.Add( host: true );
		var guestLinks = Enumerable.Range( 0, guests ).Select( _ => network.Add() ).ToArray();
		var park = NewPark();
		host = new Peer { Level = park.Level, Entities = park.Entities, Link = hostLink };
		peers.Add( host );
		var members = guestLinks.Select( link =>
		{
			var (level, list) = NewPark();
			var peer = new Peer { Level = level, Entities = list, Link = link };
			peers.Add( peer );
			return peer;
		} ).ToArray();
		// the joins the server announced before the streams existed
		hostLink.Deliver();
		foreach ( var link in guestLinks )
			link.Deliver();
		host.Enter();
		hostLogStart = host.Level!.Commands.Log.Count;
		host.Session = CoopSession.Host( host.Level, hostLink, () => clock, HostTime );
		host.Session.HostStream!.CaptureSliceBudget = TimeSpan.FromMilliseconds( 50 );
		foreach ( var member in members )
		{
			member.Enter();
			member.Join = new ReplayJoin( this, member );
			member.Session = CoopSession.Join( member.Level!, member.Link, () => clock, member.Join );
		}
		return members;
	}

	/// <summary>A member joins the running session: it has no park until its capture is swapped in.</summary>
	private Peer Joiner()
	{
		var link = network.Add();
		var peer = new Peer { Link = link };
		peers.Add( peer );
		peer.Join = new ReplayJoin( this, peer );
		link.Deliver();
		peer.Session = CoopSession.JoinRunning( link, peer.Join, () => clock );
		return peer;
	}

	private void Rounds( int count, double seconds = 1 / 60.0 )
	{
		for ( var round = 0; round < count; round++ )
		{
			clock += seconds;
			foreach ( var peer in peers.Where( peer => !peer.Frozen ).ToArray() )
				peer.Frame( seconds );
		}
	}

	/// <summary>Rounds until <paramref name="done"/> holds (at most <paramref name="limit"/>); fails with <paramref name="what"/> otherwise.</summary>
	private void RoundsUntil( Func<bool> done, string what, int limit = 6000 )
	{
		for ( var round = 0; round < limit && !done(); round++ )
			Rounds( 1 );
		Assert.IsTrue( done(), $"{what}: {Describe()}" );
	}

	/// <summary>Where the session stands, for a test that waited in vain.</summary>
	private string Describe() => $"at {clock:0.0} s the host ran {host.Level?.TickCount} ticks and took {host.Session?.HostStream?.CapturesTaken} captures; the network took "
		+ $"{network.Uploads} uploads and {network.Downloads.Count} downloads; "
		+ string.Join( "; ", peers.Where( peer => peer != host ).Select( peer => $"member {peer.Link.Id} is {peer.Session?.Phase}, assigned {peer.Session?.Assigned} "
		+ $"(the host says {host.Session?.HostStream?.GenerationOf( peer.Link.Id )}, awaiting {host.Session?.HostStream?.IsAwaitingSnapshot( peer.Link.Id )}), built {peer.Join?.Builds}, "
		+ $"ended {peer.Session?.EndedWith ?? "no"}" ) );

	/// <summary>The host holds its park and its clock until every guest stands at the host's tick with every record applied; then their digests, world hashes and logs are equal.</summary>
	private void CatchUpAndCompare( params Peer[] guests )
	{
		host.Holds = true;
		hostTimeFrozenAt = clock;
		var hostStream = host.Session!.HostStream!;
		RoundsUntil( () => guests.All( guest => guest.Level != null && guest.Session!.GuestStream is { } stream && guest.Level.TickCount == host.Level!.TickCount
			&& stream.LastRecord == hostStream.LastRecord && guest.Level.Commands.ScheduledCount == 0 && guest.Link.Waiting == 0 ), "every guest caught up", 3000 );
		hostTimeFrozenAt = null;
		host.Holds = false;
		host.Enter();
		var digest = CoopDigest.Compute( host.Level!, out var world );
		foreach ( var guest in guests )
		{
			Assert.IsNull( guest.Session!.GuestStream!.Desync, $"{guest.Session.GuestStream.Desync}" );
			guest.Enter();
			Assert.AreEqual( digest, CoopDigest.Compute( guest.Level!, out var guestWorld ), $"guest {guest.Link.Id}: the session digest" );
			Assert.AreEqual( world, guestWorld, $"guest {guest.Link.Id}: the world hash" );
			var since = host.Level!.Commands.Log.Count - guest.Level!.Commands.Log.Count;
			CollectionAssert.AreEqual( host.Level.Commands.Log.Skip( Math.Max( 0, since ) ).ToArray(), guest.Level.Commands.Log.ToArray(), $"guest {guest.Link.Id}: its stream's commands" );
		}
		Assert.IsNull( host.Session.HostStream.Failure );
	}

	/// <summary>The host's scripted build session of <paramref name="rounds"/> frames.</summary>
	private void Build( int rounds, int seed )
	{
		var random = new Random( seed );
		for ( var round = 0; round < rounds; round++ )
		{
			if ( round % 40 == 7 )
				host.Issue( new LayLineCommand( CellToolMode.Path, -1, 4 + random.Next( 6 ), 20, 4 + random.Next( 6 ), 26 ) );
			if ( round % 50 == 21 )
				host.Issue( new SetEntranceFeeCommand( random.Next( 1, 20 ) ) );
			if ( round % 70 == 33 )
				host.Issue( new RemovePathCellCommand( 4 + random.Next( 6 ), 22 ) );
			Rounds( 1 );
		}
	}

	[TestMethod]
	public void ALateJoinerGetsTheHostsParkAndFollowsIt()
	{
		Hosted();
		Assert.IsTrue( host.Issue( new SetGameSpeedCommand( GameSpeed.Fast ) ).Accepted );
		Build( 240, 3 );
		var joiner = Joiner();
		Assert.IsNull( joiner.Level );
		Assert.AreEqual( CoopGuestPhase.Joining, joiner.Session!.Phase );
		// the host builds on while the joiner waits, downloads and builds; the frames after its join are buffered, a few of them lost on their way
		joiner.Link.DropFrame = seq => seq % 7 == 3;
		network.TransferSeconds = 0.5;
		RoundsUntil( () => joiner.Level != null, "the joiner swapped its capture in" );
		joiner.Link.DropFrame = null;
		Assert.AreEqual( CoopGuestPhase.Following, joiner.Session.Phase );
		Assert.AreEqual( 1u, joiner.Session.Assigned );
		Assert.AreEqual( 1, host.Session!.HostStream!.CapturesTaken );
		Assert.IsTrue( joiner.Level!.IsCoopGuestPark && joiner.Level.CoopSession == joiner.Session, "a guest's park from its build" );
		Assert.AreEqual( 1u, joiner.Session.GuestStream!.Generation );
		Build( 300, 4 );
		CatchUpAndCompare( joiner );
		Assert.IsNull( host.Session.HostStream.JoinWindowOf( joiner.Link.Id ), "caught up: an ordinary following guest" );
		Assert.IsTrue( host.Session.HostStream.WaitedFor.Contains( joiner.Link.Id ) );
		Assert.IsNull( joiner.Link.LeftWith );
	}

	[TestMethod]
	public void ADesyncedGuestResyncsWithoutLeavingTheSession()
	{
		var guest = Hosted( 1 ).Single();
		Build( 120, 5 );
		// a fault only the session digest sees: the guest's park drifts from the host's
		guest.Enter();
		guest.Economy.Ledger.Post( LedgerCategory.OtherIncome, 1 );
		var first = guest.Level;
		RoundsUntil( () => guest.Session!.Phase == CoopGuestPhase.Resyncing, "the guest found the desync and asked for the park" );
		Assert.IsNotNull( guest.Session!.GuestStream!.Desync );
		RoundsUntil( () => guest.Level != first, "the guest swapped the host's park in" );
		Assert.AreEqual( CoopGuestPhase.Following, guest.Session.Phase );
		Assert.AreEqual( 1u, guest.Session.GuestStream!.Generation );
		Assert.IsNull( guest.Link.LeftWith, "it never left the session" );
		Assert.AreEqual( 1, guest.Session.FailedJoins, "a desync soon after it began following counts as a failed join" );
		Build( 200, 6 );
		CatchUpAndCompare( guest );
		Assert.AreEqual( 1, host.Session!.HostStream!.CapturesTaken );
	}

	[TestMethod]
	public void ALostAssignmentAndALostAnswerAreAskedForAgainAndMakeNoSecondCapture()
	{
		Hosted();
		Build( 60, 7 );
		var (lostAssignment, lostAnswer) = (false, false);
		var joiner = Joiner();
		// the host's first assignment and first answer to the joiner are lost on their way
		joiner.Link.DropMessage = ( from, payload ) => from == host.Link.Id && CoopStreamMessage.Decode( payload ) switch
		{
			CoopSnapshotAssigned when !lostAssignment => lostAssignment = true,
			CoopSnapshotReady when !lostAnswer => lostAnswer = true,
			_ => false
		};
		RoundsUntil( () => joiner.Level != null, "the joiner asked again after its wait and was served" );
		Assert.IsTrue( lostAssignment && lostAnswer );
		Assert.AreEqual( 1, host.Session!.HostStream!.CapturesTaken, "a re-ask gets the outstanding answer again, not a new capture" );
		Assert.IsTrue( joiner.Link.Sent.Count( sent => sent.Message is CoopSnapshotRequest ) >= 2 );
		Build( 120, 8 );
		CatchUpAndCompare( joiner );
	}

	[TestMethod]
	public void AnUploadSlowerThanTheReaskAndAJoinersResumeMakeNoSecondCapture()
	{
		Hosted();
		Build( 60, 9 );
		network.TransferSeconds = 45;
		var joiner = Joiner();
		RoundsUntil( () => joiner.Session!.Assigned == 1, "assigned" );
		// the joiner's socket drops and resumes while the host uploads, and its wait runs out before the answer comes
		Rounds( 60 );
		joiner.Link.Drop();
		Rounds( 30 );
		joiner.Link.Resume();
		RoundsUntil( () => joiner.Level != null, "served once the upload and the download are done", 20000 );
		Assert.AreEqual( 1, host.Session!.HostStream!.CapturesTaken );
		Assert.IsTrue( joiner.Link.Sent.Count( sent => sent.Message is CoopSnapshotRequest { Reason: CoopSnapshotReason.NoAnswer } ) >= 1, "it asked again while it waited" );
		Build( 60, 10 );
		CatchUpAndCompare( joiner );
	}

	[TestMethod]
	public void TwoJoinersWithinTheCollectionShareOneCapture()
	{
		Hosted();
		Build( 60, 11 );
		var first = Joiner();
		Rounds( 30 );
		var second = Joiner();
		RoundsUntil( () => first.Level != null && second.Level != null, "both swapped the capture in" );
		Assert.AreEqual( 1, host.Session!.HostStream!.CapturesTaken );
		Assert.AreEqual( (1u, 1u), (first.Session!.Assigned, second.Session!.Assigned) );
		// a member that joins once a capture is on its way gets the next
		network.TransferSeconds = 1;
		var third = Joiner();
		Rounds( 150 );
		var fourth = Joiner();
		RoundsUntil( () => third.Level != null && fourth.Level != null, "the later two swapped their captures in" );
		Assert.AreEqual( (3, 2u, 3u), (host.Session.HostStream.CapturesTaken, third.Session!.Assigned, fourth.Session!.Assigned) );
		Build( 120, 12 );
		CatchUpAndCompare( first, second, third, fourth );
	}

	[TestMethod]
	public void ARequestStormMakesOneCaptureAndARepeatedAnswerChangesNothingForAMemberThatFollows()
	{
		Hosted();
		Build( 60, 18 );
		var joiner = Joiner();
		// the joiner asks again and again while it waits (a socket that drops and resumes, the host's own reconnects)
		for ( var ask = 0; ask < 5; ask++ )
		{
			joiner.Link.Send( host.Link.Id, new CoopSnapshotRequest( 0, CoopSnapshotReason.NoAnswer, null ).Encode() );
			Rounds( 10 );
		}
		// its first report of the capture's generation is lost, then it reconnects: the host, which has not heard it follow, sends the assignment and its answer again, which the member
		// ignores
		var lostFirst = false;
		host.Link.DropMessage = ( from, payload ) => from == joiner.Link.Id && !lostFirst && CoopStreamMessage.Decode( payload ) is CoopProgress { Generation: 1 } && (lostFirst = true);
		RoundsUntil( () => joiner.Level != null, "joined" );
		Assert.AreEqual( 1, host.Session!.HostStream!.CapturesTaken );
		var level = joiner.Level;
		Rounds( 2 );
		Assert.IsTrue( lostFirst, "the first report was lost" );
		Assert.IsTrue( host.Session.HostStream.IsAwaitingSnapshot( joiner.Link.Id ) || host.Session.HostStream.WaitedFor.Contains( joiner.Link.Id ) );
		joiner.Link.Drop();
		Rounds( 10 );
		joiner.Link.Resume();
		Rounds( 60 );
		Assert.AreEqual( (1, 1), (host.Session.HostStream.CapturesTaken, joiner.Join!.Builds) );
		Assert.AreSame( level, joiner.Level );
		Assert.IsFalse( joiner.Session!.GuestStream!.Silenced );
		Assert.AreEqual( CoopGuestPhase.Following, joiner.Session.Phase );
		Build( 60, 19 );
		CatchUpAndCompare( joiner );
	}

	[TestMethod]
	public void AGuestTooFarBackResyncsWithoutLeaving()
	{
		var guest = Hosted( 1 ).Single();
		Build( 120, 20 );
		Assert.IsTrue( host.Session!.HostStream!.OldestFrame > 1 );
		// a guest that lost far more than the host holds
		guest.Link.Send( host.Link.Id, new CoopResendRequest( 1, 0 ).Encode() );
		var first = guest.Level;
		RoundsUntil( () => guest.Level != first, "the guest swapped the host's park in" );
		Assert.IsTrue( guest.Link.Sent.Any( sent => sent.Message is CoopSnapshotRequest { Reason: CoopSnapshotReason.TooFarBack } ) );
		Assert.IsNull( guest.Link.LeftWith );
		Build( 120, 21 );
		CatchUpAndCompare( guest );
	}

	[TestMethod]
	public void AJoinerCatchesUpWithinItsOwnWindowWhileTheParkRunsOn()
	{
		Hosted();
		Assert.IsTrue( host.Issue( new SetGameSpeedCommand( GameSpeed.Fastest ) ).Accepted );
		Build( 60, 13 );
		network.TransferSeconds = 3;
		var joiner = Joiner();
		var stream = host.Session!.HostStream!;
		RoundsUntil( () => stream.IsAwaitingSnapshot( joiner.Link.Id ), "assigned" );
		Assert.IsFalse( stream.WaitedFor.Contains( joiner.Link.Id ), "a member awaiting its capture is not waited for" );
		RoundsUntil( () => joiner.Level != null, "joined" );
		RoundsUntil( () => !stream.IsAwaitingSnapshot( joiner.Link.Id ), "its first report" );
		var window = stream.JoinWindowOf( joiner.Link.Id );
		Assert.IsNotNull( window, "a joiner further behind than the window catches up within one of its own" );
		Assert.IsTrue( window > stream.WindowTicks );
		Assert.IsTrue( stream.WaitedFor.Contains( joiner.Link.Id ), "the park waits for it, within its join window" );
		var ticks = host.Level!.TickCount;
		Rounds( 30 );
		Assert.IsTrue( host.Level.TickCount > ticks, "the park runs on while the joiner catches up" );
		RoundsUntil( () => stream.JoinWindowOf( joiner.Link.Id ) == null, "its lag came within the ordinary window" );
		CatchUpAndCompare( joiner );
	}

	[TestMethod]
	public void AFailedCaptureIsAskedForAgainAndAPermanentOneLeaves()
	{
		Hosted();
		Build( 60, 14 );
		// the first upload fails for now: the joiner waits as the host says, asks again, and the next capture serves it
		network.FailUpload = attempt => attempt == 1 ? CoopSnapshotTransfer.Failed( "Too many uploads.", retryAfter: TimeSpan.FromSeconds( 2 ) ) : null;
		var joiner = Joiner();
		RoundsUntil( () => joiner.Level != null, "served by the second capture", 20000 );
		Assert.AreEqual( 2, host.Session!.HostStream!.CapturesTaken );
		Assert.AreEqual( 2u, joiner.Session!.Assigned );
		Assert.AreEqual( 0, joiner.Session.FailedJoins, "a failure for now is no failed join" );

		// a park too large to upload: the next joiner leaves with the reason
		network.FailUpload = _ => CoopSnapshotTransfer.Failed( "The snapshot is too large.", tooLarge: true );
		var late = Joiner();
		RoundsUntil( () => late.Session!.EndedWith != null, "the late joiner left" );
		StringAssert.Contains( late.Link.LeftWith, "cannot be joined" );
		Assert.IsNull( late.Level );
	}

	[TestMethod]
	public void FailedDownloadsCountAndTheThirdLeavesButAMissingSnapshotDoesNot()
	{
		Hosted();
		Build( 60, 15 );
		var joiner = Joiner();
		var answers = new List<string>();
		// the first download finds the snapshot rotated out (not counted), the next two fail (counted), the third failure leaves
		network.FailDownload = ( member, sha ) =>
		{
			answers.Add( sha );
			return answers.Count == 1 ? CoopSnapshotTransfer.Failed( "No such snapshot.", missing: true ) : CoopSnapshotTransfer.Failed( "The line broke." );
		};
		RoundsUntil( () => joiner.Session!.EndedWith != null, "the joiner left after its third failed join", 30000 );
		Assert.AreEqual( 4, answers.Count, "a missing snapshot, then three failed downloads" );
		Assert.IsTrue( joiner.Link.Sent.Any( sent => sent.Message is CoopSnapshotRequest { Reason: CoopSnapshotReason.Missing } ) );
		StringAssert.Contains( joiner.Link.LeftWith, "could not be joined" );
	}

	[TestMethod]
	public void StaleMessagesOfAnOldGenerationChangeNothing()
	{
		var guest = Hosted( 1 ).Single();
		Build( 60, 16 );
		guest.Enter();
		guest.Economy.Ledger.Post( LedgerCategory.OtherIncome, 1 );
		var first = guest.Level;
		RoundsUntil( () => guest.Level != first, "resynced to generation 1" );
		Build( 60, 17 );
		var stream = host.Session!.HostStream!;
		Assert.IsTrue( stream.WaitedFor.Contains( guest.Link.Id ) );
		// a desync, a resend from before the history and a progress of generation 0, late on their way: the host ignores them
		guest.Link.Send( host.Link.Id, new CoopDesyncReport( 9, CoopDesyncKind.Digest, 5, 1, 2, 1, 1 ).Encode() );
		guest.Link.Send( host.Link.Id, new CoopResendRequest( 1, 0 ).Encode() );
		guest.Link.Send( host.Link.Id, new CoopProgress( CoopGuestState.TooFarBack, 1, 1, 0 ).Encode() );
		Rounds( 10 );
		Assert.IsFalse( stream.IsDesynced( guest.Link.Id ) || stream.IsTooFarBack( guest.Link.Id ) );
		Assert.IsTrue( stream.WaitedFor.Contains( guest.Link.Id ) );
		// and the host's too-far-back of generation 0 stops nothing on the guest
		host.Link.Send( guest.Link.Id, new CoopTooFarBack( 1 ).Encode() );
		Rounds( 10 );
		Assert.IsFalse( guest.Session!.GuestStream!.Stopped );
		CatchUpAndCompare( guest );
	}

	[TestMethod]
	public void AMemberAheadOfTheHostsNumbersIsOutOfStepAndLeaves()
	{
		var guest = Hosted( 1 ).Single();
		Rounds( 30 );
		guest.Link.Send( host.Link.Id, new CoopSnapshotRequest( 5, CoopSnapshotReason.Desync, null ).Encode() );
		RoundsUntil( () => guest.Session!.EndedWith != null, "the guest heard it is out of step" );
		Assert.AreEqual( 0, host.Session!.HostStream!.CapturesTaken );
	}
}
