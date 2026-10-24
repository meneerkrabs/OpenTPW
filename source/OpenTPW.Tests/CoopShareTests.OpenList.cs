using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Api;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-session] The open list of shared parks as its screen shows it (C2d-3b; docs/COOP.md, "Invitations and the open list"): the hub's line to it, the latest answer of
/// the login's requests only, a full park that cannot be joined, Join with the chosen park's code, the rows another login clears, and a game that cannot follow said up front.
/// The list's answers are the test's own (<see cref="CoopLink.Lister"/>): the server never lists a full park.
/// </summary>
public partial class CoopShareTests
{
	[TestMethod]
	public async Task TheOpenListShowsTheLatestAnswerOfItsLoginAndJoinTakesTheChosenParksCode()
	{
		var session = new OnlineSession( OnlineIntegrationTests.Folders() );
		try
		{
			var answers = new List<TaskCompletionSource<CoopSessionList>>();
			session.Coop.Lister = _ =>
			{
				var answer = new TaskCompletionSource<CoopSessionList>( TaskCreationOptions.RunContinuationsAsynchronously );
				lock ( answers )
					answers.Add( answer );
				return answer.Task;
			};
			int Asked()
			{
				lock ( answers )
					return answers.Count;
			}
			var build = session.Coop.Identity().Build;
			CoopListedSession Park( string code, string title, int players, int maximum ) => new( code, title, "Hostess", players, maximum, build );
			var joining = new FakeJoining();
			var (screens, stack) = LobbyScreens( session, new Hosting( () => null ), joining );
			async Task Until( Func<bool> condition, string what )
			{
				var deadline = DateTime.UtcNow + OnlineIntegrationTests.Deadline;
				while ( !condition() )
				{
					Assert.IsTrue( DateTime.UtcNow < deadline, $"Waited in vain for {what}." );
					session.Pump();
					Update( stack );
					await Task.Delay( 5 );
				}
			}
			string Message() => Element<UI.Original.UiLabel>( stack, "message" ).Text();

			// the hub's third line opens the list; logged out, it offers the login and asks for nothing
			screens.ShowBuildTogetherLobby();
			Element<UI.Original.UiButton>( stack, "findShared" ).Clicked!();
			Assert.AreEqual( UI.OnlineScreens.SharedParksScreenName, stack.Top!.Name );
			var list = Element<UI.Original.UiScrollList>( stack, "parks" );
			var join = Element<UI.Original.UiButton>( stack, "join" );
			var refresh = Element<UI.Original.UiButton>( stack, "refresh" );
			Update( stack );
			Assert.IsTrue( Element<UI.Original.UiButton>( stack, "login" ).Visible );
			Assert.AreEqual( English( UI.Original.OpenTpwText.LogInToJoin ), Message() );
			Assert.AreEqual( 0, Asked() );

			// logged in, it asks; a second request (Refresh's own click) answered first is the one shown, also when the older answer comes later
			LogIn( session, "Guest" );
			Update( stack );
			await Until( () => Asked() == 1, "the list to be asked for" );
			Assert.AreEqual( English( UI.Original.OpenTpwText.SharedParksLooking ), Message() );
			Assert.IsFalse( refresh.Enabled, "one request at a time from the screen" );
			refresh.Clicked!();
			await Until( () => Asked() == 2, "the second request" );
			answers[1].SetResult( new CoopSessionList( new[] { Park( "BBBBBB", "Newer park", 1, 4 ), Park( "CCCCCC", "Full park", 4, 4 ) } ) );
			await Until( () => list.Rows().Count == 2, "the newer answer" );
			answers[0].SetResult( new CoopSessionList( new[] { Park( "AAAAAA", "Older park", 1, 4 ) } ) );
			for ( var round = 0; round < 10; round++ )
			{
				session.Pump();
				Update( stack );
				await Task.Delay( 5 );
			}
			CollectionAssert.AreEqual( new[] { "Newer park (Hostess)", "Full park (Hostess)" }, list.Rows().ToArray() );
			Assert.AreEqual( "1/4", list.Trailing!( 0 ) );
			Assert.IsTrue( refresh.Enabled );

			// a full park cannot be joined; the other one is, with its code, on the join screen
			list.Selected = 1;
			Update( stack );
			Assert.IsFalse( join.Enabled );
			Assert.AreEqual( English( UI.Original.OpenTpwText.JoinFull ), Message() );
			list.Selected = 0;
			Update( stack );
			Assert.IsTrue( join.Enabled );
			Assert.AreEqual( "", Message() );
			join.Clicked!();
			CollectionAssert.AreEqual( new[] { "BBBBBB" }, joining.Started );
			Assert.AreEqual( UI.OnlineScreens.JoinSharedParkScreenName, stack.Top!.Name );
			stack.Top!.Back!();
			Assert.AreEqual( UI.OnlineScreens.SharedParksScreenName, stack.Top!.Name );

			// another login has none of these rows: they go, and its own list is asked for
			session.Disconnect();
			Update( stack );
			Assert.AreEqual( 0, list.Rows().Count );
			Assert.IsTrue( Element<UI.Original.UiButton>( stack, "login" ).Visible );
			LogIn( session, "Other" );
			Update( stack );
			await Until( () => Asked() == 3, "the new login's list" );
			Assert.AreEqual( English( UI.Original.OpenTpwText.SharedParksLooking ), Message() );
			answers[2].SetResult( new CoopSessionList( new[] { Park( "DDDDDD", "Other park", 2, 4 ) } ) );
			await Until( () => list.Rows().Count == 1, "the new login's rows" );

			// a game that cannot follow a shared park says so up front and joins nothing
			CoopSession.ChangesParkOutsideCommands = true;
			try
			{
				Update( stack );
				Assert.AreEqual( English( UI.Original.OpenTpwText.JoinNotAllowed ), Message() );
				Assert.IsFalse( join.Enabled );
			}
			finally
			{
				CoopSession.ChangesParkOutsideCommands = false;
			}
		}
		finally
		{
			session.Dispose();
		}
	}
}
