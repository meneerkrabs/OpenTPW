using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-session] Sharing a saved park from the lobby (C2d-2b; docs/COOP.md, "Sharing a saved park from the lobby"): the lobby's Build Together screen, the
/// share that goes with the saved park's queued load, and the share of the park the load made.
/// </summary>
public partial class CoopShareTests
{
	private static void SetField( object target, string name, object? value ) =>
		target.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	/// <summary>A game in the lobby (no park, no transition) over <paramref name="session"/>, as the gate tests make one.</summary>
	private static GameFlow LobbyFlow( OnlineSession session )
	{
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		SetField( flow, "<Strings>k__BackingField", OriginalUiTests.FakeStrings() );
		SetField( flow, "onlineSession", session );
		return flow;
	}

	[TestMethod]
	public void TheLobbysBuildTogetherSharesTheChosenSavedPark()
	{
		var session = new OnlineSession( OnlineIntegrationTests.Folders() );
		try
		{
			var saves = new[]
			{
				new ParkLoadEntry( "jungle", -1, false, "/saves/opentpw-jungle-world.json", "Jungle Fun", new DateTime( 2026, 10, 20, 9, 5, 0 ) ),
				new ParkLoadEntry( "space", -1, false, "/saves/opentpw-space-world.json", "Moon Base", null, Cheated: true ),
			};
			var hosting = new Hosting( () => null ) { Saves = saves };
			var (screens, stack) = Screens( session, hosting );
			screens.ShowShareSavedPark();
			Assert.AreEqual( UI.OnlineScreens.ShareSavedParkScreenName, stack.Top!.Name );
			Update( stack );
			// the saved parks read as on the Load Park screen
			var list = Element<UI.Original.UiScrollList>( stack, "savedParks" );
			CollectionAssert.AreEqual( new[] { "Jungle Fun", "Moon Base (" + English( UI.Original.OpenTpwText.CheatedPark ) + ")" }, list.Rows().ToArray() );
			Assert.AreEqual( 0, list.Selected );
			// logged out: the login is offered and the share waits for it
			Assert.IsTrue( Element<UI.Original.UiButton>( stack, "login" ).Visible );
			Assert.IsFalse( Element<UI.Original.UiButton>( stack, "share" ).Enabled );
			Assert.AreEqual( English( UI.Original.OpenTpwText.LogInToShare ), Element<UI.Original.UiLabel>( stack, "message" ).Text() );

			// logged in: the chosen park goes with the player's choices
			LogIn( session, "Hostess" );
			Update( stack );
			Assert.IsFalse( Element<UI.Original.UiButton>( stack, "login" ).Visible );
			Assert.AreEqual( "", Element<UI.Original.UiLabel>( stack, "message" ).Text() );
			Assert.AreEqual( string.Format( English( UI.Original.OpenTpwText.SharedParkDefaultTitle ), "Hostess" ), Element<UI.Original.UiTextField>( stack, "parkTitle" ).Text );
			list.Selected = 1;
			var title = Element<UI.Original.UiTextField>( stack, "parkTitle" );
			title.Text = "Moon party";
			title.Changed!( title.Text );
			Element<UI.Original.UiButton>( stack, "players" ).Clicked!();
			Update( stack );
			Assert.IsTrue( Element<UI.Original.UiButton>( stack, "share" ).Enabled );
			Element<UI.Original.UiButton>( stack, "share" ).Clicked!();
			Assert.AreEqual( (saves[1], new CoopShareRequest( "Moon party", Listed: false, MaximumPlayers: 2 )), hosting.SharedSaved.Single() );
		}
		finally
		{
			session.Dispose();
		}
	}

