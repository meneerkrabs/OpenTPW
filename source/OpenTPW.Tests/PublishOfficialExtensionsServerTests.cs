using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.Mods;
using OpenTPW.Online;
using OpenTPW.Online.Packages;
using OpenTPW.UI;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The in-process server and online session side of <see cref="PublishOfficialExtensionsTests"/> (a separate file: it needs the server, which the browser build leaves out).
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
[TestClass]
[DoNotParallelize]
public class PublishOfficialExtensionsServerTests
{
	private string root = null!;

	[TestInitialize]
	public void Initialize() => root = Directory.CreateTempSubdirectory( "opentpw-publish-extensions-server-" ).FullName;

	[TestCleanup]
	public void Cleanup() => Directory.Delete( root, true );

	private static ExtensionUse Use( IOpenTpwExtension extension ) => new( extension.Id, extension.Version.ToString() );

	private static ParkPackage Package( params ExtensionUse[] extensions ) => PublishOfficialExtensionsTests.Package( extensions );

	[TestMethod]
	public async Task TheServerAcceptsAndServesAPackageWithExtensionsAndRefusesABadOne()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			using var owner = await server.LoginAsync( "Owner" );
			var published = await owner.UploadParkAsync( Package( Use( OfficialExtensions.Winter ) ) );
			var downloaded = await owner.DownloadParkAsync( published.Id );
			Assert.AreEqual( 3, downloaded.Manifest.FormatVersion );
			CollectionAssert.AreEqual( new[] { Use( OfficialExtensions.Winter ) }, downloaded.Manifest.Extensions!.ToArray() );
			// the plain version 2 package of an older client publishes as before
			Assert.AreEqual( 2, (await owner.DownloadParkAsync( (await owner.UploadParkAsync( SharedParkTests.LayoutPackage() )).Id )).Manifest.FormatVersion );
			// extensions in a version 2 manifest are refused by the server with a 400 (no client writes one: the bytes are posted as they are)
			var good = Package( Use( OfficialExtensions.Winter ) );
			async Task<System.Net.HttpStatusCode> Post( ParkManifest manifest )
			{
				var zip = OnlineFormatTests.Zip( (ParkPackage.ManifestEntry, StrictJson.Serialize( manifest )), (ParkPackage.PayloadEntry, good.Payload) );
				using var http = new System.Net.Http.HttpClient();
				using var request = new System.Net.Http.HttpRequestMessage( System.Net.Http.HttpMethod.Post, new Uri( server.Url, "api/v1/parks" ) ) { Content = new System.Net.Http.ByteArrayContent( zip ) };
				request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue( "Bearer", owner.Session!.Token );
				request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue( "application/octet-stream" );
				return (await http.SendAsync( request )).StatusCode;
			}
			Assert.AreEqual( System.Net.HttpStatusCode.BadRequest, await Post( good.Manifest with { FormatVersion = 2 } ) );
			Assert.AreEqual( System.Net.HttpStatusCode.BadRequest, await Post( good.Manifest with { Extensions = new[] { new ExtensionUse( "community.tpi-rides", "1.0.0" ) } } ) );
			Assert.AreEqual( System.Net.HttpStatusCode.Created, await Post( good.Manifest ), "the same bytes with a valid manifest are accepted" );
		}
		finally
		{
			server.DeleteData();
		}
	}


	[TestMethod]
	public async Task ThePostcardLineShowsWhileLoggedOutAndSaysWhereToSendWhenLoggedIn()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			var folders = OnlineIntegrationTests.Folders();
			folders.Store( folders.Outbox, Postcard.Create( "Alice", new[] { "Bob" }, "Hello", "Text", "English" ) );
			using var session = new OnlineSession( folders );
			var stack = new UiScreenStack();
			stack.Push( new UiScreen( "menu" ) );
			var screens = new OnlineScreens( stack, OriginalUiTests.FakeStrings(), new UiModels( name => throw new FileNotFoundException( name ) ), new OnlineHost { Session = session } );
			screens.ShowMenu();
			var label = (UiLabel)stack.Top!.Find( "waiting" )!;
			var reminder = OnlineStrings.Ui( OnlineStrings.OutboxWaiting, "You have postcards in your outbox, waiting to be sent." );
			Assert.AreEqual( reminder, label.Text!(), "logged out: the original's reminder (UITEXT 472)" );
			session.UseServer( server.Url.ToString(), "Alice" );
			session.Register( "Alice", "correct horse" );
			await OnlineIntegrationTests.PumpUntilIdle( session );
			Assert.IsTrue( session.IsLoggedIn, session.Status );
			Assert.AreEqual( OnlineStrings.Get( OnlineLabel.OutboxReadyToSend ), label.Text!(), "logged in the reminder to go online makes no sense: the line says where to send them" );
			StringAssert.Contains( label.Text!(), "Unsent postcards" );
			// no postcard waiting: no line
			foreach ( var entry in folders.List( folders.Outbox ) )
				File.Delete( entry.Path );
			Assert.AreEqual( "", label.Text!() );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AStatusShowsOnceOnTheTopScreenAndOnlyWhileItIsRelevant()
	{
		using var session = new OnlineSession( OnlineIntegrationTests.Folders() );
		var stack = new UiScreenStack();
		stack.Push( new UiScreen( "hud" ) );
		var screens = new OnlineScreens( stack, OriginalUiTests.FakeStrings(), new UiModels( name => throw new FileNotFoundException( name ) ), new OnlineHost { Session = session } );
		screens.ShowWorld();
		var world = stack.Top!;
		int Showing( string text ) => stack.Screens.SelectMany( screen => screen.Elements.OfType<UiLabel>() ).Count( label => label.Text?.Invoke() == text );
		async Task Report( string text )
		{
			session.Run( () => Task.FromResult<string?>( text ) );
			await OnlineIntegrationTests.PumpUntilIdle( session );
		}
		await Report( "No search results" );
		Assert.AreEqual( 1, Showing( "No search results" ), "reported on the world screen: it shows there" );
		screens.ShowMenu();
		Assert.AreEqual( 0, Showing( "No search results" ), "the menu opened afterwards searched nothing: neither it nor the screen under it shows the old message" );
		await Report( "Error: something" );
		Assert.AreEqual( 1, Showing( "Error: something" ), "a new message shows once, on the menu on top (not also over the globe)" );
		Assert.AreSame( stack.Top, stack.Screens.Single( screen => screen.Elements.OfType<UiLabel>().Any( label => label.Text?.Invoke() == "Error: something" ) ) );
		stack.Pop();
		Assert.AreSame( world, stack.Top );
		Assert.AreEqual( 0, Showing( "Error: something" ), "back on the world screen, what the menu reported is not repeated" );
		await Report( "Fresh" );
		Assert.AreEqual( 1, Showing( "Fresh" ) );
	}

	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 2048, 1536 ) };

	private static string StatusOf( UiScreen screen ) => ((UiLabel)screen.Find( "status" )!).Text!();

	[TestMethod]
	public async Task ALoginThatClosesItselfLeavesItsOutcomeOnTheScreenBelow()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			using var session = new OnlineSession( OnlineIntegrationTests.Folders() );
			var stack = new UiScreenStack();
			stack.Push( new UiScreen( "hud" ) );
			var screens = new OnlineScreens( stack, OriginalUiTests.FakeStrings(), new UiModels( name => throw new FileNotFoundException( name ) ), new OnlineHost { Session = session } );
			screens.ShowMenu();
			var menu = stack.Top!;
			screens.ShowLogin();
			var login = stack.Top!;
			((UiTextField)login.Find( "server" )!).Text = server.Url.ToString();
			((UiTextField)login.Find( "name" )!).Text = "Alice";
			((UiTextField)login.Find( "password" )!).Text = "correct horse";
			((UiButton)login.Find( "register" )!).Clicked!();
			await OnlineIntegrationTests.PumpUntilIdle( session );
			Assert.IsTrue( session.IsLoggedIn, session.Status );
			login.Updating!( Context() );
			Assert.AreSame( menu, stack.Top, "the login closed itself" );
			Assert.IsTrue( session.Status.Length > 0 );
			Assert.AreEqual( session.Status, StatusOf( menu ), "what the login reported shows on the menu under it" );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task ADeletedAccountsOutcomeShowsOnTheMenuUnderTheClosedDialog()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			var session = new OnlineSession( OnlineIntegrationTests.Folders() );
			session.UseServer( server.Url.ToString(), "Alice" );
			session.Register( "Alice", "correct horse" );
			await OnlineIntegrationTests.PumpUntilIdle( session );
			Assert.IsTrue( session.IsLoggedIn, session.Status );
			var stack = new UiScreenStack();
			stack.Push( new UiScreen( "hud" ) );
			var screens = new OnlineScreens( stack, OriginalUiTests.FakeStrings(), new UiModels( name => throw new FileNotFoundException( name ) ), new OnlineHost { Session = session } );
			screens.ShowMenu();
			var menu = stack.Top!;
			screens.ShowDeleteAccount();
			var dialog = stack.Top!;
			((UiTextField)dialog.Find( "name" )!).Text = "Alice";
			((UiTextField)dialog.Find( "password" )!).Text = "correct horse";
			((UiButton)dialog.Find( "delete" )!).Clicked!();
			await OnlineIntegrationTests.PumpUntilIdle( session );
			dialog.Updating!( Context() );
			Assert.AreSame( menu, stack.Top, "the dialog closed itself" );
			Assert.AreEqual( OnlineStrings.Get( OnlineLabel.AccountDeleted ), StatusOf( menu ), "Your account has been deleted. stays on the menu" );
			session.Dispose();
		}
		finally
		{
			server.DeleteData();
		}
	}
}
