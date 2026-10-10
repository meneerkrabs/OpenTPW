using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Chat;
using OpenTPW.Online.Client;
using OpenTPW.Online.Packages;

namespace OpenTPW.Tests;

[TestClass]
public class OnlineIntegrationTests
{
	private static OnlineFolders Folders() => new( Path.Combine( Path.GetTempPath(), "opentpw-online-" + Guid.NewGuid().ToString( "N" ) ) );

	private static async Task PumpUntilIdle( OnlineSession session )
	{
		var until = DateTime.UtcNow.AddSeconds( 10 );
		while ( session.Busy > 0 && DateTime.UtcNow < until )
		{
			session.Pump();
			await Task.Delay( 10 );
		}
		session.Pump();
		Assert.AreEqual( 0, session.Busy, session.Status );
	}

	[TestMethod]
	public void LocalFoldersKeepPostcardsAndReportUnreadableFiles()
	{
		var folders = Folders();
		try
		{
			Assert.IsNull( folders.LoadSettings().ServerUrl );
			folders.SaveSettings( new OnlineSettings( "http://localhost:5000/", "Alice" ) );
			Assert.AreEqual( "Alice", folders.LoadSettings().PlayerName );
			var card = Postcard.Create( "Alice", new[] { "Bobby" }, "Hello", "Visit my park", "English" );
			var file = folders.Store( folders.Outbox, card );
			Assert.AreEqual( file, folders.Store( folders.Outbox, card ) );
			File.WriteAllText( Path.Combine( folders.Outbox, "bad" + Postcard.FileExtension ), "broken" );
			var list = folders.List( folders.Outbox );
			Assert.AreEqual( 2, list.Count );
			Assert.AreEqual( 1, list.Count( entry => entry.Card != null ) );
			Assert.AreEqual( 1, list.Count( entry => entry.Error != null ) );
			File.WriteAllText( file, "damaged existing card" );
			Assert.ThrowsException<InvalidDataException>( () => folders.Store( folders.Outbox, card ) );
		}
		finally { Directory.Delete( folders.Root, true ); }
	}

