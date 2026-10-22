using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
public partial class CoopJoinSessionTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;
	private List<Entity>? entities;
	private Level? current;
	private float timeNow, timeDelta;
	private CameraMode? camera;
	/// <summary>The view statics a construction scene's swap changes: the park camera's extent and start, and the audio listener.</summary>
	private (float Extent, global::OpenTPW.Vector3 Target, global::OpenTPW.Vector3 Position, global::OpenTPW.Vector3 Forward, global::OpenTPW.Vector3 Front, global::OpenTPW.Vector3 Up) view;
	private bool? audioWas;
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
		/// <summary>The in-memory link (the tests' controls); a member of the real links has none.</summary>
		public CoopTestNetwork.Member Link { get; init; } = null!;
		/// <summary>The real link's pump (its online session's), which raises what arrived; null for the in-memory link.</summary>
		public Action? Pump { get; init; }
		public CoopSession? Session { get; set; }
		public ReplayJoin? Join { get; set; }
		/// <summary>The host's park holds (its time scale 0) while the guests catch up at the end.</summary>
		public bool Holds { get; set; }
		/// <summary>This member runs no game frames (a stalled machine).</summary>
		public bool Frozen { get; set; }
		/// <summary>The member's game when it is the real <see cref="GameFlow"/>: its frame steps the session through the flow, whose swap makes the park the member's.</summary>
		public GameFlow? Flow { get; set; }
		/// <summary>A flow member's builds (<see cref="FlowBuild"/>): how many began, the frames the build of the given number (1 first) takes, how many were abandoned.</summary>
		public int FlowBuilds { get; set; }
		public Func<int, int> FlowSteps { get; set; } = _ => 1;
		public int FlowAbandoned { get; set; }
		/// <summary>The flow member's next swap fails before the live scene is touched, once.</summary>
		public bool StageFails { get; set; }
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
			if ( Pump != null )
				Pump();
			else
				Link.Deliver();
			if ( Flow != null )
			{
				Flow.StepCoopSession();
				if ( !ReferenceEquals( Flow.Level, Level ) )
					(Level, Entities) = (Flow.Level, Entity.All);
			}
			else
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

	/// <summary>
	/// The game's join for a test member: the candidate built by replay (<see cref="Twin"/>) over <see cref="Steps"/> frames, the old park torn down and the candidate made the member's
	/// park.
	/// </summary>
	private sealed class ReplayJoin( CoopJoinSessionTests tests, Peer member ) : ICoopJoinHost
	{
		public CoopJoinSessionTests Tests { get; } = tests;
		public int Builds { get; private set; }
		/// <summary>Breaks the next builds (a failed check), while set.</summary>
		public bool Breaks { get; set; }
		/// <summary>Builds parks that differ from the host's at the cut, while set (a restore that misses something).</summary>
		public bool Drifts { get; set; }
		/// <summary>The next build throws this at its last step (a restore that refuses the payload for its own reasons), once.</summary>
		public Exception? Throws { get; set; }
		/// <summary>The next build's step of this number throws this, once.</summary>
		public (int Step, Exception Error)? ThrowsAtStep { get; set; }
		/// <summary>The next build refuses as it starts (this game's files or mods differ from the host's), once.</summary>
		public CoopJoinRefusedException? Refuses { get; set; }
		/// <summary>The next install fails halfway, once.</summary>
		public bool InstallFails { get; set; }
		/// <summary>The frames the build of the given number (1 first) takes; a build of one frame by default.</summary>
		public Func<int, int> Steps { get; set; } = _ => 1;
		/// <summary>The candidates released without being installed.</summary>
		public int Discarded { get; set; }
		/// <summary>The builds abandoned, with a candidate or before one.</summary>
		public int Abandoned { get; set; }

		public ICoopJoinBuild Build( CoopJoinPayload payload )
		{
			Builds++;
			if ( Refuses is { } refusal )
			{
				Refuses = null;
				throw refusal;
			}
			return new ReplayBuild( this, payload, Steps( Builds ) );
		}

		public void Install( Level? old, ICoopJoinBuild build )
		{
			if ( InstallFails )
			{
				InstallFails = false;
				throw new InvalidOperationException( "The park's renderer could not start." );
			}
			var replay = (ReplayBuild)build;
			if ( old != null )
			{
				member.Enter();
				old.Dispose();
				foreach ( var entity in member.Entities.ToArray() )
					entity.Delete();
			}
			member.Level = replay.Candidate;
			member.Entities = replay.Entities;
			replay.Installed = true;
		}
	}

	/// <summary>A replay build: nothing until its last step, which builds the twin.</summary>
	private sealed class ReplayBuild( ReplayJoin join, CoopJoinPayload payload, int steps ) : ICoopJoinBuild
	{
		private int done;
		private Level? candidate;
		private bool abandoned;
		public List<Entity> Entities { get; private set; } = new();
		public bool Installed { get; set; }
		public Level Candidate => candidate ?? throw new InvalidOperationException( "The park is not built yet." );

		public bool Step()
		{
			done++;
			if ( join.ThrowsAtStep is { } fault && fault.Step == done )
			{
				join.ThrowsAtStep = null;
				throw fault.Error;
			}
			if ( done < steps )
				return false;
			if ( join.Breaks )
				throw new System.IO.InvalidDataException( "A broken build." );
			if ( join.Throws is { } error )
			{
				join.Throws = null;
				throw error;
			}
			var (level, list) = join.Tests.Twin( payload );
			if ( join.Drifts )
				level.Park!.Economy.Ledger.Post( LedgerCategory.OtherIncome, 1 );
			(candidate, Entities) = (level, list);
			return true;
		}

		public void Abandon()
		{
			if ( Installed || abandoned )
				return;
			abandoned = true;
			join.Abandoned++;
			if ( candidate == null )
				return;
			join.Discarded++;
			Entity.All = Entities;
			candidate.Dispose();
			foreach ( var entity in Entities.ToArray() )
				entity.Delete();
		}
	}

	[TestInitialize]
	public void Initialize()
	{
		entities = Entity.All;
		current = Level.Current;
		(timeNow, timeDelta) = (Time.Now, Time.Delta);
		camera = Camera.Mode;
		view = (ParkCameraMode.TargetExtent, ParkCameraMode.InitialTarget, AudioListener.Position, AudioListener.Forward, AudioListener.Front, AudioListener.Up);
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
		if ( camera != null )
			Camera.Mode = camera;
		(ParkCameraMode.TargetExtent, ParkCameraMode.InitialTarget) = (view.Extent, view.Target);
		AudioListener.RestoreCaptured( view.Position, view.Forward );
		AudioListener.RestoreCapturedAxes( view.Front, view.Up );
		if ( audioWas is { } was )
			GameAudio.Enabled = was;
	}

	/// <summary>A park of the identical start, as the stream tests build it (into <paramref name="into"/>, a staged scene's list, when given).</summary>
	private (Level Level, List<Entity> Entities) NewPark( List<Entity>? into = null )
	{
		var park = CoopStreamTests.BuildPark( fixture!, into );
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
	private (Level Level, List<Entity> Entities) Twin( CoopJoinPayload payload, List<Entity>? into = null )
	{
		var (level, list) = NewPark( into );
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

	/// <summary>
	/// A session hosted on an identical-start park, with <paramref name="guests"/> guests of the identical start (generation 0) that resync through their replay joins, or through the game's
	/// join with <paramref name="flowGuests"/>.
	/// </summary>
	private Peer[] Hosted( int guests = 0, bool flowGuests = false )
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
			if ( flowGuests )
			{
				member.Flow = NewFlow( member );
				SetField( member.Flow, "<Level>k__BackingField", member.Level );
				member.Session = member.Flow.FollowSharedPark( member.Link, () => true );
				continue;
			}
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
		// a following guest's acknowledgements let the host trim its history: the joiner's frames from the cut stay for it
		var other = Hosted( 1 ).Single();
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
		Assert.IsTrue( host.Session.HostStream.OldestFrame > 1, "the host trimmed its history" );
		CatchUpAndCompare( joiner, other );
		Assert.IsNull( host.Session.HostStream.JoinWindowOf( joiner.Link.Id ), "caught up: an ordinary following guest" );
		Assert.IsTrue( host.Session.HostStream.WaitedFor.Contains( joiner.Link.Id ) );
		Assert.IsNull( joiner.Link.LeftWith );
		// what the join cost (docs/COOP.md, "Measurements"): the host's capture over frames and its upload; the joiner's download, decode, check, swap and catch-up
		var capture = host.Session.HostStream.CaptureTimings.Single();
		Assert.IsTrue( capture is { Generation: 1, Save: { Status: CoopTimingStatus.Measured, Steps: > 0 }, Compression.Steps: > 0, PayloadBytes: > 0, UploadSeconds: not null }
			&& capture.CutMilliseconds > 0 && capture.DigestMilliseconds > 0 && capture.BodyBytes > capture.SaveBytes && capture.Frames >= 3, capture.ToString() );
		var joined = joiner.Session.JoinTimings.Single();
		Assert.IsTrue( joined is { Generation: 1, Outcome: "joined", DownloadAttempts: 1, CatchUpTicks: not null } && joined.PayloadBytes == capture.PayloadBytes
			&& joined.DecodeMilliseconds > 0 && joined.CheckMilliseconds > 0 && joined.DownloadSeconds > 0, joined.ToString() );
		Assert.AreEqual( 0, joined.Build.Count, "the replay build keeps no stages of its own" );
		Assert.AreEqual( 0, other.Session!.JoinTimings.Count, "a guest that followed from the session's start built nothing" );
	}

	[TestMethod]
	public void AJoinersCatchUpEndsOnlyAfterAFrameFromAfterTheCut()
	{
		Hosted();
		Build( 60, 41 );
		// the host's park stands still: after the cut it publishes nothing, so the joiner cannot know where the host is
		host.Holds = true;
		var joiner = Joiner();
		RoundsUntil( () => joiner.Level != null, "the joiner swapped its capture in" );
		Rounds( 30 );
		var joined = joiner.Session!.JoinTimings.Single();
		Assert.IsTrue( joined is { Outcome: "joined", CatchUpTicks: null, CatchUpFrames: null, CatchUpSeconds: null }, $"no catch-up before a frame from after the cut: {joined}" );
		// the park runs on: the host's frames come, and the joiner has caught up once it follows them
		host.Holds = false;
		RoundsUntil( () => joined.CatchUpTicks != null, "the joiner caught up" );
		Assert.IsTrue( joined is { CatchUpTicks: > 0, CatchUpFrames: > 30 } && joined.CatchUpSeconds > 0, joined.ToString() );
		CatchUpAndCompare( joiner );
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
		Assert.AreEqual( "resynced", guest.Session.JoinTimings.Single().Outcome, "a resync over the guest's park" );
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
	public void AnOutstandingAssignmentIsRepeatedOnAReconnectOnTheHostsResumeAndToAnOldStream()
	{
		var guest = Hosted( 1 ).Single();
		Build( 60, 22 );
		int Assignments() => host.Link.Sent.Count( sent => sent.To == guest.Link.Id && sent.Message is CoopSnapshotAssigned );
		// the guest's park desyncs; every assignment the host sends it is lost while it stays away and comes back
		guest.Link.DropMessage = ( from, payload ) => from == host.Link.Id && CoopStreamMessage.Decode( payload ) is CoopSnapshotAssigned && Assignments() <= 3;
		network.TransferSeconds = 20;
		guest.Enter();
		guest.Economy.Ledger.Post( LedgerCategory.OtherIncome, 1 );
		RoundsUntil( () => host.Session!.HostStream!.GenerationOf( guest.Link.Id ) == 1, "the host assigned the guest a capture" );
		var sent = Assignments();
		// the old stream's reports (generation 0) are answered with the assignment, at most once a second
		Rounds( 150 );
		Assert.IsTrue( Assignments() > sent && Assignments() <= sent + 3, $"repeated to the old stream: {Assignments() - sent}" );
		// the guest's machine runs no frames now, so neither its re-asks nor its old stream's reports bring the repeats
		guest.Frozen = true;
		Rounds( 120 );
		sent = Assignments();
		guest.Link.Drop();
		Rounds( 10 );
		guest.Link.Resume();
		Rounds( 5 );
		Assert.AreEqual( sent + 1, Assignments(), "repeated on the guest's reconnect" );
		sent = Assignments();
		host.Link.Drop();
		Rounds( 10 );
		host.Link.Resume();
		Rounds( 5 );
		Assert.AreEqual( sent + 1, Assignments(), "repeated on the host's own resume" );
		guest.Frozen = false;
		RoundsUntil( () => guest.Session!.GuestStream!.Generation == 1, "the guest swapped the capture in", 20000 );
		Assert.AreEqual( 1, host.Session!.HostStream!.CapturesTaken );
		CatchUpAndCompare( guest );
	}

	[TestMethod]
	public void AFollowingGuestAssignedACaptureStopsItsOldParkUntilTheSwap()
	{
		var guest = Hosted( 1 ).Single();
		Build( 60, 24 );
		network.TransferSeconds = 2;
		// the host assigns the following guest a capture (a request that reports a failure its park never had)
		guest.Link.Send( host.Link.Id, new CoopSnapshotRequest( 0, CoopSnapshotReason.FailedCheck, null ).Encode() );
		var old = guest.Level!;
		RoundsUntil( () => guest.Session!.Assigned == 1, "assigned" );
		Assert.IsTrue( guest.Session!.GuestStream!.Silenced && guest.Session.GuestStream.Stopped );
		Assert.AreEqual( CoopGuestPhase.Resyncing, guest.Session.Phase );
		var ticks = old.TickCount;
		Rounds( 60 );
		Assert.AreSame( old, guest.Level, "still the old park while the capture is on its way" );
		Assert.AreEqual( ticks, old.TickCount, "the old park stands still" );
		Assert.IsFalse( guest.Issue( new SetEntranceFeeCommand( 3 ) ).Accepted, "and its clicks are refused" );
		RoundsUntil( () => guest.Level != old, "swapped" );
		CatchUpAndCompare( guest );
	}

	[TestMethod]
	public void ACandidateThatDiffersAtTheCutIsAFailedJoinAndNeverFollowed()
	{
		Hosted();
		Build( 60, 23 );
		var joiner = Joiner();
		joiner.Join!.Drifts = true;
		RoundsUntil( () => joiner.Session!.FailedJoins == 1, "the first candidate failed its check" );
		Assert.IsNull( joiner.Level, "a candidate that differs from the host's park at the cut is never swapped in" );
		Assert.IsTrue( joiner.Link.Sent.Any( sent => sent.Message is CoopSnapshotRequest { Reason: CoopSnapshotReason.FailedCheck } ) );
		joiner.Join.Drifts = false;
		RoundsUntil( () => joiner.Level != null, "the next capture builds right", 20000 );
		Assert.AreEqual( (2, 2), (host.Session!.HostStream!.CapturesTaken, joiner.Join.Builds) );
		Assert.AreEqual( 1, joiner.Join.Discarded, "the candidate that differed was released" );
		CollectionAssert.AreEqual( new[] { "failed its check", "joined" }, joiner.Session!.JoinTimings.Select( timing => timing.Outcome ).ToArray() );
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
		// a waiting request that names the generation it follows gets that answer again, never a new capture
		joiner.Link.Send( host.Link.Id, new CoopSnapshotRequest( 1, CoopSnapshotReason.NoAnswer, null ) { Generation = 1 }.Encode() );
		Rounds( 60 * 15 );
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

	/// <summary>
	/// [EXT:online-coop-snapshot-join] A server that needs a frame the host trimmed (docs/COOP.md, "The host's trimmed frame"): the host assigns every member a capture and the frames go on
	/// past the gap; each member resyncs from its capture without leaving and without a failed join, and follows the host's park.
	/// </summary>
	[TestMethod]
	public void EveryMemberResyncsWhenTheServerNeedsAFrameTheHostNoLongerHolds()
	{
		var members = Hosted( 2 );
		Build( 120, 41 );
		Assert.IsTrue( host.Session!.HostStream!.OldestFrame > 2 );
		var parks = members.Select( member => member.Level ).ToArray();
		network.Restart( lastFrame: 1, seqKnown: true );
		RoundsUntil( () => members.All( member => member.Level != null && !parks.Contains( member.Level ) && member.Session!.Phase == CoopGuestPhase.Following ),
			"every member swapped the host's park in again" );
		Assert.AreEqual( 1, network.Skips.Count );
		foreach ( var member in members )
		{
			Assert.IsNull( member.Session!.EndedWith );
			Assert.IsNull( member.Link.LeftWith );
			Assert.AreEqual( 0, member.Session.FailedJoins, "a forced resync is no failed join" );
		}
		Build( 120, 42 );
		var stream = host.Session!.HostStream!;
		Assert.IsTrue( stream.OldestFrame > network.Skips[0], "the members' acknowledgements trimmed the history past the skip" );
		// the server comes back behind the skip again (a second crash to the same stored position): the frames from the skip are gone too, so the host skips anew
		parks = members.Select( member => member.Level ).ToArray();
		network.Restart( lastFrame: 1, seqKnown: true );
		RoundsUntil( () => members.All( member => member.Level != null && !parks.Contains( member.Level ) && member.Session!.Phase == CoopGuestPhase.Following ),
			"every member swapped the host's park in once more" );
		Assert.IsNull( stream.Failure );
		Assert.AreEqual( 2, network.Skips.Count );
		Assert.IsTrue( network.Skips[1] > network.Skips[0] );
		Build( 120, 43 );
		CatchUpAndCompare( members );
	}

	[TestMethod]
	public void AMemberAwayAtTheHostsSkipGetsTheParkAgainWhenItComesBack()
	{
		var members = Hosted( 2 );
		Build( 120, 45 );
		Assert.IsTrue( host.Session!.HostStream!.OldestFrame > 2 );
		var (there, away) = (members[0], members[1]);
		var parks = members.Select( member => member.Level ).ToArray();
		// the server comes back behind the frames the host holds, and one member's new socket drops before it reads a word: that member is away at the host's skip
		network.Restart( lastFrame: 1, seqKnown: true );
		away.Link.Drop();
		RoundsUntil( () => there.Level != null && !parks.Contains( there.Level ) && there.Session!.Phase == CoopGuestPhase.Following, "the member there swapped the host's park in" );
		Assert.AreEqual( 1, network.Skips.Count );
		Assert.AreSame( parks[1], away.Level, "the member away still has its old park" );
		away.Link.Resume();
		RoundsUntil( () => away.Level != null && !parks.Contains( away.Level ) && away.Session!.Phase == CoopGuestPhase.Following, "the member back swapped the host's park in" );
		Assert.IsNull( away.Session!.EndedWith );
		Assert.AreEqual( 0, away.Session.FailedJoins, "a forced resync is no failed join" );
		Build( 120, 46 );
		CatchUpAndCompare( members );
	}

	[TestMethod]
	public void ACaptureOnItsWayUpAtTheHostsSkipServesNoOneAndTheNextServesEveryMember()
	{
		var members = Hosted( 1 );
		Build( 120, 47 );
		var stream = host.Session!.HostStream!;
		Assert.IsTrue( stream.OldestFrame > 2 );
		// a joiner's capture is on its way up when the server comes back behind the frames the host holds
		network.UploadSeconds = attempt => attempt == 1 ? 10 : null;
		var joiner = Joiner();
		RoundsUntil( () => network.Uploads == 1, "the joiner's capture is on its way up" );
		var parks = members.Select( member => member.Level ).ToArray();
		network.Restart( lastFrame: 1, seqKnown: true );
		RoundsUntil( () => joiner.Level != null && joiner.Session!.Phase == CoopGuestPhase.Following
			&& members.All( member => member.Level != null && !parks.Contains( member.Level ) && member.Session!.Phase == CoopGuestPhase.Following ), "every member swapped the host's park in", 20000 );
		Assert.AreEqual( 1, network.Skips.Count );
		Assert.AreEqual( 2, stream.CapturesTaken, "the skip's own capture" );
		Assert.IsFalse( host.Link.Sent.Any( sent => sent.Message is CoopSnapshotReady { Generation: 1 } ), "the capture on its way at the skip served no one" );
		Assert.AreEqual( 2u, joiner.Session!.Assigned );
		Assert.AreEqual( 0, joiner.Session.FailedJoins );
		Build( 60, 48 );
		CatchUpAndCompare( members.Append( joiner ).ToArray() );
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
		// a following guest's acknowledgements let the host trim its history
		var other = Hosted( 1 ).Single();
		Build( 60, 14 );
		// the first uploads fail for now: the joiner waits as the host says, asks again, and a later capture serves it; the host's wait doubles with its failures in a row, and a
		// server's Retry-After only raises it (one in the past is none)
		var retries = new[] { TimeSpan.FromSeconds( 2 ), TimeSpan.FromSeconds( -5 ), TimeSpan.FromSeconds( 100 ) };
		network.FailUpload = attempt => attempt <= retries.Length ? CoopSnapshotTransfer.Failed( "Too many uploads.", retryAfter: retries[attempt - 1] ) : null;
		var joiner = Joiner();
		// while the member waits the hundred seconds, its failed capture pins no frames: it needs a new capture, with a cut of its own
		RoundsUntil( () => host.Link.Sent.Any( sent => sent.To == joiner.Link.Id && sent.Message is CoopSnapshotFailed { RetryAfterSeconds: 100 } ), "the third failure", 20000 );
		var failedAt = host.Session!.HostStream!.LastFrame;
		Rounds( 60 * 30 );
		Assert.IsTrue( host.Session.HostStream.OldestFrame > failedAt + 1, "the history moved on past the failed capture's cut" );
		RoundsUntil( () => joiner.Level != null, "served by the fourth capture", 20000 );
		Assert.AreEqual( 4, host.Session!.HostStream!.CapturesTaken );
		Assert.AreEqual( 4u, joiner.Session!.Assigned );
		Assert.AreEqual( 0, joiner.Session.FailedJoins, "a failure for now is no failed join" );
		CollectionAssert.AreEqual( new uint[] { 5, 10, 100 }, host.Link.Sent.Where( sent => sent.To == joiner.Link.Id ).Select( sent => sent.Message ).OfType<CoopSnapshotFailed>()
			.GroupBy( failed => failed.Generation ).Select( answers => answers.First().RetryAfterSeconds ).ToArray(), "the waits the host asked for" );
		CatchUpAndCompare( joiner, other );

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
		CollectionAssert.AreEqual( new[] { "failed", "failed", "failed", "failed" }, joiner.Session!.JoinTimings.Select( timing => timing.Outcome ).ToArray(), "every download's capture is finished once" );
		Assert.IsTrue( joiner.Session.JoinTimings.All( timing => timing is { DownloadAttempts: 1, PayloadBytes: 0 } && timing.Build.Count == 0 ) );
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

	[TestMethod]
	public void AHeldCoasterDragHoldsTheCaptureAndFailsForNowAfterItsWait()
	{
		Hosted();
		Build( 60, 25 );
		// the host holds a pylon in the coaster tool's panel: the drag's edited pylon is no part of the save (a station no park holds, so no command ever settles it)
		host.Enter();
		host.Level!.CoasterEdits.Begin( 1, new CoasterEdits.Held( 999_999, null!, new CoasterBuilder.NodeEdit( 0, new CoasterNode( 4, 4, 1, 0, false, 0, false ), 0, null ),
			CoasterDrag.Height, new CoasterDragRange( 0, 4, 0, 0 ) ) );
		var joiner = Joiner();
		var assignedAt = clock;
		var stream = host.Session!.HostStream!;
		RoundsUntil( () => host.Link.Sent.Any( sent => sent.To == joiner.Link.Id && sent.Message is CoopSnapshotFailed { Reason: CoopSnapshotFailure.Held, Permanent: false } ),
			"the capture gave up waiting for the drag's release" );
		Assert.IsTrue( clock - assignedAt >= CoopHostStream.HeldDragSeconds, $"it waited for the release first: {clock - assignedAt:0.0} s" );
		Assert.AreEqual( 0, stream.CapturesTaken );
		// the member asks again after the wait it was told; that capture waits too, and is taken once the drag is released
		RoundsUntil( () => stream.GenerationOf( joiner.Link.Id ) == 2, "the member asked again and was assigned the next capture" );
		Rounds( 600 );
		Assert.AreEqual( 0, stream.CapturesTaken, "the drag is still held" );
		host.Level.CoasterEdits.Clear();
		RoundsUntil( () => joiner.Level != null, "joined once the drag was released" );
		Assert.AreEqual( (1, 2u, 0), (stream.CapturesTaken, joiner.Session!.Assigned, joiner.Session.FailedJoins), "a capture that waits for a drag is no failed join" );
		Build( 60, 26 );
		CatchUpAndCompare( joiner );
	}

	[TestMethod]
	public void AParkTooLargeOrImpossibleToCaptureEndsItsJoinsForGoodAndTheNextCaptureStillRuns()
	{
		var other = Hosted( 1 ).Single();
		Build( 60, 27 );
		var stream = host.Session!.HostStream!;
		// the save outgrows the snapshot's limit while it is written in slices over frames: taken, never uploaded
		stream.MaximumSaveBytes = 1024;
		var large = Joiner();
		RoundsUntil( () => large.Session!.EndedWith != null, "the joiner of a park too large left" );
		Assert.IsTrue( host.Link.Sent.Any( sent => sent.To == large.Link.Id && sent.Message is CoopSnapshotFailed { Reason: CoopSnapshotFailure.TooLarge, Permanent: true } ) );
		Assert.AreEqual( (1, 0), (stream.CapturesTaken, network.Uploads) );
		StringAssert.Contains( large.Link.LeftWith, "cannot be joined" );
		stream.MaximumSaveBytes = ParkResumeSaveFile.MaximumFileSize;
		// a park the save path refuses at the cut (catalogue defaults it cannot write): nothing is captured, and the joiner leaves
		stream.CaptureFault = _ => throw new NotSupportedException( "This ride's catalogue defaults cannot be saved." );
		var refused = Joiner();
		RoundsUntil( () => refused.Session!.EndedWith != null, "the joiner of a park that cannot be captured left" );
		Assert.IsTrue( host.Link.Sent.Any( sent => sent.To == refused.Link.Id && sent.Message is CoopSnapshotFailed { Reason: CoopSnapshotFailure.Uncapturable, Permanent: true } ) );
		Assert.AreEqual( 1, stream.CapturesTaken );
		stream.CaptureFault = null;
		// a slice of the save's writing throws (the save's own check of an inconsistent park, or a value it cannot write): the capture is dropped as one that cannot be captured, no
		// park too large, and the joiner leaves
		stream.SliceFault = () => throw new System.IO.InvalidDataException( "World save is inconsistent: a value the save cannot hold." );
		var unwritable = Joiner();
		RoundsUntil( () => unwritable.Session!.EndedWith != null, "the joiner of a park whose save cannot be written left" );
		Assert.IsTrue( host.Link.Sent.Any( sent => sent.To == unwritable.Link.Id && sent.Message is CoopSnapshotFailed { Reason: CoopSnapshotFailure.Uncapturable, Permanent: true } ) );
		stream.SliceFault = null;
		// no failure stopped the session or held a capture back: the next joiner is served
		var served = Joiner();
		RoundsUntil( () => served.Level != null, "the next joiner was served" );
		Assert.AreEqual( (3, 1), (stream.CapturesTaken, network.Uploads), "the captures that failed were dropped, the one too large not uploaded once the limit allowed it" );
		Assert.IsNull( host.Session.EndedWith );
		Build( 60, 28 );
		CatchUpAndCompare( served, other );
	}

	[TestMethod]
	public void ASocketLostDuringTheDownloadAsksForTheFramesItMissed()
	{
		Hosted();
		Build( 60, 29 );
		network.TransferSeconds = 4;
		ulong? cut = null;
		host.Session!.HostStream!.CaptureStored += ( _, payload ) => cut = CoopJoinPayload.Decode( payload ).Frame;
		var joiner = Joiner();
		RoundsUntil( () => network.Downloads.Count == 1, "the joiner began its download" );
		// frames after the cut came while the download ran, one of them lost on its way; then the socket dropped, losing the frames on their way and those relayed while it was away
		var lostOne = false;
		joiner.Link.DropFrame = seq => !lostOne && seq > cut + 2 && (lostOne = true);
		Build( 30, 30 );
		joiner.Link.DropFrame = null;
		Assert.IsTrue( lostOne );
		joiner.Link.Drop();
		Assert.IsTrue( host.Issue( new SetEntranceFeeCommand( 17 ) ).Accepted );
		Rounds( 30 );
		// the host's park then stands still: it publishes nothing that would show the joiner the gap
		host.Holds = true;
		Rounds( 10 );
		joiner.Link.Resume();
		RoundsUntil( () => joiner.Level != null, "the joiner swapped its capture in" );
		Assert.IsTrue( joiner.Session!.GuestStream!.LastFrame > cut, "frames buffered before the drop were taken" );
		Assert.IsTrue( joiner.Link.Sent.Any( sent => sent.Message is CoopResendRequest { Generation: 1, To: 0 } ), "it asked for every frame from the first it lacks" );
		CatchUpAndCompare( joiner );
		Assert.AreEqual( 17, joiner.Economy.EntranceFee, "the record lost on its way arrived" );
	}

	[TestMethod]
	public void AJoinerBackFromAStallIsNotWaitedForUntilItIsWithinItsJoinWindowAgain()
	{
		Hosted();
		Assert.IsTrue( host.Issue( new SetGameSpeedCommand( GameSpeed.Fastest ) ).Accepted );
		Build( 60, 31 );
		network.TransferSeconds = 3;
		var joiner = Joiner();
		var stream = host.Session!.HostStream!;
		RoundsUntil( () => joiner.Level != null && !stream.IsAwaitingSnapshot( joiner.Link.Id ), "joined and reported" );
		Assert.IsNotNull( stream.JoinWindowOf( joiner.Link.Id ), "it catches up within a join window of its own" );
		// its machine stalls (a browser tab asleep): after the stall time it is not waited for, and the park runs on far past its join window
		joiner.Frozen = true;
		Rounds( 60 * 25 );
		Assert.IsFalse( stream.WaitedFor.Contains( joiner.Link.Id ) );
		joiner.Frozen = false;
		// back, it catches up as any guest back from a stall does: the park does not wait for it until it is within its join window again
		var stood = 0;
		for ( var round = 0; round < 120; round++ )
		{
			var ticks = host.Level!.TickCount;
			Rounds( 1 );
			if ( host.Level.TickCount == ticks )
				stood++;
		}
		Assert.AreEqual( 0, stood, "the park never stood still for the returning joiner" );
		RoundsUntil( () => stream.WaitedFor.Contains( joiner.Link.Id ), "waited for again once within its join window" );
		CatchUpAndCompare( joiner );
	}

	[TestMethod]
	public void ABusyServerIsAskedAgainWithoutCountingUntilItsRetriesRunOutAndARefusedDownloadLeaves()
	{
		Hosted();
		Build( 60, 32 );
		var stream = host.Session!.HostStream!;
		// the server asks the joiner to wait (too many downloads): the same snapshot again after the wait, no failed join and no new capture
		var attempts = new List<double>();
		network.FailDownload = ( member, sha ) =>
		{
			attempts.Add( clock );
			return attempts.Count <= 3 ? CoopSnapshotTransfer.Failed( "Too many downloads.", retryAfter: TimeSpan.FromSeconds( 2 ), busy: true ) : null;
		};
		var joiner = Joiner();
		RoundsUntil( () => joiner.Level != null, "served after the server's waits" );
		Assert.AreEqual( (4, 1, 0), (attempts.Count, stream.CapturesTaken, joiner.Session!.FailedJoins) );
		Assert.AreEqual( 1, network.Downloads.Select( download => download.Sha256 ).Distinct().Count(), "the same snapshot each time" );
		for ( var attempt = 1; attempt < attempts.Count; attempt++ )
			Assert.IsTrue( attempts[attempt] - attempts[attempt - 1] >= 2 - 1e-9, "each after the wait the server asked for" );
		// the download's time is the whole of it, the server's waits between the attempts included (a wall clock: the tests' rounds run in it too)
		var served = joiner.Session.JoinTimings.Single();
		Assert.IsTrue( served is { Outcome: "joined", DownloadAttempts: 4 } && served.DownloadSeconds >= served.LastAttemptSeconds && served.LastAttemptSeconds >= 0, served.ToString() );

		// a server that stays busy: once the retries run out the download counts as failed, and the next capture serves the member
		attempts.Clear();
		network.FailDownload = ( member, sha ) =>
		{
			attempts.Add( clock );
			return attempts.Count <= CoopSession.MaximumBusyRetries + 1 ? CoopSnapshotTransfer.Failed( "The server is busy.", retryAfter: TimeSpan.FromSeconds( 1 ), busy: true ) : null;
		};
		var patient = Joiner();
		RoundsUntil( () => patient.Level != null, "served by the next capture", 20000 );
		Assert.AreEqual( (CoopSession.MaximumBusyRetries + 2, 3, 1), (attempts.Count, stream.CapturesTaken, patient.Session!.FailedJoins) );
		CollectionAssert.AreEqual( new[] { ("failed", CoopSession.MaximumBusyRetries + 1), ("joined", 1) },
			patient.Session.JoinTimings.Select( timing => (timing.Outcome, timing.DownloadAttempts) ).ToArray(), "the retries are one download's attempts" );

		// a server that asks for a wait longer than the host keeps the frames after the cut for: the join ends with the server's reason, to be tried again later
		network.FailDownload = ( member, sha ) => CoopSnapshotTransfer.Failed( "Too many downloads; try again later.", retryAfter: TimeSpan.FromMinutes( 40 ), busy: true );
		var late = Joiner();
		RoundsUntil( () => late.Session!.EndedWith != null, "the joiner told to wait too long left" );
		StringAssert.Contains( late.Link.LeftWith, "cannot be downloaded now" );
		StringAssert.Contains( late.Link.LeftWith, "try again later" );
		Assert.AreEqual( 0, late.Session.FailedJoins );
		Assert.AreEqual( "failed", late.Session.JoinTimings.Single().Outcome );

		// a download the server refuses for good (no longer a player of the session) leaves at once
		network.FailDownload = ( member, sha ) => CoopSnapshotTransfer.Failed( "Not a player of this shared park.", permanent: true );
		var refused = Joiner();
		RoundsUntil( () => refused.Session!.EndedWith != null, "the refused joiner left" );
		StringAssert.Contains( refused.Link.LeftWith, "cannot be downloaded" );
		Assert.AreEqual( 0, refused.Session.FailedJoins );
		Assert.AreEqual( "failed", refused.Session.JoinTimings.Single().Outcome );
		network.FailDownload = null;
		Build( 60, 33 );
		CatchUpAndCompare( joiner, patient );
	}

	[TestMethod]
	public void AnUploadThatNeverAnswersIsGivenUpAndItsLateAnswerChangesNothing()
	{
		Hosted();
		Build( 60, 36 );
		var stored = new List<uint>();
		host.Session!.HostStream!.CaptureStored += ( generation, _ ) => stored.Add( generation );
		// the first upload answers only after the host gave it up (a lost callback), while the next capture's upload is on its way: the joiner is told to wait for now, then the next
		// capture serves it, and the late answer takes nothing from it
		network.UploadSeconds = attempt => attempt switch { 1 => CoopHostStream.UploadSeconds + 15, 2 => 30, _ => null };
		var joiner = Joiner();
		RoundsUntil( () => host.Link.Sent.Any( sent => sent.To == joiner.Link.Id && sent.Message is CoopSnapshotFailed { Reason: CoopSnapshotFailure.Busy, Permanent: false } ),
			"the host gave the upload up", 20000 );
		Assert.IsTrue( clock >= CoopHostStream.UploadSeconds );
		RoundsUntil( () => joiner.Level != null, "the next capture served the joiner", 20000 );
		Assert.AreEqual( (2, 2u), (host.Session.HostStream.CapturesTaken, joiner.Session!.Assigned) );
		CollectionAssert.AreEqual( new uint[] { 2 }, stored, "the late answer of the capture given up was ignored" );
		Assert.IsFalse( host.Link.Sent.Any( sent => sent.Message is CoopSnapshotReady { Generation: 1 } ) );
		Assert.AreEqual( CoopGuestPhase.Following, joiner.Session.Phase );
		CatchUpAndCompare( joiner );
	}

	[TestMethod]
	public void AServersLongWaitIsClampedToAnHour()
	{
		Hosted();
		Build( 60, 38 );
		network.FailUpload = _ => CoopSnapshotTransfer.Failed( "The server is in maintenance.", retryAfter: TimeSpan.FromHours( 2 ) );
		var joiner = Joiner();
		RoundsUntil( () => host.Link.Sent.Any( sent => sent.To == joiner.Link.Id && sent.Message is CoopSnapshotFailed ), "the capture failed for now" );
		Assert.AreEqual( CoopHostStream.MaximumRetrySeconds, host.Link.Sent.Select( sent => sent.Message ).OfType<CoopSnapshotFailed>().Single().RetryAfterSeconds );
	}

	[TestMethod]
	public void AFollowingGuestsResumeAsksForEveryFrameFromTheFirstItLacks()
	{
		var guest = Hosted( 1 ).Single();
		Build( 120, 37 );
		// a frame lost on its way leaves a gap, and the guest's socket drops before the copy of it comes: the frames after the ones it holds are lost too, and then the host's park
		// stands still, publishing nothing that would show them
		var lostOne = false;
		var from = host.Session!.HostStream!.LastFrame;
		guest.Link.DropFrame = seq => !lostOne && seq > from + 1 && (lostOne = true);
		RoundsUntil( () => guest.Session!.GuestStream!.HeldFrames > 0, "a frame held after the gap" );
		guest.Link.DropFrame = null;
		guest.Link.Drop();
		Assert.IsTrue( host.Issue( new SetEntranceFeeCommand( 19 ) ).Accepted );
		Rounds( 30 );
		host.Holds = true;
		Rounds( 10 );
		guest.Link.Resume();
		CatchUpAndCompare( guest );
		Assert.AreEqual( 19, guest.Economy.EntranceFee, "the record lost while it was away arrived" );
	}

	[TestMethod]
	public void ABuildThatThrowsIsAFailedCheckAndAFailedInstallEndsTheSessionWithTheCandidateReleased()
	{
		var guest = Hosted( 1 ).Single();
		Build( 60, 34 );
		// the game's restore refuses the first payload for a reason of its own: a failed check, and the next capture serves the joiner
		var joiner = Joiner();
		joiner.Join!.Throws = new NotSupportedException( "A restore step this park needs is missing." );
		RoundsUntil( () => joiner.Level != null, "the second capture built", 20000 );
		Assert.AreEqual( (2, 2, 1, 0), (joiner.Join.Builds, host.Session!.HostStream!.CapturesTaken, joiner.Session!.FailedJoins, joiner.Join.Discarded) );
		Assert.IsTrue( joiner.Link.Sent.Any( sent => sent.Message is CoopSnapshotRequest { Reason: CoopSnapshotReason.FailedCheck } ) );
		CollectionAssert.AreEqual( new[] { "failed", "joined" }, joiner.Session.JoinTimings.Select( timing => timing.Outcome ).ToArray(), "a build that throws failed" );

		// a following guest whose resync fails to install (its old park half gone) leaves the session, and the candidate built for it is released
		guest.Join!.InstallFails = true;
		guest.Enter();
		guest.Economy.Ledger.Post( LedgerCategory.OtherIncome, 1 );
		RoundsUntil( () => guest.Session!.EndedWith != null, "the guest left" );
		StringAssert.Contains( guest.Link.LeftWith, "could not be entered" );
		Assert.AreEqual( 1, guest.Join.Discarded );
		Assert.IsNull( guest.Session.Level );
		Assert.AreEqual( "failed", guest.Session.JoinTimings.Single().Outcome, "an install that fails" );
		Assert.IsNull( host.Session.EndedWith, "the session goes on without it" );
		Build( 60, 35 );
		CatchUpAndCompare( joiner );
	}

	[TestMethod]
	public void ANewerAssignmentAbandonsABuildOnItsWay()
	{
		Hosted();
		Build( 60, 40 );
		var joiner = Joiner();
		// the first build takes ten seconds of frames; meanwhile the host assigns the member a newer capture (it reports a failure its build never had)
		joiner.Join!.Steps = build => build == 1 ? 600 : 1;
		RoundsUntil( () => joiner.Join.Builds == 1, "the first build began" );
		joiner.Link.Send( host.Link.Id, new CoopSnapshotRequest( 1, CoopSnapshotReason.FailedCheck, null ).Encode() );
		RoundsUntil( () => joiner.Level != null, "the next capture built and swapped in", 20000 );
		Assert.AreEqual( (2, 1, 0), (joiner.Join.Builds, joiner.Join.Abandoned, joiner.Join.Discarded), "the first build abandoned before it had a park" );
		Assert.AreEqual( 2u, joiner.Session!.Assigned );
		CollectionAssert.AreEqual( new[] { (1u, "abandoned"), (2u, "joined") }, joiner.Session.JoinTimings.Select( timing => (timing.Generation, timing.Outcome) ).ToArray() );
		Build( 60, 41 );
		CatchUpAndCompare( joiner );
	}

	[TestMethod]
	public void ARefusalEndsTheJoinAtOnceAndAStepThatThrowsIsAFailedCheck()
	{
		Hosted();
		Build( 60, 42 );
		// a build step that throws (a file this game could not read) is a failed check: the next capture serves the member
		var joiner = Joiner();
		joiner.Join!.Steps = _ => 5;
		joiner.Join.ThrowsAtStep = (3, new System.IO.IOException( "A level file could not be read." ));
		RoundsUntil( () => joiner.Level != null, "joined after a failed build", 20000 );
		Assert.AreEqual( (2, 1, 1, 0), (joiner.Join.Builds, joiner.Session!.FailedJoins, joiner.Join.Abandoned, joiner.Join.Discarded) );
		// a refusal every build of the payload would meet (this game's files differ from the host's) ends the join at once, without more captures
		var captures = host.Session!.HostStream!.CapturesTaken;
		var refused = Joiner();
		refused.Join!.Refuses = new CoopJoinRefusedException( "This game's files differ from the host's." );
		RoundsUntil( () => refused.Session!.EndedWith != null, "the refused joiner left" );
		StringAssert.Contains( refused.Link.LeftWith, "files differ" );
		Assert.AreEqual( (1, 0), (refused.Join.Builds, refused.Session.FailedJoins) );
		Assert.AreEqual( captures + 1, host.Session.HostStream.CapturesTaken, "one capture, no more" );
		Assert.AreEqual( "refused", refused.Session.JoinTimings.Single().Outcome );
		CollectionAssert.AreEqual( new[] { "failed", "joined" }, joiner.Session.JoinTimings.Select( timing => timing.Outcome ).ToArray(), "a step that throws failed" );
		// a refusal in a later step ends it the same way
		var late = Joiner();
		late.Join!.Steps = _ => 4;
		late.Join.ThrowsAtStep = (2, new CoopJoinRefusedException( "This game's simulation mods differ from the host's." ));
		RoundsUntil( () => late.Session!.EndedWith != null, "the late refusal left" );
		StringAssert.Contains( late.Link.LeftWith, "mods differ" );
		Assert.AreEqual( (1, 1, 0), (late.Join.Builds, late.Join.Abandoned, late.Session.FailedJoins) );
		Assert.AreEqual( "refused", late.Session.JoinTimings.Single().Outcome );
		Build( 60, 43 );
		CatchUpAndCompare( joiner );
	}

	[TestMethod]
	public void ABuildThatNeverEndsIsAFailedCheckAtItsDeadline()
	{
		Hosted();
		Build( 60, 44 );
		var joiner = Joiner();
		joiner.Join!.Steps = build => build == 1 ? int.MaxValue : 1;
		RoundsUntil( () => joiner.Join.Builds == 1, "the build began" );
		var began = clock;
		RoundsUntil( () => joiner.Session!.FailedJoins == 1, "the build's deadline", 30000 );
		Assert.IsTrue( clock - began >= CoopSession.BuildDeadlineSeconds - 1, $"{clock - began:0.0} s" );
		RoundsUntil( () => joiner.Level != null, "the next capture built", 20000 );
		Assert.AreEqual( (2, 1), (joiner.Join.Builds, joiner.Join.Abandoned) );
		CollectionAssert.AreEqual( new[] { "timed out", "joined" }, joiner.Session!.JoinTimings.Select( timing => timing.Outcome ).ToArray() );
		CatchUpAndCompare( joiner );
	}

	/// <summary>A capture still on its way when the session ends is finished as ended; one superseded while downloading, as abandoned.</summary>
	[TestMethod]
	public void ACaptureOnItsWayWhenTheSessionEndsOrANewerOneComesIsFinishedOnce()
	{
		Hosted();
		Build( 60, 45 );
		// the server asks the joiner to wait: its download is on its way while a newer capture is assigned (it reports a failure its build never had)
		network.FailDownload = ( member, sha ) => CoopSnapshotTransfer.Failed( "Too many downloads.", retryAfter: TimeSpan.FromSeconds( 30 ), busy: true );
		var joiner = Joiner();
		RoundsUntil( () => joiner.Session!.JoinTimings.Count == 0 && network.Downloads.Count > 0, "the first download began" );
		joiner.Link.Send( host.Link.Id, new CoopSnapshotRequest( 1, CoopSnapshotReason.FailedCheck, null ).Encode() );
		RoundsUntil( () => joiner.Session!.Assigned == 2, "a newer capture was assigned" );
		Assert.AreEqual( (1u, "abandoned"), (joiner.Session!.JoinTimings.Single().Generation, joiner.Session.JoinTimings.Single().Outcome) );
		RoundsUntil( () => network.Downloads.Count > 1, "the newer capture's download began" );
		joiner.Session.End( "The player left." );
		CollectionAssert.AreEqual( new[] { (1u, "abandoned"), (2u, "ended") }, joiner.Session.JoinTimings.Select( timing => (timing.Generation, timing.Outcome) ).ToArray() );
		network.FailDownload = null;
	}

	[TestMethod]
	public void ASecondDownloadAnswerBuildsNothingTwice()
	{
		Hosted();
		Build( 60, 45 );
		// the download's callback is raised again a second after the first, while the build (four seconds of frames) runs
		network.RepeatDownloadAnswerAfter = 1;
		var joiner = Joiner();
		joiner.Join!.Steps = _ => 240;
		RoundsUntil( () => joiner.Level != null, "joined" );
		Rounds( 120 );
		Assert.AreEqual( 1, joiner.Join.Builds, "the second answer of the same download built nothing" );
		Build( 60, 46 );
		CatchUpAndCompare( joiner );
	}

	[TestMethod]
	public async Task ALateJoinerIsServedThroughTheRealLinksAndTheServersSnapshotStore()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var hostPlayer = await players.Add( server, "Hostess" );
			var joinPlayer = await players.Add( server, "Latecomer", firstFrame: true );
			await CoopLinkTests.Open( hostPlayer, joinPlayer );
			var state = await CoopLinkTests.Share( hostPlayer );
			var (level, list) = NewPark();
			host = new Peer { Level = level, Entities = list, Pump = hostPlayer.Session.Pump };
			peers.Add( host );
			host.Enter();
			hostLogStart = level.Commands.Log.Count;
			host.Session = CoopSession.Host( level, hostPlayer.Link, () => clock, HostTime );
			host.Session.HostStream!.CaptureSliceBudget = TimeSpan.FromMilliseconds( 50 );
			async Task Frames( int count, Func<bool>? until = null, string what = "" )
			{
				var deadline = DateTime.UtcNow + OnlineIntegrationTests.Deadline;
				for ( var round = 0; round < count || (until != null && !until()); round++ )
				{
					Assert.IsTrue( DateTime.UtcNow < deadline, $"{what}: the host took {host.Session!.HostStream!.CapturesTaken} captures; {string.Join( "; ", peers.Select( peer => $"{peer.Session?.Phase} {peer.Session?.EndedWith}" ) )}" );
					if ( round % 40 == 7 )
						host.Issue( new LayLineCommand( CellToolMode.Path, -1, 4 + round % 6, 20, 4 + round % 6, 26 ) );
					clock += 1 / 60.0;
					foreach ( var peer in peers.ToArray() )
						peer.Frame( 1 / 60.0 );
					await Task.Delay( 2 );
				}
			}
			await Frames( 120 );

			// a player joins the running session with its code: the host's capture goes up to the server and comes down to the joiner over HTTP
			Assert.IsTrue( joinPlayer.Link.Join( state.Code ) );
			await CoopLinkTests.Until( () => joinPlayer.Link.Session != null, "the joiner to join", joinPlayer, hostPlayer );
			var joiner = new Peer { Pump = joinPlayer.Session.Pump };
			peers.Add( joiner );
			joiner.Join = new ReplayJoin( this, joiner );
			joiner.Session = CoopSession.JoinRunning( joinPlayer.Link, joiner.Join, () => clock );
			await Frames( 0, () => joiner.Level != null, "the joiner swapped its capture in" );
			Assert.AreEqual( 1, host.Session.HostStream.CapturesTaken );
			Assert.AreEqual( CoopGuestPhase.Following, joiner.Session.Phase );
			await Frames( 180 );

			// the host holds until the joiner stands at its tick with every record applied
			host.Holds = true;
			hostTimeFrozenAt = clock;
			await Frames( 0, () => joiner.Level!.TickCount == host.Level.TickCount && joiner.Session.GuestStream!.LastRecord == host.Session.HostStream.LastRecord
				&& joiner.Level.Commands.ScheduledCount == 0, "the joiner caught up" );
			hostTimeFrozenAt = null;
			host.Enter();
			var digest = CoopDigest.Compute( host.Level, out var world );
			joiner.Enter();
			Assert.AreEqual( digest, CoopDigest.Compute( joiner.Level!, out var joinerWorld ) );
			Assert.AreEqual( world, joinerWorld );
			Assert.IsNull( joiner.Session.GuestStream!.Desync );
			Assert.IsTrue( joiner.Session.GuestStream.Checks >= 1, "the joiner compared the host's digests after its join" );
		} );
	}
}
