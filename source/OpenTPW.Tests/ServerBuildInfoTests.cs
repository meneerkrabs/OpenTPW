using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Server;

namespace OpenTPW.Tests;

[TestClass]
public class ServerBuildInfoTests
{
	private const string Revision = "c884e2b24394e4ced11a0ef880a66d2fa9bd00ee";

	[DataTestMethod]
	[DataRow( "1.0.0+" + Revision )]
	[DataRow( "0.7.16-preview.1+build." + Revision )]
	[DataRow( "1.0.0+C884E2B24394E4CED11A0EF880A66D2FA9BD00EE" )]
	[DataRow( "1.0.0+signed." + Revision )]
	[DataRow( "1.0.0+dirty." + Revision )]
	public void FullRevisionComesFromAssemblyBuildMetadata( string version )
	{
		var info = ServerBuildInfo.FromInformationalVersion( version );
		Assert.AreEqual( Revision, info.SourceRevision );
		Assert.IsTrue( info.RevisionKnown );
		Assert.AreEqual( version.Split( '+' )[0], info.Version );
	}

	[DataTestMethod]
	[DataRow( null, null )]
	[DataRow( "", null )]
	[DataRow( "unknown", null )]
	[DataRow( "1.0.0", "1.0.0" )]
	[DataRow( "1.0.0+unknown", "1.0.0" )]
	[DataRow( "1.0.0+c884e2b", "1.0.0" )]
	[DataRow( "1.0.0+branch-private-marker", "1.0.0" )]
	[DataRow( "1.0.0+/private/path/secret", null )]
	[DataRow( "1.0.0+" + Revision + ".dirty", "1.0.0" )]
	[DataRow( "1.0.0+" + Revision + "-dirty", "1.0.0" )]
	[DataRow( "1.0.0+" + Revision + ".signed", "1.0.0" )]
	[DataRow( "1.0.0+" + Revision + "\n", null )]
	[DataRow( "1.0.0+g884e2b24394e4ced11a0ef880a66d2fa9bd00ee", "1.0.0" )]
	[DataRow( " 1.0.0+" + Revision, null )]
	public void UnknownOrUnversionedBuildsDoNotInventARevisionOrExposeOtherMetadata( string? version, string? expectedVersion )
	{
		var info = ServerBuildInfo.FromInformationalVersion( version );
		Assert.AreEqual( expectedVersion, info.Version );
		Assert.IsNull( info.SourceRevision );
		Assert.IsFalse( info.RevisionKnown );
	}

	[DataTestMethod]
	[DataRow( "1.0.0+." + Revision )]
	[DataRow( "1.0.0+build.." + Revision )]
	[DataRow( "1.0.0+build." + Revision + "." )]
	public void EmptyMetadataComponentsCannotClaimAKnownSourceRevision( string version )
	{
		var info = ServerBuildInfo.FromInformationalVersion( version );
		Assert.IsNull( info.Version );
		Assert.IsNull( info.SourceRevision );
		Assert.IsFalse( info.RevisionKnown );
	}

	[TestMethod]
	public void OverlongMetadataRemainsUnknown()
	{
		var info = ServerBuildInfo.FromInformationalVersion( "1.0.0+" + new string( 'a', 201 ) + "." + Revision );
		Assert.IsNull( info.Version );
		Assert.IsNull( info.SourceRevision );
		Assert.IsFalse( info.RevisionKnown );
	}

	[TestMethod]
	public async Task PublicReadOnlyEndpointReportsExecutingServerAssemblyAndOnlyBuildFacts()
	{
		await using var server = await LoopbackServer.StartAsync( options =>
		{
			options.DeployToken = "private-deploy-token";
			options.ResendApiKey = "private-email-key";
			options.WebsiteOrigins.Add( "https://opentpw.io" );
		} );
		try
		{
			using var http = new HttpClient { BaseAddress = server.Url };
			var filesBefore = Directory.GetFiles( server.Directory, "*", SearchOption.AllDirectories ).OrderBy( path => path ).ToArray();
			http.DefaultRequestHeaders.Add( "Origin", "https://opentpw.io" );
			http.DefaultRequestHeaders.Add( "Authorization", "Bearer invalid-build-info-token" );
			using var response = await http.GetAsync( ServerProgram.BuildInfoRoute );
			Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
			Assert.AreEqual( "application/json", response.Content.Headers.ContentType!.MediaType );
			Assert.IsTrue( response.Headers.CacheControl!.NoStore );
			Assert.IsFalse( response.Headers.Contains( "Access-Control-Allow-Origin" ) );
			Assert.IsFalse( response.Headers.Contains( "Server" ) );
			var body = await response.Content.ReadAsStringAsync();
			using var json = JsonDocument.Parse( body );
			CollectionAssert.AreEquivalent( new[] { "version", "sourceRevision", "revisionKnown" },
				json.RootElement.EnumerateObject().Select( item => item.Name ).ToArray() );
			var expected = ServerBuildInfo.FromInformationalVersion( typeof( ServerProgram ).Assembly
				.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion );
			Assert.AreEqual( expected.Version, json.RootElement.GetProperty( "version" ).GetString() );
			Assert.AreEqual( expected.SourceRevision, json.RootElement.GetProperty( "sourceRevision" ).GetString() );
			Assert.AreEqual( expected.RevisionKnown, json.RootElement.GetProperty( "revisionKnown" ).GetBoolean() );
			Assert.IsFalse( body.Contains( "private-deploy-token" ) || body.Contains( "private-email-key" ) || body.Contains( server.Directory ) );
			using var post = await http.PostAsync( ServerProgram.BuildInfoRoute, null );
			Assert.AreEqual( HttpStatusCode.MethodNotAllowed, post.StatusCode );
			using var existing = await http.GetAsync( "/api/v1/server" );
			using var serverInfo = JsonDocument.Parse( await existing.Content.ReadAsStringAsync() );
			Assert.AreEqual( "1.0", serverInfo.RootElement.GetProperty( "version" ).GetString() );
			Assert.AreEqual( ServerProgram.ProtocolVersion, serverInfo.RootElement.GetProperty( "protocolVersion" ).GetInt32() );
			CollectionAssert.AreEqual( filesBefore, Directory.GetFiles( server.Directory, "*", SearchOption.AllDirectories ).OrderBy( path => path ).ToArray(), "inspection never creates server data or deployment requests" );
		}
		finally { server.DeleteData(); }
	}
	[TestMethod]
	public async Task PublicBuildInfoUsesTheExistingGlobalRateLimit()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.RequestsPerMinute = 1 );
		try
		{
			using var http = new HttpClient { BaseAddress = server.Url };
			using var first = await http.GetAsync( ServerProgram.BuildInfoRoute );
			Assert.AreEqual( HttpStatusCode.OK, first.StatusCode );
			using var second = await http.GetAsync( ServerProgram.BuildInfoRoute );
			Assert.AreEqual( HttpStatusCode.TooManyRequests, second.StatusCode );
			Assert.IsTrue( second.Headers.CacheControl!.NoStore );
			// [EXT:online-coop-snapshot-join] the refusal says when to try again: the window's length at most
			Assert.IsTrue( second.Headers.RetryAfter?.Delta is { } wait && wait >= TimeSpan.FromSeconds( 1 ) && wait <= TimeSpan.FromMinutes( 1 ), $"{second.Headers.RetryAfter}" );
		}
		finally { server.DeleteData(); }
	}

}
