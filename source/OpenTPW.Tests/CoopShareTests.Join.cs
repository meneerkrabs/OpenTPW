using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>[EXT:online-coop-session] The lobby's Build Together hub and the join screen (C2d-3; docs/COOP.md, "Joining a shared park from the lobby").</summary>
public partial class CoopShareTests
{
	/// <summary>The game's joining as the screen sees it: the attempt it is given, and what the screen asked.</summary>
	private sealed class FakeJoining : ICoopJoining
	{
		public CoopJoinAttempt? Attempt { get; set; }
		public CoopJoinAttempt? Joining => Attempt;
		public readonly List<string> Started = new();
		public readonly List<string> TakenOver = new();
		public int Cancelled, Dismissed;
		public bool StartJoining( string code, bool takeOver = false )
		{
			Started.Add( code );
			if ( takeOver )
				TakenOver.Add( code );
			return true;
		}
		public void CancelJoining() => Cancelled++;
		public void DismissJoining() => Dismissed++;
	}

	/// <summary>The online screens over <paramref name="session"/> with the game's sharing and joining, on a stack of their own (the lobby below).</summary>
	private static (UI.OnlineScreens Screens, UI.Original.UiScreenStack Stack) LobbyScreens( OnlineSession session, ICoopHosting hosting, ICoopJoining joining )
	{
		var stack = new UI.Original.UiScreenStack();
		stack.Push( new UI.Original.UiScreen( "frontEnd" ) );
		var screens = new UI.OnlineScreens( stack, OriginalUiTests.FakeStrings(), new UI.Original.UiModels( model => throw new System.IO.FileNotFoundException( model ) ),
			new UI.OnlineHost { Session = session, Hosting = hosting, Joining = joining } );
		return (screens, stack);
	}

	[TestMethod]
	public void TheLobbysHubOpensTheShareAndTheJoinScreens()
	{
		var session = new OnlineSession( OnlineIntegrationTests.Folders() );
		try
		{
			var (screens, stack) = LobbyScreens( session, new Hosting( () => null ), new FakeJoining() );
			screens.ShowBuildTogetherLobby();
			Assert.AreEqual( UI.OnlineScreens.BuildTogetherLobbyScreenName, stack.Top!.Name );
			Element<UI.Original.UiButton>( stack, "shareSaved" ).Clicked!();
			Assert.AreEqual( UI.OnlineScreens.ShareSavedParkScreenName, stack.Top!.Name );
			stack.Top!.Back!();
			Assert.AreEqual( UI.OnlineScreens.BuildTogetherLobbyScreenName, stack.Top!.Name );
			Element<UI.Original.UiButton>( stack, "joinWithCode" ).Clicked!();
			Assert.AreEqual( UI.OnlineScreens.JoinSharedParkScreenName, stack.Top!.Name );
		}
		finally
		{
			session.Dispose();
		}
	}

	[TestMethod]
	public void TheJoinScreenSendsACodeShowsTheStageAndItsEndAndBackGivesItUp()
	{
		var session = new OnlineSession( OnlineIntegrationTests.Folders() );
		// the attempts the screen shows run over a link of their own (logged out: it stops at once)
		var other = new OnlineSession( OnlineIntegrationTests.Folders() );
		try
		{
			var joining = new FakeJoining();
			var (screens, stack) = LobbyScreens( session, new Hosting( () => null ), joining );
			screens.ShowJoinSharedPark();
			Assert.AreEqual( UI.OnlineScreens.JoinSharedParkScreenName, stack.Top!.Name );
			Update( stack );
			// logged out: the login is offered and Join waits for it
			Assert.IsTrue( Element<UI.Original.UiButton>( stack, "login" ).Visible );
			Assert.IsFalse( Element<UI.Original.UiButton>( stack, "join" ).Enabled );
			Assert.AreEqual( English( UI.Original.OpenTpwText.LogInToJoin ), Element<UI.Original.UiLabel>( stack, "message" ).Text() );

			// logged in: a code that cannot be one is said and not sent; a code goes as typed (case, spaces and dashes do not matter to the server)
			LogIn( session, "Guest" );
			var code = Element<UI.Original.UiTextField>( stack, "code" );
			code.Text = "no such!";
			Update( stack );
			Element<UI.Original.UiButton>( stack, "join" ).Clicked!();
			Assert.AreEqual( English( UI.Original.OpenTpwText.JoinNotACode ), Element<UI.Original.UiLabel>( stack, "message" ).Text() );
			Assert.AreEqual( 0, joining.Started.Count );
			code.Text = "abc-def";
			code.Changed!( code.Text );
			Element<UI.Original.UiButton>( stack, "join" ).Clicked!();
			CollectionAssert.AreEqual( new[] { "abc-def" }, joining.Started );
			Assert.IsTrue( code.MaximumLength >= "A - B - C - D - E - F".Length, "room for a code typed with spaces and dashes" );

			// while it joins: the stage and a cancel
			joining.Attempt = new CoopJoinAttempt( other.Coop, "ABCDEF" );
			Update( stack );
			Assert.AreEqual( English( UI.Original.OpenTpwText.JoiningThePark ), Element<UI.Original.UiLabel>( stack, "message" ).Text() );
			Assert.IsTrue( Element<UI.Original.UiButton>( stack, "cancel" ).Visible );
			Assert.IsFalse( Element<UI.Original.UiButton>( stack, "join" ).Visible );
			Element<UI.Original.UiButton>( stack, "cancel" ).Clicked!();
			Assert.AreEqual( 1, joining.Cancelled );

			// over: why in words, and a way back to the code
			joining.Attempt.Step();
			Assert.AreEqual( (CoopJoinStage.Done, CoopJoinOutcome.LinkStopped), (joining.Attempt.Stage, joining.Attempt.Outcome) );
			Update( stack );
			Assert.AreEqual( English( UI.Original.OpenTpwText.SharedParkLinkLost ), Element<UI.Original.UiLabel>( stack, "message" ).Text() );
			Assert.IsTrue( Element<UI.Original.UiButton>( stack, "again" ).Visible );
			Element<UI.Original.UiButton>( stack, "again" ).Clicked!();
			Assert.AreEqual( 1, joining.Dismissed );
			joining.Attempt.Dispose();

			// Back while a join runs gives it up
			joining.Attempt = new CoopJoinAttempt( other.Coop, "ABCDEF" );
			Update( stack );
			Element<UI.Original.UiButton>( stack, "back" ).Clicked!();
			Assert.AreEqual( 2, joining.Cancelled );
			Assert.AreEqual( "frontEnd", stack.Top!.Name );
			joining.Attempt.Dispose();
		}
		finally
		{
			session.Dispose();
			other.Dispose();
		}
	}
}
