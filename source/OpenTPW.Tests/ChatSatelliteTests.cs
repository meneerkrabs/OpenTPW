using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Online.Chat;
using OpenTPW.Online.Client;
using OpenTPW.Online.Packages;
using OpenTPW.Server;
using OpenTPW.UI;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The globe's chat satellites as parks (docs/ONLINE.md "Chat parks (the satellites)"): the server's chat parks name a published park or an original
/// level, a satellite's click opens its chat park's details as tp.exe's satellite opened a park's (0x4C56E0 → 0x4C0AE0), and Enter visits the park in
/// the satellite's room.
/// </summary>
[TestClass, DoNotParallelize]
public class ChatSatelliteTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "fantasy", "space" };

	private static T WithDirectory<T>( string? json, Func<ChatParkDirectory, T> test )
	{
		var directory = Directory.CreateTempSubdirectory( "opentpw-chatparks-" ).FullName;
		try
		{
			if ( json != null )
				File.WriteAllText( Path.Combine( directory, "chatparks.json" ), json );
			return test( new ChatParkDirectory( directory ) );
		}
		finally { Directory.Delete( directory, recursive: true ); }
	}

	[TestMethod]
	public void EveryDefaultSatelliteHasALevelToBuildAndADescription()
	{
		var parks = WithDirectory( null, chatParks => chatParks.Read() );
		CollectionAssert.AreEqual( new[] { 1, 2, 3, 4, 5 }, parks.Select( park => park.Id ).ToArray() );
		foreach ( var park in parks )
		{
			CollectionAssert.Contains( Themes, park.Level, park.Name );
			Assert.IsFalse( string.IsNullOrWhiteSpace( park.Description ), park.Name );
			OnlineText.RequireText( park.Description, ParkPackage.MaximumDescriptionLength, true, false, park.Name );
			OnlineText.RequireText( park.Name, ChatParkDirectory.MaximumNameLength, false, false, park.Name );
			Assert.IsNull( park.ParkId, "no default satellite names a published park" );
		}
		CollectionAssert.IsSubsetOf( Themes, parks.Select( park => park.Level ).ToArray(), "each theme has a satellite" );
	}

	[TestMethod]
	public void TheOperatorNamesLevelsParksAndDescriptionsAndMalformedFieldsAreLeftOut()
	{
		var json = """
		[
			{ "id": 1, "name": "Old Format" },
			{ "id": 2, "name": "Space Talk", "level": "space", "parkId": "abc123", "description": "Meet here.\nLine two." },
			{ "id": 3, "name": "Bad Fields", "level": "../levels/jungle", "parkId": "a/b", "description": "bell\u0007" },
			{ "id": 4, "name": "bell\u0007 name" },
			{ "id": 5, "name": "  aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa😀 ", "level": "atlantis" },
			{ "id": 2, "name": "Duplicate" },
			null
		]
		""";
		var parks = WithDirectory( json, chatParks => chatParks.Read() );
		CollectionAssert.AreEqual( new[] { 1, 2, 3, 5 }, parks.Select( park => park.Id ).ToArray(), "a name with a control character leaves its entry out, the first of two entries counts" );
		Assert.AreEqual( new ChatParkDirectory.ChatParkDefinition( 1, "Old Format", "jungle" ), parks[0], "an entry without a level keeps its satellite's default level" );
		Assert.AreEqual( new ChatParkDirectory.ChatParkDefinition( 2, "Space Talk", "space", "abc123", "Meet here.\nLine two." ), parks[1] );
		Assert.AreEqual( new ChatParkDirectory.ChatParkDefinition( 3, "Bad Fields", "hallow" ), parks[2], "a malformed level, park id or description is left out" );
		Assert.AreEqual( new ChatParkDirectory.ChatParkDefinition( 5, new string( 'a', 31 ), "space" ), parks[3],
			"a long name is cut at 32 characters without splitting a surrogate pair, and a level that is no theme's is the default" );
	}

	[TestMethod]
	public void AScenesParkIsThePublishedParkWhileItIsPublishedAndOtherwiseTheLevelsStartingPark()
	{
		var json = """
		[
			{ "id": 1, "name": "Published", "level": "space", "parkId": "abc123" },
			{ "id": 2, "name": "Described", "parkId": "abc123", "description": "Own words" },
			{ "id": 3, "name": "Gone", "level": "fantasy", "parkId": "gone1" }
		]
		""";
		var summary = new ParkSummary( "abc123", "Ann's Park", "Ann's words", "Ann", "hallow", "English", DateTimeOffset.UnixEpoch, 1, 2, false, false, 0, false, 100 );
		var scenes = WithDirectory( json, chatParks => chatParks.Scenes( id => id == "abc123" ? summary : null ) );
		Assert.AreEqual( new ChatParkScene( 1, "Published", "Ann's words", "hallow", summary ), scenes[0], "the park's own theme and description" );
		Assert.AreEqual( new ChatParkScene( 2, "Described", "Own words", "hallow", summary ), scenes[1], "the operator's description first" );
		Assert.AreEqual( new ChatParkScene( 3, "Gone", "", "fantasy", null ), scenes[2], "a park no longer published leaves the level's starting park" );
	}

	[TestMethod]
	public async Task TheScenesNeedALoginAndNameThePublishedParkWhileItIsPublished()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			using var anonymous = new OnlineClient( server.Url );
			var refused = await Assert.ThrowsExceptionAsync<OnlineException>( () => anonymous.GetChatParkScenesAsync() );
			Assert.AreEqual( HttpStatusCode.Unauthorized, refused.Status );
			using var owner = await server.LoginAsync( "Owner" );
			CollectionAssert.AreEqual( ChatParkDirectory.Defaults.Select( park => new ChatParkScene( park.Id, park.Name, park.Description!, park.Level, null ) ).ToArray(),
				(await owner.GetChatParkScenesAsync())!.Scenes.ToArray() );
			var park = await owner.UploadParkAsync( OnlineFormatTests.Package() );
			File.WriteAllText( Path.Combine( server.Directory, "chatparks.json" ), $$"""[ { "id": 2, "name": "Owner's Corner", "parkId": "{{park.Id}}" } ]""" );
			var scene = (await owner.GetChatParkScenesAsync())!.Scenes.Single();
			Assert.AreEqual( (2, "Owner's Corner", park.Description, park.Level, park.Id), (scene.Id, scene.Name, scene.Description, scene.Level, scene.Park?.Id) );
			// a player in the satellite's room counts at the satellite, not at the park
			await using var chat = await owner.ConnectChatAsync();
			await chat.JoinAsync( ChatProtocol.SatelliteRoom( 2 ) );
			ChatParkInfo? counted = null;
			for ( var attempt = 0; attempt < 250 && counted?.Players != 1; attempt++ )
			{
				await Task.Delay( 20 );
				counted = (await owner.GetChatParksAsync())!.ChatParks.Single();
			}
			Assert.AreEqual( (2, 1), (counted!.Id, counted.Players) );
			Assert.AreEqual( 0, (await owner.GetChatParkScenesAsync())!.Scenes.Single().Park!.PlayersAtPark );
			await owner.UnpublishParkAsync( park.Id );
			var fallback = (await owner.GetChatParkScenesAsync())!.Scenes.Single();
			Assert.AreEqual( ("fantasy", (ParkSummary?)null), (fallback.Level, fallback.Park), "an unpublished park leaves the satellite's default level" );
		}
		finally
		{
			server.DeleteData();
		}
	}

	/// <summary>
	/// A logged-in session on <paramref name="server"/> with the online screens over a menu, whose visits <paramref name="visit"/> and level visits
	/// <paramref name="visitLevel"/> receive.
	/// </summary>
	private static async Task<(OnlineSession Session, OnlineScreens Screens, UiScreenStack Stack)> Screens( LoopbackServer server, string name,
		Action<ParkVisitInfo>? visit = null, Action<string, Func<ParkVisitInfo>>? visitLevel = null )
	{
		var session = new OnlineSession( OnlineIntegrationTests.Folders() );
		session.UseServer( server.Url.ToString(), name );
		session.Register( name, "correct horse" );
		await OnlineIntegrationTests.PumpUntilIdle( session );
		Assert.IsTrue( session.IsLoggedIn, session.Status );
		var (screens, stack) = Screens( session, visit, visitLevel );
		return (session, screens, stack);
	}

	private static (OnlineScreens Screens, UiScreenStack Stack) Screens( OnlineSession session, Action<ParkVisitInfo>? visit = null, Action<string, Func<ParkVisitInfo>>? visitLevel = null )
	{
		var stack = new UiScreenStack();
		stack.Push( new UiScreen( "menu" ) );
		var screens = new OnlineScreens( stack, OriginalUiTests.FakeStrings(), new UiModels( model => throw new FileNotFoundException( model ) ),
			new OnlineHost { Session = session, Visit = visit, VisitLevel = visitLevel } );
		return (screens, stack);
	}

	/// <summary>Runs <paramref name="test"/> with an empty game file system (the online strings fall back to their English texts).</summary>
	private static async Task WithoutGameFiles( Func<Task> test )
	{
		var previousFiles = OpenTPW.Common.GlobalNamespace.FileSystem;
		var root = Directory.CreateTempSubdirectory( "opentpw-chat-satellite-" ).FullName;
		try
		{
			OpenTPW.Common.GlobalNamespace.FileSystem = new BaseFileSystem( root );
			await test();
		}
		finally
		{
			OpenTPW.Common.GlobalNamespace.FileSystem = previousFiles;
			Directory.Delete( root, recursive: true );
		}
	}

	/// <summary>Clicks satellite <paramref name="number"/> on a fresh Online World and waits for its answer.</summary>
	private static async Task ClickSatellite( OnlineSession session, OnlineScreens screens, UiScreenStack stack, int number )
	{
		screens.ShowWorld();
		Assert.AreEqual( "onlineWorld", stack.Top!.Name );
		((UiButton)stack.Top.Find( $"satellite{number}" )!).Clicked!();
		await OnlineIntegrationTests.PumpUntilIdle( session );
	}

	[TestMethod]
	public async Task ASatelliteShowsItsChatParksDetailsAndWithoutAVisitEntersItsRoomFromAbove()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			await WithoutGameFiles( async () =>
			{
				var (session, screens, stack) = await Screens( server, "Ann" );
				await ClickSatellite( session, screens, stack, 5 );
				var details = stack.Top!;
				Assert.AreEqual( OnlineScreens.ChatParkDetailsScreenName, details.Name, session.Status );
				var space = ChatParkDirectory.Defaults[4];
				Assert.AreEqual( space.Name, ((UiLabel)details.Find( "parkName" )!).Text() );
				Assert.AreEqual( OnlineScreens.ChatParkThemeRow( "space" ), ((UiLabel)details.Find( "theme" )!).Text() );
				StringAssert.EndsWith( OnlineScreens.ChatParkThemeRow( "space" ), " : " + OnlineStrings.Theme( "space" ) );
				Assert.AreEqual( space.Description, ((UiLabel)details.Find( "description" )!).Text() );
				Assert.IsFalse( ((UiButton)details.Find( "unpublish" )!).Enabled, "the owner's action is disabled" );
				Assert.IsNull( details.Find( "row233Value" ), "no statistics rows" );
				details.Updating!( new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels( model => throw new FileNotFoundException( model ) ) ) { Canvas = new UiCanvas( 2048, 1536 ) } );
				Assert.IsTrue( ((UiButton)details.Find( "visit" )!).Enabled );
				// without the game flow there is no visit: Enter shows the satellite's room from above
				((UiButton)details.Find( "visit" )!).Clicked!();
				Assert.AreEqual( OnlineScreens.ChatScreenName, stack.Top!.Name );
				Assert.AreEqual( ChatProtocol.SatelliteRoom( 5 ), session.WantedRoom );
				session.Dispose();
			} );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AnOlderServerWithoutScenesEntersTheSatellitesRoomFromAbove() => await WithoutGameFiles( async () =>
	{
		// a server before the scenes route answers 404, as for any unknown route
		using var session = new OnlineSession( OnlineIntegrationTests.Folders() );
		session.UseClient( new OnlineClient( new Uri( "http://127.0.0.1:1/" ), new HttpClient( new NotFound() ) ), "Ann" );
		var (screens, stack) = Screens( session );
		await ClickSatellite( session, screens, stack, 3 );
		Assert.AreEqual( OnlineScreens.ChatScreenName, stack.Top!.Name, session.Status );
		Assert.AreEqual( ChatProtocol.SatelliteRoom( 3 ), session.WantedRoom );
	} );

	/// <summary>Every request is answered 404 (Not Found).</summary>
	private sealed class NotFound : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, System.Threading.CancellationToken cancellationToken ) =>
			Task.FromResult( new HttpResponseMessage( HttpStatusCode.NotFound ) { RequestMessage = request } );
	}

	[TestMethod]
	public async Task AnAnswerAfterTheGlobeClosedOpensNothingAndASatelliteWithoutAChatParkEntersNoRoom()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			await WithoutGameFiles( async () =>
			{
				File.WriteAllText( Path.Combine( server.Directory, "chatparks.json" ), """[ { "id": 2, "name": "Two" } ]""" );
				var (session, screens, stack) = await Screens( server, "Ann" );
				// the player left the globe before the scenes came
				screens.ShowWorld();
				((UiButton)stack.Top!.Find( "satellite2" )!).Clicked!();
				stack.Pop();
				await OnlineIntegrationTests.PumpUntilIdle( session );
				Assert.AreEqual( "menu", stack.Top!.Name );
				Assert.AreEqual( ChatProtocol.LobbyRoom, session.WantedRoom );
				// each count stands on its own satellite, and a satellite the server keeps no chat park for enters nothing
				session.FetchChatParks();
				await OnlineIntegrationTests.PumpUntilIdle( session );
				await ClickSatellite( session, screens, stack, 1 );
				var world = stack.Top!;
				Assert.AreEqual( "onlineWorld", world.Name );
				Assert.AreEqual( ("", "0"), (((UiLabel)world.Find( "satellite1Count" )!).Text(), ((UiLabel)world.Find( "satellite2Count" )!).Text()) );
				Assert.AreEqual( ChatProtocol.LobbyRoom, session.WantedRoom );
				Assert.AreEqual( OnlineStrings.ChatNotice( (int)ChatNotice.NoPark, null ), ((UiLabel)world.Find( "status" )!).Text() );
				session.Dispose();
			} );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task EnterVisitsTheSatellitesParkInTheSatellitesRoom()
	{
		var data = OriginalParkImportTests.OriginalDataPath();
		await using var server = await LoopbackServer.StartAsync();
		var previousFiles = OpenTPW.Common.GlobalNamespace.FileSystem;
		var fileSystem = new BaseFileSystem( data );
		fileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			OpenTPW.Common.GlobalNamespace.FileSystem = fileSystem;
			using var owner = await server.LoginAsync( "Owner" );
			var park = await owner.UploadParkAsync( OnlineFormatTests.Package() );
			File.WriteAllText( Path.Combine( server.Directory, "chatparks.json" ),
				$$"""[ { "id": 1, "name": "Starting Park", "level": "fantasy" }, { "id": 2, "name": "Owner's Corner", "parkId": "{{park.Id}}" } ]""" );
			ParkVisitInfo? visited = null;
			string? queued = null;
			var (session, screens, stack) = await Screens( server, "Ann", visit => visited = visit, ( level, prepare ) => { queued = level; visited = prepare(); } );

			// a satellite without a published park visits the starting park of its level, built when the game flow starts it
			await ClickSatellite( session, screens, stack, 1 );
			Assert.AreEqual( "Starting Park", ((UiLabel)stack.Top!.Find( "parkName" )!).Text() );
			((UiButton)stack.Top.Find( "visit" )!).Clicked!();
			Assert.AreEqual( "fantasy", queued, "the level is queued, so the browser copies its files first" );
			Assert.IsNotNull( visited, session.Status );
			Assert.AreEqual( (ChatProtocol.SatelliteRoom( 1 ), "fantasy", (string?)null), (visited.ChatRoom, visited.Level, visited.OnlineParkId) );
			Assert.AreEqual( ("Starting Park", OnlineScreens.ChatParkAuthor), (visited.Package.Manifest.Park.Name, visited.Package.Manifest.Park.Author) );
			Assert.IsFalse( visited.IsSandbox );
			Assert.IsTrue( visited.Payload.HasBuiltLayout, "the level's complete starting layout" );
			Assert.AreEqual( 0, visited.MissingContent.Count );

			// a satellite naming a published park downloads it as any visit, and its visit walks in the satellite's room
			visited = null;
			stack.Pop();
			stack.Pop();
			await ClickSatellite( session, screens, stack, 2 );
			((UiButton)stack.Top!.Find( "visit" )!).Clicked!();
			await OnlineIntegrationTests.PumpUntilIdle( session );
			Assert.IsNotNull( visited, session.Status );
			Assert.AreEqual( (ChatProtocol.SatelliteRoom( 2 ), park.Id, park.Level), (visited.ChatRoom, visited.OnlineParkId, visited.Level) );
			Assert.AreEqual( 1, (await owner.ListParksAsync()).Parks.Single( item => item.Id == park.Id ).Visits, "the satellite's visit is reported as any visit" );
			session.Dispose();
		}
		finally
		{
			OpenTPW.Common.GlobalNamespace.FileSystem = previousFiles;
			server.DeleteData();
		}
	}

	[TestMethod]
	public void AVisitsRoomIsTheSatellitesWhenSetAndOtherwiseItsPublishedParks()
	{
		var package = OnlineFormatTests.Package();
		var visit = new ParkVisitInfo( package, MinimalParkPayload.FromSnapshot( package.ToSnapshot() ), Array.Empty<RequiredContent>(), Array.Empty<string>(), false );
		Assert.IsNull( visit.ChatRoom, "a park file opened from disk has no room" );
		Assert.AreEqual( ChatProtocol.ParkRoom( "abc123" ), (visit with { OnlineParkId = "abc123" }).ChatRoom );
		Assert.AreEqual( ChatProtocol.ParkRoom( "abc123" ), (visit with { OnlineParkId = "abc123", ChatRoom = null }).ChatRoom, "a download's visit without a room of its own" );
		Assert.AreEqual( ChatProtocol.SatelliteRoom( 3 ), (visit with { OnlineParkId = "abc123", ChatRoom = ChatProtocol.SatelliteRoom( 3 ) }).ChatRoom );
	}

	[TestMethod]
	public void LevelVisitsReadOnlyATheme()
	{
		foreach ( var level in new[] { "../levels/jungle", "Jungle", "online_Standard", "" } )
			Assert.ThrowsException<InvalidDataException>( () => ParkSharing.PrepareLevelVisit( level, "Name", "", OnlineScreens.ChatParkAuthor ), level );
	}

	[TestMethod]
	public void TheFigureEntersAtTheCentreOfTheEntrancesFirstCell()
	{
		var cells = new ParkCellMap( 128, 128 );
		Assert.AreEqual( (61.5f, 101.5f), ParkPresence.StartOf( new GuestSettings { EntranceA = (61, 101), EntranceB = (62, 101) }, cells ), "tp.exe 0x4E5EF0: entrance 0's cell" );
		Assert.AreEqual( (64f, 64f), ParkPresence.StartOf( new GuestSettings { EntranceA = (200, 101) }, cells ), "an entrance outside the map: the park's middle" );
		Assert.AreEqual( (64f, 64f), ParkPresence.StartOf( null, cells ) );
	}

	[TestMethod]
	public void EachSatelliteHasExactlyOneRoomName()
	{
		Assert.AreEqual( 1, ChatProtocol.SatelliteIndexOf( "satellite:1" ) );
		Assert.AreEqual( 5, ChatProtocol.SatelliteIndexOf( "satellite:5" ) );
		Assert.IsNull( ChatProtocol.SatelliteIndexOf( "satellite:01" ), "a leading zero would be a second room the satellite does not count" );
		Assert.IsNull( ChatProtocol.SatelliteIndexOf( "satellite:6" ) );
		Assert.IsNull( ChatProtocol.SatelliteIndexOf( "satellite:" ) );
		Assert.IsFalse( ChatProtocol.IsValidRoom( "satellite:001" ) );
		Assert.IsTrue( ChatProtocol.IsValidRoom( ChatProtocol.SatelliteRoom( 3 ) ) );
	}
}
