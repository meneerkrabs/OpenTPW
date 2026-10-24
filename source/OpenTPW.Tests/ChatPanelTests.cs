using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Chat;
using OpenTPW.UI;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

[TestClass]
public class ChatPanelTests
{
	/// <summary>A room's chat as the overlay reads it; the test plays the server.</summary>
	private sealed class FakeChat : IChatPanelSource
	{
		public string? PlayerName { get; set; } = "Ann";
		public bool ChatOpen { get; set; } = true;
		public List<string> Players { get; } = new() { "Ann", "Bob", "Carla", "Dave" };
		public IReadOnlyList<string> RoomPlayers => Players;
		public List<(string Name, bool Online)> BuddyList { get; } = new();
		public IReadOnlyList<(string Name, bool Online)> Buddies => BuddyList;
		public HashSet<string> IgnoredNames { get; } = new( StringComparer.OrdinalIgnoreCase );
		public IReadOnlyCollection<string> Ignored => IgnoredNames;
		public bool Earmuffs { get; set; }
		public bool CanVote { get; set; } = true;
		public List<string> Lines { get; } = new();
		public IReadOnlyList<string> ChatLines => Lines;
		public List<string> Said { get; } = new();
		public List<(ChatCommand Command, string Argument)> Commands { get; } = new();
		public int BuddyRequests;
		public void SendChatLine( string line ) => Said.Add( line );
		public void SendCommand( ChatCommand command, string argument ) => Commands.Add( (command, argument) );
		public void AskBuddies() => BuddyRequests++;
	}

