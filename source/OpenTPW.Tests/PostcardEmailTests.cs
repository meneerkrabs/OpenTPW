using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Online.Client;
using OpenTPW.Online.Packages;
using OpenTPW.Server;
using OpenTPW.UI;

namespace OpenTPW.Tests;

/// <summary>All mail is captured in-process; no credentials, provider calls or real recipients are used.</summary>
[TestClass]
public class PostcardEmailTests
{
	private sealed class FakeMail : IEmailSender
	{
		public List<EmailMessage> Attempts { get; } = new();
		public List<EmailMessage> Accepted { get; } = new();
		public string? Refuse { get; set; }
		public Action<EmailMessage>? AfterAccept { get; set; }
		public Task SendAsync( EmailMessage message, CancellationToken cancel = default )
		{
			Attempts.Add( message );
			if ( message.To == Refuse ) throw new EmailException( "Synthetic mail failure." );
			if ( message.IdempotencyKey == null || !Accepted.Any( accepted => accepted.IdempotencyKey == message.IdempotencyKey ) ) Accepted.Add( message );
			AfterAccept?.Invoke( message );
			return Task.CompletedTask;
		}
	}

	private static Postcard Card( params string[] recipients ) => Postcard.Create( "Sender", recipients, "A postcard", "Hello\nfrom the park", "English", image: OnlineFormatTests.Thumbnail() );

	[TestMethod]
	public void MixedRecipientsRoundTripAndKeepPlayerSpaces()
	{
		var recipients = OnlineScreens.PostcardRecipients( "Reader; first+park@example.test, Bob Smith\r\nsecond@example.test" );
		CollectionAssert.AreEqual( new[] { "Reader", "first+park@example.test", "Bob Smith", "second@example.test" }, recipients );
		CollectionAssert.AreEqual( recipients, Postcard.Read( Card( recipients ).ToBytes() ).Manifest.To.ToArray() );
	}

	[TestMethod]
	public void AddressesRejectHeaderInjectionDisplayNamesAndMalformedMixedLists()
	{
		foreach ( var address in new[] { "a@", "@example.test", "a@@example.test", "a@localhost", "a@.test", "a@example..test", "a@-example.test", "a@example-.test",
			".a@example.test", "a..b@example.test", "a.@example.test", "Alice <a@example.test>", "\"a\"@example.test", "a@example.test\r\nBcc:b@example.test",
			"a@example.test,b@example.test", "a@example.test;b@example.test", "a@example.test\u202E", "a\uD800@example.test", "a b@example.test", new string( 'a', 65 ) + "@example.test" } )
			Assert.ThrowsException<InvalidDataException>( () => Card( "Reader", address ), address );
		Assert.ThrowsException<InvalidDataException>( () => Card( "a@example.test", "A@EXAMPLE.TEST" ) );
		Assert.ThrowsException<InvalidDataException>( () => Card( "Ｂｏｂ", "Bob" ) );
		Assert.ThrowsException<InvalidDataException>( () => Card( Enumerable.Range( 0, 11 ).Select( i => $"a{i}@example.test" ).ToArray() ) );
		Assert.ThrowsException<InvalidDataException>( () => Postcard.Create( "Sender", new[] { "a@example.test" }, "bad\r\nsubject", "body", "English" ) );
	}

	[TestMethod]
	public async Task MixedDeliveryUsesExistingMailSenderAndKeepsAddressesPrivate()
	{
		var mail = new FakeMail();
		await using var server = await LoopbackServer.StartAsync( emailSender: mail );
		try
		{
			using var sender = await server.LoginAsync( "Sender" );
			using var reader = await server.LoginAsync( "Reader" );
			var card = Card( "Reader", "one@example.test", "two@example.test" );
			var sent = await sender.SendPostcardAsync( card );
			CollectionAssert.AreEqual( card.Manifest.To.ToArray(), sent.DeliveredTo.ToArray() );
			Assert.IsNull( sent.Error );
			Assert.AreEqual( 2, mail.Accepted.Count );
			foreach ( var message in mail.Accepted )
			{
				Assert.AreEqual( card.Manifest.Title, message.Subject );
				StringAssert.Contains( message.Text, "Sender" );
				StringAssert.Contains( message.Text, card.Manifest.Text );
				Assert.IsFalse( message.Text.Contains( "@" ), "Other addressees must not appear in the mail body." );
				CollectionAssert.AreEqual( card.Image!, message.Image! );
				Assert.IsNotNull( message.IdempotencyKey );
			}
			var inbox = await reader.GetInboxAsync();
			var received = await reader.DownloadPostcardAsync( inbox.Postcards.Single().Id );
			CollectionAssert.AreEqual( new[] { "Reader" }, received.Manifest.To.ToArray() );
			CollectionAssert.AreEqual( card.Image!, received.Image! );
			await sender.SendPostcardAsync( card );
			Assert.AreEqual( 2, mail.Accepted.Count, "A repeated request is already confirmed." );
			Assert.AreEqual( 1, (await reader.GetInboxAsync()).Postcards.Count );
			await reader.DeletePostcardAsync( inbox.Postcards.Single().Id );
			await sender.SendPostcardAsync( card );
			Assert.AreEqual( 0, (await reader.GetInboxAsync()).Postcards.Count, "Downloading/deleting a card does not erase its delivery receipt." );
		}
		finally { server.DeleteData(); }
	}

