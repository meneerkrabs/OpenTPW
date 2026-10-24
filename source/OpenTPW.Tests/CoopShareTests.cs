using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Coop;
using OpenTPW.Server;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-session] Sharing the open park from the game (C2d-2; docs/COOP.md, "Sharing the park from the game"): one <see cref="CoopShare"/> attempt over a real
/// link to the loopback server, with the asset-free park of the stream tests, stepped as the game steps it (the online pump, then the attempt, then the park's frame).
/// A second player's raw link plays the guest.
/// </summary>
[TestClass, DoNotParallelize]
public partial class CoopShareTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;
	private List<Entity>? entities;
	private Level? current;
	private readonly List<List<Entity>> built = new();
	private readonly List<CoopShare> shares = new();
	private double clock;
	private (float Now, float Delta) time;
	private bool textEntry;

	[TestInitialize]
	public void Initialize()
	{
		entities = Entity.All;
		current = Level.Current;
		// the hosted park's frames move the game clock, and a focused text field of the screen marks text entry: other tests read both
		time = (Time.Now, Time.Delta);
		textEntry = Input.TextEntryActive;
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var share in shares )
		{
			share.Session?.End( "The test ended." );
			share.Dispose();
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
		if ( entities != null )
			Entity.All = entities;
		Level.Current = current!;
		(Time.Now, Time.Delta) = time;
		Input.TextEntryActive = textEntry;
	}

	private (Level Level, List<Entity> Entities) NewPark()
	{
		var park = CoopStreamTests.BuildPark( fixture! );
		built.Add( park.Entities );
		// the asset-free park has no name of its own yet and no track renderers (a closing park disposes them)
		typeof( Level ).GetProperty( nameof( Level.ParkName ) )!.SetValue( park.Level, new ParkNameLines( "Test", "Park" ) );
		typeof( Level ).GetField( "trackRenderers", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( park.Level, new Dictionary<TrackFamily, TrackRenderer>() );
		return (park.Level, park.Entities);
	}

	private CoopShare Share( Level level, CoopLinkTests.Player host, int players = 4 )
	{
		var share = new CoopShare( level, host.Link, new CoopShareRequest( "Our park", Listed: false, players ), () => clock );
		shares.Add( share );
		return share;
	}

	/// <summary>
	/// The game's frames until <paramref name="done"/>: every player's online pump, the attempt's step with <paramref name="live"/> as the game's park (by default the
	/// attempt's own), and the park's frame while it is hosted.
	/// </summary>
	private async Task Frames( CoopShare share, Func<bool> done, string what, CoopLinkTests.Player[] players, Func<Level?>? live = null, List<Entity>? park = null )
	{
		var deadline = DateTime.UtcNow + OnlineIntegrationTests.Deadline;
		while ( !done() )
		{
			foreach ( var player in players )
				player.Session.Pump();
			share.Step( live != null ? live() : share.Level );
			if ( share.Stage == CoopShareStage.Hosting && park != null )
			{
				Entity.All = park;
				Level.Current = share.Level;
				clock += 1 / 60.0;
				share.Level.SimulationTimeScale = GameFlow.SessionTimeScale( share.Level ) ?? 1f;
				Time.Update( 1 / 60f );
				share.Level.AdvanceFrame( 1 / 60.0 );
			}
			if ( DateTime.UtcNow > deadline )
				Assert.Fail( $"Waited in vain for {what}: the attempt is {share.Stage} ({share.Outcome}: {share.FailureText}); " +
					string.Join( "; ", players.Select( player => $"{player.Name} {player.Link.State} stopped {player.Link.Stopped}, errors {string.Join( ", ", player.Errors )}" ) ) );
			await Task.Delay( 5 );
		}
	}

	[TestMethod]
	public async Task SharingTheOpenParkHostsItAndTheHostsEndReachesTheGuest() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var guest = await players.Add( server, "Guest" );
		await CoopLinkTests.Open( guest );
		var (level, park) = NewPark();
		var share = Share( level, host );
		Assert.AreEqual( CoopShareStage.Preparing, share.Stage );
		await Frames( share, () => share.Stage == CoopShareStage.Hosting, "the park to be hosted", new[] { host, guest } );
		Assert.AreSame( share.Session, level.CoopSession );
		Assert.AreEqual( CoopRole.Host, level.CoopRole );
		var state = share.State!;
		Assert.AreEqual( ("Our park", false, (byte)4), (state.Title, state.Listed, state.MaximumPlayers) );

		// a guest joins with the code and follows the host's frames
		Assert.IsTrue( guest.Link.Join( state.Code ) );
		await Frames( share, () => guest.Frames.Count > 0 && share.State!.Members.Count == 2, "the guest's frames", new[] { host, guest }, park: park );
		Assert.AreEqual( string.Format( English( UI.Original.OpenTpwText.SharedParkStatus ), state.Code, 2, 4 ), GameFlow.SessionStatusLine( share, level, English ) );

		// the host ends it for everyone; the park stays the host's, and the status line says nothing of an end the player made
		share.End();
		Assert.AreEqual( (CoopShareStage.Done, CoopShareOutcome.Ended, true), (share.Stage, share.Outcome, share.EndedBySelf) );
		Assert.IsNull( level.CoopSession );
		Assert.IsNull( GameFlow.SessionStatusLine( share, level, English ) );
		await CoopLinkTests.Until( () => guest.Ended.Count == 1, "the guest to hear the end", host, guest );
		Assert.AreEqual( (CoopReason.HostLeft, CoopShare.EndedByHost), (guest.Ended[0].Reason, guest.Ended[0].Text) );
	} );

	[TestMethod]
	public async Task ACancelBeforeTheCreateSendsNothing() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var (level, _) = NewPark();
		var share = Share( level, host );
		share.Step( level );
		Assert.AreEqual( CoopShareStage.Preparing, share.Stage );
		share.Cancel();
		Assert.AreEqual( (CoopShareStage.Done, CoopShareOutcome.Cancelled), (share.Stage, share.Outcome) );
		// the link opens on (it was asked to), and no session comes of the cancelled attempt
		await CoopLinkTests.Until( () => host.Link.State == CoopLinkState.Open, "the link to open", host );
		await Task.Delay( 200 );
		host.Session.Pump();
		Assert.AreEqual( 0, host.Started.Count );
		Assert.IsNull( level.CoopSession );
	} );

	[TestMethod]
	public async Task ACancelAfterTheCreateLeavesTheSessionTheServerMade() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var guest = await players.Add( server, "Guest" );
		await CoopLinkTests.Open( guest );
		var (level, _) = NewPark();
		var share = Share( level, host );
		await Frames( share, () => share.Stage == CoopShareStage.Creating, "the create", new[] { host } );
		share.Cancel();
		Assert.AreEqual( CoopShareStage.Cancelling, share.Stage );
		await Frames( share, () => share.Stage == CoopShareStage.Done, "the cancelled attempt to end", new[] { host } );
		Assert.AreEqual( CoopShareOutcome.Cancelled, share.Outcome );
		Assert.IsNull( level.CoopSession );
		Assert.IsNull( host.Link.Session, "the created session was left" );
		// the session the server made is gone: its code admits no one
		var code = host.Started.Single().Code;
		Assert.IsTrue( guest.Link.Join( code ) );
		await CoopLinkTests.Until( () => guest.Errors.Count > 0, "the join to be refused", guest );
		Assert.AreEqual( CoopReason.UnknownCode, guest.Errors[0].Reason );
	} );

	[TestMethod]
	public async Task AParkClosedBeforeTheAnswerLeavesTheSessionTheServerMade() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var (level, _) = NewPark();
		var share = Share( level, host );
		await Frames( share, () => share.Stage == CoopShareStage.Creating, "the create", new[] { host } );
		// the game's park is gone (a load, an exit): the attempt waits for the answer and leaves what it made
		await Frames( share, () => share.Stage == CoopShareStage.Done, "the attempt to end", new[] { host }, live: () => null );
		Assert.AreEqual( CoopShareOutcome.ParkClosed, share.Outcome );
		Assert.IsNull( level.CoopSession );
		Assert.IsNull( host.Link.Session );
		Assert.AreEqual( 1, host.Started.Count, "the server made the session, which was left" );
	} );

	[TestMethod]
	public async Task AParkThatCannotBeSharedWhenTheAnswerComesIsNotHosted() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var (level, _) = NewPark();
		var share = Share( level, host );
		await Frames( share, () => share.Stage == CoopShareStage.Creating, "the create", new[] { host } );
		// a cheat waits for its park turn by the time the answer comes (decision Q17: it would apply inside the session)
		Assert.IsTrue( level.Park!.Economy.Cheats.Queue( new CheatAction( CheatKind.Money, 0 ) ) );
		await Frames( share, () => share.Stage == CoopShareStage.Done, "the attempt to end", new[] { host } );
		Assert.AreEqual( (CoopShareOutcome.Refused, CoopShareRefusal.CheatPending), (share.Outcome, share.Refusal) );
		Assert.IsNull( level.CoopSession );
		Assert.IsNull( host.Link.Session );
	} );

	[TestMethod]
	public async Task ALogoutWhileHostingEndsTheParkForTheGuestAtOnce() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var guest = await players.Add( server, "Guest" );
		await CoopLinkTests.Open( guest );
		var (level, _) = NewPark();
		var share = Share( level, host );
		await Frames( share, () => share.Stage == CoopShareStage.Hosting, "the park to be hosted", new[] { host, guest } );
		Assert.IsTrue( guest.Link.Join( share.State!.Code ) );
		await Frames( share, () => guest.Link.Session != null && share.State!.Members.Count == 2, "the guest's join", new[] { host, guest } );

		// the logout leaves the park on its socket: the guest hears the host left, with the reason, not after the away limit
		host.Session.Disconnect();
		Assert.AreEqual( (CoopShareStage.Done, CoopShareOutcome.Ended, false), (share.Stage, share.Outcome, share.EndedBySelf) );
		Assert.IsNull( level.CoopSession );
		await CoopLinkTests.Until( () => guest.Ended.Count == 1, "the guest to hear the end", guest );
		Assert.AreEqual( (CoopReason.HostLeft, OnlineSession.LeftOnLogout), (guest.Ended[0].Reason, guest.Ended[0].Text) );
	} );

	[TestMethod]
	public async Task ALogoutWhileTheCreateWaitsFailsTheAttempt() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var (level, _) = NewPark();
		var share = Share( level, host );
		await Frames( share, () => share.Stage == CoopShareStage.Creating, "the create", new[] { host } );
		host.Session.Disconnect();
		share.Step( level );
		Assert.AreEqual( (CoopShareStage.Done, CoopShareOutcome.CreateRefused, CoopReason.Unknown), (share.Stage, share.Outcome, share.CreateRefusal) );
		Assert.IsNull( level.CoopSession );
	} );

	/// <summary>The hub's live sessions, the ends it remembers (one an account) and its members (white-box, read under the hub's own lock).</summary>
	private static (int Live, (CoopReason Reason, string Text)[] Ended, int Members) Hub( LoopbackServer server )
	{
		var hub = server.Services.GetRequiredService<CoopHub>();
		object Field( string name ) => typeof( CoopHub ).GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hub )!;
		lock ( Field( "registry" ) )
		{
			var ended = ((System.Collections.IDictionary)Field( "endedSessions" )).Values.Cast<object>()
				.Select( end => ((CoopReason)end.GetType().GetField( "Item2" )!.GetValue( end )!, (string)end.GetType().GetField( "Item3" )!.GetValue( end )!) ).ToArray();
			return (((System.Collections.ICollection)Field( "sessions" )).Count, ended, ((System.Collections.ICollection)Field( "memberSessions" )).Count);
		}
	}

	[TestMethod]
	public async Task QuittingWhileTheCreateWaitsLeavesTheSessionTheServerMakes() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var (level, _) = NewPark();
		var share = Share( level, host );
		await Frames( share, () => share.Stage == CoopShareStage.Creating, "the create", new[] { host } );
		// the game quits before the answer reaches it (GameFlow.Dispose): the attempt is forgotten, then the login's close leaves on the create's socket, after it
		share.Dispose();
		host.Session.Dispose();
		var deadline = DateTime.UtcNow + OnlineIntegrationTests.Deadline;
		while ( Hub( server ).Ended.Length == 0 )
		{
			if ( DateTime.UtcNow > deadline )
				Assert.Fail( $"The server still holds what the create made: {Hub( server )}." );
			await Task.Delay( 10 );
		}
		var hub = Hub( server );
		Assert.AreEqual( (0, 0), (hub.Live, hub.Members), "no session and no member is left on the server" );
		Assert.AreEqual( (CoopReason.HostLeft, OnlineSession.LeftOnLogout), hub.Ended.Single() );
		Assert.IsNull( level.CoopSession );
	} );

	[TestMethod]
	public async Task AnEndThePumpDeliversIsOnTheStatusLineForItsTime() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var other = await players.Add( server, "Hostess", register: false );
		var (level, park) = NewPark();
		var share = Share( level, host );
		await Frames( share, () => share.Stage == CoopShareStage.Hosting, "the park to be hosted", new[] { host }, park: park );
		Assert.AreEqual( string.Format( English( UI.Original.OpenTpwText.SharedParkStatus ), share.State!.Code, 1, 4 ), GameFlow.SessionStatusLine( share, level, English ) );
		// another game of the account takes the link over: the end reaches this game in its online pump, before the attempt's step
		other.Link.Open( takeOver: true );
		await Frames( share, () => share.Stage == CoopShareStage.Done, "the take-over's end", new[] { host, other } );
		Assert.AreEqual( (CoopShareOutcome.Ended, false), (share.Outcome, share.EndedBySelf) );
		Assert.AreEqual( English( UI.Original.OpenTpwText.SharedParkEnded ), GameFlow.SessionStatusLine( share, level, English ) );
		// the player dismisses the outcome on the Build Together screen: the game keeps the end for the status line all the same
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		typeof( GameFlow ).GetField( "coopShare", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( flow, share );
		flow.DismissSharing();
		Assert.IsNull( flow.Sharing );
		Assert.AreSame( share, flow.StatusShare );
		clock += GameFlow.SharingEndedSeconds - 0.5;
		Assert.AreEqual( English( UI.Original.OpenTpwText.SharedParkEnded ), GameFlow.SessionStatusLine( flow.StatusShare, level, English ), "still within its time" );
		clock += 1;
		Assert.IsNull( GameFlow.SessionStatusLine( flow.StatusShare, level, English ), "its time is over" );
	} );

	[TestMethod]
	public async Task AnOlderServerStopsTheAttempt() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		// the server's version says it has no co-op socket
		typeof( OnlineSession ).GetProperty( nameof( OnlineSession.ServerProtocol ) )!.SetValue( host.Session, CoopProtocol.Version - 1 );
		var (level, _) = NewPark();
		var share = Share( level, host );
		await Frames( share, () => share.Stage == CoopShareStage.Done, "the attempt to end", new[] { host } );
		Assert.AreEqual( (CoopShareOutcome.LinkStopped, CoopStop.ServerTooOld), (share.Outcome, share.Stop) );
	} );

	[TestMethod]
	public async Task AFailedHostingLeavesTheSessionTheServerMade() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var (level, _) = NewPark();
		var share = Share( level, host );
		await Frames( share, () => share.Stage == CoopShareStage.Creating, "the create", new[] { host } );
		// the answer comes in the pump; the link no longer holds its session when the step hosts (CoopSession.Host refuses)
		await CoopLinkTests.Until( () => host.Started.Count == 1, "the answer", host );
		host.Link.Leave( "Gone before hosting." );
		share.Step( level );
		Assert.AreEqual( (CoopShareStage.Done, CoopShareOutcome.HostFailed), (share.Stage, share.Outcome) );
		Assert.IsNull( level.CoopSession );
	} );

	[TestMethod]
	public async Task AnInvitationToAPlayerWhoIsNotOnlineIsAnsweredAndAKickEndsTheGuestsSession() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var guest = await players.Add( server, "Guest" );
		await players.Add( server, "Absent" );
		await CoopLinkTests.Open( guest );
		var (level, _) = NewPark();
		var share = Share( level, host );
		await Frames( share, () => share.Stage == CoopShareStage.Hosting, "the park to be hosted", new[] { host, guest } );
		Assert.IsTrue( share.Invite( "Absent" ) );
		Assert.AreEqual( "Absent", share.Inviting );
		await Frames( share, () => share.LastNotice?.Refusal != null, "the invitation's answer", new[] { host, guest } );
		Assert.AreEqual( (CoopMessageType.Invite, "Absent", CoopReason.NotOnline), (share.LastNotice!.Request, share.LastNotice.Player, share.LastNotice.Refusal!.Reason) );
		Assert.IsNull( share.Inviting );
		Assert.IsTrue( guest.Link.Join( share.State!.Code ) );
		await Frames( share, () => share.State!.Members.Count == 2, "the guest's join", new[] { host, guest } );
		Assert.IsTrue( share.Kick( share.State!.Members.Single( member => member.Id != share.State!.Host ).Id ) );
		await Frames( share, () => guest.Ended.Count == 1, "the kick", new[] { host, guest } );
		Assert.AreEqual( CoopReason.Kicked, guest.Ended[0].Reason );
		Assert.AreEqual( CoopShareStage.Hosting, share.Stage, "the park stays shared" );
	} );

	[TestMethod]
	public async Task OneInvitationWaitsAtATimeSoARefusalIsItsOwn() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var guest = await players.Add( server, "Guest" );
		await players.Add( server, "Absent" );
		await CoopLinkTests.Open( guest );
		var (level, _) = NewPark();
		// late in a long run (where a float clock no longer moves by a quarter second): the waits count seconds of a clock that keeps its precision
		clock = 16_777_216;
		var share = Share( level, host );
		await Frames( share, () => share.Stage == CoopShareStage.Hosting, "the park to be hosted", new[] { host, guest } );
		// a second invitation waits for the first one's answer: the line under the invitation speaks of one player at a time
		Assert.IsTrue( share.Invite( "Absent" ) );
		Assert.IsFalse( share.Invite( "Guest" ) );
		await Frames( share, () => share.LastNotice?.Refusal != null, "the first invitation's refusal", new[] { host, guest } );
		Assert.AreEqual( ("Absent", CoopReason.NotOnline), (share.LastNotice!.Player, share.LastNotice.Refusal!.Reason) );

		// an invitation the server does not refuse went out, which the host knows once its wait is over
		Assert.IsTrue( share.Invite( "Guest" ) );
		await Frames( share, () => guest.Invitations.Count == 1, "the guest's invitation", new[] { host, guest } );
		Assert.AreEqual( "Guest", share.Inviting );
		Assert.IsFalse( share.Invite( "Absent" ) );
		clock += CoopShare.InviteAnswerSeconds - 0.25;
		share.Step( level );
		Assert.AreEqual( "Guest", share.Inviting, "still within its wait" );
		clock += 0.25;
		share.Step( level );
		Assert.IsNull( share.Inviting );
		Assert.AreEqual( new CoopShareNotice( CoopMessageType.Invite, "Guest", null ), share.LastNotice );
	} );

	[TestMethod]
	public async Task ALateRefusalNamesItsOwnPlayerAndARefusedRemovalIsNoInvitations() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var guest = await players.Add( server, "Guest" );
		await players.Add( server, "Absent" );
		await CoopLinkTests.Open( guest );
		var (level, _) = NewPark();
		var share = Share( level, host );
		await Frames( share, () => share.Stage == CoopShareStage.Hosting, "the park to be hosted", new[] { host, guest } );
		// the wait is over before the refusal reaches the game (a slow link), and a newer invitation waits by then: the refusal's text names its own player
		Assert.IsTrue( share.Invite( "Absent" ) );
		clock += CoopShare.InviteAnswerSeconds;
		share.Step( level );
		Assert.IsNull( share.Inviting );
		Assert.IsTrue( share.Invite( "Guest" ) );
		await Frames( share, () => share.LastNotice?.Refusal != null, "the late refusal", new[] { host, guest } );
		Assert.AreEqual( (CoopMessageType.Invite, "Absent", CoopReason.NotOnline), (share.LastNotice!.Request, share.LastNotice.Player, share.LastNotice.Refusal!.Reason) );
		Assert.AreEqual( "Guest", share.Inviting, "the newer invitation still waits for its own answer" );

		// a removal of no member is refused as a removal, not as an invitation
		Assert.IsTrue( share.Kick( 99 ) );
		await Frames( share, () => share.LastNotice!.Request == CoopMessageType.Kick, "the removal's refusal", new[] { host } );
		Assert.AreEqual( (CoopMessageType.Kick, (string?)null), (share.LastNotice!.Request, share.LastNotice.Player) );
		Assert.IsNotNull( share.LastNotice.Refusal );
		Assert.AreEqual( CoopShareStage.Hosting, share.Stage );
	} );

	/// <summary>The online screens over <paramref name="session"/> with <paramref name="hosting"/>, on a stack of their own (the pause menu below).</summary>
	private static (UI.OnlineScreens Screens, UI.Original.UiScreenStack Stack) Screens( OnlineSession session, ICoopHosting hosting )
	{
		var stack = new UI.Original.UiScreenStack();
		stack.Push( new UI.Original.UiScreen( "pauseMenu" ) );
		var screens = new UI.OnlineScreens( stack, OriginalUiTests.FakeStrings(), new UI.Original.UiModels( model => throw new System.IO.FileNotFoundException( model ) ),
			new UI.OnlineHost { Session = session, Hosting = hosting } );
		return (screens, stack);
	}

	private static void Update( UI.Original.UiScreenStack stack ) =>
		stack.Update( OriginalUiTests.FakeContext(), UI.Original.UiInput.Idle( new System.Numerics.Vector2( -1, -1 ) ) );

	private static T Element<T>( UI.Original.UiScreenStack stack, string id ) where T : UI.Original.UiElement =>
		(T)(stack.Top!.Find( id ) ?? throw new AssertFailedException( $"No element {id} on {stack.Top!.Name}." ));

	private static string English( UI.Original.OpenTpwText key ) => UI.Original.SupplementaryStrings.Get( key, "English" );

	/// <summary>The game's sharing as the screen sees it: the attempt it is given, and the saved parks the lobby offers.</summary>
	private sealed class Hosting( Func<CoopShare?> sharing ) : ICoopHosting
	{
		public CoopShareRefusal? ShareRefusal { get; set; } = CoopShareRefusal.None;
		public CoopShare? Sharing => sharing();
		public readonly List<CoopShareRequest> Started = new();
		public int Dismissed;
		public IReadOnlyList<FrontEnd.ParkLoadEntry> Saves { get; set; } = Array.Empty<FrontEnd.ParkLoadEntry>();
		public readonly List<(FrontEnd.ParkLoadEntry Park, CoopShareRequest Request)> SharedSaved = new();
		public bool StartSharing( CoopShareRequest request )
		{
			Started.Add( request );
			return true;
		}
		public void DismissSharing() => Dismissed++;
		public IReadOnlyList<FrontEnd.ParkLoadEntry> SavedParks() => Saves;
		public bool ShareSavedPark( FrontEnd.ParkLoadEntry park, CoopShareRequest request )
		{
			SharedSaved.Add( (park, request) );
			return true;
		}
	}

	/// <summary>Logs <paramref name="session"/> in as <paramref name="name"/> without a transport: a session's token, as the screens read it.</summary>
	private static void LogIn( OnlineSession session, string name )
	{
		var client = new Online.Client.OnlineClient( new Uri( "http://127.0.0.1:9/" ) );
		typeof( Online.Client.OnlineClient ).GetField( "<Session>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!
			.SetValue( client, new Online.Api.SessionToken( name, "screen-test-only", DateTimeOffset.MaxValue ) );
		typeof( OnlineSession ).GetProperty( nameof( OnlineSession.Client ) )!.SetValue( session, client );
	}

	[TestMethod]
	public void TheBuildTogetherScreenAsksForALoginAndSharesWithThePlayersChoices()
	{
		var session = new OnlineSession( OnlineIntegrationTests.Folders() );
		try
		{
			var hosting = new Hosting( () => null );
			var (screens, stack) = Screens( session, hosting );
			screens.ShowBuildTogether();
			Assert.AreEqual( UI.OnlineScreens.BuildTogetherScreenName, stack.Top!.Name );
			Update( stack );
			// logged out: the login is offered and the share waits for it
			Assert.IsTrue( Element<UI.Original.UiButton>( stack, "login" ).Visible );
			Assert.IsFalse( Element<UI.Original.UiButton>( stack, "share" ).Enabled );
			Assert.AreEqual( English( UI.Original.OpenTpwText.LogInToShare ), Element<UI.Original.UiLabel>( stack, "message" ).Text() );

			// logged in (a session's token, no transport): the title, the open list and the players are the player's
			LogIn( session, "Hostess" );
			Update( stack );
			Assert.IsFalse( Element<UI.Original.UiButton>( stack, "login" ).Visible );
			Assert.IsTrue( Element<UI.Original.UiButton>( stack, "share" ).Enabled );
			// the title starts as the player's park once the name is known
			Assert.AreEqual( string.Format( English( UI.Original.OpenTpwText.SharedParkDefaultTitle ), "Hostess" ), Element<UI.Original.UiTextField>( stack, "parkTitle" ).Text );
			var titleField = Element<UI.Original.UiTextField>( stack, "parkTitle" );
			titleField.Text = "Ann's coasters";
			titleField.Changed!( titleField.Text );
			Update( stack );
			Element<UI.Original.UiButton>( stack, "listed" ).Clicked!();
			Element<UI.Original.UiButton>( stack, "players" ).Clicked!();
			Assert.AreEqual( string.Format( English( UI.Original.OpenTpwText.SharedParkPlayers ), 2 ), Element<UI.Original.UiButton>( stack, "players" ).Text!() );
			Element<UI.Original.UiButton>( stack, "share" ).Clicked!();
			Assert.AreEqual( new CoopShareRequest( "Ann's coasters", Listed: true, MaximumPlayers: 2 ), hosting.Started.Single() );

			// a park that cannot be shared says why, in words, and offers no share
			hosting.ShareRefusal = CoopShareRefusal.CheatPending;
			Update( stack );
			Assert.IsFalse( Element<UI.Original.UiButton>( stack, "share" ).Enabled );
			Assert.AreEqual( English( UI.Original.OpenTpwText.ShareRefusedCheat ), Element<UI.Original.UiLabel>( stack, "message" ).Text() );
		}
		finally
		{
			session.Dispose();
		}
	}

	[TestMethod]
	public async Task TheBuildTogetherScreenShowsTheHostedParkAndEndsItForEveryone() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var guest = await players.Add( server, "Guest" );
		await players.Add( server, "Absent" );
		await CoopLinkTests.Open( guest );
		var (level, _) = NewPark();
		var share = Share( level, host );
		var (screens, stack) = Screens( host.Session, new Hosting( () => share ) );
		screens.ShowBuildTogether();
		await Frames( share, () => share.Stage != CoopShareStage.Preparing, "the create", new[] { host, guest } );
		Update( stack );
		// while it is being shared: the stage, and a cancel
		if ( share.Stage == CoopShareStage.Creating )
		{
			Assert.IsTrue( Element<UI.Original.UiButton>( stack, "cancel" ).Visible );
			Assert.AreEqual( English( UI.Original.OpenTpwText.SharingThePark ), Element<UI.Original.UiLabel>( stack, "message" ).Text() );
		}
		await Frames( share, () => share.Stage == CoopShareStage.Hosting, "the park to be hosted", new[] { host, guest } );
		Assert.IsTrue( guest.Link.Join( share.State!.Code ) );
		await Frames( share, () => share.State!.Members.Count == 2, "the guest's join", new[] { host, guest } );
		Update( stack );
		// hosted: the code, the players (the host's line marked, a removal for the guest only), the invitation and the end
		Assert.AreEqual( string.Format( English( UI.Original.OpenTpwText.SharedParkCode ), share.State!.Code ), Element<UI.Original.UiLabel>( stack, "code" ).Text() );
		Assert.AreEqual( string.Format( English( UI.Original.OpenTpwText.SharedParkHost ), "Hostess" ), Element<UI.Original.UiLabel>( stack, "member0" ).Text() );
		Assert.AreEqual( "Guest", Element<UI.Original.UiLabel>( stack, "member1" ).Text() );
		Assert.IsFalse( Element<UI.Original.UiButton>( stack, "remove0" ).Visible );
		Assert.IsTrue( Element<UI.Original.UiButton>( stack, "remove1" ).Visible );
		Assert.IsFalse( Element<UI.Original.UiButton>( stack, "remove2" ).Visible );
		Assert.IsTrue( Element<UI.Original.UiButton>( stack, "invite" ).Visible );
		Assert.IsFalse( Element<UI.Original.UiButton>( stack, "share" ).Visible );
		// an invitation by name goes out at once; the next one waits for the server's answer, and a refusal names its own player
		Element<UI.Original.UiTextField>( stack, "inviteName" ).Text = "Absent";
		Element<UI.Original.UiButton>( stack, "invite" ).Clicked!();
		Update( stack );
		Assert.AreEqual( string.Format( English( UI.Original.OpenTpwText.InviteSent ), "Absent" ), Element<UI.Original.UiLabel>( stack, "inviteResult" ).Text() );
		Assert.IsFalse( Element<UI.Original.UiButton>( stack, "invite" ).Enabled );
		await Frames( share, () => share.LastNotice?.Refusal != null, "the invitation's refusal", new[] { host, guest } );
		Update( stack );
		Assert.AreEqual( string.Format( English( UI.Original.OpenTpwText.InviteNotOnline ), "Absent" ), Element<UI.Original.UiLabel>( stack, "inviteResult" ).Text() );
		Assert.IsTrue( Element<UI.Original.UiButton>( stack, "invite" ).Enabled );
		// a refused removal is no invitation's answer
		Assert.IsTrue( share.Kick( 99 ) );
		await Frames( share, () => share.LastNotice!.Request == Online.Coop.CoopMessageType.Kick, "the removal's refusal", new[] { host, guest } );
		Update( stack );
		Assert.AreEqual( "", Element<UI.Original.UiLabel>( stack, "inviteResult" ).Text() );
		// End sharing asks first; its yes ends the park for everyone
		Element<UI.Original.UiButton>( stack, "endSharing" ).Clicked!();
		Assert.AreEqual( "confirmEndSharing", stack.Top!.Name );
		stack.Top!.Elements.OfType<UI.Original.UiButton>().First().Clicked!();
		Assert.AreEqual( UI.OnlineScreens.BuildTogetherScreenName, stack.Top!.Name );
		Assert.AreEqual( (CoopShareStage.Done, CoopShareOutcome.Ended, true), (share.Stage, share.Outcome, share.EndedBySelf) );
		await CoopLinkTests.Until( () => guest.Ended.Count == 1, "the guest to hear the end", host, guest );
		Assert.AreEqual( CoopShare.EndedByHost, guest.Ended[0].Text );
		// the player ended it: the screen offers sharing again
		Update( stack );
		Assert.IsTrue( Element<UI.Original.UiButton>( stack, "share" ).Visible );
	} );

	[TestMethod]
	public void ThePauseMenuOffersBuildTogetherForThePlayersOwnPark()
	{
		Assert.IsTrue( Hud.ParkHud.PauseMenuOffersBuildTogether( readOnlyVisit: false, coopGuestPark: false, bankrupt: false, hosting: false ) );
		Assert.IsFalse( Hud.ParkHud.PauseMenuOffersBuildTogether( readOnlyVisit: true, coopGuestPark: false, bankrupt: false, hosting: false ), "a visit" );
		Assert.IsFalse( Hud.ParkHud.PauseMenuOffersBuildTogether( readOnlyVisit: false, coopGuestPark: true, bankrupt: false, hosting: false ), "a guest's copy" );
		Assert.IsFalse( Hud.ParkHud.PauseMenuOffersBuildTogether( readOnlyVisit: false, coopGuestPark: false, bankrupt: true, hosting: false ), "a lost park" );
		Assert.IsTrue( Hud.ParkHud.PauseMenuOffersBuildTogether( readOnlyVisit: false, coopGuestPark: false, bankrupt: true, hosting: true ), "a lost park that is shared" );
	}
}
