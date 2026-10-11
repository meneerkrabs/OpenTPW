using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Online.Client;
using OpenTPW.Server;

namespace OpenTPW.Tests;

/// <summary>E-mail confirmation at registration and account recovery by e-mail (no real e-mail is sent).</summary>
[TestClass]
public class EmailRecoveryTests
{
	/// <summary>Keeps every e-mail instead of sending it.</summary>
	internal sealed class CapturingSender : IEmailSender
	{
		public ConcurrentQueue<EmailMessage> Sent { get; } = new();
		public Task SendAsync( EmailMessage message, CancellationToken cancel = default )
		{
			Sent.Enqueue( message );
			return Task.CompletedTask;
		}
	}

	private static ServerOptions Options( string directory ) => new()
	{
		DataDirectory = directory, PasswordIterations = 1000, ServerName = "Test", RequireTotp = true, RequireEmail = true,
		ResendApiKey = "test-key", EmailFrom = "OpenTPW <noreply@example.test>", EmailReplyTo = "contact@example.test",
	};

	private static string CodeIn( EmailMessage message ) => Regex.Match( message.Text, @"\b\d{8}\b" ).Value;
	private static string TotpAt( string base32, DateTimeOffset time, int offset = 0 ) => Totp.Code( Totp.FromBase32( base32 ), Totp.Step( time ) + offset );
	/// <summary>The next e-mail; recovery e-mails are sent in the background.</summary>
	private static async Task<EmailMessage> NextAsync( CapturingSender mail )
	{
		for ( var wait = 0; wait < 100; wait++ )
		{
			if ( mail.Sent.TryDequeue( out var message ) )
				return message;
			await Task.Delay( 50 );
		}
		throw new AssertFailedException( "no e-mail was sent" );
	}

	private static StoreException Refused( Action action ) => Assert.ThrowsException<StoreException>( action );