	[TestMethod]
	public async Task AnonymousForgedAndMalformedRequestsNeverSendMail()
	{
		var mail = new FakeMail();
		await using var server = await LoopbackServer.StartAsync( emailSender: mail );
		try
		{
			using var anonymous = new OnlineClient( server.Url );
			Assert.AreEqual( HttpStatusCode.Unauthorized, (await Assert.ThrowsExceptionAsync<OnlineException>( () => anonymous.SendPostcardAsync( Card( "a@example.test" ) ) )).Status );
			using var sender = await server.LoginAsync( "Sender" );
			var forged = Postcard.Create( "Other", new[] { "a@example.test" }, "Hi", "Text", "English" );
			Assert.AreEqual( HttpStatusCode.BadRequest, (await Assert.ThrowsExceptionAsync<OnlineException>( () => sender.SendPostcardAsync( forged ) )).Status );
			using var http = new HttpClient { BaseAddress = server.Url };
			http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue( "Bearer", sender.Session!.Token );
			var invalid = Card( "Reader" ) with { Manifest = Card( "Reader" ).Manifest with { To = new[] { "Reader", "bad@" }, Image = null }, Image = null };
			var bytes = BoundedZip.Write( new[] { new KeyValuePair<string, byte[]>( Postcard.CardEntry, StrictJson.Serialize( invalid.Manifest ) ) } );
			using var response = await http.PostAsync( ApiRoutes.Postcards, new ByteArrayContent( bytes ) );
			Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode );
			Assert.AreEqual( 0, mail.Attempts.Count );
		}
		finally { server.DeleteData(); }
	}

	[TestMethod]
	public async Task MissingMailConfigurationPreservesLocalPlayerDeliveryAndNamesUnknownPlayersCorrectly()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			using var sender = await server.LoginAsync( "Sender" );
			using var reader = await server.LoginAsync( "Reader" );
			var sent = await sender.SendPostcardAsync( Card( "Reader", "a@example.test" ) );
			CollectionAssert.AreEqual( new[] { "Reader" }, sent.DeliveredTo.ToArray() );
			StringAssert.Contains( sent.Error!, "no e-mail delivery configured" );
			var unknown = await Assert.ThrowsExceptionAsync<OnlineException>( () => sender.SendPostcardAsync( Card( "Unknown" ) ) );
			Assert.AreEqual( HttpStatusCode.NotFound, unknown.Status );
			StringAssert.Contains( unknown.Message, "Player recipient unknown" );
		}
		finally { server.DeleteData(); }
	}

	[TestMethod]
	public async Task PartialMailFailuresStayInOutboxAndSendAllRetriesOnlyUnconfirmedRecipients()
	{
		var mail = new FakeMail { Refuse = "fail@example.test" };
		await using var server = await LoopbackServer.StartAsync( emailSender: mail );
		var folders = OnlineIntegrationTests.Folders();
		try
		{
			using var reader = await server.LoginAsync( "Reader" );
			using var session = new OnlineSession( folders );
			session.UseServer( server.Url.ToString(), "Sender" );
			session.Register( "Sender", "correct horse" );
			await OnlineIntegrationTests.PumpUntilIdle( session );
			var card = Card( "Reader", "ok@example.test", "fail@example.test" );
			folders.Store( folders.Outbox, card );
			session.SendOutbox();
			await OnlineIntegrationTests.PumpUntilIdle( session );
			CollectionAssert.AreEqual( new[] { "fail@example.test" }, folders.List( folders.Outbox ).Single().Card!.Manifest.To.ToArray() );
			CollectionAssert.AreEqual( new[] { "Reader", "ok@example.test" }, folders.List( folders.Sent ).Single().Card!.Manifest.To.ToArray() );
			Assert.AreEqual( 1, mail.Accepted.Count );
			var retryKey = mail.Attempts.Last().IdempotencyKey;
			mail.Refuse = null;
			session.SendOutbox();
			await OnlineIntegrationTests.PumpUntilIdle( session );
			Assert.AreEqual( 0, folders.List( folders.Outbox ).Count, session.Status );
			Assert.AreEqual( 2, folders.List( folders.Sent ).Count );
			Assert.AreEqual( 2, mail.Accepted.Count );
			Assert.AreEqual( retryKey, mail.Attempts.Last().IdempotencyKey, "Shrinking the recipient list keeps the provider retry key." );
			Assert.AreEqual( 1, (await reader.GetInboxAsync()).Postcards.Count );
		}
		finally { Directory.Delete( folders.Root, true ); server.DeleteData(); }
	}

	[TestMethod]
	public async Task ReceiptsAndSenderRateLimitsSurviveRestartAndAreScopedToTheAccountAndContent()
	{
		var directory = Directory.CreateTempSubdirectory( "opentpw-card-ledger-" ).FullName;
		var now = DateTimeOffset.UtcNow;
		var options = new ServerOptions { DataDirectory = directory, PasswordIterations = 1000, RequireTotp = false, PostcardRecipientsPerHour = 2, PostcardEmailsPerAddressPerHour = 20 };
		try
		{
			var store = new ServerStore( options, () => now );
			var sender = store.Register( "Sender", "correct horse" );
			var other = store.Register( "Other", "correct horse" );
			var reader = store.Register( "Reader", "correct horse" );
			var mail = new FakeMail();
			var card = Card( "Reader", "a@example.test" );
			await store.SendPostcardAsync( sender, card, mail );
			store = new ServerStore( options, () => now );
			await store.SendPostcardAsync( sender, card, mail );
			Assert.AreEqual( 1, mail.Attempts.Count );
			Assert.AreEqual( 1, store.Inbox( reader ).Count );
			Assert.AreEqual( 429, (await Assert.ThrowsExceptionAsync<StoreException>( () => store.SendPostcardAsync( sender, Card( "new@example.test" ), mail ) )).Status );
			var otherCard = card with { Manifest = card.Manifest with { From = "Other", To = new[] { "a@example.test" } } };
			await store.SendPostcardAsync( other, otherCard, mail );
			Assert.AreEqual( 2, mail.Attempts.Count );
			Assert.AreNotEqual( mail.Attempts[0].IdempotencyKey, mail.Attempts[1].IdempotencyKey );
			now += TimeSpan.FromHours( 1 );
			await store.SendPostcardAsync( sender, card with { Manifest = card.Manifest with { Text = "Changed content", To = new[] { "a@example.test" } } }, mail );
			Assert.AreEqual( 3, mail.Attempts.Count, "Reusing a card id with different content is a new postcard." );
			Assert.IsFalse( File.ReadAllText( Path.Combine( directory, "postcard-deliveries.json" ) ).Contains( "@" ) );
			Assert.IsFalse( File.ReadAllText( Path.Combine( directory, "postcard-attempts.json" ) ).Contains( "@" ) );
		}
		finally { Directory.Delete( directory, true ); }
	}

	[TestMethod]
	public async Task DestinationLimitsSpanSendersAndFailedMailAttemptsCannotBeRetriedIndefinitely()
	{
		var directory = Directory.CreateTempSubdirectory( "opentpw-card-limit-" ).FullName;
		var now = DateTimeOffset.UtcNow;
		var options = new ServerOptions { DataDirectory = directory, PasswordIterations = 1000, RequireTotp = false, PostcardEmailsPerAddressPerHour = 1 };
		try
		{
			var store = new ServerStore( options, () => now );
			var sender = store.Register( "Sender", "correct horse" );
			var other = store.Register( "Other", "correct horse" );
			var mail = new FakeMail { Refuse = "a@example.test" };
			var card = Card( "a@example.test" );
			Assert.AreEqual( 503, (await Assert.ThrowsExceptionAsync<StoreException>( () => store.SendPostcardAsync( sender, card, mail ) )).Status );
			var otherCard = card with { Manifest = card.Manifest with { From = "Other" } };
			Assert.AreEqual( 429, (await Assert.ThrowsExceptionAsync<StoreException>( () => store.SendPostcardAsync( other, otherCard, mail ) )).Status );
			now += ServerStore.PostcardMailRetryWindow;
			store = new ServerStore( options, () => now );
			Assert.AreEqual( 409, (await Assert.ThrowsExceptionAsync<StoreException>( () => store.SendPostcardAsync( sender, card, mail ) )).Status );
			Assert.AreEqual( 1, mail.Attempts.Count );
			now += ServerStore.PostcardReceiptLifetime;
			store = new ServerStore( options, () => now );
			Assert.AreEqual( 409, (await Assert.ThrowsExceptionAsync<StoreException>( () => store.SendPostcardAsync( sender, card, mail ) )).Status );
			Assert.AreEqual( 1, mail.Attempts.Count, "An uncertain send never becomes safe merely because successful receipts expired." );
		}
		finally { Directory.Delete( directory, true ); }
	}


	[TestMethod]
	public async Task FailedInboxIndexWriteIsRepairedBeforeAReceiptCanSuppressTheCard()
	{
		var directory = Directory.CreateTempSubdirectory( "opentpw-card-storage-" ).FullName;
		var options = new ServerOptions { DataDirectory = directory, PasswordIterations = 1000, RequireTotp = false };
		try
		{
			var store = new ServerStore( options );
			var sender = store.Register( "Sender", "correct horse" );
			var reader = store.Register( "Reader", "correct horse" );
			var card = Card( "Reader" );
			var index = Path.Combine( directory, "postcards.json" );
			Directory.CreateDirectory( index );
			try
			{
				await store.SendPostcardAsync( sender, card, null );
				Assert.Fail( "The synthetic inbox-index failure should reject the request." );
			}
			catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException ) { }
			Directory.Delete( index );
			await store.SendPostcardAsync( sender, card, null );
			store = new ServerStore( options );
			Assert.AreEqual( 1, store.Inbox( reader ).Count );
			await store.SendPostcardAsync( sender, card, null );
			Assert.AreEqual( 1, store.Inbox( reader ).Count );
		}
		finally { Directory.Delete( directory, true ); }
	}


	public sealed record LegacyPostcardResult( string Id, IReadOnlyList<string> DeliveredTo );

	[TestMethod]
	public void SuccessfulResponsesStillDeserializeWithTheLegacyStrictContract()
	{
		var bytes = StrictJson.Serialize( new PostcardSent( "id", new[] { "Reader" } ) );
		var legacy = StrictJson.Deserialize<LegacyPostcardResult>( bytes, "Legacy postcard result" );
		CollectionAssert.AreEqual( new[] { "Reader" }, legacy.DeliveredTo.ToArray() );
		Assert.IsFalse( Encoding.UTF8.GetString( bytes ).Contains( "error" ) );
	}

	[TestMethod]
	public async Task AcceptedMailWithAFailedReceiptWriteRetriesTheSameProviderKeyAfterRestart()
	{
		var directory = Directory.CreateTempSubdirectory( "opentpw-card-acceptance-" ).FullName;
		var options = new ServerOptions { DataDirectory = directory, PasswordIterations = 1000, RequireTotp = false };
		try
		{
			var store = new ServerStore( options );
			var sender = store.Register( "Sender", "correct horse" );
			var card = Card( "a@example.test" );
			var ledger = Path.Combine( directory, "postcard-deliveries.json" );
			byte[]? pending = null;
			var mail = new FakeMail { AfterAccept = _ =>
			{
				// Preserve the last durable state, then make only the confirmation write fail.
				pending = File.ReadAllBytes( ledger );
				File.Delete( ledger );
				Directory.CreateDirectory( ledger );
			} };
			try
			{
				await store.SendPostcardAsync( sender, card, mail );
				Assert.Fail( "The synthetic receipt write should fail." );
			}
			catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException ) { }
			Directory.Delete( ledger );
			File.WriteAllBytes( ledger, pending! );
			mail.AfterAccept = null;
			store = new ServerStore( options );
			await store.SendPostcardAsync( sender, card, mail );
			Assert.AreEqual( 2, mail.Attempts.Count );
			Assert.AreEqual( 1, mail.Accepted.Count, "The fake provider honors the same idempotency contract as Resend." );
			Assert.AreEqual( mail.Attempts[0].IdempotencyKey, mail.Attempts[1].IdempotencyKey );
			store = new ServerStore( options );
			await store.SendPostcardAsync( sender, card, mail );
			Assert.AreEqual( 2, mail.Attempts.Count, "The confirmed receipt survives another restart." );
		}
		finally { Directory.Delete( directory, true ); }
	}

	private sealed class BlockingMail : IEmailSender
	{
		public TaskCompletionSource<bool> Started { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
		public TaskCompletionSource<bool> Release { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
		public async Task SendAsync( EmailMessage message, CancellationToken cancel = default )
		{
			Started.TrySetResult( true );
			await Release.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
		}
	}

	[TestMethod]
	public async Task ConcurrentRequestsFromOneAccountCannotRaceTheDeliveryLedger()
	{
		var directory = Directory.CreateTempSubdirectory( "opentpw-card-concurrent-" ).FullName;
		var mail = new BlockingMail();
		try
		{
			var store = new ServerStore( new ServerOptions { DataDirectory = directory, PasswordIterations = 1000, RequireTotp = false } );
			var sender = store.Register( "Sender", "correct horse" );
			var card = Card( "a@example.test" );
			var first = store.SendPostcardAsync( sender, card, mail );
			await mail.Started.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
			Assert.AreEqual( 409, (await Assert.ThrowsExceptionAsync<StoreException>( () => store.SendPostcardAsync( sender, card, mail ) )).Status );
			mail.Release.TrySetResult( true );
			Assert.AreEqual( 1, (await first).DeliveredTo.Count );
			Assert.AreEqual( 1, (await store.SendPostcardAsync( sender, card, mail )).DeliveredTo.Count );
		}
		finally { mail.Release.TrySetResult( true ); Directory.Delete( directory, true ); }
	}

	[TestMethod]
	public async Task UncertainTombstonesHaveAPerAccountBound()
	{
		var directory = Directory.CreateTempSubdirectory( "opentpw-card-tombstones-" ).FullName;
		var options = new ServerOptions { DataDirectory = directory, PasswordIterations = 1000, RequireTotp = false };
		try
		{
			var store = new ServerStore( options );
			var sender = store.Register( "Sender", "correct horse" );
			var principal = ParkPackage.Hash( Encoding.UTF8.GetBytes( sender.Key + "\n" + sender.CreatedUtc.ToString( "O" ) ) );
			var old = DateTimeOffset.UtcNow - TimeSpan.FromDays( 40 );
			var tombstones = Enumerable.Range( 0, ServerStore.MaximumUnconfirmedPostcardsPerSender ).ToDictionary( index => index.ToString(),
				_ => new ServerStore.PostcardDeliveryRecord { Sender = principal, StartedUtc = old } );
			File.WriteAllBytes( Path.Combine( directory, "postcard-deliveries.json" ), JsonSerializer.SerializeToUtf8Bytes( tombstones, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase } ) );
			store = new ServerStore( options );
			var mail = new FakeMail();
			Assert.AreEqual( 429, (await Assert.ThrowsExceptionAsync<StoreException>( () => store.SendPostcardAsync( sender, Card( "a@example.test" ), mail ) )).Status );
			Assert.AreEqual( 0, mail.Attempts.Count );
		}
		finally { Directory.Delete( directory, true ); }
	}

	private sealed class StubHandler( Func<HttpRequestMessage, Task<HttpResponseMessage>> send ) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancel ) => send( request );
	}

	[TestMethod]
	public async Task ResendSerializesThePngAttachmentAndStableIdempotencyHeader()
	{
		var image = OnlineFormatTests.Thumbnail();
		var handler = new StubHandler( async request =>
		{
			Assert.AreEqual( "postcard/test-key", request.Headers.GetValues( "Idempotency-Key" ).Single() );
			using var json = JsonDocument.Parse( await request.Content!.ReadAsStringAsync() );
			var root = json.RootElement;
			Assert.AreEqual( "a@example.test", root.GetProperty( "to" )[0].GetString() );
			Assert.AreEqual( "From server <noreply@example.test>", root.GetProperty( "from" ).GetString() );
			Assert.AreEqual( "contact@example.test", root.GetProperty( "reply_to" ).GetString() );
			Assert.AreEqual( "postcard.png", root.GetProperty( "attachments" )[0].GetProperty( "filename" ).GetString() );
			CollectionAssert.AreEqual( image, Convert.FromBase64String( root.GetProperty( "attachments" )[0].GetProperty( "content" ).GetString()! ) );
			return new HttpResponseMessage( HttpStatusCode.OK );
		} );
		var sender = new ResendEmailSender( "synthetic-test-key", "From server <noreply@example.test>", "contact@example.test", handler );
		await sender.SendAsync( new EmailMessage( "a@example.test", "Title", "Text", image, "postcard/test-key" ) );
	}
}
