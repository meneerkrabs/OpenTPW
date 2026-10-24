using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Coop;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-session] Sharing the open park from the game (C2d-2; docs/COOP.md, "Sharing the park from the game"): one <see cref="CoopShare"/> attempt over a real
/// link to the loopback server, with the asset-free park of the stream tests, stepped as the game steps it (the online pump, then the attempt, then the park's frame).
/// A second player's raw link plays the guest.
/// </summary>
[TestClass, DoNotParallelize]
public class CoopShareTests
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

		// the host ends it for everyone; the park stays the host's
		share.End();
		Assert.AreEqual( (CoopShareStage.Done, CoopShareOutcome.Ended, true), (share.Stage, share.Outcome, share.EndedBySelf) );
		Assert.IsNull( level.CoopSession );
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
		await Frames( share, () => share.LastRefusal != null, "the invitation's answer", new[] { host, guest } );
		Assert.AreEqual( CoopReason.NotOnline, share.LastRefusal!.Reason );
		Assert.IsTrue( guest.Link.Join( share.State!.Code ) );
		await Frames( share, () => share.State!.Members.Count == 2, "the guest's join", new[] { host, guest } );
		Assert.IsTrue( share.Kick( share.State!.Members.Single( member => member.Id != share.State!.Host ).Id ) );
		await Frames( share, () => guest.Ended.Count == 1, "the kick", new[] { host, guest } );
		Assert.AreEqual( CoopReason.Kicked, guest.Ended[0].Reason );
		Assert.AreEqual( CoopShareStage.Hosting, share.Stage, "the park stays shared" );
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

	/// <summary>The game's sharing as the screen sees it: the attempt it is given.</summary>
	private sealed class Hosting( Func<CoopShare?> sharing ) : ICoopHosting
	{
		public CoopShareRefusal? ShareRefusal { get; set; } = CoopShareRefusal.None;
		public CoopShare? Sharing => sharing();
		public readonly List<CoopShareRequest> Started = new();
		public int Dismissed;
		public bool StartSharing( CoopShareRequest request )
		{
			Started.Add( request );
			return true;
		}
		public void DismissSharing() => Dismissed++;
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
			var client = new Online.Client.OnlineClient( new Uri( "http://127.0.0.1:9/" ) );
			typeof( Online.Client.OnlineClient ).GetField( "<Session>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!
				.SetValue( client, new Online.Api.SessionToken( "Hostess", "screen-test-only", DateTimeOffset.MaxValue ) );
			typeof( OnlineSession ).GetProperty( nameof( OnlineSession.Client ) )!.SetValue( session, client );
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
