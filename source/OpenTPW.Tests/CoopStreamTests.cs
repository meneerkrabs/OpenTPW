using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Coop;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-lockstep] The host's command stream and its guests (docs/COOP.md, "Sessions") over an in-memory server (<see cref="CoopTestNetwork"/>), with a host and its guests as
/// levels of one asset-free park (the frame batching tests' park) built alike, each driven through whole game frames (<see cref="Level.AdvanceFrame"/>) of random lengths in a random order,
/// guests skipping frames: a build session at every speed stays equal at every checkpoint and ends with equal digests and command logs; a frame cut at the size limit holds a guest at its
/// first missing record; a lost frame is asked for and resent once; resends from before the history, lost desync reports, faults only the session digest sees, refused and errored
/// commands, flow control, the host's own link, a server restart and the session clock.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CoopStreamTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;
	private List<Entity>? entities;
	private Level? current;
	private readonly List<Peer> peers = new();
	private double clock;

	/// <summary>One member's level, its entities (process statics: <see cref="Entity.All"/> and <see cref="Level.Current"/> are switched to its own while it runs) and its stream.</summary>
	private sealed class Peer
	{
		public Level Level { get; init; } = null!;
		public ParkEconomy Economy { get; init; } = null!;
		public OriginalObject One { get; init; } = null!;
		public OriginalObject Two { get; init; } = null!;
		public List<Entity> Entities { get; init; } = null!;
		public CoopTestNetwork.Member Link { get; init; } = null!;
		public CoopHostStream? Host { get; set; }
		public CoopGuestStream? Guest { get; set; }
		/// <summary>Wall time since this member's last game frame.</summary>
		public double Pending { get; set; }
		/// <summary>The host's park holds (its time scale 0) while the guests catch up at the end.</summary>
		public bool Holds { get; set; }
		/// <summary>This member runs no game frames (a stalled machine, or a hidden tab).</summary>
		public bool Frozen { get; set; }

		public void Enter()
		{
			Entity.All = Entities;
			Level.Current = Level;
		}

		public void Frame( double seconds )
		{
			Enter();
			Link.Deliver();
			Level.SimulationTimeScale = Holds ? 0f : GameSpeedScale.Of( Economy.Speed );
			Time.Update( (float)seconds );
			Level.AdvanceFrame( seconds );
		}

		/// <summary>Issues <paramref name="command"/> as this member's player between two frames, as its HUD does.</summary>
		public ParkCommandResult Issue( ParkCommand command )
		{
			Enter();
			return Level.Commands.Issue( command );
		}
	}

	[TestInitialize]
	public void Initialize()
	{
		entities = Entity.All;
		current = Level.Current;
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var peer in peers )
		{
			peer.Host?.Dispose();
			peer.Guest?.Dispose();
			Entity.All = peer.Entities;
			foreach ( var entity in peer.Entities.ToArray() )
				entity.Delete();
		}
		Entity.All = new List<Entity>();
		fixture?.Cleanup();
		renderer?.Dispose();
		if ( entities != null )
			Entity.All = entities;
		Level.Current = current!;
	}

	/// <summary>The frame batching tests' park (objects, paths, guests, staff), built the same way for every member.</summary>
	private Peer NewPeer( CoopTestNetwork.Member link )
	{
		var list = new List<Entity>();
		Entity.All = list;
		Level.Current = null!;
		var type = typeof( UpgradeCatalogueDefaultsTests );
		var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", BindingFlags.Instance | BindingFlags.NonPublic )!
			.Invoke( fixture, new object[] { "", 0, false } )!;
		type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } );
		var level = world.Level;
		typeof( Level ).GetMethod( "SetupStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, null );
		typeof( Level ).GetMethod( "SetupParkGate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, null );
		typeof( Level ).GetMethod( "SetupPaths", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, null );
		level.ConfigureObjectHooks();
		typeof( Level ).GetField( "<CellTool>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, new CellBuildTool() );
		typeof( Level ).GetField( "<PathVariants>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, new PathTileVariants() );
		typeof( Level ).GetField( "<SceneClock>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, new SceneClock() );
		level.SimulationTimeScale = 1f;
		world.Economy.Ledger.Post( LedgerCategory.OtherIncome, 1_000_000 );
		var grid = level.Guests!.Grid;
		for ( var x = 1; x <= 12; x++ )
			grid.SetPath( x, 1, true );
		for ( var y = 1; y <= 3; y++ )
			grid.SetPath( 3, y, true );
		foreach ( var item in new[] { world.One, world.Two } )
			item.Visitors.RecomputeQueue( grid );
		foreach ( var (x, y) in new[] { (1, 1), (4, 1), (6, 1), (8, 1), (10, 1), (3, 2) } )
			level.Guests.SpawnInPark( x, y );
		EconomyTestData.HireBest( world.Economy, StaffType.Mechanic );
		EconomyTestData.HireBest( world.Economy, StaffType.Handyman );
		var peer = new Peer { Level = level, Economy = world.Economy, One = world.One, Two = world.Two, Entities = list, Link = link };
		peers.Add( peer );
		return peer;
	}

	/// <summary>While set, the host's local time stands still (the catch-up at a test's end: no clock command is left on its way when the parks are compared).</summary>
	private double? hostTimeFrozenAt;

	/// <summary>The host's local civil time in the tests: an afternoon, moving with the virtual clock.</summary>
	private DateTime HostTime() => new DateTime( 2026, 10, 21, 15, 0, 0 ).AddSeconds( hostTimeFrozenAt ?? clock );

	/// <summary>A host and <paramref name="guests"/> guests of one session, all parks alike, their streams attached.</summary>
	private (Peer Host, Peer[] Guests, CoopTestNetwork Network) Session( int guests, Action<Peer>? prepare = null )
	{
		var network = new CoopTestNetwork();
		var hostLink = network.Add( host: true );
		var guestLinks = Enumerable.Range( 0, guests ).Select( _ => network.Add() ).ToArray();
		var host = NewPeer( hostLink );
		prepare?.Invoke( host );
		var members = guestLinks.Select( link =>
		{
			var guest = NewPeer( link );
			prepare?.Invoke( guest );
			return guest;
		} ).ToArray();
		var start = CoopDigest.Compute( host.Level );
		foreach ( var guest in members )
			Assert.AreEqual( start, CoopDigest.Compute( guest.Level ), "every member starts from the same park" );
		// the joins the server announced before the streams existed
		hostLink.Deliver();
		foreach ( var link in guestLinks )
			link.Deliver();
		host.Enter();
		host.Host = new CoopHostStream( host.Level, hostLink, () => clock, HostTime );
		foreach ( var guest in members )
		{
			guest.Enter();
			guest.Guest = new CoopGuestStream( guest.Level, guest.Link, () => clock );
		}
		return (host, members, network);
	}

	/// <summary>
	/// One round of <paramref name="seconds"/> of wall time: every member that is not frozen runs a game frame in a random order, and a guest skips its frame with the chance
	/// <paramref name="skip"/> (its next frame then spans the time since its last one).
	/// </summary>
	private void Round( double seconds, Random random, double skip = 0 )
	{
		clock += seconds;
		foreach ( var peer in peers.OrderBy( _ => random.Next() ).ToArray() )
		{
			peer.Pending += seconds;
			if ( peer.Frozen || (peer.Guest != null && random.NextDouble() < skip) )
				continue;
			peer.Frame( peer.Pending );
			peer.Pending = 0;
		}
	}

	private void Rounds( int count, Random random, double skip = 0, double seconds = 1 / 60.0 )
	{
		for ( var round = 0; round < count; round++ )
			Round( seconds, random, skip );
	}

	/// <summary>
	/// The host holds its park and its clock, and the rounds run until every guest stands at the host's tick with every record applied; then their digests and command logs equal the
	/// host's. The host's clock goes on afterwards.
	/// </summary>
	private void CatchUpAndCompare( Peer host, IEnumerable<Peer> guests, Random random )
	{
		host.Holds = true;
		hostTimeFrozenAt = clock;
		var all = guests.ToArray();
		for ( var round = 0; round < 3000 && all.Any( guest => guest.Level.TickCount != host.Level.TickCount || guest.Link.Waiting > 0
			|| guest.Guest!.LastRecord != host.Host!.LastRecord || guest.Level.Commands.ScheduledCount > 0 ); round++ )
			Round( 1 / 60.0, random );
		hostTimeFrozenAt = null;
		var digest = CoopDigest.Compute( host.Level );
		foreach ( var guest in all )
		{
			Assert.IsNull( guest.Guest!.Desync, $"guest {guest.Link.Id}: {guest.Guest.Desync}" );
			Assert.AreEqual( host.Level.TickCount, guest.Level.TickCount, $"guest {guest.Link.Id} caught up" );
			Assert.AreEqual( digest, CoopDigest.Compute( guest.Level ), $"guest {guest.Link.Id}: the session digest at the end" );
			Assert.AreEqual( host.Level.ComputeStateHash(), guest.Level.ComputeStateHash(), $"guest {guest.Link.Id}: the world hash at the end" );
			CollectionAssert.AreEqual( host.Level.Commands.Log.ToArray(), guest.Level.Commands.Log.ToArray(), $"guest {guest.Link.Id}: the same commands before the same ticks" );
		}
		Assert.IsNull( host.Host!.Failure );
	}

	[TestMethod]
	public void AHostAndTwoGuestsStayEqualThroughABuildSessionAtEverySpeed()
	{
		var random = new Random( 20261021 );
		var (host, guests, _) = Session( 2 );
		// the scripted edits, by round: building, a path, the speeds (Fastest included), a pause with several edits at one tick over several frames
		var script = new (int Round, Func<ParkCommand> Command)[]
		{
			(5, () => new SetResearchEffortCommand( ResearchCategory.Ride, 60 )),
			(20, () => host.Level.BuildCommand( host.One.Entry, 20, 12, 90 )),
			(21, () => new LayLineCommand( CellToolMode.Path, -1, 5, 20, 12, 20 )),
			(40, () => new SetGameSpeedCommand( GameSpeed.Fastest )),
			(70, () => new SetAttractionOpenCommand( host.Two.Runtime.AttractionId, false )),
			(90, () => new RemovePathCellCommand( 6, 20 )),
			(110, () => new SetGameSpeedCommand( GameSpeed.Paused )),
			(112, () => new SetAttractionOpenCommand( host.Two.Runtime.AttractionId, true )),
			(115, () => new SetEntranceFeeCommand( 5 )),
			(118, () => new SetTrainingBudgetCommand( StaffType.Mechanic, 150 )),
			(120, () => new SetGameSpeedCommand( GameSpeed.Fast )),
			(160, () => new RemoveAtCommand( 20, 12 )),
			(200, () => new SetGameSpeedCommand( GameSpeed.Normal )),
			(230, () => new SetTrainingBudgetCommand( StaffType.Handyman, 200 )),
		};
		var paused = 0L;
		for ( var round = 0; round < 300; round++ )
		{
			foreach ( var (at, command) in script.Where( item => item.Round == round ) )
				Assert.IsTrue( host.Issue( command() ).Accepted, $"the scripted edit of round {round}" );
			if ( round == 112 )
				paused = host.Level.TickCount;
			if ( round == 118 )
				Assert.AreEqual( paused, host.Level.TickCount, "a paused park stays at one tick while edits come" );
			// a guest's own edit is refused before it runs, and neither logged nor streamed
			if ( round == 150 )
			{
				var own = guests[0].Issue( new SetEntranceFeeCommand( 99 ) );
				Assert.IsFalse( own.Accepted );
			}
			Round( random.Next( 1, 25 ) / 240.0, random, skip: 0.2 );
		}
		Assert.IsTrue( host.Level.TickCount > 600, $"the session ran ({host.Level.TickCount} ticks)" );
		CatchUpAndCompare( host, guests, random );
		foreach ( var guest in guests )
			Assert.IsTrue( guest.Guest!.Checks >= 8, $"guest {guest.Link.Id} compared the host's digests ({guest.Guest.Checks})" );
		Assert.AreEqual( script.Length, host.Level.Commands.Log.Count( entry => entry.Command is not SetSessionClockCommand ),
			"the host's log holds the scripted edits and its clock, not the guest's refused one" );
	}

	[TestMethod]
	public void AFrameCutAtTheSizeLimitHoldsTheGuestAtItsFirstMissingRecord()
	{
		var random = new Random( 7 );
		var (host, guests, _) = Session( 1 );
		var guest = guests[0];
		Rounds( 30, random );
		CatchUpAndCompare( host, guests, random );
		host.Holds = false;
		// three hundred long renames between two frames: more than one frame holds
		var boundary = host.Level.TickCount;
		var line = new string( 'x', 500 );
		for ( var index = 0; index < 300; index++ )
			host.Issue( new RenameParkCommand( line + index % 10, line ) );
		var before = guest.Link.Waiting;
		host.Frame( 1 / 60.0 );
		host.Host!.PublishNow();
		var frames = guest.Link.Waiting - before;
		Assert.IsTrue( frames >= 2, $"the records went out in {frames} frames" );
		// the first part alone: the guest runs up to the records' tick and no further
		guest.Enter();
		guest.Link.Deliver( 1 + before );
		for ( var round = 0; round < 30; round++ )
		{
			clock += 1 / 60.0;
			guest.Enter();
			guest.Level.AdvanceFrame( 1 / 60.0 );
		}
		Assert.AreEqual( boundary, guest.Guest!.Horizon, "the cut frame's horizon stops at its first missing record" );
		Assert.AreEqual( boundary, guest.Level.TickCount, "the guest stands at the records' tick" );
		Assert.IsNull( guest.Guest.Desync );
		CatchUpAndCompare( host, guests, random );
	}

	[TestMethod]
	public void AFrameLostOnItsWayIsAskedForAndResentOnce()
	{
		var random = new Random( 11 );
		var (host, guests, _) = Session( 2 );
		var guest = guests[0];
		guest.Link.DropFrame = seq => seq == 5;
		Rounds( 60, random );
		CatchUpAndCompare( host, guests, random );
		var copies = host.Link.Sent.Where( sent => sent.To == guest.Link.Id ).Select( sent => sent.Message ).OfType<CoopResentFrames>()
			.Sum( resent => resent.Frames.Count( frame => frame.Seq == 5 ) );
		Assert.AreEqual( 1, copies, "frame 5 went to the guest once more, and only once" );
		Assert.IsFalse( host.Link.Sent.Any( sent => sent.To == guests[1].Link.Id && sent.Message is CoopResentFrames ), "the other guest missed nothing" );

		// a resend that is lost: the guest asks again with its progress, the host sends a second copy only after the retry time
		var target = host.Host!.LastFrame + 2;
		guest.Link.DropFrame = seq => seq == target;
		var dropped = 0;
		guest.Link.DropMessage = ( from, bytes ) => CoopStreamMessage.Decode( bytes ) is CoopResentFrames && dropped++ == 0;
		host.Holds = false;
		Rounds( 400, random );
		CatchUpAndCompare( host, guests, random );
		Assert.AreEqual( 1, dropped - 1, "one resend was lost and a second one came" );
		Assert.IsTrue( guest.Link.Sent.Count( sent => sent.Message is CoopResendRequest ) >= 3, "the guest repeated its request while the gap lasted" );
	}

	[TestMethod]
	public void AResendFromBeforeTheHistoryIsTooFarBack()
	{
		var random = new Random( 3 );
		var (host, guests, _) = Session( 1 );
		var guest = guests[0];
		Rounds( 120, random );
		Assert.IsTrue( host.Host!.OldestFrame > 1, "acknowledged frames left the history" );
		// a request for frame 1 (a guest that lost far more than the host holds)
		guest.Link.Send( host.Link.Id, new CoopResendRequest( 1, 0 ).Encode() );
		Rounds( 10, random );
		Assert.IsTrue( host.Host.IsTooFarBack( guest.Link.Id ) );
		Assert.IsTrue( guest.Guest!.Stopped, "the guest stops and waits for a resync" );
		Assert.AreEqual( CoopGuestState.TooFarBack, guest.Guest.State );
		var ticks = guest.Level.TickCount;
		Rounds( 30, random );
		Assert.AreEqual( ticks, guest.Level.TickCount, "its clock stands still" );
		Assert.IsFalse( host.Host.WaitedFor.Contains( guest.Link.Id ), "the host no longer waits for it" );
	}

	[TestMethod]
	public void AFaultOnlyTheSessionDigestSeesIsADesyncAtTheNextCheckpointAndALostReportIsRepeated()
	{
		var random = new Random( 5 );
		var (host, guests, _) = Session( 1 );
		var guest = guests[0];
		Rounds( 40, random );
		// the guest's player 3 holds an employee the host's does not: only the session digest covers the hands
		guest.Level.StaffHands!.Carry( 3, guest.Economy.Staff.Members.First().Id, (1, 1) );
		var desyncs = new List<CoopDesyncReport>();
		host.Host!.Desynced += ( _, report ) => desyncs.Add( report );
		var lost = 0;
		host.Link.DropMessage = ( from, bytes ) => CoopStreamMessage.Decode( bytes ) is CoopDesyncReport && lost++ == 0;
		Rounds( 240, random );
		var desync = guest.Guest!.Desync;
		Assert.IsNotNull( desync );
		Assert.AreEqual( CoopDesyncKind.Digest, desync.Kind );
		Assert.AreEqual( 0, desync.Tick % CoopHostStream.CheckpointTicks, "found at a tick checkpoint" );
		Assert.AreEqual( desync.Tick, guest.Level.TickCount, "the guest's clock stopped there" );
		Assert.AreEqual( 1, desyncs.Count, "the host logged it once" );
		Assert.IsTrue( lost >= 2, "the first report was lost and the guest sent it again" );
		Assert.IsTrue( host.Host.IsDesynced( guest.Link.Id ) );
		// once the host has seen it, the guest stops sending it
		var reports = guest.Link.Sent.Count( sent => sent.Message is CoopDesyncReport );
		Rounds( 120, random );
		Assert.AreEqual( reports, guest.Link.Sent.Count( sent => sent.Message is CoopDesyncReport ) );
		Assert.IsTrue( guest.Link.Sent.Where( sent => sent.Message is CoopProgress ).Select( sent => (CoopProgress)sent.Message ).Last().DesyncId == 0 );
	}

	/// <summary>Rights that refuse one kind of command to everyone (a guest whose park refuses what the host's accepted).</summary>
	private sealed class Refusing( ParkCommandKind kind ) : IParkCommandRights
	{
		public bool Allows( int player, ParkCommandKind asked ) => asked != kind && player == ParkCommands.LocalPlayer;
	}

	[TestMethod]
	public void ACommandTheGuestRefusesButTheHostAcceptedIsADesyncAtOnce()
	{
		var random = new Random( 9 );
		var (host, guests, _) = Session( 1 );
		var guest = guests[0];
		Rounds( 20, random );
		guest.Level.Commands.Rights = new Refusing( ParkCommandKind.SetEntranceFee );
		Assert.IsTrue( host.Issue( new SetEntranceFeeCommand( 9 ) ).Accepted );
		var boundary = host.Level.TickCount;
		Rounds( 30, random );
		var desync = guest.Guest!.Desync;
		Assert.IsNotNull( desync );
		Assert.AreEqual( CoopDesyncKind.Flags, desync.Kind );
		Assert.AreEqual( boundary, desync.Tick, "at the record itself, before any digest" );
	}

	[TestMethod]
	public void AnErroredCommandIsCheckedRightAfterIt()
	{
		var random = new Random( 13 );
		var (host, guests, _) = Session( 1 );
		var guest = guests[0];
		Rounds( 20, random );
		// the same throw on both machines: the record says errored, and the digest right after it matches
		static void Throw( ParkCommand command )
		{
			if ( command is SetEntranceFeeCommand { Fee: 13 } )
				throw new IndexOutOfRangeException( "a rule the command's checks missed" );
		}
		host.Level.CommandFault = Throw;
		guest.Level.CommandFault = Throw;
		var checks = guest.Guest!.RecordChecks;
		Assert.ThrowsException<IndexOutOfRangeException>( () => host.Issue( new SetEntranceFeeCommand( 13 ) ), "a strict host rethrows" );
		Rounds( 20, random );
		Assert.IsNull( guest.Guest.Desync );
		Assert.AreEqual( checks + 1, guest.Guest.RecordChecks, "the record's digest was compared" );

		// a throw that leaves different partial writes: a desync at that record
		var boundary = host.Level.TickCount;
		host.Level.CommandFault = command =>
		{
			if ( command is SetEntranceFeeCommand { Fee: 14 } )
				throw new IndexOutOfRangeException( "after a write" );
		};
		guest.Level.CommandFault = command =>
		{
			if ( command is SetEntranceFeeCommand { Fee: 14 } )
			{
				guest.Level.StaffHands!.Carry( 2, 1, (0, 0) );
				throw new IndexOutOfRangeException( "after a write" );
			}
		};
		Assert.ThrowsException<IndexOutOfRangeException>( () => host.Issue( new SetEntranceFeeCommand( 14 ) ) );
		Rounds( 20, random );
		var desync = guest.Guest.Desync;
		Assert.IsNotNull( desync );
		Assert.AreEqual( CoopDesyncKind.Digest, desync.Kind );
		Assert.AreEqual( boundary, desync.Tick, "found right after the errored record" );
	}

	[TestMethod]
	public void FlowControlHoldsTheHostToItsSlowestFollowingGuest()
	{
		var random = new Random( 17 );
		var (host, guests, _) = Session( 2 );
		var slow = guests[1];
		Rounds( 60, random );
		Assert.IsTrue( host.Host!.WaitedFor.Contains( slow.Link.Id ) );
		// a guest that stops reading holds the host a window ahead of where it stopped
		slow.Frozen = true;
		var stoppedAt = slow.Level.TickCount;
		Rounds( 400, random );
		var window = host.Host.WindowTicks;
		Assert.IsTrue( host.Level.TickCount <= stoppedAt + window + 16, $"the host holds near {stoppedAt} + {window} ({host.Level.TickCount})" );
		Assert.IsTrue( host.Level.TickCount >= stoppedAt + window - 60, $"the host ran up to the window ({host.Level.TickCount})" );
		// ten seconds without a report: the host runs on
		Rounds( 60 * 11, random );
		Assert.IsFalse( host.Host.WaitedFor.Contains( slow.Link.Id ) );
		Assert.IsTrue( host.Level.TickCount > stoppedAt + window + 300, "the host runs on without the stalled guest" );
		// it comes back far behind: it catches up without holding the host, then is waited for again
		slow.Frozen = false;
		var before = host.Level.TickCount;
		Rounds( 120, random );
		Assert.IsTrue( host.Level.TickCount > before + 60, "the host did not wait for the guest catching up" );
		Rounds( 60 * 20, random );
		Assert.IsTrue( host.Host.WaitedFor.Contains( slow.Link.Id ), "back within the window, it is waited for again" );
		// the host's own link down holds its park
		host.Link.Drop();
		Rounds( 10, random );
		var held = host.Level.TickCount;
		Rounds( 60, random );
		Assert.AreEqual( held, host.Level.TickCount, "a host without its link holds" );
		host.Link.Resume();
		Rounds( 60, random );
		Assert.IsTrue( host.Level.TickCount > held );
		CatchUpAndCompare( host, guests, random );
	}

	[TestMethod]
	public void AServerRestartThatLostTheFrameNumberGoesOnFromTheHostsOldestFrame()
	{
		var random = new Random( 19 );
		var (host, guests, network) = Session( 2 );
		Rounds( 120, random );
		Assert.IsTrue( host.Host!.OldestFrame > 2 );
		// the server comes back with a frame number far behind what the guests hold and the host trimmed
		network.Restart( lastFrame: 1, seqKnown: false );
		Rounds( 120, random );
		Assert.IsNull( host.Host.Failure );
		CatchUpAndCompare( host, guests, random );
	}

	[TestMethod]
	public void AServerThatNeedsAFrameTheHostNoLongerHoldsEndsTheSession()
	{
		var random = new Random( 23 );
		var (host, guests, network) = Session( 1 );
		Rounds( 120, random );
		Assert.IsTrue( host.Host!.OldestFrame > 2 );
		network.Restart( lastFrame: 1, seqKnown: true );
		Rounds( 5, random );
		Assert.IsNotNull( host.Host.Failure );
		Assert.AreEqual( host.Host.Failure, host.Link.LeftWith, "the host left the session with the reason" );
	}

	[TestMethod]
	public void TheSessionClockKeepsLevelsWithDifferentClocksEqual()
	{
		// a ride script that writes the hour, the minute and the second into its variables every slice, as Halloween's Haunted Tower writes the hour into the park
		var root = (string)typeof( UpgradeCatalogueDefaultsTests ).GetField( "root", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( fixture )!;
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().Label( "loop" )
			.I( Opcode.HOUR, RideVMTests.V( (int)RideVariables.VAR_CAPACITY ) )
			.I( Opcode.MIN, RideVMTests.V( (int)RideVariables.VAR_DURATION ) )
			.I( Opcode.SEC, RideVMTests.V( (int)RideVariables.VAR_ONRIDE ) )
			.I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" ).Build( Enum.GetNames<RideVariables>() ) );
		var random = new Random( 29 );
		var field = typeof( RideScriptWorld ).GetField( "civilTime", BindingFlags.Instance | BindingFlags.NonPublic )!;
		var offset = 0;
		var (host, guests, _) = Session( 1, peer =>
		{
			// every machine's own clock reads another time
			var own = new DateTime( 1999, 1, 1, 3, 0, 0 ).AddMinutes( 37 * offset++ );
			field.SetValue( peer.Level.Objects.ScriptWorld, (Func<DateTime>)(() => own.AddSeconds( clock )) );
		} );
		Rounds( 300, random );
		CatchUpAndCompare( host, guests, random );
		Assert.IsNotNull( host.Level.Objects.ScriptWorld.SessionTime );
		Assert.AreEqual( host.Level.Objects.ScriptWorld.SessionTime, guests[0].Level.Objects.ScriptWorld.SessionTime );
		Assert.IsTrue( guests[0].Level.Objects.ScriptWorld.TryReadCivilTime( out var read ) );
		Assert.AreEqual( host.Level.Objects.ScriptWorld.SessionTime, read, "the guest's scripts read the session's time, not their machine's" );
		Assert.IsTrue( host.Level.Commands.Log.Count( entry => entry.Command is SetSessionClockCommand ) >= 4, "a clock command every second" );
	}
}
