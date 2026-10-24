using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Client;

namespace OpenTPW.Tests;

/// <summary>
/// The co-op link's transport check (C2c-2b-5; docs/COOP.md, "The browser's link to a real server") on the desktop: two probes, a host and a guest, against the
/// loopback server, in the order tools/web-coop-link.mjs drives two pages through. The records the check reads are the probes' own, every payload checked by
/// its length and SHA-256 against the bytes the test chose.
/// </summary>
[TestClass]
public class CoopLinkProbeTests
{
	private const string Password = "link-check-password";
	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds( 30 );

	/// <summary>Seeded bytes, as the driver makes its payloads.</summary>
	private static byte[] Seeded( int length, int seed )
	{
		var bytes = new byte[length];
		new Random( seed ).NextBytes( bytes );
		return bytes;
	}

	private static string Sha256( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );

	private static JsonObject[] Records( CoopLinkProbe probe, string kind ) =>
		probe.Status()["events"]!.AsArray().Select( item => item!.AsObject() ).Where( item => (string?)item["kind"] == kind ).ToArray();

	/// <summary>Steps both probes (each pumps its own session) until <paramref name="done"/> holds; a failure of either or the deadline fails the test.</summary>
	private static async Task Until( CoopLinkProbe host, CoopLinkProbe guest, Func<bool> done, string what )
	{
		var started = DateTime.UtcNow;
		while ( true )
		{
			host.Step();
			guest.Step();
			Assert.IsNull( host.Failure, $"the host, waiting for {what}: {host.Failure}" );
			Assert.IsNull( guest.Failure, $"the guest, waiting for {what}: {guest.Failure}" );
			if ( done() )
				return;
			if ( DateTime.UtcNow - started > Deadline )
				Assert.Fail( $"Timed out waiting for {what}: host {host.Status().ToJsonString()}; guest {guest.Status().ToJsonString()}" );
			await Task.Delay( 10 );
		}
	}