	[TestMethod]
	public async Task SessionIgnoresResultsFromDisconnectedOperations()
	{
		var folders = Folders();
		using var session = new OnlineSession( folders );
		var release = new TaskCompletionSource<string?>( TaskCreationOptions.RunContinuationsAsynchronously );
		var started = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );
		var completed = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );
		session.Run( async () => { started.SetResult( true ); var result = await release.Task; completed.SetResult( true ); return result; } );
		session.Run( () => throw new InvalidOperationException( "Busy work must not start" ) );
		Assert.AreEqual( 1, session.Busy );
		await started.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
		session.Disconnect();
		session.Run( () => Task.FromResult<string?>( "new session" ) );
		await PumpUntilIdle( session );
		release.SetResult( "stale session" );
		await completed.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
		await Task.Delay( 30 );
		session.Pump();
		Assert.AreEqual( "new session", session.Status );
		Assert.AreEqual( 0, session.Busy );
	}

	[TestMethod]
	public async Task SessionUsesLoopbackServerForParksAndPostcardFolders()
	{
		await using var server = await LoopbackServer.StartAsync();
		var folders = Folders();
		try
		{
			using var recipient = await server.LoginAsync( "Bobby" );
			using var session = new OnlineSession( folders );
			session.UseServer( server.Url.ToString(), "Alice" );
			session.Register( "Alice", "correct horse" );
			await PumpUntilIdle( session );
			Assert.IsTrue( session.IsLoggedIn, session.Status );
			session.Publish( OnlineFormatTests.Package() );
			await PumpUntilIdle( session );
			session.RefreshParks( null );
			await PumpUntilIdle( session );
			Assert.AreEqual( 1, session.Parks.Count, session.Status );
			string? downloaded = null;
			session.Download( session.Parks[0], path => downloaded = path );
			await PumpUntilIdle( session );
			Assert.IsNotNull( downloaded, session.Status );
			Assert.AreEqual( "My Jungle", ParkPackage.Load( downloaded! ).Manifest.Park.Name );
			var card = Postcard.Create( "Alice", new[] { "Bobby" }, "Hello", "Postcard", "English" );
			folders.Store( folders.Outbox, card );
			session.SendOutbox();
			await PumpUntilIdle( session );
			Assert.AreEqual( 0, folders.List( folders.Outbox ).Count );
			Assert.AreEqual( 1, folders.List( folders.Sent ).Count );
			Assert.AreEqual( 1, (await recipient.GetInboxAsync()).Postcards.Count );
			await recipient.SendPostcardAsync( Postcard.Create( "Bobby", new[] { "Alice" }, "Reply", "Hello", "English" ) );
			// Two cards that reuse one card id (a sender's choice) must both arrive instead of blocking the inbox.
			var reused = Guid.NewGuid();
			await recipient.SendPostcardAsync( Postcard.Create( "Bobby", new[] { "Alice" }, "One", "first", "English", cardId: reused ) );
			await recipient.SendPostcardAsync( Postcard.Create( "Bobby", new[] { "Alice" }, "Two", "second", "English", cardId: reused ) );
			session.FetchInbox();
			await PumpUntilIdle( session );
			Assert.AreEqual( 3, folders.List( folders.Inbox ).Count, session.Status );
			Assert.AreEqual( 0, (await session.Client!.GetInboxAsync()).Postcards.Count );
			Assert.IsFalse( File.ReadAllText( folders.SettingsFile ).Contains( "correct horse" ) );
			session.ConnectChat();
			await PumpUntilIdle( session );
			Assert.IsNotNull( session.Chat, session.Status );
			session.SendChatLine( "hello from the game session" );
			await PumpUntilIdle( session );
			var until = DateTime.UtcNow.AddSeconds( 5 );
			while ( !session.ChatLines.Any( line => line.Contains( "hello from the game session" ) ) && DateTime.UtcNow < until )
			{
				session.Pump();
				await Task.Delay( 10 );
			}
			Assert.IsTrue( session.ChatLines.Any( line => line.Contains( "hello from the game session" ) ) );
		}
		finally { Directory.Delete( folders.Root, true ); server.DeleteData(); }
	}

	[TestMethod]
	public async Task ChatReconnectsToItsRoomAfterTheServerRestarts()
	{
		var server = await LoopbackServer.StartAsync();
		var directory = server.Directory;
		var url = server.Url;
		var folders = Folders();
		try
		{
			using var owner = await server.LoginAsync( "Bobby" );
			var park = await owner.UploadParkAsync( OnlineFormatTests.Package() );
			using var session = new OnlineSession( folders );
			session.UseServer( url.ToString(), "Alice" );
			session.Register( "Alice", "correct horse" );
			await PumpUntilIdle( session );
			session.RefreshParks( null );
			await PumpUntilIdle( session );
			session.ConnectChat();
			await PumpUntilIdle( session );
			var room = ChatProtocol.ParkRoom( park.Id );
			session.JoinRoom( room );
			await PumpUntil( session, () => session.CurrentRoom == room );

			await server.DisposeAsync();
			await using var restarted = await LoopbackServer.StartAsync( directory: directory, url: url );
			await PumpUntil( session, () => session.ChatLines.Any( line => line == OnlineStrings.Get( OnlineLabel.ChatReconnected ) ), seconds: 20 );
			Assert.IsTrue( session.IsLoggedIn, "the session survived the restart" );
			await PumpUntil( session, () => session.Chat != null && session.CurrentRoom == room );
		}
		finally
		{
			Directory.Delete( folders.Root, true );
			if ( Directory.Exists( directory ) )
				Directory.Delete( directory, true );
		}
	}

	private static async Task PumpUntil( OnlineSession session, Func<bool> done, int seconds = 10 )
	{
		var until = DateTime.UtcNow.AddSeconds( seconds );
		while ( !done() && DateTime.UtcNow < until )
		{
			session.Pump();
			await Task.Delay( 20 );
		}
		Assert.IsTrue( done(), session.Status );
	}

	[TestMethod]
	public void OriginalParkExportPreparesAReadOnlyVisitWithoutEconomy()
	{
		var data = OriginalParkImportTests.OriginalDataPath();
		var previous = OpenTPW.Common.GlobalNamespace.FileSystem;
		var fileSystem = new BaseFileSystem( data );
		fileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		OpenTPW.Common.GlobalNamespace.FileSystem = fileSystem;
		try
		{
			var park = OriginalPark.Load( "jungle" );
			var snapshot = ParkSnapshotBuilder.FromOriginal( park, null );
			var package = ParkSharing.CreatePackage( snapshot, "Original Jungle", "Read-only", "Alice", park.Map );
			var visit = ParkSharing.PrepareVisit( ParkPackage.Read( package.ToBytes() ) );
			Assert.IsTrue( visit.MatchesOriginalImport );
			Assert.IsFalse( visit.IsSandbox );
			Assert.IsNull( visit.Payload.Economy );
			Assert.AreEqual( park.Save!.PathCells.Count, visit.Payload.PathCells.Count );
			Assert.AreEqual( 0, visit.MissingContent.Count );
			CollectionAssert.Contains( package.Manifest.Flags.ToArray(), OpenTPW.Online.Packages.CompatibilityFlags.NoEconomy );
			CollectionAssert.Contains( package.Manifest.Flags.ToArray(), OpenTPW.Online.Packages.CompatibilityFlags.ReadOnlyVisit );
		}
		finally { OpenTPW.Common.GlobalNamespace.FileSystem = previous; }
	}

	[TestMethod]
	public void ChatHistoryAndSupplementaryTranslationsAreComplete()
	{
		using var session = new OnlineSession( Folders() );
		for ( var index = 0; index < OnlineSession.MaximumChatLines + 5; index++ )
			session.AddChatLine( index.ToString() );
		Assert.AreEqual( OnlineSession.MaximumChatLines, session.ChatLines.Count );
		Assert.AreEqual( "5", session.ChatLines[0] );
		foreach ( var language in GameLanguage.ShippedLanguages )
			foreach ( var label in Enum.GetValues<OnlineLabel>() )
				Assert.IsFalse( string.IsNullOrWhiteSpace( OnlineStrings.Supplementary[language][label] ) );
		session.Disconnect();
		Assert.AreEqual( 0, session.ChatLines.Count );
		Assert.AreEqual( ChatProtocol.LobbyRoom, session.CurrentRoom );
	}

	[TestMethod]
	public void ServerUrlsRejectCredentialsAndAmbiguousAddresses()
	{
		foreach ( var url in new[] { "file:///tmp/server", "https://user:pass@example.test", "https://example.test/?token=a", "https://example.test/#other" } )
			Assert.ThrowsException<ArgumentException>( () => OnlineClient.ValidateServerUrl( new Uri( url ) ) );
		Assert.AreEqual( "https://example.test/sub/", OnlineClient.ValidateServerUrl( new Uri( "https://example.test/sub" ) ).ToString() );
	}

	[TestMethod]
	public void PlainHttpIsOnlyForThisComputerAndTheLocalNetwork()
	{
		foreach ( var url in new[] { "http://localhost:8080", "http://127.0.0.1:5000", "http://[::1]:8080", "http://dev.localhost", "http://192.168.1.20",
			"http://10.0.0.5", "http://172.20.1.1", "http://[fd00::5]", "http://[fe80::1]" } )
			Assert.AreEqual( "http", OnlineClient.ValidateServerUrl( new Uri( url ) ).Scheme, url );
		foreach ( var url in new[] { "http://play.opentpw.io", "http://example.test", "http://8.8.8.8", "http://172.32.0.1", "http://[2001:db8::1]", "http://localhost.evil.test", "http://nas.local" } )
			Assert.ThrowsException<ArgumentException>( () => OnlineClient.ValidateServerUrl( new Uri( url ) ), url );
	}
}