	[TestMethod]
	public void NewAccountsConfirmAnAddressBeforeTheyExist()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-mail-{Guid.NewGuid():N}" );
		try
		{
			var now = new DateTimeOffset( 2026, 10, 11, 12, 0, 0, TimeSpan.Zero );
			var store = new ServerStore( Options( directory ), () => now );
			var enrollment = store.BeginRegistration( "Hana", "correct horse" );
			Assert.AreEqual( true, enrollment.EmailRequired );
			var missing = Refused( () => store.ConfirmEnrollment( "Hana", enrollment.Ticket, TotpAt( enrollment.Secret, now ) ) );
			Assert.AreEqual( (400, ApiErrorReasons.EmailRequired), (missing.Status, missing.Reason) );
			Assert.AreEqual( 400, Refused( () => store.PrepareEnrollmentEmail( "Hana", enrollment.Ticket, "not an address", "Dutch" ) ).Status );
			Assert.AreEqual( 400, Refused( () => store.PrepareEnrollmentEmail( "Hana", "wrong ticket", "hana@example.test", "Dutch" ) ).Status );

			var mail = store.PrepareEnrollmentEmail( "Hana", enrollment.Ticket, "  Hana@Example.TEST ", "Dutch" );
			Assert.AreEqual( "hana@example.test", mail.To );
			Assert.AreEqual( "Je OpenTPW-code", mail.Subject, "the player's language" );
			var code = CodeIn( mail );
			Assert.AreEqual( 8, code.Length );
			Assert.IsFalse( mail.Subject.Contains( code ), "subjects never hold the code" );

			var wrong = Refused( () => store.ConfirmEnrollment( "Hana", enrollment.Ticket, TotpAt( enrollment.Secret, now ), code == "00000000" ? "11111111" : "00000000" ) );
			Assert.AreEqual( ApiErrorReasons.EmailCodeInvalid, wrong.Reason );
			Assert.IsNull( store.FindAccount( "Hana" ) );
			var session = store.ConfirmEnrollment( "Hana", enrollment.Ticket, TotpAt( enrollment.Secret, now ), code );
			Assert.AreEqual( "Hana", session.Name );
			Assert.AreEqual( "hana@example.test", store.FindAccount( "Hana" )!.Email );

			// One account per address, and at most three e-mails an hour per name and per address.
			var other = store.BeginRegistration( "Ivo", "correct horse" );
			Assert.AreEqual( 409, Refused( () => store.PrepareEnrollmentEmail( "Ivo", other.Ticket, "HANA@example.test", null ) ).Status );
			for ( var send = 0; send < ServerStore.MaximumEmailsPerHour; send++ )
				store.PrepareEnrollmentEmail( "Ivo", other.Ticket, "ivo@example.test", null );
			Assert.AreEqual( 429, Refused( () => store.PrepareEnrollmentEmail( "Ivo", other.Ticket, "ivo@example.test", null ) ).Status );
			var again = store.BeginRegistration( "Ivo", "correct horse" );
			Assert.AreEqual( 429, Refused( () => store.PrepareEnrollmentEmail( "Ivo", again.Ticket, "ivo2@example.test", null ) ).Status, "registering again does not reset the count" );
			var third = store.BeginRegistration( "Joy", "correct horse" );
			Assert.AreEqual( 429, Refused( () => store.PrepareEnrollmentEmail( "Joy", third.Ticket, "ivo@example.test", null ) ).Status, "nor does another name for the same address" );

			// A setup lives as long as the code its e-mail promises, past its own ten minutes.
			var late = store.BeginRegistration( "Lia", "correct horse" );
			now += TimeSpan.FromMinutes( 9 );
			var lateCode = CodeIn( store.PrepareEnrollmentEmail( "Lia", late.Ticket, "lia@example.test", null ) );
			now += TimeSpan.FromMinutes( 10 );
			Assert.AreEqual( "Lia", store.ConfirmEnrollment( "Lia", late.Ticket, TotpAt( late.Secret, now ), lateCode ).Name );
		}
		finally { Directory.Delete( directory, true ); }
	}

	[TestMethod]
	public void RecoverySetsANewPasswordEndsSessionsAndCanResetTwoStep()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-mail-{Guid.NewGuid():N}" );
		try
		{
			var now = new DateTimeOffset( 2026, 10, 11, 12, 0, 0, TimeSpan.Zero );
			var store = new ServerStore( Options( directory ), () => now );
			var enrollment = store.BeginRegistration( "Jan", "correct horse" );
			var code = CodeIn( store.PrepareEnrollmentEmail( "Jan", enrollment.Ticket, "jan@example.test", null ) );
			var session = store.ConfirmEnrollment( "Jan", enrollment.Ticket, TotpAt( enrollment.Secret, now ), code );

			Assert.IsNull( store.PrepareRecovery( "Nobody", null ), "an unknown name sends nothing" );
			Assert.IsNull( store.PrepareRecovery( "nobody@example.test", null ) );
			var byAddress = store.PrepareRecovery( "JAN@example.test", "German" );
			Assert.AreEqual( "jan@example.test", byAddress!.To );
			StringAssert.Contains( byAddress.Text, "Jan" );
			var recovery = CodeIn( byAddress );

			// Wrong codes use the code up after five tries.
			var bad = recovery == "00000000" ? "11111111" : "00000000";
			for ( var attempt = 0; attempt < ServerStore.MaximumEmailCodeFailures; attempt++ )
				Assert.AreEqual( 400, Refused( () => store.ConfirmRecovery( "Jan", bad, "new password", false, null ) ).Status );
			Assert.AreEqual( 400, Refused( () => store.ConfirmRecovery( "Jan", recovery, "new password", false, null ) ).Status, "a used-up code no longer works" );

			var fresh = CodeIn( store.PrepareRecovery( "Jan", null )! );
			Assert.AreEqual( 400, Refused( () => store.ConfirmRecovery( "Jan", fresh, "short", false, null ) ).Status, "the new password follows the usual rules" );
			var (account, notice) = store.ConfirmRecovery( "Jan", fresh, "new password", resetTwoStep: false, null );
			Assert.AreEqual( "Jan", account.Name );
			Assert.AreEqual( "Your OpenTPW password was changed", notice.Subject );
			Assert.IsNull( store.Authenticate( session.Token ), "every session ends" );
			Assert.AreEqual( 401, Refused( () => store.Login( "Jan", "correct horse", TotpAt( enrollment.Secret, now, 1 ) ) ).Status );
			Assert.IsNotNull( store.Login( "Jan", "new password", TotpAt( enrollment.Secret, now, 1 ) ).Session, "two-step login stays" );
			Assert.AreEqual( 400, Refused( () => store.ConfirmRecovery( "Jan", fresh, "another password", false, null ) ).Status, "a code works once" );

			// The address code and two recovery codes were this hour's three e-mails; an hour later there are three more.
			Assert.IsNull( store.PrepareRecovery( "Jan", null ), "at most three e-mails an hour" );
			now += TimeSpan.FromHours( 1 );
			store.PrepareRecovery( "Jan", null );
			store.PrepareRecovery( "jan@example.test", null );
			var third = store.PrepareRecovery( "Jan", null );
			Assert.IsNotNull( third );
			Assert.IsNull( store.PrepareRecovery( "Jan", null ) );
			var reset = store.ConfirmRecovery( "jan@example.test", CodeIn( third! ), "third password", resetTwoStep: true, "Swedish" );
			StringAssert.Contains( reset.Notice.Subject, "tvåsteg", StringComparison.OrdinalIgnoreCase );
			var outcome = store.Login( "Jan", "third password" );
			Assert.IsNull( outcome.Session );
			Assert.IsNotNull( outcome.Enrollment, "two-step login is set up again at the next login" );
		}
		finally { Directory.Delete( directory, true ); }
	}

	[TestMethod]
	public async Task TheHttpRoutesAnswerTheSameForUnknownAccountsAndSendOnlyForRealOnes()
	{
		var mail = new CapturingSender();
		await using var server = await LoopbackServer.StartAsync( options =>
		{
			options.RequireTotp = true;
			options.RequireEmail = true;
			options.ResendApiKey = "unused";
			options.EmailFrom = "OpenTPW <noreply@example.test>";
			options.EmailReplyTo = "contact@example.test";
		}, emailSender: mail );
		try
		{
			using var client = new OnlineClient( server.Url );
			var enrollment = await client.RegisterAsync( "Kai", "correct horse" );
			Assert.AreEqual( true, enrollment!.EmailRequired );
			var missing = await Assert.ThrowsExceptionAsync<OnlineException>( () => client.ConfirmEnrollmentAsync( enrollment, TotpAt( enrollment.Secret, DateTimeOffset.UtcNow ) ) );
			Assert.AreEqual( ApiErrorReasons.EmailRequired, missing.Reason );
			await client.SendEnrollmentEmailAsync( enrollment, "kai@example.test", "English" );
			Assert.IsTrue( mail.Sent.TryDequeue( out var confirmMail ) );
			await client.ConfirmEnrollmentAsync( enrollment, TotpAt( enrollment.Secret, DateTimeOffset.UtcNow ), CodeIn( confirmMail! ) );

			using var other = new OnlineClient( server.Url );
			await other.RequestRecoveryAsync( "Nobody" );
			await other.RequestRecoveryAsync( "kai@example.test" );
			var recoveryMail = await NextAsync( mail );
			Assert.AreEqual( "kai@example.test", recoveryMail.To, "no e-mail for an unknown name, and the same 202" );
			var wrong = await Assert.ThrowsExceptionAsync<OnlineException>( () => other.ConfirmRecoveryAsync( "Kai", "12345678" == CodeIn( recoveryMail ) ? "87654321" : "12345678", "new password", false ) );
			Assert.AreEqual( (HttpStatusCode.BadRequest, ApiErrorReasons.EmailCodeInvalid), (wrong.Status, wrong.Reason) );
			await other.ConfirmRecoveryAsync( "Kai", CodeIn( recoveryMail ), "new password", false );
			Assert.IsTrue( mail.Sent.TryDequeue( out var notice ) );
			Assert.IsTrue( mail.Sent.IsEmpty );
			StringAssert.Contains( notice!.Text, "contact@example.test" );
			Assert.AreEqual( HttpStatusCode.Unauthorized, (await Assert.ThrowsExceptionAsync<OnlineException>( () => client.ListParksAsync() )).Status, "the old session ended" );
			var login = await other.LoginAsync( "Kai", "new password", TotpAt( enrollment.Secret, DateTimeOffset.UtcNow, 1 ) );
			Assert.AreEqual( "Kai", login.Session?.Name );
		}
		finally { server.DeleteData(); }
	}

	[TestMethod]
	public async Task ResendGetsTheMessageAndTheKeyNeverLeaksIntoErrors()
	{
		HttpRequestMessage? seen = null;
		string? body = null;
		var handler = new StubHandler( async request =>
		{
			seen = request;
			body = await request.Content!.ReadAsStringAsync();
			return new HttpResponseMessage( HttpStatusCode.OK );
		} );
		var sender = new ResendEmailSender( "re_secret_key", "OpenTPW <noreply@example.test>", "contact@example.test", handler );
		await sender.SendAsync( new EmailMessage( "player@example.test", "Subject", "Text" ) );
		Assert.AreEqual( ResendEmailSender.Endpoint, seen!.RequestUri );
		Assert.AreEqual( "Bearer re_secret_key", seen.Headers.Authorization!.ToString() );
		using var json = JsonDocument.Parse( body! );
		Assert.AreEqual( "OpenTPW <noreply@example.test>", json.RootElement.GetProperty( "from" ).GetString() );
		Assert.AreEqual( "player@example.test", json.RootElement.GetProperty( "to" )[0].GetString() );
		Assert.AreEqual( "contact@example.test", json.RootElement.GetProperty( "reply_to" ).GetString() );
		Assert.AreEqual( "Text", json.RootElement.GetProperty( "text" ).GetString() );

		var refusing = new ResendEmailSender( "re_secret_key", "OpenTPW <noreply@example.test>", null, new StubHandler( _ => Task.FromResult( new HttpResponseMessage( HttpStatusCode.Forbidden ) ) ) );
		var error = await Assert.ThrowsExceptionAsync<EmailException>( () => refusing.SendAsync( new EmailMessage( "a@example.test", "s", "t" ) ) );
		Assert.IsFalse( error.Message.Contains( "re_secret_key" ) );
		StringAssert.Contains( error.Message, "403" );
	}

	[TestMethod]
	public void EveryEmailExistsInEveryLanguageAndAddressesAreChecked()
	{
		foreach ( var (kind, texts) in EmailTexts.All )
		{
			Assert.AreEqual( EmailTexts.Languages.Count, texts.Length, kind.ToString() );
			foreach ( var (subject, text) in texts )
			{
				Assert.IsFalse( subject.Contains( '{' ), $"{kind}: subjects hold no code or name" );
				StringAssert.Contains( text, "{0}", kind.ToString() );
			}
		}
		Assert.AreEqual( "Your OpenTPW code", EmailTexts.Compose( EmailKind.VerifyAddress, "Klingon", "a@b.c", "1", "n", "r" ).Subject, "unknown languages use English" );
		foreach ( var good in new[] { "a@b.co", " Mixed@Case.Example ", "first.last+tag@sub.example.org" } )
			Assert.IsNotNull( ServerStore.NormalizeEmail( good ), good );
		foreach ( var bad in new[] { "", "plain", "@example.test", "a@", "a@b", "a@@b.c", "a b@c.d", "a@b.c.", "\"x\"@y.z", "a,b@c.d", new string( 'a', 250 ) + "@b.co" } )
			Assert.IsNull( ServerStore.NormalizeEmail( bad ), bad );
		// The address is confirmed on the two-step setup screen, so requiring one needs two-step login.
		var options = Options( Path.GetTempPath() );
		options.RequireTotp = false;
		Assert.ThrowsException<InvalidOperationException>( options.Validate );
	}

	private sealed class StubHandler( Func<HttpRequestMessage, Task<HttpResponseMessage>> respond ) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken ) => respond( request );
	}
}
