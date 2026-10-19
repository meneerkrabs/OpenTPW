using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Packages;
using OpenTPW.UI;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>Send Postcard in tp.exe's postcard window, table 0x7540d8 (built by 0x4a8c00, handled by 0x4a7d70).</summary>
[TestClass, DoNotParallelize]
public class PostcardWindowTests
{
	private static void WithScreens( Action<OnlineScreens, OnlineSession, UiScreenStack> test, Func<Level?>? level = null )
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-postcard-window-" ).FullName;
		var previousFiles = OpenTPW.Common.GlobalNamespace.FileSystem;
		try
		{
			OpenTPW.Common.GlobalNamespace.FileSystem = new BaseFileSystem( root );
			var folders = new OnlineFolders( root );
			folders.SaveSettings( new OnlineSettings( null, "sender" ) );
			using var session = new OnlineSession( folders );
			var stack = new UiScreenStack();
			stack.Push( new UiScreen( "hud" ) );
			var screens = new OnlineScreens( stack, OriginalUiTests.FakeStrings(), new UiModels( name => throw new FileNotFoundException( name ) ), new OnlineHost { Session = session, Level = level ?? (() => null) } );
			test( screens, session, stack );
		}
		finally { OpenTPW.Common.GlobalNamespace.FileSystem = previousFiles; Directory.Delete( root, recursive: true ); }
	}

	[TestMethod]
	public void TheWindowFollowsTheTable() => WithScreens( ( screens, _, stack ) =>
	{
		screens.ShowSendPostcard();
		var screen = stack.Top!;
		var table = OriginalLayoutTables.PostcardWindow;
		Assert.AreEqual( "window4", ((UiModelImage)screen.Find( "window" )!).Model );
		Assert.AreEqual( table.Control( 0x5D9F ).Rect, screen.Find( "photo" )!.Bounds );
		Assert.AreEqual( table.Control( 0x5DA3 ).Rect, screen.Find( "message" )!.Bounds );
		Assert.AreEqual( table.Control( 0x5DA4 ).Rect, screen.Find( "title" )!.Bounds );
		Assert.AreEqual( table.Control( 0x5DA5 ).Rect, screen.Find( "to" )!.Bounds );
		Assert.AreEqual( 0xFF, ((UiTextField)screen.Find( "message" )!).MaximumLength );
		Assert.AreEqual( Postcard.MaximumTitleLength, ((UiTextField)screen.Find( "title" )!).MaximumLength, "the online card's 64 of the original's 95" );
		Assert.AreEqual( 0x1FF, ((UiTextField)screen.Find( "to" )!).MaximumLength );
		Assert.AreEqual( table.Model( "b_okay" ).Rect, screen.Find( "ok" )!.Bounds );
		Assert.AreEqual( table.Model( "b_exit" ).Rect, screen.Find( "back" )!.Bounds );
		Assert.IsFalse( screen.Find( "preview" )!.Enabled );
		Assert.IsFalse( screen.Find( "addressBook" )!.Enabled );
	} );

	[TestMethod]
	public void TheFieldsStartWithTheirPromptsAndTheUneditedTextsTakeTheDefaults() => WithScreens( ( screens, session, stack ) =>
	{
		screens.ShowSendPostcard();
		var screen = stack.Top!;
		var to = (UiTextField)screen.Find( "to" )!;
		Assert.AreEqual( OnlineStrings.Ui( OnlineStrings.EmailAddresses, "Email address(es)" ), to.Placeholder );
		Assert.AreEqual( OnlineStrings.Ui( OnlineStrings.MessageText, "Message text" ), ((UiTextField)screen.Find( "message" )!).Placeholder );
		Assert.AreEqual( "", to.Text, "typing does not append to the prompt" );
		to.Type( "alice; Bob Smith", 0 );
		((UiButton)screen.Find( "ok" )!).Clicked!();
		Assert.AreEqual( "hud", stack.Top!.Name, "the tick closes the window" );
		var card = session.Folders.List( session.Folders.Outbox ).Single().Card!;
		CollectionAssert.AreEqual( new[] { "alice", "Bob Smith" }, card.Manifest.To.ToArray() );
		Assert.AreEqual( OnlineStrings.Ui( OnlineStrings.PostcardTitle, "Postcard from Theme Park World" ), card.Manifest.Title );
		Assert.AreEqual( OnlineStrings.Ui( OnlineStrings.PostcardGreeting, "Greetings from Theme Park World!" ), card.Manifest.Text );
	} );

	[TestMethod]
	public void NoRecipientKeepsTheWindowAndShowsTheRefusal() => WithScreens( ( screens, session, stack ) =>
	{
		screens.ShowSendPostcard();
		var screen = stack.Top!;
		((UiButton)screen.Find( "ok" )!).Clicked!();
		Assert.AreEqual( "postcardRecipients", stack.Top!.Name, "an empty recipients field is no recipient" );
		stack.Pop();
		var to = (UiTextField)screen.Find( "to" )!;
		to.Type( ";, \n", 0 );
		((UiButton)screen.Find( "ok" )!).Clicked!();
		Assert.AreEqual( "postcardRecipients", stack.Top!.Name, "separators alone are no recipient" );
		stack.Pop();
		((UiButton)screen.Find( "clear" )!).Clicked!();
		Assert.AreEqual( "", to.Text );
		Assert.AreEqual( 0, session.Folders.List( session.Folders.Outbox ).Count );
	} );

	[TestMethod]
	public void RecipientsSplitAtTheOriginalSeparators() =>
		CollectionAssert.AreEqual( new[] { "a", "b", "c d", "e" }, OnlineScreens.PostcardRecipients( "a;b, c d\ne ;" ) );

	[TestMethod]
	public void MixedRecipientsSaveOfflineAndInvalidAddressesUseTheOriginalLocalizedRefusal() => WithScreens( ( screens, session, stack ) =>
	{
		screens.ShowSendPostcard();
		var screen = stack.Top!;
		var to = (UiTextField)screen.Find( "to" )!;
		to.Text = "Bob Smith; invalid@";
		((UiTextField)screen.Find( "message" )!).Text = "Keep my message";
		((UiButton)screen.Find( "ok" )!).Clicked!();
		Assert.AreEqual( "postcardRecipients", stack.Top!.Name );
		Assert.AreEqual( OnlineStrings.Ui( OnlineStrings.InvalidEmailAddresses, "Invalid email addresses in the send to field" ), ((UiLabel)stack.Top.Find( "message" )!).Text() );
		Assert.AreEqual( 0, session.Folders.List( session.Folders.Outbox ).Count );
		stack.Pop();
		Assert.AreSame( screen, stack.Top );
		Assert.AreEqual( "Keep my message", ((UiTextField)screen.Find( "message" )!).Text );
		to.Text = "Bob Smith; card@example.test";
		((UiButton)screen.Find( "ok" )!).Clicked!();
		Assert.AreEqual( "hud", stack.Top!.Name );
		CollectionAssert.AreEqual( new[] { "Bob Smith", "card@example.test" }, session.Folders.List( session.Folders.Outbox ).Single().Card!.Manifest.To.ToArray() );
	} );

	[TestMethod]
	public void UnsupportedPlatformOperationPreservesTheDraftAndAllowsClosing() => WithScreens( ( screens, session, stack ) =>
	{
		screens.ShowSendPostcard();
		var screen = stack.Top!;
		((UiTextField)screen.Find( "to" )!).Text = "Bob";
		((UiTextField)screen.Find( "title" )!).Text = "Keep this title";
		((UiTextField)screen.Find( "message" )!).Text = "Keep this message";
		((UiButton)screen.Find( "ok" )!).Clicked!();
		Assert.AreEqual( "postcardFailed", stack.Top!.Name );
		Assert.AreEqual( 0, session.Folders.List( session.Folders.Outbox ).Count );
		stack.Pop();
		Assert.AreSame( screen, stack.Top );
		Assert.AreEqual( "Bob", ((UiTextField)screen.Find( "to" )!).Text );
		Assert.AreEqual( "Keep this title", ((UiTextField)screen.Find( "title" )!).Text );
		Assert.AreEqual( "Keep this message", ((UiTextField)screen.Find( "message" )!).Text );
		((UiButton)screen.Find( "back" )!).Clicked!();
		Assert.AreEqual( "hud", stack.Top!.Name );
	}, () => throw new PlatformNotSupportedException( "A postcard operation is unavailable." ) );
}