	[TestMethod]
	public async Task TwoProbesShareASessionItsSnapshotFramesAndMessagesThroughTheRealServer()
	{
		await using var server = await LoopbackServer.StartAsync();
		using ( var client = new OnlineClient( server.Url ) )
		{
			Assert.IsNull( await client.RegisterAsync( "LinkHost", Password ) );
			Assert.IsNull( await client.RegisterAsync( "LinkGuest", Password ) );
		}
		using var host = new CoopLinkProbe( Path.Combine( server.Directory, "probe-host" ), server.Url.ToString(), "LinkHost", Password );
		using var guest = new CoopLinkProbe( Path.Combine( server.Directory, "probe-guest" ), server.Url.ToString(), "LinkGuest", Password );
		await Until( host, guest, () => host.Phase == "open" && guest.Phase == "open", "both welcomes" );
		Assert.AreEqual( OpenTPW.Online.Coop.CoopProtocol.Version, (int?)host.Status()["protocol"] );

		Assert.IsTrue( host.Create( "coop link check", listed: true ) );
		await Until( host, guest, () => Records( host, "session" ).Length == 1, "the created session" );
		var created = Records( host, "session" ).Single();
		var hostId = (uint)created["you"]!;
		Assert.AreEqual( hostId, (uint)created["host"]! );

		// [EXT:online-coop-session] the guest finds it on the open list (HTTP), the host invites the guest (the socket), and the guest joins with the invitation's code (C2d-3b)
		Assert.IsTrue( guest.List() );
		await Until( host, guest, () => Records( guest, "listed" ).Length == 1, "the open list" );
		var row = Records( guest, "listed" ).Single()["sessions"]!.AsArray().Single( item => (string?)item!["code"] == (string)created["code"]! )!;
		Assert.AreEqual( ("coop link check", "LinkHost", 1, 2), ((string)row["title"]!, (string)row["host"]!, (int)row["players"]!, (int)row["maximum"]!) );
		Assert.IsTrue( host.Invite( "LinkGuest" ) );
		await Until( host, guest, () => Records( guest, "invited" ).Length == 1, "the invitation" );
		var invited = Records( guest, "invited" ).Single();
		Assert.AreEqual( ((string)created["id"]!, (string)created["code"]!, "coop link check", "LinkHost"),
			((string)invited["session"]!, (string)invited["code"]!, (string)invited["title"]!, (string)invited["host"]!) );

		Assert.IsTrue( guest.Join( (string)invited["code"]! ) );
		await Until( host, guest, () => Records( guest, "session" ).Length == 1 && Records( host, "member" ).Any( item => (string?)item["state"] == "Joined" ), "the join" );
		var joined = Records( guest, "session" ).Single();
		var guestId = (uint)joined["you"]!;
		Assert.AreEqual( (string)created["id"]!, (string)joined["id"]! );
		Assert.AreEqual( hostId, (uint)joined["host"]! );
		Assert.AreEqual( guestId, (uint)Records( host, "member" ).Single( item => (string?)item["state"] == "Joined" )["id"]! );

		// the host's snapshot, stored once a member joined (the server refuses one before), and the guest's download of it
		var snapshot = Seeded( 150 * 1024, 1 );
		Assert.IsTrue( host.Upload( snapshot ) );
		await Until( host, guest, () => Records( host, "uploaded" ).Length == 1, "the upload" );
		Assert.AreEqual( Sha256( snapshot ), (string)Records( host, "uploaded" ).Single()["sha256"]! );
		Assert.IsTrue( guest.Download( Sha256( snapshot ), snapshot.Length ) );
		await Until( host, guest, () => Records( guest, "downloaded" ).Length == 1, "the download" );
		var downloaded = Records( guest, "downloaded" ).Single();
		Assert.AreEqual( snapshot.Length, (int)downloaded["length"]! );
		Assert.AreEqual( Sha256( snapshot ), (string)downloaded["sha256"]! );

		// the host's frame to the guest, a message each way
		var frame = Seeded( 700, 2 );
		Assert.IsTrue( host.SendFrame( 1, frame ) );
		await Until( host, guest, () => Records( guest, "frame" ).Length == 1, "the relayed frame" );
		var received = Records( guest, "frame" ).Single();
		Assert.AreEqual( 1UL, (ulong)received["seq"]! );
		Assert.AreEqual( Sha256( frame ), (string)received["sha256"]! );
		var toHost = Seeded( 300, 3 );
		Assert.IsTrue( guest.Send( hostId, toHost ) );
		await Until( host, guest, () => Records( host, "message" ).Length == 1, "the guest's message" );
		Assert.AreEqual( guestId, (uint)Records( host, "message" ).Single()["from"]! );
		Assert.AreEqual( Sha256( toHost ), (string)Records( host, "message" ).Single()["sha256"]! );
		var toGuest = Seeded( 200, 4 );
		Assert.IsTrue( host.Send( guestId, toGuest ) );
		await Until( host, guest, () => Records( guest, "message" ).Length == 1, "the host's message" );
		Assert.AreEqual( hostId, (uint)Records( guest, "message" ).Single()["from"]! );
		Assert.AreEqual( Sha256( toGuest ), (string)Records( guest, "message" ).Single()["sha256"]! );

		// the host's leave ends the session for the guest
		Assert.IsTrue( host.Leave( "The check ended." ) );
		await Until( host, guest, () => guest.SessionEnded, "the session's end" );
		Assert.AreEqual( (string)created["id"]!, (string)Records( guest, "ended" ).Single()["session"]! );
		Assert.IsNull( host.Failure );
		Assert.IsNull( guest.Failure );
	}

	[TestMethod]
	public async Task ALoginTheServerRefusesFailsTheProbe()
	{
		await using var server = await LoopbackServer.StartAsync();
		using var probe = new CoopLinkProbe( Path.Combine( server.Directory, "probe" ), server.Url.ToString(), "Nobody", Password );
		var started = DateTime.UtcNow;
		while ( probe.Failure == null && DateTime.UtcNow - started < Deadline )
		{
			probe.Step();
			await Task.Delay( 10 );
		}
		Assert.AreEqual( "failed", probe.Phase );
		StringAssert.StartsWith( probe.Failure, "The login failed" );
		Assert.AreEqual( "Closed", (string?)probe.Status()["state"] );
	}
}