	private static FakeChat Room()
	{
		var chat = new FakeChat();
		// Bob is a buddy in the park, Eve one online elsewhere, Zed one offline; Ann ignores Carla
		chat.BuddyList.AddRange( new[] { ("Bob", true), ("Eve", true), ("Zed", false) } );
		chat.IgnoredNames.Add( "Carla" );
		return chat;
	}

	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new System.IO.FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 2048, 1536 ) };

	/// <summary>A focused text field sets the flag in a screen's update, and the game flow clears it each frame: here the test does, for the tests after it.</summary>
	[TestCleanup]
	public void Cleanup() => Input.TextEntryActive = false;

	[TestMethod]
	public void ThePeopleListFlagsBuddiesByWhereTheyAreAndColoursTheirNames()
	{
		var chat = Room();
		chat.Players.Add( "+3" );
		var people = ChatPanelModel.People( chat, buddies: false );
		CollectionAssert.AreEqual( new[] { "Ann", "Bob", "Carla", "Dave" }, people.Select( person => person.Name ).ToArray(), "a cut roster's +N is no player" );
		Assert.IsTrue( people[0].Own );
		Assert.AreEqual( ChatPersonFlags.Buddy | ChatPersonFlags.Online | ChatPersonFlags.InPark, people[1].Flags );
		Assert.AreEqual( ChatPersonFlags.Ignored, people[2].Flags );
		Assert.AreEqual( ChatPersonFlags.None, people[3].Flags );
		var buddies = ChatPanelModel.People( chat, buddies: true );
		CollectionAssert.AreEqual( new[] { "Bob", "Eve", "Zed" }, buddies.Select( person => person.Name ).ToArray() );
		Assert.AreEqual( ChatPersonFlags.Buddy | ChatPersonFlags.Online, buddies[1].Flags );
		Assert.AreEqual( ChatPersonFlags.Buddy, buddies[2].Flags );
		// tp.exe 0x4BE730: no buddy white, offline red, online elsewhere blue, in this park green
		Assert.AreEqual( new Veldrid.RgbaByte( 255, 255, 255, 255 ), ChatPanelModel.NameColor( people[3].Flags ) );
		Assert.AreEqual( new Veldrid.RgbaByte( 254, 48, 48, 255 ), ChatPanelModel.NameColor( buddies[2].Flags ) );
		Assert.AreEqual( new Veldrid.RgbaByte( 81, 137, 239, 255 ), ChatPanelModel.NameColor( buddies[1].Flags ) );
		Assert.AreEqual( new Veldrid.RgbaByte( 0, 226, 6, 255 ), ChatPanelModel.NameColor( people[1].Flags ) );
		Assert.AreEqual( new Veldrid.RgbaByte( 255, 255, 255, 255 ), ChatPanelModel.NameColor( ChatPersonFlags.Ignored ), "the ignore flag leaves the colour" );
		// the hover help: buddy and where, ignored, and the right click
		Assert.AreEqual( 0, ChatPanelModel.PersonHelp( people[0] ).Count() );
		CollectionAssert.AreEqual( new[] { 404, 408, 406 }, ChatPanelModel.PersonHelp( people[1] ).ToArray() );
		CollectionAssert.AreEqual( new[] { 405, 406 }, ChatPanelModel.PersonHelp( people[2] ).ToArray() );
		CollectionAssert.AreEqual( new[] { 404, 409, 406 }, ChatPanelModel.PersonHelp( buddies[1] ).ToArray() );
		CollectionAssert.AreEqual( new[] { 404, 407, 406 }, ChatPanelModel.PersonHelp( buddies[2] ).ToArray() );
	}

	[TestMethod]
	public void TheOverlayAsksForTheBuddyListAtMostOnceInItsInterval()
	{
		Assert.IsTrue( OnlineSession.BuddyAskDue( null, 100, 5000 ), "the first ask goes out" );
		Assert.IsFalse( OnlineSession.BuddyAskDue( 100, 4_000, 5000 ), "an ask is out, or its answer came a moment ago" );
		Assert.IsTrue( OnlineSession.BuddyAskDue( 100, 5_100, 5000 ), "an ask without an answer in the interval is taken as dropped" );
	}

	[TestMethod]
	public void MessageRowsAreCutAtCharactersAndKeepTheLastTwenty()
	{
		static float Measure( string text ) => text.Length * 10;
		CollectionAssert.AreEqual( new[] { "Ann: hell", "o park" }, ChatPanelModel.MessageRowsOf( new[] { "Ann: hello park" }, Measure, 95 ).ToArray(),
			"cut where the characters reach the width, not at a word" );
		CollectionAssert.AreEqual( new[] { "" }, ChatPanelModel.MessageRowsOf( new[] { "" }, Measure, 95 ).ToArray() );
		CollectionAssert.AreEqual( new[] { "a", "b" }, ChatPanelModel.MessageRowsOf( new[] { "ab" }, Measure, 5 ).ToArray(), "a row holds one character at least" );
		// a surrogate pair is one character: no row ends between its halves, also where a row holds one character only
		CollectionAssert.AreEqual( new[] { "Hi ", "\U0001F600!" }, ChatPanelModel.MessageRowsOf( new[] { "Hi \U0001F600!" }, Measure, 45 ).ToArray() );
		CollectionAssert.AreEqual( new[] { "\U0001F600", "\U0001F600" }, ChatPanelModel.MessageRowsOf( new[] { "\U0001F600\U0001F600" }, Measure, 5 ).ToArray() );
		CollectionAssert.AreEqual( new[] { "a\uD83D", "b" }, ChatPanelModel.MessageRowsOf( new[] { "a\uD83Db" }, Measure, 25 ).ToArray(), "a lone high surrogate is a character of its own" );
		var lines = Enumerable.Range( 0, 25 ).Select( index => $"line {index}" ).ToArray();
		var rows = ChatPanelModel.MessageRowsOf( lines, Measure, 1000 );
		Assert.AreEqual( ChatPanelModel.MessageRows, rows.Count );
		Assert.AreEqual( "line 5", rows[0] );
		Assert.AreEqual( "line 24", rows[^1] );
		var wrapped = ChatPanelModel.MessageRowsOf( new[] { new string( 'x', 300 ) }, Measure, 100 );
		Assert.AreEqual( 20, wrapped.Count, "a long line keeps its last twenty rows" );
	}

	[TestMethod]
	public void ThePanelTakesItsPlacesFromTheOriginalTable()
	{
		var panel = new ChatPanel( OriginalUiTests.FakeStrings(), new UiScreenStack(), () => Room() );
		UiElement Element( string id ) => panel.Elements.Single( element => element.Id == id );
		Assert.IsTrue( panel.Elements.All( element => element.Anchor == UiAnchor.TopLeft ), "one anchor keeps the overlay's composition" );
		var frame = (UiModelImage)Element( "chatFrame" );
		Assert.AreEqual( "chat", frame.Model );
		Assert.IsTrue( frame.CoversShape, "the frame keeps clicks off the park only on its panels" );
		Assert.IsFalse( frame.CoversPoint( new UiCanvas( 2048, 1536 ), new NVector2( 100, 500 ) ), "before it is drawn the frame covers nothing" );
		Assert.AreEqual( new UiRect( 129, 28, 103, 102 ), Element( "chatPeopleTab" ).Bounds );
		Assert.AreEqual( "b_speeps", ((UiButton)Element( "chatPeopleTab" )).Model );
		Assert.AreEqual( new UiRect( 244, 28, 103, 102 ), Element( "chatBuddyTab" ).Bounds );
		Assert.AreEqual( "b_sbuddi", ((UiButton)Element( "chatBuddyTab" )).Model );
		Assert.AreEqual( new UiRect( 53, 162, 370, 1138 ), Element( "chatPeople" ).Bounds );
		Assert.AreEqual( ("b_chtpcard", new UiRect( 29, 1357, 154, 154 )), (((UiButton)Element( "chatPostcard" )).Model, Element( "chatPostcard" ).Bounds) );
		Assert.AreEqual( ("b_vote", new UiRect( 187, 1357, 154, 154 )), (((UiButton)Element( "chatVote" )).Model, Element( "chatVote" ).Bounds) );
		Assert.AreEqual( ("b_muff", new UiRect( 346, 1357, 154, 154 )), (((UiButton)Element( "chatEarmuffs" )).Model, Element( "chatEarmuffs" ).Bounds) );
		Assert.AreEqual( new UiRect( 562, 1175, 1322, 215 ), Element( "chatMessages" ).Bounds );
		Assert.AreEqual( new UiRect( 561, 1441, 1418, 45 ), Element( "chatEntry" ).Bounds );
		Assert.AreEqual( "help402", Element( "chatPeopleTab" ).CurrentHelp );
		Assert.AreEqual( "help461", Element( "chatVote" ).CurrentHelp );
		Assert.AreEqual( "help480", Element( "chatPostcard" ).CurrentHelp );
		Assert.IsNull( Element( "chatEntry" ).CurrentHelp, "the help box would cover the entry" );
		Assert.IsTrue( panel.Entry.OverView, "the entry takes the keys on a click, not when the pointer crosses it" );
		Assert.AreEqual( GameAudio.SelectClickEvent, ((UiButton)Element( "chatPeopleTab" )).ClickSound, "the tab group is a radio group (style bit 0x10)" );
		Assert.AreEqual( ChatPanel.Table.ClickSoundOf( ChatPanel.Table.Control( 0xC3129 ) ), ((UiButton)Element( "chatVote" )).ClickSound, "each button clicks by its parent's style" );
		Assert.IsNotNull( ((UiButton)Element( "chatPeopleUp" )).Repeat, "the list's arrows repeat while held" );
	}

	[TestMethod]
	public void TheButtonsSendTheirCommandsAndTheVoteGoesOnce()
	{
		var chat = Room();
		var postcards = 0;
		var panel = new ChatPanel( OriginalUiTests.FakeStrings(), new UiScreenStack(), () => chat ) { Postcard = () => postcards++ };
		UiButton Button( string id ) => (UiButton)panel.Elements.Single( element => element.Id == id );
		panel.Update();
		Assert.IsTrue( Button( "chatVote" ).Enabled && Button( "chatEarmuffs" ).Enabled && Button( "chatPostcard" ).Enabled );
		Button( "chatEarmuffs" ).Activate();
		Assert.AreEqual( (ChatCommand.Earmuffs, ""), chat.Commands.Single() );
		Assert.AreEqual( "help418", Button( "chatEarmuffs" ).CurrentHelp );
		chat.Earmuffs = true;
		Assert.IsTrue( Button( "chatEarmuffs" ).Selected!(), "the server's answer latches the toggle" );
		Assert.AreEqual( "help419", Button( "chatEarmuffs" ).CurrentHelp );
		Button( "chatVote" ).Activate();
		Assert.AreEqual( (ChatCommand.Vote, ""), chat.Commands[^1] );
		panel.Update();
		Assert.IsFalse( Button( "chatVote" ).Enabled, "the vote button disables itself" );
		Button( "chatPostcard" ).Activate();
		Assert.AreEqual( 1, postcards );
		// outside a published park there is no vote, and without the connection nothing works
		var satellite = new ChatPanel( OriginalUiTests.FakeStrings(), new UiScreenStack(), () => chat );
		chat.CanVote = false;
		satellite.Update();
		Assert.IsFalse( ((UiButton)satellite.Elements.Single( element => element.Id == "chatVote" )).Enabled );
		Assert.IsFalse( ((UiButton)satellite.Elements.Single( element => element.Id == "chatPostcard" )).Enabled, "no composer, no postcard button" );
		chat.ChatOpen = false;
		satellite.Update();
		Assert.IsFalse( satellite.Entry.Enabled || ((UiButton)satellite.Elements.Single( element => element.Id == "chatEarmuffs" )).Enabled );
		Assert.IsTrue( ((UiButton)satellite.Elements.Single( element => element.Id == "chatBuddyTab" )).Enabled, "the tabs work without the connection" );
		panel.Update();
		Assert.IsTrue( Button( "chatPostcard" ).Enabled, "a postcard needs no chat connection" );
		// the buddy tab lists the buddies and asks the server for them
		Assert.AreEqual( 4, panel.People.Count );
		panel.ShowTab( buddies: true );
		Assert.AreEqual( 1, chat.BuddyRequests );
		CollectionAssert.AreEqual( new[] { "Bob", "Eve", "Zed" }, panel.People.Select( person => person.Name ).ToArray() );
		Assert.IsTrue( Button( "chatBuddyTab" ).Selected!() && !Button( "chatPeopleTab" ).Selected!() );
	}

	[TestMethod]
	public void TheEntrySaysItsLineAndEmpties()
	{
		var chat = Room();
		var panel = new ChatPanel( OriginalUiTests.FakeStrings(), new UiScreenStack(), () => chat );
		panel.Entry.Text = "hello park";
		panel.Entry.Activate();
		CollectionAssert.AreEqual( new[] { "hello park" }, chat.Said );
		Assert.AreEqual( "", panel.Entry.Text );
		panel.Entry.Text = "   ";
		panel.Entry.Activate();
		Assert.AreEqual( 1, chat.Said.Count, "a blank line is not said" );
		Assert.AreEqual( 255, panel.Entry.MaximumLength, "the original's entry takes 255 characters (0x4BF460)" );
		Assert.IsTrue( panel.Entry.MaximumLength < ChatCommands.MaximumLineLength, "a full entry fits a chat line" );
	}

	[TestMethod]
	public void TheEntryTakesTheKeysFromAClickOrEnterAndLeavesThePointerToThePark()
	{
		var chat = Room();
		var stack = new UiScreenStack();
		var panel = new ChatPanel( OriginalUiTests.FakeStrings(), stack, () => chat );
		var screen = new ChatPanel.ChatScreen( "chatMode", panel ) { Modal = false };
		panel.AddTo( screen );
		stack.Push( screen );
		var context = Context();
		var vote = panel.Elements.Single( element => element.Id == "chatVote" ).ScreenRect( context.Canvas ).Center;
		var entry = panel.Entry.ScreenRect( context.Canvas ).Center;
		var park = new NVector2( 1500, 500 );
		// the arrows are the camera's keys and Tab only leaves the entry: neither moves the focus onto it
		stack.Update( context, UiInput.Key( UiKeys.Up ) );
		Assert.AreNotSame( panel.Entry, screen.Focused );
		screen.Focus( null );
		stack.Update( context, UiInput.Key( UiKeys.Tab ) );
		Assert.IsNull( screen.Focused );
		// the pointer crossing the vote button and resting on the entry leaves nothing focused: Enter there gives the entry the keys and does not vote
		stack.Update( context, UiInput.Idle( vote ) );
		Assert.AreEqual( "chatVote", screen.Focused?.Id );
		stack.Update( context, UiInput.Idle( entry ) );
		Assert.IsNull( screen.Focused );
		stack.Update( context, UiInput.Key( UiKeys.Accept ) with { Mouse = entry } );
		Assert.AreSame( panel.Entry, screen.Focused );
		Assert.AreEqual( 0, chat.Commands.Count, "no vote" );
		// while it has the keys, the entry leaves the pointer to the park outside the overlay's controls, and keeps it over them
		Input.TextEntryActive = false;
		Assert.IsFalse( stack.Update( context, UiInput.Idle( park ) ), "the park keeps the pointer" );
		Assert.IsTrue( Input.TextEntryActive, "the entry keeps the keys" );
		Assert.IsTrue( stack.Update( context, UiInput.Idle( vote ) ) );
		Assert.AreSame( panel.Entry, screen.Focused, "the pointer crossing a button does not take the keys from the entry" );
		stack.Update( context, UiInput.Type( "hi" ) with { Mouse = park } );
		stack.Update( context, UiInput.Key( UiKeys.Accept ) with { Mouse = park } );
		CollectionAssert.AreEqual( new[] { "hi" }, chat.Said );
	}

	[TestMethod]
	public void APopupsCommandQuotesANameWithASpace()
	{
		var chat = Room();
		chat.Players.Add( "Dan Smith" );
		var stack = new UiScreenStack();
		var panel = new ChatPanel( OriginalUiTests.FakeStrings(), stack, () => chat );
		var screen = new UiScreen( "chat" ) { Modal = false };
		panel.AddTo( screen );
		stack.Push( screen );
		var context = Context();
		panel.OpenPopup( new ChatPerson( "Dan Smith", ChatPersonFlags.None, false ), context.Canvas, new NVector2( 600, 400 ) );
		stack.Update( context, UiInput.Click( stack.Top!.Find( "popupMark" )!.ScreenRect( context.Canvas ).Center ) );
		Assert.AreEqual( (ChatCommand.Mark, "\"Dan Smith\""), chat.Commands.Single() );
		Assert.AreEqual( ("Dan Smith", ""), ChatCommands.SplitName( chat.Commands.Single().Argument ), "the server reads the whole name" );
		Assert.AreEqual( "Bob", ChatCommands.NameArgument( "Bob" ) );
	}

	[TestMethod]
	public void AGotoThePanelsHostTakesItselfSendsNoCommand()
	{
		// [EXT:online-coop-presence] a shared park's goto moves the view to a figure drawn there (C2d-4): no command goes out, so no "leave the park" notice; a name without one goes to the server
		var chat = Room();
		var stack = new UiScreenStack();
		var took = new List<string>();
		var went = new List<string>();
		var panel = new ChatPanel( OriginalUiTests.FakeStrings(), stack, () => chat )
		{
			GoTo = went.Add,
			TakeTo = name =>
			{
				took.Add( name );
				return name == "Bob";
			},
		};
		var screen = new UiScreen( "chat" ) { Modal = false };
		panel.AddTo( screen );
		stack.Push( screen );
		var context = Context();
		void Goto( string name )
		{
			panel.OpenPopup( new ChatPerson( name, ChatPersonFlags.Buddy, false ), context.Canvas, new NVector2( 600, 400 ) );
			stack.Update( context, UiInput.Click( stack.Top!.Find( "popupGoto" )!.ScreenRect( context.Canvas ).Center ) );
		}
		Goto( "Bob" );
		Assert.AreEqual( 0, chat.Commands.Count );
		Assert.AreEqual( 0, went.Count );
		Assert.AreSame( screen, stack.Top, "the popup closes" );
		Goto( "Eve" );
		Assert.AreEqual( (ChatCommand.Goto, "Eve"), chat.Commands.Single() );
		CollectionAssert.AreEqual( new[] { "Eve" }, went );
		CollectionAssert.AreEqual( new[] { "Bob", "Eve" }, took );
		// another button never asks the host
		panel.OpenPopup( new ChatPerson( "Bob", ChatPersonFlags.Buddy, false ), context.Canvas, new NVector2( 600, 400 ) );
		stack.Update( context, UiInput.Click( stack.Top!.Find( "popupLocate" )!.ScreenRect( context.Canvas ).Center ) );
		Assert.AreEqual( (ChatCommand.Locate, "Bob"), chat.Commands[^1] );
		Assert.AreEqual( 2, took.Count );
	}

	[TestMethod]
	public void ARightClickOnANameOpensItsPopupAtThePointer()
	{
		var chat = Room();
		var stack = new UiScreenStack();
		var went = new List<string>();
		var panel = new ChatPanel( OriginalUiTests.FakeStrings(), stack, () => chat ) { GoTo = went.Add };
		var screen = new UiScreen( "chat" ) { Modal = false };
		panel.AddTo( screen );
		stack.Push( screen );
		panel.Update();
		var context = Context();
		var people = (UiElement)panel.Elements.Single( element => element.Id == "chatPeople" );
		// rows are 40 units high until the list is drawn; the canvas maps one unit to one pixel
		NVector2 Row( int index ) => new( people.Bounds.X + 20, people.Bounds.Y + index * 40 + 5 );
		UiInput RightClick( NVector2 point ) => new( point, false, false, false, true, UiKeys.None );
		// the player's own name opens nothing
		stack.Update( context, RightClick( Row( 0 ) ) );
		Assert.AreSame( screen, stack.Top );
		// Bob, a buddy: the six-button popup, its corner at the pointer
		stack.Update( context, RightClick( Row( 1 ) ) );
		Assert.AreEqual( ChatPanel.PopupScreenName, stack.Top!.Name );
		var popup = stack.Top!;
		CollectionAssert.AreEquivalent( new[] { "popupFrame", "popupMark", "popupGoto", "popupIgnore", "popupBuddy", "popupLocate", "popupPage" },
			popup.Elements.Select( element => element.Id ).ToArray() );
		Assert.AreEqual( new UiRect( Row( 1 ).X, Row( 1 ).Y, 1170, 192 ), popup.Find( "popupFrame" )!.Bounds );
		Assert.AreEqual( "help413", popup.Find( "popupBuddy" )!.CurrentHelp, "the buddy popup's b_unbuddi removes the buddy" );
		Assert.AreEqual( "help410", popup.Find( "popupIgnore" )!.CurrentHelp );
		// a button sends its command for the name and closes the popup
		stack.Update( context, UiInput.Click( popup.Find( "popupGoto" )!.ScreenRect( context.Canvas ).Center ) );
		Assert.AreEqual( (ChatCommand.Goto, "Bob"), chat.Commands.Single() );
		CollectionAssert.AreEqual( new[] { "Bob" }, went, "the visit takes the figure to the player" );
		Assert.AreSame( screen, stack.Top );
		// Carla, ignored and no buddy: the four-button popup with the ignore button latched
		stack.Update( context, RightClick( Row( 2 ) ) );
		popup = stack.Top!;
		CollectionAssert.AreEquivalent( new[] { "popupFrame", "popupMark", "popupGoto", "popupIgnore", "popupBuddy" }, popup.Elements.Select( element => element.Id ).ToArray() );
		Assert.AreEqual( 825, popup.Find( "popupFrame" )!.Bounds.Width );
		Assert.IsTrue( ((UiButton)popup.Find( "popupIgnore" )!).Selected!() );
		Assert.AreEqual( "help411", popup.Find( "popupIgnore" )!.CurrentHelp );
		Assert.AreEqual( "help412", popup.Find( "popupBuddy" )!.CurrentHelp, "the player popup's b_unbuddi adds a buddy" );
		stack.Update( context, UiInput.Click( popup.Find( "popupIgnore" )!.ScreenRect( context.Canvas ).Center ) );
		Assert.AreEqual( (ChatCommand.Ignore, "Carla"), chat.Commands[^1] );
		// a click beside the popup closes it on its release, and neither its press nor its release reaches what lies under it
		stack.Update( context, RightClick( Row( 3 ) ) );
		Assert.AreEqual( ChatPanel.PopupScreenName, stack.Top!.Name );
		var earmuffs = panel.Elements.Single( element => element.Id == "chatEarmuffs" ).ScreenRect( context.Canvas ).Center;
		Assert.IsTrue( stack.Update( context, new UiInput( earmuffs, true, true, false, false, UiKeys.None ) ) );
		Assert.AreEqual( ChatPanel.PopupScreenName, stack.Top!.Name, "the popup waits for the release" );
		Assert.IsTrue( stack.Update( context, new UiInput( earmuffs, false, false, true, false, UiKeys.None ) ) );
		Assert.AreSame( screen, stack.Top );
		stack.Update( context, UiInput.Idle( earmuffs ) );
		Assert.AreEqual( 2, chat.Commands.Count, "the earmuffs under the click were not pressed" );
		// a popup near the canvas's edge stays inside it
		panel.OpenPopup( new ChatPerson( "Dave", ChatPersonFlags.Buddy, false ), context.Canvas, new NVector2( 2000, 1500 ) );
		var placed = stack.Top!.Find( "popupFrame" )!.Bounds;
		Assert.AreEqual( new UiRect( 2048 - 1170, 1536 - 192, 1170, 192 ), placed );
		stack.Update( context, UiInput.Key( UiKeys.Back ) );
		Assert.AreSame( screen, stack.Top, "Escape closes the popup" );
	}

	[TestMethod]
	public void AModalChatScreenWithItsEntryFocusedKeepsTheNamePopup()
	{
		// the chat park screen is modal and its entry has the keys; the right click on a name opens the popup and does not back out of the screen
		var rmbCancel = GameOptions.Current.RmbCancel;
		GameOptions.Current.RmbCancel = true;
		try
		{
			var chat = Room();
			var stack = new UiScreenStack();
			var panel = new ChatPanel( OriginalUiTests.FakeStrings(), stack, () => chat );
			var screen = new ChatPanel.ChatScreen( "chat", panel );
			panel.AddTo( screen );
			var backs = 0;
			screen.Back = () => backs++;
			stack.Push( screen );
			screen.Focus( panel.Entry );
			var context = Context();
			var people = panel.Elements.Single( element => element.Id == "chatPeople" );
			stack.Update( context, new UiInput( new NVector2( people.Bounds.X + 20, people.Bounds.Y + 45 ), false, false, false, true, UiKeys.None ) );
			Assert.AreEqual( ChatPanel.PopupScreenName, stack.Top!.Name );
			Assert.AreEqual( 0, backs, "a right click the list took is no back-out" );
		}
		finally { GameOptions.Current.RmbCancel = rmbCancel; }
	}

	[TestMethod]
	public void ANamesHoverHelpSaysWhoTheyAreAndWhatARightClickDoes()
	{
		var chat = Room();
		var stack = new UiScreenStack();
		var panel = new ChatPanel( OriginalUiTests.FakeStrings(), stack, () => chat );
		var screen = new ChatPanel.ChatScreen( "chat", panel ) { Modal = false };
		panel.AddTo( screen );
		stack.Push( screen );
		var context = Context();
		var people = panel.Elements.Single( element => element.Id == "chatPeople" );
		// Bob, a buddy in the park: buddy, here, right click
		stack.Update( context, UiInput.Idle( new NVector2( people.Bounds.X + 20, people.Bounds.Y + 45 ) ) );
		stack.Update( context, UiInput.Idle( new NVector2( people.Bounds.X + 21, people.Bounds.Y + 46 ) ) );
		Assert.AreEqual( "help404\nhelp408\nhelp406", context.HoverHelp );
		Assert.AreEqual( -1, ((IUiRowList)people).Selected, "the pointer selects nothing" );
		// while a line is typed no help covers the entry
		screen.Focus( panel.Entry );
		panel.Entry.Text = "hi";
		stack.Update( context, UiInput.Idle( new NVector2( people.Bounds.X + 20, people.Bounds.Y + 45 ) ) );
		Assert.IsNull( context.HoverHelp );
		// the pointer crossing the entry does not take the keys
		screen.Focus( null );
		stack.Update( context, UiInput.Idle( panel.Entry.ScreenRect( context.Canvas ).Center ) );
		Assert.IsNull( screen.Focused );
		stack.Update( context, UiInput.Click( panel.Entry.ScreenRect( context.Canvas ).Center ) );
		Assert.AreSame( panel.Entry, screen.Focused, "a click does" );
		// Tab leaves it
		stack.Update( context, UiInput.Key( UiKeys.Tab ) );
		Assert.IsNull( screen.Focused );
	}

	[TestMethod]
	public void ThePopupsButtonsAreTheOriginalsCommands()
	{
		// tp.exe 0x4BEDE0: black-mark, goto, ignore, buddy (add or remove), locate, page
		var models = OriginalLayoutTables.ChatBuddyPopup.All.Where( node => node.Type == 2 ).Select( node => node.Model ).ToArray();
		CollectionAssert.AreEquivalent( new[] { ChatCommand.Mark, ChatCommand.Goto, ChatCommand.Ignore, ChatCommand.Buddy, ChatCommand.Locate, ChatCommand.Page },
			models.Select( ChatPanel.PopupAction ).ToArray() );
		var player = OriginalLayoutTables.ChatPlayerPopup.All.Where( node => node.Type == 2 ).Select( node => node.Model ).ToArray();
		CollectionAssert.AreEquivalent( new[] { ChatCommand.Mark, ChatCommand.Goto, ChatCommand.Ignore, ChatCommand.Buddy }, player.Select( ChatPanel.PopupAction ).ToArray() );
		Assert.IsTrue( OriginalLayoutTables.ChatBuddyPopup.Model( "b_ignore" ).IsToggle, "the ignore button is a toggle" );
	}
}
