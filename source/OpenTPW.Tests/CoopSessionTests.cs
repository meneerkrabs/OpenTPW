using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Coop;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-lockstep] The level in a co-op session (<see cref="CoopSession"/>, docs/COOP.md, "The level in a session"), with the stream tests' asset-free park: the role,
/// every end of a session (a leave, the link's end, the level closing), the rules every peer applies alike (no cheats, no developer kinds, no secret park name), a guest's
/// refused click, a guest that never saves, a park that runs at its speed whatever screens a player has open, a queue tool that changes nothing in the park, and a host and a
/// guest kept equal through the game's real links to a loopback server, a lost socket of each included.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CoopSessionTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;
	private List<Entity>? entities;
	private Level? current;
	private readonly List<Park> parks = new();
	private double clock;
	private double? hostTimeFrozenAt;
	/// <summary>The in-memory server of the last <see cref="Shared"/> session.</summary>
	private CoopTestNetwork? network;

	/// <summary>One player's park: its level, its entities (process statics: switched to its own while it runs) and its session.</summary>
	private sealed class Park
	{
		public Level Level { get; init; } = null!;
		public ParkEconomy Economy { get; init; } = null!;
		public OriginalObject One { get; init; } = null!;
		public List<Entity> Entities { get; init; } = null!;
		/// <summary>What the game's pump does before the frame: the link raises what arrived.</summary>
		public Action Pump { get; set; } = () => { };
		public CoopSession? Session { get; set; }
		/// <summary>The host's park holds (its time scale 0) while the guest catches up at the end.</summary>
		public bool Holds { get; set; }

		public void Enter()
		{
			Entity.All = Entities;
			Level.Current = Level;
		}

		/// <summary>One game frame as the game runs it: the pump, then the park at the time scale <see cref="GameFlow.SessionTimeScale"/> gives a shared park.</summary>
		public void Frame( double seconds )
		{
			Enter();
			Pump();
			Level.SimulationTimeScale = Holds ? 0f : GameFlow.SessionTimeScale( Level ) ?? GameSpeedScale.Of( Economy.Speed );
			Time.Update( (float)seconds );
			Level.AdvanceFrame( seconds );
		}

		/// <summary>Issues <paramref name="command"/> as this park's player between two frames, as its HUD does.</summary>
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
		foreach ( var park in parks )
		{
			park.Enter();
			park.Session?.End( "The test ended." );
			foreach ( var entity in park.Entities.ToArray() )
				entity.Delete();
		}
		Entity.All = new List<Entity>();
		fixture?.Cleanup();
		renderer?.Dispose();
		CoopSession.ChangesParkOutsideCommands = false;
		GameEditions.Select( GameEdition.ThemeParkWorld );
		if ( entities != null )
			Entity.All = entities;
		Level.Current = current!;
	}

	private Park NewPark()
	{
		var built = CoopStreamTests.BuildPark( fixture! );
		var park = new Park { Level = built.Level, Economy = built.Economy, One = built.One, Entities = built.Entities };
		// the asset-free park has no name of its own yet (a renamed park needs one) and no track renderers (a closing park disposes them)
		typeof( Level ).GetProperty( nameof( Level.ParkName ) )!.SetValue( park.Level, new ParkNameLines( "Test", "Park" ) );
		typeof( Level ).GetField( "trackRenderers", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( park.Level, new Dictionary<TrackFamily, TrackRenderer>() );
		parks.Add( park );
		return park;
	}

	/// <summary>The host's local civil time in the tests: an afternoon, moving with the virtual clock.</summary>
	private DateTime HostTime() => new DateTime( 2026, 10, 21, 15, 0, 0 ).AddSeconds( hostTimeFrozenAt ?? clock );

	/// <summary>A host and a guest of one in-memory session (<see cref="CoopTestNetwork"/>), both parks alike, their sessions started through <see cref="CoopSession"/>.</summary>
	private (Park Host, Park Guest, CoopTestNetwork.Member HostLink, CoopTestNetwork.Member GuestLink) Shared()
	{
		network = new CoopTestNetwork( () => clock );
		var hostLink = network.Add( host: true );
		var guestLink = network.Add();
		var host = NewPark();
		var guest = NewPark();
		host.Pump = () => hostLink.Deliver();
		guest.Pump = () => guestLink.Deliver();
		hostLink.Deliver();
		guestLink.Deliver();
		host.Enter();
		Assert.IsTrue( CoopSession.CanShare( host.Level, out var reason ), reason );
		host.Session = CoopSession.Host( host.Level, hostLink, () => clock, HostTime );
		guest.Enter();
		guest.Session = CoopSession.Join( guest.Level, guestLink, () => clock );
		return (host, guest, hostLink, guestLink);
	}

	private void Rounds( int count, params Park[] members )
	{
		for ( var round = 0; round < count; round++ )
		{
			clock += 1 / 60.0;
			foreach ( var park in members )
				park.Frame( 1 / 60.0 );
		}
	}

	/// <summary>The host holds its park and its clock until the guest stands at the host's tick with every record applied; then their digests, world hashes and logs are equal.</summary>
	private void CatchUpAndCompare( Park host, Park guest )
	{
		host.Holds = true;
		hostTimeFrozenAt = clock;
		var hostStream = host.Session!.HostStream!;
		var guestStream = guest.Session!.GuestStream!;
		for ( var round = 0; round < 3000 && (guest.Level.TickCount != host.Level.TickCount || guestStream.LastRecord != hostStream.LastRecord
			|| guest.Level.Commands.ScheduledCount > 0); round++ )
			Rounds( 1, host, guest );
		hostTimeFrozenAt = null;
		host.Holds = false;
		Compare( host, guest );
	}

	private static void Compare( Park host, Park guest )
	{
		Assert.IsNull( guest.Session!.GuestStream!.Desync, $"{guest.Session.GuestStream.Desync}" );
		Assert.IsNull( host.Session!.HostStream!.Failure );
		Assert.AreEqual( host.Level.TickCount, guest.Level.TickCount, "the guest caught up" );
		Assert.AreEqual( CoopDigest.Compute( host.Level ), CoopDigest.Compute( guest.Level ), "the session digest" );
		Assert.AreEqual( host.Level.ComputeStateHash(), guest.Level.ComputeStateHash(), "the world hash" );
		CollectionAssert.AreEqual( host.Level.Commands.Log.ToArray(), guest.Level.Commands.Log.ToArray(), "the same commands before the same ticks" );
	}

	[TestMethod]
	public void ASharedParkHasItsRoleUntilItsSessionEnds()
	{
		var (host, guest, hostLink, guestLink) = Shared();
		Assert.AreEqual( CoopRole.Host, host.Level.CoopRole );
		Assert.AreEqual( CoopRole.Guest, guest.Level.CoopRole );
		Assert.IsNotNull( host.Session!.HostStream );
		Assert.IsNull( host.Session.GuestStream );
		Assert.IsNotNull( guest.Session!.GuestStream );
		Assert.IsFalse( CoopSession.CanShare( host.Level, out var reason ) );
		Assert.AreEqual( "The park is shared already.", reason );
		Assert.ThrowsException<InvalidOperationException>( () => CoopSession.Host( host.Level, hostLink ) );

		// a guest's own click is refused before it runs (only the host builds), its HUD hears of it, and its log stays the host's stream
		var logged = guest.Level.Commands.Log.Count;
		Assert.IsFalse( guest.Issue( new SetEntranceFeeCommand( 99 ) ).Accepted );
		Assert.AreEqual( 1, guest.Level.SessionRefusals );
		Assert.AreEqual( logged, guest.Level.Commands.Log.Count );
		Assert.IsTrue( host.Issue( new SetEntranceFeeCommand( 5 ) ).Accepted );
		Assert.AreEqual( 0, host.Level.SessionRefusals, "the host's clicks are its own" );
		Rounds( 60, host, guest );
		CatchUpAndCompare( host, guest );

		// the guest leaves: its stream goes with the session's rules, its link leaves with the reason, once
		var ended = new List<string>();
		guest.Session.Ended += ended.Add;
		guest.Session.End( "Time for tea." );
		guest.Session.End( "Again." );
		CollectionAssert.AreEqual( new[] { "Time for tea." }, ended );
		Assert.AreEqual( "Time for tea.", guestLink.LeftWith );
		Assert.AreEqual( "Time for tea.", guest.Session.EndedWith );
		Assert.AreEqual( CoopRole.None, guest.Level.CoopRole );
		Assert.IsNull( guest.Level.CoopSession );
		Assert.IsNull( guest.Level.SessionStream );
		Assert.IsFalse( guest.Level.Commands.SessionRules, "the session's rules end with it" );
		Assert.IsTrue( guest.Issue( new SetEntranceFeeCommand( 7 ) ).Accepted, "a park of its own again" );
		Assert.AreEqual( 1, guest.Level.SessionRefusals );

		// the host's link ends the session (as it does for a member kicked or away when it ended): the session ends with the link's reason and leaves nothing of its own
		hostLink.End( "The session ended while you were away." );
		host.Frame( 1 / 60.0 );
		Assert.AreEqual( "The session ended while you were away.", host.Session.EndedWith );
		Assert.IsNull( hostLink.LeftWith, "the link's own end is no leave" );
		Assert.AreEqual( CoopRole.None, host.Level.CoopRole );
		Assert.IsNull( host.Level.SessionStream );
	}

	[TestMethod]
	public void AParkThatClosesEndsItsSession()
	{
		var (host, guest, hostLink, _) = Shared();
		var session = host.Session!;
		host.Enter();
		host.Level.Dispose();
		Assert.AreEqual( "The park was closed.", session.EndedWith );
		Assert.AreEqual( "The park was closed.", hostLink.LeftWith, "a host's leave ends the session for everyone" );
		Assert.IsNull( host.Level.CoopSession );
		Assert.AreEqual( CoopRole.Guest, guest.Level.CoopRole );
	}

	[TestMethod]
	public void OnlyARunningThemeParkWorldParkOfAGameWithoutDeveloperOptionsIsShared()
	{
		var park = NewPark();
		Assert.IsTrue( CoopSession.CanShare( park.Level, out var reason ), reason );
		CoopSession.ChangesParkOutsideCommands = true;
		Assert.IsFalse( CoopSession.CanShare( park.Level, out reason ) );
		StringAssert.Contains( reason, "developer options" );
		CoopSession.ChangesParkOutsideCommands = false;
		GameEditions.Select( GameEdition.ThemeParkInc );
		Assert.IsFalse( CoopSession.CanShare( park.Level, out reason ) );
		Assert.AreEqual( "Only Theme Park World parks are shared.", reason );
		GameEditions.Select( GameEdition.ThemeParkWorld );
		var sandbox = NewPark();
		typeof( Level ).GetProperty( nameof( Level.OriginalPark ) )!.SetValue( sandbox.Level, null );
		Assert.IsFalse( CoopSession.CanShare( sandbox.Level, out reason ), "the developer sandbox is no park of the game's" );
		Assert.AreEqual( "Only Theme Park World parks are shared.", reason );
		var bare = (Level)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		typeof( Level ).GetProperty( nameof( Level.OriginalPark ) )!.SetValue( bare, park.Level.OriginalPark );
		Assert.IsFalse( CoopSession.CanShare( bare, out reason ) );
		Assert.AreEqual( "Only a running park is shared.", reason );
		var link = new CoopTestNetwork().Add( host: true );
		Assert.ThrowsException<InvalidOperationException>( () => CoopSession.Host( bare, link ) );
		Assert.IsNull( bare.CoopSession );
	}

	[TestMethod]
	public void ASessionRefusesCheatsAndTheDeveloperKindsAlikeOnEveryPeer()
	{
		// outside a session the secret park name turns the cheats on
		var alone = NewPark();
		Assert.IsTrue( alone.Issue( new RenameParkCommand( "Kevin the", "Cosmic Cow" ) ).Accepted );

		var (host, guest, _, _) = Shared();
		Rounds( 30, host, guest );
		var refused = new ParkCommand[]
		{
			new RenameParkCommand( "Kevin the", "Cosmic Cow" ),
			new QueueCheatCommand( CheatKind.Mode ),
			new QueueCheatCommand( CheatKind.Money ),
			new SetAllObjectsOpenCommand( false ),
			new SetPrototypeRunningCommand( true )
		};
		foreach ( var command in refused )
		{
			var result = host.Issue( command );
			Assert.IsFalse( result.Accepted, command.ToString() );
			StringAssert.Contains( result.Reason, "shared park", command.ToString() );
		}
		Assert.IsTrue( host.Issue( new RenameParkCommand( "Our", "Park" ) ).Accepted, "any other name goes through" );
		Rounds( 60, host, guest );
		// the guest refused the host's refused records alike: a record whose outcome differs is a desync
		CatchUpAndCompare( host, guest );
		foreach ( var command in refused )
			Assert.IsTrue( host.Level.Commands.Log.Any( entry => entry.Command.Equals( command ) ), $"the refused {command} is in the stream" );
		Assert.IsFalse( host.Economy.Cheats.Mode || guest.Economy.Cheats.Mode, "no cheats in a shared park" );
	}

	[TestMethod]
	public void AGuestNeverSavesAndASharedParkRunsAtItsSpeed()
	{
		var automaticEligible = typeof( GameFlow ).GetMethod( "AutomaticEligible", BindingFlags.Static | BindingFlags.NonPublic )!;
		bool Saves( Park park ) => (bool)automaticEligible.Invoke( null, new object[] { park.Level } )!;
		var (host, guest, _, _) = Shared();
		Assert.IsTrue( Saves( host ) && GameFlow.TakesRestartSnapshot( host.Level ), "the host saves and restarts as before" );
		Assert.IsFalse( Saves( guest ), "no automatic checkpoint, autosave or exit checkpoint for a guest" );
		Assert.IsFalse( GameFlow.TakesRestartSnapshot( guest.Level ), "no restart snapshot for a guest" );

		var alone = NewPark();
		Assert.IsNull( GameFlow.SessionTimeScale( alone.Level ), "outside a session the HUD's screens may stop the park" );
		Assert.AreEqual( GameSpeedScale.Of( GameSpeed.Normal ), GameFlow.SessionTimeScale( host.Level ) );
		Assert.IsTrue( host.Issue( new SetGameSpeedCommand( GameSpeed.Fastest ) ).Accepted );
		Assert.AreEqual( GameSpeedScale.Of( GameSpeed.Fastest ), GameFlow.SessionTimeScale( host.Level ), "a shared park runs at its speed whatever screens are open" );
		Rounds( 90, host, guest );
		CatchUpAndCompare( host, guest );
		Assert.AreEqual( GameSpeedScale.Of( GameSpeed.Fastest ), GameFlow.SessionTimeScale( guest.Level ), "the guest's park follows the host's speed" );
		Assert.IsTrue( host.Issue( new SetGameSpeedCommand( GameSpeed.Paused ) ).Accepted );
		Assert.AreEqual( 0f, GameFlow.SessionTimeScale( host.Level ), "the park's own pause stops it" );
	}

	/// <summary>
	/// The frame period, the frame sizes and the digests at the fastest speed (docs/COOP.md, "Measurements"): through a build session the host publishes at most a frame every
	/// 100 ms, each small, and takes a digest once a second of real time, every fourth park second. The measured numbers are written to the test's output.
	/// </summary>
	[TestMethod]
	public void AtTheFastestSpeedTheHostPublishesSmallFramesAtMostTenASecondAndADigestEverySecond()
	{
		var (host, guest, _, _) = Shared();
		Assert.IsTrue( host.Issue( new SetGameSpeedCommand( GameSpeed.Fastest ) ).Accepted );
		var start = clock;
		var taken = network!.Taken.Count;
		var script = new (int Round, Func<ParkCommand> Command)[]
		{
			(30, () => host.Level.BuildCommand( host.One.Entry, 20, 12, 90 )),
			(60, () => new LayLineCommand( CellToolMode.Path, -1, 5, 20, 12, 20 )),
			(90, () => new LayLineCommand( CellToolMode.Path, -1, 12, 21, 12, 26 )),
			(300, () => new RemovePathCellCommand( 6, 20 )),
			(420, () => new SetEntranceFeeCommand( 5 )),
			(600, () => new RemoveAtCommand( 20, 12 )),
			(900, () => new SetTrainingBudgetCommand( StaffType.Mechanic, 150 )),
		};
		for ( var round = 0; round < 1200; round++ )
		{
			foreach ( var (_, command) in script.Where( item => item.Round == round ) )
				Assert.IsTrue( host.Issue( command() ).Accepted, $"the scripted edit of round {round}" );
			Rounds( 1, host, guest );
		}
		var seconds = clock - start;
		var frames = network.Taken.Skip( taken ).ToArray();
		var sizes = frames.Select( frame => frame.Length ).ToArray();
		var digests = frames.SelectMany( frame => CoopStreamFrame.Decode( frame ).Checkpoints ).Where( point => point.Kind == CoopCheckpointKind.AfterTick ).Select( point => point.At ).ToArray();
		Console.WriteLine( $"The fastest speed, {seconds:0.0} s ({host.Level.TickCount} ticks): {sizes.Length} frames, {sizes.Length / seconds:0.0} a second; "
			+ $"{sizes.Average():0} bytes on average, {sizes.Max()} at most; {sizes.Sum() / seconds:0} bytes a second; {digests.Length} digests." );
		Assert.IsTrue( sizes.Length / seconds <= 10.5, $"at most a frame every 100 ms ({sizes.Length} in {seconds:0.0} s)" );
		Assert.IsTrue( sizes.Max() < 1024, $"a build session's frames stay small ({sizes.Max()} bytes)" );
		Assert.AreEqual( 4 * FixedStepClock.TicksPerSecond, CoopHostStream.CheckpointInterval( GameSpeed.Fastest ) );
		Assert.IsTrue( digests.Length >= 18 && digests.All( at => at % CoopHostStream.CheckpointInterval( GameSpeed.Fastest ) == 0 ),
			$"a digest every fourth park second: {string.Join( ", ", digests )}" );
		CollectionAssert.AreEqual( digests.Skip( 1 ).ToArray(), digests.SkipLast( 1 ).Select( at => at + CoopHostStream.CheckpointInterval( GameSpeed.Fastest ) ).ToArray(), "and no digest left out" );
		CatchUpAndCompare( host, guest );
	}

	/// <summary>A queue of <paramref name="count"/> cells for <paramref name="item"/> from (<paramref name="x"/>, 6) up toward the path row (the queue removal tests' synthetic geometry).</summary>
	private static void ConfigureQueue( GuestPathGrid grid, OriginalObject item, int x, int count )
	{
		var ride = item.Visitors;
		ride.EntranceCell = ride.ExitCell = (x, 1);
		ride.QueueFrontCell = (x, 6);
		ride.QueueEntranceDirection = 2;
		ride.OwnerWord = ParkCellMap.CellWord( item.Placement.X, item.Placement.Y );
		ride.Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 );
		Assert.AreEqual( count, QueuePaths.TryExtend( grid, ride, Enumerable.Range( 0, count ).Select( index => (x, 6 - index) ) ) );
		QueuePaths.TryConnect( grid, ride );
		grid.Cells.SetFlags( x, 6, grid.Cells.FlagsAt( x, 6 ) | ParkCellFlags.NoModify );
		grid.Cells.SetPlacementCount( x, 6, 1 );
		grid.Cells.SetType( x, 7, ParkCellTypes.Entrance );
		grid.Cells.SetLinks( x, 7, GuestPathGrid.LinkValue( 0 ) );
		grid.Cells.SetQueueLink( x, 7, GuestPathGrid.LinkValue( 0 ) );
	}

	/// <summary>Everything a recompute of <paramref name="ride"/>'s queue writes.</summary>
	private static string QueueState( RideVisitorBridge ride ) =>
		$"{string.Join( " ", ride.QueueCells )} | join {ride.JoinCell} | front {ride.QueueFrontCell} {ride.QueueEntranceDirection} | edits {ride.QueueEditCount} | room {ride.MaximumQueueLength}";

	[TestMethod]
	public void EnteringTheQueueToolChangesNothingInTheParkAndItsLinesBuildTheSameQueueOnATwin()
	{
		var issuer = NewPark();
		var twin = NewPark();
		foreach ( var park in new[] { issuer, twin } )
		{
			park.Enter();
			var grid = park.Level.Guests!.Grid;
			ConfigureQueue( grid, park.One, 10, 5 );
			// a grid edit no recompute has taken in yet (as a paused park holds one until its next tick): the queue's back cell goes
			grid.ClearQueue( 10, 2 );
		}
		var ride = issuer.One.Visitors;
		issuer.Enter();
		var fresh = ride.QueueView( issuer.Level.Guests!.Grid );
		Assert.AreEqual( 4, fresh.QueueCells.Count, "the queue as it stands" );
		Assert.AreEqual( 5, ride.QueueCells.Count, "the ride's own queue as last recomputed" );
		var before = (issuer.Level.ComputeStateHash(), CoopDigest.Compute( issuer.Level ), QueueState( ride ));

		// the player's tool (the placement's, a track's or a coaster's end, the ride window's edit button, a queue click) is this machine's alone: it changes nothing a peer shares
		Assert.IsTrue( issuer.Level.EnterQueueTool( ride ) );
		Assert.AreEqual( before, (issuer.Level.ComputeStateHash(), CoopDigest.Compute( issuer.Level ), QueueState( ride )), "entering the queue tool changes nothing in the park" );
		Assert.AreEqual( fresh.QueueBackCell, issuer.Level.CellTool.Start, "the tool starts at the queue's back cell as it stands" );
		Assert.AreEqual( (10, 3), fresh.QueueBackCell );
		var line = issuer.Level.CellTool.Click( (10, 1) );
		Assert.AreEqual( CellBuildResult.Ok, line?.StoppedBy ?? CellBuildResult.Ok, "the line lays the cell again and joins the path" );
		Assert.IsFalse( issuer.Level.CellTool.IsActive, "a line onto a path ends the tool" );
		Assert.IsInstanceOfType( issuer.Level.Commands.Log.Single().Command, typeof( QueueSegmentCommand ), "the line is the tool's one command" );
		Assert.AreEqual( 5, ride.QueueCells.Count );
		Assert.AreEqual( QueueState( ride.QueueView( issuer.Level.Guests.Grid ) ).Split( " | edits" )[0], QueueState( ride ).Split( " | edits" )[0], "the command left the queue up to date" );

		// the twin took no tool: the commands alone build the same queue and the same park
		twin.Enter();
		foreach ( var entry in issuer.Level.Commands.Log )
			Assert.IsTrue( twin.Level.Commands.Apply( entry.Command ).Accepted, entry.Command.ToString() );
		Assert.AreEqual( QueueState( ride ), QueueState( twin.One.Visitors ) );
		Assert.AreEqual( issuer.Level.ComputeStateHash(), twin.Level.ComputeStateHash() );
		Assert.AreEqual( CoopDigest.Compute( issuer.Level ), CoopDigest.Compute( twin.Level ) );
	}

	[TestMethod]
	public void AQueueCommandTakesInAQueueEditSinceTheLastRecompute()
	{
		var park = NewPark();
		park.Enter();
		var grid = park.Level.Guests!.Grid;
		ConfigureQueue( grid, park.One, 10, 5 );
		var ride = park.One.Visitors;
		// a queue cell behind the back cell that no recompute has taken in yet: the tool, reading the queue as it stands, starts its line there
		grid.SetQueue( 9, 2, towards: GuestPathGrid.DirectionBetween( 9, 2, 10, 2 ) );
		Assert.AreEqual( (9, 2), ride.QueueView( grid ).QueueBackCell );
		Assert.AreEqual( (10, 2), ride.QueueBackCell );
		var result = park.Issue( new LayLineCommand( CellToolMode.Queue, ride.AttractionId, 9, 2, 7, 2 ) );
		Assert.IsTrue( result.Accepted );
		var line = (SegmentResult)result.Value!;
		CollectionAssert.AreEqual( new[] { (8, 2), (7, 2) }, line.Built.ToArray(), "the command brought the queue up to date first: the line goes on from its back cell" );
		CollectionAssert.AreEqual( new[] { (9, 2) }, line.Existing.ToArray() );
		Assert.AreEqual( 8, ride.QueueCells.Count );
		Assert.AreEqual( (7, 2), ride.QueueBackCell );
	}

	[TestMethod]
	public async Task AHostAndAGuestStayEqualThroughTheirRealLinksAndALostSocketEach()
	{
		await CoopLinkTests.WithServer( async ( server, players ) =>
		{
			var hostPlayer = await players.Add( server, "Hostess" );
			var guestPlayer = await players.Add( server, "Guest", firstFrame: true );
			await CoopLinkTests.Open( hostPlayer, guestPlayer );
			await CoopLinkTests.Share( hostPlayer, guestPlayer );

			var host = NewPark();
			var guest = NewPark();
			host.Pump = () => hostPlayer.Session.Pump();
			guest.Pump = () => guestPlayer.Session.Pump();
			host.Enter();
			host.Session = CoopSession.Host( host.Level, hostPlayer.Link, () => clock, HostTime );
			guest.Enter();
			guest.Session = CoopSession.Join( guest.Level, guestPlayer.Link, () => clock );

			// the scripted edits by round, a lost socket of each player among them
			var script = new (int Round, Func<ParkCommand> Command)[]
			{
				(10, () => new SetResearchEffortCommand( ResearchCategory.Ride, 60 )),
				(20, () => host.Level.BuildCommand( host.One.Entry, 20, 12, 90 )),
				(25, () => new LayLineCommand( CellToolMode.Path, -1, 5, 20, 12, 20 )),
				(40, () => new SetGameSpeedCommand( GameSpeed.Fastest )),
				(90, () => new RemovePathCellCommand( 6, 20 )),
				(130, () => new SetGameSpeedCommand( GameSpeed.Paused )),
				(132, () => new SetEntranceFeeCommand( 5 )),
				(150, () => new SetGameSpeedCommand( GameSpeed.Fast )),
				(200, () => new RemoveAtCommand( 20, 12 )),
				(240, () => new SetGameSpeedCommand( GameSpeed.Normal )),
			};
			var lostSockets = 0;
			// three seconds at the normal speed after the script, whatever the lost sockets held
			for ( var round = 0; round < 420; round++ )
			{
				foreach ( var (_, command) in script.Where( item => item.Round == round ) )
					Assert.IsTrue( host.Issue( command() ).Accepted, $"the scripted edit of round {round}" );
				if ( round == 60 )
				{
					guestPlayer.Link.DropSocket();
					lostSockets++;
				}
				if ( round == 170 )
				{
					hostPlayer.Link.DropSocket();
					lostSockets++;
				}
				if ( round == 100 )
					Assert.IsFalse( guest.Issue( new SetEntranceFeeCommand( 99 ) ).Accepted, "only the host builds" );
				clock += 1 / 60.0;
				host.Frame( 1 / 60.0 );
				guest.Frame( 1 / 60.0 );
				await Task.Delay( 2 );
			}
			Assert.AreEqual( 2, lostSockets );
			Assert.IsTrue( hostPlayer.Link.Connections >= 2 && guestPlayer.Link.Connections >= 2, "each socket was lost and the link resumed" );

			// the host holds its park and its clock until the guest stands at its tick with every record applied
			host.Holds = true;
			hostTimeFrozenAt = clock;
			var until = DateTime.UtcNow + OnlineIntegrationTests.Deadline;
			while ( guest.Level.TickCount != host.Level.TickCount || guest.Session.GuestStream!.LastRecord != host.Session.HostStream!.LastRecord
				|| guest.Level.Commands.ScheduledCount > 0 )
			{
				Assert.IsTrue( DateTime.UtcNow < until, $"the guest catches up: tick {guest.Level.TickCount} of {host.Level.TickCount}, record {guest.Session.GuestStream!.LastRecord} of {host.Session.HostStream!.LastRecord}" );
				clock += 1 / 60.0;
				host.Frame( 1 / 60.0 );
				guest.Frame( 1 / 60.0 );
				await Task.Delay( 2 );
			}
			Compare( host, guest );
			// a digest a second of real time: one at the fastest speed (tick 240) and at least two in the last three seconds at the normal speed
			Assert.IsTrue( guest.Session.GuestStream!.Checks >= 3, $"the guest compared the host's digests ({guest.Session.GuestStream.Checks})" );
			Assert.AreEqual( 1, guest.Level.SessionRefusals );

			// the host ends the session: the guest's game hears it through its link, and its park is its own again
			host.Session.End( "Closing time." );
			Assert.AreEqual( CoopRole.None, host.Level.CoopRole );
			until = DateTime.UtcNow + OnlineIntegrationTests.Deadline;
			while ( guest.Session.EndedWith == null )
			{
				Assert.IsTrue( DateTime.UtcNow < until, "the guest hears the session end" );
				guest.Frame( 1 / 60.0 );
				await Task.Delay( 2 );
			}
			Assert.AreEqual( CoopRole.None, guest.Level.CoopRole );
			Assert.IsNull( guestPlayer.Link.Session );
		} );
	}
}