	[TestMethod]
	public void TheLobbysBuildTogetherSaysWhenThereIsNoSavedPark()
	{
		var session = new OnlineSession( OnlineIntegrationTests.Folders() );
		try
		{
			LogIn( session, "Hostess" );
			var (screens, stack) = Screens( session, new Hosting( () => null ) );
			screens.ShowShareSavedPark();
			Update( stack );
			Assert.AreEqual( English( UI.Original.OpenTpwText.NoSavedParks ), Element<UI.Original.UiLabel>( stack, "message" ).Text() );
			Assert.IsFalse( Element<UI.Original.UiScrollList>( stack, "savedParks" ).Visible );
			Assert.IsFalse( Element<UI.Original.UiTextField>( stack, "parkTitle" ).Visible );
			Assert.IsFalse( Element<UI.Original.UiButton>( stack, "share" ).Visible );
		}
		finally
		{
			session.Dispose();
		}
	}

	[TestMethod]
	public void ASavedParkIsSharedFromTheLobbyOnlyAndAFailedLoadSharesNothing()
	{
		var session = new OnlineSession( OnlineIntegrationTests.Folders() );
		try
		{
			var flow = LobbyFlow( session );
			var save = new ParkLoadEntry( "jungle", -1, false, "/nonexistent/opentpw-jungle-world.json", "Jungle Fun" );
			var request = new CoopShareRequest( "Saved fun", Listed: false, 4 );
			Assert.IsFalse( flow.ShareSavedPark( save, request ), "logged out" );
			LogIn( session, "Hostess" );
			Assert.IsFalse( flow.ShareSavedPark( new ParkLoadEntry( "jungle", 0, true ), request ), "the sandbox is no own save" );
			SetField( flow, "<Level>k__BackingField", NewPark().Level );
			Assert.IsFalse( flow.ShareSavedPark( save, request ), "a park is open" );
			SetField( flow, "<Level>k__BackingField", null );

			// the share goes with the save's queued load (no extensions to ask about: the save cannot be read here)
			Assert.IsTrue( flow.ShareSavedPark( save, request ) );
			var load = (Action?)typeof( GameFlow ).GetField( "pending", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( flow );
			Assert.IsNotNull( load );
			Assert.IsFalse( flow.ShareSavedPark( save, request ), "a transition waits" );
			// the load fails (there is no save): the lobby stays, and nothing is shared
			SetField( flow, "pending", null );
			load!();
			Assert.IsNull( flow.Level );
			Assert.IsNull( flow.Sharing );
		}
		finally
		{
			session.Dispose();
		}
	}

	[TestMethod]
	public async Task TheParkALoadMadeIsSharedWithTheLobbysChoices() => await CoopLinkTests.WithServer( async ( server, players ) =>
	{
		var host = await players.Add( server, "Hostess" );
		var flow = LobbyFlow( host.Session );
		flow.CoopSessionClock = () => clock;
		var (level, _) = NewPark();
		var request = new CoopShareRequest( "Saved fun", Listed: true, 3 );
		// a load that made no park (it failed in the lobby, or left the park that was open) shares nothing
		flow.ShareLoadedPark( null, request );
		Assert.IsNull( flow.Sharing );
		SetField( flow, "<Level>k__BackingField", level );
		flow.ShareLoadedPark( level, request );
		Assert.IsNull( flow.Sharing );

		// the park the load made is shared with the lobby's choices
		flow.ShareLoadedPark( null, request );
		var share = flow.Sharing ?? throw new AssertFailedException( "The loaded park is not being shared." );
		shares.Add( share );
		var deadline = DateTime.UtcNow + OnlineIntegrationTests.Deadline;
		while ( share.Stage is CoopShareStage.Preparing or CoopShareStage.Creating )
		{
			host.Session.Pump();
			flow.StepCoopShare();
			if ( DateTime.UtcNow > deadline )
				Assert.Fail( $"Waited in vain for the loaded park to be hosted: {share.Stage} ({share.Outcome}: {share.FailureText})." );
			await Task.Delay( 5 );
		}
		Assert.AreEqual( CoopShareStage.Hosting, share.Stage );
		Assert.AreEqual( ("Saved fun", true, (byte)3), (share.State!.Title, share.State.Listed, share.State.MaximumPlayers) );
		Assert.AreSame( share.Session, level.CoopSession );
	} );
}
