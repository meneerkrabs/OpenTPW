using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Online.Client;
using OpenTPW.Server;

namespace OpenTPW.Tests;

/// <summary>Two-step login (authenticator codes, RFC 6238) on the server, the client and the game session.</summary>
[TestClass]
public class TwoStepLoginTests
{
	private static ServerOptions Options( string directory ) => new()
	{
		DataDirectory = directory, PasswordIterations = 1000, ServerName = "Test Server", RequireTotp = true,
	};

	private static string CodeAt( string base32, DateTimeOffset time, int stepOffset = 0 ) =>
		Totp.Code( Totp.FromBase32( base32 ), Totp.Step( time ) + stepOffset );

	private static StoreException Refused( Action action ) => Assert.ThrowsException<StoreException>( action );

	[TestMethod]
	public void RegistrationExistsOnlyAfterTheFirstCode()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-totp-{Guid.NewGuid():N}" );
		try
		{
			var now = new DateTimeOffset( 2026, 10, 10, 12, 0, 0, TimeSpan.Zero );
			var store = new ServerStore( Options( directory ), () => now );
			var enrollment = store.BeginRegistration( "Ann Marie", "correct horse" );
			StringAssert.StartsWith( enrollment.Uri, "otpauth://totp/Test%20Server:Ann%20Marie?secret=" + enrollment.Secret );
			Assert.AreEqual( Totp.SecretBytes, Totp.FromBase32( enrollment.Secret ).Length );
			Assert.IsNull( store.FindAccount( "Ann Marie" ), "nothing is stored before the code" );
			Assert.AreEqual( 401, Refused( () => store.Login( "Ann Marie", "correct horse" ) ).Status );

			Assert.AreEqual( 400, Refused( () => store.ConfirmEnrollment( "Ann Marie", "not-the-ticket", CodeAt( enrollment.Secret, now ) ) ).Status,
				"a code without the setup's ticket is refused" );
			var wrong = Refused( () => store.ConfirmEnrollment( "Ann Marie", enrollment.Ticket, "000000" == CodeAt( enrollment.Secret, now ) ? "111111" : "000000" ) );
			Assert.AreEqual( (401, ApiErrorReasons.TotpInvalid), (wrong.Status, wrong.Reason) );
			var session = store.ConfirmEnrollment( "ann marie", enrollment.Ticket, CodeAt( enrollment.Secret, now ) );
			Assert.AreEqual( "Ann Marie", session.Name );
			Assert.AreEqual( enrollment.Secret, store.FindAccount( "Ann Marie" )!.TotpSecret );
			StringAssert.Contains( File.ReadAllText( Path.Combine( directory, "accounts.json" ) ), "\"totpSecret\"" );
			Assert.AreEqual( 400, Refused( () => store.ConfirmEnrollment( "Ann Marie", enrollment.Ticket, CodeAt( enrollment.Secret, now ) ) ).Status, "the setup is used up" );

			var missing = Refused( () => store.Login( "Ann Marie", "correct horse" ) );
			Assert.AreEqual( (401, ApiErrorReasons.TotpRequired), (missing.Status, missing.Reason) );
			Assert.AreEqual( ApiErrorReasons.TotpInvalid, Refused( () => store.Login( "Ann Marie", "correct horse", CodeAt( enrollment.Secret, now ) ) ).Reason,
				"the code that confirmed the setup cannot log in again" );
			Assert.IsNotNull( store.Login( "Ann Marie", "correct horse", CodeAt( enrollment.Secret, now, 1 ) ).Session, "the next step's code works" );
			Assert.AreEqual( 401, Refused( () => store.Login( "Ann Marie", "wrong password", CodeAt( enrollment.Secret, now, 1 ) ) ).Status );
		}
		finally { Directory.Delete( directory, true ); }
	}

	[TestMethod]
	public void SetupsExpireAndWrongCodesAreLimited()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-totp-{Guid.NewGuid():N}" );
		try
		{
			var now = new DateTimeOffset( 2026, 10, 10, 12, 0, 0, TimeSpan.Zero );
			var store = new ServerStore( Options( directory ), () => now );
			var expired = store.BeginRegistration( "Bob", "correct horse" );
			now += ServerStore.EnrollmentLifetime + TimeSpan.FromSeconds( 1 );
			Assert.AreEqual( 400, Refused( () => store.ConfirmEnrollment( "Bob", expired.Ticket, CodeAt( expired.Secret, now ) ) ).Status );

			var enrollment = store.BeginRegistration( "Bob", "correct horse" );
			var good = CodeAt( enrollment.Secret, now );
			var bad = good == "123456" ? "654321" : "123456";
			for ( var attempt = 0; attempt < 20; attempt++ )
				Assert.AreEqual( 400, Refused( () => store.ConfirmEnrollment( "Bob", "guessed-ticket", bad ) ).Status, "strangers cannot use up the attempts" );
			for ( var attempt = 0; attempt < ServerStore.MaximumCodeFailures; attempt++ )
				Assert.AreEqual( 401, Refused( () => store.ConfirmEnrollment( "Bob", enrollment.Ticket, bad ) ).Status );
			Assert.AreEqual( 429, Refused( () => store.ConfirmEnrollment( "Bob", enrollment.Ticket, good ) ).Status, "after five wrong codes even the right one waits" );
			var later = store.BeginRegistration( "Bob", "correct horse" );
			Assert.AreEqual( "Bob", store.ConfirmEnrollment( "Bob", later.Ticket, CodeAt( later.Secret, now ) ).Name, "a new setup starts with a clean count" );
			Assert.AreEqual( 409, Refused( () => store.BeginRegistration( "BOB", "other password" ) ).Status );
		}
		finally { Directory.Delete( directory, true ); }
	}

	[TestMethod]
	public void OlderAccountsKeepPasswordLoginAndTheOperatorCanResetASecretOnce()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-totp-{Guid.NewGuid():N}" );
		try
		{
			var now = new DateTimeOffset( 2026, 10, 10, 12, 0, 0, TimeSpan.Zero );
			var options = Options( directory );
			var store = new ServerStore( options, () => now );
			store.Register( "Old Timer", "correct horse" );
			Assert.IsNotNull( store.Login( "Old Timer", "correct horse" ).Session, "accounts from before two-step login need no code" );

			var enrollment = store.BeginRegistration( "Cleo", "correct horse" );
			store.ConfirmEnrollment( "Cleo", enrollment.Ticket, CodeAt( enrollment.Secret, now ) );
			var resets = Path.Combine( directory, ServerStore.TotpResetDirectory );
			Assert.IsTrue( Directory.Exists( resets ), "the server creates the reset folder so it owns it" );
			File.WriteAllText( Path.Combine( resets, "other.txt" ), "someone else\n" );
			File.WriteAllText( Path.Combine( resets, "cleo.txt" ), "cleo\n" );
			now += TimeSpan.FromMinutes( 1 );
			Assert.AreEqual( 401, Refused( () => store.Login( "Cleo", "wrong password" ) ).Status, "a wrong password does not reset" );
			Assert.IsTrue( File.Exists( Path.Combine( resets, "cleo.txt" ) ) );
			var reset = store.Login( "Cleo", "correct horse" );
			Assert.IsNull( reset.Session );
			Assert.IsNotNull( reset.Enrollment, "a reset account sets up a new secret at its next login" );
			Assert.AreNotEqual( enrollment.Secret, reset.Enrollment!.Secret );
			Assert.IsFalse( File.Exists( Path.Combine( resets, "cleo.txt" ) ), "the request is used once" );
			Assert.IsTrue( File.Exists( Path.Combine( resets, "other.txt" ) ) );
			Assert.IsNotNull( store.ConfirmEnrollment( "Cleo", reset.Enrollment.Ticket, CodeAt( reset.Enrollment.Secret, now ) ) );
			Assert.AreEqual( ApiErrorReasons.TotpRequired, Refused( () => store.Login( "Cleo", "correct horse" ) ).Reason );
		}
		finally { Directory.Delete( directory, true ); }
	}

	[TestMethod]
	public void AResetSetupCannotFinishADifferentAccountOfTheSameName()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-totp-{Guid.NewGuid():N}" );
		try
		{
			var now = new DateTimeOffset( 2026, 10, 10, 12, 0, 0, TimeSpan.Zero );
			var options = Options( directory );
			var store = new ServerStore( options, () => now );
			var first = store.BeginRegistration( "Dave", "first password" );
			store.ConfirmEnrollment( "Dave", first.Ticket, CodeAt( first.Secret, now ) );
			now += TimeSpan.FromMinutes( 1 );
			File.WriteAllText( Path.Combine( directory, ServerStore.TotpResetDirectory, "dave.txt" ), "Dave" );
			var reset = store.Login( "Dave", "first password" ).Enrollment!;
			// The old owner deletes the account; someone else takes the name before the reset setup is used.
			store.DeleteAccount( store.FindAccount( "Dave" )!, "first password" );
			options.RequireTotp = false;
			store.Register( "Dave", "second password" );
			Assert.AreEqual( 400, Refused( () => store.ConfirmEnrollment( "Dave", reset.Ticket, CodeAt( reset.Secret, now, 1 ) ) ).Status );
			Assert.IsNull( store.FindAccount( "Dave" )!.TotpSecret, "the new account is untouched" );
		}
		finally { Directory.Delete( directory, true ); }
	}

	[TestMethod]
	public void LoginCodesAreLimitedUsedOnceAcrossRestartsAndStaleResetsAreIgnored()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-totp-{Guid.NewGuid():N}" );
		try
		{
			var now = DateTimeOffset.UtcNow;
			var options = Options( directory );
			var store = new ServerStore( options, () => now );
			var enrollment = store.BeginRegistration( "Gus", "correct horse" );
			Assert.AreEqual( 409, Refused( () => store.BeginRegistration( "Gus", "a stranger's password" ) ).Status, "a stranger cannot replace a waiting setup" );
			var again = store.BeginRegistration( "Gus", "correct horse" );
			Assert.AreNotEqual( enrollment.Ticket, again.Ticket, "the same player asking again gets a fresh setup" );
			store.ConfirmEnrollment( "Gus", again.Ticket, CodeAt( again.Secret, now ) );

			var good = CodeAt( again.Secret, now, 1 );
			var bad = good == "123456" ? "654321" : "123456";
			for ( var attempt = 0; attempt < ServerStore.MaximumCodeFailures; attempt++ )
				Assert.AreEqual( 401, Refused( () => store.Login( "Gus", "correct horse", bad ) ).Status );
			Assert.AreEqual( 429, Refused( () => store.Login( "Gus", "correct horse", good ) ).Status, "five wrong login codes block codes for the account" );
			now += ServerStore.CodeFailureWindow + TimeSpan.FromSeconds( 1 );
			var fresh = CodeAt( again.Secret, now );
			Assert.IsNotNull( store.Login( "Gus", "correct horse", fresh ).Session );

			var restarted = new ServerStore( options, () => now );
			Assert.AreEqual( ApiErrorReasons.TotpInvalid, Refused( () => restarted.Login( "Gus", "correct horse", fresh ) ).Reason, "a used code stays used after a restart" );

			// A request older than the account is a leftover (for example from a deleted account of the same name).
			var stale = Path.Combine( directory, ServerStore.TotpResetDirectory, "gus.txt" );
			File.WriteAllText( stale, "Gus" );
			File.SetLastWriteTimeUtc( stale, restarted.FindAccount( "Gus" )!.CreatedUtc.UtcDateTime.AddMinutes( -1 ) );
			Assert.AreEqual( ApiErrorReasons.TotpRequired, Refused( () => restarted.Login( "Gus", "correct horse" ) ).Reason, "a stale request is not applied" );

			// A request dated in the future is used once, even when its file stays behind.
			File.SetLastWriteTimeUtc( stale, now.UtcDateTime.AddDays( 1 ) );
			var reset = restarted.Login( "Gus", "correct horse" ).Enrollment!;
			File.WriteAllText( stale, "Gus" );
			File.SetLastWriteTimeUtc( stale, now.UtcDateTime.AddDays( 1 ) );
			Assert.IsTrue( File.Exists( stale ), "the copy that could not be deleted (recreated here) stays" );
			now += TimeSpan.FromMinutes( 1 );
			restarted.ConfirmEnrollment( "Gus", reset.Ticket, CodeAt( reset.Secret, now ) );
			Assert.AreEqual( ApiErrorReasons.TotpRequired, Refused( () => restarted.Login( "Gus", "correct horse" ) ).Reason, "the left-over request does not reset again" );
		}
		finally { Directory.Delete( directory, true ); }
	}

	[TestMethod]
	public void SecretsAreReadableByTheServerOnly()
	{
		if ( OperatingSystem.IsWindows() )
			Assert.Inconclusive( "Unix file modes." );
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-totp-{Guid.NewGuid():N}" );
		try
		{
			var now = DateTimeOffset.UtcNow;
			var store = new ServerStore( Options( directory ), () => now );
			var enrollment = store.BeginRegistration( "Fay", "correct horse" );
			store.ConfirmEnrollment( "Fay", enrollment.Ticket, CodeAt( enrollment.Secret, now ) );
			Assert.AreEqual( UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode( Path.Combine( directory, "accounts.json" ) ) );
			Assert.AreEqual( UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode( directory ) );
		}
		finally { Directory.Delete( directory, true ); }
	}

	[TestMethod]
	public async Task ClientsRegisterConfirmAndLogInWithCodesOverHttp()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.RequireTotp = true );
		try
		{
			using var client = new OnlineClient( server.Url );
			var enrollment = await client.RegisterAsync( "Dana", "correct horse" );
			Assert.IsNotNull( enrollment );
			Assert.IsNull( client.Session );
			var session = await client.ConfirmEnrollmentAsync( enrollment!, CodeAt( enrollment!.Secret, DateTimeOffset.UtcNow ) );
			Assert.AreEqual( "Dana", session.Name );

			using var other = new OnlineClient( server.Url );
			var missing = await Assert.ThrowsExceptionAsync<OnlineException>( () => other.LoginAsync( "Dana", "correct horse" ) );
			Assert.AreEqual( (HttpStatusCode.Unauthorized, ApiErrorReasons.TotpRequired), (missing.Status, missing.Reason) );
			var login = await other.LoginAsync( "Dana", "correct horse", CodeAt( enrollment.Secret, DateTimeOffset.UtcNow, 1 ) );
			Assert.AreEqual( "Dana", login.Session?.Name );
			Assert.AreEqual( 0, (await other.ListParksAsync()).Total );
		}
		finally { server.DeleteData(); }
	}

	[TestMethod]
	public async Task TheGameSessionShowsTheSetupAndLogsInAfterTheCode()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.RequireTotp = true );
		var folders = OnlineIntegrationTests.Folders();
		try
		{
			using var session = new OnlineSession( folders );
			session.UseServer( server.Url.ToString(), "Erin" );
			session.Register( "Erin", "correct horse" );
			await OnlineIntegrationTests.PumpUntilIdle( session );
			Assert.IsFalse( session.IsLoggedIn );
			var enrollment = session.Enrollment;
			Assert.IsNotNull( enrollment, session.Status );
			session.ConfirmEnrollment( CodeAt( enrollment!.Secret, DateTimeOffset.UtcNow ) );
			await OnlineIntegrationTests.PumpUntilIdle( session );
			Assert.IsTrue( session.IsLoggedIn, session.Status );
			Assert.IsNull( session.Enrollment );
			Assert.IsFalse( File.ReadAllText( folders.SettingsFile ).Contains( enrollment.Secret ), "the secret is never saved on the player's machine" );

			session.Disconnect();
			session.UseServer( server.Url.ToString(), "Erin" );
			session.Login( "Erin", "correct horse" );
			await OnlineIntegrationTests.PumpUntilIdle( session );
			Assert.IsTrue( session.CodeRequested, session.Status );
			Assert.IsFalse( session.IsLoggedIn );
			session.Login( "Erin", "correct horse", CodeAt( enrollment.Secret, DateTimeOffset.UtcNow, 1 ) );
			await OnlineIntegrationTests.PumpUntilIdle( session );
			Assert.IsTrue( session.IsLoggedIn, session.Status );
			Assert.IsFalse( session.CodeRequested );
		}
		finally { Directory.Delete( folders.Root, true ); server.DeleteData(); }
	}

	[TestMethod]
	public void TheQrCodeHasFinderPatternsInThreeCorners()
	{
		var modules = UI.Original.UiQrCode.Modules( Totp.Uri( "OpenTPW", "Ann", Totp.ToBase32( Totp.NewSecret() ) ) );
		var size = modules.GetLength( 0 );
		Assert.IsTrue( size >= 21 && (size - 21) % 4 == 0, $"size {size}" );
		bool Finder( int left, int top ) => Enumerable.Range( 0, 7 ).All( index => modules[left + index, top] && modules[left + index, top + 6] && modules[left, top + index] && modules[left + 6, top + index] )
			&& !modules[left + 1, top + 1] && modules[left + 3, top + 3];
		Assert.IsTrue( Finder( 0, 0 ) && Finder( size - 7, 0 ) && Finder( 0, size - 7 ) );
		Assert.AreEqual( 0, UI.Original.UiQrCode.Modules( "" ).Length );
	}
}
